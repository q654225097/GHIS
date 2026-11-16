using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class NursingTaskModel : ClientModelBase
{
	private string _status = "待执行";

	private string _executedAt = "--";

	private string _scheduledDate = string.Empty;

	private string _timePoint = string.Empty;

	private string _frequencySnapshot = string.Empty;

	public string TaskNo { get; set; } = string.Empty;

	public string ScheduledTime { get; set; } = string.Empty;

	public string ScheduledDate
	{
		get
		{
			return _scheduledDate;
		}
		set
		{
			SetField(ref _scheduledDate, value, "ScheduledDate");
		}
	}

	public string TimePoint
	{
		get
		{
			return _timePoint;
		}
		set
		{
			if (SetField(ref _timePoint, value, "TimePoint"))
			{
				OnPropertyChanged("TimePointDisplay");
			}
		}
	}

	public string FrequencySnapshot
	{
		get
		{
			return _frequencySnapshot;
		}
		set
		{
			SetField(ref _frequencySnapshot, value, "FrequencySnapshot");
		}
	}

	public string Category { get; set; } = "护理";

	public string Content { get; set; } = string.Empty;

	public string SourceOrder { get; set; } = string.Empty;

	public string NurseName { get; set; } = string.Empty;

	public string Priority { get; set; } = "常规";

	public bool IsDecomposedTask { get; set; }

	public string TimePointDisplay => (!string.IsNullOrWhiteSpace(TimePoint)) ? TimePoint : ScheduledTime;

	public string CategoryClass
	{
		get
		{
			string category = Category;
			if (1 == 0)
			{
			}
			string result = category switch
			{
				"给药" => "Primary", 
				"监护" => "Danger", 
				"护理" => "Success", 
				"检验" => "Warning", 
				_ => "Secondary", 
			};
			if (1 == 0)
			{
			}
			return result;
		}
	}

	public string CategoryBrush
	{
		get
		{
			string category = Category;
			if (1 == 0)
			{
			}
			string result = category switch
			{
				"给药" => "#2563EB", 
				"监护" => "#DC2626", 
				"护理" => "#059669", 
				"检验" => "#D97706", 
				_ => "#64748B", 
			};
			if (1 == 0)
			{
			}
			return result;
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
				OnPropertyChanged("IsPending");
				OnPropertyChanged("CanExecute");
				OnPropertyChanged("CanVerify");
				OnPropertyChanged("IsVerified");
				OnPropertyChanged("StepStatusText");
			}
		}
	}

	public string ExecutedAt
	{
		get
		{
			return _executedAt;
		}
		set
		{
			SetField(ref _executedAt, value, "ExecutedAt");
		}
	}

	public string StatusClass
	{
		get
		{
			string status = Status;
			if (1 == 0)
			{
			}
			string result = status switch
			{
				"待执行" => "Warning", 
				"已执行" => "Primary", 
				"已核对" => "Success", 
				"已退回" => "Danger", 
				_ => "Secondary", 
			};
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
			string result = status switch
			{
				"待执行" => "#D97706", 
				"已执行" => "#2563EB", 
				"已核对" => "#059669", 
				"已退回" => "#DC2626", 
				_ => "#64748B", 
			};
			if (1 == 0)
			{
			}
			return result;
		}
	}

	public string PriorityClass
	{
		get
		{
			string result;
			if (Priority == "高")
			{
				result = "Danger";
			}
			else
			{
				result = ((Priority == "中") ? "Warning" : "Secondary");
			}
			return result;
		}
	}

	public string PriorityBrush
	{
		get
		{
			string result;
			if (Priority == "高")
			{
				result = "#DC2626";
			}
			else
			{
				result = ((Priority == "中") ? "#D97706" : "#64748B");
			}
			return result;
		}
	}

	public bool IsPending => Status == "待执行";

	public bool CanExecute => IsPending;

	public bool CanVerify => Status == "已执行";

	public bool IsVerified => Status == "已核对";

	public string StepStatusText
	{
		get
		{
			string status = Status;
			if (1 == 0)
			{
			}
			string result = status switch
			{
				"待执行" => "步骤D: 待护士扫码执行", 
				"已执行" => "步骤E: 已执行·待双人核对", 
				"已核对" => "✓ 已完成执行与双人核对闭环", 
				_ => Status, 
			};
			if (1 == 0)
			{
			}
			return result;
		}
	}
}
