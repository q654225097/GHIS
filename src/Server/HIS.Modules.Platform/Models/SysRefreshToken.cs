using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Platform.Models;

public sealed class SysRefreshToken : BaseEntity
{
	public string UserId { get; set; } = string.Empty;

	public string PostId { get; set; } = string.Empty;

	public string TokenHash { get; set; } = string.Empty;

	public int SessionVersion { get; set; }

	public long ExpiresAtMs { get; set; }

	public long? RotatedAtMs { get; set; }

	public long? RevokedAtMs { get; set; }

	public string? ReplacedByTokenHash { get; set; }
}
