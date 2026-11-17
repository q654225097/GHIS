using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Inventory.Models;

public class InvDrugTraceCode : BaseEntity
{
	public string TraceCode { get; set; } = string.Empty;
	public string NationalInsCode { get; set; } = string.Empty;
	public string DrugId { get; set; } = string.Empty;
	public string BatchId { get; set; } = string.Empty;
	public string BatchNo { get; set; } = string.Empty;
	public string WarehouseOrPharmacyId { get; set; } = string.Empty;
	public string? StockInOrderId { get; set; }
	public string? DispenseOrderId { get; set; }
	public string? DispenseOrderLineId { get; set; }
	public string Status { get; set; } = "IN_STOCK";
	public short IsRecalled { get; set; } = 0;
}
