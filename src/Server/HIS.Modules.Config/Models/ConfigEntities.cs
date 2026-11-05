using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Config.Models;

public class CfgFormSchema : BaseEntity
{
    public string FormCode { get; set; } = string.Empty;
    public string FormName { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? DeptCategory { get; set; }
    public string? TargetDeptId { get; set; }
    public int GridColumns { get; set; } = 2;
    public short IsPublished { get; set; } = 1;
}

public class CfgFieldDefinition : BaseEntity
{
    public string FormSchemaId { get; set; } = string.Empty;
    public string FieldCode { get; set; } = string.Empty;
    public string FieldLabel { get; set; } = string.Empty;
    public string DataType { get; set; } = "STRING";
    public string UrsaControlType { get; set; } = "u:TextBox";
    public string? ControlKey { get; set; }
    public string? DictCode { get; set; }
    public string? UnitText { get; set; }
    public string? ValidationRuleJson { get; set; }
    public string? VisibilityExpr { get; set; }
    public short IsIndexed { get; set; } = 0;
    public int SortOrder { get; set; } = 0;
}

public class CfgEntityFieldValue : BaseEntity
{
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string FieldCode { get; set; } = string.Empty;
    public string? ValString { get; set; }
    public long? ValInt { get; set; }
    public decimal? ValDecimal { get; set; }
    public long? ValTimeMs { get; set; }
}

