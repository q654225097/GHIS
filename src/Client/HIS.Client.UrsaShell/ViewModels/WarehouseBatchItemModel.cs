using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class WarehouseBatchItemModel : ClientModelBase
{
	private int _quantity;

	private bool _isFrozen;

	public string BatchId { get; set; } = string.Empty;

	public string WarehouseId { get; set; } = string.Empty;

	public string WarehouseName { get; set; } = string.Empty;

	public string WarehouseCategory { get; set; } = "PHARMACY";

	public string DrugId { get; set; } = string.Empty;

	public string DrugCode { get; set; } = string.Empty;

	public string DrugName { get; set; } = string.Empty;

	public string Spec { get; set; } = string.Empty;

	public string UnitName { get; set; } = "盒";

	public string BatchNo { get; set; } = string.Empty;

	public int ExpiryDateInt { get; set; } = 20280601;

	public string InvoiceNo { get; set; } = string.Empty;

	public string ColdChainInfo { get; set; } = "常温合格";

	public decimal UnitCostPrice { get; set; }

	public decimal UnitRetailPrice { get; set; }

	public int Quantity
	{
		get
		{
			return _quantity;
		}
		set
		{
			if (SetField(ref _quantity, value, "Quantity"))
			{
				OnPropertyChanged("QuantityText");
				OnPropertyChanged("RetailValuationText");
				OnPropertyChanged("CostValuationText");
			}
		}
	}

	public bool IsFrozen
	{
		get
		{
			return _isFrozen;
		}
		set
		{
			if (SetField(ref _isFrozen, value, "IsFrozen"))
			{
				OnPropertyChanged("StatusBadge");
				OnPropertyChanged("StatusBrush");
				OnPropertyChanged("FreezeActionText");
			}
		}
	}

	public string QuantityText => $"{Quantity} {UnitName}";

	public string ExpiryText => (ExpiryDateInt.ToString().Length == 8) ? $"{ExpiryDateInt.ToString().Substring(0, 4)}-{ExpiryDateInt.ToString().Substring(4, 2)}-{ExpiryDateInt.ToString().Substring(6, 2)}" : ExpiryDateInt.ToString();

	public string RetailValuationText => $"零售估值: ¥{(decimal)Quantity * UnitRetailPrice:0.00}";

	public string CostValuationText => $"成本估值: ¥{(decimal)Quantity * UnitCostPrice:0.00}";

	public bool IsNearExpiry => ExpiryDateInt <= 20270501;

	public string StatusBadge
	{
		get
		{
			string result;
			if (IsFrozen)
			{
				result = "\ud83d\udd12 已冻结锁定";
			}
			else
			{
				result = (IsNearExpiry ? "⚠\ufe0f 近效期优先(FEFO)" : "✓ 合格可用");
			}
			return result;
		}
	}

	public string StatusBrush
	{
		get
		{
			string result;
			if (IsFrozen)
			{
				result = "#DC2626";
			}
			else
			{
				result = (IsNearExpiry ? "#D97706" : "#059669");
			}
			return result;
		}
	}

	public string FreezeActionText => IsFrozen ? "解冻批次" : "冻结批次";
}
