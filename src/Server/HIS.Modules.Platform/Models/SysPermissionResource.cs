using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Platform.Models;

public class SysPermissionResource : BaseEntity
{
	public string ResourceKey { get; set; } = string.Empty;

	public string? ParentKey { get; set; }

	public string ResourceType { get; set; } = "COMPONENT";

	public string ResourceName { get; set; } = string.Empty;

	public string? UrsaControlType { get; set; }

	public string? ApiMethodPath { get; set; }
}
