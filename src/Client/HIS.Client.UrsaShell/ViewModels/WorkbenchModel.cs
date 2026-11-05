using System.Collections.ObjectModel;
using HIS.Client.Core;
using HIS.Contracts;

namespace HIS.Client.UrsaShell.ViewModels;

public class WorkbenchModel : ClientModelBase
{
    private string _campusName = "总院区 (Campus 1)";
    private DepartmentCategory _activeCategory = DepartmentCategory.OPD;
    private string _activeDeptName = "心血管内科门诊";
    private string _activeWardName = "内科一病区";
    private string _userName = "张建国 (主治医师)";
    private string _currentTab = "EMR_EDITOR";

    public string CampusName { get => _campusName; set => SetField(ref _campusName, value); }
    public DepartmentCategory ActiveCategory { get => _activeCategory; set => SetField(ref _activeCategory, value); }
    public string ActiveDeptName { get => _activeDeptName; set => SetField(ref _activeDeptName, value); }
    public string ActiveWardName { get => _activeWardName; set => SetField(ref _activeWardName, value); }
    public string UserName { get => _userName; set => SetField(ref _userName, value); }
    public string CurrentTab { get => _currentTab; set => SetField(ref _currentTab, value); }
}

public class BedCardModel : ClientModelBase
{
    public string BedCode { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string AdmissionNo { get; set; } = string.Empty;
    public bool IsBorrowedBed { get; set; }
    public string AdmittingDeptName { get; set; } = string.Empty;
    public string NursingLevel { get; set; } = "二级护理";
    public bool IsOccupied { get; set; }
}

public class EmrEditorWorkspaceModel : ClientModelBase
{
    private string _docNo = "DOC-OPD-" + DateTime.Now.ToString("yyyyMMddHHmm");
    private string _docTitle = "心血管内科门诊病历";
    private string _patientName = "王大明";
    private string _gender = "男";
    private int _age = 58;
    private string _chiefComplaint = "发作性胸痛、心悸3天，加重2小时。";
    private string _presentIllness = "患者3天前因劳累后出现心前区压榨性疼痛，向左肩放射，持续约5-10分钟，休息后可自行缓解。2小时前上述症状再发并加重。";
    private string _diagnosis = "冠状动脉粥样硬化性心脏病 (I25.101)";
    private string _menstrualHistory = "";
    private int _pregnancyCount = 0;
    private string _prostateExam = "轻度肥大，质韧无结节";
    private bool _isBusy;
    private string? _ofdNo;

    public string DocNo { get => _docNo; set => SetField(ref _docNo, value); }
    public string DocTitle { get => _docTitle; set => SetField(ref _docTitle, value); }
    public string PatientName { get => _patientName; set => SetField(ref _patientName, value); }
    public string Gender { get => _gender; set => SetField(ref _gender, value); }
    public int Age { get => _age; set => SetField(ref _age, value); }
    public string ChiefComplaint { get => _chiefComplaint; set => SetField(ref _chiefComplaint, value); }
    public string PresentIllness { get => _presentIllness; set => SetField(ref _presentIllness, value); }
    public string Diagnosis { get => _diagnosis; set => SetField(ref _diagnosis, value); }
    public string MenstrualHistory { get => _menstrualHistory; set => SetField(ref _menstrualHistory, value); }
    public int PregnancyCount { get => _pregnancyCount; set => SetField(ref _pregnancyCount, value); }
    public string ProstateExam { get => _prostateExam; set => SetField(ref _prostateExam, value); }
    public bool IsBusy { get => _isBusy; set => SetField(ref _isBusy, value); }
    public string? OfdNo { get => _ofdNo; set => SetField(ref _ofdNo, value); }
}

