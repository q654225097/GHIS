using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Ordering.Models;

public class OrdExecutionPlan : BaseEntity
{
	public string OrderId { get; set; } = string.Empty;

	public string EncounterId { get; set; } = string.Empty;

	public string WardId { get; set; } = string.Empty;

	public long PlannedTimeMs { get; set; }

	public int ExecStatus { get; set; } = 0;

	public string? ExecNurseId { get; set; }

	public string? DualCheckNurseId { get; set; }

	public long? ExecutedAtMs { get; set; }

	public string? PdaScanToken { get; set; }
}
