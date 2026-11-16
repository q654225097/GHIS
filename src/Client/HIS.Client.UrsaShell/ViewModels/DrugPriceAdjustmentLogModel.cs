using System;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class DrugPriceAdjustmentLogModel : ClientModelBase
{
	public string AdjustNo { get; set; } = string.Empty;

	public string DrugCode { get; set; } = string.Empty;

	public string DrugName { get; set; } = string.Empty;

	public decimal OldRetailPrice { get; set; }

	public decimal NewRetailPrice { get; set; }

	public decimal OldCostPrice { get; set; }

	public decimal NewCostPrice { get; set; }

	public int AffectedStockQuantity { get; set; }

	public string UnitName { get; set; } = "盒";

	public decimal PriceDiffAmount { get; set; }

	public string AdjustReason { get; set; } = string.Empty;

	public string EffectiveTime { get; set; } = string.Empty;

	public string OperatorName { get; set; } = "物价管理员";

	public string PriceChangeSummary => $"零售价: ¥{OldRetailPrice:0.00} → ¥{NewRetailPrice:0.00}  |  成本价: ¥{OldCostPrice:0.00} → ¥{NewCostPrice:0.00}";

	public string StockImpactSummary => $"涉及全院在库: {AffectedStockQuantity}{UnitName}  |  库存调价损益: {((PriceDiffAmount >= 0m) ? $"+¥{PriceDiffAmount:0.00}" : $"-¥{Math.Abs(PriceDiffAmount):0.00}")}";

	public string DiffBrush => (PriceDiffAmount >= 0m) ? "#059669" : "#D97706";
}
