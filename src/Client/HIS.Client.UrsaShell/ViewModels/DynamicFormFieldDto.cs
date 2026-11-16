namespace HIS.Client.UrsaShell.ViewModels;

public sealed class DynamicFormFieldDto
{
	public string Id { get; set; } = string.Empty;

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

	public short IsIndexed { get; set; }

	public int SortOrder { get; set; }

	public short IsDeleted { get; set; }

	public long? DeletedAtMs { get; set; }

	public string? DeletedBy { get; set; }

	public string? ExtDataJson { get; set; }

	public int SchemaVersion { get; set; } = 1;
}
