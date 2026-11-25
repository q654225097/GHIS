using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class PatientQueueItemModel : ClientModelBase
{
	private bool _isSelected;

	private bool _isEmrSigned;

	private bool _isOrdersPushed;

	private bool _isPrescriptionEditable;

	public string QueueNo { get; set; } = string.Empty;

	public string QueueId { get; set; } = string.Empty;

	public string PatientName { get; set; } = string.Empty;

	public string Gender { get; set; } = string.Empty;

	public int Age { get; set; }

	public string MedicalRecordNo { get; set; } = string.Empty;

	public string VisitNo { get; set; } = string.Empty;

	public string VisitId { get; set; } = string.Empty;

	public string PatientId { get; set; } = string.Empty;

	public string FeeType { get; set; } = string.Empty;

	public string OpdDeptId { get; set; } = string.Empty;

	public string OpdDeptName { get; set; } = string.Empty;

	public string DeptName => OpdDeptName;

	public string DoctorId { get; set; } = string.Empty;

	public string DoctorName { get; set; } = string.Empty;

	public string ClinicRoomId { get; set; } = string.Empty;

	public int QueueStatus { get; set; }

	public int VisitStatus { get; set; }

	public void SetStatus(int visitStatus, int? queueStatus = null)
	{
		VisitStatus = visitStatus;
		if (queueStatus.HasValue)
		{
			QueueStatus = queueStatus.Value;
		}
		OnPropertyChanged("Status");
		OnPropertyChanged("StatusClass");
		OnPropertyChanged("StepProgressText");
	}

	// 患者可能同时拥有多个就诊角色/科室，队列归属以服务端当前部门为准。
	public string ActiveDepartmentId { get; set; } = string.Empty;

	public long? CheckinAtMs { get; set; }

	public long? CalledAtMs { get; set; }

	public string Status => VisitStatus == 3
		? "已诊毕"
		: VisitStatus == 2
			? "就诊中"
			: QueueStatus switch
			{
				1 => "候诊中",
				2 => "已呼叫",
				3 => "过号",
				_ => "未分诊"
			};

	public string StatusClass => IsCompleted ? "Secondary" : IsInProgress ? "Primary" : QueueStatus == 1 ? "Warning" : "Secondary";

	public string ChiefComplaint { get; set; } = string.Empty;

	public string AllergyTags { get; set; } = string.Empty;

	public bool HasAllergyAlert => !string.IsNullOrWhiteSpace(AllergyTags)
		&& !string.Equals(AllergyTags.Trim(), "无", StringComparison.OrdinalIgnoreCase);

	public bool IsEmrSigned
	{
		get
		{
			return _isEmrSigned;
		}
		set
		{
			if (SetField(ref _isEmrSigned, value, "IsEmrSigned"))
			{
				OnPropertyChanged("StepProgressText");
			}
		}
	}

	public void SetEmrSigned(bool value)
	{
		IsEmrSigned = value;
	}

	public bool IsOrdersPushed
	{
		get
		{
			return _isOrdersPushed;
		}
		set
		{
			if (SetField(ref _isOrdersPushed, value, "IsOrdersPushed"))
			{
				OnPropertyChanged("StepProgressText");
			}
		}
	}

	public bool IsPrescriptionEditable
	{
		get
		{
			return _isPrescriptionEditable;
		}
		set
		{
			SetField(ref _isPrescriptionEditable, value, "IsPrescriptionEditable");
		}
	}

	public string PrescriptionNo { get; set; } = string.Empty;

	public int PrescriptionStatus { get; set; }

	public string PrescriptionStatusText { get; set; } = "尚未开立处方";

	public string PrescriptionStatusClass { get; set; } = "Secondary";

	public string? PrescriptionReviewRemark { get; set; }

	public bool IsWaiting => QueueStatus == 1 && VisitStatus == 1;

	public bool IsInProgress => VisitStatus == 2;

	public bool IsCompleted => VisitStatus == 3;

	public string StepProgressText
	{
		get
		{
			if (IsCompleted)
			{
				return "诊疗已完成";
			}
			if (IsWaiting)
			{
				return "等待呼叫接诊";
			}
			if (!IsInProgress)
			{
				return "当前不处于接诊状态";
			}
			return IsEmrSigned
				? IsOrdersPushed ? "可完成诊疗" : "病历已签，待处理处方"
				: "待录入诊断与病历";
		}
	}

	public bool IsSelected
	{
		get
		{
			return _isSelected;
		}
		set
		{
			if (SetField(ref _isSelected, value, "IsSelected"))
			{
				OnPropertyChanged("CardBorderBrush");
				OnPropertyChanged("CardBackground");
			}
		}
	}

	public global::Avalonia.Media.IBrush CardBorderBrush => IsSelected
		? global::HIS.Client.UrsaShell.Shell.ThemeBrushes.Primary
		: global::Avalonia.Media.Brushes.Transparent;

	public global::Avalonia.Media.IBrush CardBackground => IsSelected
		? global::HIS.Client.UrsaShell.Shell.ThemeBrushes.PrimaryLight
		: global::Avalonia.Media.Brushes.Transparent;
}
