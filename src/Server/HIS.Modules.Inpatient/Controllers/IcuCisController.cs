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
/// ICU 重症监护工作站 (CIS)：
/// 1) ICU 收治与 APACHE II / SOFA 自动危重评分及 Logistic 预测病死率计算
/// 2) 小时级重症特护记录（呼吸机模式/FiO2/PEEP/VT、出入量小时净平衡与24h累计、血管活性药滴定速率）
/// 3) 三管院感监测（VAP 呼吸机管道 / CLABSI 中心静脉 / CAUTI 导尿管），确诊感染自动联动写入不良事件台账
/// </summary>
[ApiController]
[RequireHisRole("ADMIN", "IPD_ATTENDING", "IPD_RESIDENT", "WARD_NURSE")]
[Route("api/v4/icu-cis")]
public sealed class IcuCisController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _session;
    private readonly IAuditTrailContract? _auditTrail;

    public IcuCisController(
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

    public sealed record AdmitIcuDto(string EncounterId, string PatientId, string PatientName, string IcuBedCode, string AdmitReason, string? AttendingDoctor);

    public sealed record SeverityScoreDto(
        decimal TempC,
        int MapMmhg,
        int HeartRate,
        int RespRate,
        decimal Pao2Fio2Ratio,
        int Platelets10e9,
        decimal BilirubinMgDl,
        decimal CreatinineMgDl,
        int GcsScore,
        int AgeYears,
        bool HasChronicOrganFailure,
        bool OnVasopressor);

    public sealed record HourlyRecordDto(
        string? VentMode,
        decimal? Fio2Pct,
        decimal? PeepCmh2o,
        int? TidalVolumeMl,
        int? RespRate,
        int? MapMmhg,
        decimal? Spo2Pct,
        decimal? LactateMmol,
        decimal IntakeMl,
        decimal OutputMl,
        string? VasopressorDrug,
        decimal? VasopressorRateMcgKgMin,
        string? Note);

    public sealed record InsertCatheterDto(string CatheterType, string SiteName);
    public sealed record ConfirmCatheterInfectionDto(string InfectionNote);

    [HttpGet("admissions")]
    public async Task<ApiResponse<object>> ListAdmissions([FromQuery] string? status = "IN_ICU", CancellationToken ct = default)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var sql = "SELECT * FROM icu_admissions WHERE 1=1";
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(status))
        {
            sql += " AND status = @Status";
            p.Add("Status", status.Trim().ToUpperInvariant());
        }
        sql += " ORDER BY admitted_at_ms DESC LIMIT 200";
        return ApiResponse<object>.Success(await conn.QueryAsync(sql, p));
    }

    [HttpPost("admissions")]
    public async Task<ApiResponse<object>> AdmitToIcu([FromBody] AdmitIcuDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.EncounterId) || string.IsNullOrWhiteSpace(dto.IcuBedCode))
            return ApiResponse<object>.Fail(400, "住院就诊ID与ICU床位号不能为空。");

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var id = Guid.NewGuid().ToString("N");
        var icuNo = $"ICU{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(10, 99)}";

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        await conn.ExecuteAsync(@"
INSERT INTO icu_admissions (
    id, icu_no, encounter_id, patient_id, patient_name, icu_bed_code, admit_reason,
    admitted_at_ms, apache_ii_score, sofa_score, predicted_mortality_pct, status, attending_doctor,
    schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no
) VALUES (
    @Id, @IcuNo, @EncId, @PatId, @PatName, @Bed, @Reason,
    @Now, 0, 0, 0, 'IN_ICU', @Doctor,
    1, @Now, @Actor, @Now, @Actor, 1
);", new
        {
            Id = id,
            IcuNo = icuNo,
            EncId = dto.EncounterId.Trim(),
            PatId = dto.PatientId.Trim(),
            PatName = dto.PatientName.Trim(),
            Bed = dto.IcuBedCode.Trim(),
            Reason = dto.AdmitReason,
            Doctor = dto.AttendingDoctor ?? ActorName,
            Now = now,
            Actor = ActorId
        });

        return ApiResponse<object>.Success(new { id, icuNo, status = "IN_ICU" });
    }

    [HttpPost("admissions/{icuAdmissionId}/severity-score")]
    public async Task<ApiResponse<object>> ComputeSeverityScores(string icuAdmissionId, [FromBody] SeverityScoreDto dto, CancellationToken ct)
    {
        if (dto.GcsScore < 3 || dto.GcsScore > 15)
            return ApiResponse<object>.Fail(400, "GCS 评分必须在 3~15 之间。");

        // 1) 标准 SOFA (0~24分): 呼吸/凝血/肝脏/心血管/神经/肾脏 6 大系统
        int sofaResp = dto.Pao2Fio2Ratio < 100 ? 4 : dto.Pao2Fio2Ratio < 200 ? 3 : dto.Pao2Fio2Ratio < 300 ? 2 : dto.Pao2Fio2Ratio < 400 ? 1 : 0;
        int sofaCoag = dto.Platelets10e9 < 20 ? 4 : dto.Platelets10e9 < 50 ? 3 : dto.Platelets10e9 < 100 ? 2 : dto.Platelets10e9 < 150 ? 1 : 0;
        int sofaLiver = dto.BilirubinMgDl >= 12.0m ? 4 : dto.BilirubinMgDl >= 6.0m ? 3 : dto.BilirubinMgDl >= 2.0m ? 2 : dto.BilirubinMgDl >= 1.2m ? 1 : 0;
        int sofaCv = dto.OnVasopressor ? 3 : (dto.MapMmhg < 70 ? 1 : 0);
        int sofaCns = dto.GcsScore < 6 ? 4 : dto.GcsScore <= 9 ? 3 : dto.GcsScore <= 12 ? 2 : dto.GcsScore <= 14 ? 1 : 0;
        int sofaRenal = dto.CreatinineMgDl >= 5.0m ? 4 : dto.CreatinineMgDl >= 3.5m ? 3 : dto.CreatinineMgDl >= 2.0m ? 2 : dto.CreatinineMgDl >= 1.2m ? 1 : 0;
        int sofa = sofaResp + sofaCoag + sofaLiver + sofaCv + sofaCns + sofaRenal;

        // 2) 标准 APACHE II (APS + 年龄分 + 慢性健康分)
        int aps = 0;
        aps += (dto.TempC >= 41m || dto.TempC <= 29.9m) ? 4 : (dto.TempC >= 39m || dto.TempC <= 31.9m) ? 3 : (dto.TempC <= 33.9m) ? 2 : (dto.TempC >= 38.5m || dto.TempC <= 35.9m) ? 1 : 0;
        aps += (dto.MapMmhg >= 160 || dto.MapMmhg <= 49) ? 4 : (dto.MapMmhg >= 130) ? 3 : (dto.MapMmhg >= 110 || dto.MapMmhg <= 69) ? 2 : 0;
        aps += (dto.HeartRate >= 180 || dto.HeartRate <= 39) ? 4 : (dto.HeartRate >= 140 || dto.HeartRate <= 54) ? 3 : (dto.HeartRate >= 110 || dto.HeartRate <= 69) ? 2 : 0;
        aps += (dto.RespRate >= 50 || dto.RespRate <= 5) ? 4 : (dto.RespRate >= 35) ? 3 : (dto.RespRate <= 9) ? 2 : (dto.RespRate >= 25 || dto.RespRate <= 11) ? 1 : 0;
        aps += (dto.CreatinineMgDl >= 3.5m) ? 4 : (dto.CreatinineMgDl >= 2.0m) ? 3 : (dto.CreatinineMgDl >= 1.5m || dto.CreatinineMgDl < 0.6m) ? 2 : 0;
        aps += (15 - dto.GcsScore);

        int agePts = dto.AgeYears >= 75 ? 6 : dto.AgeYears >= 65 ? 5 : dto.AgeYears >= 55 ? 3 : dto.AgeYears >= 45 ? 2 : 0;
        int chronicPts = dto.HasChronicOrganFailure ? 5 : 0;
        int apacheIi = aps + agePts + chronicPts;

        // Knaus APACHE II 经典 Logistic 预测病死率
        double logit = -3.517 + (apacheIi * 0.146);
        decimal mortalityPct = Math.Round((decimal)(Math.Exp(logit) / (1.0 + Math.Exp(logit)) * 100.0), 2);

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        int affected = await conn.ExecuteAsync(@"
UPDATE icu_admissions
SET apache_ii_score = @Apache,
    sofa_score = @Sofa,
    predicted_mortality_pct = @Mortality,
    updated_at_ms = @Now,
    updated_by = @Actor,
    version_no = version_no + 1
WHERE id = @Id;", new { Apache = apacheIi, Sofa = sofa, Mortality = mortalityPct, Now = now, Actor = ActorId, Id = icuAdmissionId });

        if (affected == 0) return ApiResponse<object>.Fail(404, "ICU 收治单不存在。");

        return ApiResponse<object>.Success(new
        {
            icuAdmissionId,
            apacheIiScore = apacheIi,
            sofaScore = sofa,
            predictedMortalityPct = mortalityPct,
            sepsisWarning = sofa >= 2
        });
    }

    [HttpGet("admissions/{icuAdmissionId}/hourly")]
    public async Task<ApiResponse<object>> ListHourlyRecords(string icuAdmissionId, CancellationToken ct)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var records = (await conn.QueryAsync(
            "SELECT * FROM icu_hourly_records WHERE icu_admission_id = @Id ORDER BY record_time_ms DESC LIMIT 48",
            new { Id = icuAdmissionId })).ToList();

        var cutoff24h = DateTimeOffset.UtcNow.AddHours(-24).ToUnixTimeMilliseconds();
        var summary24h = await conn.QueryFirstOrDefaultAsync(@"
SELECT COALESCE(SUM(intake_ml), 0) AS total_intake_24h,
       COALESCE(SUM(output_ml), 0) AS total_output_24h,
       COALESCE(SUM(net_balance_ml), 0) AS net_balance_24h
FROM icu_hourly_records
WHERE icu_admission_id = @Id AND record_time_ms >= @Cutoff;", new { Id = icuAdmissionId, Cutoff = cutoff24h });

        return ApiResponse<object>.Success(new { records, summary24h });
    }

    [HttpPost("admissions/{icuAdmissionId}/hourly")]
    public async Task<ApiResponse<object>> AddHourlyRecord(string icuAdmissionId, [FromBody] HourlyRecordDto dto, CancellationToken ct)
    {
        if (dto.IntakeMl < 0 || dto.OutputMl < 0)
            return ApiResponse<object>.Fail(400, "入量与出量不能为负数。");

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var net = dto.IntakeMl - dto.OutputMl;
        var id = Guid.NewGuid().ToString("N");

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        await conn.ExecuteAsync(@"
INSERT INTO icu_hourly_records (
    id, icu_admission_id, record_time_ms, vent_mode, fio2_pct, peep_cmh2o, tidal_volume_ml, resp_rate,
    map_mmhg, spo2_pct, lactate_mmol, intake_ml, output_ml, net_balance_ml,
    vasopressor_drug, vasopressor_rate_mcg_kg_min, nurse_name, note, created_at_ms, created_by
) VALUES (
    @Id, @AdmId, @Now, @VentMode, @Fio2, @Peep, @Vt, @Rr,
    @Map, @Spo2, @Lactate, @Intake, @Output, @Net,
    @VasoDrug, @VasoRate, @Nurse, @Note, @Now, @Actor
);", new
        {
            Id = id,
            AdmId = icuAdmissionId,
            Now = now,
            VentMode = dto.VentMode,
            Fio2 = dto.Fio2Pct,
            Peep = dto.PeepCmh2o,
            Vt = dto.TidalVolumeMl,
            Rr = dto.RespRate,
            Map = dto.MapMmhg,
            Spo2 = dto.Spo2Pct,
            Lactate = dto.LactateMmol,
            Intake = dto.IntakeMl,
            Output = dto.OutputMl,
            Net = net,
            VasoDrug = dto.VasopressorDrug,
            VasoRate = dto.VasopressorRateMcgKgMin,
            Nurse = ActorName,
            Note = dto.Note,
            Actor = ActorId
        });

        return ApiResponse<object>.Success(new { id, netBalanceMl = net, hypotensionAlert = dto.MapMmhg is < 65 });
    }

    [HttpGet("admissions/{icuAdmissionId}/catheters")]
    public async Task<ApiResponse<object>> ListCatheters(string icuAdmissionId, CancellationToken ct)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync(
            "SELECT * FROM icu_catheter_surveillance WHERE icu_admission_id = @Id ORDER BY inserted_at_ms DESC",
            new { Id = icuAdmissionId });
        return ApiResponse<object>.Success(rows);
    }

    [HttpPost("admissions/{icuAdmissionId}/catheters")]
    public async Task<ApiResponse<object>> InsertCatheter(string icuAdmissionId, [FromBody] InsertCatheterDto dto, CancellationToken ct)
    {
        var type = (dto.CatheterType ?? string.Empty).Trim().ToUpperInvariant();
        if (type is not ("VAP" or "CLABSI" or "CAUTI"))
            return ApiResponse<object>.Fail(400, "三管类型仅支持 VAP(呼吸机插管)/CLABSI(中心静脉)/CAUTI(导尿管)。");

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var adm = await conn.QueryFirstOrDefaultAsync("SELECT encounter_id FROM icu_admissions WHERE id = @Id", new { Id = icuAdmissionId });
        if (adm == null) return ApiResponse<object>.Fail(404, "ICU 收治单不存在。");

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var id = Guid.NewGuid().ToString("N");
        await conn.ExecuteAsync(@"
INSERT INTO icu_catheter_surveillance (
    id, icu_admission_id, encounter_id, catheter_type, site_name,
    inserted_at_ms, daily_review_passed, infection_suspected, infection_confirmed, status,
    created_at_ms, created_by, updated_at_ms, updated_by
) VALUES (
    @Id, @AdmId, @EncId, @Type, @Site,
    @Now, 1, 0, 0, 'INDWELLING',
    @Now, @Actor, @Now, @Actor
);", new { Id = id, AdmId = icuAdmissionId, EncId = (string)adm.encounter_id, Type = type, Site = dto.SiteName, Now = now, Actor = ActorId });

        return ApiResponse<object>.Success(new { id, catheterType = type, status = "INDWELLING" });
    }

    [HttpPost("catheters/{catheterId}/confirm-infection")]
    public async Task<ApiResponse<object>> ConfirmCatheterInfection(string catheterId, [FromBody] ConfirmCatheterInfectionDto dto, CancellationToken ct)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var cath = await conn.QueryFirstOrDefaultAsync(
            "SELECT c.id, c.icu_admission_id, c.encounter_id, c.catheter_type, a.patient_id, a.patient_name FROM icu_catheter_surveillance c LEFT JOIN icu_admissions a ON a.id = c.icu_admission_id WHERE c.id = @Id",
            new { Id = catheterId });
        if (cath == null) return ApiResponse<object>.Fail(404, "导管监测记录不存在。");

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var eventNo = $"HAI{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(10, 99)}";
        var safetyEventId = Guid.NewGuid().ToString("N");

        await using var tx = await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(@"
UPDATE icu_catheter_surveillance
SET infection_suspected = 1,
    infection_confirmed = 1,
    infection_note = @Note,
    updated_at_ms = @Now,
    updated_by = @Actor
WHERE id = @Id;", new { Note = dto.InfectionNote, Now = now, Actor = ActorId, Id = catheterId }, tx);

        // 跨模块闭环：自动上报至医务科不良事件/院感监测台账 (med_safety_rca_events)
        await conn.ExecuteAsync(@"
INSERT INTO med_safety_rca_events (
    id, event_no, severity_level, event_category, dept_name, event_summary, status, reporter_name,
    schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no
) VALUES (
    @EventId, @EventNo, 'LEVEL_II', 'ICU_HAI_CATHETER', 'ICU重症医学科', @Summary, 'REPORTED', @Reporter,
    1, @Now, @Actor, @Now, @Actor, 1
);", new
        {
            EventId = safetyEventId,
            EventNo = eventNo,
            Summary = $"ICU 三管院感确诊 [{cath.catheter_type}]：{dto.InfectionNote}",
            Reporter = ActorName,
            Now = now,
            Actor = ActorId
        }, tx);


        // 跨模块闭环 2：同步写入院感科监测表 (ipc_infection_reports)，消除 ICU 三管与院感科数据孤岛
        await conn.ExecuteAsync(@"
INSERT INTO ipc_infection_reports (
    id, report_no, encounter_id, patient_id, patient_name, ward_id, infection_site,
    pathogen, is_mdro, diagnosed_at_ms, clinical_manifestation, status, reporter_id,
    schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no
) VALUES (
    @RepId, @RepNo, @EncId, @PatId, @PatName, 'DEPT-ICU', @Site,
    '待药敏明确', 0, @Now, @Manifest, 'REPORTED', @Actor,
    1, @Now, @Actor, @Now, @Actor, 1
);", new
        {
            RepId = Guid.NewGuid().ToString("N"),
            RepNo = $"IPC-{eventNo}",
            EncId = (string?)cath.encounter_id ?? string.Empty,
            PatId = (string?)cath.patient_id ?? string.Empty,
            PatName = (string?)cath.patient_name ?? "ICU患者",
            Site = $"ICU三管-{cath.catheter_type}",
            Manifest = dto.InfectionNote,
            Now = now,
            Actor = ActorId
        }, tx);

        await tx.CommitAsync(ct);

        return ApiResponse<object>.Success(new { catheterId, infectionConfirmed = true, linkedSafetyEventId = safetyEventId, linkedEventNo = eventNo });
    }
}
