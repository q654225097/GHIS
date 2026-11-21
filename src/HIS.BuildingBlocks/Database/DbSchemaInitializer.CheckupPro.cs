using System.Data.Common;
using Dapper;

namespace HIS.BuildingBlocks.Database;

public static partial class DbSchemaInitializer
{
    private static async Task InitializeCheckupProTablesAsync(DbConnection conn)
    {
        await conn.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS chk_appointments (
    id VARCHAR(36) PRIMARY KEY,
    appt_no VARCHAR(40) NOT NULL UNIQUE,
    appt_date_int INT NOT NULL,
    time_slot VARCHAR(32) NOT NULL DEFAULT '08:00-10:00',
    full_name VARCHAR(128) NOT NULL,
    gender VARCHAR(16) NOT NULL DEFAULT '男',
    age INT NOT NULL DEFAULT 30,
    phone_masked VARCHAR(32),
    id_card_masked VARCHAR(32),
    exam_category VARCHAR(32) NOT NULL DEFAULT 'HEALTH',
    company_id VARCHAR(36),
    company_name VARCHAR(128),
    package_id VARCHAR(36) NOT NULL,
    package_name VARCHAR(128) NOT NULL,
    status VARCHAR(32) NOT NULL DEFAULT 'BOOKED',
    converted_order_id VARCHAR(36),
    remark VARCHAR(500),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_chk_appt_date ON chk_appointments (appt_date_int, status);

CREATE TABLE IF NOT EXISTS chk_companies (
    id VARCHAR(36) PRIMARY KEY,
    company_code VARCHAR(64) NOT NULL UNIQUE,
    company_name VARCHAR(128) NOT NULL,
    credit_code VARCHAR(64),
    contact_name VARCHAR(64),
    contact_phone VARCHAR(32),
    discount_rate DECIMAL(6,4) NOT NULL DEFAULT 0.90,
    settlement_mode VARCHAR(32) NOT NULL DEFAULT 'POST_SETTLE',
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

CREATE TABLE IF NOT EXISTS chk_group_orders (
    id VARCHAR(36) PRIMARY KEY,
    group_no VARCHAR(40) NOT NULL UNIQUE,
    company_id VARCHAR(36) NOT NULL,
    company_name VARCHAR(128) NOT NULL,
    group_name VARCHAR(128) NOT NULL,
    gender_limit VARCHAR(16) NOT NULL DEFAULT 'ALL',
    package_id VARCHAR(36) NOT NULL,
    package_name VARCHAR(128) NOT NULL,
    planned_count INT NOT NULL DEFAULT 50,
    registered_count INT NOT NULL DEFAULT 0,
    completed_count INT NOT NULL DEFAULT 0,
    unit_price DECIMAL(18,4) NOT NULL DEFAULT 0,
    total_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
    settlement_status VARCHAR(32) NOT NULL DEFAULT 'UNSETTLED',
    remark VARCHAR(500),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_chk_group_company ON chk_group_orders (company_id, created_at_ms DESC);

CREATE TABLE IF NOT EXISTS chk_knowledge_base (
    id VARCHAR(36) PRIMARY KEY,
    kb_code VARCHAR(64) NOT NULL UNIQUE,
    disease_name VARCHAR(128) NOT NULL,
    match_keywords VARCHAR(500) NOT NULL,
    severity_level VARCHAR(16) NOT NULL DEFAULT 'B_IMPORTANT',
    conclusion_template TEXT NOT NULL,
    advice_template TEXT NOT NULL,
    suggest_dept VARCHAR(64),
    is_active SMALLINT NOT NULL DEFAULT 1,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS chk_followups (
    id VARCHAR(36) PRIMARY KEY,
    order_id VARCHAR(36) NOT NULL,
    checkup_no VARCHAR(40) NOT NULL,
    full_name VARCHAR(128) NOT NULL,
    phone_masked VARCHAR(32),
    positive_grade VARCHAR(16) NOT NULL DEFAULT 'B_IMPORTANT',
    finding_summary TEXT NOT NULL,
    suggest_dept VARCHAR(64),
    followup_status VARCHAR(32) NOT NULL DEFAULT 'PENDING',
    followup_note TEXT,
    referred_pre_admission_id VARCHAR(36),
    handled_by VARCHAR(64),
    handled_at_ms BIGINT,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_chk_followup_status ON chk_followups (positive_grade, followup_status);

CREATE TABLE IF NOT EXISTS chk_occupational_records (
    id VARCHAR(36) PRIMARY KEY,
    order_id VARCHAR(36) NOT NULL UNIQUE,
    checkup_no VARCHAR(40) NOT NULL,
    full_name VARCHAR(128) NOT NULL,
    company_name VARCHAR(128),
    occ_category VARCHAR(32) NOT NULL DEFAULT 'IN_JOB',
    hazard_factors VARCHAR(255) NOT NULL DEFAULT '噪声',
    work_years INT NOT NULL DEFAULT 1,
    occ_conclusion VARCHAR(32) NOT NULL DEFAULT 'NO_ABNORMAL',
    conclusion_detail TEXT,
    health_cert_no VARCHAR(64),
    health_cert_valid_until INT,
    report_upload_status VARCHAR(32) NOT NULL DEFAULT 'PENDING',
    uploaded_at_ms BIGINT,
    upload_payload_json TEXT,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
");

        // 增量补齐 chk_orders / chk_order_items / mtc_diagnostic_reports 扩展列
        await EnsureColumnAsync(conn, "chk_orders", "exam_category", "VARCHAR(32) DEFAULT 'HEALTH'");
        await EnsureColumnAsync(conn, "chk_orders", "group_order_id", "VARCHAR(36)");
        await EnsureColumnAsync(conn, "chk_orders", "company_id", "VARCHAR(36)");
        await EnsureColumnAsync(conn, "chk_orders", "questionnaire_json", "TEXT");
        await EnsureColumnAsync(conn, "chk_orders", "final_reviewer_name", "VARCHAR(64)");
        await EnsureColumnAsync(conn, "chk_orders", "final_reviewed_at_ms", "BIGINT");
        await EnsureColumnAsync(conn, "chk_order_items", "waive_reason", "VARCHAR(255)");
        await EnsureColumnAsync(conn, "chk_order_items", "is_addon", "SMALLINT DEFAULT 0");
        await EnsureColumnAsync(conn, "mtc_diagnostic_reports", "chk_order_item_id", "VARCHAR(36)");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 预置 10 条高频总检疾病结论与健康干预知识库
        await conn.ExecuteAsync(@"
INSERT OR IGNORE INTO chk_knowledge_base (id, kb_code, disease_name, match_keywords, severity_level, conclusion_template, advice_template, suggest_dept, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('KB-CHK-01', 'KB-UA',    '高尿酸血症',       '尿酸|UA',             'B_IMPORTANT', '血尿酸水平升高，提示高尿酸血症可能。', '建议低嘌呤饮食（限制动物内脏、海鲜、啤酒及浓肉汤），每日饮水2000ml以上，1~3个月后内分泌科复查血尿酸。', '内分泌科', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('KB-CHK-02', 'KB-FL',    '脂肪肝',           '脂肪肝|回声增强',     'B_IMPORTANT', '肝脏超声提示脂肪肝改变。', '建议控制总热量摄入、减少精制碳水与油脂，每周至少150分钟中等强度有氧运动，戒酒并定期复查肝功能及腹部彩超。', '消化内科', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('KB-CHK-03', 'KB-HTN',   '血压偏高/高血压',  '血压|高血压|140/90',  'B_IMPORTANT', '本次测得血压高于正常参考范围。', '建议连续7天早晚家庭自测血压并记录，低盐饮食（每日食盐<5g），若持续≥140/90mmHg请至心内科评估用药。', '心内科', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('KB-CHK-04', 'KB-LN',    '肺部结节',         '肺结节|磨玻璃|占位',  'A_CRITICAL',  '胸部影像检出肺部结节/占位病变（重要阳性发现）。', '请携影像胶片或电子影像尽快至胸外科或呼吸内科门诊进一步行高分辨薄层CT评估及随访管理。', '胸外科/呼吸内科', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('KB-CHK-05', 'KB-TN',    '甲状腺结节',       '甲状腺结节|TI-RADS',  'B_IMPORTANT', '甲状腺超声提示结节性病变。', '建议根据TI-RADS分级至甲乳外科或内分泌科复查甲状腺功能及超声随访。', '甲乳外科', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('KB-CHK-06', 'KB-DM',    '空腹血糖升高',     '血糖|GLU|HbA1c',      'B_IMPORTANT', '空腹血糖/糖化血红蛋白高于正常范围，提示糖耐量异常或糖尿病可能。', '建议内分泌科行OGTT糖耐量试验明确诊断，控制精制糖摄入并加强餐后运动。', '内分泌科', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('KB-CHK-07', 'KB-ALT',   '肝功能转氨酶升高', 'ALT|AST|转氨酶',      'B_IMPORTANT', '血清转氨酶升高，提示肝细胞轻度损伤可能。', '近期避免饮酒、熬夜及非必要肝损伤药物，2~4周后复查肝功能并排查病毒性肝炎。', '肝病/消化科', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('KB-CHK-08', 'KB-CR',    '肾功能肌酐异常',   '肌酐|尿素|Cr|BUN',    'A_CRITICAL',  '血肌酐/尿素指标异常，提示肾功能受损风险。', '请尽快至肾内科复查肾功能、尿常规及泌尿系彩超，避免使用肾毒性药物。', '肾内科', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('KB-CHK-09', 'KB-ANEM',  '贫血/血红蛋白降低','血红蛋白|贫血|HGB',   'A_CRITICAL',  '血红蛋白低于参考下限，提示贫血。', '建议血液科进一步明确贫血原因（缺铁性、巨幼细胞性或慢性失血等）并针对性治疗。', '血液内科', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('KB-CHK-10', 'KB-ECG',   '心电图异常',       'ST段|早搏|房颤|传导阻滞', 'A_CRITICAL', '心电图检出异常波形或心律失常（重大阳性发现）。', '请立即至心内科就诊，必要时行24小时动态心电图及心脏彩超检查。', '心内科', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);
", new { Now = now });
    }
}
