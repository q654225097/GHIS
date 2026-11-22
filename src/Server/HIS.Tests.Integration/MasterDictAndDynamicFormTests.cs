using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Modules.Config.Controllers;
using HIS.Modules.Config.Models;
using Xunit;

namespace HIS.Tests.Integration;

public sealed class MasterDictAndDynamicFormTests
{
    [Fact]
    public async Task MasterDict_Initializes_Standard_Units_And_Enforces_DynamicForm_Validation()
    {
        var db = new SqliteConnectionFactory("Data Source=:memory:;Mode=Memory;Cache=Shared");
        await DbSchemaInitializer.InitializeAsync(db);

        // 1. 验证标准计量单位字典 DOSAGE_UNIT 已自动初始化
        var dictCtrl = new MasterDictionaryController(db);
        var unitsRes = await dictCtrl.GetStandardUnits();
        Assert.Equal(0, unitsRes.Code);
        Assert.True(unitsRes.Data!.Count >= 15);

        // 2. 创建一个绑定了 BLOOD_TYPE 字典的动态表单字段（关联到真实患者 PAT_PATIENT）
        long now = SystemTime.UtcNowMs;
        using (var conn = db.CreateConnection())
        {
            await conn.ExecuteAsync(@"
                INSERT INTO pat_patients
                (id, medical_record_no, full_name, gender_code, birth_date_int, id_card_type, id_card_hash, is_temp_emergency, is_active, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES ('PAT-DICT-1', 'MR9001', '测试患者', '男', 19900101, 'ID', 'HASH1', 0, 1, 1, @Now, @Now, 1)",
                new { Now = now });

            await conn.ExecuteAsync(@"
                INSERT INTO cfg_form_schemas
                (id, form_code, form_name, entity_type, grid_columns, is_published, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES ('FS-1', 'PAT_EXT_BLOOD', '患者血型扩展表单', 'PAT_PATIENT', 2, 1, 1, @Now, @Now, 1)",
                new { Now = now });

            await conn.ExecuteAsync(@"
                INSERT INTO cfg_field_definitions
                (id, form_schema_id, field_code, field_label, data_type, ursa_control_type, dict_code, unit_text, is_indexed, sort_order, is_deleted, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES ('FD-1', 'FS-1', 'blood_type', 'ABO血型', 'STRING', 'ComboBox', 'BLOOD_TYPE', NULL, 0, 1, 0, 1, @Now, @Now, 1)",
                new { Now = now });
        }

        var boCtrl = new DynamicFormBoController(db, new DapperRepository<CfgEntityFieldValue>(db, "cfg_entity_field_values"));

        // 3. 提交非法手工输入值（不在 BLOOD_TYPE 字典中）-> 必须被 400 严格拦截
        var badRes = await boCtrl.SaveBoFormValues("PAT_PATIENT", "PAT-DICT-1", new DynamicFormBoController.SaveBoValuesRequest(
            FormCode: "PAT_EXT_BLOOD",
            Values: new List<DynamicFormBoController.SaveBoFieldValueItem>
            {
                new("blood_type", "胡乱输入的血型XYZ", null, null, null)
            }), default);
        Assert.Equal(400, badRes.Code);
        Assert.Contains("不在有效字典范围内", badRes.Message);

        // 4. 提交合法字典项（如 "A型"）-> 必须成功落库并同步主表 ext_data_json
        var okRes = await boCtrl.SaveBoFormValues("PAT_PATIENT", "PAT-DICT-1", new DynamicFormBoController.SaveBoValuesRequest(
            FormCode: "PAT_EXT_BLOOD",
            Values: new List<DynamicFormBoController.SaveBoFieldValueItem>
            {
                new("blood_type", "A型", null, null, null)
            }), default);
        Assert.Equal(0, okRes.Code);
    }
}
