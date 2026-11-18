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

public static class DbSeedData
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
		await conn.ExecuteAsync("INSERT OR IGNORE INTO emr_doc_templates\n    (id, template_code, template_version, template_name, emr_scene, doc_type_code, dept_category, target_dept_id, disease_icd_prefix, time_limit_hours, required_sign_levels, ofd_layout_id, is_published, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n    ('TMPL-OPD-FIRST', 'OPD_FIRST_VISIT', 1, '门诊初诊结构化病历', 'OPD', 'OPD_FIRST_VISIT', 'OPD', 'DEPT-OPD-01', 'I25', 2, 1, 'LAYOUT-OPD-B5', 1, 1, @Now, @Now, 1),\n    ('TMPL-OPD-FOLLOWUP', 'OPD_FOLLOWUP', 1, '门诊复诊与慢病随访病历', 'OPD', 'OPD_FOLLOWUP', 'OPD', 'DEPT-OPD-01', 'I10', 2, 1, 'LAYOUT-OPD-B5', 1, 1, @Now, @Now, 1),\n    ('TMPL-IPD-ADMISSION', 'IPD_ADMISSION_24H', 1, '住院24小时入院记录', 'IPD', 'IPD_ADMISSION', 'IPD', 'DEPT-IPD-01', 'I25', 24, 3, 'LAYOUT-IPD-A4', 1, 1, @Now, @Now, 1),\n    ('TMPL-IPD-PROGRESS', 'IPD_DAILY_PROGRESS', 1, '住院首次/日常病程记录', 'IPD', 'IPD_PROGRESS', 'IPD', 'DEPT-IPD-01', 'I25', 8, 2, 'LAYOUT-IPD-A4', 1, 1, @Now, @Now, 1),\n    ('TMPL-IPD-DISCHARGE', 'IPD_DISCHARGE_SUMMARY', 1, '住院出院记录与小结', 'IPD', 'IPD_DISCHARGE', 'IPD', 'DEPT-IPD-01', 'I25', 24, 3, 'LAYOUT-IPD-A4', 1, 1, @Now, @Now, 1),\n    ('TMPL-WARD-ADMISSION', 'WARD_ADMISSION_ASSESS', 1, '住院患者入院护理评估单 (含三评量表)', 'WARD', 'NURSING_ADMISSION', 'WARD', 'DEPT-WARD-01', NULL, 4, 2, 'LAYOUT-WARD-A4', 1, 1, @Now, @Now, 1),\n    ('TMPL-WARD-CRITICAL', 'WARD_CRITICAL_CARE', 1, '危重患者特级护理观察记录单', 'WARD', 'NURSING_CRITICAL', 'WARD', 'DEPT-WARD-01', NULL, 2, 2, 'LAYOUT-WARD-A4', 1, 1, @Now, @Now, 1)", new
		{
			Now = nowMs
		});
		await conn.ExecuteAsync("INSERT OR IGNORE INTO emr_section_schemas\n    (id, template_id, section_code, section_title, render_mode, content_template_json, section_control_key, is_required, visibility_expr, sort_order, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n    ('SEC-OPD1-01', 'TMPL-OPD-FIRST', 'SEC_CHIEF', '主诉与现病史', 'GRID_FORM', '{\"elements\":[\"CHIEF_COMPLAINT\",\"PRESENT_ILLNESS\"]}', 'EMR.Opd.ChiefSection', 1, 'ALL', 1, 1, @Now, @Now, 1),\n    ('SEC-OPD1-02', 'TMPL-OPD-FIRST', 'SEC_HISTORY', '既往史与过敏史', 'GRID_FORM', '{\"elements\":[\"PAST_HISTORY\",\"ALLERGY_HISTORY\"]}', 'EMR.Opd.HistorySection', 1, 'ALL', 2, 1, @Now, @Now, 1),\n    ('SEC-OPD1-03', 'TMPL-OPD-FIRST', 'SEC_EXAM', '体格检查与专科排他检查 (GATE-03)', 'GRID_FORM', '{\"elements\":[\"PHYSICAL_EXAM\",\"DE_PROSTATE_EXAM\",\"DE_GRAVIDITY\",\"DE_PARITY\",\"DE_MENSTRUAL_HISTORY\"]}', 'EMR.Opd.ExamSection', 1, 'ALL', 3, 1, @Now, @Now, 1),\n    ('SEC-OPD1-04', 'TMPL-OPD-FIRST', 'SEC_DIAG_PLAN', '初步诊断与诊疗处置计划', 'INLINE_PARAGRAPH', '{\"elements\":[\"PRIMARY_DIAGNOSIS\",\"TREATMENT_PLAN\"]}', 'EMR.Opd.DiagPlanSection', 1, 'ALL', 4, 1, @Now, @Now, 1),\n\n    ('SEC-OPD2-01', 'TMPL-OPD-FOLLOWUP', 'SEC_FOLLOWUP_MAIN', '复诊主诉与慢病控制评估', 'GRID_FORM', '{\"elements\":[\"CHIEF_COMPLAINT\",\"PRESENT_ILLNESS\",\"AUX_EXAM\"]}', 'EMR.Opd.FollowupMain', 1, 'ALL', 1, 1, @Now, @Now, 1),\n    ('SEC-OPD2-02', 'TMPL-OPD-FOLLOWUP', 'SEC_FOLLOWUP_PLAN', '复诊诊断与续方调整方案', 'INLINE_PARAGRAPH', '{\"elements\":[\"PRIMARY_DIAGNOSIS\",\"TREATMENT_PLAN\"]}', 'EMR.Opd.FollowupPlan', 1, 'ALL', 2, 1, @Now, @Now, 1),\n\n    ('SEC-IPD1-01', 'TMPL-IPD-ADMISSION', 'SEC_IPD_HISTORY', '入院主诉、现病史与既往过敏史', 'INLINE_PARAGRAPH', '{\"elements\":[\"CHIEF_COMPLAINT\",\"PRESENT_ILLNESS\",\"PAST_HISTORY\",\"ALLERGY_HISTORY\"]}', 'EMR.Ipd.History', 1, 'ALL', 1, 1, @Now, @Now, 1),\n    ('SEC-IPD1-02', 'TMPL-IPD-ADMISSION', 'SEC_IPD_PHYSICAL', '系统体格检查与专科情况', 'GRID_FORM', '{\"elements\":[\"PHYSICAL_EXAM\",\"DE_PROSTATE_EXAM\",\"DE_GRAVIDITY\",\"DE_PARITY\",\"AUX_EXAM\"]}', 'EMR.Ipd.Physical', 1, 'ALL', 2, 1, @Now, @Now, 1),\n    ('SEC-IPD1-03', 'TMPL-IPD-ADMISSION', 'SEC_IPD_DIAG', '入院诊断与三级医师诊疗计划', 'INLINE_PARAGRAPH', '{\"elements\":[\"ADMISSION_DIAGNOSIS\",\"TREATMENT_PLAN\"]}', 'EMR.Ipd.Diagnosis', 1, 'ALL', 3, 1, @Now, @Now, 1),\n\n    ('SEC-IPD2-01', 'TMPL-IPD-PROGRESS', 'SEC_PROG_WARDROUND', '三级医师查房记录与病情评估', 'INLINE_PARAGRAPH', '{\"elements\":[\"PROGRESS_CONTENT\",\"AUX_EXAM\"]}', 'EMR.Ipd.Progress', 1, 'ALL', 1, 1, @Now, @Now, 1),\n    ('SEC-IPD2-02', 'TMPL-IPD-PROGRESS', 'SEC_PROG_ORDERPLAN', '医嘱调整与后续观察重点', 'INLINE_PARAGRAPH', '{\"elements\":[\"ADMISSION_DIAGNOSIS\",\"TREATMENT_PLAN\"]}', 'EMR.Ipd.ProgressPlan', 1, 'ALL', 2, 1, @Now, @Now, 1),\n\n    ('SEC-IPD3-01', 'TMPL-IPD-DISCHARGE', 'SEC_DIS_SUMMARY', '入院情况与住院诊疗经过', 'INLINE_PARAGRAPH', '{\"elements\":[\"ADMISSION_DIAGNOSIS\",\"DISCHARGE_COURSE\"]}', 'EMR.Ipd.DischargeCourse', 1, 'ALL', 1, 1, @Now, @Now, 1),\n    ('SEC-IPD3-02', 'TMPL-IPD-DISCHARGE', 'SEC_DIS_ADVICE', '出院诊断、带药指导与随访计划', 'INLINE_PARAGRAPH', '{\"elements\":[\"PRIMARY_DIAGNOSIS\",\"DISCHARGE_ADVICE\"]}', 'EMR.Ipd.DischargeAdvice', 1, 'ALL', 2, 1, @Now, @Now, 1),\n\n    ('SEC-WARD1-01', 'TMPL-WARD-ADMISSION', 'SEC_NUR_GENERAL', '入科一般资料与生命体征基线', 'GRID_FORM', '{\"elements\":[\"NURSING_CONSCIOUSNESS\",\"ALLERGY_HISTORY\",\"PHYSICAL_EXAM\"]}', 'EMR.Ward.General', 1, 'ALL', 1, 1, @Now, @Now, 1),\n    ('SEC-WARD1-02', 'TMPL-WARD-ADMISSION', 'SEC_NUR_SCALES', '护理三评量表 (压疮Braden / 自理Barthel / 跌倒Morse)', 'TABLE_GRID', '{\"elements\":[\"NURSING_BRADEN_SCORE\",\"NURSING_BARTHEL_SCORE\",\"NURSING_FALL_SCORE\"]}', 'EMR.Ward.Scales', 1, 'ALL', 2, 1, @Now, @Now, 1),\n    ('SEC-WARD1-03', 'TMPL-WARD-ADMISSION', 'SEC_NUR_PLAN', '专科护理措施与健康宣教记录', 'INLINE_PARAGRAPH', '{\"elements\":[\"NURSING_INTERVENTION\"]}', 'EMR.Ward.Plan', 1, 'ALL', 3, 1, @Now, @Now, 1),\n\n    ('SEC-WARD2-01', 'TMPL-WARD-CRITICAL', 'SEC_CRIT_VITALS', '危重患者严密生命体征与神志监测', 'TABLE_GRID', '{\"elements\":[\"NURSING_CONSCIOUSNESS\",\"PHYSICAL_EXAM\",\"NURSING_BRADEN_SCORE\",\"NURSING_FALL_SCORE\"]}', 'EMR.Ward.CritVitals', 1, 'ALL', 1, 1, @Now, @Now, 1),\n    ('SEC-WARD2-02', 'TMPL-WARD-CRITICAL', 'SEC_CRIT_ACTION', '特级护理管路维护与抢救护理记录', 'INLINE_PARAGRAPH', '{\"elements\":[\"NURSING_INTERVENTION\"]}', 'EMR.Ward.CritAction', 1, 'ALL', 2, 1, @Now, @Now, 1)", new
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
		await conn.ExecuteAsync("INSERT OR IGNORE INTO cfg_field_definitions\n    (id, form_schema_id, field_code, field_label, data_type, ursa_control_type, control_key, dict_code, unit_text, validation_rule_json, visibility_expr, is_indexed, sort_order, ext_data_json, schema_version, created_at_ms, updated_at_ms, version_no)\nVALUES\n    ('FIELD-RX-ITEM-01', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'DRUG_NAME', '药品名称', 'STRING', 'u:TextBox', 'PRESCRIPTION.Dynamic.DrugName', NULL, NULL, '{\"required\":true,\"placeholder\":\"输入药品名称\",\"width\":150,\"system\":true,\"editable\":true}', 'ALL', 0, 0, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-02', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'SPEC', '规格', 'STRING', 'u:TextBox', 'PRESCRIPTION.Dynamic.Spec', NULL, NULL, '{\"required\":false,\"placeholder\":\"输入规格\",\"width\":118,\"system\":true,\"editable\":false}', 'ALL', 0, 1, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-03', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'DOSE', '剂量', 'STRING', 'u:TextBox', 'PRESCRIPTION.Dynamic.Dose', NULL, NULL, '{\"required\":false,\"placeholder\":\"输入剂量\",\"width\":90,\"system\":true,\"editable\":true}', 'ALL', 0, 2, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-04', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'FREQ', '频次', 'STRING', 'u:TextBox', 'PRESCRIPTION.Dynamic.Frequency', NULL, NULL, '{\"required\":false,\"placeholder\":\"输入频次\",\"width\":148,\"system\":true,\"editable\":true}', 'ALL', 0, 3, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-05', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'ROUTE', '途径', 'STRING', 'u:TextBox', 'PRESCRIPTION.Dynamic.Route', NULL, NULL, '{\"required\":false,\"placeholder\":\"输入给药途径\",\"width\":96,\"system\":true,\"editable\":true}', 'ALL', 0, 4, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-06', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'QUANTITY', '数量', 'STRING', 'u:TextBox', 'PRESCRIPTION.Dynamic.Quantity', NULL, NULL, '{\"required\":false,\"placeholder\":\"输入数量\",\"width\":82,\"system\":true,\"editable\":true}', 'ALL', 0, 5, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-07', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'AMOUNT', '金额', 'STRING', 'u:TextBox', 'PRESCRIPTION.Dynamic.Amount', NULL, NULL, '{\"required\":false,\"placeholder\":\"输入金额\",\"width\":90,\"system\":true,\"editable\":false}', 'ALL', 0, 6, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-08', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'SKIN_TEST_STATUS', '皮试状态', 'STRING', 'u:TextBox', 'PRESCRIPTION.Dynamic.SkinTestStatus', NULL, NULL, '{\"required\":false,\"width\":132,\"system\":true,\"editable\":false}', 'ALL', 0, 7, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-09', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'AUDIT_STATUS', '审核结果', 'STRING', 'u:TextBox', 'PRESCRIPTION.Dynamic.AuditStatus', NULL, NULL, '{\"required\":false,\"width\":104,\"system\":true,\"editable\":false}', 'ALL', 0, 8, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-10', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'MEDICATION_NOTE', '用药备注', 'STRING', 'u:TextBox', 'PRESCRIPTION.Dynamic.MedicationNote', NULL, NULL, '{\"required\":false,\"placeholder\":\"补充用药说明\",\"width\":150,\"system\":true,\"editable\":true}', 'ALL', 0, 9, NULL, 1, @Now, @Now, 1),\n    ('FIELD-RX-ITEM-11', 'SCHEMA-PRESCRIPTION-ITEM-FIELDS', 'INFUSION_NOTE', '给药说明', 'STRING', 'u:TextBox', 'PRESCRIPTION.Dynamic.InfusionNote', NULL, NULL, '{\"required\":false,\"placeholder\":\"例如：静脉滴注 40 滴/分\",\"width\":160,\"system\":true,\"editable\":true}', 'ALL', 0, 10, NULL, 1, @Now, @Now, 1)", new
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