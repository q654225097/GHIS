using HIS.BuildingBlocks.Database;

namespace HIS.Modules.MedTech.Models;

public class MedtechCriticalValue : BaseEntity
{
	public string ReportId { get; set; } = string.Empty;
	public string SourceType { get; set; } = "LAB";
	public string VisitOrEncounterId { get; set; } = string.Empty;
	public string PatientId { get; set; } = string.Empty;
	public string ItemCode { get; set; } = string.Empty;
	public string ItemName { get; set; } = string.Empty;
	public string CriticalValueText { get; set; } = string.Empty;
	public string PublishedBy { get; set; } = string.Empty;
	public long PublishedAtMs { get; set; }
	public string? DocAckedBy { get; set; }
	public long? DocAckedAtMs { get; set; }
	public string? NurseAckedBy { get; set; }
	public long? NurseAckedAtMs { get; set; }
	public string? ActionOrderId { get; set; }
	public string Status { get; set; } = "PENDING_ACK";
}
