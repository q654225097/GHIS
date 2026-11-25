using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Inpatient.Models;

public class IpdEncounter : BaseEntity
{
	public string AdmissionNo { get; set; } = string.Empty;

	public string PatientId { get; set; } = string.Empty;

	public string CampusId { get; set; } = "CAMPUS-01";

	public string AdmitDeptId { get; set; } = string.Empty;

	public string? MedGroupId { get; set; }

	public string AdmitWardId { get; set; } = string.Empty;

	public short IsBorrowedBed { get; set; } = 0;

	public string? BedCode { get; set; }

	public string ActiveBedToken { get; set; } = string.Empty;

	public string? AttendingDoctorId { get; set; }

	public string? PrimaryNurseId { get; set; }

	public string NursingLevelCode { get; set; } = "LEVEL_2";

	public decimal PrepaymentBalance { get; set; } = 0.00m;

	public string? ArchiveOfdId { get; set; }

	public int Status { get; set; } = 1;

	public int TriageStatus { get; set; } = 0;

	public long? AcceptedAtMs { get; set; }

	public string? AcceptedBy { get; set; }

	public long AdmitTimeMs { get; set; }
}
