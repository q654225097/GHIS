using System;
using System.Data.Common;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;

namespace HIS.BuildingBlocks.Database;

public static partial class DbSchemaInitializer
{
    private static async Task InitializeMasterDataCompleteAsync(DbConnection conn)
    {
        // 1. 新建缺失的核心主数据与标准术语表
        await conn.ExecuteAsync(@"
-- 1) 医疗机构 / 院区主数据 (GB 32100-2015 / WS/T 364.14-2023 / 国家医保定点机构码)
CREATE TABLE IF NOT EXISTS mdm_organizations (
    id VARCHAR(36) PRIMARY KEY,
    org_code VARCHAR(64) NOT NULL UNIQUE,
    uscc VARCHAR(18) NOT NULL UNIQUE,
    yb_org_code VARCHAR(64),
    org_name VARCHAR(200) NOT NULL,
    campus_tag VARCHAR(64) NOT NULL DEFAULT 'MAIN',
    org_level VARCHAR(32) NOT NULL DEFAULT '三级甲等',
    org_category_code VARCHAR(32) NOT NULL DEFAULT 'A100',
    admin_region_code VARCHAR(12) NOT NULL DEFAULT '110101',
    practice_license_no VARCHAR(80),
    is_active SMALLINT NOT NULL DEFAULT 1,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

-- 2) 诊疗与医疗服务项目主数据 (国家医疗服务价格项目分类与代码)
CREATE TABLE IF NOT EXISTS cfg_order_items (
    id VARCHAR(36) PRIMARY KEY,
    order_item_code VARCHAR(64) NOT NULL UNIQUE,
    order_item_name VARCHAR(200) NOT NULL,
    pinyin_code VARCHAR(64),
    order_category VARCHAR(32) NOT NULL DEFAULT 'EXAM',
    exec_dept_id VARCHAR(36),
    sample_or_body_part VARCHAR(128),
    item_connotation VARCHAR(1000),
    price_item_code VARCHAR(64),
    yb_service_code VARCHAR(64),
    is_active SMALLINT NOT NULL DEFAULT 1,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_cfg_order_items_cat ON cfg_order_items (order_category, is_active);

-- 3) 收费项目与医保目录生效期对照表 (charge_item_id + ins_type + eff_date)
CREATE TABLE IF NOT EXISTS cfg_charge_item_mappings (
    id VARCHAR(36) PRIMARY KEY,
    charge_item_id VARCHAR(36) NOT NULL,
    ins_type VARCHAR(32) NOT NULL,
    eff_date_int INT NOT NULL,
    exp_date_int INT NOT NULL DEFAULT 20991231,
    yb_catalog_code VARCHAR(64) NOT NULL,
    ins_grade VARCHAR(16) NOT NULL DEFAULT 'CLASS_A',
    self_pay_ratio DECIMAL(18,4) NOT NULL DEFAULT 0,
    is_active SMALLINT NOT NULL DEFAULT 1,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1,
    UNIQUE (charge_item_id, ins_type, eff_date_int)
);

-- 4) 中医病证字典 (医保中医诊断 95国标 6位编码)
CREATE TABLE IF NOT EXISTS cfg_tcm_diagnoses (
    id VARCHAR(36) PRIMARY KEY,
    std_ver VARCHAR(32) NOT NULL DEFAULT 'TCM-GB95',
    tcm_code VARCHAR(32) NOT NULL,
    tcm_name VARCHAR(200) NOT NULL,
    pinyin_code VARCHAR(64),
    tcm_type VARCHAR(16) NOT NULL DEFAULT 'DISEASE',
    is_active SMALLINT NOT NULL DEFAULT 1,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1,
    UNIQUE (std_ver, tcm_code)
);

-- 5) 手术操作字典 (ICD-9-CM-3 医保版)
CREATE TABLE IF NOT EXISTS cfg_icd9_operations (
    id VARCHAR(36) PRIMARY KEY,
    std_ver VARCHAR(32) NOT NULL DEFAULT 'ICD9-CM3-YB',
    icd9_code VARCHAR(32) NOT NULL,
    operation_name VARCHAR(200) NOT NULL,
    pinyin_code VARCHAR(64),
    surgery_level INT NOT NULL DEFAULT 1,
    is_minimally_invasive SMALLINT NOT NULL DEFAULT 0,
    is_interventional SMALLINT NOT NULL DEFAULT 0,
    is_day_surgery SMALLINT NOT NULL DEFAULT 0,
    default_incision_grade VARCHAR(16) NOT NULL DEFAULT 'I',
    is_active SMALLINT NOT NULL DEFAULT 1,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1,
    UNIQUE (std_ver, icd9_code)
);

-- 6) 检验/检查标准项目字典 (LOINC / SNOMED CT / WS/T 364)
CREATE TABLE IF NOT EXISTS cfg_exam_std_items (
    id VARCHAR(36) PRIMARY KEY,
    std_code VARCHAR(64) NOT NULL UNIQUE,
    std_name VARCHAR(200) NOT NULL,
    item_kind VARCHAR(16) NOT NULL DEFAULT 'LAB',
    specimen_type VARCHAR(64),
    result_unit VARCHAR(32),
    ref_range VARCHAR(128),
    critical_low VARCHAR(64),
    critical_high VARCHAR(64),
    loinc_code VARCHAR(64),
    body_part_code VARCHAR(64),
    is_active SMALLINT NOT NULL DEFAULT 1,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
");

        // 2. 扩展现有核心实体与临床术语表字段（向后兼容增量扩列）
        // 科室与病区 (WS/T 363.14 / 医保科室分类)
        await EnsureColumnAsync(conn, "sys_departments", "org_id", "VARCHAR(36)");
        await EnsureColumnAsync(conn, "sys_departments", "opd_flag", "SMALLINT NOT NULL DEFAULT 1");
        await EnsureColumnAsync(conn, "sys_departments", "ipd_flag", "SMALLINT NOT NULL DEFAULT 1");
        await EnsureColumnAsync(conn, "sys_departments", "std_subject_code", "VARCHAR(32)");
        await EnsureColumnAsync(conn, "sys_departments", "cost_center_code", "VARCHAR(64)");
        await EnsureColumnAsync(conn, "sys_departments", "yb_dept_code", "VARCHAR(32)");

        // 床位资源 (区分编制床位与开放床位、性别限制、收费等级)
        await EnsureColumnAsync(conn, "ipd_beds", "charge_level", "VARCHAR(32) NOT NULL DEFAULT 'NORMAL'");
        await EnsureColumnAsync(conn, "ipd_beds", "gender_limit", "VARCHAR(16) NOT NULL DEFAULT 'ALL'");
        await EnsureColumnAsync(conn, "ipd_beds", "is_authorized_bed", "SMALLINT NOT NULL DEFAULT 1");
        await EnsureColumnAsync(conn, "ipd_beds", "is_open_bed", "SMALLINT NOT NULL DEFAULT 1");

        // 医护与员工 (WS/T 364.15 / 医保医师护师码 / 三级权限)
        await EnsureColumnAsync(conn, "hr_employees", "id_type", "VARCHAR(32) NOT NULL DEFAULT 'ID_CARD'");
        await EnsureColumnAsync(conn, "hr_employees", "practice_cert_no", "VARCHAR(64)");
        await EnsureColumnAsync(conn, "hr_employees", "prescribe_level", "INT NOT NULL DEFAULT 1");
        await EnsureColumnAsync(conn, "hr_employees", "antibiotic_level", "INT NOT NULL DEFAULT 1");
        await EnsureColumnAsync(conn, "hr_employees", "narcotic_level", "INT NOT NULL DEFAULT 0");
        await EnsureColumnAsync(conn, "hr_employees", "yb_staff_code", "VARCHAR(64)");
        await EnsureColumnAsync(conn, "sys_users", "antibiotic_level", "INT NOT NULL DEFAULT 1");
        await EnsureColumnAsync(conn, "sys_users", "narcotic_level", "INT NOT NULL DEFAULT 0");
        await EnsureColumnAsync(conn, "sys_users", "yb_staff_code", "VARCHAR(64)");

        // 患者主索引 EMPI (WS/T 364-2023 / GB/T 2260)
        await EnsureColumnAsync(conn, "pat_patients", "ethnicity_code", "VARCHAR(16) NOT NULL DEFAULT '01'");
        await EnsureColumnAsync(conn, "pat_patients", "marital_status_code", "VARCHAR(16) NOT NULL DEFAULT '20'");
        await EnsureColumnAsync(conn, "pat_patients", "current_address", "VARCHAR(255)");
        await EnsureColumnAsync(conn, "pat_patients", "region_code", "VARCHAR(12) NOT NULL DEFAULT '110101'");
        await EnsureColumnAsync(conn, "pat_patients", "blood_type_code", "VARCHAR(16) NOT NULL DEFAULT 'UNKNOWN'");
        await EnsureColumnAsync(conn, "pat_patients", "has_allergy_flag", "SMALLINT NOT NULL DEFAULT 0");
        await EnsureColumnAsync(conn, "pat_patients", "merge_status", "VARCHAR(16) NOT NULL DEFAULT 'ACTIVE'");

        // 供应商 / 生产厂商 (GB 32100-2015 / GSP)
        await EnsureColumnAsync(conn, "inv_suppliers", "uscc", "VARCHAR(18)");
        await EnsureColumnAsync(conn, "inv_suppliers", "entity_type", "VARCHAR(32) NOT NULL DEFAULT 'DRUG_DISTRIBUTOR'");
        await EnsureColumnAsync(conn, "inv_suppliers", "gsp_cert_no", "VARCHAR(80)");
        await EnsureColumnAsync(conn, "inv_suppliers", "gsp_expiry_int", "INT NOT NULL DEFAULT 20991231");
        await EnsureColumnAsync(conn, "inv_suppliers", "bank_account", "VARCHAR(64)");
        await EnsureColumnAsync(conn, "inv_suppliers", "bank_name", "VARCHAR(128)");

        // 药品主数据 (通用名/商品名/剂型/换算比/ATC/基药/集采/DDD/医保码/本位码)
        await EnsureColumnAsync(conn, "inv_drugs", "generic_name", "VARCHAR(200)");
        await EnsureColumnAsync(conn, "inv_drugs", "trade_name", "VARCHAR(200)");
        await EnsureColumnAsync(conn, "inv_drugs", "dosage_form", "VARCHAR(64)");
        await EnsureColumnAsync(conn, "inv_drugs", "pack_unit", "VARCHAR(32)");
        await EnsureColumnAsync(conn, "inv_drugs", "base_dose_unit", "VARCHAR(32)");
        await EnsureColumnAsync(conn, "inv_drugs", "conversion_ratio", "DECIMAL(18,4) NOT NULL DEFAULT 1");
        await EnsureColumnAsync(conn, "inv_drugs", "approval_no", "VARCHAR(64)");
        await EnsureColumnAsync(conn, "inv_drugs", "manufacturer", "VARCHAR(200)");
        await EnsureColumnAsync(conn, "inv_drugs", "atc_code", "VARCHAR(32)");
        await EnsureColumnAsync(conn, "inv_drugs", "is_essential_drug", "SMALLINT NOT NULL DEFAULT 0");
        await EnsureColumnAsync(conn, "inv_drugs", "is_central_procured", "SMALLINT NOT NULL DEFAULT 0");
        await EnsureColumnAsync(conn, "inv_drugs", "ddd_value", "DECIMAL(18,4)");
        await EnsureColumnAsync(conn, "inv_drugs", "yb_drug_code", "VARCHAR(64)");
        await EnsureColumnAsync(conn, "inv_drugs", "drug_standard_code", "VARCHAR(64)");

        // 医用耗材主数据 (UDI-DI / 植入物 / 一次性 / 注册证 / 可收费 / 医保耗材码)
        await EnsureColumnAsync(conn, "csm_consumables", "udi_di", "VARCHAR(64)");
        await EnsureColumnAsync(conn, "csm_consumables", "is_implant", "SMALLINT NOT NULL DEFAULT 0");
        await EnsureColumnAsync(conn, "csm_consumables", "is_disposable", "SMALLINT NOT NULL DEFAULT 1");
        await EnsureColumnAsync(conn, "csm_consumables", "reg_cert_no", "VARCHAR(80)");
        await EnsureColumnAsync(conn, "csm_consumables", "manufacturer", "VARCHAR(200)");
        await EnsureColumnAsync(conn, "csm_consumables", "is_chargeable", "SMALLINT NOT NULL DEFAULT 1");
        await EnsureColumnAsync(conn, "csm_consumables", "yb_consumable_code", "VARCHAR(64)");

        // 收费项目 (自付比例 / 生效失效日期 / 物价编码)
        await EnsureColumnAsync(conn, "cfg_price_items", "self_pay_ratio", "DECIMAL(18,4) NOT NULL DEFAULT 0");
        await EnsureColumnAsync(conn, "cfg_price_items", "eff_date_int", "INT NOT NULL DEFAULT 20200101");
        await EnsureColumnAsync(conn, "cfg_price_items", "exp_date_int", "INT NOT NULL DEFAULT 20991231");
        await EnsureColumnAsync(conn, "cfg_price_items", "price_code", "VARCHAR(64)");

        // 西医疾病诊断 (标准版本 / 五笔 / 类目层级 / DRG-DIP标志)
        await EnsureColumnAsync(conn, "cfg_icd10_diagnoses", "std_ver", "VARCHAR(32) NOT NULL DEFAULT 'ICD10-YB2.0'");
        await EnsureColumnAsync(conn, "cfg_icd10_diagnoses", "wubi_code", "VARCHAR(64)");
        await EnsureColumnAsync(conn, "cfg_icd10_diagnoses", "chapter_code", "VARCHAR(32)");
        await EnsureColumnAsync(conn, "cfg_icd10_diagnoses", "drg_dip_flag", "SMALLINT NOT NULL DEFAULT 1");

        // 通用参考值域字典 (国标/行标映射码)
        await EnsureColumnAsync(conn, "cfg_clinical_dictionaries", "std_mapping_code", "VARCHAR(64)");

        // 3. 预置国家/行业标准主数据种子
        long now = SystemTime.UtcNowMs;

        await conn.ExecuteAsync(@"
INSERT OR IGNORE INTO mdm_organizations
(id, org_code, uscc, yb_org_code, org_name, campus_tag, org_level, org_category_code, admin_region_code, practice_license_no, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('CAMPUS-01', 'ORG-001', '12110000400001001X', 'H11010100001', 'GHIS 示范中心医院（本部）', 'MAIN', '三级甲等', 'A100', '110101', 'PDY00001111010111A1001', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('CAMPUS-02', 'ORG-002', '12110000400001002Y', 'H11010500002', 'GHIS 示范中心医院（东院区）', 'EAST', '三级甲等', 'A100', '110105', 'PDY00002111010511A1001', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);
", new { Now = now });

        await conn.ExecuteAsync(@"
INSERT OR IGNORE INTO cfg_order_items
(id, order_item_code, order_item_name, pinyin_code, order_category, exec_dept_id, sample_or_body_part, item_connotation, price_item_code, yb_service_code, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('ORDI-LAB-CBC', 'LAB-CBC', '血细胞分析(五分类血常规)', 'XCG', 'LAB', 'DEPT-LAB-01', '静脉全血(EDTA-K2)', '含白细胞分类、红细胞及血小板计数', 'LAB-CBC', '250101015', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('ORDI-EXM-DR', 'EXM-DR', '数字化摄影(胸部正位DR)', 'XBDR', 'EXAM', 'DEPT-RAD-01', '胸部正位', '含DR曝光及放射科报告诊断', 'EXM-DR', '210101001', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('ORDI-TRT-NEB', 'TRT-NEB', '氧气雾化吸入治疗', 'WHXR', 'TREATMENT', 'DEPT-WARD-01', '呼吸道', '含一次性雾化装置与雾化护理观察', 'TRT-NEB', '120200001', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('ORDI-SUR-APP', 'SUR-APP', '腹腔镜下阑尾切除术', 'FQJLW', 'SURGERY', 'DEPT-OR-01', '右下腹阑尾', '含腔镜探查、阑尾切除及止血闭合', 'SUR-APP', '331002011', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('ORDI-NUR-L1', 'NUR-L1', '一级护理', 'YJHL', 'NURSING', 'DEPT-WARD-01', '病区床旁', '每小时巡视观察生命体征与基础护理', 'NUR-L1', '120100002', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('ORDI-DIET-DM', 'DIET-DM', '糖尿病治疗膳食', 'TNBS', 'DIET', 'DEPT-NUT-01', '营养膳食', '按每日总热量与碳水化合物比例配餐', 'DIET-DM', '120300001', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);
", new { Now = now });

        await conn.ExecuteAsync(@"
INSERT OR IGNORE INTO cfg_charge_item_mappings
(id, charge_item_id, ins_type, eff_date_int, exp_date_int, yb_catalog_code, ins_grade, self_pay_ratio, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('CIM-CBC-EMP', 'PI-LAB-CBC', 'EMPLOYEE_INS', 20240101, 20991231, '250101015000001', 'CLASS_A', 0.00, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('CIM-CYSC-EMP', 'PI-LAB-CYSC', 'EMPLOYEE_INS', 20240101, 20991231, '250307015000001', 'CLASS_B', 0.10, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('CIM-PHY-EMP', 'PI-PHY-GEN', 'EMPLOYEE_INS', 20240101, 20991231, '110100001000001', 'SELF_PAY', 1.00, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);
", new { Now = now });

        await conn.ExecuteAsync(@"
INSERT OR IGNORE INTO cfg_tcm_diagnoses
(id, std_ver, tcm_code, tcm_name, pinyin_code, tcm_type, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('TCM-001', 'TCM-GB95', 'BNF010', '感冒病', 'GMB', 'DISEASE', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('TCM-002', 'TCM-GB95', 'BNF030', '咳嗽病', 'KSB', 'DISEASE', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('TCM-003', 'TCM-GB95', 'BNX010', '胸痹心痛病', 'XBXTB', 'DISEASE', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('TCM-101', 'TCM-GB95', 'ZBF011', '风寒束肺证', 'FHSFZ', 'SYNDROME', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('TCM-102', 'TCM-GB95', 'ZBF022', '风热犯肺证', 'FRFFZ', 'SYNDROME', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('TCM-103', 'TCM-GB95', 'ZBX031', '心血瘀阻证', 'XXYZZ', 'SYNDROME', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);
", new { Now = now });

        await conn.ExecuteAsync(@"
INSERT OR IGNORE INTO cfg_icd9_operations
(id, std_ver, icd9_code, operation_name, pinyin_code, surgery_level, is_minimally_invasive, is_interventional, is_day_surgery, default_incision_grade, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('OPR-001', 'ICD9-CM3-YB', '47.0100', '腹腔镜下阑尾切除术', 'FQJLWQCS', 2, 1, 0, 0, 'II', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('OPR-002', 'ICD9-CM3-YB', '51.2300', '腹腔镜下胆囊切除术', 'FQJDNQCS', 3, 1, 0, 1, 'II', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('OPR-003', 'ICD9-CM3-YB', '36.0700', '经皮冠状动脉药物洗脱支架置入术', 'JRGZDMZJ', 4, 1, 1, 0, '0', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('OPR-004', 'ICD9-CM3-YB', '13.4100', '超声乳化白内障吸除术', 'CSRHBNZ', 2, 1, 0, 1, 'I', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);
", new { Now = now });

        await conn.ExecuteAsync(@"
INSERT OR IGNORE INTO cfg_exam_std_items
(id, std_code, std_name, item_kind, specimen_type, result_unit, ref_range, critical_low, critical_high, loinc_code, body_part_code, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('EXS-HGB', 'STD-LAB-HGB', '血红蛋白测定 (HGB)', 'LAB', 'VENOUS_BLOOD', 'g/L', '115-175', '50', '200', '718-7', NULL, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('EXS-K', 'STD-LAB-K', '血清钾测定 (K+)', 'LAB', 'VENOUS_BLOOD', 'mmol/L', '3.5-5.3', '2.8', '6.2', '2823-3', NULL, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('EXS-GLU', 'STD-LAB-GLU', '空腹血糖测定 (GLU)', 'LAB', 'VENOUS_BLOOD', 'mmol/L', '3.9-6.1', '2.2', '22.2', '2345-7', NULL, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('EXS-CR', 'STD-LAB-CR', '血清肌酐测定 (Cr)', 'LAB', 'VENOUS_BLOOD', 'μmol/L', '57-97', NULL, '707', '2160-0', NULL, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('EXS-CXR', 'STD-EXM-CXR', '胸部正位数字化摄影 (DR)', 'EXAM', NULL, NULL, '双肺纹理清晰', NULL, '气胸/大量胸腔积液', '30746-2', 'CHEST_PA', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);
", new { Now = now });

        // 补充通用参考值域字典分类与国标映射码
        var extraCats = new (string Type, string Name, string Group, string Desc)[]
        {
            ("ETHNICITY", "民族字典 (GB 3304)", "BASIC", "患者主索引 EMPI 与病案首页民族标准编码"),
            ("ADMIT_WAY", "入院途径字典 (WS/T 364)", "INPATIENT", "急诊/门诊/其他医疗机构转入"),
            ("DISCHARGE_WAY", "离院方式字典 (WS/T 364)", "INPATIENT", "医嘱离院/医嘱转院/非医嘱离院/死亡"),
            ("ORDER_FREQUENCY", "给药频次字典", "CLINICAL", "QD/BID/TID/ST 等医嘱频次标准"),
            ("ALLERGEN", "过敏源字典", "CLINICAL", "常见药物/食物/接触过敏源")
        };
        foreach (var cat in extraCats)
        {
            await conn.ExecuteAsync(@"
                INSERT OR IGNORE INTO cfg_dict_categories
                (id, dict_type, dict_name, category_group, is_system, description, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES (@Id, @Type, @Name, @Grp, 1, @Desc, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1)",
                new { Id = Guid.NewGuid().ToString("D"), cat.Type, cat.Name, Grp = cat.Group, cat.Desc, Now = now });
        }

        var extraDictItems = new (string Type, string Code, string Name, string Py, int Sort, int Def, string StdCode)[]
        {
            ("ETHNICITY", "01", "汉族", "HZ", 1, 1, "GB3304-01"),
            ("ETHNICITY", "02", "蒙古族", "MGZ", 2, 0, "GB3304-02"),
            ("ETHNICITY", "03", "回族", "HZ", 3, 0, "GB3304-03"),
            ("ETHNICITY", "04", "藏族", "ZZ", 4, 0, "GB3304-04"),
            ("ADMIT_WAY", "1", "急诊", "JZ", 1, 0, "WST364-CV09.00.403-1"),
            ("ADMIT_WAY", "2", "门诊", "MZ", 2, 1, "WST364-CV09.00.403-2"),
            ("ADMIT_WAY", "3", "其他医疗机构转入", "QTZR", 3, 0, "WST364-CV09.00.403-3"),
            ("DISCHARGE_WAY", "1", "医嘱离院", "YZLY", 1, 1, "WST364-CV06.00.226-1"),
            ("DISCHARGE_WAY", "2", "医嘱转院", "YZZY", 2, 0, "WST364-CV06.00.226-2"),
            ("DISCHARGE_WAY", "4", "非医嘱离院", "FYZLY", 3, 0, "WST364-CV06.00.226-4"),
            ("DISCHARGE_WAY", "5", "死亡", "SW", 4, 0, "WST364-CV06.00.226-5"),
            ("ORDER_FREQUENCY", "QD", "每日一次 (qd)", "MRYC", 1, 1, "FREQ-QD"),
            ("ORDER_FREQUENCY", "BID", "每日两次 (bid)", "MRLC", 2, 0, "FREQ-BID"),
            ("ORDER_FREQUENCY", "TID", "每日三次 (tid)", "MRSC", 3, 0, "FREQ-TID"),
            ("ORDER_FREQUENCY", "ST", "立即执行 (st)", "LJZX", 4, 0, "FREQ-ST"),
            ("ALLERGEN", "PENICILLIN", "青霉素类", "QMS", 1, 0, "ALG-J01C"),
            ("ALLERGEN", "CEPHALOSPORIN", "头孢菌素类", "TBJS", 2, 0, "ALG-J01D"),
            ("ALLERGEN", "SULFONAMIDE", "磺胺类", "HAL", 3, 0, "ALG-J01E")
        };
        foreach (var it in extraDictItems)
        {
            int exists = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM cfg_clinical_dictionaries WHERE dict_type = @Type AND item_code = @Code",
                new { it.Type, it.Code });
            if (exists == 0)
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO cfg_clinical_dictionaries
                    (id, dict_type, item_code, item_name, pinyin_code, parent_code, sort_order, is_default, is_active, std_mapping_code, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                    VALUES (@Id, @Type, @Code, @Name, @Py, NULL, @Sort, @Def, 1, @StdCode, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1)",
                    new { Id = Guid.NewGuid().ToString("D"), it.Type, it.Code, it.Name, it.Py, it.Sort, Def = it.Def, it.StdCode, Now = now });
            }
        }
    }
}
