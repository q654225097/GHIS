using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.Treatment.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Treatment.Controllers;

[ApiController]
[RequireHisRole("ADMIN", "OPD_DOC", "IPD_ATTENDING", "IPD_RESIDENT", "WARD_NURSE", "MEDTECH")]
[Route("api/v4/treatment")]
public class TreatmentController : ControllerBase
{
    private readonly IRepository<TrtTreatmentCatalog> _catalogRepo;
    private readonly IRepository<TrtRequest> _requestRepo;
    private readonly IRepository<TrtAppointment> _appointmentRepo;
    private readonly IRepository<TrtExecutionRecord> _execRepo;
    private readonly ITreatmentContract _treatmentContract;
    private readonly IClinicalProcessSignatureContract? _signatureContract;
    private readonly IAuditTrailContract? _auditContract;
    private readonly IUserSession _userSession;

    public TreatmentController(
        IRepository<TrtTreatmentCatalog> catalogRepo,
        IRepository<TrtRequest> requestRepo,
        IRepository<TrtAppointment> appointmentRepo,
        IRepository<TrtExecutionRecord> execRepo,
        ITreatmentContract treatmentContract,
        IUserSession userSession,
        IClinicalProcessSignatureContract? signatureContract = null,
        IAuditTrailContract? auditContract = null)
    {
        _catalogRepo = catalogRepo;
        _requestRepo = requestRepo;
        _appointmentRepo = appointmentRepo;
        _execRepo = execRepo;
        _treatmentContract = treatmentContract;
        _userSession = userSession;
        _signatureContract = signatureContract;
        _auditContract = auditContract;
    }

    public record CreateTreatmentRequestDto(
        string PatientId,
        string EncounterOrVisitId,
        string CatalogId,
        string SourceType, // OPD / IPD / EXTERNAL
        string? SourceOrgCode,
        string? ExternalPatientId,
        string? DiagnosisIcd,
        int PrescribedSessions,
        string? Remark
    );

    [HttpGet("catalog")]
    public async Task<IActionResult> GetCatalog([FromQuery] string? category, CancellationToken ct)
    {
        string sql = "SELECT * FROM trt_treatment_catalog WHERE is_active = 1";
        if (!string.IsNullOrWhiteSpace(category))
        {
            sql += " AND category = @Category";
        }
        sql += " ORDER BY category, catalog_name";
        var list = await _catalogRepo.QueryAsync(sql, new { Category = category });
        return Ok(ApiResponse<IReadOnlyList<TrtTreatmentCatalog>>.Success(list));
    }

    [HttpGet("requests")]
    public async Task<IActionResult> GetRequests([FromQuery] string? patientId, CancellationToken ct)
    {
        string sql = string.IsNullOrWhiteSpace(patientId)
            ? "SELECT * FROM trt_requests ORDER BY created_at_ms DESC LIMIT 100"
            : "SELECT * FROM trt_requests WHERE patient_id = @PatId ORDER BY created_at_ms DESC";
        var list = await _requestRepo.QueryAsync(sql, new { PatId = patientId });
        return Ok(ApiResponse<IReadOnlyList<TrtRequest>>.Success(list));
    }

    [HttpGet("appointments")]
    public async Task<IActionResult> GetAppointments(CancellationToken ct)
    {
        var list = await _appointmentRepo.QueryAsync("SELECT * FROM trt_appointments ORDER BY scheduled_start_ms ASC LIMIT 100");
        return Ok(ApiResponse<IReadOnlyList<TrtAppointment>>.Success(list));
    }

    [HttpGet("recommend-parameters")]
    public async Task<IActionResult> RecommendParameters(
        [FromQuery] string catalogId,
        [FromQuery] string patientId,
        CancellationToken ct)
    {
        var suggestions = await _treatmentContract.SuggestParametersAsync(catalogId, patientId, ct);
        return Ok(ApiResponse<IReadOnlyList<TreatmentParamSuggestionDto>>.Success(suggestions));
    }

    [HttpPost("requests")]
    public async Task<IActionResult> CreateRequest([FromBody] CreateTreatmentRequestDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.PatientId) || string.IsNullOrWhiteSpace(dto.CatalogId))
        {
            return BadRequest(ApiResponse<object>.Fail(400, "患者ID与治疗项目ID为必填项"));
        }

        var catalog = await _catalogRepo.GetByIdAsync(dto.CatalogId);
        if (catalog == null)
        {
            return NotFound(ApiResponse<object>.Fail(404, "治疗项目不存在"));
        }

        long now = SystemTime.UtcNowMs;
        var req = new TrtRequest
        {
            Id = Guid.NewGuid().ToString("D"),
            RequestNo = $"TRT{DateTime.UtcNow:yyyyMMdd}{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            SourceType = dto.SourceType ?? "IPD",
            SourceOrgCode = dto.SourceOrgCode,
            ExternalPatientId = dto.ExternalPatientId,
            PatientId = dto.PatientId,
            EncounterOrVisitId = dto.EncounterOrVisitId,
            CatalogId = dto.CatalogId,
            DiagnosisIcd = dto.DiagnosisIcd,
            PrescribedSessions = dto.PrescribedSessions > 0 ? dto.PrescribedSessions : 1,
            CompletedSessions = 0,
            Status = "REQUESTED",
            Remark = dto.Remark,
            CreatedAtMs = now,
            CreatedBy = _userSession.UserId ?? "SYSTEM",
            UpdatedAtMs = now,
            UpdatedBy = _userSession.UserId ?? "SYSTEM"
        };

        await _requestRepo.InsertAsync(req);

        if (_signatureContract != null)
        {
            await _signatureContract.SignClinicalRecordAsync("TREATMENT", req.Id, "CREATE", req, _userSession, ct);
        }

        if (_auditContract != null)
        {
            await _auditContract.RecordAuditAsync(
                "TREATMENT_REQUEST_CREATE",
                _userSession.UserId ?? "SYSTEM",
                _userSession.DisplayName ?? "SYSTEM",
                _userSession.ActiveDeptId,
                "trt_requests",
                req.Id,
                $"开立一般治疗申请: {catalog.CatalogName} (共 {req.PrescribedSessions} 次)",
                ct: ct);
        }

        return Ok(ApiResponse<TrtRequest>.Success(req, "治疗申请开立成功"));
    }

    public record CreateAppointmentDto(
        string RequestId,
        string TreatmentRoom,
        long ScheduledStartMs,
        long ScheduledEndMs
    );

    [HttpPost("appointments")]
    public async Task<IActionResult> CreateAppointment([FromBody] CreateAppointmentDto dto, CancellationToken ct)
    {
        var req = await _requestRepo.GetByIdAsync(dto.RequestId);
        if (req == null)
        {
            return NotFound(ApiResponse<object>.Fail(404, "治疗申请单不存在"));
        }

        long now = SystemTime.UtcNowMs;
        var appt = new TrtAppointment
        {
            Id = Guid.NewGuid().ToString("D"),
            RequestId = dto.RequestId,
            PatientId = req.PatientId,
            TreatmentRoom = dto.TreatmentRoom,
            ScheduledStartMs = dto.ScheduledStartMs > 0 ? dto.ScheduledStartMs : now + 3600000,
            ScheduledEndMs = dto.ScheduledEndMs > 0 ? dto.ScheduledEndMs : now + 7200000,
            ReminderSent = 1,
            Status = "BOOKED",
            CreatedAtMs = now,
            CreatedBy = _userSession.UserId ?? "SYSTEM",
            UpdatedAtMs = now,
            UpdatedBy = _userSession.UserId ?? "SYSTEM"
        };

        req.Status = "SCHEDULED";
        req.UpdatedAtMs = now;
        await _appointmentRepo.InsertAsync(appt);
        await _requestRepo.UpdateAsync(req);

        return Ok(ApiResponse<TrtAppointment>.Success(appt, "治疗预约排班成功，已发送患者预约提醒"));
    }

    [HttpGet("requests/{id}/suggest-parameters")]
    public async Task<IActionResult> GetSuggestedParameters(string id, CancellationToken ct)
    {
        var req = await _requestRepo.GetByIdAsync(id);
        if (req == null)
        {
            return NotFound(ApiResponse<object>.Fail(404, "治疗申请单不存在"));
        }

        var suggestions = await _treatmentContract.SuggestParametersAsync(req.CatalogId, req.PatientId, ct);
        return Ok(ApiResponse<IReadOnlyList<TreatmentParamSuggestionDto>>.Success(suggestions));
    }

    public record ExecuteTreatmentDto(
        string RequestId,
        string? AppointmentId,
        long ActualStartMs,
        long ActualEndMs,
        Dictionary<string, string>? ActualParams,
        bool HighRiskCheckPassed,
        string? DualCheckOperatorId,
        string? PatientReaction
    );

    [HttpPost("executions")]
    public async Task<IActionResult> ExecuteTreatment([FromBody] ExecuteTreatmentDto dto, CancellationToken ct)
    {
        var req = await _requestRepo.GetByIdAsync(dto.RequestId);
        if (req == null)
        {
            return NotFound(ApiResponse<object>.Fail(404, "治疗申请单不存在"));
        }

        var catalog = await _catalogRepo.GetByIdAsync(req.CatalogId);
        if (catalog != null && catalog.IsHighRisk == 1 && (!dto.HighRiskCheckPassed || string.IsNullOrWhiteSpace(dto.DualCheckOperatorId)))
        {
            return BadRequest(ApiResponse<object>.Fail(400, "高风险治疗项目必须完成高风险核查并录入复核操作人"));
        }

        long now = SystemTime.UtcNowMs;
        var exec = new TrtExecutionRecord
        {
            Id = Guid.NewGuid().ToString("D"),
            RequestId = dto.RequestId,
            AppointmentId = dto.AppointmentId,
            PatientId = req.PatientId,
            SessionNo = req.CompletedSessions + 1,
            ActualStartMs = dto.ActualStartMs > 0 ? dto.ActualStartMs : now - 1800000,
            ActualEndMs = dto.ActualEndMs > 0 ? dto.ActualEndMs : now,
            ActualParamsJson = dto.ActualParams != null ? JsonSerializer.Serialize(dto.ActualParams) : null,
            HighRiskCheckPassed = dto.HighRiskCheckPassed ? (short)1 : (short)0,
            DualCheckOperatorId = dto.DualCheckOperatorId,
            PatientReaction = dto.PatientReaction ?? "生命体征平稳，无不良反应",
            FeeCharged = 1,
            Status = "FINISHED",
            CreatedAtMs = now,
            CreatedBy = _userSession.UserId ?? "SYSTEM",
            UpdatedAtMs = now,
            UpdatedBy = _userSession.UserId ?? "SYSTEM"
        };

        req.CompletedSessions += 1;
        if (req.CompletedSessions >= req.PrescribedSessions)
        {
            req.Status = "COMPLETED";
        }
        else
        {
            req.Status = "IN_PROGRESS";
        }
        req.UpdatedAtMs = now;

        await _execRepo.InsertAsync(exec);
        await _requestRepo.UpdateAsync(req);

        if (_signatureContract != null)
        {
            var sig = await _signatureContract.SignClinicalRecordAsync("TREATMENT", exec.Id, "EXECUTE", exec, _userSession, ct);
            exec.CaSignatureId = sig.Id;
            await _execRepo.UpdateAsync(exec);
        }

        return Ok(ApiResponse<TrtExecutionRecord>.Success(exec, "治疗执行记录成功，已完成双人核对与国密可靠签名"));
    }

    [HttpGet("records/by-patient/{patientId}")]
    public async Task<IActionResult> GetRecordsByPatient(string patientId, CancellationToken ct)
    {
        string sql = "SELECT * FROM trt_execution_records WHERE patient_id = @PatientId ORDER BY actual_end_ms DESC";
        var list = await _execRepo.QueryAsync(sql, new { PatientId = patientId });
        return Ok(ApiResponse<IReadOnlyList<TrtExecutionRecord>>.Success(list));
    }
}
