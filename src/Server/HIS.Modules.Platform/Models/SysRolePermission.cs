using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Platform.Models;

public class SysRolePermission : BaseEntity
{
	public string RoleId { get; set; } = string.Empty;

	public string ResourceKey { get; set; } = string.Empty;

	public int AccessPolicy { get; set; } = 3;
}
