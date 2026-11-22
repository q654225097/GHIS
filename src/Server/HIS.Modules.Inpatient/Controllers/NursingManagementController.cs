using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Inpatient.Controllers;

/// <summary>
/// 护理管理中心 (Nursing Management)：
/// 1) 护士能级(N0-N4)档案、专科资质认证与三基三严技能考核晋级
/// 2) 病区智能约束校验排班（拦截大夜接白班、连续夜班超限）+ 基于在院患者护理级别的实时 Acuity 护患比人力缺口测算
/// 3) 护理部三级质控督查与 PDCA 整改闭环
/// 4) 病区结构化 SBAR 床旁交接班（交班/接班双人异人签收）
/// </summary>
[ApiController]
[Route("api/v4/nursing-mgmt")]
public sealed class NursingManagementController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _session;

    public NursingManagementController(IDbConnectionFactory connFactory, IUserSession? session = null)
    {
        _connFactory = connFactory;
        _session = session;
    }

    private string ActorId => _session?.UserId?.Trim() ?? "SYSTEM";
    private string ActorName => _session?.DisplayName ?? _session?.UserName ?? ActorId;

    public sealed record SaveCompetencyDto(string NurseUserId, string NurseName, string WardName, string NLevel, string? SpecialtyCerts, decimal TheoryScore, decimal SkillScore, decimal CmeCredits);
    public sealed record ScheduleShiftDto(string WardName, int ShiftDateInt, string NurseUserId, string NurseName, string NLevel, string ShiftCode, decimal ShiftHours, string? Remark);
    public sealed record CreateQcDto(string WardName, string InspectLevel, decimal Score, string? DefectSummary, string? RectificationPlan);
    public sealed record RectifyQcDto(string RectificationPlan, string Status);
    public sealed record CreateHandoverDto(string WardName, string FromShiftCode, string ToShiftCode, int TotalPatients, int CriticalPatients, int SurgeryPatients, int HighRiskFallUlcer, string SbarSummary);
    public sealed record AckHandoverDto(string ToNurseName);

    // ===================== 1. 护士能级 (N0-N4) 档案与晋级 =====================

    [HttpGet("competencies")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetCompetencies()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT id AS Id, nurse_user_id AS NurseUserId, nurse_name AS NurseName, ward_name AS WardName,
                   n_level AS NLevel, specialty_certs AS SpecialtyCerts, theory_score AS TheoryScore,
                   skill_score AS SkillScore, cme_credits AS CmeCredits, status AS Status
            FROM nur_staff_competencies ORDER BY ward_name ASC, n_level DESC LIMIT 200")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("competencies")]
    public async Task<ApiResponse<object>> SaveCompetency([FromBody] SaveCompetencyDto req)
    {
        if (string.IsNullOrWhiteSpace(req.NurseUserId) || string.IsNullOrWhiteSpace(req.NurseName))
            return ApiResponse<object>.Fail(400, "护士工号与姓名不能为空");

        long now = SystemTime.UtcNowMs;
        using var conn = _connFactory.CreateConnection();
        string? existingId = await conn.QueryFirstOrDefaultAsync<string?>(
            "SELECT id FROM nur_staff_competencies WHERE nurse_user_id = @Uid LIMIT 1", new { Uid = req.NurseUserId.Trim() });

        if (!string.IsNullOrWhiteSpace(existingId))
        {
            await conn.ExecuteAsync(@"
                UPDATE nur_staff_competencies
                SET nurse_name = @Name, ward_name = @Ward, n_level = @Lvl, specialty_certs = @Certs,
                    theory_score = @Th, skill_score = @Sk, cme_credits = @Cme, updated_at_ms = @Now, updated_by = @Actor
                WHERE id = @Id",
                new { Name = req.NurseName.Trim(), Ward = req.WardName.Trim(), Lvl = req.NLevel.Trim().ToUpperInvariant(), Certs = req.SpecialtyCerts?.Trim(), Th = req.TheoryScore, Sk = req.SkillScore, Cme = req.CmeCredits, Now = now, Actor = ActorId, Id = existingId });
            return ApiResponse<object>.Success(new { Id = existingId }, $"已更新护士 {req.NurseName} ({req.NLevel}) 档案");
        }

        string id = Guid.NewGuid().ToString("D");
        await conn.ExecuteAsync(@"
            INSERT INTO nur_staff_competencies
            (id, nurse_user_id, nurse_name, ward_name, n_level, specialty_certs, theory_score, skill_score, cme_credits, status,
             schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @Uid, @Name, @Ward, @Lvl, @Certs, @Th, @Sk, @Cme, 'ACTIVE', 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, Uid = req.NurseUserId.Trim(), Name = req.NurseName.Trim(), Ward = req.WardName.Trim(), Lvl = req.NLevel.Trim().ToUpperInvariant(), Certs = req.SpecialtyCerts?.Trim(), Th = req.TheoryScore, Sk = req.SkillScore, Cme = req.CmeCredits, Now = now, Actor = ActorId });
        return ApiResponse<object>.Success(new { Id = id }, $"已建档护士 {req.NurseName} ({req.NLevel})");
    }

    // ===================== 2. 病区排班（硬约束校验）+ Acuity 护患比测算 =====================

    [HttpGet("shifts")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetShifts([FromQuery] string? wardName = null)
    {
        using var conn = _connFactory.CreateConnection();
        var sql = @"SELECT id AS Id, ward_name AS WardName, shift_date_int AS ShiftDateInt, nurse_user_id AS NurseUserId,
                           nurse_name AS NurseName, n_level AS NLevel, shift_code AS ShiftCode,
                           shift_hours AS ShiftHours, remark AS Remark
                    FROM nur_ward_shifts WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(wardName) && wardName != "ALL") sql += " AND ward_name = @Ward";
        sql += " ORDER BY shift_date_int DESC, shift_code ASC LIMIT 200";
        var rows = (await conn.QueryAsync<dynamic>(sql, new { Ward = wardName?.Trim() })).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("shifts")]
    public async Task<ApiResponse<object>> ScheduleShift([FromBody] ScheduleShiftDto req)
    {
        if (string.IsNullOrWhiteSpace(req.NurseUserId) || req.ShiftDateInt < 20200101)
            return ApiResponse<object>.Fail(400, "护士工号与排班日期不能为空");

        string code = req.ShiftCode.Trim().ToUpperInvariant();
        DateTime dt = new DateTime(req.ShiftDateInt / 10000, (req.ShiftDateInt / 100) % 100, req.ShiftDateInt % 100);
        int prev1 = int.Parse(dt.AddDays(-1).ToString("yyyyMMdd"));
        int prev2 = int.Parse(dt.AddDays(-2).ToString("yyyyMMdd"));

        using var conn = _connFactory.CreateConnection();
        // 约束 1：同日不可重复排班
        int sameDay = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM nur_ward_shifts WHERE nurse_user_id = @Uid AND shift_date_int = @Dt",
            new { Uid = req.NurseUserId.Trim(), Dt = req.ShiftDateInt });
        if (sameDay > 0)
            return ApiResponse<object>.Fail(400, $"排班约束阻断：{req.NurseName} 在 {req.ShiftDateInt} 已有排班记录");

        string? prev1Shift = await conn.QueryFirstOrDefaultAsync<string?>(
            "SELECT shift_code FROM nur_ward_shifts WHERE nurse_user_id = @Uid AND shift_date_int = @Dt LIMIT 1",
            new { Uid = req.NurseUserId.Trim(), Dt = prev1 });
        string? prev2Shift = await conn.QueryFirstOrDefaultAsync<string?>(
            "SELECT shift_code FROM nur_ward_shifts WHERE nurse_user_id = @Uid AND shift_date_int = @Dt LIMIT 1",
            new { Uid = req.NurseUserId.Trim(), Dt = prev2 });

        // 约束 2：禁止前一日大夜班 (NIGHT) 次日直接接白班 (DAY)
        if (prev1Shift == "NIGHT" && code == "DAY")
            return ApiResponse<object>.Fail(400, $"排班约束阻断：{req.NurseName} 前一日为大夜班(NIGHT)，次日严禁直接排白班(DAY)以防疲劳差错");

        // 约束 3：禁止连续大夜班超过 2 天
        if (code == "NIGHT" && prev1Shift == "NIGHT" && prev2Shift == "NIGHT")
            return ApiResponse<object>.Fail(400, $"排班约束阻断：{req.NurseName} 已连续 2 日大夜班，严禁连续 3 日大夜班");

        long now = SystemTime.UtcNowMs;
        string id = Guid.NewGuid().ToString("D");
        await conn.ExecuteAsync(@"
            INSERT INTO nur_ward_shifts
            (id, ward_name, shift_date_int, nurse_user_id, nurse_name, n_level, shift_code, shift_hours, remark,
             schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @Ward, @Dt, @Uid, @Name, @Lvl, @Code, @Hrs, @Rem, 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, Ward = req.WardName.Trim(), Dt = req.ShiftDateInt, Uid = req.NurseUserId.Trim(), Name = req.NurseName.Trim(), Lvl = req.NLevel, Code = code, Hrs = req.ShiftHours > 0 ? req.ShiftHours : 8.0m, Rem = req.Remark?.Trim(), Now = now, Actor = ActorId });

        return ApiResponse<object>.Success(new { Id = id }, $"已完成 {req.NurseName} 在 {req.ShiftDateInt} 的 [{code}] 排班（通过全部安全约束校验）");
    }

    /// <summary>
    /// 基于真实在院患者护理级别 (Acuity) 自动测算所需护士人数与排班缺口（特级 1:1、一级 1:3、二级 1:6、三级 1:10）
    /// </summary>
    [HttpGet("staffing-acuity")]
    public async Task<ApiResponse<object>> GetStaffingAcuity([FromQuery] int? shiftDateInt = null)
    {
        int targetDate = shiftDateInt is > 20200101 ? shiftDateInt.Value : int.Parse(SystemTime.ShanghaiNow.ToString("yyyyMMdd"));
        using var conn = _connFactory.CreateConnection();
        var levels = (await conn.QueryAsync<string>("SELECT COALESCE(nursing_level_code, 'LEVEL_2') FROM ipd_encounters WHERE status = 1")).ToList();

        int special = levels.Count(l => l.Contains("SPECIAL", StringComparison.OrdinalIgnoreCase) || l.Contains("特级"));
        int lvl1 = levels.Count(l => l.Contains("LEVEL_1", StringComparison.OrdinalIgnoreCase) || l.Contains("一级"));
        int lvl2 = levels.Count(l => l.Contains("LEVEL_2", StringComparison.OrdinalIgnoreCase) || l.Contains("二级"));
        int lvl3 = Math.Max(0, levels.Count - special - lvl1 - lvl2);

        decimal requiredNurses = Math.Ceiling(special / 1m + lvl1 / 3m + lvl2 / 6m + lvl3 / 10m);
        int scheduledNurses = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(DISTINCT nurse_user_id) FROM nur_ward_shifts WHERE shift_date_int = @Dt AND shift_code <> 'OFF'",
            new { Dt = targetDate });
        int gap = (int)requiredNurses - scheduledNurses;

        return ApiResponse<object>.Success(new
        {
            ShiftDateInt = targetDate,
            ActiveInpatients = levels.Count,
            SpecialCareCount = special,
            Level1Count = lvl1,
            Level2Count = lvl2,
            Level3Count = lvl3,
            RequiredNursesByAcuity = (int)requiredNurses,
            ScheduledActiveNurses = scheduledNurses,
            StaffingGap = gap,
            Status = gap > 0 ? $"人力缺口 {gap} 人（建议机动库调配）" : "当前护患比达标"
        });
    }

    // ===================== 3. 三级护理质控督查与 PDCA 整改 =====================

    [HttpGet("qc-inspections")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetQcInspections()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT id AS Id, inspect_no AS InspectNo, ward_name AS WardName, inspect_level AS InspectLevel,
                   inspect_date_int AS InspectDateInt, score AS Score, defect_summary AS DefectSummary,
                   rectification_plan AS RectificationPlan, status AS Status, inspector_name AS InspectorName
            FROM nur_qc_inspections ORDER BY created_at_ms DESC LIMIT 120")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("qc-inspections")]
    public async Task<ApiResponse<object>> CreateQcInspection([FromBody] CreateQcDto req)
    {
        long now = SystemTime.UtcNowMs;
        string id = Guid.NewGuid().ToString("D");
        string no = "NQC-" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss");
        int dt = int.Parse(SystemTime.ShanghaiNow.ToString("yyyyMMdd"));

        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO nur_qc_inspections
            (id, inspect_no, ward_name, inspect_level, inspect_date_int, score, defect_summary, rectification_plan, status, inspector_name,
             schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @No, @Ward, @Lvl, @Dt, @Score, @Def, @Plan, @St, @ActorName, 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, No = no, Ward = req.WardName.Trim(), Lvl = req.InspectLevel, Dt = dt, Score = req.Score, Def = req.DefectSummary?.Trim(), Plan = req.RectificationPlan?.Trim(), St = req.Score >= 95m && string.IsNullOrWhiteSpace(req.DefectSummary) ? "PASSED" : "OPEN", ActorName, Now = now, Actor = ActorId });
        return ApiResponse<object>.Success(new { Id = id, InspectNo = no }, $"三级护理质控记录 {no} 已提交");
    }

    [HttpPut("qc-inspections/{id}/rectify")]
    public async Task<ApiResponse<object>> RectifyQc(string id, [FromBody] RectifyQcDto req)
    {
        using var conn = _connFactory.CreateConnection();
        int n = await conn.ExecuteAsync(
            "UPDATE nur_qc_inspections SET rectification_plan = @Plan, status = @St, updated_at_ms = @Now, updated_by = @Actor WHERE id = @Id",
            new { Plan = req.RectificationPlan?.Trim(), St = req.Status.Trim().ToUpperInvariant(), Now = SystemTime.UtcNowMs, Actor = ActorId, Id = id });
        if (n == 0) return ApiResponse<object>.Fail(404, "未找到该质控记录");
        return ApiResponse<object>.Success(new { Id = id, req.Status }, "已更新护理质控整改状态");
    }

    // ===================== 4. 病区结构化 SBAR 床旁交接班（双人异人签收） =====================

    [HttpGet("handovers")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetHandovers()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT id AS Id, handover_no AS HandoverNo, ward_name AS WardName, shift_date_int AS ShiftDateInt,
                   from_shift_code AS FromShiftCode, to_shift_code AS ToShiftCode,
                   total_patients AS TotalPatients, critical_patients AS CriticalPatients,
                   surgery_patients AS SurgeryPatients, high_risk_fall_ulcer AS HighRiskFallUlcer,
                   sbar_summary AS SbarSummary, from_nurse_name AS FromNurseName,
                   to_nurse_name AS ToNurseName, status AS Status
            FROM nur_shift_handovers ORDER BY created_at_ms DESC LIMIT 120")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("handovers")]
    public async Task<ApiResponse<object>> CreateHandover([FromBody] CreateHandoverDto req)
    {
        if (string.IsNullOrWhiteSpace(req.SbarSummary)) return ApiResponse<object>.Fail(400, "SBAR 交接重点摘要不能为空");
        long now = SystemTime.UtcNowMs;
        string id = Guid.NewGuid().ToString("D");
        string no = "SBAR-" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss");
        int dt = int.Parse(SystemTime.ShanghaiNow.ToString("yyyyMMdd"));

        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO nur_shift_handovers
            (id, handover_no, ward_name, shift_date_int, from_shift_code, to_shift_code, total_patients, critical_patients,
             surgery_patients, high_risk_fall_ulcer, sbar_summary, from_nurse_name, status,
             schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @No, @Ward, @Dt, @From, @To, @Tot, @Crit, @Surg, @Risk, @Sbar, @FromNurse, 'PENDING_ACK', 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, No = no, Ward = req.WardName.Trim(), Dt = dt, From = req.FromShiftCode, To = req.ToShiftCode, Tot = req.TotalPatients, Crit = req.CriticalPatients, Surg = req.SurgeryPatients, Risk = req.HighRiskFallUlcer, Sbar = req.SbarSummary.Trim(), FromNurse = ActorName, Now = now, Actor = ActorId });
        return ApiResponse<object>.Success(new { Id = id, HandoverNo = no }, $"SBAR 交接单 {no} 已发起，待接班护士双签确认");
    }

    [HttpPost("handovers/{id}/ack")]
    public async Task<ApiResponse<object>> AckHandover(string id, [FromBody] AckHandoverDto req)
    {
        if (string.IsNullOrWhiteSpace(req.ToNurseName)) return ApiResponse<object>.Fail(400, "请填写接班护士姓名");
        using var conn = _connFactory.CreateConnection();
        var item = await conn.QueryFirstOrDefaultAsync<dynamic>("SELECT from_nurse_name AS FromNurse, status AS Status FROM nur_shift_handovers WHERE id = @Id", new { Id = id });
        if (item == null) return ApiResponse<object>.Fail(404, "交接单不存在");
        if (string.Equals(req.ToNurseName.Trim(), (string)item.FromNurse, StringComparison.OrdinalIgnoreCase))
            return ApiResponse<object>.Fail(400, "职责分离阻断：接班护士不得与交班护士为同一人");

        long now = SystemTime.UtcNowMs;
        await conn.ExecuteAsync(
            "UPDATE nur_shift_handovers SET to_nurse_name = @To, status = 'ACKED', acked_at_ms = @Now, updated_at_ms = @Now, updated_by = @Actor WHERE id = @Id",
            new { To = req.ToNurseName.Trim(), Now = now, Actor = ActorId, Id = id });
        return ApiResponse<object>.Success(new { Id = id, Status = "ACKED" }, $"已完成 SBAR 床旁交接双签（接班：{req.ToNurseName}）");
    }
}
