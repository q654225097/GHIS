using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class PharmacyDispenseModel : ClientModelBase
{
	private string _dispenseStatus = string.Empty;

	private string _paymentNoticeStatus = string.Empty;

	private string _pharmacistName = string.Empty;

	private string _safetyAudit = string.Empty;

	private bool _isAllergyBlocked;

	private bool _isPaidNotified = true;

	private bool _isOrderClosed;

	private bool _isReturned;

	private bool _canDispense;

	private bool _isFinancialRefunded;

	public string PrescriptionNo { get; set; } = string.Empty;

	public string PatientName { get; set; } = string.Empty;

	public string DrugId { get; set; } = "DRUG-03";

	public int DispenseQuantity { get; set; } = 2;

	public decimal TotalAmount { get; set; } = 45.00m;

	public string DrugSummary { get; set; } = string.Empty;

	public string DispenseStatus
	{
		get
		{
			return _dispenseStatus;
		}
		set
		{
			if (SetField(ref _dispenseStatus, value, "DispenseStatus"))
			{
				OnPropertyChanged("StatusBrush");
			}
		}
	}

	public string PaymentNoticeStatus
	{
		get
		{
			return _paymentNoticeStatus;
		}
		set
		{
			SetField(ref _paymentNoticeStatus, value, "PaymentNoticeStatus");
		}
	}

	public string PharmacistName
	{
		get
		{
			return _pharmacistName;
		}
		set
		{
			SetField(ref _pharmacistName, value, "PharmacistName");
		}
	}

	public string SafetyAudit
	{
		get
		{
			return _safetyAudit;
		}
		set
		{
			SetField(ref _safetyAudit, value, "SafetyAudit");
		}
	}

	public bool IsAllergyBlocked
	{
		get
		{
			return _isAllergyBlocked;
		}
		set
		{
			if (SetField(ref _isAllergyBlocked, value, "IsAllergyBlocked"))
			{
				NotifyStateMachine();
			}
		}
	}

	public bool IsPaidNotified
	{
		get
		{
			return _isPaidNotified;
		}
		set
		{
			if (SetField(ref _isPaidNotified, value, "IsPaidNotified"))
			{
				NotifyStateMachine();
			}
		}
	}

	public bool IsOrderClosed
	{
		get
		{
			return _isOrderClosed;
		}
		set
		{
			if (SetField(ref _isOrderClosed, value, "IsOrderClosed"))
			{
				NotifyStateMachine();
			}
		}
	}

	public bool IsReturned
	{
		get
		{
			return _isReturned;
		}
		set
		{
			if (SetField(ref _isReturned, value, "IsReturned"))
			{
				NotifyStateMachine();
			}
		}
	}

	public bool IsFinancialRefunded
	{
		get
		{
			return _isFinancialRefunded;
		}
		set
		{
			if (SetField(ref _isFinancialRefunded, value, "IsFinancialRefunded"))
			{
				NotifyStateMachine();
			}
		}
	}

	public bool CanDispense
	{
		get
		{
			return IsPaidNotified && !IsOrderClosed && !IsReturned && !IsAllergyBlocked;
		}
		set
		{
			_canDispense = value;
			NotifyStateMachine();
		}
	}

	public bool NeedsCashierSettlement => !IsPaidNotified && !IsOrderClosed && !IsReturned;

	public bool CanReturnDrug => IsOrderClosed && !IsReturned && !IsFinancialRefunded;

	public bool CanExecuteFinancialRefund => IsReturned && !IsFinancialRefunded;

	public string DispenseButtonText
	{
		get
		{
			string result;
			if (IsFinancialRefunded)
			{
				result = "✓ 已退药退费闭环";
			}
			else if (IsReturned)
			{
				result = "已退药(待步骤E退费)";
			}
			else if (IsOrderClosed)
			{
				result = "\ud83d\udd12 步骤C已完成: 已关单";
			}
			else if (IsPaidNotified)
			{
				result = (IsAllergyBlocked ? "\ud83d\udee1\ufe0f 用药红线阻断" : "\ud83d\udce6 待扫码核验发药");
			}
			else
			{
				result = "\ud83d\udd12 需先完成步骤B收费";
			}
			return result;
		}
	}

	public string StatusBrush
	{
		get
		{
			string result;
			if (IsFinancialRefunded)
			{
				result = "#059669";
			}
			else if (IsReturned)
			{
				result = "#2563EB";
			}
			else if (IsOrderClosed)
			{
				result = "#059669";
			}
			else if (IsPaidNotified)
			{
				result = (IsAllergyBlocked ? "#DC2626" : "#1565C0");
			}
			else
			{
				result = "#D97706";
			}
			return result;
		}
	}

	private void NotifyStateMachine()
	{
		OnPropertyChanged("NeedsCashierSettlement");
		OnPropertyChanged("CanDispense");
		OnPropertyChanged("CanReturnDrug");
		OnPropertyChanged("CanExecuteFinancialRefund");
		OnPropertyChanged("DispenseButtonText");
		OnPropertyChanged("StatusBrush");
	}
}
