using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;

namespace HIS.Server.Host;

public static partial class DbSeedData
{
	private const string DemoPassword = "123";

	private static readonly string[] DemoUserNames =
	[
		"admin",
		"doc_opd",
		"doc_res",
		"doc_att",
		"nurse_w1",
		"nurse_w2",
		"phm_user",
		"cashier_01",
		"tech_user",
		"warehouse_01",
		"no_context"
	];

	private sealed class DemoUserPasswordRow
	{
		public string Id { get; set; } = string.Empty;

		public string UserName { get; set; } = string.Empty;

		public string PasswordHash { get; set; } = string.Empty;
	}

	public static async Task SeedAsync(IDbConnectionFactory connectionFactory, bool resetDemoPasswords = false)
	{
		using DbConnection conn = connectionFactory.CreateConnection();
		long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		int todayInt = SystemTime.TodayInt;
		await conn.ExecuteAsync("INSERT OR IGNORE INTO sys_departments (id, campus_id, dept_code, dept_name, pinyin_code, dept_category, sub_specialty_type, rated_bed_count, is_allow_appointment, is_active, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES \n('DEPT-OPD-01', 'CAMPUS-01', 'OPD_CARDIO', '心血管内科门诊', 'XNGNKMZ', 'OPD', 'CARDIOLOGY', 0, 1, 1, 1, @Now, @Now, 1),\n('DEPT-ER-01', 'CAMPUS-01', 'ER_MAIN', '急诊医学科', 'JZYXK', 'OPD', 'EMERGENCY', 0, 1, 1, 1, @Now, @Now, 1),\n('DEPT-IPD-01', 'CAMPUS-01', 'IPD_CARDIO_1', '心血管内科住院一科', 'XNGNKZY1K', 'IPD', 'CARDIOLOGY', 45, 0, 1, 1, @Now, @Now, 1),\n('DEPT-IPD-02', 'CAMPUS-01', 'IPD_THORACIC', '胸外科住院科', 'XWKZYK', 'IPD', 'THORACIC', 40, 0, 1, 1, @Now, @Now, 1),\n('DEPT-WARD-01', 'CAMPUS-01', 'WARD_INT_1', '内科一病区', 'NKYBQ', 'WARD', 'INTERNAL', 45, 0, 1, 1, @Now, @Now, 1),\n('DEPT-WARD-02', 'CAMPUS-01', 'WARD_INT_2', '内科二病区', 'NKEBQ', 'WARD', 'RESPIRATORY', 50, 0, 1, 1, @Now, @Now, 1),\n('DEPT-MTC-LAB', 'CAMPUS-01', 'MTC_LAB_MAIN', '医学检验科', 'YXJYK', 'MEDTECH', 'LAB', 0, 0, 1, 1, @Now, @Now, 1),\n('DEPT-MTC-RAD', 'CAMPUS-01', 'MTC_RAD_MAIN', '放射影像科', 'FSYXK', 'MEDTECH', 'RADIOLOGY', 0, 1, 1, 1, @Now, @Now, 1),\n('DEPT-MTC-OR', 'CAMPUS-01', 'MTC_OR_MAIN', '手术麻醉科', 'SSMZK', 'MEDTECH', 'OR_ANES', 0, 0, 1, 1, @Now, @Now, 1),\n('DEPT-PHM-01', 'CAMPUS-01', 'PHM_OPD_WEST', '门诊西药房', 'MZXFY', 'PHARMACY', 'WESTERN', 0, 0, 1, 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO sys_departments (id, campus_id, dept_code, dept_name, pinyin_code, dept_category, sub_specialty_type, rated_bed_count, is_allow_appointment, is_active, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n('DEPT-ADMIN-01', 'CAMPUS-01', 'ADMIN_CENTER', '信息管理中心', 'XXGLZX', 'ADMIN', 'ADMIN', 0, 0, 1, 1, @Now, @Now, 1),\n('DEPT-FIN-01', 'CAMPUS-01', 'FINANCE_OUTPATIENT', '门急诊财务处', 'MJZCWC', 'FINANCE', 'FINANCE', 0, 0, 1, 1, @Now, @Now, 1),\n('DEPT-PHM-02', 'CAMPUS-01', 'PHM_IPD_CENTER', '住院中心药房', 'ZYZXYF', 'PHARMACY', 'INPATIENT', 0, 0, 1, 1, @Now, @Now, 1),\n('DEPT-WH-01', 'CAMPUS-01', 'WH_CENTRAL', '全院中心药库', 'QYZXZK', 'WAREHOUSE', 'GENERAL', 0, 0, 1, 1, @Now, @Now, 1),\n('DEPT-WH-02', 'CAMPUS-01', 'WH_COLD', '冷链生物制品专库', 'LLSWZPZK', 'WAREHOUSE', 'COLD_CHAIN', 0, 0, 1, 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO sys_departments (id, campus_id, dept_code, dept_name, pinyin_code, dept_category, sub_specialty_type, rated_bed_count, is_allow_appointment, is_active, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n('DEPT-OPD-ENDO-01', 'CAMPUS-01', 'OPD_ENDO', '内分泌科门诊', 'NFMKMZ', 'OPD', 'ENDOCRINOLOGY', 0, 1, 1, 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO sys_dept_ward_relations (id, ipd_dept_id, ward_dept_id, relation_type, max_borrow_beds, priority_order, is_active, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n('REL-01', 'DEPT-IPD-01', 'DEPT-WARD-01', 'PRIMARY', 45, 1, 1, 1, @Now, @Now, 1),\n('REL-02', 'DEPT-IPD-01', 'DEPT-WARD-02', 'BORROW_ALLOWED', 10, 2, 1, 1, @Now, @Now, 1),\n('REL-03', 'DEPT-IPD-02', 'DEPT-WARD-02', 'PRIMARY', 40, 1, 1, 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO sys_medtech_exec_routings (id, campus_id, source_scene, order_dept_id, service_item_code, exec_med_tech_dept_id, exec_room_code, time_window_type, priority_weight, is_active, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n('RTE-01', 'CAMPUS-01', 'ALL', NULL, 'LAB_BLOOD_ROUTINE', 'DEPT-MTC-LAB', 'ROOM-LAB-01', 'ALL_DAY', 10, 1, 1, @Now, @Now, 1),\n('RTE-02', 'CAMPUS-01', 'ALL', NULL, 'RAD_CHEST_CT', 'DEPT-MTC-RAD', 'ROOM-CT-01', 'ALL_DAY', 10, 1, 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO ipd_beds (id, campus_id, ward_id, room_code, bed_code, bed_type, daily_fee, bed_status, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n('BED-01', 'CAMPUS-01', 'DEPT-WARD-01', 'R101', '01', 'STANDARD', 60.00, 0, 1, @Now, @Now, 1),\n('BED-02', 'CAMPUS-01', 'DEPT-WARD-01', 'R101', '02', 'STANDARD', 60.00, 0, 1, @Now, @Now, 1),\n('BED-03', 'CAMPUS-01', 'DEPT-WARD-02', 'R201', '01', 'STANDARD', 60.00, 0, 1, @Now, @Now, 1),\n('BED-04', 'CAMPUS-01', 'DEPT-WARD-02', 'R201', '02', 'STANDARD', 60.00, 0, 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO opd_schedule_slots (id, campus_id, opd_dept_id, clinic_room_id, doctor_id, schedule_date_int, slot_period_code, starts_at_ms, ends_at_ms, reg_level_code, registration_fee, total_capacity, reserved_count, slot_status, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n('SLOT-OPD-01', 'CAMPUS-01', 'DEPT-OPD-01', 'ROOM-OPD-101', 'USER-DOC-OPD', @TodayInt, 'MORNING', @Now, @EndsAt, 'EXPERT', 25.00, 30, 0, 1, 1, @Now, @Now, 1),\n('SLOT-ER-01', 'CAMPUS-01', 'DEPT-ER-01', 'ROOM-ER-01', 'USER-DOC-OPD', @TodayInt, 'ALL_DAY', @Now, @EndsAt, 'EMERGENCY', 30.00, 100, 0, 1, 1, @Now, @Now, 1)", new
		{
			TodayInt = todayInt,
			Now = nowMs,
			EndsAt = nowMs + 28800000
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO opd_schedule_templates (id, campus_id, opd_dept_id, clinic_room_id, doctor_id, template_name, weekday_codes, slot_period_code, start_minute, end_minute, reg_level_code, registration_fee, total_capacity, is_active, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n('SCHEDULE-TPL-CARDIO-AM', 'CAMPUS-01', 'DEPT-OPD-01', 'ROOM-OPD-101', 'USER-DOC-OPD', '心内专家门诊·工作日上午', '1,2,3,4,5', 'MORNING', 480, 720, 'EXPERT', 25.00, 30, 1, 1, @Now, @Now, 1),\n('SCHEDULE-TPL-CARDIO-PM', 'CAMPUS-01', 'DEPT-OPD-01', 'ROOM-OPD-101', 'USER-DOC-OPD', '心内专家门诊·周六下午', '6', 'AFTERNOON', 840, 1020, 'EXPERT', 25.00, 20, 1, 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		string pwdHash = PasswordSecurityHelper.HashPassword(DemoPassword);
		await conn.ExecuteAsync("INSERT OR IGNORE INTO sys_users (id, user_name, display_name, employee_no, password_hash, prescribe_level, surgery_level, emr_sign_level, session_version, is_active, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n('USER-ADMIN', 'admin', '系统管理员', 'EMP001', @Pwd, 3, 4, 3, 1, 1, 1, @Now, @Now, 1),\n('USER-DOC-OPD', 'doc_opd', '张建国 (门诊心内主治)', 'EMP002', @Pwd, 2, 2, 2, 1, 1, 1, @Now, @Now, 1),\n('USER-DOC-RESIDENT', 'doc_res', '李华 (心内住院医师)', 'EMP003', @Pwd, 1, 1, 1, 1, 1, 1, @Now, @Now, 1),\n('USER-DOC-ATTENDING', 'doc_att', '王崇 (心内主治医师)', 'EMP004', @Pwd, 2, 2, 2, 1, 1, 1, @Now, @Now, 1),\n('USER-NURSE-W1', 'nurse_w1', '陈芳 (内科一病区护士)', 'EMP005', @Pwd, 0, 0, 0, 1, 1, 1, @Now, @Now, 1),\n('USER-NURSE-W2', 'nurse_w2', '赵敏 (内科二病区护士)', 'EMP006', @Pwd, 0, 0, 0, 1, 1, 1, @Now, @Now, 1),\n('USER-PHM-01', 'phm_user', '孙伟 (门诊西药房药师)', 'EMP007', @Pwd, 0, 0, 0, 1, 1, 1, @Now, @Now, 1),\n('USER-CASHIER-01', 'cashier_01', '刘洋 (收费结算处收费员)', 'EMP008', @Pwd, 0, 0, 0, 1, 1, 1, @Now, @Now, 1),\n('USER-MEDTECH-01', 'tech_user', '李建 (检验/放射技师)', 'EMP010', @Pwd, 0, 0, 0, 1, 1, 1, @Now, @Now, 1),\n('USER-WH-01', 'warehouse_01', '周强 (中心药库管理员)', 'EMP011', @Pwd, 0, 0, 0, 1, 1, 1, @Now, @Now, 1),\n('USER-NO-CONTEXT', 'no_context', '未配置身份用户', 'EMP009', @Pwd, 0, 0, 0, 1, 1, 1, @Now, @Now, 1)", new
		{
			Pwd = pwdHash,
			Now = nowMs
		});
		if (resetDemoPasswords)
		{
			await ResetDemoPasswordsAsync(conn, nowMs);
		}
		await conn.ExecuteAsync("INSERT OR IGNORE INTO emr_data_elements (id, element_code, nhc_std_code, element_name, data_type, ursa_control_type, unit_text, macro_source, gender_exclusive, is_indexed, is_active, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n('EL-01', 'CHIEF_COMPLAINT', 'DE04.01.119.00', '主诉', 'STRING', 'u:TextBox', NULL, 'CHIEF_COMPLAINT', 'ALL', 1, 1, 1, @Now, @Now, 1),\n('EL-02', 'PRESENT_ILLNESS', 'DE02.10.071.00', '现病史', 'TEXT', 'u:TextArea', NULL, 'PRESENT_ILLNESS', 'ALL', 1, 1, 1, @Now, @Now, 1),\n('EL-03', 'PRIMARY_DIAGNOSIS', 'DE05.01.024.00', '初步诊断', 'STRING', 'u:AutoCompleteBox', NULL, 'PRIMARY_DIAGNOSIS', 'ALL', 1, 1, 1, @Now, @Now, 1),\n('EL-04', 'DE_MENSTRUAL_HISTORY', 'DE02.10.032.00', '月经史', 'STRING', 'u:TextBox', NULL, NULL, 'FEMALE_ONLY', 1, 1, 1, @Now, @Now, 1),\n('EL-05', 'DE_GRAVIDITY', 'DE04.01.106.00', '孕次', 'INTEGER', 'u:NumericIntUpDown', '次', NULL, 'FEMALE_ONLY', 1, 1, 1, @Now, @Now, 1),\n('EL-05B', 'DE_PARITY', 'DE02.10.002.00', '产次', 'INTEGER', 'u:NumericIntUpDown', '次', NULL, 'FEMALE_ONLY', 1, 1, 1, @Now, @Now, 1),\n('EL-06', 'DE_PROSTATE_EXAM', 'DE04.10.001.00', '前列腺触诊', 'STRING', 'u:TextBox', NULL, NULL, 'MALE_ONLY', 1, 1, 1, @Now, @Now, 1),\n('EL-07', 'PAST_HISTORY', 'DE02.10.099.00', '既往史与慢病史', 'TEXT', 'u:TextArea', NULL, 'PAST_HISTORY', 'ALL', 1, 1, 1, @Now, @Now, 1),\n('EL-08', 'ALLERGY_HISTORY', 'DE02.10.022.00', '药物与食物过敏史', 'STRING', 'u:TextBox', NULL, 'PAT.AllergyTags', 'ALL', 1, 1, 1, @Now, @Now, 1),\n('EL-09', 'PHYSICAL_EXAM', 'DE04.10.258.00', '体格检查与生命体征', 'TEXT', 'u:TextArea', NULL, 'PHYSICAL_EXAM', 'ALL', 1, 1, 1, @Now, @Now, 1),\n('EL-10', 'AUX_EXAM', 'DE04.30.010.00', '辅助检查摘要', 'TEXT', 'u:TextArea', NULL, NULL, 'ALL', 1, 1, 1, @Now, @Now, 1),\n('EL-11', 'TREATMENT_PLAN', 'DE05.10.131.00', '诊疗计划与处置意见', 'TEXT', 'u:TextArea', NULL, NULL, 'ALL', 1, 1, 1, @Now, @Now, 1),\n('EL-12', 'ADMISSION_DIAGNOSIS', 'DE05.01.024.00', '入院诊断', 'STRING', 'u:AutoCompleteBox', NULL, 'PRIMARY_DIAGNOSIS', 'ALL', 1, 1, 1, @Now, @Now, 1),\n('EL-13', 'PROGRESS_CONTENT', 'DE06.00.182.00', '查房病情变化与分析', 'TEXT', 'u:TextArea', NULL, NULL, 'ALL', 1, 1, 1, @Now, @Now, 1),\n('EL-14', 'DISCHARGE_COURSE', 'DE06.00.193.00', '住院诊疗经过与转归', 'TEXT', 'u:TextArea', NULL, NULL, 'ALL', 1, 1, 1, @Now, @Now, 1),\n('EL-15', 'DISCHARGE_ADVICE', 'DE06.00.287.00', '出院医嘱与带药指导', 'TEXT', 'u:TextArea', NULL, NULL, 'ALL', 1, 1, 1, @Now, @Now, 1),\n('EL-16', 'NURSING_CONSCIOUSNESS', 'DE04.10.167.00', '神志与精神状态', 'ENUM', 'ComboBox', NULL, NULL, 'ALL', 1, 1, 1, @Now, @Now, 1),\n('EL-17', 'NURSING_BRADEN_SCORE', 'DE04.10.008.00', 'Braden压疮风险评分', 'INTEGER', 'u:NumericIntUpDown', '分', NULL, 'ALL', 1, 1, 1, @Now, @Now, 1),\n('EL-18', 'NURSING_BARTHEL_SCORE', 'DE05.10.003.00', 'Barthel自理能力评分', 'INTEGER', 'u:NumericIntUpDown', '分', NULL, 'ALL', 1, 1, 1, @Now, @Now, 1),\n('EL-19', 'NURSING_FALL_SCORE', 'DE05.10.145.00', 'Morse跌倒坠床风险评分', 'INTEGER', 'u:NumericIntUpDown', '分', NULL, 'ALL', 1, 1, 1, @Now, @Now, 1),\n('EL-20', 'NURSING_INTERVENTION', 'DE06.00.342.00', '护理观察与专科护理措施', 'TEXT', 'u:TextArea', NULL, NULL, 'ALL', 1, 1, 1, @Now, @Now, 1),\n('EL-21', 'VITAL_TEMPERATURE', 'DE04.10.186.00', '体温', 'DECIMAL', 'u:NumericDoubleUpDown', '℃', NULL, 'ALL', 1, 1, 1, @Now, @Now, 1),\n('EL-22', 'VITAL_PULSE', 'DE04.10.118.00', '脉率', 'INTEGER', 'u:NumericIntUpDown', '次/min', NULL, 'ALL', 1, 1, 1, @Now, @Now, 1),\n('EL-23', 'VITAL_RESPIRATION', 'DE04.10.082.00', '呼吸频率', 'INTEGER', 'u:NumericIntUpDown', '次/min', NULL, 'ALL', 1, 1, 1, @Now, @Now, 1),\n('EL-24', 'VITAL_SBP', 'DE04.10.174.00', '收缩压', 'INTEGER', 'u:NumericIntUpDown', 'mmHg', NULL, 'ALL', 1, 1, 1, @Now, @Now, 1),\n('EL-25', 'VITAL_DBP', 'DE04.10.176.00', '舒张压', 'INTEGER', 'u:NumericIntUpDown', 'mmHg', NULL, 'ALL', 1, 1, 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO emr_ofd_layout_schemas\n    (id, layout_code, layout_name, page_size_code, page_width_mm, page_height_mm, margin_top_mm, margin_bottom_mm, margin_left_mm, margin_right_mm, is_continuous_flow, header_layout_json, footer_layout_json, sign_slots_json, watermark_policy, is_active, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n    ('LAYOUT-OPD-B5', 'OPD_STD_B5', '门诊病历标准 B5 紧凑版式', 'B5', 176.0, 250.0, 16.0, 16.0, 18.0, 16.0, 0,\n     '{\"hospitalName\":\"国家区域医疗中心 · 三级甲等综合医院\",\"subTitle\":\"门 诊 结 构 化 病 历\",\"showPatientBanner\":true,\"showTrackChanges\":false,\"accentColor\":\"#B71C1C\"}',\n     '{\"footerText\":\"遵循国家卫健委《电子病历基本数据集》与 GB/T 33190-2016 标准 · 门诊病历归档件\",\"showPageNumber\":true}',\n     '[{\"level\":1,\"role\":\"接诊医师\",\"label\":\"接诊医师签名\",\"required\":true}]',\n     '国家三甲医院门诊病历归档专章', 1, 1, @Now, @Now, 1),\n    ('LAYOUT-IPD-A4', 'IPD_STD_A4', '住院病历国标 A4 三级审签版式', 'A4', 210.0, 297.0, 25.0, 20.0, 25.0, 20.0, 1,\n     '{\"hospitalName\":\"国家区域医疗中心 · 三级甲等综合医院\",\"subTitle\":\"住 院 病 历 归 档 记 录\",\"showPatientBanner\":true,\"showBedAndAdmissionNo\":true,\"showTrackChanges\":true,\"accentColor\":\"#1565C0\"}',\n     '{\"footerText\":\"住院病案归档件 · 遵循 GB/T 33190-2016 双层语义嵌入与三级审签锁止规范\",\"showPageNumber\":true}',\n     '[{\"level\":1,\"role\":\"经治住院医师\",\"label\":\"住院医师签名\",\"required\":true},{\"level\":2,\"role\":\"主治医师\",\"label\":\"主治医师审签\",\"required\":true},{\"level\":3,\"role\":\"主任/副主任医师\",\"label\":\"主任医师终审\",\"required\":true}]',\n     '国家三甲医院住院病案归档专章', 1, 1, @Now, @Now, 1),\n    ('LAYOUT-WARD-A4', 'WARD_NURSING_A4', '护理病历与评估单 A4 质控版式', 'A4', 210.0, 297.0, 20.0, 18.0, 20.0, 18.0, 1,\n     '{\"hospitalName\":\"国家区域医疗中心 · 三级甲等综合医院\",\"subTitle\":\"护 理 评 估 与 专 科 护 理 记 录 单\",\"showPatientBanner\":true,\"showVitalSignsBanner\":true,\"showNursingScales\":true,\"accentColor\":\"#059669\"}',\n     '{\"footerText\":\"护理质量控制标准表单 · 责任护士与护士长双人质控核对\",\"showPageNumber\":true}',\n     '[{\"level\":1,\"role\":\"责任护士\",\"label\":\"责任护士签名\",\"required\":true},{\"level\":2,\"role\":\"病区护士长/质控护师\",\"label\":\"质控护士长审签\",\"required\":true}]',\n     '国家三甲医院护理病历质控专章', 1, 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO emr_doc_templates\n    (id, template_code, template_version, template_name, emr_scene, doc_type_code, dept_category, target_dept_id, disease_icd_prefix, time_limit_hours, required_sign_levels, ofd_layout_id, is_published, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n    ('TMPL-OPD-FIRST', 'OPD_FIRST_VISIT', 1, '门诊初诊结构化病历', 'OPD', 'OPD_FIRST_VISIT', '全科门诊', 'DEPT-OPD-01', 'I25', 2, 1, 'LAYOUT-OPD-B5', 1, 1, @Now, @Now, 1),\n    ('TMPL-OPD-FOLLOWUP', 'OPD_FOLLOWUP', 1, '门诊复诊与慢病随访病历', 'OPD', 'OPD_FOLLOWUP', '慢病科', 'DEPT-OPD-01', 'I10', 2, 1, 'LAYOUT-OPD-B5', 1, 1, @Now, @Now, 1),\n    ('TMPL-OPD-CARDIO', 'OPD_CARDIO_SPECIAL', 1, '心血管内科门诊专科病历', 'OPD', 'OPD_CARDIO', '心血管科', 'DEPT-OPD-01', 'I25', 2, 1, 'LAYOUT-OPD-B5', 1, 1, @Now, @Now, 1),\n    ('TMPL-OPD-PEDIATRIC', 'OPD_PEDIATRIC', 1, '儿科门诊初诊病历', 'OPD', 'OPD_PEDIATRIC', '儿科', 'DEPT-OPD-02', 'J06', 2, 1, 'LAYOUT-OPD-B5', 1, 1, @Now, @Now, 1),\n    ('TMPL-IPD-ADMISSION', 'IPD_ADMISSION_24H', 1, '住院24小时入院记录', 'IPD', 'IPD_ADMISSION', 'IPD', 'DEPT-IPD-01', 'I25', 24, 3, 'LAYOUT-IPD-A4', 1, 1, @Now, @Now, 1),\n    ('TMPL-IPD-PROGRESS', 'IPD_DAILY_PROGRESS', 1, '住院首次/日常病程记录', 'IPD', 'IPD_PROGRESS', 'IPD', 'DEPT-IPD-01', 'I25', 8, 2, 'LAYOUT-IPD-A4', 1, 1, @Now, @Now, 1),\n    ('TMPL-IPD-DISCHARGE', 'IPD_DISCHARGE_SUMMARY', 1, '住院出院记录与小结', 'IPD', 'IPD_DISCHARGE', 'IPD', 'DEPT-IPD-01', 'I25', 24, 3, 'LAYOUT-IPD-A4', 1, 1, @Now, @Now, 1),\n    ('TMPL-WARD-ADMISSION', 'WARD_ADMISSION_ASSESS', 1, '住院患者入院护理评估单 (含三评量表)', 'WARD', 'NURSING_ADMISSION', 'WARD', 'DEPT-WARD-01', NULL, 4, 2, 'LAYOUT-WARD-A4', 1, 1, @Now, @Now, 1),\n    ('TMPL-WARD-CRITICAL', 'WARD_CRITICAL_CARE', 1, '危重患者特级护理观察记录单', 'WARD', 'NURSING_CRITICAL', 'WARD', 'DEPT-WARD-01', NULL, 2, 2, 'LAYOUT-WARD-A4', 1, 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO emr_section_schemas\n    (id, template_id, section_code, section_title, render_mode, content_template_json, section_control_key, is_required, visibility_expr, sort_order, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n    ('SEC-OPD1-01', 'TMPL-OPD-FIRST', 'SEC_CHIEF', '主诉与现病史', 'GRID_FORM', '{\"elements\":[\"CHIEF_COMPLAINT\",\"PRESENT_ILLNESS\"]}', 'EMR.Opd.ChiefSection', 1, 'ALL', 1, 1, @Now, @Now, 1),\n    ('SEC-OPD1-02', 'TMPL-OPD-FIRST', 'SEC_HISTORY', '既往史与过敏史', 'GRID_FORM', '{\"elements\":[\"PAST_HISTORY\",\"ALLERGY_HISTORY\"]}', 'EMR.Opd.HistorySection', 1, 'ALL', 2, 1, @Now, @Now, 1),\n    ('SEC-OPD1-03', 'TMPL-OPD-FIRST', 'SEC_EXAM', '体格检查与专科排他检查 (GATE-03)', 'GRID_FORM', '{\"elements\":[\"PHYSICAL_EXAM\",\"DE_PROSTATE_EXAM\",\"DE_GRAVIDITY\",\"DE_PARITY\",\"DE_MENSTRUAL_HISTORY\"]}', 'EMR.Opd.ExamSection', 1, 'ALL', 3, 1, @Now, @Now, 1),\n    ('SEC-OPD1-04', 'TMPL-OPD-FIRST', 'SEC_DIAG_PLAN', '初步诊断与诊疗处置计划', 'INLINE_PARAGRAPH', '{\"elements\":[\"PRIMARY_DIAGNOSIS\",\"TREATMENT_PLAN\"]}', 'EMR.Opd.DiagPlanSection', 1, 'ALL', 4, 1, @Now, @Now, 1),\n\n    ('SEC-OPD2-01', 'TMPL-OPD-FOLLOWUP', 'SEC_FOLLOWUP_MAIN', '复诊主诉与慢病控制评估', 'GRID_FORM', '{\"elements\":[\"CHIEF_COMPLAINT\",\"PRESENT_ILLNESS\",\"AUX_EXAM\"]}', 'EMR.Opd.FollowupMain', 1, 'ALL', 1, 1, @Now, @Now, 1),\n    ('SEC-OPD2-02', 'TMPL-OPD-FOLLOWUP', 'SEC_FOLLOWUP_PLAN', '复诊诊断与续方调整方案', 'INLINE_PARAGRAPH', '{\"elements\":[\"PRIMARY_DIAGNOSIS\",\"TREATMENT_PLAN\"]}', 'EMR.Opd.FollowupPlan', 1, 'ALL', 2, 1, @Now, @Now, 1),\n\n    ('SEC-OPD3-01', 'TMPL-OPD-CARDIO', 'SEC_CARDIO_CHIEF', '心前区主诉与发作特征', 'GRID_FORM', '{\"elements\":[\"CHIEF_COMPLAINT\",\"PRESENT_ILLNESS\"]}', 'EMR.Opd.CardioChief', 1, 'ALL', 1, 1, @Now, @Now, 1),\n    ('SEC-OPD3-02', 'TMPL-OPD-CARDIO', 'SEC_CARDIO_RISK', '心血管危险因素与既往用药', 'GRID_FORM', '{\"elements\":[\"PAST_HISTORY\",\"ALLERGY_HISTORY\"]}', 'EMR.Opd.CardioRisk', 1, 'ALL', 2, 1, @Now, @Now, 1),\n    ('SEC-OPD3-03', 'TMPL-OPD-CARDIO', 'SEC_CARDIO_EXAM', '心脏专科体征与排他查体', 'GRID_FORM', '{\"elements\":[\"PHYSICAL_EXAM\",\"DE_PROSTATE_EXAM\",\"DE_GRAVIDITY\",\"AUX_EXAM\"]}', 'EMR.Opd.CardioExam', 1, 'ALL', 3, 1, @Now, @Now, 1),\n    ('SEC-OPD3-04', 'TMPL-OPD-CARDIO', 'SEC_CARDIO_PLAN', '初步心血管诊断与诊疗方案', 'INLINE_PARAGRAPH', '{\"elements\":[\"PRIMARY_DIAGNOSIS\",\"TREATMENT_PLAN\"]}', 'EMR.Opd.CardioPlan', 1, 'ALL', 4, 1, @Now, @Now, 1),\n\n    ('SEC-OPD4-01', 'TMPL-OPD-PEDIATRIC', 'SEC_PED_CHIEF', '患儿主诉与起病急缓', 'GRID_FORM', '{\"elements\":[\"CHIEF_COMPLAINT\",\"PRESENT_ILLNESS\"]}', 'EMR.Opd.PedChief', 1, 'ALL', 1, 1, @Now, @Now, 1),\n    ('SEC-OPD4-02', 'TMPL-OPD-PEDIATRIC', 'SEC_PED_EXAM', '儿科体格检查与生命体征', 'GRID_FORM', '{\"elements\":[\"PHYSICAL_EXAM\",\"ALLERGY_HISTORY\"]}', 'EMR.Opd.PedExam', 1, 'ALL', 2, 1, @Now, @Now, 1),\n    ('SEC-OPD4-03', 'TMPL-OPD-PEDIATRIC', 'SEC_PED_PLAN', '初步诊断与儿童用药处置', 'INLINE_PARAGRAPH', '{\"elements\":[\"PRIMARY_DIAGNOSIS\",\"TREATMENT_PLAN\"]}', 'EMR.Opd.PedPlan', 1, 'ALL', 3, 1, @Now, @Now, 1),\n\n    ('SEC-IPD1-01', 'TMPL-IPD-ADMISSION', 'SEC_IPD_HISTORY', '入院主诉、现病史与既往过敏史', 'INLINE_PARAGRAPH', '{\"elements\":[\"CHIEF_COMPLAINT\",\"PRESENT_ILLNESS\",\"PAST_HISTORY\",\"ALLERGY_HISTORY\"]}', 'EMR.Ipd.History', 1, 'ALL', 1, 1, @Now, @Now, 1),\n    ('SEC-IPD1-02', 'TMPL-IPD-ADMISSION', 'SEC_IPD_PHYSICAL', '系统体格检查与专科情况', 'GRID_FORM', '{\"elements\":[\"PHYSICAL_EXAM\",\"DE_PROSTATE_EXAM\",\"DE_GRAVIDITY\",\"DE_PARITY\",\"AUX_EXAM\"]}', 'EMR.Ipd.Physical', 1, 'ALL', 2, 1, @Now, @Now, 1),\n    ('SEC-IPD1-03', 'TMPL-IPD-ADMISSION', 'SEC_IPD_DIAG', '入院诊断与三级医师诊疗计划', 'INLINE_PARAGRAPH', '{\"elements\":[\"ADMISSION_DIAGNOSIS\",\"TREATMENT_PLAN\"]}', 'EMR.Ipd.Diagnosis', 1, 'ALL', 3, 1, @Now, @Now, 1),\n\n    ('SEC-IPD2-01', 'TMPL-IPD-PROGRESS', 'SEC_PROG_WARDROUND', '三级医师查房记录与病情评估', 'INLINE_PARAGRAPH', '{\"elements\":[\"PROGRESS_CONTENT\",\"AUX_EXAM\"]}', 'EMR.Ipd.Progress', 1, 'ALL', 1, 1, @Now, @Now, 1),\n    ('SEC-IPD2-02', 'TMPL-IPD-PROGRESS', 'SEC_PROG_ORDERPLAN', '医嘱调整与后续观察重点', 'INLINE_PARAGRAPH', '{\"elements\":[\"ADMISSION_DIAGNOSIS\",\"TREATMENT_PLAN\"]}', 'EMR.Ipd.ProgressPlan', 1, 'ALL', 2, 1, @Now, @Now, 1),\n\n    ('SEC-IPD3-01', 'TMPL-IPD-DISCHARGE', 'SEC_DIS_SUMMARY', '入院情况与住院诊疗经过', 'INLINE_PARAGRAPH', '{\"elements\":[\"ADMISSION_DIAGNOSIS\",\"DISCHARGE_COURSE\"]}', 'EMR.Ipd.DischargeCourse', 1, 'ALL', 1, 1, @Now, @Now, 1),\n    ('SEC-IPD3-02', 'TMPL-IPD-DISCHARGE', 'SEC_DIS_ADVICE', '出院诊断、带药指导与随访计划', 'INLINE_PARAGRAPH', '{\"elements\":[\"PRIMARY_DIAGNOSIS\",\"DISCHARGE_ADVICE\"]}', 'EMR.Ipd.DischargeAdvice', 1, 'ALL', 2, 1, @Now, @Now, 1),\n\n    ('SEC-WARD1-01', 'TMPL-WARD-ADMISSION', 'SEC_NUR_GENERAL', '入科一般资料与生命体征基线', 'GRID_FORM', '{\"elements\":[\"NURSING_CONSCIOUSNESS\",\"ALLERGY_HISTORY\",\"PHYSICAL_EXAM\"]}', 'EMR.Ward.General', 1, 'ALL', 1, 1, @Now, @Now, 1),\n    ('SEC-WARD1-02', 'TMPL-WARD-ADMISSION', 'SEC_NUR_SCALES', '护理三评量表 (压疮Braden / 自理Barthel / 跌倒Morse)', 'TABLE_GRID', '{\"elements\":[\"NURSING_BRADEN_SCORE\",\"NURSING_BARTHEL_SCORE\",\"NURSING_FALL_SCORE\"]}', 'EMR.Ward.Scales', 1, 'ALL', 2, 1, @Now, @Now, 1),\n    ('SEC-WARD1-03', 'TMPL-WARD-ADMISSION', 'SEC_NUR_PLAN', '专科护理措施与健康宣教记录', 'INLINE_PARAGRAPH', '{\"elements\":[\"NURSING_INTERVENTION\"]}', 'EMR.Ward.Plan', 1, 'ALL', 3, 1, @Now, @Now, 1),\n\n    ('SEC-WARD2-01', 'TMPL-WARD-CRITICAL', 'SEC_CRIT_VITALS', '危重患者严密生命体征与神志监测', 'TABLE_GRID', '{\"elements\":[\"NURSING_CONSCIOUSNESS\",\"PHYSICAL_EXAM\",\"NURSING_BRADEN_SCORE\",\"NURSING_FALL_SCORE\"]}', 'EMR.Ward.CritVitals', 1, 'ALL', 1, 1, @Now, @Now, 1),\n    ('SEC-WARD2-02', 'TMPL-WARD-CRITICAL', 'SEC_CRIT_ACTION', '特级护理管路维护与抢救护理记录', 'INLINE_PARAGRAPH', '{\"elements\":[\"NURSING_INTERVENTION\"]}', 'EMR.Ward.CritAction', 1, 'ALL', 2, 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
				await conn.ExecuteAsync(@"INSERT OR IGNORE INTO emr_user_templates
    (id, user_id, user_name, dept_id, dept_name, base_template_id, template_code, template_name, emr_scene, doc_type_code, default_content_json, is_shared_dept, sort_order, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('UTMPL-IPD-CORONARY', 'USER-DOC-ATTENDING', '李主治', 'DEPT-IPD-01', '心血管内科一病区', 'TMPL-IPD-PROGRESS', 'UTMPL_CORONARY_ROUND', '心血管内科-冠心病初次查房常用模板', 'IPD', 'IPD_PROGRESS', '{""PROGRESS_CONTENT"":""今日查房：患者诉胸闷偶发，无夜间端坐呼吸，双下肢无浮肿。查体心率72次/分，律齐，未闻及明显杂音。心电图复查ST段回升，肌钙蛋白趋势下降。处置：继续强化抗血小板、调脂稳定斑块治疗，密切监测血压心率变化。"",""AUX_EXAM"":""TNT: 0.08 ng/mL, BNP: 145 pg/mL, 心电图窦性心律""}', 1, 1, 1, 1, @Now, 'USER-DOC-ATTENDING', @Now, 'USER-DOC-ATTENDING', 1),
    ('UTMPL-IPD-HEARTFAIL', 'USER-DOC-ATTENDING', '李主治', 'DEPT-IPD-01', '心血管内科一病区', 'TMPL-IPD-ADMISSION', 'UTMPL_HF_ADMISSION', '心力衰竭入院记录常用模板 (NYHA III级)', 'IPD', 'IPD_ADMISSION', '{""CHIEF_COMPLAINT"":""活动后气促伴双下肢水肿1周，加重2天"",""PRESENT_ILLNESS"":""患者1周前劳累后出现气促、胸闷，夜间需高枕卧位，近2天双下肢水肿明显加重。既往有高血压病史10年。"",""PAST_HISTORY"":""高血压病病史10年，规律服用降压药；否认糖尿病、肝肾疾病史。"",""ALLERGY_HISTORY"":""否认明确药物及食物过敏史"",""PHYSICAL_EXAM"":""T 36.6℃, P 88次/分, R 20次/分, BP 142/88mmHg。双下肺可闻及细湿啰音，心界向左下扩大，双下肢中度可凹性水肿。"",""ADMISSION_DIAGNOSIS"":""1. 慢性充血性心力衰竭 (NYHA III级)\n2. 原发性高血压 3级 (很高危)"",""TREATMENT_PLAN"":""1. 心内科特级护理，低盐低脂饮食\n2. 利尿减轻心脏负荷，改善心室重构\n3. 监测每日出入量与电解质""}', 0, 2, 1, 1, @Now, 'USER-DOC-ATTENDING', @Now, 'USER-DOC-ATTENDING', 1),
    ('UTMPL-WARD-POSTOP', 'USER-NURSE-W2', '陈护士', 'DEPT-WARD-01', '心血管内科病区', 'TMPL-WARD-CRITICAL', 'UTMPL_CRIT_POSTOP', '危重症特级护理-术后观察常用模板', 'WARD', 'NURSING_CRITICAL', '{""NURSING_CONSCIOUSNESS"":""神志清楚，精神可，自主体位，对答切题"",""PHYSICAL_EXAM"":""心电监护示窦性心律，心率75次/分，律齐，SpO2 98%，穿刺点敷料干燥无渗血渗液"",""NURSING_BRADEN_SCORE"":""15"",""NURSING_FALL_SCORE"":""35"",""NURSING_INTERVENTION"":""1. 予以心电、无创血压、脉搏血氧饱和度持续监护；\n2. 保持呼吸道通畅，低流量吸氧2L/min；\n3. 密切观察穿刺侧肢体皮温、足背动脉搏动及局部血肿情况；\n4. 落实防跌倒、防压疮护理措施，严格交接班。""}', 1, 1, 1, 1, @Now, 'USER-NURSE-W2', @Now, 'USER-NURSE-W2', 1),
    ('UTMPL-WARD-ELDERLY', 'USER-NURSE-W2', '陈护士', 'DEPT-WARD-01', '心血管内科病区', 'TMPL-WARD-ADMISSION', 'UTMPL_NUR_ELDERLY', '高龄患者入院三评与护理评估模板', 'WARD', 'NURSING_ADMISSION', '{""NURSING_CONSCIOUSNESS"":""神清，慢性病容，步态不稳需家属搀扶"",""ALLERGY_HISTORY"":""青霉素过敏"",""PHYSICAL_EXAM"":""T 36.5℃, P 76次/分, R 18次/分, BP 130/80mmHg，皮肤弹性稍差，无破损"",""NURSING_BRADEN_SCORE"":""13"",""NURSING_BARTHEL_SCORE"":""55"",""NURSING_FALL_SCORE"":""50"",""NURSING_INTERVENTION"":""1. 入院宣教并挂黄色跌倒高危警示牌，床栏双侧拉起；\n2. 指导家属24小时专人陪护，下床时执行3个半分钟原则；\n3. 评估骶尾部受压处皮肤，每2小时翻身拍背1次；\n4. 告知呼叫器使用方法置于手边方便处。""}', 0, 2, 1, 1, @Now, 'USER-NURSE-W2', @Now, 'USER-NURSE-W2', 1),
    ('UTMPL-OPD-CAD', 'USER-DOC-OPD', '张建国', 'DEPT-OPD-01', '心血管内科门诊', 'TMPL-OPD-FIRST', 'UTMPL_CAD_FIRST', '心血管内科-冠心病劳力性心绞痛门诊初诊模板', 'OPD', 'OPD_FIRST', '{""CHIEF_COMPLAINT"":""反复发作性心前区闷痛2周，加重2天"",""PRESENT_ILLNESS"":""患者2周前于快步行走或劳累时出现心前区闷痛，向左肩背部放射，持续约3-5分钟，休息后可自行缓解。近2天发作频率增加，轻微活动即感胸闷，无夜间阵发性呼吸困难、无心悸晕厥。"",""PAST_HISTORY"":""有高血压病史5年，规律服用降压药物，血压控制尚可；否认糖尿病、肝肾疾病史。"",""ALLERGY_HISTORY"":""否认明确食物及药物过敏史"",""PHYSICAL_EXAM"":""BP 138/84mmHg, HR 76次/分, 律齐, 各瓣膜听诊区未闻及病理性杂音, 双下肢无水肿。"",""PRIMARY_DIAGNOSIS"":""1. 冠状动脉粥样硬化性心脏病 (劳力性心绞痛 CCS II级)\n2. 原发性高血压 2级 (很高危)"",""TREATMENT_PLAN"":""1. 完善静息心电图、心肌钙蛋白I及心脏彩超检查\n2. 予以阿司匹林肠溶片抗血小板、阿托伐他汀钙片调脂稳定斑块\n3. 门诊心内科随访，若胸痛持续加重需立即急诊就诊""}', 1, 1, 1, 1, @Now, 'USER-DOC-OPD', @Now, 'USER-DOC-OPD', 1),
    ('UTMPL-OPD-HTN', 'USER-DOC-OPD', '张建国', 'DEPT-OPD-01', '心血管内科门诊', 'TMPL-OPD-FOLLOWUP', 'UTMPL_HTN_FOLLOWUP', '心血管内科-高血压慢病规范随访模板', 'OPD', 'OPD_FOLLOWUP', '{""CHIEF_COMPLAINT"":""确诊原发性高血压3年，门诊常规复诊与配药"",""PRESENT_ILLNESS"":""患者规律服用氨氯地平片5mg qd，近期自测晨起血压在130-140/80-88mmHg之间，偶有轻微头昏，无视物旋转，无心慌胸闷，二便正常，睡眠一般。"",""AUX_EXAM"":""今日门诊复测血压 135/82mmHg，心率 72次/分，心律规整。近期生化示肝肾功能及血脂基本正常。"",""PRIMARY_DIAGNOSIS"":""原发性高血压 2级 (高危)"",""TREATMENT_PLAN"":""1. 继续维持当前降压药物治疗方案，坚持低盐低脂饮食\n2. 每日早晚规律监测血压并做日记记录\n3. 4周后门诊慢病专科复诊，必要时联合降压药物""}', 1, 2, 1, 1, @Now, 'USER-DOC-OPD', @Now, 'USER-DOC-OPD', 1),
    ('UTMPL-OPD-GASTRO', 'USER-DOC-OPD', '张建国', 'DEPT-OPD-01', '心血管内科门诊', 'TMPL-OPD-FIRST', 'UTMPL_GASTRO_FIRST', '张医师-急性胃肠炎门诊初诊个人模板', 'OPD', 'OPD_FIRST', '{""CHIEF_COMPLAINT"":""腹痛伴腹泻、恶心1天"",""PRESENT_ILLNESS"":""患者昨日进食不洁生冷食物后突发脐周阵发性隐痛，随后出现水样便4次，伴恶心、反酸，无明显发热、无里急后重及脓血便。"",""PAST_HISTORY"":""平素健康，无慢性腹泻及消化道溃疡病史。"",""ALLERGY_HISTORY"":""青霉素过敏"",""PHYSICAL_EXAM"":""腹平软，脐周轻度压痛，无反跳痛及肌紧张，肠鸣音活跃约6-7次/分。"",""PRIMARY_DIAGNOSIS"":""急性胃肠炎"",""TREATMENT_PLAN"":""1. 急查便常规+潜血、血常规及电解质\n2. 口服补液盐纠正脱水，蒙脱石散止泻收敛，抗痉挛对症支持\n3. 忌油腻生冷辛辣，半流质饮食，若出现高热脱水症状即刻复诊""}', 0, 3, 1, 1, @Now, 'USER-DOC-OPD', @Now, 'USER-DOC-OPD', 1)", new
		{
			Now = nowMs
		});
		string pat1IdCard = "330106198506182215";
		string demoPatIdCard = "110101197812110052";
		await conn.ExecuteAsync(@"INSERT OR IGNORE INTO pat_patients
    (id, medical_record_no, full_name, pinyin_code, gender_code, birth_date_int, id_card_type, id_card_cipher, id_card_hash, phone_cipher, phone_masked, guardian_name, guardian_id_type, guardian_id_cipher, guardian_phone_cipher, guardian_relation_code, nation_code, marital_status_code, occupation_code, abo_blood_code, rh_blood_code, address_region_code, address_detail, allergy_tags_json, is_temp_emergency, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('PAT85061801', 'PAT85061801', '周建强', 'ZJQ', '1', 19850618, '01', @Pat1IdCardCipher, @Pat1IdCardHash, @Pat1PhoneCipher, '138****1801', '周父', '01', @Pat1GuardIdCipher, @Pat1GuardPhoneCipher, '1', '01', '20', '11', 'A', 'RH_POS', '330106', '浙江省杭州市西湖区文三路100号', '[""青霉素"",""磺胺类""]', 0, 1, 1, @Now, 'USER-ADMIN', @Now, 'USER-ADMIN', 1),
    ('PAT-PHM-DEMO-01', 'PAT20261105002', '钱学森', 'QXS', '1', 19781211, '01', @DemoIdCardCipher, @DemoIdCardHash, @DemoPhoneCipher, '139****5678', NULL, NULL, NULL, NULL, NULL, '01', '20', '01', 'O', 'RH_POS', '110101', '北京市东城区东长安街1号', '[]', 0, 1, 1, @Now, 'USER-ADMIN', @Now, 'USER-ADMIN', 1)", new
		{
			Now = nowMs,
			Pat1IdCardCipher = CryptoHelper.EncryptSm4Gcm(pat1IdCard),
			Pat1IdCardHash = CryptoHelper.ComputeBlindIndex(pat1IdCard),
			Pat1PhoneCipher = CryptoHelper.EncryptSm4Gcm("13800001801"),
			Pat1GuardIdCipher = CryptoHelper.EncryptSm4Gcm("330106195503022218"),
			Pat1GuardPhoneCipher = CryptoHelper.EncryptSm4Gcm("13800001802"),
			DemoIdCardCipher = CryptoHelper.EncryptSm4Gcm(demoPatIdCard),
			DemoIdCardHash = CryptoHelper.ComputeBlindIndex(demoPatIdCard),
			DemoPhoneCipher = CryptoHelper.EncryptSm4Gcm("13900005678")
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO opd_visits\n    (id, visit_no, campus_id, opd_dept_id, clinic_room_id, patient_id, schedule_id, doctor_id, is_emergency, is_green_channel, is_return_visit, status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)\nVALUES\n    ('VISIT-OPD-20261105001', 'OPD20261105001', 'CAMPUS-01', 'DEPT-OPD-01', 'ROOM-OPD-101', 'PAT85061801', NULL, 'USER-DOC-OPD', 0, 0, 0, 2, 1, @Now, 'USER-DOC-OPD', @Now, 'USER-DOC-OPD', 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO ipd_encounters\n    (id, admission_no, patient_id, campus_id, admit_dept_id, admit_ward_id, is_borrowed_bed, bed_code, active_bed_token, attending_doctor_id, primary_nurse_id, nursing_level_code, prepayment_balance, status, triage_status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)\nVALUES\n    ('ADM20261105001', 'ADM20261105001', 'PAT85061801', 'CAMPUS-01', 'DEPT-IPD-01', 'DEPT-WARD-02', 1, '02', 'BED-04', 'USER-DOC-ATTENDING', 'USER-NURSE-W2', 'LEVEL_1', 0.00, 1, 1, 1, @Now, 'USER-CASHIER-01', @Now, 'USER-CASHIER-01', 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("UPDATE ipd_beds\nSET bed_status = 1, current_encounter_id = 'ADM20261105001', updated_at_ms = @Now, version_no = version_no + 1\nWHERE id = 'BED-04' AND (bed_status = 0 OR current_encounter_id = 'ADM20261105001')", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO opd_triage_queues\n    (id, visit_id, opd_dept_id, clinic_room_id, priority_score, pass_count, queue_status, checkin_at_ms, called_at_ms, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n    ('QUEUE-SEED-OPD-01', 'VISIT-OPD-20261105001', 'DEPT-OPD-01', 'ROOM-OPD-101', 0, 0, 3, @Now, @Now, 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("UPDATE emr_documents\nSET visit_or_encounter_id = 'VISIT-OPD-20261105001', updated_at_ms = @Now\nWHERE id = 'DOC-SEED-OPD-01' AND visit_or_encounter_id = 'OPD20261105001'", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("""
INSERT OR IGNORE INTO emr_documents
    (id, doc_no, campus_id, emr_scene, visit_or_encounter_id, patient_id, dept_id, ward_id, template_id, template_version, doc_type_code, doc_title, clinical_time_ms, structured_ast_json, plain_text_summary, status, author_id, resident_signed_at_ms, attending_doctor_id, attending_signed_at_ms, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES
    ('DOC-SEED-OPD-01', 'DOC-OPD-20261105001', 'CAMPUS-01', 'OPD', 'VISIT-OPD-20261105001', 'PAT85061801', 'DEPT-OPD-01', NULL, 'TMPL-OPD-FIRST', 1, 'OPD_FIRST_VISIT', '心血管内科门诊初诊病历', @Now,
     '{"CHIEF_COMPLAINT":"阵发性心前区隐痛、心悸3天，活动后加重半天。","PRESENT_ILLNESS":"患者3天前过度劳累后出现心前区憋闷不适，伴阵发性心悸，持续3-5分钟。半天前登山后胸痛加重，放射至左肩背部。","PAST_HISTORY":"高血压病史5年，最高165/100mmHg。","ALLERGY_HISTORY":"青霉素过敏史, 磺胺类过敏","PHYSICAL_EXAM":"T 36.5℃，P 82次/分，R 18次/分，BP 138/86mmHg，心界不大，心律齐。","DE_PROSTATE_EXAM":"轻度肥大，质地韧，中央沟浅，未触及明显硬结。","PRIMARY_DIAGNOSIS":"冠状动脉粥样硬化性心脏病 (I25.101)；劳力性心绞痛","TREATMENT_PLAN":"1. 查心肌酶谱、12导联心电图；2. 予阿司匹林、氯吡格雷双抗血小板治疗；3. 建议收入心内科住院进一步行冠脉造影。"}',
     '主诉：阵发性心前区隐痛、心悸3天，活动后加重半天。诊断：冠状动脉粥样硬化性心脏病 (I25.101)。', 2, 'USER-DOC-OPD', @Now, 'USER-DOC-OPD', @Now, 1, @Now, @Now, 1),
    ('DOC-SEED-IPD-01', 'DOC-IPD-20261105001', 'CAMPUS-01', 'IPD', 'ADM20261105001', 'PAT85061801', 'DEPT-IPD-01', 'DEPT-WARD-02', 'TMPL-IPD-ADMISSION', 1, 'IPD_ADMISSION', '心血管内科住院24小时入院记录', @Now,
     '{"CHIEF_COMPLAINT":"反复胸闷痛3天，加重伴大汗2小时入院。","PRESENT_ILLNESS":"患者因急性非ST段抬高型心肌梗死由门诊急收入院，跨病区借床收治于内科二病区02床。","PAST_HISTORY":"高血压病史5年，高脂血症2年。","ALLERGY_HISTORY":"青霉素过敏史, 磺胺类过敏","PHYSICAL_EXAM":"T 36.6℃，P 84次/分，R 19次/分，BP 135/82mmHg，双肺呼吸音清，心率84次/分，律齐。","AUX_EXAM":"超敏肌钙蛋白I (hs-cTnI) 12.85 ng/mL (危急值)，心电图示V1-V4导联ST段压低0.15mV。","ADMISSION_DIAGNOSIS":"急性非ST段抬高型心肌梗死 (I21.401)；冠状动脉粥样硬化性心脏病 (I25.101)","TREATMENT_PLAN":"1. 一级护理、持续心电血氧监护；2. 双联抗血小板+他汀强化降脂；3. 急诊导管室行经皮冠状动脉造影及支架植入术 (PCI)。"}',
     '住院24小时入院记录：急性非ST段抬高型心肌梗死 (I21.401)，已启动胸痛中心绿色通道拟行PCI。', 2, 'USER-DOC-RESIDENT', @Now, 'USER-DOC-ATTENDING', @Now, 1, @Now, @Now, 1),
    ('DOC-SEED-WARD-01', 'DOC-WARD-20261105001', 'CAMPUS-01', 'WARD', 'ADM20261105001', 'PAT85061801', 'DEPT-IPD-01', 'DEPT-WARD-02', 'TMPL-WARD-ADMISSION', 1, 'NURSING_ADMISSION', '住院患者入院护理评估与三评记录单', @Now,
     '{"NURSING_CONSCIOUSNESS":"神志清楚，精神稍紧张，对答切题","ALLERGY_HISTORY":"青霉素过敏史, 磺胺类过敏 (已佩戴红色过敏腕带)","PHYSICAL_EXAM":"T 36.5℃，P 78次/分，R 18次/分，BP 128/78mmHg，SpO2 98% (鼻导管吸氧2L/min)","NURSING_BRADEN_SCORE":"19","NURSING_BARTHEL_SCORE":"75","NURSING_FALL_SCORE":"45","NURSING_INTERVENTION":"1. 严格落实跨病区借床患者腕带双人扫码核对及红色过敏标识；2. 持续心电、血压、指脉氧监护，每小时巡视记录；3. Morse跌倒评分45分（中风险），已拉起床档并悬挂防跌倒警示牌；4. 做好急诊PCI术前双侧桡动脉备皮及心理疏导。"}',
     '入院护理评估单：神志清楚，Braden评分19分(低危)，Barthel评分75分(轻度依赖)，Morse跌倒评分45分(中风险)，已落实防跌倒与过敏腕带护理措施。', 2, 'USER-NURSE-W2', @Now, NULL, NULL, 1, @Now, @Now, 1)
""", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO inv_drugs (id, drug_code, drug_name, spec, unit_name, price, cost_price, antibiotic_level, is_narcotic, is_skin_test_required, national_ins_code, reimburse_type, self_pay_ratio, min_dose, min_dose_unit, base_unit_name, pack_factor, is_split_allowed_ipd, approval_no, manufacturer, is_active, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n('DRUG-01', 'PENICILLIN_G', '注射用青霉素钠', '80万单位/支', '支', 12.50, 8.00, 1, 0, 1, 'J01CE01001000100', 'CLASS_A', 0, 800000, '单位', '支', 1, 0, '国药准字H13020655', '石药集团', 1, 1, @Now, @Now, 1),\n('DRUG-02', 'AMOXICILLIN_CAPS', '阿莫西林胶囊', '0.25g*24粒/盒', '盒', 18.00, 11.20, 1, 0, 1, 'J01CA04002000100', 'CLASS_A', 0, 250, 'mg', '粒', 24, 1, '国药准字H44021351', '珠海联邦制药', 1, 1, @Now, @Now, 1),\n('DRUG-03', 'NORMAL_SALINE_500', '0.9%氯化钠注射液', '500ml/瓶', '瓶', 5.50, 3.20, 0, 0, 0, 'B05BB01001000100', 'CLASS_A', 0, 500, 'ml', '瓶', 1, 0, '国药准字H20033100', '华润双鹤药业', 1, 1, @Now, @Now, 1),\n('DRUG-04', 'DRUG-CARD-006', '阿司匹林肠溶片', '100mg*30片/盒', '盒', 22.50, 14.00, 0, 0, 0, 'B01AC06001000100', 'CLASS_A', 0, 100, 'mg', '片', 30, 1, '国药准字H20065051', '拜耳医药保健', 1, 1, @Now, @Now, 1),\n('DRUG-05', 'DRUG-CARD-005', '硫酸氢氯吡格雷片', '75mg*7片/盒', '盒', 56.80, 38.00, 0, 0, 0, 'B01AC04001000100', 'CLASS_B', 0.1000, 75, 'mg', '片', 7, 1, '国药准字H20000564', '赛诺菲杭州制药', 1, 1, @Now, @Now, 1),\n('DRUG-06', 'DRUG-LIPID-001', '阿托伐他汀钙片', '20mg*7片/盒', '盒', 84.50, 52.00, 0, 0, 0, 'X01AA01001000100', 'CLASS_A', 0, 20, 'mg', '片', 7, 1, '国药准字H20051408', '北京新时代制药', 1, 1, @Now, @Now, 1),\n('DRUG-07', 'INSULIN_GLARGINE', '甘精胰岛素注射液', '3ml:300单位/支', '支', 145.00, 98.00, 0, 0, 0, 'A10AE04001000100', 'CLASS_B', 0.1000, 300, '单位', '支', 1, 0, '国药准字S20100015', '吉林生物制品有限公司', 1, 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO inv_drug_batches (id, warehouse_or_pharmacy_id, drug_id, batch_no, expiry_date_int, invoice_no, cold_chain_temp_celsius, quantity, is_frozen, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n('BATCH-01-NEAR', 'DEPT-PHM-01', 'DRUG-01', 'LOT202601A', 20270301, 'INV-2026-001', NULL, 50, 0, 1, @Now, @Now, 1),\n('BATCH-01-FAR',  'DEPT-PHM-01', 'DRUG-01', 'LOT202606B', 20280601, 'INV-2026-002', NULL, 200, 0, 1, @Now, @Now, 1),\n('BATCH-02-MAIN', 'DEPT-PHM-01', 'DRUG-02', 'LOT202604A', 20271231, 'INV-2026-003', NULL, 150, 0, 1, @Now, @Now, 1),\n('BATCH-03-NEAR', 'DEPT-PHM-01', 'DRUG-03', 'LOT202602S', 20270501, 'INV-2026-004', NULL, 100, 0, 1, @Now, @Now, 1),\n('BATCH-03-FAR',  'DEPT-PHM-01', 'DRUG-03', 'LOT202608S', 20280801, 'INV-2026-005', NULL, 300, 0, 1, @Now, @Now, 1),\n('BATCH-04-MAIN', 'DEPT-PHM-01', 'DRUG-04', 'LOT202605C', 20280501, 'INV-2026-006', NULL, 320, 0, 1, @Now, @Now, 1),\n('BATCH-05-MAIN', 'DEPT-PHM-01', 'DRUG-05', 'LOT202605D', 20280501, 'INV-2026-007', NULL, 180, 0, 1, @Now, @Now, 1),\n('BATCH-WH1-01',  'DEPT-WH-01',  'DRUG-01', 'LOT202609W', 20280901, 'INV-2026-101', NULL, 1000, 0, 1, @Now, @Now, 1),\n('BATCH-WH1-03',  'DEPT-WH-01',  'DRUG-03', 'LOT202610W', 20281001, 'INV-2026-102', NULL, 2000, 0, 1, @Now, @Now, 1),\n('BATCH-WH1-04',  'DEPT-WH-01',  'DRUG-04', 'LOT202611W', 20281101, 'INV-2026-103', NULL, 1500, 0, 1, @Now, @Now, 1),\n('BATCH-WH2-05',  'DEPT-WH-02',  'DRUG-07', 'LOT202612C', 20281201, 'INV-2026-104', 4.2, 600, 0, 1, @Now, @Now, 1),\n('BATCH-PHM2-03', 'DEPT-PHM-02', 'DRUG-03', 'LOT202608S', 20280801, 'INV-2026-005', NULL, 400, 0, 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO inv_drug_price_logs\n    (id, adjust_no, drug_id, drug_code, drug_name, old_retail_price, new_retail_price, old_cost_price, new_cost_price, affected_stock_quantity, price_diff_amount, adjust_reason, effective_at_ms, operator_id, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n    ('PRCLOG-01', 'PRC-ADJ-20261101-01', 'DRUG-04', 'DRUG-CARD-006', '阿司匹林肠溶片', 25.00, 22.50, 15.50, 14.00, 1820, -4550.00, '国家第九批药品集中带量采购中选零差率调价', @Now, 'USER-ADMIN', 1, @Now, @Now, 1),\n    ('PRCLOG-02', 'PRC-ADJ-20261103-02', 'DRUG-05', 'DRUG-CARD-005', '硫酸氢氯吡格雷片', 62.00, 56.80, 41.00, 38.00, 780, -4056.00, '省医保集采续约联动降价', @Now, 'USER-ADMIN', 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO inv_transfer_orders\n    (id, transfer_no, transfer_type, source_warehouse_id, target_warehouse_id, drug_id, drug_name_snapshot, batch_id, batch_no, expiry_date_int, quantity, unit_cost_price, unit_retail_price, total_retail_amount, status, applicant_id, dispatched_by_id, dispatched_at_ms, received_by_id, received_at_ms, remark, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n    ('TRF-SEED-01', 'ISS-20261105-001', 'WAREHOUSE_ISSUE', 'DEPT-WH-01', 'DEPT-PHM-01', 'DRUG-04', '阿司匹林肠溶片', 'BATCH-WH1-04', 'LOT202611W', 20281101, 120, 14.00, 22.50, 2700.00, 2, 'USER-PHM-01', 'USER-ADMIN', @Now, 'USER-PHM-01', @Now, '门诊西药房周常补货请领单 (药库FEFO发药出库)', 1, @Now, @Now, 1),\n    ('TRF-SEED-02', 'TRF-20261106-002', 'INTER_WAREHOUSE', 'DEPT-PHM-01', 'DEPT-PHM-02', 'DRUG-03', '0.9%氯化钠注射液', 'BATCH-03-FAR', 'LOT202608S', 20280801, 50, 3.20, 5.50, 275.00, 2, 'USER-PHM-01', 'USER-PHM-01', @Now, 'USER-PHM-01', @Now, '门诊西药房向住院中心药房跨库房应急调拨', 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO sys_roles (id, role_code, role_name, data_scope, is_active, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n('ROLE-ADMIN', 'ADMIN', '超级管理员', 'ALL_CAMPUS', 1, 1, @Now, @Now, 1),\n('ROLE-OPD-DOC', 'OPD_DOC', '门诊医师', 'OPD_DEPT_SCOPE', 1, 1, @Now, @Now, 1),\n('ROLE-IPD-RESIDENT', 'IPD_RESIDENT', '住院医师', 'IPD_DEPT_AND_BORROWED', 1, 1, @Now, @Now, 1),\n('ROLE-IPD-ATTENDING', 'IPD_ATTENDING', '主治/主任医师', 'IPD_DEPT_AND_BORROWED', 1, 1, @Now, @Now, 1),\n('ROLE-WARD-NURSE', 'WARD_NURSE', '病区护士', 'NURSING_WARD_SCOPE', 1, 1, @Now, @Now, 1),\n('ROLE-PHARMACIST', 'PHARMACIST', '药师', 'MEDTECH_EXEC_SCOPE', 1, 1, @Now, @Now, 1),\n('ROLE-CASHIER', 'CASHIER', '收费员', 'ALL_CAMPUS', 1, 1, @Now, @Now, 1),\n('ROLE-MEDTECH', 'MEDTECH', '医技技师', 'MEDTECH_EXEC_SCOPE', 1, 1, @Now, @Now, 1),\n('ROLE-WAREHOUSE', 'WAREHOUSE_KEEPER', '药库管理员', 'ALL_CAMPUS', 1, 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO sys_roles (id, role_code, role_name, data_scope, is_active, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n('ROLE-ENDOCRINOLOGIST', 'ENDOCRINOLOGIST', '内分泌科医师', 'OPD_DEPT_SCOPE', 1, 1, @Now, @Now, 1),\n('ROLE-CARDIOLOGIST', 'CARDIOLOGIST', '心内科医师', 'OPD_DEPT_SCOPE', 1, 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO sys_role_permissions (id, role_id, resource_key, access_policy, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n('PERM-01', 'ROLE-OPD-DOC', 'OPD.ConsultView.BtnSignPrescription', 3, 1, @Now, @Now, 1),\n('PERM-02', 'ROLE-IPD-ATTENDING', 'EMR.Toolbar.BtnExportOfd', 3, 1, @Now, @Now, 1),\n('PERM-03', 'ROLE-IPD-RESIDENT', 'EMR.Toolbar.BtnExportOfd', 2, 1, @Now, @Now, 1),\n('PERM-04', 'ROLE-WARD-NURSE', 'EMR.Toolbar.BtnExportOfd', 0, 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO sys_user_dept_posts (id, user_id, dept_id, dept_category, role_id, is_default_context, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n('UDP-ADMIN-ADMIN', 'USER-ADMIN', 'DEPT-ADMIN-01', 'ADMIN', 'ROLE-ADMIN', 1, 1, @Now, @Now, 1),\n('UDP-DOC-OPD-OPD', 'USER-DOC-OPD', 'DEPT-OPD-01', 'OPD', 'ROLE-OPD-DOC', 1, 1, @Now, @Now, 1),\n('UDP-DOC-OPD-IPD', 'USER-DOC-OPD', 'DEPT-IPD-01', 'IPD', 'ROLE-IPD-ATTENDING', 0, 1, @Now, @Now, 1),\n('UDP-DOC-RES-IPD', 'USER-DOC-RESIDENT', 'DEPT-IPD-01', 'IPD', 'ROLE-IPD-RESIDENT', 1, 1, @Now, @Now, 1),\n('UDP-DOC-ATT-IPD1', 'USER-DOC-ATTENDING', 'DEPT-IPD-01', 'IPD', 'ROLE-IPD-ATTENDING', 1, 1, @Now, @Now, 1),\n('UDP-DOC-ATT-IPD2', 'USER-DOC-ATTENDING', 'DEPT-IPD-02', 'IPD', 'ROLE-IPD-ATTENDING', 0, 1, @Now, @Now, 1),\n('UDP-NURSE-W1', 'USER-NURSE-W1', 'DEPT-WARD-01', 'WARD', 'ROLE-WARD-NURSE', 1, 1, @Now, @Now, 1),\n('UDP-NURSE-W2', 'USER-NURSE-W2', 'DEPT-WARD-02', 'WARD', 'ROLE-WARD-NURSE', 1, 1, @Now, @Now, 1),\n('UDP-PHM-01', 'USER-PHM-01', 'DEPT-PHM-01', 'PHARMACY', 'ROLE-PHARMACIST', 1, 1, @Now, @Now, 1),\n('UDP-CASHIER-01', 'USER-CASHIER-01', 'DEPT-FIN-01', 'FINANCE', 'ROLE-CASHIER', 1, 1, @Now, @Now, 1),\n('UDP-MEDTECH-01', 'USER-MEDTECH-01', 'DEPT-MTC-LAB', 'MEDTECH', 'ROLE-MEDTECH', 1, 1, @Now, @Now, 1),\n('UDP-WH-01', 'USER-WH-01', 'DEPT-WH-01', 'WAREHOUSE', 'ROLE-WAREHOUSE', 1, 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO sys_user_dept_posts (id, user_id, dept_id, dept_category, role_id, is_default_context, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n('UDP-DOC-OPD-ENDO', 'USER-DOC-OPD', 'DEPT-OPD-ENDO-01', 'OPD', 'ROLE-ENDOCRINOLOGIST', 0, 1, @Now, @Now, 1),\n('UDP-DOC-OPD-CARDIO', 'USER-DOC-OPD', 'DEPT-OPD-01', 'OPD', 'ROLE-CARDIOLOGIST', 0, 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO sys_user_dept_posts (id, user_id, dept_id, dept_category, role_id, is_default_context, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES ('UDP-PHM-02', 'USER-PHM-01', 'DEPT-PHM-02', 'PHARMACY', 'ROLE-PHARMACIST', 0, 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO cfg_form_schemas\n    (id, form_code, form_name, entity_type, dept_category, target_dept_id, grid_columns, is_published, ext_data_json, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n    ('SCHEMA-CARDIO-FOLLOWUP-V2', 'FORM-CARDIO-FOLLOWUP-V2', '心血管专病结构化随访表单', 'OPD_VISIT', 'OPD', 'DEPT-OPD-01', 2, 1, '{\"version\":\"v2.1\",\"description\":\"心血管门诊与住院随访扩展\"}', 1, @Now, @Now, 1),\n    ('SCHEMA-RESP-COPD-ASSESS', 'FORM-RESP-COPD-ASSESS', '慢性阻塞性肺疾病 CAT 评分量表', 'OPD_VISIT', 'OPD', 'DEPT-ER-01', 2, 1, '{\"version\":\"v1.4\",\"description\":\"呼吸专病评估\"}', 1, @Now, @Now, 1),\n    ('SCHEMA-OBGYN-FIRST-VISIT', 'FORM-OBGYN-FIRST-VISIT', '妇产科门诊初诊专科数据采集表', 'OPD_VISIT', 'OPD', 'DEPT-OPD-01', 2, 1, '{\"version\":\"v3.0\",\"description\":\"妇产科初诊采集\"}', 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO cfg_form_schemas\n    (id, form_code, form_name, entity_type, dept_category, target_dept_id, grid_columns, is_published, ext_data_json, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n    ('SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'FORM-PRESCRIPTION-ITEM-FIELDS', '门诊处方明细字段配置', 'OPD_PRESCRIPTION_ITEM', 'OPD', 'DEPT-OPD-01', 1, 1, '{\"version\":\"v1.0\",\"description\":\"门诊处方明细动态列配置\"}', 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO cfg_field_definitions\n    (id, form_schema_id, field_code, field_label, data_type, ursa_control_type, control_key, dict_code, unit_text, validation_rule_json, visibility_expr, is_indexed, sort_order, ext_data_json, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n    ('FIELD-CARDIO-01', 'SCHEMA-CARDIO-FOLLOWUP-V2', 'INSURANCE_ACCOUNT', '家庭医保共济账户授权绑定', 'STRING', 'u:TextBox', 'EMR.Dynamic.InsuranceAccount', NULL, NULL, '{\"required\":false,\"placeholder\":\"请输入授权账户\"}', 'ALL', 0, 0, NULL, 1, @Now, @Now, 1),\n    ('FIELD-CARDIO-02', 'SCHEMA-CARDIO-FOLLOWUP-V2', 'CHRONIC_DISEASE_CLASS', '特殊慢性病种待遇认定分类', 'STRING', 'u:TextBox', 'EMR.Dynamic.ChronicDiseaseClass', NULL, NULL, '{\"required\":true,\"placeholder\":\"请输入慢病分类\"}', 'ALL', 1, 1, NULL, 1, @Now, @Now, 1),\n    ('FIELD-CARDIO-05', 'SCHEMA-CARDIO-FOLLOWUP-V2', 'PROSTATE_EXAM', '前列腺触诊', 'TEXT', 'u:TextArea', 'EMR.Dynamic.ProstateExam', NULL, NULL, '{\"required\":false,\"placeholder\":\"记录触诊所见\"}', 'MALE_ONLY', 0, 2, NULL, 1, @Now, @Now, 1),\n    ('FIELD-CARDIO-06', 'SCHEMA-CARDIO-FOLLOWUP-V2', 'PREGNANCY_COUNT', '孕产次数', 'INTEGER', 'u:NumericIntUpDown', 'EMR.Dynamic.PregnancyCount', NULL, '次', '{\"required\":false,\"min\":0,\"max\":20}', 'FEMALE_ONLY', 0, 3, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RESP-01', 'SCHEMA-RESP-COPD-ASSESS', 'CAT_COUGH', '咳嗽影响程度', 'ENUM', 'ComboBox', 'EMR.Dynamic.CatCough', 'CAT_SCORE', '分', '{\"required\":true,\"options\":[\"0\",\"1\",\"2\",\"3\",\"4\",\"5\"]}', 'ALL', 1, 0, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RESP-02', 'SCHEMA-RESP-COPD-ASSESS', 'CAT_PHLEGM', '咳痰影响程度', 'ENUM', 'ComboBox', 'EMR.Dynamic.CatPhlegm', 'CAT_SCORE', '分', '{\"required\":true,\"options\":[\"0\",\"1\",\"2\",\"3\",\"4\",\"5\"]}', 'ALL', 1, 1, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RESP-03', 'SCHEMA-RESP-COPD-ASSESS', 'FEV1_VALUE', '第一秒用力呼气容积', 'DECIMAL', 'u:NumericDoubleUpDown', 'EMR.Dynamic.Fev1', NULL, 'L', '{\"required\":false,\"min\":0,\"max\":10}', 'ALL', 1, 2, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RESP-04', 'SCHEMA-RESP-COPD-ASSESS', 'OXYGEN_THERAPY', '当前氧疗方式', 'STRING', 'u:TextBox', 'EMR.Dynamic.OxygenTherapy', NULL, NULL, '{\"required\":false,\"placeholder\":\"鼻导管/面罩/无\"}', 'ALL', 0, 3, NULL, 1, @Now, @Now, 1),\n    ('FIELD-OBGYN-01', 'SCHEMA-OBGYN-FIRST-VISIT', 'MENSTRUAL_HISTORY', '月经史', 'TEXT', 'u:TextArea', 'EMR.Dynamic.MenstrualHistory', NULL, NULL, '{\"required\":true,\"placeholder\":\"记录初潮、周期及末次月经\"}', 'FEMALE_ONLY', 1, 0, NULL, 1, @Now, @Now, 1),\n    ('FIELD-OBGYN-02', 'SCHEMA-OBGYN-FIRST-VISIT', 'PREGNANCY_COUNT', '孕产次数', 'INTEGER', 'u:NumericIntUpDown', 'EMR.Dynamic.PregnancyCount', NULL, '次', '{\"required\":true,\"min\":0,\"max\":20}', 'FEMALE_ONLY', 1, 1, NULL, 1, @Now, @Now, 1),\n    ('FIELD-OBGYN-03', 'SCHEMA-OBGYN-FIRST-VISIT', 'CONTRACEPTION_METHOD', '现用避孕方式', 'STRING', 'u:TextBox', 'EMR.Dynamic.ContraceptionMethod', NULL, NULL, '{\"required\":false,\"placeholder\":\"请输入避孕方式\"}', 'FEMALE_ONLY', 0, 2, NULL, 1, @Now, @Now, 1),\n    ('FIELD-OBGYN-04', 'SCHEMA-OBGYN-FIRST-VISIT', 'IS_PREGNANT', '当前是否妊娠', 'BOOLEAN', 'CheckBox', 'EMR.Dynamic.IsPregnant', NULL, NULL, '{\"required\":true}', 'FEMALE_ONLY', 1, 3, NULL, 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("DELETE FROM cfg_field_definitions WHERE form_schema_id = 'SCHEMA-PRESCRIPTION-ITEM-FIELDS'");
		await conn.ExecuteAsync("INSERT OR IGNORE INTO cfg_field_definitions\n    (id, form_schema_id, field_code, field_label, data_type, ursa_control_type, control_key, dict_code, unit_text, validation_rule_json, visibility_expr, is_indexed, sort_order, ext_data_json, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n    ('FIELD-RX-ITEM-01', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'ORDER_TYPE', '医嘱类型', 'STRING', 'ComboBox', 'PRESCRIPTION.Dynamic.OrderType', 'ORDER_TYPE', NULL, '{\"required\":true,\"width\":80,\"system\":true,\"editable\":true}', 'ALL', 1, 0, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-02', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'DRUG_NAME', '医嘱名称', 'STRING', 'u:TextBox', 'PRESCRIPTION.Dynamic.DrugName', NULL, NULL, '{\"required\":true,\"placeholder\":\"输入药品/医嘱名称\",\"width\":200,\"system\":true,\"editable\":true}', 'ALL', 1, 1, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-03', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'SPEC', '规格', 'STRING', 'u:TextBox', 'PRESCRIPTION.Dynamic.Spec', NULL, NULL, '{\"required\":false,\"placeholder\":\"规格\",\"width\":95,\"system\":true,\"editable\":true}', 'ALL', 0, 2, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-04', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'GROUP_NO', '组', 'STRING', 'u:TextBox', 'PRESCRIPTION.Dynamic.GroupNo', NULL, NULL, '{\"required\":false,\"width\":60,\"system\":true,\"editable\":true}', 'ALL', 0, 3, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-05', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'SKIN_TEST', '皮试', 'STRING', 'ComboBox', 'PRESCRIPTION.Dynamic.SkinTest', 'SKIN_TEST_RESULT', NULL, '{\"required\":false,\"width\":85,\"system\":true,\"editable\":true}', 'ALL', 0, 4, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-06', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'TOTAL_QTY', '总量', 'INTEGER', 'u:NumericIntUpDown', 'PRESCRIPTION.Dynamic.TotalQuantity', NULL, '数量', '{\"required\":true,\"width\":55,\"system\":true,\"editable\":true}', 'ALL', 0, 5, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-07', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'TOTAL_UNIT', '总单位', 'STRING', 'ComboBox', 'PRESCRIPTION.Dynamic.TotalUnit', 'PACKAGE_UNIT', '单位', '{\"required\":true,\"width\":55,\"system\":true,\"editable\":true}', 'ALL', 0, 6, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-08', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'DOSE', '每次用量', 'DECIMAL', 'u:NumericDoubleUpDown', 'PRESCRIPTION.Dynamic.Dose', NULL, '剂量', '{\"required\":true,\"placeholder\":\"每次用量\",\"width\":70,\"system\":true,\"editable\":true}', 'ALL', 0, 7, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-09', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'DOSE_UNIT', '单位', 'STRING', 'ComboBox', 'PRESCRIPTION.Dynamic.DoseUnit', 'DOSAGE_UNIT', NULL, '{\"required\":true,\"width\":50,\"system\":true,\"editable\":true}', 'ALL', 0, 8, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-10', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'PAYMENT_COUNT', '付数', 'INTEGER', 'u:NumericIntUpDown', 'PRESCRIPTION.Dynamic.PaymentCount', NULL, '付', '{\"required\":false,\"width\":50,\"system\":true,\"editable\":true}', 'ALL', 0, 9, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-11', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'FREQ_CODE', '频次编码', 'STRING', 'ComboBox', 'PRESCRIPTION.Dynamic.FreqCode', 'ORDER_FREQUENCY', NULL, '{\"required\":true,\"width\":75,\"system\":true,\"editable\":true}', 'ALL', 0, 10, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-12', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'ROUTE_NAME', '用法名称', 'STRING', 'ComboBox', 'PRESCRIPTION.Dynamic.RouteName', 'ADMIN_ROUTE', NULL, '{\"required\":true,\"width\":85,\"system\":true,\"editable\":true}', 'ALL', 0, 11, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-13', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'HOSP_INJECT_CNT', '院注次数', 'INTEGER', 'u:NumericIntUpDown', 'PRESCRIPTION.Dynamic.HospitalInjectionCount', NULL, '次', '{\"required\":false,\"width\":70,\"system\":true,\"editable\":true}', 'ALL', 0, 12, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-14', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'DOCTOR_NAME', '开立医生', 'STRING', 'u:TextBox', 'PRESCRIPTION.Dynamic.DoctorName', NULL, NULL, '{\"required\":false,\"width\":75,\"system\":true,\"editable\":false}', 'ALL', 0, 13, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-15', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'EXEC_DEPT', '执行科室', 'STRING', 'ComboBox', 'PRESCRIPTION.Dynamic.ExecDept', NULL, NULL, '{\"required\":false,\"width\":95,\"system\":true,\"editable\":true}', 'ALL', 0, 14, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-16', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'IS_URGENT', '加急', 'BOOLEAN', 'CheckBox', 'PRESCRIPTION.Dynamic.IsUrgent', NULL, NULL, '{\"required\":false,\"width\":55,\"system\":true,\"editable\":true}', 'ALL', 0, 15, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-17', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'EXAM_PART', '检查部位', 'STRING', 'u:TextBox', 'PRESCRIPTION.Dynamic.ExamPart', NULL, NULL, '{\"required\":false,\"placeholder\":\"检查部位\",\"width\":85,\"system\":true,\"editable\":true}', 'ALL', 0, 16, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-18', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'SPECIMEN_TYPE', '样本类型', 'STRING', 'ComboBox', 'PRESCRIPTION.Dynamic.SpecimenType', NULL, NULL, '{\"required\":false,\"placeholder\":\"样本类型\",\"width\":75,\"system\":true,\"editable\":true}', 'ALL', 0, 17, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-19', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'DEDUCT_DEPT', '扣库科室', 'STRING', 'ComboBox', 'PRESCRIPTION.Dynamic.DeductDept', NULL, NULL, '{\"required\":false,\"width\":95,\"system\":true,\"editable\":true}', 'ALL', 0, 18, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-20', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'MEDICATION_NOTE', '备注', 'STRING', 'u:TextBox', 'PRESCRIPTION.Dynamic.MedicationNote', NULL, NULL, '{\"required\":false,\"placeholder\":\"补充特别医嘱/用药说明\",\"width\":110,\"system\":true,\"editable\":true}', 'ALL', 0, 19, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-21', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'OPERATOR', '录入人', 'STRING', 'u:TextBox', 'PRESCRIPTION.Dynamic.Operator', NULL, NULL, '{\"required\":false,\"width\":75,\"system\":true,\"editable\":false}', 'ALL', 0, 20, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-22', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'OPD_DEPT', '开立科室', 'STRING', 'u:TextBox', 'PRESCRIPTION.Dynamic.OpdDept', NULL, NULL, '{\"required\":false,\"width\":85,\"system\":true,\"editable\":false}', 'ALL', 0, 21, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-23', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'CREATED_AT', '开立时间', 'STRING', 'u:TextBox', 'PRESCRIPTION.Dynamic.CreatedAt', NULL, NULL, '{\"required\":false,\"width\":120,\"system\":true,\"editable\":false}', 'ALL', 0, 22, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-24', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'STOP_TIME', '停止时间', 'STRING', 'u:TextBox', 'PRESCRIPTION.Dynamic.StopTime', NULL, NULL, '{\"required\":false,\"width\":80,\"system\":true,\"editable\":false}', 'ALL', 0, 23, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-25', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'STOP_DOCTOR', '停止人', 'STRING', 'u:TextBox', 'PRESCRIPTION.Dynamic.StopDoctor', NULL, NULL, '{\"required\":false,\"width\":70,\"system\":true,\"editable\":false}', 'ALL', 0, 24, NULL, 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO cfg_icd10_diagnoses\n    (id, icd_code, disease_name, pinyin_code, category_name, gender_exclusive, is_chronic_special, is_infectious_report, is_active, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n    ('ICD-01', 'I25.101', '冠状动脉粥样硬化性心脏病', 'GZDMZYHXXZB', 'CIRCULATORY', 'ALL', 1, 0, 1, 1, @Now, @Now, 1),\n    ('ICD-02', 'I20.000', '不稳定型心绞痛', 'BWDXXJT', 'CIRCULATORY', 'ALL', 1, 0, 1, 1, @Now, @Now, 1),\n    ('ICD-03', 'I10.x00', '原发性高血压', 'YFXGXY', 'CIRCULATORY', 'ALL', 1, 0, 1, 1, @Now, @Now, 1),\n    ('ICD-04', 'I48.x01', '阵发性心房颤动', 'ZFXXFCD', 'CIRCULATORY', 'ALL', 1, 0, 1, 1, @Now, @Now, 1),\n    ('ICD-05', 'I21.401', '急性非ST段抬高型心肌梗死', 'JXFSTDTGXXJGS', 'CIRCULATORY', 'ALL', 1, 0, 1, 1, @Now, @Now, 1),\n    ('ICD-06', 'J44.900', '慢性阻塞性肺疾病', 'MXZSXFJB', 'RESPIRATORY', 'ALL', 1, 0, 1, 1, @Now, @Now, 1),\n    ('ICD-07', 'J18.900', '社区获得性肺炎', 'SQHDXFY', 'RESPIRATORY', 'ALL', 0, 0, 1, 1, @Now, @Now, 1),\n    ('ICD-08', 'N40.x00', '良性前列腺增生', 'LXQLXZS', 'UROLOGY', 'MALE_ONLY', 1, 0, 1, 1, @Now, @Now, 1),\n    ('ICD-09', 'O80.900', '单胎顺产', 'DTSC', 'OBGYN', 'FEMALE_ONLY', 0, 0, 1, 1, @Now, @Now, 1),\n    ('ICD-10', 'N92.600', '月经不调', 'YJBT', 'OBGYN', 'FEMALE_ONLY', 0, 0, 1, 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO cfg_price_items\n    (id, item_code, national_ins_code, item_name, pinyin_code, fee_category, spec, unit_name, unit_price, reimburse_type, default_exec_dept_id, is_active, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n    ('PRC-01', 'REG_NORMAL', '110200001A', '普通门诊诊察费', 'PTMZZCF', 'REGISTRATION', '普通号', '次', 15.00, 'CLASS_A', 'DEPT-OPD-01', 1, 1, @Now, @Now, 1),\n    ('PRC-02', 'REG_EXPERT', '110200002A', '副主任/主治医师门诊诊察费', 'ZJMZZCF', 'REGISTRATION', '专家号', '次', 25.00, 'CLASS_A', 'DEPT-OPD-01', 1, 1, @Now, @Now, 1),\n    ('PRC-03', 'REG_EMERGENCY', '110200003A', '急诊诊察费', 'JZZCF', 'REGISTRATION', '急诊号', '次', 30.00, 'CLASS_A', 'DEPT-ER-01', 1, 1, @Now, @Now, 1),\n    ('PRC-04', 'LAB_BLOOD_ROUTINE', '250101015A', '全血细胞分析与五分类 (血常规)', 'XCGWFL', 'LAB', '紫头EDTA管', '次', 25.00, 'CLASS_A', 'DEPT-MTC-LAB', 1, 1, @Now, @Now, 1),\n    ('PRC-05', 'LAB_MYOCARDIAL_3', '250305008B', '心肌坏死标志物三项 (hs-cTnI/CK-MB/Myo)', 'XJHSSX', 'LAB', '黄头促凝管', '项', 115.00, 'CLASS_B', 'DEPT-MTC-LAB', 1, 1, @Now, @Now, 1),\n    ('PRC-06', 'RAD_CHEST_CT', '210103011A', '胸部高分辨螺旋CT平扫', 'XBGFFCT', 'EXAM', '64排螺旋CT', '部位', 180.00, 'CLASS_A', 'DEPT-MTC-RAD', 1, 1, @Now, @Now, 1),\n    ('PRC-07', 'ECG_12_LEAD', '310701001A', '12导联同步常规心电图', '12DLXDT', 'EXAM', '12导联', '次', 35.00, 'CLASS_A', 'DEPT-MTC-LAB', 1, 1, @Now, @Now, 1),\n    ('PRC-08', 'US_HEART_3D', '220302006B', '心脏高分辨三维彩色多普勒超声 (UCG)', 'XZSWCC', 'EXAM', '三维彩超', '次', 220.00, 'CLASS_B', 'DEPT-MTC-RAD', 1, 1, @Now, @Now, 1),\n    ('PRC-09', 'BED_STD_DAILY', '110900001A', '普通病房标准三人间床位费', 'BZCW', 'BED', '标准编制床', '日', 60.00, 'CLASS_A', 'DEPT-WARD-01', 1, 1, @Now, @Now, 1),\n    ('PRC-10', 'NURSING_LEVEL_1', '120100002A', '一级护理费', 'YJHLF', 'NURSING', '住院护理', '日', 36.00, 'CLASS_A', 'DEPT-WARD-01', 1, 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO cfg_order_frequencies\n    (id, freq_code, freq_name, daily_times, time_points_text, applicable_term, description, sort_order, is_active, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n    ('FREQ-01', 'qd', '每日一次', 1, '08:00', 'STANDING', '每日晨间8点执行给药或常规护理', 1, 1, 1, @Now, @Now, 1),\n    ('FREQ-02', 'bid', '每日两次', 2, '08:00, 16:00', 'STANDING', '早8点、下午16点各执行一次', 2, 1, 1, @Now, @Now, 1),\n    ('FREQ-03', 'tid', '每日三次', 3, '08:00, 12:00, 16:00', 'STANDING', '早8点、中午12点、下午16点各执行一次', 3, 1, 1, @Now, @Now, 1),\n    ('FREQ-04', 'qid', '每日四次', 4, '08:00, 12:00, 16:00, 20:00', 'STANDING', '早、中、晚及夜间各执行一次', 4, 1, 1, @Now, @Now, 1),\n    ('FREQ-05', 'qn', '每晚一次', 1, '20:00', 'STANDING', '晚间20点睡前给药或处置', 5, 1, 1, @Now, @Now, 1),\n    ('FREQ-06', 'q8h', '每8小时一次', 3, '06:00, 14:00, 22:00', 'STANDING', '维持抗感染血药峰浓度恒定，每8小时规律给药', 6, 1, 1, @Now, @Now, 1),\n    ('FREQ-07', 'q12h', '每12小时一次', 2, '08:00, 20:00', 'STANDING', '早8点与晚20点间隔12小时执行', 7, 1, 1, @Now, @Now, 1),\n    ('FREQ-08', '持续', '持续微量泵入/监护', 1, '08:00', 'STANDING', '全天微量泵入或心电监护，每日白班交接核对', 8, 1, 1, @Now, @Now, 1),\n    ('FREQ-09', 'st', '立即执行', 1, '即刻', 'STAT', '急救或临时处置医嘱，下达后即刻执行', 9, 1, 1, @Now, @Now, 1),\n    ('FREQ-10', 'prn', '必要时', 1, '按需', 'STAT', '备用医嘱，病情发作时按需核对执行', 10, 1, 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO cfg_clinical_dictionaries\n    (id, dict_type, item_code, item_name, pinyin_code, parent_code, sort_order, is_default, is_active, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n    ('DICT-RT-01', 'ADMIN_ROUTE', 'PO', '口服', 'KF', NULL, 1, 1, 1, 1, @Now, @Now, 1),\n    ('DICT-RT-02', 'ADMIN_ROUTE', 'IV_DRIP', '静脉滴注', 'JMDZ', NULL, 2, 0, 1, 1, @Now, @Now, 1),\n    ('DICT-RT-03', 'ADMIN_ROUTE', 'IV_PUSH', '静脉推注', 'JMTZ', NULL, 3, 0, 1, 1, @Now, @Now, 1),\n    ('DICT-RT-04', 'ADMIN_ROUTE', 'MICRO_PUMP', '持续微量泵入', 'CXWLBR', NULL, 4, 0, 1, 1, @Now, @Now, 1),\n    ('DICT-RT-05', 'ADMIN_ROUTE', 'SC', '皮下注射', 'PXZS', NULL, 5, 0, 1, 1, @Now, @Now, 1),\n    ('DICT-RT-06', 'ADMIN_ROUTE', 'IM', '肌内注射', 'JNZS', NULL, 6, 0, 1, 1, @Now, @Now, 1),\n    ('DICT-RT-07', 'ADMIN_ROUTE', 'INH', '雾化吸入', 'WHXR', NULL, 7, 0, 1, 1, @Now, @Now, 1),\n    ('DICT-TB-01', 'SPECIMEN_TUBE', 'TUBE_EDTA_PURPLE', '紫头EDTA抗凝管', 'ZTEDTAKNG', NULL, 1, 1, 1, 1, @Now, @Now, 1),\n    ('DICT-TB-02', 'SPECIMEN_TUBE', 'TUBE_GEL_YELLOW', '黄头分离胶促凝管', 'HTFLJCNG', NULL, 2, 0, 1, 1, @Now, @Now, 1),\n    ('DICT-TB-03', 'SPECIMEN_TUBE', 'TUBE_CITRATE_BLUE', '蓝头枸橼酸钠凝血管', 'LTJYSNNXG', NULL, 3, 0, 1, 1, @Now, @Now, 1),\n    ('DICT-NL-01', 'NURSING_LEVEL', 'LEVEL_SPECIAL', '特级护理', 'TJHL', NULL, 1, 0, 1, 1, @Now, @Now, 1),\n    ('DICT-NL-02', 'NURSING_LEVEL', 'LEVEL_1', '一级护理', 'YJHL', NULL, 2, 0, 1, 1, @Now, @Now, 1),\n    ('DICT-NL-03', 'NURSING_LEVEL', 'LEVEL_2', '二级护理', 'EJHL', NULL, 3, 1, 1, 1, @Now, @Now, 1),\n    ('DICT-NL-04', 'NURSING_LEVEL', 'LEVEL_3', '三级护理', 'SJHL', NULL, 4, 0, 1, 1, @Now, @Now, 1),\n    ('DICT-FT-01', 'FEE_TYPE', 'INS_EMPLOYEE', '城镇职工基本医疗保险', 'CZZGYB', NULL, 1, 1, 1, 1, @Now, @Now, 1),\n    ('DICT-FT-02', 'FEE_TYPE', 'INS_RESIDENT', '城乡居民基本医疗保险', 'CXJMYB', NULL, 2, 0, 1, 1, @Now, @Now, 1),\n    ('DICT-FT-03', 'FEE_TYPE', 'ER_GREEN', '急诊绿色通道先诊疗后付费', 'JZLSTD', NULL, 3, 0, 1, 1, @Now, @Now, 1),\n    ('DICT-FT-04', 'FEE_TYPE', 'SELF_PAY', '全自费医疗', 'QZFYL', NULL, 4, 0, 1, 1, @Now, @Now, 1),\n    ('DICT-BP-01', 'BLOOD_PRODUCT', 'RBC_LEUKO_DEPLETED', '去白细胞悬浮红细胞', 'QBXXFHXB', NULL, 1, 1, 1, 1, @Now, @Now, 1),\n    ('DICT-BP-02', 'BLOOD_PRODUCT', 'FFP_FRESH_FROZEN', '新鲜冰冻血浆', 'XXBDXJ', NULL, 2, 0, 1, 1, @Now, @Now, 1),\n    ('DICT-BP-03', 'BLOOD_PRODUCT', 'PLT_APHERESIS', '单采血小板', 'DCXXB', NULL, 3, 0, 1, 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		await SeedPharmacyDemoAsync(conn, nowMs);
	}

	private static async Task SeedPharmacyDemoAsync(DbConnection conn, long nowMs)
	{
		// Keep a dedicated admitted encounter for the inpatient pharmacy demo.
		await conn.ExecuteAsync(
			"""
			INSERT OR IGNORE INTO ipd_encounters
			    (id, admission_no, patient_id, campus_id, admit_dept_id, admit_ward_id,
			     is_borrowed_bed, bed_code, active_bed_token, attending_doctor_id, primary_nurse_id,
			     nursing_level_code, prepayment_balance, status, triage_status, schema_version,
			     created_at_ms, created_by, updated_at_ms, updated_by, version_no)
			VALUES
			    ('ADM-PHM-DEMO-01', 'ADMPHMDEMO01', 'PAT-PHM-DEMO-01', 'CAMPUS-01',
			     'DEPT-IPD-01', 'DEPT-WARD-02', 0, '01', 'BED-03', 'USER-DOC-ATTENDING',
			     'USER-NURSE-W2', 'LEVEL_2', 0.00, 1, 1, 1, @Now, 'USER-CASHIER-01',
			     @Now, 'USER-CASHIER-01', 1)
			""", new { Now = nowMs });

		await conn.ExecuteAsync(
			"""
			UPDATE ipd_beds
			SET bed_status = 1, current_encounter_id = 'ADM-PHM-DEMO-01', updated_at_ms = @Now, version_no = version_no + 1
			WHERE id = 'BED-03' AND (bed_status = 0 OR current_encounter_id = 'ADM-PHM-DEMO-01')
			""", new { Now = nowMs });

		await conn.ExecuteAsync(
			"""
			INSERT OR IGNORE INTO inv_drug_batches
			    (id, warehouse_or_pharmacy_id, drug_id, batch_no, expiry_date_int, invoice_no,
			     cold_chain_temp_celsius, quantity, is_frozen, schema_version,
			     created_at_ms, updated_at_ms, version_no)
			VALUES
			    ('BATCH-PHM2-04', 'DEPT-PHM-02', 'DRUG-04', 'LOT202605C', 20280501,
			     'INV-2026-206', NULL, 260, 0, 1, @Now, @Now, 1),
			    ('BATCH-PHM2-05', 'DEPT-PHM-02', 'DRUG-06', 'LOT202605D', 20280501,
			     'INV-2026-207', NULL, 160, 0, 1, @Now, @Now, 1)
			""", new { Now = nowMs });

		// Paid and reviewed outpatient prescription, deliberately left undispensed.
		await conn.ExecuteAsync(
			"""
			INSERT OR IGNORE INTO fin_payment_orders
			    (id, payment_no, request_key, scene, patient_id, visit_or_encounter_id,
			     total_amount, insurance_amount, prepayment_deduct, self_pay_amount,
			     payment_channel, prepayment_id, status, paid_at_ms, invoice_ofd_id,
			     schema_version, created_at_ms, updated_at_ms, version_no)
			VALUES
			    ('PAY-SEED-PHM-OPD-01', 'PAY20261105001', 'SEED-PHM-OPD-01', 'OPD',
			     'PAT85061801', 'VISIT-OPD-20261105001', 107.00, 74.90, 0.00, 32.10,
			     'INSURANCE', NULL, 1, @Now, NULL, 1, @Now, @Now, 1)
			""", new { Now = nowMs });

		await conn.ExecuteAsync(
			"""
			INSERT OR IGNORE INTO opd_prescriptions
			    (id, prescription_no, visit_id, patient_id, opd_dept_id, doctor_id,
			     target_pharmacy_id, rx_type, herb_doses, total_amount, status,
			     payment_order_id, reviewer_pharmacist_id, review_remark, rx_ofd_archive_id,
			     schema_version, created_at_ms, updated_at_ms, version_no)
			VALUES
			    ('RX-SEED-PHM-OPD-01', 'RX20261105PHM01', 'VISIT-OPD-20261105001',
			     'PAT85061801', 'DEPT-OPD-01', 'USER-DOC-OPD', 'DEPT-PHM-01',
			     'WESTERN', NULL, 107.00, 3, 'PAY-SEED-PHM-OPD-01', 'USER-PHM-01',
			     '药师审方通过：无青霉素、磺胺类过敏冲突', NULL, 1, @Now, @Now, 1)
			""", new { Now = nowMs });

		await conn.ExecuteAsync(
			"""
			INSERT OR IGNORE INTO opd_prescription_items
			    (id, prescription_id, group_no, drug_id, drug_name_snapshot, dose_once,
			     dose_unit, frequency_code, route_code, days, total_quantity, unit_price,
			     skin_test_flag, skin_test_batch_no, skin_test_checker_id, is_deleted,
			     deleted_at_ms, deleted_by, schema_version, created_at_ms, updated_at_ms, version_no)
			VALUES
			    ('RXI-SEED-PHM-OPD-01', 'RX-SEED-PHM-OPD-01', 1, 'DRUG-04',
			     '阿司匹林肠溶片', 100, 'mg', 'QD', 'PO', 30, 1, 22.50, 0,
			     NULL, NULL, 0, NULL, NULL, 1, @Now, @Now, 1),
			    ('RXI-SEED-PHM-OPD-02', 'RX-SEED-PHM-OPD-01', 2, 'DRUG-06',
			     '阿托伐他汀钙片', 20, 'mg', 'QN', 'PO', 30, 1, 84.50, 0,
			     NULL, NULL, 0, NULL, NULL, 1, @Now, @Now, 1)
			""", new { Now = nowMs });

		// Reviewed inpatient medication orders, left unclaimed by any dispense order.
		await conn.ExecuteAsync(
			"""
			INSERT INTO ord_medical_orders
			    (id, order_no, encounter_id, patient_id, order_ipd_dept_id, patient_ward_id,
			     exec_target_dept_id, order_term_type, group_no, order_category, item_code,
			     item_name, dosage, frequency_code, route_code, status, order_doctor_id,
			     verify_nurse_id, plan_start_at_ms, plan_stop_at_ms, ext_data_json,
			     schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
			VALUES
			    ('ORD-SEED-PHM-IPD-01', 'Y20261105-PHM01', 'ADM-PHM-DEMO-01',
			     'PAT-PHM-DEMO-01', 'DEPT-IPD-01', 'DEPT-WARD-02', 'DEPT-PHM-02', 2, 1,
			     'DRUG', 'DRUG-04', '阿司匹林肠溶片', 100, 'qd', 'PO', 1,
			     'USER-DOC-ATTENDING', 'USER-NURSE-W2', @Now, NULL,
			     '{"seed":"pharmacy-ward-demo"}', 1, @Now, 'USER-DOC-ATTENDING', @Now,
			     'USER-DOC-ATTENDING', 1),
			    ('ORD-SEED-PHM-IPD-02', 'Y20261105-PHM02', 'ADM-PHM-DEMO-01',
			     'PAT-PHM-DEMO-01', 'DEPT-IPD-01', 'DEPT-WARD-02', 'DEPT-PHM-02', 2, 2,
			     'DRUG', 'DRUG-06', '阿托伐他汀钙片', 20, 'qn', 'PO', 1,
			     'USER-DOC-ATTENDING', 'USER-NURSE-W2', @Now, NULL,
			     '{"seed":"pharmacy-ward-demo"}', 1, @Now, 'USER-DOC-ATTENDING', @Now,
			     'USER-DOC-ATTENDING', 1)
			ON CONFLICT DO NOTHING
			""", new { Now = nowMs });

		// Repair the existing demo rows without touching orders claimed by a dispense order.
		await conn.ExecuteAsync(
			"""
			UPDATE ord_medical_orders
			SET encounter_id = 'ADM-PHM-DEMO-01', patient_id = 'PAT-PHM-DEMO-01', updated_at_ms = @Now,
			    item_code = CASE WHEN id = 'ORD-SEED-PHM-IPD-02' THEN 'DRUG-06' ELSE item_code END,
			    version_no = version_no + 1
			WHERE id IN ('ORD-SEED-PHM-IPD-01', 'ORD-SEED-PHM-IPD-02')
			  AND (encounter_id = 'ADM20261105001' OR patient_id = 'PAT85061801')
			  AND NOT EXISTS (
			      SELECT 1 FROM phm_dispense_order_lines l
			      WHERE l.medical_order_id = ord_medical_orders.id
			  )
			""", new { Now = nowMs });
		// 15. Seed P1 Security & Cryptographic KMS Keys
		long oneYearMs = 365L * 24 * 3600 * 1000;
		long halfYearMs = 180L * 24 * 3600 * 1000;
		await conn.ExecuteAsync("""
			INSERT OR IGNORE INTO ops_kms_keys (id, key_name, algorithm, version, state, hsm_protected, key_fingerprint, expires_at_ms, rotated_at_ms, description, ext_data_json, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
			VALUES
			    ('KMS-KEY-01', 'JWT_SIGNING_KEY', 'SM2', 1, 'ACTIVE', 1, 'SM2-PUB-7A39B206F18E94B3C1', @Now + @OneYear, NULL, '全院登录认证与令牌签发私钥 (HSM 硬件密码机隔离)', '{"hsm_slot":1}', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
			    ('KMS-KEY-02', 'SM4_DATABASE_KEY', 'SM4-GCM', 1, 'ACTIVE', 1, 'SM4-FPR-91BC832E54AA62B8', @Now + @HalfYear, NULL, '患者敏感信息 (PII) 数据库存储加密主密钥', '{"hsm_slot":2}', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
			    ('KMS-KEY-03', 'SM2_SIGNATURE_KEY', 'SM2', 1, 'ACTIVE', 1, 'SM2-CA-55F4D3C210E87391', @Now + @OneYear, NULL, '电子病历三级审签与 OFD 归档国密签名私钥', '{"hsm_slot":3}', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
			    ('KMS-KEY-04', 'HMAC_AUDIT_KEY', 'HMAC-SM3', 1, 'ACTIVE', 1, 'HMAC-SM3-A01B2C3D4E5F6789', @Now + @HalfYear, NULL, '等保三级安全审计日志哈希链防篡改计算密钥', '{"hsm_slot":4}', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1)
			""", new { Now = nowMs, OneYear = oneYearMs, HalfYear = halfYearMs });

		// 16. Seed P1 Intrusion Detection Baseline Alerts
		await conn.ExecuteAsync("""
			INSERT OR IGNORE INTO ops_security_alerts (id, alert_no, alert_type, severity, source_ip, user_id, operator_name, target_resource, description, threat_indicators_json, status, resolved_by, resolved_at_ms, resolution_note, ext_data_json, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
			VALUES
			    ('ALERT-SEED-01', 'ALT-202611-0001', 'BRUTE_FORCE_LOGIN', 'HIGH', '192.168.10.105', 'USER-CASHIER-01', '刘洋 (收费结算处收费员)', 'api/v4/platform/auth/login', '检测到连续 5 次口令尝试失败，触发持久化 15 分钟账户安全锁定', '{"failed_count":5,"rule":"IDPS-01-BRUTE-FORCE"}', 'RESOLVED', 'USER-ADMIN', @Now, '安全管理员核实为窗口误操作，已完成口令重置与安全培训', '{"mitre_technique":"T1110"}', 1, @Now - 3600000, 'IDPS_MONITOR', @Now, 'USER-ADMIN', 1)
			""", new { Now = nowMs });

		// 17. Seed P1 Consumables & CSSD Logistics Baseline
		await conn.ExecuteAsync("""
			INSERT OR IGNORE INTO sys_departments (id, campus_id, dept_code, dept_name, pinyin_code, dept_category, sub_specialty_type, rated_bed_count, is_allow_appointment, is_active, schema_version, created_at_ms, updated_at_ms, version_no)
			VALUES
			    ('DEPT-WH-CSM', 'CAMPUS-01', 'WH_CSM_CENTRAL', '医用耗材中心库', 'YYHCZXK', 'WAREHOUSE', 'CONSUMABLE', 0, 0, 1, 1, @Now, @Now, 1),
			    ('DEPT-CSSD', 'CAMPUS-01', 'CSSD_CENTRAL', '消毒供应中心(CSSD)', 'XDGYZX', 'MEDTECH', 'STERILIZATION', 0, 0, 1, 1, @Now, @Now, 1);

			INSERT OR IGNORE INTO csm_consumables (id, code, name, spec, unit, category, is_sterile, is_high_value, price, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
			VALUES
			    ('CSM-001', 'HC001', '一次性使用无菌注射器 5ml', '5ml 带针', '支', 'GENERAL', 1, 0, 1.50, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
			    ('CSM-002', 'HC002', '一次性使用精密过滤输液器', '1.2um', '套', 'GENERAL', 1, 0, 12.00, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
			    ('CSM-003', 'HC003', '钛合金锁定接骨板', '8孔直型', '块', 'IMPLANT', 1, 1, 4800.00, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
			    ('CSM-004', 'HC004', '外科缝线带针 3-0', '1/2弧 22mm', '包', 'SURGICAL', 1, 0, 28.00, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

			INSERT OR IGNORE INTO csm_stocks (id, consumable_id, warehouse_id, quantity, reserved_qty, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
			VALUES
			    ('STK-CSM-01', 'CSM-001', 'DEPT-WH-CSM', 5000, 0, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
			    ('STK-CSM-02', 'CSM-002', 'DEPT-WH-CSM', 2000, 0, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
			    ('STK-CSM-03', 'CSM-003', 'DEPT-WH-CSM', 20, 0, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
			    ('STK-CSM-04', 'CSM-004', 'DEPT-WH-CSM', 300, 0, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

			INSERT OR IGNORE INTO csm_batches (id, consumable_id, batch_no, expiry_date, manufacture_date, quantity, unit_cost, is_frozen, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
			VALUES
			    ('BAT-CSM-01', 'CSM-001', 'BATCH-HC202611-01', '2028-11-01', '2026-11-01', 5000, 0.80, 0, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
			    ('BAT-CSM-02', 'CSM-002', 'BATCH-HC202611-02', '2028-11-01', '2026-11-01', 2000, 6.50, 0, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
			    ('BAT-CSM-03', 'CSM-003', 'BATCH-HC202611-03', '2030-11-01', '2026-11-01', 20, 2600.00, 0, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

			INSERT OR IGNORE INTO csm_trace_codes (id, consumable_id, trace_code, batch_no, status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
			VALUES
			    ('TC-01', 'CSM-003', 'UDI-HC-2026110001', 'BATCH-HC202611-03', 'IN_STOCK', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
			    ('TC-02', 'CSM-003', 'UDI-HC-2026110002', 'BATCH-HC202611-03', 'IN_STOCK', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

			INSERT OR IGNORE INTO csm_sterile_packages (id, package_code, package_name, contents_json, sterilization_batch_no, status, valid_until_ms, current_location, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
			VALUES
			    ('PKG-01', 'PKG-GEN-SURG-01', '基础普外科开腹器械包', '[{"name":"手术刀柄#3","qty":2},{"name":"止血钳","qty":10},{"name":"持针钳","qty":2},{"name":"组织剪","qty":2}]', 'ST-BATCH-20261101', 'STERILIZED', @Now + @HalfYear, 'DEPT-CSSD', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);
			""", new { Now = nowMs, HalfYear = halfYearMs });

		// Step 18: P1-C Surgery and Anesthesia seed
		await conn.ExecuteAsync(@"
INSERT OR IGNORE INTO mte_surgery_schedules
    (id, schedule_no, encounter_id, patient_id, patient_name, surgery_code, surgery_name,
     surgery_level, or_room_id, table_seq, surgeon_id, surgeon_name,
     anesthesiologist_id, anesthesiologist_name, circulating_nurse_id,
     scheduled_start_ms, estimated_duration_min, status, who_sign_in_ok, who_timeout_ok, who_sign_out_ok,
     instrument_count_balanced, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('SCHED-SEED-01', 'SURG20261118001', 'ENC-IPD-01', 'PAT-01', '张建国', 'SURG-THOR-01', '胸腔镜下肺叶切除术',
     3, 'ROOM-OR-01', 1, 'USER-DOC-IPD', '王建国',
     'USER-ANES-01', '李麻醉', 'USER-NURSE-IPD',
     @Now + 3600000, 120, 0, 0, 0, 0, 0, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

INSERT OR IGNORE INTO mtc_surgery_schedules
    (id, encounter_id, or_dept_id, or_room_code, table_seq, surgery_code, surgery_name,
     surgery_level, main_surgeon_id, anesthesiologist_id, circulating_nurse_id,
     sign_in_ok, time_out_ok, sign_out_ok, instrument_count_balanced, status,
     schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('SCHED-SEED-01', 'ENC-IPD-01', 'DEPT-MTC-OR', 'ROOM-OR-01', 1, 'SURG-THOR-01', '胸腔镜下肺叶切除术',
     3, 'USER-DOC-IPD', 'USER-ANES-01', 'USER-NURSE-IPD',
     0, 0, 0, 0, 0, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);
", new { Now = nowMs });

		// Step 19: P1-D Nursing & P1-E Infection Control seed
		await conn.ExecuteAsync("""
INSERT OR IGNORE INTO nur_care_plans
    (id, encounter_id, patient_id, diagnosis_or_problem, goal, interventions_json, target_date_ms, status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('PLAN-SEED-01', 'ADM20261105001', 'PAT85061801', '气体交换受损（与肺部感染及支气管痉挛有关）', '患者3天内呼吸平稳，SpO2维持在95%以上', '["遵医嘱低流量鼻导管吸氧","协助患者取半卧位，指导有效咳嗽排痰","密切监测血气分析与生命体征"]', @Now + 259200000, 0, 1, @Now, 'USER-NURSE-W2', @Now, 'USER-NURSE-W2', 1);

INSERT OR IGNORE INTO nur_pressure_ulcer_assessments
    (id, encounter_id, patient_id, sensory_perception, moisture, activity, mobility, nutrition, friction_shear, total_score, risk_level, preventive_measures_json, assessor_id, assessed_at_ms, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('BRADEN-SEED-01', 'ADM20261105001', 'PAT85061801', 3, 3, 2, 2, 3, 2, 15, 'MILD', '["保持床单位清洁干燥","每2小时协助翻身"]', 'USER-NURSE-W2', @Now, 1, @Now, 'USER-NURSE-W2', @Now, 'USER-NURSE-W2', 1);

INSERT OR IGNORE INTO nur_fall_assessments
    (id, encounter_id, patient_id, history_of_falling, secondary_diagnosis, ambulatory_aid, iv_or_saline_lock, gait, mental_status, total_score, risk_level, preventive_measures_json, assessor_id, assessed_at_ms, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('MORSE-SEED-01', 'ADM20261105001', 'PAT85061801', 0, 15, 0, 20, 0, 0, 35, 'MEDIUM', '["开启床档防护","呼叫器置于易取处","下床活动需家属或护士搀扶"]', 'USER-NURSE-W2', @Now, 1, @Now, 'USER-NURSE-W2', @Now, 'USER-NURSE-W2', 1);

INSERT OR IGNORE INTO nur_quality_indicators
    (id, ward_id, month_str, patient_days, fall_count, pressure_ulcer_count, unplanned_extubation_count, cauti_count, clabsi_count, reporter_id, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('QI-SEED-01', 'DEPT-WARD-01', '2026-11', 1250, 1, 0, 0, 0, 0, 'USER-NURSE-W2', 1, @Now, 'USER-NURSE-W2', @Now, 'USER-NURSE-W2', 1);

INSERT OR IGNORE INTO ipc_infection_reports
    (id, report_no, encounter_id, patient_id, patient_name, ward_id, infection_site, pathogen, is_mdro, diagnosed_at_ms, clinical_manifestation, status, reporter_id, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('HAI-SEED-01', 'HAI20261118001', 'ADM20261105001', 'PAT85061801', '李建国', 'DEPT-WARD-01', '呼吸系统', '耐甲氧西林金黄色葡萄球菌 (MRSA)', 1, @Now, '发热38.8℃，咳黄脓痰，肺部湿啰音', 0, 'USER-DOC-ATTENDING', 1, @Now, 'USER-DOC-ATTENDING', @Now, 'USER-DOC-ATTENDING', 1);

INSERT OR IGNORE INTO ipc_isolation_orders
    (id, order_no, encounter_id, patient_id, ward_id, isolation_type, reason, start_ms, status, prescribed_by, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('ISO-SEED-01', 'ISO20261118001', 'ADM20261105001', 'PAT85061801', 'DEPT-WARD-01', 'CONTACT', 'MRSA 肺部感染，接触隔离防护', @Now, 0, 'USER-DOC-ATTENDING', 1, @Now, 'USER-DOC-ATTENDING', @Now, 'USER-DOC-ATTENDING', 1);

INSERT OR IGNORE INTO ipc_hand_hygiene_audits
    (id, ward_id, observer_id, audit_date, moment_type, staff_category, opportunity_count, performed_count, correct_count, compliance_rate, accuracy_rate, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('HAND-SEED-01', 'DEPT-WARD-01', 'USER-NURSE-W2', '2026-11-18', 'TOUCH_PATIENT', 'NURSE', 20, 19, 18, 95.00, 94.74, 1, @Now, 'USER-NURSE-W2', @Now, 'USER-NURSE-W2', 1);
""", new { Now = nowMs });

		// Step 20: P2-A Patient Services & P2-B Management Support seed
		await conn.ExecuteAsync("""
INSERT OR IGNORE INTO srv_satisfaction_surveys
    (id, encounter_id, visit_id, patient_id, patient_name, dept_id, score, medical_skill_score, service_attitude_score, environment_score, comment, submitted_at_ms, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('SAT-SEED-01', 'ADM20261105001', 'VISIT-001', 'PAT85061801', '李建国', 'DEPT-RESP-01', 5, 5, 5, 5, '医护人员非常耐心细致，病区环境整洁舒适！', @Now, 1, @Now, 'PAT85061801', @Now, 'PAT85061801', 1);

INSERT OR IGNORE INTO srv_complaints
    (id, complaint_no, patient_id, patient_name, patient_phone, dept_id, complaint_type, description, status, handler_id, handler_name, handling_result, patient_satisfaction, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('CMPL-SEED-01', 'CMPL20261118001', 'PAT85061801', '李建国', '13800138000', 'DEPT-OPD-01', 'WAITING_TIME', '门诊高峰期取药排队时间较长', 3, 'USER-ADMIN', '系统管理员', '已开启快速发药应急窗口，电话回访患者表示理解', 'SATISFIED', 1, @Now, 'USER-ADMIN', @Now, 'USER-ADMIN', 1);

INSERT OR IGNORE INTO srv_informed_consents
    (id, consent_no, patient_id, encounter_id, consent_type, title, document_content_summary, signer_type, signer_name, signed_at_ms, witness_doctor_id, signature_hash, status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('CONSENT-SEED-01', 'IC20261118001', 'PAT85061801', 'ADM20261105001', 'SURGERY_CONSENT', '支气管镜下诊疗知情同意书', '术前风险告知、麻醉方式及术中可能并发症充分知情并签署', 'PATIENT', '李建国', @Now, 'USER-DOC-ATTENDING', 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855', 1, 1, @Now, 'USER-DOC-ATTENDING', @Now, 'USER-DOC-ATTENDING', 1);

INSERT OR IGNORE INTO fin_cost_centers
    (id, dept_id, period, direct_cost, allocated_cost, total_cost, revenue, profit_margin, allocation_method, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('CC-SEED-01', 'DEPT-RESP-01', '2026-10', 450000.00, 120000.00, 570000.00, 720000.00, 20.83, 'STEP_DOWN', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('CC-SEED-02', 'DEPT-CARD-01', '2026-10', 680000.00, 180000.00, 860000.00, 1150000.00, 25.22, 'STEP_DOWN', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

INSERT OR IGNORE INTO eqp_equipments
    (id, code, name, model_spec, dept_id, purchase_date, price, warranty_end, is_mandatory_inspection, next_inspection_date, status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('EQP-SEED-01', 'EQP-RESP-VENT-01', '高频呼吸机', 'Servo-u 3.0', 'DEPT-WARD-01', '2024-01-15', 380000.00, '2027-01-15', 1, '2027-01-10', 'NORMAL', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('EQP-SEED-02', 'EQP-OR-MON-01', '多参数麻醉监护仪', 'Mindray BeneVision N17', 'DEPT-MTC-OR', '2023-06-20', 120000.00, '2026-06-20', 1, '2026-12-01', 'NORMAL', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

INSERT OR IGNORE INTO eqp_maintenance_records
    (id, equipment_id, type, maintenance_date, cost, vendor, engineer_name, result, remark, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('MAINT-SEED-01', 'EQP-SEED-01', 'CALIBRATION', '2026-01-10', 1500.00, '市计量科学研究院', '张工', 'QUALIFIED', '气道压力传感器精度合格，出具法定检定证书', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

-- Step 21: Custom SQL Reports & Parameterized Dynamic Queries
INSERT OR IGNORE INTO ops_report_definitions
    (id, report_code, report_name, category, description, sql_template, parameters_schema_json, display_columns_json, is_published, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('RPT-DEF-01', 'RPT_OPD_DEPT_SUMMARY', '门急诊科室诊疗汇总报表', '门急诊', '按就诊状态与科室名称动态统计各临床科室门诊量及患者总数',
'SELECT 
    COALESCE(d.dept_name, v.opd_dept_id) AS 科室名称,
    COUNT(v.id) AS 就诊总人次,
    COUNT(DISTINCT v.patient_id) AS 患者总数
FROM opd_visits v
LEFT JOIN sys_departments d ON d.id = v.opd_dept_id
WHERE (@Status IS NULL OR v.status = @Status)
  AND (@DeptName IS NULL OR @DeptName = '''' OR d.dept_name LIKE ''%'' || @DeptName || ''%'')
GROUP BY d.dept_name, v.opd_dept_id
ORDER BY 就诊总人次 DESC',
'[
  {"Name":"Status","Label":"就诊状态 (1=候诊, 2=已完成)","Type":"INT","DefaultValue":"2","IsRequired":false,"Description":"就诊状态代码，留空统计全部"},
  {"Name":"DeptName","Label":"科室名称关键字","Type":"STRING","DefaultValue":"","IsRequired":false,"Description":"科室名称模糊搜索，留空查询全院"}
]',
'["科室名称","就诊总人次","患者总数"]', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),

    ('RPT-DEF-02', 'RPT_IPD_WARD_OCCUPANCY', '住院病区在院床位与费用负荷报表', '住院业务', '动态查询各病区当前在院患者负荷与预交金结余总额',
'SELECT 
    COALESCE(w.dept_name, e.admit_ward_id) AS 病区名称,
    COUNT(e.id) AS 在院患者数,
    COALESCE(SUM(e.prepayment_balance), 0) AS 预交金结余总额
FROM ipd_encounters e
LEFT JOIN sys_departments w ON w.id = e.admit_ward_id
WHERE (@Status IS NULL OR e.status = @Status)
  AND (@WardName IS NULL OR @WardName = '''' OR w.dept_name LIKE ''%'' || @WardName || ''%'')
GROUP BY w.dept_name, e.admit_ward_id
ORDER BY 在院患者数 DESC',
'[
  {"Name":"Status","Label":"在院状态码 (1=在院, 2=已出院)","Type":"INT","DefaultValue":"1","IsRequired":false,"Description":"1=在院患者, 2=已办理出院"},
  {"Name":"WardName","Label":"病区名称关键字","Type":"STRING","DefaultValue":"","IsRequired":false,"Description":"病区名称模糊搜索，留空查询全院"}
]',
'["病区名称","在院患者数","预交金结余总额"]', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),

    ('RPT-DEF-03', 'RPT_DRUG_DISPENSE_TOP', '处方调剂药品消耗与销售排行报表', '药事进销存', '按药品名称和最低金额过滤门诊药房发药消耗与销售金额排行榜',
'SELECT 
    i.drug_id AS 药品编号,
    i.drug_name_snapshot AS 药品名称,
    COUNT(i.id) AS 调剂明细数,
    SUM(i.total_quantity) AS 发出总数量,
    SUM(i.total_quantity * i.unit_price) AS 预估总金额
FROM opd_prescription_items i
WHERE i.is_deleted = 0
  AND (@DrugName IS NULL OR @DrugName = '''' OR i.drug_name_snapshot LIKE ''%'' || @DrugName || ''%'')
GROUP BY i.drug_id, i.drug_name_snapshot
HAVING (@MinAmount IS NULL OR SUM(i.total_quantity * i.unit_price) >= @MinAmount)
ORDER BY 预估总金额 DESC',
'[
  {"Name":"DrugName","Label":"药品名称关键字","Type":"STRING","DefaultValue":"","IsRequired":false,"Description":"药品名称模糊检索，留空统计全部"},
  {"Name":"MinAmount","Label":"最低累计销售金额","Type":"DECIMAL","DefaultValue":"0","IsRequired":false,"Description":"过滤大于等于该销售额的药品"}
]',
'["药品编号","药品名称","调剂明细数","发出总数量","预估总金额"]', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),

    ('RPT-DEF-04', 'RPT_FIN_PAYMENT_CHANNEL', '财务收费与支付渠道对账汇总报表', '财务结算', '多维汇总门急诊与住院各支付渠道收款笔数与实收总额',
'SELECT 
    p.payment_channel AS 支付渠道,
    p.scene AS 结算场景,
    COUNT(p.id) AS 结算笔数,
    SUM(p.total_amount) AS 结算总额,
    SUM(p.self_pay_amount) AS 实收自付总额
FROM fin_payment_orders p
WHERE p.status = 1
  AND (@Channel IS NULL OR @Channel = '''' OR p.payment_channel = @Channel)
  AND (@Scene IS NULL OR @Scene = '''' OR p.scene = @Scene)
GROUP BY p.payment_channel, p.scene
ORDER BY 结算总额 DESC',
'[
  {"Name":"Channel","Label":"支付渠道 (WECHAT/ALIPAY/CASH/CARD)","Type":"STRING","DefaultValue":"","IsRequired":false,"Description":"支付渠道代码，留空统计全渠道"},
  {"Name":"Scene","Label":"业务场景 (OPD/IPD)","Type":"STRING","DefaultValue":"","IsRequired":false,"Description":"业务场景代码，留空统计全部"}
]',
'["支付渠道","结算场景","结算笔数","结算总额","实收自付总额"]', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

""", new { Now = nowMs });

		await SeedEmrLevel7DataAsync(conn, nowMs);
		await SeedWorkflowDataAsync(conn, nowMs);
	}

	private static async Task ResetDemoPasswordsAsync(DbConnection conn, long nowMs)
	{
		IReadOnlyList<DemoUserPasswordRow> users = (await conn.QueryAsync<DemoUserPasswordRow>(
			"SELECT id AS Id, user_name AS UserName, password_hash AS PasswordHash FROM sys_users WHERE user_name IN @UserNames",
			new { UserNames = DemoUserNames })).ToList();

		foreach (DemoUserPasswordRow user in users)
		{
			var (isValid, _) = PasswordSecurityHelper.VerifyPassword(DemoPassword, user.PasswordHash);
			if (isValid)
			{
				continue;
			}

			await conn.ExecuteAsync(
				"UPDATE sys_users SET password_hash = @PasswordHash, session_version = session_version + 1, updated_at_ms = @NowMs, version_no = version_no + 1 WHERE id = @Id",
				new
				{
					PasswordHash = PasswordSecurityHelper.HashPassword(DemoPassword),
					NowMs = nowMs,
					user.Id
				});
		}
	}
}