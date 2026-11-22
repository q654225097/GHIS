using System;
using System.Data.Common;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;

namespace HIS.BuildingBlocks.Database;

public static partial class DbSchemaInitializer
{
    private static async Task InitializeMasterDictTablesAsync(DbConnection conn)
    {
        await conn.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS cfg_dict_categories (
    id VARCHAR(36) PRIMARY KEY,
    dict_type VARCHAR(64) NOT NULL UNIQUE,
    dict_name VARCHAR(128) NOT NULL,
    category_group VARCHAR(64) NOT NULL DEFAULT 'CLINICAL',
    is_system SMALLINT NOT NULL DEFAULT 1,
    description VARCHAR(500),
    is_active SMALLINT NOT NULL DEFAULT 1,
    ext_data_json VARCHAR(4000),
    schema_version INT NOT NULL DEFAULT 1,
    created_at_ms BIGINT NOT NULL,
    created_by VARCHAR(36),
    updated_at_ms BIGINT NOT NULL,
    updated_by VARCHAR(36),
    version_no INT NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_cfg_dict_type_active ON cfg_clinical_dictionaries (dict_type, is_active, sort_order ASC);
");

        long now = SystemTime.UtcNowMs;

        var defaultCategories = new (string Type, string Name, string Group, string Desc)[]
        {
            ("DOSAGE_UNIT", "计量与临床单位字典", "BASIC", "药品剂量、检验检验指标与动态表单通用标准单位（防止人工拼写错误）"),
            ("ADMIN_ROUTE", "给药途径字典", "CLINICAL", "处方与住院医嘱标准给药途径"),
            ("GENDER_CODE", "性别字典", "BASIC", "国标患者性别分类"),
            ("BLOOD_TYPE", "ABO血型字典", "CLINICAL", "临床用血与体检血型分类"),
            ("ALLERGY_SEVERITY", "过敏严重程度字典", "CLINICAL", "药物与食物过敏分级"),
            ("NURSING_LEVEL", "住院护理级别字典", "INPATIENT", "特级/一级/二级/三级护理标准"),
            ("EDUCATION_LEVEL", "文化程度字典", "BASIC", "病案首页与健康档案文化程度"),
            ("MARITAL_STATUS", "婚姻状况字典", "BASIC", "病案首页与患者档案婚姻状况"),
            ("INSURANCE_TYPE", "费别与参保类型字典", "FINANCE", "门诊与住院结算费别"),
            ("SPECIMEN_TYPE", "检验标本类型字典", "MEDTECH", "LIS 采样与体检标本类型")
        };

        foreach (var cat in defaultCategories)
        {
            int exists = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM cfg_dict_categories WHERE dict_type = @Type",
                new { cat.Type });
            if (exists == 0)
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO cfg_dict_categories
                    (id, dict_type, dict_name, category_group, is_system, description, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                    VALUES
                    (@Id, @Type, @Name, @Grp, 1, @Desc, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1)",
                    new { Id = Guid.NewGuid().ToString("D"), cat.Type, cat.Name, Grp = cat.Group, cat.Desc, Now = now });
            }
        }

        var defaultItems = new (string Type, string Code, string Name, string Py, int Sort, int IsDefault)[]
        {
            // 计量与临床单位字典 DOSAGE_UNIT
            ("DOSAGE_UNIT", "mg", "mg", "MG", 1, 1),
            ("DOSAGE_UNIT", "g", "g", "G", 2, 0),
            ("DOSAGE_UNIT", "ug", "μg", "UG", 3, 0),
            ("DOSAGE_UNIT", "ml", "ml", "ML", 4, 0),
            ("DOSAGE_UNIT", "L", "L", "L", 5, 0),
            ("DOSAGE_UNIT", "TAB", "片", "P", 6, 0),
            ("DOSAGE_UNIT", "CAP", "粒", "L", 7, 0),
            ("DOSAGE_UNIT", "AMP", "支", "Z", 8, 0),
            ("DOSAGE_UNIT", "BTL", "瓶", "P", 9, 0),
            ("DOSAGE_UNIT", "BOX", "盒", "H", 10, 0),
            ("DOSAGE_UNIT", "BAG", "袋", "D", 11, 0),
            ("DOSAGE_UNIT", "IU", "IU", "IU", 12, 0),
            ("DOSAGE_UNIT", "TIME", "次", "C", 13, 0),
            ("DOSAGE_UNIT", "DAY", "天", "T", 14, 0),
            ("DOSAGE_UNIT", "MMHG", "mmHg", "MMHG", 15, 0),
            ("DOSAGE_UNIT", "MMOL_L", "mmol/L", "MMOLL", 16, 0),
            ("DOSAGE_UNIT", "CELSIUS", "℃", "C", 17, 0),
            ("DOSAGE_UNIT", "BPM", "次/分", "CF", 18, 0),
            ("DOSAGE_UNIT", "KG", "kg", "KG", 19, 0),
            ("DOSAGE_UNIT", "CM", "cm", "CM", 20, 0),
            ("DOSAGE_UNIT", "PCT", "%", "BFB", 21, 0),

            // 给药途径 ADMIN_ROUTE
            ("ADMIN_ROUTE", "PO", "口服", "KF", 1, 1),
            ("ADMIN_ROUTE", "IVGTT", "静脉滴注", "JMDZ", 2, 0),
            ("ADMIN_ROUTE", "IV", "静脉推注", "JMTZ", 3, 0),
            ("ADMIN_ROUTE", "IM", "肌内注射", "JNZS", 4, 0),
            ("ADMIN_ROUTE", "IH", "皮下注射", "PXZS", 5, 0),
            ("ADMIN_ROUTE", "INH", "雾化吸入", "WHXR", 6, 0),
            ("ADMIN_ROUTE", "EXT", "外用", "WY", 7, 0),

            // 性别 GENDER_CODE
            ("GENDER_CODE", "M", "男", "N", 1, 1),
            ("GENDER_CODE", "F", "女", "N", 2, 0),
            ("GENDER_CODE", "U", "未知", "WZ", 3, 0),

            // 血型 BLOOD_TYPE
            ("BLOOD_TYPE", "A", "A型", "AX", 1, 0),
            ("BLOOD_TYPE", "B", "B型", "BX", 2, 0),
            ("BLOOD_TYPE", "AB", "AB型", "ABX", 3, 0),
            ("BLOOD_TYPE", "O", "O型", "OX", 4, 0),
            ("BLOOD_TYPE", "UNKNOWN", "未查", "WC", 5, 1),

            // 过敏严重程度 ALLERGY_SEVERITY
            ("ALLERGY_SEVERITY", "MILD", "轻度（皮疹/瘙痒）", "QD", 1, 1),
            ("ALLERGY_SEVERITY", "MODERATE", "中度（荨麻疹/呼吸不适）", "ZD", 2, 0),
            ("ALLERGY_SEVERITY", "SEVERE", "重度（过敏性休克/喉头水肿）", "ZD", 3, 0),

            // 护理级别 NURSING_LEVEL
            ("NURSING_LEVEL", "SPECIAL", "特级护理", "TJHL", 1, 0),
            ("NURSING_LEVEL", "LEVEL_1", "一级护理", "YJHL", 2, 0),
            ("NURSING_LEVEL", "LEVEL_2", "二级护理", "EJHL", 3, 1),
            ("NURSING_LEVEL", "LEVEL_3", "三级护理", "SJHL", 4, 0),

            // 文化程度 EDUCATION_LEVEL
            ("EDUCATION_LEVEL", "PRIMARY", "小学", "XX", 1, 0),
            ("EDUCATION_LEVEL", "MIDDLE", "初中", "CZ", 2, 0),
            ("EDUCATION_LEVEL", "HIGH", "高中/中专", "GZ", 3, 0),
            ("EDUCATION_LEVEL", "COLLEGE", "大专", "DZ", 4, 0),
            ("EDUCATION_LEVEL", "BACHELOR", "本科", "BK", 5, 1),
            ("EDUCATION_LEVEL", "MASTER_PLUS", "硕士及以上", "SS", 6, 0),

            // 婚姻状况 MARITAL_STATUS
            ("MARITAL_STATUS", "SINGLE", "未婚", "WH", 1, 0),
            ("MARITAL_STATUS", "MARRIED", "已婚", "YH", 2, 1),
            ("MARITAL_STATUS", "DIVORCED", "离异", "LY", 3, 0),
            ("MARITAL_STATUS", "WIDOWED", "丧偶", "SO", 4, 0),

            // 费别 INSURANCE_TYPE
            ("INSURANCE_TYPE", "SELF_PAY", "自费", "ZF", 1, 1),
            ("INSURANCE_TYPE", "EMPLOYEE_INS", "城镇职工医保", "CZZG", 2, 0),
            ("INSURANCE_TYPE", "RESIDENT_INS", "城乡居民医保", "CXJM", 3, 0),
            ("INSURANCE_TYPE", "COMMERCIAL", "商业保险", "SYBX", 4, 0),

            // 检验标本类型 SPECIMEN_TYPE
            ("SPECIMEN_TYPE", "VENOUS_BLOOD", "静脉血", "JMX", 1, 1),
            ("SPECIMEN_TYPE", "CAPILLARY_BLOOD", "末梢血", "MSX", 2, 0),
            ("SPECIMEN_TYPE", "URINE", "尿液", "NY", 3, 0),
            ("SPECIMEN_TYPE", "STOOL", "粪便", "FB", 4, 0),
            ("SPECIMEN_TYPE", "SPUTUM", "痰液", "TY", 5, 0),
            ("SPECIMEN_TYPE", "THROAT_SWAB", "咽拭子", "YSZ", 6, 0)
        };

        foreach (var it in defaultItems)
        {
            int exists = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM cfg_clinical_dictionaries WHERE dict_type = @Type AND item_code = @Code",
                new { it.Type, it.Code });
            if (exists == 0)
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO cfg_clinical_dictionaries
                    (id, dict_type, item_code, item_name, pinyin_code, parent_code, sort_order, is_default, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                    VALUES
                    (@Id, @Type, @Code, @Name, @Py, NULL, @Sort, @IsDef, 1, 1, @Now, 'SYSTEM', @Now, 'SYSTEM', 1)",
                    new { Id = Guid.NewGuid().ToString("D"), it.Type, it.Code, it.Name, it.Py, it.Sort, IsDef = it.IsDefault, Now = now });
            }
        }
    }
}
