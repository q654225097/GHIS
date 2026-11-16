using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Pharmacy.Models;

public class PhmDispenseOrder : BaseEntity
{
	public string DispenseNo { get; set; } = string.Empty;

	public string PharmacyId { get; set; } = string.Empty;

	public string? PrescriptionId { get; set; }

	public string? WardSummaryId { get; set; }

	public string PatientId { get; set; } = string.Empty;

	public int Status { get; set; } = 0;

	public string? DispensedById { get; set; }

	public long? DispensedAtMs { get; set; }

	public short DrugReturnConfirmed { get; set; } = 0;
}
