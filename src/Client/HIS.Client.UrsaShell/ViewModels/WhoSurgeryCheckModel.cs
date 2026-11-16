using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class WhoSurgeryCheckModel : ClientModelBase
{
	private bool _stage1Confirmed;

	private bool _stage2Confirmed;

	private bool _stage3Confirmed;

	private string _checkStatus = "请按顺序完成：阶段A(麻醉前Sign-In) ➔ 阶段B(切皮前Time-Out) ➔ 阶段C(离室前Sign-Out)";

	public string PatientName { get; set; } = "周建强";

	public string AdmissionNo { get; set; } = "ADM20261105001";

	public string SurgeryName { get; set; } = "经皮冠状动脉造影及支架植入术 (PCI)";

	public string AnesthesiaType { get; set; } = "局部浸润麻醉 (1% 利多卡因)";

	public string Surgeon { get; set; } = "张建国 (主刀医师)";

	public string Anesthesiologist { get; set; } = "陈建安 (副主任医师)";

	public string ScrubNurse { get; set; } = "王玉玲 (器械护士)";

	public string CirculatingNurse { get; set; } = "赵彩霞 (巡回护士)";

	public bool SignInIdentityOk { get; set; } = true;

	public bool SignInSiteMarked { get; set; } = true;

	public bool SignInAnesthesiaSafetyCheck { get; set; } = true;

	public bool TimeOutAllTeamAgreed { get; set; } = true;

	public bool TimeOutAntibioticGiven { get; set; } = true;

	public bool TimeOutImagingDisplayed { get; set; } = true;

	public bool SignOutInstrumentCountMatched { get; set; } = true;

	public bool SignOutSpecimenLabeled { get; set; } = true;

	public string UdiImplantBarcode { get; set; } = "(01)06941234567890(17)281231(10)LOT20261105";

	public bool Stage1Confirmed
	{
		get
		{
			return _stage1Confirmed;
		}
		set
		{
			if (SetField(ref _stage1Confirmed, value, "Stage1Confirmed"))
			{
				NotifyWhoState();
			}
		}
	}

	public bool Stage2Confirmed
	{
		get
		{
			return _stage2Confirmed;
		}
		set
		{
			if (SetField(ref _stage2Confirmed, value, "Stage2Confirmed"))
			{
				NotifyWhoState();
			}
		}
	}

	public bool Stage3Confirmed
	{
		get
		{
			return _stage3Confirmed;
		}
		set
		{
			if (SetField(ref _stage3Confirmed, value, "Stage3Confirmed"))
			{
				NotifyWhoState();
			}
		}
	}

	public bool CanConfirmStage1 => !Stage1Confirmed;

	public bool CanConfirmStage2 => Stage1Confirmed && !Stage2Confirmed;

	public bool CanConfirmStage3 => Stage1Confirmed && Stage2Confirmed && !Stage3Confirmed;

	public string Stage1Badge => Stage1Confirmed ? "✓ 阶段A已核查" : "\ud83d\udc49 待核查阶段A";

	public string Stage2Badge
	{
		get
		{
			string result;
			if (Stage2Confirmed)
			{
				result = "✓ 阶段B已核查";
			}
			else
			{
				result = (Stage1Confirmed ? "\ud83d\udc49 待核查阶段B" : "\ud83d\udd12 需先完成阶段A");
			}
			return result;
		}
	}

	public string Stage3Badge
	{
		get
		{
			string result;
			if (Stage3Confirmed)
			{
				result = "✓ 阶段C已核查";
			}
			else
			{
				result = (Stage2Confirmed ? "\ud83d\udc49 待核查阶段C" : "\ud83d\udd12 需先完成阶段B");
			}
			return result;
		}
	}

	public string CheckStatus
	{
		get
		{
			return _checkStatus;
		}
		set
		{
			SetField(ref _checkStatus, value, "CheckStatus");
		}
	}

	private void NotifyWhoState()
	{
		OnPropertyChanged("CanConfirmStage1");
		OnPropertyChanged("CanConfirmStage2");
		OnPropertyChanged("CanConfirmStage3");
		OnPropertyChanged("Stage1Badge");
		OnPropertyChanged("Stage2Badge");
		OnPropertyChanged("Stage3Badge");
	}
}
