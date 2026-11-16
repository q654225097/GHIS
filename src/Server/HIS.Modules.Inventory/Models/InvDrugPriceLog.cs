using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Inventory.Models;

public class InvDrugPriceLog : BaseEntity
{
	public string AdjustNo { get; set; } = string.Empty;

	public string DrugId { get; set; } = string.Empty;

	public string DrugCode { get; set; } = string.Empty;

	public string DrugName { get; set; } = string.Empty;

	public decimal OldRetailPrice { get; set; }

	public decimal NewRetailPrice { get; set; }

	public decimal OldCostPrice { get; set; }

	public decimal NewCostPrice { get; set; }

	public int AffectedStockQuantity { get; set; }

	public decimal PriceDiffAmount { get; set; }

	public string AdjustReason { get; set; } = string.Empty;

	public long EffectiveAtMs { get; set; }

	public string? OperatorId { get; set; }
}
