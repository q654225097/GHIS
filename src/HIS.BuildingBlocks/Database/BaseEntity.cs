using System;

namespace HIS.BuildingBlocks.Database;

public abstract class BaseEntity
{
	public string Id { get; set; } = Guid.NewGuid().ToString("D");

	public string? ExtDataJson { get; set; }

	public int SchemaVersion { get; set; } = 1;

	public long CreatedAtMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

	public string? CreatedBy { get; set; }

	public long UpdatedAtMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

	public string? UpdatedBy { get; set; }

	public int VersionNo { get; set; } = 1;
}
