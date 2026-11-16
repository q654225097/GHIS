using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class DrugCatalogItemModel : ClientModelBase
{
	public string DrugId { get; set; } = string.Empty;

	public string DrugCode { get; set; } = string.Empty;

	public string UnitName { get; set; } = string.Empty;

	public string DrugName { get; set; } = string.Empty;

	public string Spec { get; set; } = string.Empty;

	public string DefaultDose { get; set; } = string.Empty;

	public string DefaultFreq { get; set; } = string.Empty;

	public string DefaultRoute { get; set; } = string.Empty;

	public string UnitPrice { get; set; } = string.Empty;

	public string StockQuantity { get; set; } = string.Empty;

	public bool IsSkinTestRequired { get; set; }

	public bool IsHighRisk { get; set; }
}
