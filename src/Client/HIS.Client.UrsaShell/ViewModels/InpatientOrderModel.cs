using System;
using System.Linq;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public partial class InpatientOrderModel : ClientModelBase
{
	private string _status = "待护士校对";
	private string _stopTime = "--";
	private string _stopDoctor = "--";
	private string _cancelTime = "--";
	private string _cancelDoctor = "--";
	private string _nurseSign = "--";
	private bool _isSelected;
	private bool _isUrgent;
	private string _itemCategory = "全部";
	private string _groupNo = string.Empty;
	private string _groupIcon = "-";
	private string _spec = string.Empty;
	private string _singleDose = "1";
	private string _doseUnit = "片";
	private string _usageAmount = "1";
	private string _usageUnit = "次";
	private int _days = 1;
	private int _packageQuantity = 1;
	private int _quantity = 1;
	private string _unit = "盒";
	private string _dripRate = "--";
	private string _execDept = "普外二病区护理站";
	private string _unitPrice = "¥0.00";
	private string _totalPrice = "¥0.00";
	private string _remark = string.Empty;
	private string _specialAttr = "常规";
	private string _startExecTime = "--";
	private string _stopReason = "--";
	private string _specimen = "--";
	private string _bodyPart = "--";
	private string _examMethod = "--";
	private string _skinTestInfo = "免试";
	private string _groupComboNo = string.Empty;
	private int _seqNo = 1;

	private string _singleDoseEquiv = "0.000";
	private string _singleDoseEquivUnit = "g";
	private int _hospitalInjectionCount = 0;
	private string _deductDept = "西药库";
	private string _inputOperator = "当前医生";
	private string _orderDept = "普外二病区";
	private int _prescriptionPacks = 1;
	private string _alertStatus = "✓";
	private string _alertTooltip = "审方合规·无配伍禁忌";
	private string _alertColor = "#10B981";
	private string _approvalStatus = "APPROVED";
	private string _workflowInstanceId = string.Empty;

	public string OrderNo { get; set; } = string.Empty;

	public string OrderType { get; set; } = "长期医嘱";

	public string Content { get; set; } = string.Empty;

	public string Dose { get; set; } = string.Empty;

	public string Route { get; set; } = string.Empty;

	public string Frequency { get; set; } = string.Empty;

	public string StartTime { get; set; } = string.Empty;

	public string DoctorName { get; set; } = string.Empty;

	public string ReviewInfo { get; set; } = string.Empty;

	public bool IsSelected
	{
		get => _isSelected;
		set => SetField(ref _isSelected, value, nameof(IsSelected));
	}

	public bool IsUrgent
	{
		get => _isUrgent;
		set
		{
			if (SetField(ref _isUrgent, value, nameof(IsUrgent)))
			{
				OnPropertyChanged(nameof(IsUrgentText));
			}
		}
	}

	public string IsUrgentText => IsUrgent ? "加急" : "常规";

	public string ItemCategory
	{
		get => _itemCategory;
		set => SetField(ref _itemCategory, value, nameof(ItemCategory));
	}

	public string GroupNo
	{
		get => _groupNo;
		set
		{
			if (SetField(ref _groupNo, value, nameof(GroupNo)))
			{
				OnPropertyChanged(nameof(GroupDisplay));
			}
		}
	}

	public string GroupIcon
	{
		get => _groupIcon;
		set
		{
			if (SetField(ref _groupIcon, value, nameof(GroupIcon)))
			{
				OnPropertyChanged(nameof(GroupDisplay));
			}
		}
	}

	public string GroupDisplay => !string.IsNullOrEmpty(GroupIcon) && GroupIcon != "-" ? $"{GroupIcon} 组{GroupNo}" : (string.IsNullOrEmpty(GroupNo) || GroupNo == "0" ? "-" : $"组{GroupNo}");

	public string GroupBracket => !string.IsNullOrEmpty(GroupIcon) && GroupIcon != "-" ? GroupIcon : " ";

	public static void RefreshGroupSymbols(System.Collections.Generic.IEnumerable<InpatientOrderModel> orders)
	{
		var list = orders.ToList();
		var grouped = list.Where(o => !string.IsNullOrWhiteSpace(o.GroupNo) && o.GroupNo != "-" && o.GroupNo != "0")
						  .GroupBy(o => (o.OrderType, o.GroupNo));

		foreach (var o in list)
		{
			if (string.IsNullOrWhiteSpace(o.GroupNo) || o.GroupNo == "-" || o.GroupNo == "0")
			{
				o.GroupIcon = "-";
			}
		}

		foreach (var grp in grouped)
		{
			var items = grp.ToList();
			if (items.Count == 1)
			{
				items[0].GroupIcon = "-";
			}
			else
			{
				for (int i = 0; i < items.Count; i++)
				{
					if (i == 0)
						items[i].GroupIcon = "┌";
					else if (i == items.Count - 1)
						items[i].GroupIcon = "└";
					else
						items[i].GroupIcon = "├";
				}
			}
		}
	}

	public string Spec
	{
		get => _spec;
		set => SetField(ref _spec, value, nameof(Spec));
	}

	public string SingleDose
	{
		get => !string.IsNullOrWhiteSpace(_singleDose) ? _singleDose : Dose;
		set => SetField(ref _singleDose, value, nameof(SingleDose));
	}

	public string DoseUnit
	{
		get => _doseUnit;
		set => SetField(ref _doseUnit, value, nameof(DoseUnit));
	}

	public string UsageAmount
	{
		get => _usageAmount;
		set => SetField(ref _usageAmount, value, nameof(UsageAmount));
	}

	public string UsageUnit
	{
		get => _usageUnit;
		set => SetField(ref _usageUnit, value, nameof(UsageUnit));
	}

	public int Days
	{
		get => _days;
		set => SetField(ref _days, value, nameof(Days));
	}

	public int PackageQuantity
	{
		get => _packageQuantity;
		set => SetField(ref _packageQuantity, value, nameof(PackageQuantity));
	}

	public int Quantity
	{
		get => _quantity;
		set => SetField(ref _quantity, value, nameof(Quantity));
	}

	public string Unit
	{
		get => _unit;
		set => SetField(ref _unit, value, nameof(Unit));
	}

	public string DripRate
	{
		get => _dripRate;
		set => SetField(ref _dripRate, value, nameof(DripRate));
	}

	public string ExecDept
	{
		get => _execDept;
		set => SetField(ref _execDept, value, nameof(ExecDept));
	}

	public string UnitPrice
	{
		get => _unitPrice;
		set => SetField(ref _unitPrice, value, nameof(UnitPrice));
	}

	public string TotalPrice
	{
		get => _totalPrice;
		set => SetField(ref _totalPrice, value, nameof(TotalPrice));
	}

	public string Remark
	{
		get => _remark;
		set => SetField(ref _remark, value, nameof(Remark));
	}

	public string SpecialAttr
	{
		get => _specialAttr;
		set => SetField(ref _specialAttr, value, nameof(SpecialAttr));
	}

	public string StartExecTime
	{
		get => !string.IsNullOrWhiteSpace(_startExecTime) && _startExecTime != "--" ? _startExecTime : StartTime;
		set => SetField(ref _startExecTime, value, nameof(StartExecTime));
	}

	public string StopReason
	{
		get => _stopReason;
		set => SetField(ref _stopReason, value, nameof(StopReason));
	}

	public string Specimen
	{
		get => _specimen;
		set => SetField(ref _specimen, value, nameof(Specimen));
	}

	public string BodyPart
	{
		get => _bodyPart;
		set => SetField(ref _bodyPart, value, nameof(BodyPart));
	}

	public string SpecimenBodyPart
	{
		get
		{
			if (!string.IsNullOrEmpty(Specimen) && Specimen != "--" && !string.IsNullOrEmpty(BodyPart) && BodyPart != "--")
				return $"{Specimen}/{BodyPart}";
			if (!string.IsNullOrEmpty(Specimen) && Specimen != "--")
				return Specimen;
			if (!string.IsNullOrEmpty(BodyPart) && BodyPart != "--")
				return BodyPart;
			return "--";
		}
	}

	public string ExamMethod
	{
		get => _examMethod;
		set => SetField(ref _examMethod, value, nameof(ExamMethod));
	}

	public string SkinTestInfo
	{
		get => _skinTestInfo;
		set => SetField(ref _skinTestInfo, value, nameof(SkinTestInfo));
	}

	public string GroupComboNo
	{
		get => _groupComboNo;
		set => SetField(ref _groupComboNo, value, nameof(GroupComboNo));
	}

	public int SeqNo
	{
		get => _seqNo;
		set => SetField(ref _seqNo, value, nameof(SeqNo));
	}

	public string SingleDoseEquiv
	{
		get => _singleDoseEquiv;
		set => SetField(ref _singleDoseEquiv, value, nameof(SingleDoseEquiv));
	}

	public string SingleDoseEquivUnit
	{
		get => _singleDoseEquivUnit;
		set => SetField(ref _singleDoseEquivUnit, value, nameof(SingleDoseEquivUnit));
	}

	public int HospitalInjectionCount
	{
		get => _hospitalInjectionCount;
		set => SetField(ref _hospitalInjectionCount, value, nameof(HospitalInjectionCount));
	}

	public string DeductDept
	{
		get => _deductDept;
		set => SetField(ref _deductDept, value, nameof(DeductDept));
	}

	public string InputOperator
	{
		get => _inputOperator;
		set => SetField(ref _inputOperator, value, nameof(InputOperator));
	}

	public string OrderDept
	{
		get => _orderDept;
		set => SetField(ref _orderDept, value, nameof(OrderDept));
	}

	public int PrescriptionPacks
	{
		get => _prescriptionPacks;
		set => SetField(ref _prescriptionPacks, value, nameof(PrescriptionPacks));
	}

	public string AlertStatus
	{
		get => _alertStatus;
		set => SetField(ref _alertStatus, value, nameof(AlertStatus));
	}

	public string AlertTooltip
	{
		get => _alertTooltip;
		set => SetField(ref _alertTooltip, value, nameof(AlertTooltip));
	}

	public string AlertColor
	{
		get => _alertColor;
		set => SetField(ref _alertColor, value, nameof(AlertColor));
	}

	public string ApprovalStatus
	{
		get => _approvalStatus;
		set => SetField(ref _approvalStatus, value, nameof(ApprovalStatus));
	}

	public string WorkflowInstanceId
	{
		get => _workflowInstanceId;
		set => SetField(ref _workflowInstanceId, value, nameof(WorkflowInstanceId));
	}

	public string StopTime
	{
		get => _stopTime;
		set => SetField(ref _stopTime, value, nameof(StopTime));
	}

	public string StopDoctor
	{
		get => _stopDoctor;
		set => SetField(ref _stopDoctor, value, nameof(StopDoctor));
	}

	public string CancelTime
	{
		get => _cancelTime;
		set => SetField(ref _cancelTime, value, nameof(CancelTime));
	}

	public string CancelDoctor
	{
		get => _cancelDoctor;
		set => SetField(ref _cancelDoctor, value, nameof(CancelDoctor));
	}

	public string NurseSign
	{
		get => _nurseSign;
		set => SetField(ref _nurseSign, value, nameof(NurseSign));
	}

	public string Status
	{
		get => _status;
		set
		{
			if (SetField(ref _status, value, nameof(Status)))
			{
				OnPropertyChanged(nameof(StatusClass));
				OnPropertyChanged(nameof(StatusBrush));
				OnPropertyChanged(nameof(LegendBrush));
				OnPropertyChanged(nameof(IsPendingReview));
				OnPropertyChanged(nameof(IsReviewedWaitingDecompose));
				OnPropertyChanged(nameof(IsExecuting));
				OnPropertyChanged(nameof(CanStop));
				OnPropertyChanged(nameof(CanCancel));
				OnPropertyChanged(nameof(IsStopped));
				OnPropertyChanged(nameof(IsCancelled));
				OnPropertyChanged(nameof(LifecycleStepBadge));
			}
		}
	}

	public string TypeClass => (OrderType == "长期医嘱") ? "Primary" : "Warning";

	public string StatusClass
	{
		get
		{
			string status = Status;
			return status switch
			{
				"待主治审核" or "待护士校对" or "待审核" => "Warning",
				"已审核待执行" or "已校对待执行" or "已校对待分解" => "Primary",
				"执行中" => "Success",
				"已停止" => "Secondary",
				"已作废" or "已驳回" => "Danger",
				_ => "Secondary"
			};
		}
	}

	public string StatusBrush
	{
		get
		{
			string status = Status;
			return status switch
			{
				"待主治审核" or "待护士校对" or "待审核" => "#D97706",
				"已审核待执行" or "已校对待执行" or "已校对待分解" => "#2563EB",
				"执行中" => "#059669",
				"已停止" => "#64748B",
				"已作废" or "已驳回" => "#DC2626",
				_ => "#64748B"
			};
		}
	}

	/// <summary>
	/// 对应住院医嘱截图右上角图例: 绿色: 新开, 蓝色: 审核, 黄色: 执行, 红色: 停止
	/// </summary>
	public string LegendBrush
	{
		get
		{
			string status = Status;
			if (status.Contains("新开") || status.Contains("待校对") || status.Contains("待审核") || status.Contains("待主治"))
			{
				return "#10B981"; // 绿色: 新开
			}
			if (status.Contains("已审核") || status.Contains("已校对"))
			{
				return "#2563EB"; // 蓝色: 审核
			}
			if (status.Contains("执行"))
			{
				return "#D97706"; // 黄色: 执行
			}
			if (status.Contains("停止") || status.Contains("作废") || status.Contains("驳回"))
			{
				return "#DC2626"; // 红色: 停止
			}
			return "#10B981";
		}
	}

	/// <summary>
	/// 对应住院医嘱截图表格行首状态简字: 新 (绿色), 审 (蓝色), 执 (黄色), 停 (红色)
	/// </summary>
	public string StatusLegendInitial
	{
		get
		{
			string status = Status;
			if (status.Contains("新开") || status.Contains("待校对") || status.Contains("待审核") || status.Contains("待主治"))
			{
				return "新";
			}
			if (status.Contains("已审核") || status.Contains("已校对"))
			{
				return "审";
			}
			if (status.Contains("执行"))
			{
				return "执";
			}
			if (status.Contains("停止") || status.Contains("作废") || status.Contains("驳回"))
			{
				return "停";
			}
			return "新";
		}
	}

	public bool IsPendingReview => Status is "待主治审核" or "待护士校对" or "待审核";

	public bool IsReviewedWaitingDecompose => Status is "已校对待分解" or "已校对待执行" or "已审核待执行";

	public bool IsExecuting => Status == "执行中";

	public bool IsActive => Status != "已停止" && Status != "已驳回" && Status != "已作废";

	public bool CanStop => Status is "执行中" or "已校对待分解" or "已校对待执行" or "已审核待执行";

	public bool CanCancel => IsPendingReview;

	public bool IsStopped => Status == "已停止";

	public bool IsCancelled => Status == "已作废";

	public string LifecycleStepBadge => Status switch
	{
		"待主治审核" or "待护士校对" or "待审核" => "步骤A: 待护士校对",
		"已校对待分解" or "已校对待执行" or "已审核待执行" => "步骤B: 已校对·待分解",
		"执行中" => "步骤C: 已分解执行中",
		"已停止" => "已停止闭环",
		"已作废" => "已作废",
		_ => Status
	};

	public string OrderSummary => string.Join(" · ", new[] { Dose, Frequency, Route }.Where(x => !string.IsNullOrWhiteSpace(x)));

	public string DisplaySummary => string.IsNullOrWhiteSpace(OrderSummary) ? Content : ($"{Content} · {OrderSummary}");
}
