using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.BuildingBlocks.Security;
using HIS.Modules.Operations.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Operations.Controllers;

[ApiController]
[RequireHisRole("ADMIN", "WARD_NURSE", "IPD_ATTENDING", "IPD_RESIDENT")]
[Route("api/v4/ipc")]
[RequireRole(RoleCode.Admin, RoleCode.DoctorOpd, RoleCode.DoctorIpd, RoleCode.NurseIpd, RoleCode.Pharmacist, "IPC", "INFECTION_CONTROL")]
public class InfectionControlController : ControllerBase
{
    private readonly IRepository<IpcInfectionReport> _infRepo;
    private readonly IRepository<IpcIsolationOrder> _isoRepo;
    private readonly IRepository<IpcOccupationalExposure> _expRepo;
    private readonly IRepository<IpcAdverseEvent> _advRepo;
    private readonly IRepository<IpcHandHygieneAudit> _handRepo;
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _session;
    private readonly IAuditTrailContract? _auditTrail;

    public InfectionControlController(
        IRepository<IpcInfectionReport> infRepo,
        IRepository<IpcIsolationOrder> isoRepo,
        IRepository<IpcOccupationalExposure> expRepo,
        IRepository<IpcAdverseEvent> advRepo,
        IRepository<IpcHandHygieneAudit> handRepo,
        IDbConnectionFactory connFactory,
        IUserSession? session = null,
        IAuditTrailContract? auditTrail = null)
    {
        _infRepo = infRepo;
        _isoRepo = isoRepo;
        _expRepo = expRepo;
        _advRepo = advRepo;
        _handRepo = handRepo;
        _connFactory = connFactory;
        _session = session;
        _auditTrail = auditTrail;
    }

    private string CurrentUserId => _session?.UserId ?? "USER-IPC-01";

    // 1. 院内感染病例报告 (Infection Reports)
    [HttpPost("infection-reports")]
    public async Task<ApiResponse<IpcInfectionReport>> CreateInfectionReport([FromBody] CreateInfectionReportRequest req, CancellationToken ct = default)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.EncounterId) || string.IsNullOrWhiteSpace(req.InfectionSite))
            return ApiResponse<IpcInfectionReport>.Fail(400, "住院号与感染发生部位不能为空");

        string reportNo = "HAI" + DateTime.Now.ToString("yyMMddHHmmssfff") + Random.Shared.Next(10, 99);

        var report = new IpcInfectionReport
        {
            ReportNo = reportNo,
            EncounterId = req.EncounterId,
            PatientId = req.PatientId,
            PatientName = req.PatientName,
            WardId = req.WardId,
            InfectionSite = req.InfectionSite,
            Pathogen = req.Pathogen,
            IsMdro = (short)(req.IsMdro ? 1 : 0),
            DiagnosedAtMs = SystemTime.UtcNowMs,
            ClinicalManifestation = req.ClinicalManifestation,
            Status = 0,
            ReporterId = CurrentUserId,
            CreatedBy = CurrentUserId,
            UpdatedBy = CurrentUserId
        };

        await _infRepo.InsertAsync(report, ct);
        await RecordAuditAsync("IPC_INFECTION_REPORT", nameof(IpcInfectionReport), report.Id,
            $"上报院内感染病例 [{reportNo}]：患者={req.PatientName}，部位={req.InfectionSite}，病原体={req.Pathogen}，MDRO={req.IsMdro}", ct);

        return ApiResponse<IpcInfectionReport>.Success(report, "院内感染报告卡已提交并送院感科审核");
    }

    [HttpGet("infection-reports")]
    public async Task<ApiResponse<IReadOnlyList<IpcInfectionReport>>> GetInfectionReports(
        [FromQuery] string? encounterId = null,
        [FromQuery] string? wardId = null,
        [FromQuery] int? status = null,
        [FromQuery] bool? isMdro = null)
    {
        short? mdroVal = isMdro.HasValue ? (short)(isMdro.Value ? 1 : 0) : null;
        var list = await _infRepo.QueryAsync(@"
SELECT * FROM ipc_infection_reports
WHERE (@EncounterId IS NULL OR encounter_id = @EncounterId)
  AND (@WardId IS NULL OR ward_id = @WardId)
  AND (@Status IS NULL OR status = @Status)
  AND (@IsMdro IS NULL OR is_mdro = @IsMdro)
ORDER BY diagnosed_at_ms DESC", new { EncounterId = encounterId, WardId = wardId, Status = status, IsMdro = mdroVal });

        return ApiResponse<IReadOnlyList<IpcInfectionReport>>.Success(list);
    }

    [HttpPut("infection-reports/{id}/status")]
    public async Task<ApiResponse<IpcInfectionReport>> UpdateInfectionReportStatus(string id, [FromBody] UpdateInfectionReportStatusRequest req, CancellationToken ct = default)
    {
        var report = await _infRepo.GetByIdAsync(id, ct);
        if (report == null)
            return ApiResponse<IpcInfectionReport>.Fail(404, "院感报告卡不存在");

        report.Status = req.Status;
        if (!string.IsNullOrWhiteSpace(req.ReviewerId)) report.ReviewerId = req.ReviewerId;
        else report.ReviewerId = CurrentUserId;

        report.UpdatedBy = CurrentUserId;
        await _infRepo.UpdateAsync(report, ct);
        await RecordAuditAsync("IPC_INFECTION_STATUS", nameof(IpcInfectionReport), report.Id,
            $"院感报告 [{report.ReportNo}] 状态更新为 {req.Status}，审核员={report.ReviewerId}", ct);

        return ApiResponse<IpcInfectionReport>.Success(report, "院感报告卡状态已更新");
    }

    // 2. 隔离医嘱 (Isolation Orders)
    [HttpPost("isolation-orders")]
    public async Task<ApiResponse<IpcIsolationOrder>> CreateIsolationOrder([FromBody] CreateIsolationOrderRequest req, CancellationToken ct = default)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.EncounterId) || string.IsNullOrWhiteSpace(req.Reason))
            return ApiResponse<IpcIsolationOrder>.Fail(400, "住院号与隔离原因不能为空");

        string orderNo = "ISO" + DateTime.Now.ToString("yyMMddHHmmssfff") + Random.Shared.Next(10, 99);

        var order = new IpcIsolationOrder
        {
            OrderNo = orderNo,
            EncounterId = req.EncounterId,
            PatientId = req.PatientId,
            WardId = req.WardId,
            IsolationType = req.IsolationType ?? "CONTACT",
            Reason = req.Reason,
            StartMs = SystemTime.UtcNowMs,
            Status = 0,
            PrescribedBy = CurrentUserId,
            CreatedBy = CurrentUserId,
            UpdatedBy = CurrentUserId
        };

        await _isoRepo.InsertAsync(order, ct);
        await RecordAuditAsync("IPC_ISOLATION_ORDER", nameof(IpcIsolationOrder), order.Id,
            $"开立隔离医嘱 [{orderNo}]：隔离类型={order.IsolationType}，隔离原因={order.Reason}", ct);

        return ApiResponse<IpcIsolationOrder>.Success(order, $"隔离医嘱 [{order.IsolationType}] 已开立并下发病区护士站执行床边隔离防护");
    }

    [HttpGet("isolation-orders")]
    public async Task<ApiResponse<IReadOnlyList<IpcIsolationOrder>>> GetIsolationOrders(
        [FromQuery] string? encounterId = null,
        [FromQuery] string? wardId = null,
        [FromQuery] int? status = null)
    {
        var list = await _isoRepo.QueryAsync(@"
SELECT * FROM ipc_isolation_orders
WHERE (@EncounterId IS NULL OR encounter_id = @EncounterId)
  AND (@WardId IS NULL OR ward_id = @WardId)
  AND (@Status IS NULL OR status = @Status)
ORDER BY start_ms DESC", new { EncounterId = encounterId, WardId = wardId, Status = status });

        return ApiResponse<IReadOnlyList<IpcIsolationOrder>>.Success(list);
    }

    [HttpPost("isolation-orders/{id}/end")]
    public async Task<ApiResponse<IpcIsolationOrder>> EndIsolationOrder(string id, [FromBody] EndIsolationOrderRequest req, CancellationToken ct = default)
    {
        var order = await _isoRepo.GetByIdAsync(id, ct);
        if (order == null)
            return ApiResponse<IpcIsolationOrder>.Fail(404, "隔离医嘱不存在");

        if (order.Status == 1)
            return ApiResponse<IpcIsolationOrder>.Success(order, "该隔离医嘱已解除（幂等返回）");

        order.Status = 1;
        order.EndMs = SystemTime.UtcNowMs;
        order.EndReason = req?.EndReason ?? "连续3次病原学培养阴性或病情好转解除隔离";
        order.EndedBy = CurrentUserId;
        order.UpdatedBy = CurrentUserId;

        await _isoRepo.UpdateAsync(order, ct);
        await RecordAuditAsync("IPC_ISOLATION_END", nameof(IpcIsolationOrder), order.Id,
            $"解除隔离医嘱 [{order.OrderNo}]，解除原因：{order.EndReason}", ct);

        return ApiResponse<IpcIsolationOrder>.Success(order, "隔离医嘱已顺利解除并归档");
    }

    // 3. 医务人员职业暴露 (Occupational Exposures)
    [HttpPost("occupational-exposures")]
    public async Task<ApiResponse<IpcOccupationalExposure>> CreateOccupationalExposure([FromBody] CreateOccupationalExposureRequest req, CancellationToken ct = default)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.StaffId) || string.IsNullOrWhiteSpace(req.ExposureType))
            return ApiResponse<IpcOccupationalExposure>.Fail(400, "暴露人员工号与暴露类型不能为空");

        string reportNo = "EXP" + DateTime.Now.ToString("yyMMddHHmmssfff") + Random.Shared.Next(10, 99);

        var exp = new IpcOccupationalExposure
        {
            ReportNo = reportNo,
            StaffId = req.StaffId,
            StaffName = req.StaffName,
            DeptId = req.DeptId,
            ExposureType = req.ExposureType,
            SourcePatientId = req.SourcePatientId,
            SourcePathogen = req.SourcePathogen ?? "UNKNOWN",
            ExposedAtMs = SystemTime.UtcNowMs,
            EmergencyDisposition = req.EmergencyDisposition ?? "肥皂水冲洗伤口，流动水反复冲洗，挤压血液并用0.5%碘伏消毒",
            PepTreatment = req.PepTreatment,
            Status = 0,
            ReporterId = CurrentUserId,
            CreatedBy = CurrentUserId,
            UpdatedBy = CurrentUserId
        };

        await _expRepo.InsertAsync(exp, ct);
        await RecordAuditAsync("IPC_EXPOSURE_REPORT", nameof(IpcOccupationalExposure), exp.Id,
            $"登记医务人员职业暴露 [{reportNo}]：人员={req.StaffName}，类型={req.ExposureType}，源病原体={exp.SourcePathogen}", ct);

        return ApiResponse<IpcOccupationalExposure>.Success(exp, "职业暴露已登记，请遵医嘱落实阻断用药及追踪复查！");
    }

    [HttpGet("occupational-exposures")]
    public async Task<ApiResponse<IReadOnlyList<IpcOccupationalExposure>>> GetOccupationalExposures(
        [FromQuery] string? staffId = null,
        [FromQuery] int? status = null)
    {
        var list = await _expRepo.QueryAsync(@"
SELECT * FROM ipc_occupational_exposures
WHERE (@StaffId IS NULL OR staff_id = @StaffId)
  AND (@Status IS NULL OR status = @Status)
ORDER BY exposed_at_ms DESC", new { StaffId = staffId, Status = status });

        return ApiResponse<IReadOnlyList<IpcOccupationalExposure>>.Success(list);
    }

    [HttpPut("occupational-exposures/{id}/disposition")]
    public async Task<ApiResponse<IpcOccupationalExposure>> UpdateExposureDisposition(string id, [FromBody] UpdateExposureDispositionRequest req, CancellationToken ct = default)
    {
        var exp = await _expRepo.GetByIdAsync(id, ct);
        if (exp == null)
            return ApiResponse<IpcOccupationalExposure>.Fail(404, "职业暴露记录不存在");

        exp.Status = req.Status;
        if (!string.IsNullOrWhiteSpace(req.EmergencyDisposition)) exp.EmergencyDisposition = req.EmergencyDisposition;
        if (!string.IsNullOrWhiteSpace(req.PepTreatment)) exp.PepTreatment = req.PepTreatment;
        exp.UpdatedBy = CurrentUserId;

        await _expRepo.UpdateAsync(exp, ct);
        await RecordAuditAsync("IPC_EXPOSURE_UPDATE", nameof(IpcOccupationalExposure), exp.Id,
            $"更新职业暴露 [{exp.ReportNo}] 处置进展：状态={exp.Status}", ct);

        return ApiResponse<IpcOccupationalExposure>.Success(exp, "职业暴露处置记录已更新");
    }

    // 4. 医疗安全不良事件 (Adverse Events)
    [HttpPost("adverse-events")]
    public async Task<ApiResponse<IpcAdverseEvent>> CreateAdverseEvent([FromBody] CreateAdverseEventRequest req, CancellationToken ct = default)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.Description) || string.IsNullOrWhiteSpace(req.EventCategory))
            return ApiResponse<IpcAdverseEvent>.Fail(400, "事件类别与事件详细经过描述不能为空");

        string eventNo = "ADV" + DateTime.Now.ToString("yyMMddHHmmssfff") + Random.Shared.Next(10, 99);

        var ev = new IpcAdverseEvent
        {
            EventNo = eventNo,
            ReporterId = CurrentUserId,
            ReporterName = _session?.DisplayName ?? "医护人员",
            DeptId = req.DeptId,
            EncounterId = req.EncounterId,
            PatientId = req.PatientId,
            EventCategory = req.EventCategory,
            SeverityLevel = req.SeverityLevel ?? "IV",
            Description = req.Description,
            ImmediateAction = req.ImmediateAction,
            Status = 0,
            CreatedBy = CurrentUserId,
            UpdatedBy = CurrentUserId
        };

        await _advRepo.InsertAsync(ev, ct);
        await RecordAuditAsync("IPC_ADVERSE_EVENT", nameof(IpcAdverseEvent), ev.Id,
            $"上报不良事件 [{eventNo}]：级别={ev.SeverityLevel}，类别={ev.EventCategory}，简述={ev.Description[..Math.Min(ev.Description.Length, 40)]}", ct);

        return ApiResponse<IpcAdverseEvent>.Success(ev, "医疗安全不良事件已提交，遵循非惩罚性原则进入闭环调查");
    }

    [HttpGet("adverse-events")]
    public async Task<ApiResponse<IReadOnlyList<IpcAdverseEvent>>> GetAdverseEvents(
        [FromQuery] string? deptId = null,
        [FromQuery] string? category = null,
        [FromQuery] string? severity = null,
        [FromQuery] int? status = null)
    {
        var list = await _advRepo.QueryAsync(@"
SELECT * FROM ipc_adverse_events
WHERE (@DeptId IS NULL OR dept_id = @DeptId)
  AND (@Category IS NULL OR event_category = @Category)
  AND (@Severity IS NULL OR severity_level = @Severity)
  AND (@Status IS NULL OR status = @Status)
ORDER BY created_at_ms DESC", new { DeptId = deptId, Category = category, Severity = severity, Status = status });

        return ApiResponse<IReadOnlyList<IpcAdverseEvent>>.Success(list);
    }

    [HttpPut("adverse-events/{id}/close")]
    public async Task<ApiResponse<IpcAdverseEvent>> CloseAdverseEvent(string id, [FromBody] CloseAdverseEventRequest req, CancellationToken ct = default)
    {
        var ev = await _advRepo.GetByIdAsync(id, ct);
        if (ev == null)
            return ApiResponse<IpcAdverseEvent>.Fail(404, "不良事件记录不存在");

        ev.Status = 3; // CLOSED
        ev.ClosureComment = req?.ClosureComment ?? "完成根因分析 (RCA) 并落实改进措施，予以结案";
        ev.UpdatedBy = CurrentUserId;

        await _advRepo.UpdateAsync(ev, ct);
        await RecordAuditAsync("IPC_ADVERSE_CLOSE", nameof(IpcAdverseEvent), ev.Id,
            $"不良事件 [{ev.EventNo}] 调查结案：{ev.ClosureComment}", ct);

        return ApiResponse<IpcAdverseEvent>.Success(ev, "不良事件已完成根因分析与纠正措施归档结案");
    }

    // 5. 手卫生督查 (Hand Hygiene Audits)
    [HttpPost("hand-hygiene-audits")]
    public async Task<ApiResponse<IpcHandHygieneAudit>> CreateHandHygieneAudit([FromBody] CreateHandHygieneAuditRequest req, CancellationToken ct = default)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.WardId) || string.IsNullOrWhiteSpace(req.AuditDate))
            return ApiResponse<IpcHandHygieneAudit>.Fail(400, "督查病区与日期不能为空");

        int opp = Math.Max(1, req.OpportunityCount);
        int perf = Math.Clamp(req.PerformedCount, 0, opp);
        int corr = Math.Clamp(req.CorrectCount, 0, perf);

        decimal compliance = Math.Round((decimal)perf / opp * 100m, 2);
        decimal accuracy = perf > 0 ? Math.Round((decimal)corr / perf * 100m, 2) : 0m;

        var audit = new IpcHandHygieneAudit
        {
            WardId = req.WardId,
            ObserverId = CurrentUserId,
            AuditDate = req.AuditDate,
            MomentType = req.MomentType ?? "TOUCH_PATIENT",
            StaffCategory = req.StaffCategory ?? "NURSE",
            OpportunityCount = opp,
            PerformedCount = perf,
            CorrectCount = corr,
            ComplianceRate = compliance,
            AccuracyRate = accuracy,
            CreatedBy = CurrentUserId,
            UpdatedBy = CurrentUserId
        };

        await _handRepo.InsertAsync(audit, ct);
        await RecordAuditAsync("IPC_HAND_HYGIENE", nameof(IpcHandHygieneAudit), audit.Id,
            $"记录手卫生督查：病区={req.WardId}，依从率={compliance}%，正确率={accuracy}%", ct);

        return ApiResponse<IpcHandHygieneAudit>.Success(audit, $"手卫生督查记录已保存：依从率 {compliance}%，正确率 {accuracy}%");
    }

    [HttpGet("hand-hygiene-audits")]
    public async Task<ApiResponse<IReadOnlyList<IpcHandHygieneAudit>>> GetHandHygieneAudits(
        [FromQuery] string? wardId = null,
        [FromQuery] string? auditDate = null)
    {
        var list = await _handRepo.QueryAsync(@"
SELECT * FROM ipc_hand_hygiene_audits
WHERE (@WardId IS NULL OR ward_id = @WardId)
  AND (@AuditDate IS NULL OR audit_date = @AuditDate)
ORDER BY created_at_ms DESC", new { WardId = wardId, AuditDate = auditDate });

        return ApiResponse<IReadOnlyList<IpcHandHygieneAudit>>.Success(list);
    }

    // 6. 院感监控态势仪表板 (IPC Summary)
    [HttpGet("summary")]
    public async Task<ApiResponse<IpcSummaryDto>> GetSummary()
    {
        using var conn = _connFactory.CreateConnection();
        int activeIso = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ipc_isolation_orders WHERE status = 0");
        int totalInf = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ipc_infection_reports");
        int mdroCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ipc_infection_reports WHERE is_mdro = 1");
        int openAdverse = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ipc_adverse_events WHERE status < 3");
        int exposures = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ipc_occupational_exposures");

        decimal avgCompliance = await conn.ExecuteScalarAsync<decimal>(
            "SELECT COALESCE(AVG(compliance_rate), 0) FROM ipc_hand_hygiene_audits");

        var summary = new IpcSummaryDto(
            ActiveIsolationCount: activeIso,
            TotalInfectionReports: totalInf,
            MdroInfectionCount: mdroCount,
            OpenAdverseEventsCount: openAdverse,
            OccupationalExposuresCount: exposures,
            AverageHandHygieneComplianceRate: Math.Round(avgCompliance, 2)
        );

        return ApiResponse<IpcSummaryDto>.Success(summary);
    }

    private async Task RecordAuditAsync(string eventType, string entityName, string entityId, string detail, CancellationToken ct = default)
    {
        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync(eventType, entityName, entityId, detail,
                CurrentUserId, _session?.DisplayName, _session?.ActiveDeptId, ct);
    }
}
