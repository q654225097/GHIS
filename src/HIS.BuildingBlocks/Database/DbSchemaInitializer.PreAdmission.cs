using System.Data.Common;
using Dapper;

namespace HIS.BuildingBlocks.Database;

public static partial class DbSchemaInitializer
{
    private static async Task InitializePreAdmissionTablesAsync(DbConnection conn)
    {
        await conn.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS ipd_pre_admission_config (
    id VARCHAR(36) PRIMARY KEY,
    default_valid_days INT NOT NULL DEFAULT 7,
    expiry_warn_hours INT NOT NULL DEFAULT 48,
    critical_escalate_minutes INT NOT NULL DEFAULT 30,
    eval_escalate_hours INT NOT NULL DEFAULT 12,
    allowed_fee_categories VARCHAR(255) NOT NULL DEFAULT 'LAB,EXAM,PREOP_PREP',
    blocked_fee_categories VARCHAR(255) NOT NULL DEFAULT 'BED,NURSING,IPD_CONSULT',
    require_informed_consent SMALLINT NOT NULL DEFAULT 1,
    allow_doctor_pre_split SMALLINT NOT NULL DEFAULT 1,
    require_finance_split_audit SMALLINT NOT NULL DEFAULT 1,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS ipd_pre_admissions (
    id VARCHAR(36) PRIMARY KEY,
    pre_admit_no VARCHAR(40) NOT NULL UNIQUE,
    patient_id VARCHAR(36),
    patient_name VARCHAR(128) NOT NULL,
    gender VARCHAR(16) NOT NULL DEFAULT '男',
    age INT NOT NULL DEFAULT 45,
    medical_record_no VARCHAR(32),
    source_opd_visit_id VARCHAR(36),
    target_dept_id VARCHAR(36),
    target_dept_name VARCHAR(64) NOT NULL,
    primary_diag_code VARCHAR(32),
    primary_diag_name VARCHAR(200) NOT NULL,
    planned_surgery_name VARCHAR(200),
    consent_signed SMALLINT NOT NULL DEFAULT 1,
    consent_doc_id VARCHAR(36),
    valid_days INT NOT NULL DEFAULT 7,
    registered_at_ms BIGINT NOT NULL,
    valid_until_ms BIGINT NOT NULL,
    status VARCHAR(32) NOT NULL DEFAULT 'CHECKING',
    total_order_count INT NOT NULL DEFAULT 0,
    done_order_count INT NOT NULL DEFAULT 0,
    has_critical_alert SMALLINT NOT NULL DEFAULT 0,
    critical_diag_name VARCHAR(200),
    critical_summary VARCHAR(1000),
    critical_triggered_at_ms BIGINT,
    converted_encounter_id VARCHAR(36),
    converted_dept_name VARCHAR(64),
    converted_primary_diag VARCHAR(200),
    admitted_at_ms BIGINT,
    remark VARCHAR(500),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_ipd_pre_adm_status ON ipd_pre_admissions (status, registered_at_ms DESC);

CREATE TABLE IF NOT EXISTS ipd_pre_admission_orders (
    id VARCHAR(36) PRIMARY KEY,
    pre_admission_id VARCHAR(36) NOT NULL,
    bundle_id VARCHAR(36),
    service_request_id VARCHAR(36),
    report_id VARCHAR(36),
    fee_category VARCHAR(32) NOT NULL DEFAULT 'LAB',
    item_code VARCHAR(64) NOT NULL,
    item_name VARCHAR(200) NOT NULL,
    exec_dept_name VARCHAR(64),
    amount DECIMAL(18,4) NOT NULL DEFAULT 0,
    exec_status VARCHAR(32) NOT NULL DEFAULT 'PENDING',
    billing_track VARCHAR(32) NOT NULL DEFAULT 'PRE_ADMIT_HOLD',
    doctor_split_suggest VARCHAR(32) NOT NULL DEFAULT 'KEEP_IPD',
    finance_split_confirm VARCHAR(32) NOT NULL DEFAULT 'PENDING',
    is_critical SMALLINT NOT NULL DEFAULT 0,
    result_summary VARCHAR(1000),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_ipd_pre_adm_ord_main ON ipd_pre_admission_orders (pre_admission_id, exec_status);

CREATE TABLE IF NOT EXISTS ipd_pre_admission_evaluations (
    id VARCHAR(36) PRIMARY KEY,
    pre_admission_id VARCHAR(36) NOT NULL,
    eval_type VARCHAR(32) NOT NULL DEFAULT 'ROUTINE_EVAL',
    rule_level VARCHAR(16) NOT NULL DEFAULT 'P2',
    decision VARCHAR(32) NOT NULL,
    new_primary_diag VARCHAR(200),
    new_target_dept VARCHAR(64),
    reason_note TEXT,
    referenced_report_ids_json TEXT,
    evaluator_id VARCHAR(36),
    evaluator_name VARCHAR(64),
    signature_hash VARCHAR(64),
    evaluated_at_ms BIGINT NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_ipd_pre_adm_eval_main ON ipd_pre_admission_evaluations (pre_admission_id, evaluated_at_ms DESC);

CREATE TABLE IF NOT EXISTS ipd_pre_admission_rules (
    id VARCHAR(36) PRIMARY KEY,
    rule_code VARCHAR(64) NOT NULL UNIQUE,
    rule_name VARCHAR(128) NOT NULL,
    rule_level VARCHAR(16) NOT NULL DEFAULT 'P0',
    source_diag_prefix VARCHAR(64) NOT NULL DEFAULT '*',
    match_keyword VARCHAR(200) NOT NULL,
    target_specialty_dept VARCHAR(64) NOT NULL,
    on_hit_action VARCHAR(64) NOT NULL DEFAULT 'INTERRUPT_AND_HOLD',
    is_active SMALLINT NOT NULL DEFAULT 1,
    remark VARCHAR(500),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
");

        // 增量补齐跨模块关联字段（不破坏既有表结构）
        await EnsureColumnAsync(conn, "emr_documents", "pre_admission_id", "VARCHAR(36)");
        await EnsureColumnAsync(conn, "mtc_diagnostic_reports", "pre_admission_id", "VARCHAR(36)");
        await EnsureColumnAsync(conn, "fin_charge_ledgers", "pre_admission_id", "VARCHAR(36)");
        await EnsureColumnAsync(conn, "fin_charge_ledgers", "billing_track", "VARCHAR(32) DEFAULT 'STANDARD'");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 默认可配置参数（7天有效期、30分钟P0危急值升级、12小时齐套未评升级、复合拆分开启）
        await conn.ExecuteAsync(@"
INSERT OR IGNORE INTO ipd_pre_admission_config
(id, default_valid_days, expiry_warn_hours, critical_escalate_minutes, eval_escalate_hours, allowed_fee_categories, blocked_fee_categories, require_informed_consent, allow_doctor_pre_split, require_finance_split_audit, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('DEFAULT', 7, 48, 30, 12, 'LAB,EXAM,PREOP_PREP', 'BED,NURSING,IPD_CONSULT', 1, 1, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

INSERT OR IGNORE INTO ipd_pre_admission_rules
(id, rule_code, rule_name, rule_level, source_diag_prefix, match_keyword, target_specialty_dept, on_hit_action, is_active, remark, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('PA-R-P0-AMI',  'P0-AMI',  '急性心肌梗死/恶性心律失常危急值抢占', 'P0', '*', '心肌梗死|ST段抬高|室颤|肌钙蛋白危急', '心内科/急诊科', 'INTERRUPT_AND_HOLD', 1, '命中即中断预住院并锁停未执行高危检查', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('PA-R-P0-K',    'P0-K',    '重度高钾/低钾血症危急值抢占',       'P0', '*', '高钾|血钾>6.2|血钾<2.5',              '急诊科/肾内科', 'INTERRUPT_AND_HOLD', 1, '防止携严重电解质紊乱进行侵入性检查',   1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('PA-R-P0-PE',   'P0-PE',   '肺栓塞/主动脉夹层/消化道穿孔抢占',   'P0', '*', '肺栓塞|主动脉夹层|消化道穿孔|颅内出血', '急诊科/ICU',    'INTERRUPT_AND_HOLD', 1, '直接转绿色通道或紧急收治',             1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('PA-R-P1-DM',   'P1-DM',   '重度高血糖/酮症手术禁忌会诊拦截',     'P1', '*', '血糖>13.9|酮症|HbA1c>10',             '内分泌科/麻醉科', 'REQUIRE_CONSULT',  1, '阻断直接转正式住院，需专科会诊调理',   1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('PA-R-P1-COAG', 'P1-COAG', '显著凝血功能异常术前评估拦截',       'P1', '*', 'INR>1.5|血小板<50|凝血异常',           '血液科/麻醉科',   'REQUIRE_CONSULT',  1, '需麻醉科与专科联合评估出血风险',       1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);
", new { Now = now });
    }
}
