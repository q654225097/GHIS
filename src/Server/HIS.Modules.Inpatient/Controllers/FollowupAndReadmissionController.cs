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

namespace HIS.Modules.Inpatient.Controllers;

/// <summary>
/// 院后专病随访与 30 天非计划再入院追踪：
/// 1) 出院自动生成专病随访计划（7天/14天/30天节点）
/// 2) 随访执行登记（症状转归、用药依从性、患者满意度 1~5 分）
/// 3) 30 天非计划再入院自动检测（比对上次出院时间与本次入院时间，<=30天自动入库质控追踪）
/// </summary>
[ApiController]
[RequireHisRole("ADMIN", "IPD_ATTENDING", "IPD_RESIDENT", "WARD_NURSE", "OPD_DOC")]
[Route("api/v4/followup")]
public sealed class FollowupAndReadmissionController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _session;

    public FollowupAndReadmissionController(IDbConnectionFactory connFactory, IUserSession? session = null)
    {
        _connFactory = connFactory;
        _session = session;
    }

    private string ActorId => _session?.UserId?.Trim() ?? "SYSTEM";
    private string ActorName => _session?.DisplayName ?? _session?.UserName ?? ActorId;

    public sealed record GenerateFollowupDto(
        string EncounterId,
        string PatientId,
        string PatientName,
        string? PhoneNumber,
        string SpecialtyTag,
        long? DischargeAtMs,
        int FollowupAfterDays,
        string? Channel);

    public sealed record CompleteFollowupDto(
        string SymptomStatus,
        string MedicationAdherence,
        int SatisfactionScore,
        string? FollowupNote);

    public sealed record DetectReadmissionDto(
        string PatientId,
        string PatientName,
        string IndexEncounterId,
        long IndexDischargeAtMs,
        string ReadmitEncounterId,
        long? ReadmitAtMs,
        bool IsUnplanned,
        string? PrimaryDiagnosis);

    [HttpGet("plans")]
    public async Task<ApiResponse<object>> ListPlans([FromQuery] string? status = null, [FromQuery] string? specialtyTag = null, CancellationToken ct = default)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var sql = "SELECT * FROM followup_plans WHERE 1=1";
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(status))
        {
            sql += " AND status = @Status";
            p.Add("Status", status.Trim().ToUpperInvariant());
        }
        if (!string.IsNullOrWhiteSpace(specialtyTag))
        {
            sql += " AND specialty_tag = @Tag";
            p.Add("Tag", specialtyTag.Trim());
        }
        sql += " ORDER BY planned_date_int ASC LIMIT 200";
        return ApiResponse<object>.Success(await conn.QueryAsync(sql, p));
    }

    [HttpPost("plans/generate-from-discharge")]
    public async Task<ApiResponse<object>> GenerateFromDischarge([FromBody] GenerateFollowupDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.EncounterId) || string.IsNullOrWhiteSpace(dto.PatientId))
            return ApiResponse<object>.Fail(400, "出院就诊ID与患者ID不能为空。");

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var dischargeMs = dto.DischargeAtMs ?? now;
        int days = dto.FollowupAfterDays <= 0 ? 7 : dto.FollowupAfterDays;
        var plannedDate = DateTimeOffset.FromUnixTimeMilliseconds(dischargeMs).UtcDateTime.AddDays(days);
        int plannedDateInt = int.Parse(plannedDate.ToString("yyyyMMdd"));

        var id = Guid.NewGuid().ToString("N");
        var planNo = $"FU{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(100, 999)}";

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        await conn.ExecuteAsync(@"
INSERT INTO followup_plans (
    id, plan_no, encounter_id, patient_id, patient_name, phone_number, specialty_tag,
    discharge_at_ms, planned_date_int, channel, status, created_at_ms, created_by, updated_at_ms, updated_by
) VALUES (
    @Id, @PlanNo, @EncId, @PatId, @PatName, @Phone, @Tag,
    @DisAt, @PlanDate, @Channel, 'SCHEDULED', @Now, @Actor, @Now, @Actor
);", new
        {
            Id = id,
            PlanNo = planNo,
            EncId = dto.EncounterId.Trim(),
            PatId = dto.PatientId.Trim(),
            PatName = dto.PatientName.Trim(),
            Phone = dto.PhoneNumber,
            Tag = string.IsNullOrWhiteSpace(dto.SpecialtyTag) ? "常规出院随访" : dto.SpecialtyTag.Trim(),
            DisAt = dischargeMs,
            PlanDate = plannedDateInt,
            Channel = string.IsNullOrWhiteSpace(dto.Channel) ? "PHONE" : dto.Channel.Trim().ToUpperInvariant(),
            Now = now,
            Actor = ActorId
        });

        return ApiResponse<object>.Success(new { id, planNo, plannedDateInt, status = "SCHEDULED" });
    }

    [HttpPost("plans/{planId}/complete")]
    public async Task<ApiResponse<object>> CompleteFollowup(string planId, [FromBody] CompleteFollowupDto dto, CancellationToken ct)
    {
        if (dto.SatisfactionScore is < 1 or > 5)
            return ApiResponse<object>.Fail(400, "患者满意度评分必须在 1~5 之间。");

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        int affected = await conn.ExecuteAsync(@"
UPDATE followup_plans
SET status = 'COMPLETED',
    symptom_status = @Symptom,
    medication_adherence = @Adherence,
    satisfaction_score = @Score,
    followup_note = @Note,
    followed_by = @ActorName,
    completed_at_ms = @Now,
    updated_at_ms = @Now,
    updated_by = @Actor
WHERE id = @Id;", new
        {
            Symptom = dto.SymptomStatus.Trim(),
            Adherence = dto.MedicationAdherence.Trim(),
            Score = dto.SatisfactionScore,
            Note = dto.FollowupNote,
            ActorName,
            Now = now,
            Actor = ActorId,
            Id = planId
        });

        if (affected == 0) return ApiResponse<object>.Fail(404, "随访计划不存在。");
        return ApiResponse<object>.Success(new { planId, status = "COMPLETED", satisfactionScore = dto.SatisfactionScore });
    }

    [HttpPost("readmissions/detect")]
    public async Task<ApiResponse<object>> DetectReadmission30d([FromBody] DetectReadmissionDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.PatientId) || string.IsNullOrWhiteSpace(dto.IndexEncounterId) || string.IsNullOrWhiteSpace(dto.ReadmitEncounterId))
            return ApiResponse<object>.Fail(400, "患者ID、原出院ID与新入院ID不能为空。");

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var readmitAt = dto.ReadmitAtMs ?? now;
        if (readmitAt < dto.IndexDischargeAtMs)
            return ApiResponse<object>.Fail(400, "再入院时间不能早于原出院时间。");

        int intervalDays = (int)Math.Floor((readmitAt - dto.IndexDischargeAtMs) / 86400000.0);
        if (intervalDays > 30)
        {
            return ApiResponse<object>.Success(new { isReadmission30d = false, intervalDays, message = $"间隔 {intervalDays} 天超过 30 天，不计入 30 天再入院质控。" });
        }

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var existingId = await conn.ExecuteScalarAsync<string?>(
            "SELECT id FROM readmission_tracking WHERE readmit_encounter_id = @ReadmitId",
            new { ReadmitId = dto.ReadmitEncounterId.Trim() });

        if (existingId != null)
        {
            return ApiResponse<object>.Success(new { isReadmission30d = true, id = existingId, intervalDays, alreadyTracked = true });
        }

        var id = Guid.NewGuid().ToString("N");
        await conn.ExecuteAsync(@"
INSERT INTO readmission_tracking (
    id, patient_id, patient_name, index_encounter_id, readmit_encounter_id,
    index_discharge_at_ms, readmit_at_ms, interval_days, is_unplanned,
    primary_diagnosis, qc_status, created_at_ms, updated_at_ms
) VALUES (
    @Id, @PatId, @PatName, @IdxEnc, @ReadmitEnc,
    @DisAt, @ReadmitAt, @Days, @Unplanned,
    @Diag, 'PENDING_REVIEW', @Now, @Now
);", new
        {
            Id = id,
            PatId = dto.PatientId.Trim(),
            PatName = dto.PatientName.Trim(),
            IdxEnc = dto.IndexEncounterId.Trim(),
            ReadmitEnc = dto.ReadmitEncounterId.Trim(),
            DisAt = dto.IndexDischargeAtMs,
            ReadmitAt = readmitAt,
            Days = intervalDays,
            Unplanned = dto.IsUnplanned ? 1 : 0,
            Diag = dto.PrimaryDiagnosis,
            Now = now
        });

        return ApiResponse<object>.Success(new { isReadmission30d = true, id, intervalDays, isUnplanned = dto.IsUnplanned, qcStatus = "PENDING_REVIEW" });
    }

    [HttpGet("readmissions")]
    public async Task<ApiResponse<object>> ListReadmissions(CancellationToken ct)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var items = await conn.QueryAsync("SELECT * FROM readmission_tracking ORDER BY readmit_at_ms DESC LIMIT 200");
        var stats = await conn.QueryFirstOrDefaultAsync(@"
SELECT COUNT(1) AS total_30d,
       SUM(CASE WHEN is_unplanned = 1 THEN 1 ELSE 0 END) AS unplanned_30d,
       AVG(interval_days) AS avg_interval_days
FROM readmission_tracking;");
        return ApiResponse<object>.Success(new { stats, items });
    }
}
