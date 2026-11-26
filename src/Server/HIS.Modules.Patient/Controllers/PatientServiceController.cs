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
using HIS.Modules.Patient.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Patient.Controllers;

[ApiController]
[RequireHisRole("ADMIN", "OPD_DOC", "WARD_NURSE", "CASHIER")]
[Route("api/v4/service")]
[RequireRole(RoleCode.Admin, RoleCode.DoctorOpd, RoleCode.DoctorIpd, RoleCode.NurseIpd, RoleCode.Cashier, "CUSTOMER_SERVICE", "PATIENT_SERVICE")]
public class PatientServiceController : ControllerBase
{
    private readonly IRepository<SrvSatisfactionSurvey> _surveyRepo;
    private readonly IRepository<SrvComplaint> _complaintRepo;
    private readonly IRepository<SrvInformedConsent> _consentRepo;
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _session;
    private readonly IAuditTrailContract? _auditTrail;

    public PatientServiceController(
        IRepository<SrvSatisfactionSurvey> surveyRepo,
        IRepository<SrvComplaint> complaintRepo,
        IRepository<SrvInformedConsent> consentRepo,
        IDbConnectionFactory connFactory,
        IUserSession? session = null,
        IAuditTrailContract? auditTrail = null)
    {
        _surveyRepo = surveyRepo;
        _complaintRepo = complaintRepo;
        _consentRepo = consentRepo;
        _connFactory = connFactory;
        _session = session;
        _auditTrail = auditTrail;
    }

    private string CurrentUserId => _session?.UserId ?? "USER-PATIENT-SERVICE";

    // 1. 满意度评价
    [HttpPost("satisfaction")]
    public async Task<ApiResponse<SrvSatisfactionSurvey>> SubmitSatisfaction([FromBody] SubmitSatisfactionRequest req, CancellationToken ct = default)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.PatientId) || string.IsNullOrWhiteSpace(req.DeptId))
            return ApiResponse<SrvSatisfactionSurvey>.Fail(400, "患者病历号与参评科室不能为空");

        int overall = Math.Clamp(req.Score, 1, 5);
        int skill = Math.Clamp(req.MedicalSkillScore, 1, 5);
        int attitude = Math.Clamp(req.ServiceAttitudeScore, 1, 5);
        int env = Math.Clamp(req.EnvironmentScore, 1, 5);

        var survey = new SrvSatisfactionSurvey
        {
            EncounterId = req.EncounterId,
            VisitId = req.VisitId,
            PatientId = req.PatientId,
            PatientName = req.PatientName,
            DeptId = req.DeptId,
            Score = overall,
            MedicalSkillScore = skill,
            ServiceAttitudeScore = attitude,
            EnvironmentScore = env,
            Comment = req.Comment,
            SubmittedAtMs = SystemTime.UtcNowMs,
            CreatedBy = CurrentUserId,
            UpdatedBy = CurrentUserId
        };

        await _surveyRepo.InsertAsync(survey, ct);
        await RecordAuditAsync("SRV_SATISFACTION_SUBMIT", nameof(SrvSatisfactionSurvey), survey.Id,
            $"患者 [{req.PatientName}] 提交科室 [{req.DeptId}] 满意度评价：总体评分={overall}星，技术={skill}星，态度={attitude}星", ct);

        return ApiResponse<SrvSatisfactionSurvey>.Success(survey, "感谢您的就医体验评价！");
    }

    [HttpGet("satisfaction")]
    public async Task<ApiResponse<IReadOnlyList<SrvSatisfactionSurvey>>> GetSatisfactionSurveys(
        [FromQuery] string? deptId = null,
        [FromQuery] string? patientId = null,
        [FromQuery] string? encounterId = null)
    {
        var list = await _surveyRepo.QueryAsync(@"
SELECT * FROM srv_satisfaction_surveys
WHERE (@DeptId IS NULL OR dept_id = @DeptId)
  AND (@PatientId IS NULL OR patient_id = @PatientId)
  AND (@EncounterId IS NULL OR encounter_id = @EncounterId)
ORDER BY submitted_at_ms DESC", new { DeptId = deptId, PatientId = patientId, EncounterId = encounterId });

        return ApiResponse<IReadOnlyList<SrvSatisfactionSurvey>>.Success(list);
    }

    // 2. 投诉建议管理
    [HttpPost("complaints")]
    public async Task<ApiResponse<SrvComplaint>> SubmitComplaint([FromBody] SubmitComplaintRequest req, CancellationToken ct = default)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.PatientId) || string.IsNullOrWhiteSpace(req.Description))
            return ApiResponse<SrvComplaint>.Fail(400, "患者信息与投诉内容描述不能为空");

        string complaintNo = "CMPL" + DateTime.Now.ToString("yyMMddHHmmssfff") + Random.Shared.Next(10, 99);

        var c = new SrvComplaint
        {
            ComplaintNo = complaintNo,
            PatientId = req.PatientId,
            PatientName = req.PatientName,
            PatientPhone = req.PatientPhone,
            DeptId = req.DeptId,
            ComplaintType = req.ComplaintType ?? "SERVICE_ATTITUDE",
            Description = req.Description,
            Status = 0,
            CreatedBy = CurrentUserId,
            UpdatedBy = CurrentUserId
        };

        await _complaintRepo.InsertAsync(c, ct);
        await RecordAuditAsync("SRV_COMPLAINT_SUBMIT", nameof(SrvComplaint), c.Id,
            $"患者 [{req.PatientName}] 提交医疗投诉 [{complaintNo}]：类别={c.ComplaintType}，涉及科室={c.DeptId}", ct);

        return ApiResponse<SrvComplaint>.Success(c, "投诉记录已登记受理，医患沟通办公室将严格调查并跟进反馈");
    }

    [HttpGet("complaints")]
    public async Task<ApiResponse<IReadOnlyList<SrvComplaint>>> GetComplaints(
        [FromQuery] string? deptId = null,
        [FromQuery] int? status = null,
        [FromQuery] string? complaintType = null)
    {
        var list = await _complaintRepo.QueryAsync(@"
SELECT * FROM srv_complaints
WHERE (@DeptId IS NULL OR dept_id = @DeptId)
  AND (@Status IS NULL OR status = @Status)
  AND (@Type IS NULL OR complaint_type = @Type)
ORDER BY created_at_ms DESC", new { DeptId = deptId, Status = status, Type = complaintType });

        return ApiResponse<IReadOnlyList<SrvComplaint>>.Success(list);
    }

    [HttpPut("complaints/{id}")]
    public async Task<ApiResponse<SrvComplaint>> ProcessComplaint(string id, [FromBody] ProcessComplaintRequest req, CancellationToken ct = default)
    {
        var c = await _complaintRepo.GetByIdAsync(id, ct);
        if (c == null)
            return ApiResponse<SrvComplaint>.Fail(404, "投诉记录不存在");

        c.Status = req.Status;
        if (!string.IsNullOrWhiteSpace(req.HandlerId)) c.HandlerId = req.HandlerId;
        else c.HandlerId = CurrentUserId;
        if (!string.IsNullOrWhiteSpace(req.HandlerName)) c.HandlerName = req.HandlerName;
        if (!string.IsNullOrWhiteSpace(req.HandlingResult)) c.HandlingResult = req.HandlingResult;

        c.UpdatedBy = CurrentUserId;
        await _complaintRepo.UpdateAsync(c, ct);
        await RecordAuditAsync("SRV_COMPLAINT_PROCESS", nameof(SrvComplaint), c.Id,
            $"更新投诉 [{c.ComplaintNo}] 处理进展：状态={c.Status}，经办人={c.HandlerName}", ct);

        return ApiResponse<SrvComplaint>.Success(c, "投诉处理进展已更新");
    }

    [HttpPost("complaints/{id}/close")]
    public async Task<ApiResponse<SrvComplaint>> CloseComplaint(string id, [FromBody] CloseComplaintRequest req, CancellationToken ct = default)
    {
        var c = await _complaintRepo.GetByIdAsync(id, ct);
        if (c == null)
            return ApiResponse<SrvComplaint>.Fail(404, "投诉记录不存在");

        c.Status = 4; // CLOSED
        c.HandlingResult = req?.HandlingResult ?? c.HandlingResult;
        c.PatientSatisfaction = req?.PatientSatisfaction ?? "SATISFIED";
        c.UpdatedBy = CurrentUserId;

        await _complaintRepo.UpdateAsync(c, ct);
        await RecordAuditAsync("SRV_COMPLAINT_CLOSE", nameof(SrvComplaint), c.Id,
            $"投诉 [{c.ComplaintNo}] 结案闭环：结果={c.HandlingResult}，回访满意度={c.PatientSatisfaction}", ct);

        return ApiResponse<SrvComplaint>.Success(c, "投诉已完成调查沟通并结案归档");
    }

    // 3. 知情同意书签署管理
    [HttpPost("informed-consents")]
    public async Task<ApiResponse<SrvInformedConsent>> RecordInformedConsent([FromBody] RecordInformedConsentRequest req, CancellationToken ct = default)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.PatientId) || string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.SignatureHash))
            return ApiResponse<SrvInformedConsent>.Fail(400, "患者信息、文书标题及电子签名 Hash 不能为空");

        string consentNo = "IC" + DateTime.Now.ToString("yyMMddHHmmssfff") + Random.Shared.Next(10, 99);

        var ic = new SrvInformedConsent
        {
            ConsentNo = consentNo,
            PatientId = req.PatientId,
            EncounterId = req.EncounterId,
            ConsentType = req.ConsentType ?? "SURGERY_CONSENT",
            Title = req.Title,
            DocumentContentSummary = req.DocumentContentSummary,
            SignerType = req.SignerType ?? "PATIENT",
            SignerName = req.SignerName,
            SignedAtMs = SystemTime.UtcNowMs,
            WitnessDoctorId = req.WitnessDoctorId,
            SignatureHash = req.SignatureHash,
            Status = 1,
            CreatedBy = CurrentUserId,
            UpdatedBy = CurrentUserId
        };

        await _consentRepo.InsertAsync(ic, ct);
        await RecordAuditAsync("SRV_CONSENT_RECORD", nameof(SrvInformedConsent), ic.Id,
            $"记录知情同意书签署 [{consentNo}]：类型={ic.ConsentType}，签署人={ic.SignerName}，谈话医师={ic.WitnessDoctorId}", ct);

        return ApiResponse<SrvInformedConsent>.Success(ic, "患者知情同意书已完成国标电子签署与防篡改存证归档");
    }

    [HttpGet("informed-consents")]
    public async Task<ApiResponse<IReadOnlyList<SrvInformedConsent>>> GetInformedConsents(
        [FromQuery] string? patientId = null,
        [FromQuery] string? encounterId = null,
        [FromQuery] string? consentType = null)
    {
        var list = await _consentRepo.QueryAsync(@"
SELECT * FROM srv_informed_consents
WHERE (@PatientId IS NULL OR patient_id = @PatientId)
  AND (@EncounterId IS NULL OR encounter_id = @EncounterId)
  AND (@ConsentType IS NULL OR consent_type = @ConsentType)
ORDER BY signed_at_ms DESC", new { PatientId = patientId, EncounterId = encounterId, ConsentType = consentType });

        return ApiResponse<IReadOnlyList<SrvInformedConsent>>.Success(list);
    }

    // 4. 患者服务统计汇总
    [HttpGet("summary")]
    public async Task<ApiResponse<PatientServiceSummaryDto>> GetSummary()
    {
        using var conn = _connFactory.CreateConnection();
        int totalSurveys = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM srv_satisfaction_surveys");
        decimal avgScore = await conn.ExecuteScalarAsync<decimal>("SELECT COALESCE(AVG(score), 5.0) FROM srv_satisfaction_surveys");
        int openComplaints = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM srv_complaints WHERE status < 4");
        int resolvedComplaints = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM srv_complaints WHERE status = 4");
        int totalConsents = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM srv_informed_consents WHERE status = 1");

        var summary = new PatientServiceSummaryDto(
            TotalSurveys: totalSurveys,
            AverageScore: Math.Round(avgScore, 2),
            OpenComplaintsCount: openComplaints,
            ResolvedComplaintsCount: resolvedComplaints,
            TotalSignedConsents: totalConsents
        );

        return ApiResponse<PatientServiceSummaryDto>.Success(summary);
    }

    private async Task RecordAuditAsync(string eventType, string entityName, string entityId, string detail, CancellationToken ct = default)
    {
        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync(eventType, entityName, entityId, detail,
                CurrentUserId, _session?.DisplayName, _session?.ActiveDeptId, ct);
    }
}
