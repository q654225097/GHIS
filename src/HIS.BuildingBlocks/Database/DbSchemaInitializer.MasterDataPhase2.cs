using System;
using System.Data.Common;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;

namespace HIS.BuildingBlocks.Database;

public static partial class DbSchemaInitializer
{
    private static async Task InitializeMasterDataPhase2Async(DbConnection conn)
    {
        await conn.ExecuteAsync(@"
-- 1. 医保病种目录 (门慢特M6位 / 按病种结算B7位 / 日间手术R开头)
CREATE TABLE IF NOT EXISTS mdm_yb_diseases (
    id VARCHAR(36) PRIMARY KEY,
    disease_code VARCHAR(32) NOT NULL UNIQUE,
    disease_name VARCHAR(200) NOT NULL,
    disease_category VARCHAR(32) NOT NULL DEFAULT 'OPD_CHRONIC',
    quota_limit_amount DECIMAL(18,2) NOT NULL DEFAULT 0,
    eff_date_int INT NOT NULL DEFAULT 20240101,
    exp_date_int INT NOT NULL DEFAULT 20991231,
    is_active SMALLINT NOT NULL DEFAULT 1,
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

-- 2. 体外诊断试剂 (IVD) 目录
CREATE TABLE IF NOT EXISTS mdm_ivd_reagents (
    id VARCHAR(36) PRIMARY KEY,
    reagent_code VARCHAR(64) NOT NULL UNIQUE,
    generic_name VARCHAR(200) NOT NULL,
    spec_model VARCHAR(128),
    methodology VARCHAR(64),
    applicable_device VARCHAR(128),
    reg_cert_no VARCHAR(80),
    yb_ivd_code VARCHAR(64),
    udi_di VARCHAR(64),
    is_active SMALLINT NOT NULL DEFAULT 1,
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

-- 3. 费别、统筹区与商保协议单位
CREATE TABLE IF NOT EXISTS mdm_payer_policies (
    id VARCHAR(36) PRIMARY KEY,
    fee_type_code VARCHAR(32) NOT NULL,
    fee_type_name VARCHAR(64) NOT NULL,
    pool_area_code VARCHAR(16) NOT NULL,
    contract_unit_name VARCHAR(200),
    settlement_rules_json VARCHAR(2000),
    is_active SMALLINT NOT NULL DEFAULT 1,
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1,
    UNIQUE (fee_type_code, pool_area_code)
);

-- 4. 库房、药房与货位
CREATE TABLE IF NOT EXISTS mdm_warehouses (
    id VARCHAR(36) PRIMARY KEY,
    warehouse_code VARCHAR(64) NOT NULL UNIQUE,
    warehouse_name VARCHAR(128) NOT NULL,
    warehouse_type VARCHAR(32) NOT NULL DEFAULT 'OPD_PHM',
    temp_humidity_req VARCHAR(64),
    bin_locations_json VARCHAR(2000),
    service_dept_scope VARCHAR(500),
    is_active SMALLINT NOT NULL DEFAULT 1,
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

-- 5. 消毒供应 (CSSD) 器械包
CREATE TABLE IF NOT EXISTS mdm_cssd_packs (
    id VARCHAR(36) PRIMARY KEY,
    pack_code VARCHAR(64) NOT NULL UNIQUE,
    pack_name VARCHAR(128) NOT NULL,
    instruments_json VARCHAR(2000),
    sterilization_method VARCHAR(32) NOT NULL DEFAULT 'STEAM',
    validity_days INT NOT NULL DEFAULT 180,
    charge_item_code VARCHAR(64),
    is_active SMALLINT NOT NULL DEFAULT 1,
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

-- 6. 非医用/总务后勤物资
CREATE TABLE IF NOT EXISTS mdm_logistics_items (
    id VARCHAR(36) PRIMARY KEY,
    item_code VARCHAR(64) NOT NULL UNIQUE,
    item_name VARCHAR(128) NOT NULL,
    category VARCHAR(32) NOT NULL DEFAULT 'OFFICE',
    spec VARCHAR(100),
    unit_name VARCHAR(32) NOT NULL DEFAULT '件',
    ref_price DECIMAL(18,4) NOT NULL DEFAULT 0,
    is_active SMALLINT NOT NULL DEFAULT 1,
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

-- 7. 用药规则与医嘱辅助字典
CREATE TABLE IF NOT EXISTS mdm_medication_rules (
    id VARCHAR(36) PRIMARY KEY,
    rule_code VARCHAR(64) NOT NULL UNIQUE,
    rule_name VARCHAR(128) NOT NULL,
    route_code VARCHAR(32) NOT NULL,
    frequency_code VARCHAR(32) NOT NULL,
    default_exec_times VARCHAR(128),
    tcm_decoction_method VARCHAR(64),
    skin_test_required SMALLINT NOT NULL DEFAULT 0,
    default_solvent_code VARCHAR(64),
    is_active SMALLINT NOT NULL DEFAULT 1,
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

-- 8. 微生物与药敏抗菌药物字典
CREATE TABLE IF NOT EXISTS mdm_microbiology_dict (
    id VARCHAR(36) PRIMARY KEY,
    micro_code VARCHAR(64) NOT NULL UNIQUE,
    micro_name VARCHAR(128) NOT NULL,
    micro_type VARCHAR(32) NOT NULL DEFAULT 'BACTERIA',
    specimen_scope VARCHAR(128),
    susceptibility_panel_json VARCHAR(1000),
    is_mdr_monitored SMALLINT NOT NULL DEFAULT 0,
    is_active SMALLINT NOT NULL DEFAULT 1,
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

-- 9. 影像检查部位与设备映射
CREATE TABLE IF NOT EXISTS mdm_imaging_mappings (
    id VARCHAR(36) PRIMARY KEY,
    mapping_code VARCHAR(64) NOT NULL UNIQUE,
    modality VARCHAR(16) NOT NULL,
    body_part_name VARCHAR(128) NOT NULL,
    scan_protocol VARCHAR(128),
    exam_room_code VARCHAR(64),
    dicom_ae_title VARCHAR(64),
    is_active SMALLINT NOT NULL DEFAULT 1,
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

-- 10. 手术间与手麻字典
CREATE TABLE IF NOT EXISTS mdm_or_rooms (
    id VARCHAR(36) PRIMARY KEY,
    room_code VARCHAR(32) NOT NULL UNIQUE,
    room_name VARCHAR(64) NOT NULL,
    clean_level VARCHAR(32) NOT NULL DEFAULT 'CLASS_1000',
    supported_anesthesia VARCHAR(256),
    default_asa_scope VARCHAR(64) DEFAULT 'I-V',
    default_incision_grade VARCHAR(16) DEFAULT 'I',
    is_active SMALLINT NOT NULL DEFAULT 1,
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

-- 11. 膳食与临床营养制剂字典
CREATE TABLE IF NOT EXISTS mdm_nutrition_items (
    id VARCHAR(36) PRIMARY KEY,
    item_code VARCHAR(64) NOT NULL UNIQUE,
    item_name VARCHAR(128) NOT NULL,
    nutrition_type VARCHAR(16) NOT NULL DEFAULT 'DIET',
    formula_desc VARCHAR(500),
    kcal_per_unit DECIMAL(10,2) NOT NULL DEFAULT 0,
    unit_name VARCHAR(32) NOT NULL DEFAULT '份',
    is_active SMALLINT NOT NULL DEFAULT 1,
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

-- 12. 物理空间位置树 (院区 -> 楼宇 -> 楼层 -> 房间)
CREATE TABLE IF NOT EXISTS mdm_locations (
    id VARCHAR(36) PRIMARY KEY,
    location_code VARCHAR(64) NOT NULL UNIQUE,
    location_name VARCHAR(128) NOT NULL,
    location_level VARCHAR(16) NOT NULL DEFAULT 'ROOM',
    parent_location_id VARCHAR(36),
    room_type VARCHAR(32),
    queue_screen_id VARCHAR(64),
    is_active SMALLINT NOT NULL DEFAULT 1,
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

-- 13. 医疗组 / 主诊医师组
CREATE TABLE IF NOT EXISTS mdm_medical_groups (
    id VARCHAR(36) PRIMARY KEY,
    group_code VARCHAR(64) NOT NULL UNIQUE,
    group_name VARCHAR(128) NOT NULL,
    dept_id VARCHAR(36) NOT NULL,
    leader_staff_id VARCHAR(36) NOT NULL,
    members_json VARCHAR(1000),
    bed_scope VARCHAR(256),
    eff_date_int INT NOT NULL DEFAULT 20240101,
    is_active SMALLINT NOT NULL DEFAULT 1,
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

-- 14. 号别与出诊排班模板
CREATE TABLE IF NOT EXISTS mdm_reg_levels (
    id VARCHAR(36) PRIMARY KEY,
    reg_level_code VARCHAR(32) NOT NULL UNIQUE,
    reg_level_name VARCHAR(64) NOT NULL,
    charge_item_code VARCHAR(64),
    reg_fee DECIMAL(18,2) NOT NULL DEFAULT 0,
    period_scope VARCHAR(64) NOT NULL DEFAULT 'MORNING,AFTERNOON',
    pool_ratio_json VARCHAR(500),
    is_active SMALLINT NOT NULL DEFAULT 1,
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

-- 15. 会计科目与成本中心核算字典
CREATE TABLE IF NOT EXISTS mdm_accounting_subjects (
    id VARCHAR(36) PRIMARY KEY,
    subject_code VARCHAR(64) NOT NULL UNIQUE,
    subject_name VARCHAR(128) NOT NULL,
    income_expense_type VARCHAR(24) NOT NULL DEFAULT 'REVENUE',
    cost_center_type VARCHAR(24) NOT NULL DEFAULT 'DIRECT_MED',
    allocation_weight DECIMAL(8,4) NOT NULL DEFAULT 1.0000,
    is_active SMALLINT NOT NULL DEFAULT 1,
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

-- 16. 业务系统与终端设备注册表
CREATE TABLE IF NOT EXISTS mdm_system_terminals (
    id VARCHAR(36) PRIMARY KEY,
    sys_code VARCHAR(64) NOT NULL UNIQUE,
    sys_name VARCHAR(128) NOT NULL,
    entry_type VARCHAR(24) NOT NULL DEFAULT 'BIZ_SYS',
    endpoint_or_ip VARCHAR(256),
    auth_scope VARCHAR(256),
    location_code VARCHAR(64),
    is_active SMALLINT NOT NULL DEFAULT 1,
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

-- 17. 医护电子签名与 CA 证书
CREATE TABLE IF NOT EXISTS mdm_ca_certificates (
    id VARCHAR(36) PRIMARY KEY,
    cert_serial VARCHAR(128) NOT NULL UNIQUE,
    staff_id VARCHAR(36) NOT NULL,
    ca_org_name VARCHAR(128) NOT NULL DEFAULT '北京数字认证(BJCA)',
    seal_image_uri VARCHAR(256),
    issued_at_ms BIGINT NOT NULL,
    expires_at_ms BIGINT NOT NULL,
    revoke_status VARCHAR(24) NOT NULL DEFAULT 'VALID',
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);

-- 18. 医保扩展人员与定点药店 (Y13位 / P12位)
CREATE TABLE IF NOT EXISTS mdm_yb_ext_entities (
    id VARCHAR(36) PRIMARY KEY,
    yb_code VARCHAR(32) NOT NULL UNIQUE,
    entity_type VARCHAR(32) NOT NULL DEFAULT 'PHARMACIST',
    entity_name VARCHAR(128) NOT NULL,
    org_name VARCHAR(200),
    is_active SMALLINT NOT NULL DEFAULT 1,
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
");

        // 增量扩列既有表
        await EnsureColumnAsync(conn, "drg_benchmark_rules", "mdc_code", "VARCHAR(16)");
        await EnsureColumnAsync(conn, "drg_benchmark_rules", "adrg_code", "VARCHAR(16)");
        await EnsureColumnAsync(conn, "drg_benchmark_rules", "diag_aux_rule", "VARCHAR(500)");
        await EnsureColumnAsync(conn, "drg_benchmark_rules", "std_ver", "VARCHAR(32) NOT NULL DEFAULT 'CHS-DRG-3.0'");

        await EnsureColumnAsync(conn, "eqp_equipments", "serial_no", "VARCHAR(128)");
        await EnsureColumnAsync(conn, "eqp_equipments", "depreciation_years", "INT NOT NULL DEFAULT 5");
        await EnsureColumnAsync(conn, "eqp_equipments", "boot_hours_metric", "DECIMAL(10,2) NOT NULL DEFAULT 0");
        await EnsureColumnAsync(conn, "eqp_equipments", "yb_imaging_device_code", "VARCHAR(64)");

        await EnsureColumnAsync(conn, "bld_blood_dictionary", "crossmatch_rule", "VARCHAR(256)");
        await EnsureColumnAsync(conn, "cfg_specimen_dictionary", "barcode_merge_rule", "VARCHAR(128)");
        await EnsureColumnAsync(conn, "cfg_specimen_dictionary", "instrument_code", "VARCHAR(64)");
        await EnsureColumnAsync(conn, "fin_cost_centers", "cost_center_type", "VARCHAR(32) NOT NULL DEFAULT 'DIRECT_MED'");
        await EnsureColumnAsync(conn, "fin_cost_centers", "allocation_weight", "DECIMAL(8,4) NOT NULL DEFAULT 1.0000");

        long now = SystemTime.UtcNowMs;

        // 种子数据
        await conn.ExecuteAsync(@"
INSERT OR IGNORE INTO mdm_yb_diseases (id, disease_code, disease_name, disease_category, quota_limit_amount, eff_date_int, exp_date_int, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('YBD-M01', 'M01001', '高血压病(门诊慢特病)', 'OPD_CHRONIC', 3600.00, 20240101, 20991231, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('YBD-M02', 'M02001', '糖尿病(门诊慢特病)', 'OPD_CHRONIC', 4800.00, 20240101, 20991231, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('YBD-B01', 'B001001', '急性阑尾炎按病种结算', 'DISEASE_SETTLE', 8500.00, 20240101, 20991231, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('YBD-R01', 'R00101', '白内障超声乳化日间手术', 'DAY_SURGERY', 5200.00, 20240101, 20991231, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

INSERT OR IGNORE INTO mdm_ivd_reagents (id, reagent_code, generic_name, spec_model, methodology, applicable_device, reg_cert_no, yb_ivd_code, udi_di, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('IVD-01', 'IVD-PCT-01', '降钙素原(PCT)检测试剂盒', '100人份/盒', '化学发光免疫分析法', '全自动化学发光免疫分析仪', '国械注准20223400123', 'IVD250401001', '06901234000111', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('IVD-02', 'IVD-CRP-01', '超敏C反应蛋白(hs-CRP)测定试剂', '200测试/盒', '免疫比浊法', '全自动生化分析仪', '国械注准20213400456', 'IVD250102002', '06901234000222', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

INSERT OR IGNORE INTO mdm_payer_policies (id, fee_type_code, fee_type_name, pool_area_code, contract_unit_name, settlement_rules_json, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('PP-EMP-BJ', 'EMPLOYEE_INS', '城镇职工医保', '110100', '北京市医保中心', '{""deductible"":1300,""reimburseRate"":0.85}', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('PP-RES-BJ', 'RESIDENT_INS', '城乡居民医保', '110100', '北京市医保中心', '{""deductible"":1000,""reimburseRate"":0.75}', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('PP-COM-01', 'COMMERCIAL', '高端商业健康险', '110100', '泰康/平安商保直付协议', '{""directBilling"":true,""reimburseRate"":0.90}', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

INSERT OR IGNORE INTO mdm_warehouses (id, warehouse_code, warehouse_name, warehouse_type, temp_humidity_req, bin_locations_json, service_dept_scope, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('WH-MAIN-01', 'DEPT-WH-01', '中心药库', 'CENTRAL_WH', '常温10-30℃ / 阴凉≤20℃ / 冷藏2-8℃', '[""A-01-01"",""A-01-02"",""COLD-01""]', '全院药房补货', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('WH-PHM-01', 'DEPT-PHM-01', '门诊中心药房', 'OPD_PHM', '常温10-25℃ / RH 35%-75%', '[""W01-A"",""W02-B""]', '门诊各诊室', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('WH-PIVAS-01', 'DEPT-PIVAS-01', '静脉用药调配中心(PIVAS)', 'PIVAS', '万级洁净 / 局部百级', '[""PIVAS-01"",""PIVAS-02""]', '全院住院病区', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

INSERT OR IGNORE INTO mdm_cssd_packs (id, pack_code, pack_name, instruments_json, sterilization_method, validity_days, charge_item_code, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('CSSD-01', 'PK-APP-01', '腹腔镜阑尾基础器械包', '[""腔镜抓钳x2"",""电凝钩x1"",""持针器x1""]', 'PLASMA', 180, 'SUR-APP', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('CSSD-02', 'PK-DRESS-01', '无菌换药包', '[""有齿镊x1"",""无齿镊x1"",""换药碗x1""]', 'STEAM', 14, 'TRT-DRESS', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

INSERT OR IGNORE INTO mdm_logistics_items (id, item_code, item_name, category, spec, unit_name, ref_price, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('LOG-01', 'OFF-A4-01', 'A4 打印纸(80g)', 'OFFICE', '500张/包', '包', 22.00, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('LOG-02', 'LIN-BED-01', '病房纯棉三件套被服', 'LINEN', '200x150cm', '套', 135.00, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

INSERT OR IGNORE INTO mdm_medication_rules (id, rule_code, rule_name, route_code, frequency_code, default_exec_times, tcm_decoction_method, skin_test_required, default_solvent_code, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('MR-IVGTT-BID', 'RULE-IVGTT-BID', '静脉滴注每日两次规则', 'IVGTT', 'BID', '08:00,16:00', NULL, 0, '0.9%NS-100ML', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('MR-PO-TID', 'RULE-PO-TID', '口服每日三次规则', 'PO', 'TID', '08:00,12:00,18:00', NULL, 0, NULL, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('MR-TCM-DECOCT', 'RULE-TCM-FIRST', '中药先煎后下煎服规则', 'PO', 'BID', '09:00,19:00', '先煎30分钟后下芳香药', 0, NULL, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

INSERT OR IGNORE INTO mdm_microbiology_dict (id, micro_code, micro_name, micro_type, specimen_scope, susceptibility_panel_json, is_mdr_monitored, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('MIC-MRSA', 'BACT-MRSA', '耐甲氧西林金黄色葡萄球菌(MRSA)', 'BACTERIA', '痰液/血培养/分泌物', '[""万古霉素"",""利奈唑胺"",""替考拉宁""]', 1, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('MIC-CRE', 'BACT-CRE', '耐碳青霉烯类肠杆菌(CRE)', 'BACTERIA', '血培养/尿液/痰液', '[""多粘菌素B"",""替加环素"",""头孢他啶阿维巴坦""]', 1, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

INSERT OR IGNORE INTO mdm_imaging_mappings (id, mapping_code, modality, body_part_name, scan_protocol, exam_room_code, dicom_ae_title, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('IMG-CT-CHEST', 'CT-CHEST-PLAIN', 'CT', '胸部平扫', '螺旋薄层 1.25mm', 'RAD-CT-01', 'GHIS_CT_01', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('IMG-MR-BRAIN', 'MR-BRAIN-PLAIN', 'MR', '颅脑平扫', 'T1WI+T2WI+DWI+FLAIR', 'RAD-MR-01', 'GHIS_MR_01', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

INSERT OR IGNORE INTO mdm_or_rooms (id, room_code, room_name, clean_level, supported_anesthesia, default_asa_scope, default_incision_grade, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('OR-01', 'OR-01', '1号百级层流手术间', 'CLASS_100', '全麻/硬膜外/神经阻滞', 'I-V', 'I', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('OR-02', 'OR-02', '2号千级腔镜手术间', 'CLASS_1000', '气管插管全麻', 'I-IV', 'II', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

INSERT OR IGNORE INTO mdm_nutrition_items (id, item_code, item_name, nutrition_type, formula_desc, kcal_per_unit, unit_name, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('NUT-DIET-DM', 'DIET-DM-01', '糖尿病低GI治疗餐', 'DIET', '总热量1800kcal/日，碳水占50%', 600.00, '餐', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('NUT-EN-TP', 'EN-TP-500', '整蛋白型肠内营养乳剂(TP)', 'EN', '500ml含750kcal，高氮低渣', 750.00, '瓶', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

INSERT OR IGNORE INTO mdm_locations (id, location_code, location_name, location_level, parent_location_id, room_type, queue_screen_id, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('LOC-CAMPUS-01', 'LOC-C01', '本部中心院区', 'CAMPUS', NULL, NULL, NULL, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('LOC-BLDG-01', 'LOC-B01', '门急诊医技综合楼', 'BUILDING', 'LOC-CAMPUS-01', NULL, NULL, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('LOC-FLR-02', 'LOC-F02', '门诊综合楼二层', 'FLOOR', 'LOC-BLDG-01', NULL, NULL, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('LOC-RM-201', 'LOC-R201', '内科诊室201', 'ROOM', 'LOC-FLR-02', 'CLINIC_ROOM', 'SCR-OPD-2F', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

INSERT OR IGNORE INTO mdm_medical_groups (id, group_code, group_name, dept_id, leader_staff_id, members_json, bed_scope, eff_date_int, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('MG-RESP-01', 'GRP-RESP-A', '呼吸内科一诊疗组', 'DEPT-RESP-01', 'USER-DOC-ATTENDING', '[""USER-DOC-ATTENDING"",""USER-DOC-RESIDENT""]', '01-12床', 20240101, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

INSERT OR IGNORE INTO mdm_reg_levels (id, reg_level_code, reg_level_name, charge_item_code, reg_fee, period_scope, pool_ratio_json, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('RL-NORM', 'NORMAL', '普通门诊号', 'REG-NORM', 15.00, 'MORNING,AFTERNOON', '{""onsite"":0.3,""online"":0.5,""followup"":0.1,""referral"":0.1}', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('RL-EXP', 'EXPERT', '主任医师专家号', 'REG-EXPERT', 50.00, 'MORNING', '{""onsite"":0.2,""online"":0.6,""followup"":0.1,""referral"":0.1}', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('RL-MDT', 'MDT', '多学科联合诊疗(MDT)', 'REG-MDT', 300.00, 'AFTERNOON', '{""onsite"":0.1,""online"":0.7,""referral"":0.2}', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

INSERT OR IGNORE INTO mdm_accounting_subjects (id, subject_code, subject_name, income_expense_type, cost_center_type, allocation_weight, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('SUB-400101', '40010101', '事业收入-医疗收入-门诊检查收入', 'REVENUE', 'MEDTECH', 1.0000, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('SUB-500101', '50010101', '业务活动费用-卫生材料及药品费', 'EXPENSE', 'DIRECT_MED', 1.0000, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

INSERT OR IGNORE INTO mdm_system_terminals (id, sys_code, sys_name, entry_type, endpoint_or_ip, auth_scope, location_code, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('SYS-PACS', 'SYS-PACS-01', '院内PACS影像归档系统', 'BIZ_SYS', 'http://10.10.20.11:8042/dicom-web', 'DICOM,WADO-RS', 'LOC-C01', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('TRM-PDA-01', 'PDA-W01-01', '一病区移动护理PDA-01', 'PDA', '10.10.30.101', 'NURSING_EXEC,VITAL_SIGN', 'LOC-F02', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

INSERT OR IGNORE INTO mdm_ca_certificates (id, cert_serial, staff_id, ca_org_name, seal_image_uri, issued_at_ms, expires_at_ms, revoke_status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('CA-001', 'BJCA-2026-88001122', 'USER-DOC-ATTENDING', '北京数字认证(BJCA)', 'seal://attending_01.png', @Now, @Now + 31536000000, 'VALID', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

INSERT OR IGNORE INTO mdm_yb_ext_entities (id, yb_code, entity_type, entity_name, org_name, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('YBE-PHM-01', 'Y110101000123', 'PHARMACIST', '张执业药师', 'GHIS 示范中心医院中心药房', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('YBE-RET-01', 'P11010100088', 'RETAIL_PHARMACY', '国药控股北京双通道定点药店', '北京国大药房连锁', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);
", new { Now = now });
    }
}
