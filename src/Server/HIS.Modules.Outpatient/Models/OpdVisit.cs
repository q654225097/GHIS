using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Outpatient.Models;

public class OpdVisit : BaseEntity
{
	public string VisitNo { get; set; } = string.Empty;

	public string? RequestKey { get; set; }

	public string CampusId { get; set; } = "CAMPUS-01";

	public string OpdDeptId { get; set; } = string.Empty;

	public string? ClinicRoomId { get; set; }

	public string PatientId { get; set; } = string.Empty;

	public string? ScheduleId { get; set; }

	public string DoctorId { get; set; } = string.Empty;

	public short IsEmergency { get; set; } = 0;

	public int? ErTriageLevel { get; set; }

	public short IsGreenChannel { get; set; } = 0;

	public decimal? GreenCreditLimit { get; set; }

	public short IsReturnVisit { get; set; } = 0;

	public int Status { get; set; } = 1;

	public string? PrimaryDiagCode { get; set; }

	public string? PrimaryDiagName { get; set; }

	public string? EmrDocumentId { get; set; }

	public string? OfdArchiveId { get; set; }

	public long? SignedAtMs { get; set; }
}
