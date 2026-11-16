using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class DrugMasterItemModel : ClientModelBase
{
	private string _drugId = string.Empty;

	private string _drugCode = string.Empty;

	private string _drugName = string.Empty;

	private string _spec = string.Empty;

	private string _unitName = "盒";

	private decimal _retailPrice;

	private decimal _costPrice;

	private int _antibioticLevel;

	private bool _isNarcotic;

	private bool _isSkinTestRequired;

	private bool _isActive = true;

	private int _totalStockQuantity;

	public string DrugId
	{
		get
		{
			return _drugId;
		}
		set
		{
			SetField(ref _drugId, value, "DrugId");
		}
	}

	public string DrugCode
	{
		get
		{
			return _drugCode;
		}
		set
		{
			SetField(ref _drugCode, value, "DrugCode");
		}
	}

	public string DrugName
	{
		get
		{
			return _drugName;
		}
		set
		{
			SetField(ref _drugName, value, "DrugName");
		}
	}

	public string Spec
	{
		get
		{
			return _spec;
		}
		set
		{
			SetField(ref _spec, value, "Spec");
		}
	}

	public string UnitName
	{
		get
		{
			return _unitName;
		}
		set
		{
			SetField(ref _unitName, value, "UnitName");
		}
	}

	public decimal RetailPrice
	{
		get
		{
			return _retailPrice;
		}
		set
		{
			if (SetField(ref _retailPrice, value, "RetailPrice"))
			{
				OnPropertyChanged("RetailPriceText");
				OnPropertyChanged("TotalRetailValuationText");
				OnPropertyChanged("MarginRateText");
			}
		}
	}

	public decimal CostPrice
	{
		get
		{
			return _costPrice;
		}
		set
		{
			if (SetField(ref _costPrice, value, "CostPrice"))
			{
				OnPropertyChanged("CostPriceText");
				OnPropertyChanged("TotalCostValuationText");
				OnPropertyChanged("MarginRateText");
			}
		}
	}

	public int AntibioticLevel
	{
		get
		{
			return _antibioticLevel;
		}
		set
		{
			if (SetField(ref _antibioticLevel, value, "AntibioticLevel"))
			{
				OnPropertyChanged("AntibioticBadge");
			}
		}
	}

	public bool IsNarcotic
	{
		get
		{
			return _isNarcotic;
		}
		set
		{
			if (SetField(ref _isNarcotic, value, "IsNarcotic"))
			{
				OnPropertyChanged("ControlTagsText");
			}
		}
	}

	public bool IsSkinTestRequired
	{
		get
		{
			return _isSkinTestRequired;
		}
		set
		{
			if (SetField(ref _isSkinTestRequired, value, "IsSkinTestRequired"))
			{
				OnPropertyChanged("ControlTagsText");
			}
		}
	}

	public bool IsActive
	{
		get
		{
			return _isActive;
		}
		set
		{
			if (SetField(ref _isActive, value, "IsActive"))
			{
				OnPropertyChanged("StatusText");
				OnPropertyChanged("StatusBrush");
			}
		}
	}

	public int TotalStockQuantity
	{
		get
		{
			return _totalStockQuantity;
		}
		set
		{
			if (SetField(ref _totalStockQuantity, value, "TotalStockQuantity"))
			{
				OnPropertyChanged("TotalStockText");
				OnPropertyChanged("TotalRetailValuationText");
				OnPropertyChanged("TotalCostValuationText");
			}
		}
	}

	public string RetailPriceText => $"¥{RetailPrice:0.00}";

	public string CostPriceText => $"¥{CostPrice:0.00}";

	public string TotalStockText => $"{TotalStockQuantity} {UnitName}";

	public string TotalRetailValuationText => $"¥{(decimal)TotalStockQuantity * RetailPrice:0.00}";

	public string TotalCostValuationText => $"¥{(decimal)TotalStockQuantity * CostPrice:0.00}";

	public string MarginRateText => (RetailPrice > 0m) ? $"差价 ¥{RetailPrice - CostPrice:0.00}" : "零差率";

	public string AntibioticBadge
	{
		get
		{
			int antibioticLevel = AntibioticLevel;
			if (1 == 0)
			{
			}
			string result = antibioticLevel switch
			{
				2 => "特殊级抗菌药", 
				1 => "限制级抗菌药", 
				_ => "普通非抗菌药", 
			};
			if (1 == 0)
			{
			}
			return result;
		}
	}

	public string ControlTagsText => AntibioticBadge + " · " + (IsSkinTestRequired ? "强制皮试" : "免皮试") + (IsNarcotic ? " · 麻精专管" : "");

	public string StatusText => IsActive ? "在用" : "已停用";

	public string StatusBrush => IsActive ? "#059669" : "#DC2626";
}
