using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Inpatient.Controllers;

/// <summary>
/// 深水区临床安全与运营细节闭环控制器（覆盖 R-03, R-04, R-06, R-07, R-08, R-10）：
/// - R-03: 临床死亡宣告（强制停止全部长嘱 + 阻断死后滚费 + 签发死亡医学证明书）与非医嘱自动出院 (AMA)
/// - R-04: 住院欠费分级授信担保与 ICU/抢救绿色通道自动豁免
/// - R-06: 检验多项目按标本管类型（黄头/紫头/蓝头）自动合管与采血费去重
/// - R-07: “一次挂号管三天”0元续诊号与门诊处方 72 小时超期自动作废
/// - R-08: 危急值超 30 分钟未双签自动升级至科主任/总值班
/// - R-10: 超期 30 天未执行已缴费医技单财务挂账扫描与退费提醒
/// </summary>
[ApiController]
[RequireHisRole("ADMIN", "OPD_DOC", "IPD_ATTENDING", "IPD_RESIDENT", "WARD_NURSE", "CASHIER", "MEDTECH", "PHARMACIST")]
[Route("api/v4/deep-safety")]
public sealed class DeepSafetyClosuresController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _session;

    public DeepSafetyClosuresController(IDbConnectionFactory connFactory, IUserSession? session = null)
    {
        _connFactory = connFactory;
        _session = session;
    }

    private string ActorId => _session?.UserId?.Trim() ?? "SYSTEM";
    private string ActorName => _session?.DisplayName ?? _session?.UserName ?? ActorId;

    public sealed record DeclareDeathDto(string EncounterId, string PatientId, string PatientName, string DeptName, long? DeathTimeMs, string DirectCauseIcd, string DirectCauseName, string? UnderlyingCauseIcd, string? UnderlyingCauseName);
    public sealed record AmaDischargeDto(string EncounterId, string PatientId, string PatientName, string RiskExplanation, string FamilySignerName, string FamilyRelation, string FamilySignSvg);
    public sealed record GrantCreditDto(string EncounterId, string PatientId, decimal CreditLimitAmount, int ValidHours, string GuarantorRole, string Reason);
    public sealed record MergeLabTubesDto(string VisitOrEncounterId, string PatientId, List<string> LabItemNames);
    public sealed record ThreeDayRevisitDto(string PatientId, string OpdDeptId, string TargetScheduleId);

    // ===================== R-03: 死亡宣告强制停嘱停费 & AMA 自动出院 =====================

    [HttpPost("death-declare")]
    public async Task<ApiResponse<object>> DeclareClinicalDeath([FromBody] DeclareDeathDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.EncounterId) || string.IsNullOrWhiteSpace(dto.DirectCauseIcd) || string.IsNullOrWhiteSpace(dto.DirectCauseName))
            return ApiResponse<object>.Fail(400, "住院就诊ID、直接死因 ICD-10 编码及名称不能为空。");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long deathMs = dto.DeathTimeMs ?? now;
        string certId = Guid.NewGuid().ToString("N");
        string certNo = $"DEATH{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(10, 99)}";

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // 1) 强制停止该患者全部在执行长期/临时医嘱，杜绝死后继续执行与计费
        int stoppedOrders = await conn.ExecuteAsync(@"
UPDATE ord_medical_orders
SET status = 'STOPPED_DECEASED', plan_stop_at_ms = @DeathMs, updated_at_ms = @Now, updated_by = @Actor
WHERE encounter_id = @EncId AND status NOT IN ('STOPPED', 'STOPPED_DECEASED', 'VOIDED', 'COMPLETED');",
            new { DeathMs = deathMs, Now = now, Actor = ActorId, EncId = dto.EncounterId.Trim() }, tx);

        // 2) 标记住院记录为已故 (is_deceased = 1, status = 9)，自动从次日滚费中剔除
        await conn.ExecuteAsync(@"
UPDATE ipd_encounters
SET is_deceased = 1, status = 9, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1
WHERE id = @EncId;", new { Now = now, Actor = ActorId, EncId = dto.EncounterId.Trim() }, tx);

        // 3) 签发《居民死亡医学证明（推断）书》
        await conn.ExecuteAsync(@"
INSERT INTO emr_death_certificates (
    id, cert_no, encounter_id, patient_id, patient_name, dept_name, death_time_ms,
    direct_cause_icd, direct_cause_name, underlying_cause_icd, underlying_cause_name,
    declaring_doctor, morgue_handover_status, stopped_orders_count, created_at_ms, updated_at_ms
) VALUES (
    @Id, @No, @EncId, @PatId, @PatName, @Dept, @DeathMs,
    @DirIcd, @DirName, @UndIcd, @UndName,
    @Doc, 'PENDING_HANDOVER', @Stopped, @Now, @Now
);", new
        {
            Id = certId, No = certNo, EncId = dto.EncounterId.Trim(), PatId = dto.PatientId.Trim(),
            PatName = dto.PatientName.Trim(), Dept = dto.DeptName.Trim(), DeathMs = deathMs,
            DirIcd = dto.DirectCauseIcd.Trim().ToUpperInvariant(), DirName = dto.DirectCauseName.Trim(),
            UndIcd = dto.UnderlyingCauseIcd, UndName = dto.UnderlyingCauseName,
            Doc = ActorName, Stopped = stoppedOrders, Now = now
        }, tx);

        await tx.CommitAsync(ct);
        return ApiResponse<object>.Success(new
        {
            certificateId = certId,
            certNo,
            stoppedOrdersCount = stoppedOrders,
            dailyBillingHalted = true
        }, "已宣告临床死亡，强制停止全部未结医嘱与自动滚费，并签发《居民死亡医学证明书》。");
    }

    [HttpPost("ama-discharge")]
    public async Task<ApiResponse<object>> RegisterAmaDischarge([FromBody] AmaDischargeDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.FamilySignerName) || string.IsNullOrWhiteSpace(dto.FamilySignSvg))
            return ApiResponse<object>.Fail(400, "非医嘱自动出院(AMA)必须采集患者或直系亲属电子手写签名！");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string id = Guid.NewGuid().ToString("N");
        string amaNo = $"AMA{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(10, 99)}";

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        await conn.ExecuteAsync(@"
INSERT INTO emr_ama_discharges (
    id, ama_no, encounter_id, patient_id, patient_name, risk_explanation,
    family_signer_name, family_relation, family_sign_svg, attending_doctor, signed_at_ms, created_at_ms
) VALUES (
    @Id, @No, @EncId, @PatId, @PatName, @Risk,
    @Signer, @Rel, @Svg, @Doc, @Now, @Now
);", new
        {
            Id = id, No = amaNo, EncId = dto.EncounterId.Trim(), PatId = dto.PatientId.Trim(),
            PatName = dto.PatientName.Trim(), Risk = dto.RiskExplanation.Trim(),
            Signer = dto.FamilySignerName.Trim(), Rel = dto.FamilyRelation.Trim(),
            Svg = dto.FamilySignSvg, Doc = ActorName, Now = now
        });

        return ApiResponse<object>.Success(new { id, amaNo, status = "SIGNED_AMA" });
    }

    // ===================== R-04: 住院欠费分级授信担保与 ICU/抢救豁免 =====================

    [HttpPost("credit-guarantees")]
    public async Task<ApiResponse<object>> GrantCreditGuarantee([FromBody] GrantCreditDto dto, CancellationToken ct)
    {
        if (dto.CreditLimitAmount <= 0 || string.IsNullOrWhiteSpace(dto.Reason))
            return ApiResponse<object>.Fail(400, "授信担保金额必须大于 0 且必须说明担保事由。");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int hours = dto.ValidHours is > 0 and <= 168 ? dto.ValidHours : 48;
        long expireAt = now + (hours * 3600_000L);
        string id = Guid.NewGuid().ToString("N");
        string no = $"CG{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(10, 99)}";

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(@"
INSERT INTO ipd_credit_guarantees (
    id, guarantee_no, encounter_id, patient_id, credit_limit_amount, expire_at_ms,
    guarantor_name, guarantor_role, reason, status, created_at_ms
) VALUES (
    @Id, @No, @EncId, @PatId, @Amt, @Exp, @Name, @Role, @Reason, 'ACTIVE', @Now
);", new
        {
            Id = id, No = no, EncId = dto.EncounterId.Trim(), PatId = dto.PatientId.Trim(),
            Amt = dto.CreditLimitAmount, Exp = expireAt, Name = ActorName,
            Role = string.IsNullOrWhiteSpace(dto.GuarantorRole) ? "DEPT_DIRECTOR" : dto.GuarantorRole.Trim().ToUpperInvariant(),
            Reason = dto.Reason.Trim(), Now = now
        }, tx);

        await conn.ExecuteAsync(@"
UPDATE ipd_encounters
SET credit_limit_amount = @Amt, credit_expire_at_ms = @Exp, updated_at_ms = @Now
WHERE id = @EncId;", new { Amt = dto.CreditLimitAmount, Exp = expireAt, Now = now, EncId = dto.EncounterId.Trim() }, tx);

        await tx.CommitAsync(ct);
        return ApiResponse<object>.Success(new { id, guaranteeNo = no, creditLimitAmount = dto.CreditLimitAmount, expireAtMs = expireAt });
    }

    [HttpGet("encounters/{encounterId}/arrear-check")]
    public async Task<ApiResponse<object>> CheckArrearAndRescueExemption(string encounterId, CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var enc = await conn.QueryFirstOrDefaultAsync(
            "SELECT id, prepayment_balance, credit_limit_amount, credit_expire_at_ms, is_deceased FROM ipd_encounters WHERE id = @Id",
            new { Id = encounterId });
        if (enc == null) return ApiResponse<object>.Fail(404, "住院记录不存在。");

        decimal prepay = Convert.ToDecimal(enc.prepayment_balance);
        decimal totalCharged = await conn.ExecuteScalarAsync<decimal>(
            "SELECT COALESCE(SUM(total_amount), 0) FROM fin_charge_ledgers WHERE visit_or_encounter_id = @Id AND status <> 'VOIDED'",
            new { Id = encounterId });
        decimal netBalance = prepay - totalCharged;

        decimal activeCredit = (enc.credit_expire_at_ms != null && Convert.ToInt64(enc.credit_expire_at_ms) > now)
            ? Convert.ToDecimal(enc.credit_limit_amount) : 0m;

        int inIcu = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM icu_admissions WHERE encounter_id = @Id AND status = 'IN_ICU'", new { Id = encounterId });
        int hasCritical = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM emr_critical_notices WHERE encounter_id = @Id", new { Id = encounterId });

        bool isRescueExempt = inIcu > 0 || hasCritical > 0;
        bool allowed = isRescueExempt || (netBalance + activeCredit >= 0m);

        return ApiResponse<object>.Success(new
        {
            encounterId,
            prepaymentBalance = prepay,
            totalCharged,
            netBalance,
            activeCreditLimit = activeCredit,
            isRescueExempt,
            allowOrderAndDispense = allowed,
            policyNote = isRescueExempt
                ? "ICU/病危抢救绿色通道：自动豁免欠费阻断，优先保障抢救用药与检查。"
                : allowed
                    ? "可用余额或授信担保额度充足，允许正常诊疗。"
                    : "余额不足且超出授信额度，请向患者推送催缴单或由科主任签发临时担保。"
        });
    }

    // ===================== R-06: 检验多项目自动合管与采血费去重 =====================

    [HttpPost("lab/merge-tubes")]
    public async Task<ApiResponse<object>> MergeLabSpecimenTubes([FromBody] MergeLabTubesDto dto, CancellationToken ct)
    {
        if (dto.LabItemNames == null || dto.LabItemNames.Count == 0)
            return ApiResponse<object>.Fail(400, "检验项目列表不能为空。");

        static (string Color, string Specimen) ClassifyTube(string itemName)
        {
            if (itemName.Contains("血常规") || itemName.Contains("糖化") || itemName.Contains("血型"))
                return ("PURPLE_EDTA", "全血(紫头EDTA管)");
            if (itemName.Contains("凝血") || itemName.Contains("D-二聚体") || itemName.Contains("PT"))
                return ("BLUE_CITRATE", "血浆(蓝头枸橼酸钠管)");
            return ("YELLOW_SST", "血清(黄头促凝分离胶管)");
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var grouped = dto.LabItemNames.GroupBy(ClassifyTube).ToList();
        var tubes = new List<object>();

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        foreach (var g in grouped)
        {
            string id = Guid.NewGuid().ToString("N");
            string barcode = $"TUBE-{DateTime.UtcNow:yyyyMMdd}-{g.Key.Color[..3]}-{id[..6].ToUpperInvariant()}";
            var items = g.ToList();
            await conn.ExecuteAsync(@"
INSERT INTO mtc_specimen_tube_merges (
    id, tube_barcode, visit_or_encounter_id, patient_id, tube_color, specimen_type,
    merged_items_json, item_count, collection_fee_count, created_at_ms
) VALUES (
    @Id, @Bc, @Vid, @Pid, @Color, @Spec, @Json, @Cnt, 1, @Now
);", new
            {
                Id = id, Bc = barcode, Vid = dto.VisitOrEncounterId.Trim(), Pid = dto.PatientId.Trim(),
                Color = g.Key.Color, Spec = g.Key.Specimen, Json = JsonSerializer.Serialize(items),
                Cnt = items.Count, Now = now
            });

            tubes.Add(new { tubeBarcode = barcode, tubeColor = g.Key.Color, specimenType = g.Key.Specimen, mergedItems = items, tubeCount = 1 });
        }

        return ApiResponse<object>.Success(new
        {
            originalItemCount = dto.LabItemNames.Count,
            mergedTubeCount = tubes.Count,
            venipunctureFeeCount = 1, // 同一次采血仅收 1 次静脉采血费
            tubes
        }, $"已将 {dto.LabItemNames.Count} 项检验智能合管为 {tubes.Count} 支采血管（仅计收 1 次静脉采血费）。");
    }

    // ===================== R-07: 一次挂号管三天 & 处方 72h 自动失效 =====================

    [HttpPost("opd/three-day-revisit")]
    public async Task<ApiResponse<object>> IssueThreeDayFreeRevisit([FromBody] ThreeDayRevisitDto dto, CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long cutoff72h = now - (72L * 3600_000L);

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        // 核验 72 小时内该患者是否已有有效挂号记录
        int hasRecentPaid = await conn.ExecuteScalarAsync<int>(@"
SELECT COUNT(1)
FROM opd_appointments a
JOIN opd_schedule_slots s ON s.id = a.schedule_id
WHERE a.patient_id = @Pid AND s.opd_dept_id = @DeptId
  AND a.status IN ('PAID', 'CHECKED_IN', 'COMPLETED')
  AND a.created_at_ms >= @Cutoff;", new { Pid = dto.PatientId.Trim(), DeptId = dto.OpdDeptId.Trim(), Cutoff = cutoff72h });

        if (hasRecentPaid == 0)
            return ApiResponse<object>.Fail(400, "该患者 72 小时内在本专科无有效首诊挂号记录，不符合“一次挂号管三天”免挂号费政策。");

        string apptId = Guid.NewGuid().ToString("N");
        string apptNo = $"REV0-{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(10, 99)}";
        await conn.ExecuteAsync(@"
INSERT INTO opd_appointments (
    id, appoint_no, schedule_id, patient_id, channel, status, paid_amount, payment_channel, paid_at_ms,
    schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no
) VALUES (
    @Id, @No, @SlotId, @Pid, 'THREE_DAY_FREE_REVISIT', 'PAID', 0, 'FREE_REVISIT', @Now,
    1, @Now, @Actor, @Now, @Actor, 1
);", new { Id = apptId, No = apptNo, SlotId = dto.TargetScheduleId.Trim(), Pid = dto.PatientId.Trim(), Now = now, Actor = ActorId });

        return ApiResponse<object>.Success(new { appointmentId = apptId, appointNo = apptNo, registrationFee = 0m, policy = "一次挂号管三天(0元续诊号)" });
    }

    [HttpPost("opd/expire-overdue-rx")]
    public async Task<ApiResponse<object>> ExpireOverduePrescriptions(CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long cutoff72h = now - (72L * 3600_000L);

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        int expired = await conn.ExecuteAsync(@"
UPDATE opd_prescriptions
SET status = 9, updated_at_ms = @Now, updated_by = 'SYSTEM_RX_EXPIRE'
WHERE status IN (0, 1)
  AND ((expires_at_ms IS NOT NULL AND expires_at_ms < @Now) OR (expires_at_ms IS NULL AND created_at_ms < @Cutoff));",
            new { Now = now, Cutoff = cutoff72h });

        return ApiResponse<object>.Success(new { expiredPrescriptionCount = expired, rule = "《处方管理办法》超 72 小时未缴费处方自动作废" });
    }

    // ===================== R-08: 危急值超 30 分钟未双签自动升级 =====================

    [HttpPost("critical-values/escalate-overdue")]
    public async Task<ApiResponse<object>> EscalateOverdueCriticalValues(CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long cutoff30m = now - (30L * 60_000L);

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var overdue = (await conn.QueryAsync(@"
SELECT id, patient_name, exam_item_name, critical_detail
FROM medtech_critical_values
WHERE status <> 'CLOSED' AND created_at_ms < @Cutoff;", new { Cutoff = cutoff30m })).ToList();

        int escalated = 0;
        foreach (var cv in overdue)
        {
            await conn.ExecuteAsync(@"
INSERT INTO ops_security_alerts (
    id, alert_type, severity, target_user_id, description, status,
    schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no
) VALUES (
    @Id, 'CRITICAL_VALUE_30M_ESCALATION', 'HIGH', 'DEPT_DIRECTOR_AND_DUTY', @Desc, 'OPEN',
    1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1
);", new
            {
                Id = Guid.NewGuid().ToString("N"),
                Desc = $"危急值超时30分钟未闭环升级告警：患者[{cv.patient_name}] {cv.exam_item_name} - {cv.critical_detail}",
                Now = now
            });
            escalated++;
        }

        return ApiResponse<object>.Success(new { escalatedCount = escalated, thresholdMinutes = 30 });
    }

    // ===================== R-10: 超 30 天未执行已缴费医技挂账扫描 =====================

    [HttpGet("medtech/stale-paid-orders")]
    public async Task<ApiResponse<object>> ListStalePaidMedTechOrders(CancellationToken ct)
    {
        long cutoff30d = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeMilliseconds();
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var rows = (await conn.QueryAsync(@"
SELECT id, patient_id, visit_or_encounter_id, item_code, item_name, total_amount, created_at_ms
FROM fin_charge_ledgers
WHERE status = 'SETTLED' AND created_at_ms < @Cutoff
ORDER BY created_at_ms ASC LIMIT 200;", new { Cutoff = cutoff30d })).ToList();

        return ApiResponse<object>.Success(new { staleCount = rows.Count, items = rows });
    }
}
