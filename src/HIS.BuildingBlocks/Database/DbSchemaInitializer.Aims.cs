using System.Data.Common;
using System.Threading.Tasks;
using Dapper;

namespace HIS.BuildingBlocks.Database;

public static partial class DbSchemaInitializer
{
    private static async Task InitializeAimsTablesAsync(DbConnection conn)
    {
        await conn.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS aims_preop_assessments (
    id VARCHAR(36) PRIMARY KEY,
    assessment_no VARCHAR(40) NOT NULL UNIQUE,
    schedule_id VARCHAR(36) NOT NULL,
    encounter_id VARCHAR(36),
    patient_name VARCHAR(128) NOT NULL,
    weight_kg DECIMAL(6,2) NOT NULL DEFAULT 65.0,
    asa_class VARCHAR(16) NOT NULL DEFAULT 'II',
    mallampati_class INT NOT NULL DEFAULT 1,
    stop_bang_score INT NOT NULL DEFAULT 1,
    fasting_hours INT NOT NULL DEFAULT 8,
    planned_anesthesia_method VARCHAR(64) NOT NULL DEFAULT '全身麻醉(气管插管)',
    tidal_volume_range_ml VARCHAR(64),
    lidocaine_max_mg DECIMAL(8,2) NOT NULL DEFAULT 0,
    ropivacaine_max_mg DECIMAL(8,2) NOT NULL DEFAULT 0,
    risk_alerts VARCHAR(500),
    assessor_name VARCHAR(64),
    assessed_at_ms BIGINT NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS aims_intraop_fluid_events (
    id VARCHAR(36) PRIMARY KEY,
    schedule_id VARCHAR(36) NOT NULL,
    record_type VARCHAR(32) NOT NULL,
    category_name VARCHAR(64) NOT NULL,
    volume_ml DECIMAL(10,2) NOT NULL DEFAULT 0,
    event_detail VARCHAR(500),
    recorded_by VARCHAR(64),
    recorded_at_ms BIGINT NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_aims_fluid_sched ON aims_intraop_fluid_events (schedule_id, recorded_at_ms ASC);

CREATE TABLE IF NOT EXISTS aims_narcotic_ledgers (
    id VARCHAR(36) PRIMARY KEY,
    ledger_no VARCHAR(40) NOT NULL UNIQUE,
    schedule_id VARCHAR(36) NOT NULL,
    patient_name VARCHAR(128) NOT NULL,
    drug_name VARCHAR(200) NOT NULL,
    batch_no VARCHAR(80),
    issued_ampoules INT NOT NULL DEFAULT 1,
    used_ampoules INT NOT NULL DEFAULT 0,
    returned_intact_ampoules INT NOT NULL DEFAULT 0,
    empty_ampoules_recovered INT NOT NULL DEFAULT 0,
    waste_volume_ml DECIMAL(8,2) NOT NULL DEFAULT 0,
    anesthesiologist_name VARCHAR(64),
    witness_name VARCHAR(64),
    status VARCHAR(32) NOT NULL DEFAULT 'ISSUED',
    reconciled_at_ms BIGINT,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS aims_pacu_assessments (
    id VARCHAR(36) PRIMARY KEY,
    schedule_id VARCHAR(36) NOT NULL,
    patient_name VARCHAR(128) NOT NULL,
    activity_score INT NOT NULL DEFAULT 2,
    respiration_score INT NOT NULL DEFAULT 2,
    circulation_score INT NOT NULL DEFAULT 2,
    consciousness_score INT NOT NULL DEFAULT 2,
    spo2_score INT NOT NULL DEFAULT 2,
    aldrete_total INT NOT NULL DEFAULT 10,
    sbar_handoff_summary TEXT,
    receiving_ward_nurse VARCHAR(64),
    status VARCHAR(32) NOT NULL DEFAULT 'IN_PACU',
    handed_off_at_ms BIGINT,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS aims_pca_records (
    id VARCHAR(36) PRIMARY KEY,
    pca_no VARCHAR(40) NOT NULL UNIQUE,
    schedule_id VARCHAR(36) NOT NULL,
    patient_name VARCHAR(128) NOT NULL,
    pca_mode VARCHAR(32) NOT NULL DEFAULT 'PCIA',
    formula_summary VARCHAR(500) NOT NULL,
    background_rate_ml_h DECIMAL(6,2) NOT NULL DEFAULT 2.0,
    bolus_dose_ml DECIMAL(6,2) NOT NULL DEFAULT 0.5,
    lockout_minutes INT NOT NULL DEFAULT 15,
    vas_24h INT,
    vas_48h INT,
    vas_72h INT,
    adverse_events VARCHAR(500),
    status VARCHAR(32) NOT NULL DEFAULT 'ACTIVE',
    started_at_ms BIGINT NOT NULL,
    stopped_at_ms BIGINT,
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
