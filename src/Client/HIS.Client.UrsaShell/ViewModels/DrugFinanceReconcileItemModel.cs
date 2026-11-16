using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class DrugFinanceReconcileItemModel : ClientModelBase
{
	public string DrugCode { get; set; } = string.Empty;

	public string DrugName { get; set; } = string.Empty;

	public string Spec { get; set; } = string.Empty;

	public string UnitName { get; set; } = "盒";

	public decimal CostPrice { get; set; }

	public decimal RetailPrice { get; set; }

	public int PurchasedQty { get; set; }

	public decimal PurchasedCostAmount { get; set; }

	public int DispensedQty { get; set; }

	public decimal DispensedSalesAmount { get; set; }

	public int ReturnedQty { get; set; }

	public decimal ReturnedSalesAmount { get; set; }

	public int CurrentStockQty { get; set; }

	public decimal CurrentStockRetailValue { get; set; }

	public decimal NetSalesAmount => DispensedSalesAmount - ReturnedSalesAmount;

	public decimal GrossProfitAmount => (decimal)(DispensedQty - ReturnedQty) * (RetailPrice - CostPrice);

	public string PurchasedSummary => $"{PurchasedQty}{UnitName} / 成本 ¥{PurchasedCostAmount:0.00}";

	public string DispensedSummary => $"{DispensedQty}{UnitName} / 销售 ¥{DispensedSalesAmount:0.00}";

	public string ReturnedSummary => $"{ReturnedQty}{UnitName} / 冲红 -¥{ReturnedSalesAmount:0.00}";

	public string NetSalesText => $"¥{NetSalesAmount:0.00}";

	public string GrossProfitText => $"¥{GrossProfitAmount:0.00}";

	public string CurrentStockSummary => $"{CurrentStockQty}{UnitName} (估值 ¥{CurrentStockRetailValue:0.00})";
}
