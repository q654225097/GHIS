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
