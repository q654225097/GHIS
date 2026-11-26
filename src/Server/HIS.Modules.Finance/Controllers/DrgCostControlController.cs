using HIS.BuildingBlocks.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Finance.Controllers;

/// <summary>
/// DRG/DIP 事中实时控费预警中心：
/// 1) DRG/DIP 病组基准权重与限额规则维护
/// 2) 实时聚合在院患者 fin_charge_ledgers 累计费用，与病组基准比对，判定 NORMAL / YELLOW_WARN / RED_OVERRUN / HIGH_OUTLIER / LOW_OUTLIER
/// 3) 全院在院患者一键扫描与特病单议申诉闭环
/// </summary>
[ApiController]
[RequireHisRole("ADMIN", "CASHIER", "IPD_ATTENDING", "IPD_RESIDENT")]
[Route("api/v4/drg-cost")]
public sealed class DrgCostControlController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _session;

    public DrgCostControlController(IDbConnectionFactory connFactory, IUserSession? session = null)
    {
        _connFactory = connFactory;
        _session = session;
    }

    private string ActorName => _session?.DisplayName ?? _session?.UserName ?? _session?.UserId ?? "SYSTEM";

    public sealed record UpsertBenchmarkDto(
        string GroupCode,
        string GroupName,
        string PayMode,
        string? IcdPrefix,
        decimal RwWeight,
        decimal BenchmarkFee,
        decimal WarnRatio,
        decimal HighOutlierRatio,
        decimal LowOutlierRatio,
        int StandardLosDays);

    public sealed record EvaluateEncounterDto(string GroupCode, string? PatientName, string? DeptName);
    public sealed record SpecialAppealDto(string AppealReason);

    [HttpGet("benchmarks")]
    public async Task<ApiResponse<object>> ListBenchmarks(CancellationToken ct)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        return ApiResponse<object>.Success(await conn.QueryAsync("SELECT * FROM drg_benchmark_rules WHERE is_active = 1 ORDER BY group_code"));
    }

    [HttpPost("benchmarks")]
    public async Task<ApiResponse<object>> UpsertBenchmark([FromBody] UpsertBenchmarkDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.GroupCode) || dto.BenchmarkFee <= 0)
            return ApiResponse<object>.Fail(400, "病组编码不能为空且基准费用必须大于 0。");

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var code = dto.GroupCode.Trim().ToUpperInvariant();
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var existingId = await conn.ExecuteScalarAsync<string?>("SELECT id FROM drg_benchmark_rules WHERE group_code = @Code", new { Code = code });
        if (existingId != null)
        {
            await conn.ExecuteAsync(@"
UPDATE drg_benchmark_rules
SET group_name = @Name, pay_mode = @PayMode, icd_prefix = @Icd, rw_weight = @Rw,
    benchmark_fee = @Fee, warn_ratio = @Warn, high_outlier_ratio = @High, low_outlier_ratio = @Low,
    standard_los_days = @Los, updated_at_ms = @Now
WHERE id = @Id;", new
            {
                Id = existingId,
                Name = dto.GroupName.Trim(),
                PayMode = string.IsNullOrWhiteSpace(dto.PayMode) ? "DRG" : dto.PayMode.Trim().ToUpperInvariant(),
                Icd = dto.IcdPrefix,
                Rw = dto.RwWeight,
                Fee = dto.BenchmarkFee,
                Warn = dto.WarnRatio <= 0 ? 0.80m : dto.WarnRatio,
                High = dto.HighOutlierRatio <= 0 ? 2.00m : dto.HighOutlierRatio,
                Low = dto.LowOutlierRatio <= 0 ? 0.40m : dto.LowOutlierRatio,
                Los = dto.StandardLosDays <= 0 ? 7 : dto.StandardLosDays,
                Now = now
            });
            return ApiResponse<object>.Success(new { id = existingId, groupCode = code, updated = true });
        }

        var id = Guid.NewGuid().ToString("N");
        await conn.ExecuteAsync(@"
INSERT INTO drg_benchmark_rules (
    id, group_code, group_name, pay_mode, icd_prefix, rw_weight, benchmark_fee,
    warn_ratio, high_outlier_ratio, low_outlier_ratio, standard_los_days, is_active, created_at_ms, updated_at_ms
) VALUES (
    @Id, @Code, @Name, @PayMode, @Icd, @Rw, @Fee,
    @Warn, @High, @Low, @Los, 1, @Now, @Now
);", new
        {
            Id = id,
            Code = code,
            Name = dto.GroupName.Trim(),
            PayMode = string.IsNullOrWhiteSpace(dto.PayMode) ? "DRG" : dto.PayMode.Trim().ToUpperInvariant(),
            Icd = dto.IcdPrefix,
            Rw = dto.RwWeight,
            Fee = dto.BenchmarkFee,
            Warn = dto.WarnRatio <= 0 ? 0.80m : dto.WarnRatio,
            High = dto.HighOutlierRatio <= 0 ? 2.00m : dto.HighOutlierRatio,
            Low = dto.LowOutlierRatio <= 0 ? 0.40m : dto.LowOutlierRatio,
            Los = dto.StandardLosDays <= 0 ? 7 : dto.StandardLosDays,
            Now = now
        });
        return ApiResponse<object>.Success(new { id, groupCode = code, created = true });
    }

    [HttpPost("evaluate/{encounterId}")]
    public async Task<ApiResponse<object>> EvaluateEncounter(string encounterId, [FromBody] EvaluateEncounterDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.GroupCode))
            return ApiResponse<object>.Fail(400, "请指定 DRG/DIP 病组编码。");

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var rule = await conn.QueryFirstOrDefaultAsync(
            "SELECT * FROM drg_benchmark_rules WHERE group_code = @Code AND is_active = 1",
            new { Code = dto.GroupCode.Trim().ToUpperInvariant() });
        if (rule == null) return ApiResponse<object>.Fail(404, $"未找到启用的 DRG/DIP 病组规则：{dto.GroupCode}");

        // 实时聚合真实住院费用明细 fin_charge_ledgers
        decimal totalFee = await conn.ExecuteScalarAsync<decimal>(
            "SELECT COALESCE(SUM(total_amount), 0) FROM fin_charge_ledgers WHERE visit_or_encounter_id = @EncId AND status = 1",
            new { EncId = encounterId });

        decimal benchmarkFee = Convert.ToDecimal(rule.benchmark_fee);
        decimal warnRatio = Convert.ToDecimal(rule.warn_ratio);
        decimal highRatio = Convert.ToDecimal(rule.high_outlier_ratio);
        decimal lowRatio = Convert.ToDecimal(rule.low_outlier_ratio);

        decimal ratio = benchmarkFee > 0 ? Math.Round(totalFee / benchmarkFee, 4) : 0m;
        decimal variance = Math.Round(benchmarkFee - totalFee, 2); // 正数为结余，负数为超支

        string level = ratio >= highRatio ? "HIGH_OUTLIER"
            : ratio >= 1.00m ? "RED_OVERRUN"
            : ratio >= warnRatio ? "YELLOW_WARN"
            : (totalFee > 0 && ratio <= lowRatio) ? "LOW_OUTLIER"
            : "NORMAL";

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var existingId = await conn.ExecuteScalarAsync<string?>("SELECT id FROM drg_realtime_alerts WHERE encounter_id = @EncId", new { EncId = encounterId });

        if (existingId != null)
        {
            await conn.ExecuteAsync(@"
UPDATE drg_realtime_alerts
SET group_code = @Code, group_name = @Name, pay_mode = @PayMode, benchmark_fee = @Bench,
    current_total_fee = @Total, fee_ratio = @Ratio, variance_amount = @Var,
    alert_level = @Level, evaluated_at_ms = @Now, updated_at_ms = @Now
WHERE id = @Id;", new
            {
                Id = existingId,
                Code = (string)rule.group_code,
                Name = (string)rule.group_name,
                PayMode = (string)rule.pay_mode,
                Bench = benchmarkFee,
                Total = totalFee,
                Ratio = ratio,
                Var = variance,
                Level = level,
                Now = now
            });
        }
        else
        {
            await conn.ExecuteAsync(@"
INSERT INTO drg_realtime_alerts (
    id, encounter_id, patient_name, dept_name, group_code, group_name, pay_mode,
    benchmark_fee, current_total_fee, fee_ratio, variance_amount, alert_level,
    special_case_appeal, evaluated_at_ms, created_at_ms, updated_at_ms
) VALUES (
    @Id, @EncId, @PatName, @DeptName, @Code, @Name, @PayMode,
    @Bench, @Total, @Ratio, @Var, @Level,
    0, @Now, @Now, @Now
);", new
            {
                Id = Guid.NewGuid().ToString("N"),
                EncId = encounterId,
                PatName = dto.PatientName ?? encounterId,
                DeptName = dto.DeptName ?? "临床科室",
                Code = (string)rule.group_code,
                Name = (string)rule.group_name,
                PayMode = (string)rule.pay_mode,
                Bench = benchmarkFee,
                Total = totalFee,
                Ratio = ratio,
                Var = variance,
                Level = level,
                Now = now
            });
        }

        return ApiResponse<object>.Success(new
        {
            encounterId,
            groupCode = (string)rule.group_code,
            groupName = (string)rule.group_name,
            benchmarkFee,
            currentTotalFee = totalFee,
            feeRatio = ratio,
            varianceAmount = variance,
            alertLevel = level,
            eligibleForSpecialAppeal = level is "HIGH_OUTLIER" or "RED_OVERRUN"
        });
    }

    [HttpGet("alerts")]
    public async Task<ApiResponse<object>> ListAlerts([FromQuery] string? alertLevel = null, CancellationToken ct = default)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var sql = "SELECT * FROM drg_realtime_alerts WHERE 1=1";
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(alertLevel))
        {
            sql += " AND alert_level = @Level";
            p.Add("Level", alertLevel.Trim().ToUpperInvariant());
        }
        sql += " ORDER BY fee_ratio DESC LIMIT 200";
        return ApiResponse<object>.Success(await conn.QueryAsync(sql, p));
    }

    [HttpPost("alerts/{encounterId}/appeal")]
    public async Task<ApiResponse<object>> SubmitSpecialCaseAppeal(string encounterId, [FromBody] SpecialAppealDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.AppealReason))
            return ApiResponse<object>.Fail(400, "特病单议申诉理由不能为空。");

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        int affected = await conn.ExecuteAsync(@"
UPDATE drg_realtime_alerts
SET special_case_appeal = 1,
    appeal_reason = @Reason,
    reviewed_by = @Actor,
    updated_at_ms = @Now
WHERE encounter_id = @EncId;", new { Reason = dto.AppealReason.Trim(), Actor = ActorName, Now = now, EncId = encounterId });

        if (affected == 0) return ApiResponse<object>.Fail(404, "未找到该住院记录的 DRG 控费评估单。");
        return ApiResponse<object>.Success(new { encounterId, specialCaseAppeal = true, reviewedBy = ActorName });
    }
}
