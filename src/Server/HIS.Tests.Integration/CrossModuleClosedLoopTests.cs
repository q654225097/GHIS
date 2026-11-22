using System;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Modules.Checkup.Controllers;
using HIS.Modules.Checkup.Models;
using HIS.Modules.Inpatient.Controllers;
using HIS.Modules.Inpatient.Services;
using HIS.Modules.Inventory.Controllers;
using HIS.Modules.Outpatient.Controllers;
using Xunit;

namespace HIS.Tests.Integration;

/// <summary>
/// 全院 9 大跨模块端到端业务闭环集成测试
/// </summary>
public sealed class CrossModuleClosedLoopTests
{
    private static async Task<SqliteConnectionFactory> CreateFreshDbAsync()
    {
        var db = new SqliteConnectionFactory("Data Source=:memory:;Mode=Memory;Cache=Shared");
        await DbSchemaInitializer.InitializeAsync(db);
        return db;
    }

    [Fact]
    public async Task Loop1_ErAdmitToIpd_Creates_AdmissionCard_And_CarriesOver_GreenCredit()
    {
        var db = await CreateFreshDbAsync();
        var erCtrl = new EmergencyAndOpdProController(db);

        await erCtrl.CreateErTriage(new EmergencyAndOpdProController.CreateErTriageDto(
            "急诊患者A", "男", 60, "急性心肌梗死", 36.8m, 115, 24, 85, 92, 13, true));

        using var conn = db.CreateConnection();
        string erId = await conn.ExecuteScalarAsync<string>("SELECT id FROM er_triage_visits LIMIT 1") ?? string.Empty;

        var res = await erCtrl.UpdateErStatus(erId, new EmergencyAndOpdProController.UpdateErStatusDto("ADMITTED_IPD", "CARDIO_WARD", "CCU-01", 1280.50m));
        Assert.Equal(0, res.Code);

        int cardCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ipd_admission_cards WHERE visit_id = @Id", new { Id = erId });
        decimal carriedFee = await conn.ExecuteScalarAsync<decimal>("SELECT COALESCE(SUM(total_amount),0) FROM fin_charge_ledgers WHERE item_code = 'ER_GREEN_CREDIT_CARRYOVER'");
        Assert.Equal(1, cardCount);
        Assert.Equal(1280.50m, carriedFee);
    }

    [Fact]
    public async Task Loop3_And_Loop4_DrugReturn_Restocks_Batch_And_Checklist_Blocks_Pending_Items()
    {
        var db = await CreateFreshDbAsync();
        long now = SystemTime.UtcNowMs;
        int exp = int.Parse(SystemTime.ShanghaiNow.AddDays(180).ToString("yyyyMMdd"));

        using (var conn = db.CreateConnection())
        {
            await conn.ExecuteAsync(@"
                INSERT INTO inv_drugs (id, drug_code, drug_name, spec, unit_name, price, cost_price, antibiotic_level, is_narcotic, is_skin_test_required, is_active, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES ('DRG-1', 'CEF01', '头孢曲松钠', '1g', '支', 25.0, 12.0, 2, 0, 1, 1, 1, @Now, @Now, 1)", new { Now = now });
            await conn.ExecuteAsync(@"
                INSERT INTO inv_drug_batches (id, warehouse_or_pharmacy_id, drug_id, batch_no, expiry_date_int, quantity, is_frozen, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES ('BAT-1', 'IPD_PHARMACY', 'DRG-1', 'B2026', @Exp, 100, 0, 1, @Now, @Now, 1)", new { Exp = exp, Now = now });
        }

        var ipdCtrl = new InpatientProController(db);
        await ipdCtrl.CreateDrugReturn(new InpatientProController.CreateDrugReturnDto("ENC-LOOP", "住院患者B", "02床", "头孢曲松钠", 3, 75m, "停嘱退药"));
        await ipdCtrl.CreateConsultation(new InpatientProController.CreateConsultDto("ENC-LOOP", "住院患者B", "02床", "内科", "外科", "ROUTINE", "评估手术指征"));
        await ipdCtrl.CreateDischargeMed(new InpatientProController.CreateDischargeMedDto("ENC-LOOP", "住院患者B", "头孢曲松钠 1g 口服", 50m));

        // 闭环 4：出院核查必须阻断未完成会诊、待审退药与未发出院带药
        var checklist = new DischargeChecklistService(db);
        var checkRes = await checklist.PerformSixItemCheckAsync("ENC-LOOP");
        Assert.Contains(checkRes.BlockingReasons, r => r.Contains("全院会诊"));
        Assert.Contains(checkRes.BlockingReasons, r => r.Contains("病区退药"));
        Assert.Contains(checkRes.BlockingReasons, r => r.Contains("出院带药"));

        // 闭环 3：退药审批通过 -> 库存 100 -> 103
        using var conn2 = db.CreateConnection();
        string retId = await conn2.ExecuteScalarAsync<string>("SELECT id FROM ipd_drug_returns LIMIT 1") ?? string.Empty;
        var rev = await ipdCtrl.ReviewDrugReturn(retId, new InpatientProController.ReviewDrugReturnDto("APPROVED"));
        Assert.Equal(0, rev.Code);
        int qtyAfterReturn = await conn2.ExecuteScalarAsync<int>("SELECT quantity FROM inv_drug_batches WHERE id = 'BAT-1'");
        Assert.Equal(103, qtyAfterReturn);
    }

    [Fact]
    public async Task Loop8_And_Loop9_Forecast_Generates_TransferOrder_And_DaySurgery_Issues_AdmissionCard()
    {
        var db = await CreateFreshDbAsync();
        long now = SystemTime.UtcNowMs;
        int exp = int.Parse(SystemTime.ShanghaiNow.AddDays(180).ToString("yyyyMMdd"));

        using (var conn = db.CreateConnection())
        {
            await conn.ExecuteAsync(@"
                INSERT INTO inv_drugs (id, drug_code, drug_name, spec, unit_name, price, cost_price, antibiotic_level, is_narcotic, is_skin_test_required, is_active, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES ('DRG-F', 'ASP01', '阿司匹林肠溶片', '100mg', '盒', 15.0, 8.0, 0, 0, 0, 1, 1, @Now, @Now, 1)", new { Now = now });
            await conn.ExecuteAsync(@"
                INSERT INTO inv_drug_batches (id, warehouse_or_pharmacy_id, drug_id, batch_no, expiry_date_int, quantity, is_frozen, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES ('BAT-SRC', 'WH-MAIN', 'DRG-F', 'L1', @Exp, 500, 0, 1, @Now, @Now, 1),
                       ('BAT-TGT', 'PHM-OPD', 'DRG-F', 'L1', @Exp, 10, 0, 1, @Now, @Now, 1)", new { Exp = exp, Now = now });
            await conn.ExecuteAsync(@"
                INSERT INTO inv_stock_ledgers (id, warehouse_or_pharmacy_id, drug_id, batch_id, change_type, change_quantity, balance_after, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES ('SL-1', 'PHM-OPD', 'DRG-F', 'BAT-TGT', 'DISPENSE', -200, 10, 1, @Now, @Now, 1)", new { Now = now });
        }

        // 闭环 8：一键生成补货调拨单写入 inv_transfer_orders
        var fCtrl = new InventoryForecastController(db);
        var genRes = await fCtrl.GenerateReplenishmentTransfers(new InventoryForecastController.GenerateReplenishTransferDto("WH-MAIN", "PHM-OPD", 30, 14));
        Assert.Equal(0, genRes.Code);

        using var conn2 = db.CreateConnection();
        int trCnt = await conn2.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM inv_transfer_orders WHERE transfer_type = 'AUTO_REPLENISH'");
        Assert.Equal(1, trCnt);

        // 闭环 9：日间手术超期转常规住院自动签发入院证
        var ipdCtrl = new InpatientProController(db);
        await ipdCtrl.CreateDaySurgeryCase(new InpatientProController.CreateDaySurgeryDto("日间患者C", "女", 42, "普外科", "腹腔镜胆囊切除术", "王主任"));
        string dsId = await conn2.ExecuteScalarAsync<string>("SELECT id FROM ipd_day_surgery_cases LIMIT 1") ?? string.Empty;
        var conv = await ipdCtrl.UpdateDaySurgeryStatus(dsId, new InpatientProController.UpdateDaySurgeryDto("CONVERTED_IPD", 7, "术后低热留院观察"));
        Assert.Equal(0, conv.Code);

        int dsCardCnt = await conn2.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ipd_admission_cards WHERE source_dept_id = 'DAY_SURGERY'");
        Assert.Equal(1, dsCardCnt);
    }
}
