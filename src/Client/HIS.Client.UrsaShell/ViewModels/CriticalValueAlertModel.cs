using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class CriticalValueAlertModel : ClientModelBase
{
	private string _doctorAck = "未签收 (待执行步骤A)";

	private string _nurseAck = "未签收 (需先完成步骤A医生审签)";

	public string ReportId { get; set; } = "CRIT-LAB-2026110509";

	public string PatientName { get; set; } = "周建强";

	public string BedCode { get; set; } = "02床 (借床)";

	public string TestItem { get; set; } = "超敏肌钙蛋白 I (hs-cTnI)";

	public string MeasuredValue { get; set; } = "12.85 ng/mL";

	public string ReferenceRange { get; set; } = "0.00 ~ 0.04 ng/mL";

	public string AlertLevel { get; set; } = "超危急值 (超高警戒线 300 倍)";

	public string ReportingTime { get; set; } = "2026-11-06 08:05:12";

	public string ReportingDept { get; set; } = "医学检验科 急诊急救生化室";

	public string DoctorAck
	{
		get
		{
			return _doctorAck;
		}
		set
		{
			if (SetField(ref _doctorAck, value, "DoctorAck"))
			{
				OnPropertyChanged("IsDoctorAcked");
				OnPropertyChanged("CanDoctorAck");
				OnPropertyChanged("CanNurseAck");
				OnPropertyChanged("IsAcknowledged");
			}
		}
	}

	public string NurseAck
	{
		get
		{
			return _nurseAck;
		}
		set
		{
			if (SetField(ref _nurseAck, value, "NurseAck"))
			{
				OnPropertyChanged("IsNurseAcked");
				OnPropertyChanged("CanNurseAck");
				OnPropertyChanged("IsAcknowledged");
			}
		}
	}

	public bool IsDoctorAcked => DoctorAck.StartsWith("✓ 已签收") || DoctorAck.StartsWith("已签收");

	public bool IsNurseAcked => NurseAck.StartsWith("✓ 已签收") || NurseAck.StartsWith("已签收");

	public bool CanDoctorAck => !IsDoctorAcked;

	public bool CanNurseAck => IsDoctorAcked && !IsNurseAcked;

	public bool IsAcknowledged => IsDoctorAcked && IsNurseAcked;

	public string ClinicalAction { get; set; } = "必须遵循：步骤A(经治医生首接审签并下达抢救指令) ➔ 步骤B(责任护士复核签收并执行监护) ➔ 步骤C(启动胸痛中心PCI急救绿通)";
}
