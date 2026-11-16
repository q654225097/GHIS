using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class MedTechOrderItemModel : ClientModelBase
{
	private string _status = string.Empty;

	private string _statusClass = "Primary";

	private bool _isFeeLocked;

	public string RequestNo { get; set; } = string.Empty;

	public string ItemName { get; set; } = string.Empty;

	public string DeptName { get; set; } = string.Empty;

	public string ExecLocation { get; set; } = string.Empty;

	public string Fee { get; set; } = string.Empty;

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
				OnPropertyChanged("CanPublishReport");
				OnPropertyChanged("CanCancelOrder");
			}
		}
	}

	public string StatusClass
	{
		get
		{
			return _statusClass;
		}
		set
		{
			SetField(ref _statusClass, value, "StatusClass");
		}
	}

	public bool IsFeeLocked
	{
		get
		{
			return _isFeeLocked;
		}
		set
		{
			if (SetField(ref _isFeeLocked, value, "IsFeeLocked"))
			{
				OnPropertyChanged("CanConfirmFee");
				OnPropertyChanged("CanPublishReport");
				OnPropertyChanged("CanCancelOrder");
				OnPropertyChanged("FeeLockBadge");
			}
		}
	}

	public bool CanConfirmFee => !IsFeeLocked && Status != "已出报告" && Status != "已撤单退回";

	public bool CanPublishReport => IsFeeLocked && Status != "已出报告" && Status != "已撤单退回";

	public bool CanCancelOrder => Status != "已出报告" && Status != "已撤单退回";

	public string FeeLockBadge
	{
		get
		{
			string result;
			if (Status == "已出报告")
			{
				result = "✓ 步骤C已完成: 报告已发布";
			}
			else
			{
				result = (IsFeeLocked ? "\ud83d\udd12 步骤B已完成: 已确费锁单(可发布报告)" : "⏳ 步骤A: 待扫码确费锁单");
			}
			return result;
		}
	}
}
