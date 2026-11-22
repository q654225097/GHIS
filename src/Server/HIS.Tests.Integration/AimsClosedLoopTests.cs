using System;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Modules.MedTech.Controllers;
using Xunit;

namespace HIS.Tests.Integration;

public sealed class AimsClosedLoopTests
{
    private static async Task<SqliteConnectionFactory> CreateFreshDbAsync()
    {
        var db = new SqliteConnectionFactory("Data Source=:memory:;Mode=Memory;Cache=Shared");
        await DbSchemaInitializer.InitializeAsync(db);
        return db;
    }

    [Fact]
    public async Task EmergencyInsert_Shifts_Elective_TableSeq_And_Preop_Writes_Emr()
    {
        var db = await CreateFreshDbAsync();
        long now = SystemTime.UtcNowMs;
        using (var conn = db.CreateConnection())
        {
            await conn.ExecuteAsync(@"
                INSERT INTO mtc_surgery_schedules
                (id, encounter_id, or_dept_id, or_room_code, table_seq, surgery_code, surgery_name, surgery_level, main_surgeon_id, anesthesiologist_id, circulating_nurse_id, sign_in_ok, time_out_ok, sign_out_ok, instrument_count_balanced, status, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES ('S-ELEC', 'ENC-1', 'OR', 'OR-01', 1, 'S01', '择期胆囊切除', 3, 'DOC1', 'ANES1', 'N1', 0, 0, 0, 1, 0, 1, @Now, @Now, 1)",
                new { Now = now });
        }

        var ctrl = new AimsController(db);
        var emgRes = await ctrl.EmergencyInsertOr(new AimsController.EmergencyInsertOrDto("ENC-EMG", "OR-01", "S99", "急性阑尾穿孔修补", 3, "张主任", "李麻醉"));
        Assert.Equal(0, emgRes.Code);

        using var conn2 = db.CreateConnection();
        int shiftedSeq = await conn2.ExecuteScalarAsync<int>("SELECT table_seq FROM mtc_surgery_schedules WHERE id = 'S-ELEC'");
        Assert.Equal(2, shiftedSeq);

        var preop = await ctrl.CreatePreopAssessment(new AimsController.CreatePreopDto("S-ELEC", "ENC-1", "陈患者", 70m, "III", 3, 6, 8, "全身麻醉"), default);
        Assert.Equal(0, preop.Code);
        int emrCnt = await conn2.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM emr_documents WHERE doc_type_code = 'ANESTHESIA_PREOP'");
        Assert.Equal(1, emrCnt);
    }

    [Fact]
    public async Task Narcotic_Reconcile_Enforces_Ampoule_Balance_And_DualSign_And_Pacu_Blocks_Low_Aldrete()
    {
        var db = await CreateFreshDbAsync();
        var ctrl = new AimsController(db);

        await ctrl.IssueNarcotic(new AimsController.IssueNarcoticDto("S-1", "患者甲", "舒芬太尼注射液", "LOT-N1", 2));
        using var conn = db.CreateConnection();
        string nid = await conn.ExecuteScalarAsync<string>("SELECT id FROM aims_narcotic_ledgers LIMIT 1") ?? string.Empty;

        // 空安瓿不符 -> 400 阻断
        var badNarc = await ctrl.ReconcileNarcotic(nid, new AimsController.ReconcileNarcoticDto(2, 0, 1, 0.5m, "王护士"), default);
        Assert.Equal(400, badNarc.Code);

        // 账实相符 + 异人双签 -> 通过
        var okNarc = await ctrl.ReconcileNarcotic(nid, new AimsController.ReconcileNarcoticDto(1, 1, 1, 0.2m, "王护士"), default);
        Assert.Equal(0, okNarc.Code);

        // PACU Aldrete 7 分 (<9) -> 阻断转出
        await ctrl.SavePacuAssessment(new AimsController.SavePacuDto("S-1", "患者甲", 1, 2, 1, 1, 2, "尚嗜睡"));
        string pidLow = await conn.ExecuteScalarAsync<string>("SELECT id FROM aims_pacu_assessments LIMIT 1") ?? string.Empty;
        var blocked = await ctrl.CompletePacuHandoff(pidLow, new AimsController.PacuHandoffDto("赵护师", "SBAR"));
        Assert.Equal(400, blocked.Code);

        // PACU Aldrete 10 分 -> 顺利交接
        await ctrl.SavePacuAssessment(new AimsController.SavePacuDto("S-1", "患者甲", 2, 2, 2, 2, 2, "清醒肌力恢复"));
        string pidOk = await conn.ExecuteScalarAsync<string>("SELECT id FROM aims_pacu_assessments WHERE aldrete_total = 10 LIMIT 1") ?? string.Empty;
        var okHandoff = await ctrl.CompletePacuHandoff(pidOk, new AimsController.PacuHandoffDto("赵护师", "生命体征平稳，PCA运行正常"));
        Assert.Equal(0, okHandoff.Code);
    }
}
