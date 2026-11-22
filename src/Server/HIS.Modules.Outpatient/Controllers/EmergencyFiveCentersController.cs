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

namespace HIS.Modules.Outpatient.Controllers;

/// <summary>
/// 急诊五大中心时间轴质控（胸痛/卒中/创伤/危重孕产妇/危重新生儿）：
/// 1) 专案建档与标准 SLA 目标绑定（胸痛 D2W<=90m / 卒中 DNT<=60m / 创伤 60m / 孕产妇 30m / 新生儿 15m）
/// 2) 急救时间节点毫秒级留痕与 SLA 实时超时判定（ECG<=10m, 肌钙蛋白<=20m, 头颅CT<=25m）
/// 3) 结案强制超时归因（超时未填原因 HTTP 400 拦截）与质控达标率看板
/// </summary>
[ApiController]
[Route("api/v4/five-centers")]
public sealed class EmergencyFiveCentersController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _session;
    private readonly IAuditTrailContract? _auditTrail;

    public EmergencyFiveCentersController(
        IDbConnectionFactory connFactory,
        IUserSession? session = null,
        IAuditTrailContract? auditTrail = null)
    {
        _connFactory = connFactory;
        _session = session;
        _auditTrail = auditTrail;
    }

    private string ActorId => _session?.UserId?.Trim() ?? "SYSTEM";
    private string ActorName => _session?.DisplayName ?? _session?.UserName ?? ActorId;

    public sealed record OpenCenterCaseDto(
        string CenterType,
        string PatientId,
        string PatientName,
        string? VisitOrEncounterId,
        long? OnsetAtMs,
        long? DoorAtMs,
        string? AttendingDoctor);

    public sealed record RecordMilestoneDto(
        string MilestoneCode,
        string MilestoneName,
        long? OccurredAtMs,
        string? Note);

    public sealed record CompleteCenterCaseDto(
        string Outcome,
        string? OvertimeReason);

    private static (string MetricName, int LimitMin) GetCenterSla(string centerType) => centerType.ToUpperInvariant() switch
    {
        "CHEST_PAIN" => ("D2W", 90),
        "STROKE" => ("DNT", 60),
        "TRAUMA" => ("GOLDEN_HOUR", 60),
        "MATERNAL" => ("DDI", 30),
        "NEONATAL" => ("RESUS", 15),
        _ => ("RESCUE", 60)
    };

    private static int GetMilestoneSlaMinutes(string code) => code.ToUpperInvariant() switch
    {
        "FIRST_ECG" => 10,
        "TROPONIN" => 20,
        "HEAD_CT" => 25,
        "THROMBOLYSIS" => 60,
        "WIRE_CROSS" => 90,
        "EMERGENCY_CS" => 30,
        "NEONATAL_INTUBATION" => 15,
        _ => 0
    };

    [HttpGet("cases")]
    public async Task<IActionResult> ListCases([FromQuery] string? centerType = null, [FromQuery] string? status = null, CancellationToken ct = default)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var sql = "SELECT * FROM er_five_center_cases WHERE 1=1";
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(centerType))
        {
            sql += " AND center_type = @CenterType";
            p.Add("CenterType", centerType.Trim().ToUpperInvariant());
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            sql += " AND status = @Status";
            p.Add("Status", status.Trim().ToUpperInvariant());
        }
        sql += " ORDER BY door_at_ms DESC LIMIT 200";
        var rows = await conn.QueryAsync(sql, p);
        return Ok(rows);
    }

    [HttpPost("cases")]
    public async Task<IActionResult> OpenCase([FromBody] OpenCenterCaseDto dto, CancellationToken ct)
    {
        var center = (dto.CenterType ?? string.Empty).Trim().ToUpperInvariant();
        if (center is not ("CHEST_PAIN" or "STROKE" or "TRAUMA" or "MATERNAL" or "NEONATAL"))
            return BadRequest(new { message = "不支持的急救中心类型，仅支持 CHEST_PAIN/STROKE/TRAUMA/MATERNAL/NEONATAL。" });
        if (string.IsNullOrWhiteSpace(dto.PatientId) || string.IsNullOrWhiteSpace(dto.PatientName))
            return BadRequest(new { message = "患者ID与患者姓名不能为空。" });

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var doorAt = dto.DoorAtMs ?? now;
        var (metricName, limitMin) = GetCenterSla(center);
        var id = Guid.NewGuid().ToString("N");
        var caseNo = $"FC{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(100, 999)}";

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(@"
INSERT INTO er_five_center_cases (
    id, case_no, center_type, patient_id, patient_name, visit_or_encounter_id,
    onset_at_ms, door_at_ms, target_metric_name, target_limit_minutes,
    is_overtime, outcome, attending_doctor, status,
    schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no
) VALUES (
    @Id, @CaseNo, @CenterType, @PatientId, @PatientName, @VisitId,
    @OnsetAt, @DoorAt, @MetricName, @LimitMin,
    0, 'IN_PROGRESS', @Doctor, 'ACTIVE',
    1, @Now, @Actor, @Now, @Actor, 1
);", new
        {
            Id = id,
            CaseNo = caseNo,
            CenterType = center,
            PatientId = dto.PatientId.Trim(),
            PatientName = dto.PatientName.Trim(),
            VisitId = dto.VisitOrEncounterId,
            OnsetAt = dto.OnsetAtMs,
            DoorAt = doorAt,
            MetricName = metricName,
            LimitMin = limitMin,
            Doctor = dto.AttendingDoctor ?? ActorName,
            Now = now,
            Actor = ActorId
        }, tx);

        await conn.ExecuteAsync(@"
INSERT INTO er_five_center_milestones (
    id, case_id, milestone_code, milestone_name, occurred_at_ms,
    elapsed_from_door_min, sla_limit_min, is_sla_breached, operator_name, note, created_at_ms, created_by
) VALUES (
    @MsId, @CaseId, 'DOOR', '患者到院(Door)', @DoorAt,
    0, 0, 0, @OpName, '五大中心绿色通道启动', @Now, @Actor
);", new { MsId = Guid.NewGuid().ToString("N"), CaseId = id, DoorAt = doorAt, OpName = ActorName, Now = now, Actor = ActorId }, tx);

        await tx.CommitAsync(ct);

        return Ok(new
        {
            id,
            caseNo,
            centerType = center,
            targetMetricName = metricName,
            targetLimitMinutes = limitMin,
            doorAtMs = doorAt
        });
    }

    [HttpGet("cases/{caseId}/milestones")]
    public async Task<IActionResult> GetMilestones(string caseId, CancellationToken ct)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync(
            "SELECT * FROM er_five_center_milestones WHERE case_id = @CaseId ORDER BY occurred_at_ms ASC",
            new { CaseId = caseId });
        return Ok(rows);
    }

    [HttpPost("cases/{caseId}/milestones")]
    public async Task<IActionResult> RecordMilestone(string caseId, [FromBody] RecordMilestoneDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.MilestoneCode) || string.IsNullOrWhiteSpace(dto.MilestoneName))
            return BadRequest(new { message = "时间节点编码与名称不能为空。" });

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var c = await conn.QueryFirstOrDefaultAsync(
            "SELECT id, door_at_ms, target_limit_minutes, center_type, status FROM er_five_center_cases WHERE id = @Id",
            new { Id = caseId });
        if (c == null) return NotFound(new { message = "急救专案不存在。" });

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var occurredAt = dto.OccurredAtMs ?? now;
        long doorAt = Convert.ToInt64(c.door_at_ms);
        if (occurredAt < doorAt)
            return BadRequest(new { message = "节点发生时间不能早于患者到院时间。" });

        int elapsedMin = (int)Math.Round((occurredAt - doorAt) / 60000.0, MidpointRounding.AwayFromZero);
        var code = dto.MilestoneCode.Trim().ToUpperInvariant();
        int slaLimit = GetMilestoneSlaMinutes(code);
        int breached = (slaLimit > 0 && elapsedMin > slaLimit) ? 1 : 0;

        await using var tx = await conn.BeginTransactionAsync(ct);

        var msId = Guid.NewGuid().ToString("N");
        await conn.ExecuteAsync(@"
INSERT INTO er_five_center_milestones (
    id, case_id, milestone_code, milestone_name, occurred_at_ms,
    elapsed_from_door_min, sla_limit_min, is_sla_breached, operator_name, note, created_at_ms, created_by
) VALUES (
    @MsId, @CaseId, @Code, @Name, @OccurredAt,
    @Elapsed, @SlaLimit, @Breached, @OpName, @Note, @Now, @Actor
);", new
        {
            MsId = msId,
            CaseId = caseId,
            Code = code,
            Name = dto.MilestoneName.Trim(),
            OccurredAt = occurredAt,
            Elapsed = elapsedMin,
            SlaLimit = slaLimit,
            Breached = breached,
            OpName = ActorName,
            Note = dto.Note,
            Now = now,
            Actor = ActorId
        }, tx);

        // 同步核心时间轴列并动态更新关键质控指标
        int targetLimit = Convert.ToInt32(c.target_limit_minutes);
        int caseOvertime = elapsedMin > targetLimit ? 1 : 0;

        var updateSql = code switch
        {
            "FIRST_ECG" => "UPDATE er_five_center_cases SET first_ecg_at_ms = @OccurredAt, updated_at_ms = @Now, updated_by = @Actor WHERE id = @CaseId",
            "TROPONIN" => "UPDATE er_five_center_cases SET troponin_at_ms = @OccurredAt, updated_at_ms = @Now, updated_by = @Actor WHERE id = @CaseId",
            "HEAD_CT" => "UPDATE er_five_center_cases SET ct_at_ms = @OccurredAt, updated_at_ms = @Now, updated_by = @Actor WHERE id = @CaseId",
            "THROMBOLYSIS" => "UPDATE er_five_center_cases SET thrombolysis_at_ms = @OccurredAt, actual_minutes = @Elapsed, is_overtime = @CaseOvertime, updated_at_ms = @Now, updated_by = @Actor WHERE id = @CaseId",
            "WIRE_CROSS" => "UPDATE er_five_center_cases SET wire_cross_at_ms = @OccurredAt, actual_minutes = @Elapsed, is_overtime = @CaseOvertime, updated_at_ms = @Now, updated_by = @Actor WHERE id = @CaseId",
            "EMERGENCY_CS" or "NEONATAL_INTUBATION" or "DEFINITIVE_TREATMENT" =>
                "UPDATE er_five_center_cases SET actual_minutes = @Elapsed, is_overtime = @CaseOvertime, updated_at_ms = @Now, updated_by = @Actor WHERE id = @CaseId",
            _ => "UPDATE er_five_center_cases SET updated_at_ms = @Now, updated_by = @Actor WHERE id = @CaseId"
        };

        await conn.ExecuteAsync(updateSql, new
        {
            OccurredAt = occurredAt,
            Elapsed = elapsedMin,
            CaseOvertime = caseOvertime,
            Now = now,
            Actor = ActorId,
            CaseId = caseId
        }, tx);

        await tx.CommitAsync(ct);

        return Ok(new
        {
            milestoneId = msId,
            milestoneCode = code,
            elapsedFromDoorMin = elapsedMin,
            slaLimitMin = slaLimit,
            isSlaBreached = breached == 1
        });
    }

    [HttpPost("cases/{caseId}/complete")]
    public async Task<IActionResult> CompleteCase(string caseId, [FromBody] CompleteCenterCaseDto dto, CancellationToken ct)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var c = await conn.QueryFirstOrDefaultAsync(
            "SELECT id, door_at_ms, target_limit_minutes, actual_minutes, is_overtime FROM er_five_center_cases WHERE id = @Id",
            new { Id = caseId });
        if (c == null) return NotFound(new { message = "急救专案不存在。" });

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int actualMin = c.actual_minutes != null
            ? Convert.ToInt32(c.actual_minutes)
            : (int)Math.Round((now - Convert.ToInt64(c.door_at_ms)) / 60000.0, MidpointRounding.AwayFromZero);
        int targetLimit = Convert.ToInt32(c.target_limit_minutes);
        bool isOvertime = actualMin > targetLimit || Convert.ToInt32(c.is_overtime) == 1;

        if (isOvertime && string.IsNullOrWhiteSpace(dto.OvertimeReason))
            return BadRequest(new { message = $"该专案耗时 {actualMin} 分钟超过标准限额 {targetLimit} 分钟，结案必须填写超时归因说明。" });

        await conn.ExecuteAsync(@"
UPDATE er_five_center_cases
SET actual_minutes = @ActualMin,
    is_overtime = @Overtime,
    overtime_reason = @Reason,
    outcome = @Outcome,
    status = 'COMPLETED',
    updated_at_ms = @Now,
    updated_by = @Actor,
    version_no = version_no + 1
WHERE id = @Id;", new
        {
            ActualMin = actualMin,
            Overtime = isOvertime ? 1 : 0,
            Reason = dto.OvertimeReason,
            Outcome = string.IsNullOrWhiteSpace(dto.Outcome) ? "STABILIZED" : dto.Outcome.Trim().ToUpperInvariant(),
            Now = now,
            Actor = ActorId,
            Id = caseId
        });

        return Ok(new { caseId, actualMinutes = actualMin, isOvertime, status = "COMPLETED" });
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(CancellationToken ct)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var stats = await conn.QueryAsync(@"
SELECT center_type,
       COUNT(1) AS total_cases,
       SUM(CASE WHEN status = 'ACTIVE' THEN 1 ELSE 0 END) AS active_cases,
       SUM(CASE WHEN is_overtime = 0 THEN 1 ELSE 0 END) AS on_time_cases,
       SUM(CASE WHEN is_overtime = 1 THEN 1 ELSE 0 END) AS overtime_cases,
       AVG(actual_minutes) AS avg_minutes
FROM er_five_center_cases
GROUP BY center_type;");
        return Ok(stats);
    }
}
