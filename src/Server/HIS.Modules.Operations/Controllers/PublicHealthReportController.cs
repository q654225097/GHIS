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

namespace HIS.Modules.Operations.Controllers;

/// <summary>
/// 公共卫生多报卡强触发中心（传染病/慢病/肿瘤/食源性）：
/// 1) 临床下达 ICD-10 诊断时自动匹配 ph_icd_trigger_rules，命中即强制生成待报卡并返回 HardBlock 拦截信号与时限倒计时
/// 2) 报卡提交（自动判定是否超时漏报）与公卫科审核/驳回闭环
/// 3) 全院公卫报卡逾期与待办统计看板
/// </summary>
[ApiController]
[Route("api/v4/public-health")]
public sealed class PublicHealthReportController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _session;

    public PublicHealthReportController(IDbConnectionFactory connFactory, IUserSession? session = null)
    {
        _connFactory = connFactory;
        _session = session;
    }

    private string ActorId => _session?.UserId?.Trim() ?? "SYSTEM";
    private string ActorName => _session?.DisplayName ?? _session?.UserName ?? ActorId;

    public sealed record EvaluateDiagnosisDto(
        string PatientId,
        string PatientName,
        string VisitOrEncounterId,
        string? DeptName,
        string IcdCode,
        string? DiagnosisName);

    public sealed record SubmitCardDto(string? ClinicalPayloadJson, string? ReportDoctor);
    public sealed record AuditCardDto(bool Approved, string? AuditNote);

    [HttpGet("rules")]
    public async Task<IActionResult> ListRules(CancellationToken ct)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        return Ok(await conn.QueryAsync("SELECT * FROM ph_icd_trigger_rules WHERE is_active = 1 ORDER BY report_deadline_hours ASC, icd_prefix"));
    }

    [HttpPost("evaluate-diagnosis")]
    public async Task<IActionResult> EvaluateDiagnosisTrigger([FromBody] EvaluateDiagnosisDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.IcdCode) || string.IsNullOrWhiteSpace(dto.PatientId) || string.IsNullOrWhiteSpace(dto.VisitOrEncounterId))
            return BadRequest(new { message = "ICD-10 编码、患者ID与就诊ID不能为空。" });

        var icd = dto.IcdCode.Trim().ToUpperInvariant();
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var rules = (await conn.QueryAsync("SELECT * FROM ph_icd_trigger_rules WHERE is_active = 1")).ToList();
        var matched = rules.FirstOrDefault(r => icd.StartsWith(((string)r.icd_prefix).ToUpperInvariant(), StringComparison.OrdinalIgnoreCase));

        if (matched == null)
        {
            return Ok(new { triggered = false, hardBlock = false, message = "当前诊断未命中公卫法定报卡触发规则。" });
        }

        string cardType = (string)matched.card_type;
        int deadlineHours = Convert.ToInt32(matched.report_deadline_hours);
        bool isHardBlock = Convert.ToInt32(matched.is_hard_block) == 1;

        // 幂等防重：同一就诊同类型同ICD不重复建卡
        var existing = await conn.QueryFirstOrDefaultAsync(
            "SELECT id, card_no, status, deadline_at_ms FROM ph_report_cards WHERE visit_or_encounter_id = @VisitId AND card_type = @CardType AND icd_code = @Icd",
            new { VisitId = dto.VisitOrEncounterId.Trim(), CardType = cardType, Icd = icd });

        if (existing != null)
        {
            string st = (string)existing.status;
            bool stillBlocked = isHardBlock && st == "PENDING_REPORT";
            return Ok(new
            {
                triggered = true,
                hardBlock = stillBlocked,
                cardId = (string)existing.id,
                cardNo = (string)existing.card_no,
                cardType,
                status = st,
                deadlineAtMs = Convert.ToInt64(existing.deadline_at_ms),
                message = stillBlocked
                    ? $"强制拦截：存在未提交的 {cardType} 报卡（{existing.card_no}），请先完成填报。"
                    : $"已存在对应公卫报卡（{existing.card_no}，状态：{st}）。"
            });
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var deadlineAt = now + (deadlineHours * 3600_000L);
        var id = Guid.NewGuid().ToString("N");
        var cardNo = $"PH{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(100, 999)}";

        await conn.ExecuteAsync(@"
INSERT INTO ph_report_cards (
    id, card_no, card_type, patient_id, patient_name, visit_or_encounter_id, dept_name,
    icd_code, disease_name, diagnosed_at_ms, deadline_at_ms, is_overdue, status,
    created_at_ms, created_by, updated_at_ms, updated_by, version_no
) VALUES (
    @Id, @CardNo, @CardType, @PatId, @PatName, @VisitId, @Dept,
    @Icd, @Disease, @Now, @Deadline, 0, 'PENDING_REPORT',
    @Now, @Actor, @Now, @Actor, 1
);", new
        {
            Id = id,
            CardNo = cardNo,
            CardType = cardType,
            PatId = dto.PatientId.Trim(),
            PatName = dto.PatientName.Trim(),
            VisitId = dto.VisitOrEncounterId.Trim(),
            Dept = dto.DeptName ?? "临床科室",
            Icd = icd,
            Disease = string.IsNullOrWhiteSpace(dto.DiagnosisName) ? (string)matched.disease_name : dto.DiagnosisName.Trim(),
            Now = now,
            Deadline = deadlineAt,
            Actor = ActorId
        });

        return Ok(new
        {
            triggered = true,
            hardBlock = isHardBlock,
            cardId = id,
            cardNo,
            cardType,
            diseaseClass = (string?)matched.disease_class,
            reportDeadlineHours = deadlineHours,
            deadlineAtMs = deadlineAt,
            status = "PENDING_REPORT",
            message = $"诊断 [{icd}] 命中法定公卫报卡（{cardType}），须在 {deadlineHours} 小时内完成填报。"
        });
    }

    [HttpGet("cards")]
    public async Task<IActionResult> ListCards([FromQuery] string? status = null, [FromQuery] string? cardType = null, CancellationToken ct = default)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var sql = "SELECT * FROM ph_report_cards WHERE 1=1";
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(status))
        {
            sql += " AND status = @Status";
            p.Add("Status", status.Trim().ToUpperInvariant());
        }
        if (!string.IsNullOrWhiteSpace(cardType))
        {
            sql += " AND card_type = @Type";
            p.Add("Type", cardType.Trim().ToUpperInvariant());
        }
        sql += " ORDER BY deadline_at_ms ASC LIMIT 200";
        return Ok(await conn.QueryAsync(sql, p));
    }

    [HttpPost("cards/{cardId}/submit")]
    public async Task<IActionResult> SubmitCard(string cardId, [FromBody] SubmitCardDto dto, CancellationToken ct)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var card = await conn.QueryFirstOrDefaultAsync("SELECT id, deadline_at_ms, status FROM ph_report_cards WHERE id = @Id", new { Id = cardId });
        if (card == null) return NotFound(new { message = "公卫报卡不存在。" });

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int isOverdue = now > Convert.ToInt64(card.deadline_at_ms) ? 1 : 0;

        await conn.ExecuteAsync(@"
UPDATE ph_report_cards
SET status = 'SUBMITTED',
    reported_at_ms = @Now,
    is_overdue = @Overdue,
    report_doctor = @Doctor,
    clinical_payload_json = @Payload,
    updated_at_ms = @Now,
    updated_by = @Actor,
    version_no = version_no + 1
WHERE id = @Id;", new
        {
            Now = now,
            Overdue = isOverdue,
            Doctor = string.IsNullOrWhiteSpace(dto.ReportDoctor) ? ActorName : dto.ReportDoctor.Trim(),
            Payload = dto.ClinicalPayloadJson,
            Actor = ActorId,
            Id = cardId
        });

        return Ok(new { cardId, status = "SUBMITTED", isOverdue = isOverdue == 1 });
    }

    [HttpPost("cards/{cardId}/audit")]
    public async Task<IActionResult> AuditCard(string cardId, [FromBody] AuditCardDto dto, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var nextStatus = dto.Approved ? "AUDITED" : "REJECTED";
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        int affected = await conn.ExecuteAsync(@"
UPDATE ph_report_cards
SET status = @Status,
    auditor_name = @Auditor,
    audit_note = @Note,
    updated_at_ms = @Now,
    updated_by = @Actor,
    version_no = version_no + 1
WHERE id = @Id AND status = 'SUBMITTED';", new
        {
            Status = nextStatus,
            Auditor = ActorName,
            Note = dto.AuditNote,
            Now = now,
            Actor = ActorId,
            Id = cardId
        });

        if (affected == 0) return BadRequest(new { message = "仅已提交(SUBMITTED)的报卡允许公卫科审核。" });
        return Ok(new { cardId, status = nextStatus, auditorName = ActorName });
    }

    [HttpGet("overdue-summary")]
    public async Task<IActionResult> GetOverdueSummary(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var summary = await conn.QueryFirstOrDefaultAsync(@"
SELECT COUNT(1) AS total_cards,
       SUM(CASE WHEN status = 'PENDING_REPORT' THEN 1 ELSE 0 END) AS pending_count,
       SUM(CASE WHEN status = 'SUBMITTED' THEN 1 ELSE 0 END) AS awaiting_audit_count,
       SUM(CASE WHEN status = 'AUDITED' THEN 1 ELSE 0 END) AS audited_count,
       SUM(CASE WHEN is_overdue = 1 OR (status = 'PENDING_REPORT' AND deadline_at_ms < @Now) THEN 1 ELSE 0 END) AS overdue_count
FROM ph_report_cards;", new { Now = now });
        return Ok(summary);
    }
}
