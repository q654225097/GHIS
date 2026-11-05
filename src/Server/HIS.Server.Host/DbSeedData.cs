using Dapper;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;

namespace HIS.Server.Host;

public static class DbSeedData
{
    public static async Task SeedAsync(IDbConnectionFactory connectionFactory)
    {
        using var conn = connectionFactory.CreateConnection();

        // 1. Check if seeded
        var count = await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM sys_departments");
        if (count > 0) return;

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 2. Departments (4 dimensions)
        await conn.ExecuteAsync("""
        INSERT INTO sys_departments (id, campus_id, dept_code, dept_name, pinyin_code, dept_category, sub_specialty_type, rated_bed_count, is_allow_appointment, is_active, schema_version, created_at_ms, updated_at_ms, version_no)
        VALUES 
        ('DEPT-OPD-01', 'CAMPUS-01', 'OPD_CARDIO', '心血管内科门诊', 'XNGNKMZ', 'OPD', 'CARDIOLOGY', 0, 1, 1, 1, @Now, @Now, 1),
        ('DEPT-ER-01', 'CAMPUS-01', 'ER_MAIN', '急诊医学科', 'JZYXK', 'OPD', 'EMERGENCY', 0, 1, 1, 1, @Now, @Now, 1),
        ('DEPT-IPD-01', 'CAMPUS-01', 'IPD_CARDIO_1', '心血管内科住院一科', 'XNGNKZY1K', 'IPD', 'CARDIOLOGY', 45, 0, 1, 1, @Now, @Now, 1),
        ('DEPT-IPD-02', 'CAMPUS-01', 'IPD_THORACIC', '胸外科住院科', 'XWKZYK', 'IPD', 'THORACIC', 40, 0, 1, 1, @Now, @Now, 1),
        ('DEPT-WARD-01', 'CAMPUS-01', 'WARD_INT_1', '内科一病区', 'NKYBQ', 'WARD', 'INTERNAL', 45, 0, 1, 1, @Now, @Now, 1),
        ('DEPT-WARD-02', 'CAMPUS-01', 'WARD_INT_2', '内科二病区', 'NKEBQ', 'WARD', 'RESPIRATORY', 50, 0, 1, 1, @Now, @Now, 1),
        ('DEPT-MTC-LAB', 'CAMPUS-01', 'MTC_LAB_MAIN', '医学检验科', 'YXJYK', 'MEDTECH', 'LAB', 0, 0, 1, 1, @Now, @Now, 1),
        ('DEPT-MTC-RAD', 'CAMPUS-01', 'MTC_RAD_MAIN', '放射影像科', 'FSYXK', 'MEDTECH', 'RADIOLOGY', 0, 1, 1, 1, @Now, @Now, 1),
        ('DEPT-MTC-OR', 'CAMPUS-01', 'MTC_OR_MAIN', '手术麻醉科', 'SSMZK', 'MEDTECH', 'OR_ANES', 0, 0, 1, 1, @Now, @Now, 1),
        ('DEPT-PHM-01', 'CAMPUS-01', 'PHM_OPD_WEST', '门诊西药房', 'MZXFY', 'PHARMACY', 'WESTERN', 0, 0, 1, 1, @Now, @Now, 1)
        """, new { Now = nowMs });

        // 3. Dept-Ward Relations (N:M + Borrow Allowed)
        await conn.ExecuteAsync("""
        INSERT INTO sys_dept_ward_relations (id, ipd_dept_id, ward_dept_id, relation_type, max_borrow_beds, priority_order, is_active, schema_version, created_at_ms, updated_at_ms, version_no)
        VALUES
        ('REL-01', 'DEPT-IPD-01', 'DEPT-WARD-01', 'PRIMARY', 45, 1, 1, 1, @Now, @Now, 1),
        ('REL-02', 'DEPT-IPD-01', 'DEPT-WARD-02', 'BORROW_ALLOWED', 10, 2, 1, 1, @Now, @Now, 1)
        """, new { Now = nowMs });

        // 4. Medtech Routing
        await conn.ExecuteAsync("""
        INSERT INTO sys_medtech_exec_routings (id, campus_id, source_scene, order_dept_id, service_item_code, exec_med_tech_dept_id, exec_room_code, time_window_type, priority_weight, is_active, schema_version, created_at_ms, updated_at_ms, version_no)
        VALUES
        ('RTE-01', 'CAMPUS-01', 'ALL', NULL, 'LAB_BLOOD_ROUTINE', 'DEPT-MTC-LAB', 'ROOM-LAB-01', 'ALL_DAY', 10, 1, 1, @Now, @Now, 1),
        ('RTE-02', 'CAMPUS-01', 'ALL', NULL, 'RAD_CHEST_CT', 'DEPT-MTC-RAD', 'ROOM-CT-01', 'ALL_DAY', 10, 1, 1, @Now, @Now, 1)
        """, new { Now = nowMs });

        // 5. Beds in Ward 01 and Ward 02
        await conn.ExecuteAsync("""
        INSERT INTO ipd_beds (id, campus_id, ward_id, room_code, bed_code, bed_type, daily_fee, bed_status, schema_version, created_at_ms, updated_at_ms, version_no)
        VALUES
        ('BED-01', 'CAMPUS-01', 'DEPT-WARD-01', 'R101', '01', 'STANDARD', 60.00, 0, 1, @Now, @Now, 1),
        ('BED-02', 'CAMPUS-01', 'DEPT-WARD-01', 'R101', '02', 'STANDARD', 60.00, 0, 1, @Now, @Now, 1),
        ('BED-03', 'CAMPUS-01', 'DEPT-WARD-02', 'R201', '01', 'STANDARD', 60.00, 0, 1, @Now, @Now, 1),
        ('BED-04', 'CAMPUS-01', 'DEPT-WARD-02', 'R201', '02', 'STANDARD', 60.00, 0, 1, @Now, @Now, 1)
        """, new { Now = nowMs });

        // 6. Users
        var pwdHash = CryptoHelper.ComputeSha256("123456");
        await conn.ExecuteAsync("""
        INSERT INTO sys_users (id, user_name, display_name, employee_no, password_hash, prescribe_level, surgery_level, emr_sign_level, session_version, is_active, schema_version, created_at_ms, updated_at_ms, version_no)
        VALUES
        ('USER-ADMIN', 'admin', '系统管理员', 'EMP001', @Pwd, 3, 4, 3, 1, 1, 1, @Now, @Now, 1),
        ('USER-DOC-OPD', 'doc_opd', '张建国 (门诊心内主治)', 'EMP002', @Pwd, 2, 2, 2, 1, 1, 1, @Now, @Now, 1),
        ('USER-DOC-RESIDENT', 'doc_res', '李华 (心内住院医师)', 'EMP003', @Pwd, 1, 1, 1, 1, 1, 1, @Now, @Now, 1),
        ('USER-DOC-ATTENDING', 'doc_att', '王崇 (心内主治医师)', 'EMP004', @Pwd, 2, 2, 2, 1, 1, 1, @Now, @Now, 1),
        ('USER-NURSE-W1', 'nurse_w1', '陈芳 (内科一病区护士)', 'EMP005', @Pwd, 0, 0, 0, 1, 1, 1, @Now, @Now, 1),
        ('USER-NURSE-W2', 'nurse_w2', '赵敏 (内科二病区护士)', 'EMP006', @Pwd, 0, 0, 0, 1, 1, 1, @Now, @Now, 1),
        ('USER-PHM-01', 'phm_user', '孙伟 (门诊西药房药师)', 'EMP007', @Pwd, 0, 0, 0, 1, 1, 1, @Now, @Now, 1),
        ('USER-CASHIER-01', 'cashier_01', '刘洋 (收费结算处收费员)', 'EMP008', @Pwd, 0, 0, 0, 1, 1, 1, @Now, @Now, 1)
        """, new { Pwd = pwdHash, Now = nowMs });

        // 7. Standard EMR Data Elements (with Gender Exclusion)
        await conn.ExecuteAsync("""
        INSERT INTO emr_data_elements (id, element_code, nhc_std_code, element_name, data_type, ursa_control_type, gender_exclusive, is_indexed, is_active, schema_version, created_at_ms, updated_at_ms, version_no)
        VALUES
        ('EL-01', 'CHIEF_COMPLAINT', 'DE04.01.119.00', '主诉', 'STRING', 'u:TextBox', 'ALL', 1, 1, 1, @Now, @Now, 1),
        ('EL-02', 'PRESENT_ILLNESS', 'DE02.10.071.00', '现病史', 'TEXT', 'u:TextArea', 'ALL', 1, 1, 1, @Now, @Now, 1),
        ('EL-03', 'PRIMARY_DIAGNOSIS', 'DE05.01.024.00', '初步诊断', 'STRING', 'u:AutoCompleteBox', 'ALL', 1, 1, 1, @Now, @Now, 1),
        ('EL-04', 'DE_MENSTRUAL_HISTORY', 'DE02.10.032.00', '月经史', 'STRING', 'u:TextBox', 'FEMALE_ONLY', 1, 1, 1, @Now, @Now, 1),
        ('EL-05', 'DE_PREGNANCY_COUNT', 'DE02.10.002.00', '孕产次数', 'INTEGER', 'u:NumericIntUpDown', 'FEMALE_ONLY', 1, 1, 1, @Now, @Now, 1),
        ('EL-06', 'DE_PROSTATE_EXAM', 'DE04.10.188.00', '前列腺触诊', 'STRING', 'u:TextBox', 'MALE_ONLY', 1, 1, 1, @Now, @Now, 1)
        """, new { Now = nowMs });

        // 8. Drugs (with skin test requirement)
        await conn.ExecuteAsync("""
        INSERT INTO inv_drugs (id, drug_code, drug_name, spec, unit_name, price, cost_price, antibiotic_level, is_narcotic, is_skin_test_required, is_active, schema_version, created_at_ms, updated_at_ms, version_no)
        VALUES
        ('DRUG-01', 'PENICILLIN_G', '注射用青霉素钠', '80万单位/支', '支', 12.50, 8.00, 1, 0, 1, 1, 1, @Now, @Now, 1),
        ('DRUG-02', 'AMOXICILLIN_CAPS', '阿莫西林胶囊', '0.25g*24粒/盒', '盒', 18.00, 11.20, 1, 0, 1, 1, 1, @Now, @Now, 1),
        ('DRUG-03', 'NORMAL_SALINE_500', '0.9%氯化钠注射液', '500ml/瓶', '瓶', 5.50, 3.20, 0, 0, 0, 1, 1, @Now, @Now, 1)
        """, new { Now = nowMs });

        // 9. Roles and Permissions
        await conn.ExecuteAsync("""
        INSERT INTO sys_roles (id, role_code, role_name, data_scope, is_active, schema_version, created_at_ms, updated_at_ms, version_no)
        VALUES
        ('ROLE-ADMIN', 'ADMIN', '超级管理员', 'ALL_CAMPUS', 1, 1, @Now, @Now, 1),
        ('ROLE-OPD-DOC', 'OPD_DOC', '门诊医师', 'OPD_DEPT_SCOPE', 1, 1, @Now, @Now, 1),
        ('ROLE-IPD-RESIDENT', 'IPD_RESIDENT', '住院医师', 'IPD_DEPT_AND_BORROWED', 1, 1, @Now, @Now, 1),
        ('ROLE-IPD-ATTENDING', 'IPD_ATTENDING', '主治/主任医师', 'IPD_DEPT_AND_BORROWED', 1, 1, @Now, @Now, 1),
        ('ROLE-WARD-NURSE', 'WARD_NURSE', '病区护士', 'NURSING_WARD_SCOPE', 1, 1, @Now, @Now, 1)
        """, new { Now = nowMs });

        await conn.ExecuteAsync("""
        INSERT INTO sys_role_permissions (id, role_id, resource_key, access_policy, schema_version, created_at_ms, updated_at_ms, version_no)
        VALUES
        ('PERM-01', 'ROLE-OPD-DOC', 'OPD.ConsultView.BtnSignPrescription', 3, 1, @Now, @Now, 1),
        ('PERM-02', 'ROLE-IPD-ATTENDING', 'EMR.Toolbar.BtnExportOfd', 3, 1, @Now, @Now, 1),
        ('PERM-03', 'ROLE-IPD-RESIDENT', 'EMR.Toolbar.BtnExportOfd', 2, 1, @Now, @Now, 1),
        ('PERM-04', 'ROLE-WARD-NURSE', 'EMR.Toolbar.BtnExportOfd', 0, 1, @Now, @Now, 1)
        """, new { Now = nowMs });
    }
}

