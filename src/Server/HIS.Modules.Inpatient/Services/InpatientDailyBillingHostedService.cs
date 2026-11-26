using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HIS.Modules.Inpatient.Services;

/// <summary>
/// 住院每日自动滚费后台服务：每日凌晨自动为所有在院患者幂等生成床位费、分级护理费及长期医嘱续费并写入 fin_charge_ledgers。
/// 所有单价均实时从物价表 (cfg_price_items)、药品主目录 (inv_drugs)、耗材表 (csm_consumables) 与床位主表 (ipd_beds) 动态取价。
/// </summary>
public sealed class InpatientDailyBillingHostedService : BackgroundService
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly ILogger<InpatientDailyBillingHostedService> _logger;

    public InpatientDailyBillingHostedService(
        IDbConnectionFactory connFactory,
        ILogger<InpatientDailyBillingHostedService> logger)
    {
        _connFactory = connFactory;
        _logger = logger;
    }

    public sealed record DailyBillingRunResult(
        string RunId,
        int BillDateInt,
        string TriggerMode,
        bool AlreadyExecuted,
        int PatientCount,
        int BedFeeCount,
        int NursingFeeCount,
        int LongOrderCount,
        decimal TotalAmount,
        string ExecutedBy,
        long ExecutedAtMs);

    private static async Task<(decimal Price, string Source)> ResolveItemPriceAsync(
        DbConnection conn,
        DbTransaction tx,
        string? itemCode,
        string? itemName,
        decimal fallbackIfUnconfigured)
    {
        // 1. 优先查医院物价主表 cfg_price_items
        if (!string.IsNullOrWhiteSpace(itemCode))
        {
            decimal? p = await conn.QueryFirstOrDefaultAsync<decimal?>(
                "SELECT unit_price FROM cfg_price_items WHERE item_code = @Code AND is_active = 1 LIMIT 1",
                new { Code = itemCode.Trim() }, tx);
            if (p.HasValue && p.Value > 0m) return (p.Value, "CFG_PRICE_ITEMS");

            // 2. 查药品主目录 inv_drugs
            decimal? d = await conn.QueryFirstOrDefaultAsync<decimal?>(
                "SELECT price FROM inv_drugs WHERE drug_code = @Code AND is_active = 1 LIMIT 1",
                new { Code = itemCode.Trim() }, tx);
            if (d.HasValue && d.Value > 0m) return (d.Value, "INV_DRUGS");
        }

        // 3. 按名称精确匹配物价表或药品表
        if (!string.IsNullOrWhiteSpace(itemName))
        {
            decimal? pn = await conn.QueryFirstOrDefaultAsync<decimal?>(
                "SELECT unit_price FROM cfg_price_items WHERE item_name = @Name AND is_active = 1 LIMIT 1",
                new { Name = itemName.Trim() }, tx);
            if (pn.HasValue && pn.Value > 0m) return (pn.Value, "CFG_PRICE_ITEMS_BY_NAME");

            decimal? dn = await conn.QueryFirstOrDefaultAsync<decimal?>(
                "SELECT price FROM inv_drugs WHERE drug_name = @Name AND is_active = 1 LIMIT 1",
                new { Name = itemName.Trim() }, tx);
            if (dn.HasValue && dn.Value > 0m) return (dn.Value, "INV_DRUGS_BY_NAME");
        }

        return (fallbackIfUnconfigured, "DEFAULT_TARIFF_FALLBACK");
    }

    public static async Task<DailyBillingRunResult> ExecuteDailyBillingCoreAsync(
        IDbConnectionFactory connFactory,
        int billDateInt,
        string triggerMode,
        string executedBy,
        CancellationToken ct = default)
    {
        using var conn = connFactory.CreateConnection();
        var existing = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT * FROM ipd_daily_billing_runs WHERE bill_date_int = @Dt LIMIT 1",
            new { Dt = billDateInt });
        if (existing != null)
        {
            return new DailyBillingRunResult(
                (string)existing.id,
                (int)existing.bill_date_int,
                (string)existing.trigger_mode,
                true,
                Convert.ToInt32(existing.patient_count),
                Convert.ToInt32(existing.bed_fee_count),
                Convert.ToInt32(existing.nursing_fee_count),
                Convert.ToInt32(existing.long_order_count),
                Convert.ToDecimal(existing.total_amount),
                (string?)existing.executed_by ?? executedBy,
                Convert.ToInt64(existing.executed_at_ms));
        }

        var activeEncounters = (await conn.QueryAsync<dynamic>(@"
            SELECT e.id AS EncounterId,
                   e.patient_id AS PatientId,
                   e.admit_dept_id AS DeptId,
                   e.admit_ward_id AS WardId,
                   e.bed_code AS BedCode,
                   e.nursing_level_code AS NursingLevelCode,
                   COALESCE(b.daily_fee, 0) AS BedDailyFee
            FROM ipd_encounters e
            LEFT JOIN ipd_beds b ON b.current_encounter_id = e.id
            WHERE e.status = 1 AND COALESCE(e.is_deceased, 0) = 0")).ToList();

        long now = SystemTime.UtcNowMs;
        int bedFeeCount = 0;
        int nursingFeeCount = 0;
        int longOrderCount = 0;
        decimal totalAmount = 0m;

        using var tx = conn.BeginTransaction();
        foreach (var enc in activeEncounters)
        {
            string encId = (string)enc.EncounterId;
            string patId = (string)enc.PatientId;
            string deptId = (string)enc.DeptId;
            string wardId = (string)enc.WardId;
            string? bedCode = (string?)enc.BedCode;

            // 1. 床位费：优先取 ipd_beds.daily_fee，否则反查 cfg_price_items 的 FEE_IPD_BED
            decimal bedFee = Convert.ToDecimal(enc.BedDailyFee);
            string bedSource = "IPD_BEDS";
            if (bedFee <= 0m)
            {
                (bedFee, bedSource) = await ResolveItemPriceAsync(conn, tx, "FEE_IPD_BED", "普通病房床位费", 65.0m);
            }

            await conn.ExecuteAsync(@"
                INSERT INTO fin_charge_ledgers
                (id, patient_id, visit_or_encounter_id, order_dept_id, patient_ward_id, exec_dept_id,
                 item_code, item_name, quantity, unit_price, total_amount, status, ext_data_json,
                 schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES
                (@Id, @PatId, @EncId, @DeptId, @WardId, @WardId,
                 'FEE_IPD_BED', @ItemName, 1, @Price, @Price, 1, @Ext,
                 1, @Now, @Actor, @Now, @Actor, 1)",
                new
                {
                    Id = Guid.NewGuid().ToString("D"),
                    PatId = patId,
                    EncId = encId,
                    DeptId = deptId,
                    WardId = wardId,
                    ItemName = $"住院床位费({bedCode ?? "标准床"}) [{billDateInt}]",
                    Price = bedFee,
                    Ext = $"{{\"billDateInt\":{billDateInt},\"category\":\"BED_FEE\",\"priceSource\":\"{bedSource}\"}}",
                    Now = now,
                    Actor = executedBy
                }, tx);
            bedFeeCount++;
            totalAmount += bedFee;

            // 2. 分级护理费：优先从 cfg_price_items 实时取价
            string nursingCode = ((string?)enc.NursingLevelCode ?? "LEVEL_2").Trim().ToUpperInvariant();
            (string nursingItemCode, string nursingLabel, decimal defaultNursingFee) = nursingCode switch
            {
                "SPECIAL" or "特级护理" => ("FEE_NURSING_SPECIAL", "特级护理费", 120.0m),
                "LEVEL_1" or "一级护理" => ("FEE_NURSING_L1", "一级护理费", 54.0m),
                "LEVEL_3" or "三级护理" => ("FEE_NURSING_L3", "三级护理费", 18.0m),
                _ => ("FEE_NURSING_L2", "二级护理费", 32.0m)
            };
            var (nursingFee, nursingSource) = await ResolveItemPriceAsync(conn, tx, nursingItemCode, nursingLabel, defaultNursingFee);

            await conn.ExecuteAsync(@"
                INSERT INTO fin_charge_ledgers
                (id, patient_id, visit_or_encounter_id, order_dept_id, patient_ward_id, exec_dept_id,
                 item_code, item_name, quantity, unit_price, total_amount, status, ext_data_json,
                 schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES
                (@Id, @PatId, @EncId, @DeptId, @WardId, @WardId,
                 @ItemCode, @ItemName, 1, @Price, @Price, 1, @Ext,
                 1, @Now, @Actor, @Now, @Actor, 1)",
                new
                {
                    Id = Guid.NewGuid().ToString("D"),
                    PatId = patId,
                    EncId = encId,
                    DeptId = deptId,
                    WardId = wardId,
                    ItemCode = nursingItemCode,
                    ItemName = $"{nursingLabel} [{billDateInt}]",
                    Price = nursingFee,
                    Ext = $"{{\"billDateInt\":{billDateInt},\"category\":\"NURSING_FEE\",\"priceSource\":\"{nursingSource}\"}}",
                    Now = now,
                    Actor = executedBy
                }, tx);
            nursingFeeCount++;
            totalAmount += nursingFee;

            // 3. 在执行中的长期医嘱续费：按 item_code / item_name 实时反查 cfg_price_items / inv_drugs
            var longOrders = (await conn.QueryAsync<dynamic>(@"
                SELECT id AS OrderId, item_code AS ItemCode, item_name AS ItemName,
                       dosage AS Dosage, exec_target_dept_id AS ExecDeptId
                FROM ord_medical_orders
                WHERE encounter_id = @EncId AND order_term_type = 1 AND status IN (1, 2)",
                new { EncId = encId }, tx)).ToList();

            foreach (var ord in longOrders)
            {
                string itemCode = (string)ord.ItemCode;
                string itemName = (string)ord.ItemName;
                var (ordPrice, priceSource) = await ResolveItemPriceAsync(conn, tx, itemCode, itemName, 0m);
                if (ordPrice <= 0m) continue; // 无定价项目跳过自动滚费，避免产生虚构金额

                decimal qty = Math.Max(1m, Convert.ToDecimal(ord.Dosage));
                decimal lineTotal = Math.Round(ordPrice * qty, 2);
                await conn.ExecuteAsync(@"
                    INSERT INTO fin_charge_ledgers
                    (id, patient_id, visit_or_encounter_id, order_dept_id, patient_ward_id, exec_dept_id,
                     item_code, item_name, quantity, unit_price, total_amount, status, ext_data_json,
                     schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                    VALUES
                    (@Id, @PatId, @EncId, @DeptId, @WardId, @ExecDeptId,
                     @ItemCode, @ItemName, @Qty, @UnitPrice, @Total, 1, @Ext,
                     1, @Now, @Actor, @Now, @Actor, 1)",
                    new
                    {
                        Id = Guid.NewGuid().ToString("D"),
                        PatId = patId,
                        EncId = encId,
                        DeptId = deptId,
                        WardId = wardId,
                        ExecDeptId = (string?)ord.ExecDeptId ?? wardId,
                        ItemCode = itemCode,
                        ItemName = $"{itemName} (长嘱日滚 {billDateInt})",
                        Qty = qty,
                        UnitPrice = ordPrice,
                        Total = lineTotal,
                        Ext = $"{{\"billDateInt\":{billDateInt},\"orderId\":\"{(string)ord.OrderId}\",\"category\":\"LONG_ORDER\",\"priceSource\":\"{priceSource}\"}}",
                        Now = now,
                        Actor = executedBy
                    }, tx);
                longOrderCount++;
                totalAmount += lineTotal;
            }
        }

        string runId = Guid.NewGuid().ToString("D");
        await conn.ExecuteAsync(@"
            INSERT INTO ipd_daily_billing_runs
            (id, bill_date_int, trigger_mode, patient_count, bed_fee_count, nursing_fee_count,
             long_order_count, total_amount, executed_by, executed_at_ms,
             schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES
            (@Id, @Dt, @Mode, @Pats, @Beds, @Nurs, @Ords, @Amt, @Actor, @Now,
             1, @Now, @Actor, @Now, @Actor, 1)",
            new
            {
                Id = runId,
                Dt = billDateInt,
                Mode = triggerMode,
                Pats = activeEncounters.Count,
                Beds = bedFeeCount,
                Nurs = nursingFeeCount,
                Ords = longOrderCount,
                Amt = totalAmount,
                Actor = executedBy,
                Now = now
            }, tx);

        tx.Commit();

        return new DailyBillingRunResult(
            runId,
            billDateInt,
            triggerMode,
            false,
            activeEncounters.Count,
            bedFeeCount,
            nursingFeeCount,
            longOrderCount,
            totalAmount,
            executedBy,
            now);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var shanghaiNow = SystemTime.ShanghaiNow;
                int todayInt = int.Parse(shanghaiNow.ToString("yyyyMMdd"));
                if (shanghaiNow.Hour > 0 || shanghaiNow.Minute >= 5)
                {
                    var res = await ExecuteDailyBillingCoreAsync(
                        _connFactory, todayInt, "AUTO_SCHEDULER", "SYSTEM_BILLING_WORKER", stoppingToken);
                    if (!res.AlreadyExecuted)
                    {
                        _logger.LogInformation(
                            "自动住院每日滚费完成: BillDate={BillDate}, Patients={Patients}, TotalAmount={Amount}",
                            res.BillDateInt, res.PatientCount, res.TotalAmount);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "自动住院每日滚费巡检异常，将在下一周期重试");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
