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
/// 医务管理中心 (Medical Affairs)：
/// 1) 医师分级动态授权与停权（实时同步回写 sys_users.prescribe_level / surgery_level）
/// 2) 医疗安全（不良）事件分级（Ⅰ~Ⅳ级）+ RCA 根因分析 + PDCA 持续改进闭环
/// 3) 医疗纠纷专案全周期管理（一键联动 mrms_case_records 依法封存病案）
/// 4) 新技术新项目临床准入审批与并发症监测
/// </summary>
[ApiController]
[Route("api/v4/medical-affairs")]
public sealed class MedicalAffairsController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _session;
    private readonly IAuditTrailContract? _auditTrail;

    public MedicalAffairsController(
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

    public sealed record GrantPrivilegeDto(string UserId, string DoctorName, string DeptName, int PrescribeLevel, int SurgeryLevel, bool NarcoticAuth, int ValidUntilInt);
    public sealed record SuspendPrivilegeDto(string Reason);
    public sealed record ReportSafetyEventDto(string SeverityLevel, string EventCategory, string DeptName, string EventSummary);
    public sealed record UpdateRcaDto(string RcaRootCause, string PdcaActionPlan, string Status);
    public sealed record CreateDisputeDto(string PatientName, string DeptName, string? MrmsCaseId, string DisputeSummary, decimal CompensationAmount);
    public sealed record CreateNewTechDto(string ProjectName, string DeptName, string LeadDoctorName, string RiskGrade, bool EthicsApproved);
    public sealed record ReviewNewTechDto(string Status, int PerformedCases, int ComplicationCases, string? ReviewComment);

    [HttpGet("dashboard")]
    public async Task<ApiResponse<object>> GetDashboard()
    {
        using var conn = _connFactory.CreateConnection();
        int activeGrants = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM med_privilege_grants WHERE status = 'ACTIVE'");
        int suspendedGrants = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM med_privilege_grants WHERE status = 'SUSPENDED'");
        int sentinelEvents = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM med_safety_rca_events WHERE severity_level IN ('LEVEL_I','LEVEL_II') AND status <> 'CLOSED'");
        int openDisputes = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM med_dispute_cases WHERE status <> 'CLOSED'");
        int approvedTechs = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM med_new_tech_projects WHERE status = 'APPROVED'");

        return ApiResponse<object>.Success(new
        {
            ActivePrivileges = activeGrants,
            SuspendedPrivileges = suspendedGrants,
            OpenSentinelEvents = sentinelEvents,
            OpenDisputes = openDisputes,
            ApprovedNewTechs = approvedTechs
        });
    }

    // ===================== 1. 医师动态分级授权与停权联动 =====================

    [HttpGet("privileges")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetPrivileges()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT id AS Id, grant_no AS GrantNo, user_id AS UserId, doctor_name AS DoctorName,
                   dept_name AS DeptName, prescribe_level AS PrescribeLevel, surgery_level AS SurgeryLevel,
                   narcotic_auth AS NarcoticAuth, valid_until_int AS ValidUntilInt,
                   status AS Status, suspend_reason AS SuspendReason, approved_by AS ApprovedBy
            FROM med_privilege_grants ORDER BY updated_at_ms DESC LIMIT 150")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("privileges")]
    public async Task<ApiResponse<object>> GrantPrivilege([FromBody] GrantPrivilegeDto req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.UserId) || string.IsNullOrWhiteSpace(req.DoctorName))
            return ApiResponse<object>.Fail(400, "医师账号ID与姓名不能为空");

        long now = SystemTime.UtcNowMs;
        string id = Guid.NewGuid().ToString("D");
        string no = "AUTH-" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss");
        int rxLvl = Math.Clamp(req.PrescribeLevel, 0, 3);
        int surgLvl = Math.Clamp(req.SurgeryLevel, 0, 4);

        using var conn = _connFactory.CreateConnection();
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(@"
            INSERT INTO med_privilege_grants
            (id, grant_no, user_id, doctor_name, dept_name, prescribe_level, surgery_level, narcotic_auth,
             valid_until_int, status, approved_by, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES
            (@Id, @No, @Uid, @Doc, @Dept, @Rx, @Surg, @Narc,
             @Until, 'ACTIVE', @ActorName, 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, No = no, Uid = req.UserId.Trim(), Doc = req.DoctorName.Trim(), Dept = req.DeptName.Trim(), Rx = rxLvl, Surg = surgLvl, Narc = req.NarcoticAuth ? 1 : 0, Until = req.ValidUntilInt > 20000101 ? req.ValidUntilInt : 20271231, ActorName, Now = now, Actor = ActorId }, tx);

        // 实时同步回写 sys_users 的真实临床权限等级，直接驱动处方与手术分级拦截
        await conn.ExecuteAsync(
            "UPDATE sys_users SET prescribe_level = @Rx, surgery_level = @Surg, updated_at_ms = @Now, updated_by = @Actor WHERE id = @Uid OR user_name = @Uid",
            new { Rx = rxLvl, Surg = surgLvl, Now = now, Actor = ActorId, Uid = req.UserId.Trim() }, tx);
        tx.Commit();

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("MED_PRIVILEGE_GRANT", "med_privilege_grants", id, $"医务科动态授权：{req.DoctorName}（处方{rxLvl}级/手术{surgLvl}级）已同步 sys_users", ActorId, ActorName, null, ct);

        return ApiResponse<object>.Success(new { Id = id, GrantNo = no }, $"已完成 {req.DoctorName} 分级授权并实时生效");
    }

    [HttpPut("privileges/{id}/suspend")]
    public async Task<ApiResponse<object>> SuspendPrivilege(string id, [FromBody] SuspendPrivilegeDto req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Reason))
            return ApiResponse<object>.Fail(400, "暂停授权原因不能为空");

        using var conn = _connFactory.CreateConnection();
        var grant = await conn.QueryFirstOrDefaultAsync<dynamic>("SELECT * FROM med_privilege_grants WHERE id = @Id", new { Id = id });
        if (grant == null) return ApiResponse<object>.Fail(404, "未找到授权记录");

        long now = SystemTime.UtcNowMs;
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(
            "UPDATE med_privilege_grants SET status = 'SUSPENDED', suspend_reason = @Reason, updated_at_ms = @Now, updated_by = @Actor WHERE id = @Id",
            new { Reason = req.Reason.Trim(), Now = now, Actor = ActorId, Id = id }, tx);
        // 停权实时将 sys_users 处方权与手术权清零阻断
        await conn.ExecuteAsync(
            "UPDATE sys_users SET prescribe_level = 0, surgery_level = 0, updated_at_ms = @Now, updated_by = @Actor WHERE id = @Uid OR user_name = @Uid",
            new { Now = now, Actor = ActorId, Uid = (string)grant.user_id }, tx);
        tx.Commit();

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("MED_PRIVILEGE_SUSPEND", "med_privilege_grants", id, $"医务科紧急停权：{(string)grant.doctor_name}，原因：{req.Reason}", ActorId, ActorName, null, ct);

        return ApiResponse<object>.Success(new { Id = id, Status = "SUSPENDED" }, $"已暂停 {(string)grant.doctor_name} 的临床处方与手术权限并实时锁单");
    }

    // ===================== 2. 医疗安全（不良）事件分级 + RCA / PDCA =====================

    [HttpGet("safety-events")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetSafetyEvents()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT id AS Id, event_no AS EventNo, severity_level AS SeverityLevel, event_category AS EventCategory,
                   dept_name AS DeptName, event_summary AS EventSummary, rca_root_cause AS RcaRootCause,
                   pdca_action_plan AS PdcaActionPlan, status AS Status, reporter_name AS ReporterName, reviewer_name AS ReviewerName
            FROM med_safety_rca_events ORDER BY created_at_ms DESC LIMIT 150")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("safety-events")]
    public async Task<ApiResponse<object>> ReportSafetyEvent([FromBody] ReportSafetyEventDto req)
    {
        if (string.IsNullOrWhiteSpace(req.EventSummary)) return ApiResponse<object>.Fail(400, "事件经过摘要不能为空");
        long now = SystemTime.UtcNowMs;
        string id = Guid.NewGuid().ToString("D");
        string no = "SAFE-" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss");

        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO med_safety_rca_events
            (id, event_no, severity_level, event_category, dept_name, event_summary, status, reporter_name,
             schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES
            (@Id, @No, @Sev, @Cat, @Dept, @Sum, 'REPORTED', @ActorName,
             1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, No = no, Sev = req.SeverityLevel, Cat = req.EventCategory, Dept = req.DeptName, Sum = req.EventSummary.Trim(), ActorName, Now = now, Actor = ActorId });

        return ApiResponse<object>.Success(new { Id = id, EventNo = no }, $"医疗安全事件 {no} 已非惩罚性上报，待医务科开展 RCA 分析");
    }

    [HttpPut("safety-events/{id}/rca")]
    public async Task<ApiResponse<object>> UpdateRcaAndPdca(string id, [FromBody] UpdateRcaDto req)
    {
        long now = SystemTime.UtcNowMs;
        string st = string.IsNullOrWhiteSpace(req.Status) ? "PDCA_TRACKING" : req.Status.Trim().ToUpperInvariant();
        long? closedAt = st == "CLOSED" ? now : null;
        using var conn = _connFactory.CreateConnection();
        int n = await conn.ExecuteAsync(@"
            UPDATE med_safety_rca_events
            SET rca_root_cause = @Rca, pdca_action_plan = @Pdca, status = @St, reviewer_name = @Rev,
                closed_at_ms = COALESCE(@Closed, closed_at_ms), updated_at_ms = @Now, updated_by = @Actor
            WHERE id = @Id",
            new { Rca = req.RcaRootCause?.Trim(), Pdca = req.PdcaActionPlan?.Trim(), St = st, Rev = ActorName, Closed = closedAt, Now = now, Actor = ActorId, Id = id });
        if (n == 0) return ApiResponse<object>.Fail(404, "未找到该安全事件");
        return ApiResponse<object>.Success(new { Id = id, Status = st }, "已更新 RCA 根因分析与 PDCA 持续改进措施");
    }

    // ===================== 3. 医疗纠纷专案 + 一键联动 MRMS 封存病案 =====================

    [HttpGet("disputes")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetDisputes()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT id AS Id, dispute_no AS DisputeNo, patient_name AS PatientName, dept_name AS DeptName,
                   mrms_case_id AS MrmsCaseId, mrms_sealed AS MrmsSealed, dispute_summary AS DisputeSummary,
                   resolution_type AS ResolutionType, compensation_amount AS CompensationAmount,
                   status AS Status, handler_name AS HandlerName
            FROM med_dispute_cases ORDER BY created_at_ms DESC LIMIT 120")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("disputes")]
    public async Task<ApiResponse<object>> CreateDispute([FromBody] CreateDisputeDto req)
    {
        if (string.IsNullOrWhiteSpace(req.PatientName) || string.IsNullOrWhiteSpace(req.DisputeSummary))
            return ApiResponse<object>.Fail(400, "患者姓名与纠纷摘要不能为空");
        long now = SystemTime.UtcNowMs;
        string id = Guid.NewGuid().ToString("D");
        string no = "DISP-" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss");

        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO med_dispute_cases
            (id, dispute_no, patient_name, dept_name, mrms_case_id, mrms_sealed, dispute_summary,
             compensation_amount, status, handler_name, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES
            (@Id, @No, @Pat, @Dept, @Cid, 0, @Sum,
             @Comp, 'OPEN', @ActorName, 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, No = no, Pat = req.PatientName.Trim(), Dept = req.DeptName.Trim(), Cid = req.MrmsCaseId?.Trim(), Sum = req.DisputeSummary.Trim(), Comp = Math.Max(0m, req.CompensationAmount), ActorName, Now = now, Actor = ActorId });

        return ApiResponse<object>.Success(new { Id = id, DisputeNo = no }, $"医疗纠纷专案 {no} 已立案");
    }

    [HttpPost("disputes/{id}/seal-mrms")]
    public async Task<ApiResponse<object>> SealDisputeMrmsCase(string id)
    {
        using var conn = _connFactory.CreateConnection();
        var disp = await conn.QueryFirstOrDefaultAsync<dynamic>("SELECT * FROM med_dispute_cases WHERE id = @Id", new { Id = id });
        if (disp == null) return ApiResponse<object>.Fail(404, "纠纷专案不存在");

        string? caseId = (string?)disp.mrms_case_id;
        if (string.IsNullOrWhiteSpace(caseId))
        {
            caseId = await conn.ExecuteScalarAsync<string?>(
                "SELECT id FROM mrms_case_records WHERE patient_name = @Pat ORDER BY updated_at_ms DESC LIMIT 1",
                new { Pat = (string)disp.patient_name });
        }
        if (string.IsNullOrWhiteSpace(caseId))
            return ApiResponse<object>.Fail(404, "未在病案室找到该患者关联病案，请先归档或指定病案ID");

        long now = SystemTime.UtcNowMs;
        int dueDate = int.Parse(SystemTime.ShanghaiNow.AddYears(10).ToString("yyyyMMdd"));
        var caseRow = await conn.QueryFirstAsync<dynamic>("SELECT case_no AS CaseNo, patient_name AS Pat FROM mrms_case_records WHERE id = @Id", new { Id = caseId });

        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync("UPDATE mrms_case_records SET status = 'SEALED', updated_at_ms = @Now, updated_by = @Actor WHERE id = @Id", new { Now = now, Actor = ActorId, Id = caseId }, tx);
        await conn.ExecuteAsync(@"
            INSERT INTO mrms_borrow_logs
            (id, case_id, case_no, patient_name, action_type, applicant_name, purpose, due_date_int, status, handled_by, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @Cid, @No, @Pat, 'SEAL', '医务科', @Purp, @Due, 'ACTIVE', @ActorName, 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = Guid.NewGuid().ToString("D"), Cid = caseId, No = (string)caseRow.CaseNo, Pat = (string)caseRow.Pat, Purp = $"医疗纠纷专案 {(string)disp.dispute_no} 依法封存", Due = dueDate, ActorName, Now = now, Actor = ActorId }, tx);
        await conn.ExecuteAsync("UPDATE med_dispute_cases SET mrms_case_id = @Cid, mrms_sealed = 1, updated_at_ms = @Now, updated_by = @Actor WHERE id = @Id", new { Cid = caseId, Now = now, Actor = ActorId, Id = id }, tx);
        tx.Commit();

        return ApiResponse<object>.Success(new { DisputeId = id, MrmsCaseId = caseId, Status = "SEALED" }, $"已联动病案室依法封存病案 [{(string)caseRow.CaseNo}]");
    }

    // ===================== 4. 新技术新项目准入与随访 =====================

    [HttpGet("new-tech")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetNewTechProjects()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT id AS Id, project_no AS ProjectNo, project_name AS ProjectName, dept_name AS DeptName,
                   lead_doctor_name AS LeadDoctorName, risk_grade AS RiskGrade, ethics_approved AS EthicsApproved,
                   status AS Status, performed_cases AS PerformedCases, complication_cases AS ComplicationCases,
                   review_comment AS ReviewComment
            FROM med_new_tech_projects ORDER BY created_at_ms DESC LIMIT 120")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("new-tech")]
    public async Task<ApiResponse<object>> CreateNewTech([FromBody] CreateNewTechDto req)
    {
        if (string.IsNullOrWhiteSpace(req.ProjectName) || string.IsNullOrWhiteSpace(req.LeadDoctorName))
            return ApiResponse<object>.Fail(400, "项目名称与负责人不能为空");
        long now = SystemTime.UtcNowMs;
        string id = Guid.NewGuid().ToString("D");
        string no = "TECH-" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss");

        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO med_new_tech_projects
            (id, project_no, project_name, dept_name, lead_doctor_name, risk_grade, ethics_approved, status,
             schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @No, @Name, @Dept, @Lead, @Risk, @Eth, 'SUBMITTED', 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, No = no, Name = req.ProjectName.Trim(), Dept = req.DeptName.Trim(), Lead = req.LeadDoctorName.Trim(), Risk = req.RiskGrade, Eth = req.EthicsApproved ? 1 : 0, Now = now, Actor = ActorId });
        return ApiResponse<object>.Success(new { Id = id, ProjectNo = no }, $"新技术新项目 {no} 已提交准入审批");
    }

    [HttpPut("new-tech/{id}/review")]
    public async Task<ApiResponse<object>> ReviewNewTech(string id, [FromBody] ReviewNewTechDto req)
    {
        using var conn = _connFactory.CreateConnection();
        int n = await conn.ExecuteAsync(@"
            UPDATE med_new_tech_projects
            SET status = @St, performed_cases = @Perf, complication_cases = @Comp, review_comment = @Cmt,
                updated_at_ms = @Now, updated_by = @Actor
            WHERE id = @Id",
            new { St = req.Status.Trim().ToUpperInvariant(), Perf = req.PerformedCases, Comp = req.ComplicationCases, Cmt = req.ReviewComment?.Trim(), Now = SystemTime.UtcNowMs, Actor = ActorId, Id = id });
        if (n == 0) return ApiResponse<object>.Fail(404, "未找到该项目");
        return ApiResponse<object>.Success(new { Id = id, req.Status }, "已更新新技术准入状态与随访数据");
    }
}
