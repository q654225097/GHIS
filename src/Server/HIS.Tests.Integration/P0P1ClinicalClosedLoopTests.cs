using System;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Modules.Consumables.Controllers;
using HIS.Modules.Finance.Controllers;
using HIS.Modules.Inpatient.Controllers;
using HIS.Modules.Operations.Controllers;
using HIS.Modules.Outpatient.Controllers;
using HIS.Modules.Pharmacy.Controllers;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace HIS.Tests.Integration;

public sealed class P0P1ClinicalClosedLoopTests
{
    private static async Task<SqliteConnectionFactory> CreateFreshDbAsync()
    {
        var db = new SqliteConnectionFactory($"Data Source=p0p1_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await DbSchemaInitializer.InitializeAsync(db);
        return db;
    }

    [Fact]
    public async Task P0_FiveCenters_Enforces_Sla_And_Overtime_Reason_On_Complete()
    {
        var db = await CreateFreshDbAsync();
        var ctrl = new EmergencyFiveCentersController(db);
        long doorAt = DateTimeOffset.UtcNow.AddMinutes(-110).ToUnixTimeMilliseconds();

        var openRes = await ctrl.OpenCase(
            new EmergencyFiveCentersController.OpenCenterCaseDto("CHEST_PAIN", "PAT-CP-1", "急性心梗患者", "ER-1", doorAt - 1800_000, doorAt, "张主任"), default); Assert.Equal(200, openRes.Code);
        using var conn = db.CreateConnection();
        string caseId = await conn.ExecuteScalarAsync<string>("SELECT id FROM er_five_center_cases LIMIT 1") ?? string.Empty;

        // 导丝通过耗时 105 分钟 > 90 分钟标准限额
        await ctrl.RecordMilestone(caseId, new EmergencyFiveCentersController.RecordMilestoneDto("WIRE_CROSS", "导丝通过(Wire Cross)", doorAt + 105 * 60_000L, "PCI"), default);

        // 未填超时归因 -> HTTP 400 阻断
        var badComplete = await ctrl.CompleteCase(caseId, new EmergencyFiveCentersController.CompleteCenterCaseDto("PCI_SUCCESS", null), default);
        Assert.Equal(400, (badComplete).Code);

        // 填写归因 -> 成功结案
        var okComplete = await ctrl.CompleteCase(caseId, new EmergencyFiveCentersController.CompleteCenterCaseDto("PCI_SUCCESS", "家属签字延迟20分钟"), default);
        Assert.Equal(200, (okComplete).Code);
        Assert.Equal("COMPLETED", await conn.ExecuteScalarAsync<string>("SELECT status FROM er_five_center_cases WHERE id = @Id", new { Id = caseId }));
    }

    [Fact]
    public async Task P0_IcuCis_Computes_Apache_Sofa_And_Catheter_Hai_Links_SafetyEvent()
    {
        var db = await CreateFreshDbAsync();
        var ctrl = new IcuCisController(db);

        Assert.Equal(200, (await ctrl.AdmitToIcu(new IcuCisController.AdmitIcuDto("ENC-ICU-1", "PAT-ICU-1", "重症肺炎患者", "ICU-03", "呼吸衰竭", "李主任"), default)).Code);
        using var conn = db.CreateConnection();
        string admId = await conn.ExecuteScalarAsync<string>("SELECT id FROM icu_admissions LIMIT 1") ?? string.Empty;

        // 计算 APACHE II & SOFA
        Assert.Equal(200, (await ctrl.ComputeSeverityScores(admId, new IcuCisController.SeverityScoreDto(
            39.2m, 62, 125, 28, 160m, 80, 2.5m, 2.4m, 10, 72, true, true), default)).Code);
        int sofa = await conn.ExecuteScalarAsync<int>("SELECT sofa_score FROM icu_admissions WHERE id = @Id", new { Id = admId });
        Assert.True(sofa >= 10);

        // 登记三管并确诊 VAP -> 自动联动写入医务安全事件 med_safety_rca_events
        Assert.Equal(200, (await ctrl.InsertCatheter(admId, new IcuCisController.InsertCatheterDto("VAP", "经口气管插管"), default)).Code);
        string cathId = await conn.ExecuteScalarAsync<string>("SELECT id FROM icu_catheter_surveillance LIMIT 1") ?? string.Empty;
        Assert.Equal(200, (await ctrl.ConfirmCatheterInfection(cathId, new IcuCisController.ConfirmCatheterInfectionDto("痰培养鲍曼不动杆菌阳性"), default)).Code);

        int linkedHai = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM med_safety_rca_events WHERE event_category = 'ICU_HAI_CATHETER'");
        Assert.Equal(1, linkedHai);
    }

    [Fact]
    public async Task P0_DrgCost_Aggregates_Real_ChargeLedgers_And_Flags_RedOverrun()
    {
        var db = await CreateFreshDbAsync();
        long now = SystemTime.UtcNowMs;
        using (var conn = db.CreateConnection())
        {
            // EB19 肺炎基准费用 8500，写入 9800 元真实费用 -> 应触发 RED_OVERRUN
            await conn.ExecuteAsync(@"
INSERT INTO fin_charge_ledgers (id, patient_id, visit_or_encounter_id, order_dept_id, patient_ward_id, exec_dept_id, item_code, item_name, quantity, unit_price, total_amount, status, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES ('CL-1', 'PAT-D1', 'ENC-DRG-1', 'DEPT-1', 'WARD-1', 'DEPT-1', 'ITEM-1', '住院综合诊疗', 1, 9800.00, 9800.00, 'UNSETTLED', 1, @Now, @Now, 1);", new { Now = now });
        }

        var ctrl = new DrgCostControlController(db);
        Assert.Equal(200, (await ctrl.EvaluateEncounter("ENC-DRG-1", new DrgCostControlController.EvaluateEncounterDto("EB19", "王某", "呼吸科"), default)).Code);

        using var conn2 = db.CreateConnection();
        string level = await conn2.ExecuteScalarAsync<string>("SELECT alert_level FROM drg_realtime_alerts WHERE encounter_id = 'ENC-DRG-1'") ?? string.Empty;
        Assert.Equal("RED_OVERRUN", level);
    }

    [Fact]
    public async Task P0_PivasAndAbx_Blocks_OverPrivilege_And_Enforces_DualSign_Check()
    {
        var db = await CreateFreshDbAsync();
        long now = SystemTime.UtcNowMs;
        using (var conn = db.CreateConnection())
        {
            await conn.ExecuteAsync(@"
INSERT INTO sys_users (id, user_name, display_name, employee_no, password_hash, prescribe_level, surgery_level, emr_sign_level, session_version, is_active, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES ('DOC-JR', 'dr_jr', '初级医师', 'E101', 'HASH', 1, 1, 1, 1, 1, 1, @Now, @Now, 1);", new { Now = now });
        }

        var ctrl = new PivasAndAntimicrobialController(db);
        // 1级医师尝试开具3级特殊抗菌药美罗培南 -> allowed=false
        var privRes = await ctrl.CheckAntibioticPrivilege(new PivasAndAntimicrobialController.CheckAbxPrivilegeDto("DOC-JR", "DRUG-MEROPENEM"), default); Assert.Equal(200, privRes.Code);
        Assert.Contains("False", privRes.Data?.ToString() ?? string.Empty);

        // PIVAS 全流程：创建 -> 审方 -> 贴签 -> 调配 -> 同人核对400拦截 -> 异人核对通过 -> 出仓
        Assert.Equal(200, (await ctrl.CreatePivasOrder(new PivasAndAntimicrobialController.CreatePivasOrderDto("ORD-1", "ENC-1", "PAT-1", "张某", "ICU", "DRUG-MEROPENEM", "美罗培南", "0.9%NS 100ml", "0.5g", "BATCH_1"), default)).Code);
        using var conn2 = db.CreateConnection();
        string pivasId = await conn2.ExecuteScalarAsync<string>("SELECT id FROM pivas_orders LIMIT 1") ?? string.Empty;

        Assert.Equal(200, (await ctrl.ReviewPivasOrder(pivasId, new PivasAndAntimicrobialController.ReviewPivasDto(true, "BATCH_1", null, "审方药师"), default)).Code);
        Assert.Equal(200, (await ctrl.PrintLabel(pivasId, default)).Code);
        Assert.Equal(200, (await ctrl.CompoundOrder(pivasId, new PivasAndAntimicrobialController.CompoundPivasDto("药师甲"), default)).Code);

        // 调配人与核对人相同 -> HTTP 400 阻断
        Assert.Equal(400, (await ctrl.DoubleCheckOrder(pivasId, new PivasAndAntimicrobialController.CheckPivasDto("药师甲"), default)).Code);
        // 异人双签核对通过并出仓
        Assert.Equal(200, (await ctrl.DoubleCheckOrder(pivasId, new PivasAndAntimicrobialController.CheckPivasDto("药师乙"), default)).Code);
        Assert.Equal(200, (await ctrl.DispatchOrder(pivasId, default)).Code);
        Assert.Equal("DISPATCHED", await conn2.ExecuteScalarAsync<string>("SELECT status FROM pivas_orders WHERE id = @Id", new { Id = pivasId }));
    }

    [Fact]
    public async Task P1_PublicHealth_Triggers_HardBlock_Card_On_Icd10_Match()
    {
        var db = await CreateFreshDbAsync();
        var ctrl = new PublicHealthReportController(db);

        // 诊断肺结核 A15.0 -> 自动触发 INFECTIOUS 待报卡 + HardBlock
        Assert.Equal(200, (await ctrl.EvaluateDiagnosisTrigger(
            new PublicHealthReportController.EvaluateDiagnosisDto("PAT-TB", "肺结核患者", "VIS-TB-1", "感染科", "A15.0", "继发性肺结核"), default)).Code);

        using var conn = db.CreateConnection();
        string cardId = await conn.ExecuteScalarAsync<string>("SELECT id FROM ph_report_cards WHERE icd_code = 'A15.0'") ?? string.Empty;
        Assert.False(string.IsNullOrWhiteSpace(cardId));

        Assert.Equal(200, (await ctrl.SubmitCard(cardId, new PublicHealthReportController.SubmitCardDto("{\"sputum\":\"positive\"}", "林医生"), default)).Code);
        Assert.Equal(200, (await ctrl.AuditCard(cardId, new PublicHealthReportController.AuditCardDto(true, "审核通过"), default)).Code);
        Assert.Equal("AUDITED", await conn.ExecuteScalarAsync<string>("SELECT status FROM ph_report_cards WHERE id = @Id", new { Id = cardId }));
    }

    [Fact]
    public async Task P1_Followup_Detects_30Day_Unplanned_Readmission()
    {
        var db = await CreateFreshDbAsync();
        var ctrl = new FollowupAndReadmissionController(db);

        long disAt = DateTimeOffset.UtcNow.AddDays(-12).ToUnixTimeMilliseconds();
        Assert.Equal(200, (await ctrl.GenerateFromDischarge(
            new FollowupAndReadmissionController.GenerateFollowupDto("ENC-IDX-1", "PAT-HF-1", "心衰患者", "13800001111", "心衰专病", disAt, 7, "PHONE"), default)).Code);

        // 12 天后非计划再入院 -> 自动记录到 readmission_tracking
        Assert.Equal(200, (await ctrl.DetectReadmission30d(
            new FollowupAndReadmissionController.DetectReadmissionDto("PAT-HF-1", "心衰患者", "ENC-IDX-1", disAt, "ENC-READMIT-1", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), true, "心力衰竭急性加重"), default)).Code);

        using var conn = db.CreateConnection();
        Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM readmission_tracking WHERE readmit_encounter_id = 'ENC-READMIT-1'"));
    }

    [Fact]
    public async Task P1_UdiConsumables_Consumes_Bills_ChargeLedger_And_Generates_ConsignmentStatement()
    {
        var db = await CreateFreshDbAsync();
        var ctrl = new UdiConsumablesController(db);
        string udi = "(01)06912345678901(17)291231(10)LOT99(21)SN888";

        Assert.Equal(200, (await ctrl.ReceiveUdi(new UdiConsumablesController.ReceiveUdiDto(
            udi, "STENT-01", "药物洗脱冠脉支架", "3.0x18mm", "微创医疗", true, 6500m, 7200m, null), default)).Code);

        // 扫码消耗 -> 原子写 fin_charge_ledgers
        Assert.Equal(200, (await ctrl.ConsumeAndBillUdi(new UdiConsumablesController.ConsumeUdiDto(udi, "ENC-OR-9", "PAT-OR-9", "SURG-9", "OR", "OR"), default)).Code);

        using var conn = db.CreateConnection();
        Assert.Equal(7200m, await conn.ExecuteScalarAsync<decimal>("SELECT SUM(total_amount) FROM fin_charge_ledgers WHERE visit_or_encounter_id = 'ENC-OR-9'"));

        // 生成供应商代销对账单 -> 耗材状态变为 RECONCILED
        Assert.Equal(200, (await ctrl.GenerateConsignmentStatement(new UdiConsumablesController.GenerateStatementDto("微创医疗", "2026-11"), default)).Code);
        Assert.Equal("RECONCILED", await conn.ExecuteScalarAsync<string>("SELECT status FROM udi_implant_inventory WHERE udi_full = @Udi", new { Udi = udi }));
    }

    [Fact]
    public async Task P1_ClinicalNutrition_Nrs2002_Triggers_HighRisk_Consult_And_DietOrder()
    {
        var db = await CreateFreshDbAsync();
        var ctrl = new ClinicalNutritionController(db);

        // 疾病2分 + 营养1分 + 年龄75岁(1分) = 4分 >= 3 -> HIGH_RISK & trigger_consult=1
        Assert.Equal(200, (await ctrl.SubmitNrs2002(
            new ClinicalNutritionController.Nrs2002Dto("ENC-NUT-1", "PAT-NUT-1", "老年肿瘤患者", 17.5m, 2, 1, 75, "近期体重下降>5%"), default)).Code);

        using var conn = db.CreateConnection();
        Assert.Equal("HIGH_RISK", await conn.ExecuteScalarAsync<string>("SELECT risk_level FROM nut_nrs2002_screenings WHERE encounter_id = 'ENC-NUT-1'"));
        Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT trigger_consult FROM nut_nrs2002_screenings WHERE encounter_id = 'ENC-NUT-1'"));

        Assert.Equal(200, (await ctrl.CreateDietOrder(
            new ClinicalNutritionController.CreateDietOrderDto("ENC-NUT-1", "PAT-NUT-1", "老年肿瘤患者", "肿瘤一病区", "08床", "EN_FORMULA", 2000, 85, "早/中/晚/加餐", "海鲜过敏"), default)).Code);
        Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM nut_diet_orders WHERE encounter_id = 'ENC-NUT-1'"));
    }
}
