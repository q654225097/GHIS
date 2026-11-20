using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.BloodBank.Models;

namespace HIS.Modules.BloodBank.Services;

public class BloodBankService : IBloodBankContract
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IRepository<BldInventoryBag> _bagRepo;
    private readonly IRepository<BldStockPolicy> _policyRepo;

    public BloodBankService(
        IDbConnectionFactory connFactory,
        IRepository<BldInventoryBag> bagRepo,
        IRepository<BldStockPolicy> policyRepo)
    {
        _connFactory = connFactory;
        _bagRepo = bagRepo;
        _policyRepo = policyRepo;
    }

    public async Task<TransfusionSafetyCheckResultDto> CheckSafetyAsync(
        string bloodRequestId,
        CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();

        // Query the blood request
        var req = await conn.QuerySingleOrDefaultAsync<(string PatientId, string AboType, string RhType, string BloodProductCode, int VolumeMl)>(
            "SELECT patient_id, abo_type, rh_type, blood_product_code, volume_ml FROM mtc_blood_requests WHERE id = @Id",
            new { Id = bloodRequestId });

        if (string.IsNullOrWhiteSpace(req.PatientId))
        {
            return new TransfusionSafetyCheckResultDto(false, "用血申请单不存在", true);
        }

        // Query latest HGB and PLT from lab results
        var latestHgb = await conn.QueryFirstOrDefaultAsync<decimal?>(
            """
            SELECT value_num FROM mtc_lab_result_items
            WHERE patient_id = @PatientId AND (item_code LIKE '%HGB%' OR item_name LIKE '%血红蛋白%')
            ORDER BY reported_at_ms DESC LIMIT 1
            """,
            new { PatientId = req.PatientId });

        var latestPlt = await conn.QueryFirstOrDefaultAsync<decimal?>(
            """
            SELECT value_num FROM mtc_lab_result_items
            WHERE patient_id = @PatientId AND (item_code LIKE '%PLT%' OR item_name LIKE '%血小板%')
            ORDER BY reported_at_ms DESC LIMIT 1
            """,
            new { PatientId = req.PatientId });

        // Rational blood use safety rule checks:
        // Red Blood Cells: if HGB > 100 g/L, restrict unless active hemorrhage or cardiopulmonary insufficiency
        if (req.BloodProductCode.Contains("RBC", StringComparison.OrdinalIgnoreCase) || req.BloodProductCode.Contains("红细胞", StringComparison.OrdinalIgnoreCase))
        {
            if (latestHgb.HasValue && latestHgb.Value > 100)
            {
                return new TransfusionSafetyCheckResultDto(
                    SafeToTransfuse: false,
                    WarningMessage: $"【合理用血警示】患者最新血红蛋白 HGB={latestHgb.Value}g/L > 100g/L，依据卫生健康委临床输血技术规范，不符合输注红细胞常规适应症！",
                    HardBlocked: false
                );
            }
        }

        // Platelets: if PLT > 50*10^9/L, warn
        if (req.BloodProductCode.Contains("PLT", StringComparison.OrdinalIgnoreCase) || req.BloodProductCode.Contains("血小板", StringComparison.OrdinalIgnoreCase))
        {
            if (latestPlt.HasValue && latestPlt.Value > 50)
            {
                return new TransfusionSafetyCheckResultDto(
                    SafeToTransfuse: false,
                    WarningMessage: $"【合理用血警示】患者最新血小板计数 PLT={latestPlt.Value}×10⁹/L > 50×10⁹/L，请确认是否存在手术止血异常指征。",
                    HardBlocked: false
                );
            }
        }

        // Check large volume blood use approval requirement (> 1600ml / 8U)
        if (req.VolumeMl > 1600)
        {
            return new TransfusionSafetyCheckResultDto(
                SafeToTransfuse: true,
                WarningMessage: "【大量用血分级审批提示】本次申请用血量超过 1600ml，已自动触发医务部/副院长审批流程备查。",
                HardBlocked: false
            );
        }

        return new TransfusionSafetyCheckResultDto(true, null, false);
    }

    public async Task<IReadOnlyList<BloodStockAdvisoryDto>> GetStockAdvisoryAsync(CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();

        var policies = await _policyRepo.QueryAsync("SELECT * FROM bld_stock_policies WHERE is_active = 1");
        var list = new List<BloodStockAdvisoryDto>();

        foreach (var p in policies)
        {
            int currentStock = await conn.ExecuteScalarAsync<int>(
                """
                SELECT count(*) FROM bld_inventory_bags
                WHERE blood_code = @BloodCode AND abo_type = @AboType AND rh_type = @RhType AND status = 'IN_STOCK'
                """,
                new { p.BloodCode, p.AboType, p.RhType });

            int pendingDemand = await conn.ExecuteScalarAsync<int>(
                """
                SELECT count(*) FROM mtc_blood_requests
                WHERE blood_product_code = @BloodCode AND abo_type = @AboType AND status IN (0, 1)
                """,
                new { p.BloodCode, p.AboType });

            string advisory = "库存正常";
            if (currentStock <= p.WarningStockBags)
            {
                advisory = "CRITICAL_SHORTAGE 极度紧缺，紧急申领";
            }
            else if (currentStock < p.SafeStockBags || currentStock < pendingDemand)
            {
                advisory = "LOW_STOCK 备血预警，建议向中心血站申领";
            }

            string name = p.BloodCode switch
            {
                "RBC" => "悬浮红细胞",
                "FFP" => "新鲜冰冻血浆",
                "PLT" => "单采血小板",
                "CRYO" => "冷沉淀凝血因子",
                _ => p.BloodCode
            };

            list.Add(new BloodStockAdvisoryDto(
                BloodCode: p.BloodCode,
                BloodName: name,
                AboType: p.AboType,
                RhType: p.RhType,
                CurrentStock: currentStock,
                SafeStock: p.SafeStockBags,
                PendingDemand: pendingDemand,
                AdvisoryStatus: advisory
            ));
        }

        return list;
    }
}
