using System;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Modules.Emr.Controllers;
using HIS.Modules.Inpatient.Controllers;
using HIS.Modules.Inventory.Controllers;
using HIS.Modules.Outpatient.Controllers;
using Xunit;

namespace HIS.Tests.Integration;

public sealed class ClinicalClosedLoopAndForecastTests
{
    private static async Task<SqliteConnectionFactory> CreateFreshDbAsync()
    {
        var db = new SqliteConnectionFactory("Data Source=:memory:;Mode=Memory;Cache=Shared");
        await DbSchemaInitializer.InitializeAsync(db);
        return db;
    }

    [Fact]
    public async Task ErTriage_Computes_Mews_Level1_And_Opens_GreenChannel()
    {
        var db = await CreateFreshDbAsync();
        var ctrl = new EmergencyAndOpdProController(db);

        var res = await ctrl.CreateErTriage(new EmergencyAndOpdProController.CreateErTriageDto(
            PatientName: "张三",
            Gender: "男",
            Age: 68,
            ChiefComplaint: "意识模糊伴低血压",
            TempC: 39.2m,
            HeartRate: 138,
            RespRate: 32,
            Sbp: 75,
            Spo2: 88,
            GcsScore: 8,
            IsGreenChannel: false));

        Assert.True(res.Code is 0 or 200, res.Message);
        using var conn = db.CreateConnection();
        var row = await conn.QueryFirstAsync<dynamic>("SELECT triage_level, is_green_channel, destination_zone FROM er_triage_visits LIMIT 1");
        Assert.Equal(1, Convert.ToInt32(row.triage_level));
        Assert.Equal(1, Convert.ToInt32(row.is_green_channel));
        Assert.Equal("RESUS_ROOM", (string)row.destination_zone);
    }

    [Fact]
    public async Task Consultation_Completion_AutoWrites_EmrDocument()
    {
        var db = await CreateFreshDbAsync();
        var ctrl = new InpatientProController(db);

        var create = await ctrl.CreateConsultation(new InpatientProController.CreateConsultDto(
            EncounterId: "ENC-99",
            PatientName: "李四",
            BedCode: "08床",
            RequestDeptName: "普外科",
            TargetDeptName: "心内科",
            ConsultType: "EMERGENCY",
            PurposeSummary: "术后频发室早"));
        Assert.True(create.Code is 0 or 200, create.Message);

        using var conn = db.CreateConnection();
        string cid = await conn.ExecuteScalarAsync<string>("SELECT id FROM ipd_consultations LIMIT 1") ?? string.Empty;
        var done = await ctrl.CompleteConsultation(cid, new InpatientProController.CompleteConsultDto("建议胺碘酮负荷量静推并持续心电监护"));
        Assert.True(done.Code is 0 or 200, done.Message);

        int emrCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM emr_documents WHERE doc_type_code = 'CONSULT_RECORD'");
        Assert.Equal(1, emrCount);
    }

    [Fact]
    public async Task InventoryForecast_Computes_Weighted_Usage_And_DaysOutOfStock()
    {
        var db = await CreateFreshDbAsync();
        long now = SystemTime.UtcNowMs;
        int futureExp = int.Parse(SystemTime.ShanghaiNow.AddDays(180).ToString("yyyyMMdd"));

        using (var conn = db.CreateConnection())
        {
            await conn.ExecuteAsync(@"
                INSERT INTO inv_drugs (id, drug_code, drug_name, spec, unit_name, price, cost_price, antibiotic_level, is_narcotic, is_skin_test_required, is_active, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES ('D-1', 'AMX001', '阿莫西林胶囊', '0.25g*24', '盒', 18.0, 10.0, 1, 0, 1, 1, 1, @Now, @Now, 1)",
                new { Now = now });

            await conn.ExecuteAsync(@"
                INSERT INTO inv_drug_batches (id, warehouse_or_pharmacy_id, drug_id, batch_no, expiry_date_int, quantity, is_frozen, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES ('B-1', 'WH-MAIN', 'D-1', 'LOT2026', @Exp, 50, 0, 1, @Now, @Now, 1)",
                new { Exp = futureExp, Now = now });

            await conn.ExecuteAsync(@"
                INSERT INTO inv_stock_ledgers (id, warehouse_or_pharmacy_id, drug_id, batch_id, change_type, change_quantity, balance_after, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES ('L-1', 'WH-MAIN', 'D-1', 'B-1', 'DISPENSE', -300, 50, 1, @Now, @Now, 1)",
                new { Now = now });
        }

        var ctrl = new InventoryForecastController(db);
        var res = await ctrl.GetReplenishmentForecast("WH-MAIN", safetyDays: 14);
        Assert.True(res.Code is 0 or 200, res.Message);
        var item = Assert.Single(res.Data!);
        Assert.Equal(50, item.CurrentValidStock);
        Assert.True(item.DaysOutOfStock < 14);
        Assert.Equal("HIGH_SHORTAGE_RISK", item.RiskLevel);
        Assert.True(item.SuggestedOrder30d > 0);
    }

    [Fact]
    public async Task Mrms_Coding_And_Seal_Locks_Case_Record()
    {
        var db = await CreateFreshDbAsync();
        var ctrl = new MrmsController(db);

        var c = await ctrl.CreateCase(new MrmsController.CreateCaseDto("王五", "女", 52, "呼吸内科", "J18.9", "社区获得性肺炎"));
        Assert.True(c.Code is 0 or 200, c.Message);

        using var conn = db.CreateConnection();
        string id = await conn.ExecuteScalarAsync<string>("SELECT id FROM mrms_case_records LIMIT 1") ?? string.Empty;

        var coded = await ctrl.UpdateCodingAndArchive(id, new MrmsController.UpdateCodingDto("J18.9", "社区获得性肺炎", null, null, null, "ARCHIVED", "B-02-01", "质控合格"));
        Assert.True(coded.Code is 0 or 200, coded.Message);

        var sealedRes = await ctrl.CreateBorrowLog(new MrmsController.CreateBorrowDto(id, "SEAL", "医务科", "医疗争议依法封存", 20261231));
        Assert.True(sealedRes.Code is 0 or 200, sealedRes.Message);

        string status = await conn.ExecuteScalarAsync<string>("SELECT status FROM mrms_case_records WHERE id = @Id", new { Id = id }) ?? string.Empty;
        Assert.Equal("SEALED", status);
    }
}
