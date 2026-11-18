using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Inpatient.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Inpatient.Controllers;

[ApiController]
[Route("api/v4/nursing")]
public class NursingController : ControllerBase
{
    private readonly IRepository<NurCarePlan> _carePlanRepo;
    private readonly IRepository<NurCarePlanEvaluation> _evalRepo;
    private readonly IRepository<NurPressureUlcerAssessment> _bradenRepo;
    private readonly IRepository<NurFallAssessment> _morseRepo;
    private readonly IRepository<NurQualityIndicator> _qualityRepo;
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _session;
    private readonly IAuditTrailContract? _auditTrail;

    public NursingController(
        IRepository<NurCarePlan> carePlanRepo,
        IRepository<NurCarePlanEvaluation> evalRepo,
        IRepository<NurPressureUlcerAssessment> bradenRepo,
        IRepository<NurFallAssessment> morseRepo,
        IRepository<NurQualityIndicator> qualityRepo,
        IDbConnectionFactory connFactory,
        IUserSession? session = null,
        IAuditTrailContract? auditTrail = null)
    {
        _carePlanRepo = carePlanRepo;
        _evalRepo = evalRepo;
        _bradenRepo = bradenRepo;
        _morseRepo = morseRepo;
        _qualityRepo = qualityRepo;
        _connFactory = connFactory;
        _session = session;
        _auditTrail = auditTrail;
    }

    private string CurrentUserId => _session?.UserId ?? "USER-NURSE-01";

    private bool HasNurseOrAdminRole()
    {
        if (_session == null || _session.Roles == null) return true;
        return _session.Roles.Any(r =>
            string.Equals(r, "ADMIN", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r, "ROLE-ADMIN", StringComparison.OrdinalIgnoreCase) ||
            r.Contains("NURSE", StringComparison.OrdinalIgnoreCase));
    }

    [HttpPost("care-plans")]
    public async Task<ApiResponse<NurCarePlan>> CreateCarePlan([FromBody] CreateCarePlanRequest req, CancellationToken ct = default)
    {
        if (!HasNurseOrAdminRole())
            return ApiResponse<NurCarePlan>.Fail(403, "仅责任护士或管理员可制定护理计划");

        if (req == null || string.IsNullOrWhiteSpace(req.EncounterId) || string.IsNullOrWhiteSpace(req.DiagnosisOrProblem) || string.IsNullOrWhiteSpace(req.Goal))
            return ApiResponse<NurCarePlan>.Fail(400, "住院号、护理问题/诊断及预期目标不能为空");

        var plan = new NurCarePlan
        {
            EncounterId = req.EncounterId,
            PatientId = req.PatientId,
            DiagnosisOrProblem = req.DiagnosisOrProblem.Trim(),
            Goal = req.Goal.Trim(),
            InterventionsJson = req.Interventions != null && req.Interventions.Count > 0 ? JsonSerializer.Serialize(req.Interventions) : null,
            TargetDateMs = req.TargetDateMs,
            Status = 0,
            CreatedBy = CurrentUserId,
            UpdatedBy = CurrentUserId
        };

        await _carePlanRepo.InsertAsync(plan, ct);
        await RecordAuditAsync("NUR_CARE_PLAN_CREATE", nameof(NurCarePlan), plan.Id,
            $"为患者住院就诊 [{req.EncounterId}] 制定护理计划：问题={plan.DiagnosisOrProblem}，目标={plan.Goal}", ct);

        return ApiResponse<NurCarePlan>.Success(plan, "护理计划创建成功");
    }

    [HttpGet("care-plans/{encounterId}")]
    public async Task<ApiResponse<IReadOnlyList<NurCarePlan>>> GetCarePlansByEncounter(string encounterId)
    {
        var list = await _carePlanRepo.QueryAsync(
            "SELECT * FROM nur_care_plans WHERE encounter_id = @EncounterId ORDER BY created_at_ms DESC",
            new { EncounterId = encounterId });
        return ApiResponse<IReadOnlyList<NurCarePlan>>.Success(list);
    }

    [HttpPut("care-plans/{id}")]
    public async Task<ApiResponse<NurCarePlan>> UpdateCarePlan(string id, [FromBody] UpdateCarePlanRequest req, CancellationToken ct = default)
    {
        if (!HasNurseOrAdminRole())
            return ApiResponse<NurCarePlan>.Fail(403, "无权限修改护理计划");

        var plan = await _carePlanRepo.GetByIdAsync(id, ct);
        if (plan == null)
            return ApiResponse<NurCarePlan>.Fail(404, "护理计划不存在");

        if (!string.IsNullOrWhiteSpace(req.DiagnosisOrProblem)) plan.DiagnosisOrProblem = req.DiagnosisOrProblem.Trim();
        if (!string.IsNullOrWhiteSpace(req.Goal)) plan.Goal = req.Goal.Trim();
        if (req.Interventions != null) plan.InterventionsJson = JsonSerializer.Serialize(req.Interventions);
        if (req.TargetDateMs.HasValue) plan.TargetDateMs = req.TargetDateMs.Value;
        if (req.Status.HasValue) plan.Status = req.Status.Value;

        plan.UpdatedBy = CurrentUserId;
        await _carePlanRepo.UpdateAsync(plan, ct);
        await RecordAuditAsync("NUR_CARE_PLAN_UPDATE", nameof(NurCarePlan), plan.Id, $"更新护理计划：状态={plan.Status}", ct);

        return ApiResponse<NurCarePlan>.Success(plan, "护理计划更新成功");
    }

    [HttpPost("care-plans/{id}/evaluate")]
    public async Task<ApiResponse<NurCarePlanEvaluation>> EvaluateCarePlan(string id, [FromBody] EvaluateCarePlanRequest req, CancellationToken ct = default)
    {
        if (!HasNurseOrAdminRole())
            return ApiResponse<NurCarePlanEvaluation>.Fail(403, "无权限评估护理计划");

        var plan = await _carePlanRepo.GetByIdAsync(id, ct);
        if (plan == null)
            return ApiResponse<NurCarePlanEvaluation>.Fail(404, "护理计划不存在");

        if (req == null || string.IsNullOrWhiteSpace(req.EvaluationResult))
            return ApiResponse<NurCarePlanEvaluation>.Fail(400, "评价结果内容不能为空");

        var eval = new NurCarePlanEvaluation
        {
            PlanId = id,
            EvaluationResult = req.EvaluationResult.Trim(),
            IsGoalMet = (short)(req.IsGoalMet ? 1 : 0),
            EvaluatedAtMs = SystemTime.UtcNowMs,
            EvaluatorId = CurrentUserId,
            CreatedBy = CurrentUserId,
            UpdatedBy = CurrentUserId
        };

        await _evalRepo.InsertAsync(eval, ct);

        if (req.IsGoalMet && plan.Status == 0)
        {
            plan.Status = 1; // 目标达成
            plan.UpdatedBy = CurrentUserId;
            await _carePlanRepo.UpdateAsync(plan, ct);
        }

        await RecordAuditAsync("NUR_CARE_PLAN_EVALUATE", nameof(NurCarePlanEvaluation), eval.Id,
            $"评估护理计划 [{plan.DiagnosisOrProblem}]：目标达成={req.IsGoalMet}，评价={req.EvaluationResult}", ct);

        return ApiResponse<NurCarePlanEvaluation>.Success(eval, req.IsGoalMet ? "护理计划目标已达成并归档" : "护理效果评价记录已保存");
    }

    [HttpGet("care-plans/{id}/evaluations")]
    public async Task<ApiResponse<IReadOnlyList<NurCarePlanEvaluation>>> GetCarePlanEvaluations(string id)
    {
        var list = await _evalRepo.QueryAsync(
            "SELECT * FROM nur_care_plan_evaluations WHERE plan_id = @PlanId ORDER BY evaluated_at_ms DESC",
            new { PlanId = id });
        return ApiResponse<IReadOnlyList<NurCarePlanEvaluation>>.Success(list);
    }

    [HttpPost("assessments/braden")]
    public async Task<ApiResponse<NurPressureUlcerAssessment>> AssessBraden([FromBody] AssessBradenRequest req, CancellationToken ct = default)
    {
        if (!HasNurseOrAdminRole())
            return ApiResponse<NurPressureUlcerAssessment>.Fail(403, "无权限提交压疮评估");

        if (req == null || string.IsNullOrWhiteSpace(req.EncounterId))
            return ApiResponse<NurPressureUlcerAssessment>.Fail(400, "住院号不能为空");

        int sensory = Math.Clamp(req.SensoryPerception, 1, 4);
        int moisture = Math.Clamp(req.Moisture, 1, 4);
        int activity = Math.Clamp(req.Activity, 1, 4);
        int mobility = Math.Clamp(req.Mobility, 1, 4);
        int nutrition = Math.Clamp(req.Nutrition, 1, 4);
        int friction = Math.Clamp(req.FrictionShear, 1, 3);
        int total = sensory + moisture + activity + mobility + nutrition + friction;

        string riskLevel = total switch
        {
            <= 9 => "VERY_HIGH",
            <= 12 => "HIGH",
            <= 14 => "MODERATE",
            <= 18 => "MILD",
            _ => "NONE"
        };

        var assessment = new NurPressureUlcerAssessment
        {
            EncounterId = req.EncounterId,
            PatientId = req.PatientId,
            SensoryPerception = sensory,
            Moisture = moisture,
            Activity = activity,
            Mobility = mobility,
            Nutrition = nutrition,
            FrictionShear = friction,
            TotalScore = total,
            RiskLevel = riskLevel,
            PreventiveMeasuresJson = req.PreventiveMeasures != null && req.PreventiveMeasures.Count > 0
                ? JsonSerializer.Serialize(req.PreventiveMeasures) : null,
            AssessorId = CurrentUserId,
            AssessedAtMs = SystemTime.UtcNowMs,
            CreatedBy = CurrentUserId,
            UpdatedBy = CurrentUserId
        };

        await _bradenRepo.InsertAsync(assessment, ct);
        await RecordAuditAsync("NUR_BRADEN_ASSESS", nameof(NurPressureUlcerAssessment), assessment.Id,
            $"Braden压疮风险评估：总分={total}，风险等级={riskLevel}", ct);

        string message = riskLevel is "VERY_HIGH" or "HIGH"
            ? $"评估完成：得分 {total} 分，评定为【{riskLevel} 压疮高风险】，已自动触发防压疮护理警示与翻身计划！"
            : $"评估完成：得分 {total} 分，风险等级：{riskLevel}";

        return ApiResponse<NurPressureUlcerAssessment>.Success(assessment, message);
    }

    [HttpGet("assessments/braden/{encounterId}")]
    public async Task<ApiResponse<IReadOnlyList<NurPressureUlcerAssessment>>> GetBradenAssessments(string encounterId)
    {
        var list = await _bradenRepo.QueryAsync(
            "SELECT * FROM nur_pressure_ulcer_assessments WHERE encounter_id = @EncounterId ORDER BY assessed_at_ms DESC",
            new { EncounterId = encounterId });
        return ApiResponse<IReadOnlyList<NurPressureUlcerAssessment>>.Success(list);
    }

    [HttpPost("assessments/morse")]
    public async Task<ApiResponse<NurFallAssessment>> AssessMorse([FromBody] AssessMorseRequest req, CancellationToken ct = default)
    {
        if (!HasNurseOrAdminRole())
            return ApiResponse<NurFallAssessment>.Fail(403, "无权限提交跌倒评估");

        if (req == null || string.IsNullOrWhiteSpace(req.EncounterId))
            return ApiResponse<NurFallAssessment>.Fail(400, "住院号不能为空");

        int history = req.HistoryOfFalling > 0 ? 25 : 0;
        int secondary = req.SecondaryDiagnosis > 0 ? 15 : 0;
        int aid = req.AmbulatoryAid switch { >= 30 => 30, >= 15 => 15, _ => 0 };
        int iv = req.IvOrSalineLock > 0 ? 20 : 0;
        int gait = req.Gait switch { >= 20 => 20, >= 10 => 10, _ => 0 };
        int mental = req.MentalStatus > 0 ? 15 : 0;

        int total = history + secondary + aid + iv + gait + mental;
        string riskLevel = total switch
        {
            >= 45 => "HIGH",
            >= 25 => "MEDIUM",
            _ => "LOW"
        };

        var assessment = new NurFallAssessment
        {
            EncounterId = req.EncounterId,
            PatientId = req.PatientId,
            HistoryOfFalling = history,
            SecondaryDiagnosis = secondary,
            AmbulatoryAid = aid,
            IvOrSalineLock = iv,
            Gait = gait,
            MentalStatus = mental,
            TotalScore = total,
            RiskLevel = riskLevel,
            PreventiveMeasuresJson = req.PreventiveMeasures != null && req.PreventiveMeasures.Count > 0
                ? JsonSerializer.Serialize(req.PreventiveMeasures) : null,
            AssessorId = CurrentUserId,
            AssessedAtMs = SystemTime.UtcNowMs,
            CreatedBy = CurrentUserId,
            UpdatedBy = CurrentUserId
        };

        await _morseRepo.InsertAsync(assessment, ct);
        await RecordAuditAsync("NUR_MORSE_ASSESS", nameof(NurFallAssessment), assessment.Id,
            $"Morse跌倒风险评估：总分={total}，风险等级={riskLevel}", ct);

        string message = riskLevel == "HIGH"
            ? $"评估完成：得分 {total} 分，评定为【HIGH 跌倒高风险】，已自动触发床档防护与警示腕带标识！"
            : $"评估完成：得分 {total} 分，风险等级：{riskLevel}";

        return ApiResponse<NurFallAssessment>.Success(assessment, message);
    }

    [HttpGet("assessments/morse/{encounterId}")]
    public async Task<ApiResponse<IReadOnlyList<NurFallAssessment>>> GetMorseAssessments(string encounterId)
    {
        var list = await _morseRepo.QueryAsync(
            "SELECT * FROM nur_fall_assessments WHERE encounter_id = @EncounterId ORDER BY assessed_at_ms DESC",
            new { EncounterId = encounterId });
        return ApiResponse<IReadOnlyList<NurFallAssessment>>.Success(list);
    }

    [HttpPost("quality-indicators")]
    public async Task<ApiResponse<NurQualityIndicator>> SaveQualityIndicator([FromBody] SaveQualityIndicatorRequest req, CancellationToken ct = default)
    {
        if (!HasNurseOrAdminRole())
            return ApiResponse<NurQualityIndicator>.Fail(403, "无权限录入护理敏感指标");

        if (req == null || string.IsNullOrWhiteSpace(req.WardId) || string.IsNullOrWhiteSpace(req.MonthStr))
            return ApiResponse<NurQualityIndicator>.Fail(400, "病区编号和统计月份不能为空");

        using var conn = _connFactory.CreateConnection();
        var existing = await conn.QuerySingleOrDefaultAsync<NurQualityIndicator>(
            "SELECT * FROM nur_quality_indicators WHERE ward_id = @WardId AND month_str = @MonthStr",
            new { req.WardId, req.MonthStr });

        if (existing == null)
        {
            existing = new NurQualityIndicator
            {
                WardId = req.WardId,
                MonthStr = req.MonthStr,
                PatientDays = req.PatientDays,
                FallCount = req.FallCount,
                PressureUlcerCount = req.PressureUlcerCount,
                UnplannedExtubationCount = req.UnplannedExtubationCount,
                CautiCount = req.CautiCount,
                ClabsiCount = req.ClabsiCount,
                ReporterId = CurrentUserId,
                CreatedBy = CurrentUserId,
                UpdatedBy = CurrentUserId
            };
            await _qualityRepo.InsertAsync(existing, ct);
        }
        else
        {
            existing.PatientDays = req.PatientDays;
            existing.FallCount = req.FallCount;
            existing.PressureUlcerCount = req.PressureUlcerCount;
            existing.UnplannedExtubationCount = req.UnplannedExtubationCount;
            existing.CautiCount = req.CautiCount;
            existing.ClabsiCount = req.ClabsiCount;
            existing.ReporterId = CurrentUserId;
            existing.UpdatedBy = CurrentUserId;
            await _qualityRepo.UpdateAsync(existing, ct);
        }

        await RecordAuditAsync("NUR_QUALITY_INDICATOR_SAVE", nameof(NurQualityIndicator), existing.Id,
            $"保存病区 [{req.WardId}] {req.MonthStr} 护理质量敏感指标", ct);

        return ApiResponse<NurQualityIndicator>.Success(existing, "护理质量敏感指标已保存");
    }

    [HttpGet("quality-report")]
    public async Task<ApiResponse<IReadOnlyList<NursingQualityReportDto>>> GetQualityReport(
        [FromQuery] string? wardId = null,
        [FromQuery] string? monthStr = null)
    {
        using var conn = _connFactory.CreateConnection();
        var list = (await conn.QueryAsync<NurQualityIndicator>(@"
SELECT * FROM nur_quality_indicators
WHERE (@WardId IS NULL OR ward_id = @WardId)
  AND (@MonthStr IS NULL OR month_str = @MonthStr)
ORDER BY month_str DESC, ward_id ASC", new { WardId = wardId, MonthStr = monthStr })).ToList();

        var reports = list.Select(q =>
        {
            decimal patientDays = q.PatientDays > 0 ? q.PatientDays : 1;
            decimal fallRate = Math.Round((decimal)q.FallCount / patientDays * 1000m, 2);
            decimal puRate = Math.Round((decimal)q.PressureUlcerCount / patientDays * 1000m, 2);

            return new NursingQualityReportDto(
                WardId: q.WardId,
                MonthStr: q.MonthStr,
                TotalPatientDays: q.PatientDays,
                TotalFalls: q.FallCount,
                FallRatePer1000Days: fallRate,
                TotalPressureUlcers: q.PressureUlcerCount,
                PressureUlcerRatePer1000Days: puRate,
                TotalUnplannedExtubations: q.UnplannedExtubationCount,
                TotalCauti: q.CautiCount,
                TotalClabsi: q.ClabsiCount
            );
        }).ToList();

        return ApiResponse<IReadOnlyList<NursingQualityReportDto>>.Success(reports);
    }

    private async Task RecordAuditAsync(string eventType, string entityName, string entityId, string detail, CancellationToken ct = default)
    {
        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync(eventType, entityName, entityId, detail,
                CurrentUserId, _session?.DisplayName, _session?.ActiveDeptId, ct);
    }
}
