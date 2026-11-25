using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Printing;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.PreAdmission;

public sealed class PreAdmissionSummaryDto
{
    public int ActiveCount { get; set; }
    public int PendingEvalCount { get; set; }
    public int CriticalInterruptedCount { get; set; }
    public int ReadyToAdmitCount { get; set; }
    public int ConvertedIpdCount { get; set; }
    public int ConvertedOpdCount { get; set; }
    public int ExpiringSoonCount { get; set; }
    public int DefaultValidDays { get; set; } = 7;
    public int CriticalEscalateMinutes { get; set; } = 30;
    public int EvalEscalateHours { get; set; } = 12;
}

public sealed class PreAdmissionConfigDto
{
    public int DefaultValidDays { get; set; } = 7;
    public int ExpiryWarnHours { get; set; } = 48;
    public int CriticalEscalateMinutes { get; set; } = 30;
    public int EvalEscalateHours { get; set; } = 12;
    public string AllowedFeeCategories { get; set; } = "LAB,EXAM,PREOP_PREP";
    public string BlockedFeeCategories { get; set; } = "BED,NURSING,IPD_CONSULT";
    public short RequireInformedConsent { get; set; } = 1;
    public short AllowDoctorPreSplit { get; set; } = 1;
    public short RequireFinanceSplitAudit { get; set; } = 1;
}

public sealed class PreAdmissionRuleRow
{
    public string Id { get; set; } = string.Empty;
    public string RuleCode { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string RuleLevel { get; set; } = "P0";
    public string SourceDiagPrefix { get; set; } = "*";
    public string MatchKeyword { get; set; } = string.Empty;
    public string TargetSpecialtyDept { get; set; } = string.Empty;
    public string OnHitAction { get; set; } = "INTERRUPT_AND_HOLD";
    public string? Remark { get; set; }
}

public sealed class PreAdmissionRow
{
    public string Id { get; set; } = string.Empty;
    public string PreAdmitNo { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string Gender { get; set; } = "男";
    public int Age { get; set; }
    public string? MedicalRecordNo { get; set; }
    public string TargetDeptName { get; set; } = string.Empty;
    public string PrimaryDiagName { get; set; } = string.Empty;
    public string? PlannedSurgeryName { get; set; }
    public int ValidDays { get; set; } = 7;
    public long RegisteredAtMs { get; set; }
    public long ValidUntilMs { get; set; }
    public string Status { get; set; } = "CHECKING";
    public int TotalOrderCount { get; set; }
    public int DoneOrderCount { get; set; }
    public short HasCriticalAlert { get; set; }
    public string? CriticalDiagName { get; set; }
    public string? CriticalSummary { get; set; }
    public string? ConvertedEncounterId { get; set; }
    public string? ConvertedDeptName { get; set; }
    public string? ConvertedPrimaryDiag { get; set; }

    public string StatusDisplay => Status switch
    {
        "REGISTERED" => "已登记",
        "CHECKING" => $"院前检查中 ({DoneOrderCount}/{TotalOrderCount})",
        "PENDING_EVALUATION" => "齐套·待准入评估",
        "CRITICAL_INTERRUPTED" => "⚠ P0危急值中断",
        "READY_TO_ADMIT" => "准予住院·待占床",
        "DEFER_PREP" => "暂缓调理中",
        "CONVERTED_IPD" => "已转正式住院",
        "ESCALATED_EMERGENCY" => "已紧急绿色通道收治",
        "CONVERTED_OPD" => "已中止转门诊结算",
        _ => Status
    };

    public string ValidUntilText => ValidUntilMs > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(ValidUntilMs).ToLocalTime().ToString("yyyy-MM-dd")
        : "-";
}

public sealed class PreAdmissionOrderRow : ClientModelBase
{
    private string _doctorSplitSuggest = "KEEP_IPD";
    private string _financeSplitConfirm = "KEEP_IPD";

    public string Id { get; set; } = string.Empty;
    public string PreAdmissionId { get; set; } = string.Empty;
    public string FeeCategory { get; set; } = "LAB";
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? ExecDeptName { get; set; }
    public decimal Amount { get; set; }
    public string ExecStatus { get; set; } = "PENDING";
    public string BillingTrack { get; set; } = "PRE_ADMIT_HOLD";
    public short IsCritical { get; set; }
    public string? ResultSummary { get; set; }

    public string DoctorSplitSuggest { get => _doctorSplitSuggest; set => SetField(ref _doctorSplitSuggest, value); }
    public string FinanceSplitConfirm { get => _financeSplitConfirm; set => SetField(ref _financeSplitConfirm, value); }

    public string ExecStatusDisplay => ExecStatus switch
    {
        "PENDING" => "待检查",
        "DONE" => IsCritical == 1 ? "已完成 [⚠危急值]" : "已完成",
        "HOLD_BY_CRITICAL" => "🔒 P0危急值锁停",
        "CANCELLED" => "已撤单退费",
        _ => ExecStatus
    };

    public string TrackDisplay => BillingTrack switch
    {
        "PRE_ADMIT_HOLD" => "预住院暂挂",
        "MERGED_TO_IPD" => "合并入住院",
        "SPLIT_TO_OPD" => "拆回门诊结算",
        "REFUNDED" => "已退费",
        _ => BillingTrack
    };

    public bool IsDone => ExecStatus == "DONE";
}

public sealed class PreAdmissionEvalRow
{
    public string Id { get; set; } = string.Empty;
    public string EvalType { get; set; } = string.Empty;
    public string RuleLevel { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public string? NewPrimaryDiag { get; set; }
    public string? NewTargetDept { get; set; }
    public string? ReasonNote { get; set; }
    public string? EvaluatorName { get; set; }
    public long EvaluatedAtMs { get; set; }

    public string TimeText => EvaluatedAtMs > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(EvaluatedAtMs).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
        : "-";
}

public sealed class PreAdmissionDetailEnvelope
{
    public PreAdmissionRow? Main { get; set; }
    public List<PreAdmissionOrderRow>? Orders { get; set; }
    public List<PreAdmissionEvalRow>? Evaluations { get; set; }
}

public sealed class PreBundleOption
{
    public string Id { get; set; } = string.Empty;
    public string BundleCode { get; set; } = string.Empty;
    public string BundleName { get; set; } = string.Empty;
    public decimal PackagePrice { get; set; }
    public override string ToString() => $"{BundleName} ({BundleCode} · ¥{PackagePrice:0.00})";
}

public sealed class PreAdmissionViewModel : ClientModelBase, IPrintContextProvider
{
    private readonly ShellViewModel? _shell;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private bool _isBusy;
    private string _statusMessage = "就绪";
    private int _selectedTabIndex;

    // KPI
    private int _activeCount;
    private int _pendingEvalCount;
    private int _criticalInterruptedCount;
    private int _readyToAdmitCount;
    private int _convertedIpdCount;
    private int _convertedOpdCount;
    private int _expiringSoonCount;

    // Filter & selection
    private string _filterStatus = "ALL";
    private string _searchKeyword = string.Empty;
    private PreAdmissionRow? _selectedMain;

    // Register form
    private string _regPatientName = string.Empty;
    private string _regGender = "男";
    private int _regAge = 48;
    private string _regTargetDept = "普外科";
    private string _regPrimaryDiag = "胆囊结石伴慢性胆囊炎";
    private string _regPlannedSurgery = "腹腔镜胆囊切除术";
    private bool _regConsentSigned = true;
    private PreBundleOption? _regSelectedBundle;

    // Add pre-order form (tests whitelist/blacklist!)
    private string _addOrderCat = "LAB";
    private string _addOrderName = string.Empty;
    private string _addOrderDept = "检验科";
    private decimal _addOrderAmount = 50m;

    // P0 Critical trigger form
    private string _critDiagName = "急性ST段抬高型心肌梗死 (B诊断)";
    private string _critSummary = "心电图提示II/III/aVF导联ST段弓背向上抬高0.3mV，肌钙蛋白I危急值显著升高";

    // Evaluation form
    private string _evalDecision = "APPROVE_ADMIT";
    private string _evalNewDiag = string.Empty;
    private string _evalNewDept = "心内科";
    private string _evalReason = "院前检查齐全，指征明确";

    // Formal admission form
    private string _admitBedCode = "08床";
    private string _admitWardName = "普外一病区";

    // Admin Config (Tab 4)
    private int _cfgValidDays = 7;
    private int _cfgExpiryWarnHours = 48;
    private int _cfgCritEscalateMin = 30;
    private int _cfgEvalEscalateHours = 12;
    private string _cfgAllowedCats = "LAB,EXAM,PREOP_PREP";
    private string _cfgBlockedCats = "BED,NURSING,IPD_CONSULT";

    // Admin Rule add
    private string _ruleLevel = "P0";
    private string _ruleName = string.Empty;
    private string _ruleKeywords = string.Empty;
    private string _ruleTargetDept = "急诊科/ICU";
    private string _ruleAction = "INTERRUPT_AND_HOLD";

    public PreAdmissionViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        RefreshCommand = new RelayCommand(() => _ = LoadAllAsync());
        SearchCommand = new RelayCommand(() => _ = LoadListAsync());
        RegisterCommand = new RelayCommand(() => _ = RegisterAsync());
        AddOrderCommand = new RelayCommand(() => _ = AddOrderAsync());
        TriggerCriticalCommand = new RelayCommand(() => _ = TriggerCriticalAsync());
        EvaluateCommand = new RelayCommand(() => _ = EvaluateAsync());
        ConvertToInpatientCommand = new RelayCommand(() => _ = ConvertToInpatientAsync());
        FinanceConfirmSplitCommand = new RelayCommand(() => _ = FinanceConfirmSplitAsync());
        SaveConfigCommand = new RelayCommand(() => _ = SaveConfigAsync());
        SaveRuleCommand = new RelayCommand(() => _ = SaveRuleAsync());
        DisableRuleCommand = new RelayCommand(p => _ = DisableRuleAsync(p as PreAdmissionRuleRow));

        _ = LoadAllAsync();
    }

    public WorkbenchModel Workbench { get; }

    public ObservableCollection<PreAdmissionRow> PreAdmissions { get; } = new();
    public ObservableCollection<PreAdmissionOrderRow> SelectedOrders { get; } = new();
    public ObservableCollection<PreAdmissionEvalRow> SelectedEvaluations { get; } = new();
    public ObservableCollection<PreBundleOption> Bundles { get; } = new();
    public ObservableCollection<PreAdmissionRuleRow> Rules { get; } = new();

    public IReadOnlyList<string> StatusFilterOptions { get; } = new[] { "ALL", "CRITICAL_INTERRUPTED", "PENDING_EVALUATION", "CHECKING", "READY_TO_ADMIT", "CONVERTED_IPD", "CONVERTED_OPD" };
    public IReadOnlyList<string> GenderOptions { get; } = new[] { "男", "女" };
    public IReadOnlyList<string> FeeCategoryOptions { get; } = new[] { "LAB", "EXAM", "PREOP_PREP", "BED", "NURSING", "IPD_CONSULT" };
    public IReadOnlyList<string> DecisionOptions { get; } = new[] { "APPROVE_ADMIT", "SWITCH_TO_B_IPD", "ESCALATE_EMERGENCY", "DEFER_PREP", "CANCEL_TO_OPD", "EXCLUDE_FALSE_CRITICAL" };
    public IReadOnlyList<string> SplitActionOptions { get; } = new[] { "KEEP_IPD", "SPLIT_OPD" };
    public IReadOnlyList<string> RuleLevelOptions { get; } = new[] { "P0", "P1", "P2" };
    public IReadOnlyList<string> RuleActionOptions { get; } = new[] { "INTERRUPT_AND_HOLD", "REQUIRE_CONSULT", "WARN_ONLY" };

    public ICommand RefreshCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand RegisterCommand { get; }
    public ICommand AddOrderCommand { get; }
    public ICommand TriggerCriticalCommand { get; }
    public ICommand EvaluateCommand { get; }
    public ICommand ConvertToInpatientCommand { get; }
    public ICommand FinanceConfirmSplitCommand { get; }
    public ICommand SaveConfigCommand { get; }
    public ICommand SaveRuleCommand { get; }
    public ICommand DisableRuleCommand { get; }

    public bool IsBusy { get => _isBusy; set => SetField(ref _isBusy, value); }
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
    public int SelectedTabIndex { get => _selectedTabIndex; set => SetField(ref _selectedTabIndex, value); }

    public int ActiveCount { get => _activeCount; set => SetField(ref _activeCount, value); }
    public int PendingEvalCount { get => _pendingEvalCount; set => SetField(ref _pendingEvalCount, value); }
    public int CriticalInterruptedCount { get => _criticalInterruptedCount; set => SetField(ref _criticalInterruptedCount, value); }
    public int ReadyToAdmitCount { get => _readyToAdmitCount; set => SetField(ref _readyToAdmitCount, value); }
    public int ConvertedIpdCount { get => _convertedIpdCount; set => SetField(ref _convertedIpdCount, value); }
    public int ConvertedOpdCount { get => _convertedOpdCount; set => SetField(ref _convertedOpdCount, value); }
    public int ExpiringSoonCount { get => _expiringSoonCount; set => SetField(ref _expiringSoonCount, value); }

    public string FilterStatus
    {
        get => _filterStatus;
        set { if (SetField(ref _filterStatus, value)) _ = LoadListAsync(); }
    }
    public string SearchKeyword { get => _searchKeyword; set => SetField(ref _searchKeyword, value); }

    public PreAdmissionRow? SelectedMain
    {
        get => _selectedMain;
        set
        {
            if (SetField(ref _selectedMain, value))
            {
                OnPropertyChanged(nameof(HasSelectedMain));
                if (value != null)
                {
                    if (value.Status == "CRITICAL_INTERRUPTED")
                    {
                        EvalDecision = "SWITCH_TO_B_IPD";
                        EvalNewDiag = value.CriticalDiagName ?? string.Empty;
                    }
                    else
                    {
                        EvalDecision = "APPROVE_ADMIT";
                    }
                    _ = LoadDetailAsync(value.Id);
                }
            }
        }
    }
    public bool HasSelectedMain => _selectedMain != null;

    public string RegPatientName { get => _regPatientName; set => SetField(ref _regPatientName, value); }
    public string RegGender { get => _regGender; set => SetField(ref _regGender, value); }
    public int RegAge { get => _regAge; set => SetField(ref _regAge, value); }
    public string RegTargetDept { get => _regTargetDept; set => SetField(ref _regTargetDept, value); }
    public string RegPrimaryDiag { get => _regPrimaryDiag; set => SetField(ref _regPrimaryDiag, value); }
    public string RegPlannedSurgery { get => _regPlannedSurgery; set => SetField(ref _regPlannedSurgery, value); }
    public bool RegConsentSigned { get => _regConsentSigned; set => SetField(ref _regConsentSigned, value); }
    public PreBundleOption? RegSelectedBundle { get => _regSelectedBundle; set => SetField(ref _regSelectedBundle, value); }

    public string AddOrderCat { get => _addOrderCat; set => SetField(ref _addOrderCat, value); }
    public string AddOrderName { get => _addOrderName; set => SetField(ref _addOrderName, value); }
    public string AddOrderDept { get => _addOrderDept; set => SetField(ref _addOrderDept, value); }
    public decimal AddOrderAmount { get => _addOrderAmount; set => SetField(ref _addOrderAmount, value); }

    public string CritDiagName { get => _critDiagName; set => SetField(ref _critDiagName, value); }
    public string CritSummary { get => _critSummary; set => SetField(ref _critSummary, value); }

    public string EvalDecision { get => _evalDecision; set => SetField(ref _evalDecision, value); }
    public string EvalNewDiag { get => _evalNewDiag; set => SetField(ref _evalNewDiag, value); }
    public string EvalNewDept { get => _evalNewDept; set => SetField(ref _evalNewDept, value); }
    public string EvalReason { get => _evalReason; set => SetField(ref _evalReason, value); }

    public string AdmitBedCode { get => _admitBedCode; set => SetField(ref _admitBedCode, value); }
    public string AdmitWardName { get => _admitWardName; set => SetField(ref _admitWardName, value); }

    public int CfgValidDays { get => _cfgValidDays; set => SetField(ref _cfgValidDays, Math.Clamp(value, 1, 60)); }
    public int CfgExpiryWarnHours { get => _cfgExpiryWarnHours; set => SetField(ref _cfgExpiryWarnHours, Math.Clamp(value, 6, 168)); }
    public int CfgCritEscalateMin { get => _cfgCritEscalateMin; set => SetField(ref _cfgCritEscalateMin, Math.Clamp(value, 5, 240)); }
    public int CfgEvalEscalateHours { get => _cfgEvalEscalateHours; set => SetField(ref _cfgEvalEscalateHours, Math.Clamp(value, 1, 72)); }
    public string CfgAllowedCats { get => _cfgAllowedCats; set => SetField(ref _cfgAllowedCats, value); }
    public string CfgBlockedCats { get => _cfgBlockedCats; set => SetField(ref _cfgBlockedCats, value); }

    public string RuleLevel { get => _ruleLevel; set => SetField(ref _ruleLevel, value); }
    public string RuleName { get => _ruleName; set => SetField(ref _ruleName, value); }
    public string RuleKeywords { get => _ruleKeywords; set => SetField(ref _ruleKeywords, value); }
    public string RuleTargetDept { get => _ruleTargetDept; set => SetField(ref _ruleTargetDept, value); }
    public string RuleAction { get => _ruleAction; set => SetField(ref _ruleAction, value); }

    public Dictionary<string, string> BuildPrintContext(object? rowContext = null)
    {
        var ctx = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var m = rowContext as PreAdmissionRow ?? SelectedMain;
        if (m != null)
        {
            ctx["preadmit.preAdmitNo"] = m.PreAdmitNo;
            ctx["preadmit.patientName"] = m.PatientName;
            ctx["preadmit.gender"] = m.Gender;
            ctx["preadmit.age"] = m.Age.ToString();
            ctx["preadmit.medicalRecordNo"] = m.MedicalRecordNo ?? string.Empty;
            ctx["preadmit.targetDeptName"] = m.TargetDeptName;
            ctx["preadmit.primaryDiagName"] = m.PrimaryDiagName;
            ctx["preadmit.plannedSurgeryName"] = m.PlannedSurgeryName ?? string.Empty;
            ctx["preadmit.validUntilText"] = m.ValidUntilText;
            ctx["patient.fullName"] = m.PatientName;
        }
        return ctx;
    }

    public async Task LoadAllAsync()
    {
        IsBusy = true;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var sumRes = await http.GetFromJsonAsync<ClientApiEnvelope<PreAdmissionSummaryDto>>("/api/v4/pre-admission/summary", JsonOpts);
            if (sumRes?.Data != null)
            {
                ActiveCount = sumRes.Data.ActiveCount;
                PendingEvalCount = sumRes.Data.PendingEvalCount;
                CriticalInterruptedCount = sumRes.Data.CriticalInterruptedCount;
                ReadyToAdmitCount = sumRes.Data.ReadyToAdmitCount;
                ConvertedIpdCount = sumRes.Data.ConvertedIpdCount;
                ConvertedOpdCount = sumRes.Data.ConvertedOpdCount;
                ExpiringSoonCount = sumRes.Data.ExpiringSoonCount;
            }

            var cfgRes = await http.GetFromJsonAsync<ClientApiEnvelope<PreAdmissionConfigDto>>("/api/v4/pre-admission/config", JsonOpts);
            if (cfgRes?.Data != null)
            {
                CfgValidDays = cfgRes.Data.DefaultValidDays;
                CfgExpiryWarnHours = cfgRes.Data.ExpiryWarnHours;
                CfgCritEscalateMin = cfgRes.Data.CriticalEscalateMinutes;
                CfgEvalEscalateHours = cfgRes.Data.EvalEscalateHours;
                CfgAllowedCats = cfgRes.Data.AllowedFeeCategories;
                CfgBlockedCats = cfgRes.Data.BlockedFeeCategories;
            }

            var ruleRes = await http.GetFromJsonAsync<ClientApiEnvelope<List<PreAdmissionRuleRow>>>("/api/v4/pre-admission/rules", JsonOpts);
            Rules.Clear();
            if (ruleRes?.Data != null) foreach (var r in ruleRes.Data) Rules.Add(r);

            var bndRes = await http.GetFromJsonAsync<ClientApiEnvelope<List<PreBundleOption>>>("/api/v4/catalog/bundles", JsonOpts);
            Bundles.Clear();
            if (bndRes?.Data != null) foreach (var b in bndRes.Data) Bundles.Add(b);
            RegSelectedBundle ??= Bundles.FirstOrDefault();

            await LoadListAsync();
            StatusMessage = $"已同步 {PreAdmissions.Count} 份预住院单（默认有效期 {CfgValidDays} 天，P0升级 {CfgCritEscalateMin} 分钟）";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败：{ex.Message}";
            _shell?.ShowToast(StatusMessage, "Error");
        }
        finally { IsBusy = false; }
    }

    private async Task LoadListAsync()
    {
        using var http = AuthenticatedHttpClientFactory.Create();
        var qs = new List<string>();
        if (FilterStatus != "ALL") qs.Add("status=" + Uri.EscapeDataString(FilterStatus));
        if (!string.IsNullOrWhiteSpace(SearchKeyword)) qs.Add("keyword=" + Uri.EscapeDataString(SearchKeyword.Trim()));
        string url = "/api/v4/pre-admission/list" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
        var res = await http.GetFromJsonAsync<ClientApiEnvelope<List<PreAdmissionRow>>>(url, JsonOpts);
        string? prevId = SelectedMain?.Id;
        PreAdmissions.Clear();
        if (res?.Data != null) foreach (var row in res.Data) PreAdmissions.Add(row);
        SelectedMain = (prevId != null ? PreAdmissions.FirstOrDefault(x => x.Id == prevId) : null) ?? PreAdmissions.FirstOrDefault();
    }

    private async Task LoadDetailAsync(string id)
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.GetFromJsonAsync<ClientApiEnvelope<PreAdmissionDetailEnvelope>>($"/api/v4/pre-admission/{id}", JsonOpts);
            SelectedOrders.Clear();
            SelectedEvaluations.Clear();
            if (res?.Data?.Orders != null)
            {
                foreach (var o in res.Data.Orders)
                {
                    if (string.IsNullOrWhiteSpace(o.FinanceSplitConfirm) || o.FinanceSplitConfirm == "PENDING")
                        o.FinanceSplitConfirm = o.DoctorSplitSuggest;
                    SelectedOrders.Add(o);
                }
            }
            if (res?.Data?.Evaluations != null)
                foreach (var e in res.Data.Evaluations) SelectedEvaluations.Add(e);
        }
        catch { }
    }

    private async Task RegisterAsync()
    {
        if (string.IsNullOrWhiteSpace(RegPatientName) || string.IsNullOrWhiteSpace(RegPrimaryDiag))
        {
            _shell?.ShowToast("请填写患者姓名与拟入院主诊断（A诊断）。", "Warning");
            return;
        }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("/api/v4/pre-admission/register", new
        {
            PatientName = RegPatientName.Trim(),
            Gender = RegGender,
            Age = RegAge,
            TargetDeptName = RegTargetDept,
            PrimaryDiagName = RegPrimaryDiag.Trim(),
            PlannedSurgeryName = RegPlannedSurgery,
            ConsentSigned = RegConsentSigned,
            BundleId = RegSelectedBundle?.Id
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if ((env?.Code is 200 or 0))
        {
            _shell?.ShowToast(env.Message ?? "预住院登记成功", "Success");
            RegPatientName = string.Empty;
            await LoadAllAsync();
        }
        else _shell?.ShowToast(env?.Message ?? "登记失败", "Error");
    }

    private async Task AddOrderAsync()
    {
        if (SelectedMain == null || string.IsNullOrWhiteSpace(AddOrderName))
        {
            _shell?.ShowToast("请先选择预住院单并输入项目名称。", "Warning");
            return;
        }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"/api/v4/pre-admission/{SelectedMain.Id}/orders", new
        {
            FeeCategory = AddOrderCat,
            ItemCode = "PRE-" + DateTime.Now.ToString("HHmmss"),
            ItemName = AddOrderName.Trim(),
            ExecDeptName = AddOrderDept,
            Amount = AddOrderAmount
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if ((env?.Code is 200 or 0))
        {
            _shell?.ShowToast(env.Message ?? "已加开院前检查", "Success");
            AddOrderName = string.Empty;
            await LoadAllAsync();
        }
        else _shell?.ShowToast(env?.Message ?? "开立被拦截", "Error");
    }

    private async Task TriggerCriticalAsync()
    {
        if (SelectedMain == null) return;
        var firstPending = SelectedOrders.FirstOrDefault(o => o.ExecStatus == "PENDING");
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"/api/v4/pre-admission/{SelectedMain.Id}/trigger-critical", new
        {
            OrderId = firstPending?.Id,
            CriticalDiagName = CritDiagName,
            CriticalSummary = CritSummary
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if ((env?.Code is 200 or 0))
        {
            _shell?.ShowToast(env.Message ?? "已触发 P0 危急值中断并锁停未做检查", "Warning");
            await LoadAllAsync();
        }
        else _shell?.ShowToast(env?.Message ?? "触发失败", "Error");
    }

    private async Task EvaluateAsync()
    {
        if (SelectedMain == null) return;
        var doctorSuggestions = SelectedOrders
            .Where(o => o.ExecStatus == "DONE")
            .Select(o => new { OrderId = o.Id, SuggestAction = o.DoctorSplitSuggest })
            .ToList();

        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"/api/v4/pre-admission/{SelectedMain.Id}/evaluate", new
        {
            Decision = EvalDecision,
            NewPrimaryDiag = EvalNewDiag,
            NewTargetDept = EvalNewDept,
            ReasonNote = EvalReason,
            DoctorSplitSuggestions = doctorSuggestions
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if ((env?.Code is 200 or 0))
        {
            _shell?.ShowToast(env.Message ?? "评估决策已生效", "Success");
            await LoadAllAsync();
        }
        else _shell?.ShowToast(env?.Message ?? "评估提交失败", "Error");
    }

    private async Task ConvertToInpatientAsync()
    {
        if (SelectedMain == null) return;
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"/api/v4/pre-admission/{SelectedMain.Id}/convert-to-inpatient", new
        {
            BedCode = AdmitBedCode,
            WardName = AdmitWardName
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if ((env?.Code is 200 or 0))
        {
            _shell?.ShowToast(env.Message ?? "已正式入科占床", "Success");
            await LoadAllAsync();
        }
        else _shell?.ShowToast(env?.Message ?? "转正式住院失败", "Error");
    }

    private async Task FinanceConfirmSplitAsync()
    {
        if (SelectedMain == null) return;
        var items = SelectedOrders
            .Where(o => o.ExecStatus == "DONE")
            .Select(o => new { OrderId = o.Id, FinalAction = o.FinanceSplitConfirm })
            .ToList();
        if (items.Count == 0)
        {
            _shell?.ShowToast("当前没有需要复核的已完成费用明细。", "Warning");
            return;
        }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"/api/v4/pre-admission/{SelectedMain.Id}/finance-confirm-split", new { Items = items });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if ((env?.Code is 200 or 0))
        {
            _shell?.ShowToast(env.Message ?? "财务/医保复合拆分已终审", "Success");
            await LoadAllAsync();
        }
        else _shell?.ShowToast(env?.Message ?? "复核失败", "Error");
    }

    private async Task SaveConfigAsync()
    {
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PutAsJsonAsync("/api/v4/pre-admission/config", new
        {
            DefaultValidDays = CfgValidDays,
            ExpiryWarnHours = CfgExpiryWarnHours,
            CriticalEscalateMinutes = CfgCritEscalateMin,
            EvalEscalateHours = CfgEvalEscalateHours,
            AllowedFeeCategories = CfgAllowedCats,
            BlockedFeeCategories = CfgBlockedCats,
            RequireInformedConsent = true,
            AllowDoctorPreSplit = true,
            RequireFinanceSplitAudit = true
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if ((env?.Code is 200 or 0))
        {
            _shell?.ShowToast(env.Message ?? "全局参数已保存", "Success");
            await LoadAllAsync();
        }
        else _shell?.ShowToast(env?.Message ?? "保存参数失败", "Error");
    }

    private async Task SaveRuleAsync()
    {
        if (string.IsNullOrWhiteSpace(RuleName) || string.IsNullOrWhiteSpace(RuleKeywords))
        {
            _shell?.ShowToast("请填写规则名称与匹配关键词。", "Warning");
            return;
        }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("/api/v4/pre-admission/rules", new
        {
            Id = (string?)null,
            RuleCode = $"{RuleLevel}-{DateTime.Now:HHmmss}",
            RuleName = RuleName.Trim(),
            RuleLevel = RuleLevel,
            SourceDiagPrefix = "*",
            MatchKeyword = RuleKeywords.Trim(),
            TargetSpecialtyDept = RuleTargetDept,
            OnHitAction = RuleAction
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if ((env?.Code is 200 or 0))
        {
            _shell?.ShowToast("新规则已保存生效。", "Success");
            RuleName = string.Empty;
            RuleKeywords = string.Empty;
            await LoadAllAsync();
        }
    }

    private async Task DisableRuleAsync(PreAdmissionRuleRow? rule)
    {
        if (rule == null) return;
        using var http = AuthenticatedHttpClientFactory.Create();
        await http.DeleteAsync($"/api/v4/pre-admission/rules/{rule.Id}");
        _shell?.ShowToast("规则已停用。", "Warning");
        await LoadAllAsync();
    }
}
