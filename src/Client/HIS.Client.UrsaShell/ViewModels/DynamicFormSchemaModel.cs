namespace HIS.Client.UrsaShell.ViewModels;

public sealed class DynamicFormSchemaModel
{
	public string Id { get; set; } = string.Empty;

	public string FormCode { get; set; } = string.Empty;

	public string FormName { get; set; } = string.Empty;

	public string EntityType { get; set; } = string.Empty;

	public string? DeptCategory { get; set; }

	public string? TargetDeptId { get; set; }

	public int GridColumns { get; set; } = 2;

	public short IsPublished { get; set; } = 1;

	public string? ExtDataJson { get; set; }

	public int SchemaVersion { get; set; } = 1;
}
