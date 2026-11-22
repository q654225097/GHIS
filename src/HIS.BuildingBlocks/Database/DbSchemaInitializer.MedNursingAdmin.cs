using System.Data.Common;
using System.Threading.Tasks;
using Dapper;

namespace HIS.BuildingBlocks.Database;

public static partial class DbSchemaInitializer
{
    private static async Task InitializeMedNursingAdminTablesAsync(DbConnection conn)
    {
        await conn.ExecuteAsync(@"
-- 1. 医务管理：医师分级动态授权台账
CREATE TABLE IF NOT EXISTS med_privilege_grants (
    id VARCHAR(36) PRIMARY KEY,
    grant_no VARCHAR(40) NOT NULL UNIQUE,
    user_id VARCHAR(36) NOT NULL,
    doctor_name VARCHAR(128) NOT NULL,
    dept_name VARCHAR(64) NOT NULL,
    prescribe_level INT NOT NULL DEFAULT 1,
    surgery_level INT NOT NULL DEFAULT 1,
    narcotic_auth SMALLINT NOT NULL DEFAULT 0,
    valid_until_int INT NOT NULL,
    status VARCHAR(32) NOT NULL DEFAULT 'ACTIVE',
    suspend_reason VARCHAR(500),
    approved_by VARCHAR(64),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

-- 2. 医务管理：医疗安全(不良)事件分级与 RCA/PDCA 改进台账
CREATE TABLE IF NOT EXISTS med_safety_rca_events (
    id VARCHAR(36) PRIMARY KEY,
    event_no VARCHAR(40) NOT NULL UNIQUE,
    severity_level VARCHAR(16) NOT NULL DEFAULT 'LEVEL_III',
    event_category VARCHAR(64) NOT NULL,
    dept_name VARCHAR(64) NOT NULL,
    event_summary TEXT NOT NULL,
    rca_root_cause TEXT,
    pdca_action_plan TEXT,
    status VARCHAR(32) NOT NULL DEFAULT 'REPORTED',
    reporter_name VARCHAR(64),
    reviewer_name VARCHAR(64),
    closed_at_ms BIGINT,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

-- 3. 医务管理：医疗纠纷专案（联动病案室依法封存）
CREATE TABLE IF NOT EXISTS med_dispute_cases (
    id VARCHAR(36) PRIMARY KEY,
    dispute_no VARCHAR(40) NOT NULL UNIQUE,
    patient_name VARCHAR(128) NOT NULL,
    dept_name VARCHAR(64) NOT NULL,
    mrms_case_id VARCHAR(36),
    mrms_sealed SMALLINT NOT NULL DEFAULT 0,
    dispute_summary TEXT NOT NULL,
    resolution_type VARCHAR(32) DEFAULT 'MEDIATION',
    compensation_amount DECIMAL(18,2) NOT NULL DEFAULT 0,
    status VARCHAR(32) NOT NULL DEFAULT 'OPEN',
    handler_name VARCHAR(64),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

-- 4. 医务管理：新技术新项目临床准入与随访
CREATE TABLE IF NOT EXISTS med_new_tech_projects (
    id VARCHAR(36) PRIMARY KEY,
    project_no VARCHAR(40) NOT NULL UNIQUE,
    project_name VARCHAR(200) NOT NULL,
    dept_name VARCHAR(64) NOT NULL,
    lead_doctor_name VARCHAR(64) NOT NULL,
    risk_grade VARCHAR(32) NOT NULL DEFAULT 'MEDIUM',
    ethics_approved SMALLINT NOT NULL DEFAULT 0,
    status VARCHAR(32) NOT NULL DEFAULT 'SUBMITTED',
    performed_cases INT NOT NULL DEFAULT 0,
    complication_cases INT NOT NULL DEFAULT 0,
    review_comment VARCHAR(500),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

-- 5. 护理管理：护士能级(N0-N4)档案与技能考核
CREATE TABLE IF NOT EXISTS nur_staff_competencies (
    id VARCHAR(36) PRIMARY KEY,
    nurse_user_id VARCHAR(36) NOT NULL UNIQUE,
    nurse_name VARCHAR(128) NOT NULL,
    ward_name VARCHAR(64) NOT NULL,
    n_level VARCHAR(16) NOT NULL DEFAULT 'N1',
    specialty_certs VARCHAR(255),
    theory_score DECIMAL(5,1) NOT NULL DEFAULT 85.0,
    skill_score DECIMAL(5,1) NOT NULL DEFAULT 88.0,
    cme_credits DECIMAL(5,1) NOT NULL DEFAULT 15.0,
    status VARCHAR(32) NOT NULL DEFAULT 'ACTIVE',
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

-- 6. 护理管理：病区排班表（含夜班/连班约束校验）
CREATE TABLE IF NOT EXISTS nur_ward_shifts (
    id VARCHAR(36) PRIMARY KEY,
    ward_name VARCHAR(64) NOT NULL,
    shift_date_int INT NOT NULL,
    nurse_user_id VARCHAR(36) NOT NULL,
    nurse_name VARCHAR(128) NOT NULL,
    n_level VARCHAR(16) NOT NULL DEFAULT 'N1',
    shift_code VARCHAR(32) NOT NULL,
    shift_hours DECIMAL(4,1) NOT NULL DEFAULT 8.0,
    remark VARCHAR(255),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_nur_shifts_nurse_dt ON nur_ward_shifts (nurse_user_id, shift_date_int);

-- 7. 护理管理：三级护理质控督查与 PDCA 整改
CREATE TABLE IF NOT EXISTS nur_qc_inspections (
    id VARCHAR(36) PRIMARY KEY,
    inspect_no VARCHAR(40) NOT NULL UNIQUE,
    ward_name VARCHAR(64) NOT NULL,
    inspect_level VARCHAR(32) NOT NULL DEFAULT 'NURSING_DEPT',
    inspect_date_int INT NOT NULL,
    score DECIMAL(5,1) NOT NULL DEFAULT 95.0,
    defect_summary TEXT,
    rectification_plan TEXT,
    status VARCHAR(32) NOT NULL DEFAULT 'OPEN',
    inspector_name VARCHAR(64),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

-- 8. 护理管理：病区结构化 SBAR 床旁交接班（双人签收）
CREATE TABLE IF NOT EXISTS nur_shift_handovers (
    id VARCHAR(36) PRIMARY KEY,
    handover_no VARCHAR(40) NOT NULL UNIQUE,
    ward_name VARCHAR(64) NOT NULL,
    shift_date_int INT NOT NULL,
    from_shift_code VARCHAR(32) NOT NULL,
    to_shift_code VARCHAR(32) NOT NULL,
    total_patients INT NOT NULL DEFAULT 0,
    critical_patients INT NOT NULL DEFAULT 0,
    surgery_patients INT NOT NULL DEFAULT 0,
    high_risk_fall_ulcer INT NOT NULL DEFAULT 0,
    sbar_summary TEXT NOT NULL,
    from_nurse_name VARCHAR(64) NOT NULL,
    to_nurse_name VARCHAR(64),
    status VARCHAR(32) NOT NULL DEFAULT 'PENDING_ACK',
    acked_at_ms BIGINT,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
");
    }
}
