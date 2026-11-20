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

	public string? PatientWristbandCode { get; set; }

	public string? ItemBarcode { get; set; }

	public int ScanMatched { get; set; } = 0;

	public long? InfusionStartMs { get; set; }

	public long? InfusionEndMs { get; set; }

	public string? HighRiskWarningJson { get; set; }

	public string? HighRiskAckBy { get; set; }

	public string? ClinicalCheckSnapshotJson { get; set; }
}
