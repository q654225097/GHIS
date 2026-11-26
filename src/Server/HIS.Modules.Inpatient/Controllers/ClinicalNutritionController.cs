using HIS.BuildingBlocks.Security;
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

namespace HIS.Modules.Inpatient.Controllers;

/// <summary>
/// 临床营养筛查 NRS-2002 与营养膳食配餐中心：
/// 1) 入院 24h NRS-2002 营养风险筛查（疾病严重度 0~3 + 营养受损 0~3 + 年龄>=70 加 1 分；总分>=3 自动高风险并触发会诊建议）
/// 2) 治疗膳食与肠内营养 (EN) 配餐医嘱管理（糖尿病餐/低盐低脂/肾病餐/流质/肠内营养制剂）
/// 3) 营养科配餐汇总看板与送餐闭环
/// </summary>
[ApiController]
[RequireHisRole("ADMIN", "IPD_ATTENDING", "IPD_RESIDENT", "WARD_NURSE")]
[Route("api/v4/nutrition")]
public sealed class ClinicalNutritionController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _session;

    public ClinicalNutritionController(IDbConnectionFactory connFactory, IUserSession? session = null)
    {
        _connFactory = connFactory;
        _session = session;
    }

    private string ActorName => _session?.DisplayName ?? _session?.UserName ?? _session?.UserId ?? "SYSTEM";

    public sealed record Nrs2002Dto(
        string EncounterId,
        string PatientId,
        string PatientName,
        decimal? Bmi,
        int DiseaseSeverityScore,
        int NutritionalImpairedScore,
        int AgeYears,
        string? Note);

    public sealed record CreateDietOrderDto(
        string EncounterId,
        string PatientId,
        string PatientName,
        string? WardName,
        string? BedCode,
        string DietType,
        int KcalTarget,
        int ProteinGrams,
        string? MealSchedule,
        string? AllergyAvoidance);

    public sealed record UpdateDietStatusDto(string Status);

    [HttpGet("nrs2002")]
    public async Task<ApiResponse<object>> ListScreenings([FromQuery] string? riskLevel = null, CancellationToken ct = default)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var sql = "SELECT * FROM nut_nrs2002_screenings WHERE 1=1";
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(riskLevel))
        {
            sql += " AND risk_level = @Risk";
            p.Add("Risk", riskLevel.Trim().ToUpperInvariant());
        }
        sql += " ORDER BY screened_at_ms DESC LIMIT 200";
        return ApiResponse<object>.Success(await conn.QueryAsync(sql, p));
    }

    [HttpPost("nrs2002")]
    public async Task<ApiResponse<object>> SubmitNrs2002([FromBody] Nrs2002Dto dto, CancellationToken ct)
    {
        if (dto.DiseaseSeverityScore is < 0 or > 3 || dto.NutritionalImpairedScore is < 0 or > 3)
            return ApiResponse<object>.Fail(400, "NRS-2002 疾病严重度与营养受损评分必须在 0~3 分之间。");
        if (string.IsNullOrWhiteSpace(dto.EncounterId) || string.IsNullOrWhiteSpace(dto.PatientId))
            return ApiResponse<object>.Fail(400, "住院就诊ID与患者ID不能为空。");

        int ageScore = dto.AgeYears >= 70 ? 1 : 0;
        int total = dto.DiseaseSeverityScore + dto.NutritionalImpairedScore + ageScore;
        bool isHighRisk = total >= 3;
        string riskLevel = isHighRisk ? "HIGH_RISK" : "LOW_RISK";
        string intervention = total >= 5 ? "PN_OR_EN_CONSULT" : isHighRisk ? "EN_NUTRITION_CONSULT" : "ORAL_DIET";

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var id = Guid.NewGuid().ToString("N");

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        await conn.ExecuteAsync(@"
INSERT INTO nut_nrs2002_screenings (
    id, encounter_id, patient_id, patient_name, bmi,
    disease_severity_score, nutritional_impaired_score, age_ge_70_score, total_score,
    risk_level, trigger_consult, intervention_type, screener_name, screened_at_ms, note,
    created_at_ms, updated_at_ms
) VALUES (
    @Id, @EncId, @PatId, @PatName, @Bmi,
    @DisScore, @NutScore, @AgeScore, @Total,
    @Risk, @Trigger, @Intervention, @Screener, @Now, @Note,
    @Now, @Now
);", new
        {
            Id = id,
            EncId = dto.EncounterId.Trim(),
            PatId = dto.PatientId.Trim(),
            PatName = dto.PatientName.Trim(),
            Bmi = dto.Bmi,
            DisScore = dto.DiseaseSeverityScore,
            NutScore = dto.NutritionalImpairedScore,
            AgeScore = ageScore,
            Total = total,
            Risk = riskLevel,
            Trigger = isHighRisk ? 1 : 0,
            Intervention = intervention,
            Screener = ActorName,
            Now = now,
            Note = dto.Note
        });

        return ApiResponse<object>.Success(new
        {
            id,
            totalScore = total,
            riskLevel,
            triggerConsult = isHighRisk,
            recommendedIntervention = intervention
        });
    }

    [HttpGet("diet-orders")]
    public async Task<ApiResponse<object>> ListDietOrders([FromQuery] string? status = null, CancellationToken ct = default)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var sql = "SELECT * FROM nut_diet_orders WHERE 1=1";
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(status))
        {
            sql += " AND status = @Status";
            p.Add("Status", status.Trim().ToUpperInvariant());
        }
        sql += " ORDER BY created_at_ms DESC LIMIT 200";
        return ApiResponse<object>.Success(await conn.QueryAsync(sql, p));
    }

    [HttpPost("diet-orders")]
    public async Task<ApiResponse<object>> CreateDietOrder([FromBody] CreateDietOrderDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.EncounterId) || string.IsNullOrWhiteSpace(dto.DietType))
            return ApiResponse<object>.Fail(400, "住院就诊ID与膳食类型不能为空。");

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var id = Guid.NewGuid().ToString("N");
        var dietNo = $"NUT{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(10, 99)}";

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);

        string dietKey = dto.DietType.Trim().ToUpperInvariant();
        var nutMdm = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT item_code AS ItemCode, kcal_per_unit AS KcalPerUnit, is_active AS IsActive FROM mdm_nutrition_items WHERE UPPER(item_code) = @Key OR UPPER(item_name) = @Key OR UPPER(nutrition_type) = @Key LIMIT 1",
            new { Key = dietKey });
        if (nutMdm != null && Convert.ToInt32(nutMdm.IsActive) == 0)
            return ApiResponse<object>.Fail(400, $"营养制剂主数据 [{dto.DietType}] 已停用，禁止开立膳食医嘱！");

        decimal resolvedKcal = dto.KcalTarget > 0 ? dto.KcalTarget
            : (nutMdm != null && Convert.ToDecimal(nutMdm.KcalPerUnit) > 0 ? Convert.ToDecimal(nutMdm.KcalPerUnit) : 1800m);

        await conn.ExecuteAsync(@"
INSERT INTO nut_diet_orders (
    id, diet_no, encounter_id, patient_id, patient_name, ward_name, bed_code,
    diet_type, kcal_target, protein_grams, meal_schedule, allergy_avoidance,
    status, ordered_by, created_at_ms, updated_at_ms
) VALUES (
    @Id, @DietNo, @EncId, @PatId, @PatName, @Ward, @Bed,
    @DietType, @Kcal, @Protein, @Schedule, @Allergy,
    'ACTIVE', @OrderedBy, @Now, @Now
);", new
        {
            Id = id,
            DietNo = dietNo,
            EncId = dto.EncounterId.Trim(),
            PatId = dto.PatientId.Trim(),
            PatName = dto.PatientName.Trim(),
            Ward = dto.WardName ?? "住院病区",
            Bed = dto.BedCode ?? "-",
            DietType = dietKey,
            Kcal = resolvedKcal,
            Protein = dto.ProteinGrams <= 0 ? 65 : dto.ProteinGrams,
            Schedule = string.IsNullOrWhiteSpace(dto.MealSchedule) ? "早/中/晚" : dto.MealSchedule.Trim(),
            Allergy = dto.AllergyAvoidance,
            OrderedBy = ActorName,
            Now = now
        });

        return ApiResponse<object>.Success(new { id, dietNo, status = "ACTIVE" });
    }

    [HttpPost("diet-orders/{id}/status")]
    public async Task<ApiResponse<object>> UpdateDietStatus(string id, [FromBody] UpdateDietStatusDto dto, CancellationToken ct)
    {
        var st = (dto.Status ?? string.Empty).Trim().ToUpperInvariant();
        if (st is not ("PREPARING" or "DELIVERED" or "STOPPED"))
            return ApiResponse<object>.Fail(400, "状态仅支持 PREPARING/DELIVERED/STOPPED。");

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        int affected = await conn.ExecuteAsync(@"
UPDATE nut_diet_orders
SET status = @Status,
    delivered_at_ms = CASE WHEN @Status = 'DELIVERED' THEN @Now ELSE delivered_at_ms END,
    updated_at_ms = @Now
WHERE id = @Id;", new { Status = st, Now = now, Id = id });

        if (affected == 0) return ApiResponse<object>.Fail(404, "营养配餐单不存在。");
        return ApiResponse<object>.Success(new { id, status = st });
    }

    [HttpGet("kitchen-summary")]
    public async Task<ApiResponse<object>> GetKitchenSummary(CancellationToken ct)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var summary = await conn.QueryAsync(@"
SELECT ward_name, diet_type, COUNT(1) AS meal_count, AVG(kcal_target) AS avg_kcal
FROM nut_diet_orders
WHERE status IN ('ACTIVE', 'PREPARING')
GROUP BY ward_name, diet_type
ORDER BY ward_name, diet_type;");
        return ApiResponse<object>.Success(summary);
    }
}
