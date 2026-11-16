using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Platform.Models;

public class SysRole : BaseEntity
{
	public string RoleCode { get; set; } = string.Empty;

	public string RoleName { get; set; } = string.Empty;

	public string DataScope { get; set; } = "ALL_CAMPUS";

	public short IsActive { get; set; } = 1;
}
