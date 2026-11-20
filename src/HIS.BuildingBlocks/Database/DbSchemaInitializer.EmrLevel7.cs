using System.Data.Common;
using System.Threading.Tasks;
using Dapper;

namespace HIS.BuildingBlocks.Database;

public static partial class DbSchemaInitializer
{
	private static async Task EnsureEmrLevel7SchemaAsync(DbConnection conn)
	{
		// TASK-01: Reliable Digital Signatures & TSA Framework
		await conn.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS sec_digital_signatures (
    id VARCHAR(36) PRIMARY KEY,
    entity_type VARCHAR(64) NOT NULL,
    entity_id VARCHAR(64) NOT NULL,
    business_stage VARCHAR(32) NOT NULL,
    signer_user_id VARCHAR(36) NOT NULL,
    signer_name VARCHAR(64) NOT NULL,
    ca_provider VARCHAR(64) NOT NULL,
    ca_cert_sn VARCHAR(128) NOT NULL,
    ca_cert_issuer VARCHAR(256) NOT NULL,
    data_sm3_hash VARCHAR(128) NOT NULL,
    signature_alg VARCHAR(32) NOT NULL DEFAULT 'SM2-SM3',
    p7_signature_base64 TEXT NOT NULL,
    tsa_provider_url VARCHAR(256) NOT NULL,
    tsa_policy_oid VARCHAR(64) NOT NULL,
    tsa_token_base64 TEXT NOT NULL,
    tsa_gen_time_ms BIGINT NOT NULL,
    verify_status SMALLINT NOT NULL DEFAULT 1,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_sec_sig_entity ON sec_digital_signatures (entity_type, entity_id, business_stage);
CREATE INDEX IF NOT EXISTS ix_sec_sig_signer ON sec_digital_signatures (signer_user_id, created_at_ms);
");

		// TASK-02: Fine-Grained EMR Access Control & Read Audits
		await EnsureColumnAsync(conn, "sys_users", "professional_title", "VARCHAR(32) DEFAULT 'RESIDENT'");
		await EnsureColumnAsync(conn, "sys_users", "special_exam_level", "INT NOT NULL DEFAULT 1");
		await EnsureColumnAsync(conn, "sys_users", "is_external_doctor", "SMALLINT NOT NULL DEFAULT 0");
		await EnsureColumnAsync(conn, "sys_users", "external_org_code", "VARCHAR(64)");
		await EnsureColumnAsync(conn, "sys_users", "external_org_name", "VARCHAR(128)");

		await conn.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS sec_emr_access_grants (
    id VARCHAR(36) PRIMARY KEY,
    patient_id VARCHAR(36) NOT NULL,
    encounter_id VARCHAR(36),
    grantee_user_id VARCHAR(36) NOT NULL,
    grantee_org_code VARCHAR(64),
    is_external_grant SMALLINT NOT NULL DEFAULT 0,
    allowed_scopes_json VARCHAR(1000) NOT NULL,
    valid_from_ms BIGINT NOT NULL,
    valid_to_ms BIGINT NOT NULL,
    grant_reason VARCHAR(64) NOT NULL,
    patient_consent_id VARCHAR(36),
    approved_by VARCHAR(36) NOT NULL,
    status SMALLINT NOT NULL DEFAULT 1,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_sec_grants_user_time ON sec_emr_access_grants (grantee_user_id, status, valid_from_ms, valid_to_ms);
CREATE INDEX IF NOT EXISTS ix_sec_grants_patient ON sec_emr_access_grants (patient_id, status);

CREATE TABLE IF NOT EXISTS emr_section_acl (
    id VARCHAR(36) PRIMARY KEY,
    template_id VARCHAR(36) NOT NULL,
    section_code VARCHAR(64) NOT NULL,
    role_code VARCHAR(64) NOT NULL,
    can_read SMALLINT NOT NULL DEFAULT 1,
    can_write SMALLINT NOT NULL DEFAULT 1,
    mask_sensitive SMALLINT NOT NULL DEFAULT 0,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_emr_section_acl_tpl ON emr_section_acl (template_id, section_code, role_code);

CREATE TABLE IF NOT EXISTS sec_emr_read_audit_logs (
    id VARCHAR(36) PRIMARY KEY,
    access_user_id VARCHAR(36) NOT NULL,
    access_user_name VARCHAR(128) NOT NULL,
    access_org_code VARCHAR(64),
    is_external SMALLINT NOT NULL DEFAULT 0,
    patient_id VARCHAR(36) NOT NULL,
    encounter_id VARCHAR(36),
    resource_type VARCHAR(64) NOT NULL,
    resource_id VARCHAR(64) NOT NULL,
    section_codes_json VARCHAR(1000),
    action_type VARCHAR(32) NOT NULL DEFAULT 'READ',
    grant_id VARCHAR(36),
    client_ip VARCHAR(64),
    decision VARCHAR(16) NOT NULL,
    deny_reason VARCHAR(500),
    prev_hash VARCHAR(128) NOT NULL,
    curr_hash VARCHAR(128) NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_sec_read_audit_user ON sec_emr_read_audit_logs (access_user_id, created_at_ms);
CREATE INDEX IF NOT EXISTS ix_sec_read_audit_patient ON sec_emr_read_audit_logs (patient_id, created_at_ms);
");

		// TASK-03: Knowledge Base (CDSS) & Clinical Pathway (CPW)
		await conn.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS kb_documents (
    id VARCHAR(36) PRIMARY KEY,
    doc_code VARCHAR(64) NOT NULL UNIQUE,
    category VARCHAR(32) NOT NULL,
    specialty_dept VARCHAR(64),
    title VARCHAR(256) NOT NULL,
    icd10_codes_json VARCHAR(1000),
    item_codes_json VARCHAR(1000),
    content_markdown TEXT NOT NULL,
    source_org VARCHAR(128),
    version_tag VARCHAR(32) NOT NULL DEFAULT 'v1.0',
    is_active SMALLINT NOT NULL DEFAULT 1,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_kb_docs_cat ON kb_documents (category, is_active);

CREATE TABLE IF NOT EXISTS kb_clinical_rules (
    id VARCHAR(36) PRIMARY KEY,
    rule_code VARCHAR(64) NOT NULL UNIQUE,
    rule_domain VARCHAR(32) NOT NULL,
    severity VARCHAR(16) NOT NULL,
    condition_dsl_json TEXT NOT NULL,
    explanation_text VARCHAR(1000) NOT NULL,
    action_suggestion_text VARCHAR(1000) NOT NULL,
    evidence_doc_id VARCHAR(36),
    is_active SMALLINT NOT NULL DEFAULT 1,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_kb_rules_domain ON kb_clinical_rules (rule_domain, is_active);

CREATE TABLE IF NOT EXISTS kb_order_bindings (
    id VARCHAR(36) PRIMARY KEY,
    item_type VARCHAR(32) NOT NULL,
    item_code VARCHAR(64) NOT NULL,
    kb_doc_id VARCHAR(36),
    rule_code VARCHAR(64),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_kb_order_bindings ON kb_order_bindings (item_type, item_code);

CREATE TABLE IF NOT EXISTS kb_external_sources (
    id VARCHAR(36) PRIMARY KEY,
    source_code VARCHAR(64) NOT NULL UNIQUE,
    source_name VARCHAR(128) NOT NULL,
    source_type VARCHAR(32) NOT NULL,
    endpoint_url VARCHAR(256) NOT NULL,
    auth_header_config VARCHAR(1000),
    timeout_ms INT NOT NULL DEFAULT 3000,
    is_enabled SMALLINT NOT NULL DEFAULT 1,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS cpw_pathways (
    id VARCHAR(36) PRIMARY KEY,
    pathway_code VARCHAR(64) NOT NULL UNIQUE,
    pathway_name VARCHAR(128) NOT NULL,
    applicable_icd10_json VARCHAR(1000) NOT NULL,
    standard_days INT NOT NULL,
    min_cost DECIMAL(18,4) NOT NULL DEFAULT 0,
    max_cost DECIMAL(18,4) NOT NULL DEFAULT 0,
    version VARCHAR(32) NOT NULL DEFAULT 'v1.0',
    status SMALLINT NOT NULL DEFAULT 1,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS cpw_stages (
    id VARCHAR(36) PRIMARY KEY,
    pathway_id VARCHAR(36) NOT NULL,
    stage_order INT NOT NULL,
    stage_name VARCHAR(128) NOT NULL,
    day_from INT NOT NULL,
    day_to INT NOT NULL,
    emr_template_snippet_json TEXT,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_cpw_stages_pw ON cpw_stages (pathway_id, stage_order);

CREATE TABLE IF NOT EXISTS cpw_stage_items (
    id VARCHAR(36) PRIMARY KEY,
    stage_id VARCHAR(36) NOT NULL,
    item_category VARCHAR(32) NOT NULL,
    item_code VARCHAR(64) NOT NULL,
    item_name VARCHAR(128) NOT NULL,
    is_mandatory SMALLINT NOT NULL DEFAULT 1,
    is_default_selected SMALLINT NOT NULL DEFAULT 1,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_cpw_stage_items_stage ON cpw_stage_items (stage_id);

CREATE TABLE IF NOT EXISTS cpw_patient_enrollments (
    id VARCHAR(36) PRIMARY KEY,
    encounter_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    pathway_id VARCHAR(36) NOT NULL,
    current_stage_id VARCHAR(36),
    enrolled_at_ms BIGINT NOT NULL,
    enrolled_by VARCHAR(36) NOT NULL,
    status VARCHAR(32) NOT NULL DEFAULT 'ACTIVE',
    exit_reason VARCHAR(500),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_cpw_enrollments_enc ON cpw_patient_enrollments (encounter_id, status);

CREATE TABLE IF NOT EXISTS cpw_variances (
    id VARCHAR(36) PRIMARY KEY,
    enrollment_id VARCHAR(36) NOT NULL,
    stage_id VARCHAR(36) NOT NULL,
    stage_item_id VARCHAR(36),
    related_order_id VARCHAR(36),
    variance_type VARCHAR(32) NOT NULL,
    reason_category VARCHAR(32) NOT NULL,
    reason_detail VARCHAR(500) NOT NULL,
    recorded_by VARCHAR(36) NOT NULL,
    recorded_at_ms BIGINT NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_cpw_variances_enr ON cpw_variances (enrollment_id);
");

		// TASK-04: Rational Drug Review, ADR, Prescription Commenting, PIVAS & Unit-Dose
		await EnsureColumnAsync(conn, "inv_drugs", "is_high_alert", "SMALLINT NOT NULL DEFAULT 0");
		await EnsureColumnAsync(conn, "inv_drugs", "high_alert_grade", "VARCHAR(16)");
		await EnsureColumnAsync(conn, "inv_drugs", "gender_restriction", "VARCHAR(16) NOT NULL DEFAULT 'ALL'");
		await EnsureColumnAsync(conn, "inv_drugs", "indication_icd_codes_json", "TEXT");
		await EnsureColumnAsync(conn, "inv_drugs", "renal_adjust_rule_json", "TEXT");
		await EnsureColumnAsync(conn, "inv_drugs", "hepatic_adjust_rule_json", "TEXT");

		await EnsureColumnAsync(conn, "ord_medical_orders", "dosage_unit", "VARCHAR(32)");
		await EnsureColumnAsync(conn, "ord_medical_orders", "cpw_stage_item_id", "VARCHAR(36)");
		await EnsureColumnAsync(conn, "ord_medical_orders", "review_status", "VARCHAR(16)");
		await EnsureColumnAsync(conn, "ord_medical_orders", "review_override_reason", "VARCHAR(500)");

		await conn.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS ord_order_review_logs (
    id VARCHAR(36) PRIMARY KEY,
    encounter_or_visit_id VARCHAR(36) NOT NULL,
    order_or_rx_id VARCHAR(36) NOT NULL,
    rule_type VARCHAR(64) NOT NULL,
    severity VARCHAR(16) NOT NULL,
    rule_code VARCHAR(64) NOT NULL,
    message VARCHAR(500) NOT NULL,
    is_blocked SMALLINT NOT NULL DEFAULT 0,
    is_overridden SMALLINT NOT NULL DEFAULT 0,
    override_reason VARCHAR(500),
    reviewed_at_ms BIGINT NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_ord_review_logs_target ON ord_order_review_logs (order_or_rx_id);

CREATE TABLE IF NOT EXISTS phm_prescription_comments (
    id VARCHAR(36) PRIMARY KEY,
    source_type VARCHAR(16) NOT NULL,
    target_id VARCHAR(36) NOT NULL,
    doctor_id VARCHAR(36) NOT NULL,
    pharmacist_id VARCHAR(36) NOT NULL,
    is_rational SMALLINT NOT NULL DEFAULT 1,
    defect_codes_json VARCHAR(500),
    comment_text VARCHAR(1000) NOT NULL,
    error_category VARCHAR(64),
    doctor_ack_status VARCHAR(32) NOT NULL DEFAULT 'PENDING',
    doctor_reply VARCHAR(1000),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_phm_comments_doc ON phm_prescription_comments (doctor_id, doctor_ack_status);

CREATE TABLE IF NOT EXISTS adr_reports (
    id VARCHAR(36) PRIMARY KEY,
    patient_id VARCHAR(36) NOT NULL,
    encounter_or_visit_id VARCHAR(36) NOT NULL,
    order_or_rx_id VARCHAR(36),
    drug_id VARCHAR(36) NOT NULL,
    drug_name VARCHAR(200) NOT NULL,
    batch_no VARCHAR(80),
    reaction_name VARCHAR(200) NOT NULL,
    onset_time_ms BIGINT NOT NULL,
    severity VARCHAR(32) NOT NULL DEFAULT 'GENERAL',
    outcome VARCHAR(64) NOT NULL DEFAULT 'RECOVERED',
    causality_grade VARCHAR(32) NOT NULL DEFAULT 'PROBABLE',
    report_status VARCHAR(32) NOT NULL DEFAULT 'DRAFT',
    external_report_no VARCHAR(64),
    kb_rule_generated_id VARCHAR(36),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_adr_reports_drug ON adr_reports (drug_id, report_status);

CREATE TABLE IF NOT EXISTS phm_ward_pivas_batches (
    id VARCHAR(36) PRIMARY KEY,
    encounter_id VARCHAR(36),
    ward_id VARCHAR(36) NOT NULL,
    batch_no VARCHAR(64) NOT NULL UNIQUE,
    prep_type VARCHAR(32) NOT NULL,
    pharmacist_audit_by VARCHAR(36) NOT NULL,
    label_printed_at_ms BIGINT,
    compounded_by VARCHAR(36),
    rechecked_by VARCHAR(36),
    dispatched_at_ms BIGINT,
    status VARCHAR(32) NOT NULL DEFAULT 'CREATED',
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_pivas_batches_ward ON phm_ward_pivas_batches (ward_id, status);

CREATE TABLE IF NOT EXISTS phm_unit_dose_barcodes (
    id VARCHAR(36) PRIMARY KEY,
    barcode VARCHAR(128) NOT NULL UNIQUE,
    dispense_order_id VARCHAR(36),
    pivas_batch_id VARCHAR(36),
    medical_order_id VARCHAR(36) NOT NULL,
    drug_id VARCHAR(36) NOT NULL,
    dose_text VARCHAR(64) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    status VARCHAR(32) NOT NULL DEFAULT 'PRINTED',
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_unit_dose_order ON phm_unit_dose_barcodes (medical_order_id, patient_id);

CREATE TABLE IF NOT EXISTS phm_medication_reconciliations (
    id VARCHAR(36) PRIMARY KEY,
    encounter_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    source VARCHAR(32) NOT NULL,
    drug_name VARCHAR(200) NOT NULL,
    dosage_text VARCHAR(64) NOT NULL,
    decision VARCHAR(32) NOT NULL,
    reconciled_by VARCHAR(36) NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_phm_recon_enc ON phm_medication_reconciliations (encounter_id);

CREATE TABLE IF NOT EXISTS phm_external_prescriptions (
    id VARCHAR(36) PRIMARY KEY,
    external_rx_no VARCHAR(64) NOT NULL UNIQUE,
    source_org_code VARCHAR(64) NOT NULL,
    source_org_name VARCHAR(128) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    rx_payload_json TEXT NOT NULL,
    ca_signature_verified SMALLINT NOT NULL DEFAULT 0,
    dispense_status VARCHAR(32) NOT NULL DEFAULT 'PENDING',
    dispensed_by VARCHAR(36),
    dispensed_at_ms BIGINT,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_external_rx_pat ON phm_external_prescriptions (patient_id, dispense_status);
");

		// TASK-05: LIS/PACS Protocol Adapters, Exam Smart Scheduling & Specimen Closed Loop
		await conn.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS cfg_specimen_dictionary (
    id VARCHAR(36) PRIMARY KEY,
    specimen_code VARCHAR(64) NOT NULL UNIQUE,
    specimen_name VARCHAR(128) NOT NULL,
    tube_color_type VARCHAR(64) NOT NULL,
    min_volume_ml DECIMAL(8,2) NOT NULL DEFAULT 2.0,
    storage_temp_c VARCHAR(32) NOT NULL DEFAULT '2-8℃',
    collection_notes VARCHAR(500),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS cfg_lab_item_profiles (
    id VARCHAR(36) PRIMARY KEY,
    price_item_id VARCHAR(36) NOT NULL UNIQUE,
    loinc_code VARCHAR(64),
    specimen_code VARCHAR(64) NOT NULL,
    indications VARCHAR(500),
    clinical_significance VARCHAR(500),
    gender_restriction VARCHAR(16) NOT NULL DEFAULT 'ALL',
    repeat_window_hours INT NOT NULL DEFAULT 24,
    drug_interference_json TEXT,
    cycle_sensitive SMALLINT NOT NULL DEFAULT 0,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS cfg_exam_item_profiles (
    id VARCHAR(36) PRIMARY KEY,
    price_item_id VARCHAR(36) NOT NULL UNIQUE,
    modality VARCHAR(16) NOT NULL,
    body_part VARCHAR(64) NOT NULL,
    prep_instructions VARCHAR(500),
    indications VARCHAR(500),
    contraindications VARCHAR(500),
    required_exam_level INT NOT NULL DEFAULT 1,
    repeat_window_hours INT NOT NULL DEFAULT 72,
    duration_minutes INT NOT NULL DEFAULT 15,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS mtc_exam_resources (
    id VARCHAR(36) PRIMARY KEY,
    resource_code VARCHAR(64) NOT NULL UNIQUE,
    resource_name VARCHAR(128) NOT NULL,
    modality VARCHAR(16) NOT NULL,
    dept_id VARCHAR(36) NOT NULL,
    room_code VARCHAR(64),
    time_slot_config_json TEXT,
    is_active SMALLINT NOT NULL DEFAULT 1,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS mtc_exam_slots (
    id VARCHAR(36) PRIMARY KEY,
    resource_id VARCHAR(36) NOT NULL,
    slot_date_int INT NOT NULL,
    start_time_ms BIGINT NOT NULL,
    end_time_ms BIGINT NOT NULL,
    total_capacity INT NOT NULL DEFAULT 5,
    booked_count INT NOT NULL DEFAULT 0,
    status SMALLINT NOT NULL DEFAULT 1,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_exam_slots_res_date ON mtc_exam_slots (resource_id, slot_date_int);

CREATE TABLE IF NOT EXISTS mtc_exam_appointments (
    id VARCHAR(36) PRIMARY KEY,
    request_id VARCHAR(36) NOT NULL UNIQUE,
    slot_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    sequence_no INT NOT NULL DEFAULT 1,
    prep_notice_text VARCHAR(1000),
    status VARCHAR(32) NOT NULL DEFAULT 'BOOKED',
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_exam_appts_slot ON mtc_exam_appointments (slot_id, status);

CREATE TABLE IF NOT EXISTS mtc_exam_conflict_rules (
    id VARCHAR(36) PRIMARY KEY,
    item_a_code VARCHAR(64) NOT NULL,
    item_b_code VARCHAR(64) NOT NULL,
    relation_type VARCHAR(32) NOT NULL,
    min_gap_minutes INT NOT NULL DEFAULT 0,
    reason VARCHAR(500) NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS mtc_specimen_barcodes (
    id VARCHAR(36) PRIMARY KEY,
    barcode VARCHAR(128) NOT NULL UNIQUE,
    request_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    specimen_code VARCHAR(64) NOT NULL,
    source_org_code VARCHAR(64),
    is_external SMALLINT NOT NULL DEFAULT 0,
    current_stage VARCHAR(32) NOT NULL DEFAULT 'PRINTED',
    reject_reason VARCHAR(500),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_specimen_req ON mtc_specimen_barcodes (request_id, current_stage);

CREATE TABLE IF NOT EXISTS mtc_specimen_lifecycle_logs (
    id VARCHAR(36) PRIMARY KEY,
    barcode_id VARCHAR(36) NOT NULL,
    stage VARCHAR(32) NOT NULL,
    operator_id VARCHAR(36) NOT NULL,
    device_code VARCHAR(64),
    scan_time_ms BIGINT NOT NULL,
    remark VARCHAR(500),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_specimen_logs ON mtc_specimen_lifecycle_logs (barcode_id, scan_time_ms);

CREATE TABLE IF NOT EXISTS mtc_lab_result_items (
    id VARCHAR(36) PRIMARY KEY,
    request_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    item_code VARCHAR(64) NOT NULL,
    item_name VARCHAR(128) NOT NULL,
    loinc_code VARCHAR(64),
    value_num DECIMAL(18,4),
    value_text VARCHAR(256),
    unit VARCHAR(32),
    ref_low DECIMAL(18,4),
    ref_high DECIMAL(18,4),
    ref_text VARCHAR(128),
    abnormal_flag VARCHAR(16) NOT NULL DEFAULT 'NORMAL',
    delta_percent DECIMAL(8,2),
    is_external SMALLINT NOT NULL DEFAULT 0,
    source_org_code VARCHAR(64),
    source_org_name VARCHAR(128),
    analyzer_id VARCHAR(64),
    reported_at_ms BIGINT NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_lab_results_pat_item ON mtc_lab_result_items (patient_id, item_code, reported_at_ms);

CREATE TABLE IF NOT EXISTS mtc_lab_report_attachments (
    id VARCHAR(36) PRIMARY KEY,
    request_id VARCHAR(36) NOT NULL,
    attachment_type VARCHAR(32) NOT NULL,
    mime_type VARCHAR(64) NOT NULL,
    content_base64_or_path TEXT NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS mtc_lab_auto_verify_rules (
    id VARCHAR(36) PRIMARY KEY,
    item_code VARCHAR(64) NOT NULL,
    gender VARCHAR(16) NOT NULL DEFAULT 'ALL',
    age_min INT NOT NULL DEFAULT 0,
    age_max INT NOT NULL DEFAULT 150,
    auto_pass_low DECIMAL(18,4) NOT NULL,
    auto_pass_high DECIMAL(18,4) NOT NULL,
    max_delta_percent DECIMAL(8,2) NOT NULL DEFAULT 50.0,
    critical_low DECIMAL(18,4),
    critical_high DECIMAL(18,4),
    nature_judgment_rule_json TEXT,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS mtc_exam_records (
    id VARCHAR(36) PRIMARY KEY,
    request_id VARCHAR(36) NOT NULL UNIQUE,
    device_ae_title VARCHAR(64),
    accession_no VARCHAR(64) NOT NULL,
    mwl_status VARCHAR(32) NOT NULL DEFAULT 'ARRIVED',
    mpps_status VARCHAR(32) NOT NULL DEFAULT 'IN_PROGRESS',
    mismatch_checked SMALLINT NOT NULL DEFAULT 1,
    mismatch_alert_text VARCHAR(500),
    structured_measurements_json TEXT,
    kb_calc_judgment_json TEXT,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS mtc_exam_report_templates (
    id VARCHAR(36) PRIMARY KEY,
    modality VARCHAR(16) NOT NULL,
    body_part VARCHAR(64) NOT NULL,
    icd_code VARCHAR(32),
    template_name VARCHAR(128) NOT NULL,
    structured_schema_json TEXT NOT NULL,
    smart_phrases_json TEXT,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS mtc_exam_reports (
    id VARCHAR(36) PRIMARY KEY,
    request_id VARCHAR(36) NOT NULL UNIQUE,
    patient_id VARCHAR(36) NOT NULL,
    accession_no VARCHAR(64) NOT NULL,
    modality VARCHAR(16) NOT NULL,
    body_part VARCHAR(64) NOT NULL,
    findings TEXT NOT NULL,
    impression TEXT NOT NULL,
    structured_measurements_json TEXT,
    ref_ranges_json TEXT,
    kb_diff_diagnosis_json TEXT,
    stage_status VARCHAR(32) NOT NULL DEFAULT 'FINAL',
    is_external SMALLINT NOT NULL DEFAULT 0,
    source_org_code VARCHAR(64),
    cda_xml TEXT,
    ofd_archive_id VARCHAR(36),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS mtc_dicom_studies (
    id VARCHAR(36) PRIMARY KEY,
    study_instance_uid VARCHAR(128) NOT NULL UNIQUE,
    accession_no VARCHAR(64) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    modality VARCHAR(16) NOT NULL,
    series_count INT NOT NULL DEFAULT 1,
    instance_count INT NOT NULL DEFAULT 1,
    wado_rs_base_url VARCHAR(256) NOT NULL,
    default_window_center INT NOT NULL DEFAULT 40,
    default_window_width INT NOT NULL DEFAULT 400,
    qc_status VARCHAR(32) NOT NULL DEFAULT 'PASSED',
    is_external SMALLINT NOT NULL DEFAULT 0,
    source_org_code VARCHAR(64),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_dicom_studies_pat ON mtc_dicom_studies (patient_id, accession_no);

CREATE TABLE IF NOT EXISTS mtc_dicom_qc_logs (
    id VARCHAR(36) PRIMARY KEY,
    study_uid VARCHAR(128) NOT NULL,
    action VARCHAR(64) NOT NULL,
    operator_id VARCHAR(36) NOT NULL,
    detail_json TEXT,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS mtc_dicom_annotations (
    id VARCHAR(36) PRIMARY KEY,
    study_uid VARCHAR(128) NOT NULL,
    series_uid VARCHAR(128),
    instance_uid VARCHAR(128),
    author_id VARCHAR(36) NOT NULL,
    annotation_json TEXT NOT NULL,
    shared_with_clinical SMALLINT NOT NULL DEFAULT 1,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS mtc_dicom_access_grants (
    id VARCHAR(36) PRIMARY KEY,
    study_uid VARCHAR(128) NOT NULL,
    grantee_id VARCHAR(36) NOT NULL,
    grantee_type VARCHAR(32) NOT NULL DEFAULT 'USER',
    valid_to_ms BIGINT NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
");

		// TASK-06: Ward Nursing Closed Loop, Vitals, PDA Dual Check & Activity Tracking
		await EnsureColumnAsync(conn, "ord_execution_plans", "patient_wristband_code", "VARCHAR(64)");
		await EnsureColumnAsync(conn, "ord_execution_plans", "item_barcode", "VARCHAR(128)");
		await EnsureColumnAsync(conn, "ord_execution_plans", "scan_matched", "SMALLINT NOT NULL DEFAULT 0");
		await EnsureColumnAsync(conn, "ord_execution_plans", "infusion_start_ms", "BIGINT");
		await EnsureColumnAsync(conn, "ord_execution_plans", "infusion_end_ms", "BIGINT");
		await EnsureColumnAsync(conn, "ord_execution_plans", "high_risk_warning_json", "TEXT");
		await EnsureColumnAsync(conn, "ord_execution_plans", "high_risk_ack_by", "VARCHAR(36)");
		await EnsureColumnAsync(conn, "ord_execution_plans", "clinical_check_snapshot_json", "TEXT");

		await conn.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS nur_vital_signs (
    id VARCHAR(36) PRIMARY KEY,
    encounter_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    measured_at_ms BIGINT NOT NULL,
    temperature_c DECIMAL(4,2),
    pulse_bpm INT,
    heart_rate_bpm INT,
    resp_rate INT,
    sys_bp INT,
    dia_bp INT,
    spo2 DECIMAL(5,2),
    blood_glucose DECIMAL(5,2),
    pain_score INT,
    intake_ml INT,
    output_ml INT,
    capture_source VARCHAR(32) NOT NULL DEFAULT 'MANUAL',
    device_id VARCHAR(64),
    mews_score INT NOT NULL DEFAULT 0,
    auto_triggered_measures_json TEXT,
    is_external SMALLINT NOT NULL DEFAULT 0,
    source_org_code VARCHAR(64),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_nur_vitals_enc ON nur_vital_signs (encounter_id, measured_at_ms);

CREATE TABLE IF NOT EXISTS nur_nursing_records (
    id VARCHAR(36) PRIMARY KEY,
    encounter_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    record_type VARCHAR(32) NOT NULL DEFAULT 'GENERAL',
    recorded_at_ms BIGINT NOT NULL,
    vital_sign_id VARCHAR(36),
    cpw_stage_id VARCHAR(36),
    content_json TEXT NOT NULL,
    nursing_actions_json TEXT,
    generated_fee_item_ids_json TEXT,
    nurse_id VARCHAR(36) NOT NULL,
    ca_signature_id VARCHAR(36),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_nur_records_enc ON nur_nursing_records (encounter_id, recorded_at_ms);

CREATE TABLE IF NOT EXISTS nur_admission_assessments (
    id VARCHAR(36) PRIMARY KEY,
    encounter_id VARCHAR(36) NOT NULL UNIQUE,
    patient_id VARCHAR(36) NOT NULL,
    template_id VARCHAR(36) NOT NULL,
    assessment_json TEXT NOT NULL,
    auto_risk_summary_json TEXT,
    referenced_history_ids_json TEXT,
    assessor_id VARCHAR(36) NOT NULL,
    assessed_at_ms BIGINT NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS nur_intake_checklist_templates (
    id VARCHAR(36) PRIMARY KEY,
    template_code VARCHAR(64) NOT NULL UNIQUE,
    template_name VARCHAR(128) NOT NULL,
    flow_type VARCHAR(32) NOT NULL,
    checklist_schema_json TEXT NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS nur_care_plan_templates (
    id VARCHAR(36) PRIMARY KEY,
    template_code VARCHAR(64) NOT NULL UNIQUE,
    template_name VARCHAR(128) NOT NULL,
    trigger_condition_json TEXT,
    remind_interval_minutes INT NOT NULL DEFAULT 120,
    interventions_json TEXT NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS ipd_patient_activity_tracks (
    id VARCHAR(36) PRIMARY KEY,
    encounter_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    activity_type VARCHAR(32) NOT NULL,
    from_location VARCHAR(128) NOT NULL,
    to_location VARCHAR(128) NOT NULL,
    scan_barcode VARCHAR(128),
    handover_from_user VARCHAR(64),
    handover_to_user VARCHAR(64),
    occurred_at_ms BIGINT NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_patient_activities ON ipd_patient_activity_tracks (encounter_id, occurred_at_ms);
");

		// TASK-07: General Treatment, Enhanced Surgery/Anesthesia & Monitoring
		await conn.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS trt_treatment_catalog (
    id VARCHAR(36) PRIMARY KEY,
    catalog_code VARCHAR(64) NOT NULL UNIQUE,
    catalog_name VARCHAR(128) NOT NULL,
    category VARCHAR(32) NOT NULL,
    exec_dept_id VARCHAR(36) NOT NULL,
    unit_price DECIMAL(10,2) NOT NULL DEFAULT 0,
    is_high_risk SMALLINT NOT NULL DEFAULT 0,
    is_active SMALLINT NOT NULL DEFAULT 1,
    checklist_schema_json TEXT,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS trt_requests (
    id VARCHAR(36) PRIMARY KEY,
    request_no VARCHAR(40) NOT NULL UNIQUE,
    source_type VARCHAR(16) NOT NULL,
    source_org_code VARCHAR(64),
    external_patient_id VARCHAR(64),
    patient_id VARCHAR(36) NOT NULL,
    encounter_or_visit_id VARCHAR(36) NOT NULL,
    catalog_id VARCHAR(36) NOT NULL,
    diagnosis_icd VARCHAR(32),
    prescribed_sessions INT NOT NULL DEFAULT 1,
    status VARCHAR(32) NOT NULL DEFAULT 'REQUESTED',
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_trt_requests_pat ON trt_requests (patient_id, status);

CREATE TABLE IF NOT EXISTS trt_appointments (
    id VARCHAR(36) PRIMARY KEY,
    request_id VARCHAR(36) NOT NULL,
    treatment_room VARCHAR(64) NOT NULL,
    scheduled_start_ms BIGINT NOT NULL,
    scheduled_end_ms BIGINT NOT NULL,
    reminder_sent SMALLINT NOT NULL DEFAULT 0,
    status VARCHAR(32) NOT NULL DEFAULT 'BOOKED',
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_trt_appointments_req ON trt_appointments (request_id);

CREATE TABLE IF NOT EXISTS trt_parameter_rules (
    id VARCHAR(36) PRIMARY KEY,
    catalog_id VARCHAR(36) NOT NULL,
    lab_item_code VARCHAR(64) NOT NULL,
    parameter_name VARCHAR(128) NOT NULL DEFAULT '',
    formula_or_range_json TEXT NOT NULL,
    param_suggestion_template VARCHAR(500) NOT NULL,
    warning_notice VARCHAR(500),
    is_active SMALLINT NOT NULL DEFAULT 1,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS trt_execution_records (
    id VARCHAR(36) PRIMARY KEY,
    request_id VARCHAR(36) NOT NULL,
    appointment_id VARCHAR(36),
    patient_id VARCHAR(36) NOT NULL DEFAULT '',
    session_no INT NOT NULL DEFAULT 1,
    actual_start_ms BIGINT NOT NULL,
    actual_end_ms BIGINT,
    suggested_params_json TEXT,
    actual_params_json TEXT NOT NULL,
    high_risk_check_passed SMALLINT NOT NULL DEFAULT 1,
    dual_check_operator_id VARCHAR(36),
    patient_reaction VARCHAR(500),
    fee_charged SMALLINT NOT NULL DEFAULT 0,
    ca_signature_id VARCHAR(36),
    status VARCHAR(32) NOT NULL DEFAULT 'FINISHED',
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_trt_executions_req ON trt_execution_records (request_id);
");

		await EnsureColumnAsync(conn, "mte_surgery_schedules", "source_type", "VARCHAR(16) DEFAULT 'IPD'");
		await EnsureColumnAsync(conn, "mte_surgery_schedules", "visit_id", "VARCHAR(36)");
		await EnsureColumnAsync(conn, "mte_surgery_schedules", "external_org_code", "VARCHAR(64)");
		await EnsureColumnAsync(conn, "mte_surgery_schedules", "external_patient_id", "VARCHAR(64)");
		await EnsureColumnAsync(conn, "mte_surgery_schedules", "scan_verify_json", "TEXT");
		await EnsureColumnAsync(conn, "mte_surgery_schedules", "high_risk_warnings_json", "TEXT");
		await EnsureColumnAsync(conn, "mte_surgery_schedules", "emr_surgery_doc_id", "VARCHAR(36)");

		await conn.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS mte_surgery_prep_checklists (
    id VARCHAR(36) PRIMARY KEY,
    surgery_id VARCHAR(36) NOT NULL UNIQUE,
    check_items_json TEXT NOT NULL,
    completed_by VARCHAR(36) NOT NULL,
    completed_at_ms BIGINT NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS mte_surgery_milestones (
    id VARCHAR(36) PRIMARY KEY,
    surgery_id VARCHAR(36) NOT NULL,
    milestone_code VARCHAR(32) NOT NULL,
    milestone_name VARCHAR(64) NOT NULL,
    occurred_at_ms BIGINT NOT NULL,
    operator_id VARCHAR(36) NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_surgery_milestones ON mte_surgery_milestones (surgery_id, occurred_at_ms);

CREATE TABLE IF NOT EXISTS mte_surgery_item_counts (
    id VARCHAR(36) PRIMARY KEY,
    surgery_id VARCHAR(36) NOT NULL,
    item_type VARCHAR(64) NOT NULL,
    pre_count INT NOT NULL DEFAULT 0,
    pre_closing_count INT NOT NULL DEFAULT 0,
    post_closing_count INT NOT NULL DEFAULT 0,
    is_balanced SMALLINT NOT NULL DEFAULT 1,
    counted_by VARCHAR(36) NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_surgery_item_counts ON mte_surgery_item_counts (surgery_id);

CREATE TABLE IF NOT EXISTS mte_anesthesia_telemetry (
    id VARCHAR(36) PRIMARY KEY,
    anesthesia_record_id VARCHAR(36) NOT NULL,
    device_type VARCHAR(32) NOT NULL,
    device_code VARCHAR(64) NOT NULL,
    sampled_at_ms BIGINT NOT NULL,
    hr INT,
    sys_bp INT,
    dia_bp INT,
    map INT,
    spo2 DECIMAL(5,2),
    etco2 DECIMAL(5,2),
    rr INT,
    temp_c DECIMAL(4,2),
    mac_value DECIMAL(4,2),
    infusion_rate_ml_h DECIMAL(8,2),
    is_abnormal SMALLINT NOT NULL DEFAULT 0,
    abnormal_reason VARCHAR(256),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_anesthesia_telem ON mte_anesthesia_telemetry (anesthesia_record_id, sampled_at_ms);

CREATE TABLE IF NOT EXISTS mte_anesthesia_alerts (
    id VARCHAR(36) PRIMARY KEY,
    anesthesia_record_id VARCHAR(36) NOT NULL,
    alert_type VARCHAR(64) NOT NULL,
    parameter_name VARCHAR(64) NOT NULL,
    parameter_value VARCHAR(64) NOT NULL,
    guidance_text VARCHAR(500) NOT NULL,
    created_at_ms BIGINT NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS mon_devices (
    id VARCHAR(36) PRIMARY KEY,
    device_code VARCHAR(64) NOT NULL UNIQUE,
    protocol_type VARCHAR(32) NOT NULL DEFAULT 'HL7_MLLP',
    ward_id VARCHAR(36) NOT NULL,
    bed_code VARCHAR(32) NOT NULL,
    current_encounter_id VARCHAR(36),
    is_online SMALLINT NOT NULL DEFAULT 1,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_mon_devices_ward_bed ON mon_devices (ward_id, bed_code);

CREATE TABLE IF NOT EXISTS mon_vital_streams (
    id VARCHAR(36) PRIMARY KEY,
    device_id VARCHAR(36) NOT NULL,
    encounter_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    sampled_at_ms BIGINT NOT NULL,
    hr INT,
    rr INT,
    sys_bp INT,
    dia_bp INT,
    map INT,
    spo2 DECIMAL(5,2),
    temp_c DECIMAL(4,2),
    cvp DECIMAL(5,2),
    gcs INT,
    ecg_rhythm VARCHAR(32),
    is_alarm SMALLINT NOT NULL DEFAULT 0,
    alarm_level VARCHAR(16),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_mon_vital_streams ON mon_vital_streams (encounter_id, sampled_at_ms);

CREATE TABLE IF NOT EXISTS mon_alarms (
    id VARCHAR(36) PRIMARY KEY,
    stream_id VARCHAR(36),
    encounter_id VARCHAR(36) NOT NULL,
    alarm_type VARCHAR(64) NOT NULL,
    severity VARCHAR(16) NOT NULL,
    message VARCHAR(500) NOT NULL,
    acked_by VARCHAR(36),
    acked_at_ms BIGINT,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_mon_alarms_enc ON mon_alarms (encounter_id, severity);

CREATE TABLE IF NOT EXISTS mon_risk_scores (
    id VARCHAR(36) PRIMARY KEY,
    encounter_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL DEFAULT '',
    score_type VARCHAR(32) NOT NULL,
    total_score INT NOT NULL,
    risk_level VARCHAR(16) NOT NULL DEFAULT 'LOW',
    component_scores_json TEXT NOT NULL,
    drug_lab_correlation_json TEXT,
    kb_guidance_text TEXT,
    calculated_at_ms BIGINT NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_mon_risk_scores ON mon_risk_scores (encounter_id, score_type);
");

		// TASK-08: Blood Bank Management & Transfusion Safety
		await conn.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS bld_blood_dictionary (
    id VARCHAR(36) PRIMARY KEY,
    blood_code VARCHAR(32) NOT NULL UNIQUE,
    blood_name VARCHAR(128) NOT NULL,
    capacity_ml INT NOT NULL DEFAULT 200,
    storage_temp_range VARCHAR(32) NOT NULL DEFAULT '2-6℃',
    validity_days INT NOT NULL DEFAULT 35,
    unit_price DECIMAL(18,4) NOT NULL DEFAULT 0,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS bld_inventory_bags (
    id VARCHAR(36) PRIMARY KEY,
    bag_barcode VARCHAR(80) NOT NULL UNIQUE,
    donation_code VARCHAR(80) NOT NULL,
    source_blood_center VARCHAR(128) NOT NULL,
    blood_code VARCHAR(32) NOT NULL,
    abo_type VARCHAR(16) NOT NULL,
    rh_type VARCHAR(16) NOT NULL,
    volume_ml INT NOT NULL,
    collection_date_int INT NOT NULL,
    expiry_date_int INT NOT NULL,
    cold_chain_temp DECIMAL(4,2),
    status VARCHAR(32) NOT NULL DEFAULT 'IN_STOCK',
    reserved_for_patient_id VARCHAR(36),
    matched_request_id VARCHAR(36),
    target_patient_id VARCHAR(36),
    target_request_id VARCHAR(36),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_bld_inventory_status ON bld_inventory_bags (blood_code, abo_type, rh_type, status);

CREATE TABLE IF NOT EXISTS bld_stock_policies (
    id VARCHAR(36) PRIMARY KEY,
    blood_code VARCHAR(32) NOT NULL,
    abo_type VARCHAR(16) NOT NULL,
    rh_type VARCHAR(16) NOT NULL,
    safe_stock_bags INT NOT NULL DEFAULT 10,
    warning_stock_bags INT NOT NULL DEFAULT 5,
    surgery_reserve_ratio DECIMAL(5,2) NOT NULL DEFAULT 1.2,
    is_active SMALLINT NOT NULL DEFAULT 1,
    min_safe_stock INT NOT NULL DEFAULT 5,
    alert_threshold INT NOT NULL DEFAULT 2,
    advisory_ratio_json TEXT,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS bld_crossmatch_records (
    id VARCHAR(36) PRIMARY KEY,
    blood_request_id VARCHAR(36) NOT NULL,
    bag_id VARCHAR(36) NOT NULL,
    patient_abo VARCHAR(16) NOT NULL,
    patient_rh VARCHAR(16) NOT NULL,
    antibody_screen_result VARCHAR(32) NOT NULL DEFAULT 'NEGATIVE',
    major_crossmatch VARCHAR(32) NOT NULL DEFAULT 'COMPATIBLE',
    minor_crossmatch VARCHAR(32) NOT NULL DEFAULT 'COMPATIBLE',
    result VARCHAR(32) NOT NULL DEFAULT 'COMPATIBLE',
    technician_id VARCHAR(36) NOT NULL,
    verifier_id VARCHAR(36) NOT NULL,
    matched_at_ms BIGINT NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_bld_crossmatch_req ON bld_crossmatch_records (blood_request_id);

CREATE TABLE IF NOT EXISTS bld_transfusion_executions (
    id VARCHAR(36) PRIMARY KEY,
    blood_request_id VARCHAR(36) NOT NULL,
    bag_id VARCHAR(36) NOT NULL,
    encounter_id VARCHAR(36) NOT NULL,
    patient_wristband_code VARCHAR(64) NOT NULL,
    start_nurse_id VARCHAR(36) NOT NULL,
    dual_check_nurse_id VARCHAR(36) NOT NULL,
    pre_vitals_json TEXT NOT NULL,
    mid15_vitals_json TEXT,
    post_vitals_json TEXT,
    started_at_ms BIGINT NOT NULL,
    ended_at_ms BIGINT,
    empty_bag_returned_at_ms BIGINT,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_bld_transfusions_enc ON bld_transfusion_executions (encounter_id);

CREATE TABLE IF NOT EXISTS bld_transfusion_reactions (
    id VARCHAR(36) PRIMARY KEY,
    execution_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    bag_barcode VARCHAR(80) NOT NULL,
    reaction_type VARCHAR(64) NOT NULL,
    severity VARCHAR(32) NOT NULL DEFAULT 'MILD',
    symptoms VARCHAR(500) NOT NULL,
    lab_recheck_json TEXT,
    disposition VARCHAR(1000) NOT NULL,
    reported_by VARCHAR(36) NOT NULL,
    reported_at_ms BIGINT NOT NULL,
    shared_external SMALLINT NOT NULL DEFAULT 0,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_bld_reactions_pat ON bld_transfusion_reactions (patient_id);

CREATE TABLE IF NOT EXISTS bld_external_sync_logs (
    id VARCHAR(36) PRIMARY KEY,
    direction VARCHAR(16) NOT NULL,
    peer_center_name VARCHAR(128) NOT NULL,
    sync_type VARCHAR(32) NOT NULL,
    item_count INT NOT NULL DEFAULT 0,
    status VARCHAR(32) NOT NULL DEFAULT 'SUCCESS',
    detail_json TEXT,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
");

		// TASK-09: EMR QC, Full-Text Search & Storage Policies
		await conn.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS emr_qc_rules (
    id VARCHAR(36) PRIMARY KEY,
    rule_code VARCHAR(64) NOT NULL UNIQUE,
    rule_name VARCHAR(128) NOT NULL,
    qc_dimension VARCHAR(32) NOT NULL,
    specialty_dept_id VARCHAR(36),
    icd10_pattern VARCHAR(64),
    doc_type_code VARCHAR(64),
    time_limit_hours INT NOT NULL DEFAULT 24,
    deduct_points DECIMAL(5,2) NOT NULL DEFAULT 5.0,
    rule_expr_json TEXT,
    is_active SMALLINT NOT NULL DEFAULT 1,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS emr_qc_tasks (
    id VARCHAR(36) PRIMARY KEY,
    encounter_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    task_type VARCHAR(32) NOT NULL,
    total_score DECIMAL(5,2) NOT NULL DEFAULT 100.0,
    grade VARCHAR(16) NOT NULL DEFAULT 'A',
    status VARCHAR(32) NOT NULL DEFAULT 'ACTIVE',
    reviewer_id VARCHAR(36),
    reviewed_at_ms BIGINT,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_emr_qc_tasks ON emr_qc_tasks (encounter_id, status);

CREATE TABLE IF NOT EXISTS emr_qc_defects (
    id VARCHAR(36) PRIMARY KEY,
    task_id VARCHAR(36) NOT NULL,
    encounter_id VARCHAR(36) NOT NULL,
    doc_id VARCHAR(36),
    rule_code VARCHAR(64) NOT NULL,
    qc_dimension VARCHAR(32) NOT NULL,
    defect_description VARCHAR(500) NOT NULL,
    deduct_points DECIMAL(5,2) NOT NULL DEFAULT 5.0,
    responsible_doctor_id VARCHAR(36) NOT NULL,
    deadline_ms BIGINT NOT NULL,
    rectification_note VARCHAR(1000),
    status VARCHAR(32) NOT NULL DEFAULT 'OPEN',
    verified_by VARCHAR(36),
    verified_at_ms BIGINT,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_emr_qc_defects_doc ON emr_qc_defects (responsible_doctor_id, status);

CREATE TABLE IF NOT EXISTS emr_qc_cross_org_tracks (
    id VARCHAR(36) PRIMARY KEY,
    patient_id VARCHAR(36) NOT NULL,
    source_org_code VARCHAR(64) NOT NULL,
    external_doc_id VARCHAR(64) NOT NULL,
    qc_reviewer_id VARCHAR(36) NOT NULL,
    finding_summary VARCHAR(1000) NOT NULL,
    track_status VARCHAR(32) NOT NULL DEFAULT 'TRACKED',
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS emr_digitized_history (
    id VARCHAR(36) PRIMARY KEY,
    patient_id VARCHAR(36) NOT NULL,
    legacy_mrn VARCHAR(64) NOT NULL,
    visit_date VARCHAR(32) NOT NULL,
    doc_title VARCHAR(200) NOT NULL,
    file_storage_uri VARCHAR(256) NOT NULL,
    ocr_text TEXT,
    sm3_hash VARCHAR(128) NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_emr_digitized_pat ON emr_digitized_history (patient_id);

CREATE TABLE IF NOT EXISTS emr_storage_policies (
    id VARCHAR(36) PRIMARY KEY,
    scene_type VARCHAR(16) NOT NULL UNIQUE,
    retention_years INT NOT NULL,
    retain_until_ms BIGINT NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS emr_external_health_records (
    id VARCHAR(36) PRIMARY KEY,
    patient_id VARCHAR(36) NOT NULL,
    source_org_code VARCHAR(64) NOT NULL,
    source_org_name VARCHAR(128) NOT NULL,
    record_category VARCHAR(32) NOT NULL,
    external_record_id VARCHAR(64) NOT NULL,
    clinical_time_ms BIGINT NOT NULL,
    cda_or_fhir_json TEXT NOT NULL,
    summary_text VARCHAR(2000),
    synced_at_ms BIGINT NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_emr_ext_health_pat ON emr_external_health_records (patient_id, record_category);
");

		// TASK-10: HIE Gateway, EMPI, and Code Mappings
		await conn.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS hie_empi_mappings (
    id VARCHAR(36) PRIMARY KEY,
    local_patient_id VARCHAR(36) NOT NULL,
    empi_id VARCHAR(64) NOT NULL,
    id_card_blind_index VARCHAR(64) NOT NULL,
    external_org_code VARCHAR(64) NOT NULL,
    external_org_name VARCHAR(128) NOT NULL,
    external_patient_id VARCHAR(64) NOT NULL,
    match_confidence DECIMAL(5,2) NOT NULL DEFAULT 1.0,
    verified_at_ms BIGINT NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_hie_empi_local ON hie_empi_mappings (local_patient_id);
CREATE INDEX IF NOT EXISTS ix_hie_empi_id ON hie_empi_mappings (empi_id);

CREATE TABLE IF NOT EXISTS hie_item_code_mappings (
    id VARCHAR(36) PRIMARY KEY,
    domain VARCHAR(32) NOT NULL,
    local_code VARCHAR(64) NOT NULL,
    standard_code VARCHAR(64) NOT NULL,
    external_org_code VARCHAR(64),
    external_code VARCHAR(64) NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_hie_code_map ON hie_item_code_mappings (domain, local_code);

CREATE TABLE IF NOT EXISTS hie_exchange_logs (
    id VARCHAR(36) PRIMARY KEY,
    direction VARCHAR(16) NOT NULL,
    protocol VARCHAR(32) NOT NULL,
    peer_org_code VARCHAR(64) NOT NULL,
    patient_id VARCHAR(36),
    resource_type VARCHAR(64) NOT NULL,
    payload_sm3_hash VARCHAR(128) NOT NULL,
    status VARCHAR(32) NOT NULL DEFAULT 'SUCCESS',
    error_message VARCHAR(1000),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_hie_exchange_logs ON hie_exchange_logs (direction, status, created_at_ms);
");

		await EnsureColumnAsync(conn, "trt_treatment_catalog", "unit_price", "DECIMAL(10,2) NOT NULL DEFAULT 0");
		await EnsureColumnAsync(conn, "trt_treatment_catalog", "is_active", "SMALLINT NOT NULL DEFAULT 1");
		await EnsureColumnAsync(conn, "trt_parameter_rules", "parameter_name", "VARCHAR(128) NOT NULL DEFAULT ''");
		await EnsureColumnAsync(conn, "trt_parameter_rules", "warning_notice", "VARCHAR(500)");
		await EnsureColumnAsync(conn, "trt_parameter_rules", "is_active", "SMALLINT NOT NULL DEFAULT 1");
		await EnsureColumnAsync(conn, "mon_risk_scores", "patient_id", "VARCHAR(36) NOT NULL DEFAULT ''");
		await EnsureColumnAsync(conn, "mon_risk_scores", "risk_level", "VARCHAR(16) NOT NULL DEFAULT 'LOW'");
		await EnsureColumnAsync(conn, "bld_stock_policies", "safe_stock_bags", "INT NOT NULL DEFAULT 10");
		await EnsureColumnAsync(conn, "bld_stock_policies", "warning_stock_bags", "INT NOT NULL DEFAULT 5");
		await EnsureColumnAsync(conn, "bld_stock_policies", "surgery_reserve_ratio", "DECIMAL(5,2) NOT NULL DEFAULT 1.2");
		await EnsureColumnAsync(conn, "bld_stock_policies", "is_active", "SMALLINT NOT NULL DEFAULT 1");
		await EnsureColumnAsync(conn, "emr_qc_tasks", "reviewed_at_ms", "BIGINT");
		await EnsureColumnAsync(conn, "trt_execution_records", "patient_id", "VARCHAR(36) NOT NULL DEFAULT ''");
		await EnsureColumnAsync(conn, "trt_execution_records", "status", "VARCHAR(32) NOT NULL DEFAULT 'FINISHED'");
		await EnsureColumnAsync(conn, "bld_inventory_bags", "reserved_for_patient_id", "VARCHAR(36)");
		await EnsureColumnAsync(conn, "bld_inventory_bags", "matched_request_id", "VARCHAR(36)");

		// Seed standard Blood Stock Policies if empty
		await conn.ExecuteAsync(@"
INSERT OR IGNORE INTO bld_stock_policies (id, blood_code, abo_type, rh_type, safe_stock_bags, warning_stock_bags, surgery_reserve_ratio, is_active, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES
('BLD-POL-01', 'RBC', 'A', 'POSITIVE', 10, 5, 1.2, 1, 1, 1700000000000, 1700000000000, 1),
('BLD-POL-02', 'RBC', 'B', 'POSITIVE', 10, 5, 1.2, 1, 1, 1700000000000, 1700000000000, 1),
('BLD-POL-03', 'RBC', 'O', 'POSITIVE', 12, 6, 1.2, 1, 1, 1700000000000, 1700000000000, 1),
('BLD-POL-04', 'RBC', 'AB', 'POSITIVE', 8, 4, 1.2, 1, 1, 1700000000000, 1700000000000, 1);
");

		// Seed standard Treatment Catalog items if empty
		await conn.ExecuteAsync(@"
INSERT OR IGNORE INTO trt_treatment_catalog (id, catalog_code, catalog_name, category, exec_dept_id, unit_price, is_high_risk, is_active, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES 
('TRT-CAT-001', 'HD-001', '血液透析常规治疗 (高通量超滤)', 'DIALYSIS', 'DEPT-DIALYSIS', 420.00, 1, 1, 1, 1700000000000, 1700000000000, 1),
('TRT-CAT-002', 'CHEMO-001', '肿瘤全身化疗 (培美曲塞+顺铂方案)', 'CHEMO', 'DEPT-CHEMO', 850.00, 1, 1, 1, 1700000000000, 1700000000000, 1),
('TRT-CAT-003', 'HBO-001', '高压氧舱加压治疗', 'HBO', 'DEPT-HBO', 120.00, 1, 1, 1, 1700000000000, 1700000000000, 1),
('TRT-CAT-004', 'REHAB-001', '下肢关节功能主动被动训练 (CPM)', 'REHAB', 'DEPT-REHAB', 65.00, 0, 1, 1, 1700000000000, 1700000000000, 1),
('TRT-CAT-005', 'PHYSIO-001', '超短波物理治疗', 'PHYSIO', 'DEPT-PHYSIO', 45.00, 0, 1, 1, 1700000000000, 1700000000000, 1);

INSERT OR IGNORE INTO trt_parameter_rules (id, catalog_id, lab_item_code, parameter_name, formula_or_range_json, param_suggestion_template, warning_notice, is_active, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES
('TRT-RULE-001', 'TRT-CAT-001', 'K', '透析液配方与超滤量', '{""threshold"": 5.5}', '根据血钾与干体重设定', '血钾>5.5mmol/L 须选用低钾配方', 1, 1, 1700000000000, 1700000000000, 1),
('TRT-RULE-002', 'TRT-CAT-002', 'WBC', '化疗药物剂量与滴速', '{""threshold"": 3.5}', '根据白细胞与体表面积设定', 'WBC<3.5x10^9/L 需评估骨髓抑制', 1, 1, 1700000000000, 1700000000000, 1);

INSERT OR IGNORE INTO emr_qc_rules (id, rule_code, rule_name, qc_dimension, time_limit_hours, deduct_points, is_active, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES
('QC-R-001', 'QC-TIME-ADMISSION-24H', '入院记录24小时时限', 'TIMELINESS', 24, 10.0, 1, 1, 1700000000000, 1700000000000, 1),
('QC-R-002', 'QC-TIME-FIRST-COURSE-8H', '首次病程记录8小时时限', 'TIMELINESS', 8, 5.0, 1, 1, 1700000000000, 1700000000000, 1),
('QC-R-003', 'QC-TIME-DISCHARGE-24H', '出院记录24小时时限', 'TIMELINESS', 24, 5.0, 1, 1, 1700000000000, 1700000000000, 1),
('QC-R-004', 'QC-DRUG-ANTIBIOTIC-SPEC', '限制级抗感染药物内涵质控', 'DRUG_RATIONALITY', 24, 5.0, 1, 1, 1700000000000, 1700000000000, 1),
('QC-R-005', 'QC-CONTENT-ROUNDS-72H', '主治医师三日内查房记录', 'CONTENT', 72, 5.0, 1, 1, 1700000000000, 1700000000000, 1);
");
	}
}
