using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Inventory.Models;

public class InvDrugBatch : BaseEntity
{
	public string WarehouseOrPharmacyId { get; set; } = string.Empty;

	public string DrugId { get; set; } = string.Empty;

	public string BatchNo { get; set; } = string.Empty;

	public int ExpiryDateInt { get; set; }

	public string? InvoiceNo { get; set; }

	public decimal? ColdChainTempCelsius { get; set; }

	public int Quantity { get; set; }

	public short IsFrozen { get; set; } = 0;
}
