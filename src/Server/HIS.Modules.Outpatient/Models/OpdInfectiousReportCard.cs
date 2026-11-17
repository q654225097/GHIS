using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Outpatient.Models;

public class OpdInfectiousReportCard : BaseEntity
{
	public string ReportNo { get; set; } = string.Empty;
	public string VisitId { get; set; } = string.Empty;
	public string PatientId { get; set; } = string.Empty;
	public string IcdCode { get; set; } = string.Empty;
	public string DiseaseName { get; set; } = string.Empty;
	public string DiseaseCategory { get; set; } = "CLASS_B";
	public string DiagnoseType { get; set; } = "CONFIRMED";
	public int OnsetDateInt { get; set; }
	public int DiagnosedDateInt { get; set; }
	public string ReportDoctorId { get; set; } = string.Empty;
	public long ReportedAtMs { get; set; }
	public string CdcSyncStatus { get; set; } = "PENDING";
	public long? CdcSyncAtMs { get; set; }
	public string Status { get; set; } = "REPORTED";
}
