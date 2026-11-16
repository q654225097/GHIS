using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Outpatient.Models;

public class OpdPrescription : BaseEntity
{
	public string PrescriptionNo { get; set; } = string.Empty;

	public string VisitId { get; set; } = string.Empty;

	public string PatientId { get; set; } = string.Empty;

	public string OpdDeptId { get; set; } = string.Empty;

	public string DoctorId { get; set; } = string.Empty;

	public string TargetPharmacyId { get; set; } = string.Empty;

	public string RxType { get; set; } = "WESTERN";

	public int? HerbDoses { get; set; }

	public decimal TotalAmount { get; set; }

	public int Status { get; set; } = 0;

	public string? PaymentOrderId { get; set; }

	public string? ReviewerPharmacistId { get; set; }

	public string? ReviewRemark { get; set; }

	public string? RxOfdArchiveId { get; set; }
}
