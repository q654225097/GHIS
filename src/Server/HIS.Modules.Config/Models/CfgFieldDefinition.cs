using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Config.Models;

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

	public short IsDeleted { get; set; } = 0;

	public long? DeletedAtMs { get; set; }

	public string? DeletedBy { get; set; }
}
