using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Patient.Controllers;

/// <summary>
/// 角色四（患者 C 端轻量化门户与自助服务 API）：
/// 1) 患者账号注册与家庭成员（老人/儿童）代绑
/// 2) 实时候诊排队查询（前面人数 + 预计等待分钟）
/// 3) 自动生成含楼层定位与空腹/憋尿注意事项的就诊指引单 (cfg_exam_prep_instructions)
/// 4) 患者脱敏查检验检查报告与住院每日费用清单
/// 5) 知情同意书电子手写签名回填 (srv_informed_consents)
/// 6) 病历复印在线申请、病案室审核与快递单号邮寄全闭环 (mrms_copy_requests)
/// </summary>
[ApiController]
[RequireHisRole("ADMIN", "OPD_DOC", "IPD_ATTENDING", "WARD_NURSE", "CASHIER")]
[Route("api/v4/portal")]
public sealed class PatientPortalController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _session;

    public PatientPortalController(IDbConnectionFactory connFactory, IUserSession? session = null)
    {
        _connFactory = connFactory;
        _session = session;
    }

    private string ActorName => _session?.DisplayName ?? _session?.UserName ?? _session?.UserId ?? "PORTAL";

    public sealed record RegisterPortalAccountDto(string PhoneNumber, string DisplayName, string PrimaryPatientId, string? WechatOpenId);
    public sealed record BindFamilyMemberDto(string PatientId, string PatientName, string Relation);
    public sealed record SignConsentDto(string SignerName, string SignerType, string SignatureHashOrSvg);
    public sealed record CreateCopyRequestDto(string PatientId, string PatientName, string EncounterId, string Purpose, int CopiesCount, string DeliveryMode, string? MailAddress, string? ContactPhone);
    public sealed record ReviewCopyRequestDto(bool Approved, string? ExpressNo);

    [HttpPost("accounts/register")]
    public async Task<ApiResponse<object>> RegisterAccount([FromBody] RegisterPortalAccountDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.PhoneNumber) || string.IsNullOrWhiteSpace(dto.PrimaryPatientId))
            return ApiResponse<object>.Fail(400, "手机号与主就诊人ID不能为空。");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string id = Guid.NewGuid().ToString("N");
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var existingId = await conn.ExecuteScalarAsync<string?>(
            "SELECT id FROM pat_portal_accounts WHERE phone_number = @Phone", new { Phone = dto.PhoneNumber.Trim() });
        if (existingId != null)
            return ApiResponse<object>.Success(new { id = existingId, phoneNumber = dto.PhoneNumber.Trim(), alreadyRegistered = true });

        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync(@"
INSERT INTO pat_portal_accounts (id, phone_number, display_name, wechat_openid, primary_patient_id, is_active, created_at_ms, updated_at_ms)
VALUES (@Id, @Phone, @Name, @OpenId, @Pid, 1, @Now, @Now);", new
        {
            Id = id, Phone = dto.PhoneNumber.Trim(), Name = dto.DisplayName.Trim(),
            OpenId = dto.WechatOpenId, Pid = dto.PrimaryPatientId.Trim(), Now = now
        }, tx);

        await conn.ExecuteAsync(@"
INSERT INTO pat_family_bindings (id, portal_account_id, patient_id, patient_name, relation, is_default, created_at_ms)
VALUES (@Bid, @AccId, @Pid, @Name, 'SELF', 1, @Now);", new
        {
            Bid = Guid.NewGuid().ToString("N"), AccId = id, Pid = dto.PrimaryPatientId.Trim(), Name = dto.DisplayName.Trim(), Now = now
        }, tx);
        await tx.CommitAsync(ct);

        return ApiResponse<object>.Success(new { id, phoneNumber = dto.PhoneNumber.Trim(), primaryPatientId = dto.PrimaryPatientId.Trim() });
    }

    [HttpPost("accounts/{accountId}/family")]
    public async Task<ApiResponse<object>> BindFamilyMember(string accountId, [FromBody] BindFamilyMemberDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.PatientId) || string.IsNullOrWhiteSpace(dto.PatientName))
            return ApiResponse<object>.Fail(400, "被绑定家庭成员ID与姓名不能为空。");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string id = Guid.NewGuid().ToString("N");
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        int cnt = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM pat_family_bindings WHERE portal_account_id = @AccId", new { AccId = accountId });
        if (cnt >= 5)
            return ApiResponse<object>.Fail(400, "单个患者账号最多绑定 5 位家庭就诊人（含本人）。");

        await conn.ExecuteAsync(@"
INSERT INTO pat_family_bindings (id, portal_account_id, patient_id, patient_name, relation, is_default, created_at_ms)
VALUES (@Id, @AccId, @Pid, @Name, @Rel, 0, @Now);", new
        {
            Id = id, AccId = accountId, Pid = dto.PatientId.Trim(), Name = dto.PatientName.Trim(),
            Rel = string.IsNullOrWhiteSpace(dto.Relation) ? "FAMILY" : dto.Relation.Trim(), Now = now
        });

        return ApiResponse<object>.Success(new { id, accountId, patientId = dto.PatientId.Trim(), relation = dto.Relation });
    }

    [HttpGet("queue-status/{visitId}")]
    public async Task<ApiResponse<object>> GetLiveQueueStatus(string visitId, CancellationToken ct)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var myTicket = await conn.QueryFirstOrDefaultAsync(
            "SELECT * FROM opd_triage_queues WHERE visit_id = @Vid ORDER BY checkin_at_ms DESC LIMIT 1",
            new { Vid = visitId });
        if (myTicket == null) return ApiResponse<object>.Fail(404, "未找到该就诊的候诊排队记录。");

        int ahead = await conn.ExecuteScalarAsync<int>(@"
SELECT COUNT(1)
FROM opd_triage_queues
WHERE opd_dept_id = @DeptId
  AND queue_status = 'WAITING'
  AND (priority_score > @Score OR (priority_score = @Score AND checkin_at_ms < @Checkin));", new
        {
            DeptId = (string)myTicket.opd_dept_id,
            Score = Convert.ToInt32(myTicket.priority_score),
            Checkin = Convert.ToInt64(myTicket.checkin_at_ms)
        });

        return ApiResponse<object>.Success(new
        {
            visitId,
            deptId = (string)myTicket.opd_dept_id,
            clinicRoomId = (string?)myTicket.clinic_room_id,
            visitStage = (string?)myTicket.visit_stage ?? "INITIAL",
            queueStatus = (string)myTicket.queue_status,
            patientsAhead = ahead,
            estimatedWaitMinutes = ahead * 6
        });
    }

    [HttpGet("guide-sheet/{visitId}")]
    public async Task<ApiResponse<object>> GenerateVisitGuideSheet(string visitId, [FromQuery] string? itemKeywords = null, CancellationToken ct = default)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var prepRules = (await conn.QueryAsync("SELECT * FROM cfg_exam_prep_instructions WHERE is_active = 1")).ToList();

        // 汇总当次就诊在费用表/检验检查申请单中的项目名称 + 可选传入关键词
        var itemNames = (await conn.QueryAsync<string>(
            "SELECT item_name FROM fin_charge_ledgers WHERE visit_or_encounter_id = @Vid AND status <> 'VOIDED'",
            new { Vid = visitId })).ToList();

        if (!string.IsNullOrWhiteSpace(itemKeywords))
        {
            itemNames.AddRange(itemKeywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        var steps = new List<object>();
        bool anyFasting = false;
        foreach (var name in itemNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var matched = prepRules.FirstOrDefault(r => name.Contains((string)r.item_keyword, StringComparison.OrdinalIgnoreCase));
            bool fasting = matched != null && Convert.ToInt32(matched.fasting_required) == 1;
            if (fasting) anyFasting = true;
            steps.Add(new
            {
                itemName = name,
                locationGuide = matched != null ? (string)matched.location_guide : "请至门诊导医台查询对应执行科室",
                fastingRequired = fasting,
                prepNotice = matched != null ? (string)matched.prep_notice : "请携带就诊二维码按序候检。",
                serviceHours = matched != null ? (string)matched.service_hours : "08:00-17:30"
            });
        }

        return ApiResponse<object>.Success(new
        {
            visitId,
            hasFastingItem = anyFasting,
            fastingBanner = anyFasting ? "⚠ 本次就诊包含需空腹/禁食项目，请先完成空腹采血或空腹检查后再进食！" : "本次就诊暂无空腹限制项目。",
            guideSteps = steps
        });
    }

    [HttpGet("my-reports/{patientId}")]
    public async Task<ApiResponse<object>> GetPatientReports(string patientId, CancellationToken ct)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        var reports = (await conn.QueryAsync(@"
SELECT id, report_no, exam_item_name, medtech_dept_name, status, findings_summary, diagnosis_impression, reviewed_at_ms
FROM mtc_diagnostic_reports
WHERE patient_id = @Pid AND status IN ('APPROVED', 'SIGNED', 'PUBLISHED')
ORDER BY reviewed_at_ms DESC LIMIT 50;", new { Pid = patientId })).ToList();
        return ApiResponse<object>.Success(reports);
    }

    [HttpGet("my-daily-bill/{encounterId}")]
    public async Task<ApiResponse<object>> GetPatientDailyBill(string encounterId, CancellationToken ct)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        var items = (await conn.QueryAsync(@"
SELECT item_code, item_name, quantity, unit_price, total_amount, status, created_at_ms
FROM fin_charge_ledgers
WHERE visit_or_encounter_id = @EncId AND status <> 'VOIDED'
ORDER BY created_at_ms DESC LIMIT 200;", new { EncId = encounterId })).ToList();

        decimal total = items.Sum(x => Convert.ToDecimal(x.total_amount));
        return ApiResponse<object>.Success(new { encounterId, totalAmount = total, itemCount = items.Count, items });
    }

    [HttpPost("consents/{consentId}/sign")]
    public async Task<ApiResponse<object>> SignInformedConsent(string consentId, [FromBody] SignConsentDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.SignerName) || string.IsNullOrWhiteSpace(dto.SignatureHashOrSvg))
            return ApiResponse<object>.Fail(400, "签署人姓名与电子签名数据不能为空。");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        int n = await conn.ExecuteAsync(@"
UPDATE srv_informed_consents
SET signer_name = @Name, signer_type = @Type, signature_hash = @Sig, signed_at_ms = @Now, status = 'SIGNED', updated_at_ms = @Now
WHERE id = @Id;", new
        {
            Name = dto.SignerName.Trim(),
            Type = string.IsNullOrWhiteSpace(dto.SignerType) ? "PATIENT" : dto.SignerType.Trim().ToUpperInvariant(),
            Sig = dto.SignatureHashOrSvg,
            Now = now,
            Id = consentId
        });

        if (n == 0) return ApiResponse<object>.Fail(404, "知情同意书不存在。");
        return ApiResponse<object>.Success(new { consentId, status = "SIGNED", signedAtMs = now });
    }

    [HttpGet("copy-requests")]
    public async Task<ApiResponse<object>> ListCopyRequests([FromQuery] string? status = null, CancellationToken ct = default)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        var sql = "SELECT * FROM mrms_copy_requests WHERE 1=1";
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(status)) { sql += " AND status = @S"; p.Add("S", status.Trim().ToUpperInvariant()); }
        sql += " ORDER BY created_at_ms DESC LIMIT 200";
        return ApiResponse<object>.Success((await conn.QueryAsync(sql, p)).ToList());
    }

    [HttpPost("copy-requests")]
    public async Task<ApiResponse<object>> SubmitMedicalRecordCopyRequest([FromBody] CreateCopyRequestDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.PatientId) || string.IsNullOrWhiteSpace(dto.EncounterId))
            return ApiResponse<object>.Fail(400, "患者ID与住院就诊ID不能为空。");
        if (string.Equals(dto.DeliveryMode, "EXPRESS", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(dto.MailAddress))
            return ApiResponse<object>.Fail(400, "选择邮寄到家(EXPRESS)时必须填写收件地址。");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string id = Guid.NewGuid().ToString("N");
        string copyNo = $"CP{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(10, 99)}";

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        await conn.ExecuteAsync(@"
INSERT INTO mrms_copy_requests (
    id, copy_no, patient_id, patient_name, encounter_id, purpose, copies_count,
    delivery_mode, mail_address, contact_phone, status, created_at_ms, updated_at_ms
) VALUES (
    @Id, @No, @Pid, @Pname, @EncId, @Purpose, @Cnt,
    @Mode, @Addr, @Phone, 'SUBMITTED', @Now, @Now
);", new
        {
            Id = id, No = copyNo, Pid = dto.PatientId.Trim(), Pname = dto.PatientName.Trim(),
            EncId = dto.EncounterId.Trim(), Purpose = string.IsNullOrWhiteSpace(dto.Purpose) ? "商保理赔/复诊" : dto.Purpose.Trim(),
            Cnt = dto.CopiesCount <= 0 ? 1 : dto.CopiesCount,
            Mode = string.IsNullOrWhiteSpace(dto.DeliveryMode) ? "EXPRESS" : dto.DeliveryMode.Trim().ToUpperInvariant(),
            Addr = dto.MailAddress, Phone = dto.ContactPhone, Now = now
        });

        return ApiResponse<object>.Success(new { id, copyNo, status = "SUBMITTED" });
    }

    [HttpPost("copy-requests/{id}/review")]
    public async Task<ApiResponse<object>> ReviewAndShipCopyRequest(string id, [FromBody] ReviewCopyRequestDto dto, CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string nextStatus = dto.Approved ? (string.IsNullOrWhiteSpace(dto.ExpressNo) ? "APPROVED" : "SHIPPED") : "REJECTED";

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        int n = await conn.ExecuteAsync(@"
UPDATE mrms_copy_requests
SET status = @St, express_no = @ExpNo, reviewer_name = @Rev, reviewed_at_ms = @Now, updated_at_ms = @Now
WHERE id = @Id;", new { St = nextStatus, ExpNo = dto.ExpressNo, Rev = ActorName, Now = now, Id = id });

        if (n == 0) return ApiResponse<object>.Fail(404, "病历复印申请单不存在。");
        return ApiResponse<object>.Success(new { id, status = nextStatus, expressNo = dto.ExpressNo });
    }
}
