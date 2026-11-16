using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Config.Models;

public class CfgEntityFieldValue : BaseEntity
{
	public string EntityType { get; set; } = string.Empty;

	public string EntityId { get; set; } = string.Empty;

	public string FieldCode { get; set; } = string.Empty;

	public string? ValString { get; set; }

	public long? ValInt { get; set; }

	public decimal? ValDecimal { get; set; }

	public long? ValTimeMs { get; set; }
}
