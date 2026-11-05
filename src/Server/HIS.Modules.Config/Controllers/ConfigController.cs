using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Modules.Config.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Config.Controllers;

[ApiController]
[Route("api/v4/config")]
public class ConfigController : ControllerBase
{
    private readonly IRepository<CfgFormSchema> _schemaRepo;
    private readonly IRepository<CfgFieldDefinition> _fieldRepo;
    private readonly IRepository<CfgEntityFieldValue> _valRepo;

    public ConfigController(
        IRepository<CfgFormSchema> schemaRepo,
        IRepository<CfgFieldDefinition> fieldRepo,
        IRepository<CfgEntityFieldValue> valRepo)
    {
        _schemaRepo = schemaRepo;
        _fieldRepo = fieldRepo;
        _valRepo = valRepo;
    }

    public sealed record FormSchemaDetailDto(
        CfgFormSchema Schema,
        IReadOnlyList<CfgFieldDefinition> Fields
    );

    [HttpGet("schemas/{formCode}")]
    public async Task<ApiResponse<FormSchemaDetailDto>> GetSchema(string formCode)
    {
        var schemas = await _schemaRepo.QueryAsync(
            "SELECT * FROM cfg_form_schemas WHERE form_code = @FormCode AND is_published = 1",
            new { FormCode = formCode });
        var schema = schemas.FirstOrDefault();
        if (schema == null)
            return ApiResponse<FormSchemaDetailDto>.Fail(404, $"未找到表单配置：{formCode}");

        var fields = await _fieldRepo.QueryAsync(
            "SELECT * FROM cfg_field_definitions WHERE form_schema_id = @SchemaId ORDER BY sort_order ASC",
            new { SchemaId = schema.Id });

        return ApiResponse<FormSchemaDetailDto>.Success(new FormSchemaDetailDto(schema, fields));
    }

    [HttpPost("values")]
    public async Task<ApiResponse<int>> SaveEntityValues([FromBody] List<CfgEntityFieldValue> values)
    {
        int count = 0;
        foreach (var val in values)
        {
            var existing = await _valRepo.QueryAsync(
                "SELECT * FROM cfg_entity_field_values WHERE entity_type = @Type AND entity_id = @Id AND field_code = @Code",
                new { Type = val.EntityType, Id = val.EntityId, Code = val.FieldCode });

            if (existing.Count > 0)
            {
                var cur = existing[0];
                cur.ValString = val.ValString;
                cur.ValInt = val.ValInt;
                cur.ValDecimal = val.ValDecimal;
                cur.ValTimeMs = val.ValTimeMs;
                await _valRepo.UpdateAsync(cur);
            }
            else
            {
                await _valRepo.InsertAsync(val);
            }
            count++;
        }
        return ApiResponse<int>.Success(count, "动态扩展字段保存成功");
    }

    [HttpGet("values/{entityType}/{entityId}")]
    public async Task<ApiResponse<IReadOnlyList<CfgEntityFieldValue>>> GetEntityValues(string entityType, string entityId)
    {
        var list = await _valRepo.QueryAsync(
            "SELECT * FROM cfg_entity_field_values WHERE entity_type = @Type AND entity_id = @Id",
            new { Type = entityType, Id = entityId });
        return ApiResponse<IReadOnlyList<CfgEntityFieldValue>>.Success(list);
    }
}

