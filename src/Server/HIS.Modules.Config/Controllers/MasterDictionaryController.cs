using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Config.Controllers;

/// <summary>
/// 全院主数据字典中心：管理字典分类与标准字典项，供动态表单设计器引用（限制用户输入、标准计量单位直接点选）及服务端强校验
/// </summary>
[ApiController]
[Route("api/v4/config/dictionaries")]
public sealed class MasterDictionaryController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _session;
    private readonly IAuditTrailContract? _auditTrail;

    public MasterDictionaryController(
        IDbConnectionFactory connFactory,
        IUserSession? session = null,
        IAuditTrailContract? auditTrail = null)
    {
        _connFactory = connFactory;
        _session = session;
        _auditTrail = auditTrail;
    }

    private string ActorId => _session?.UserId?.Trim() ?? "SYSTEM";
    private string ActorName => _session?.DisplayName ?? _session?.UserName ?? ActorId;

    public sealed record SaveCategoryDto(string DictType, string DictName, string CategoryGroup, string? Description);
    public sealed record SaveDictItemDto(string DictType, string ItemCode, string ItemName, string? PinyinCode, int SortOrder, bool IsDefault);

    [HttpGet("categories")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetCategories([FromQuery] string? keyword = null)
    {
        using var conn = _connFactory.CreateConnection();
        var sql = @"
            SELECT c.id AS Id, c.dict_type AS DictType, c.dict_name AS DictName,
                   c.category_group AS CategoryGroup, c.is_system AS IsSystem,
                   c.description AS Description, c.is_active AS IsActive,
                   (SELECT COUNT(1) FROM cfg_clinical_dictionaries d WHERE d.dict_type = c.dict_type AND d.is_active = 1) AS ActiveItemCount
            FROM cfg_dict_categories c
            WHERE c.is_active = 1";
        if (!string.IsNullOrWhiteSpace(keyword))
            sql += " AND (c.dict_type LIKE @Kw OR c.dict_name LIKE @Kw OR c.description LIKE @Kw)";
        sql += " ORDER BY c.is_system DESC, c.dict_type ASC";

        var rows = (await conn.QueryAsync<dynamic>(sql, new { Kw = $"%{keyword?.Trim()}%" })).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("categories")]
    public async Task<ApiResponse<object>> SaveCategory([FromBody] SaveCategoryDto req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.DictType) || string.IsNullOrWhiteSpace(req.DictName))
            return ApiResponse<object>.Fail(400, "字典编码与字典名称不能为空");

        string typeCode = req.DictType.Trim().ToUpperInvariant();
        long now = SystemTime.UtcNowMs;
        using var conn = _connFactory.CreateConnection();
        var existingId = await conn.QueryFirstOrDefaultAsync<string?>(
            "SELECT id FROM cfg_dict_categories WHERE dict_type = @Type LIMIT 1", new { Type = typeCode });

        if (!string.IsNullOrWhiteSpace(existingId))
        {
            await conn.ExecuteAsync(@"
                UPDATE cfg_dict_categories
                SET dict_name = @Name, category_group = @Grp, description = @Desc,
                    updated_at_ms = @Now, updated_by = @Actor
                WHERE id = @Id",
                new { Name = req.DictName.Trim(), Grp = string.IsNullOrWhiteSpace(req.CategoryGroup) ? "CUSTOM" : req.CategoryGroup.Trim().ToUpperInvariant(), Desc = req.Description?.Trim(), Now = now, Actor = ActorId, Id = existingId });
            return ApiResponse<object>.Success(new { Id = existingId, DictType = typeCode }, $"已更新字典分类 [{typeCode}]");
        }

        string id = Guid.NewGuid().ToString("D");
        await conn.ExecuteAsync(@"
            INSERT INTO cfg_dict_categories
            (id, dict_type, dict_name, category_group, is_system, description, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES
            (@Id, @Type, @Name, @Grp, 0, @Desc, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, Type = typeCode, Name = req.DictName.Trim(), Grp = string.IsNullOrWhiteSpace(req.CategoryGroup) ? "CUSTOM" : req.CategoryGroup.Trim().ToUpperInvariant(), Desc = req.Description?.Trim(), Now = now, Actor = ActorId });

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("DICT_CATEGORY_CREATE", "cfg_dict_categories", id, $"新增主数据字典分类：{typeCode} ({req.DictName})", ActorId, ActorName, null, ct);

        return ApiResponse<object>.Success(new { Id = id, DictType = typeCode }, $"已创建主数据字典分类 [{typeCode}]");
    }

    [HttpGet("items/{dictType}")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetItems(string dictType, [FromQuery] bool includeInactive = false)
    {
        if (string.IsNullOrWhiteSpace(dictType))
            return ApiResponse<IReadOnlyList<object>>.Fail(400, "字典编码不能为空");

        using var conn = _connFactory.CreateConnection();
        string sql = @"
            SELECT id AS Id, dict_type AS DictType, item_code AS ItemCode, item_name AS ItemName,
                   pinyin_code AS PinyinCode, sort_order AS SortOrder, is_default AS IsDefault, is_active AS IsActive
            FROM cfg_clinical_dictionaries
            WHERE UPPER(dict_type) = UPPER(@Type)";
        if (!includeInactive) sql += " AND is_active = 1";
        sql += " ORDER BY sort_order ASC, item_code ASC";

        var rows = (await conn.QueryAsync<dynamic>(sql, new { Type = dictType.Trim() })).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    /// <summary>
    /// 快捷获取标准计量单位字典 (DOSAGE_UNIT)，供动态表单设计器「计量单位」直接引用
    /// </summary>
    [HttpGet("units")]
    public Task<ApiResponse<IReadOnlyList<object>>> GetStandardUnits() => GetItems("DOSAGE_UNIT", includeInactive: false);

    [HttpPost("items")]
    public async Task<ApiResponse<object>> SaveItem([FromBody] SaveDictItemDto req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.DictType) || string.IsNullOrWhiteSpace(req.ItemCode) || string.IsNullOrWhiteSpace(req.ItemName))
            return ApiResponse<object>.Fail(400, "字典分类、项编码与项名称均不能为空");

        string typeCode = req.DictType.Trim().ToUpperInvariant();
        string itemCode = req.ItemCode.Trim();
        long now = SystemTime.UtcNowMs;

        using var conn = _connFactory.CreateConnection();
        var existingId = await conn.QueryFirstOrDefaultAsync<string?>(
            "SELECT id FROM cfg_clinical_dictionaries WHERE UPPER(dict_type) = @Type AND UPPER(item_code) = UPPER(@Code) LIMIT 1",
            new { Type = typeCode, Code = itemCode });

        if (!string.IsNullOrWhiteSpace(existingId))
        {
            await conn.ExecuteAsync(@"
                UPDATE cfg_clinical_dictionaries
                SET item_name = @Name, pinyin_code = @Py, sort_order = @Sort, is_default = @IsDef, is_active = 1,
                    updated_at_ms = @Now, updated_by = @Actor
                WHERE id = @Id",
                new { Name = req.ItemName.Trim(), Py = req.PinyinCode?.Trim()?.ToUpperInvariant(), Sort = req.SortOrder, IsDef = req.IsDefault ? 1 : 0, Now = now, Actor = ActorId, Id = existingId });
            return ApiResponse<object>.Success(new { Id = existingId, ItemCode = itemCode }, $"已更新字典项 [{itemCode}]");
        }

        string id = Guid.NewGuid().ToString("D");
        await conn.ExecuteAsync(@"
            INSERT INTO cfg_clinical_dictionaries
            (id, dict_type, item_code, item_name, pinyin_code, sort_order, is_default, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES
            (@Id, @Type, @Code, @Name, @Py, @Sort, @IsDef, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, Type = typeCode, Code = itemCode, Name = req.ItemName.Trim(), Py = req.PinyinCode?.Trim()?.ToUpperInvariant(), Sort = req.SortOrder, IsDef = req.IsDefault ? 1 : 0, Now = now, Actor = ActorId });

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("DICT_ITEM_SAVE", "cfg_clinical_dictionaries", id, $"维护主数据字典 [{typeCode}] 项：{itemCode} - {req.ItemName}", ActorId, ActorName, null, ct);

        return ApiResponse<object>.Success(new { Id = id, ItemCode = itemCode }, $"已新增字典项 [{itemCode} - {req.ItemName}]");
    }

    [HttpPut("items/{id}/toggle")]
    public async Task<ApiResponse<object>> ToggleItem(string id)
    {
        using var conn = _connFactory.CreateConnection();
        int? cur = await conn.QueryFirstOrDefaultAsync<int?>("SELECT is_active FROM cfg_clinical_dictionaries WHERE id = @Id", new { Id = id });
        if (!cur.HasValue) return ApiResponse<object>.Fail(404, "未找到该字典项");
        int next = cur.Value == 1 ? 0 : 1;
        await conn.ExecuteAsync(
            "UPDATE cfg_clinical_dictionaries SET is_active = @Act, updated_at_ms = @Now, updated_by = @Actor WHERE id = @Id",
            new { Act = next, Now = SystemTime.UtcNowMs, Actor = ActorId, Id = id });
        return ApiResponse<object>.Success(new { Id = id, IsActive = next }, next == 1 ? "已启用该字典项" : "已停用该字典项（历史引用数据不受影响）");
    }
}
