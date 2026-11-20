using System.Collections.Generic;
using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Operations.Models;

/// <summary>
/// 自定义报表定义实体 (支持带动态参数占位符的只读 SQL)
/// </summary>
public class OpsReportDefinition : BaseEntity
{
    public string ReportCode { get; set; } = string.Empty;
    public string ReportName { get; set; } = string.Empty;
    public string Category { get; set; } = "综合";
    public string? Description { get; set; }
    public string SqlTemplate { get; set; } = string.Empty;
    public string? ParametersSchemaJson { get; set; }
    public string? DisplayColumnsJson { get; set; }
    public int IsPublished { get; set; } = 1;
}

/// <summary>
/// 报表执行审计历史
/// </summary>
public class OpsReportExecution : BaseEntity
{
    public string ReportDefinitionId { get; set; } = string.Empty;
    public string ReportCode { get; set; } = string.Empty;
    public string ReportName { get; set; } = string.Empty;
    public string? ParametersJson { get; set; }
    public int ResultRowCount { get; set; } = 0;
    public long ExecutionDurationMs { get; set; } = 0;
    public string? ExecutedBy { get; set; }
    public string? ExecutedByName { get; set; }
    public long ExecutedAtMs { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 报表参数定义元数据
/// </summary>
public sealed class ReportParameterDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = "STRING"; // STRING, INT, DECIMAL, DATE, DATETIME, BOOLEAN
    public string? DefaultValue { get; set; }
    public bool IsRequired { get; set; }
    public string? Description { get; set; }
}
