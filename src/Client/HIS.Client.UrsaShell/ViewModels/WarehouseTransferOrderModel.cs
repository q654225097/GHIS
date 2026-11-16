using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class WarehouseTransferOrderModel : ClientModelBase
{
	public string TransferNo { get; set; } = string.Empty;

	public string TransferType { get; set; } = "WAREHOUSE_ISSUE";

	public string SourceWarehouseId { get; set; } = string.Empty;

	public string SourceWarehouseName { get; set; } = string.Empty;

	public string TargetWarehouseId { get; set; } = string.Empty;

	public string TargetWarehouseName { get; set; } = string.Empty;

	public string DrugName { get; set; } = string.Empty;

	public string Spec { get; set; } = string.Empty;

	public string BatchNo { get; set; } = string.Empty;

	public int ExpiryDateInt { get; set; }

	public int Quantity { get; set; }

	public string UnitName { get; set; } = "盒";

	public decimal TotalRetailAmount { get; set; }

	public string OperatorName { get; set; } = string.Empty;

	public string TransferTime { get; set; } = string.Empty;

	public string StatusText { get; set; } = "✓ 已出库并验收入账";

	public string Remark { get; set; } = string.Empty;

	public string TypeBadgeText => (TransferType == "WAREHOUSE_ISSUE") ? "\ud83d\udce6 药库向药房发药 (WAREHOUSE_ISSUE)" : "\ud83d\udd04 多库房跨库调拨 (INTER_WAREHOUSE)";

	public string TypeBrush => (TransferType == "WAREHOUSE_ISSUE") ? "#1565C0" : "#059669";

	public string RouteSummary => SourceWarehouseName + "  ➔  " + TargetWarehouseName;

	public string QuantityAndAmountText => $"{Quantity}{UnitName} (批号: {BatchNo} · 零售额 ¥{TotalRetailAmount:0.00})";
}
