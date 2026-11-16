using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Platform.Models;

public class SysDeptWardRelation : BaseEntity
{
	public string IpdDeptId { get; set; } = string.Empty;

	public string WardDeptId { get; set; } = string.Empty;

	public string RelationType { get; set; } = "PRIMARY";

	public int MaxBorrowBeds { get; set; } = 10;

	public int PriorityOrder { get; set; } = 1;

	public short IsActive { get; set; } = 1;
}
