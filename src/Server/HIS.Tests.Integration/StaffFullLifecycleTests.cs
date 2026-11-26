using System;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Modules.Hr.Controllers;
using Xunit;

namespace HIS.Tests.Integration;

/// <summary>
/// 医院职工全生命周期端到端沙盘回归测试：
/// 入职准入拦截 -> 4步绿灯转正(签CA+医务授权) -> 外出进修停诊并托管管床 -> 销假复岗还原 ->
/// 跨科调岗旧科清零与授权重评 -> 离职未清零409拦截 -> 一键交接吊销CA -> 退休专家返聘 ->
/// 外院专家到期自动熔断 & CME定期考核不合格自动停权。
/// </summary>
public sealed class StaffFullLifecycleTests
{
    private static async Task<SqliteConnectionFactory> CreateFreshDbAsync()
    {
        var db = new SqliteConnectionFactory($"Data Source=staff_lc_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await DbSchemaInitializer.InitializeAsync(db);
        return db;
    }

    [Fact]
    public async Task StaffLifecycle_EndToEnd_Onboarding_LeaveDelegation_Transfer_OffboardGate_Rehire_And_ExpirySweep()
    {
        var db = await CreateFreshDbAsync();
        long now = SystemTime.UtcNowMs;
        int today = int.Parse(DateTime.UtcNow.ToString("yyyyMMdd"));

        using (var conn = db.CreateConnection())
        {
            await conn.ExecuteAsync(@"
INSERT INTO sys_users (id, user_name, display_name, employee_no, password_hash, prescribe_level, surgery_level, emr_sign_level, session_version, is_active, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES
('U-DOC-A', 'dr_chen', '陈主治', 'E201', 'H', 0, 0, 1, 1, 1, 1, @Now, @Now, 1),
('U-DOC-B', 'dr_wang', '王代理', 'E202', 'H', 3, 3, 2, 1, 1, 1, @Now, @Now, 1);

INSERT INTO sys_user_dept_posts (id, user_id, dept_id, dept_category, role_id, is_default_context, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES ('POST-A', 'U-DOC-A', 'DEPT-SURG', 'IPD', 'ROLE-IPD-ATTENDING', 1, 1, @Now, @Now, 1);

INSERT INTO hr_employees (id, employee_no, user_id, full_name, gender, hire_date_int, employment_type, staff_category, employment_status, approval_status, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES ('EMP-A', 'E201', 'U-DOC-A', '陈主治', 'M', @Today, 'FORMAL', 'DOCTOR', 'ONBOARDING', 'APPROVED', 1, @Now, @Now, 1);

INSERT INTO ipd_encounters (id, admission_no, patient_id, campus_id, admit_dept_id, admit_ward_id, is_borrowed_bed, bed_code, attending_doctor_id, prepayment_balance, status, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES ('ENC-A1', 'ADM-A1', 'PAT-A1', 'C1', 'DEPT-SURG', 'WARD-1', 0, '01床', 'U-DOC-A', 5000, 1, 1, @Now, @Now, 1);

INSERT INTO opd_schedule_slots (id, campus_id, opd_dept_id, doctor_id, schedule_date_int, slot_period_code, reg_level_code, registration_fee, total_capacity, used_count, is_active, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES ('SLOT-A1', 'C1', 'DEPT-SURG', 'U-DOC-A', @Today, 'AM', 'EXPERT', 50, 20, 1, 1, 1, @Now, @Now, 1);",
                new { Now = now, Today = today });
        }

        var ctrl = new StaffLifecycleController(db);

        // 1) 上岗准入：未过4项直接转正 -> 400拦截；4项全绿灯 -> 转正激活并签发CA和医务授权
        Assert.Equal(200, (await ctrl.InitOnboardingChecklist(new StaffLifecycleController.CreateOnboardingDto("EMP-A", "U-DOC-A", "陈主治", "U-DOC-B"), default)).Code);
        Assert.Equal(400, (await ctrl.ActivateIndependentDuty("EMP-A", new StaffLifecycleController.ActivateOnboardingDto(2, 3, true, "普外科"), default)).Code);
        Assert.Equal(200, (await ctrl.VerifyOnboardingStep("EMP-A", new StaffLifecycleController.VerifyOnboardingStepDto(true, true, true, true), default)).Code);
        Assert.Equal(200, (await ctrl.ActivateIndependentDuty("EMP-A", new StaffLifecycleController.ActivateOnboardingDto(2, 3, true, "普外科"), default)).Code);

        using (var conn = db.CreateConnection())
        {
            Assert.Equal("ACTIVE", await conn.ExecuteScalarAsync<string>("SELECT employment_status FROM hr_employees WHERE id = 'EMP-A'"));
            Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM mdm_ca_certificates WHERE staff_id = 'U-DOC-A' AND revoke_status = 0"));
        }

        // 2) 外出进修申请 -> 自动托管在院管床给 U-DOC-B；销假复岗 -> 自动还原管床给 U-DOC-A
        Assert.Equal(200, (await ctrl.ApplyLeaveWithDelegation(new StaffLifecycleController.ApplyLeaveDto("EMP-A", "U-DOC-A", "陈主治", "STUDY_LEAVE", now, now + 7L * 86400_000L, "U-DOC-B", "王代理", "协和进修"), default)).Code);
        using (var conn = db.CreateConnection())
        {
            Assert.Equal("U-DOC-B", await conn.ExecuteScalarAsync<string>("SELECT attending_doctor_id FROM ipd_encounters WHERE id = 'ENC-A1'"));
            string lvId = await conn.ExecuteScalarAsync<string>("SELECT id FROM hr_leave_delegations LIMIT 1") ?? string.Empty;
            Assert.Equal(200, (await ctrl.ReturnFromLeave(lvId, default)).Code);
            Assert.Equal("U-DOC-A", await conn.ExecuteScalarAsync<string>("SELECT attending_doctor_id FROM ipd_encounters WHERE id = 'ENC-A1'"));
        }

        // 3) 跨科调岗：有在院管床未指定交接人 -> 400阻断；指定交接人后成功切科并将原医务授权置为 PENDING_RE_EVAL
        Assert.Equal(400, (await ctrl.TransferDepartmentWithClearance("EMP-A", new StaffLifecycleController.TransferWithClearanceDto("DEPT-CARDIO", "IPD", "CC-02", null, "调科"), default)).Code);
        Assert.Equal(200, (await ctrl.TransferDepartmentWithClearance("EMP-A", new StaffLifecycleController.TransferWithClearanceDto("DEPT-CARDIO", "IPD", "CC-02", "U-DOC-B", "调科"), default)).Code);
        using (var conn = db.CreateConnection())
        {
            Assert.Equal("PENDING_RE_EVAL", await conn.ExecuteScalarAsync<string>("SELECT status FROM med_privilege_grants WHERE user_id = 'U-DOC-A' LIMIT 1"));
        }

        // 4) 退休清零门禁：故意再放回一个在院患者 -> 无交接人409阻断；指定交接人后一键交接、禁号并吊销CA -> 退休返聘重新激活
        using (var conn = db.CreateConnection())
        {
            await conn.ExecuteAsync("UPDATE ipd_encounters SET attending_doctor_id = 'U-DOC-A' WHERE id = 'ENC-A1'");
        }
        Assert.Equal(409, (await ctrl.ExecuteOffboardingWithClearance("EMP-A", new StaffLifecycleController.ExecuteOffboardingDto("RETIRE", null, "到龄退休"), default)).Code);
        Assert.Equal(200, (await ctrl.ExecuteOffboardingWithClearance("EMP-A", new StaffLifecycleController.ExecuteOffboardingDto("RETIRE", "U-DOC-B", "到龄退休"), default)).Code);
        using (var conn = db.CreateConnection())
        {
            Assert.Equal(0, await conn.ExecuteScalarAsync<int>("SELECT is_active FROM sys_users WHERE id = 'U-DOC-A'"));
            Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT revoke_status FROM mdm_ca_certificates WHERE staff_id = 'U-DOC-A'"));
        }

        Assert.Equal(200, (await ctrl.ActivateRetiredExpertRehire("EMP-A", new StaffLifecycleController.RehireExpertDto(20281231, 3, "普外科"), default)).Code);
        using (var conn = db.CreateConnection())
        {
            Assert.Equal("REHIRED_EXPERT", await conn.ExecuteScalarAsync<string>("SELECT employment_status FROM hr_employees WHERE id = 'EMP-A'"));
            Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT is_active FROM sys_users WHERE id = 'U-DOC-A'"));
            Assert.Equal(0, await conn.ExecuteScalarAsync<int>("SELECT surgery_level FROM sys_users WHERE id = 'U-DOC-A'"));
        }

        // 5) 外院多点执业专家到期自动熔断 + CME定期考核不合格自动停权
        Assert.Equal(200, (await ctrl.RegisterExternalExpert(new StaffLifecycleController.RegisterExternalExpertDto("刘院士", "北京协和医院", "LIC-999", 4, 5), default)).Code);
        using (var conn = db.CreateConnection())
        {
            // 模拟外院专家备案已超期 -> 巡检自动熔断
            await conn.ExecuteAsync("UPDATE hr_external_experts SET valid_until_ms = @Past", new { Past = now - 60_000L });
        }
        var sweep = await ctrl.RunExpiryAndRotationSweep(default);
        Assert.Equal(200, sweep.Code);
        Assert.Contains("expiredExternalExpertsDisabled = 1", sweep.Data?.ToString() ?? string.Empty);

        Assert.Equal(200, (await ctrl.RecordCmeAndPeriodicExam(new StaffLifecycleController.RecordCmeEthicsDto("EMP-A", "U-DOC-A", "陈主治", 2026, 5m, 5m, "PASS", "FAIL", "定期考核未通过"), default)).Code);
        using (var conn = db.CreateConnection())
        {
            Assert.Equal(0, await conn.ExecuteScalarAsync<int>("SELECT prescribe_level FROM sys_users WHERE id = 'U-DOC-A'"));
        }
    }
}
