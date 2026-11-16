using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Outpatient.Models;

public class OpdPrescriptionItem : BaseEntity
{
	public string PrescriptionId { get; set; } = string.Empty;

	public int GroupNo { get; set; } = 1;

	public string DrugId { get; set; } = string.Empty;

	public string DrugNameSnapshot { get; set; } = string.Empty;

	public decimal DoseOnce { get; set; }

	public string DoseUnit { get; set; } = "mg";

	public string FrequencyCode { get; set; } = "QD";

	public string RouteCode { get; set; } = "PO";

	public int Days { get; set; } = 3;

	public int TotalQuantity { get; set; } = 1;

	public decimal UnitPrice { get; set; }

	public int SkinTestFlag { get; set; } = 0;

	public string? SkinTestBatchNo { get; set; }

	public string? SkinTestCheckerId { get; set; }

	public short IsDeleted { get; set; } = 0;

	public long? DeletedAtMs { get; set; }

	public string? DeletedBy { get; set; }
}
