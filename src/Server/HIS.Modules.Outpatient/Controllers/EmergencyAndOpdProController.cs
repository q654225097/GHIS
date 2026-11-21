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
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Outpatient.Controllers;

/// <summary>
/// 急诊 5 级预检分诊（MEWS/GCS/抢救留观/绿通转住院）与门诊增强（临床优先级排队、慢病一键续方、诊断证明/病假条防伪签发）
/// </summary>
[ApiController]
[Route("api/v4/er-opd")]
public sealed class EmergencyAndOpdProController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _session;
    private readonly IAuditTrailContract? _auditTrail;

    public EmergencyAndOpdProController(
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

    public sealed record CreateErTriageDto(
        string PatientName,
        string Gender,
        int Age,
        string ChiefComplaint,
        decimal TempC,
        int HeartRate,
        int RespRate,
        int Sbp,
        int Spo2,
        int GcsScore,
        bool IsGreenChannel,
        string? BedCode = null);

    public sealed record UpdateErStatusDto(string Status, string? DestinationZone, string? BedCode, decimal? AddCreditAmount);
    public sealed record SetQueuePriorityDto(int PriorityLevel, string PriorityTag);
    public sealed record IssueCertificateDto(string? VisitId, string PatientName, string Gender, int Age, string DeptName, string CertType, string DiagnosisText, int RestDays, string AdviceText);

    // ===================== 1. 急诊 5 级分诊与 MEWS 自动评分 =====================

    private static (int Mews, int TriageLevel, string DefaultZone) ComputeMewsAndTriage(
        decimal tempC, int hr, int rr, int sbp, int spo2, int gcs)
    {
        int mews = 0;
        // 呼吸频率
        if (rr < 9) mews += 2;
        else if (rr >= 15 && rr <= 20) mews += 1;
        else if (rr >= 21 && rr <= 29) mews += 2;
        else if (rr >= 30) mews += 3;

        // 心率
        if (hr <= 40) mews += 2;
        else if (hr <= 50) mews += 1;
        else if (hr >= 101 && hr <= 110) mews += 1;
        else if (hr >= 111 && hr <= 129) mews += 2;
        else if (hr >= 130) mews += 3;

        // 收缩压
        if (sbp <= 70) mews += 3;
        else if (sbp <= 80) mews += 2;
        else if (sbp <= 100) mews += 1;
        else if (sbp >= 200) mews += 2;

        // 体温
        if (tempC < 35.0m || tempC >= 38.5m) mews += 2;

        // 意识 GCS
        if (gcs <= 8) mews += 3;
        else if (gcs <= 12) mews += 2;
        else if (gcs <= 14) mews += 1;

        int level;
        string zone;
        if (mews >= 6 || spo2 < 90 || gcs <= 8) { level = 1; zone = "RESUS_ROOM"; }
        else if (mews >= 4 || spo2 < 93 || gcs <= 12) { level = 2; zone = "RESUS_ROOM"; }
        else if (mews >= 2) { level = 3; zone = "OBS_WARD"; }
        else if (mews == 1) { level = 4; zone = "ER_CLINIC"; }
        else { level = 5; zone = "ER_CLINIC"; }

        return (mews, level, zone);
    }

    [HttpGet("er/visits")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetErVisits([FromQuery] string? status = null)
    {
        using var conn = _connFactory.CreateConnection();
        var sql = @"SELECT id AS Id, er_no AS ErNo, patient_name AS PatientName, gender AS Gender, age AS Age,
                           chief_complaint AS ChiefComplaint, temp_c AS TempC, heart_rate AS HeartRate,
                           resp_rate AS RespRate, sbp AS Sbp, spo2 AS Spo2, gcs_score AS GcsScore,
                           mews_score AS MewsScore, triage_level AS TriageLevel, destination_zone AS DestinationZone,
                           bed_code AS BedCode, is_green_channel AS IsGreenChannel,
                           unpaid_credit_amount AS UnpaidCreditAmount, status AS Status,
                           triage_nurse_name AS TriageNurseName, triaged_at_ms AS TriagedAtMs
                    FROM er_triage_visits WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(status) && status != "ALL") sql += " AND status = @St";
        sql += " ORDER BY triage_level ASC, triaged_at_ms DESC LIMIT 200";
        var rows = (await conn.QueryAsync<dynamic>(sql, new { St = status?.Trim() })).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("er/triage")]
    public async Task<ApiResponse<object>> CreateErTriage([FromBody] CreateErTriageDto req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.PatientName) || string.IsNullOrWhiteSpace(req.ChiefComplaint))
            return ApiResponse<object>.Fail(400, "患者姓名与急诊主诉不能为空");

        var (mews, level, zone) = ComputeMewsAndTriage(req.TempC, req.HeartRate, req.RespRate, req.Sbp, req.Spo2, req.GcsScore);
        bool green = req.IsGreenChannel || level <= 2;
        long now = SystemTime.UtcNowMs;
        string id = Guid.NewGuid().ToString("D");
        string erNo = "ER" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss") + Random.Shared.Next(10, 99);
        string status = zone == "RESUS_ROOM" ? "RESUSCITATING" : zone == "OBS_WARD" ? "OBSERVING" : "WAITING";

        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO er_triage_visits
            (id, er_no, patient_name, gender, age, chief_complaint, temp_c, heart_rate, resp_rate, sbp, spo2, gcs_score, mews_score, triage_level, destination_zone, bed_code, is_green_channel, unpaid_credit_amount, status, triage_nurse_name, triaged_at_ms, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES
            (@Id, @ErNo, @Name, @Gender, @Age, @Cc, @Temp, @Hr, @Rr, @Sbp, @Spo2, @Gcs, @Mews, @Lvl, @Zone, @Bed, @Green, 0, @Status, @Nurse, @Now, 1, @Now, @Actor, @Now, @Actor, 1)",
            new
            {
                Id = id, ErNo = erNo, Name = req.PatientName.Trim(), Gender = req.Gender, Age = req.Age,
                Cc = req.ChiefComplaint.Trim(), Temp = req.TempC, Hr = req.HeartRate, Rr = req.RespRate,
                req.Sbp, req.Spo2, Gcs = req.GcsScore, Mews = mews, Lvl = level, Zone = zone,
                Bed = req.BedCode?.Trim(), Green = green ? 1 : 0, Status = status, Nurse = ActorName, Now = now, Actor = ActorId
            });

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("ER_TRIAGE", "er_triage_visits", id, $"急诊预检分诊：{req.PatientName}（MEWS={mews}，{level}级，去向 {zone}，绿通={green}）", ActorId, ActorName, null, ct);

        return ApiResponse<object>.Success(new { Id = id, ErNo = erNo, MewsScore = mews, TriageLevel = level, DestinationZone = zone, IsGreenChannel = green },
            $"分诊完成：MEWS {mews} 分 → {level} 级（自动分配至 {zone}）");
    }

    [HttpPut("er/visits/{id}/status")]
    public async Task<ApiResponse<object>> UpdateErStatus(string id, [FromBody] UpdateErStatusDto req)
    {
        long now = SystemTime.UtcNowMs;
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE er_triage_visits
            SET status = @St, destination_zone = COALESCE(@Zone, destination_zone),
                bed_code = COALESCE(@Bed, bed_code),
                unpaid_credit_amount = unpaid_credit_amount + COALESCE(@Credit, 0),
                updated_at_ms = @Now, updated_by = @Actor
            WHERE id = @Id",
            new { St = req.Status, Zone = req.DestinationZone, Bed = req.BedCode, Credit = req.AddCreditAmount, Now = now, Actor = ActorId, Id = id });
        return ApiResponse<object>.Success(new { id, req.Status }, "急诊留观/抢救状态已更新");
    }

    // ===================== 2. 门诊临床优先级队列与预计候诊时间估算 =====================

    [HttpGet("opd/priority-queue")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetPriorityQueue()
    {
        using var conn = _connFactory.CreateConnection();
        var raw = (await conn.QueryAsync<dynamic>(@"
            SELECT q.id AS Id, q.queue_no AS QueueNo, COALESCE(p.full_name, q.patient_id) AS PatientName,
                   COALESCE(q.priority_level, 4) AS PriorityLevel,
                   COALESCE(q.priority_tag, '普通') AS PriorityTag,
                   q.status AS Status, q.created_at_ms AS CreatedAtMs
            FROM opd_queue_tickets q
            LEFT JOIN pat_patients p ON p.id = q.patient_id
            WHERE q.status IN (0, 1)
            ORDER BY COALESCE(q.priority_level, 4) ASC, q.created_at_ms ASC LIMIT 100")).ToList();

        var result = new List<object>();
        int idx = 0;
        foreach (var r in raw)
        {
            result.Add(new
            {
                Id = (string)r.Id,
                QueueNo = (string?)r.QueueNo ?? "-",
                PatientName = (string?)r.PatientName ?? "-",
                PriorityLevel = Convert.ToInt32(r.PriorityLevel),
                PriorityTag = (string?)r.PriorityTag ?? "普通",
                Status = Convert.ToInt32(r.Status),
                EstimatedWaitMinutes = idx * 8
            });
            idx++;
        }
        return ApiResponse<IReadOnlyList<object>>.Success(result);
    }

    [HttpPut("opd/priority-queue/{ticketId}")]
    public async Task<ApiResponse<object>> SetQueuePriority(string ticketId, [FromBody] SetQueuePriorityDto req)
    {
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE opd_queue_tickets SET priority_level = @Lvl, priority_tag = @Tag, updated_at_ms = @Now WHERE id = @Id",
            new { Lvl = Math.Clamp(req.PriorityLevel, 1, 4), Tag = req.PriorityTag, Now = SystemTime.UtcNowMs, Id = ticketId });
        return ApiResponse<object>.Success(new { ticketId, req.PriorityLevel, req.PriorityTag }, "已更新门诊临床优先级别");
    }

    /// <summary>
    /// 慢病复诊一键复制患者最近一次历史处方明细
    /// </summary>
    [HttpPost("opd/copy-last-rx/{patientId}")]
    public async Task<ApiResponse<object>> CopyLastPrescription(string patientId)
    {
        using var conn = _connFactory.CreateConnection();
        var lastRx = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT id AS Id, prescription_no AS RxNo, total_amount AS TotalAmount FROM opd_prescriptions WHERE patient_id = @Pid ORDER BY created_at_ms DESC LIMIT 1",
            new { Pid = patientId });
        if (lastRx == null) return ApiResponse<object>.Fail(404, "该患者暂无可续方的历史处方记录");

        var items = (await conn.QueryAsync<dynamic>(
            "SELECT drug_id AS DrugId, drug_name_snapshot AS DrugName, dose_once AS Dose, frequency_code AS Freq, route_code AS Route, total_quantity AS Qty, unit_price AS UnitPrice FROM opd_prescription_items WHERE prescription_id = @Rid AND is_deleted = 0",
            new { Rid = (string)lastRx.Id })).ToList();

        return ApiResponse<object>.Success(new { SourceRxNo = (string)lastRx.RxNo, Items = items }, $"已提取历史处方 [{lastRx.RxNo}] 共 {items.Count} 条药品供一键续方");
    }

    // ===================== 3. 门诊诊断证明书与病假条防伪签发 =====================

    [HttpGet("opd/certificates")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetCertificates()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT id AS Id, cert_no AS CertNo, patient_name AS PatientName, gender AS Gender, age AS Age,
                   dept_name AS DeptName, cert_type AS CertType, diagnosis_text AS DiagnosisText,
                   rest_days AS RestDays, advice_text AS AdviceText, doctor_name AS DoctorName,
                   verify_hash AS VerifyHash, issued_at_ms AS IssuedAtMs
            FROM opd_medical_certificates ORDER BY issued_at_ms DESC LIMIT 100")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("opd/certificates")]
    public async Task<ApiResponse<object>> IssueCertificate([FromBody] IssueCertificateDto req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.PatientName) || string.IsNullOrWhiteSpace(req.DiagnosisText))
            return ApiResponse<object>.Fail(400, "患者姓名与临床诊断不能为空");

        long now = SystemTime.UtcNowMs;
        string id = Guid.NewGuid().ToString("D");
        string certNo = (req.CertType == "SICK_LEAVE" ? "BJT" : "ZDM") + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss");
        string hash = CryptoHelper.ComputeSm3($"{certNo}|{req.PatientName}|{req.DiagnosisText}|{req.RestDays}|{ActorId}|{now}");

        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO opd_medical_certificates
            (id, cert_no, visit_id, patient_name, gender, age, dept_name, cert_type, diagnosis_text, rest_days, advice_text, doctor_name, verify_hash, issued_at_ms, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES
            (@Id, @No, @Vid, @Name, @Gender, @Age, @Dept, @Type, @Diag, @Days, @Advice, @Doc, @Hash, @Now, 1, @Now, @Actor, @Now, @Actor, 1)",
            new
            {
                Id = id, No = certNo, Vid = req.VisitId, Name = req.PatientName.Trim(), req.Gender, req.Age,
                Dept = string.IsNullOrWhiteSpace(req.DeptName) ? "内科门诊" : req.DeptName.Trim(),
                Type = req.CertType, Diag = req.DiagnosisText.Trim(), Days = Math.Clamp(req.RestDays, 0, 90),
                Advice = req.AdviceText?.Trim(), Doc = ActorName, Hash = hash, Now = now, Actor = ActorId
            });

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("OPD_ISSUE_CERT", "opd_medical_certificates", id, $"签发证明书：{req.PatientName}（{certNo}，休假 {req.RestDays} 天）", ActorId, ActorName, null, ct);

        return ApiResponse<object>.Success(new { Id = id, CertNo = certNo, VerifyHash = hash }, $"已签发带 SM3 防伪码的证明书 [{certNo}]");
    }
}
