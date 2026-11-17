using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Inventory.Models;

public class InvStockTakeLine : BaseEntity
{
	public string StockTakeId { get; set; } = string.Empty;
	public string DrugId { get; set; } = string.Empty;
	public string BatchId { get; set; } = string.Empty;
	public string DrugCode { get; set; } = string.Empty;
	public string DrugName { get; set; } = string.Empty;
	public string? Spec { get; set; }
	public string? UnitName { get; set; }
	public string BatchNo { get; set; } = string.Empty;
	public int SystemQty { get; set; }
	public int ActualQty { get; set; }
	public int DiffQty { get; set; }
	public decimal UnitCostPrice { get; set; }
	public decimal DiffCostAmount { get; set; }
	public string? Reason { get; set; }
}
