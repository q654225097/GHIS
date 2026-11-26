using System;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Modules.Inpatient.Controllers;
using HIS.Modules.Outpatient.Controllers;
using HIS.Modules.Patient.Controllers;
using Xunit;

namespace HIS.Tests.Integration;

/// <summary>
/// 四角色（挂号员 · 医生 · 护士 · 患者）16 处现实闭环端到端回归测试
/// </summary>
public sealed class FourRoleRealWorkflowClosureTests
{
    private static async Task<SqliteConnectionFactory> CreateFreshDbAsync()
    {
        var db = new SqliteConnectionFactory($"Data Source=four_role_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await DbSchemaInitializer.InitializeAsync(db);
        return db;
    }

    [Fact]
    public async Task Role1_Registrar_Reschedule_NoShowBlacklist_And_ThreePartyRefundGate()
    {
        var db = await CreateFreshDbAsync();
        long now = SystemTime.UtcNowMs;
        using (var conn = db.CreateConnection())
        {
            // 准备两个排班号源（普通号15元 -> 专家号50元）和一条已挂号记录 + 3条超时未取号记录
            await conn.ExecuteAsync(@"
INSERT INTO opd_schedule_slots (id, campus_id, opd_dept_id, doctor_id, schedule_date_int, slot_period_code, reg_level_code, registration_fee, total_capacity, used_count, is_active, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES
('SLOT-OLD', 'C1', 'DEPT-1', 'DOC-1', 20261126, 'AM', 'NORMAL', 15.00, 30, 1, 1, 1, @Now, @Now, 1),
('SLOT-NEW', 'C1', 'DEPT-2', 'DOC-2', 20261126, 'AM', 'EXPERT', 50.00, 20, 0, 1, 1, @Now, @Now, 1);

INSERT INTO opd_appointments (id, appoint_no, schedule_id, patient_id, channel, status, paid_amount, checkin_deadline_ms, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES
('APPT-RESCH', 'AP001', 'SLOT-OLD', 'PAT-1', 'WINDOW', 'PAID', 15.00, @Future, 1, @Now, @Now, 1),
('APPT-EXP-1', 'AP002', 'SLOT-OLD', 'PAT-BLK', 'ONLINE', 'RESERVED', 0, @Past, 1, @Now, @Now, 1),
('APPT-EXP-2', 'AP003', 'SLOT-OLD', 'PAT-BLK', 'ONLINE', 'RESERVED', 0, @Past, 1, @Now, @Now, 1),
('APPT-EXP-3', 'AP004', 'SLOT-OLD', 'PAT-BLK', 'ONLINE', 'RESERVED', 0, @Past, 1, @Now, @Now, 1);

INSERT INTO fin_charge_ledgers (id, patient_id, visit_or_encounter_id, order_dept_id, patient_ward_id, exec_dept_id, item_code, item_name, quantity, unit_price, total_amount, status, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES ('CL-CT-1', 'PAT-1', 'VIS-1', 'DEPT-1', 'DEPT-1', 'RAD-1', 'CT-CHEST', '胸部CT平扫', 1, 260, 260, 'SETTLED', 1, @Now, @Now, 1);",
                new { Now = now, Future = now + 3600_000L, Past = now - 3600_000L });
        }

        var ctrl = new RegistrarWindowClosureController(db);

        // 1) 同日改签：差价 50 - 15 = +35 元，旧号池减1、新号池加1
        var resch = await ctrl.RescheduleAppointment("APPT-RESCH", new RegistrarWindowClosureController.RescheduleDto("SLOT-NEW", "患者要求换专家号"), default);
        Assert.Equal(200, resch.Code);
        using (var conn = db.CreateConnection())
        {
            Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT used_count FROM opd_schedule_slots WHERE id = 'SLOT-NEW'"));
        }

        // 2) 超时未取号自动释号 + 3次爽约触发黑名单冻结
        var rel = await ctrl.ReleaseExpiredReservations(default);
        Assert.Equal(200, rel.Code);
        var blk = await ctrl.GetBlacklistStatus("PAT-BLK", default);
        Assert.Equal(200, blk.Code);
        Assert.Contains("isBlacklisted = True", blk.Data?.ToString() ?? string.Empty);

        // 3) 三方退费门禁：未医技确认直接退费 -> 400 阻断；确认后 -> 200 退费并作废原费用台账
        var createRf = await ctrl.CreateRefundRequest(new RegistrarWindowClosureController.CreateRefundRequestDto("PAT-1", "张某", "VIS-1", "CL-CT-1", "胸部CT平扫", 260m, "患者暂缓检查", "李医生"), default);
        Assert.Equal(200, createRf.Code);
        using var conn2 = db.CreateConnection();
        string rfId = await conn2.ExecuteScalarAsync<string>("SELECT id FROM opd_refund_requests LIMIT 1") ?? string.Empty;

        Assert.Equal(400, (await ctrl.ExecuteCashierRefund(rfId, default)).Code);
        Assert.Equal(200, (await ctrl.ConfirmNotExecuted(rfId, new RegistrarWindowClosureController.ExecConfirmDto("放射科技师"), default)).Code);
        Assert.Equal(200, (await ctrl.ExecuteCashierRefund(rfId, default)).Code);
        Assert.Equal("VOIDED", await conn2.ExecuteScalarAsync<string>("SELECT status FROM fin_charge_ledgers WHERE id = 'CL-CT-1'"));
    }

    [Fact]
    public async Task Role2_Doctor_ReportReturnQueue_CriticalNotice_And_LabImportToEmr()
    {
        var db = await CreateFreshDbAsync();
        long now = SystemTime.UtcNowMs;
        using (var conn = db.CreateConnection())
        {
            await conn.ExecuteAsync(@"
INSERT INTO emr_documents (id, doc_no, campus_id, emr_scene, visit_or_encounter_id, patient_id, dept_id, template_id, template_version, doc_type_code, doc_title, clinical_time_ms, plain_text_summary, status, author_id, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES ('DOC-1', 'EMR001', 'C1', 'IPD', 'ENC-1', 'PAT-1', 'DEPT-1', 'T1', 1, 'PROGRESS', '首次病程记录', @Now, '患者入院一般情况可。', 1, 'DOC-1', 1, @Now, @Now, 1);", new { Now = now });
        }

        var ctrl = new DoctorClinicalClosureController(db);

        // 1) 看报告回诊 2:1 插队
        var ret = await ctrl.CheckinReportReturn(new DoctorClinicalClosureController.ReportReturnCheckinDto("VIS-RET-1", "DEPT-OPD-1", "R1"), default);
        Assert.Equal(200, ret.Code);

        // 2) 病危通知书签发 + 家属电子签名归档
        Assert.Equal(200, (await ctrl.IssueCriticalNotice(new DoctorClinicalClosureController.IssueCriticalNoticeDto("ENC-1", "PAT-1", "危重患者", "ICU", "CRITICALLY_ILL", "ORD-CRIT", "休克、呼吸衰竭"), default)).Code);
        using var conn2 = db.CreateConnection();
        string nid = await conn2.ExecuteScalarAsync<string>("SELECT id FROM emr_critical_notices LIMIT 1") ?? string.Empty;
        Assert.Equal(200, (await ctrl.SignCriticalNoticeByFamily(nid, new DoctorClinicalClosureController.FamilySignNoticeDto("王某", "儿子", "<svg/>"), default)).Code);
        Assert.Equal("SIGNED", await conn2.ExecuteScalarAsync<string>("SELECT status FROM emr_critical_notices WHERE id = @Id", new { Id = nid }));

        // 3) 检验结果一键回写病历
        Assert.Equal(200, (await ctrl.ImportLabResultIntoEmr(new DoctorClinicalClosureController.ImportLabToEmrDto("DOC-1", "ENC-1", "REP-88", "血钾 6.3 mmol/L (危急高值)"), default)).Code);
        string summary = await conn2.ExecuteScalarAsync<string>("SELECT plain_text_summary FROM emr_documents WHERE id = 'DOC-1'") ?? string.Empty;
        Assert.Contains("血钾 6.3 mmol/L", summary);
    }

    [Fact]
    public async Task Role3_Nurse_SkinTestDualSign_InfusionReactionAdr_And_UdOralLifecycle()
    {
        var db = await CreateFreshDbAsync();
        long now = SystemTime.UtcNowMs;
        using (var conn = db.CreateConnection())
        {
            await conn.ExecuteAsync(@"
INSERT INTO pat_patients (id, medical_record_no, full_name, gender_code, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES ('PAT-N1', 'MRN-N1', '过敏患者', 'F', 1, @Now, @Now, 1);", new { Now = now });
        }

        var ctrl = new NurseSafetyClosureController(db);

        // 1) 皮试注射 -> 同人复核400拦截 -> 异人双签阳性并自动写回 pat_patients.allergy_history_text
        Assert.Equal(200, (await ctrl.InjectSkinTest(new NurseSafetyClosureController.InjectSkinTestDto("ENC-N1", "PAT-N1", "过敏患者", "DRUG-PEN", "青霉素钠", "B01", 20), default)).Code);
        using var conn2 = db.CreateConnection();
        string stId = await conn2.ExecuteScalarAsync<string>("SELECT id FROM nur_skin_test_records LIMIT 1") ?? string.Empty;

        Assert.Equal(400, (await ctrl.JudgeSkinTest(stId, new NurseSafetyClosureController.JudgeSkinTestDto("POSITIVE", "护士甲", "护士甲", null), default)).Code);
        Assert.Equal(200, (await ctrl.JudgeSkinTest(stId, new NurseSafetyClosureController.JudgeSkinTestDto("POSITIVE", "护士甲", "护士乙", "红肿硬结"), default)).Code);
        string allergy = await conn2.ExecuteScalarAsync<string>("SELECT allergy_history_text FROM pat_patients WHERE id = 'PAT-N1'") ?? string.Empty;
        Assert.Contains("青霉素钠", allergy);

        // 2) 突发输液反应应急停液封存 -> 自动写入 adr_reports
        Assert.Equal(200, (await ctrl.EmergencyStopInfusion(new NurseSafetyClosureController.EmergencyStopInfusionDto("INF-9", "PAT-N1", "过敏患者", "头孢呋辛", "B02", 60m, "寒战高热", "停液吸氧", "王医生"), default)).Code);
        Assert.Equal(1, await conn2.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM adr_reports WHERE patient_id = 'PAT-N1'"));

        // 3) 住院口服药 UD 分包 -> 病区核收 -> PDA 发药
        Assert.Equal(200, (await ctrl.PackUdOralMedication(new NurseSafetyClosureController.PackUdDto("ENC-N1", "PAT-N1", "过敏患者", "心内病区", "05床", 20261126, "MORNING", "阿司匹林100mg"), default)).Code);
        string udId = await conn2.ExecuteScalarAsync<string>("SELECT id FROM phm_ward_ud_packages LIMIT 1") ?? string.Empty;
        Assert.Equal(200, (await ctrl.WardReceiveUdPackage(udId, default)).Code);
        Assert.Equal(200, (await ctrl.AdministerUdPackageByPda(udId, default)).Code);
        Assert.Equal("ADMINISTERED", await conn2.ExecuteScalarAsync<string>("SELECT status FROM phm_ward_ud_packages WHERE id = @Id", new { Id = udId }));
    }

    [Fact]
    public async Task Role4_PatientPortal_FamilyBind_FastingGuideSheet_And_MedicalRecordCopyExpress()
    {
        var db = await CreateFreshDbAsync();
        var ctrl = new PatientPortalController(db);

        // 1) 患者账号注册 + 家庭成员代绑
        var reg = await ctrl.RegisterAccount(new PatientPortalController.RegisterPortalAccountDto("13900001111", "陈先生", "PAT-P1", "wx_open_01"), default);
        Assert.Equal(200, reg.Code);
        using var conn = db.CreateConnection();
        string accId = await conn.ExecuteScalarAsync<string>("SELECT id FROM pat_portal_accounts LIMIT 1") ?? string.Empty;
        Assert.Equal(200, (await ctrl.BindFamilyMember(accId, new PatientPortalController.BindFamilyMemberDto("PAT-CHILD", "陈宝宝", "子女"), default)).Code);

        // 2) 自动生成含楼层与空腹警示的就诊指引单
        var guide = await ctrl.GenerateVisitGuideSheet("VIS-G1", "肝功能生化全套,腹部超声,取药", default);
        Assert.Equal(200, guide.Code);
        Assert.Contains("hasFastingItem = True", guide.Data?.ToString() ?? string.Empty);

        // 3) 病历复印邮寄申请（无地址400拦截 -> 填地址200提交 -> 病案室审核填快递单号变为 SHIPPED）
        Assert.Equal(400, (await ctrl.SubmitMedicalRecordCopyRequest(new PatientPortalController.CreateCopyRequestDto("PAT-P1", "陈先生", "ENC-P1", "商保理赔", 1, "EXPRESS", null, "13900001111"), default)).Code);
        Assert.Equal(200, (await ctrl.SubmitMedicalRecordCopyRequest(new PatientPortalController.CreateCopyRequestDto("PAT-P1", "陈先生", "ENC-P1", "商保理赔", 2, "EXPRESS", "上海市浦东新区88号", "13900001111"), default)).Code);

        string copyId = await conn.ExecuteScalarAsync<string>("SELECT id FROM mrms_copy_requests LIMIT 1") ?? string.Empty;
        Assert.Equal(200, (await ctrl.ReviewAndShipCopyRequest(copyId, new PatientPortalController.ReviewCopyRequestDto(true, "SF9988776655"), default)).Code);
        Assert.Equal("SHIPPED", await conn.ExecuteScalarAsync<string>("SELECT status FROM mrms_copy_requests WHERE id = @Id", new { Id = copyId }));
    }
}
