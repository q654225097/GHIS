using System;
using System.Data.Common;
using System.Threading.Tasks;
using Dapper;

namespace HIS.BuildingBlocks.Database;

public static partial class DbSchemaInitializer
{
    private static async Task InitializeDeepSafetyClosuresTablesAsync(DbConnection conn)
    {
        await conn.ExecuteAsync(@"
-- R-03: 居民死亡医学证明（推断）书
CREATE TABLE IF NOT EXISTS emr_death_certificates (
    id VARCHAR(36) PRIMARY KEY,
    cert_no VARCHAR(40) NOT NULL UNIQUE,
    encounter_id VARCHAR(36) NOT NULL UNIQUE,
    patient_id VARCHAR(36) NOT NULL,
    patient_name VARCHAR(128) NOT NULL,
    dept_name VARCHAR(64) NOT NULL,
    death_time_ms BIGINT NOT NULL,
    direct_cause_icd VARCHAR(32) NOT NULL,
    direct_cause_name VARCHAR(128) NOT NULL,
    underlying_cause_icd VARCHAR(32),
    underlying_cause_name VARCHAR(128),
    declaring_doctor VARCHAR(64) NOT NULL,
    morgue_handover_status VARCHAR(24) NOT NULL DEFAULT 'PENDING_HANDOVER',
    stopped_orders_count INT NOT NULL DEFAULT 0,
    created_at_ms BIGINT NOT NULL,
    updated_at_ms BIGINT NOT NULL
);

-- R-03: 非医嘱离院（自动出院 AMA）法定告知书
CREATE TABLE IF NOT EXISTS emr_ama_discharges (
    id VARCHAR(36) PRIMARY KEY,
    ama_no VARCHAR(40) NOT NULL UNIQUE,
    encounter_id VARCHAR(36) NOT NULL UNIQUE,
    patient_id VARCHAR(36) NOT NULL,
    patient_name VARCHAR(128) NOT NULL,
    risk_explanation TEXT NOT NULL,
    family_signer_name VARCHAR(64) NOT NULL,
    family_relation VARCHAR(32) NOT NULL,
    family_sign_svg TEXT NOT NULL,
    attending_doctor VARCHAR(64) NOT NULL,
    signed_at_ms BIGINT NOT NULL,
    created_at_ms BIGINT NOT NULL
);

-- R-04: 住院欠费授信担保台账（科主任/总值班绿色通道担保）
CREATE TABLE IF NOT EXISTS ipd_credit_guarantees (
    id VARCHAR(36) PRIMARY KEY,
    guarantee_no VARCHAR(40) NOT NULL UNIQUE,
    encounter_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    credit_limit_amount DECIMAL(18,2) NOT NULL,
    expire_at_ms BIGINT NOT NULL,
    guarantor_name VARCHAR(64) NOT NULL,
    guarantor_role VARCHAR(32) NOT NULL,
    reason VARCHAR(500) NOT NULL,
    status VARCHAR(24) NOT NULL DEFAULT 'ACTIVE',
    created_at_ms BIGINT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_ipd_credit_enc ON ipd_credit_guarantees (encounter_id, status);

-- R-06: 检验多项目自动合管与采血耗材去重记录
CREATE TABLE IF NOT EXISTS mtc_specimen_tube_merges (
    id VARCHAR(36) PRIMARY KEY,
    tube_barcode VARCHAR(64) NOT NULL UNIQUE,
    visit_or_encounter_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    tube_color VARCHAR(32) NOT NULL,
    specimen_type VARCHAR(64) NOT NULL,
    merged_items_json VARCHAR(2000) NOT NULL,
    item_count INT NOT NULL DEFAULT 1,
    collection_fee_count INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_mtc_tube_visit ON mtc_specimen_tube_merges (visit_or_encounter_id);
");

        // 扩展既有表列（安全幂等）
        await EnsureColumnAsync(conn, "ipd_encounters", "credit_limit_amount", "DECIMAL(18,2) NOT NULL DEFAULT 0");
        await EnsureColumnAsync(conn, "ipd_encounters", "credit_expire_at_ms", "BIGINT");
        await EnsureColumnAsync(conn, "ipd_encounters", "is_deceased", "SMALLINT NOT NULL DEFAULT 0");
        await EnsureColumnAsync(conn, "opd_prescriptions", "expires_at_ms", "BIGINT");
    }
}
