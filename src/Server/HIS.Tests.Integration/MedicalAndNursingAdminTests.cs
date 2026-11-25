using System;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Modules.Emr.Controllers;
using HIS.Modules.Inpatient.Controllers;
using HIS.Modules.Operations.Controllers;
using Xunit;

namespace HIS.Tests.Integration;

public sealed class MedicalAndNursingAdminTests
{
    private static async Task<SqliteConnectionFactory> CreateFreshDbAsync()
    {
        var db = new SqliteConnectionFactory("Data Source=:memory:;Mode=Memory;Cache=Shared");
        await DbSchemaInitializer.InitializeAsync(db);
        return db;
    }

    [Fact]
    public async Task MedicalAffairs_PrivilegeSuspend_Syncs_SysUsers_And_Dispute_Seals_Mrms()
    {
        var db = await CreateFreshDbAsync();
        long now = SystemTime.UtcNowMs;
        using (var conn = db.CreateConnection())
        {
            await conn.ExecuteAsync(@"
                INSERT INTO sys_users (id, user_name, display_name, employee_no, password_hash, prescribe_level, surgery_level, emr_sign_level, session_version, is_active, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES ('DOC-9', 'dr_lin', '林医师', 'E009', 'HASH', 1, 1, 1, 1, 1, 1, @Now, @Now, 1)", new { Now = now });
        }

        var medCtrl = new MedicalAffairsController(db);
        await medCtrl.GrantPrivilege(new MedicalAffairsController.GrantPrivilegeDto("DOC-9", "林医师", "普外科", 3, 4, true, 20271231), default);

        using var conn2 = db.CreateConnection();
        Assert.Equal(4, await conn2.ExecuteScalarAsync<int>("SELECT surgery_level FROM sys_users WHERE id = 'DOC-9'"));

        string gid = await conn2.ExecuteScalarAsync<string>("SELECT id FROM med_privilege_grants LIMIT 1") ?? string.Empty;
        await medCtrl.SuspendPrivilege(gid, new MedicalAffairsController.SuspendPrivilegeDto("发生非计划再次手术暂停四级手术权限"), default);
        Assert.Equal(0, await conn2.ExecuteScalarAsync<int>("SELECT surgery_level FROM sys_users WHERE id = 'DOC-9'"));

        // 纠纷联动病案封存
        var mrmsCtrl = new MrmsController(db);
        await mrmsCtrl.CreateCase(new MrmsController.CreateCaseDto("纠纷患者", "男", 50, "普外科", "K35.8", "急性阑尾炎"));
        await medCtrl.CreateDispute(new MedicalAffairsController.CreateDisputeDto("纠纷患者", "普外科", null, "家属质疑术后并发症要求封存病历", 0m));
        string did = await conn2.ExecuteScalarAsync<string>("SELECT id FROM med_dispute_cases LIMIT 1") ?? string.Empty;
        var sealRes = await medCtrl.SealDisputeMrmsCase(did);
        Assert.True(sealRes.Code is 0 or 200, sealRes.Message);
        Assert.Equal("SEALED", await conn2.ExecuteScalarAsync<string>("SELECT status FROM mrms_case_records WHERE patient_name = '纠纷患者'"));
    }

    [Fact]
    public async Task NursingMgmt_Blocks_NightToDay_Shift_And_Enforces_Sbar_DualSign()
    {
        var db = await CreateFreshDbAsync();
        var nurCtrl = new NursingManagementController(db);

        var s1 = await nurCtrl.ScheduleShift(new NursingManagementController.ScheduleShiftDto("心内一病区", 20261121, "N01", "陈护士", "N2", "NIGHT", 10m, null));
        Assert.True(s1.Code is 0 or 200, s1.Message);

        // 次日直接排白班 -> 400 阻断
        var badShift = await nurCtrl.ScheduleShift(new NursingManagementController.ScheduleShiftDto("心内一病区", 20261122, "N01", "陈护士", "N2", "DAY", 8m, null));
        Assert.Equal(400, badShift.Code);
        Assert.Contains("严禁直接排白班", badShift.Message);

        // SBAR 交接班异人双签
        await nurCtrl.CreateHandover(new NursingManagementController.CreateHandoverDto("心内一病区", "DAY", "EVENING", 32, 2, 3, 5, "3床病危持续心电监护"));
        using var conn = db.CreateConnection();
        string hid = await conn.ExecuteScalarAsync<string>("SELECT id FROM nur_shift_handovers LIMIT 1") ?? string.Empty;
        var badAck = await nurCtrl.AckHandover(hid, new NursingManagementController.AckHandoverDto("SYSTEM"));
        Assert.Equal(400, badAck.Code);
        var okAck = await nurCtrl.AckHandover(hid, new NursingManagementController.AckHandoverDto("李护师"));
        Assert.True(okAck.Code is 0 or 200, okAck.Message);
    }
}
