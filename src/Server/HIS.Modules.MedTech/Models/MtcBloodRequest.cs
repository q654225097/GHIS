using HIS.BuildingBlocks.Database;

namespace HIS.Modules.MedTech.Models;

public class MtcBloodRequest : BaseEntity
{
	public string EncounterId { get; set; } = string.Empty;

	public string PatientId { get; set; } = string.Empty;

	public string AboType { get; set; } = "O";

	public string RhType { get; set; } = "POSITIVE";

	public string BloodProductCode { get; set; } = "RBC";

	public int VolumeMl { get; set; } = 400;

	public string? CrossMatchResult { get; set; }

	public string? BagBarcode { get; set; }

	public string? DispensedById { get; set; }

	public string? DualCheckDispenserId { get; set; }

	public short EmptyBagReturned { get; set; } = 0;

	public int Status { get; set; } = 0;
}
