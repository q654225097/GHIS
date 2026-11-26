using System;
using System.Data.Common;
using System.Threading.Tasks;
using Dapper;

namespace HIS.BuildingBlocks.Database;

public static partial class DbSchemaInitializer
{
    private static async Task InitializeFourRoleClosedLoopTablesAsync(DbConnection conn)
    {
        await conn.ExecuteAsync(@"
-- ============================================================================
-- 角色一：挂号员/收费员闭环表（爽约黑名单、三方退检退费申请池、交接班备用金）
-- ============================================================================
CREATE TABLE IF NOT EXISTS pat_no_show_records (
    id VARCHAR(36) PRIMARY KEY,
    patient_id VARCHAR(36) NOT NULL,
    patient_name VARCHAR(128) NOT NULL,
    appointment_id VARCHAR(36) NOT NULL,
    missed_at_ms BIGINT NOT NULL,
    blacklist_until_ms BIGINT,
    is_blacklisted SMALLINT NOT NULL DEFAULT 0,
    remark VARCHAR(500),
    created_at_ms BIGINT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_pat_noshow_patient ON pat_no_show_records (patient_id, missed_at_ms);

CREATE TABLE IF NOT EXISTS opd_refund_requests (
    id VARCHAR(36) PRIMARY KEY,
    request_no VARCHAR(40) NOT NULL UNIQUE,
    patient_id VARCHAR(36) NOT NULL,
    patient_name VARCHAR(128) NOT NULL,
    visit_id VARCHAR(36) NOT NULL,
    charge_ledger_id VARCHAR(36) NOT NULL,
    item_name VARCHAR(128) NOT NULL,
    refund_amount DECIMAL(18,2) NOT NULL,
    reason VARCHAR(500) NOT NULL,
    initiator_doctor VARCHAR(64) NOT NULL,
    exec_dept_confirmed SMALLINT NOT NULL DEFAULT 0,
    exec_confirmer_name VARCHAR(64),
    exec_confirmed_at_ms BIGINT,
    status VARCHAR(32) NOT NULL DEFAULT 'PENDING_EXEC_CONFIRM',
    cashier_name VARCHAR(64),
    refunded_at_ms BIGINT,
    created_at_ms BIGINT NOT NULL,
    updated_at_ms BIGINT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_opd_refund_req_status ON opd_refund_requests (status);

CREATE TABLE IF NOT EXISTS fin_cashier_shift_handovers (
    id VARCHAR(36) PRIMARY KEY,
    shift_no VARCHAR(40) NOT NULL UNIQUE,
    cashier_id VARCHAR(36) NOT NULL,
    cashier_name VARCHAR(64) NOT NULL,
    shift_type VARCHAR(24) NOT NULL,
    opening_petty_cash DECIMAL(18,2) NOT NULL DEFAULT 0,
    system_cash_total DECIMAL(18,2) NOT NULL DEFAULT 0,
    actual_cash_counted DECIMAL(18,2) NOT NULL DEFAULT 0,
    diff_amount DECIMAL(18,2) NOT NULL DEFAULT 0,
    receiver_name VARCHAR(64),
    status VARCHAR(24) NOT NULL DEFAULT 'SUBMITTED',
    note VARCHAR(500),
    handover_at_ms BIGINT NOT NULL,
    created_at_ms BIGINT NOT NULL
);

-- ============================================================================
-- 角色二：医生临床闭环表（病危/病重法定通知书、检验结果一键回写病历溯源）
-- ============================================================================
CREATE TABLE IF NOT EXISTS emr_critical_notices (
    id VARCHAR(36) PRIMARY KEY,
    notice_no VARCHAR(40) NOT NULL UNIQUE,
    encounter_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    patient_name VARCHAR(128) NOT NULL,
    dept_name VARCHAR(64) NOT NULL,
    notice_level VARCHAR(24) NOT NULL DEFAULT 'CRITICALLY_ILL',
    linked_order_id VARCHAR(36),
    clinical_condition TEXT NOT NULL,
    issued_doctor VARCHAR(64) NOT NULL,
    family_signer_name VARCHAR(64),
    family_relation VARCHAR(32),
    family_sign_svg TEXT,
    signed_at_ms BIGINT,
    reported_to_med_affairs SMALLINT NOT NULL DEFAULT 0,
    status VARCHAR(24) NOT NULL DEFAULT 'PENDING_FAMILY_SIGN',
    created_at_ms BIGINT NOT NULL,
    updated_at_ms BIGINT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_emr_critical_enc ON emr_critical_notices (encounter_id, status);

CREATE TABLE IF NOT EXISTS emr_lab_import_refs (
    id VARCHAR(36) PRIMARY KEY,
    emr_document_id VARCHAR(36) NOT NULL,
    encounter_id VARCHAR(36) NOT NULL,
    report_id VARCHAR(36) NOT NULL,
    imported_summary TEXT NOT NULL,
    imported_by VARCHAR(64) NOT NULL,
    imported_at_ms BIGINT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_emr_lab_ref_doc ON emr_lab_import_refs (emr_document_id);

-- ============================================================================
-- 角色三：护士安全闭环表（皮试20分双人判定、输液反应联动ADR、UD分时摆药、床旁宣教）
-- ============================================================================
CREATE TABLE IF NOT EXISTS nur_skin_test_records (
    id VARCHAR(36) PRIMARY KEY,
    test_no VARCHAR(40) NOT NULL UNIQUE,
    encounter_or_visit_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    patient_name VARCHAR(128) NOT NULL,
    drug_code VARCHAR(64) NOT NULL,
    drug_name VARCHAR(128) NOT NULL,
    batch_no VARCHAR(64),
    injected_at_ms BIGINT NOT NULL,
    due_at_ms BIGINT NOT NULL,
    injector_name VARCHAR(64) NOT NULL,
    result VARCHAR(24) NOT NULL DEFAULT 'PENDING',
    reader_name VARCHAR(64),
    checker_name VARCHAR(64),
    read_at_ms BIGINT,
    allergy_synced SMALLINT NOT NULL DEFAULT 0,
    note VARCHAR(500),
    created_at_ms BIGINT NOT NULL,
    updated_at_ms BIGINT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_nur_skintest_pat ON nur_skin_test_records (patient_id, result);

CREATE TABLE IF NOT EXISTS nur_infusion_reactions (
    id VARCHAR(36) PRIMARY KEY,
    reaction_no VARCHAR(40) NOT NULL UNIQUE,
    infusion_order_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    patient_name VARCHAR(128) NOT NULL,
    drug_name VARCHAR(128) NOT NULL,
    batch_no VARCHAR(64),
    stopped_at_ms BIGINT NOT NULL,
    remaining_ml DECIMAL(8,2) NOT NULL DEFAULT 0,
    seal_code VARCHAR(64) NOT NULL,
    symptoms TEXT NOT NULL,
    rescue_action TEXT,
    notified_doctor VARCHAR(64),
    linked_adr_id VARCHAR(36),
    reporter_name VARCHAR(64) NOT NULL,
    status VARCHAR(24) NOT NULL DEFAULT 'SEALED_AND_REPORTED',
    created_at_ms BIGINT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_nur_infusion_react_pat ON nur_infusion_reactions (patient_id);

CREATE TABLE IF NOT EXISTS phm_ward_ud_packages (
    id VARCHAR(36) PRIMARY KEY,
    package_barcode VARCHAR(64) NOT NULL UNIQUE,
    encounter_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    patient_name VARCHAR(128) NOT NULL,
    ward_name VARCHAR(64) NOT NULL,
    bed_code VARCHAR(32),
    take_date_int INT NOT NULL,
    take_slot VARCHAR(24) NOT NULL,
    drug_summary VARCHAR(500) NOT NULL,
    status VARCHAR(24) NOT NULL DEFAULT 'PACKED',
    packed_by VARCHAR(64),
    ward_received_by VARCHAR(64),
    ward_received_at_ms BIGINT,
    administered_by VARCHAR(64),
    administered_at_ms BIGINT,
    returned_reason VARCHAR(500),
    returned_at_ms BIGINT,
    created_at_ms BIGINT NOT NULL,
    updated_at_ms BIGINT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_phm_ud_ward_date ON phm_ward_ud_packages (ward_name, take_date_int, status);

CREATE TABLE IF NOT EXISTS nur_bedside_education_logs (
    id VARCHAR(36) PRIMARY KEY,
    encounter_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    patient_name VARCHAR(128) NOT NULL,
    edu_stage VARCHAR(32) NOT NULL,
    edu_content TEXT NOT NULL,
    mastery_level VARCHAR(24) NOT NULL DEFAULT 'UNDERSTOOD',
    educator_name VARCHAR(64) NOT NULL,
    patient_acknowledged SMALLINT NOT NULL DEFAULT 1,
    educated_at_ms BIGINT NOT NULL
);

-- ============================================================================
-- 角色四：患者 C 端轻量化门户表（账号代绑、检查准备指引主数据、病历复印邮寄、消息触达）
-- ============================================================================
CREATE TABLE IF NOT EXISTS pat_portal_accounts (
    id VARCHAR(36) PRIMARY KEY,
    phone_number VARCHAR(32) NOT NULL UNIQUE,
    display_name VARCHAR(128) NOT NULL,
    wechat_openid VARCHAR(128),
    primary_patient_id VARCHAR(36) NOT NULL,
    is_active SMALLINT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    updated_at_ms BIGINT NOT NULL
);

CREATE TABLE IF NOT EXISTS pat_family_bindings (
    id VARCHAR(36) PRIMARY KEY,
    portal_account_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    patient_name VARCHAR(128) NOT NULL,
    relation VARCHAR(32) NOT NULL,
    is_default SMALLINT NOT NULL DEFAULT 0,
    created_at_ms BIGINT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_pat_family_acc ON pat_family_bindings (portal_account_id);

CREATE TABLE IF NOT EXISTS cfg_exam_prep_instructions (
    id VARCHAR(36) PRIMARY KEY,
    item_keyword VARCHAR(64) NOT NULL UNIQUE,
    location_guide VARCHAR(128) NOT NULL,
    fasting_required SMALLINT NOT NULL DEFAULT 0,
    prep_notice VARCHAR(500) NOT NULL,
    service_hours VARCHAR(64) NOT NULL DEFAULT '07:30-17:00',
    is_active SMALLINT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL
);

CREATE TABLE IF NOT EXISTS mrms_copy_requests (
    id VARCHAR(36) PRIMARY KEY,
    copy_no VARCHAR(40) NOT NULL UNIQUE,
    patient_id VARCHAR(36) NOT NULL,
    patient_name VARCHAR(128) NOT NULL,
    encounter_id VARCHAR(36) NOT NULL,
    purpose VARCHAR(64) NOT NULL,
    copies_count INT NOT NULL DEFAULT 1,
    delivery_mode VARCHAR(24) NOT NULL DEFAULT 'EXPRESS',
    mail_address VARCHAR(500),
    contact_phone VARCHAR(32),
    express_no VARCHAR(64),
    status VARCHAR(24) NOT NULL DEFAULT 'SUBMITTED',
    reviewer_name VARCHAR(64),
    reviewed_at_ms BIGINT,
    created_at_ms BIGINT NOT NULL,
    updated_at_ms BIGINT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_mrms_copy_status ON mrms_copy_requests (status);

CREATE TABLE IF NOT EXISTS pat_notification_logs (
    id VARCHAR(36) PRIMARY KEY,
    patient_id VARCHAR(36) NOT NULL,
    channel VARCHAR(24) NOT NULL DEFAULT 'MINIAPP',
    scene VARCHAR(32) NOT NULL,
    title VARCHAR(128) NOT NULL,
    content VARCHAR(1000) NOT NULL,
    sent_at_ms BIGINT NOT NULL
);
");

        // 扩展既有表列（安全幂等）
        await EnsureColumnAsync(conn, "opd_appointments", "checkin_deadline_ms", "BIGINT");
        await EnsureColumnAsync(conn, "opd_triage_queues", "visit_stage", "VARCHAR(24) NOT NULL DEFAULT 'INITIAL'");
        await EnsureColumnAsync(conn, "pat_patients", "allergy_history_text", "VARCHAR(1000)");

        // 预置 8 条高频检验检查患者指引与注意事项主数据（空腹/憋尿/楼层定位）
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var prepSeeds = new (string Id, string Keyword, string Loc, int Fasting, string Notice, string Hours)[]
        {
            ("PREP-BLOOD", "生化", "门诊楼 2 层检验科采血大厅 1-6 号窗", 1, "需空腹 8-12 小时（可少量饮白开水），建议上午 10:00 前完成采血。", "07:30-11:30"),
            ("PREP-CBC", "血常规", "门诊楼 2 层检验科采血大厅", 0, "无需空腹，采血后请按压针眼 5 分钟避免淤青。", "07:30-17:30"),
            ("PREP-CT", "CT", "医技楼 1 层放射科 CT 候诊区", 0, "增强 CT 需空腹 4 小时并提前确认无碘对比剂过敏史，检查时请去除金属饰品。", "08:00-17:30"),
            ("PREP-MRI", "磁共振", "医技楼 1 层核磁共振室", 0, "严禁携带金属物品、银行卡、手机入室；体内有心脏起搏器或金属植入物请提前告知技师。", "08:00-17:00"),
            ("PREP-US-ABD", "腹部超声", "门诊楼 3 层超声医学科", 1, "肝胆胰脾超声需空腹 8 小时以上；泌尿系/妇科经腹超声需提前饮水 500-800ml 适度憋尿。", "08:00-17:00"),
            ("PREP-ECG", "心电图", "门诊楼 3 层心电功能科", 0, "无需空腹，检查前请静坐休息 5-10 分钟，穿着宽松易解衣物。", "08:00-17:30"),
            ("PREP-GASTRO", "胃镜", "医技楼 3 层内镜中心", 1, "检查前禁食 8 小时、禁水 4 小时；无痛胃镜须由家属陪同且检查后 24 小时内禁止驾车。", "08:00-12:00"),
            ("PREP-PHM", "取药", "门诊楼 1 层中心西药房 / 中药房", 0, "缴费后请查看指引单或候药大屏，凭就诊二维码至对应窗口核对姓名取药。", "08:00-18:00")
        };

        foreach (var p in prepSeeds)
        {
            int exists = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM cfg_exam_prep_instructions WHERE id = @Id OR item_keyword = @Kw", new { p.Id, Kw = p.Keyword });
            if (exists == 0)
            {
                await conn.ExecuteAsync(@"
INSERT INTO cfg_exam_prep_instructions (id, item_keyword, location_guide, fasting_required, prep_notice, service_hours, is_active, created_at_ms)
VALUES (@Id, @Kw, @Loc, @Fast, @Notice, @Hours, 1, @Now);",
                    new { p.Id, Kw = p.Keyword, p.Loc, Fast = p.Fasting, p.Notice, p.Hours, Now = now });
            }
        }
    }
}
