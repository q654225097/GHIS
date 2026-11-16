using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Inventory.Models;

public class InvStockLedger : BaseEntity
{
	public string WarehouseOrPharmacyId { get; set; } = string.Empty;

	public string DrugId { get; set; } = string.Empty;

	public string BatchId { get; set; } = string.Empty;

	public string ChangeType { get; set; } = "IN_PURCHASE";

	public int ChangeQuantity { get; set; }

	public int BalanceAfter { get; set; }

	public string? ReferenceDocNo { get; set; }

	public string? PaymentOrderId { get; set; }

	public decimal? UnitCostPrice { get; set; }

	public decimal? UnitRetailPrice { get; set; }
}
