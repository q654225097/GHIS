using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Inventory.Models;

public class InvDrug : BaseEntity
{
	public string DrugCode { get; set; } = string.Empty;

	public string DrugName { get; set; } = string.Empty;

	public string Spec { get; set; } = string.Empty;

	public string UnitName { get; set; } = "支";

	public decimal Price { get; set; }

	public decimal CostPrice { get; set; }

	public int AntibioticLevel { get; set; } = 0;

	public short IsNarcotic { get; set; } = 0;

	public short IsSkinTestRequired { get; set; } = 0;

	public short IsActive { get; set; } = 1;
}
