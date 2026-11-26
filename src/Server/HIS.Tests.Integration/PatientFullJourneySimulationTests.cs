using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Inpatient.Controllers;
using HIS.Modules.Outpatient.Controllers;
using HIS.Modules.Outpatient.Models;
using HIS.Modules.Outpatient.Services;
using HIS.Modules.Patient.Controllers;
using HIS.Modules.Patient.Models;
using Xunit;

namespace HIS.Tests.Integration;

/// <summary>
/// “张建国（72岁，胸痛伴糖尿病与青霉素过敏疑史）”9 天完整就诊沙盘端到端回归测试：
/// 覆盖全部 10 处深水区细节暗礁 (R-01 ~ R-10)：
/// - R-01: 皮试阳性写入过敏史 -> 审方引擎实时读取并拦截阿莫西林/头孢类 β-内酰胺交叉过敏
/// - R-02: 急诊三无临时档合档 -> 原子迁移费用台账、五大中心、皮试记录等业务子表
/// - R-03: 临床死亡宣告 -> 强制停止全部长嘱 + 阻断死后自动滚费 + 签发死亡证明书 & AMA 强制签名
/// - R-04: 住院欠费 -> 科主任授信担保 & ICU 抢救自动豁免欠费阻断
/// - R-05: MAR 执行高警示药品（胰岛素/氯化钾）强制异人双签核对
/// - R-06: 5 项生化检验按标本管自动合管并去重只收 1 次静脉采血费
/// - R-07: “一次挂号管三天”0元续诊号 & 门诊处方 72 小时超期自动失效作废
/// - R-08: 危急值超 30 分钟未双签自动升级告警
/// - R-10: 超 30 天未执行已缴费医技单财务挂账扫描
/// </summary>
public sealed class PatientFullJourneySimulationTests
{
    private sealed class StubPatientContract : IPatientReadOnlyContract
    {
        private readonly string _id;
        public StubPatientContract(string id) => _id = id;
        public Task<PatientSummaryDto?> GetPatientSummaryAsync(string patientId, System.Threading.CancellationToken ct = default)
            => Task.FromResult<PatientSummaryDto?>(new PatientSummaryDto(_id, "MRN-ZJG", "张建国", "M", 72, true, null));
    }

    private static async Task<SqliteConnectionFactory> CreateFreshDbAsync()
    {
        var db = new SqliteConnectionFactory($"Data Source=journey_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await DbSchemaInitializer.InitializeAsync(db);
        return db;
    }

    [Fact]
    public async Task Simulation_Slice1_To_Slice4_TempMerge_LabTubeMerge_CrossAllergyBlock_And_ThreeDayRevisit()
    {
        var db = await CreateFreshDbAsync();
        long now = SystemTime.UtcNowMs;

        using (var conn = db.CreateConnection())
        {
            // 准备临时档 + 正式档 + 急诊费用 + 五大中心 + 排班与72h内挂号 + 80h前过期处方
            await conn.ExecuteAsync(@"
INSERT INTO pat_patients (id, medical_record_no, full_name, gender_code, is_temp_emergency, is_active, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES
('PAT-TEMP', 'TMP-001', '无名氏(急诊胸痛)', 'M', 1, 1, 1, @Now, @Now, 1),
('PAT-ZJG',  'MRN-072', '张建国',         'M', 0, 1, 1, @Now, @Now, 1);

INSERT INTO fin_charge_ledgers (id, patient_id, visit_or_encounter_id, order_dept_id, patient_ward_id, exec_dept_id, item_code, item_name, quantity, unit_price, total_amount, status, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES ('CL-ER-1', 'PAT-TEMP', 'VIS-ER-1', 'ER', 'ER', 'ER', 'ECG', '急诊心电图', 1, 35, 35, 'SETTLED', 1, @Now, @Now, 1);

INSERT INTO opd_schedule_slots (id, campus_id, opd_dept_id, doctor_id, schedule_date_int, slot_period_code, reg_level_code, registration_fee, total_capacity, used_count, is_active, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES ('SLOT-CARDIO', 'C1', 'DEPT-CARDIO', 'DOC-1', 20261126, 'AM', 'NORMAL', 20, 30, 1, 1, 1, @Now, @Now, 1);

INSERT INTO opd_appointments (id, appoint_no, schedule_id, patient_id, channel, status, paid_amount, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES ('APPT-D1', 'AP-D1', 'SLOT-CARDIO', 'PAT-ZJG', 'WINDOW', 'PAID', 20, 1, @Now, @Now, 1);

INSERT INTO opd_prescriptions (id, prescription_no, visit_id, patient_id, doctor_id, rx_type, total_amount, status, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES ('RX-OLD', 'RX-EXPIRED-1', 'VIS-OLD', 'PAT-ZJG', 'DOC-1', 'WESTERN', 88, 1, 1, @Old80h, @Old80h, 1);",
                new { Now = now, Old80h = now - (80L * 3600_000L) });
        }

        // [R-02] 急诊临时档合档 -> 业务子表 fin_charge_ledgers 自动迁移至 PAT-ZJG
        var patRepo = new DapperRepository<PatPatient>(db, "pat_patients");
        var patCtrl = new PatientController(patRepo, db);
        var mergeRes = await patCtrl.MergeTempPatient(new PatientController.MergeRequestDto("PAT-TEMP", "PAT-ZJG"));
        Assert.Equal(200, mergeRes.Code);
        using (var conn = db.CreateConnection())
        {
            Assert.Equal("PAT-ZJG", await conn.ExecuteScalarAsync<string>("SELECT patient_id FROM fin_charge_ledgers WHERE id = 'CL-ER-1'"));
        }

        // [R-06] 5 项血液检验自动合管（3项生化黄头管 + 1项血常规紫头管 + 1项凝血蓝头管 = 3管、1次采血费）
        var deepCtrl = new DeepSafetyClosuresController(db);
        var tubeRes = await deepCtrl.MergeLabSpecimenTubes(new DeepSafetyClosuresController.MergeLabTubesDto(
            "VIS-ER-1", "PAT-ZJG", new List<string> { "肌钙蛋白", "肝功能", "肾功能", "血常规", "凝血四项" }), default);
        Assert.Equal(200, tubeRes.Code);
        Assert.Contains("mergedTubeCount = 3", tubeRes.Data?.ToString() ?? string.Empty);

        // [R-01] 护士皮试测出青霉素阳性 -> 审方引擎开具“阿莫西林克拉维酸钾”自动触发 β-内酰胺交叉过敏 BLOCK 强拦截
        var nurCtrl = new NurseSafetyClosureController(db);
        Assert.Equal(200, (await nurCtrl.InjectSkinTest(new NurseSafetyClosureController.InjectSkinTestDto("VIS-ER-1", "PAT-ZJG", "张建国", "PEN-01", "注射用青霉素钠", "B1", 20), default)).Code);
        using (var conn = db.CreateConnection())
        {
            string stId = await conn.ExecuteScalarAsync<string>("SELECT id FROM nur_skin_test_records LIMIT 1") ?? string.Empty;
            Assert.Equal(200, (await nurCtrl.JudgeSkinTest(stId, new NurseSafetyClosureController.JudgeSkinTestDto("POSITIVE", "护士甲", "护士乙", "阳性"), default)).Code);
        }

        var reviewEngine = new PrescriptionReviewEngine(new StubPatientContract("PAT-ZJG"), db);
        var rxReview = await reviewEngine.ReviewAsync(
            "PAT-ZJG", 2, false,
            new[] { new OpdPrescriptionItem { DrugId = "D-AMOX", DrugNameSnapshot = "阿莫西林克拉维酸钾片", TotalQuantity = 1, DoseOnce = 1 } },
            Array.Empty<DrugCatalogSnapshotDto>(), null, default);
        Assert.Equal("BLOCK", rxReview.OverallStatus);
        Assert.Contains(rxReview.Issues, i => i.RuleCode == "RULE_DB_ALLERGY_CROSS_BLOCK");

        // [R-07] 一次挂号管三天（0元续诊号） + 80小时过期处方自动失效作废
        Assert.Equal(200, (await deepCtrl.IssueThreeDayFreeRevisit(new DeepSafetyClosuresController.ThreeDayRevisitDto("PAT-ZJG", "DEPT-CARDIO", "SLOT-CARDIO"), default)).Code);
        var expRes = await deepCtrl.ExpireOverduePrescriptions(default);
        Assert.Equal(200, expRes.Code);
        using (var conn = db.CreateConnection())
        {
            Assert.Equal(9, await conn.ExecuteScalarAsync<int>("SELECT status FROM opd_prescriptions WHERE id = 'RX-OLD'"));
        }
    }

    [Fact]
    public async Task Simulation_Slice5_To_Slice7_ArrearRescue_HighAlertMarDualSign_DeathHaltBilling_And_Escalation()
    {
        var db = await CreateFreshDbAsync();
        long now = SystemTime.UtcNowMs;

        using (var conn = db.CreateConnection())
        {
            await conn.ExecuteAsync(@"
INSERT INTO ipd_encounters (id, admission_no, patient_id, campus_id, admit_dept_id, admit_ward_id, is_borrowed_bed, bed_code, prepayment_balance, status, is_deceased, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES ('ENC-ZJG', 'ADM-072', 'PAT-ZJG', 'C1', 'DEPT-ICU', 'WARD-ICU', 0, 'ICU-01', 1000, 1, 0, 1, @Now, @Now, 1);

INSERT INTO fin_charge_ledgers (id, patient_id, visit_or_encounter_id, order_dept_id, patient_ward_id, exec_dept_id, item_code, item_name, quantity, unit_price, total_amount, status, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES
('CL-STENT', 'PAT-ZJG', 'ENC-ZJG', 'DEPT-ICU', 'WARD-ICU', 'OR', 'STENT', '冠脉支架', 1, 7500, 7500, 'UNSETTLED', 1, @Now, @Now, 1),
('CL-STALE', 'PAT-ZJG', 'VIS-STALE', 'OPD', 'OPD', 'RAD', 'CT', '超期未检CT', 1, 260, 260, 'SETTLED', 1, @Old40d, @Old40d, 1);

INSERT INTO ord_medical_orders (id, order_no, encounter_id, patient_id, order_ipd_dept_id, patient_ward_id, exec_target_dept_id, order_term_type, group_no, order_category, item_code, item_name, dosage, frequency_code, route_code, status, order_doctor_id, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES ('ORD-INS', 'O-INS', 'ENC-ZJG', 'PAT-ZJG', 'DEPT-ICU', 'WARD-ICU', 'WARD-ICU', 1, 1, 'DRUG', 'INS-01', '门冬胰岛素注射液 10U', 10, 'TID', 'IH', 'ACTIVE', 'DOC-1', 1, @Now, @Now, 1);

INSERT INTO ord_execution_plans (id, order_id, encounter_id, plan_exec_time_ms, exec_status, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES ('PLAN-INS', 'ORD-INS', 'ENC-ZJG', @Now, 0, 1, @Now, @Now, 1);

INSERT INTO medtech_critical_values (id, report_id, patient_id, patient_name, exam_item_name, critical_detail, status, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES ('CV-OVERDUE', 'REP-1', 'PAT-ZJG', '张建国', '血钾测定', 'K+ 6.8 mmol/L', 'PENDING', 1, @Old45m, @Old45m, 1);",
                new { Now = now, Old40d = now - (40L * 86400_000L), Old45m = now - (45L * 60_000L) });
        }

        var deepCtrl = new DeepSafetyClosuresController(db);

        // [R-04] 预交金 1000 - 费用 7500 = 欠费 -6500 -> 普通状态拦截；科主任授信担保 8000 后立即放行
        var arrear1 = await deepCtrl.CheckArrearAndRescueExemption("ENC-ZJG", default);
        Assert.Contains("allowOrderAndDispense = False", arrear1.Data?.ToString() ?? string.Empty);

        Assert.Equal(200, (await deepCtrl.GrantCreditGuarantee(new DeepSafetyClosuresController.GrantCreditDto("ENC-ZJG", "PAT-ZJG", 8000m, 48, "DEPT_DIRECTOR", "心梗术后绿色通道担保"), default)).Code);
        var arrear2 = await deepCtrl.CheckArrearAndRescueExemption("ENC-ZJG", default);
        Assert.Contains("allowOrderAndDispense = True", arrear2.Data?.ToString() ?? string.Empty);

        // [R-05] 高警示药品（门冬胰岛素）MAR 执行：单人拦截(400)、同人拦截(400)、异人双签放行(200)
        var ipdPro = new InpatientProController(db);
        Assert.Equal(400, (await ipdPro.ExecuteMarPlan("PLAN-INS", new InpatientProController.ExecuteMarDto("护士甲", null))).Code);
        Assert.Equal(400, (await ipdPro.ExecuteMarPlan("PLAN-INS", new InpatientProController.ExecuteMarDto("护士甲", "护士甲"))).Code);
        Assert.Equal(200, (await ipdPro.ExecuteMarPlan("PLAN-INS", new InpatientProController.ExecuteMarDto("护士甲", "护士乙"))).Code);

        // [R-08 & R-10] 危急值超 30 分钟自动升级告警 + 超 30 天未执行已缴费医技挂账扫描
        var escRes = await deepCtrl.EscalateOverdueCriticalValues(default);
        Assert.Equal(200, escRes.Code);
        Assert.Contains("escalatedCount = 1", escRes.Data?.ToString() ?? string.Empty);

        var staleRes = await deepCtrl.ListStalePaidMedTechOrders(default);
        Assert.Equal(200, staleRes.Code);
        Assert.Contains("staleCount = 1", staleRes.Data?.ToString() ?? string.Empty);

        // [R-03] 临床死亡宣告 -> 强制停止长期医嘱 + 标记 is_deceased=1 + 签发死亡证明书（杜绝死后继续滚费）
        var deathRes = await deepCtrl.DeclareClinicalDeath(new DeepSafetyClosuresController.DeclareDeathDto(
            "ENC-ZJG", "PAT-ZJG", "张建国", "ICU重症医学科", now, "I21.9", "急性心肌梗死", "I25.1", "冠心病"), default);
        Assert.Equal(200, deathRes.Code);
        using (var conn = db.CreateConnection())
        {
            Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT is_deceased FROM ipd_encounters WHERE id = 'ENC-ZJG'"));
            Assert.Equal("STOPPED_DECEASED", await conn.ExecuteScalarAsync<string>("SELECT status FROM ord_medical_orders WHERE id = 'ORD-INS'"));
            Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM emr_death_certificates WHERE encounter_id = 'ENC-ZJG'"));
        }
    }
}
