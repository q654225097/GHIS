using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.ClinicalPathway.Models;
using HIS.Modules.ClinicalPathway.Services;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.ClinicalPathway.Controllers;

[ApiController]
[Route("api/v4/cpw")]
public class ClinicalPathwayController : ControllerBase
{
    private readonly IClinicalPathwayContract _cpwService;
    private readonly ClinicalPathwayService _cpwConcreteService;
    private readonly IRepository<CpwPathway> _pathwayRepo;
    private readonly IRepository<CpwPatientEnrollment> _enrollmentRepo;

    public record EnrollRequestDto(
        string EncounterId,
        string PatientId,
        string PathwayId,
        string EnrolledBy
    );

    public record RecordVarianceRequestDto(
        string StageId,
        string? StageItemId,
        string? RelatedOrderId,
        string VarianceType,
        string ReasonCategory,
        string ReasonDetail,
        string RecordedBy
    );

    public ClinicalPathwayController(
        IClinicalPathwayContract cpwService,
        ClinicalPathwayService cpwConcreteService,
        IRepository<CpwPathway> pathwayRepo,
        IRepository<CpwPatientEnrollment> enrollmentRepo)
    {
        _cpwService = cpwService;
        _cpwConcreteService = cpwConcreteService;
        _pathwayRepo = pathwayRepo;
        _enrollmentRepo = enrollmentRepo;
    }

    [HttpGet("match")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CpwMatchResultDto>>>> Match(
        [FromQuery] string icdCode,
        CancellationToken ct)
    {
        var result = await _cpwService.MatchPathwaysAsync(icdCode ?? string.Empty, ct);
        return Ok(ApiResponse<IReadOnlyList<CpwMatchResultDto>>.Success(result, "匹配临床路径成功"));
    }

    [HttpPost("enroll")]
    public async Task<ActionResult<ApiResponse<CpwEnrollmentDto>>> Enroll(
        [FromBody] EnrollRequestDto req,
        CancellationToken ct)
    {
        var result = await _cpwService.EnrollPatientAsync(req.EncounterId, req.PatientId, req.PathwayId, req.EnrolledBy, ct);
        return Ok(ApiResponse<CpwEnrollmentDto>.Success(result, "患者成功进入临床路径"));
    }

    [HttpPost("enrollments/{id}/compare-orders")]
    public async Task<ActionResult<ApiResponse<CpwOrderComparisonResultDto>>> CompareOrders(
        string id,
        CancellationToken ct)
    {
        var result = await _cpwService.CompareOrdersAsync(id, ct);
        return Ok(ApiResponse<CpwOrderComparisonResultDto>.Success(result, "路径医嘱对比完成"));
    }

    [HttpPost("enrollments/{id}/variances")]
    public async Task<ActionResult<ApiResponse<string>>> RecordVariance(
        string id,
        [FromBody] RecordVarianceRequestDto req,
        CancellationToken ct)
    {
        string varId = await _cpwConcreteService.RecordVarianceAsync(
            id, req.StageId, req.StageItemId, req.RelatedOrderId,
            req.VarianceType, req.ReasonCategory, req.ReasonDetail, req.RecordedBy, ct);
        return Ok(ApiResponse<string>.Success(varId, "临床路径变异记录成功"));
    }

    [HttpGet("enrollments/{id}/emr-fusion")]
    public async Task<ActionResult<ApiResponse<CpwEmrFusionSnippetDto>>> GetEmrFusion(
        string id,
        CancellationToken ct)
    {
        var result = await _cpwService.GetEmrFusionSnippetAsync(id, ct);
        return Ok(ApiResponse<CpwEmrFusionSnippetDto>.Success(result, "获取临床路径病历融合段落成功"));
    }

    [HttpGet("enrollments/by-encounter/{encounterId}")]
    public async Task<ActionResult<ApiResponse<CpwEnrollmentDto?>>> GetByEncounter(
        string encounterId,
        CancellationToken ct)
    {
        var list = await _enrollmentRepo.QueryAsync(
            "SELECT * FROM cpw_patient_enrollments WHERE encounter_id = @EncId AND status = 'ACTIVE' ORDER BY created_at_ms DESC LIMIT 1",
            new { EncId = encounterId }, ct);

        var first = list.FirstOrDefault();
        if (first == null)
        {
            return Ok(ApiResponse<CpwEnrollmentDto?>.Success(null, "当前就诊尚未入径"));
        }

        var pathway = await _pathwayRepo.GetByIdAsync(first.PathwayId, ct);
        var dto = new CpwEnrollmentDto(
            Id: first.Id,
            EncounterId: first.EncounterId,
            PatientId: first.PatientId,
            PathwayId: first.PathwayId,
            PathwayName: pathway?.PathwayName ?? "临床路径",
            CurrentStageId: first.CurrentStageId ?? string.Empty,
            CurrentStageName: "执行中阶段",
            Status: first.Status
        );

        return Ok(ApiResponse<CpwEnrollmentDto?>.Success(dto, "获取当前在径状态成功"));
    }
}
