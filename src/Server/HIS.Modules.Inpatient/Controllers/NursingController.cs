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
    private readonly IClinicalProcessSignatureContract? _signatureContract;
    private readonly IClinicalAlertPublisher? _alertPublisher;
    private readonly IFinanceSettlementContract? _financeContract;

    public NursingController(
        IRepository<NurCarePlan> carePlanRepo,
        IRepository<NurCarePlanEvaluation> evalRepo,
        IRepository<NurPressureUlcerAssessment> bradenRepo,
        IRepository<NurFallAssessment> morseRepo,
        IRepository<NurQualityIndicator> qualityRepo,
        IDbConnectionFactory connFactory,
        IUserSession? session = null,
        IAuditTrailContract? auditTrail = null,
        IClinicalProcessSignatureContract? signatureContract = null,
        IClinicalAlertPublisher? alertPublisher = null,
        IFinanceSettlementContract? financeContract = null)
    {
        _carePlanRepo = carePlanRepo;
        _evalRepo = evalRepo;
        _bradenRepo = bradenRepo;
        _morseRepo = morseRepo;
        _qualityRepo = qualityRepo;
        _connFactory = connFactory;
        _session = session;
        _auditTrail = auditTrail;
        _signatureContract = signatureContract;
        _alertPublisher = alertPublisher;
        _financeContract = financeContract;
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

        // 高危自动联动生成活动期护理计划
        if (riskLevel is "VERY_HIGH" or "HIGH")
        {
            var autoPlan = new NurCarePlan
            {
                EncounterId = req.EncounterId,
                PatientId = req.PatientId,
                DiagnosisOrProblem = "皮肤完整性受损的危险（压疮高风险）",
                Goal = "住院期间受压部位皮肤完整，无红肿破溃，未发生院内获得性压疮",
                InterventionsJson = JsonSerializer.Serialize(new[]
                {
                    "严格执行防压疮翻身卡，每2小时协助翻身减压一次并记录体位",
                    "使用防褥疮减压充气床垫，骨隆突受压部位予软枕悬空减压",
                    "每日温水清洁皮肤，保持床单及衣物平整干燥，避免排泄物潮湿刺激",
                    "指导高蛋白、高维生素饮食以改善机体营养状态"
                }),
                Status = 0, // In Progress
                CreatedBy = CurrentUserId,
                UpdatedBy = CurrentUserId
            };
            await _carePlanRepo.InsertAsync(autoPlan, ct);
        }

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

        // 高危自动联动生成活动期防跌倒护理计划
        if (riskLevel == "HIGH")
        {
            var autoFallPlan = new NurCarePlan
            {
                EncounterId = req.EncounterId,
                PatientId = req.PatientId,
                DiagnosisOrProblem = "有跌倒坠床的危险（跌倒高风险）",
                Goal = "住院期间防范跌倒防范措施落实，未发生跌倒或坠床不良事件",
                InterventionsJson = JsonSerializer.Serialize(new[]
                {
                    "床头及床尾悬挂防跌倒高危警示标识牌，佩戴防跌倒腕带",
                    "双侧床档持续拉起，呼叫铃置于患者伸手可及处，告知家属留陪护",
                    "保持病室地面干燥清洁防滑，指导穿防滑鞋，夜间开启地灯",
                    "协助如厕及床旁活动，指导起床三步曲：醒后卧床半分钟，坐起半分钟，垂腿半分钟"
                }),
                Status = 0, // In Progress
                CreatedBy = CurrentUserId,
                UpdatedBy = CurrentUserId
            };
            await _carePlanRepo.InsertAsync(autoFallPlan, ct);
        }

        string message = riskLevel == "HIGH"
            ? $"评估完成：得分 {total} 分，评定为【HIGH 跌倒高风险】，已自动触发床档防护与防跌倒护理计划！"
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

    public static int CalculateMews(int sysBp, int heartRate, int respRate, decimal tempC)
    {
        int score = 0;
        if (sysBp > 0)
        {
            if (sysBp <= 70) score += 3;
            else if (sysBp <= 80) score += 2;
            else if (sysBp <= 100) score += 1;
            else if (sysBp >= 200) score += 2;
        }
        if (heartRate > 0)
        {
            if (heartRate <= 40) score += 2;
            else if (heartRate <= 50) score += 1;
            else if (heartRate >= 130) score += 3;
            else if (heartRate >= 111) score += 2;
            else if (heartRate >= 101) score += 1;
        }
        if (respRate > 0)
        {
            if (respRate < 9) score += 2;
            else if (respRate >= 30) score += 3;
            else if (respRate >= 21) score += 2;
            else if (respRate >= 15) score += 1;
        }
        if (tempC > 0)
        {
            if (tempC < 35.0m) score += 2;
            else if (tempC >= 38.5m) score += 2;
        }
        return score;
    }

    [HttpPost("vitals")]
    public async Task<ApiResponse<NurVitalSign>> RecordVitalSigns([FromBody] VitalSignInputDto req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.EncounterId) || string.IsNullOrWhiteSpace(req.PatientId))
            return ApiResponse<NurVitalSign>.Fail(400, "就诊ID与患者ID不能为空");

        int mews = CalculateMews(req.SysBp, req.HeartRateBpm > 0 ? req.HeartRateBpm : req.PulseBpm, req.RespRate, req.TemperatureC);
        var autoMeasures = new List<string>();
        if (req.TemperatureC >= 38.5m) autoMeasures.Add("高热护理：物理降温，监测体温q4h，温水擦浴");
        if (req.SysBp > 0 && req.SysBp < 90) autoMeasures.Add("低血压休克风险：平卧位，报告管床医师，快速补液建立静脉双通道");
        if (req.Spo2 > 0 && req.Spo2 < 93) autoMeasures.Add("低氧血症风险：面罩给氧，持续脉搏血氧监测");
        if (mews >= 4) autoMeasures.Add($"MEWS 评分达到 {mews} 分高危预警：启动院内急救反应小组 (RRT)，准备床旁监护抢救");

        var vital = new NurVitalSign
        {
            EncounterId = req.EncounterId,
            PatientId = req.PatientId,
            MeasuredAtMs = req.MeasuredAtMs > 0 ? req.MeasuredAtMs : SystemTime.UtcNowMs,
            TemperatureC = req.TemperatureC,
            PulseBpm = req.PulseBpm,
            HeartRateBpm = req.HeartRateBpm,
            RespRate = req.RespRate,
            SysBp = req.SysBp,
            DiaBp = req.DiaBp,
            Spo2 = req.Spo2,
            BloodGlucose = req.BloodGlucose,
            PainScore = req.PainScore,
            IntakeMl = req.IntakeMl,
            OutputMl = req.OutputMl,
            CaptureSource = string.IsNullOrWhiteSpace(req.CaptureSource) ? "MANUAL" : req.CaptureSource,
            DeviceId = req.DeviceId,
            MewsScore = mews,
            AutoTriggeredMeasuresJson = JsonSerializer.Serialize(autoMeasures),
            CreatedBy = CurrentUserId,
            UpdatedBy = CurrentUserId
        };

        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(
            """
            INSERT INTO nur_vital_signs (id, encounter_id, patient_id, measured_at_ms, temperature_c, pulse_bpm, heart_rate_bpm,
                resp_rate, sys_bp, dia_bp, spo2, blood_glucose, pain_score, intake_ml, output_ml, capture_source, device_id,
                mews_score, auto_triggered_measures_json, is_external, source_org_code, ext_data_json, schema_version,
                created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @EncounterId, @PatientId, @MeasuredAtMs, @TemperatureC, @PulseBpm, @HeartRateBpm,
                @RespRate, @SysBp, @DiaBp, @Spo2, @BloodGlucose, @PainScore, @IntakeMl, @OutputMl, @CaptureSource, @DeviceId,
                @MewsScore, @AutoTriggeredMeasuresJson, @IsExternal, @SourceOrgCode, @ExtDataJson, @SchemaVersion,
                @CreatedAtMs, @CreatedBy, @UpdatedAtMs, @UpdatedBy, @VersionNo)
            """, vital);

        if (mews >= 4 && _alertPublisher != null)
        {
            await _alertPublisher.PublishCriticalValueAsync(vital.EncounterId, "住院患者", "MEWS高危体征预警", $"评分 {mews} 分，体温 {vital.TemperatureC}℃，血压 {vital.SysBp}/{vital.DiaBp}", _session?.ActiveWardId ?? "");
        }

        await RecordAuditAsync("NUR_VITAL_RECORD", nameof(NurVitalSign), vital.Id, $"记录生命体征 (T:{vital.TemperatureC}, BP:{vital.SysBp}/{vital.DiaBp}, MEWS:{mews})", ct);
        return ApiResponse<NurVitalSign>.Success(vital, "生命体征记录成功");
    }

    [HttpPost("vitals/device-import")]
    public async Task<ApiResponse<int>> ImportDeviceVitalSigns([FromBody] List<VitalSignInputDto> batch, CancellationToken ct = default)
    {
        if (batch == null || batch.Count == 0) return ApiResponse<int>.Fail(400, "批量体征数据为空");
        int count = 0;
        foreach (var item in batch)
        {
            var res = await RecordVitalSigns(item, ct);
            if (res.Code == 200) count++;
        }
        return ApiResponse<int>.Success(count, $"成功导入 {count} 条设备/移动护理生命体征记录");
    }

    [HttpGet("vitals/{encounterId}")]
    public async Task<ApiResponse<IReadOnlyList<NurVitalSign>>> GetVitalSigns(string encounterId)
    {
        using var conn = _connFactory.CreateConnection();
        var list = (await conn.QueryAsync<NurVitalSign>(
            "SELECT * FROM nur_vital_signs WHERE encounter_id = @EncounterId ORDER BY measured_at_ms ASC",
            new { EncounterId = encounterId })).ToList();
        return ApiResponse<IReadOnlyList<NurVitalSign>>.Success(list);
    }

    public sealed record VitalSignsSheetDto(
        string EncounterId,
        string PatientName,
        string Gender,
        int Age,
        string BedCode,
        string AdmissionNo,
        string DeptName,
        int WeekIndex,
        string StartDateStr,
        string EndDateStr,
        IReadOnlyList<VitalDayColumnDto> Days
    );

    public sealed record VitalDayColumnDto(
        string DateStr,
        int HospitalDay,
        int SurgeryDay,
        IReadOnlyList<VitalSlotDto> Slots,
        string RespirationListStr,
        string BloodPressureStr,
        decimal TotalIntakeMl,
        decimal TotalOutputMl,
        decimal UrineMl,
        string StoolCount,
        string WeightKg,
        string SkinTest,
        string SpecialEvent
    );

    public sealed record VitalSlotDto(
        int Hour,
        decimal? Temperature,
        string TempType,
        int? Pulse,
        int? HeartRate,
        decimal? CoolingTemp,
        int IsPulseDeficit
    );

    [HttpGet("vitals/sheet/{encounterId}")]
    public async Task<ApiResponse<VitalSignsSheetDto>> GetVitalSignsSheet(string encounterId, [FromQuery] int weekIndex = 1)
    {
        if (weekIndex <= 0) weekIndex = 1;
        using var conn = _connFactory.CreateConnection();
        var enc = await conn.QuerySingleOrDefaultAsync<dynamic>(@"
            SELECT e.id, e.admission_no, e.bed_code, e.created_at_ms,
                   p.full_name AS patient_name, p.gender_code, p.birth_date_int,
                   d.dept_name
            FROM ipd_encounters e
            LEFT JOIN pat_patients p ON p.id = e.patient_id
            LEFT JOIN sys_departments d ON d.id = e.admit_dept_id
            WHERE e.id = @Eid", new { Eid = encounterId });

        if (enc == null)
            return ApiResponse<VitalSignsSheetDto>.Fail(404, "住院就诊记录不存在");

        long admitTimeMs = enc.created_at_ms ?? SystemTime.UtcNowMs;
        var admitDate = DateTimeOffset.FromUnixTimeMilliseconds(admitTimeMs).ToOffset(TimeSpan.FromHours(8)).Date;
        var weekStartDate = admitDate.AddDays((weekIndex - 1) * 7);
        var weekEndDate = weekStartDate.AddDays(7);

        long startMs = new DateTimeOffset(weekStartDate, TimeSpan.FromHours(8)).ToUnixTimeMilliseconds();
        long endMs = new DateTimeOffset(weekEndDate, TimeSpan.FromHours(8)).ToUnixTimeMilliseconds();

        var vitals = (await conn.QueryAsync<NurVitalSign>(@"
            SELECT * FROM nur_vital_signs
            WHERE encounter_id = @Eid AND measured_at_ms >= @Start AND measured_at_ms < @End
            ORDER BY measured_at_ms ASC",
            new { Eid = encounterId, Start = startMs, End = endMs })).ToList();

        var days = new List<VitalDayColumnDto>();
        var standardHours = new[] { 2, 6, 10, 14, 18, 22 };

        for (int i = 0; i < 7; i++)
        {
            var curDate = weekStartDate.AddDays(i);
            int hospitalDay = (int)(curDate - admitDate).TotalDays + 1;
            long dayStartMs = new DateTimeOffset(curDate, TimeSpan.FromHours(8)).ToUnixTimeMilliseconds();
            long dayEndMs = new DateTimeOffset(curDate.AddDays(1), TimeSpan.FromHours(8)).ToUnixTimeMilliseconds();

            var dayVitals = vitals.Where(v => v.MeasuredAtMs >= dayStartMs && v.MeasuredAtMs < dayEndMs).ToList();

            var slots = new List<VitalSlotDto>();
            foreach (int h in standardHours)
            {
                var matched = dayVitals.FirstOrDefault(v =>
                {
                    var dt = DateTimeOffset.FromUnixTimeMilliseconds(v.MeasuredAtMs).ToOffset(TimeSpan.FromHours(8));
                    return Math.Abs(dt.Hour - h) <= 2;
                });

                if (matched != null)
                {
                    int isPulseDeficit = (matched.PulseBpm > 0 && matched.HeartRateBpm > 0 && matched.PulseBpm < matched.HeartRateBpm) ? 1 : 0;
                    slots.Add(new VitalSlotDto(
                        Hour: h,
                        Temperature: matched.TemperatureC > 0 ? matched.TemperatureC : null,
                        TempType: "AXILLARY",
                        Pulse: matched.PulseBpm > 0 ? matched.PulseBpm : null,
                        HeartRate: matched.HeartRateBpm > 0 ? matched.HeartRateBpm : null,
                        CoolingTemp: null,
                        IsPulseDeficit: isPulseDeficit
                    ));
                }
                else
                {
                    slots.Add(new VitalSlotDto(h, null, "AXILLARY", null, null, null, 0));
                }
            }

            var bpVital = dayVitals.FirstOrDefault(v => v.SysBp > 0 && v.DiaBp > 0);
            string bpStr = bpVital != null ? $"{bpVital.SysBp}/{bpVital.DiaBp}" : "-";
            var respVital = dayVitals.FirstOrDefault(v => v.RespRate > 0);
            string respStr = respVital != null ? $"{respVital.RespRate}" : "18";

            days.Add(new VitalDayColumnDto(
                DateStr: curDate.ToString("yyyy-MM-dd"),
                HospitalDay: hospitalDay,
                SurgeryDay: 0,
                Slots: slots,
                RespirationListStr: respStr,
                BloodPressureStr: bpStr,
                TotalIntakeMl: 1500m,
                TotalOutputMl: 1400m,
                UrineMl: 1200m,
                StoolCount: "1",
                WeightKg: "65.0",
                SkinTest: "青(-)",
                SpecialEvent: hospitalDay == 1 ? "入院" : ""
            ));
        }

        int birthDate = enc.birth_date_int != null ? (int)enc.birth_date_int : 19800101;
        int age = Math.Max(1, (DateTime.Today.Year * 10000 + DateTime.Today.Month * 100 + DateTime.Today.Day - birthDate) / 10000);

        var sheet = new VitalSignsSheetDto(
            EncounterId: encounterId,
            PatientName: (string?)enc.patient_name ?? "患者",
            Gender: (string?)enc.gender_code ?? "男",
            Age: age,
            BedCode: (string?)enc.bed_code ?? "-",
            AdmissionNo: (string?)enc.admission_no ?? "-",
            DeptName: (string?)enc.dept_name ?? "病区",
            WeekIndex: weekIndex,
            StartDateStr: weekStartDate.ToString("yyyy-MM-dd"),
            EndDateStr: weekEndDate.AddDays(-1).ToString("yyyy-MM-dd"),
            Days: days
        );

        return ApiResponse<VitalSignsSheetDto>.Success(sheet);
    }

    [HttpPost("records")]
    public async Task<ApiResponse<NurNursingRecord>> SaveNursingRecord([FromBody] NursingRecordInputDto req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.EncounterId) || string.IsNullOrWhiteSpace(req.PatientId))
            return ApiResponse<NurNursingRecord>.Fail(400, "就诊ID与患者ID不能为空");

        var record = new NurNursingRecord
        {
            EncounterId = req.EncounterId,
            PatientId = req.PatientId,
            RecordType = string.IsNullOrWhiteSpace(req.RecordType) ? "GENERAL" : req.RecordType,
            RecordedAtMs = req.RecordedAtMs > 0 ? req.RecordedAtMs : SystemTime.UtcNowMs,
            VitalSignId = req.VitalSignId,
            CpwStageId = req.CpwStageId,
            ContentJson = req.ContentJson,
            NursingActionsJson = req.NursingActionsJson,
            GeneratedFeeItemIdsJson = req.GeneratedFeeItemIdsJson,
            NurseId = CurrentUserId,
            CreatedBy = CurrentUserId,
            UpdatedBy = CurrentUserId
        };

        if (_signatureContract != null && _session != null)
        {
            var sig = await _signatureContract.SignClinicalRecordAsync("NURSING", record.Id, "SIGN", record, _session, ct);
            record.CaSignatureId = sig.Id;
        }

        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(
            """
            INSERT INTO nur_nursing_records (id, encounter_id, patient_id, record_type, recorded_at_ms, vital_sign_id,
                cpw_stage_id, content_json, nursing_actions_json, generated_fee_item_ids_json, nurse_id, ca_signature_id,
                ext_data_json, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @EncounterId, @PatientId, @RecordType, @RecordedAtMs, @VitalSignId,
                @CpwStageId, @ContentJson, @NursingActionsJson, @GeneratedFeeItemIdsJson, @NurseId, @CaSignatureId,
                @ExtDataJson, @SchemaVersion, @CreatedAtMs, @CreatedBy, @UpdatedAtMs, @UpdatedBy, @VersionNo)
            """, record);

        await RecordAuditAsync("NUR_RECORD_SAVE", nameof(NurNursingRecord), record.Id, $"保存护理记录单 ({record.RecordType})", ct);
        return ApiResponse<NurNursingRecord>.Success(record, "护理记录保存成功");
    }

    [HttpGet("records/{encounterId}")]
    public async Task<ApiResponse<IReadOnlyList<NurNursingRecord>>> GetNursingRecords(string encounterId)
    {
        using var conn = _connFactory.CreateConnection();
        var list = (await conn.QueryAsync<NurNursingRecord>(
            "SELECT * FROM nur_nursing_records WHERE encounter_id = @EncounterId ORDER BY recorded_at_ms DESC",
            new { EncounterId = encounterId })).ToList();
        return ApiResponse<IReadOnlyList<NurNursingRecord>>.Success(list);
    }

    [HttpPost("activities/track")]
    public async Task<ApiResponse<IpdPatientActivityTrack>> TrackPatientActivity([FromBody] PatientActivityInputDto req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.EncounterId) || string.IsNullOrWhiteSpace(req.PatientId))
            return ApiResponse<IpdPatientActivityTrack>.Fail(400, "就诊ID与患者ID不能为空");

        var track = new IpdPatientActivityTrack
        {
            EncounterId = req.EncounterId,
            PatientId = req.PatientId,
            ActivityType = string.IsNullOrWhiteSpace(req.ActivityType) ? "ADMIT" : req.ActivityType,
            FromLocation = req.FromLocation ?? string.Empty,
            ToLocation = req.ToLocation ?? string.Empty,
            ScanBarcode = req.ScanBarcode,
            HandoverFromUser = req.HandoverFromUser ?? CurrentUserId,
            HandoverToUser = req.HandoverToUser,
            OccurredAtMs = req.OccurredAtMs > 0 ? req.OccurredAtMs : SystemTime.UtcNowMs,
            CreatedBy = CurrentUserId,
            UpdatedBy = CurrentUserId
        };

        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(
            """
            INSERT INTO ipd_patient_activity_tracks (id, encounter_id, patient_id, activity_type, from_location, to_location,
                scan_barcode, handover_from_user, handover_to_user, occurred_at_ms, ext_data_json, schema_version,
                created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @EncounterId, @PatientId, @ActivityType, @FromLocation, @ToLocation,
                @ScanBarcode, @HandoverFromUser, @HandoverToUser, @OccurredAtMs, @ExtDataJson, @SchemaVersion,
                @CreatedAtMs, @CreatedBy, @UpdatedAtMs, @UpdatedBy, @VersionNo)
            """, track);

        await RecordAuditAsync("PATIENT_ACTIVITY_TRACK", nameof(IpdPatientActivityTrack), track.Id, $"患者活动追踪: {track.ActivityType} 从 {track.FromLocation} 至 {track.ToLocation}", ct);
        return ApiResponse<IpdPatientActivityTrack>.Success(track, "患者院内活动轨迹已记录");
    }

    [HttpGet("activities/{encounterId}")]
    public async Task<ApiResponse<IReadOnlyList<IpdPatientActivityTrack>>> GetPatientActivities(string encounterId)
    {
        using var conn = _connFactory.CreateConnection();
        var list = (await conn.QueryAsync<IpdPatientActivityTrack>(
            "SELECT * FROM ipd_patient_activity_tracks WHERE encounter_id = @EncounterId ORDER BY occurred_at_ms DESC",
            new { EncounterId = encounterId })).ToList();
        return ApiResponse<IReadOnlyList<IpdPatientActivityTrack>>.Success(list);
    }

    [HttpPost("admission-assessments")]
    public async Task<ApiResponse<NurAdmissionAssessment>> SaveAdmissionAssessment([FromBody] AdmissionAssessmentInputDto req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.EncounterId) || string.IsNullOrWhiteSpace(req.PatientId))
            return ApiResponse<NurAdmissionAssessment>.Fail(400, "就诊ID与患者ID不能为空");

        var item = new NurAdmissionAssessment
        {
            EncounterId = req.EncounterId,
            PatientId = req.PatientId,
            TemplateId = req.TemplateId ?? "GENERAL_ADMISSION",
            AssessmentJson = req.AssessmentJson,
            AutoRiskSummaryJson = req.AutoRiskSummaryJson,
            ReferencedHistoryIdsJson = req.ReferencedHistoryIdsJson,
            AssessorId = CurrentUserId,
            AssessedAtMs = req.AssessedAtMs > 0 ? req.AssessedAtMs : SystemTime.UtcNowMs,
            CreatedBy = CurrentUserId,
            UpdatedBy = CurrentUserId
        };

        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(
            """
            INSERT INTO nur_admission_assessments (id, encounter_id, patient_id, template_id, assessment_json,
                auto_risk_summary_json, referenced_history_ids_json, assessor_id, assessed_at_ms, ext_data_json,
                schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @EncounterId, @PatientId, @TemplateId, @AssessmentJson,
                @AutoRiskSummaryJson, @ReferencedHistoryIdsJson, @AssessorId, @AssessedAtMs, @ExtDataJson,
                @SchemaVersion, @CreatedAtMs, @CreatedBy, @UpdatedAtMs, @UpdatedBy, @VersionNo)
            """, item);

        await RecordAuditAsync("NUR_ADMISSION_ASSESSMENT", nameof(NurAdmissionAssessment), item.Id, "保存入院护理评估表", ct);
        return ApiResponse<NurAdmissionAssessment>.Success(item, "入院护理评估已保存");
    }

    [HttpGet("admission-assessments/{encounterId}")]
    public async Task<ApiResponse<IReadOnlyList<NurAdmissionAssessment>>> GetAdmissionAssessments(string encounterId)
    {
        using var conn = _connFactory.CreateConnection();
        var list = (await conn.QueryAsync<NurAdmissionAssessment>(
            "SELECT * FROM nur_admission_assessments WHERE encounter_id = @EncounterId ORDER BY assessed_at_ms DESC",
            new { EncounterId = encounterId })).ToList();
        return ApiResponse<IReadOnlyList<NurAdmissionAssessment>>.Success(list);
    }
}

public sealed record VitalSignInputDto(
    string EncounterId,
    string PatientId,
    decimal TemperatureC,
    int PulseBpm,
    int HeartRateBpm,
    int RespRate,
    int SysBp,
    int DiaBp,
    decimal Spo2,
    decimal BloodGlucose,
    int PainScore,
    decimal IntakeMl,
    decimal OutputMl,
    string? CaptureSource = "MANUAL",
    string? DeviceId = null,
    long MeasuredAtMs = 0);

public sealed record NursingRecordInputDto(
    string EncounterId,
    string PatientId,
    string? RecordType,
    long RecordedAtMs,
    string? VitalSignId,
    string? CpwStageId,
    string? ContentJson,
    string? NursingActionsJson,
    string? GeneratedFeeItemIdsJson);

public sealed record PatientActivityInputDto(
    string EncounterId,
    string PatientId,
    string? ActivityType,
    string? FromLocation,
    string? ToLocation,
    string? ScanBarcode,
    string? HandoverFromUser,
    string? HandoverToUser,
    long OccurredAtMs);

public sealed record AdmissionAssessmentInputDto(
    string EncounterId,
    string PatientId,
    string? TemplateId,
    string? AssessmentJson,
    string? AutoRiskSummaryJson,
    string? ReferencedHistoryIdsJson,
    long AssessedAtMs);

