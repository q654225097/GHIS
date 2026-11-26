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
using HIS.Modules.Inpatient.Services;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Inpatient.Controllers;

/// <summary>
/// 住院临床与运营增强引擎：
/// 1) 图形化床位总览看板（换床/设隔离） + 护士时间轴 MAR + 三测单趋势
/// 2) 全院会诊中心（普通24h/急会诊10min/MDT，自动回写 emr_documents）
/// 3) 每日自动滚费管理、病区退药申请与药房审批冲正、出院带药专单
/// 4) 专科扩展（产科分娩与新生儿建档、日间手术24h闭环+PADSS评分、病区陪护/探视证）
/// </summary>
[ApiController]
[RequireHisRole("ADMIN", "IPD_ATTENDING", "IPD_RESIDENT", "WARD_NURSE")]
[Route("api/v4/ipd-pro")]
public sealed class InpatientProController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _session;
    private readonly IAuditTrailContract? _auditTrail;

    public InpatientProController(
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

    public sealed record SetIsolationDto(bool IsIsolation);
    public sealed record SwapBedDto(string SourceBedId, string TargetBedId);
    public sealed record CreateConsultDto(
        string? EncounterId,
        string PatientName,
        string? BedCode,
        string RequestDeptName,
        string TargetDeptName,
        string ConsultType,
        string PurposeSummary);
    public sealed record CompleteConsultDto(string ConsultOpinion);
    public sealed record TriggerBillingDto(int? BillDateInt);
    public sealed record CreateDrugReturnDto(
        string? EncounterId,
        string PatientName,
        string? BedCode,
        string DrugName,
        decimal ReturnQty,
        decimal RefundAmount,
        string Reason);
    public sealed record ReviewDrugReturnDto(string Decision);
    public sealed record CreateDischargeMedDto(
        string? EncounterId,
        string PatientName,
        string MedicationsSummary,
        decimal TotalAmount);
    public sealed record CreateMaternityDto(
        string? MotherEncounterId,
        string MotherName,
        string GestationalWeeks,
        string DeliveryMode,
        string NewbornGender,
        int BirthWeightG,
        decimal BirthLengthCm,
        string ApgarScores);
    public sealed record CreateVisitorPassDto(
        string? EncounterId,
        string PatientName,
        string? BedCode,
        string VisitorName,
        string RelationName,
        string? Phone,
        string PassType,
        int ValidDays);
    public sealed record CreateDaySurgeryDto(
        string PatientName,
        string Gender,
        int Age,
        string DeptName,
        string SurgeryName,
        string? SurgeonName);
    public sealed record UpdateDaySurgeryDto(string Status, int PadssScore, string? Remark);

    // ===================== 1. 图形化床位总览看板 / MAR / 三测单 =====================

    [HttpGet("beds/board")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetBedBoard([FromQuery] string? wardId = null)
    {
        using var conn = _connFactory.CreateConnection();
        var sql = @"
            SELECT b.id AS BedId, b.ward_id AS WardId, COALESCE(d.dept_name, b.ward_id) AS WardName,
                   b.room_code AS RoomCode, b.bed_code AS BedCode, b.bed_type AS BedType,
                   b.daily_fee AS DailyFee, b.bed_status AS BedStatus,
                   COALESCE(b.is_isolation, 0) AS IsIsolation,
                   b.current_encounter_id AS EncounterId,
                   e.admission_no AS AdmissionNo,
                   e.nursing_level_code AS NursingLevelCode,
                   e.prepayment_balance AS PrepaymentBalance,
                   p.full_name AS PatientName,
                   p.gender_code AS Gender
            FROM ipd_beds b
            LEFT JOIN sys_departments d ON d.id = b.ward_id
            LEFT JOIN ipd_encounters e ON e.id = b.current_encounter_id
            LEFT JOIN pat_patients p ON p.id = e.patient_id
            WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(wardId) && wardId != "ALL")
            sql += " AND b.ward_id = @WardId";
        sql += " ORDER BY b.ward_id ASC, b.room_code ASC, b.bed_code ASC LIMIT 200";
        var rows = (await conn.QueryAsync<dynamic>(sql, new { WardId = wardId?.Trim() })).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPut("beds/{id}/isolation")]
    public async Task<ApiResponse<object>> ToggleBedIsolation(string id, [FromBody] SetIsolationDto req)
    {
        using var conn = _connFactory.CreateConnection();
        int updated = await conn.ExecuteAsync(
            "UPDATE ipd_beds SET is_isolation = @Iso, updated_at_ms = @Now, updated_by = @Actor WHERE id = @Id",
            new { Iso = req.IsIsolation ? 1 : 0, Now = SystemTime.UtcNowMs, Actor = ActorId, Id = id });
        if (updated == 0) return ApiResponse<object>.Fail(404, "未找到对应床位");
        return ApiResponse<object>.Success(new { BedId = id, req.IsIsolation }, req.IsIsolation ? "已设为隔离床位" : "已取消隔离状态");
    }

    [HttpPost("beds/swap")]
    public async Task<ApiResponse<object>> SwapBed([FromBody] SwapBedDto req)
    {
        if (string.IsNullOrWhiteSpace(req.SourceBedId) || string.IsNullOrWhiteSpace(req.TargetBedId) || req.SourceBedId == req.TargetBedId)
            return ApiResponse<object>.Fail(400, "请选择不同的源床位与目标床位");

        using var conn = _connFactory.CreateConnection();
        var src = await conn.QueryFirstOrDefaultAsync<dynamic>("SELECT * FROM ipd_beds WHERE id = @Id", new { Id = req.SourceBedId });
        var dst = await conn.QueryFirstOrDefaultAsync<dynamic>("SELECT * FROM ipd_beds WHERE id = @Id", new { Id = req.TargetBedId });
        if (src == null || dst == null) return ApiResponse<object>.Fail(404, "源床位或目标床位不存在");

        string? srcEnc = (string?)src.current_encounter_id;
        string? dstEnc = (string?)dst.current_encounter_id;
        long now = SystemTime.UtcNowMs;

        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(
            "UPDATE ipd_beds SET current_encounter_id = @Enc, bed_status = @St, updated_at_ms = @Now, updated_by = @Actor WHERE id = @Id",
            new { Enc = dstEnc, St = string.IsNullOrWhiteSpace(dstEnc) ? 0 : 1, Now = now, Actor = ActorId, Id = req.SourceBedId }, tx);
        await conn.ExecuteAsync(
            "UPDATE ipd_beds SET current_encounter_id = @Enc, bed_status = @St, updated_at_ms = @Now, updated_by = @Actor WHERE id = @Id",
            new { Enc = srcEnc, St = string.IsNullOrWhiteSpace(srcEnc) ? 0 : 1, Now = now, Actor = ActorId, Id = req.TargetBedId }, tx);

        if (!string.IsNullOrWhiteSpace(srcEnc))
        {
            await conn.ExecuteAsync(
                "UPDATE ipd_encounters SET bed_code = @BedCode, admit_ward_id = @WardId, updated_at_ms = @Now, updated_by = @Actor WHERE id = @Id",
                new { BedCode = (string)dst.bed_code, WardId = (string)dst.ward_id, Now = now, Actor = ActorId, Id = srcEnc }, tx);
        }
        if (!string.IsNullOrWhiteSpace(dstEnc))
        {
            await conn.ExecuteAsync(
                "UPDATE ipd_encounters SET bed_code = @BedCode, admit_ward_id = @WardId, updated_at_ms = @Now, updated_by = @Actor WHERE id = @Id",
                new { BedCode = (string)src.bed_code, WardId = (string)src.ward_id, Now = now, Actor = ActorId, Id = dstEnc }, tx);
        }
        tx.Commit();

        return ApiResponse<object>.Success(new { req.SourceBedId, req.TargetBedId }, $"已完成 {(string)src.bed_code} ⇄ {(string)dst.bed_code} 换床调配");
    }

    [HttpGet("mar/{encounterId}")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetMarTimeline(string encounterId)
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT p.id AS PlanId, p.order_id AS OrderId, p.planned_time_ms AS PlannedTimeMs,
                   p.exec_status AS ExecStatus, p.exec_nurse_id AS ExecNurseId,
                   p.executed_at_ms AS ExecutedAtMs,
                   o.item_name AS ItemName, o.dosage AS Dosage, o.frequency_code AS FrequencyCode,
                   o.route_code AS RouteCode, o.order_category AS OrderCategory
            FROM ord_execution_plans p
            LEFT JOIN ord_medical_orders o ON o.id = p.order_id
            WHERE p.encounter_id = @EncId
            ORDER BY p.planned_time_ms ASC LIMIT 120", new { EncId = encounterId })).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    public sealed record ExecuteMarDto(string? FirstNurseName = null, string? SecondNurseName = null);

    [HttpPost("mar/{planId}/execute")]
    public async Task<ApiResponse<object>> ExecuteMarPlan(string planId, [FromBody] ExecuteMarDto? dto = null)
    {
        using var conn = _connFactory.CreateConnection();
        string firstNurse = string.IsNullOrWhiteSpace(dto?.FirstNurseName) ? ActorName : dto!.FirstNurseName!.Trim();
        string? itemName = await conn.ExecuteScalarAsync<string?>(@"
SELECT o.item_name
FROM ord_execution_plans p
JOIN ord_medical_orders o ON o.id = p.order_id
WHERE p.id = @Id;", new { Id = planId });

        // R-05 闭环：高警示药品（胰岛素/氯化钾/高渗盐水/肝素/华法林/血管活性药/吗啡）强制要求异人双签核对
        bool isHighAlert = !string.IsNullOrWhiteSpace(itemName) && new[]
        {
            "胰岛素", "氯化钾", "高渗盐水", "肝素", "华法林", "去甲肾上腺素", "肾上腺素", "吗啡"
        }.Any(kw => itemName.Contains(kw, StringComparison.OrdinalIgnoreCase));

        if (isHighAlert)
        {
            if (string.IsNullOrWhiteSpace(dto?.SecondNurseName))
                return ApiResponse<object>.Fail(400, $"高警示药品 [{itemName}] 必须执行双人核对制度，请指定第二核对护士！");
            if (string.Equals(firstNurse, dto!.SecondNurseName!.Trim(), StringComparison.OrdinalIgnoreCase))
                return ApiResponse<object>.Fail(400, $"高警示药品 [{itemName}] 严禁同人双签：复核护士不能与执行护士相同！");
        }

        long now = SystemTime.UtcNowMs;
        string recorder = isHighAlert ? $"{firstNurse} / 复核:{dto!.SecondNurseName!.Trim()}" : firstNurse;
        int n = await conn.ExecuteAsync(
            "UPDATE ord_execution_plans SET exec_status = 1, exec_nurse_id = @Nurse, executed_at_ms = @Now, updated_at_ms = @Now, updated_by = @Actor WHERE id = @Id",
            new { Nurse = recorder, Now = now, Actor = ActorId, Id = planId });
        if (n == 0) return ApiResponse<object>.Fail(404, "未找到该执行计划");
        return ApiResponse<object>.Success(new { PlanId = planId, ExecutedAtMs = now, HighAlertDualChecked = isHighAlert, Recorder = recorder }, "已完成床旁扫码执行确认");
    }

    [HttpGet("vitals-chart/{encounterId}")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetVitalsChart(string encounterId)
    {
        using var conn = _connFactory.CreateConnection();
        // 从体征记录或护理评估表读取（兼容存在/不存在 mon_vital_signs 表）
        try
        {
            var rows = (await conn.QueryAsync<dynamic>(@"
                SELECT id AS Id, recorded_at_ms AS RecordedAtMs, temperature_c AS TempC,
                       pulse_bpm AS PulseBpm, resp_rate AS RespRate, sbp AS Sbp, dbp AS Dbp, spo2 AS Spo2
                FROM mon_vital_signs
                WHERE encounter_id = @EncId
                ORDER BY recorded_at_ms DESC LIMIT 30", new { EncId = encounterId })).ToList<object>();
            return ApiResponse<IReadOnlyList<object>>.Success(rows);
        }
        catch
        {
            return ApiResponse<IReadOnlyList<object>>.Success(Array.Empty<object>());
        }
    }

    // ===================== 2. 全院会诊中心（普通24h / 急会诊10min / MDT） =====================

    [HttpGet("consultations")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetConsultations([FromQuery] string? status = null)
    {
        using var conn = _connFactory.CreateConnection();
        var sql = @"
            SELECT id AS Id, consult_no AS ConsultNo, encounter_id AS EncounterId,
                   patient_name AS PatientName, bed_code AS BedCode,
                   request_dept_name AS RequestDeptName, request_doctor_name AS RequestDoctorName,
                   target_dept_name AS TargetDeptName, assigned_doctor_name AS AssignedDoctorName,
                   consult_type AS ConsultType, purpose_summary AS PurposeSummary,
                   deadline_ms AS DeadlineMs, status AS Status,
                   consult_opinion AS ConsultOpinion, synced_to_emr AS SyncedToEmr,
                   completed_at_ms AS CompletedAtMs
            FROM ipd_consultations WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(status) && status != "ALL")
            sql += " AND status = @St";
        sql += " ORDER BY deadline_ms ASC LIMIT 200";
        var rows = (await conn.QueryAsync<dynamic>(sql, new { St = status?.Trim() })).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("consultations")]
    public async Task<ApiResponse<object>> CreateConsultation([FromBody] CreateConsultDto req)
    {
        if (string.IsNullOrWhiteSpace(req.PatientName) || string.IsNullOrWhiteSpace(req.TargetDeptName))
            return ApiResponse<object>.Fail(400, "患者姓名与被邀科室不能为空");

        long now = SystemTime.UtcNowMs;
        string ctype = string.IsNullOrWhiteSpace(req.ConsultType) ? "ROUTINE" : req.ConsultType.Trim().ToUpperInvariant();
        long deadlineMs = ctype switch
        {
            "EMERGENCY" => now + 10L * 60L * 1000L, // 急会诊 10 分钟到达
            "MDT" => now + 48L * 3600L * 1000L,     // MDT 48 小时内
            _ => now + 24L * 3600L * 1000L          // 普通会诊 24 小时内
        };
        string id = Guid.NewGuid().ToString("D");
        string no = "CS" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss");

        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO ipd_consultations
            (id, consult_no, encounter_id, patient_name, bed_code, request_dept_name, request_doctor_name,
             target_dept_name, consult_type, purpose_summary, deadline_ms, status, synced_to_emr,
             schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES
            (@Id, @No, @EncId, @Pat, @Bed, @ReqDept, @ReqDoc,
             @TgtDept, @Type, @Purpose, @Deadline, 'REQUESTED', 0,
             1, @Now, @Actor, @Now, @Actor, 1)",
            new
            {
                Id = id,
                No = no,
                EncId = req.EncounterId,
                Pat = req.PatientName.Trim(),
                Bed = req.BedCode?.Trim(),
                ReqDept = string.IsNullOrWhiteSpace(req.RequestDeptName) ? "当前病区" : req.RequestDeptName.Trim(),
                ReqDoc = ActorName,
                TgtDept = req.TargetDeptName.Trim(),
                Type = ctype,
                Purpose = req.PurposeSummary?.Trim() ?? "协助明确诊断及制定专科治疗方案",
                Deadline = deadlineMs,
                Now = now,
                Actor = ActorId
            });

        return ApiResponse<object>.Success(new { Id = id, ConsultNo = no, DeadlineMs = deadlineMs }, $"会诊申请 {no} 已发送至 {req.TargetDeptName}");
    }

    [HttpPost("consultations/{id}/accept")]
    public async Task<ApiResponse<object>> AcceptConsultation(string id)
    {
        using var conn = _connFactory.CreateConnection();
        int n = await conn.ExecuteAsync(
            "UPDATE ipd_consultations SET status = 'ACCEPTED', assigned_doctor_name = @Doc, updated_at_ms = @Now, updated_by = @Actor WHERE id = @Id AND status = 'REQUESTED'",
            new { Doc = ActorName, Now = SystemTime.UtcNowMs, Actor = ActorId, Id = id });
        if (n == 0) return ApiResponse<object>.Fail(400, "会诊单不存在或已被签收");
        return ApiResponse<object>.Success(new { Id = id, AssignedDoctor = ActorName }, "已签收会诊申请");
    }

    [HttpPost("consultations/{id}/complete")]
    public async Task<ApiResponse<object>> CompleteConsultation(string id, [FromBody] CompleteConsultDto req)
    {
        if (string.IsNullOrWhiteSpace(req.ConsultOpinion))
            return ApiResponse<object>.Fail(400, "会诊意见不能为空");

        using var conn = _connFactory.CreateConnection();
        var item = await conn.QueryFirstOrDefaultAsync<dynamic>("SELECT * FROM ipd_consultations WHERE id = @Id", new { Id = id });
        if (item == null) return ApiResponse<object>.Fail(404, "未找到会诊单");

        long now = SystemTime.UtcNowMs;
        string opinion = req.ConsultOpinion.Trim();
        string? encId = (string?)item.encounter_id;
        string docNo = "EMR-CS-" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss");

        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(@"
            UPDATE ipd_consultations
            SET status = 'COMPLETED', consult_opinion = @Op, assigned_doctor_name = COALESCE(assigned_doctor_name, @Doc),
                synced_to_emr = 1, completed_at_ms = @Now, updated_at_ms = @Now, updated_by = @Actor
            WHERE id = @Id",
            new { Op = opinion, Doc = ActorName, Now = now, Actor = ActorId, Id = id }, tx);

        // 自动回写 emr_documents 住院会诊记录
        string emrDocId = Guid.NewGuid().ToString("D");
        await conn.ExecuteAsync(@"
            INSERT INTO emr_documents
            (id, doc_no, emr_scene, visit_or_encounter_id, patient_id, dept_id, template_id, template_version,
             doc_type_code, doc_title, clinical_time_ms, plain_text_summary, status, author_id,
             schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES
            (@Id, @DocNo, 'IPD', @EncId, @PatId, @DeptId, 'TPL_CONSULT_RECORD', 1,
             'CONSULT_RECORD', @Title, @Now, @Summary, 2, @Actor,
             1, @Now, @Actor, @Now, @Actor, 1)",
            new
            {
                Id = emrDocId,
                DocNo = docNo,
                EncId = string.IsNullOrWhiteSpace(encId) ? id : encId,
                PatId = (string)item.patient_name,
                DeptId = (string)item.target_dept_name,
                Title = $"会诊记录 - {(string)item.target_dept_name} ({(string)item.consult_no})",
                Now = now,
                Summary = $"【被邀科室】{(string)item.target_dept_name}\n【会诊医师】{ActorName}\n【会诊意见】{opinion}",
                Actor = ActorId
            }, tx);

        tx.Commit();
        return ApiResponse<object>.Success(new { Id = id, EmrDocumentId = emrDocId, DocNo = docNo }, "会诊已完成并自动回写电子病历病程");
    }

    // ===================== 3. 住院每日自动滚费 / 病区退药冲正 / 出院带药 =====================

    [HttpGet("daily-billing/runs")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetDailyBillingRuns()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT id AS Id, bill_date_int AS BillDateInt, trigger_mode AS TriggerMode,
                   patient_count AS PatientCount, bed_fee_count AS BedFeeCount,
                   nursing_fee_count AS NursingFeeCount, long_order_count AS LongOrderCount,
                   total_amount AS TotalAmount, executed_by AS ExecutedBy, executed_at_ms AS ExecutedAtMs
            FROM ipd_daily_billing_runs
            ORDER BY bill_date_int DESC LIMIT 60")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("daily-billing/run")]
    public async Task<ApiResponse<object>> TriggerDailyBilling([FromBody] TriggerBillingDto? req, CancellationToken ct)
    {
        int billDateInt = req?.BillDateInt is > 20000101
            ? req.BillDateInt.Value
            : int.Parse(SystemTime.ShanghaiNow.ToString("yyyyMMdd"));

        var res = await InpatientDailyBillingHostedService.ExecuteDailyBillingCoreAsync(
            _connFactory, billDateInt, "MANUAL_TRIGGER", ActorName, ct);

        string msg = res.AlreadyExecuted
            ? $"账单日 {billDateInt} 已执行过每日滚费（幂等防重，共 {res.PatientCount} 人，¥{res.TotalAmount:F2}）"
            : $"账单日 {billDateInt} 每日滚费完成：在院 {res.PatientCount} 人，生成床位/护理/长嘱费用合计 ¥{res.TotalAmount:F2}";
        return ApiResponse<object>.Success(res, msg);
    }

    [HttpGet("drug-returns")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetDrugReturns()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT id AS Id, return_no AS ReturnNo, encounter_id AS EncounterId,
                   patient_name AS PatientName, bed_code AS BedCode, drug_name AS DrugName,
                   return_qty AS ReturnQty, refund_amount AS RefundAmount, reason AS Reason,
                   status AS Status, applicant_name AS ApplicantName, reviewer_name AS ReviewerName,
                   reviewed_at_ms AS ReviewedAtMs
            FROM ipd_drug_returns ORDER BY created_at_ms DESC LIMIT 200")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("drug-returns")]
    public async Task<ApiResponse<object>> CreateDrugReturn([FromBody] CreateDrugReturnDto req)
    {
        if (string.IsNullOrWhiteSpace(req.PatientName) || string.IsNullOrWhiteSpace(req.DrugName))
            return ApiResponse<object>.Fail(400, "患者姓名与退药药品名称不能为空");

        long now = SystemTime.UtcNowMs;
        string id = Guid.NewGuid().ToString("D");
        string no = "TY" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss");

        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO ipd_drug_returns
            (id, return_no, encounter_id, patient_name, bed_code, drug_name, return_qty, refund_amount,
             reason, status, applicant_name, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES
            (@Id, @No, @EncId, @Pat, @Bed, @Drug, @Qty, @Amt,
             @Reason, 'APPLIED', @App, 1, @Now, @Actor, @Now, @Actor, 1)",
            new
            {
                Id = id,
                No = no,
                EncId = req.EncounterId,
                Pat = req.PatientName.Trim(),
                Bed = req.BedCode?.Trim(),
                Drug = req.DrugName.Trim(),
                Qty = req.ReturnQty <= 0 ? 1m : req.ReturnQty,
                Amt = Math.Max(0m, req.RefundAmount),
                Reason = req.Reason?.Trim() ?? "停嘱/出院余药退回",
                App = ActorName,
                Now = now,
                Actor = ActorId
            });

        return ApiResponse<object>.Success(new { Id = id, ReturnNo = no }, $"退药申请 {no} 已提交，待住院药房审核退费");
    }

    [HttpPost("drug-returns/{id}/review")]
    public async Task<ApiResponse<object>> ReviewDrugReturn(string id, [FromBody] ReviewDrugReturnDto req)
    {
        string decision = (req.Decision ?? "APPROVED").Trim().ToUpperInvariant();
        if (decision != "APPROVED" && decision != "REJECTED")
            return ApiResponse<object>.Fail(400, "审批决定必须为 APPROVED 或 REJECTED");

        using var conn = _connFactory.CreateConnection();
        var item = await conn.QueryFirstOrDefaultAsync<dynamic>("SELECT * FROM ipd_drug_returns WHERE id = @Id", new { Id = id });
        if (item == null) return ApiResponse<object>.Fail(404, "未找到退药申请");
        if ((string)item.status != "APPLIED") return ApiResponse<object>.Fail(400, "该退药单已审批完成");

        long now = SystemTime.UtcNowMs;
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(
            "UPDATE ipd_drug_returns SET status = @St, reviewer_name = @Rev, reviewed_at_ms = @Now, updated_at_ms = @Now, updated_by = @Actor WHERE id = @Id",
            new { St = decision, Rev = ActorName, Now = now, Actor = ActorId, Id = id }, tx);

        if (decision == "APPROVED" && Convert.ToDecimal(item.refund_amount) > 0m)
        {
            decimal negAmt = -Math.Abs(Convert.ToDecimal(item.refund_amount));
            await conn.ExecuteAsync(@"
                INSERT INTO fin_charge_ledgers
                (id, patient_id, visit_or_encounter_id, order_dept_id, patient_ward_id, exec_dept_id,
                 item_code, item_name, quantity, unit_price, total_amount, status, ext_data_json,
                 schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES
                (@Id, @Pat, @Enc, 'IPD_WARD', 'IPD_WARD', 'IPD_PHARMACY',
                 'DRUG_RETURN_REFUND', @ItemName, @Qty, @Amt, @Amt, 1, @Ext,
                 1, @Now, @Actor, @Now, @Actor, 1)",
                new
                {
                    Id = Guid.NewGuid().ToString("D"),
                    Pat = (string)item.patient_name,
                    Enc = (string?)item.encounter_id ?? id,
                    ItemName = $"病区退药冲正: {(string)item.drug_name} ({(string)item.return_no})",
                    Qty = -Math.Abs(Convert.ToDecimal(item.return_qty)),
                    Amt = negAmt,
                    Ext = $"{{\"returnNo\":\"{(string)item.return_no}\"}}",
                    Now = now,
                    Actor = ActorId
                }, tx);
        }
        if (decision == "APPROVED")
        {
            // 闭环：退药审批通过后同步回补药房有效批次库存并写入 inv_stock_ledgers
            int retQtyInt = Math.Max(1, (int)Math.Round(Convert.ToDecimal(item.return_qty)));
            var batch = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT b.id AS BatchId, b.warehouse_or_pharmacy_id AS WhId, b.drug_id AS DrugId, b.quantity AS Qty
                FROM inv_drug_batches b
                INNER JOIN inv_drugs d ON d.id = b.drug_id
                WHERE d.drug_name = @DrugName AND b.is_frozen = 0
                ORDER BY b.expiry_date_int DESC LIMIT 1",
                new { DrugName = (string)item.drug_name }, tx);
            if (batch != null)
            {
                int newBal = Convert.ToInt32(batch.Qty) + retQtyInt;
                await conn.ExecuteAsync(
                    "UPDATE inv_drug_batches SET quantity = @Qty, updated_at_ms = @Now, updated_by = @Actor WHERE id = @Bid",
                    new { Qty = newBal, Now = now, Actor = ActorId, Bid = (string)batch.BatchId }, tx);
                await conn.ExecuteAsync(@"
                    INSERT INTO inv_stock_ledgers
                    (id, warehouse_or_pharmacy_id, drug_id, batch_id, change_type, change_quantity, balance_after, reference_doc_no, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                    VALUES
                    (@Id, @Wh, @Did, @Bid, 'WARD_DRUG_RETURN', @Chg, @Bal, @Ref, 1, @Now, @Actor, @Now, @Actor, 1)",
                    new { Id = Guid.NewGuid().ToString("D"), Wh = (string)batch.WhId, Did = (string)batch.DrugId, Bid = (string)batch.BatchId, Chg = retQtyInt, Bal = newBal, Ref = (string)item.return_no, Now = now, Actor = ActorId }, tx);
            }
        }
        tx.Commit();

        return ApiResponse<object>.Success(new { Id = id, Status = decision }, decision == "APPROVED" ? "药房已确认退药、回补批次库存并写入负数冲正费用" : "已驳回该退药申请");
    }

    [HttpGet("discharge-meds")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetDischargeMeds()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT id AS Id, rx_no AS RxNo, encounter_id AS EncounterId, patient_name AS PatientName,
                   medications_summary AS MedicationsSummary, total_amount AS TotalAmount,
                   prescribed_by AS PrescribedBy, status AS Status, dispensed_at_ms AS DispensedAtMs
            FROM ipd_discharge_medications ORDER BY created_at_ms DESC LIMIT 200")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("discharge-meds")]
    public async Task<ApiResponse<object>> CreateDischargeMed([FromBody] CreateDischargeMedDto req)
    {
        if (string.IsNullOrWhiteSpace(req.PatientName) || string.IsNullOrWhiteSpace(req.MedicationsSummary))
            return ApiResponse<object>.Fail(400, "患者姓名与带药明细不能为空");

        long now = SystemTime.UtcNowMs;
        string id = Guid.NewGuid().ToString("D");
        string no = "CYDY" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss");

        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO ipd_discharge_medications
            (id, rx_no, encounter_id, patient_name, medications_summary, total_amount, prescribed_by,
             status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES
            (@Id, @No, @EncId, @Pat, @Summary, @Amt, @Doc,
             'PRESCRIBED', 1, @Now, @Actor, @Now, @Actor, 1)",
            new
            {
                Id = id,
                No = no,
                EncId = req.EncounterId,
                Pat = req.PatientName.Trim(),
                Summary = req.MedicationsSummary.Trim(),
                Amt = Math.Max(0m, req.TotalAmount),
                Doc = ActorName,
                Now = now,
                Actor = ActorId
            });

        return ApiResponse<object>.Success(new { Id = id, RxNo = no }, $"出院带药专单 {no} 已开立");
    }

    [HttpPost("discharge-meds/{id}/dispense")]
    public async Task<ApiResponse<object>> DispenseDischargeMed(string id)
    {
        using var conn = _connFactory.CreateConnection();
        var item = await conn.QueryFirstOrDefaultAsync<dynamic>("SELECT * FROM ipd_discharge_medications WHERE id = @Id", new { Id = id });
        if (item == null) return ApiResponse<object>.Fail(404, "未找到出院带药单");
        if ((string)item.status == "DISPENSED") return ApiResponse<object>.Fail(400, "该出院带药单已完成发药");

        long now = SystemTime.UtcNowMs;
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(
            "UPDATE ipd_discharge_medications SET status = 'DISPENSED', dispensed_at_ms = @Now, updated_at_ms = @Now, updated_by = @Actor WHERE id = @Id",
            new { Now = now, Actor = ActorId, Id = id }, tx);

        decimal amt = Convert.ToDecimal(item.total_amount);
        if (amt > 0m)
        {
            await conn.ExecuteAsync(@"
                INSERT INTO fin_charge_ledgers
                (id, patient_id, visit_or_encounter_id, order_dept_id, exec_dept_id, item_code, item_name, quantity, unit_price, total_amount, status, ext_data_json, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES
                (@Id, @Pat, @Enc, 'IPD_WARD', 'IPD_PHARMACY', 'IPD_DISCHARGE_MED', @Name, 1, @Amt, @Amt, 1, @Ext, 1, @Now, @Actor, @Now, @Actor, 1)",
                new { Id = Guid.NewGuid().ToString("D"), Pat = (string)item.patient_name, Enc = (string?)item.encounter_id ?? id, Name = $"出院带药 ({(string)item.rx_no}): {(string)item.medications_summary}", Amt = amt, Ext = $"{{\"rxNo\":\"{(string)item.rx_no}\"}}", Now = now, Actor = ActorId }, tx);
        }

        // 扣减匹配药品的药房有效库存批次
        string summary = (string)item.medications_summary;
        var matchedBatch = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT b.id AS BatchId, b.warehouse_or_pharmacy_id AS WhId, b.drug_id AS DrugId, b.quantity AS Qty
            FROM inv_drug_batches b
            INNER JOIN inv_drugs d ON d.id = b.drug_id
            WHERE b.is_frozen = 0 AND b.quantity > 0 AND @Sum LIKE '%' || d.drug_name || '%'
            ORDER BY b.expiry_date_int ASC LIMIT 1",
            new { Sum = summary }, tx);
        if (matchedBatch != null)
        {
            int newBal = Math.Max(0, Convert.ToInt32(matchedBatch.Qty) - 1);
            await conn.ExecuteAsync("UPDATE inv_drug_batches SET quantity = @Qty, updated_at_ms = @Now WHERE id = @Bid", new { Qty = newBal, Now = now, Bid = (string)matchedBatch.BatchId }, tx);
            await conn.ExecuteAsync(@"
                INSERT INTO inv_stock_ledgers
                (id, warehouse_or_pharmacy_id, drug_id, batch_id, change_type, change_quantity, balance_after, reference_doc_no, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES
                (@Id, @Wh, @Did, @Bid, 'DISCHARGE_DISPENSE', -1, @Bal, @Ref, 1, @Now, @Actor, @Now, @Actor, 1)",
                new { Id = Guid.NewGuid().ToString("D"), Wh = (string)matchedBatch.WhId, Did = (string)matchedBatch.DrugId, Bid = (string)matchedBatch.BatchId, Bal = newBal, Ref = (string)item.rx_no, Now = now, Actor = ActorId }, tx);
        }
        tx.Commit();
        return ApiResponse<object>.Success(new { Id = id, Status = "DISPENSED" }, "已完成出院带药发药核对、库存扣减与住院记账");
    }

    // ===================== 4. 专科扩展：产科分娩新生儿 / 日间手术24h / 陪护探视证 =====================

    [HttpGet("maternity")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetMaternityRecords()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT id AS Id, delivery_no AS DeliveryNo, mother_encounter_id AS MotherEncounterId,
                   mother_name AS MotherName, gestational_weeks AS GestationalWeeks,
                   delivery_mode AS DeliveryMode, newborn_gender AS NewbornGender,
                   birth_weight_g AS BirthWeightG, birth_length_cm AS BirthLengthCm,
                   apgar_scores AS ApgarScores, birth_cert_pre_no AS BirthCertPreNo,
                   delivered_at_ms AS DeliveredAtMs
            FROM spec_maternity_records ORDER BY delivered_at_ms DESC LIMIT 200")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("maternity")]
    public async Task<ApiResponse<object>> CreateMaternityRecord([FromBody] CreateMaternityDto req)
    {
        if (string.IsNullOrWhiteSpace(req.MotherName))
            return ApiResponse<object>.Fail(400, "产妇姓名不能为空");

        long now = SystemTime.UtcNowMs;
        string id = Guid.NewGuid().ToString("D");
        string no = "FM" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss");
        string birthCertPre = "CSZ-" + SystemTime.ShanghaiNow.ToString("yyyyMMdd") + "-" + Random.Shared.Next(1000, 9999);

        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO spec_maternity_records
            (id, delivery_no, mother_encounter_id, mother_name, gestational_weeks, delivery_mode,
             newborn_gender, birth_weight_g, birth_length_cm, apgar_scores, birth_cert_pre_no, delivered_at_ms,
             schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES
            (@Id, @No, @EncId, @Mother, @Weeks, @Mode,
             @Gender, @Weight, @Len, @Apgar, @Cert, @Now,
             1, @Now, @Actor, @Now, @Actor, 1)",
            new
            {
                Id = id,
                No = no,
                EncId = req.MotherEncounterId,
                Mother = req.MotherName.Trim(),
                Weeks = string.IsNullOrWhiteSpace(req.GestationalWeeks) ? "39+2周" : req.GestationalWeeks.Trim(),
                Mode = string.IsNullOrWhiteSpace(req.DeliveryMode) ? "顺产" : req.DeliveryMode.Trim(),
                Gender = string.IsNullOrWhiteSpace(req.NewbornGender) ? "男" : req.NewbornGender.Trim(),
                Weight = req.BirthWeightG > 0 ? req.BirthWeightG : 3250,
                Len = req.BirthLengthCm > 0 ? req.BirthLengthCm : 50.0m,
                Apgar = string.IsNullOrWhiteSpace(req.ApgarScores) ? "10-10-10" : req.ApgarScores.Trim(),
                Cert = birthCertPre,
                Now = now,
                Actor = ActorId
            });

        return ApiResponse<object>.Success(new { Id = id, DeliveryNo = no, BirthCertPreNo = birthCertPre }, $"已完成分娩登记与新生儿母婴关联建档（出生证预编号：{birthCertPre}）");
    }

    [HttpGet("visitor-passes")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetVisitorPasses()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT id AS Id, pass_no AS PassNo, encounter_id AS EncounterId,
                   patient_name AS PatientName, bed_code AS BedCode,
                   visitor_name AS VisitorName, relation_name AS RelationName,
                   phone_masked AS PhoneMasked, pass_type AS PassType,
                   valid_until_int AS ValidUntilInt, status AS Status
            FROM ipd_visitor_passes ORDER BY created_at_ms DESC LIMIT 200")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("visitor-passes")]
    public async Task<ApiResponse<object>> CreateVisitorPass([FromBody] CreateVisitorPassDto req)
    {
        if (string.IsNullOrWhiteSpace(req.PatientName) || string.IsNullOrWhiteSpace(req.VisitorName))
            return ApiResponse<object>.Fail(400, "患者姓名与陪护/探视人姓名不能为空");

        long now = SystemTime.UtcNowMs;
        string id = Guid.NewGuid().ToString("D");
        string no = "PHZ" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss");
        int days = req.ValidDays > 0 ? req.ValidDays : 7;
        int validUntil = int.Parse(SystemTime.ShanghaiNow.AddDays(days).ToString("yyyyMMdd"));
        string masked = string.IsNullOrWhiteSpace(req.Phone) ? "-" : CryptoHelper.MaskPhone(req.Phone.Trim());

        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO ipd_visitor_passes
            (id, pass_no, encounter_id, patient_name, bed_code, visitor_name, relation_name,
             phone_masked, pass_type, valid_until_int, status,
             schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES
            (@Id, @No, @EncId, @Pat, @Bed, @Vis, @Rel,
             @Phone, @Type, @Until, 'ACTIVE',
             1, @Now, @Actor, @Now, @Actor, 1)",
            new
            {
                Id = id,
                No = no,
                EncId = req.EncounterId,
                Pat = req.PatientName.Trim(),
                Bed = req.BedCode?.Trim(),
                Vis = req.VisitorName.Trim(),
                Rel = string.IsNullOrWhiteSpace(req.RelationName) ? "配偶" : req.RelationName.Trim(),
                Phone = masked,
                Type = string.IsNullOrWhiteSpace(req.PassType) ? "ESCORT" : req.PassType.Trim().ToUpperInvariant(),
                Until = validUntil,
                Now = now,
                Actor = ActorId
            });

        return ApiResponse<object>.Success(new { Id = id, PassNo = no, ValidUntilInt = validUntil }, $"已签发陪护/探视证 {no}");
    }

    [HttpPut("visitor-passes/{id}/revoke")]
    public async Task<ApiResponse<object>> RevokeVisitorPass(string id)
    {
        using var conn = _connFactory.CreateConnection();
        int n = await conn.ExecuteAsync(
            "UPDATE ipd_visitor_passes SET status = 'REVOKED', updated_at_ms = @Now, updated_by = @Actor WHERE id = @Id",
            new { Now = SystemTime.UtcNowMs, Actor = ActorId, Id = id });
        if (n == 0) return ApiResponse<object>.Fail(404, "未找到该陪护证");
        return ApiResponse<object>.Success(new { Id = id, Status = "REVOKED" }, "已注销该陪护/探视证");
    }

    [HttpGet("day-surgery")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetDaySurgeryCases()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT id AS Id, case_no AS CaseNo, patient_name AS PatientName,
                   gender AS Gender, age AS Age, dept_name AS DeptName,
                   surgery_name AS SurgeryName, surgeon_name AS SurgeonName,
                   padss_score AS PadssScore, status AS Status,
                   admitted_at_ms AS AdmittedAtMs, discharged_at_ms AS DischargedAtMs, remark AS Remark
            FROM ipd_day_surgery_cases ORDER BY admitted_at_ms DESC LIMIT 200")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("day-surgery")]
    public async Task<ApiResponse<object>> CreateDaySurgeryCase([FromBody] CreateDaySurgeryDto req)
    {
        if (string.IsNullOrWhiteSpace(req.PatientName) || string.IsNullOrWhiteSpace(req.SurgeryName))
            return ApiResponse<object>.Fail(400, "患者姓名与日间手术名称不能为空");

        long now = SystemTime.UtcNowMs;
        string id = Guid.NewGuid().ToString("D");
        string no = "RJSS" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss");

        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO ipd_day_surgery_cases
            (id, case_no, patient_name, gender, age, dept_name, surgery_name, surgeon_name,
             padss_score, status, admitted_at_ms, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES
            (@Id, @No, @Pat, @Gender, @Age, @Dept, @Surg, @Doc,
             0, 'ADMITTED', @Now, 1, @Now, @Actor, @Now, @Actor, 1)",
            new
            {
                Id = id,
                No = no,
                Pat = req.PatientName.Trim(),
                Gender = string.IsNullOrWhiteSpace(req.Gender) ? "男" : req.Gender.Trim(),
                Age = req.Age > 0 ? req.Age : 38,
                Dept = string.IsNullOrWhiteSpace(req.DeptName) ? "日间手术中心" : req.DeptName.Trim(),
                Surg = req.SurgeryName.Trim(),
                Doc = string.IsNullOrWhiteSpace(req.SurgeonName) ? ActorName : req.SurgeonName.Trim(),
                Now = now,
                Actor = ActorId
            });

        return ApiResponse<object>.Success(new { Id = id, CaseNo = no }, $"已登记 24h 日间手术 {no}");
    }

    [HttpPut("day-surgery/{id}/status")]
    public async Task<ApiResponse<object>> UpdateDaySurgeryStatus(string id, [FromBody] UpdateDaySurgeryDto req)
    {
        string st = (req.Status ?? "OBSERVING").Trim().ToUpperInvariant();
        if (st == "DISCHARGED" && req.PadssScore < 9)
            return ApiResponse<object>.Fail(400, $"PADSS 离院评分仅 {req.PadssScore} 分（需 ≥ 9 分方可日间离院，否则请留观或转常规住院）");

        long now = SystemTime.UtcNowMs;
        long? dischargedAt = st == "DISCHARGED" ? now : null;
        using var conn = _connFactory.CreateConnection();
        int n = await conn.ExecuteAsync(@"
            UPDATE ipd_day_surgery_cases
            SET status = @St, padss_score = @Padss, remark = @Remark,
                discharged_at_ms = COALESCE(@Disch, discharged_at_ms),
                updated_at_ms = @Now, updated_by = @Actor
            WHERE id = @Id",
            new { St = st, Padss = req.PadssScore, Remark = req.Remark?.Trim(), Disch = dischargedAt, Now = now, Actor = ActorId, Id = id });
        if (n == 0) return ApiResponse<object>.Fail(404, "未找到该日间手术记录");
        if (st == "CONVERTED_IPD")
        {
            var dsRow = await conn.QueryFirstOrDefaultAsync<dynamic>("SELECT case_no AS CaseNo, patient_name AS Pat, dept_name AS Dept, surgery_name AS Surg FROM ipd_day_surgery_cases WHERE id = @Id", new { Id = id });
            if (dsRow != null)
            {
                string cardNo = "ADM-DS-" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss") + Random.Shared.Next(10, 99);
                await conn.ExecuteAsync(@"
                    INSERT OR IGNORE INTO ipd_admission_cards
                    (id, card_no, request_key, patient_id, source_doctor_id, source_dept_id, suggested_ipd_dept_id, admission_diagnosis, remark, status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                    VALUES
                    (@Id, @CardNo, @ReqKey, @Pat, @Actor, 'DAY_SURGERY', @Dept, @Diag, @Remark, 'PENDING', 1, @Now, @Actor, @Now, @Actor, 1)",
                    new { Id = Guid.NewGuid().ToString("D"), CardNo = cardNo, ReqKey = "DS_ADM_" + id, Pat = (string)dsRow.Pat, Actor = ActorId, Dept = (string)dsRow.Dept, Diag = $"日间手术超期转住院：{(string)dsRow.Surg}", Remark = $"日间手术单号 {(string)dsRow.CaseNo}，PADSS={req.PadssScore}", Now = now });
            }
        }
        return ApiResponse<object>.Success(new { Id = id, Status = st, req.PadssScore }, st == "CONVERTED_IPD" ? "已超期转常规住院并自动签发住院入院证" : "已更新日间手术状态与 PADSS 评分");
    }
}
