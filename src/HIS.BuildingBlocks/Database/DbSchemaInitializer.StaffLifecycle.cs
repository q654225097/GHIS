using System;
using System.Data.Common;
using System.Threading.Tasks;
using Dapper;

namespace HIS.BuildingBlocks.Database;

public static partial class DbSchemaInitializer
{
    private static async Task InitializeStaffLifecycleTablesAsync(DbConnection conn)
    {
        await conn.ExecuteAsync(@"
-- 1. 新职工上岗前合规准入清单（串联执业证、院感培训、CA证书、医务授权与带教联签）
CREATE TABLE IF NOT EXISTS hr_onboarding_checklists (
    id VARCHAR(36) PRIMARY KEY,
    employee_id VARCHAR(36) NOT NULL UNIQUE,
    user_id VARCHAR(36),
    employee_name VARCHAR(128) NOT NULL,
    license_verified SMALLINT NOT NULL DEFAULT 0,
    ipc_training_passed SMALLINT NOT NULL DEFAULT 0,
    ca_cert_issued SMALLINT NOT NULL DEFAULT 0,
    med_privilege_granted SMALLINT NOT NULL DEFAULT 0,
    mentor_user_id VARCHAR(36),
    is_independent_duty SMALLINT NOT NULL DEFAULT 0,
    probation_passed_at_ms BIGINT,
    status VARCHAR(24) NOT NULL DEFAULT 'IN_PROGRESS',
    created_at_ms BIGINT NOT NULL,
    updated_at_ms BIGINT NOT NULL
);

-- 2. 请休假 / 外出进修 / 对口支援与临床权限临时委托代理
CREATE TABLE IF NOT EXISTS hr_leave_delegations (
    id VARCHAR(36) PRIMARY KEY,
    leave_no VARCHAR(40) NOT NULL UNIQUE,
    employee_id VARCHAR(36) NOT NULL,
    user_id VARCHAR(36) NOT NULL,
    employee_name VARCHAR(128) NOT NULL,
    leave_type VARCHAR(32) NOT NULL,
    start_at_ms BIGINT NOT NULL,
    end_at_ms BIGINT NOT NULL,
    delegate_user_id VARCHAR(36) NOT NULL,
    delegate_name VARCHAR(128),
    migrated_appts_cnt INT NOT NULL DEFAULT 0,
    delegated_ipd_ids_json TEXT,
    reason VARCHAR(500),
    status VARCHAR(24) NOT NULL DEFAULT 'ACTIVE',
    returned_at_ms BIGINT,
    created_at_ms BIGINT NOT NULL,
    updated_at_ms BIGINT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_hr_leave_user ON hr_leave_delegations (user_id, status);

-- 3. 离职 / 退休 / 跨科调岗 5 项临床在途清零与交接单
CREATE TABLE IF NOT EXISTS hr_offboarding_clearances (
    id VARCHAR(36) PRIMARY KEY,
    clearance_no VARCHAR(40) NOT NULL UNIQUE,
    employee_id VARCHAR(36) NOT NULL,
    user_id VARCHAR(36) NOT NULL,
    employee_name VARCHAR(128) NOT NULL,
    scene_type VARCHAR(24) NOT NULL,
    handover_to_user_id VARCHAR(36),
    pending_ipd_cnt INT NOT NULL DEFAULT 0,
    unsigned_emr_cnt INT NOT NULL DEFAULT 0,
    future_slots_cnt INT NOT NULL DEFAULT 0,
    pending_wf_cnt INT NOT NULL DEFAULT 0,
    pending_consult_cnt INT NOT NULL DEFAULT 0,
    ca_revoked SMALLINT NOT NULL DEFAULT 0,
    privilege_revoked SMALLINT NOT NULL DEFAULT 0,
    status VARCHAR(24) NOT NULL DEFAULT 'COMPLETED',
    reason VARCHAR(500),
    operator_name VARCHAR(64),
    completed_at_ms BIGINT NOT NULL,
    created_at_ms BIGINT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_hr_offboard_emp ON hr_offboarding_clearances (employee_id);

-- 4. 住培生 / 进修生 / 新职工跨科轮转计划
CREATE TABLE IF NOT EXISTS hr_rotations (
    id VARCHAR(36) PRIMARY KEY,
    employee_id VARCHAR(36) NOT NULL,
    user_id VARCHAR(36) NOT NULL,
    employee_name VARCHAR(128) NOT NULL,
    target_dept_id VARCHAR(36) NOT NULL,
    target_dept_name VARCHAR(128),
    mentor_user_id VARCHAR(36),
    start_date_int INT NOT NULL,
    end_date_int INT NOT NULL,
    auto_switched SMALLINT NOT NULL DEFAULT 0,
    evaluation_score INT,
    status VARCHAR(24) NOT NULL DEFAULT 'SCHEDULED',
    created_at_ms BIGINT NOT NULL,
    updated_at_ms BIGINT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_hr_rotations_date ON hr_rotations (start_date_int, status);

-- 5. 外院特邀专家与多点执业限时备案台账
CREATE TABLE IF NOT EXISTS hr_external_experts (
    id VARCHAR(36) PRIMARY KEY,
    filing_no VARCHAR(40) NOT NULL UNIQUE,
    expert_name VARCHAR(128) NOT NULL,
    origin_hospital VARCHAR(128) NOT NULL,
    license_no VARCHAR(64) NOT NULL,
    temp_user_id VARCHAR(36) NOT NULL,
    allowed_surgery_level INT NOT NULL DEFAULT 4,
    valid_from_ms BIGINT NOT NULL,
    valid_until_ms BIGINT NOT NULL,
    status VARCHAR(24) NOT NULL DEFAULT 'ACTIVE',
    approved_by VARCHAR(64),
    created_at_ms BIGINT NOT NULL,
    updated_at_ms BIGINT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_hr_ext_expert_exp ON hr_external_experts (valid_until_ms, status);

-- 6. 年度 CME 学分、医德医风考评与医师定期考核档案
CREATE TABLE IF NOT EXISTS hr_cme_ethics_records (
    id VARCHAR(36) PRIMARY KEY,
    employee_id VARCHAR(36) NOT NULL,
    user_id VARCHAR(36),
    employee_name VARCHAR(128) NOT NULL,
    eval_year INT NOT NULL,
    cme_cat1_credits DECIMAL(6,2) NOT NULL DEFAULT 0,
    cme_cat2_credits DECIMAL(6,2) NOT NULL DEFAULT 0,
    ethics_grade VARCHAR(24) NOT NULL DEFAULT 'EXCELLENT',
    periodic_exam_result VARCHAR(24) NOT NULL DEFAULT 'PASS',
    suspend_until_int INT,
    remark VARCHAR(500),
    created_at_ms BIGINT NOT NULL,
    updated_at_ms BIGINT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_hr_cme_emp_year ON hr_cme_ethics_records (employee_id, eval_year);
");

        // 现有表平滑扩展列（兼容 SQLite / 达梦 DM8 / PostgreSQL）
        await EnsureColumnAsync(conn, "hr_employees", "cost_center_code", "VARCHAR(64)");
        await EnsureColumnAsync(conn, "hr_employees", "rehire_contract_end_int", "INT");
        await EnsureColumnAsync(conn, "sys_users", "account_expire_at_ms", "BIGINT");
    }
}
