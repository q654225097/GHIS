using Dapper;

namespace HIS.BuildingBlocks.Database;

public static class DbSchemaInitializer
{
    public const string DdlScript = """
    -- 1. emr_data_elements
    CREATE TABLE IF NOT EXISTS emr_data_elements (
        id VARCHAR(36) PRIMARY KEY,
        element_code VARCHAR(64) NOT NULL UNIQUE,
        nhc_std_code VARCHAR(64),
        element_name VARCHAR(128) NOT NULL,
        data_type VARCHAR(32) NOT NULL,
        ursa_control_type VARCHAR(64) NOT NULL,
        dict_code VARCHAR(64),
        unit_text VARCHAR(32),
        macro_source VARCHAR(128),
        gender_exclusive VARCHAR(16) NOT NULL,
        validation_rule_json VARCHAR(2000),
        control_key VARCHAR(128),
        is_indexed SMALLINT NOT NULL,
        is_active SMALLINT NOT NULL,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 2. emr_ofd_layout_schemas
    CREATE TABLE IF NOT EXISTS emr_ofd_layout_schemas (
        id VARCHAR(36) PRIMARY KEY,
        layout_code VARCHAR(64) NOT NULL UNIQUE,
        layout_name VARCHAR(128) NOT NULL,
        page_size_code VARCHAR(16) NOT NULL,
        page_width_mm DECIMAL(18,4) NOT NULL,
        page_height_mm DECIMAL(18,4) NOT NULL,
        margin_top_mm DECIMAL(18,4) NOT NULL,
        margin_bottom_mm DECIMAL(18,4) NOT NULL,
        margin_left_mm DECIMAL(18,4) NOT NULL,
        margin_right_mm DECIMAL(18,4) NOT NULL,
        is_continuous_flow SMALLINT NOT NULL,
        header_layout_json TEXT,
        footer_layout_json TEXT,
        sign_slots_json VARCHAR(2000),
        watermark_policy VARCHAR(32),
        is_active SMALLINT NOT NULL,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 3. emr_doc_templates
    CREATE TABLE IF NOT EXISTS emr_doc_templates (
        id VARCHAR(36) PRIMARY KEY,
        template_code VARCHAR(64) NOT NULL,
        template_version INT NOT NULL,
        template_name VARCHAR(128) NOT NULL,
        emr_scene VARCHAR(16) NOT NULL,
        doc_type_code VARCHAR(64) NOT NULL,
        dept_category VARCHAR(32),
        target_dept_id VARCHAR(36),
        disease_icd_prefix VARCHAR(32),
        time_limit_hours INT NOT NULL,
        required_sign_levels INT NOT NULL,
        ofd_layout_id VARCHAR(36),
        is_published SMALLINT NOT NULL,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 4. emr_section_schemas
    CREATE TABLE IF NOT EXISTS emr_section_schemas (
        id VARCHAR(36) PRIMARY KEY,
        template_id VARCHAR(36) NOT NULL,
        section_code VARCHAR(64) NOT NULL,
        section_title VARCHAR(128) NOT NULL,
        render_mode VARCHAR(32) NOT NULL,
        content_template_json TEXT,
        section_control_key VARCHAR(128),
        is_required SMALLINT NOT NULL,
        visibility_expr VARCHAR(500),
        sort_order INT NOT NULL,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 5. emr_documents
    CREATE TABLE IF NOT EXISTS emr_documents (
        id VARCHAR(36) PRIMARY KEY,
        doc_no VARCHAR(40) NOT NULL UNIQUE,
        request_key VARCHAR(100) UNIQUE,
        campus_id VARCHAR(36),
        emr_scene VARCHAR(16) NOT NULL,
        visit_or_encounter_id VARCHAR(36) NOT NULL,
        patient_id VARCHAR(36) NOT NULL,
        dept_id VARCHAR(36) NOT NULL,
        ward_id VARCHAR(36),
        template_id VARCHAR(36) NOT NULL,
        template_version INT NOT NULL,
        doc_type_code VARCHAR(64) NOT NULL,
        doc_title VARCHAR(200) NOT NULL,
        clinical_time_ms BIGINT NOT NULL,
        structured_ast_json TEXT,
        plain_text_summary TEXT,
        track_changes_json TEXT,
        status INT NOT NULL,
        author_id VARCHAR(36) NOT NULL,
        resident_signed_at_ms BIGINT,
        attending_doctor_id VARCHAR(36),
        attending_signed_at_ms BIGINT,
        chief_doctor_id VARCHAR(36),
        chief_signed_at_ms BIGINT,
        patient_sign_svg TEXT,
        latest_ofd_archive_id VARCHAR(36),
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 6. emr_document_elements
    CREATE TABLE IF NOT EXISTS emr_document_elements (
        id VARCHAR(36) PRIMARY KEY,
        document_id VARCHAR(36) NOT NULL,
        patient_id VARCHAR(36) NOT NULL,
        emr_scene VARCHAR(16) NOT NULL,
        doc_type_code VARCHAR(64) NOT NULL,
        section_code VARCHAR(64) NOT NULL,
        element_code VARCHAR(64) NOT NULL,
        nhc_std_code VARCHAR(64),
        val_string VARCHAR(1000),
        val_int BIGINT,
        val_decimal DECIMAL(18,4),
        val_time_ms BIGINT,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 7. emr_ofd_archives
    CREATE TABLE IF NOT EXISTS emr_ofd_archives (
        id VARCHAR(36) PRIMARY KEY,
        ofd_no VARCHAR(40) NOT NULL UNIQUE,
        archive_scope VARCHAR(32) NOT NULL,
        visit_or_encounter_id VARCHAR(36) NOT NULL,
        document_id VARCHAR(36),
        patient_id VARCHAR(36) NOT NULL,
        ofd_version_standard VARCHAR(32) NOT NULL,
        page_count INT NOT NULL,
        file_storage_key VARCHAR(255) NOT NULL,
        file_size_bytes BIGINT NOT NULL,
        content_sha256 VARCHAR(64) NOT NULL,
        sm3_digest VARCHAR(64) NOT NULL,
        ca_sign_serial_no VARCHAR(128),
        tsa_timestamp_token VARCHAR(500),
        has_embedded_semantic_xml SMALLINT NOT NULL,
        archive_status INT NOT NULL,
        generated_at_ms BIGINT NOT NULL,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 8. sys_departments
    CREATE TABLE IF NOT EXISTS sys_departments (
        id VARCHAR(36) PRIMARY KEY,
        campus_id VARCHAR(36) NOT NULL,
        dept_code VARCHAR(64) NOT NULL UNIQUE,
        dept_name VARCHAR(128) NOT NULL,
        pinyin_code VARCHAR(64),
        dept_category VARCHAR(32) NOT NULL,
        sub_specialty_type VARCHAR(32),
        parent_admin_id VARCHAR(36),
        gender_limit VARCHAR(16),
        min_age_days INT,
        max_age_years INT,
        default_pharmacy_id VARCHAR(36),
        rated_bed_count INT,
        location_address VARCHAR(255),
        is_allow_appointment SMALLINT NOT NULL,
        is_active SMALLINT NOT NULL,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 9. sys_dept_ward_relations
    CREATE TABLE IF NOT EXISTS sys_dept_ward_relations (
        id VARCHAR(36) PRIMARY KEY,
        ipd_dept_id VARCHAR(36) NOT NULL,
        ward_dept_id VARCHAR(36) NOT NULL,
        relation_type VARCHAR(32) NOT NULL,
        max_borrow_beds INT NOT NULL,
        priority_order INT NOT NULL,
        is_active SMALLINT NOT NULL,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 10. sys_medtech_exec_routings
    CREATE TABLE IF NOT EXISTS sys_medtech_exec_routings (
        id VARCHAR(36) PRIMARY KEY,
        campus_id VARCHAR(36) NOT NULL,
        source_scene VARCHAR(32) NOT NULL,
        order_dept_id VARCHAR(36),
        service_item_code VARCHAR(64) NOT NULL,
        exec_med_tech_dept_id VARCHAR(36) NOT NULL,
        exec_room_code VARCHAR(64),
        time_window_type VARCHAR(32) NOT NULL,
        priority_weight INT NOT NULL,
        is_active SMALLINT NOT NULL,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 11. sys_users
    CREATE TABLE IF NOT EXISTS sys_users (
        id VARCHAR(36) PRIMARY KEY,
        user_name VARCHAR(64) NOT NULL UNIQUE,
        display_name VARCHAR(128) NOT NULL,
        employee_no VARCHAR(64) NOT NULL,
        password_hash VARCHAR(255) NOT NULL,
        bound_patient_id VARCHAR(36),
        prescribe_level INT NOT NULL,
        surgery_level INT NOT NULL,
        emr_sign_level INT NOT NULL,
        ca_cert_id VARCHAR(128),
        session_version INT NOT NULL,
        is_active SMALLINT NOT NULL,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 12. sys_user_dept_posts
    CREATE TABLE IF NOT EXISTS sys_user_dept_posts (
        id VARCHAR(36) PRIMARY KEY,
        user_id VARCHAR(36) NOT NULL,
        dept_id VARCHAR(36) NOT NULL,
        dept_category VARCHAR(32) NOT NULL,
        med_group_id VARCHAR(36),
        role_id VARCHAR(36) NOT NULL,
        is_default_context SMALLINT NOT NULL,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 13. sys_roles
    CREATE TABLE IF NOT EXISTS sys_roles (
        id VARCHAR(36) PRIMARY KEY,
        role_code VARCHAR(64) NOT NULL UNIQUE,
        role_name VARCHAR(128) NOT NULL,
        data_scope VARCHAR(32) NOT NULL,
        is_active SMALLINT NOT NULL,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 14. sys_permission_resources
    CREATE TABLE IF NOT EXISTS sys_permission_resources (
        id VARCHAR(36) PRIMARY KEY,
        resource_key VARCHAR(128) NOT NULL UNIQUE,
        parent_key VARCHAR(128),
        resource_type VARCHAR(32) NOT NULL,
        resource_name VARCHAR(128) NOT NULL,
        ursa_control_type VARCHAR(64),
        api_method_path VARCHAR(255),
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 15. sys_role_permissions
    CREATE TABLE IF NOT EXISTS sys_role_permissions (
        id VARCHAR(36) PRIMARY KEY,
        role_id VARCHAR(36) NOT NULL,
        resource_key VARCHAR(128) NOT NULL,
        access_policy INT NOT NULL,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 16. cfg_form_schemas
    CREATE TABLE IF NOT EXISTS cfg_form_schemas (
        id VARCHAR(36) PRIMARY KEY,
        form_code VARCHAR(64) NOT NULL UNIQUE,
        form_name VARCHAR(128) NOT NULL,
        entity_type VARCHAR(64) NOT NULL,
        dept_category VARCHAR(32),
        target_dept_id VARCHAR(36),
        grid_columns INT NOT NULL,
        is_published SMALLINT NOT NULL,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 17. cfg_field_definitions
    CREATE TABLE IF NOT EXISTS cfg_field_definitions (
        id VARCHAR(36) PRIMARY KEY,
        form_schema_id VARCHAR(36) NOT NULL,
        field_code VARCHAR(64) NOT NULL,
        field_label VARCHAR(128) NOT NULL,
        data_type VARCHAR(32) NOT NULL,
        ursa_control_type VARCHAR(64) NOT NULL,
        control_key VARCHAR(128),
        dict_code VARCHAR(64),
        unit_text VARCHAR(32),
        validation_rule_json VARCHAR(2000),
        visibility_expr VARCHAR(500),
        is_indexed SMALLINT NOT NULL,
        sort_order INT NOT NULL,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 18. cfg_entity_field_values
    CREATE TABLE IF NOT EXISTS cfg_entity_field_values (
        id VARCHAR(36) PRIMARY KEY,
        entity_type VARCHAR(64) NOT NULL,
        entity_id VARCHAR(36) NOT NULL,
        field_code VARCHAR(64) NOT NULL,
        val_string VARCHAR(500),
        val_int BIGINT,
        val_decimal DECIMAL(18,4),
        val_time_ms BIGINT,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 19. pat_patients
    CREATE TABLE IF NOT EXISTS pat_patients (
        id VARCHAR(36) PRIMARY KEY,
        medical_record_no VARCHAR(32) NOT NULL UNIQUE,
        full_name VARCHAR(128) NOT NULL,
        pinyin_code VARCHAR(64),
        gender_code VARCHAR(16) NOT NULL,
        birth_date_int INT NOT NULL,
        id_card_type VARCHAR(32) NOT NULL,
        id_card_cipher VARCHAR(255),
        id_card_hash VARCHAR(64) NOT NULL,
        phone_cipher VARCHAR(255),
        phone_masked VARCHAR(32),
        guardian_name VARCHAR(128),
        guardian_id_hash VARCHAR(64),
        allergy_tags_json VARCHAR(2000),
        is_temp_emergency SMALLINT NOT NULL,
        merged_to_patient_id VARCHAR(36),
        is_active SMALLINT NOT NULL,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 20. opd_schedule_slots
    CREATE TABLE IF NOT EXISTS opd_schedule_slots (
        id VARCHAR(36) PRIMARY KEY,
        campus_id VARCHAR(36) NOT NULL,
        opd_dept_id VARCHAR(36) NOT NULL,
        clinic_room_id VARCHAR(36),
        doctor_id VARCHAR(36) NOT NULL,
        substitute_doctor_id VARCHAR(36),
        schedule_date_int INT NOT NULL,
        slot_period_code VARCHAR(32) NOT NULL,
        starts_at_ms BIGINT NOT NULL,
        ends_at_ms BIGINT NOT NULL,
        reg_level_code VARCHAR(32) NOT NULL,
        registration_fee DECIMAL(18,4) NOT NULL,
        total_capacity INT NOT NULL,
        reserved_count INT NOT NULL,
        slot_status INT NOT NULL,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 21. opd_appointments
    CREATE TABLE IF NOT EXISTS opd_appointments (
        id VARCHAR(36) PRIMARY KEY,
        appointment_no VARCHAR(40) NOT NULL UNIQUE,
        request_key VARCHAR(100) UNIQUE,
        patient_id VARCHAR(36) NOT NULL,
        schedule_id VARCHAR(36) NOT NULL,
        active_unique_token VARCHAR(36) NOT NULL,
        queue_no INT NOT NULL,
        status INT NOT NULL,
        lock_expires_at_ms BIGINT NOT NULL,
        visit_id VARCHAR(36),
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 22. opd_visits
    CREATE TABLE IF NOT EXISTS opd_visits (
        id VARCHAR(36) PRIMARY KEY,
        visit_no VARCHAR(40) NOT NULL UNIQUE,
        request_key VARCHAR(100) UNIQUE,
        campus_id VARCHAR(36) NOT NULL,
        opd_dept_id VARCHAR(36) NOT NULL,
        clinic_room_id VARCHAR(36),
        patient_id VARCHAR(36) NOT NULL,
        schedule_id VARCHAR(36),
        doctor_id VARCHAR(36) NOT NULL,
        is_emergency SMALLINT NOT NULL,
        er_triage_level INT,
        is_green_channel SMALLINT NOT NULL,
        green_credit_limit DECIMAL(18,4),
        is_return_visit SMALLINT NOT NULL,
        status INT NOT NULL,
        primary_diag_code VARCHAR(32),
        primary_diag_name VARCHAR(255),
        emr_document_id VARCHAR(36),
        ofd_archive_id VARCHAR(36),
        signed_at_ms BIGINT,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 23. opd_triage_queues
    CREATE TABLE IF NOT EXISTS opd_triage_queues (
        id VARCHAR(36) PRIMARY KEY,
        visit_id VARCHAR(36) NOT NULL UNIQUE,
        opd_dept_id VARCHAR(36) NOT NULL,
        clinic_room_id VARCHAR(36),
        priority_score INT NOT NULL,
        pass_count INT NOT NULL,
        queue_status INT NOT NULL,
        checkin_at_ms BIGINT NOT NULL,
        called_at_ms BIGINT,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 24. opd_prescriptions
    CREATE TABLE IF NOT EXISTS opd_prescriptions (
        id VARCHAR(36) PRIMARY KEY,
        prescription_no VARCHAR(40) NOT NULL UNIQUE,
        visit_id VARCHAR(36) NOT NULL,
        patient_id VARCHAR(36) NOT NULL,
        opd_dept_id VARCHAR(36) NOT NULL,
        doctor_id VARCHAR(36) NOT NULL,
        target_pharmacy_id VARCHAR(36) NOT NULL,
        rx_type VARCHAR(32) NOT NULL,
        herb_doses INT,
        total_amount DECIMAL(18,4) NOT NULL,
        status INT NOT NULL,
        reviewer_pharmacist_id VARCHAR(36),
        review_remark VARCHAR(500),
        rx_ofd_archive_id VARCHAR(36),
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 25. opd_prescription_items
    CREATE TABLE IF NOT EXISTS opd_prescription_items (
        id VARCHAR(36) PRIMARY KEY,
        prescription_id VARCHAR(36) NOT NULL,
        group_no INT NOT NULL,
        drug_id VARCHAR(36) NOT NULL,
        drug_name_snapshot VARCHAR(200) NOT NULL,
        dose_once DECIMAL(18,4) NOT NULL,
        dose_unit VARCHAR(32) NOT NULL,
        frequency_code VARCHAR(32) NOT NULL,
        route_code VARCHAR(32) NOT NULL,
        days INT NOT NULL,
        total_quantity INT NOT NULL,
        unit_price DECIMAL(18,4) NOT NULL,
        skin_test_flag INT NOT NULL,
        skin_test_batch_no VARCHAR(80),
        skin_test_checker_id VARCHAR(36),
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 26. ipd_beds
    CREATE TABLE IF NOT EXISTS ipd_beds (
        id VARCHAR(36) PRIMARY KEY,
        campus_id VARCHAR(36) NOT NULL,
        ward_id VARCHAR(36) NOT NULL,
        room_code VARCHAR(40) NOT NULL,
        bed_code VARCHAR(40) NOT NULL,
        bed_type VARCHAR(32) NOT NULL,
        daily_fee DECIMAL(18,4) NOT NULL,
        bed_status INT NOT NULL,
        current_encounter_id VARCHAR(36),
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 27. ipd_encounters
    CREATE TABLE IF NOT EXISTS ipd_encounters (
        id VARCHAR(36) PRIMARY KEY,
        admission_no VARCHAR(40) NOT NULL UNIQUE,
        patient_id VARCHAR(36) NOT NULL,
        campus_id VARCHAR(36) NOT NULL,
        admit_dept_id VARCHAR(36) NOT NULL,
        med_group_id VARCHAR(36),
        admit_ward_id VARCHAR(36) NOT NULL,
        is_borrowed_bed SMALLINT NOT NULL,
        bed_code VARCHAR(40),
        active_bed_token VARCHAR(36) NOT NULL,
        attending_doctor_id VARCHAR(36),
        primary_nurse_id VARCHAR(36),
        nursing_level_code VARCHAR(32),
        prepayment_balance DECIMAL(18,4) NOT NULL,
        archive_ofd_id VARCHAR(36),
        status INT NOT NULL,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 28. ipd_transfer_logs
    CREATE TABLE IF NOT EXISTS ipd_transfer_logs (
        id VARCHAR(36) PRIMARY KEY,
        encounter_id VARCHAR(36) NOT NULL,
        transfer_type VARCHAR(32) NOT NULL,
        from_dept_id VARCHAR(36) NOT NULL,
        from_ward_id VARCHAR(36) NOT NULL,
        from_bed_code VARCHAR(40),
        to_dept_id VARCHAR(36) NOT NULL,
        to_ward_id VARCHAR(36) NOT NULL,
        to_bed_code VARCHAR(40),
        handover_out_at_ms BIGINT NOT NULL,
        handover_in_at_ms BIGINT,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 29. ord_medical_orders
    CREATE TABLE IF NOT EXISTS ord_medical_orders (
        id VARCHAR(36) PRIMARY KEY,
        order_no VARCHAR(40) NOT NULL UNIQUE,
        encounter_id VARCHAR(36) NOT NULL,
        patient_id VARCHAR(36) NOT NULL,
        order_ipd_dept_id VARCHAR(36) NOT NULL,
        patient_ward_id VARCHAR(36) NOT NULL,
        exec_target_dept_id VARCHAR(36) NOT NULL,
        order_term_type INT NOT NULL,
        group_no INT NOT NULL,
        order_category VARCHAR(32) NOT NULL,
        item_code VARCHAR(64) NOT NULL,
        item_name VARCHAR(200) NOT NULL,
        dosage DECIMAL(18,4) NOT NULL,
        frequency_code VARCHAR(32) NOT NULL,
        route_code VARCHAR(32) NOT NULL,
        status INT NOT NULL,
        order_doctor_id VARCHAR(36) NOT NULL,
        verify_nurse_id VARCHAR(36),
        plan_start_at_ms BIGINT NOT NULL,
        plan_stop_at_ms BIGINT,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 30. ord_execution_plans
    CREATE TABLE IF NOT EXISTS ord_execution_plans (
        id VARCHAR(36) PRIMARY KEY,
        order_id VARCHAR(36) NOT NULL,
        encounter_id VARCHAR(36) NOT NULL,
        ward_id VARCHAR(36) NOT NULL,
        planned_time_ms BIGINT NOT NULL,
        exec_status INT NOT NULL,
        exec_nurse_id VARCHAR(36),
        dual_check_nurse_id VARCHAR(36),
        executed_at_ms BIGINT,
        pda_scan_token VARCHAR(128),
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 31. mtc_service_requests
    CREATE TABLE IF NOT EXISTS mtc_service_requests (
        id VARCHAR(36) PRIMARY KEY,
        request_no VARCHAR(40) NOT NULL UNIQUE,
        source_type VARCHAR(16) NOT NULL,
        source_visit_id VARCHAR(36),
        source_order_id VARCHAR(36),
        patient_id VARCHAR(36) NOT NULL,
        order_dept_id VARCHAR(36) NOT NULL,
        order_ward_id VARCHAR(36),
        exec_med_tech_dept_id VARCHAR(36) NOT NULL,
        med_tech_sub_type VARCHAR(32) NOT NULL,
        exec_room_code VARCHAR(64),
        specimen_tube_code VARCHAR(64),
        service_item_code VARCHAR(64) NOT NULL,
        fee_amount DECIMAL(18,4) NOT NULL,
        is_fee_confirmed SMALLINT NOT NULL,
        status INT NOT NULL,
        collected_at_ms BIGINT,
        received_at_ms BIGINT,
        executed_at_ms BIGINT,
        report_summary VARCHAR(2000),
        report_ofd_archive_id VARCHAR(36),
        is_critical_value SMALLINT NOT NULL,
        critical_ack_doc_id VARCHAR(36),
        critical_ack_nurse_id VARCHAR(36),
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 32. mtc_surgery_schedules
    CREATE TABLE IF NOT EXISTS mtc_surgery_schedules (
        id VARCHAR(36) PRIMARY KEY,
        encounter_id VARCHAR(36) NOT NULL,
        or_dept_id VARCHAR(36) NOT NULL,
        or_room_code VARCHAR(64) NOT NULL,
        table_seq INT NOT NULL,
        surgery_code VARCHAR(64) NOT NULL,
        surgery_name VARCHAR(200) NOT NULL,
        surgery_level INT NOT NULL,
        main_surgeon_id VARCHAR(36) NOT NULL,
        anesthesiologist_id VARCHAR(36) NOT NULL,
        circulating_nurse_id VARCHAR(36) NOT NULL,
        sign_in_ok SMALLINT NOT NULL,
        time_out_ok SMALLINT NOT NULL,
        sign_out_ok SMALLINT NOT NULL,
        instrument_count_balanced SMALLINT NOT NULL,
        status INT NOT NULL,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 33. mtc_blood_requests
    CREATE TABLE IF NOT EXISTS mtc_blood_requests (
        id VARCHAR(36) PRIMARY KEY,
        encounter_id VARCHAR(36) NOT NULL,
        patient_id VARCHAR(36) NOT NULL,
        abo_type VARCHAR(16) NOT NULL,
        rh_type VARCHAR(16) NOT NULL,
        blood_product_code VARCHAR(64) NOT NULL,
        volume_ml INT NOT NULL,
        cross_match_result VARCHAR(32),
        bag_barcode VARCHAR(80),
        dispensed_by_id VARCHAR(36),
        dual_check_dispenser_id VARCHAR(36),
        empty_bag_returned SMALLINT NOT NULL,
        status INT NOT NULL,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 34. inv_drugs
    CREATE TABLE IF NOT EXISTS inv_drugs (
        id VARCHAR(36) PRIMARY KEY,
        drug_code VARCHAR(64) NOT NULL UNIQUE,
        drug_name VARCHAR(200) NOT NULL,
        spec VARCHAR(100) NOT NULL,
        unit_name VARCHAR(32) NOT NULL,
        price DECIMAL(18,4) NOT NULL,
        cost_price DECIMAL(18,4) NOT NULL,
        antibiotic_level INT NOT NULL,
        is_narcotic SMALLINT NOT NULL,
        is_skin_test_required SMALLINT NOT NULL,
        is_active SMALLINT NOT NULL,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 35. inv_drug_batches
    CREATE TABLE IF NOT EXISTS inv_drug_batches (
        id VARCHAR(36) PRIMARY KEY,
        warehouse_or_pharmacy_id VARCHAR(36) NOT NULL,
        drug_id VARCHAR(36) NOT NULL,
        batch_no VARCHAR(80) NOT NULL,
        expiry_date_int INT NOT NULL,
        invoice_no VARCHAR(80),
        cold_chain_temp_celsius DECIMAL(18,4),
        quantity INT NOT NULL,
        is_frozen SMALLINT NOT NULL,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 36. inv_stock_ledgers
    CREATE TABLE IF NOT EXISTS inv_stock_ledgers (
        id VARCHAR(36) PRIMARY KEY,
        warehouse_or_pharmacy_id VARCHAR(36) NOT NULL,
        drug_id VARCHAR(36) NOT NULL,
        batch_id VARCHAR(36) NOT NULL,
        change_type VARCHAR(32) NOT NULL,
        change_quantity INT NOT NULL,
        balance_after INT NOT NULL,
        reference_doc_no VARCHAR(80),
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 37. phm_dispense_orders
    CREATE TABLE IF NOT EXISTS phm_dispense_orders (
        id VARCHAR(36) PRIMARY KEY,
        dispense_no VARCHAR(40) NOT NULL UNIQUE,
        pharmacy_id VARCHAR(36) NOT NULL,
        prescription_id VARCHAR(36),
        ward_summary_id VARCHAR(36),
        patient_id VARCHAR(36) NOT NULL,
        status INT NOT NULL,
        dispensed_by_id VARCHAR(36),
        dispensed_at_ms BIGINT,
        drug_return_confirmed SMALLINT NOT NULL,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 38. fin_payment_orders
    CREATE TABLE IF NOT EXISTS fin_payment_orders (
        id VARCHAR(36) PRIMARY KEY,
        payment_no VARCHAR(40) NOT NULL UNIQUE,
        request_key VARCHAR(100) UNIQUE,
        scene VARCHAR(16) NOT NULL,
        patient_id VARCHAR(36) NOT NULL,
        visit_or_encounter_id VARCHAR(36) NOT NULL,
        total_amount DECIMAL(18,4) NOT NULL,
        insurance_amount DECIMAL(18,4) NOT NULL,
        prepayment_deduct DECIMAL(18,4) NOT NULL,
        self_pay_amount DECIMAL(18,4) NOT NULL,
        payment_channel VARCHAR(32) NOT NULL,
        status INT NOT NULL,
        paid_at_ms BIGINT,
        invoice_ofd_id VARCHAR(36),
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 39. fin_refund_orders
    CREATE TABLE IF NOT EXISTS fin_refund_orders (
        id VARCHAR(36) PRIMARY KEY,
        refund_no VARCHAR(40) NOT NULL UNIQUE,
        original_payment_id VARCHAR(36) NOT NULL,
        refund_amount DECIMAL(18,4) NOT NULL,
        reason VARCHAR(500) NOT NULL,
        drug_return_confirmed SMALLINT NOT NULL,
        approved_by VARCHAR(36),
        refunded_at_ms BIGINT,
        status INT NOT NULL,
        reissued_invoice_ofd_id VARCHAR(36),
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 40. fin_charge_ledgers
    CREATE TABLE IF NOT EXISTS fin_charge_ledgers (
        id VARCHAR(36) PRIMARY KEY,
        patient_id VARCHAR(36) NOT NULL,
        visit_or_encounter_id VARCHAR(36) NOT NULL,
        order_dept_id VARCHAR(36) NOT NULL,
        patient_ward_id VARCHAR(36),
        exec_dept_id VARCHAR(36) NOT NULL,
        item_code VARCHAR(64) NOT NULL,
        item_name VARCHAR(200) NOT NULL,
        quantity DECIMAL(18,4) NOT NULL,
        unit_price DECIMAL(18,4) NOT NULL,
        total_amount DECIMAL(18,4) NOT NULL,
        status INT NOT NULL,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 41. fin_daily_closings
    CREATE TABLE IF NOT EXISTS fin_daily_closings (
        id VARCHAR(36) PRIMARY KEY,
        closing_no VARCHAR(40) NOT NULL UNIQUE,
        cashier_user_id VARCHAR(36) NOT NULL,
        closing_date_int INT NOT NULL,
        total_income DECIMAL(18,4) NOT NULL,
        total_refund DECIMAL(18,4) NOT NULL,
        net_amount DECIMAL(18,4) NOT NULL,
        reconciled_status INT NOT NULL,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 42. ops_audit_events
    CREATE TABLE IF NOT EXISTS ops_audit_events (
        id VARCHAR(36) PRIMARY KEY,
        event_type VARCHAR(64) NOT NULL,
        user_id VARCHAR(36),
        operator_name VARCHAR(128),
        dept_id VARCHAR(36),
        target_entity VARCHAR(64) NOT NULL,
        target_id VARCHAR(36) NOT NULL,
        action_detail VARCHAR(2000),
        prev_hash VARCHAR(64) NOT NULL,
        curr_hash VARCHAR(64) NOT NULL,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 43. ops_idempotency_keys
    CREATE TABLE IF NOT EXISTS ops_idempotency_keys (
        id VARCHAR(36) PRIMARY KEY,
        request_key VARCHAR(100) NOT NULL UNIQUE,
        request_sha256 VARCHAR(64) NOT NULL,
        response_json TEXT,
        expires_at_ms BIGINT NOT NULL,
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );

    -- 44. ops_outbox_messages
    CREATE TABLE IF NOT EXISTS ops_outbox_messages (
        id VARCHAR(36) PRIMARY KEY,
        event_type VARCHAR(128) NOT NULL,
        payload_json TEXT NOT NULL,
        status INT NOT NULL,
        retry_count INT NOT NULL,
        last_error VARCHAR(1000),
        ext_data_json VARCHAR(4000),
        schema_version INT NOT NULL,
        created_at_ms BIGINT NOT NULL,
        created_by VARCHAR(36),
        updated_at_ms BIGINT NOT NULL,
        updated_by VARCHAR(36),
        version_no INT NOT NULL
    );
    """;

    public static async Task InitializeAsync(IDbConnectionFactory connectionFactory)
    {
        using var conn = connectionFactory.CreateConnection();
        // Execute DDL statement by statement or as whole block
        var statements = DdlScript.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var stmt in statements)
        {
            if (string.IsNullOrWhiteSpace(stmt)) continue;
            await conn.ExecuteAsync(stmt);
        }
    }
}

