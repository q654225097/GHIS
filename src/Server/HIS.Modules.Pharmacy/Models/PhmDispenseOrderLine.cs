using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Pharmacy.Models;

public class PhmDispenseOrderLine : BaseEntity
{
	public string DispenseOrderId { get; set; } = string.Empty;

	public string? MedicalOrderId { get; set; }

	public string DrugId { get; set; } = string.Empty;

	public string DrugCode { get; set; } = string.Empty;

	public string DrugName { get; set; } = string.Empty;

	public int Quantity { get; set; }

	public decimal UnitPrice { get; set; }

	public decimal UnitCostPrice { get; set; }
}
