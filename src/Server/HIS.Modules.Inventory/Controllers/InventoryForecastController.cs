using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Inventory.Controllers;

/// <summary>
/// 药库/药房智能补货预测与缺货天数预警引擎（对标 Open Hospital 二级药房缺货天数 + MedCore 30/60/90 天采购预测）
/// 基于近 90 天真实 inv_stock_ledgers 出库消耗流水与当前 inv_drug_batches 有效未过期库存实时计算
/// </summary>
[ApiController]
[Route("api/v4/inventory/forecast")]
public sealed class InventoryForecastController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _session;

    public InventoryForecastController(IDbConnectionFactory connFactory, IUserSession? session = null)
    {
        _connFactory = connFactory;
        _session = session;
    }

    public sealed record DrugForecastRow(
        string DrugId,
        string DrugCode,
        string DrugName,
        string Spec,
        string UnitName,
        int CurrentValidStock,
        int OutboundLast30Days,
        int OutboundLast90Days,
        decimal WeightedDailyUsage,
        int DaysOutOfStock,
        int SuggestedOrder30d,
        int SuggestedOrder60d,
        int SuggestedOrder90d,
        decimal EstimatedCost30d,
        string RiskLevel);

    [HttpGet("replenishment")]
    public async Task<ApiResponse<IReadOnlyList<DrugForecastRow>>> GetReplenishmentForecast(
        [FromQuery] string? warehouseId = null,
        [FromQuery] int safetyDays = 14)
    {
        safetyDays = Math.Clamp(safetyDays, 3, 90);
        long nowMs = SystemTime.UtcNowMs;
        long ms30dAgo = nowMs - 30L * 24 * 3600 * 1000;
        long ms90dAgo = nowMs - 90L * 24 * 3600 * 1000;
        int todayInt = int.Parse(SystemTime.ShanghaiNow.ToString("yyyyMMdd"));

        using var conn = _connFactory.CreateConnection();
        var drugs = (await conn.QueryAsync<dynamic>(
            "SELECT id AS Id, drug_code AS Code, drug_name AS Name, spec AS Spec, unit_name AS Unit, cost_price AS CostPrice FROM inv_drugs WHERE is_active = 1")).ToList();

        string whFilter = !string.IsNullOrWhiteSpace(warehouseId) && warehouseId != "ALL"
            ? " AND warehouse_or_pharmacy_id = @WhId"
            : string.Empty;

        // 1. 当前未冻结且未过期有效库存
        var stockRaw = await conn.QueryAsync<(string DrugId, decimal Qty)>($@"
            SELECT drug_id AS DrugId, COALESCE(SUM(quantity), 0) AS Qty
            FROM inv_drug_batches
            WHERE is_frozen = 0 AND expiry_date_int >= @Today {whFilter}
            GROUP BY drug_id",
            new { Today = todayInt, WhId = warehouseId?.Trim() });
        var stockRows = stockRaw.ToDictionary(r => r.DrugId, r => (int)r.Qty);

        // 2. 近 90 天与近 30 天真实出库消耗量（change_quantity < 0）
        var usageRaw = await conn.QueryAsync<(string DrugId, decimal Out30, decimal Out90)>($@"
            SELECT drug_id AS DrugId,
                   COALESCE(SUM(CASE WHEN created_at_ms >= @Ms30 THEN ABS(change_quantity) ELSE 0 END), 0) AS Out30,
                   COALESCE(SUM(ABS(change_quantity)), 0) AS Out90
            FROM inv_stock_ledgers
            WHERE change_quantity < 0 AND created_at_ms >= @Ms90 {whFilter}
            GROUP BY drug_id",
            new { Ms30 = ms30dAgo, Ms90 = ms90dAgo, WhId = warehouseId?.Trim() });
        var usageRows = usageRaw.ToDictionary(r => r.DrugId, r => (Out30: (int)r.Out30, Out90: (int)r.Out90));

        var result = new List<DrugForecastRow>(drugs.Count);
        foreach (var d in drugs)
        {
            string drugId = (string)d.Id;
            int currentStock = stockRows.TryGetValue(drugId, out int s) ? s : 0;
            var (out30, out90) = usageRows.TryGetValue(drugId, out var u) ? u : (0, 0);

            // 加权日均消耗：近30天权重60% + 近90天均值权重40%（应对季节波动）
            decimal avg30 = out30 / 30m;
            decimal avg90 = out90 / 90m;
            decimal weightedDaily = Math.Round(avg30 * 0.6m + avg90 * 0.4m, 2);

            int daysLeft = weightedDaily > 0m
                ? (int)Math.Floor(currentStock / weightedDaily)
                : (currentStock > 0 ? 999 : 0);

            int sug30 = Math.Max(0, (int)Math.Ceiling(weightedDaily * (30 + safetyDays) - currentStock));
            int sug60 = Math.Max(0, (int)Math.Ceiling(weightedDaily * (60 + safetyDays) - currentStock));
            int sug90 = Math.Max(0, (int)Math.Ceiling(weightedDaily * (90 + safetyDays) - currentStock));
            decimal costPrice = Convert.ToDecimal(d.CostPrice);

            string risk = currentStock <= 0
                ? "CRITICAL_OUT_OF_STOCK"
                : daysLeft <= safetyDays ? "HIGH_SHORTAGE_RISK"
                : daysLeft <= 30 ? "MEDIUM_REORDER" : "HEALTHY";

            result.Add(new DrugForecastRow(
                DrugId: drugId,
                DrugCode: (string)d.Code,
                DrugName: (string)d.Name,
                Spec: (string)d.Spec,
                UnitName: (string)d.Unit,
                CurrentValidStock: currentStock,
                OutboundLast30Days: out30,
                OutboundLast90Days: out90,
                WeightedDailyUsage: weightedDaily,
                DaysOutOfStock: daysLeft,
                SuggestedOrder30d: sug30,
                SuggestedOrder60d: sug60,
                SuggestedOrder90d: sug90,
                EstimatedCost30d: Math.Round(sug30 * costPrice, 2),
                RiskLevel: risk));
        }

        var ordered = result
            .OrderBy(r => r.DaysOutOfStock)
            .ThenByDescending(r => r.SuggestedOrder30d)
            .ToList();

        return ApiResponse<IReadOnlyList<DrugForecastRow>>.Success(ordered, $"已完成 {ordered.Count} 种药品的 30/60/90 天智能补货预测与缺货天数评估");
    }

    public sealed record GenerateReplenishTransferDto(string SourceWarehouseId, string TargetWarehouseId, int HorizonDays = 30, int SafetyDays = 14);

    /// <summary>
    /// 闭环：根据 90 天智能补货预测结果，一键批量生成待审核药库调拨补货单写入 inv_transfer_orders
    /// </summary>
    [HttpPost("generate-transfer")]
    public async Task<ApiResponse<object>> GenerateReplenishmentTransfers([FromBody] GenerateReplenishTransferDto req)
    {
        if (string.IsNullOrWhiteSpace(req.SourceWarehouseId) || string.IsNullOrWhiteSpace(req.TargetWarehouseId))
            return ApiResponse<object>.Fail(400, "源药库与目标药房ID不能为空");

        var forecastRes = await GetReplenishmentForecast(req.TargetWarehouseId, req.SafetyDays);
        if (forecastRes.Code is not (0 or 200) || forecastRes.Data == null)
            return ApiResponse<object>.Fail(500, "无法计算补货预测");

        long now = SystemTime.UtcNowMs;
        int created = 0;
        var createdNos = new List<string>();
        string actor = _session?.UserId?.Trim() ?? "SYSTEM";

        using var conn = _connFactory.CreateConnection();
        using var tx = conn.BeginTransaction();
        foreach (var row in forecastRes.Data)
        {
            int qty = req.HorizonDays >= 90 ? row.SuggestedOrder90d : req.HorizonDays >= 60 ? row.SuggestedOrder60d : row.SuggestedOrder30d;
            if (qty <= 0) continue;

            var srcBatch = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT b.id AS BatchId, b.batch_no AS BatchNo, b.expiry_date_int AS ExpInt, d.cost_price AS CostPrice, d.price AS RetailPrice
                FROM inv_drug_batches b
                INNER JOIN inv_drugs d ON d.id = b.drug_id
                WHERE b.warehouse_or_pharmacy_id = @SrcWh AND b.drug_id = @Did AND b.is_frozen = 0 AND b.quantity > 0
                ORDER BY b.expiry_date_int ASC LIMIT 1",
                new { SrcWh = req.SourceWarehouseId.Trim(), Did = row.DrugId }, tx);

            string batchId = srcBatch != null ? (string)srcBatch.BatchId : "PENDING_PURCHASE";
            string batchNo = srcBatch != null ? (string)srcBatch.BatchNo : "AUTO_FORECAST";
            int expInt = srcBatch != null ? Convert.ToInt32(srcBatch.ExpInt) : int.Parse(SystemTime.ShanghaiNow.AddYears(1).ToString("yyyyMMdd"));
            decimal costPrice = srcBatch != null ? Convert.ToDecimal(srcBatch.CostPrice) : 0m;
            decimal retailPrice = srcBatch != null ? Convert.ToDecimal(srcBatch.RetailPrice) : 0m;
            string tNo = "TR-AUTO-" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss") + "-" + (created + 1).ToString("D2");

            await conn.ExecuteAsync(@"
                INSERT INTO inv_transfer_orders
                (id, transfer_no, transfer_type, source_warehouse_id, target_warehouse_id, drug_id, drug_name_snapshot, batch_id, batch_no, expiry_date_int, quantity, unit_cost_price, unit_retail_price, total_retail_amount, status, applicant_id, remark, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES
                (@Id, @No, 'AUTO_REPLENISH', @Src, @Tgt, @Did, @DName, @Bid, @BNo, @Exp, @Qty, @Cost, @Retail, @Total, 0, @Actor, @Remark, 1, @Now, @Actor, @Now, @Actor, 1)",
                new
                {
                    Id = Guid.NewGuid().ToString("D"),
                    No = tNo,
                    Src = req.SourceWarehouseId.Trim(),
                    Tgt = req.TargetWarehouseId.Trim(),
                    Did = row.DrugId,
                    DName = row.DrugName,
                    Bid = batchId,
                    BNo = batchNo,
                    Exp = expInt,
                    Qty = qty,
                    Cost = costPrice,
                    Retail = retailPrice,
                    Total = Math.Round(retailPrice * qty, 2),
                    Actor = actor,
                    Remark = $"智能预测自动请领（预计缺货 {row.DaysOutOfStock} 天，补货周期 {req.HorizonDays} 天）",
                    Now = now
                }, tx);
            created++;
            createdNos.Add(tNo);
        }
        tx.Commit();

        return ApiResponse<object>.Success(new { CreatedCount = created, TransferNos = createdNos },
            created > 0 ? $"已按智能预测一键生成 {created} 张待审核补货调拨单" : "当前各药品库存充足，无需生成补货单");
    }
}
