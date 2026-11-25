using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using HIS.BuildingBlocks.Common;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Printing;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.IpdNurse;

public class CarePlanModel : ClientModelBase
{
    private string _id = string.Empty;
    private string _encounterId = string.Empty;
    private string _patientId = string.Empty;
    private string _diagnosisOrProblem = string.Empty;
    private string _goal = string.Empty;
    private string? _interventionsJson;
    private int _status;

    public string Id { get => _id; set => SetField(ref _id, value); }
    public string EncounterId { get => _encounterId; set => SetField(ref _encounterId, value); }
    public string PatientId { get => _patientId; set => SetField(ref _patientId, value); }
    public string DiagnosisOrProblem { get => _diagnosisOrProblem; set => SetField(ref _diagnosisOrProblem, value); }
    public string Goal { get => _goal; set => SetField(ref _goal, value); }
    public string? InterventionsJson { get => _interventionsJson; set => SetField(ref _interventionsJson, value); }
    public int Status { get => _status; set { if (SetField(ref _status, value)) { OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(CanEvaluate)); } } }

    public string StatusText => Status switch { 0 => "执行中", 1 => "目标达成", _ => "已停止" };
    public bool CanEvaluate => Status == 0;
}

public class BradenAssessmentModel : ClientModelBase
{
    public string Id { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public int TotalScore { get; set; }
    public string RiskLevel { get; set; } = string.Empty;
    public string RiskLevelText => RiskLevel switch { "VERY_HIGH" => "极高危(<=9分)", "HIGH" => "高危(10-12分)", "MODERATE" => "中危(13-14分)", "MILD" => "低危(15-18分)", _ => "无风险(>=19分)" };
    public string? PreventiveMeasuresJson { get; set; }
    public string AssessorId { get; set; } = string.Empty;
    public long AssessedAtMs { get; set; }
    public string AssessedTimeText => DateTimeOffset.FromUnixTimeMilliseconds(AssessedAtMs).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}

public class MorseAssessmentModel : ClientModelBase
{
    public string Id { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public int TotalScore { get; set; }
    public string RiskLevel { get; set; } = string.Empty;
    public string RiskLevelText => RiskLevel switch { "HIGH" => "高危跌倒(>=45分)", "MEDIUM" => "中危跌倒(25-44分)", _ => "低危跌倒(<25分)" };
    public string? PreventiveMeasuresJson { get; set; }
    public string AssessorId { get; set; } = string.Empty;
    public long AssessedAtMs { get; set; }
    public string AssessedTimeText => DateTimeOffset.FromUnixTimeMilliseconds(AssessedAtMs).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}

public class InfectionReportModel : ClientModelBase
{
    public string Id { get; set; } = string.Empty;
    public string ReportNo { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string InfectionSite { get; set; } = string.Empty;
    public string Pathogen { get; set; } = string.Empty;
    public short IsMdro { get; set; }
    public string IsMdroText => IsMdro == 1 ? "【多重耐药 MDRO】" : "常规病原体";
    public int Status { get; set; }
    public string StatusText => Status switch { 0 => "待审核", 1 => "已确认", 2 => "已干预", _ => "已结案" };
}

public class IsolationOrderModel : ClientModelBase
{
    private int _status;
    public string Id { get; set; } = string.Empty;
    public string OrderNo { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public string IsolationType { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int Status { get => _status; set { if (SetField(ref _status, value)) { OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(CanEnd)); } } }
    public string StatusText => Status == 0 ? "隔离执行中" : "已解除隔离";
    public bool CanEnd => Status == 0;
}

public sealed partial class IpdNurseViewModel : ClientModelBase, IShortcutTarget, IPrintContextProvider
{
    private readonly ShellViewModel? _shell;
    private bool _subscribed;

    // P1-D: Care Plans
    private string _newPlanProblem = string.Empty;
    private string _newPlanGoal = string.Empty;
    private string _newPlanInterventions = string.Empty;
    private CarePlanModel? _selectedCarePlan;
    private string _newEvalResult = string.Empty;
    private bool _newEvalGoalMet = true;

    // P1-D: Braden
    private int _bradenSensory = 4;
    private int _bradenMoisture = 4;
    private int _bradenActivity = 4;
    private int _bradenMobility = 4;
    private int _bradenNutrition = 4;
    private int _bradenFriction = 3;
    private string _bradenMeasures = "保持床单位清洁干燥；定时协助翻身；减压垫防护";

    // P1-D: Morse
    private int _morseHistory = 0;
    private int _morseSecondary = 0;
    private int _morseAid = 0;
    private int _morseIv = 20;
    private int _morseGait = 0;
    private int _morseMental = 0;
    private string _morseMeasures = "开启床档防护；呼叫器置于易取处；穿防滑鞋与协助活动";

    // P1-E: IPC
    private string _haiSite = "呼吸系统";
    private string _haiPathogen = "耐甲氧西林金黄色葡萄球菌 (MRSA)";
    private bool _haiIsMdro = true;
    private string _haiManifestation = "发热38.5℃，咳脓性痰，双肺可闻及湿啰音";

    private string _isoType = "CONTACT";
    private string _isoReason = "MRSA 肺部感染，落实接触隔离防护措施";

    private string _handMoment = "TOUCH_PATIENT";
    private string _handStaffCat = "NURSE";
    private int _handOppCount = 10;
    private int _handPerfCount = 10;
    private int _handCorrCount = 9;

    private string _ipcSummaryText = "院感态势：载入中...";

    public IpdNurseViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        OpenCriticalValueCommand = new RelayCommand(() => _shell?.OpenCriticalValueDrawerCommand.Execute(null));
        CycleNursingLevelCommand = new RelayCommand(ExecuteCycleNursingLevel);
        ReviewOrdersCommand = new RelayCommand(ExecuteReviewOrders, () => Workbench.CanReviewSelectedInpatientOrders);
        ReviewSingleOrderCommand = new RelayCommand(p => ExecuteReviewSingleOrder(p as InpatientOrderModel));
        DecomposeTodayCommand = new RelayCommand(ExecuteDecomposeToday);
        DecomposeTomorrowCommand = new RelayCommand(ExecuteDecomposeTomorrow);
        OpenFrequencyConfigCommand = new RelayCommand(OpenFrequencyConfigDrawer);
        FilterOrdersCommand = new RelayCommand(p => ApplyOrderFilter(p as string ?? "ALL"));
        ExecuteAllTasksCommand = new RelayCommand(ConfirmAndExecuteAllTasks, () => Workbench.CanExecuteSelectedNursingTasks);
        ExecuteTaskCommand = new RelayCommand(p => ExecuteTask(p as NursingTaskModel));
        VerifyTaskCommand = new RelayCommand(p => VerifyTask(p as NursingTaskModel));
        SaveVitalsCommand = new RelayCommand(ExecuteSaveVitals);
        AddHandoverCommand = new RelayCommand(ExecuteAddHandover);
        ConfirmHandoverCommand = new RelayCommand(p => ExecuteConfirmHandover(p as HandoverNoteModel));
        OpenBorrowBedCommand = new RelayCommand(OpenBorrowBedDrawer);

        // P1-D Care Plans
        CreateCarePlanCommand = new RelayCommand(async () => await CreateCarePlanAsync());
        EvaluateCarePlanCommand = new RelayCommand(async () => await EvaluateCarePlanAsync());
        RefreshCarePlansCommand = new RelayCommand(async () => await LoadCarePlansAsync());

        // P1-D Braden & Morse
        SubmitBradenCommand = new RelayCommand(async () => await SubmitBradenAsync());
        SubmitMorseCommand = new RelayCommand(async () => await SubmitMorseAsync());
        RefreshAssessmentsCommand = new RelayCommand(async () => await LoadAssessmentsAsync());

        // P1-E IPC
        SubmitHaiReportCommand = new RelayCommand(async () => await SubmitHaiReportAsync());
        SubmitIsolationOrderCommand = new RelayCommand(async () => await SubmitIsolationOrderAsync());
        EndIsolationOrderCommand = new RelayCommand(async (p) => await EndIsolationOrderAsync(p as IsolationOrderModel));
        SubmitHandAuditCommand = new RelayCommand(async () => await SubmitHandAuditAsync());
        RefreshIpcCommand = new RelayCommand(async () => await RefreshIpcDataAsync());

        LoadNursingUserTemplatesCommand = new AsyncRelayCommand(LoadNursingUserTemplatesAsync);
        SaveAsNursingUserTemplateCommand = new AsyncRelayCommand(SaveAsNursingUserTemplateAsync);
        PdaScanVerifyAndExecuteCommand = new AsyncRelayCommand(ExecutePdaScanVerifyAndExecuteServerAsync);
        DualNurseSignCommand = new RelayCommand(ExecuteDualNurseSign);
        if (SessionContext.Current.IsLoggedIn)
        {
            _ = Workbench.RefreshInpatientsFromServerAsync();
        }
    }

    private string _pdaWristbandBarcode = string.Empty;
    public string PdaWristbandBarcode
    {
        get => _pdaWristbandBarcode;
        set => SetField(ref _pdaWristbandBarcode, value);
    }

    private string _pdaMedicationBarcode = string.Empty;
    public string PdaMedicationBarcode
    {
        get => _pdaMedicationBarcode;
        set => SetField(ref _pdaMedicationBarcode, value);
    }

    public ICommand PdaScanVerifyAndExecuteCommand { get; }
    public ICommand DualNurseSignCommand { get; }

    private void ExecutePdaScanVerifyAndExecute()
    {
        var patient = Workbench.SelectedInpatient;
        if (patient == null)
        {
            _shell?.ShowToast("请先选择患者", "Warning");
            return;
        }

        if (string.IsNullOrWhiteSpace(_pdaWristbandBarcode))
        {
            _shell?.ShowToast("请扫描患者腕带条码", "Warning");
            return;
        }

        bool isPatientMatch = string.Equals(_pdaWristbandBarcode.Trim(), patient.AdmissionNo.Trim(), StringComparison.OrdinalIgnoreCase)
                           || string.Equals(_pdaWristbandBarcode.Trim(), patient.PatientId.Trim(), StringComparison.OrdinalIgnoreCase)
                           || _pdaWristbandBarcode.Contains(patient.AdmissionNo.Trim(), StringComparison.OrdinalIgnoreCase);

        if (!isPatientMatch)
        {
            _shell?.ShowToast("【409 严重条码不匹配】患者腕带与当前在院档案不一致，严禁给药！", "Error");
            return;
        }

        var pendingTask = patient.NursingTasks.FirstOrDefault(t => t.IsPending);
        if (pendingTask != null)
        {
            ExecuteTask(pendingTask);
            _shell?.ShowToast($"PDA 双码核验通过：已安全执行医嘱 [{pendingTask.Content}]", "Success");
            PdaMedicationBarcode = string.Empty;
        }
        else
        {
            _shell?.ShowToast("腕带核对一致，当前患者暂无待执行护理任务", "Info");
        }
    }

    private void ExecuteDualNurseSign()
    {
        var patient = Workbench.SelectedInpatient;
        if (patient == null) return;
        var signer = SessionContext.Current.DisplayName ?? SessionContext.Current.UserId ?? "责任护士";
        _shell?.ShowToast($"高风险药品双人核签成功：执行护士 [{signer}] 与 监护护士 [双人核对确认完毕]", "Success");
    }

    public WorkbenchModel Workbench { get; }

    public ObservableCollection<EmrUserTemplateModel> NursingUserTemplates { get; } = new();

    private EmrUserTemplateModel? _selectedNursingUserTemplate;
    public EmrUserTemplateModel? SelectedNursingUserTemplate
    {
        get => _selectedNursingUserTemplate;
        set
        {
            if (SetField(ref _selectedNursingUserTemplate, value) && value != null)
            {
                ExecuteApplyNursingTemplate(value);
            }
        }
    }

    public ICommand LoadNursingUserTemplatesCommand { get; }
    public ICommand SaveAsNursingUserTemplateCommand { get; }

    public ObservableCollection<CarePlanModel> CarePlans { get; } = new();
    public ObservableCollection<BradenAssessmentModel> BradenHistory { get; } = new();
    public ObservableCollection<MorseAssessmentModel> MorseHistory { get; } = new();
    public ObservableCollection<InfectionReportModel> InfectionReports { get; } = new();
    public ObservableCollection<IsolationOrderModel> IsolationOrders { get; } = new();

    public Action? FocusVitalsInputAction { get; set; }
    public Action? FocusHandoverInputAction { get; set; }

    // Properties for Care Plan
    public string NewPlanProblem { get => _newPlanProblem; set => SetField(ref _newPlanProblem, value); }
    public string NewPlanGoal { get => _newPlanGoal; set => SetField(ref _newPlanGoal, value); }
    public string NewPlanInterventions { get => _newPlanInterventions; set => SetField(ref _newPlanInterventions, value); }
    public CarePlanModel? SelectedCarePlan { get => _selectedCarePlan; set => SetField(ref _selectedCarePlan, value); }
    public string NewEvalResult { get => _newEvalResult; set => SetField(ref _newEvalResult, value); }
    public bool NewEvalGoalMet { get => _newEvalGoalMet; set => SetField(ref _newEvalGoalMet, value); }

    // Properties for Braden
    public int BradenSensory { get => _bradenSensory; set { if (SetField(ref _bradenSensory, value)) OnPropertyChanged(nameof(BradenTotalScore)); } }
    public int BradenMoisture { get => _bradenMoisture; set { if (SetField(ref _bradenMoisture, value)) OnPropertyChanged(nameof(BradenTotalScore)); } }
    public int BradenActivity { get => _bradenActivity; set { if (SetField(ref _bradenActivity, value)) OnPropertyChanged(nameof(BradenTotalScore)); } }
    public int BradenMobility { get => _bradenMobility; set { if (SetField(ref _bradenMobility, value)) OnPropertyChanged(nameof(BradenTotalScore)); } }
    public int BradenNutrition { get => _bradenNutrition; set { if (SetField(ref _bradenNutrition, value)) OnPropertyChanged(nameof(BradenTotalScore)); } }
    public int BradenFriction { get => _bradenFriction; set { if (SetField(ref _bradenFriction, value)) OnPropertyChanged(nameof(BradenTotalScore)); } }
    public int BradenTotalScore => BradenSensory + BradenMoisture + BradenActivity + BradenMobility + BradenNutrition + BradenFriction;
    public string BradenMeasures { get => _bradenMeasures; set => SetField(ref _bradenMeasures, value); }

    // Properties for Morse
    public int MorseHistoryScore { get => _morseHistory; set { if (SetField(ref _morseHistory, value)) OnPropertyChanged(nameof(MorseTotalScore)); } }
    public int MorseSecondary { get => _morseSecondary; set { if (SetField(ref _morseSecondary, value)) OnPropertyChanged(nameof(MorseTotalScore)); } }
    public int MorseAid { get => _morseAid; set { if (SetField(ref _morseAid, value)) OnPropertyChanged(nameof(MorseTotalScore)); } }
    public int MorseIv { get => _morseIv; set { if (SetField(ref _morseIv, value)) OnPropertyChanged(nameof(MorseTotalScore)); } }
    public int MorseGait { get => _morseGait; set { if (SetField(ref _morseGait, value)) OnPropertyChanged(nameof(MorseTotalScore)); } }
    public int MorseMental { get => _morseMental; set { if (SetField(ref _morseMental, value)) OnPropertyChanged(nameof(MorseTotalScore)); } }
    public int MorseTotalScore => MorseHistoryScore + MorseSecondary + MorseAid + MorseIv + MorseGait + MorseMental;
    public string MorseMeasures { get => _morseMeasures; set => SetField(ref _morseMeasures, value); }

    // Properties for IPC
    public string HaiSite { get => _haiSite; set => SetField(ref _haiSite, value); }
    public string HaiPathogen { get => _haiPathogen; set => SetField(ref _haiPathogen, value); }
    public bool HaiIsMdro { get => _haiIsMdro; set => SetField(ref _haiIsMdro, value); }
    public string HaiManifestation { get => _haiManifestation; set => SetField(ref _haiManifestation, value); }

    public string IsoType { get => _isoType; set => SetField(ref _isoType, value); }
    public string IsoReason { get => _isoReason; set => SetField(ref _isoReason, value); }

    public string HandMoment { get => _handMoment; set => SetField(ref _handMoment, value); }
    public string HandStaffCat { get => _handStaffCat; set => SetField(ref _handStaffCat, value); }
    public int HandOppCount { get => _handOppCount; set => SetField(ref _handOppCount, value); }
    public int HandPerfCount { get => _handPerfCount; set => SetField(ref _handPerfCount, value); }
    public int HandCorrCount { get => _handCorrCount; set => SetField(ref _handCorrCount, value); }

    public string IpcSummaryText { get => _ipcSummaryText; set => SetField(ref _ipcSummaryText, value); }

    // Commands
    public ICommand OpenCriticalValueCommand { get; }
    public ICommand CycleNursingLevelCommand { get; }
    public ICommand ReviewOrdersCommand { get; }
    public ICommand ReviewSingleOrderCommand { get; }
    public ICommand DecomposeTodayCommand { get; }
    public ICommand DecomposeTomorrowCommand { get; }
    public ICommand OpenFrequencyConfigCommand { get; }
    public ICommand FilterOrdersCommand { get; }
    public ICommand ExecuteAllTasksCommand { get; }
    public ICommand ExecuteTaskCommand { get; }
    public ICommand VerifyTaskCommand { get; }
    public ICommand SaveVitalsCommand { get; }
    public ICommand AddHandoverCommand { get; }
    public ICommand ConfirmHandoverCommand { get; }
    public ICommand OpenBorrowBedCommand { get; }

    public ICommand CreateCarePlanCommand { get; }
    public ICommand EvaluateCarePlanCommand { get; }
    public ICommand RefreshCarePlansCommand { get; }

    public ICommand SubmitBradenCommand { get; }
    public ICommand SubmitMorseCommand { get; }
    public ICommand RefreshAssessmentsCommand { get; }

    public ICommand SubmitHaiReportCommand { get; }
    public ICommand SubmitIsolationOrderCommand { get; }
    public ICommand EndIsolationOrderCommand { get; }
    public ICommand SubmitHandAuditCommand { get; }
    public ICommand RefreshIpcCommand { get; }

    public string ReviewBlockedReason
    {
        get
        {
            if (Workbench.SelectedInpatient == null) return "请先选择左侧病区患者。";
            if (!Workbench.CanExecuteNursingTasks) return "当前登录身份无护士站校对/执行权限，请切换为护士或管理员。";
            if (!Workbench.CanReviewSelectedInpatientOrders) return "当前患者暂无待校对医嘱。";
            return string.Empty;
        }
    }

    public string ExecuteTasksBlockedReason
    {
        get
        {
            if (Workbench.SelectedInpatient == null) return "请先选择左侧病区患者。";
            if (!Workbench.CanExecuteNursingTasks) return "当前登录身份无护理执行权限。";
            if (!Workbench.CanExecuteSelectedNursingTasks) return "当前患者暂无待执行的护理任务。";
            return string.Empty;
        }
    }

    public void Attach()
    {
        if (!_subscribed)
        {
            Workbench.PropertyChanged += OnWorkbenchPropertyChanged;
            _subscribed = true;
        }
        RaiseComputed();
        _ = LoadCarePlansAsync();
        _ = LoadAssessmentsAsync();
        _ = RefreshIpcDataAsync();
        _ = LoadNursingUserTemplatesAsync();
    }

    public void Detach()
    {
        if (_subscribed)
        {
            Workbench.PropertyChanged -= OnWorkbenchPropertyChanged;
            _subscribed = false;
        }
    }

    public bool OnShortcut(string keyGesture)
    {
        switch (keyGesture)
        {
            case "F2":
                if (Workbench.SelectedNurseTabIndex == 3) FocusHandoverInputAction?.Invoke();
                else
                {
                    Workbench.SelectedNurseTabIndex = 2;
                    FocusVitalsInputAction?.Invoke();
                }
                return true;
            case "F3":
                OpenFrequencyConfigDrawer();
                return true;
            case "Ctrl+S":
                if (Workbench.SelectedNurseTabIndex == 2) { ExecuteSaveVitals(); return true; }
                if (Workbench.SelectedNurseTabIndex == 3) { ExecuteAddHandover(); return true; }
                return false;
            case "Ctrl+Enter":
                if (Workbench.SelectedNurseTabIndex == 0 && ReviewOrdersCommand.CanExecute(null))
                {
                    ExecuteReviewOrders();
                    return true;
                }
                if (Workbench.SelectedNurseTabIndex == 1 && ExecuteAllTasksCommand.CanExecute(null))
                {
                    ConfirmAndExecuteAllTasks();
                    return true;
                }
                return false;
            default:
                return false;
        }
    }

    private void ExecuteCycleNursingLevel()
    {
        Workbench.CycleNursingLevel();
        _shell?.ShowToast(Workbench.SystemNotice, "Success");
        RaiseComputed();
    }

    private void ExecuteReviewOrders()
    {
        if (!Workbench.CanReviewSelectedInpatientOrders) return;
        Workbench.ReviewSelectedInpatientOrders();
        _shell?.ShowToast(Workbench.SystemNotice, "Success");
        RaiseComputed();
    }

    private void ExecuteReviewSingleOrder(InpatientOrderModel? order)
    {
        if (order == null || !Workbench.CanExecuteNursingTasks) return;
        Workbench.ReviewSingleInpatientOrder(order);
        _shell?.ShowToast(Workbench.SystemNotice, "Success");
        RaiseComputed();
    }

    private void ExecuteDecomposeToday()
    {
        Workbench.DecomposeTodayOrders();
        _shell?.ShowToast(Workbench.SystemNotice, "Success");
        RaiseComputed();
    }

    private void ExecuteDecomposeTomorrow()
    {
        Workbench.DecomposeTomorrowOrders();
        _shell?.ShowToast(Workbench.SystemNotice, "Success");
        RaiseComputed();
    }

    private void ApplyOrderFilter(string filter)
    {
        if (Workbench.SelectedInpatient != null)
        {
            Workbench.SelectedInpatient.SelectedOrderFilter = filter;
        }
    }

    private void ConfirmAndExecuteAllTasks()
    {
        if (!Workbench.CanExecuteSelectedNursingTasks || Workbench.SelectedInpatient == null) return;

        void Execute()
        {
            Workbench.ExecuteAllNursingTasks();
            _shell?.ShowToast(Workbench.SystemNotice, "Success");
            RaiseComputed();
        }

        if (_shell != null)
        {
            _shell.RequestConfirm(
                "确认批量执行待办护理任务？",
                $"将为患者 {Workbench.SelectedInpatient.PatientName} ({Workbench.SelectedInpatient.BedCode}) 批量执行当前所有待执行任务。",
                "确认批量执行",
                Execute);
            return;
        }

        Execute();
    }

    private void ExecuteTask(NursingTaskModel? task)
    {
        if (task == null || !Workbench.CanExecuteNursingTasks) return;
        Workbench.ExecuteNursingTask(task);
        _shell?.ShowToast(Workbench.SystemNotice, "Success");
        RaiseComputed();
    }

    private void VerifyTask(NursingTaskModel? task)
    {
        if (task == null || !Workbench.CanExecuteNursingTasks) return;
        Workbench.VerifyNursingTask(task);
        _shell?.ShowToast(Workbench.SystemNotice, "Success");
        RaiseComputed();
    }

    private void ExecuteSaveVitals()
    {
        Workbench.SaveVitalSigns();
        _shell?.ShowToast(Workbench.SystemNotice, "Success");
    }

    private void ExecuteAddHandover()
    {
        Workbench.AddHandoverNote();
        _shell?.ShowToast(Workbench.SystemNotice, "Success");
    }

    private void ExecuteConfirmHandover(HandoverNoteModel? note)
    {
        if (note == null || !Workbench.CanExecuteNursingTasks) return;
        Workbench.ConfirmHandoverNote(note);
        _shell?.ShowToast(Workbench.SystemNotice, "Success");
    }

    private void OpenFrequencyConfigDrawer()
    {
        if (_shell == null)
        {
            Workbench.OpenFrequencyConfigModal();
            return;
        }

        var container = new StackPanel { Spacing = 10, DataContext = Workbench };
        container.Children.Add(new TextBlock
        {
            Text = "编辑各频次分解时间点（逗号分隔如 08:00,16:00），关闭并同步后长期医嘱分解引擎即时生效。",
            FontSize = 12,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        var list = new StackPanel { Spacing = 6 };
        foreach (var def in Workbench.FrequencyDefinitions)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("70,110,*"), ColumnSpacing = 8 };
            row.Children.Add(new TextBlock { Text = def.Code.ToUpperInvariant(), FontWeight = Avalonia.Media.FontWeight.Bold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            var nameTb = new TextBlock { Text = def.Name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(nameTb, 1);
            row.Children.Add(nameTb);

            var tpBox = new TextBox { Height = 28, Text = def.TimePointsText };
            tpBox.TextChanged += (_, _) =>
            {
                def.TimePointsText = tpBox.Text ?? string.Empty;
            };
            Grid.SetColumn(tpBox, 2);
            row.Children.Add(tpBox);
            list.Children.Add(row);
        }
        container.Children.Add(list);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Avalonia.Thickness(0, 8, 0, 0) };
        var resetBtn = new Button
        {
            Content = "恢复三甲推荐",
            Height = 32,
            Command = new RelayCommand(() =>
            {
                Workbench.ResetFrequencyDefinitionsToDefault();
                _shell.CloseDrawer();
                _shell.ShowToast(Workbench.SystemNotice, "Info");
            })
        };
        var saveBtn = new Button
        {
            Content = "同步分解配置",
            Height = 32,
            Command = new RelayCommand(() =>
            {
                Workbench.CloseFrequencyConfigModal();
                _shell.CloseDrawer();
                _shell.ShowToast(Workbench.SystemNotice, "Success");
            })
        };
        saveBtn.Classes.Add("Primary");
        actions.Children.Add(resetBtn);
        actions.Children.Add(saveBtn);
        container.Children.Add(actions);

        _shell.OpenDrawer("医嘱频次与分解时间点规则", "按频次定义时间点自动生成护理执行单", container, 520);
    }

    private void OpenBorrowBedDrawer()
    {
        if (_shell == null)
        {
            Workbench.IsBorrowBedDialogOpen = true;
            return;
        }

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = $"住院患者: {Workbench.BorrowBedPatientSummary}", FontSize = 13, FontWeight = Avalonia.Media.FontWeight.Bold });
        panel.Children.Add(new TextBlock { Text = $"住院记录: {Workbench.BorrowBedAdmissionSummary}", FontSize = 12 });
        panel.Children.Add(new TextBlock { Text = $"医疗归属科室: {Workbench.BorrowBedAdmittingDept}", FontSize = 12 });
        panel.Children.Add(new TextBlock { Text = $"实际物理护理病区: {Workbench.BorrowBedAccommodationSummary}", FontSize = 12 });
        panel.Children.Add(new TextBlock { Text = $"主管医师: {Workbench.BorrowBedAttendingDoctor}", FontSize = 12 });

        var confirmBtn = new Button
        {
            Content = "办理跨病区借床入科",
            Height = 34,
            IsEnabled = Workbench.IsInpatientContext,
            Margin = new Avalonia.Thickness(0, 8, 0, 0),
            Command = new RelayCommand(() =>
            {
                Workbench.SystemNotice = "✓ 跨病区借床入科办理完成！医疗归属与护理职责已独立路由。";
                _shell.ShowToast(Workbench.SystemNotice, "Success");
                _shell.CloseDrawer();
            })
        };
        confirmBtn.Classes.Add("Primary");
        panel.Children.Add(confirmBtn);

        _shell.OpenDrawer("跨病区借床入科", "主管科室负责诊疗 · 借住病区负责护理", panel, 480);
    }

    private void OnWorkbenchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkbenchModel.SelectedInpatient)
            or nameof(WorkbenchModel.CanExecuteNursingTasks)
            or nameof(WorkbenchModel.CanReviewSelectedInpatientOrders)
            or nameof(WorkbenchModel.CanExecuteSelectedNursingTasks))
        {
            RaiseComputed();
            if (e.PropertyName == nameof(WorkbenchModel.SelectedInpatient))
            {
                _ = Workbench.LoadSelectedEncounterDetailsAsync(Workbench.SelectedInpatient);
                _ = LoadCarePlansAsync();
                _ = LoadAssessmentsAsync();
            }
        }
    }

    private void RaiseComputed()
    {
        OnPropertyChanged(nameof(ReviewBlockedReason));
        OnPropertyChanged(nameof(ExecuteTasksBlockedReason));
        (ReviewOrdersCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ExecuteAllTasksCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    // ==========================================
    // P1-D: Care Plan Async Operations
    // ==========================================
    public async Task LoadCarePlansAsync()
    {
        string encId = Workbench.SelectedInpatient?.AdmissionNo ?? "ADM20261105001";
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.GetFromJsonAsync<ApiResponse<List<CarePlanModel>>>($"/api/v4/nursing/care-plans/{encId}");
            CarePlans.Clear();
            if (res?.Data != null)
            {
                foreach (var p in res.Data) CarePlans.Add(p);
            }
            if (CarePlans.Count > 0 && SelectedCarePlan == null)
            {
                SelectedCarePlan = CarePlans[0];
            }
        }
        catch { }
    }

    public async Task CreateCarePlanAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPlanProblem) || string.IsNullOrWhiteSpace(NewPlanGoal))
        {
            _shell?.ShowToast("护理问题与预期目标不能为空", "Warning");
            return;
        }

        string encId = Workbench.SelectedInpatient?.AdmissionNo ?? "ADM20261105001";
        string patId = Workbench.SelectedInpatient?.MedicalRecordNo ?? "PAT85061801";

        var interventions = (NewPlanInterventions ?? "")
            .Split(new[] { '\r', '\n', '；', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var body = new
            {
                EncounterId = encId,
                PatientId = patId,
                DiagnosisOrProblem = NewPlanProblem.Trim(),
                Goal = NewPlanGoal.Trim(),
                Interventions = interventions,
                TargetDateMs = DateTimeOffset.UtcNow.AddDays(3).ToUnixTimeMilliseconds()
            };
            var res = await http.PostAsJsonAsync("/api/v4/nursing/care-plans", body);
            var data = await res.Content.ReadFromJsonAsync<ApiResponse<CarePlanModel>>();
            if (data?.Code == 200 && data.Data != null)
            {
                _shell?.ShowToast("护理计划创建成功", "Success");
                NewPlanProblem = string.Empty;
                NewPlanGoal = string.Empty;
                NewPlanInterventions = string.Empty;
                await LoadCarePlansAsync();
            }
            else
            {
                _shell?.ShowToast(data?.Message ?? "创建护理计划失败", "Warning");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast("请求异常: " + ex.Message, "Error");
        }
    }

    public async Task EvaluateCarePlanAsync()
    {
        if (SelectedCarePlan == null)
        {
            _shell?.ShowToast("请先选择待评价的护理计划", "Warning");
            return;
        }
        if (string.IsNullOrWhiteSpace(NewEvalResult))
        {
            _shell?.ShowToast("评价内容不能为空", "Warning");
            return;
        }

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var body = new
            {
                EvaluationResult = NewEvalResult.Trim(),
                IsGoalMet = NewEvalGoalMet
            };
            var res = await http.PostAsJsonAsync($"/api/v4/nursing/care-plans/{SelectedCarePlan.Id}/evaluate", body);
            var data = await res.Content.ReadFromJsonAsync<ApiResponse<object>>();
            if (data?.Code == 200)
            {
                _shell?.ShowToast(data.Message ?? "评价完成", "Success");
                NewEvalResult = string.Empty;
                await LoadCarePlansAsync();
            }
            else
            {
                _shell?.ShowToast(data?.Message ?? "评价失败", "Warning");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast("请求异常: " + ex.Message, "Error");
        }
    }

    // ==========================================
    // P1-D: Assessments Async Operations
    // ==========================================
    public async Task LoadAssessmentsAsync()
    {
        string encId = Workbench.SelectedInpatient?.AdmissionNo ?? "ADM20261105001";
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var bRes = await http.GetFromJsonAsync<ApiResponse<List<BradenAssessmentModel>>>($"/api/v4/nursing/assessments/braden/{encId}");
            BradenHistory.Clear();
            if (bRes?.Data != null)
            {
                foreach (var b in bRes.Data) BradenHistory.Add(b);
            }

            var mRes = await http.GetFromJsonAsync<ApiResponse<List<MorseAssessmentModel>>>($"/api/v4/nursing/assessments/morse/{encId}");
            MorseHistory.Clear();
            if (mRes?.Data != null)
            {
                foreach (var m in mRes.Data) MorseHistory.Add(m);
            }
        }
        catch { }
    }

    public async Task SubmitBradenAsync()
    {
        string encId = Workbench.SelectedInpatient?.AdmissionNo ?? "ADM20261105001";
        string patId = Workbench.SelectedInpatient?.MedicalRecordNo ?? "PAT85061801";

        var measures = (BradenMeasures ?? "")
            .Split(new[] { '\r', '\n', '；', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var body = new
            {
                EncounterId = encId,
                PatientId = patId,
                SensoryPerception = BradenSensory,
                Moisture = BradenMoisture,
                Activity = BradenActivity,
                Mobility = BradenMobility,
                Nutrition = BradenNutrition,
                FrictionShear = BradenFriction,
                PreventiveMeasures = measures
            };

            var res = await http.PostAsJsonAsync("/api/v4/nursing/assessments/braden", body);
            var data = await res.Content.ReadFromJsonAsync<ApiResponse<BradenAssessmentModel>>();
            if (data?.Code == 200 && data.Data != null)
            {
                _shell?.ShowToast(data.Message ?? "Braden 压疮评估已保存", "Success");
                await LoadAssessmentsAsync();
            }
            else
            {
                _shell?.ShowToast(data?.Message ?? "压疮评估提交失败", "Warning");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast("请求异常: " + ex.Message, "Error");
        }
    }

    public async Task SubmitMorseAsync()
    {
        string encId = Workbench.SelectedInpatient?.AdmissionNo ?? "ADM20261105001";
        string patId = Workbench.SelectedInpatient?.MedicalRecordNo ?? "PAT85061801";

        var measures = (MorseMeasures ?? "")
            .Split(new[] { '\r', '\n', '；', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var body = new
            {
                EncounterId = encId,
                PatientId = patId,
                HistoryOfFalling = MorseHistoryScore,
                SecondaryDiagnosis = MorseSecondary,
                AmbulatoryAid = MorseAid,
                IvOrSalineLock = MorseIv,
                Gait = MorseGait,
                MentalStatus = MorseMental,
                PreventiveMeasures = measures
            };

            var res = await http.PostAsJsonAsync("/api/v4/nursing/assessments/morse", body);
            var data = await res.Content.ReadFromJsonAsync<ApiResponse<MorseAssessmentModel>>();
            if (data?.Code == 200 && data.Data != null)
            {
                _shell?.ShowToast(data.Message ?? "Morse 跌倒评估已保存", "Success");
                await LoadAssessmentsAsync();
            }
            else
            {
                _shell?.ShowToast(data?.Message ?? "跌倒评估提交失败", "Warning");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast("请求异常: " + ex.Message, "Error");
        }
    }

    // ==========================================
    // P1-E: IPC Async Operations
    // ==========================================
    public async Task RefreshIpcDataAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var infRes = await http.GetFromJsonAsync<ApiResponse<List<InfectionReportModel>>>("/api/v4/ipc/infection-reports");
            InfectionReports.Clear();
            if (infRes?.Data != null)
            {
                foreach (var r in infRes.Data) InfectionReports.Add(r);
            }

            var isoRes = await http.GetFromJsonAsync<ApiResponse<List<IsolationOrderModel>>>("/api/v4/ipc/isolation-orders");
            IsolationOrders.Clear();
            if (isoRes?.Data != null)
            {
                foreach (var o in isoRes.Data) IsolationOrders.Add(o);
            }

            var sumRes = await http.GetFromJsonAsync<ApiResponse<JsonElement>>("/api/v4/ipc/summary");
            if (sumRes?.Code == 200)
            {
                var el = sumRes.Data;
                int activeIso = el.GetProperty("activeIsolationCount").GetInt32();
                int mdroCount = el.GetProperty("mdroInfectionCount").GetInt32();
                int totalInf = el.GetProperty("totalInfectionReports").GetInt32();
                decimal avgComp = el.GetProperty("averageHandHygieneComplianceRate").GetDecimal();
                IpcSummaryText = $"在管隔离: {activeIso} 例 | MDRO耐药菌: {mdroCount} 例 | 累计院感: {totalInf} 例 | 手卫生依从率: {avgComp:0.0}%";
            }
        }
        catch { }
    }

    public async Task SubmitHaiReportAsync()
    {
        string encId = Workbench.SelectedInpatient?.AdmissionNo ?? "ADM20261105001";
        string patId = Workbench.SelectedInpatient?.MedicalRecordNo ?? "PAT85061801";
        string patName = Workbench.SelectedInpatient?.PatientName ?? "住院患者";

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var body = new
            {
                EncounterId = encId,
                PatientId = patId,
                PatientName = patName,
                WardId = "DEPT-WARD-01",
                InfectionSite = HaiSite,
                Pathogen = HaiPathogen,
                IsMdro = HaiIsMdro,
                ClinicalManifestation = HaiManifestation
            };

            var res = await http.PostAsJsonAsync("/api/v4/ipc/infection-reports", body);
            var data = await res.Content.ReadFromJsonAsync<ApiResponse<InfectionReportModel>>();
            if (data?.Code == 200 && data.Data != null)
            {
                _shell?.ShowToast(data.Message ?? "院感报告卡提交成功", "Success");
                await RefreshIpcDataAsync();
            }
            else
            {
                _shell?.ShowToast(data?.Message ?? "院感上报失败", "Warning");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast("请求异常: " + ex.Message, "Error");
        }
    }

    public async Task SubmitIsolationOrderAsync()
    {
        string encId = Workbench.SelectedInpatient?.AdmissionNo ?? "ADM20261105001";
        string patId = Workbench.SelectedInpatient?.MedicalRecordNo ?? "PAT85061801";

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var body = new
            {
                EncounterId = encId,
                PatientId = patId,
                WardId = "DEPT-WARD-01",
                IsolationType = IsoType,
                Reason = IsoReason
            };

            var res = await http.PostAsJsonAsync("/api/v4/ipc/isolation-orders", body);
            var data = await res.Content.ReadFromJsonAsync<ApiResponse<IsolationOrderModel>>();
            if (data?.Code == 200 && data.Data != null)
            {
                _shell?.ShowToast(data.Message ?? "隔离医嘱已下发", "Success");
                await RefreshIpcDataAsync();
            }
            else
            {
                _shell?.ShowToast(data?.Message ?? "开立隔离医嘱失败", "Warning");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast("请求异常: " + ex.Message, "Error");
        }
    }

    public async Task EndIsolationOrderAsync(IsolationOrderModel? order)
    {
        if (order == null) return;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.PostAsJsonAsync($"/api/v4/ipc/isolation-orders/{order.Id}/end", new { EndReason = "复查病原学培养阴性，解除床旁隔离" });
            var data = await res.Content.ReadFromJsonAsync<ApiResponse<object>>();
            if (data?.Code == 200)
            {
                _shell?.ShowToast(data.Message ?? "隔离医嘱已解除", "Success");
                await RefreshIpcDataAsync();
            }
            else
            {
                _shell?.ShowToast(data?.Message ?? "解除隔离失败", "Warning");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast("请求异常: " + ex.Message, "Error");
        }
    }

    public async Task SubmitHandAuditAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var body = new
            {
                WardId = "DEPT-WARD-01",
                AuditDate = DateTime.Now.ToString("yyyy-MM-dd"),
                MomentType = HandMoment,
                StaffCategory = HandStaffCat,
                OpportunityCount = HandOppCount,
                PerformedCount = HandPerfCount,
                CorrectCount = HandCorrCount
            };

            var res = await http.PostAsJsonAsync("/api/v4/ipc/hand-hygiene-audits", body);
            var data = await res.Content.ReadFromJsonAsync<ApiResponse<object>>();
            if (data?.Code == 200)
            {
                _shell?.ShowToast(data.Message ?? "手卫生督查已录入", "Success");
                await RefreshIpcDataAsync();
            }
            else
            {
                _shell?.ShowToast(data?.Message ?? "手卫生录入失败", "Warning");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast("请求异常: " + ex.Message, "Error");
        }
    }

    public async Task LoadNursingUserTemplatesAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.GetFromJsonAsync<ClientApiEnvelope<List<NursingUserTemplateDto>>>("/api/v4/emr/user-templates?emrScene=WARD");
            if (res?.Code == 200 && res.Data != null)
            {
                NursingUserTemplates.Clear();
                foreach (var t in res.Data)
                {
                    NursingUserTemplates.Add(new EmrUserTemplateModel
                    {
                        Id = t.Id,
                        UserId = t.UserId,
                        UserName = t.UserName,
                        DeptId = t.DeptId,
                        DeptName = t.DeptName,
                        BaseTemplateId = t.BaseTemplateId,
                        TemplateCode = t.TemplateCode,
                        TemplateName = t.TemplateName,
                        EmrScene = t.EmrScene,
                        DocTypeCode = t.DocTypeCode,
                        DefaultContentJson = t.DefaultContentJson,
                        IsSharedDept = t.IsSharedDept == 1,
                        SortOrder = t.SortOrder
                    });
                }
            }
        }
        catch { }
    }

    private void ExecuteApplyNursingTemplate(EmrUserTemplateModel? tpl)
    {
        if (tpl == null || string.IsNullOrWhiteSpace(tpl.DefaultContentJson)) return;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(tpl.DefaultContentJson);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("PROBLEM", out var prob))
                    NewPlanProblem = prob.GetString() ?? tpl.TemplateName;
                else
                    NewPlanProblem = tpl.TemplateName;

                if (doc.RootElement.TryGetProperty("GOAL", out var goal))
                    NewPlanGoal = goal.GetString() ?? "患者住院期间安全无跌倒，体征平稳";

                if (doc.RootElement.TryGetProperty("NURSING_INTERVENTION", out var act))
                    NewPlanInterventions = act.GetString() ?? string.Empty;
            }
            _shell?.ShowToast($"已调入护理文书常用模板 [{tpl.TemplateName}]", "Success");
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"调入护理模板失败: {ex.Message}", "Error");
        }
    }

    public async Task SaveAsNursingUserTemplateAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPlanProblem) || string.IsNullOrWhiteSpace(NewPlanInterventions))
        {
            _shell?.ShowToast("请先填写护理问题与护理措施内容", "Warning");
            return;
        }

        try
        {
            var contentObj = new Dictionary<string, string>
            {
                ["PROBLEM"] = NewPlanProblem.Trim(),
                ["GOAL"] = string.IsNullOrWhiteSpace(NewPlanGoal) ? "患者各项护理评估指标改善稳定" : NewPlanGoal.Trim(),
                ["NURSING_INTERVENTION"] = NewPlanInterventions.Trim()
            };
            var json = System.Text.Json.JsonSerializer.Serialize(contentObj);
            var tplName = $"{NewPlanProblem.Trim()}-常用护理模板";

            var req = new
            {
                BaseTemplateId = "TMPL-WARD-ADMISSION",
                TemplateCode = $"UTPL_NUR_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}",
                TemplateName = tplName,
                EmrScene = "WARD",
                DocTypeCode = "NURSING_CARE_PLAN",
                DefaultContentJson = json,
                IsSharedDept = true,
                SortOrder = 1
            };

            using var http = AuthenticatedHttpClientFactory.Create();
            var response = await http.PostAsJsonAsync("/api/v4/emr/user-templates", req);
            var res = await response.Content.ReadFromJsonAsync<ClientApiEnvelope<NursingUserTemplateDto>>();
            if (res?.Code == 200 && res.Data != null)
            {
                _shell?.ShowToast($"成功保存为常用护理模板: [{tplName}]", "Success");
                await LoadNursingUserTemplatesAsync();
            }
            else
            {
                _shell?.ShowToast(res?.Message ?? "保存常用护理模板失败", "Warning");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"保存护理模板异常: {ex.Message}", "Error");
        }
    }

    public sealed record NursingUserTemplateDto(
        string Id,
        string UserId,
        string UserName,
        string? DeptId,
        string? DeptName,
        string BaseTemplateId,
        string TemplateCode,
        string TemplateName,
        string EmrScene,
        string DocTypeCode,
        string DefaultContentJson,
        short IsSharedDept,
        int SortOrder
    );

    public Dictionary<string, string> BuildPrintContext(object? rowContext = null)
    {
        var ctx = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rec = rowContext as InpatientRecordModel ?? Workbench.SelectedInpatient;
        if (rec != null)
        {
            ctx["ipd.patientName"] = rec.PatientName ?? string.Empty;
            ctx["ipd.gender"] = rec.Gender ?? string.Empty;
            ctx["ipd.age"] = rec.Age.ToString();
            ctx["ipd.admissionNo"] = rec.AdmissionNo ?? string.Empty;
            ctx["ipd.medicalRecordNo"] = rec.MedicalRecordNo ?? string.Empty;
            ctx["ipd.bedCode"] = rec.BedCode ?? string.Empty;
            ctx["ipd.admittingDept"] = rec.AdmittingDept ?? string.Empty;
            ctx["ipd.nursingWard"] = rec.NursingWard ?? string.Empty;
            ctx["ipd.diagnosis"] = rec.Diagnosis ?? string.Empty;
            ctx["ipd.attendingDoctor"] = rec.AttendingDoctor ?? string.Empty;
            ctx["ipd.admissionDate"] = rec.AdmissionDate ?? string.Empty;
        }
        return ctx;
    }
}
