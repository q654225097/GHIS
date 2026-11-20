using System;
using System.Data.Common;
using System.Threading.Tasks;
using Dapper;

namespace HIS.Server.Host;

public static partial class DbSeedData
{
    public static async Task SeedEmrLevel7DataAsync(DbConnection conn, long nowMs)
    {
        // 1. Clinical Pathways
        await conn.ExecuteAsync("""
INSERT OR IGNORE INTO cpw_pathways
    (id, pathway_code, pathway_name, applicable_icd10_json, standard_days, min_cost, max_cost, version, status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('CPW-CAP-01', 'CPW-J18.9', '社区获得性肺炎临床路径', '["J18.900","J18.901","J15.900"]', 7, 3000.00, 6000.00, 'v1.0', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('CPW-T2DM-01', 'CPW-E11.9', '2型糖尿病标准化诊疗路径', '["E11.900","E11.651","E11.201"]', 8, 3500.00, 7000.00, 'v1.0', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('CPW-APP-01', 'CPW-K35.8', '急性单纯性阑尾炎临床路径', '["K35.800","K35.801"]', 4, 4000.00, 8000.00, 'v1.0', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

INSERT OR IGNORE INTO cpw_stages
    (id, pathway_id, stage_order, stage_name, day_from, day_to, emr_template_snippet_json, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('STG-CAP-01', 'CPW-CAP-01', 1, '第1天：入院病情评估与初始抗感染', 1, 1, '{"SectionTitle":"临床路径：初始评估","Template":"患者今日入径，评估 CURB-65 评分，开立血常规、降钙素原及胸部影像，给予初始经验性抗感染。"}', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('STG-CAP-02', 'CPW-CAP-01', 2, '第2-4天：病情观察与序贯治疗', 2, 4, '{"SectionTitle":"临床路径：病情观察","Template":"患者体温下降，咳嗽咳痰好转，复查炎症指标并考虑口服序贯抗生素。"}', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('STG-CAP-03', 'CPW-CAP-01', 3, '第5-7天：出院前评估与健康教育', 5, 7, '{"SectionTitle":"临床路径：出院评估","Template":"患者临床症状缓解，肺部体征明显吸收，符合出院标准，准予出院。"}', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

INSERT OR IGNORE INTO cpw_stage_items
    (id, stage_id, item_category, item_code, item_name, is_mandatory, is_default_selected, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('ITM-CAP-01', 'STG-CAP-01', 'LAB', 'LAB-CBC', '全血细胞分析+五分类 (CBC)', 1, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('ITM-CAP-02', 'STG-CAP-01', 'EXAM', 'EXAM-CT-CHEST', '胸部CT平扫', 1, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('ITM-CAP-03', 'STG-CAP-01', 'DRUG', 'DRUG-01', '注射用青霉素钠', 1, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('ITM-CAP-04', 'STG-CAP-02', 'LAB', 'LAB-CRP', '超敏C反应蛋白 (hs-CRP)', 1, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('ITM-CAP-05', 'STG-CAP-03', 'NURSING', 'NUR-DISCHARGE-EDU', '出院呼吸康复健康宣教', 1, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);
""", new { Now = nowMs });

        // 2. CDSS Knowledge Base Documents & Rules
        await conn.ExecuteAsync("""
INSERT OR IGNORE INTO kb_documents
    (id, doc_code, category, specialty_dept, title, icd10_codes_json, content_markdown, source_org, version_tag, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('KB-DOC-01', 'GUIDE-CAP-2024', 'GUIDELINE', '呼吸内科', '中国成人社区获得性肺炎诊断与治疗指南 (2024年版)', '["J18.9","J15.9"]', '# 社区获得性肺炎诊疗规范\n\n1. 诊断依据：咳嗽、咳痰、发热、肺部湿性啰音、影像实质浸润。\n2. 抗菌治疗原则：首选β-内酰胺类联合大环内酯类。', '中华医学会呼吸病学分会', '2024.1', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('KB-DOC-02', 'GUIDE-T2DM-2023', 'GUIDELINE', '内分泌科', '国家基层糖尿病防治管理指南 (2023)', '["E11.9"]', '# 2型糖尿病综合管理规范\n\n1. 控制目标：空腹血糖 4.4~7.0 mmol/L，HbA1c < 7.0%。\n2. 肾功能监测：eGFR < 30 ml/min 禁用二甲双胍。', '中华医学会糖尿病学分会', '2023.2', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('KB-DOC-03', 'SOP-BLOOD-2022', 'SOP', '输血科', '临床输血技术规范与相容性核对 SOP', '[]', '# 临床输血标准作业程序\n\n1. 必须核对 ABO 与 Rh(D) 同型输血。\n2. 输血前 15 分钟密切观察体征，若发生寒战高热立即终止。', '国家卫生健康委员会', 'WS/T 800-2022', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

INSERT OR IGNORE INTO kb_clinical_rules
    (id, rule_code, rule_domain, severity, condition_dsl_json, explanation_text, action_suggestion_text, evidence_doc_id, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('KB-RULE-01', 'RULE-K-POTASSIUM-HIGH', 'ORDER', 'BLOCK', '{"trigger":"POTASSIUM","max":5.5}', '【高钾血症用药强拦截】患者最新血钾 > 5.5 mmol/L，严禁开立氯化钾或保钾利尿剂。', '建议停用排钾阻滞剂，可考虑降钾树脂或利尿剂治疗。', 'KB-DOC-01', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('KB-RULE-02', 'RULE-RENAL-EGFR-LOW', 'ORDER', 'BLOCK', '{"trigger":"EGFR","min":30}', '【重度肾功能不全用药警示】患者 eGFR < 30 ml/min，二甲双胍具有乳酸酸中毒致死风险，造影剂具有急性肾损伤风险。', '建议减量或停用肾毒性药物，改用胰岛素降糖。', 'KB-DOC-02', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('KB-RULE-03', 'RULE-GENDER-FEMALE-RESTRICT', 'ORDER', 'BLOCK', '{"gender":"F","prohibited":["非那雄胺","前列地尔"]}', '【性别禁忌强拦截】女性患者严禁使用非那雄胺等男性专科药物。', '请撤除该医嘱或处方。', 'KB-DOC-01', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('KB-RULE-04', 'RULE-HIGH-ALERT-DOUBLE-CHECK', 'ORDER', 'WARNING', '{"is_high_alert":1}', '【高警示药品强警示】该药品属于国家高警示药品（A/B级），具有高度用药差错危害。', '实施前必须由两名医护人员进行双人独立核对并记录。', 'KB-DOC-03', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);
""", new { Now = nowMs });

        // 3. General Treatment Catalog
        await conn.ExecuteAsync("""
INSERT OR IGNORE INTO trt_treatment_catalog
    (id, catalog_code, catalog_name, category, exec_dept_id, is_high_risk, unit_price, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('TRT-CAT-01', 'TRT-DIALYSIS', '血液透析治疗 (HD)', 'DIALYSIS', 'DEPT-MEDTECH-01', 1, 420.00, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('TRT-CAT-02', 'TRT-CHEMO', '静脉化疗输注治疗', 'CHEMO', 'DEPT-MEDTECH-01', 1, 280.00, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('TRT-CAT-03', 'TRT-HBO', '高压氧舱综合治疗', 'HBO', 'DEPT-MEDTECH-01', 0, 150.00, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('TRT-CAT-04', 'TRT-REHAB-PT', '偏瘫肢体综合物理康复训练', 'REHAB', 'DEPT-MEDTECH-01', 0, 85.00, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('TRT-CAT-05', 'TRT-PHYSIO-SW', '超短波物理理疗', 'PHYSIO', 'DEPT-MEDTECH-01', 0, 35.00, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

INSERT OR IGNORE INTO trt_parameter_rules
    (id, catalog_id, lab_item_code, parameter_name, formula_or_range_json, param_suggestion_template, warning_notice, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('TRT-RULE-01', 'TRT-CAT-01', 'K', '透析液钾浓度', '{"high":5.5}', '根据血钾自动匹配低钾或标准透析液', '高钾血症患者优先选用 K+ 2.0 mmol/L 透析液', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('TRT-RULE-02', 'TRT-CAT-02', 'WBC', '化疗剂量调整比例', '{"low":3.5}', '白细胞 < 3.5×10⁹/L 建议减量 20%', '骨髓抑制风险预警', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);
""", new { Now = nowMs });

        // 4. Blood Bank Dictionary, Policies & Initial Bags
        await conn.ExecuteAsync("""
INSERT OR IGNORE INTO bld_blood_dictionary
    (id, blood_code, blood_name, capacity_ml, storage_temp_range, validity_days, unit_price, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('BLD-DICT-01', 'RBC', '悬浮红细胞', 200, '2~6℃', 35, 210.00, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('BLD-DICT-02', 'FFP', '新鲜冰冻血浆', 100, '<-18℃', 365, 90.00, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('BLD-DICT-03', 'PLT', '单采血小板', 250, '20~24℃振荡', 5, 1450.00, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('BLD-DICT-04', 'CRYO', '冷沉淀凝血因子', 25, '<-18℃', 365, 50.00, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

INSERT OR IGNORE INTO bld_stock_policies
    (id, blood_code, abo_type, rh_type, safe_stock_bags, warning_stock_bags, surgery_reserve_ratio, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('BLD-POL-01', 'RBC', 'A', 'POSITIVE', 20, 8, 1.2, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('BLD-POL-02', 'RBC', 'B', 'POSITIVE', 20, 8, 1.2, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('BLD-POL-03', 'RBC', 'O', 'POSITIVE', 25, 10, 1.3, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('BLD-POL-04', 'RBC', 'AB', 'POSITIVE', 10, 4, 1.1, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

INSERT OR IGNORE INTO bld_inventory_bags
    (id, bag_barcode, donation_code, source_blood_center, blood_code, abo_type, rh_type, volume_ml, collection_date_int, expiry_date_int, cold_chain_temp, status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('BLD-BAG-01', 'BAG-A-POS-001', 'DON20261101001', '市中心血站', 'RBC', 'A', 'POSITIVE', 200.0, 20261101, 20261205, 4.2, 'IN_STOCK', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('BLD-BAG-02', 'BAG-B-POS-002', 'DON20261101002', '市中心血站', 'RBC', 'B', 'POSITIVE', 200.0, 20261101, 20261205, 4.0, 'IN_STOCK', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('BLD-BAG-03', 'BAG-O-POS-003', 'DON20261101003', '市中心血站', 'RBC', 'O', 'POSITIVE', 200.0, 20261101, 20261205, 3.8, 'IN_STOCK', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('BLD-BAG-04', 'BAG-AB-POS-004', 'DON20261101004', '市中心血站', 'RBC', 'AB', 'POSITIVE', 200.0, 20261101, 20261205, 4.1, 'IN_STOCK', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);
""", new { Now = nowMs });

        // 5. EMR Quality Control Rules
        await conn.ExecuteAsync("""
INSERT OR IGNORE INTO emr_qc_rules
    (id, rule_code, rule_name, qc_dimension, doc_type_code, time_limit_hours, deduct_points, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('QC-RULE-01', 'QC-TIME-ADMISSION-24H', '入院记录书写时限质控 (24小时)', 'TIMELINESS', 'ADMISSION', 24, 10.0, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('QC-RULE-02', 'QC-TIME-FIRST-COURSE-8H', '首次病程记录书写时限质控 (8小时)', 'TIMELINESS', 'FIRST_COURSE', 8, 5.0, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('QC-RULE-03', 'QC-TIME-RESCUE-6H', '抢救记录据实补记时限质控 (6小时)', 'TIMELINESS', 'RESCUE', 6, 15.0, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('QC-RULE-04', 'QC-TIME-POSTOP-24H', '术后首次病程记录时限质控 (24小时)', 'TIMELINESS', 'POSTOP_COURSE', 24, 8.0, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('QC-RULE-05', 'QC-TIME-DISCHARGE-24H', '出院记录书写时限质控 (24小时)', 'TIMELINESS', 'DISCHARGE', 24, 10.0, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);
""", new { Now = nowMs });

        // 6. Specimen Dictionary
        await conn.ExecuteAsync("""
INSERT OR IGNORE INTO cfg_specimen_dictionary
    (id, specimen_code, specimen_name, tube_color_type, min_volume_ml, storage_temp_c, collection_notes, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    ('SPEC-01', 'SPEC-EDTA', 'EDTA抗凝全血 (血常规/血型)', '紫色(EDTA-K2抗凝管)', 2.0, '2~8℃', '采集后立即颠倒混匀5~8次，严防凝血', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('SPEC-02', 'SPEC-SERUM', '干燥血清 (生化/免疫)', '红色/黄色(促凝分离胶管)', 4.0, '室温避光', '静置30分钟后再行离心分离', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
    ('SPEC-03', 'SPEC-CITRATE', '枸橼酸钠抗凝血 (凝血四项)', '蓝色(枸橼酸钠1:9抗凝管)', 2.7, '室温', '采血量必须严格对准刻度线', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);
""", new { Now = nowMs });
    }
}
