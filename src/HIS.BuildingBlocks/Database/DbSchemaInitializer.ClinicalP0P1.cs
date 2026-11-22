using System.Data.Common;
using System.Threading.Tasks;
using Dapper;

namespace HIS.BuildingBlocks.Database;

public static partial class DbSchemaInitializer
{
    private static async Task InitializeClinicalP0P1TablesAsync(DbConnection conn)
    {
        await conn.ExecuteAsync(@"
-- ============================================================================
-- P0 (1) 急诊五大中心时间轴质控（胸痛/卒中/创伤/危重孕产妇/危重新生儿）
-- ============================================================================
CREATE TABLE IF NOT EXISTS er_five_center_cases (
    id VARCHAR(36) PRIMARY KEY,
    case_no VARCHAR(40) NOT NULL UNIQUE,
    center_type VARCHAR(32) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    patient_name VARCHAR(128) NOT NULL,
    visit_or_encounter_id VARCHAR(36),
    onset_at_ms BIGINT,
    door_at_ms BIGINT NOT NULL,
    first_ecg_at_ms BIGINT,
    troponin_at_ms BIGINT,
    ct_at_ms BIGINT,
    thrombolysis_at_ms BIGINT,
    wire_cross_at_ms BIGINT,
    target_metric_name VARCHAR(32) NOT NULL DEFAULT 'D2W',
    target_limit_minutes INT NOT NULL DEFAULT 90,
    actual_minutes INT,
    is_overtime SMALLINT NOT NULL DEFAULT 0,
    overtime_reason VARCHAR(500),
    outcome VARCHAR(64) DEFAULT 'IN_PROGRESS',
    attending_doctor VARCHAR(64),
    status VARCHAR(32) NOT NULL DEFAULT 'ACTIVE',
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS er_five_center_milestones (
    id VARCHAR(36) PRIMARY KEY,
    case_id VARCHAR(36) NOT NULL,
    milestone_code VARCHAR(40) NOT NULL,
    milestone_name VARCHAR(128) NOT NULL,
    occurred_at_ms BIGINT NOT NULL,
    elapsed_from_door_min INT NOT NULL DEFAULT 0,
    sla_limit_min INT NOT NULL DEFAULT 0,
    is_sla_breached SMALLINT NOT NULL DEFAULT 0,
    operator_name VARCHAR(64),
    note VARCHAR(500),
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36)
);
CREATE INDEX IF NOT EXISTS ix_er_five_center_ms_case ON er_five_center_milestones (case_id);

-- ============================================================================
-- P0 (2) ICU 重症监护工作站 (CIS)
-- ============================================================================
CREATE TABLE IF NOT EXISTS icu_admissions (
    id VARCHAR(36) PRIMARY KEY,
    icu_no VARCHAR(40) NOT NULL UNIQUE,
    encounter_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    patient_name VARCHAR(128) NOT NULL,
    icu_bed_code VARCHAR(32) NOT NULL,
    admit_reason VARCHAR(500),
    admitted_at_ms BIGINT NOT NULL,
    discharged_at_ms BIGINT,
    apache_ii_score INT NOT NULL DEFAULT 0,
    sofa_score INT NOT NULL DEFAULT 0,
    predicted_mortality_pct DECIMAL(6,2) NOT NULL DEFAULT 0,
    status VARCHAR(32) NOT NULL DEFAULT 'IN_ICU',
    attending_doctor VARCHAR(64),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_icu_admissions_enc ON icu_admissions (encounter_id);

CREATE TABLE IF NOT EXISTS icu_hourly_records (
    id VARCHAR(36) PRIMARY KEY,
    icu_admission_id VARCHAR(36) NOT NULL,
    record_time_ms BIGINT NOT NULL,
    vent_mode VARCHAR(32),
    fio2_pct DECIMAL(5,2),
    peep_cmh2o DECIMAL(5,2),
    tidal_volume_ml INT,
    resp_rate INT,
    map_mmhg INT,
    spo2_pct DECIMAL(5,2),
    lactate_mmol DECIMAL(5,2),
    intake_ml DECIMAL(10,2) NOT NULL DEFAULT 0,
    output_ml DECIMAL(10,2) NOT NULL DEFAULT 0,
    net_balance_ml DECIMAL(10,2) NOT NULL DEFAULT 0,
    vasopressor_drug VARCHAR(64),
    vasopressor_rate_mcg_kg_min DECIMAL(8,4),
    nurse_name VARCHAR(64),
    note VARCHAR(500),
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36)
);
CREATE INDEX IF NOT EXISTS ix_icu_hourly_adm ON icu_hourly_records (icu_admission_id, record_time_ms);

CREATE TABLE IF NOT EXISTS icu_catheter_surveillance (
    id VARCHAR(36) PRIMARY KEY,
    icu_admission_id VARCHAR(36) NOT NULL,
    encounter_id VARCHAR(36) NOT NULL,
    catheter_type VARCHAR(24) NOT NULL,
    site_name VARCHAR(64),
    inserted_at_ms BIGINT NOT NULL,
    removed_at_ms BIGINT,
    daily_review_passed SMALLINT NOT NULL DEFAULT 1,
    infection_suspected SMALLINT NOT NULL DEFAULT 0,
    infection_confirmed SMALLINT NOT NULL DEFAULT 0,
    infection_note VARCHAR(500),
    status VARCHAR(24) NOT NULL DEFAULT 'INDWELLING',
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36)
);
CREATE INDEX IF NOT EXISTS ix_icu_catheter_adm ON icu_catheter_surveillance (icu_admission_id);

-- ============================================================================
-- P0 (3) DRG/DIP 事中实时控费预警
-- ============================================================================
CREATE TABLE IF NOT EXISTS drg_benchmark_rules (
    id VARCHAR(36) PRIMARY KEY,
    group_code VARCHAR(32) NOT NULL UNIQUE,
    group_name VARCHAR(128) NOT NULL,
    pay_mode VARCHAR(16) NOT NULL DEFAULT 'DRG',
    icd_prefix VARCHAR(32),
    rw_weight DECIMAL(8,4) NOT NULL DEFAULT 1.0000,
    benchmark_fee DECIMAL(18,2) NOT NULL,
    warn_ratio DECIMAL(5,2) NOT NULL DEFAULT 0.80,
    high_outlier_ratio DECIMAL(5,2) NOT NULL DEFAULT 2.00,
    low_outlier_ratio DECIMAL(5,2) NOT NULL DEFAULT 0.40,
    standard_los_days INT NOT NULL DEFAULT 7,
    is_active SMALLINT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    updated_at_ms BIGINT NOT NULL
);

CREATE TABLE IF NOT EXISTS drg_realtime_alerts (
    id VARCHAR(36) PRIMARY KEY,
    encounter_id VARCHAR(36) NOT NULL UNIQUE,
    patient_name VARCHAR(128),
    dept_name VARCHAR(64),
    group_code VARCHAR(32) NOT NULL,
    group_name VARCHAR(128) NOT NULL,
    pay_mode VARCHAR(16) NOT NULL DEFAULT 'DRG',
    benchmark_fee DECIMAL(18,2) NOT NULL,
    current_total_fee DECIMAL(18,2) NOT NULL DEFAULT 0,
    fee_ratio DECIMAL(8,4) NOT NULL DEFAULT 0,
    variance_amount DECIMAL(18,2) NOT NULL DEFAULT 0,
    alert_level VARCHAR(24) NOT NULL DEFAULT 'NORMAL',
    special_case_appeal SMALLINT NOT NULL DEFAULT 0,
    appeal_reason VARCHAR(500),
    reviewed_by VARCHAR(64),
    evaluated_at_ms BIGINT NOT NULL,
    created_at_ms BIGINT NOT NULL,
    updated_at_ms BIGINT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_drg_alerts_level ON drg_realtime_alerts (alert_level);

-- ============================================================================
-- P0 (4) 抗菌药物 DDDs 监测与 PIVAS 静配全闭环
-- ============================================================================
CREATE TABLE IF NOT EXISTS abx_ddd_standards (
    id VARCHAR(36) PRIMARY KEY,
    drug_code VARCHAR(64) NOT NULL UNIQUE,
    drug_name VARCHAR(128) NOT NULL,
    antibiotic_level INT NOT NULL DEFAULT 1,
    ddd_value_grams DECIMAL(10,4) NOT NULL,
    spec_grams_per_unit DECIMAL(10,4) NOT NULL,
    atc_code VARCHAR(32),
    is_active SMALLINT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    updated_at_ms BIGINT NOT NULL
);

CREATE TABLE IF NOT EXISTS pivas_orders (
    id VARCHAR(36) PRIMARY KEY,
    pivas_no VARCHAR(40) NOT NULL UNIQUE,
    order_id VARCHAR(36) NOT NULL,
    encounter_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    patient_name VARCHAR(128) NOT NULL,
    ward_name VARCHAR(64),
    drug_code VARCHAR(64) NOT NULL,
    drug_name VARCHAR(128) NOT NULL,
    solvent_name VARCHAR(128) NOT NULL,
    dosage_text VARCHAR(64) NOT NULL,
    batch_code VARCHAR(24) NOT NULL DEFAULT 'BATCH_1',
    status VARCHAR(32) NOT NULL DEFAULT 'PENDING_REVIEW',
    reviewer_name VARCHAR(64),
    reviewed_at_ms BIGINT,
    reject_reason VARCHAR(500),
    label_barcode VARCHAR(64),
    compounder_name VARCHAR(64),
    compounded_at_ms BIGINT,
    checker_name VARCHAR(64),
    checked_at_ms BIGINT,
    dispatched_at_ms BIGINT,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_pivas_orders_status ON pivas_orders (status, batch_code);

-- ============================================================================
-- P1 (5) 公共卫生多报卡强触发（传染病/慢病/肿瘤/食源性）
-- ============================================================================
CREATE TABLE IF NOT EXISTS ph_icd_trigger_rules (
    id VARCHAR(36) PRIMARY KEY,
    icd_prefix VARCHAR(32) NOT NULL UNIQUE,
    disease_name VARCHAR(128) NOT NULL,
    card_type VARCHAR(32) NOT NULL,
    disease_class VARCHAR(16) DEFAULT 'CLASS_B',
    report_deadline_hours INT NOT NULL DEFAULT 24,
    is_hard_block SMALLINT NOT NULL DEFAULT 1,
    is_active SMALLINT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL
);

CREATE TABLE IF NOT EXISTS ph_report_cards (
    id VARCHAR(36) PRIMARY KEY,
    card_no VARCHAR(40) NOT NULL UNIQUE,
    card_type VARCHAR(32) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    patient_name VARCHAR(128) NOT NULL,
    visit_or_encounter_id VARCHAR(36) NOT NULL,
    dept_name VARCHAR(64),
    icd_code VARCHAR(32) NOT NULL,
    disease_name VARCHAR(128) NOT NULL,
    diagnosed_at_ms BIGINT NOT NULL,
    deadline_at_ms BIGINT NOT NULL,
    reported_at_ms BIGINT,
    is_overdue SMALLINT NOT NULL DEFAULT 0,
    status VARCHAR(32) NOT NULL DEFAULT 'PENDING_REPORT',
    report_doctor VARCHAR(64),
    auditor_name VARCHAR(64),
    audit_note VARCHAR(500),
    clinical_payload_json VARCHAR(4000),
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_ph_cards_status ON ph_report_cards (status, card_type);

-- ============================================================================
-- P1 (6) 院后专病随访与 30 天再入院追踪
-- ============================================================================
CREATE TABLE IF NOT EXISTS followup_plans (
    id VARCHAR(36) PRIMARY KEY,
    plan_no VARCHAR(40) NOT NULL UNIQUE,
    encounter_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    patient_name VARCHAR(128) NOT NULL,
    phone_number VARCHAR(32),
    specialty_tag VARCHAR(64) NOT NULL,
    discharge_at_ms BIGINT NOT NULL,
    planned_date_int INT NOT NULL,
    channel VARCHAR(24) NOT NULL DEFAULT 'PHONE',
    status VARCHAR(32) NOT NULL DEFAULT 'SCHEDULED',
    symptom_status VARCHAR(64),
    medication_adherence VARCHAR(32),
    satisfaction_score INT,
    followup_note VARCHAR(1000),
    followed_by VARCHAR(64),
    completed_at_ms BIGINT,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36)
);
CREATE INDEX IF NOT EXISTS ix_followup_plans_patient ON followup_plans (patient_id, status);

CREATE TABLE IF NOT EXISTS readmission_tracking (
    id VARCHAR(36) PRIMARY KEY,
    patient_id VARCHAR(36) NOT NULL,
    patient_name VARCHAR(128) NOT NULL,
    index_encounter_id VARCHAR(36) NOT NULL,
    readmit_encounter_id VARCHAR(36) NOT NULL UNIQUE,
    index_discharge_at_ms BIGINT NOT NULL,
    readmit_at_ms BIGINT NOT NULL,
    interval_days INT NOT NULL,
    is_unplanned SMALLINT NOT NULL DEFAULT 1,
    primary_diagnosis VARCHAR(256),
    root_cause_note VARCHAR(500),
    qc_status VARCHAR(32) NOT NULL DEFAULT 'PENDING_REVIEW',
    reviewer_name VARCHAR(64),
    created_at_ms BIGINT NOT NULL,
    updated_at_ms BIGINT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_readmission_patient ON readmission_tracking (patient_id, interval_days);

-- ============================================================================
-- P1 (7) 手术高值耗材 UDI 消耗-记账-代销对账
-- ============================================================================
CREATE TABLE IF NOT EXISTS udi_implant_inventory (
    id VARCHAR(36) PRIMARY KEY,
    udi_full VARCHAR(128) NOT NULL UNIQUE,
    udi_di VARCHAR(64) NOT NULL,
    udi_pi_lot VARCHAR(64),
    udi_pi_serial VARCHAR(64),
    expiry_date_int INT NOT NULL,
    material_code VARCHAR(64) NOT NULL,
    material_name VARCHAR(128) NOT NULL,
    spec VARCHAR(128),
    supplier_name VARCHAR(128) NOT NULL,
    is_consignment SMALLINT NOT NULL DEFAULT 1,
    cost_price DECIMAL(18,2) NOT NULL,
    unit_price DECIMAL(18,2) NOT NULL,
    status VARCHAR(32) NOT NULL DEFAULT 'IN_STOCK',
    encounter_id VARCHAR(36),
    patient_id VARCHAR(36),
    surgery_id VARCHAR(36),
    charge_ledger_id VARCHAR(36),
    statement_id VARCHAR(36),
    consumed_at_ms BIGINT,
    consumed_by VARCHAR(64),
    created_at_ms BIGINT NOT NULL,
    updated_at_ms BIGINT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_udi_implant_supplier_status ON udi_implant_inventory (supplier_name, status);

CREATE TABLE IF NOT EXISTS udi_consignment_statements (
    id VARCHAR(36) PRIMARY KEY,
    statement_no VARCHAR(40) NOT NULL UNIQUE,
    supplier_name VARCHAR(128) NOT NULL,
    period_ym VARCHAR(16) NOT NULL,
    item_count INT NOT NULL DEFAULT 0,
    total_cost_amount DECIMAL(18,2) NOT NULL DEFAULT 0,
    total_charge_amount DECIMAL(18,2) NOT NULL DEFAULT 0,
    status VARCHAR(32) NOT NULL DEFAULT 'DRAFT',
    confirmed_by VARCHAR(64),
    confirmed_at_ms BIGINT,
    created_at_ms BIGINT NOT NULL,
    updated_at_ms BIGINT NOT NULL
);

-- ============================================================================
-- P1 (8) 临床营养筛查 NRS-2002 与膳食配餐
-- ============================================================================
CREATE TABLE IF NOT EXISTS nut_nrs2002_screenings (
    id VARCHAR(36) PRIMARY KEY,
    encounter_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    patient_name VARCHAR(128) NOT NULL,
    bmi DECIMAL(5,2),
    disease_severity_score INT NOT NULL DEFAULT 0,
    nutritional_impaired_score INT NOT NULL DEFAULT 0,
    age_ge_70_score INT NOT NULL DEFAULT 0,
    total_score INT NOT NULL DEFAULT 0,
    risk_level VARCHAR(24) NOT NULL DEFAULT 'LOW_RISK',
    trigger_consult SMALLINT NOT NULL DEFAULT 0,
    intervention_type VARCHAR(32) DEFAULT 'ORAL_DIET',
    screener_name VARCHAR(64),
    screened_at_ms BIGINT NOT NULL,
    note VARCHAR(500),
    created_at_ms BIGINT NOT NULL,
    updated_at_ms BIGINT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_nut_nrs2002_enc ON nut_nrs2002_screenings (encounter_id);

CREATE TABLE IF NOT EXISTS nut_diet_orders (
    id VARCHAR(36) PRIMARY KEY,
    diet_no VARCHAR(40) NOT NULL UNIQUE,
    encounter_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    patient_name VARCHAR(128) NOT NULL,
    ward_name VARCHAR(64),
    bed_code VARCHAR(32),
    diet_type VARCHAR(32) NOT NULL,
    kcal_target INT NOT NULL DEFAULT 1800,
    protein_grams INT NOT NULL DEFAULT 65,
    meal_schedule VARCHAR(128) NOT NULL DEFAULT '早/中/晚',
    allergy_avoidance VARCHAR(256),
    status VARCHAR(32) NOT NULL DEFAULT 'ACTIVE',
    ordered_by VARCHAR(64),
    delivered_at_ms BIGINT,
    created_at_ms BIGINT NOT NULL,
    updated_at_ms BIGINT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_nut_diet_enc ON nut_diet_orders (encounter_id, status);
");

        // 幂等预置国家级标准基准规则（DRG/DIP 基准、公卫 ICD-10 强触发规则、WHO 抗菌药 DDD 标准）
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await conn.ExecuteAsync(@"
INSERT OR IGNORE INTO drg_benchmark_rules (id, group_code, group_name, pay_mode, icd_prefix, rw_weight, benchmark_fee, warn_ratio, high_outlier_ratio, low_outlier_ratio, standard_los_days, is_active, created_at_ms, updated_at_ms) VALUES
('drg-eb19', 'EB19', '肺炎（无并发症）', 'DRG', 'J18', 0.85, 8500.00, 0.80, 2.00, 0.40, 7, 1, @Now, @Now),
('drg-fm19', 'FM19', '经皮冠状动脉支架植入术(PCI)', 'DRG', 'I21', 2.45, 32000.00, 0.80, 2.00, 0.40, 6, 1, @Now, @Now),
('drg-br23', 'BR23', '缺血性脑卒中伴溶栓', 'DRG', 'I63', 1.68, 18500.00, 0.80, 2.00, 0.40, 9, 1, @Now, @Now),
('drg-gb29', 'GB29', '腹腔镜胆囊切除术', 'DRG', 'K80', 1.12, 12800.00, 0.80, 2.00, 0.40, 4, 1, @Now, @Now),
('drg-ob19', 'OB19', '剖宫产术', 'DRG', 'O82', 0.95, 9600.00, 0.80, 2.00, 0.40, 5, 1, @Now, @Now);

INSERT OR IGNORE INTO ph_icd_trigger_rules (id, icd_prefix, disease_name, card_type, disease_class, report_deadline_hours, is_hard_block, is_active, created_at_ms) VALUES
('ph-a00', 'A00', '霍乱', 'INFECTIOUS', 'CLASS_A', 2, 1, 1, @Now),
('ph-a20', 'A20', '鼠疫', 'INFECTIOUS', 'CLASS_A', 2, 1, 1, @Now),
('ph-a15', 'A15', '肺结核', 'INFECTIOUS', 'CLASS_B', 24, 1, 1, @Now),
('ph-b16', 'B16', '急性乙型病毒性肝炎', 'INFECTIOUS', 'CLASS_B', 24, 1, 1, @Now),
('ph-j10', 'J10', '流行性感冒', 'INFECTIOUS', 'CLASS_C', 24, 1, 1, @Now),
('ph-c34', 'C34', '支气管和肺恶性肿瘤', 'TUMOR', 'CHRONIC', 168, 1, 1, @Now),
('ph-i63', 'I63', '脑梗死（心脑血管事件）', 'CHRONIC', 'CHRONIC', 168, 1, 1, @Now),
('ph-a05', 'A05', '食源性细菌性食物中毒', 'FOODBORNE', 'FOODBORNE', 24, 1, 1, @Now);

INSERT OR IGNORE INTO abx_ddd_standards (id, drug_code, drug_name, antibiotic_level, ddd_value_grams, spec_grams_per_unit, atc_code, is_active, created_at_ms, updated_at_ms) VALUES
('ddd-cef', 'DRUG-CEFTRIAXONE', '注射用头孢曲松钠 1g', 1, 2.0000, 1.0000, 'J01DD04', 1, @Now, @Now),
('ddd-lev', 'DRUG-LEVOFLOXACIN', '左氧氟沙星氯化钠注射液 0.5g', 2, 0.5000, 0.5000, 'J01MA12', 1, @Now, @Now),
('ddd-mer', 'DRUG-MEROPENEM', '注射用美罗培南 0.5g', 3, 3.0000, 0.5000, 'J01DH02', 1, @Now, @Now),
('ddd-van', 'DRUG-VANCOMYCIN', '注射用盐酸万古霉素 0.5g', 3, 2.0000, 0.5000, 'J01XA01', 1, @Now, @Now);
", new { Now = nowMs });
    }
}
