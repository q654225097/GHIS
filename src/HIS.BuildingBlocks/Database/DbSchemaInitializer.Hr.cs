using System.Data.Common;
using System.Threading.Tasks;
using Dapper;

namespace HIS.BuildingBlocks.Database;

public static partial class DbSchemaInitializer
{
    public static async Task InitializeHrTablesAsync(DbConnection conn)
    {
        await conn.ExecuteAsync(@"
-- 1. 医院职工人事主档表 (hr_employees)
CREATE TABLE IF NOT EXISTS hr_employees (
    id VARCHAR(36) PRIMARY KEY,
    employee_no VARCHAR(32) NOT NULL UNIQUE,
    user_id VARCHAR(36),
    full_name VARCHAR(64) NOT NULL,
    gender VARCHAR(16) NOT NULL DEFAULT 'UNKNOWN',
    birth_date_int INT,
    id_card_cipher VARCHAR(512),
    id_card_blind_index VARCHAR(128),
    id_card_masked VARCHAR(32),
    phone_cipher VARCHAR(256),
    phone_masked VARCHAR(32),
    email VARCHAR(128),
    education VARCHAR(32),
    graduated_school VARCHAR(128),
    major_name VARCHAR(128),
    political_status VARCHAR(32),
    hire_date_int INT NOT NULL,
    contract_end_date_int INT,
    employment_type VARCHAR(32) NOT NULL DEFAULT 'CONTRACT',
    staff_category VARCHAR(32) NOT NULL DEFAULT 'DOCTOR',
    primary_dept_id VARCHAR(36) NOT NULL,
    direct_supervisor_id VARCHAR(36),
    professional_title VARCHAR(64) NOT NULL DEFAULT '住院医师',
    job_position VARCHAR(64),
    employment_status VARCHAR(24) NOT NULL DEFAULT 'ACTIVE',
    resign_date_int INT,
    resign_reason VARCHAR(500),
    approval_status VARCHAR(24) NOT NULL DEFAULT 'APPROVED',
    workflow_instance_id VARCHAR(36),
    remark VARCHAR(500),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_hr_emp_user ON hr_employees (user_id);
CREATE INDEX IF NOT EXISTS ix_hr_emp_dept ON hr_employees (primary_dept_id, employment_status);
CREATE INDEX IF NOT EXISTS ix_hr_emp_idcard ON hr_employees (id_card_blind_index);

-- 2. 执业资格证与医疗权限证书台账 (hr_employee_credentials)
CREATE TABLE IF NOT EXISTS hr_employee_credentials (
    id VARCHAR(36) PRIMARY KEY,
    employee_id VARCHAR(36) NOT NULL,
    credential_type VARCHAR(48) NOT NULL,
    cert_no VARCHAR(128) NOT NULL,
    issuing_authority VARCHAR(128),
    issue_date_int INT,
    expiry_date_int INT,
    granted_level INT NOT NULL DEFAULT 1,
    verify_status VARCHAR(24) NOT NULL DEFAULT 'VALID',
    attachment_uri VARCHAR(512),
    remark VARCHAR(500),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_hr_cred_emp ON hr_employee_credentials (employee_id, credential_type);
CREATE INDEX IF NOT EXISTS ix_hr_cred_exp ON hr_employee_credentials (expiry_date_int, verify_status);

-- 3. 全生命周期人事异动记录表 (hr_change_records)
CREATE TABLE IF NOT EXISTS hr_change_records (
    id VARCHAR(36) PRIMARY KEY,
    change_no VARCHAR(48) NOT NULL UNIQUE,
    employee_id VARCHAR(36) NOT NULL,
    employee_no VARCHAR(32) NOT NULL,
    employee_name VARCHAR(64) NOT NULL,
    change_type VARCHAR(32) NOT NULL,
    before_snapshot_json TEXT,
    after_snapshot_json TEXT,
    effective_date_int INT NOT NULL,
    reason VARCHAR(500),
    approval_status VARCHAR(24) NOT NULL DEFAULT 'APPROVED',
    workflow_instance_id VARCHAR(36),
    applied_at_ms BIGINT,
    applied_by VARCHAR(36),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_hr_chg_emp ON hr_change_records (employee_id, created_at_ms);

-- 4. 劳动/聘用合同台账表 (hr_contracts)
CREATE TABLE IF NOT EXISTS hr_contracts (
    id VARCHAR(36) PRIMARY KEY,
    contract_no VARCHAR(64) NOT NULL UNIQUE,
    employee_id VARCHAR(36) NOT NULL,
    contract_type VARCHAR(32) NOT NULL DEFAULT 'FIXED_TERM',
    start_date_int INT NOT NULL,
    end_date_int INT,
    probation_months INT NOT NULL DEFAULT 3,
    status VARCHAR(24) NOT NULL DEFAULT 'ACTIVE',
    remark VARCHAR(500),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_hr_contract_emp ON hr_contracts (employee_id, status);
");
    }
}
