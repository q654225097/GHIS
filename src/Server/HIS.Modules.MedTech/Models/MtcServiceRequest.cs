using HIS.BuildingBlocks.Database;

namespace HIS.Modules.MedTech.Models;

public class MtcServiceRequest : BaseEntity
{
	public string RequestNo { get; set; } = string.Empty;

	public string SourceType { get; set; } = "OPD";

	public string? SourceVisitId { get; set; }

	public string? SourceOrderId { get; set; }

	public string PatientId { get; set; } = string.Empty;

	public string OrderDeptId { get; set; } = string.Empty;

	public string? OrderWardId { get; set; }

	public string ExecMedTechDeptId { get; set; } = string.Empty;

	public string MedTechSubType { get; set; } = "LAB";

	public string? ExecRoomCode { get; set; }

	public string? SpecimenTubeCode { get; set; }

	public string ServiceItemCode { get; set; } = string.Empty;

	public decimal FeeAmount { get; set; }

	public short IsFeeConfirmed { get; set; } = 0;

	public int Status { get; set; } = 0;

	public long? CollectedAtMs { get; set; }

	public long? ReceivedAtMs { get; set; }

	public long? ExecutedAtMs { get; set; }

	public string? ReportSummary { get; set; }

	public string? ReportOfdArchiveId { get; set; }

	public short IsCriticalValue { get; set; } = 0;

	public string? CriticalAckDocId { get; set; }

	public string? CriticalAckNurseId { get; set; }
}
