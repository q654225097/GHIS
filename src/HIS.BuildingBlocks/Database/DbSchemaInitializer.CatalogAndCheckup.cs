using System.Data.Common;
using Dapper;

namespace HIS.BuildingBlocks.Database;

public static partial class DbSchemaInitializer
{
    private static async Task InitializeCatalogAndCheckupTablesAsync(DbConnection conn)
    {
        await conn.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS cfg_order_bundles (
    id VARCHAR(36) PRIMARY KEY,
    bundle_code VARCHAR(64) NOT NULL UNIQUE,
    bundle_name VARCHAR(128) NOT NULL,
    pinyin_code VARCHAR(64),
    bundle_category VARCHAR(32) NOT NULL DEFAULT 'LAB_PANEL',
    applicable_scene VARCHAR(32) NOT NULL DEFAULT 'ALL',
    gender_limit VARCHAR(16) NOT NULL DEFAULT 'ALL',
    dept_name VARCHAR(64),
    specimen_type VARCHAR(64),
    package_price DECIMAL(18,4) NOT NULL DEFAULT 0,
    sum_item_price DECIMAL(18,4) NOT NULL DEFAULT 0,
    is_allow_split SMALLINT NOT NULL DEFAULT 0,
    is_active SMALLINT NOT NULL DEFAULT 1,
    remark VARCHAR(500),
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_cfg_order_bundles_cat ON cfg_order_bundles (bundle_category, is_active);

CREATE TABLE IF NOT EXISTS cfg_order_bundle_items (
    id VARCHAR(36) PRIMARY KEY,
    bundle_id VARCHAR(36) NOT NULL,
    item_type VARCHAR(32) NOT NULL DEFAULT 'PRICE_ITEM',
    target_item_id VARCHAR(36),
    item_code VARCHAR(64) NOT NULL,
    item_name VARCHAR(200) NOT NULL,
    spec VARCHAR(100),
    unit_name VARCHAR(32) NOT NULL DEFAULT '次',
    quantity DECIMAL(18,4) NOT NULL DEFAULT 1,
    unit_price DECIMAL(18,4) NOT NULL DEFAULT 0,
    subtotal_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
    exec_dept_name VARCHAR(64),
    sort_order INT NOT NULL DEFAULT 10,
    is_required SMALLINT NOT NULL DEFAULT 1,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_cfg_order_bundle_items_bundle ON cfg_order_bundle_items (bundle_id, sort_order);

CREATE TABLE IF NOT EXISTS chk_orders (
    id VARCHAR(36) PRIMARY KEY,
    checkup_no VARCHAR(40) NOT NULL UNIQUE,
    guide_barcode VARCHAR(64) NOT NULL,
    patient_id VARCHAR(36),
    full_name VARCHAR(128) NOT NULL,
    gender VARCHAR(16) NOT NULL DEFAULT '男',
    age INT NOT NULL DEFAULT 30,
    id_card_masked VARCHAR(32),
    phone_masked VARCHAR(32),
    checkup_type VARCHAR(16) NOT NULL DEFAULT 'PERSONAL',
    company_name VARCHAR(128),
    package_id VARCHAR(36) NOT NULL,
    package_name VARCHAR(128) NOT NULL,
    total_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
    payment_status VARCHAR(16) NOT NULL DEFAULT 'UNPAID',
    status VARCHAR(32) NOT NULL DEFAULT 'REGISTERED',
    total_item_count INT NOT NULL DEFAULT 0,
    done_item_count INT NOT NULL DEFAULT 0,
    abnormal_count INT NOT NULL DEFAULT 0,
    summary_doctor_id VARCHAR(36),
    summary_doctor_name VARCHAR(64),
    overall_conclusion TEXT,
    health_advice TEXT,
    registered_at_ms BIGINT NOT NULL,
    completed_at_ms BIGINT,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_chk_orders_status ON chk_orders (status, registered_at_ms DESC);

CREATE TABLE IF NOT EXISTS chk_order_items (
    id VARCHAR(36) PRIMARY KEY,
    order_id VARCHAR(36) NOT NULL,
    bundle_item_id VARCHAR(36),
    dept_name VARCHAR(64) NOT NULL DEFAULT '检验科',
    item_code VARCHAR(64) NOT NULL,
    item_name VARCHAR(200) NOT NULL,
    unit_price DECIMAL(18,4) NOT NULL DEFAULT 0,
    result_value VARCHAR(500),
    reference_range VARCHAR(128),
    is_abnormal SMALLINT NOT NULL DEFAULT 0,
    abnormal_flag VARCHAR(16) NOT NULL DEFAULT 'NORMAL',
    exam_doctor_name VARCHAR(64),
    status VARCHAR(16) NOT NULL DEFAULT 'PENDING',
    checked_at_ms BIGINT,
    sort_order INT NOT NULL DEFAULT 10,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_chk_order_items_order ON chk_order_items (order_id, sort_order);
");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 1. 预置常用底层医疗收费明细项目（供组合套餐拆分引用）
        await conn.ExecuteAsync(@"
INSERT OR IGNORE INTO cfg_price_items (id, item_code, national_ins_code, item_name, pinyin_code, fee_category, spec, unit_name, unit_price, reimburse_type, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('PI-LAB-BUN',  'LAB-BUN',  '250307001', '血清尿素测定 (BUN)', 'XQNSC', 'LAB', '生化法', '项', 8.00, 'CLASS_A', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('PI-LAB-CR',   'LAB-CR',   '250307002', '血清肌酐测定 (Cr)', 'XQJGC', 'LAB', '肌氨酸氧化酶法', '项', 9.00, 'CLASS_A', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('PI-LAB-UA',   'LAB-UA',   '250307003', '血清尿酸测定 (UA)', 'XQNSC', 'LAB', '尿酸酶法', '项', 8.00, 'CLASS_A', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('PI-LAB-CYSC', 'LAB-CYSC', '250307015', '血清胱抑素C测定 (Cys-C)', 'XQGYSC', 'LAB', '免疫比浊法', '项', 35.00, 'CLASS_B', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('PI-LAB-ALT',  'LAB-ALT',  '250305007', '丙氨酸氨基转移酶测定 (ALT)', 'BAS', 'LAB', '速率法', '项', 7.00, 'CLASS_A', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('PI-LAB-AST',  'LAB-AST',  '250305008', '天门冬氨酸氨基转移酶测定 (AST)', 'TMD', 'LAB', '速率法', '项', 7.00, 'CLASS_A', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('PI-LAB-TBIL', 'LAB-TBIL', '250305001', '血清总胆红素测定 (TBIL)', 'ZDH', 'LAB', '化学氧化法', '项', 6.00, 'CLASS_A', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('PI-LAB-CBC',  'LAB-CBC',  '250101015', '血细胞分析 (五分类血常规)', 'XCG', 'LAB', '全血', '次', 22.00, 'CLASS_A', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('PI-EXM-ECG',  'EXM-ECG',  '310701001', '常规十二导联心电图', 'XDT', 'EXAM', '12导联', '次', 25.00, 'CLASS_A', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('PI-EXM-DR',   'EXM-DR',   '210101001', '数字化摄影 (胸部正位DR)', 'XBDR', 'EXAM', '正位', '次', 85.00, 'CLASS_A', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('PI-EXM-US',   'EXM-US',   '220301001', '腹部彩色多普勒超声 (肝胆胰脾)', 'FBCS', 'EXAM', '彩超', '次', 120.00, 'CLASS_A', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('PI-PHY-GEN',  'PHY-GEN',  '110100001', '一般体格检查 (身高/体重/血压/内外科)', 'TGJC', 'EXAM', '常规', '次', 20.00, 'SELF_PAY', 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);
", new { Now = now });

        // 2. 预置现代医疗组合医嘱套餐与体检套餐（医生前台开「肾功能全套」，后台自动拆解为 4 个具体收费明细）
        await conn.ExecuteAsync(@"
INSERT OR IGNORE INTO cfg_order_bundles (id, bundle_code, bundle_name, pinyin_code, bundle_category, applicable_scene, gender_limit, dept_name, specimen_type, package_price, sum_item_price, is_allow_split, is_active, remark, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
('BND-RENAL',   'PANEL-RENAL',   '肾功能全套',         'SGNQT',  'LAB_PANEL',      'ALL',     'ALL', '检验科',   '静脉血(黄头促凝管)', 60.00,  60.00,  0, 1, '包含尿素氮、肌酐、尿酸、胱抑素C四项底层收费明细', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('BND-LIVER',   'PANEL-LIVER',   '肝功能基础三项',     'GGNSX',  'LAB_PANEL',      'ALL',     'ALL', '检验科',   '静脉血(黄头促凝管)', 20.00,  20.00,  0, 1, '包含ALT、AST、总胆红素三项底层收费明细', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('BND-CHK-A',   'CHK-ENTRY-A',   '入职常规健康体检套餐A', 'RZTJA',  'HEALTH_CHECKUP', 'CHECKUP', 'ALL', '健康管理中心', '血液+心电+影像',  188.00, 212.00, 0, 1, '适用于企事业单位新员工入职及常规年度基础体检', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('BND-CHK-B',   'CHK-COMPR-B',   '全面深度体检套餐B',   'QMTJB',  'HEALTH_CHECKUP', 'CHECKUP', 'ALL', '健康管理中心', '血液+超声+心电+DR', 298.00, 332.00, 0, 1, '包含一般体格检查、血常规、肝肾功能、心电图、胸部DR及腹部彩超', 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);

INSERT OR IGNORE INTO cfg_order_bundle_items (id, bundle_id, item_type, target_item_id, item_code, item_name, spec, unit_name, quantity, unit_price, subtotal_amount, exec_dept_name, sort_order, is_required, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
-- 肾功能全套明细
('BNDI-R1', 'BND-RENAL', 'PRICE_ITEM', 'PI-LAB-BUN',  'LAB-BUN',  '血清尿素测定 (BUN)',      '生化法',       '项', 1,  8.00,  8.00, '检验科', 10, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('BNDI-R2', 'BND-RENAL', 'PRICE_ITEM', 'PI-LAB-CR',   'LAB-CR',   '血清肌酐测定 (Cr)',       '肌氨酸氧化酶法', '项', 1,  9.00,  9.00, '检验科', 20, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('BNDI-R3', 'BND-RENAL', 'PRICE_ITEM', 'PI-LAB-UA',   'LAB-UA',   '血清尿酸测定 (UA)',       '尿酸酶法',     '项', 1,  8.00,  8.00, '检验科', 30, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('BNDI-R4', 'BND-RENAL', 'PRICE_ITEM', 'PI-LAB-CYSC', 'LAB-CYSC', '血清胱抑素C测定 (Cys-C)', '免疫比浊法',   '项', 1, 35.00, 35.00, '检验科', 40, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
-- 肝功能基础三项明细
('BNDI-L1', 'BND-LIVER', 'PRICE_ITEM', 'PI-LAB-ALT',  'LAB-ALT',  '丙氨酸氨基转移酶测定 (ALT)', '速率法',   '项', 1,  7.00,  7.00, '检验科', 10, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('BNDI-L2', 'BND-LIVER', 'PRICE_ITEM', 'PI-LAB-AST',  'LAB-AST',  '天门冬氨酸氨基转移酶测定 (AST)', '速率法', '项', 1, 7.00, 7.00, '检验科', 20, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('BNDI-L3', 'BND-LIVER', 'PRICE_ITEM', 'PI-LAB-TBIL', 'LAB-TBIL', '血清总胆红素测定 (TBIL)', '化学氧化法', '项', 1,  6.00,  6.00, '检验科', 30, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
-- 入职常规体检套餐A明细
('BNDI-CA1', 'BND-CHK-A', 'PRICE_ITEM', 'PI-PHY-GEN',  'PHY-GEN',  '一般体格检查 (身高/体重/血压/内外科)', '常规', '次', 1, 20.00, 20.00, '内科体检室', 10, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('BNDI-CA2', 'BND-CHK-A', 'PRICE_ITEM', 'PI-LAB-CBC',  'LAB-CBC',  '血细胞分析 (五分类血常规)', '全血', '次', 1, 22.00, 22.00, '检验科', 20, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('BNDI-CA3', 'BND-CHK-A', 'PRICE_ITEM', 'PI-LAB-ALT',  'LAB-ALT',  '丙氨酸氨基转移酶测定 (ALT)', '速率法', '项', 1, 7.00, 7.00, '检验科', 30, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('BNDI-CA4', 'BND-CHK-A', 'PRICE_ITEM', 'PI-LAB-CR',   'LAB-CR',   '血清肌酐测定 (Cr)', '肌氨酸氧化酶法', '项', 1, 9.00, 9.00, '检验科', 40, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('BNDI-CA5', 'BND-CHK-A', 'PRICE_ITEM', 'PI-EXM-ECG',  'EXM-ECG',  '常规十二导联心电图', '12导联', '次', 1, 25.00, 25.00, '心电图室', 50, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('BNDI-CA6', 'BND-CHK-A', 'PRICE_ITEM', 'PI-EXM-DR',   'EXM-DR',   '数字化摄影 (胸部正位DR)', '正位', '次', 1, 85.00, 85.00, '放射科', 60, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
-- 全面深度体检套餐B明细
('BNDI-CB1', 'BND-CHK-B', 'PRICE_ITEM', 'PI-PHY-GEN',  'PHY-GEN',  '一般体格检查 (身高/体重/血压/内外科)', '常规', '次', 1, 20.00, 20.00, '内科体检室', 10, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('BNDI-CB2', 'BND-CHK-B', 'PRICE_ITEM', 'PI-LAB-CBC',  'LAB-CBC',  '血细胞分析 (五分类血常规)', '全血', '次', 1, 22.00, 22.00, '检验科', 20, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('BNDI-CB3', 'BND-CHK-B', 'PRICE_ITEM', 'PI-LAB-BUN',  'LAB-BUN',  '血清尿素测定 (BUN)', '生化法', '项', 1, 8.00, 8.00, '检验科', 30, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('BNDI-CB4', 'BND-CHK-B', 'PRICE_ITEM', 'PI-LAB-CR',   'LAB-CR',   '血清肌酐测定 (Cr)', '肌氨酸氧化酶法', '项', 1, 9.00, 9.00, '检验科', 40, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('BNDI-CB5', 'BND-CHK-B', 'PRICE_ITEM', 'PI-LAB-ALT',  'LAB-ALT',  '丙氨酸氨基转移酶测定 (ALT)', '速率法', '项', 1, 7.00, 7.00, '检验科', 50, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('BNDI-CB6', 'BND-CHK-B', 'PRICE_ITEM', 'PI-EXM-ECG',  'EXM-ECG',  '常规十二导联心电图', '12导联', '次', 1, 25.00, 25.00, '心电图室', 60, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('BNDI-CB7', 'BND-CHK-B', 'PRICE_ITEM', 'PI-EXM-DR',   'EXM-DR',   '数字化摄影 (胸部正位DR)', '正位', '次', 1, 85.00, 85.00, '放射科', 70, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1),
('BNDI-CB8', 'BND-CHK-B', 'PRICE_ITEM', 'PI-EXM-US',   'EXM-US',   '腹部彩色多普勒超声 (肝胆胰脾)', '彩超', '次', 1, 120.00, 120.00, '超声科', 80, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1);
", new { Now = now });
    }
}
