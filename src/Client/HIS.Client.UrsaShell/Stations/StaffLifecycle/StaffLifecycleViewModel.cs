using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.StaffLifecycle;

public sealed class OnboardingRow
{
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public short LicenseVerified { get; set; }
    public short IpcTrainingPassed { get; set; }
    public short CaCertIssued { get; set; }
    public short MedPrivilegeGranted { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class LeaveRow
{
    public string Id { get; set; } = string.Empty;
    public string LeaveNo { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string LeaveType { get; set; } = string.Empty;
    public string DelegateUserId { get; set; } = string.Empty;
    public int MigratedApptsCnt { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class ExternalExpertRow
{
    public string FilingNo { get; set; } = string.Empty;
    public string ExpertName { get; set; } = string.Empty;
    public string OriginHospital { get; set; } = string.Empty;
    public string TempUserId { get; set; } = string.Empty;
    public int AllowedSurgeryLevel { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class RotationRow
{
    public string EmployeeName { get; set; } = string.Empty;
    public string TargetDeptName { get; set; } = string.Empty;
    public int StartDateInt { get; set; }
    public int EndDateInt { get; set; }
    public short AutoSwitched { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class StaffLifecycleViewModel : ClientModelBase
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly ShellViewModel _shell;

    private string _summaryBanner = "加载医院职工全生命周期治理数据...";
    private string _offboardAuditDetail = "请输入职工ID并点击“扫描该职工 5 项在途业务”查看在院管床、未签病历、未来排班与待办审批清零状态。";

    private string _obEmpId = string.Empty, _obUserId = string.Empty, _obName = string.Empty;
    private OnboardingRow? _selectedOnboarding;

    private string _lvType = "STUDY_LEAVE", _lvEmpId = string.Empty, _lvUserId = string.Empty, _lvName = string.Empty, _lvDelegateUserId = string.Empty;
    private LeaveRow? _selectedLeave;

    private string _offEmpId = string.Empty, _offHandoverTo = string.Empty, _offTargetDeptId = "DEPT-IPD-02";
    private string _rehireEmpId = string.Empty, _extName = string.Empty, _extHospital = "北京协和医院", _extLicense = "110110000012345";
    private string _rotEmpId = string.Empty, _rotUserId = string.Empty, _rotName = string.Empty, _rotDeptId = "DEPT-ICU-01";

    public ObservableCollection<OnboardingRow> Onboardings { get; } = new();
    public ObservableCollection<LeaveRow> Leaves { get; } = new();
    public ObservableCollection<ExternalExpertRow> ExternalExperts { get; } = new();
    public ObservableCollection<RotationRow> Rotations { get; } = new();

    public string[] LeaveTypes { get; } = new[] { "ANNUAL", "MATERNITY", "STUDY_LEAVE", "SUPPORT" };

    public string SummaryBanner { get => _summaryBanner; set => SetField(ref _summaryBanner, value); }
    public string OffboardAuditDetail { get => _offboardAuditDetail; set => SetField(ref _offboardAuditDetail, value); }

    public string ObEmpId { get => _obEmpId; set => SetField(ref _obEmpId, value); }
    public string ObUserId { get => _obUserId; set => SetField(ref _obUserId, value); }
    public string ObName { get => _obName; set => SetField(ref _obName, value); }
    public OnboardingRow? SelectedOnboarding { get => _selectedOnboarding; set => SetField(ref _selectedOnboarding, value); }

    public string LvType { get => _lvType; set => SetField(ref _lvType, value); }
    public string LvEmpId { get => _lvEmpId; set => SetField(ref _lvEmpId, value); }
    public string LvUserId { get => _lvUserId; set => SetField(ref _lvUserId, value); }
    public string LvName { get => _lvName; set => SetField(ref _lvName, value); }
    public string LvDelegateUserId { get => _lvDelegateUserId; set => SetField(ref _lvDelegateUserId, value); }
    public LeaveRow? SelectedLeave { get => _selectedLeave; set => SetField(ref _selectedLeave, value); }

    public string OffEmpId { get => _offEmpId; set => SetField(ref _offEmpId, value); }
    public string OffHandoverTo { get => _offHandoverTo; set => SetField(ref _offHandoverTo, value); }
    public string OffTargetDeptId { get => _offTargetDeptId; set => SetField(ref _offTargetDeptId, value); }

    public string RehireEmpId { get => _rehireEmpId; set => SetField(ref _rehireEmpId, value); }
    public string ExtName { get => _extName; set => SetField(ref _extName, value); }
    public string ExtHospital { get => _extHospital; set => SetField(ref _extHospital, value); }
    public string ExtLicense { get => _extLicense; set => SetField(ref _extLicense, value); }

    public string RotEmpId { get => _rotEmpId; set => SetField(ref _rotEmpId, value); }
    public string RotUserId { get => _rotUserId; set => SetField(ref _rotUserId, value); }
    public string RotName { get => _rotName; set => SetField(ref _rotName, value); }
    public string RotDeptId { get => _rotDeptId; set => SetField(ref _rotDeptId, value); }

    public ICommand RefreshAllCommand { get; }
    public ICommand InitOnboardingCommand { get; }
    public ICommand VerifyAllOnboardingStepsCommand { get; }
    public ICommand ActivateOnboardingCommand { get; }
    public ICommand ApplyLeaveCommand { get; }
    public ICommand ReturnLeaveCommand { get; }
    public ICommand AuditOffboardingCommand { get; }
    public ICommand ExecuteOffboardingCommand { get; }
    public ICommand TransferWithClearanceCommand { get; }
    public ICommand RehireExpertCommand { get; }
    public ICommand RegisterExternalExpertCommand { get; }
    public ICommand ScheduleRotationCommand { get; }
    public ICommand RunGovernanceSweepCommand { get; }

    public StaffLifecycleViewModel(WorkbenchModel workbench, ShellViewModel shell)
    {
        _shell = shell;
        RefreshAllCommand = new RelayCommand(async () => await RefreshAllAsync());
        InitOnboardingCommand = new RelayCommand(async () => await InitOnboardingAsync());
        VerifyAllOnboardingStepsCommand = new RelayCommand(async () => await VerifyAllOnboardingStepsAsync());
        ActivateOnboardingCommand = new RelayCommand(async () => await ActivateOnboardingAsync());
        ApplyLeaveCommand = new RelayCommand(async () => await ApplyLeaveAsync());
        ReturnLeaveCommand = new RelayCommand(async () => await ReturnLeaveAsync());
        AuditOffboardingCommand = new RelayCommand(async () => await AuditOffboardingAsync());
        ExecuteOffboardingCommand = new RelayCommand(async () => await ExecuteOffboardingAsync());
        TransferWithClearanceCommand = new RelayCommand(async () => await TransferWithClearanceAsync());
        RehireExpertCommand = new RelayCommand(async () => await RehireExpertAsync());
        RegisterExternalExpertCommand = new RelayCommand(async () => await RegisterExternalExpertAsync());
        ScheduleRotationCommand = new RelayCommand(async () => await ScheduleRotationAsync());
        RunGovernanceSweepCommand = new RelayCommand(async () => await RunGovernanceSweepAsync());
        _ = RefreshAllAsync();
    }

    private async Task RefreshAllAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            await LoadListAsync(http, "api/v4/hr/lifecycle/onboarding", Onboardings);
            await LoadListAsync(http, "api/v4/hr/lifecycle/leaves", Leaves);
            await LoadListAsync(http, "api/v4/hr/lifecycle/external-experts", ExternalExperts);
            await LoadListAsync(http, "api/v4/hr/lifecycle/rotations", Rotations);
            SummaryBanner = $"待转正准入 {Onboardings.Count} · 请假/外出代理 {Leaves.Count} · 外院专家备案 {ExternalExperts.Count} · 住培轮转 {Rotations.Count}";
        }
        catch (Exception ex) { _shell.ShowToast($"刷新职工全生命周期数据失败：{ex.Message}", "Error"); }
    }

    private static async Task LoadListAsync<T>(System.Net.Http.HttpClient http, string url, ObservableCollection<T> target)
    {
        var env = await http.GetFromJsonAsync<ClientApiEnvelope<List<T>>>(url, JsonOpts);
        target.Clear();
        if (env?.Data != null) foreach (var it in env.Data) target.Add(it);
    }

    private async Task InitOnboardingAsync()
    {
        if (string.IsNullOrWhiteSpace(ObEmpId) || string.IsNullOrWhiteSpace(ObName)) { _shell.ShowToast("请填写职工ID与姓名", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/hr/lifecycle/onboarding", new { EmployeeId = ObEmpId, UserId = ObUserId, EmployeeName = ObName });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Code is 200 or 0 ? "已建立上岗准入清单" : (env?.Message ?? "建单失败"), env?.Code is 200 or 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task VerifyAllOnboardingStepsAsync()
    {
        if (SelectedOnboarding == null) { _shell.ShowToast("请先选择一条准入清单", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"api/v4/hr/lifecycle/onboarding/{SelectedOnboarding.EmployeeId}/verify-step", new
        {
            LicenseVerified = true, IpcTrainingPassed = true, CaCertIssued = true, MedPrivilegeGranted = true
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Code is 200 or 0 ? "4项上岗合规核验已全部绿灯通过" : (env?.Message ?? "核验失败"), env?.Code is 200 or 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task ActivateOnboardingAsync()
    {
        if (SelectedOnboarding == null) { _shell.ShowToast("请先选择一条准入清单", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"api/v4/hr/lifecycle/onboarding/{SelectedOnboarding.EmployeeId}/activate", new { PrescribeLevel = 2, SurgeryLevel = 2, NarcoticAuth = false });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "已激活", env?.Code is 200 or 0 ? "Success" : "Warning");
        await RefreshAllAsync();
    }

    private async Task ApplyLeaveAsync()
    {
        if (string.IsNullOrWhiteSpace(LvEmpId) || string.IsNullOrWhiteSpace(LvDelegateUserId)) { _shell.ShowToast("请填写离岗职工与临床代理人", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var resp = await http.PostAsJsonAsync("api/v4/hr/lifecycle/leaves/apply", new
        {
            EmployeeId = LvEmpId, UserId = LvUserId, EmployeeName = LvName, LeaveType = LvType,
            StartAtMs = now, EndAtMs = now + (14L * 86400_000L), DelegateUserId = LvDelegateUserId, Reason = "外出进修/休假"
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Code is 200 or 0 ? "已生效离岗并自动托管管床与排班" : (env?.Message ?? "申请失败"), env?.Code is 200 or 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task ReturnLeaveAsync()
    {
        if (SelectedLeave == null) { _shell.ShowToast("请先选择一条离岗代理单", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsync($"api/v4/hr/lifecycle/leaves/{SelectedLeave.Id}/return-duty", null);
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "已销假复岗", env?.Code is 200 or 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task AuditOffboardingAsync()
    {
        if (string.IsNullOrWhiteSpace(OffEmpId)) { _shell.ShowToast("请填写职工ID", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var env = await http.GetFromJsonAsync<ClientApiEnvelope<JsonElement>>($"api/v4/hr/lifecycle/offboarding/{OffEmpId}/audit", JsonOpts);
        if (env?.Data.ValueKind == JsonValueKind.Object)
        {
            OffboardAuditDetail = $"职工：{env.Data.GetProperty("employeeName").GetString()}\n" +
                $"· 在院管床患者：{env.Data.GetProperty("pendingInpatients").GetInt32()} 人\n" +
                $"· 未审签病历：{env.Data.GetProperty("unsignedEmrDocuments").GetInt32()} 份\n" +
                $"· 未来有效排班：{env.Data.GetProperty("futureActiveScheduleSlots").GetInt32()} 个\n" +
                $"· 待办工作流：{env.Data.GetProperty("pendingWorkflowTasks").GetInt32()} 项\n" +
                $"· 未结会诊：{env.Data.GetProperty("pendingConsultations").GetInt32()} 项\n" +
                $"· 是否可直接离职：{(env.Data.GetProperty("canDirectOffboard").GetBoolean() ? "✅ 可直接办理" : "⛔ 存在在途业务，必须指定接替人交接")}";
        }
    }

    private async Task ExecuteOffboardingAsync()
    {
        if (string.IsNullOrWhiteSpace(OffEmpId)) return;
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"api/v4/hr/lifecycle/offboarding/{OffEmpId}/execute", new { SceneType = "RESIGN", HandoverToUserId = OffHandoverTo, Reason = "个人离职" });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "离职办理完成", env?.Code is 200 or 0 ? "Success" : "Warning");
        await AuditOffboardingAsync();
    }

    private async Task TransferWithClearanceAsync()
    {
        if (string.IsNullOrWhiteSpace(OffEmpId)) return;
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"api/v4/hr/lifecycle/transfer-with-clearance/{OffEmpId}", new { NewDeptId = OffTargetDeptId, NewDeptCategory = "IPD", HandoverToUserId = OffHandoverTo, Reason = "院内跨科调任" });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "调科完成", env?.Code is 200 or 0 ? "Success" : "Warning");
    }

    private async Task RehireExpertAsync()
    {
        if (string.IsNullOrWhiteSpace(RehireEmpId)) { _shell.ShowToast("请填写退休专家职工ID", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"api/v4/hr/lifecycle/rehire/{RehireEmpId}", new { ContractEndDateInt = int.Parse(DateTime.UtcNow.AddYears(1).ToString("yyyyMMdd")), PrescribeLevel = 3 });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Code is 200 or 0 ? "已激活退休专家返聘(保留专家门诊与CA)" : (env?.Message ?? "返聘失败"), env?.Code is 200 or 0 ? "Success" : "Warning");
    }

    private async Task RegisterExternalExpertAsync()
    {
        if (string.IsNullOrWhiteSpace(ExtName)) { _shell.ShowToast("请填写外院专家姓名", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/hr/lifecycle/external-experts/register", new { ExpertName = ExtName, OriginHospital = ExtHospital, LicenseNo = ExtLicense, AllowedSurgeryLevel = 4, ValidDays = 7 });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Code is 200 or 0 ? "已签发 7 天外院专家限时备案与临时手术账号" : (env?.Message ?? "备案失败"), env?.Code is 200 or 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task ScheduleRotationAsync()
    {
        if (string.IsNullOrWhiteSpace(RotEmpId) || string.IsNullOrWhiteSpace(RotUserId)) return;
        using var http = AuthenticatedHttpClientFactory.Create();
        int today = int.Parse(DateTime.UtcNow.ToString("yyyyMMdd"));
        int end = int.Parse(DateTime.UtcNow.AddDays(30).ToString("yyyyMMdd"));
        var resp = await http.PostAsJsonAsync("api/v4/hr/lifecycle/rotations", new { EmployeeId = RotEmpId, UserId = RotUserId, EmployeeName = RotName, TargetDeptId = RotDeptId, TargetDeptName = RotDeptId, StartDateInt = today, EndDateInt = end });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Code is 200 or 0 ? "跨科轮转计划已排定" : (env?.Message ?? "排定失败"), env?.Code is 200 or 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task RunGovernanceSweepAsync()
    {
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsync("api/v4/hr/lifecycle/governance/run-expiry-sweep", null);
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Code is 200 or 0 ? "已完成证照超期停权、外院备案熔断与到期轮转切科巡检" : (env?.Message ?? "巡检失败"), "Info");
        await RefreshAllAsync();
    }
}
