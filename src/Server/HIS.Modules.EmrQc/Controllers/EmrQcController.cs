using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.EmrQc.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.EmrQc.Controllers;

[ApiController]
[Route("api/v4/emr-qc")]
public class EmrQcController : ControllerBase
{
    private readonly IRepository<EmrQcRule> _ruleRepo;
    private readonly IRepository<EmrQcTask> _taskRepo;
    private readonly IRepository<EmrQcDefect> _defectRepo;
    private readonly IRepository<EmrQcCrossOrgTrack> _crossOrgRepo;
    private readonly IEmrQcContract _qcContract;
    private readonly IUserSession _userSession;
    private readonly IAuditTrailContract? _auditContract;

    public EmrQcController(
        IRepository<EmrQcRule> ruleRepo,
        IRepository<EmrQcTask> taskRepo,
        IRepository<EmrQcDefect> defectRepo,
        IRepository<EmrQcCrossOrgTrack> crossOrgRepo,
        IEmrQcContract qcContract,
        IUserSession userSession,
        IAuditTrailContract? auditContract = null)
    {
        _ruleRepo = ruleRepo;
        _taskRepo = taskRepo;
        _defectRepo = defectRepo;
        _crossOrgRepo = crossOrgRepo;
        _qcContract = qcContract;
        _userSession = userSession;
        _auditContract = auditContract;
    }

    [HttpPost("run-auto/{encounterId}")]
    public async Task<IActionResult> RunAutoQc(string encounterId, CancellationToken ct)
    {
        var report = await _qcContract.RunAutoQcAsync(encounterId, ct);
        return Ok(ApiResponse<EmrQcReportDto>.Success(report, $"病历自动质控完成，得分: {report.TotalScore} (等级: {report.Grade})"));
    }

    [HttpGet("tasks/{encounterId}")]
    public async Task<IActionResult> GetTasksByEncounter(string encounterId, CancellationToken ct)
    {
        string sql = "SELECT * FROM emr_qc_tasks WHERE encounter_id = @EncounterId ORDER BY created_at_ms DESC";
        var list = await _taskRepo.QueryAsync(sql, new { EncounterId = encounterId });
        return Ok(ApiResponse<IReadOnlyList<EmrQcTask>>.Success(list));
    }

    [HttpGet("tasks")]
    public async Task<IActionResult> GetAllTasks([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var list = await _taskRepo.QueryAsync("SELECT * FROM emr_qc_tasks ORDER BY created_at_ms DESC LIMIT @Limit", new { Limit = limit });
        return Ok(ApiResponse<IReadOnlyList<EmrQcTask>>.Success(list));
    }

    [HttpGet("defects")]
    public async Task<IActionResult> GetDefects(
        [FromQuery] string? encounterId,
        [FromQuery] string? doctorId,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        string sql = "SELECT * FROM emr_qc_defects WHERE 1=1";
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(encounterId))
        {
            sql += " AND encounter_id = @EncounterId";
            p.Add("EncounterId", encounterId);
        }
        if (!string.IsNullOrWhiteSpace(doctorId))
        {
            sql += " AND responsible_doctor_id = @DoctorId";
            p.Add("DoctorId", doctorId);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            sql += " AND status = @Status";
            p.Add("Status", status);
        }
        sql += " ORDER BY created_at_ms DESC LIMIT 100";

        var list = await _defectRepo.QueryAsync(sql, p);
        return Ok(ApiResponse<IReadOnlyList<EmrQcDefect>>.Success(list));
    }

    public record CreateDefectDto(
        string EncounterId,
        string? DocId,
        string RuleCode,
        string QcDimension,
        string DefectDescription,
        decimal DeductPoints,
        string ResponsibleDoctorId,
        long? DeadlineMs
    );

    [HttpPost("defects")]
    public async Task<IActionResult> CreateDefect([FromBody] CreateDefectDto dto, CancellationToken ct)
    {
        long now = SystemTime.UtcNowMs;
        var defect = new EmrQcDefect
        {
            Id = Guid.NewGuid().ToString("D"),
            TaskId = "MANUAL_QC",
            EncounterId = dto.EncounterId,
            DocId = dto.DocId,
            RuleCode = dto.RuleCode,
            QcDimension = dto.QcDimension,
            DefectDescription = dto.DefectDescription,
            DeductPoints = dto.DeductPoints,
            ResponsibleDoctorId = dto.ResponsibleDoctorId,
            DeadlineMs = dto.DeadlineMs ?? (now + 24 * 3600 * 1000),
            Status = "OPEN",
            CreatedAtMs = now,
            CreatedBy = _userSession.UserId ?? "SYSTEM",
            UpdatedAtMs = now,
            UpdatedBy = _userSession.UserId ?? "SYSTEM"
        };
        await _defectRepo.InsertAsync(defect);
        return Ok(ApiResponse<EmrQcDefect>.Success(defect, "人工质控缺陷下发成功"));
    }

    public record RectifyDefectDto(string RectificationNote);

    [HttpPost("defects/{id}/rectify")]
    public async Task<IActionResult> RectifyDefect(string id, [FromBody] RectifyDefectDto dto, CancellationToken ct)
    {
        var defect = await _defectRepo.GetByIdAsync(id);
        if (defect == null) return NotFound(ApiResponse<object>.Fail(404, "质控缺陷不存在"));

        defect.Status = "DOCTOR_FIXED";
        defect.RectificationNote = dto.RectificationNote;
        defect.UpdatedAtMs = SystemTime.UtcNowMs;
        defect.UpdatedBy = _userSession.UserId ?? "SYSTEM";
        await _defectRepo.UpdateAsync(defect);

        return Ok(ApiResponse<EmrQcDefect>.Success(defect, "医师整改说明已提交，等待质控员复核"));
    }

    public record VerifyDefectDto(bool Pass, string? RejectReason);

    [HttpPost("defects/{id}/verify")]
    public async Task<IActionResult> VerifyDefect(string id, [FromBody] VerifyDefectDto dto, CancellationToken ct)
    {
        var defect = await _defectRepo.GetByIdAsync(id);
        if (defect == null) return NotFound(ApiResponse<object>.Fail(404, "质控缺陷不存在"));

        long now = SystemTime.UtcNowMs;
        defect.Status = dto.Pass ? "VERIFIED_CLOSED" : "REJECTED";
        defect.VerifiedBy = _userSession.UserId ?? "SYSTEM";
        defect.VerifiedAtMs = now;
        defect.UpdatedAtMs = now;
        defect.UpdatedBy = _userSession.UserId ?? "SYSTEM";
        await _defectRepo.UpdateAsync(defect);

        return Ok(ApiResponse<EmrQcDefect>.Success(defect, dto.Pass ? "缺陷整改复核通过，已闭环关闭" : "缺陷整改不合格，已打回重新修改"));
    }

    public record TrackCrossOrgDto(
        string PatientId,
        string SourceOrgCode,
        string ExternalDocId,
        string FindingSummary
    );

    [HttpGet("cross-org/tracks")]
    public async Task<IActionResult> GetCrossOrgTracks(CancellationToken ct)
    {
        var list = await _crossOrgRepo.QueryAsync("SELECT * FROM emr_qc_cross_org_tracks ORDER BY created_at_ms DESC LIMIT 50");
        return Ok(ApiResponse<IReadOnlyList<EmrQcCrossOrgTrack>>.Success(list));
    }

    [HttpPost("cross-org/tracks")]
    public async Task<IActionResult> TrackCrossOrg([FromBody] TrackCrossOrgDto dto, CancellationToken ct)
    {
        long now = SystemTime.UtcNowMs;
        var track = new EmrQcCrossOrgTrack
        {
            Id = Guid.NewGuid().ToString("D"),
            PatientId = dto.PatientId,
            SourceOrgCode = dto.SourceOrgCode,
            ExternalDocId = dto.ExternalDocId,
            QcReviewerId = _userSession.UserId ?? "SYSTEM",
            FindingSummary = dto.FindingSummary,
            TrackStatus = "REVIEWED",
            CreatedAtMs = now,
            CreatedBy = _userSession.UserId ?? "SYSTEM",
            UpdatedAtMs = now,
            UpdatedBy = _userSession.UserId ?? "SYSTEM"
        };
        await _crossOrgRepo.InsertAsync(track);

        return Ok(ApiResponse<EmrQcCrossOrgTrack>.Success(track, "跨机构病历质控跟踪登记成功"));
    }
}
