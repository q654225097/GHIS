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

namespace HIS.Modules.Hr.Controllers;

/// <summary>
/// 医院职工全生命周期编排控制器（HR × 医务科 × 门诊办 × 住院病区 × 信息科 × 成本中心六维联动）：
/// 1) 上岗准入 4 步绿灯清单与一键转正（串联 CA 证书与医务分级授权）
/// 2) 请休假/外出进修/对口支援与临床权限限时委托代理（自动停诊迁移预约 + 托管管床 + 销假一键还原）
/// 3) 跨科调岗/调任“旧科清零 + 新科权限重评”闭环（同步成本中心）
/// 4) 离职/退休 5 项临床在途清零门禁与一键交接（含踢下线 + 吊销 CA + 停权医务授权）
/// 5) 退休专家返聘激活（REHIRED_EXPERT：保留专家门诊/会诊/处方与 CA，收回行政与常规管床）
/// 6) 外院特邀专家/多点执业限时备案（带 account_expire_at_ms 自动熔断）
/// 7) 住培/进修跨科轮转自动切科 + CME 学分/定期考核不合格自动停权 + 证照到期自动降权扫描
/// </summary>
[ApiController]
[RequireHisRole("ADMIN", "HR_ADMIN", "IPD_ATTENDING")]
[Route("api/v4/hr/lifecycle")]
public sealed class StaffLifecycleController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _session;

    public StaffLifecycleController(IDbConnectionFactory connFactory, IUserSession? session = null)
    {
        _connFactory = connFactory;
        _session = session;
    }

    private string ActorId => _session?.UserId?.Trim() ?? "SYSTEM";
    private string ActorName => _session?.DisplayName ?? _session?.UserName ?? ActorId;

    public sealed record CreateOnboardingDto(string EmployeeId, string? UserId, string EmployeeName, string? MentorUserId);
    public sealed record VerifyOnboardingStepDto(bool? LicenseVerified, bool? IpcTrainingPassed, bool? CaCertIssued, bool? MedPrivilegeGranted);
    public sealed record ActivateOnboardingDto(int PrescribeLevel = 2, int SurgeryLevel = 2, bool NarcoticAuth = false, string? DeptName = null);

    public sealed record ApplyLeaveDto(string EmployeeId, string UserId, string EmployeeName, string LeaveType, long StartAtMs, long EndAtMs, string DelegateUserId, string? DelegateName, string? Reason);
    public sealed record TransferWithClearanceDto(string NewDeptId, string NewDeptCategory, string? NewCostCenterCode, string? HandoverToUserId, string Reason);
    public sealed record ExecuteOffboardingDto(string SceneType, string? HandoverToUserId, string Reason);
    public sealed record RehireExpertDto(int ContractEndDateInt, int PrescribeLevel = 3, string? DeptName = null);

    public sealed record RegisterExternalExpertDto(string ExpertName, string OriginHospital, string LicenseNo, int AllowedSurgeryLevel, int ValidDays);
    public sealed record CreateRotationDto(string EmployeeId, string UserId, string EmployeeName, string TargetDeptId, string TargetDeptName, string? MentorUserId, int StartDateInt, int EndDateInt);
    public sealed record RecordCmeEthicsDto(string EmployeeId, string? UserId, string EmployeeName, int EvalYear, decimal Cat1Credits, decimal Cat2Credits, string EthicsGrade, string PeriodicExamResult, string? Remark);

    // =========================================================================
    // 1. 新职工上岗前合规准入清单与一键转正激活
    // =========================================================================

    [HttpGet("onboarding")]
    public async Task<ApiResponse<object>> ListOnboardingChecklists(CancellationToken ct)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        return ApiResponse<object>.Success((await conn.QueryAsync("SELECT * FROM hr_onboarding_checklists ORDER BY created_at_ms DESC LIMIT 200")).ToList());
    }

    [HttpPost("onboarding")]
    public async Task<ApiResponse<object>> InitOnboardingChecklist([FromBody] CreateOnboardingDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.EmployeeId) || string.IsNullOrWhiteSpace(dto.EmployeeName))
            return ApiResponse<object>.Fail(400, "职工ID与姓名不能为空。");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string id = Guid.NewGuid().ToString("N");
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var existingId = await conn.ExecuteScalarAsync<string?>("SELECT id FROM hr_onboarding_checklists WHERE employee_id = @Eid", new { Eid = dto.EmployeeId.Trim() });
        if (existingId != null)
            return ApiResponse<object>.Success(new { id = existingId, alreadyExists = true });

        await conn.ExecuteAsync(@"
INSERT INTO hr_onboarding_checklists (
    id, employee_id, user_id, employee_name, license_verified, ipc_training_passed,
    ca_cert_issued, med_privilege_granted, mentor_user_id, is_independent_duty, status, created_at_ms, updated_at_ms
) VALUES (
    @Id, @Eid, @Uid, @Name, 0, 0, 0, 0, @Mentor, 0, 'IN_PROGRESS', @Now, @Now
);", new { Id = id, Eid = dto.EmployeeId.Trim(), Uid = dto.UserId, Name = dto.EmployeeName.Trim(), Mentor = dto.MentorUserId, Now = now });

        return ApiResponse<object>.Success(new { id, employeeId = dto.EmployeeId.Trim(), status = "IN_PROGRESS" });
    }

    [HttpPost("onboarding/{empId}/verify-step")]
    public async Task<ApiResponse<object>> VerifyOnboardingStep(string empId, [FromBody] VerifyOnboardingStepDto dto, CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        int n = await conn.ExecuteAsync(@"
UPDATE hr_onboarding_checklists
SET license_verified = COALESCE(@Lic, license_verified),
    ipc_training_passed = COALESCE(@Ipc, ipc_training_passed),
    ca_cert_issued = COALESCE(@Ca, ca_cert_issued),
    med_privilege_granted = COALESCE(@Priv, med_privilege_granted),
    updated_at_ms = @Now
WHERE employee_id = @Eid;", new
        {
            Lic = dto.LicenseVerified.HasValue ? (dto.LicenseVerified.Value ? 1 : 0) : (int?)null,
            Ipc = dto.IpcTrainingPassed.HasValue ? (dto.IpcTrainingPassed.Value ? 1 : 0) : (int?)null,
            Ca = dto.CaCertIssued.HasValue ? (dto.CaCertIssued.Value ? 1 : 0) : (int?)null,
            Priv = dto.MedPrivilegeGranted.HasValue ? (dto.MedPrivilegeGranted.Value ? 1 : 0) : (int?)null,
            Now = now,
            Eid = empId.Trim()
        });

        if (n == 0) return ApiResponse<object>.Fail(404, "未找到该职工的上岗准入清单。");
        return ApiResponse<object>.Success(new { employeeId = empId.Trim(), updated = true });
    }

    [HttpPost("onboarding/{empId}/activate")]
    public async Task<ApiResponse<object>> ActivateIndependentDuty(string empId, [FromBody] ActivateOnboardingDto dto, CancellationToken ct)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var chk = await conn.QueryFirstOrDefaultAsync("SELECT * FROM hr_onboarding_checklists WHERE employee_id = @Eid", new { Eid = empId.Trim() });
        if (chk == null) return ApiResponse<object>.Fail(404, "未找到上岗准入清单，禁止跳过合规审查直接转正！");

        var missing = new List<string>();
        if (Convert.ToInt32(chk.license_verified) != 1) missing.Add("执业医师/护士证核验");
        if (Convert.ToInt32(chk.ipc_training_passed) != 1) missing.Add("院感与核心制度岗前考核");
        if (Convert.ToInt32(chk.ca_cert_issued) != 1) missing.Add("CA数字签名证书签发");
        if (Convert.ToInt32(chk.med_privilege_granted) != 1) missing.Add("医务科分级授权核定");
        if (missing.Count > 0)
            return ApiResponse<object>.Fail(400, $"上岗准入门禁拦截：以下必审项尚未绿灯通过：{string.Join("、", missing)}。");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string? userId = (string?)chk.user_id;
        string empName = (string)chk.employee_name;

        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync(@"
UPDATE hr_onboarding_checklists
SET is_independent_duty = 1, probation_passed_at_ms = @Now, status = 'ACTIVATED', updated_at_ms = @Now
WHERE employee_id = @Eid;", new { Now = now, Eid = empId.Trim() }, tx);

        await conn.ExecuteAsync(@"
UPDATE hr_employees
SET employment_status = 'ACTIVE', updated_at_ms = @Now, updated_by = @Actor
WHERE id = @Eid;", new { Now = now, Actor = ActorId, Eid = empId.Trim() }, tx);

        if (!string.IsNullOrWhiteSpace(userId))
        {
            await conn.ExecuteAsync(@"
UPDATE sys_users
SET is_active = 1, prescribe_level = @Rx, surgery_level = @Surg, updated_at_ms = @Now, updated_by = @Actor
WHERE id = @Uid;", new { Rx = dto.PrescribeLevel, Surg = dto.SurgeryLevel, Now = now, Actor = ActorId, Uid = userId }, tx);

            // 同步签发 CA 证书台账与医务科分级授权台账
            await conn.ExecuteAsync(@"
INSERT INTO mdm_ca_certificates (id, cert_serial, staff_id, ca_org_name, issued_at_ms, expires_at_ms, revoke_status, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES (@Id, @Ser, @Uid, '卫健委医疗CA中心', @Now, @Exp, 0, 1, @Now, @Now, 1);", new
            {
                Id = Guid.NewGuid().ToString("N"), Ser = $"CA-{DateTime.UtcNow:yyyyMMdd}-{userId[..Math.Min(6, userId.Length)].ToUpperInvariant()}",
                Uid = userId, Now = now, Exp = now + (365L * 3 * 86400_000L)
            }, tx);

            await conn.ExecuteAsync(@"
INSERT INTO med_privilege_grants (
    id, grant_no, user_id, doctor_name, dept_name, prescribe_level, surgery_level, narcotic_auth,
    valid_until_int, status, approved_by, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no
) VALUES (
    @Id, @No, @Uid, @Name, @Dept, @Rx, @Surg, @Narc,
    @ValidUntil, 'ACTIVE', @ActorName, 1, @Now, @Actor, @Now, @Actor, 1
);", new
            {
                Id = Guid.NewGuid().ToString("N"), No = $"PRV{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(10, 99)}",
                Uid = userId, Name = empName, Dept = dto.DeptName ?? "临床科室",
                Rx = dto.PrescribeLevel, Surg = dto.SurgeryLevel, Narc = dto.NarcoticAuth ? 1 : 0,
                ValidUntil = int.Parse(DateTime.UtcNow.AddYears(2).ToString("yyyyMMdd")),
                ActorName, Now = now, Actor = ActorId
            }, tx);
        }

        await tx.CommitAsync(ct);
        return ApiResponse<object>.Success(new { employeeId = empId.Trim(), status = "ACTIVE", independentDuty = true }, "上岗准入全部通过，已完成转正并同步签发 CA 与医务授权。");
    }

    // =========================================================================
    // 2. 请休假 / 外出进修 / 对口支援与临床代理托管（离岗不离责）
    // =========================================================================

    [HttpGet("leaves")]
    public async Task<ApiResponse<object>> ListLeaves([FromQuery] string? status = null, CancellationToken ct = default)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        var sql = "SELECT * FROM hr_leave_delegations WHERE 1=1";
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(status)) { sql += " AND status = @S"; p.Add("S", status.Trim().ToUpperInvariant()); }
        sql += " ORDER BY created_at_ms DESC LIMIT 200";
        return ApiResponse<object>.Success((await conn.QueryAsync(sql, p)).ToList());
    }

    [HttpPost("leaves/apply")]
    public async Task<ApiResponse<object>> ApplyLeaveWithDelegation([FromBody] ApplyLeaveDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.UserId) || string.IsNullOrWhiteSpace(dto.DelegateUserId))
            return ApiResponse<object>.Fail(400, "离岗职工账号与临床代理人账号不能为空。");
        if (string.Equals(dto.UserId.Trim(), dto.DelegateUserId.Trim(), StringComparison.OrdinalIgnoreCase))
            return ApiResponse<object>.Fail(400, "临床代理人不能指定为本人！");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string leaveId = Guid.NewGuid().ToString("N");
        string leaveNo = $"LV{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(10, 99)}";
        string leaveType = (dto.LeaveType ?? "ANNUAL").Trim().ToUpperInvariant();
        string empStatus = leaveType is "STUDY" or "STUDY_LEAVE" ? "STUDY_LEAVE" : leaveType == "SUPPORT" ? "EXTERNAL_SUPPORT" : "ON_LEAVE";

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // 1) 更新人事状态为离岗态（不禁用档案）
        await conn.ExecuteAsync(
            "UPDATE hr_employees SET employment_status = @St, updated_at_ms = @Now, updated_by = @Actor WHERE id = @Eid",
            new { St = empStatus, Now = now, Actor = ActorId, Eid = dto.EmployeeId.Trim() }, tx);

        // 2) 自动将名下在院管床患者临时托管给代理人（记录原始快照以便销假还原）
        var myIpds = (await conn.QueryAsync<string>(
            "SELECT id FROM ipd_encounters WHERE attending_doctor_id = @Uid AND status = 1",
            new { Uid = dto.UserId.Trim() }, tx)).ToList();

        if (myIpds.Count > 0)
        {
            await conn.ExecuteAsync(
                "UPDATE ipd_encounters SET attending_doctor_id = @Del, updated_at_ms = @Now, updated_by = @Actor WHERE attending_doctor_id = @Uid AND status = 1",
                new { Del = dto.DelegateUserId.Trim(), Now = now, Actor = ActorId, Uid = dto.UserId.Trim() }, tx);
        }

        // 3) 自动停用未来门诊排班并向受影响患者写入通知日志
        int startDateInt = int.Parse(DateTimeOffset.FromUnixTimeMilliseconds(dto.StartAtMs).UtcDateTime.ToString("yyyyMMdd"));
        int endDateInt = int.Parse(DateTimeOffset.FromUnixTimeMilliseconds(dto.EndAtMs).UtcDateTime.ToString("yyyyMMdd"));
        var affectedSlots = (await conn.QueryAsync<string>(
            "SELECT id FROM opd_schedule_slots WHERE doctor_id = @Uid AND schedule_date_int >= @S AND schedule_date_int <= @E AND is_active = 1",
            new { Uid = dto.UserId.Trim(), S = startDateInt, E = endDateInt }, tx)).ToList();

        int migratedAppts = 0;
        foreach (string slotId in affectedSlots)
        {
            await conn.ExecuteAsync("UPDATE opd_schedule_slots SET is_active = 0, updated_at_ms = @Now WHERE id = @Id", new { Now = now, Id = slotId }, tx);
            var appts = (await conn.QueryAsync<string>(
                "SELECT patient_id FROM opd_appointments WHERE schedule_id = @SlotId AND status IN ('RESERVED', 'PAID', 'CONFIRMED')",
                new { SlotId = slotId }, tx)).ToList();
            foreach (string pid in appts)
            {
                await conn.ExecuteAsync(@"
INSERT INTO pat_notification_logs (id, patient_id, channel, scene, title, content, sent_at_ms)
VALUES (@Nid, @Pid, 'SMS', 'DOCTOR_LEAVE_DELEGATE', '医师外出/休假替诊通知', @Content, @Now);", new
                {
                    Nid = Guid.NewGuid().ToString("N"), Pid = pid,
                    Content = $"您预约的 {dto.EmployeeName} 医师因 {leaveType} 离岗，已由替诊医师({dto.DelegateName ?? dto.DelegateUserId})接诊，请知悉。",
                    Now = now
                }, tx);
                migratedAppts++;
            }
        }

        await conn.ExecuteAsync(@"
INSERT INTO hr_leave_delegations (
    id, leave_no, employee_id, user_id, employee_name, leave_type, start_at_ms, end_at_ms,
    delegate_user_id, delegate_name, migrated_appts_cnt, delegated_ipd_ids_json, reason, status, created_at_ms, updated_at_ms
) VALUES (
    @Id, @No, @Eid, @Uid, @Name, @Type, @Start, @End,
    @DelId, @DelName, @Mig, @IpdJson, @Reason, 'ACTIVE', @Now, @Now
);", new
        {
            Id = leaveId, No = leaveNo, Eid = dto.EmployeeId.Trim(), Uid = dto.UserId.Trim(), Name = dto.EmployeeName.Trim(),
            Type = leaveType, Start = dto.StartAtMs, End = dto.EndAtMs, DelId = dto.DelegateUserId.Trim(),
            DelName = dto.DelegateName, Mig = migratedAppts, IpdJson = JsonSerializer.Serialize(myIpds), Reason = dto.Reason, Now = now
        }, tx);

        await tx.CommitAsync(ct);
        return ApiResponse<object>.Success(new
        {
            leaveId,
            leaveNo,
            employmentStatus = empStatus,
            delegatedInpatientCount = myIpds.Count,
            stoppedScheduleCount = affectedSlots.Count,
            notifiedAppointmentsCount = migratedAppts
        });
    }

    [HttpPost("leaves/{leaveId}/return-duty")]
    public async Task<ApiResponse<object>> ReturnFromLeave(string leaveId, CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var lv = await conn.QueryFirstOrDefaultAsync("SELECT * FROM hr_leave_delegations WHERE id = @Id", new { Id = leaveId });
        if (lv == null) return ApiResponse<object>.Fail(404, "请假/外出记录不存在。");

        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync(
            "UPDATE hr_leave_delegations SET status = 'RETURNED', returned_at_ms = @Now, updated_at_ms = @Now WHERE id = @Id",
            new { Now = now, Id = leaveId }, tx);

        await conn.ExecuteAsync(
            "UPDATE hr_employees SET employment_status = 'ACTIVE', updated_at_ms = @Now, updated_by = @Actor WHERE id = @Eid",
            new { Now = now, Actor = ActorId, Eid = (string)lv.employee_id }, tx);

        // 还原仍在院的托管患者管床权
        int restoredIpds = 0;
        string? json = (string?)lv.delegated_ipd_ids_json;
        if (!string.IsNullOrWhiteSpace(json))
        {
            var ids = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            foreach (string encId in ids)
            {
                restoredIpds += await conn.ExecuteAsync(
                    "UPDATE ipd_encounters SET attending_doctor_id = @OrigUid, updated_at_ms = @Now WHERE id = @EncId AND status = 1",
                    new { OrigUid = (string)lv.user_id, Now = now, EncId = encId }, tx);
            }
        }

        await tx.CommitAsync(ct);
        return ApiResponse<object>.Success(new { leaveId, status = "RETURNED", restoredInpatientCount = restoredIpds }, "已办理销假复岗并收回临床代理权限。");
    }

    // =========================================================================
    // 3. 跨科调岗/调任“旧科清零 + 新科权限重评”闭环
    // =========================================================================

    [HttpPost("transfer-with-clearance/{empId}")]
    public async Task<ApiResponse<object>> TransferDepartmentWithClearance(string empId, [FromBody] TransferWithClearanceDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.NewDeptId))
            return ApiResponse<object>.Fail(400, "调入目标科室ID不能为空。");

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var emp = await conn.QueryFirstOrDefaultAsync("SELECT id, user_id, full_name FROM hr_employees WHERE id = @Id", new { Id = empId.Trim() });
        if (emp == null) return ApiResponse<object>.Fail(404, "职工档案不存在。");
        string userId = (string?)emp.user_id ?? string.Empty;

        int activeIpds = string.IsNullOrWhiteSpace(userId) ? 0 : await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM ipd_encounters WHERE attending_doctor_id = @Uid AND status = 1", new { Uid = userId });
        if (activeIpds > 0 && string.IsNullOrWhiteSpace(dto.HandoverToUserId))
            return ApiResponse<object>.Fail(400, $"调科清零门禁拦截：该医师在原科室仍有 {activeIpds} 名在院管床患者，必须指定原科室接替医师(HandoverToUserId)！");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await using var tx = await conn.BeginTransactionAsync(ct);

        if (activeIpds > 0 && !string.IsNullOrWhiteSpace(dto.HandoverToUserId))
        {
            await conn.ExecuteAsync(
                "UPDATE ipd_encounters SET attending_doctor_id = @To, updated_at_ms = @Now WHERE attending_doctor_id = @Uid AND status = 1",
                new { To = dto.HandoverToUserId.Trim(), Now = now, Uid = userId }, tx);
        }

        if (!string.IsNullOrWhiteSpace(userId))
        {
            await conn.ExecuteAsync(
                "UPDATE sys_user_dept_posts SET dept_id = @Dept, dept_category = @Cat, updated_at_ms = @Now WHERE user_id = @Uid AND is_default_context = 1",
                new { Dept = dto.NewDeptId.Trim(), Cat = string.IsNullOrWhiteSpace(dto.NewDeptCategory) ? "IPD" : dto.NewDeptCategory.Trim().ToUpperInvariant(), Now = now, Uid = userId }, tx);

            // 旧专科手术/处方权置为 PENDING_RE_EVAL，禁止跨专科自动沿用高风险手术权
            await conn.ExecuteAsync(
                "UPDATE med_privilege_grants SET status = 'PENDING_RE_EVAL', suspend_reason = '跨科调岗待新专科重评授权', updated_at_ms = @Now WHERE user_id = @Uid AND status = 'ACTIVE'",
                new { Now = now, Uid = userId }, tx);
        }

        await conn.ExecuteAsync(
            "UPDATE hr_employees SET cost_center_code = COALESCE(@Cc, cost_center_code), updated_at_ms = @Now, updated_by = @Actor WHERE id = @Eid",
            new { Cc = dto.NewCostCenterCode, Now = now, Actor = ActorId, Eid = empId.Trim() }, tx);

        await tx.CommitAsync(ct);
        return ApiResponse<object>.Success(new
        {
            employeeId = empId.Trim(),
            newDeptId = dto.NewDeptId.Trim(),
            handedOverInpatients = activeIpds,
            privilegeStatus = "PENDING_RE_EVAL"
        }, "跨科调岗已完成：原科室管床已交接，医务授权已转入新专科待重评状态。");
    }

    // =========================================================================
    // 4. 离职 / 退休 5 项临床在途清零门禁与一键交接
    // =========================================================================

    private async Task<(int Ipd, int Emr, int Slots, int Wf, int Consult)> ScanPendingWorkAsync(IDbConnection conn, string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return (0, 0, 0, 0, 0);
        int todayInt = int.Parse(DateTime.UtcNow.ToString("yyyyMMdd"));
        int ipd = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ipd_encounters WHERE attending_doctor_id = @Uid AND status = 1", new { Uid = userId });
        int emr = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM emr_documents WHERE author_id = @Uid AND status < 3", new { Uid = userId });
        int slots = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM opd_schedule_slots WHERE doctor_id = @Uid AND schedule_date_int >= @Today AND is_active = 1", new { Uid = userId, Today = todayInt });
        int wf = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM wf_tasks WHERE assignee_user_id = @Uid AND status = 'PENDING'", new { Uid = userId });
        int consult = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ipd_consultations WHERE invited_doctor_name = @Uid AND status = 'PENDING'", new { Uid = userId });
        return (ipd, emr, slots, wf, consult);
    }

    [HttpGet("offboarding/{empId}/audit")]
    public async Task<ApiResponse<object>> AuditOffboardingClearance(string empId, CancellationToken ct)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var emp = await conn.QueryFirstOrDefaultAsync("SELECT id, user_id, full_name, employment_status FROM hr_employees WHERE id = @Id", new { Id = empId.Trim() });
        if (emp == null) return ApiResponse<object>.Fail(404, "职工档案不存在。");

        string uid = (string?)emp.user_id ?? string.Empty;
        var (ipd, emr, slots, wf, consult) = await ScanPendingWorkAsync(conn, uid);
        int totalPending = ipd + emr + slots + wf + consult;

        return ApiResponse<object>.Success(new
        {
            employeeId = empId.Trim(),
            employeeName = (string)emp.full_name,
            userId = uid,
            pendingInpatients = ipd,
            unsignedEmrDocuments = emr,
            futureActiveScheduleSlots = slots,
            pendingWorkflowTasks = wf,
            pendingConsultations = consult,
            canDirectOffboard = totalPending == 0,
            requiresHandover = totalPending > 0
        });
    }

    [HttpPost("offboarding/{empId}/execute")]
    public async Task<ApiResponse<object>> ExecuteOffboardingWithClearance(string empId, [FromBody] ExecuteOffboardingDto dto, CancellationToken ct)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var emp = await conn.QueryFirstOrDefaultAsync("SELECT id, user_id, full_name FROM hr_employees WHERE id = @Id", new { Id = empId.Trim() });
        if (emp == null) return ApiResponse<object>.Fail(404, "职工档案不存在。");
        string uid = (string?)emp.user_id ?? string.Empty;
        string empName = (string)emp.full_name;

        var (ipd, emr, slots, wf, consult) = await ScanPendingWorkAsync(conn, uid);
        int totalPending = ipd + emr + slots + wf + consult;
        if (totalPending > 0 && string.IsNullOrWhiteSpace(dto.HandoverToUserId))
        {
            return ApiResponse<object>.Fail(409,
                $"离职/退休清零门禁阻断：名下仍有 {ipd} 位在院管床、{emr} 份未签病历、{slots} 个未来排班、{wf} 项待办审批。请指定接替医师(HandoverToUserId)执行一键交接后再办离职！");
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int todayInt = int.Parse(DateTime.UtcNow.ToString("yyyyMMdd"));
        string targetStatus = string.Equals(dto.SceneType, "RETIRE", StringComparison.OrdinalIgnoreCase) ? "RETIRED" : "RESIGNED";
        string clearanceId = Guid.NewGuid().ToString("N");
        string clearanceNo = $"CLR{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(10, 99)}";

        await using var tx = await conn.BeginTransactionAsync(ct);

        // 1) 一键交接在院管床与待办审批，停用未来排班
        if (!string.IsNullOrWhiteSpace(uid))
        {
            if (!string.IsNullOrWhiteSpace(dto.HandoverToUserId))
            {
                string toUid = dto.HandoverToUserId.Trim();
                await conn.ExecuteAsync("UPDATE ipd_encounters SET attending_doctor_id = @To, updated_at_ms = @Now WHERE attending_doctor_id = @Uid AND status = 1", new { To = toUid, Now = now, Uid = uid }, tx);
                await conn.ExecuteAsync("UPDATE wf_tasks SET assignee_user_id = @To, updated_at_ms = @Now WHERE assignee_user_id = @Uid AND status = 'PENDING'", new { To = toUid, Now = now, Uid = uid }, tx);
            }
            await conn.ExecuteAsync("UPDATE opd_schedule_slots SET is_active = 0, updated_at_ms = @Now WHERE doctor_id = @Uid AND schedule_date_int >= @Today", new { Now = now, Uid = uid, Today = todayInt }, tx);

            // 2) 禁用账号 + 强制踢下线 + 吊销 CA 证书 + 停权医务分级授权
            await conn.ExecuteAsync("UPDATE sys_users SET is_active = 0, session_version = session_version + 1, updated_at_ms = @Now, updated_by = @Actor WHERE id = @Uid", new { Now = now, Actor = ActorId, Uid = uid }, tx);
            await conn.ExecuteAsync("UPDATE mdm_ca_certificates SET revoke_status = 1, updated_at_ms = @Now WHERE staff_id = @Uid AND revoke_status = 0", new { Now = now, Uid = uid }, tx);
            await conn.ExecuteAsync("UPDATE med_privilege_grants SET status = 'REVOKED', suspend_reason = @Reason, updated_at_ms = @Now WHERE user_id = @Uid AND status = 'ACTIVE'", new { Reason = $"人员{targetStatus}自动收回", Now = now, Uid = uid }, tx);
            await conn.ExecuteAsync("UPDATE sec_emr_access_grants SET status = 0, updated_at_ms = @Now WHERE grantee_user_id = @Uid AND status = 1", new { Now = now, Uid = uid }, tx);
        }

        // 3) 更新人事状态并写入清零归档单
        await conn.ExecuteAsync(
            "UPDATE hr_employees SET employment_status = @St, resign_date_int = @Today, resign_reason = @Reason, updated_at_ms = @Now, updated_by = @Actor WHERE id = @Eid",
            new { St = targetStatus, Today = todayInt, Reason = dto.Reason, Now = now, Actor = ActorId, Eid = empId.Trim() }, tx);

        await conn.ExecuteAsync(@"
INSERT INTO hr_offboarding_clearances (
    id, clearance_no, employee_id, user_id, employee_name, scene_type, handover_to_user_id,
    pending_ipd_cnt, unsigned_emr_cnt, future_slots_cnt, pending_wf_cnt, pending_consult_cnt,
    ca_revoked, privilege_revoked, status, reason, operator_name, completed_at_ms, created_at_ms
) VALUES (
    @Id, @No, @Eid, @Uid, @Name, @Scene, @ToUid,
    @Ipd, @Emr, @Slots, @Wf, @Cons,
    1, 1, 'COMPLETED', @Reason, @Op, @Now, @Now
);", new
        {
            Id = clearanceId, No = clearanceNo, Eid = empId.Trim(), Uid = uid, Name = empName,
            Scene = targetStatus, ToUid = dto.HandoverToUserId, Ipd = ipd, Emr = emr, Slots = slots, Wf = wf, Cons = consult,
            Reason = dto.Reason, Op = ActorName, Now = now
        }, tx);

        await tx.CommitAsync(ct);
        return ApiResponse<object>.Success(new
        {
            clearanceId,
            clearanceNo,
            employmentStatus = targetStatus,
            handedOverInpatients = ipd,
            stoppedFutureSlots = slots,
            caRevoked = true,
            privilegeRevoked = true
        }, $"已完成 {targetStatus} 全部在途交接清零、账号禁停、CA 吊销与医务授权收回。");
    }

    // =========================================================================
    // 5. 退休专家返聘激活（REHIRED_EXPERT：保留专家门诊/会诊，收回行政与管床）
    // =========================================================================

    [HttpPost("rehire/{empId}")]
    public async Task<ApiResponse<object>> ActivateRetiredExpertRehire(string empId, [FromBody] RehireExpertDto dto, CancellationToken ct)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var emp = await conn.QueryFirstOrDefaultAsync("SELECT id, user_id, full_name, employment_status FROM hr_employees WHERE id = @Id", new { Id = empId.Trim() });
        if (emp == null) return ApiResponse<object>.Fail(404, "职工档案不存在。");
        if (!string.Equals((string?)emp.employment_status, "RETIRED", StringComparison.OrdinalIgnoreCase))
            return ApiResponse<object>.Fail(400, "仅已退休(RETIRED)专家允许办理退休返聘激活。");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string? uid = (string?)emp.user_id;

        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync(
            "UPDATE hr_employees SET employment_status = 'REHIRED_EXPERT', rehire_contract_end_int = @EndInt, updated_at_ms = @Now, updated_by = @Actor WHERE id = @Eid",
            new { EndInt = dto.ContractEndDateInt, Now = now, Actor = ActorId, Eid = empId.Trim() }, tx);

        if (!string.IsNullOrWhiteSpace(uid))
        {
            // 激活返聘专家账号：保留专家门诊处方与会诊权，收回行政/常规住院手术管床权（surgery_level=0）
            await conn.ExecuteAsync(
                "UPDATE sys_users SET is_active = 1, prescribe_level = @Rx, surgery_level = 0, updated_at_ms = @Now WHERE id = @Uid",
                new { Rx = dto.PrescribeLevel, Now = now, Uid = uid }, tx);

            await conn.ExecuteAsync(
                "UPDATE mdm_ca_certificates SET revoke_status = 0, updated_at_ms = @Now WHERE staff_id = @Uid",
                new { Now = now, Uid = uid }, tx);

            await conn.ExecuteAsync(
                "UPDATE sys_user_dept_posts SET role_id = 'ROLE-OPD-DOC', dept_category = 'OPD', updated_at_ms = @Now WHERE user_id = @Uid",
                new { Now = now, Uid = uid }, tx);
        }

        await tx.CommitAsync(ct);
        return ApiResponse<object>.Success(new
        {
            employeeId = empId.Trim(),
            employmentStatus = "REHIRED_EXPERT",
            rehireContractEndInt = dto.ContractEndDateInt,
            scope = "保留专家门诊/会诊/处方与CA签章权，收回行政职务与常规住院管床权"
        });
    }

    // =========================================================================
    // 6. 外院特邀专家与多点执业限时备案
    // =========================================================================

    [HttpGet("external-experts")]
    public async Task<ApiResponse<object>> ListExternalExperts(CancellationToken ct)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        return ApiResponse<object>.Success((await conn.QueryAsync("SELECT * FROM hr_external_experts ORDER BY created_at_ms DESC LIMIT 100")).ToList());
    }

    [HttpPost("external-experts/register")]
    public async Task<ApiResponse<object>> RegisterExternalExpert([FromBody] RegisterExternalExpertDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.ExpertName) || string.IsNullOrWhiteSpace(dto.OriginHospital) || string.IsNullOrWhiteSpace(dto.LicenseNo))
            return ApiResponse<object>.Fail(400, "外院专家姓名、原执业医院与执业证号不能为空。");
        if (dto.ValidDays is <= 0 or > 30)
            return ApiResponse<object>.Fail(400, "单次外院多点执业限时备案天数必须在 1~30 天之间。");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long validUntil = now + (dto.ValidDays * 86400_000L);
        string id = Guid.NewGuid().ToString("N");
        string filingNo = $"EXT{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(10, 99)}";
        string tempUserId = $"EXT-DOC-{id[..8].ToUpperInvariant()}";

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(@"
INSERT INTO sys_users (
    id, user_name, display_name, employee_no, password_hash, prescribe_level, surgery_level, emr_sign_level,
    session_version, is_active, account_expire_at_ms, schema_version, created_at_ms, updated_at_ms, version_no
) VALUES (
    @Uid, @Uname, @Dname, @EmpNo, 'TEMP_EXT_HASH', 3, @Surg, 2,
    1, 1, @ExpMs, 1, @Now, @Now, 1
);", new
        {
            Uid = tempUserId, Uname = tempUserId.ToLowerInvariant(), Dname = $"{dto.ExpertName.Trim()}(外院特邀)",
            EmpNo = filingNo, Surg = dto.AllowedSurgeryLevel, ExpMs = validUntil, Now = now
        }, tx);

        await conn.ExecuteAsync(@"
INSERT INTO hr_external_experts (
    id, filing_no, expert_name, origin_hospital, license_no, temp_user_id,
    allowed_surgery_level, valid_from_ms, valid_until_ms, status, approved_by, created_at_ms, updated_at_ms
) VALUES (
    @Id, @No, @Name, @Hosp, @Lic, @Uid, @Surg, @Now, @ExpMs, 'ACTIVE', @Op, @Now, @Now
);", new
        {
            Id = id, No = filingNo, Name = dto.ExpertName.Trim(), Hosp = dto.OriginHospital.Trim(),
            Lic = dto.LicenseNo.Trim(), Uid = tempUserId, Surg = dto.AllowedSurgeryLevel,
            Now = now, ExpMs = validUntil, Op = ActorName
        }, tx);

        await tx.CommitAsync(ct);
        return ApiResponse<object>.Success(new { id, filingNo, tempUserId, validUntilMs = validUntil, status = "ACTIVE" });
    }

    // =========================================================================
    // 7. 住培轮转、CME 定期考核与证照到期自动熔断巡检
    // =========================================================================

    [HttpGet("rotations")]
    public async Task<ApiResponse<object>> ListRotations(CancellationToken ct)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        return ApiResponse<object>.Success((await conn.QueryAsync("SELECT * FROM hr_rotations ORDER BY start_date_int DESC LIMIT 100")).ToList());
    }

    [HttpPost("rotations")]
    public async Task<ApiResponse<object>> ScheduleRotation([FromBody] CreateRotationDto dto, CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string id = Guid.NewGuid().ToString("N");
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        await conn.ExecuteAsync(@"
INSERT INTO hr_rotations (
    id, employee_id, user_id, employee_name, target_dept_id, target_dept_name,
    mentor_user_id, start_date_int, end_date_int, auto_switched, status, created_at_ms, updated_at_ms
) VALUES (
    @Id, @Eid, @Uid, @Name, @DeptId, @DeptName, @Mentor, @Start, @End, 0, 'SCHEDULED', @Now, @Now
);", new
        {
            Id = id, Eid = dto.EmployeeId.Trim(), Uid = dto.UserId.Trim(), Name = dto.EmployeeName.Trim(),
            DeptId = dto.TargetDeptId.Trim(), DeptName = dto.TargetDeptName, Mentor = dto.MentorUserId,
            Start = dto.StartDateInt, End = dto.EndDateInt, Now = now
        });
        return ApiResponse<object>.Success(new { id, status = "SCHEDULED" });
    }

    [HttpPost("cme-ethics")]
    public async Task<ApiResponse<object>> RecordCmeAndPeriodicExam([FromBody] RecordCmeEthicsDto dto, CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string id = Guid.NewGuid().ToString("N");
        string res = (dto.PeriodicExamResult ?? "PASS").Trim().ToUpperInvariant();
        bool failed = res == "FAIL";
        int? suspendUntil = failed ? int.Parse(DateTime.UtcNow.AddMonths(3).ToString("yyyyMMdd")) : null;

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(@"
INSERT INTO hr_cme_ethics_records (
    id, employee_id, user_id, employee_name, eval_year, cme_cat1_credits, cme_cat2_credits,
    ethics_grade, periodic_exam_result, suspend_until_int, remark, created_at_ms, updated_at_ms
) VALUES (
    @Id, @Eid, @Uid, @Name, @Yr, @C1, @C2, @Eth, @Res, @Susp, @Rem, @Now, @Now
);", new
        {
            Id = id, Eid = dto.EmployeeId.Trim(), Uid = dto.UserId, Name = dto.EmployeeName.Trim(),
            Yr = dto.EvalYear, C1 = dto.Cat1Credits, C2 = dto.Cat2Credits,
            Eth = dto.EthicsGrade.Trim().ToUpperInvariant(), Res = res, Susp = suspendUntil, Rem = dto.Remark, Now = now
        }, tx);

        // 定期考核不合格 -> 自动暂停处方/手术权 3 个月
        if (failed && !string.IsNullOrWhiteSpace(dto.UserId))
        {
            await conn.ExecuteAsync(
                "UPDATE sys_users SET prescribe_level = 0, surgery_level = 0, updated_at_ms = @Now WHERE id = @Uid",
                new { Now = now, Uid = dto.UserId.Trim() }, tx);
            await conn.ExecuteAsync(
                "UPDATE med_privilege_grants SET status = 'SUSPENDED', suspend_reason = '医师定期考核不合格暂停执业3个月', updated_at_ms = @Now WHERE user_id = @Uid AND status = 'ACTIVE'",
                new { Now = now, Uid = dto.UserId.Trim() }, tx);
        }

        await tx.CommitAsync(ct);
        return ApiResponse<object>.Success(new { id, periodicExamResult = res, autoSuspended = failed, suspendUntilInt = suspendUntil });
    }

    [HttpPost("governance/run-expiry-sweep")]
    public async Task<ApiResponse<object>> RunExpiryAndRotationSweep(CancellationToken ct)
    {
        var summary = await ExecuteGovernanceSweepCoreAsync(_connFactory, ct);
        return ApiResponse<object>.Success(summary);
    }

    public static async Task<object> ExecuteGovernanceSweepCoreAsync(IDbConnectionFactory connFactory, CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int todayInt = int.Parse(DateTime.UtcNow.ToString("yyyyMMdd"));

        await using var conn = connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        // 1) 外院多点执业专家到期自动熔断
        var expiredExperts = (await conn.QueryAsync<(string Id, string TempUserId)>(
            "SELECT id, temp_user_id FROM hr_external_experts WHERE status = 'ACTIVE' AND valid_until_ms < @Now",
            new { Now = now })).ToList();
        foreach (var ex in expiredExperts)
        {
            await conn.ExecuteAsync("UPDATE hr_external_experts SET status = 'EXPIRED', updated_at_ms = @Now WHERE id = @Id", new { Now = now, Id = ex.Id });
            await conn.ExecuteAsync("UPDATE sys_users SET is_active = 0, session_version = session_version + 1, updated_at_ms = @Now WHERE id = @Uid", new { Now = now, Uid = ex.TempUserId });
        }

        // 2) 医务分级授权到期自动降权停权
        var expiredPrivs = (await conn.QueryAsync<(string Id, string UserId)>(
            "SELECT id, user_id FROM med_privilege_grants WHERE status = 'ACTIVE' AND valid_until_int < @Today",
            new { Today = todayInt })).ToList();
        foreach (var pr in expiredPrivs)
        {
            await conn.ExecuteAsync("UPDATE med_privilege_grants SET status = 'EXPIRED_SUSPENDED', suspend_reason = '授权超期自动停权', updated_at_ms = @Now WHERE id = @Id", new { Now = now, Id = pr.Id });
            await conn.ExecuteAsync("UPDATE sys_users SET prescribe_level = 0, surgery_level = 0, updated_at_ms = @Now WHERE id = @Uid", new { Now = now, Uid = pr.UserId });
        }

        // 3) 住培/进修轮转到期自动切换科室上下文
        var dueRotations = (await conn.QueryAsync<(string Id, string UserId, string TargetDeptId)>(
            "SELECT id, user_id, target_dept_id FROM hr_rotations WHERE status = 'SCHEDULED' AND start_date_int <= @Today AND end_date_int >= @Today",
            new { Today = todayInt })).ToList();
        foreach (var rot in dueRotations)
        {
            await conn.ExecuteAsync("UPDATE sys_user_dept_posts SET dept_id = @Dept, updated_at_ms = @Now WHERE user_id = @Uid AND is_default_context = 1", new { Dept = rot.TargetDeptId, Now = now, Uid = rot.UserId });
            await conn.ExecuteAsync("UPDATE hr_rotations SET status = 'ACTIVE', auto_switched = 1, updated_at_ms = @Now WHERE id = @Id", new { Now = now, Id = rot.Id });
        }

        return new
        {
            sweptAtMs = now,
            expiredExternalExpertsDisabled = expiredExperts.Count,
            expiredPrivilegesSuspended = expiredPrivs.Count,
            rotationsAutoSwitched = dueRotations.Count
        };
    }
}
