using System.Data.Common;
using Dapper;

namespace HIS.BuildingBlocks.Database;

public static partial class DbSchemaInitializer
{
    private static async Task InitializeClinicalCompleteTablesAsync(DbConnection conn)
    {
        await conn.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS er_triage_visits (
    id VARCHAR(36) PRIMARY KEY,
    er_no VARCHAR(40) NOT NULL UNIQUE,
    patient_name VARCHAR(128) NOT NULL,
    gender VARCHAR(16) NOT NULL DEFAULT '男',
    age INT NOT NULL DEFAULT 40,
    chief_complaint VARCHAR(500) NOT NULL,
    temp_c DECIMAL(5,2) NOT NULL DEFAULT 36.8,
    heart_rate INT NOT NULL DEFAULT 82,
    resp_rate INT NOT NULL DEFAULT 18,
    sbp INT NOT NULL DEFAULT 120,
    spo2 INT NOT NULL DEFAULT 98,
    gcs_score INT NOT NULL DEFAULT 15,
    mews_score INT NOT NULL DEFAULT 0,
    triage_level INT NOT NULL DEFAULT 4,
    destination_zone VARCHAR(32) NOT NULL DEFAULT 'ER_CLINIC',
    bed_code VARCHAR(32),
    is_green_channel SMALLINT NOT NULL DEFAULT 0,
    unpaid_credit_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
    status VARCHAR(32) NOT NULL DEFAULT 'WAITING',
    converted_encounter_id VARCHAR(36),
    triage_nurse_name VARCHAR(64),
    triaged_at_ms BIGINT NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_er_triage_level ON er_triage_visits (triage_level, status, triaged_at_ms DESC);

CREATE TABLE IF NOT EXISTS opd_medical_certificates (
    id VARCHAR(36) PRIMARY KEY,
    cert_no VARCHAR(40) NOT NULL UNIQUE,
    visit_id VARCHAR(36),
    patient_name VARCHAR(128) NOT NULL,
    gender VARCHAR(16) NOT NULL DEFAULT '男',
    age INT NOT NULL DEFAULT 35,
    dept_name VARCHAR(64),
    cert_type VARCHAR(32) NOT NULL DEFAULT 'DIAGNOSIS_CERT',
    diagnosis_text VARCHAR(500) NOT NULL,
    rest_days INT NOT NULL DEFAULT 0,
    advice_text TEXT,
    doctor_name VARCHAR(64),
    verify_hash VARCHAR(64),
    issued_at_ms BIGINT NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS ipd_consultations (
    id VARCHAR(36) PRIMARY KEY,
    consult_no VARCHAR(40) NOT NULL UNIQUE,
    encounter_id VARCHAR(36),
    patient_name VARCHAR(128) NOT NULL,
    bed_code VARCHAR(32),
    request_dept_name VARCHAR(64) NOT NULL,
    request_doctor_name VARCHAR(64),
    target_dept_name VARCHAR(64) NOT NULL,
    assigned_doctor_name VARCHAR(64),
    consult_type VARCHAR(32) NOT NULL DEFAULT 'ROUTINE',
    purpose_summary TEXT NOT NULL,
    deadline_ms BIGINT NOT NULL,
    status VARCHAR(32) NOT NULL DEFAULT 'REQUESTED',
    consult_opinion TEXT,
    synced_to_emr SMALLINT NOT NULL DEFAULT 0,
    completed_at_ms BIGINT,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_ipd_consult_status ON ipd_consultations (status, deadline_ms ASC);

CREATE TABLE IF NOT EXISTS ipd_daily_billing_runs (
    id VARCHAR(36) PRIMARY KEY,
    bill_date_int INT NOT NULL UNIQUE,
    trigger_mode VARCHAR(32) NOT NULL DEFAULT 'AUTO_SCHEDULER',
    patient_count INT NOT NULL DEFAULT 0,
    bed_fee_count INT NOT NULL DEFAULT 0,
    nursing_fee_count INT NOT NULL DEFAULT 0,
    long_order_count INT NOT NULL DEFAULT 0,
    total_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
    executed_by VARCHAR(64),
    executed_at_ms BIGINT NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS ipd_drug_returns (
    id VARCHAR(36) PRIMARY KEY,
    return_no VARCHAR(40) NOT NULL UNIQUE,
    encounter_id VARCHAR(36),
    patient_name VARCHAR(128) NOT NULL,
    bed_code VARCHAR(32),
    drug_name VARCHAR(200) NOT NULL,
    return_qty DECIMAL(18,4) NOT NULL DEFAULT 1,
    refund_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
    reason VARCHAR(500),
    status VARCHAR(32) NOT NULL DEFAULT 'APPLIED',
    applicant_name VARCHAR(64),
    reviewer_name VARCHAR(64),
    reviewed_at_ms BIGINT,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS ipd_discharge_medications (
    id VARCHAR(36) PRIMARY KEY,
    rx_no VARCHAR(40) NOT NULL UNIQUE,
    encounter_id VARCHAR(36),
    patient_name VARCHAR(128) NOT NULL,
    medications_summary TEXT NOT NULL,
    total_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
    prescribed_by VARCHAR(64),
    status VARCHAR(32) NOT NULL DEFAULT 'PRESCRIBED',
    dispensed_at_ms BIGINT,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS mrms_case_records (
    id VARCHAR(36) PRIMARY KEY,
    case_no VARCHAR(40) NOT NULL UNIQUE,
    encounter_id VARCHAR(36),
    patient_name VARCHAR(128) NOT NULL,
    gender VARCHAR(16) NOT NULL DEFAULT '男',
    age INT NOT NULL DEFAULT 45,
    discharge_dept_name VARCHAR(64),
    discharge_date_int INT,
    primary_icd10_code VARCHAR(32),
    primary_icd10_name VARCHAR(200),
    primary_icd9_code VARCHAR(32),
    primary_icd9_name VARCHAR(200),
    drg_group_code VARCHAR(64),
    status VARCHAR(32) NOT NULL DEFAULT 'PENDING_RETRIEVE',
    shelf_location VARCHAR(64),
    qc_note VARCHAR(500),
    coder_name VARCHAR(64),
    archived_at_ms BIGINT,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS mrms_borrow_logs (
    id VARCHAR(36) PRIMARY KEY,
    case_id VARCHAR(36) NOT NULL,
    case_no VARCHAR(40) NOT NULL,
    patient_name VARCHAR(128) NOT NULL,
    action_type VARCHAR(32) NOT NULL DEFAULT 'BORROW',
    applicant_name VARCHAR(64) NOT NULL,
    purpose VARCHAR(255),
    due_date_int INT,
    status VARCHAR(32) NOT NULL DEFAULT 'ACTIVE',
    handled_by VARCHAR(64),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS spec_maternity_records (
    id VARCHAR(36) PRIMARY KEY,
    delivery_no VARCHAR(40) NOT NULL UNIQUE,
    mother_encounter_id VARCHAR(36),
    mother_name VARCHAR(128) NOT NULL,
    gestational_weeks VARCHAR(32) NOT NULL DEFAULT '39+2周',
    delivery_mode VARCHAR(32) NOT NULL DEFAULT '顺产',
    newborn_gender VARCHAR(16) NOT NULL DEFAULT '男',
    birth_weight_g INT NOT NULL DEFAULT 3250,
    birth_length_cm DECIMAL(5,1) NOT NULL DEFAULT 50.0,
    apgar_scores VARCHAR(32) NOT NULL DEFAULT '10-10-10',
    birth_cert_pre_no VARCHAR(64),
    delivered_at_ms BIGINT NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS ipd_visitor_passes (
    id VARCHAR(36) PRIMARY KEY,
    pass_no VARCHAR(40) NOT NULL UNIQUE,
    encounter_id VARCHAR(36),
    patient_name VARCHAR(128) NOT NULL,
    bed_code VARCHAR(32),
    visitor_name VARCHAR(64) NOT NULL,
    relation_name VARCHAR(32) NOT NULL DEFAULT '配偶',
    phone_masked VARCHAR(32),
    pass_type VARCHAR(32) NOT NULL DEFAULT 'ESCORT',
    valid_until_int INT NOT NULL,
    status VARCHAR(32) NOT NULL DEFAULT 'ACTIVE',
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS ipd_day_surgery_cases (
    id VARCHAR(36) PRIMARY KEY,
    case_no VARCHAR(40) NOT NULL UNIQUE,
    patient_name VARCHAR(128) NOT NULL,
    gender VARCHAR(16) NOT NULL DEFAULT '男',
    age INT NOT NULL DEFAULT 38,
    dept_name VARCHAR(64) NOT NULL DEFAULT '日间手术中心',
    surgery_name VARCHAR(200) NOT NULL,
    surgeon_name VARCHAR(64),
    padss_score INT NOT NULL DEFAULT 0,
    status VARCHAR(32) NOT NULL DEFAULT 'SCHEDULED',
    admitted_at_ms BIGINT NOT NULL,
    discharged_at_ms BIGINT,
    remark VARCHAR(500),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS opd_queue_tickets (
    id VARCHAR(36) PRIMARY KEY,
    patient_id VARCHAR(36) NOT NULL,
    dept_id VARCHAR(36),
    doctor_id VARCHAR(36),
    queue_no VARCHAR(40),
    priority_level INT DEFAULT 4,
    priority_tag VARCHAR(32) DEFAULT '普通',
    status INT NOT NULL DEFAULT 0,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_opd_queue_status ON opd_queue_tickets (status, priority_level, created_at_ms);
");

        await EnsureColumnAsync(conn, "opd_queue_tickets", "priority_level", "INT DEFAULT 4");
        await EnsureColumnAsync(conn, "opd_queue_tickets", "priority_tag", "VARCHAR(32) DEFAULT '普通'");
        await EnsureColumnAsync(conn, "ipd_beds", "is_isolation", "SMALLINT DEFAULT 0");
    }
}
