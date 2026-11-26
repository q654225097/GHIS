using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.Operations.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Operations.Controllers;

[ApiController]
[RequireHisRole("ADMIN", "CASHIER")]
[Route("api/v4/ops/reports")]
[RequireRole(RoleCode.Admin, "DIRECTOR", "LEADER", RoleCode.Cashier, RoleCode.DoctorOpd, RoleCode.DoctorIpd)]
public class CustomReportController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Regex ParameterRegex = new(@"@([A-Za-z0-9_]+)", RegexOptions.Compiled);
    private static readonly string[] ForbiddenTokens =
    [
        "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "CREATE", "TRUNCATE",
        "ATTACH", "DETACH", "PRAGMA", "EXEC", "EXECUTE", "REPLACE", "GRANT", "REVOKE", "VACUUM"
    ];

    private readonly IDbConnectionFactory _connFactory;
    private readonly IRepository<OpsReportDefinition> _reportRepo;
    private readonly IRepository<OpsReportExecution> _executionRepo;
    private readonly IUserSession? _userSession;

    public CustomReportController(
        IDbConnectionFactory connFactory,
        IRepository<OpsReportDefinition> reportRepo,
        IRepository<OpsReportExecution> executionRepo,
        IUserSession? userSession = null)
    {
        _connFactory = connFactory;
        _reportRepo = reportRepo;
        _executionRepo = executionRepo;
        _userSession = userSession;
    }

    #region DTOs

    public sealed record ReportDefinitionDto(
        string? Id,
        string ReportCode,
        string ReportName,
        string Category,
        string? Description,
        string SqlTemplate,
        string? ParametersSchemaJson,
        string? DisplayColumnsJson,
        int IsPublished,
        long CreatedAtMs,
        string? CreatedBy,
        long UpdatedAtMs
    );

    public sealed record UpsertReportDefinitionRequest(
        string ReportCode,
        string ReportName,
        string? Category,
        string? Description,
        string SqlTemplate,
        string? ParametersSchemaJson,
        string? DisplayColumnsJson,
        int IsPublished
    );

    public sealed record ExecuteReportRequest(
        string? ReportDefinitionId,
        string? SqlTemplate,
        Dictionary<string, object?>? Parameters
    );

    public sealed record ValidateSqlRequest(
        string SqlTemplate
    );

    public sealed record SqlValidationResult(
        bool IsValid,
        string? ErrorMessage,
        List<ReportParameterDefinition> ExtractedParameters
    );

    public sealed record ReportQueryResult(
        string ReportCode,
        string ReportName,
        List<string> Columns,
        List<List<string>> Rows,
        int TotalRows,
        long ExecutionDurationMs,
        string ExecutedAt
    );

    #endregion

    /// <summary>
    /// 获取报表模板列表 (管理员查看全部，其他角色查看已发布)
    /// </summary>
    [HttpGet("definitions")]
    public async Task<ApiResponse<IReadOnlyList<ReportDefinitionDto>>> GetDefinitions([FromQuery] string? category = null)
    {
        bool isAdmin = _userSession?.Roles?.Contains(RoleCode.Admin) == true || _userSession?.RoleCode == RoleCode.Admin;
        using var conn = _connFactory.CreateConnection();

        string sql = "SELECT * FROM ops_report_definitions WHERE 1=1";
        if (!isAdmin)
        {
            sql += " AND is_published = 1";
        }
        if (!string.IsNullOrWhiteSpace(category) && category != "全部")
        {
            sql += " AND category = @Category";
        }
        sql += " ORDER BY category ASC, report_name ASC";

        var list = (await conn.QueryAsync<OpsReportDefinition>(sql, new { Category = category })).ToList();
        var dtos = list.Select(MapToDto).ToList();
        return ApiResponse<IReadOnlyList<ReportDefinitionDto>>.Success(dtos);
    }

    /// <summary>
    /// 获取单条报表定义详情
    /// </summary>
    [HttpGet("definitions/{id}")]
    public async Task<ApiResponse<ReportDefinitionDto>> GetDefinitionById(string id)
    {
        var item = await _reportRepo.GetByIdAsync(id);
        if (item == null)
        {
            return ApiResponse<ReportDefinitionDto>.Fail(404, "指定的报表定义不存在");
        }
        return ApiResponse<ReportDefinitionDto>.Success(MapToDto(item));
    }

    /// <summary>
    /// 管理员创建报表定义 (带 SQL 语法与注入校验)
    /// </summary>
    [HttpPost("definitions")]
    [RequireRole(RoleCode.Admin)]
    public async Task<ApiResponse<ReportDefinitionDto>> CreateDefinition([FromBody] UpsertReportDefinitionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ReportCode) || string.IsNullOrWhiteSpace(request.ReportName))
        {
            return ApiResponse<ReportDefinitionDto>.Fail(400, "报表编码与报表名称不能为空");
        }

        var validation = ValidateSql(request.SqlTemplate);
        if (!validation.IsValid)
        {
            return ApiResponse<ReportDefinitionDto>.Fail(400, $"SQL 验证失败: {validation.ErrorMessage}");
        }

        using var conn = _connFactory.CreateConnection();
        var existing = await conn.QueryFirstOrDefaultAsync<OpsReportDefinition>(
            "SELECT * FROM ops_report_definitions WHERE report_code = @ReportCode",
            new { request.ReportCode });

        if (existing != null)
        {
            return ApiResponse<ReportDefinitionDto>.Fail(409, $"报表编码 [{request.ReportCode}] 已存在");
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string currentUserId = _userSession?.UserId ?? "ADMIN";

        // 如果参数 JSON 为空，则自动根据 SQL 提取参数骨架
        string paramJson = request.ParametersSchemaJson ?? string.Empty;
        if (string.IsNullOrWhiteSpace(paramJson) || paramJson == "[]")
        {
            paramJson = JsonSerializer.Serialize(validation.ExtractedParameters, JsonOptions);
        }

        var entity = new OpsReportDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            ReportCode = request.ReportCode.Trim(),
            ReportName = request.ReportName.Trim(),
            Category = string.IsNullOrWhiteSpace(request.Category) ? "综合" : request.Category.Trim(),
            Description = request.Description?.Trim(),
            SqlTemplate = request.SqlTemplate.Trim(),
            ParametersSchemaJson = paramJson,
            DisplayColumnsJson = request.DisplayColumnsJson,
            IsPublished = request.IsPublished,
            SchemaVersion = 1,
            CreatedAtMs = now,
            CreatedBy = currentUserId,
            UpdatedAtMs = now,
            UpdatedBy = currentUserId,
            VersionNo = 1
        };

        await _reportRepo.InsertAsync(entity);
        return ApiResponse<ReportDefinitionDto>.Success(MapToDto(entity), "报表定义创建成功");
    }

    /// <summary>
    /// 管理员更新报表定义
    /// </summary>
    [HttpPut("definitions/{id}")]
    [RequireRole(RoleCode.Admin)]
    public async Task<ApiResponse<ReportDefinitionDto>> UpdateDefinition(string id, [FromBody] UpsertReportDefinitionRequest request)
    {
        var entity = await _reportRepo.GetByIdAsync(id);
        if (entity == null)
        {
            return ApiResponse<ReportDefinitionDto>.Fail(404, "指定的报表定义不存在");
        }

        var validation = ValidateSql(request.SqlTemplate);
        if (!validation.IsValid)
        {
            return ApiResponse<ReportDefinitionDto>.Fail(400, $"SQL 验证失败: {validation.ErrorMessage}");
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string currentUserId = _userSession?.UserId ?? "ADMIN";

        entity.ReportName = request.ReportName.Trim();
        entity.Category = string.IsNullOrWhiteSpace(request.Category) ? "综合" : request.Category.Trim();
        entity.Description = request.Description?.Trim();
        entity.SqlTemplate = request.SqlTemplate.Trim();
        entity.ParametersSchemaJson = request.ParametersSchemaJson;
        entity.DisplayColumnsJson = request.DisplayColumnsJson;
        entity.IsPublished = request.IsPublished;
        entity.UpdatedAtMs = now;
        entity.UpdatedBy = currentUserId;

        await _reportRepo.UpdateAsync(entity);
        return ApiResponse<ReportDefinitionDto>.Success(MapToDto(entity), "报表定义更新成功");
    }

    /// <summary>
    /// 管理员删除报表定义
    /// </summary>
    [HttpDelete("definitions/{id}")]
    [RequireRole(RoleCode.Admin)]
    public async Task<ApiResponse<bool>> DeleteDefinition(string id)
    {
        var entity = await _reportRepo.GetByIdAsync(id);
        if (entity == null)
        {
            return ApiResponse<bool>.Fail(404, "指定的报表定义不存在");
        }

        await _reportRepo.DeleteAsync(id);
        return ApiResponse<bool>.Success(true, "报表定义已删除");
    }

    /// <summary>
    /// 校验 SQL 模板合法性并提取参数占位符
    /// </summary>
    [HttpPost("validate-sql")]
    [RequireRole(RoleCode.Admin)]
    public ApiResponse<SqlValidationResult> ValidateSqlEndpoint([FromBody] ValidateSqlRequest request)
    {
        var result = ValidateSql(request.SqlTemplate);
        return ApiResponse<SqlValidationResult>.Success(result);
    }

    /// <summary>
    /// 执行已定义的报表 (动态参数绑定)
    /// </summary>
    [HttpPost("definitions/{id}/execute")]
    public async Task<ApiResponse<ReportQueryResult>> ExecuteReport(string id, [FromBody] Dictionary<string, object?>? parameters)
    {
        var def = await _reportRepo.GetByIdAsync(id);
        if (def == null)
        {
            return ApiResponse<ReportQueryResult>.Fail(404, "指定的报表定义不存在");
        }

        return await ExecuteInternalAsync(def.SqlTemplate, def.ParametersSchemaJson, parameters, def.Id, def.ReportCode, def.ReportName);
    }

    /// <summary>
    /// 管理员在线试运行临时 SQL (设计器调试模式)
    /// </summary>
    [HttpPost("execute-raw")]
    [RequireRole(RoleCode.Admin)]
    public async Task<ApiResponse<ReportQueryResult>> ExecuteRawSql([FromBody] ExecuteReportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SqlTemplate))
        {
            return ApiResponse<ReportQueryResult>.Fail(400, "SQL 模板不能为空");
        }

        return await ExecuteInternalAsync(
            request.SqlTemplate,
            null,
            request.Parameters,
            "ADHOC",
            "ADHOC_QUERY",
            "临时动态查询");
    }

    /// <summary>
    /// 查询报表历史执行审计记录
    /// </summary>
    [HttpGet("definitions/{id}/executions")]
    public async Task<ApiResponse<IReadOnlyList<OpsReportExecution>>> GetExecutionHistory(string id)
    {
        using var conn = _connFactory.CreateConnection();
        var list = (await conn.QueryAsync<OpsReportExecution>(
            "SELECT * FROM ops_report_executions WHERE report_definition_id = @Id ORDER BY executed_at_ms DESC LIMIT 30",
            new { Id = id })).ToList();
        return ApiResponse<IReadOnlyList<OpsReportExecution>>.Success(list);
    }

    /// <summary>
    /// 导出当前报表结果为 CSV
    /// </summary>
    [HttpPost("export-csv")]
    public async Task<IActionResult> ExportCsv([FromBody] ExecuteReportRequest request)
    {
        string sql = request.SqlTemplate ?? string.Empty;
        string reportName = "报表导出";
        string? schemaJson = null;

        if (!string.IsNullOrWhiteSpace(request.ReportDefinitionId))
        {
            var def = await _reportRepo.GetByIdAsync(request.ReportDefinitionId);
            if (def != null)
            {
                sql = def.SqlTemplate;
                reportName = def.ReportName;
                schemaJson = def.ParametersSchemaJson;
            }
        }

        var res = await ExecuteInternalAsync(sql, schemaJson, request.Parameters, "EXPORT", "EXPORT", reportName);
        if (res.Code != 200 || res.Data == null)
        {
            return BadRequest(res.Message);
        }

        var sb = new StringBuilder();
        // UTF-8 BOM for Excel Chinese compatibility
        sb.Append('\uFEFF');

        // Headers
        sb.AppendLine(string.Join(",", res.Data.Columns.Select(EscapeCsv)));

        // Rows
        foreach (var row in res.Data.Rows)
        {
            sb.AppendLine(string.Join(",", row.Select(EscapeCsv)));
        }

        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        string filename = $"{reportName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        return File(bytes, "text/csv; charset=utf-8", filename);
    }

    #region Internal Execution Engine

    private async Task<ApiResponse<ReportQueryResult>> ExecuteInternalAsync(
        string sqlTemplate,
        string? schemaJson,
        Dictionary<string, object?>? userParams,
        string reportId,
        string reportCode,
        string reportName)
    {
        var validation = ValidateSql(sqlTemplate);
        if (!validation.IsValid)
        {
            return ApiResponse<ReportQueryResult>.Fail(400, $"SQL 验证失败: {validation.ErrorMessage}");
        }

        // 解析参数元数据
        List<ReportParameterDefinition> paramDefs = new();
        if (!string.IsNullOrWhiteSpace(schemaJson))
        {
            try
            {
                paramDefs = JsonSerializer.Deserialize<List<ReportParameterDefinition>>(schemaJson, JsonOptions) ?? new();
            }
            catch
            {
                // ignore
            }
        }

        // 绑定 Dapper DynamicParameters
        var dapperParams = new DynamicParameters();
        var passedParams = userParams ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        // 收集所有在 SQL 中引用的参数占位符
        var sqlPlaceholders = ParameterRegex.Matches(sqlTemplate)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var placeholder in sqlPlaceholders)
        {
            var meta = paramDefs.FirstOrDefault(p => string.Equals(p.Name, placeholder, StringComparison.OrdinalIgnoreCase));
            object? rawValue = null;

            // 优先从传入参数中寻找 (不区分大小写)
            var matchKey = passedParams.Keys.FirstOrDefault(k => string.Equals(k, placeholder, StringComparison.OrdinalIgnoreCase) || string.Equals(k, "@" + placeholder, StringComparison.OrdinalIgnoreCase));
            if (matchKey != null)
            {
                rawValue = passedParams[matchKey];
            }
            else if (meta != null && !string.IsNullOrWhiteSpace(meta.DefaultValue))
            {
                rawValue = meta.DefaultValue;
            }

            // 类型转换
            object? typedValue = ConvertValue(rawValue, meta?.Type ?? "STRING");
            dapperParams.Add(placeholder, typedValue);
        }

        // 安全限制: 若 SQL 无 LIMIT，追加 LIMIT 5000 保护服务端内存
        string finalSql = sqlTemplate.TrimEnd();
        if (finalSql.EndsWith(';'))
        {
            finalSql = finalSql[..^1].TrimEnd();
        }
        if (!Regex.IsMatch(finalSql, @"\bLIMIT\b\s+\d+", RegexOptions.IgnoreCase))
        {
            finalSql += " LIMIT 5000";
        }

        var sw = Stopwatch.StartNew();
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string currentUserId = _userSession?.UserId ?? "GUEST";
        string currentUserName = _userSession?.UserName ?? "访客";

        try
        {
            using var conn = _connFactory.CreateConnection();
            var rowsData = (await conn.QueryAsync(finalSql, dapperParams)).ToList();
            sw.Stop();

            List<string> columns = new();
            List<List<string>> rows = new();

            if (rowsData.Count > 0)
            {
                if (rowsData[0] is IDictionary<string, object> firstRowDict)
                {
                    columns = firstRowDict.Keys.ToList();
                }

                foreach (var item in rowsData)
                {
                    if (item is IDictionary<string, object> dict)
                    {
                        var rowVals = columns.Select(col =>
                        {
                            if (dict.TryGetValue(col, out var val) && val != null && val != DBNull.Value)
                            {
                                if (val is decimal dec)
                                {
                                    return dec.ToString("F2");
                                }
                                if (val is double dbl)
                                {
                                    return dbl.ToString("F2");
                                }
                                if (val is DateTime dt)
                                {
                                    return dt.ToString("yyyy-MM-dd HH:mm:ss");
                                }
                                return val.ToString() ?? string.Empty;
                            }
                            return string.Empty;
                        }).ToList();
                        rows.Add(rowVals);
                    }
                }
            }

            var queryResult = new ReportQueryResult(
                reportCode,
                reportName,
                columns,
                rows,
                rows.Count,
                sw.ElapsedMilliseconds,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            );

            // 记录审计历史
            try
            {
                var exec = new OpsReportExecution
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ReportDefinitionId = reportId,
                    ReportCode = reportCode,
                    ReportName = reportName,
                    ParametersJson = JsonSerializer.Serialize(passedParams, JsonOptions),
                    ResultRowCount = rows.Count,
                    ExecutionDurationMs = sw.ElapsedMilliseconds,
                    ExecutedBy = currentUserId,
                    ExecutedByName = currentUserName,
                    ExecutedAtMs = now,
                    ErrorMessage = null,
                    SchemaVersion = 1,
                    CreatedAtMs = now,
                    CreatedBy = currentUserId,
                    UpdatedAtMs = now,
                    UpdatedBy = currentUserId,
                    VersionNo = 1
                };
                await _executionRepo.InsertAsync(exec);
            }
            catch
            {
                // Audit execution failure shouldn't fail query
            }

            return ApiResponse<ReportQueryResult>.Success(queryResult);
        }
        catch (Exception ex)
        {
            sw.Stop();
            // 记录错误审计
            try
            {
                var exec = new OpsReportExecution
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ReportDefinitionId = reportId,
                    ReportCode = reportCode,
                    ReportName = reportName,
                    ParametersJson = JsonSerializer.Serialize(passedParams, JsonOptions),
                    ResultRowCount = 0,
                    ExecutionDurationMs = sw.ElapsedMilliseconds,
                    ExecutedBy = currentUserId,
                    ExecutedByName = currentUserName,
                    ExecutedAtMs = now,
                    ErrorMessage = ex.Message,
                    SchemaVersion = 1,
                    CreatedAtMs = now,
                    CreatedBy = currentUserId,
                    UpdatedAtMs = now,
                    UpdatedBy = currentUserId,
                    VersionNo = 1
                };
                await _executionRepo.InsertAsync(exec);
            }
            catch
            {
                // ignore
            }

            return ApiResponse<ReportQueryResult>.Fail(500, $"SQL 执行异常: {ex.Message}");
        }
    }

    private static object? ConvertValue(object? raw, string type)
    {
        if (raw == null || raw == DBNull.Value) return null;
        string str = raw.ToString()?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(str)) return null;

        switch (type.ToUpperInvariant())
        {
            case "INT":
            case "INTEGER":
                return int.TryParse(str, out int iVal) ? iVal : null;
            case "BIGINT":
            case "LONG":
                return long.TryParse(str, out long lVal) ? lVal : null;
            case "DECIMAL":
            case "NUMBER":
                return decimal.TryParse(str, out decimal dVal) ? dVal : null;
            case "BOOLEAN":
            case "BOOL":
                return bool.TryParse(str, out bool bVal) ? bVal : (str == "1");
            case "DATE":
            case "DATETIME":
                if (DateTime.TryParse(str, out DateTime dtVal))
                {
                    return dtVal;
                }
                return str;
            default:
                return str;
        }
    }

    private static SqlValidationResult ValidateSql(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return new SqlValidationResult(false, "SQL 语句不能为空", new());
        }

        string trimmed = sql.Trim();

        // 仅允许以 SELECT 或 WITH 开头
        if (!trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
        {
            return new SqlValidationResult(false, "为保障系统数据安全，自定义报表仅允许执行只读 SELECT 查询（或 CTE WITH ... SELECT）", new());
        }

        // 禁止多条执行语句
        var parts = trimmed.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length > 1)
        {
            return new SqlValidationResult(false, "禁止堆叠执行多条 SQL 语句 (检测到分号分割的多条语句)", new());
        }

        // 校验违禁关键词
        foreach (var token in ForbiddenTokens)
        {
            string pattern = $@"\b{token}\b";
            if (Regex.IsMatch(trimmed, pattern, RegexOptions.IgnoreCase))
            {
                return new SqlValidationResult(false, $"检测到高危或非法的 SQL 指令 [{token}]，系统拒绝执行写操作及管理指令", new());
            }
        }

        // 提取参数占位符
        var matches = ParameterRegex.Matches(trimmed);
        var paramList = new List<ReportParameterDefinition>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in matches)
        {
            string name = m.Groups[1].Value;
            if (seen.Add(name))
            {
                string inferredType = "STRING";
                string label = name;
                string? defaultVal = "";

                if (name.Contains("date", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("time", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("day", StringComparison.OrdinalIgnoreCase))
                {
                    inferredType = "INT";
                    label = $"{name} (如 20261101)";
                    defaultVal = "20261101";
                }
                else if (name.Contains("amount", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("price", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("fee", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("cost", StringComparison.OrdinalIgnoreCase))
                {
                    inferredType = "DECIMAL";
                    label = $"{name} 金额";
                    defaultVal = "0";
                }
                else if (name.Contains("status", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("count", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("type", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("code", StringComparison.OrdinalIgnoreCase))
                {
                    inferredType = "INT";
                    label = $"{name} 状态/代码";
                    defaultVal = "1";
                }

                paramList.Add(new ReportParameterDefinition
                {
                    Name = name,
                    Label = label,
                    Type = inferredType,
                    DefaultValue = defaultVal,
                    IsRequired = false,
                    Description = $"动态参数占位符 @{name}"
                });
            }
        }

        return new SqlValidationResult(true, null, paramList);
    }

    private static ReportDefinitionDto MapToDto(OpsReportDefinition entity)
    {
        return new ReportDefinitionDto(
            entity.Id,
            entity.ReportCode,
            entity.ReportName,
            entity.Category,
            entity.Description,
            entity.SqlTemplate,
            entity.ParametersSchemaJson,
            entity.DisplayColumnsJson,
            entity.IsPublished,
            entity.CreatedAtMs,
            entity.CreatedBy,
            entity.UpdatedAtMs
        );
    }

    private static string EscapeCsv(string? field)
    {
        if (string.IsNullOrEmpty(field)) return "\"\"";
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }

    #endregion
}
