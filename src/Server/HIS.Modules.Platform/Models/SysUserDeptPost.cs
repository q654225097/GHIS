using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Platform.Models;

public class SysUserDeptPost : BaseEntity
{
	public string UserId { get; set; } = string.Empty;

	public string DeptId { get; set; } = string.Empty;

	public string DeptCategory { get; set; } = "OPD";

	public string? MedGroupId { get; set; }

	public string RoleId { get; set; } = string.Empty;

	public short IsDefaultContext { get; set; } = 1;
}
