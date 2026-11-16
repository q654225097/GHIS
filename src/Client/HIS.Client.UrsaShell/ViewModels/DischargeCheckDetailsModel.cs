using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class DischargeCheckDetailsModel : ClientModelBase
{
	private InpatientRecordModel? _patient;

	public InpatientRecordModel? Patient
	{
		get
		{
			return _patient;
		}
		set
		{
			if (SetField(ref _patient, value, "Patient"))
			{
				Refresh();
			}
		}
	}

	public string PatientHeader => (Patient == null) ? "未选择住院患者" : $"{Patient.PatientName} · {Patient.Gender} {Patient.Age}岁 · {Patient.MedicalRecordNo}";

	public string AdmissionNo => Patient?.AdmissionNo ?? "--";

	public string BedCode => Patient?.BedCode ?? "--";

	public string PatientName => Patient?.PatientName ?? "--";

	public bool OrdersAllStopped
	{
		get
		{
			InpatientRecordModel? patient = Patient;
			return patient != null && patient.OrdersClosed && (Patient?.AllNursingTasksVerified ?? false);
		}
	}

	public bool MedTechReportsAllSigned => Patient?.MedTechReportsAllSigned ?? false;

	public bool DischargeMedsDispensed => Patient?.DischargeMedsDispensed ?? false;

	public bool EmrAllThreeLevelsSigned => Patient?.AllNotesSigned ?? false;

	public bool ClinicalGatesAllPassed => OrdersAllStopped && MedTechReportsAllSigned && DischargeMedsDispensed && EmrAllThreeLevelsSigned;

	public bool MedicalInsuranceSettled => Patient?.MedicalInsuranceSettled ?? false;

	public bool PrepaymentCleared => Patient?.PrepaymentCleared ?? false;

	public bool AllPassed => ClinicalGatesAllPassed && MedicalInsuranceSettled && PrepaymentCleared;

	public bool CanCompleteGate1
	{
		get
		{
			InpatientRecordModel patient = Patient;
			return patient != null && patient.IsActiveInpatient && !OrdersAllStopped;
		}
	}

	public bool CanCompleteGate2
	{
		get
		{
			InpatientRecordModel patient = Patient;
			return patient != null && patient.IsActiveInpatient && !MedTechReportsAllSigned;
		}
	}

	public bool CanCompleteGate3
	{
		get
		{
			InpatientRecordModel patient = Patient;
			return patient != null && patient.IsActiveInpatient && OrdersAllStopped && !DischargeMedsDispensed;
		}
	}

	public bool CanCompleteGate4
	{
		get
		{
			InpatientRecordModel patient = Patient;
			return patient != null && patient.IsActiveInpatient && !EmrAllThreeLevelsSigned;
		}
	}

	public bool CanCompleteGate5
	{
		get
		{
			InpatientRecordModel patient = Patient;
			return patient != null && patient.IsActiveInpatient && ClinicalGatesAllPassed && !MedicalInsuranceSettled;
		}
	}

	public bool CanCompleteGate6
	{
		get
		{
			InpatientRecordModel patient = Patient;
			return patient != null && patient.IsActiveInpatient && ClinicalGatesAllPassed && MedicalInsuranceSettled && !PrepaymentCleared;
		}
	}

	public string OrdersGateText => GateText(OrdersAllStopped, "步骤A已完成 [临床医护]: 医嘱全部停止且护理任务已核销", "步骤A未完成 [临床医护职责]: 仍有执行中/待审医嘱或未核对护理任务");

	public string OrdersGateBrush => GateBrush(OrdersAllStopped);

	public string MedTechGateText => GateText(MedTechReportsAllSigned, "步骤B已完成 [医技科]: 医技报告全部归档审签", "步骤B未完成 [医技科职责]: 存在未归档医技报告");

	public string MedTechGateBrush => GateBrush(MedTechReportsAllSigned);

	public string MedsGateText => GateText(DischargeMedsDispensed, "步骤C已完成 [药剂科]: 出院带药已由住院药房配发", OrdersAllStopped ? "步骤C待执行 [药剂科专属职责]: 请由药剂师在药房中心核发出院带药" : "\ud83d\udd12 需先完成步骤A(停止在院医嘱)后，方可由药剂科配发出院带药");

	public string MedsGateBrush => GateBrush(DischargeMedsDispensed);

	public string EmrGateText => GateText(EmrAllThreeLevelsSigned, "步骤D已完成 [临床主治]: 病程记录主治全部审签", "步骤D未完成 [临床主治职责]: 存在未审签病程记录");

	public string EmrGateBrush => GateBrush(EmrAllThreeLevelsSigned);

	public string InsuranceGateText => GateText(MedicalInsuranceSettled, "步骤E已完成 [财务处]: 医保基金结算单已核验", ClinicalGatesAllPassed ? "步骤E待执行 [财务收费员专属职责]: 请在【财务结算中心】执行医保预结算" : "\ud83d\udd12 需先完成步骤A~D(临床与药房前四关全部通过)，财务处方可解锁医保结算");

	public string InsuranceGateBrush => GateBrush(MedicalInsuranceSettled);

	public string PrepaymentGateText => GateText(PrepaymentCleared, "步骤F已完成 [财务处]: 预交金账目多退少补已结清", MedicalInsuranceSettled ? "步骤F待执行 [财务收费员专属职责]: 请在【财务结算中心】结清预交金" : "\ud83d\udd12 需先完成步骤E(财务医保结算核验)方可解锁预交金清算");

	public string PrepaymentGateBrush => GateBrush(PrepaymentCleared);

	public string AllPassedText => AllPassed ? "✓ A➔B➔C➔D➔E➔F 跨部门六严关全部闭环通过，允许主治医师终审办结出院！" : "⚠\ufe0f 严格遵循临床(A/B/D) ➔ 药房(C) ➔ 财务处(E/F) ➔ 主治终审(G) 跨部门职责分离闭环";

	public string AllPassedBrush => AllPassed ? "#059669" : "#DC2626";

	public decimal PrepaymentAmount => Patient?.PrepaymentAmount ?? 0m;

	public decimal ActualSelfPay => Patient?.ActualSelfPay ?? 0m;

	public decimal RefundAmount => PrepaymentAmount - ActualSelfPay;

	public void Refresh()
	{
		OnPropertyChanged("PatientHeader");
		OnPropertyChanged("AdmissionNo");
		OnPropertyChanged("BedCode");
		OnPropertyChanged("PatientName");
		OnPropertyChanged("OrdersAllStopped");
		OnPropertyChanged("MedTechReportsAllSigned");
		OnPropertyChanged("DischargeMedsDispensed");
		OnPropertyChanged("EmrAllThreeLevelsSigned");
		OnPropertyChanged("ClinicalGatesAllPassed");
		OnPropertyChanged("MedicalInsuranceSettled");
		OnPropertyChanged("PrepaymentCleared");
		OnPropertyChanged("AllPassed");
		OnPropertyChanged("CanCompleteGate1");
		OnPropertyChanged("CanCompleteGate2");
		OnPropertyChanged("CanCompleteGate3");
		OnPropertyChanged("CanCompleteGate4");
		OnPropertyChanged("CanCompleteGate5");
		OnPropertyChanged("CanCompleteGate6");
		OnPropertyChanged("OrdersGateText");
		OnPropertyChanged("OrdersGateBrush");
		OnPropertyChanged("MedTechGateText");
		OnPropertyChanged("MedTechGateBrush");
		OnPropertyChanged("MedsGateText");
		OnPropertyChanged("MedsGateBrush");
		OnPropertyChanged("EmrGateText");
		OnPropertyChanged("EmrGateBrush");
		OnPropertyChanged("InsuranceGateText");
		OnPropertyChanged("InsuranceGateBrush");
		OnPropertyChanged("PrepaymentGateText");
		OnPropertyChanged("PrepaymentGateBrush");
		OnPropertyChanged("AllPassedText");
		OnPropertyChanged("AllPassedBrush");
		OnPropertyChanged("PrepaymentAmount");
		OnPropertyChanged("ActualSelfPay");
		OnPropertyChanged("RefundAmount");
	}

	private static string GateText(bool passed, string success, string blocked)
	{
		return passed ? ("✓ " + success) : ("! " + blocked);
	}

	private static string GateBrush(bool passed)
	{
		return passed ? "#059669" : "#D97706";
	}
}
