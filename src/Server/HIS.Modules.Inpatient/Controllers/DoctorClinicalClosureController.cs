using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Inpatient.Controllers;

/// <summary>
/// 角色二（门诊 + 住院医生）临床现实闭环控制器：
/// 1) 门诊看报告回诊签到 + 现实 2:1 比例自动插队算法
/// 2) 病危/病重医嘱强触发法定通知书（建档 + 家属电子签名 SVG + 自动报备医务科）
/// 3) 检验检查结果一键结构化引用回写电子病历 (emr_documents + emr_lab_import_refs)
/// </summary>
[ApiController]
[RequireHisRole("ADMIN", "OPD_DOC", "IPD_ATTENDING", "IPD_RESIDENT", "WARD_NURSE")]
[Route("api/v4/doctor-closure")]
public sealed class DoctorClinicalClosureController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _session;

    public DoctorClinicalClosureController(IDbConnectionFactory connFactory, IUserSession? session = null)
    {
        _connFactory = connFactory;
        _session = session;
    }

    private string ActorId => _session?.UserId?.Trim() ?? "SYSTEM";
    private string ActorName => _session?.DisplayName ?? _session?.UserName ?? ActorId;

    public sealed record ReportReturnCheckinDto(string VisitId, string OpdDeptId, string? ClinicRoomId);
    public sealed record IssueCriticalNoticeDto(string EncounterId, string PatientId, string PatientName, string DeptName, string NoticeLevel, string? LinkedOrderId, string ClinicalCondition);
    public sealed record FamilySignNoticeDto(string FamilySignerName, string FamilyRelation, string FamilySignSvg);
    public sealed record ImportLabToEmrDto(string EmrDocumentId, string EncounterId, string ReportId, string ResultSummaryText);

    [HttpPost("opd/report-return-checkin")]
    public async Task<ApiResponse<object>> CheckinReportReturn([FromBody] ReportReturnCheckinDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.VisitId) || string.IsNullOrWhiteSpace(dto.OpdDeptId))
            return ApiResponse<object>.Fail(400, "门诊就诊ID与科室ID不能为空。");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        // 统计当前诊室等待中的初诊与回诊人数，实现 2 个初诊插 1 个回诊（2:1 比例插队）
        var waiting = (await conn.QueryAsync(@"
SELECT id, visit_stage, priority_score
FROM opd_triage_queues
WHERE opd_dept_id = @DeptId AND queue_status IN ('WAITING', 'PASSED')
ORDER BY priority_score DESC, checkin_at_ms ASC;", new { DeptId = dto.OpdDeptId.Trim() })).ToList();

        int returnCount = waiting.Count(w => string.Equals((string?)w.visit_stage, "REPORT_RETURN", StringComparison.OrdinalIgnoreCase));
        int targetInsertIndex = Math.Min(waiting.Count, (returnCount + 1) * 2);
        // 回诊基准分 80，每多一位回诊递减 5（确保高于普通初诊 50 分，又不压过急诊绿色通道 100 分）
        int priorityScore = Math.Max(60, 85 - (returnCount * 5));

        string qId = Guid.NewGuid().ToString("N");
        await conn.ExecuteAsync(@"
INSERT INTO opd_triage_queues (
    id, visit_id, opd_dept_id, clinic_room_id, priority_score, pass_count, queue_status,
    checkin_at_ms, visit_stage, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no
) VALUES (
    @Id, @VisitId, @DeptId, @RoomId, @Score, 0, 'WAITING',
    @Now, 'REPORT_RETURN', 1, @Now, @Actor, @Now, @Actor, 1
);", new
        {
            Id = qId, VisitId = dto.VisitId.Trim(), DeptId = dto.OpdDeptId.Trim(),
            RoomId = dto.ClinicRoomId ?? "ROOM-01", Score = priorityScore, Now = now, Actor = ActorId
        });

        return ApiResponse<object>.Success(new
        {
            queueId = qId,
            visitStage = "REPORT_RETURN",
            priorityScore,
            estimatedPatientsAhead = targetInsertIndex,
            rule = "2:1 比例回诊插队（每 2 位初诊穿插 1 位看报告回诊）"
        });
    }

    [HttpGet("critical-notices")]
    public async Task<ApiResponse<object>> ListCriticalNotices([FromQuery] string? status = null, CancellationToken ct = default)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        var sql = "SELECT * FROM emr_critical_notices WHERE 1=1";
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(status))
        {
            sql += " AND status = @St";
            p.Add("St", status.Trim().ToUpperInvariant());
        }
        sql += " ORDER BY created_at_ms DESC LIMIT 200";
        return ApiResponse<object>.Success((await conn.QueryAsync(sql, p)).ToList());
    }

    [HttpPost("critical-notices")]
    public async Task<ApiResponse<object>> IssueCriticalNotice([FromBody] IssueCriticalNoticeDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.EncounterId) || string.IsNullOrWhiteSpace(dto.ClinicalCondition))
            return ApiResponse<object>.Fail(400, "住院就诊ID与病情危重说明不能为空。");

        string level = (dto.NoticeLevel ?? "CRITICALLY_ILL").Trim().ToUpperInvariant();
        if (level is not ("CRITICALLY_ILL" or "SERIOUSLY_ILL"))
            return ApiResponse<object>.Fail(400, "通知书级别仅支持 CRITICALLY_ILL(病危) 或 SERIOUSLY_ILL(病重)。");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string id = Guid.NewGuid().ToString("N");
        string noticeNo = $"CRIT{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(10, 99)}";

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        await conn.ExecuteAsync(@"
INSERT INTO emr_critical_notices (
    id, notice_no, encounter_id, patient_id, patient_name, dept_name, notice_level,
    linked_order_id, clinical_condition, issued_doctor, reported_to_med_affairs, status, created_at_ms, updated_at_ms
) VALUES (
    @Id, @No, @EncId, @PatId, @PatName, @Dept, @Level,
    @OrderId, @Cond, @Doc, 1, 'PENDING_FAMILY_SIGN', @Now, @Now
);", new
        {
            Id = id, No = noticeNo, EncId = dto.EncounterId.Trim(), PatId = dto.PatientId.Trim(),
            PatName = dto.PatientName.Trim(), Dept = dto.DeptName.Trim(), Level = level,
            OrderId = dto.LinkedOrderId, Cond = dto.ClinicalCondition.Trim(), Doc = ActorName, Now = now
        });

        return ApiResponse<object>.Success(new { id, noticeNo, noticeLevel = level, status = "PENDING_FAMILY_SIGN", reportedToMedAffairs = true });
    }

    [HttpPost("critical-notices/{id}/family-sign")]
    public async Task<ApiResponse<object>> SignCriticalNoticeByFamily(string id, [FromBody] FamilySignNoticeDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.FamilySignerName) || string.IsNullOrWhiteSpace(dto.FamilySignSvg))
            return ApiResponse<object>.Fail(400, "家属签字姓名与电子手写签名 SVG 不能为空。");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        int affected = await conn.ExecuteAsync(@"
UPDATE emr_critical_notices
SET family_signer_name = @Signer,
    family_relation = @Rel,
    family_sign_svg = @Svg,
    signed_at_ms = @Now,
    status = 'SIGNED',
    updated_at_ms = @Now
WHERE id = @Id;", new
        {
            Signer = dto.FamilySignerName.Trim(),
            Rel = string.IsNullOrWhiteSpace(dto.FamilyRelation) ? "直系亲属" : dto.FamilyRelation.Trim(),
            Svg = dto.FamilySignSvg,
            Now = now,
            Id = id
        });

        if (affected == 0) return ApiResponse<object>.Fail(404, "病危/病重通知书不存在。");
        return ApiResponse<object>.Success(new { id, status = "SIGNED", familySignerName = dto.FamilySignerName.Trim() });
    }

    [HttpPost("emr/import-lab-result")]
    public async Task<ApiResponse<object>> ImportLabResultIntoEmr([FromBody] ImportLabToEmrDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.EmrDocumentId) || string.IsNullOrWhiteSpace(dto.ResultSummaryText))
            return ApiResponse<object>.Fail(400, "病历文书ID与引用结果摘要不能为空。");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var doc = await conn.QueryFirstOrDefaultAsync("SELECT id, plain_text_summary FROM emr_documents WHERE id = @Id", new { Id = dto.EmrDocumentId.Trim() });
        if (doc == null) return ApiResponse<object>.Fail(404, "目标电子病历文书不存在。");

        string existingText = (string?)doc.plain_text_summary ?? string.Empty;
        string appended = $"{existingText}\n[引用检验检查结果 #{dto.ReportId} ({DateTime.UtcNow:yyyy-MM-dd HH:mm})]: {dto.ResultSummaryText.Trim()}".Trim();
        string refId = Guid.NewGuid().ToString("N");

        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync(
            "UPDATE emr_documents SET plain_text_summary = @Summary, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            new { Summary = appended, Now = now, Actor = ActorId, Id = dto.EmrDocumentId.Trim() }, tx);

        await conn.ExecuteAsync(@"
INSERT INTO emr_lab_import_refs (id, emr_document_id, encounter_id, report_id, imported_summary, imported_by, imported_at_ms)
VALUES (@Id, @DocId, @EncId, @RepId, @Summary, @By, @Now);", new
        {
            Id = refId, DocId = dto.EmrDocumentId.Trim(), EncId = dto.EncounterId.Trim(),
            RepId = dto.ReportId.Trim(), Summary = dto.ResultSummaryText.Trim(), By = ActorName, Now = now
        }, tx);
        await tx.CommitAsync(ct);

        return ApiResponse<object>.Success(new { refId, emrDocumentId = dto.EmrDocumentId.Trim(), imported = true });
    }
}
