using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class InpatientRecordModel : ClientModelBase
{
	private string _nursingLevel = string.Empty;

	private bool _isCritical;

	private bool _isDischargeReady;

	private bool _isDischarged;

	private bool _isSelected;

	private bool _dischargeMedsDispensed;

	private bool _medTechReportsAllSigned;

	private bool _medicalInsuranceSettled;

	private bool _prepaymentCleared;

	private string _selectedOrderFilter = "ALL";

	public string AdmissionNo { get; set; } = string.Empty;

	public string PatientName { get; set; } = string.Empty;

	public string Gender { get; set; } = string.Empty;

	public int Age { get; set; }

	public string MedicalRecordNo { get; set; } = string.Empty;

	public string BedCode { get; set; } = string.Empty;

	public string AdmittingDept { get; set; } = string.Empty;

	public string NursingWard { get; set; } = string.Empty;

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

	public string CardBorderBrush => IsSelected ? "#165DFF" : "#21262D";

	public string CardBackground => IsSelected ? "#1A253A" : "#161B22";

	public string NursingLevel
	{
		get
		{
			return _nursingLevel;
		}
		set
		{
			if (SetField(ref _nursingLevel, value, "NursingLevel"))
			{
				OnPropertyChanged("NursingLevelBrush");
			}
		}
	}

	public string Diagnosis { get; set; } = string.Empty;

	public string AttendingDoctor { get; set; } = string.Empty;

	public string AdmissionDate { get; set; } = string.Empty;

	public string DailySummary { get; set; } = string.Empty;

	public bool DischargeMedsDispensed
	{
		get
		{
			return _dischargeMedsDispensed;
		}
		set
		{
			if (SetField(ref _dischargeMedsDispensed, value, "DischargeMedsDispensed"))
			{
				NotifyWorkflowChanged();
			}
		}
	}

	public bool MedTechReportsAllSigned
	{
		get
		{
			return _medTechReportsAllSigned;
		}
		set
		{
			if (SetField(ref _medTechReportsAllSigned, value, "MedTechReportsAllSigned"))
			{
				NotifyWorkflowChanged();
			}
		}
	}

	public bool MedicalInsuranceSettled
	{
		get
		{
			return _medicalInsuranceSettled;
		}
		set
		{
			if (SetField(ref _medicalInsuranceSettled, value, "MedicalInsuranceSettled"))
			{
				NotifyWorkflowChanged();
			}
		}
	}

	public bool PrepaymentCleared
	{
		get
		{
			return _prepaymentCleared;
		}
		set
		{
			if (SetField(ref _prepaymentCleared, value, "PrepaymentCleared"))
			{
				NotifyWorkflowChanged();
			}
		}
	}

	public decimal PrepaymentAmount { get; set; }

	public decimal ActualSelfPay { get; set; }

	public decimal RefundAmount => PrepaymentAmount - ActualSelfPay;

	public bool IsCritical
	{
		get
		{
			return _isCritical;
		}
		set
		{
			if (SetField(ref _isCritical, value, "IsCritical"))
			{
				OnPropertyChanged("PatientStatus");
				OnPropertyChanged("PatientStatusClass");
				OnPropertyChanged("PatientStatusBrush");
			}
		}
	}

	public bool IsDischargeReady
	{
		get
		{
			return _isDischargeReady;
		}
		set
		{
			if (SetField(ref _isDischargeReady, value, "IsDischargeReady"))
			{
				OnPropertyChanged("PatientStatus");
				OnPropertyChanged("PatientStatusClass");
				OnPropertyChanged("PatientStatusBrush");
			}
		}
	}

	public bool IsDischarged
	{
		get
		{
			return _isDischarged;
		}
		set
		{
			if (SetField(ref _isDischarged, value, "IsDischarged"))
			{
				OnPropertyChanged("PatientStatus");
				OnPropertyChanged("PatientStatusClass");
				OnPropertyChanged("PatientStatusBrush");
				OnPropertyChanged("IsActiveInpatient");
			}
		}
	}

	public bool HasAllergyAlert { get; set; }

	public string AllergyTags { get; set; } = string.Empty;

	public ObservableCollection<InpatientOrderModel> Orders { get; } = new ObservableCollection<InpatientOrderModel>();

	public ObservableCollection<InpatientOrderModel> FilteredOrders { get; } = new ObservableCollection<InpatientOrderModel>();

	public string SelectedOrderFilter
	{
		get
		{
			return _selectedOrderFilter;
		}
		set
		{
			if (SetField(ref _selectedOrderFilter, value, "SelectedOrderFilter"))
			{
				ApplyOrderFilter();
				OnPropertyChanged("IsOrderFilterAll");
				OnPropertyChanged("IsOrderFilterLong");
				OnPropertyChanged("IsOrderFilterTemp");
				OnPropertyChanged("IsOrderFilterPending");
			}
		}
	}

	public bool IsOrderFilterAll => SelectedOrderFilter == "ALL";

	public bool IsOrderFilterLong => SelectedOrderFilter == "LONG";

	public bool IsOrderFilterTemp => SelectedOrderFilter == "TEMP";

	public bool IsOrderFilterPending => SelectedOrderFilter == "PENDING";

	public int LongTermOrderCount => Enumerable.Count(Orders, (InpatientOrderModel x) => x.OrderType == "长期医嘱" && x.IsActive);

	public int TempOrderCount => Enumerable.Count(Orders, (InpatientOrderModel x) => x.OrderType == "临时医嘱" && x.IsActive);

	public int WaitingDecomposeOrderCount => Enumerable.Count(Orders, (InpatientOrderModel x) => x.IsReviewedWaitingDecompose);

	public ObservableCollection<ProgressNoteModel> ProgressNotes { get; } = new ObservableCollection<ProgressNoteModel>();

	public ObservableCollection<NursingTaskModel> NursingTasks { get; } = new ObservableCollection<NursingTaskModel>();

	public ObservableCollection<VitalSignModel> VitalSigns { get; } = new ObservableCollection<VitalSignModel>();

	public ObservableCollection<HandoverNoteModel> HandoverNotes { get; } = new ObservableCollection<HandoverNoteModel>();

	public string PatientContext => $"{Gender} · {Age}岁 · {MedicalRecordNo}";

	public string BedAndWard => BedCode + " · " + NursingWard;

	public string PatientStatus
	{
		get
		{
			string result;
			if (IsDischarged)
			{
				result = "已出院";
			}
			else if (IsCritical)
			{
				result = "病危";
			}
			else
			{
				result = (IsDischargeReady ? "待出院" : "在院");
			}
			return result;
		}
	}

	public string PatientStatusClass
	{
		get
		{
			string result;
			if (IsDischarged)
			{
				result = "Secondary";
			}
			else if (IsCritical)
			{
				result = "Danger";
			}
			else
			{
				result = (IsDischargeReady ? "Warning" : "Success");
			}
			return result;
		}
	}

	public string PatientStatusBrush
	{
		get
		{
			string result;
			if (IsDischarged)
			{
				result = "#64748B";
			}
			else if (IsCritical)
			{
				result = "#DC2626";
			}
			else
			{
				result = (IsDischargeReady ? "#D97706" : "#059669");
			}
			return result;
		}
	}

	public string NursingLevelBrush
	{
		get
		{
			string result;
			if (NursingLevel.Contains("特级"))
			{
				result = "#DC2626";
			}
			else if (NursingLevel.Contains("一级"))
			{
				result = "#D97706";
			}
			else
			{
				result = (NursingLevel.Contains("二级") ? "#2563EB" : "#059669");
			}
			return result;
		}
	}

	public int PendingOrderCount => Enumerable.Count(Orders, (InpatientOrderModel x) => x.IsPendingReview);

	public int ActiveOrderCount => Enumerable.Count(Orders, (InpatientOrderModel x) => x.IsActive);

	public int PendingNursingTaskCount => Enumerable.Count(NursingTasks, (NursingTaskModel x) => x.IsPending);

	public int UnverifiedNursingTaskCount => Enumerable.Count(NursingTasks, (NursingTaskModel x) => x.CanVerify);

	public int UnsignedNoteCount => Enumerable.Count(ProgressNotes, (ProgressNoteModel x) => !x.IsSigned);

	public int UnconfirmedHandoverCount => Enumerable.Count(HandoverNotes, (HandoverNoteModel x) => !x.IsConfirmed);

	public string AllergySummary => HasAllergyAlert ? AllergyTags : "无已知药物过敏";

	public string WorkflowCounts => $"A.待校对:{PendingOrderCount} · B.待分解:{WaitingDecomposeOrderCount} · D.待执行:{PendingNursingTaskCount} · E.待核对:{UnverifiedNursingTaskCount}";

	public string WorkflowSummary
	{
		get
		{
			string result;
			if (PendingOrderCount > 0)
			{
				result = $"步骤B: {PendingOrderCount} 条医嘱待护士校对";
			}
			else if (WaitingDecomposeOrderCount > 0)
			{
				result = $"步骤C: {WaitingDecomposeOrderCount} 条已校对医嘱待分解";
			}
			else if (PendingNursingTaskCount > 0)
			{
				result = $"步骤D: {PendingNursingTaskCount} 项护理待扫码执行";
			}
			else if (UnverifiedNursingTaskCount > 0)
			{
				result = $"步骤E: {UnverifiedNursingTaskCount} 项已执行待双人核对";
			}
			else
			{
				result = ((UnsignedNoteCount > 0) ? $"{UnsignedNoteCount} 份病程待主治审签" : "今日医护流程已闭环");
			}
			return result;
		}
	}

	public bool OrdersClosed => Orders.Count > 0 && Orders.All((InpatientOrderModel x) => !x.IsActive);

	public bool AllNotesSigned => ProgressNotes.Count > 0 && ProgressNotes.All((ProgressNoteModel x) => x.IsSigned);

	public bool AllNursingTasksVerified => NursingTasks.Count == 0 || NursingTasks.All((NursingTaskModel x) =>
	{
		string status = x.Status;
		return (status == "已核对" || status == "已退回") ? true : false;
	});

	public bool AllHandoversConfirmed => HandoverNotes.Count > 0 && HandoverNotes.All((HandoverNoteModel x) => x.IsConfirmed);

	public bool IsActiveInpatient => !IsDischarged;

	public bool ClinicalGatesAllPassed => OrdersClosed && AllNursingTasksVerified && MedTechReportsAllSigned && DischargeMedsDispensed && AllNotesSigned;

	public bool CanDispenseDischargeMeds => IsActiveInpatient && OrdersClosed && AllNursingTasksVerified && !DischargeMedsDispensed;

	public bool CanSettleInsurance => IsActiveInpatient && ClinicalGatesAllPassed && !MedicalInsuranceSettled;

	public bool CanClearPrepayment => IsActiveInpatient && ClinicalGatesAllPassed && MedicalInsuranceSettled && !PrepaymentCleared;

	public string ClinicalGatesSummaryText => ClinicalGatesAllPassed ? "✓ 临床与药房前4关(A~D)已全部完成 · 财务可办理步骤E(医保)与步骤F(预交金)" : $"⏳ 临床前4关未全通 (医嘱闭环:{((OrdersClosed && AllNursingTasksVerified) ? "✓" : "未通")} | 医技:{(MedTechReportsAllSigned ? "✓" : "未通")} | 出院带药:{(DischargeMedsDispensed ? "✓" : "未通")} | 病程:{(AllNotesSigned ? "✓" : "未通")})";

	public string DischargeGateSummary
	{
		get
		{
			int num = 0;
			if (OrdersClosed && AllNursingTasksVerified)
			{
				num++;
			}
			if (MedTechReportsAllSigned)
			{
				num++;
			}
			if (DischargeMedsDispensed)
			{
				num++;
			}
			if (AllNotesSigned)
			{
				num++;
			}
			if (MedicalInsuranceSettled)
			{
				num++;
			}
			if (PrepaymentCleared)
			{
				num++;
			}
			return $"{num}/6 关通过";
		}
	}

	public string DischargeGateBrush => DischargeGateSummary.StartsWith("6/6") ? "#059669" : "#D97706";

	public string BorrowSummary => NursingWard.Contains("借床") ? "跨病区借床" : NursingWard;

	public void ApplyOrderFilter()
	{
		string filter = SelectedOrderFilter;
		List<InpatientOrderModel> list = Orders.Where((InpatientOrderModel x) =>
		{
			if (1 == 0)
			{
			}
			bool result = filter switch
			{
				"LONG" => x.OrderType == "长期医嘱", 
				"TEMP" => x.OrderType == "临时医嘱", 
				"PENDING" => x.IsPendingReview || x.IsReviewedWaitingDecompose, 
				_ => true, 
			};
			if (1 == 0)
			{
			}
			return result;
		}).ToList();
		FilteredOrders.Clear();
		foreach (InpatientOrderModel item in list)
		{
			FilteredOrders.Add(item);
		}
	}

	public void NotifyWorkflowChanged()
	{
		ApplyOrderFilter();
		OnPropertyChanged("PendingOrderCount");
		OnPropertyChanged("WaitingDecomposeOrderCount");
		OnPropertyChanged("ActiveOrderCount");
		OnPropertyChanged("LongTermOrderCount");
		OnPropertyChanged("TempOrderCount");
		OnPropertyChanged("PendingNursingTaskCount");
		OnPropertyChanged("UnverifiedNursingTaskCount");
		OnPropertyChanged("UnsignedNoteCount");
		OnPropertyChanged("UnconfirmedHandoverCount");
		OnPropertyChanged("WorkflowCounts");
		OnPropertyChanged("WorkflowSummary");
		OnPropertyChanged("OrdersClosed");
		OnPropertyChanged("AllNotesSigned");
		OnPropertyChanged("AllNursingTasksVerified");
		OnPropertyChanged("AllHandoversConfirmed");
		OnPropertyChanged("IsActiveInpatient");
		OnPropertyChanged("ClinicalGatesAllPassed");
		OnPropertyChanged("CanDispenseDischargeMeds");
		OnPropertyChanged("CanSettleInsurance");
		OnPropertyChanged("CanClearPrepayment");
		OnPropertyChanged("ClinicalGatesSummaryText");
		OnPropertyChanged("DischargeGateSummary");
		OnPropertyChanged("DischargeGateBrush");
	}
}
