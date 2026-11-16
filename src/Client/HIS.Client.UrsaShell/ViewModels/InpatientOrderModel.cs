using System.Linq;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class InpatientOrderModel : ClientModelBase
{
	private string _status = "待护士校对";

	private string _stopTime = "--";

	private string _stopDoctor = "--";

	private string _cancelTime = "--";

	private string _cancelDoctor = "--";

	private string _nurseSign = "--";

	public string OrderNo { get; set; } = string.Empty;

	public string OrderType { get; set; } = "长期医嘱";

	public string Content { get; set; } = string.Empty;

	public string Dose { get; set; } = string.Empty;

	public string Route { get; set; } = string.Empty;

	public string Frequency { get; set; } = string.Empty;

	public string StartTime { get; set; } = string.Empty;

	public string DoctorName { get; set; } = string.Empty;

	public string ReviewInfo { get; set; } = string.Empty;

	public string StopTime
	{
		get
		{
			return _stopTime;
		}
		set
		{
			SetField(ref _stopTime, value, "StopTime");
		}
	}

	public string StopDoctor
	{
		get
		{
			return _stopDoctor;
		}
		set
		{
			SetField(ref _stopDoctor, value, "StopDoctor");
		}
	}

	public string CancelTime
	{
		get
		{
			return _cancelTime;
		}
		set
		{
			SetField(ref _cancelTime, value, "CancelTime");
		}
	}

	public string CancelDoctor
	{
		get
		{
			return _cancelDoctor;
		}
		set
		{
			SetField(ref _cancelDoctor, value, "CancelDoctor");
		}
	}

	public string NurseSign
	{
		get
		{
			return _nurseSign;
		}
		set
		{
			SetField(ref _nurseSign, value, "NurseSign");
		}
	}

	public string Status
	{
		get
		{
			return _status;
		}
		set
		{
			if (SetField(ref _status, value, "Status"))
			{
				OnPropertyChanged("StatusClass");
				OnPropertyChanged("StatusBrush");
				OnPropertyChanged("IsPendingReview");
				OnPropertyChanged("IsReviewedWaitingDecompose");
				OnPropertyChanged("IsExecuting");
				OnPropertyChanged("CanStop");
				OnPropertyChanged("CanCancel");
				OnPropertyChanged("IsStopped");
				OnPropertyChanged("IsCancelled");
				OnPropertyChanged("LifecycleStepBadge");
			}
		}
	}

	public string TypeClass => (OrderType == "长期医嘱") ? "Primary" : "Warning";

	public string StatusClass
	{
		get
		{
			string status = Status;
			if (1 == 0)
			{
			}
			string result;
			switch (status)
			{
			case "待主治审核":
			case "待护士校对":
			case "待审核":
				result = "Warning";
				break;
			case "已审核待执行":
			case "已校对待执行":
			case "已校对待分解":
				result = "Primary";
				break;
			case "执行中":
				result = "Success";
				break;
			case "已停止":
				result = "Secondary";
				break;
			case "已作废":
				result = "Danger";
				break;
			case "已驳回":
				result = "Danger";
				break;
			default:
				result = "Secondary";
				break;
			}
			if (1 == 0)
			{
			}
			return result;
		}
	}

	public string StatusBrush
	{
		get
		{
			string status = Status;
			if (1 == 0)
			{
			}
			string result;
			switch (status)
			{
			case "待主治审核":
			case "待护士校对":
			case "待审核":
				result = "#D97706";
				break;
			case "已审核待执行":
			case "已校对待执行":
			case "已校对待分解":
				result = "#2563EB";
				break;
			case "执行中":
				result = "#059669";
				break;
			case "已停止":
				result = "#64748B";
				break;
			case "已作废":
				result = "#DC2626";
				break;
			case "已驳回":
				result = "#DC2626";
				break;
			default:
				result = "#64748B";
				break;
			}
			if (1 == 0)
			{
			}
			return result;
		}
	}

	public bool IsPendingReview
	{
		get
		{
			switch (Status)
			{
			case "待主治审核":
			case "待护士校对":
			case "待审核":
				return true;
			default:
				return false;
			}
		}
	}

	public bool IsReviewedWaitingDecompose
	{
		get
		{
			switch (Status)
			{
			case "已校对待分解":
			case "已校对待执行":
			case "已审核待执行":
				return true;
			default:
				return false;
			}
		}
	}

	public bool IsExecuting => Status == "执行中";

	public bool IsActive
	{
		get
		{
			string status = Status;
			return !(status == "已停止") && !(status == "已驳回") && !(status == "已作废");
		}
	}

	public bool CanStop
	{
		get
		{
			switch (Status)
			{
			case "执行中":
			case "已校对待分解":
			case "已校对待执行":
			case "已审核待执行":
				return true;
			default:
				return false;
			}
		}
	}

	public bool CanCancel => IsPendingReview;

	public bool IsStopped => Status == "已停止";

	public bool IsCancelled => Status == "已作废";

	public string LifecycleStepBadge
	{
		get
		{
			string status = Status;
			if (1 == 0)
			{
			}
			string result;
			switch (status)
			{
			case "待主治审核":
			case "待护士校对":
			case "待审核":
				result = "步骤A: 待护士校对";
				break;
			case "已校对待分解":
			case "已校对待执行":
			case "已审核待执行":
				result = "步骤B: 已校对·待分解";
				break;
			case "执行中":
				result = "步骤C: 已分解执行中";
				break;
			case "已停止":
				result = "已停止闭环";
				break;
			case "已作废":
				result = "已作废";
				break;
			default:
				result = Status;
				break;
			}
			if (1 == 0)
			{
			}
			return result;
		}
	}

	public string OrderSummary => string.Join(" · ", new string[3] { Dose, Frequency, Route }.Where((string x) => !string.IsNullOrWhiteSpace(x)));

	public string DisplaySummary => string.IsNullOrWhiteSpace(OrderSummary) ? Content : (Content + " · " + OrderSummary);
}
