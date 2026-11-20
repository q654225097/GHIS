using System;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;

namespace HIS.BuildingBlocks.Database;

public static partial class DbSchemaInitializer
{
	private sealed class TableColumnInfo
	{
		public string Name { get; set; } = string.Empty;
	}

	public const string DdlScript = "-- 1. emr_data_elements\nCREATE TABLE IF NOT EXISTS emr_data_elements (\n    id VARCHAR(36) PRIMARY KEY,\n    element_code VARCHAR(64) NOT NULL UNIQUE,\n    nhc_std_code VARCHAR(64),\n    element_name VARCHAR(128) NOT NULL,\n    data_type VARCHAR(32) NOT NULL,\n    ursa_control_type VARCHAR(64) NOT NULL,\n    dict_code VARCHAR(64),\n    unit_text VARCHAR(32),\n    macro_source VARCHAR(128),\n    gender_exclusive VARCHAR(16) NOT NULL,\n    validation_rule_json VARCHAR(2000),\n    control_key VARCHAR(128),\n    is_indexed SMALLINT NOT NULL,\n    is_active SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 2. emr_ofd_layout_schemas\nCREATE TABLE IF NOT EXISTS emr_ofd_layout_schemas (\n    id VARCHAR(36) PRIMARY KEY,\n    layout_code VARCHAR(64) NOT NULL UNIQUE,\n    layout_name VARCHAR(128) NOT NULL,\n    page_size_code VARCHAR(16) NOT NULL,\n    page_width_mm DECIMAL(18,4) NOT NULL,\n    page_height_mm DECIMAL(18,4) NOT NULL,\n    margin_top_mm DECIMAL(18,4) NOT NULL,\n    margin_bottom_mm DECIMAL(18,4) NOT NULL,\n    margin_left_mm DECIMAL(18,4) NOT NULL,\n    margin_right_mm DECIMAL(18,4) NOT NULL,\n    is_continuous_flow SMALLINT NOT NULL,\n    header_layout_json TEXT,\n    footer_layout_json TEXT,\n    sign_slots_json VARCHAR(2000),\n    watermark_policy VARCHAR(32),\n    is_active SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 3. emr_doc_templates\nCREATE TABLE IF NOT EXISTS emr_doc_templates (\n    id VARCHAR(36) PRIMARY KEY,\n    template_code VARCHAR(64) NOT NULL,\n    template_version INT NOT NULL,\n    template_name VARCHAR(128) NOT NULL,\n    emr_scene VARCHAR(16) NOT NULL,\n    doc_type_code VARCHAR(64) NOT NULL,\n    dept_category VARCHAR(32),\n    target_dept_id VARCHAR(36),\n    disease_icd_prefix VARCHAR(32),\n    time_limit_hours INT NOT NULL,\n    required_sign_levels INT NOT NULL,\n    ofd_layout_id VARCHAR(36),\n    is_published SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 4. emr_section_schemas\nCREATE TABLE IF NOT EXISTS emr_section_schemas (\n    id VARCHAR(36) PRIMARY KEY,\n    template_id VARCHAR(36) NOT NULL,\n    section_code VARCHAR(64) NOT NULL,\n    section_title VARCHAR(128) NOT NULL,\n    render_mode VARCHAR(32) NOT NULL,\n    content_template_json TEXT,\n    section_control_key VARCHAR(128),\n    is_required SMALLINT NOT NULL,\n    visibility_expr VARCHAR(500),\n    sort_order INT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 5. emr_documents\nCREATE TABLE IF NOT EXISTS emr_documents (\n    id VARCHAR(36) PRIMARY KEY,\n    doc_no VARCHAR(40) NOT NULL UNIQUE,\n    request_key VARCHAR(100) UNIQUE,\n    campus_id VARCHAR(36),\n    emr_scene VARCHAR(16) NOT NULL,\n    visit_or_encounter_id VARCHAR(36) NOT NULL,\n    patient_id VARCHAR(36) NOT NULL,\n    dept_id VARCHAR(36) NOT NULL,\n    ward_id VARCHAR(36),\n    template_id VARCHAR(36) NOT NULL,\n    template_version INT NOT NULL,\n    doc_type_code VARCHAR(64) NOT NULL,\n    doc_title VARCHAR(200) NOT NULL,\n    clinical_time_ms BIGINT NOT NULL,\n    structured_ast_json TEXT,\n    plain_text_summary TEXT,\n    track_changes_json TEXT,\n    status INT NOT NULL,\n    author_id VARCHAR(36) NOT NULL,\n    resident_signed_at_ms BIGINT,\n    attending_doctor_id VARCHAR(36),\n    attending_signed_at_ms BIGINT,\n    chief_doctor_id VARCHAR(36),\n    chief_signed_at_ms BIGINT,\n    patient_sign_svg TEXT,\n    latest_ofd_archive_id VARCHAR(36),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 6. emr_document_elements\nCREATE TABLE IF NOT EXISTS emr_document_elements (\n    id VARCHAR(36) PRIMARY KEY,\n    document_id VARCHAR(36) NOT NULL,\n    patient_id VARCHAR(36) NOT NULL,\n    emr_scene VARCHAR(16) NOT NULL,\n    doc_type_code VARCHAR(64) NOT NULL,\n    section_code VARCHAR(64) NOT NULL,\n    element_code VARCHAR(64) NOT NULL,\n    nhc_std_code VARCHAR(64),\n    val_string VARCHAR(1000),\n    val_int BIGINT,\n    val_decimal DECIMAL(18,4),\n    val_time_ms BIGINT,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 7. emr_ofd_archives\nCREATE TABLE IF NOT EXISTS emr_ofd_archives (\n    id VARCHAR(36) PRIMARY KEY,\n    ofd_no VARCHAR(40) NOT NULL UNIQUE,\n    archive_scope VARCHAR(32) NOT NULL,\n    visit_or_encounter_id VARCHAR(36) NOT NULL,\n    document_id VARCHAR(36),\n    patient_id VARCHAR(36) NOT NULL,\n    ofd_version_standard VARCHAR(32) NOT NULL,\n    page_count INT NOT NULL,\n    file_storage_key VARCHAR(255) NOT NULL,\n    file_size_bytes BIGINT NOT NULL,\n    content_sha256 VARCHAR(64) NOT NULL,\n    sm3_digest VARCHAR(64) NOT NULL,\n    ca_sign_serial_no VARCHAR(128),\n    tsa_timestamp_token VARCHAR(500),\n    has_embedded_semantic_xml SMALLINT NOT NULL,\n    archive_status INT NOT NULL,\n    generated_at_ms BIGINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 8. sys_departments\nCREATE TABLE IF NOT EXISTS sys_departments (\n    id VARCHAR(36) PRIMARY KEY,\n    campus_id VARCHAR(36) NOT NULL,\n    dept_code VARCHAR(64) NOT NULL UNIQUE,\n    dept_name VARCHAR(128) NOT NULL,\n    pinyin_code VARCHAR(64),\n    dept_category VARCHAR(32) NOT NULL,\n    sub_specialty_type VARCHAR(32),\n    parent_admin_id VARCHAR(36),\n    gender_limit VARCHAR(16),\n    min_age_days INT,\n    max_age_years INT,\n    default_pharmacy_id VARCHAR(36),\n    rated_bed_count INT,\n    location_address VARCHAR(255),\n    is_allow_appointment SMALLINT NOT NULL,\n    is_active SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 9. sys_dept_ward_relations\nCREATE TABLE IF NOT EXISTS sys_dept_ward_relations (\n    id VARCHAR(36) PRIMARY KEY,\n    ipd_dept_id VARCHAR(36) NOT NULL,\n    ward_dept_id VARCHAR(36) NOT NULL,\n    relation_type VARCHAR(32) NOT NULL,\n    max_borrow_beds INT NOT NULL,\n    priority_order INT NOT NULL,\n    is_active SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 10. sys_medtech_exec_routings\nCREATE TABLE IF NOT EXISTS sys_medtech_exec_routings (\n    id VARCHAR(36) PRIMARY KEY,\n    campus_id VARCHAR(36) NOT NULL,\n    source_scene VARCHAR(32) NOT NULL,\n    order_dept_id VARCHAR(36),\n    service_item_code VARCHAR(64) NOT NULL,\n    exec_med_tech_dept_id VARCHAR(36) NOT NULL,\n    exec_room_code VARCHAR(64),\n    time_window_type VARCHAR(32) NOT NULL,\n    priority_weight INT NOT NULL,\n    is_active SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 11. sys_users\nCREATE TABLE IF NOT EXISTS sys_users (\n    id VARCHAR(36) PRIMARY KEY,\n    user_name VARCHAR(64) NOT NULL UNIQUE,\n    display_name VARCHAR(128) NOT NULL,\n    employee_no VARCHAR(64) NOT NULL,\n    password_hash VARCHAR(255) NOT NULL,\n    bound_patient_id VARCHAR(36),\n    prescribe_level INT NOT NULL,\n    surgery_level INT NOT NULL,\n    emr_sign_level INT NOT NULL,\n    ca_cert_id VARCHAR(128),\n    session_version INT NOT NULL,\n    is_active SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 12. sys_user_dept_posts\nCREATE TABLE IF NOT EXISTS sys_user_dept_posts (\n    id VARCHAR(36) PRIMARY KEY,\n    user_id VARCHAR(36) NOT NULL,\n    dept_id VARCHAR(36) NOT NULL,\n    dept_category VARCHAR(32) NOT NULL,\n    med_group_id VARCHAR(36),\n    role_id VARCHAR(36) NOT NULL,\n    is_default_context SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 13. sys_roles\nCREATE TABLE IF NOT EXISTS sys_roles (\n    id VARCHAR(36) PRIMARY KEY,\n    role_code VARCHAR(64) NOT NULL UNIQUE,\n    role_name VARCHAR(128) NOT NULL,\n    data_scope VARCHAR(32) NOT NULL,\n    is_active SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 14. sys_permission_resources\nCREATE TABLE IF NOT EXISTS sys_permission_resources (\n    id VARCHAR(36) PRIMARY KEY,\n    resource_key VARCHAR(128) NOT NULL UNIQUE,\n    parent_key VARCHAR(128),\n    resource_type VARCHAR(32) NOT NULL,\n    resource_name VARCHAR(128) NOT NULL,\n    ursa_control_type VARCHAR(64),\n    api_method_path VARCHAR(255),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 15. sys_role_permissions\nCREATE TABLE IF NOT EXISTS sys_role_permissions (\n    id VARCHAR(36) PRIMARY KEY,\n    role_id VARCHAR(36) NOT NULL,\n    resource_key VARCHAR(128) NOT NULL,\n    access_policy INT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 16. cfg_form_schemas\nCREATE TABLE IF NOT EXISTS cfg_form_schemas (\n    id VARCHAR(36) PRIMARY KEY,\n    form_code VARCHAR(64) NOT NULL UNIQUE,\n    form_name VARCHAR(128) NOT NULL,\n    entity_type VARCHAR(64) NOT NULL,\n    dept_category VARCHAR(32),\n    target_dept_id VARCHAR(36),\n    grid_columns INT NOT NULL,\n    is_published SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 17. cfg_field_definitions\nCREATE TABLE IF NOT EXISTS cfg_field_definitions (\n    id VARCHAR(36) PRIMARY KEY,\n    form_schema_id VARCHAR(36) NOT NULL,\n    field_code VARCHAR(64) NOT NULL,\n    field_label VARCHAR(128) NOT NULL,\n    data_type VARCHAR(32) NOT NULL,\n    ursa_control_type VARCHAR(64) NOT NULL,\n    control_key VARCHAR(128),\n    dict_code VARCHAR(64),\n    unit_text VARCHAR(32),\n    validation_rule_json VARCHAR(2000),\n    visibility_expr VARCHAR(500),\n    is_indexed SMALLINT NOT NULL,\n    sort_order INT NOT NULL,\n    is_deleted SMALLINT NOT NULL DEFAULT 0,\n    deleted_at_ms BIGINT,\n    deleted_by VARCHAR(36),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 18. cfg_entity_field_values\nCREATE TABLE IF NOT EXISTS cfg_entity_field_values (\n    id VARCHAR(36) PRIMARY KEY,\n    entity_type VARCHAR(64) NOT NULL,\n    entity_id VARCHAR(36) NOT NULL,\n    field_code VARCHAR(64) NOT NULL,\n    val_string VARCHAR(500),\n    val_int BIGINT,\n    val_decimal DECIMAL(18,4),\n    val_time_ms BIGINT,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 19. pat_patients\nCREATE TABLE IF NOT EXISTS pat_patients (\n    id VARCHAR(36) PRIMARY KEY,\n    medical_record_no VARCHAR(32) NOT NULL UNIQUE,\n    full_name VARCHAR(128) NOT NULL,\n    pinyin_code VARCHAR(64),\n    gender_code VARCHAR(16) NOT NULL,\n    birth_date_int INT NOT NULL,\n    id_card_type VARCHAR(32) NOT NULL,\n    id_card_cipher VARCHAR(255),\n    id_card_hash VARCHAR(64) NOT NULL,\n    phone_cipher VARCHAR(255),\n    phone_masked VARCHAR(32),\n    guardian_name VARCHAR(128),\n    guardian_id_hash VARCHAR(64),\n    allergy_tags_json VARCHAR(2000),\n    is_temp_emergency SMALLINT NOT NULL,\n    merged_to_patient_id VARCHAR(36),\n    is_active SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 20. opd_schedule_slots\nCREATE TABLE IF NOT EXISTS opd_schedule_slots (\n    id VARCHAR(36) PRIMARY KEY,\n    campus_id VARCHAR(36) NOT NULL,\n    opd_dept_id VARCHAR(36) NOT NULL,\n    clinic_room_id VARCHAR(36),\n    doctor_id VARCHAR(36) NOT NULL,\n    substitute_doctor_id VARCHAR(36),\n    schedule_date_int INT NOT NULL,\n    slot_period_code VARCHAR(32) NOT NULL,\n    starts_at_ms BIGINT NOT NULL,\n    ends_at_ms BIGINT NOT NULL,\n    reg_level_code VARCHAR(32) NOT NULL,\n    registration_fee DECIMAL(18,4) NOT NULL,\n    total_capacity INT NOT NULL,\n    reserved_count INT NOT NULL,\n    slot_status INT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 21. opd_appointments\nCREATE TABLE IF NOT EXISTS opd_appointments (\n    id VARCHAR(36) PRIMARY KEY,\n    appointment_no VARCHAR(40) NOT NULL UNIQUE,\n    request_key VARCHAR(100) UNIQUE,\n    patient_id VARCHAR(36) NOT NULL,\n    schedule_id VARCHAR(36) NOT NULL,\n    active_unique_token VARCHAR(36) NOT NULL,\n    queue_no INT NOT NULL,\n    status INT NOT NULL,\n    lock_expires_at_ms BIGINT NOT NULL,\n    visit_id VARCHAR(36),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 22. opd_visits\nCREATE TABLE IF NOT EXISTS opd_visits (\n    id VARCHAR(36) PRIMARY KEY,\n    visit_no VARCHAR(40) NOT NULL UNIQUE,\n    request_key VARCHAR(100) UNIQUE,\n    campus_id VARCHAR(36) NOT NULL,\n    opd_dept_id VARCHAR(36) NOT NULL,\n    clinic_room_id VARCHAR(36),\n    patient_id VARCHAR(36) NOT NULL,\n    schedule_id VARCHAR(36),\n    doctor_id VARCHAR(36) NOT NULL,\n    is_emergency SMALLINT NOT NULL,\n    er_triage_level INT,\n    is_green_channel SMALLINT NOT NULL,\n    green_credit_limit DECIMAL(18,4),\n    is_return_visit SMALLINT NOT NULL,\n    status INT NOT NULL,\n    primary_diag_code VARCHAR(32),\n    primary_diag_name VARCHAR(255),\n    emr_document_id VARCHAR(36),\n    ofd_archive_id VARCHAR(36),\n    signed_at_ms BIGINT,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 23. opd_triage_queues\nCREATE TABLE IF NOT EXISTS opd_triage_queues (\n    id VARCHAR(36) PRIMARY KEY,\n    visit_id VARCHAR(36) NOT NULL UNIQUE,\n    opd_dept_id VARCHAR(36) NOT NULL,\n    clinic_room_id VARCHAR(36),\n    priority_score INT NOT NULL,\n    pass_count INT NOT NULL,\n    queue_status INT NOT NULL,\n    checkin_at_ms BIGINT NOT NULL,\n    called_at_ms BIGINT,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 24. opd_prescriptions\nCREATE TABLE IF NOT EXISTS opd_prescriptions (\n    id VARCHAR(36) PRIMARY KEY,\n    prescription_no VARCHAR(40) NOT NULL UNIQUE,\n    visit_id VARCHAR(36) NOT NULL,\n    patient_id VARCHAR(36) NOT NULL,\n    opd_dept_id VARCHAR(36) NOT NULL,\n    doctor_id VARCHAR(36) NOT NULL,\n    target_pharmacy_id VARCHAR(36) NOT NULL,\n    rx_type VARCHAR(32) NOT NULL,\n    herb_doses INT,\n    total_amount DECIMAL(18,4) NOT NULL,\n    status INT NOT NULL,\n    reviewer_pharmacist_id VARCHAR(36),\n    review_remark VARCHAR(500),\n    rx_ofd_archive_id VARCHAR(36),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 25. opd_prescription_items\nCREATE TABLE IF NOT EXISTS opd_prescription_items (\n    id VARCHAR(36) PRIMARY KEY,\n    prescription_id VARCHAR(36) NOT NULL,\n    group_no INT NOT NULL,\n    drug_id VARCHAR(36) NOT NULL,\n    drug_name_snapshot VARCHAR(200) NOT NULL,\n    dose_once DECIMAL(18,4) NOT NULL,\n    dose_unit VARCHAR(32) NOT NULL,\n    frequency_code VARCHAR(32) NOT NULL,\n    route_code VARCHAR(32) NOT NULL,\n    days INT NOT NULL,\n    total_quantity INT NOT NULL,\n    unit_price DECIMAL(18,4) NOT NULL,\n    skin_test_flag INT NOT NULL,\n    skin_test_batch_no VARCHAR(80),\n    skin_test_checker_id VARCHAR(36),\n    is_deleted SMALLINT NOT NULL DEFAULT 0,\n    deleted_at_ms BIGINT,\n    deleted_by VARCHAR(36),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 26. ipd_beds\nCREATE TABLE IF NOT EXISTS ipd_beds (\n    id VARCHAR(36) PRIMARY KEY,\n    campus_id VARCHAR(36) NOT NULL,\n    ward_id VARCHAR(36) NOT NULL,\n    room_code VARCHAR(40) NOT NULL,\n    bed_code VARCHAR(40) NOT NULL,\n    bed_type VARCHAR(32) NOT NULL,\n    daily_fee DECIMAL(18,4) NOT NULL,\n    bed_status INT NOT NULL,\n    current_encounter_id VARCHAR(36),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 27. ipd_encounters\nCREATE TABLE IF NOT EXISTS ipd_encounters (\n    id VARCHAR(36) PRIMARY KEY,\n    admission_no VARCHAR(40) NOT NULL UNIQUE,\n    patient_id VARCHAR(36) NOT NULL,\n    campus_id VARCHAR(36) NOT NULL,\n    admit_dept_id VARCHAR(36) NOT NULL,\n    med_group_id VARCHAR(36),\n    admit_ward_id VARCHAR(36) NOT NULL,\n    is_borrowed_bed SMALLINT NOT NULL,\n    bed_code VARCHAR(40),\n    active_bed_token VARCHAR(36) NOT NULL,\n    attending_doctor_id VARCHAR(36),\n    primary_nurse_id VARCHAR(36),\n    nursing_level_code VARCHAR(32),\n    prepayment_balance DECIMAL(18,4) NOT NULL,\n    archive_ofd_id VARCHAR(36),\n    status INT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 28. ipd_transfer_logs\nCREATE TABLE IF NOT EXISTS ipd_transfer_logs (\n    id VARCHAR(36) PRIMARY KEY,\n    encounter_id VARCHAR(36) NOT NULL,\n    transfer_type VARCHAR(32) NOT NULL,\n    from_dept_id VARCHAR(36) NOT NULL,\n    from_ward_id VARCHAR(36) NOT NULL,\n    from_bed_code VARCHAR(40),\n    to_dept_id VARCHAR(36) NOT NULL,\n    to_ward_id VARCHAR(36) NOT NULL,\n    to_bed_code VARCHAR(40),\n    handover_out_at_ms BIGINT NOT NULL,\n    handover_in_at_ms BIGINT,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 29. ord_medical_orders\nCREATE TABLE IF NOT EXISTS ord_medical_orders (\n    id VARCHAR(36) PRIMARY KEY,\n    order_no VARCHAR(40) NOT NULL UNIQUE,\n    encounter_id VARCHAR(36) NOT NULL,\n    patient_id VARCHAR(36) NOT NULL,\n    order_ipd_dept_id VARCHAR(36) NOT NULL,\n    patient_ward_id VARCHAR(36) NOT NULL,\n    exec_target_dept_id VARCHAR(36) NOT NULL,\n    order_term_type INT NOT NULL,\n    group_no INT NOT NULL,\n    order_category VARCHAR(32) NOT NULL,\n    item_code VARCHAR(64) NOT NULL,\n    item_name VARCHAR(200) NOT NULL,\n    dosage DECIMAL(18,4) NOT NULL,\n    frequency_code VARCHAR(32) NOT NULL,\n    route_code VARCHAR(32) NOT NULL,\n    status INT NOT NULL,\n    order_doctor_id VARCHAR(36) NOT NULL,\n    verify_nurse_id VARCHAR(36),\n    plan_start_at_ms BIGINT NOT NULL,\n    plan_stop_at_ms BIGINT,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 30. ord_execution_plans\nCREATE TABLE IF NOT EXISTS ord_execution_plans (\n    id VARCHAR(36) PRIMARY KEY,\n    order_id VARCHAR(36) NOT NULL,\n    encounter_id VARCHAR(36) NOT NULL,\n    ward_id VARCHAR(36) NOT NULL,\n    planned_time_ms BIGINT NOT NULL,\n    exec_status INT NOT NULL,\n    exec_nurse_id VARCHAR(36),\n    dual_check_nurse_id VARCHAR(36),\n    executed_at_ms BIGINT,\n    pda_scan_token VARCHAR(128),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 31. mtc_service_requests\nCREATE TABLE IF NOT EXISTS mtc_service_requests (\n    id VARCHAR(36) PRIMARY KEY,\n    request_no VARCHAR(40) NOT NULL UNIQUE,\n    source_type VARCHAR(16) NOT NULL,\n    source_visit_id VARCHAR(36),\n    source_order_id VARCHAR(36),\n    patient_id VARCHAR(36) NOT NULL,\n    order_dept_id VARCHAR(36) NOT NULL,\n    order_ward_id VARCHAR(36),\n    exec_med_tech_dept_id VARCHAR(36) NOT NULL,\n    med_tech_sub_type VARCHAR(32) NOT NULL,\n    exec_room_code VARCHAR(64),\n    specimen_tube_code VARCHAR(64),\n    service_item_code VARCHAR(64) NOT NULL,\n    fee_amount DECIMAL(18,4) NOT NULL,\n    is_fee_confirmed SMALLINT NOT NULL,\n    status INT NOT NULL,\n    collected_at_ms BIGINT,\n    received_at_ms BIGINT,\n    executed_at_ms BIGINT,\n    report_summary VARCHAR(2000),\n    report_ofd_archive_id VARCHAR(36),\n    is_critical_value SMALLINT NOT NULL,\n    critical_ack_doc_id VARCHAR(36),\n    critical_ack_nurse_id VARCHAR(36),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 32. mtc_surgery_schedules\nCREATE TABLE IF NOT EXISTS mtc_surgery_schedules (\n    id VARCHAR(36) PRIMARY KEY,\n    encounter_id VARCHAR(36) NOT NULL,\n    or_dept_id VARCHAR(36) NOT NULL,\n    or_room_code VARCHAR(64) NOT NULL,\n    table_seq INT NOT NULL,\n    surgery_code VARCHAR(64) NOT NULL,\n    surgery_name VARCHAR(200) NOT NULL,\n    surgery_level INT NOT NULL,\n    main_surgeon_id VARCHAR(36) NOT NULL,\n    anesthesiologist_id VARCHAR(36) NOT NULL,\n    circulating_nurse_id VARCHAR(36) NOT NULL,\n    sign_in_ok SMALLINT NOT NULL,\n    time_out_ok SMALLINT NOT NULL,\n    sign_out_ok SMALLINT NOT NULL,\n    instrument_count_balanced SMALLINT NOT NULL,\n    status INT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 33. mtc_blood_requests\nCREATE TABLE IF NOT EXISTS mtc_blood_requests (\n    id VARCHAR(36) PRIMARY KEY,\n    encounter_id VARCHAR(36) NOT NULL,\n    patient_id VARCHAR(36) NOT NULL,\n    abo_type VARCHAR(16) NOT NULL,\n    rh_type VARCHAR(16) NOT NULL,\n    blood_product_code VARCHAR(64) NOT NULL,\n    volume_ml INT NOT NULL,\n    cross_match_result VARCHAR(32),\n    bag_barcode VARCHAR(80),\n    dispensed_by_id VARCHAR(36),\n    dual_check_dispenser_id VARCHAR(36),\n    empty_bag_returned SMALLINT NOT NULL,\n    status INT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 34. inv_drugs\nCREATE TABLE IF NOT EXISTS inv_drugs (\n    id VARCHAR(36) PRIMARY KEY,\n    drug_code VARCHAR(64) NOT NULL UNIQUE,\n    drug_name VARCHAR(200) NOT NULL,\n    spec VARCHAR(100) NOT NULL,\n    unit_name VARCHAR(32) NOT NULL,\n    price DECIMAL(18,4) NOT NULL,\n    cost_price DECIMAL(18,4) NOT NULL,\n    antibiotic_level INT NOT NULL,\n    is_narcotic SMALLINT NOT NULL,\n    is_skin_test_required SMALLINT NOT NULL,\n    is_active SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 35. inv_drug_batches\nCREATE TABLE IF NOT EXISTS inv_drug_batches (\n    id VARCHAR(36) PRIMARY KEY,\n    warehouse_or_pharmacy_id VARCHAR(36) NOT NULL,\n    drug_id VARCHAR(36) NOT NULL,\n    batch_no VARCHAR(80) NOT NULL,\n    expiry_date_int INT NOT NULL,\n    invoice_no VARCHAR(80),\n    cold_chain_temp_celsius DECIMAL(18,4),\n    quantity INT NOT NULL,\n    is_frozen SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 36. inv_stock_ledgers\nCREATE TABLE IF NOT EXISTS inv_stock_ledgers (\n    id VARCHAR(36) PRIMARY KEY,\n    warehouse_or_pharmacy_id VARCHAR(36) NOT NULL,\n    drug_id VARCHAR(36) NOT NULL,\n    batch_id VARCHAR(36) NOT NULL,\n    change_type VARCHAR(32) NOT NULL,\n    change_quantity INT NOT NULL,\n    balance_after INT NOT NULL,\n    reference_doc_no VARCHAR(80),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 37. phm_dispense_orders\nCREATE TABLE IF NOT EXISTS phm_dispense_orders (\n    id VARCHAR(36) PRIMARY KEY,\n    dispense_no VARCHAR(40) NOT NULL UNIQUE,\n    pharmacy_id VARCHAR(36) NOT NULL,\n    prescription_id VARCHAR(36),\n    ward_summary_id VARCHAR(36),\n    patient_id VARCHAR(36) NOT NULL,\n    status INT NOT NULL,\n    dispensed_by_id VARCHAR(36),\n    dispensed_at_ms BIGINT,\n    drug_return_confirmed SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 38. fin_payment_orders\nCREATE TABLE IF NOT EXISTS fin_payment_orders (\n    id VARCHAR(36) PRIMARY KEY,\n    payment_no VARCHAR(40) NOT NULL UNIQUE,\n    request_key VARCHAR(100) UNIQUE,\n    scene VARCHAR(16) NOT NULL,\n    patient_id VARCHAR(36) NOT NULL,\n    visit_or_encounter_id VARCHAR(36) NOT NULL,\n    total_amount DECIMAL(18,4) NOT NULL,\n    insurance_amount DECIMAL(18,4) NOT NULL,\n    prepayment_deduct DECIMAL(18,4) NOT NULL,\n    self_pay_amount DECIMAL(18,4) NOT NULL,\n    payment_channel VARCHAR(32) NOT NULL,\n    status INT NOT NULL,\n    paid_at_ms BIGINT,\n    invoice_ofd_id VARCHAR(36),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 39. fin_refund_orders\nCREATE TABLE IF NOT EXISTS fin_refund_orders (\n    id VARCHAR(36) PRIMARY KEY,\n    refund_no VARCHAR(40) NOT NULL UNIQUE,\n    original_payment_id VARCHAR(36) NOT NULL,\n    refund_amount DECIMAL(18,4) NOT NULL,\n    reason VARCHAR(500) NOT NULL,\n    drug_return_confirmed SMALLINT NOT NULL,\n    approved_by VARCHAR(36),\n    refunded_at_ms BIGINT,\n    status INT NOT NULL,\n    reissued_invoice_ofd_id VARCHAR(36),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 40. fin_charge_ledgers\nCREATE TABLE IF NOT EXISTS fin_charge_ledgers (\n    id VARCHAR(36) PRIMARY KEY,\n    patient_id VARCHAR(36) NOT NULL,\n    visit_or_encounter_id VARCHAR(36) NOT NULL,\n    order_dept_id VARCHAR(36) NOT NULL,\n    patient_ward_id VARCHAR(36),\n    exec_dept_id VARCHAR(36) NOT NULL,\n    item_code VARCHAR(64) NOT NULL,\n    item_name VARCHAR(200) NOT NULL,\n    quantity DECIMAL(18,4) NOT NULL,\n    unit_price DECIMAL(18,4) NOT NULL,\n    total_amount DECIMAL(18,4) NOT NULL,\n    status INT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 41. fin_daily_closings\nCREATE TABLE IF NOT EXISTS fin_daily_closings (\n    id VARCHAR(36) PRIMARY KEY,\n    closing_no VARCHAR(40) NOT NULL UNIQUE,\n    cashier_user_id VARCHAR(36) NOT NULL,\n    closing_date_int INT NOT NULL,\n    total_income DECIMAL(18,4) NOT NULL,\n    total_refund DECIMAL(18,4) NOT NULL,\n    net_amount DECIMAL(18,4) NOT NULL,\n    reconciled_status INT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 42. ops_audit_events\nCREATE TABLE IF NOT EXISTS ops_audit_events (\n    id VARCHAR(36) PRIMARY KEY,\n    event_type VARCHAR(64) NOT NULL,\n    user_id VARCHAR(36),\n    operator_name VARCHAR(128),\n    dept_id VARCHAR(36),\n    target_entity VARCHAR(64) NOT NULL,\n    target_id VARCHAR(36) NOT NULL,\n    action_detail VARCHAR(2000),\n    prev_hash VARCHAR(64) NOT NULL,\n    curr_hash VARCHAR(64) NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 43. ops_idempotency_keys\nCREATE TABLE IF NOT EXISTS ops_idempotency_keys (\n    id VARCHAR(36) PRIMARY KEY,\n    request_key VARCHAR(100) NOT NULL UNIQUE,\n    request_sha256 VARCHAR(64) NOT NULL,\n    response_json TEXT,\n    expires_at_ms BIGINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 44. ops_outbox_messages\nCREATE TABLE IF NOT EXISTS ops_outbox_messages (\n    id VARCHAR(36) PRIMARY KEY,\n    event_type VARCHAR(128) NOT NULL,\n    payload_json TEXT NOT NULL,\n    status INT NOT NULL,\n    retry_count INT NOT NULL,\n    last_error VARCHAR(1000),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 45. cfg_icd10_diagnoses\nCREATE TABLE IF NOT EXISTS cfg_icd10_diagnoses (\n    id VARCHAR(36) PRIMARY KEY,\n    icd_code VARCHAR(32) NOT NULL UNIQUE,\n    disease_name VARCHAR(200) NOT NULL,\n    pinyin_code VARCHAR(64),\n    category_name VARCHAR(64) NOT NULL,\n    gender_exclusive VARCHAR(16) NOT NULL,\n    is_chronic_special SMALLINT NOT NULL,\n    is_infectious_report SMALLINT NOT NULL,\n    is_active SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 46. cfg_price_items\nCREATE TABLE IF NOT EXISTS cfg_price_items (\n    id VARCHAR(36) PRIMARY KEY,\n    item_code VARCHAR(64) NOT NULL UNIQUE,\n    national_ins_code VARCHAR(64),\n    item_name VARCHAR(200) NOT NULL,\n    pinyin_code VARCHAR(64),\n    fee_category VARCHAR(32) NOT NULL,\n    spec VARCHAR(100),\n    unit_name VARCHAR(32) NOT NULL,\n    unit_price DECIMAL(18,4) NOT NULL,\n    reimburse_type VARCHAR(16) NOT NULL,\n    default_exec_dept_id VARCHAR(36),\n    is_active SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 47. cfg_order_frequencies\nCREATE TABLE IF NOT EXISTS cfg_order_frequencies (\n    id VARCHAR(36) PRIMARY KEY,\n    freq_code VARCHAR(32) NOT NULL UNIQUE,\n    freq_name VARCHAR(64) NOT NULL,\n    daily_times INT NOT NULL,\n    time_points_text VARCHAR(255) NOT NULL,\n    applicable_term VARCHAR(32) NOT NULL,\n    description VARCHAR(500),\n    sort_order INT NOT NULL,\n    is_active SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 48. cfg_clinical_dictionaries\nCREATE TABLE IF NOT EXISTS cfg_clinical_dictionaries (\n    id VARCHAR(36) PRIMARY KEY,\n    dict_type VARCHAR(64) NOT NULL,\n    item_code VARCHAR(64) NOT NULL,\n    item_name VARCHAR(128) NOT NULL,\n    pinyin_code VARCHAR(64),\n    parent_code VARCHAR(64),\n    sort_order INT NOT NULL,\n    is_default SMALLINT NOT NULL,\n    is_active SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 49. inv_drug_price_logs\nCREATE TABLE IF NOT EXISTS inv_drug_price_logs (\n    id VARCHAR(36) PRIMARY KEY,\n    adjust_no VARCHAR(40) NOT NULL UNIQUE,\n    drug_id VARCHAR(36) NOT NULL,\n    drug_code VARCHAR(64) NOT NULL,\n    drug_name VARCHAR(200) NOT NULL,\n    old_retail_price DECIMAL(18,4) NOT NULL,\n    new_retail_price DECIMAL(18,4) NOT NULL,\n    old_cost_price DECIMAL(18,4) NOT NULL,\n    new_cost_price DECIMAL(18,4) NOT NULL,\n    affected_stock_quantity INT NOT NULL,\n    price_diff_amount DECIMAL(18,4) NOT NULL,\n    adjust_reason VARCHAR(500) NOT NULL,\n    effective_at_ms BIGINT NOT NULL,\n    operator_id VARCHAR(36),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 50. inv_transfer_orders\nCREATE TABLE IF NOT EXISTS inv_transfer_orders (\n    id VARCHAR(36) PRIMARY KEY,\n    transfer_no VARCHAR(40) NOT NULL UNIQUE,\n    transfer_type VARCHAR(32) NOT NULL,\n    source_warehouse_id VARCHAR(36) NOT NULL,\n    target_warehouse_id VARCHAR(36) NOT NULL,\n    drug_id VARCHAR(36) NOT NULL,\n    drug_name_snapshot VARCHAR(200) NOT NULL,\n    batch_id VARCHAR(36) NOT NULL,\n    batch_no VARCHAR(80) NOT NULL,\n    expiry_date_int INT NOT NULL,\n    quantity INT NOT NULL,\n    unit_cost_price DECIMAL(18,4) NOT NULL,\n    unit_retail_price DECIMAL(18,4) NOT NULL,\n    total_retail_amount DECIMAL(18,4) NOT NULL,\n    status INT NOT NULL,\n    applicant_id VARCHAR(36),\n    dispatched_by_id VARCHAR(36),\n    dispatched_at_ms BIGINT,\n    received_by_id VARCHAR(36),\n    received_at_ms BIGINT,\n    remark VARCHAR(500),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);";

	public static async Task InitializeAsync(IDbConnectionFactory connectionFactory)
	{
		using DbConnection conn = connectionFactory.CreateConnection();
		string[] statements = "-- 1. emr_data_elements\nCREATE TABLE IF NOT EXISTS emr_data_elements (\n    id VARCHAR(36) PRIMARY KEY,\n    element_code VARCHAR(64) NOT NULL UNIQUE,\n    nhc_std_code VARCHAR(64),\n    element_name VARCHAR(128) NOT NULL,\n    data_type VARCHAR(32) NOT NULL,\n    ursa_control_type VARCHAR(64) NOT NULL,\n    dict_code VARCHAR(64),\n    unit_text VARCHAR(32),\n    macro_source VARCHAR(128),\n    gender_exclusive VARCHAR(16) NOT NULL,\n    validation_rule_json VARCHAR(2000),\n    control_key VARCHAR(128),\n    is_indexed SMALLINT NOT NULL,\n    is_active SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 2. emr_ofd_layout_schemas\nCREATE TABLE IF NOT EXISTS emr_ofd_layout_schemas (\n    id VARCHAR(36) PRIMARY KEY,\n    layout_code VARCHAR(64) NOT NULL UNIQUE,\n    layout_name VARCHAR(128) NOT NULL,\n    page_size_code VARCHAR(16) NOT NULL,\n    page_width_mm DECIMAL(18,4) NOT NULL,\n    page_height_mm DECIMAL(18,4) NOT NULL,\n    margin_top_mm DECIMAL(18,4) NOT NULL,\n    margin_bottom_mm DECIMAL(18,4) NOT NULL,\n    margin_left_mm DECIMAL(18,4) NOT NULL,\n    margin_right_mm DECIMAL(18,4) NOT NULL,\n    is_continuous_flow SMALLINT NOT NULL,\n    header_layout_json TEXT,\n    footer_layout_json TEXT,\n    sign_slots_json VARCHAR(2000),\n    watermark_policy VARCHAR(32),\n    is_active SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 3. emr_doc_templates\nCREATE TABLE IF NOT EXISTS emr_doc_templates (\n    id VARCHAR(36) PRIMARY KEY,\n    template_code VARCHAR(64) NOT NULL,\n    template_version INT NOT NULL,\n    template_name VARCHAR(128) NOT NULL,\n    emr_scene VARCHAR(16) NOT NULL,\n    doc_type_code VARCHAR(64) NOT NULL,\n    dept_category VARCHAR(32),\n    target_dept_id VARCHAR(36),\n    disease_icd_prefix VARCHAR(32),\n    time_limit_hours INT NOT NULL,\n    required_sign_levels INT NOT NULL,\n    ofd_layout_id VARCHAR(36),\n    is_published SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 4. emr_section_schemas\nCREATE TABLE IF NOT EXISTS emr_section_schemas (\n    id VARCHAR(36) PRIMARY KEY,\n    template_id VARCHAR(36) NOT NULL,\n    section_code VARCHAR(64) NOT NULL,\n    section_title VARCHAR(128) NOT NULL,\n    render_mode VARCHAR(32) NOT NULL,\n    content_template_json TEXT,\n    section_control_key VARCHAR(128),\n    is_required SMALLINT NOT NULL,\n    visibility_expr VARCHAR(500),\n    sort_order INT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 5. emr_documents\nCREATE TABLE IF NOT EXISTS emr_documents (\n    id VARCHAR(36) PRIMARY KEY,\n    doc_no VARCHAR(40) NOT NULL UNIQUE,\n    request_key VARCHAR(100) UNIQUE,\n    campus_id VARCHAR(36),\n    emr_scene VARCHAR(16) NOT NULL,\n    visit_or_encounter_id VARCHAR(36) NOT NULL,\n    patient_id VARCHAR(36) NOT NULL,\n    dept_id VARCHAR(36) NOT NULL,\n    ward_id VARCHAR(36),\n    template_id VARCHAR(36) NOT NULL,\n    template_version INT NOT NULL,\n    doc_type_code VARCHAR(64) NOT NULL,\n    doc_title VARCHAR(200) NOT NULL,\n    clinical_time_ms BIGINT NOT NULL,\n    structured_ast_json TEXT,\n    plain_text_summary TEXT,\n    track_changes_json TEXT,\n    status INT NOT NULL,\n    author_id VARCHAR(36) NOT NULL,\n    resident_signed_at_ms BIGINT,\n    attending_doctor_id VARCHAR(36),\n    attending_signed_at_ms BIGINT,\n    chief_doctor_id VARCHAR(36),\n    chief_signed_at_ms BIGINT,\n    patient_sign_svg TEXT,\n    latest_ofd_archive_id VARCHAR(36),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 6. emr_document_elements\nCREATE TABLE IF NOT EXISTS emr_document_elements (\n    id VARCHAR(36) PRIMARY KEY,\n    document_id VARCHAR(36) NOT NULL,\n    patient_id VARCHAR(36) NOT NULL,\n    emr_scene VARCHAR(16) NOT NULL,\n    doc_type_code VARCHAR(64) NOT NULL,\n    section_code VARCHAR(64) NOT NULL,\n    element_code VARCHAR(64) NOT NULL,\n    nhc_std_code VARCHAR(64),\n    val_string VARCHAR(1000),\n    val_int BIGINT,\n    val_decimal DECIMAL(18,4),\n    val_time_ms BIGINT,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 7. emr_ofd_archives\nCREATE TABLE IF NOT EXISTS emr_ofd_archives (\n    id VARCHAR(36) PRIMARY KEY,\n    ofd_no VARCHAR(40) NOT NULL UNIQUE,\n    archive_scope VARCHAR(32) NOT NULL,\n    visit_or_encounter_id VARCHAR(36) NOT NULL,\n    document_id VARCHAR(36),\n    patient_id VARCHAR(36) NOT NULL,\n    ofd_version_standard VARCHAR(32) NOT NULL,\n    page_count INT NOT NULL,\n    file_storage_key VARCHAR(255) NOT NULL,\n    file_size_bytes BIGINT NOT NULL,\n    content_sha256 VARCHAR(64) NOT NULL,\n    sm3_digest VARCHAR(64) NOT NULL,\n    ca_sign_serial_no VARCHAR(128),\n    tsa_timestamp_token VARCHAR(500),\n    has_embedded_semantic_xml SMALLINT NOT NULL,\n    archive_status INT NOT NULL,\n    generated_at_ms BIGINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 8. sys_departments\nCREATE TABLE IF NOT EXISTS sys_departments (\n    id VARCHAR(36) PRIMARY KEY,\n    campus_id VARCHAR(36) NOT NULL,\n    dept_code VARCHAR(64) NOT NULL UNIQUE,\n    dept_name VARCHAR(128) NOT NULL,\n    pinyin_code VARCHAR(64),\n    dept_category VARCHAR(32) NOT NULL,\n    sub_specialty_type VARCHAR(32),\n    parent_admin_id VARCHAR(36),\n    gender_limit VARCHAR(16),\n    min_age_days INT,\n    max_age_years INT,\n    default_pharmacy_id VARCHAR(36),\n    rated_bed_count INT,\n    location_address VARCHAR(255),\n    is_allow_appointment SMALLINT NOT NULL,\n    is_active SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 9. sys_dept_ward_relations\nCREATE TABLE IF NOT EXISTS sys_dept_ward_relations (\n    id VARCHAR(36) PRIMARY KEY,\n    ipd_dept_id VARCHAR(36) NOT NULL,\n    ward_dept_id VARCHAR(36) NOT NULL,\n    relation_type VARCHAR(32) NOT NULL,\n    max_borrow_beds INT NOT NULL,\n    priority_order INT NOT NULL,\n    is_active SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 10. sys_medtech_exec_routings\nCREATE TABLE IF NOT EXISTS sys_medtech_exec_routings (\n    id VARCHAR(36) PRIMARY KEY,\n    campus_id VARCHAR(36) NOT NULL,\n    source_scene VARCHAR(32) NOT NULL,\n    order_dept_id VARCHAR(36),\n    service_item_code VARCHAR(64) NOT NULL,\n    exec_med_tech_dept_id VARCHAR(36) NOT NULL,\n    exec_room_code VARCHAR(64),\n    time_window_type VARCHAR(32) NOT NULL,\n    priority_weight INT NOT NULL,\n    is_active SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 11. sys_users\nCREATE TABLE IF NOT EXISTS sys_users (\n    id VARCHAR(36) PRIMARY KEY,\n    user_name VARCHAR(64) NOT NULL UNIQUE,\n    display_name VARCHAR(128) NOT NULL,\n    employee_no VARCHAR(64) NOT NULL,\n    password_hash VARCHAR(255) NOT NULL,\n    bound_patient_id VARCHAR(36),\n    prescribe_level INT NOT NULL,\n    surgery_level INT NOT NULL,\n    emr_sign_level INT NOT NULL,\n    ca_cert_id VARCHAR(128),\n    session_version INT NOT NULL,\n    is_active SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 12. sys_user_dept_posts\nCREATE TABLE IF NOT EXISTS sys_user_dept_posts (\n    id VARCHAR(36) PRIMARY KEY,\n    user_id VARCHAR(36) NOT NULL,\n    dept_id VARCHAR(36) NOT NULL,\n    dept_category VARCHAR(32) NOT NULL,\n    med_group_id VARCHAR(36),\n    role_id VARCHAR(36) NOT NULL,\n    is_default_context SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 13. sys_roles\nCREATE TABLE IF NOT EXISTS sys_roles (\n    id VARCHAR(36) PRIMARY KEY,\n    role_code VARCHAR(64) NOT NULL UNIQUE,\n    role_name VARCHAR(128) NOT NULL,\n    data_scope VARCHAR(32) NOT NULL,\n    is_active SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 14. sys_permission_resources\nCREATE TABLE IF NOT EXISTS sys_permission_resources (\n    id VARCHAR(36) PRIMARY KEY,\n    resource_key VARCHAR(128) NOT NULL UNIQUE,\n    parent_key VARCHAR(128),\n    resource_type VARCHAR(32) NOT NULL,\n    resource_name VARCHAR(128) NOT NULL,\n    ursa_control_type VARCHAR(64),\n    api_method_path VARCHAR(255),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 15. sys_role_permissions\nCREATE TABLE IF NOT EXISTS sys_role_permissions (\n    id VARCHAR(36) PRIMARY KEY,\n    role_id VARCHAR(36) NOT NULL,\n    resource_key VARCHAR(128) NOT NULL,\n    access_policy INT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 16. cfg_form_schemas\nCREATE TABLE IF NOT EXISTS cfg_form_schemas (\n    id VARCHAR(36) PRIMARY KEY,\n    form_code VARCHAR(64) NOT NULL UNIQUE,\n    form_name VARCHAR(128) NOT NULL,\n    entity_type VARCHAR(64) NOT NULL,\n    dept_category VARCHAR(32),\n    target_dept_id VARCHAR(36),\n    grid_columns INT NOT NULL,\n    is_published SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 17. cfg_field_definitions\nCREATE TABLE IF NOT EXISTS cfg_field_definitions (\n    id VARCHAR(36) PRIMARY KEY,\n    form_schema_id VARCHAR(36) NOT NULL,\n    field_code VARCHAR(64) NOT NULL,\n    field_label VARCHAR(128) NOT NULL,\n    data_type VARCHAR(32) NOT NULL,\n    ursa_control_type VARCHAR(64) NOT NULL,\n    control_key VARCHAR(128),\n    dict_code VARCHAR(64),\n    unit_text VARCHAR(32),\n    validation_rule_json VARCHAR(2000),\n    visibility_expr VARCHAR(500),\n    is_indexed SMALLINT NOT NULL,\n    sort_order INT NOT NULL,\n    is_deleted SMALLINT NOT NULL DEFAULT 0,\n    deleted_at_ms BIGINT,\n    deleted_by VARCHAR(36),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 18. cfg_entity_field_values\nCREATE TABLE IF NOT EXISTS cfg_entity_field_values (\n    id VARCHAR(36) PRIMARY KEY,\n    entity_type VARCHAR(64) NOT NULL,\n    entity_id VARCHAR(36) NOT NULL,\n    field_code VARCHAR(64) NOT NULL,\n    val_string VARCHAR(500),\n    val_int BIGINT,\n    val_decimal DECIMAL(18,4),\n    val_time_ms BIGINT,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 19. pat_patients\nCREATE TABLE IF NOT EXISTS pat_patients (\n    id VARCHAR(36) PRIMARY KEY,\n    medical_record_no VARCHAR(32) NOT NULL UNIQUE,\n    full_name VARCHAR(128) NOT NULL,\n    pinyin_code VARCHAR(64),\n    gender_code VARCHAR(16) NOT NULL,\n    birth_date_int INT NOT NULL,\n    id_card_type VARCHAR(32) NOT NULL,\n    id_card_cipher VARCHAR(255),\n    id_card_hash VARCHAR(64) NOT NULL,\n    phone_cipher VARCHAR(255),\n    phone_masked VARCHAR(32),\n    guardian_name VARCHAR(128),\n    guardian_id_hash VARCHAR(64),\n    allergy_tags_json VARCHAR(2000),\n    is_temp_emergency SMALLINT NOT NULL,\n    merged_to_patient_id VARCHAR(36),\n    is_active SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 20. opd_schedule_slots\nCREATE TABLE IF NOT EXISTS opd_schedule_slots (\n    id VARCHAR(36) PRIMARY KEY,\n    campus_id VARCHAR(36) NOT NULL,\n    opd_dept_id VARCHAR(36) NOT NULL,\n    clinic_room_id VARCHAR(36),\n    doctor_id VARCHAR(36) NOT NULL,\n    substitute_doctor_id VARCHAR(36),\n    schedule_date_int INT NOT NULL,\n    slot_period_code VARCHAR(32) NOT NULL,\n    starts_at_ms BIGINT NOT NULL,\n    ends_at_ms BIGINT NOT NULL,\n    reg_level_code VARCHAR(32) NOT NULL,\n    registration_fee DECIMAL(18,4) NOT NULL,\n    total_capacity INT NOT NULL,\n    reserved_count INT NOT NULL,\n    slot_status INT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 21. opd_appointments\nCREATE TABLE IF NOT EXISTS opd_appointments (\n    id VARCHAR(36) PRIMARY KEY,\n    appointment_no VARCHAR(40) NOT NULL UNIQUE,\n    request_key VARCHAR(100) UNIQUE,\n    patient_id VARCHAR(36) NOT NULL,\n    schedule_id VARCHAR(36) NOT NULL,\n    active_unique_token VARCHAR(36) NOT NULL,\n    queue_no INT NOT NULL,\n    status INT NOT NULL,\n    lock_expires_at_ms BIGINT NOT NULL,\n    visit_id VARCHAR(36),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 22. opd_visits\nCREATE TABLE IF NOT EXISTS opd_visits (\n    id VARCHAR(36) PRIMARY KEY,\n    visit_no VARCHAR(40) NOT NULL UNIQUE,\n    request_key VARCHAR(100) UNIQUE,\n    campus_id VARCHAR(36) NOT NULL,\n    opd_dept_id VARCHAR(36) NOT NULL,\n    clinic_room_id VARCHAR(36),\n    patient_id VARCHAR(36) NOT NULL,\n    schedule_id VARCHAR(36),\n    doctor_id VARCHAR(36) NOT NULL,\n    is_emergency SMALLINT NOT NULL,\n    er_triage_level INT,\n    is_green_channel SMALLINT NOT NULL,\n    green_credit_limit DECIMAL(18,4),\n    is_return_visit SMALLINT NOT NULL,\n    status INT NOT NULL,\n    primary_diag_code VARCHAR(32),\n    primary_diag_name VARCHAR(255),\n    emr_document_id VARCHAR(36),\n    ofd_archive_id VARCHAR(36),\n    signed_at_ms BIGINT,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 23. opd_triage_queues\nCREATE TABLE IF NOT EXISTS opd_triage_queues (\n    id VARCHAR(36) PRIMARY KEY,\n    visit_id VARCHAR(36) NOT NULL UNIQUE,\n    opd_dept_id VARCHAR(36) NOT NULL,\n    clinic_room_id VARCHAR(36),\n    priority_score INT NOT NULL,\n    pass_count INT NOT NULL,\n    queue_status INT NOT NULL,\n    checkin_at_ms BIGINT NOT NULL,\n    called_at_ms BIGINT,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 24. opd_prescriptions\nCREATE TABLE IF NOT EXISTS opd_prescriptions (\n    id VARCHAR(36) PRIMARY KEY,\n    prescription_no VARCHAR(40) NOT NULL UNIQUE,\n    visit_id VARCHAR(36) NOT NULL,\n    patient_id VARCHAR(36) NOT NULL,\n    opd_dept_id VARCHAR(36) NOT NULL,\n    doctor_id VARCHAR(36) NOT NULL,\n    target_pharmacy_id VARCHAR(36) NOT NULL,\n    rx_type VARCHAR(32) NOT NULL,\n    herb_doses INT,\n    total_amount DECIMAL(18,4) NOT NULL,\n    status INT NOT NULL,\n    reviewer_pharmacist_id VARCHAR(36),\n    review_remark VARCHAR(500),\n    rx_ofd_archive_id VARCHAR(36),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 25. opd_prescription_items\nCREATE TABLE IF NOT EXISTS opd_prescription_items (\n    id VARCHAR(36) PRIMARY KEY,\n    prescription_id VARCHAR(36) NOT NULL,\n    group_no INT NOT NULL,\n    drug_id VARCHAR(36) NOT NULL,\n    drug_name_snapshot VARCHAR(200) NOT NULL,\n    dose_once DECIMAL(18,4) NOT NULL,\n    dose_unit VARCHAR(32) NOT NULL,\n    frequency_code VARCHAR(32) NOT NULL,\n    route_code VARCHAR(32) NOT NULL,\n    days INT NOT NULL,\n    total_quantity INT NOT NULL,\n    unit_price DECIMAL(18,4) NOT NULL,\n    skin_test_flag INT NOT NULL,\n    skin_test_batch_no VARCHAR(80),\n    skin_test_checker_id VARCHAR(36),\n    is_deleted SMALLINT NOT NULL DEFAULT 0,\n    deleted_at_ms BIGINT,\n    deleted_by VARCHAR(36),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 26. ipd_beds\nCREATE TABLE IF NOT EXISTS ipd_beds (\n    id VARCHAR(36) PRIMARY KEY,\n    campus_id VARCHAR(36) NOT NULL,\n    ward_id VARCHAR(36) NOT NULL,\n    room_code VARCHAR(40) NOT NULL,\n    bed_code VARCHAR(40) NOT NULL,\n    bed_type VARCHAR(32) NOT NULL,\n    daily_fee DECIMAL(18,4) NOT NULL,\n    bed_status INT NOT NULL,\n    current_encounter_id VARCHAR(36),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 27. ipd_encounters\nCREATE TABLE IF NOT EXISTS ipd_encounters (\n    id VARCHAR(36) PRIMARY KEY,\n    admission_no VARCHAR(40) NOT NULL UNIQUE,\n    patient_id VARCHAR(36) NOT NULL,\n    campus_id VARCHAR(36) NOT NULL,\n    admit_dept_id VARCHAR(36) NOT NULL,\n    med_group_id VARCHAR(36),\n    admit_ward_id VARCHAR(36) NOT NULL,\n    is_borrowed_bed SMALLINT NOT NULL,\n    bed_code VARCHAR(40),\n    active_bed_token VARCHAR(36) NOT NULL,\n    attending_doctor_id VARCHAR(36),\n    primary_nurse_id VARCHAR(36),\n    nursing_level_code VARCHAR(32),\n    prepayment_balance DECIMAL(18,4) NOT NULL,\n    archive_ofd_id VARCHAR(36),\n    status INT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 28. ipd_transfer_logs\nCREATE TABLE IF NOT EXISTS ipd_transfer_logs (\n    id VARCHAR(36) PRIMARY KEY,\n    encounter_id VARCHAR(36) NOT NULL,\n    transfer_type VARCHAR(32) NOT NULL,\n    from_dept_id VARCHAR(36) NOT NULL,\n    from_ward_id VARCHAR(36) NOT NULL,\n    from_bed_code VARCHAR(40),\n    to_dept_id VARCHAR(36) NOT NULL,\n    to_ward_id VARCHAR(36) NOT NULL,\n    to_bed_code VARCHAR(40),\n    handover_out_at_ms BIGINT NOT NULL,\n    handover_in_at_ms BIGINT,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 29. ord_medical_orders\nCREATE TABLE IF NOT EXISTS ord_medical_orders (\n    id VARCHAR(36) PRIMARY KEY,\n    order_no VARCHAR(40) NOT NULL UNIQUE,\n    encounter_id VARCHAR(36) NOT NULL,\n    patient_id VARCHAR(36) NOT NULL,\n    order_ipd_dept_id VARCHAR(36) NOT NULL,\n    patient_ward_id VARCHAR(36) NOT NULL,\n    exec_target_dept_id VARCHAR(36) NOT NULL,\n    order_term_type INT NOT NULL,\n    group_no INT NOT NULL,\n    order_category VARCHAR(32) NOT NULL,\n    item_code VARCHAR(64) NOT NULL,\n    item_name VARCHAR(200) NOT NULL,\n    dosage DECIMAL(18,4) NOT NULL,\n    frequency_code VARCHAR(32) NOT NULL,\n    route_code VARCHAR(32) NOT NULL,\n    status INT NOT NULL,\n    order_doctor_id VARCHAR(36) NOT NULL,\n    verify_nurse_id VARCHAR(36),\n    plan_start_at_ms BIGINT NOT NULL,\n    plan_stop_at_ms BIGINT,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 30. ord_execution_plans\nCREATE TABLE IF NOT EXISTS ord_execution_plans (\n    id VARCHAR(36) PRIMARY KEY,\n    order_id VARCHAR(36) NOT NULL,\n    encounter_id VARCHAR(36) NOT NULL,\n    ward_id VARCHAR(36) NOT NULL,\n    planned_time_ms BIGINT NOT NULL,\n    exec_status INT NOT NULL,\n    exec_nurse_id VARCHAR(36),\n    dual_check_nurse_id VARCHAR(36),\n    executed_at_ms BIGINT,\n    pda_scan_token VARCHAR(128),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 31. mtc_service_requests\nCREATE TABLE IF NOT EXISTS mtc_service_requests (\n    id VARCHAR(36) PRIMARY KEY,\n    request_no VARCHAR(40) NOT NULL UNIQUE,\n    source_type VARCHAR(16) NOT NULL,\n    source_visit_id VARCHAR(36),\n    source_order_id VARCHAR(36),\n    patient_id VARCHAR(36) NOT NULL,\n    order_dept_id VARCHAR(36) NOT NULL,\n    order_ward_id VARCHAR(36),\n    exec_med_tech_dept_id VARCHAR(36) NOT NULL,\n    med_tech_sub_type VARCHAR(32) NOT NULL,\n    exec_room_code VARCHAR(64),\n    specimen_tube_code VARCHAR(64),\n    service_item_code VARCHAR(64) NOT NULL,\n    fee_amount DECIMAL(18,4) NOT NULL,\n    is_fee_confirmed SMALLINT NOT NULL,\n    status INT NOT NULL,\n    collected_at_ms BIGINT,\n    received_at_ms BIGINT,\n    executed_at_ms BIGINT,\n    report_summary VARCHAR(2000),\n    report_ofd_archive_id VARCHAR(36),\n    is_critical_value SMALLINT NOT NULL,\n    critical_ack_doc_id VARCHAR(36),\n    critical_ack_nurse_id VARCHAR(36),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 32. mtc_surgery_schedules\nCREATE TABLE IF NOT EXISTS mtc_surgery_schedules (\n    id VARCHAR(36) PRIMARY KEY,\n    encounter_id VARCHAR(36) NOT NULL,\n    or_dept_id VARCHAR(36) NOT NULL,\n    or_room_code VARCHAR(64) NOT NULL,\n    table_seq INT NOT NULL,\n    surgery_code VARCHAR(64) NOT NULL,\n    surgery_name VARCHAR(200) NOT NULL,\n    surgery_level INT NOT NULL,\n    main_surgeon_id VARCHAR(36) NOT NULL,\n    anesthesiologist_id VARCHAR(36) NOT NULL,\n    circulating_nurse_id VARCHAR(36) NOT NULL,\n    sign_in_ok SMALLINT NOT NULL,\n    time_out_ok SMALLINT NOT NULL,\n    sign_out_ok SMALLINT NOT NULL,\n    instrument_count_balanced SMALLINT NOT NULL,\n    status INT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 33. mtc_blood_requests\nCREATE TABLE IF NOT EXISTS mtc_blood_requests (\n    id VARCHAR(36) PRIMARY KEY,\n    encounter_id VARCHAR(36) NOT NULL,\n    patient_id VARCHAR(36) NOT NULL,\n    abo_type VARCHAR(16) NOT NULL,\n    rh_type VARCHAR(16) NOT NULL,\n    blood_product_code VARCHAR(64) NOT NULL,\n    volume_ml INT NOT NULL,\n    cross_match_result VARCHAR(32),\n    bag_barcode VARCHAR(80),\n    dispensed_by_id VARCHAR(36),\n    dual_check_dispenser_id VARCHAR(36),\n    empty_bag_returned SMALLINT NOT NULL,\n    status INT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 34. inv_drugs\nCREATE TABLE IF NOT EXISTS inv_drugs (\n    id VARCHAR(36) PRIMARY KEY,\n    drug_code VARCHAR(64) NOT NULL UNIQUE,\n    drug_name VARCHAR(200) NOT NULL,\n    spec VARCHAR(100) NOT NULL,\n    unit_name VARCHAR(32) NOT NULL,\n    price DECIMAL(18,4) NOT NULL,\n    cost_price DECIMAL(18,4) NOT NULL,\n    antibiotic_level INT NOT NULL,\n    is_narcotic SMALLINT NOT NULL,\n    is_skin_test_required SMALLINT NOT NULL,\n    is_active SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 35. inv_drug_batches\nCREATE TABLE IF NOT EXISTS inv_drug_batches (\n    id VARCHAR(36) PRIMARY KEY,\n    warehouse_or_pharmacy_id VARCHAR(36) NOT NULL,\n    drug_id VARCHAR(36) NOT NULL,\n    batch_no VARCHAR(80) NOT NULL,\n    expiry_date_int INT NOT NULL,\n    invoice_no VARCHAR(80),\n    cold_chain_temp_celsius DECIMAL(18,4),\n    quantity INT NOT NULL,\n    is_frozen SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 36. inv_stock_ledgers\nCREATE TABLE IF NOT EXISTS inv_stock_ledgers (\n    id VARCHAR(36) PRIMARY KEY,\n    warehouse_or_pharmacy_id VARCHAR(36) NOT NULL,\n    drug_id VARCHAR(36) NOT NULL,\n    batch_id VARCHAR(36) NOT NULL,\n    change_type VARCHAR(32) NOT NULL,\n    change_quantity INT NOT NULL,\n    balance_after INT NOT NULL,\n    reference_doc_no VARCHAR(80),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 37. phm_dispense_orders\nCREATE TABLE IF NOT EXISTS phm_dispense_orders (\n    id VARCHAR(36) PRIMARY KEY,\n    dispense_no VARCHAR(40) NOT NULL UNIQUE,\n    pharmacy_id VARCHAR(36) NOT NULL,\n    prescription_id VARCHAR(36),\n    ward_summary_id VARCHAR(36),\n    patient_id VARCHAR(36) NOT NULL,\n    status INT NOT NULL,\n    dispensed_by_id VARCHAR(36),\n    dispensed_at_ms BIGINT,\n    drug_return_confirmed SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 38. fin_payment_orders\nCREATE TABLE IF NOT EXISTS fin_payment_orders (\n    id VARCHAR(36) PRIMARY KEY,\n    payment_no VARCHAR(40) NOT NULL UNIQUE,\n    request_key VARCHAR(100) UNIQUE,\n    scene VARCHAR(16) NOT NULL,\n    patient_id VARCHAR(36) NOT NULL,\n    visit_or_encounter_id VARCHAR(36) NOT NULL,\n    total_amount DECIMAL(18,4) NOT NULL,\n    insurance_amount DECIMAL(18,4) NOT NULL,\n    prepayment_deduct DECIMAL(18,4) NOT NULL,\n    self_pay_amount DECIMAL(18,4) NOT NULL,\n    payment_channel VARCHAR(32) NOT NULL,\n    status INT NOT NULL,\n    paid_at_ms BIGINT,\n    invoice_ofd_id VARCHAR(36),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 39. fin_refund_orders\nCREATE TABLE IF NOT EXISTS fin_refund_orders (\n    id VARCHAR(36) PRIMARY KEY,\n    refund_no VARCHAR(40) NOT NULL UNIQUE,\n    original_payment_id VARCHAR(36) NOT NULL,\n    refund_amount DECIMAL(18,4) NOT NULL,\n    reason VARCHAR(500) NOT NULL,\n    drug_return_confirmed SMALLINT NOT NULL,\n    approved_by VARCHAR(36),\n    refunded_at_ms BIGINT,\n    status INT NOT NULL,\n    reissued_invoice_ofd_id VARCHAR(36),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 40. fin_charge_ledgers\nCREATE TABLE IF NOT EXISTS fin_charge_ledgers (\n    id VARCHAR(36) PRIMARY KEY,\n    patient_id VARCHAR(36) NOT NULL,\n    visit_or_encounter_id VARCHAR(36) NOT NULL,\n    order_dept_id VARCHAR(36) NOT NULL,\n    patient_ward_id VARCHAR(36),\n    exec_dept_id VARCHAR(36) NOT NULL,\n    item_code VARCHAR(64) NOT NULL,\n    item_name VARCHAR(200) NOT NULL,\n    quantity DECIMAL(18,4) NOT NULL,\n    unit_price DECIMAL(18,4) NOT NULL,\n    total_amount DECIMAL(18,4) NOT NULL,\n    status INT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 41. fin_daily_closings\nCREATE TABLE IF NOT EXISTS fin_daily_closings (\n    id VARCHAR(36) PRIMARY KEY,\n    closing_no VARCHAR(40) NOT NULL UNIQUE,\n    cashier_user_id VARCHAR(36) NOT NULL,\n    closing_date_int INT NOT NULL,\n    total_income DECIMAL(18,4) NOT NULL,\n    total_refund DECIMAL(18,4) NOT NULL,\n    net_amount DECIMAL(18,4) NOT NULL,\n    reconciled_status INT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 42. ops_audit_events\nCREATE TABLE IF NOT EXISTS ops_audit_events (\n    id VARCHAR(36) PRIMARY KEY,\n    event_type VARCHAR(64) NOT NULL,\n    user_id VARCHAR(36),\n    operator_name VARCHAR(128),\n    dept_id VARCHAR(36),\n    target_entity VARCHAR(64) NOT NULL,\n    target_id VARCHAR(36) NOT NULL,\n    action_detail VARCHAR(2000),\n    prev_hash VARCHAR(64) NOT NULL,\n    curr_hash VARCHAR(64) NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 43. ops_idempotency_keys\nCREATE TABLE IF NOT EXISTS ops_idempotency_keys (\n    id VARCHAR(36) PRIMARY KEY,\n    request_key VARCHAR(100) NOT NULL UNIQUE,\n    request_sha256 VARCHAR(64) NOT NULL,\n    response_json TEXT,\n    expires_at_ms BIGINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 44. ops_outbox_messages\nCREATE TABLE IF NOT EXISTS ops_outbox_messages (\n    id VARCHAR(36) PRIMARY KEY,\n    event_type VARCHAR(128) NOT NULL,\n    payload_json TEXT NOT NULL,\n    status INT NOT NULL,\n    retry_count INT NOT NULL,\n    last_error VARCHAR(1000),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 45. cfg_icd10_diagnoses\nCREATE TABLE IF NOT EXISTS cfg_icd10_diagnoses (\n    id VARCHAR(36) PRIMARY KEY,\n    icd_code VARCHAR(32) NOT NULL UNIQUE,\n    disease_name VARCHAR(200) NOT NULL,\n    pinyin_code VARCHAR(64),\n    category_name VARCHAR(64) NOT NULL,\n    gender_exclusive VARCHAR(16) NOT NULL,\n    is_chronic_special SMALLINT NOT NULL,\n    is_infectious_report SMALLINT NOT NULL,\n    is_active SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 46. cfg_price_items\nCREATE TABLE IF NOT EXISTS cfg_price_items (\n    id VARCHAR(36) PRIMARY KEY,\n    item_code VARCHAR(64) NOT NULL UNIQUE,\n    national_ins_code VARCHAR(64),\n    item_name VARCHAR(200) NOT NULL,\n    pinyin_code VARCHAR(64),\n    fee_category VARCHAR(32) NOT NULL,\n    spec VARCHAR(100),\n    unit_name VARCHAR(32) NOT NULL,\n    unit_price DECIMAL(18,4) NOT NULL,\n    reimburse_type VARCHAR(16) NOT NULL,\n    default_exec_dept_id VARCHAR(36),\n    is_active SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 47. cfg_order_frequencies\nCREATE TABLE IF NOT EXISTS cfg_order_frequencies (\n    id VARCHAR(36) PRIMARY KEY,\n    freq_code VARCHAR(32) NOT NULL UNIQUE,\n    freq_name VARCHAR(64) NOT NULL,\n    daily_times INT NOT NULL,\n    time_points_text VARCHAR(255) NOT NULL,\n    applicable_term VARCHAR(32) NOT NULL,\n    description VARCHAR(500),\n    sort_order INT NOT NULL,\n    is_active SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 48. cfg_clinical_dictionaries\nCREATE TABLE IF NOT EXISTS cfg_clinical_dictionaries (\n    id VARCHAR(36) PRIMARY KEY,\n    dict_type VARCHAR(64) NOT NULL,\n    item_code VARCHAR(64) NOT NULL,\n    item_name VARCHAR(128) NOT NULL,\n    pinyin_code VARCHAR(64),\n    parent_code VARCHAR(64),\n    sort_order INT NOT NULL,\n    is_default SMALLINT NOT NULL,\n    is_active SMALLINT NOT NULL,\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 49. inv_drug_price_logs\nCREATE TABLE IF NOT EXISTS inv_drug_price_logs (\n    id VARCHAR(36) PRIMARY KEY,\n    adjust_no VARCHAR(40) NOT NULL UNIQUE,\n    drug_id VARCHAR(36) NOT NULL,\n    drug_code VARCHAR(64) NOT NULL,\n    drug_name VARCHAR(200) NOT NULL,\n    old_retail_price DECIMAL(18,4) NOT NULL,\n    new_retail_price DECIMAL(18,4) NOT NULL,\n    old_cost_price DECIMAL(18,4) NOT NULL,\n    new_cost_price DECIMAL(18,4) NOT NULL,\n    affected_stock_quantity INT NOT NULL,\n    price_diff_amount DECIMAL(18,4) NOT NULL,\n    adjust_reason VARCHAR(500) NOT NULL,\n    effective_at_ms BIGINT NOT NULL,\n    operator_id VARCHAR(36),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);\n\n-- 50. inv_transfer_orders\nCREATE TABLE IF NOT EXISTS inv_transfer_orders (\n    id VARCHAR(36) PRIMARY KEY,\n    transfer_no VARCHAR(40) NOT NULL UNIQUE,\n    transfer_type VARCHAR(32) NOT NULL,\n    source_warehouse_id VARCHAR(36) NOT NULL,\n    target_warehouse_id VARCHAR(36) NOT NULL,\n    drug_id VARCHAR(36) NOT NULL,\n    drug_name_snapshot VARCHAR(200) NOT NULL,\n    batch_id VARCHAR(36) NOT NULL,\n    batch_no VARCHAR(80) NOT NULL,\n    expiry_date_int INT NOT NULL,\n    quantity INT NOT NULL,\n    unit_cost_price DECIMAL(18,4) NOT NULL,\n    unit_retail_price DECIMAL(18,4) NOT NULL,\n    total_retail_amount DECIMAL(18,4) NOT NULL,\n    status INT NOT NULL,\n    applicant_id VARCHAR(36),\n    dispatched_by_id VARCHAR(36),\n    dispatched_at_ms BIGINT,\n    received_by_id VARCHAR(36),\n    received_at_ms BIGINT,\n    remark VARCHAR(500),\n    ext_data_json VARCHAR(4000),\n    schema_version INT NOT NULL,\n    created_at_ms BIGINT NOT NULL,\n    created_by VARCHAR(36),\n    updated_at_ms BIGINT NOT NULL,\n    updated_by VARCHAR(36),\n    version_no INT NOT NULL\n);".Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		string[] array = statements;
		foreach (string stmt in array)
		{
			if (!string.IsNullOrWhiteSpace(stmt))
			{
				await conn.ExecuteAsync(stmt);
			}
		}
		await EnsureColumnAsync(conn, "opd_prescription_items", "is_deleted", "SMALLINT NOT NULL DEFAULT 0");
		await EnsureColumnAsync(conn, "opd_prescription_items", "deleted_at_ms", "BIGINT");
		await EnsureColumnAsync(conn, "opd_prescription_items", "deleted_by", "VARCHAR(36)");
		await EnsureColumnAsync(conn, "opd_prescriptions", "payment_order_id", "VARCHAR(36)");
		await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_opd_prescriptions_payment ON opd_prescriptions (payment_order_id)");
		await EnsureColumnAsync(conn, "fin_payment_orders", "prepayment_id", "VARCHAR(36)");
		await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_fin_payment_orders_prepayment ON fin_payment_orders (prepayment_id)");
		await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_fin_payment_orders_paid ON fin_payment_orders (paid_at_ms, created_by)");
		await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_fin_refund_orders_created ON fin_refund_orders (created_at_ms, created_by, original_payment_id)");
		await EnsureColumnAsync(conn, "fin_charge_ledgers", "refund_amount", "DECIMAL(18,4) NOT NULL DEFAULT 0");
		await conn.ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS ux_fin_daily_closings_cashier_date ON fin_daily_closings (cashier_user_id, closing_date_int)");
		await EnsureColumnAsync(conn, "cfg_field_definitions", "is_deleted", "SMALLINT NOT NULL DEFAULT 0");
		await EnsureColumnAsync(conn, "cfg_field_definitions", "deleted_at_ms", "BIGINT");
		await EnsureColumnAsync(conn, "cfg_field_definitions", "deleted_by", "VARCHAR(36)");
		await conn.ExecuteAsync("CREATE TABLE IF NOT EXISTS ops_audit_anchors (id VARCHAR(36) PRIMARY KEY, checkpoint_type VARCHAR(32) NOT NULL, last_event_id VARCHAR(36) NOT NULL, last_event_hash VARCHAR(64) NOT NULL, total_events_count INT NOT NULL, created_at_ms BIGINT NOT NULL, created_by VARCHAR(36), ext_data_json VARCHAR(4000), schema_version INT NOT NULL DEFAULT 1, updated_at_ms BIGINT NOT NULL DEFAULT 0, updated_by VARCHAR(36), version_no INT NOT NULL DEFAULT 1)");
		await conn.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS sys_refresh_tokens (
    id VARCHAR(36) PRIMARY KEY,
    user_id VARCHAR(36) NOT NULL,
    post_id VARCHAR(36) NOT NULL,
    token_hash VARCHAR(64) NOT NULL UNIQUE,
    session_version INT NOT NULL,
    expires_at_ms BIGINT NOT NULL,
    rotated_at_ms BIGINT,
    revoked_at_ms BIGINT,
    replaced_by_token_hash VARCHAR(64),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_sys_refresh_tokens_user ON sys_refresh_tokens (user_id, session_version);
CREATE INDEX IF NOT EXISTS ix_sys_refresh_tokens_expiry ON sys_refresh_tokens (expires_at_ms);");
		await EnsureInpatientFlowSchemaAsync(conn);
		await EnsurePharmacyDispenseSchemaAsync(conn);
		await conn.ExecuteAsync("CREATE TABLE IF NOT EXISTS opd_schedule_templates (id VARCHAR(36) PRIMARY KEY, campus_id VARCHAR(36) NOT NULL, opd_dept_id VARCHAR(36) NOT NULL, clinic_room_id VARCHAR(36), doctor_id VARCHAR(36) NOT NULL, template_name VARCHAR(128) NOT NULL, weekday_codes VARCHAR(20) NOT NULL, slot_period_code VARCHAR(32) NOT NULL, start_minute INT NOT NULL, end_minute INT NOT NULL, reg_level_code VARCHAR(32) NOT NULL, registration_fee DECIMAL(18,4) NOT NULL, total_capacity INT NOT NULL, is_active SMALLINT NOT NULL, ext_data_json VARCHAR(4000), schema_version INT NOT NULL, created_at_ms BIGINT NOT NULL, created_by VARCHAR(36), updated_at_ms BIGINT NOT NULL, updated_by VARCHAR(36), version_no INT NOT NULL)");
		await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_opd_schedule_slots_date_dept ON opd_schedule_slots (schedule_date_int, opd_dept_id)");
		await EnsureRegistrationColumnsAsync(conn);
		await EnsureSchemaExtensionsAsync(conn);
		await EnsureEmrLevel7SchemaAsync(conn);
		await EnsureWorkflowSchemaAsync(conn);
		await CleanUpDirtyDataAsync(conn);
	}

	private static async Task EnsureColumnAsync(DbConnection conn, string table, string column, string definition)
	{
		try
		{
			if (!(await conn.QueryAsync<TableColumnInfo>("PRAGMA table_info(" + table + ")")).Any((TableColumnInfo row) => string.Equals(row.Name, column, StringComparison.OrdinalIgnoreCase)))
			{
				await conn.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {column} {definition}");
			}
		}
		catch (Exception ex) when (ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
		{
			// Concurrently added by another thread/test, safe to ignore
		}
	}

	private static async Task EnsureRegistrationColumnsAsync(DbConnection conn)
	{
		await EnsureColumnAsync(conn, "opd_appointments", "payment_id", "VARCHAR(36)");
		await EnsureColumnAsync(conn, "opd_appointments", "paid_amount", "DECIMAL(18,4) NOT NULL DEFAULT 0");
		await EnsureColumnAsync(conn, "opd_appointments", "payment_channel", "VARCHAR(32)");
		await EnsureColumnAsync(conn, "opd_appointments", "paid_at_ms", "BIGINT");
		await EnsureColumnAsync(conn, "opd_appointments", "refund_id", "VARCHAR(36)");
		await EnsureColumnAsync(conn, "opd_appointments", "refund_reason", "VARCHAR(500)");
		await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_opd_appointments_run_status ON opd_appointments (schedule_id, status)");
		await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_fin_charge_ledgers_visit ON fin_charge_ledgers (visit_or_encounter_id)");
		await EnsureColumnAsync(conn, "fin_charge_ledgers", "payment_order_id", "VARCHAR(36)");
		await EnsureColumnAsync(conn, "fin_charge_ledgers", "source_service_request_id", "VARCHAR(36)");
		await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_fin_charge_ledgers_payment ON fin_charge_ledgers (payment_order_id)");
		await EnsureColumnAsync(conn, "inv_stock_ledgers", "payment_order_id", "VARCHAR(36)");
		await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_inv_stock_ledgers_payment ON inv_stock_ledgers (payment_order_id)");
                await EnsureColumnAsync(conn, "inv_stock_ledgers", "unit_cost_price", "DECIMAL(18,4)");
                await EnsureColumnAsync(conn, "inv_stock_ledgers", "unit_retail_price", "DECIMAL(18,4)");
		await conn.ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS ux_fin_charge_ledgers_source_service ON fin_charge_ledgers (source_service_request_id) WHERE source_service_request_id IS NOT NULL");
		await conn.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS ipd_discharge_requests (
    id VARCHAR(36) PRIMARY KEY,
    request_no VARCHAR(40) NOT NULL UNIQUE,
    encounter_id VARCHAR(36) NOT NULL UNIQUE,
    patient_id VARCHAR(36) NOT NULL,
    discharge_diagnosis VARCHAR(1000),
    discharge_instructions VARCHAR(2000),
    requested_at_ms BIGINT NOT NULL,
    settled_at_ms BIGINT,
    settled_by VARCHAR(36),
    status VARCHAR(20) NOT NULL,
    settlement_payment_id VARCHAR(36),
    settlement_total_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
    insurance_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
    prepayment_deduct DECIMAL(18,4) NOT NULL DEFAULT 0,
    self_pay_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
    refund_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL
);");
		await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_ipd_discharge_requests_status ON ipd_discharge_requests (status, requested_at_ms)");
		await EnsureColumnAsync(conn, "ipd_discharge_requests", "settlement_request_key", "VARCHAR(100)");
		await conn.ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS ux_ipd_discharge_requests_settlement_key ON ipd_discharge_requests (settlement_request_key) WHERE settlement_request_key IS NOT NULL");
	}

	private static async Task EnsureInpatientFlowSchemaAsync(DbConnection conn)
	{
		await EnsureColumnAsync(conn, "ipd_encounters", "triage_status", "INT NOT NULL DEFAULT 0");
		await EnsureColumnAsync(conn, "ipd_encounters", "accepted_at_ms", "BIGINT");
		await EnsureColumnAsync(conn, "ipd_encounters", "accepted_by", "VARCHAR(36)");
		await conn.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS ipd_admission_cards (
    id VARCHAR(36) PRIMARY KEY,
    card_no VARCHAR(40) NOT NULL UNIQUE,
    request_key VARCHAR(100) NOT NULL UNIQUE,
    patient_id VARCHAR(36) NOT NULL,
    visit_id VARCHAR(36),
    source_doctor_id VARCHAR(36) NOT NULL,
    source_dept_id VARCHAR(36) NOT NULL,
    suggested_ipd_dept_id VARCHAR(36) NOT NULL,
    suggested_ward_id VARCHAR(36),
    admission_diagnosis VARCHAR(500),
    contact_phone VARCHAR(32),
    remark VARCHAR(500),
    status VARCHAR(20) NOT NULL,
    encounter_id VARCHAR(36),
    registered_by VARCHAR(36),
    registered_at_ms BIGINT,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_ipd_admission_cards_patient_pending
    ON ipd_admission_cards (patient_id) WHERE status = 'PENDING';
CREATE INDEX IF NOT EXISTS ix_ipd_admission_cards_status_created
    ON ipd_admission_cards (status, created_at_ms);
CREATE TABLE IF NOT EXISTS ipd_prepayments (
    id VARCHAR(36) PRIMARY KEY,
    prepayment_no VARCHAR(40) NOT NULL UNIQUE,
    request_key VARCHAR(100) NOT NULL UNIQUE,
    encounter_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    amount DECIMAL(18,4) NOT NULL,
    balance_after DECIMAL(18,4) NOT NULL,
    payment_channel VARCHAR(32) NOT NULL,
    cashier_id VARCHAR(36) NOT NULL,
    paid_at_ms BIGINT NOT NULL,
    remark VARCHAR(500),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_ipd_prepayments_encounter_paid
    ON ipd_prepayments (encounter_id, paid_at_ms);
");
	}

	private static async Task EnsurePharmacyDispenseSchemaAsync(DbConnection conn)
	{
		await conn.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS phm_dispense_order_lines (
    id VARCHAR(36) PRIMARY KEY,
    dispense_order_id VARCHAR(36) NOT NULL,
    medical_order_id VARCHAR(36),
    drug_id VARCHAR(36) NOT NULL,
    drug_code VARCHAR(64) NOT NULL,
    drug_name VARCHAR(200) NOT NULL,
    quantity INT NOT NULL,
    unit_price DECIMAL(18,4) NOT NULL,
    unit_cost_price DECIMAL(18,4) NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_phm_dispense_order_lines_order
    ON phm_dispense_order_lines (dispense_order_id);
CREATE INDEX IF NOT EXISTS ix_phm_dispense_order_lines_drug
    ON phm_dispense_order_lines (drug_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_phm_dispense_order_lines_medical_order
    ON phm_dispense_order_lines (medical_order_id)
    WHERE medical_order_id IS NOT NULL;
");
	}

	private static async Task EnsureSchemaExtensionsAsync(DbConnection conn)
	{
		// P1-05: inv_drugs medical insurance fields
		await EnsureColumnAsync(conn, "inv_drugs", "national_ins_code", "VARCHAR(32)");
		await EnsureColumnAsync(conn, "inv_drugs", "reimburse_type", "VARCHAR(16) NOT NULL DEFAULT 'CLASS_A'");
		await EnsureColumnAsync(conn, "inv_drugs", "self_pay_ratio", "DECIMAL(5,4) NOT NULL DEFAULT 0");

		// P2-01: inv_drugs packaging and split fields
		await EnsureColumnAsync(conn, "inv_drugs", "min_dose", "DECIMAL(18,4) NOT NULL DEFAULT 1");
		await EnsureColumnAsync(conn, "inv_drugs", "min_dose_unit", "VARCHAR(32) NOT NULL DEFAULT 'mg'");
		await EnsureColumnAsync(conn, "inv_drugs", "base_unit_name", "VARCHAR(32) NOT NULL DEFAULT '片'");
		await EnsureColumnAsync(conn, "inv_drugs", "pack_factor", "INT NOT NULL DEFAULT 1");
		await EnsureColumnAsync(conn, "inv_drugs", "is_split_allowed_ipd", "SMALLINT NOT NULL DEFAULT 1");
		await EnsureColumnAsync(conn, "inv_drugs", "approval_no", "VARCHAR(64)");
		await EnsureColumnAsync(conn, "inv_drugs", "manufacturer", "VARCHAR(200)");

		// P1-06: sys_users login lockout & password fields
		await EnsureColumnAsync(conn, "sys_users", "failed_login_count", "INT NOT NULL DEFAULT 0");
		await EnsureColumnAsync(conn, "sys_users", "lockout_until_ms", "BIGINT");
		await EnsureColumnAsync(conn, "sys_users", "password_changed_at_ms", "BIGINT");
		await EnsureColumnAsync(conn, "sys_users", "must_change_password", "SMALLINT NOT NULL DEFAULT 0");

		// P2-01: ord_medical_orders extensions
		await EnsureColumnAsync(conn, "ord_medical_orders", "dose_unit", "VARCHAR(32) NOT NULL DEFAULT 'mg'");
		await EnsureColumnAsync(conn, "ord_medical_orders", "total_quantity", "INT NOT NULL DEFAULT 1");
		await EnsureColumnAsync(conn, "ord_medical_orders", "unit_price", "DECIMAL(18,4) NOT NULL DEFAULT 0");
		await EnsureColumnAsync(conn, "ord_medical_orders", "skin_test_flag", "INT NOT NULL DEFAULT 0");
		await EnsureColumnAsync(conn, "ord_medical_orders", "verify_at_ms", "BIGINT");
		await EnsureColumnAsync(conn, "ord_medical_orders", "stop_doctor_id", "VARCHAR(36)");
		await EnsureColumnAsync(conn, "ord_medical_orders", "stop_verify_nurse_id", "VARCHAR(36)");
		await EnsureColumnAsync(conn, "ord_medical_orders", "actual_stop_at_ms", "BIGINT");

		// P2-02: pat_patients reversible guardian ciphers and demographic fields
		await EnsureColumnAsync(conn, "pat_patients", "guardian_id_type", "VARCHAR(32)");
		await EnsureColumnAsync(conn, "pat_patients", "guardian_id_cipher", "VARCHAR(255)");
		await EnsureColumnAsync(conn, "pat_patients", "guardian_phone_cipher", "VARCHAR(255)");
		await EnsureColumnAsync(conn, "pat_patients", "guardian_relation_code", "VARCHAR(16)");
		await EnsureColumnAsync(conn, "pat_patients", "nation_code", "VARCHAR(16)");
		await EnsureColumnAsync(conn, "pat_patients", "marital_status_code", "VARCHAR(16)");
		await EnsureColumnAsync(conn, "pat_patients", "occupation_code", "VARCHAR(32)");
		await EnsureColumnAsync(conn, "pat_patients", "abo_blood_code", "VARCHAR(8)");
		await EnsureColumnAsync(conn, "pat_patients", "rh_blood_code", "VARCHAR(8)");
		await EnsureColumnAsync(conn, "pat_patients", "address_region_code", "VARCHAR(16)");
		await EnsureColumnAsync(conn, "pat_patients", "address_detail", "VARCHAR(500)");

		// P2-03: emr_documents nursing & resident sign fields
		await EnsureColumnAsync(conn, "emr_documents", "resident_doctor_id", "VARCHAR(36)");
		await EnsureColumnAsync(conn, "emr_documents", "primary_nurse_id", "VARCHAR(36)");
		await EnsureColumnAsync(conn, "emr_documents", "primary_nurse_signed_at_ms", "BIGINT");
		await EnsureColumnAsync(conn, "emr_documents", "head_nurse_id", "VARCHAR(36)");
		await EnsureColumnAsync(conn, "emr_documents", "head_nurse_signed_at_ms", "BIGINT");

		// P4-03: semantic control_type for emr_data_elements and cfg_field_definitions
		await EnsureColumnAsync(conn, "emr_data_elements", "control_type", "VARCHAR(32) NOT NULL DEFAULT 'SINGLE_TEXT'");
		await EnsureColumnAsync(conn, "cfg_field_definitions", "control_type", "VARCHAR(32) NOT NULL DEFAULT 'SINGLE_TEXT'");
		await conn.ExecuteAsync(@"
UPDATE emr_data_elements SET control_type = CASE
    WHEN ursa_control_type = 'u:TextArea' THEN 'MULTI_TEXT'
    WHEN ursa_control_type = 'u:NumericIntUpDown' THEN 'INT_INPUT'
    WHEN ursa_control_type = 'u:NumericDoubleUpDown' THEN 'DECIMAL_INPUT'
    WHEN ursa_control_type = 'ComboBox' THEN 'SELECT'
    WHEN ursa_control_type = 'DatePicker' THEN 'DATE'
    WHEN ursa_control_type = 'CheckBox' THEN 'CHECKBOX'
    ELSE 'SINGLE_TEXT'
END WHERE control_type IS NULL OR control_type = 'SINGLE_TEXT';
");

		// P2-04: New clinical and financial tables
		await conn.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS fin_inpatient_fee_items (
    id VARCHAR(36) PRIMARY KEY,
    encounter_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    fee_date_int INT NOT NULL,
    item_category VARCHAR(32) NOT NULL,
    item_code VARCHAR(64) NOT NULL,
    item_name VARCHAR(200) NOT NULL,
    spec VARCHAR(100),
    unit_name VARCHAR(32) NOT NULL,
    unit_price DECIMAL(18,4) NOT NULL,
    quantity DECIMAL(18,4) NOT NULL,
    total_amount DECIMAL(18,4) NOT NULL,
    self_pay_ratio DECIMAL(5,4) NOT NULL DEFAULT 0,
    reimburse_type VARCHAR(16) NOT NULL DEFAULT 'CLASS_A',
    charge_dept_id VARCHAR(36) NOT NULL,
    exec_dept_id VARCHAR(36) NOT NULL,
    order_id VARCHAR(36),
    is_settled SMALLINT NOT NULL DEFAULT 0,
    settle_payment_id VARCHAR(36),
    is_refunded SMALLINT NOT NULL DEFAULT 0,
    original_fee_item_id VARCHAR(36),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_fin_inpatient_fee_encounter_date ON fin_inpatient_fee_items (encounter_id, fee_date_int);
CREATE INDEX IF NOT EXISTS ix_fin_inpatient_fee_settle ON fin_inpatient_fee_items (is_settled, encounter_id);

CREATE TABLE IF NOT EXISTS inv_drug_trace_codes (
    id VARCHAR(36) PRIMARY KEY,
    trace_code VARCHAR(64) NOT NULL UNIQUE,
    national_ins_code VARCHAR(32) NOT NULL,
    drug_id VARCHAR(36) NOT NULL,
    batch_id VARCHAR(36) NOT NULL,
    batch_no VARCHAR(80) NOT NULL,
    warehouse_or_pharmacy_id VARCHAR(36) NOT NULL,
    stock_in_order_id VARCHAR(36),
    dispense_order_id VARCHAR(36),
    dispense_order_line_id VARCHAR(36),
    status VARCHAR(32) NOT NULL,
    is_recalled SMALLINT NOT NULL DEFAULT 0,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_inv_drug_trace_batch ON inv_drug_trace_codes (batch_id);
CREATE INDEX IF NOT EXISTS ix_inv_drug_trace_dispense ON inv_drug_trace_codes (dispense_order_line_id);

CREATE TABLE IF NOT EXISTS inv_stock_takes (
    id VARCHAR(36) PRIMARY KEY,
    stock_take_no VARCHAR(40) NOT NULL UNIQUE,
    warehouse_or_pharmacy_id VARCHAR(36) NOT NULL,
    dept_name VARCHAR(128) NOT NULL,
    stock_take_type VARCHAR(32) NOT NULL,
    status VARCHAR(32) NOT NULL,
    total_system_qty INT NOT NULL DEFAULT 0,
    total_actual_qty INT NOT NULL DEFAULT 0,
    total_diff_qty INT NOT NULL DEFAULT 0,
    total_diff_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
    created_by_user_id VARCHAR(36) NOT NULL,
    approved_by_user_id VARCHAR(36),
    approved_at_ms BIGINT,
    remark VARCHAR(500),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE TABLE IF NOT EXISTS inv_stock_take_lines (
    id VARCHAR(36) PRIMARY KEY,
    stock_take_id VARCHAR(36) NOT NULL,
    drug_id VARCHAR(36) NOT NULL,
    batch_id VARCHAR(36) NOT NULL,
    drug_code VARCHAR(64) NOT NULL,
    drug_name VARCHAR(200) NOT NULL,
    spec VARCHAR(100),
    unit_name VARCHAR(32),
    batch_no VARCHAR(80) NOT NULL,
    system_qty INT NOT NULL,
    actual_qty INT NOT NULL,
    diff_qty INT NOT NULL,
    unit_cost_price DECIMAL(18,4) NOT NULL,
    diff_cost_amount DECIMAL(18,4) NOT NULL,
    reason VARCHAR(255),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_inv_stock_take_lines_take ON inv_stock_take_lines (stock_take_id);

CREATE TABLE IF NOT EXISTS medtech_critical_values (
    id VARCHAR(36) PRIMARY KEY,
    report_id VARCHAR(36) NOT NULL,
    source_type VARCHAR(16) NOT NULL,
    visit_or_encounter_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    item_code VARCHAR(64) NOT NULL,
    item_name VARCHAR(200) NOT NULL,
    critical_value_text VARCHAR(1000) NOT NULL,
    published_by VARCHAR(36) NOT NULL,
    published_at_ms BIGINT NOT NULL,
    doc_acked_by VARCHAR(36),
    doc_acked_at_ms BIGINT,
    nurse_acked_by VARCHAR(36),
    nurse_acked_at_ms BIGINT,
    action_order_id VARCHAR(36),
    status VARCHAR(32) NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_medtech_critical_values_enc ON medtech_critical_values (visit_or_encounter_id, status);

CREATE TABLE IF NOT EXISTS opd_infectious_report_cards (
    id VARCHAR(36) PRIMARY KEY,
    report_no VARCHAR(40) NOT NULL UNIQUE,
    visit_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    icd_code VARCHAR(32) NOT NULL,
    disease_name VARCHAR(200) NOT NULL,
    disease_category VARCHAR(16) NOT NULL,
    diagnose_type VARCHAR(32) NOT NULL,
    onset_date_int INT NOT NULL,
    diagnosed_date_int INT NOT NULL,
    report_doctor_id VARCHAR(36) NOT NULL,
    reported_at_ms BIGINT NOT NULL,
    cdc_sync_status VARCHAR(32) NOT NULL DEFAULT 'PENDING',
    cdc_sync_at_ms BIGINT,
    status VARCHAR(32) NOT NULL DEFAULT 'REPORTED',
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_opd_infectious_report_visit ON opd_infectious_report_cards (visit_id);

CREATE TABLE IF NOT EXISTS emr_inpatient_front_sheets (
    id VARCHAR(36) PRIMARY KEY,
    encounter_id VARCHAR(36) NOT NULL UNIQUE,
    patient_id VARCHAR(36) NOT NULL,
    medical_record_no VARCHAR(32) NOT NULL,
    admission_times INT NOT NULL DEFAULT 1,
    pay_type_code VARCHAR(16) NOT NULL,
    health_card_no VARCHAR(64),
    admit_dept_id VARCHAR(36) NOT NULL,
    admit_ward_id VARCHAR(36) NOT NULL,
    admit_time_ms BIGINT NOT NULL,
    discharge_dept_id VARCHAR(36),
    discharge_ward_id VARCHAR(36),
    discharge_time_ms BIGINT,
    inpatient_days INT NOT NULL DEFAULT 0,
    outpatient_diag_code VARCHAR(32),
    outpatient_diag_name VARCHAR(200),
    primary_diag_code VARCHAR(32),
    primary_diag_name VARCHAR(200),
    primary_admission_condition VARCHAR(16),
    other_diags_json TEXT,
    surgeries_json TEXT,
    total_cost DECIMAL(18,4) NOT NULL DEFAULT 0,
    self_pay_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
    fee_breakdown_json TEXT,
    chief_physician_id VARCHAR(36),
    attending_physician_id VARCHAR(36),
    resident_physician_id VARCHAR(36),
    primary_nurse_id VARCHAR(36),
    quality_level VARCHAR(16),
    status VARCHAR(32) NOT NULL DEFAULT 'DRAFT',
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS ops_security_alerts (
    id VARCHAR(36) PRIMARY KEY,
    alert_no VARCHAR(40) NOT NULL UNIQUE,
    alert_type VARCHAR(64) NOT NULL,
    severity VARCHAR(16) NOT NULL,
    source_ip VARCHAR(64),
    user_id VARCHAR(36),
    operator_name VARCHAR(128),
    target_resource VARCHAR(128),
    description VARCHAR(1000) NOT NULL,
    threat_indicators_json TEXT,
    status VARCHAR(32) NOT NULL DEFAULT 'ACTIVE',
    resolved_by VARCHAR(36),
    resolved_at_ms BIGINT,
    resolution_note VARCHAR(1000),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_ops_security_alerts_status ON ops_security_alerts (status, severity);

CREATE TABLE IF NOT EXISTS ops_kms_keys (
    id VARCHAR(36) PRIMARY KEY,
    key_name VARCHAR(64) NOT NULL,
    algorithm VARCHAR(32) NOT NULL,
    version INT NOT NULL,
    state VARCHAR(32) NOT NULL DEFAULT 'ACTIVE',
    hsm_protected SMALLINT NOT NULL DEFAULT 1,
    key_fingerprint VARCHAR(64) NOT NULL,
    expires_at_ms BIGINT NOT NULL,
    rotated_at_ms BIGINT,
    description VARCHAR(255),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_ops_kms_keys_name ON ops_kms_keys (key_name, version);

-- Consumables & CSSD tables (P1-A)
CREATE TABLE IF NOT EXISTS csm_consumables (
    id VARCHAR(36) PRIMARY KEY,
    code VARCHAR(64) NOT NULL UNIQUE,
    name VARCHAR(200) NOT NULL,
    spec VARCHAR(100) NOT NULL,
    unit VARCHAR(32) NOT NULL,
    category VARCHAR(64) NOT NULL,
    is_sterile SMALLINT NOT NULL DEFAULT 0,
    is_high_value SMALLINT NOT NULL DEFAULT 0,
    price DECIMAL(18,4) NOT NULL DEFAULT 0,
    is_active SMALLINT NOT NULL DEFAULT 1,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_csm_consumables_code ON csm_consumables (code);

CREATE TABLE IF NOT EXISTS csm_stocks (
    id VARCHAR(36) PRIMARY KEY,
    consumable_id VARCHAR(36) NOT NULL,
    warehouse_id VARCHAR(36) NOT NULL,
    quantity INT NOT NULL DEFAULT 0,
    reserved_qty INT NOT NULL DEFAULT 0,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_csm_stocks_wh_item ON csm_stocks (consumable_id, warehouse_id);

CREATE TABLE IF NOT EXISTS csm_batches (
    id VARCHAR(36) PRIMARY KEY,
    consumable_id VARCHAR(36) NOT NULL,
    batch_no VARCHAR(80) NOT NULL,
    expiry_date VARCHAR(32) NOT NULL,
    manufacture_date VARCHAR(32),
    quantity INT NOT NULL DEFAULT 0,
    unit_cost DECIMAL(18,4) NOT NULL DEFAULT 0,
    is_frozen SMALLINT NOT NULL DEFAULT 0,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_csm_batches_item ON csm_batches (consumable_id, batch_no);

CREATE TABLE IF NOT EXISTS csm_stock_movements (
    id VARCHAR(36) PRIMARY KEY,
    consumable_id VARCHAR(36) NOT NULL,
    batch_id VARCHAR(36),
    from_warehouse_id VARCHAR(36),
    to_warehouse_id VARCHAR(36),
    movement_type VARCHAR(32) NOT NULL,
    quantity INT NOT NULL,
    unit_price DECIMAL(18,4) NOT NULL DEFAULT 0,
    reference_id VARCHAR(64),
    remark VARCHAR(500),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_csm_movements_item ON csm_stock_movements (consumable_id, created_at_ms);

CREATE TABLE IF NOT EXISTS csm_requisitions (
    id VARCHAR(36) PRIMARY KEY,
    requisition_no VARCHAR(64) NOT NULL UNIQUE,
    ward_id VARCHAR(36) NOT NULL,
    dept_id VARCHAR(36) NOT NULL,
    status VARCHAR(32) NOT NULL DEFAULT 'PENDING',
    requested_by VARCHAR(36) NOT NULL,
    approved_by VARCHAR(36),
    issued_by VARCHAR(36),
    remark VARCHAR(500),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_csm_requisitions_status ON csm_requisitions (status, ward_id);

CREATE TABLE IF NOT EXISTS csm_requisition_lines (
    id VARCHAR(36) PRIMARY KEY,
    requisition_id VARCHAR(36) NOT NULL,
    consumable_id VARCHAR(36) NOT NULL,
    quantity INT NOT NULL,
    issued_quantity INT NOT NULL DEFAULT 0,
    unit_price DECIMAL(18,4) NOT NULL DEFAULT 0,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_csm_req_lines_req ON csm_requisition_lines (requisition_id);

CREATE TABLE IF NOT EXISTS csm_sterile_packages (
    id VARCHAR(36) PRIMARY KEY,
    package_code VARCHAR(64) NOT NULL UNIQUE,
    package_name VARCHAR(200) NOT NULL,
    contents_json TEXT,
    sterilization_batch_no VARCHAR(80),
    status VARCHAR(32) NOT NULL DEFAULT 'CREATED',
    valid_until_ms BIGINT,
    current_location VARCHAR(64) NOT NULL DEFAULT 'CSSD',
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_csm_sterile_pkg_code ON csm_sterile_packages (package_code, status);

CREATE TABLE IF NOT EXISTS csm_sterilization_batches (
    id VARCHAR(36) PRIMARY KEY,
    batch_no VARCHAR(80) NOT NULL UNIQUE,
    method VARCHAR(64) NOT NULL,
    operator_id VARCHAR(36) NOT NULL,
    started_at_ms BIGINT NOT NULL,
    completed_at_ms BIGINT,
    result VARCHAR(32) NOT NULL DEFAULT 'QUALIFIED',
    remark VARCHAR(500),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS csm_package_movements (
    id VARCHAR(36) PRIMARY KEY,
    package_id VARCHAR(36) NOT NULL,
    from_location VARCHAR(64) NOT NULL,
    to_location VARCHAR(64) NOT NULL,
    movement_type VARCHAR(32) NOT NULL,
    operator_id VARCHAR(36) NOT NULL,
    remark VARCHAR(500),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS csm_trace_codes (
    id VARCHAR(36) PRIMARY KEY,
    consumable_id VARCHAR(36) NOT NULL,
    trace_code VARCHAR(80) NOT NULL UNIQUE,
    batch_no VARCHAR(80) NOT NULL,
    patient_id VARCHAR(36),
    encounter_id VARCHAR(36),
    status VARCHAR(32) NOT NULL DEFAULT 'IN_STOCK',
    used_at_ms BIGINT,
    used_by VARCHAR(36),
    recalled_at_ms BIGINT,
    recall_reason VARCHAR(500),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_csm_trace_codes_patient ON csm_trace_codes (patient_id, status);

-- P1-C: Surgery and Anesthesia Management
CREATE TABLE IF NOT EXISTS mte_surgery_schedules (
    id VARCHAR(36) PRIMARY KEY,
    schedule_no VARCHAR(40) NOT NULL UNIQUE,
    encounter_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    patient_name VARCHAR(128) NOT NULL,
    surgery_code VARCHAR(64) NOT NULL,
    surgery_name VARCHAR(200) NOT NULL,
    surgery_level INT NOT NULL DEFAULT 1,
    or_room_id VARCHAR(36) NOT NULL,
    table_seq INT NOT NULL DEFAULT 1,
    surgeon_id VARCHAR(36) NOT NULL,
    surgeon_name VARCHAR(128) NOT NULL,
    assistant_ids_json VARCHAR(1000),
    anesthesiologist_id VARCHAR(36) NOT NULL,
    anesthesiologist_name VARCHAR(128) NOT NULL,
    circulating_nurse_id VARCHAR(36),
    scrub_nurse_id VARCHAR(36),
    scheduled_start_ms BIGINT NOT NULL,
    estimated_duration_min INT NOT NULL DEFAULT 60,
    actual_start_ms BIGINT,
    actual_end_ms BIGINT,
    status INT NOT NULL DEFAULT 0,
    who_sign_in_ok SMALLINT NOT NULL DEFAULT 0,
    who_timeout_ok SMALLINT NOT NULL DEFAULT 0,
    who_sign_out_ok SMALLINT NOT NULL DEFAULT 0,
    instrument_count_balanced SMALLINT NOT NULL DEFAULT 0,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_mte_surgery_patient ON mte_surgery_schedules (patient_id, status);
CREATE INDEX IF NOT EXISTS ix_mte_surgery_room ON mte_surgery_schedules (or_room_id, scheduled_start_ms);

CREATE TABLE IF NOT EXISTS mte_anesthesia_records (
    id VARCHAR(36) PRIMARY KEY,
    record_no VARCHAR(40) NOT NULL UNIQUE,
    schedule_id VARCHAR(36) NOT NULL UNIQUE,
    encounter_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    asa_grade VARCHAR(16) NOT NULL,
    anesthesia_type VARCHAR(64) NOT NULL,
    preop_diagnosis VARCHAR(500),
    airway_assessment VARCHAR(500),
    anesthesiologist_id VARCHAR(36) NOT NULL,
    anesthesiologist_name VARCHAR(128) NOT NULL,
    anesthesia_start_ms BIGINT,
    anesthesia_end_ms BIGINT,
    blood_loss_ml DECIMAL(18,2) NOT NULL DEFAULT 0,
    urine_output_ml DECIMAL(18,2) NOT NULL DEFAULT 0,
    fluid_input_ml DECIMAL(18,2) NOT NULL DEFAULT 0,
    intraop_events_json TEXT,
    status INT NOT NULL DEFAULT 0,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_mte_anesthesia_patient ON mte_anesthesia_records (patient_id);

CREATE TABLE IF NOT EXISTS mte_anesthesia_medications (
    id VARCHAR(36) PRIMARY KEY,
    anesthesia_record_id VARCHAR(36) NOT NULL,
    drug_id VARCHAR(36) NOT NULL,
    drug_name VARCHAR(200) NOT NULL,
    dose DECIMAL(18,4) NOT NULL,
    dose_unit VARCHAR(32) NOT NULL,
    route VARCHAR(32) NOT NULL,
    given_at_ms BIGINT NOT NULL,
    operator_id VARCHAR(36) NOT NULL,
    remark VARCHAR(500),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_mte_anes_med_record ON mte_anesthesia_medications (anesthesia_record_id);

CREATE TABLE IF NOT EXISTS mte_recovery_records (
    id VARCHAR(36) PRIMARY KEY,
    anesthesia_record_id VARCHAR(36) NOT NULL UNIQUE,
    schedule_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    pacu_bed_no VARCHAR(32),
    entry_time_ms BIGINT NOT NULL,
    exit_time_ms BIGINT,
    steward_score INT NOT NULL DEFAULT 0,
    aldrete_score INT NOT NULL DEFAULT 0,
    consciousness VARCHAR(64),
    respiration VARCHAR(64),
    circulation VARCHAR(64),
    spo2 DECIMAL(5,2),
    discharge_criteria_met SMALLINT NOT NULL DEFAULT 0,
    evaluator_id VARCHAR(36) NOT NULL,
    remark VARCHAR(500),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS mte_postop_followups (
    id VARCHAR(36) PRIMARY KEY,
    anesthesia_record_id VARCHAR(36) NOT NULL,
    schedule_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    followup_day INT NOT NULL DEFAULT 1,
    pain_score INT NOT NULL DEFAULT 0,
    nausea_vomiting SMALLINT NOT NULL DEFAULT 0,
    consciousness_recovery SMALLINT NOT NULL DEFAULT 1,
    throat_pain SMALLINT NOT NULL DEFAULT 0,
    complications VARCHAR(500),
    followup_time_ms BIGINT NOT NULL,
    followup_by VARCHAR(36) NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_mte_postop_anes ON mte_postop_followups (anesthesia_record_id);

-- P1-D: Nursing Management
CREATE TABLE IF NOT EXISTS nur_care_plans (
    id VARCHAR(36) PRIMARY KEY,
    encounter_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    diagnosis_or_problem VARCHAR(255) NOT NULL,
    goal VARCHAR(500) NOT NULL,
    interventions_json TEXT,
    target_date_ms BIGINT,
    status INT NOT NULL DEFAULT 0,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_nur_care_plans_enc ON nur_care_plans (encounter_id, status);

CREATE TABLE IF NOT EXISTS nur_care_plan_evaluations (
    id VARCHAR(36) PRIMARY KEY,
    plan_id VARCHAR(36) NOT NULL,
    evaluation_result VARCHAR(1000) NOT NULL,
    is_goal_met SMALLINT NOT NULL DEFAULT 0,
    evaluated_at_ms BIGINT NOT NULL,
    evaluator_id VARCHAR(36) NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_nur_eval_plan ON nur_care_plan_evaluations (plan_id);

CREATE TABLE IF NOT EXISTS nur_pressure_ulcer_assessments (
    id VARCHAR(36) PRIMARY KEY,
    encounter_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    sensory_perception INT NOT NULL,
    moisture INT NOT NULL,
    activity INT NOT NULL,
    mobility INT NOT NULL,
    nutrition INT NOT NULL,
    friction_shear INT NOT NULL,
    total_score INT NOT NULL,
    risk_level VARCHAR(32) NOT NULL,
    preventive_measures_json TEXT,
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
CREATE INDEX IF NOT EXISTS ix_nur_braden_enc ON nur_pressure_ulcer_assessments (encounter_id);

CREATE TABLE IF NOT EXISTS nur_fall_assessments (
    id VARCHAR(36) PRIMARY KEY,
    encounter_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    history_of_falling INT NOT NULL,
    secondary_diagnosis INT NOT NULL,
    ambulatory_aid INT NOT NULL,
    iv_or_saline_lock INT NOT NULL,
    gait INT NOT NULL,
    mental_status INT NOT NULL,
    total_score INT NOT NULL,
    risk_level VARCHAR(32) NOT NULL,
    preventive_measures_json TEXT,
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
CREATE INDEX IF NOT EXISTS ix_nur_fall_enc ON nur_fall_assessments (encounter_id);

CREATE TABLE IF NOT EXISTS nur_quality_indicators (
    id VARCHAR(36) PRIMARY KEY,
    ward_id VARCHAR(36) NOT NULL,
    month_str VARCHAR(16) NOT NULL,
    patient_days INT NOT NULL DEFAULT 0,
    fall_count INT NOT NULL DEFAULT 0,
    pressure_ulcer_count INT NOT NULL DEFAULT 0,
    unplanned_extubation_count INT NOT NULL DEFAULT 0,
    cauti_count INT NOT NULL DEFAULT 0,
    clabsi_count INT NOT NULL DEFAULT 0,
    reporter_id VARCHAR(36) NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_nur_quality_ward_mo ON nur_quality_indicators (ward_id, month_str);

-- P1-E: Infection Control (IPC)
CREATE TABLE IF NOT EXISTS ipc_infection_reports (
    id VARCHAR(36) PRIMARY KEY,
    report_no VARCHAR(40) NOT NULL UNIQUE,
    encounter_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    patient_name VARCHAR(128) NOT NULL,
    ward_id VARCHAR(36) NOT NULL,
    infection_site VARCHAR(64) NOT NULL,
    pathogen VARCHAR(200),
    is_mdro SMALLINT NOT NULL DEFAULT 0,
    diagnosed_at_ms BIGINT NOT NULL,
    clinical_manifestation VARCHAR(1000),
    status INT NOT NULL DEFAULT 0,
    reporter_id VARCHAR(36) NOT NULL,
    reviewer_id VARCHAR(36),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_ipc_inf_enc ON ipc_infection_reports (encounter_id);
CREATE INDEX IF NOT EXISTS ix_ipc_inf_status ON ipc_infection_reports (status, is_mdro);

CREATE TABLE IF NOT EXISTS ipc_isolation_orders (
    id VARCHAR(36) PRIMARY KEY,
    order_no VARCHAR(40) NOT NULL UNIQUE,
    encounter_id VARCHAR(36) NOT NULL,
    patient_id VARCHAR(36) NOT NULL,
    ward_id VARCHAR(36) NOT NULL,
    isolation_type VARCHAR(64) NOT NULL,
    reason VARCHAR(500) NOT NULL,
    start_ms BIGINT NOT NULL,
    end_ms BIGINT,
    status INT NOT NULL DEFAULT 0,
    end_reason VARCHAR(500),
    prescribed_by VARCHAR(36) NOT NULL,
    ended_by VARCHAR(36),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_ipc_isolation_enc ON ipc_isolation_orders (encounter_id, status);

CREATE TABLE IF NOT EXISTS ipc_occupational_exposures (
    id VARCHAR(36) PRIMARY KEY,
    report_no VARCHAR(40) NOT NULL UNIQUE,
    staff_id VARCHAR(36) NOT NULL,
    staff_name VARCHAR(128) NOT NULL,
    dept_id VARCHAR(36) NOT NULL,
    exposure_type VARCHAR(64) NOT NULL,
    source_patient_id VARCHAR(36),
    source_pathogen VARCHAR(128),
    exposed_at_ms BIGINT NOT NULL,
    emergency_disposition VARCHAR(1000),
    pep_treatment VARCHAR(1000),
    status INT NOT NULL DEFAULT 0,
    reporter_id VARCHAR(36) NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_ipc_exposure_staff ON ipc_occupational_exposures (staff_id, status);

CREATE TABLE IF NOT EXISTS ipc_adverse_events (
    id VARCHAR(36) PRIMARY KEY,
    event_no VARCHAR(40) NOT NULL UNIQUE,
    reporter_id VARCHAR(36) NOT NULL,
    reporter_name VARCHAR(128) NOT NULL,
    dept_id VARCHAR(36) NOT NULL,
    encounter_id VARCHAR(36),
    patient_id VARCHAR(36),
    event_category VARCHAR(64) NOT NULL,
    severity_level VARCHAR(32) NOT NULL,
    description VARCHAR(2000) NOT NULL,
    immediate_action VARCHAR(1000),
    status INT NOT NULL DEFAULT 0,
    closure_comment VARCHAR(1000),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_ipc_adverse_dept ON ipc_adverse_events (dept_id, status);

CREATE TABLE IF NOT EXISTS ipc_hand_hygiene_audits (
    id VARCHAR(36) PRIMARY KEY,
    ward_id VARCHAR(36) NOT NULL,
    observer_id VARCHAR(36) NOT NULL,
    audit_date VARCHAR(16) NOT NULL,
    moment_type VARCHAR(64) NOT NULL,
    staff_category VARCHAR(64) NOT NULL,
    opportunity_count INT NOT NULL,
    performed_count INT NOT NULL,
    correct_count INT NOT NULL,
    compliance_rate DECIMAL(5,2) NOT NULL DEFAULT 0,
    accuracy_rate DECIMAL(5,2) NOT NULL DEFAULT 0,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_ipc_hand_ward ON ipc_hand_hygiene_audits (ward_id, audit_date);

-- P2-A: Patient Services
CREATE TABLE IF NOT EXISTS srv_satisfaction_surveys (
    id VARCHAR(36) PRIMARY KEY,
    encounter_id VARCHAR(36),
    visit_id VARCHAR(36),
    patient_id VARCHAR(36) NOT NULL,
    patient_name VARCHAR(128) NOT NULL,
    dept_id VARCHAR(36) NOT NULL,
    score INT NOT NULL DEFAULT 5,
    medical_skill_score INT NOT NULL DEFAULT 5,
    service_attitude_score INT NOT NULL DEFAULT 5,
    environment_score INT NOT NULL DEFAULT 5,
    comment VARCHAR(1000),
    submitted_at_ms BIGINT NOT NULL,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_srv_sat_dept ON srv_satisfaction_surveys (dept_id, submitted_at_ms);

CREATE TABLE IF NOT EXISTS srv_complaints (
    id VARCHAR(36) PRIMARY KEY,
    complaint_no VARCHAR(40) NOT NULL UNIQUE,
    patient_id VARCHAR(36) NOT NULL,
    patient_name VARCHAR(128) NOT NULL,
    patient_phone VARCHAR(32),
    dept_id VARCHAR(36) NOT NULL,
    complaint_type VARCHAR(64) NOT NULL,
    description VARCHAR(2000) NOT NULL,
    status INT NOT NULL DEFAULT 0,
    handler_id VARCHAR(36),
    handler_name VARCHAR(128),
    handling_result VARCHAR(2000),
    patient_satisfaction VARCHAR(32),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_srv_comp_status ON srv_complaints (status, dept_id);

CREATE TABLE IF NOT EXISTS srv_informed_consents (
    id VARCHAR(36) PRIMARY KEY,
    consent_no VARCHAR(40) NOT NULL UNIQUE,
    patient_id VARCHAR(36) NOT NULL,
    encounter_id VARCHAR(36),
    consent_type VARCHAR(64) NOT NULL,
    title VARCHAR(200) NOT NULL,
    document_content_summary VARCHAR(1000),
    signer_type VARCHAR(32) NOT NULL DEFAULT 'PATIENT',
    signer_name VARCHAR(128) NOT NULL,
    signed_at_ms BIGINT NOT NULL,
    witness_doctor_id VARCHAR(36) NOT NULL,
    signature_hash VARCHAR(128) NOT NULL,
    status INT NOT NULL DEFAULT 1,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_srv_consent_pat ON srv_informed_consents (patient_id, consent_type);

-- P2-B: Management Support (Cost Accounting & Equipment)
CREATE TABLE IF NOT EXISTS fin_cost_centers (
    id VARCHAR(36) PRIMARY KEY,
    dept_id VARCHAR(36) NOT NULL,
    period VARCHAR(16) NOT NULL,
    direct_cost DECIMAL(18,4) NOT NULL DEFAULT 0,
    allocated_cost DECIMAL(18,4) NOT NULL DEFAULT 0,
    total_cost DECIMAL(18,4) NOT NULL DEFAULT 0,
    revenue DECIMAL(18,4) NOT NULL DEFAULT 0,
    profit_margin DECIMAL(8,4) NOT NULL DEFAULT 0,
    allocation_method VARCHAR(64) NOT NULL DEFAULT 'STEP_DOWN',
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_fin_cost_dept_per ON fin_cost_centers (dept_id, period);

CREATE TABLE IF NOT EXISTS eqp_equipments (
    id VARCHAR(36) PRIMARY KEY,
    code VARCHAR(64) NOT NULL UNIQUE,
    name VARCHAR(200) NOT NULL,
    model_spec VARCHAR(100),
    dept_id VARCHAR(36) NOT NULL,
    purchase_date VARCHAR(16),
    price DECIMAL(18,4) NOT NULL DEFAULT 0,
    warranty_end VARCHAR(16),
    is_mandatory_inspection SMALLINT NOT NULL DEFAULT 0,
    next_inspection_date VARCHAR(16),
    status VARCHAR(32) NOT NULL DEFAULT 'NORMAL',
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_eqp_code_status ON eqp_equipments (code, status);

CREATE TABLE IF NOT EXISTS eqp_maintenance_records (
    id VARCHAR(36) PRIMARY KEY,
    equipment_id VARCHAR(36) NOT NULL,
    type VARCHAR(32) NOT NULL,
    maintenance_date VARCHAR(16) NOT NULL,
    cost DECIMAL(18,4) NOT NULL DEFAULT 0,
    vendor VARCHAR(200),
    engineer_name VARCHAR(128),
    result VARCHAR(32) NOT NULL DEFAULT 'QUALIFIED',
    remark VARCHAR(500),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_eqp_maint_eqp ON eqp_maintenance_records (equipment_id);

-- Custom SQL Reports & Parameterized Dynamic Queries
CREATE TABLE IF NOT EXISTS ops_report_definitions (
    id VARCHAR(36) PRIMARY KEY,
    report_code VARCHAR(64) NOT NULL UNIQUE,
    report_name VARCHAR(200) NOT NULL,
    category VARCHAR(64) NOT NULL DEFAULT '综合',
    description VARCHAR(1000),
    sql_template TEXT NOT NULL,
    parameters_schema_json TEXT,
    display_columns_json TEXT,
    is_published SMALLINT NOT NULL DEFAULT 1,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_ops_report_definitions_code ON ops_report_definitions (report_code);
CREATE INDEX IF NOT EXISTS ix_ops_report_definitions_cat ON ops_report_definitions (category);

CREATE TABLE IF NOT EXISTS ops_report_executions (
    id VARCHAR(36) PRIMARY KEY,
    report_definition_id VARCHAR(36) NOT NULL,
    report_code VARCHAR(64) NOT NULL,
    report_name VARCHAR(200) NOT NULL,
    parameters_json TEXT,
    result_row_count INT NOT NULL DEFAULT 0,
    execution_duration_ms BIGINT NOT NULL DEFAULT 0,
    executed_by VARCHAR(36),
    executed_by_name VARCHAR(128),
    executed_at_ms BIGINT NOT NULL,
    error_message TEXT,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_ops_report_executions_report ON ops_report_executions (report_definition_id, executed_at_ms);

-- Highly Structured EMR & Nursing User Custom Templates
CREATE TABLE IF NOT EXISTS emr_user_templates (
    id VARCHAR(36) PRIMARY KEY,
    user_id VARCHAR(36) NOT NULL,
    user_name VARCHAR(128) NOT NULL,
    dept_id VARCHAR(36),
    dept_name VARCHAR(128),
    base_template_id VARCHAR(36) NOT NULL,
    template_code VARCHAR(64) NOT NULL,
    template_name VARCHAR(128) NOT NULL,
    emr_scene VARCHAR(16) NOT NULL,
    doc_type_code VARCHAR(64) NOT NULL,
    default_content_json TEXT NOT NULL,
    is_shared_dept SMALLINT NOT NULL DEFAULT 0,
    sort_order INT NOT NULL DEFAULT 0,
    is_active SMALLINT NOT NULL DEFAULT 1,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_emr_user_templates_user ON emr_user_templates (user_id, emr_scene);
CREATE INDEX IF NOT EXISTS ix_emr_user_templates_dept ON emr_user_templates (dept_id, emr_scene);
");
	}

	private static async Task CleanUpDirtyDataAsync(DbConnection conn)
	{
		// P0-03: Clean dirty data in opd_prescriptions and opd_prescription_items
		await conn.ExecuteAsync(@"
UPDATE opd_prescriptions 
SET visit_id = 'VISIT-OPD-20261105001' 
WHERE visit_id = 'OPD20261105001' AND id = '31a78448-614a-470c-993a-34787d01b7bb';

UPDATE opd_prescription_items SET drug_id = 'DRUG-01' WHERE drug_id = 'DRUG-ANTI-001';
UPDATE opd_prescription_items SET drug_id = 'DRUG-02' WHERE drug_id = 'DRUG-LOCAL';
UPDATE opd_prescription_items SET drug_id = 'DRUG-05' WHERE drug_id = 'DRUG-CARD-005';

UPDATE opd_prescription_items 
SET is_deleted = 1, deleted_at_ms = 1731312000000, deleted_by = 'SYSTEM_CLEANUP' 
WHERE id = 'ed6eb32f-0fea-44f4-9a8d-95e1c194fcc9' 
   OR ext_data_json LIKE '%❌ 皮试阳性 (强阻断)%';
");
	}
}
