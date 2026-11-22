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

namespace HIS.Client.UrsaShell.Stations.MedicalAffairs;

public sealed class PrivilegeRow
{
    public string Id { get; set; } = string.Empty;
    public string GrantNo { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public string DeptName { get; set; } = string.Empty;
    public int PrescribeLevel { get; set; }
    public int SurgeryLevel { get; set; }
    public short NarcoticAuth { get; set; }
    public int ValidUntilInt { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public string? SuspendReason { get; set; }
}

public sealed class SafetyRcaRow
{
    public string Id { get; set; } = string.Empty;
    public string EventNo { get; set; } = string.Empty;
    public string SeverityLevel { get; set; } = string.Empty;
    public string EventCategory { get; set; } = string.Empty;
    public string DeptName { get; set; } = string.Empty;
    public string EventSummary { get; set; } = string.Empty;
    public string? RcaRootCause { get; set; }
    public string? PdcaActionPlan { get; set; }
    public string Status { get; set; } = "REPORTED";
}

public sealed class DisputeRow
{
    public string Id { get; set; } = string.Empty;
    public string DisputeNo { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string DeptName { get; set; } = string.Empty;
    public short MrmsSealed { get; set; }
    public string DisputeSummary { get; set; } = string.Empty;
    public decimal CompensationAmount { get; set; }
    public string Status { get; set; } = "OPEN";
    public string SealDisplay => MrmsSealed == 1 ? "🔒 已依法封存" : "未封存";
}

public sealed class NewTechRow
{
    public string Id { get; set; } = string.Empty;
    public string ProjectNo { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string DeptName { get; set; } = string.Empty;
    public string LeadDoctorName { get; set; } = string.Empty;
    public string RiskGrade { get; set; } = "MEDIUM";
    public string Status { get; set; } = "SUBMITTED";
    public int PerformedCases { get; set; }
    public int ComplicationCases { get; set; }
}

public sealed class MedicalAffairsViewModel : ClientModelBase
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly ShellViewModel _shell;

    private string _dashSummary = "加载医务质控总览...";
    private PrivilegeRow? _selectedPrivilege;
    private string _pUserId = string.Empty, _pDocName = string.Empty, _pDept = "普外科", _pSuspendReason = "发生非计划再手术暂扣权限";
    private int _pRxLevel = 2, _pSurgLevel = 3;
    private bool _pNarcotic = true;

    private SafetyRcaRow? _selectedEvent;
    private string _eSev = "LEVEL_III", _eCat = "用药/手术安全", _eDept = "内科", _eSum = string.Empty, _eRca = string.Empty, _ePdca = string.Empty;

    private DisputeRow? _selectedDispute;
    private string _dPat = string.Empty, _dDept = "外科", _dSum = string.Empty;

    private NewTechRow? _selectedTech;
    private string _tName = string.Empty, _tDept = "心内科", _tLead = string.Empty, _tRisk = "HIGH";

    public ObservableCollection<PrivilegeRow> Privileges { get; } = new();
    public ObservableCollection<SafetyRcaRow> SafetyEvents { get; } = new();
    public ObservableCollection<DisputeRow> Disputes { get; } = new();
    public ObservableCollection<NewTechRow> NewTechs { get; } = new();

    public string[] SeverityOptions { get; } = new[] { "LEVEL_I", "LEVEL_II", "LEVEL_III", "LEVEL_IV" };
    public string[] RiskOptions { get; } = new[] { "LOW", "MEDIUM", "HIGH" };

    public string DashSummary { get => _dashSummary; set => SetField(ref _dashSummary, value); }
    public PrivilegeRow? SelectedPrivilege { get => _selectedPrivilege; set => SetField(ref _selectedPrivilege, value); }
    public string PUserId { get => _pUserId; set => SetField(ref _pUserId, value); }
    public string PDocName { get => _pDocName; set => SetField(ref _pDocName, value); }
    public string PDept { get => _pDept; set => SetField(ref _pDept, value); }
    public int PRxLevel { get => _pRxLevel; set => SetField(ref _pRxLevel, value); }
    public int PSurgLevel { get => _pSurgLevel; set => SetField(ref _pSurgLevel, value); }
    public bool PNarcotic { get => _pNarcotic; set => SetField(ref _pNarcotic, value); }
    public string PSuspendReason { get => _pSuspendReason; set => SetField(ref _pSuspendReason, value); }

    public SafetyRcaRow? SelectedEvent { get => _selectedEvent; set => SetField(ref _selectedEvent, value); }
    public string ESev { get => _eSev; set => SetField(ref _eSev, value); }
    public string ECat { get => _eCat; set => SetField(ref _eCat, value); }
    public string EDept { get => _eDept; set => SetField(ref _eDept, value); }
    public string ESum { get => _eSum; set => SetField(ref _eSum, value); }
    public string ERca { get => _eRca; set => SetField(ref _eRca, value); }
    public string EPdca { get => _ePdca; set => SetField(ref _ePdca, value); }

    public DisputeRow? SelectedDispute { get => _selectedDispute; set => SetField(ref _selectedDispute, value); }
    public string DPat { get => _dPat; set => SetField(ref _dPat, value); }
    public string DDept { get => _dDept; set => SetField(ref _dDept, value); }
    public string DSum { get => _dSum; set => SetField(ref _dSum, value); }

    public NewTechRow? SelectedTech { get => _selectedTech; set => SetField(ref _selectedTech, value); }
    public string TName { get => _tName; set => SetField(ref _tName, value); }
    public string TDept { get => _tDept; set => SetField(ref _tDept, value); }
    public string TLead { get => _tLead; set => SetField(ref _tLead, value); }
    public string TRisk { get => _tRisk; set => SetField(ref _tRisk, value); }

    public ICommand RefreshAllCommand { get; }
    public ICommand GrantPrivilegeCommand { get; }
    public ICommand SuspendPrivilegeCommand { get; }
    public ICommand ReportEventCommand { get; }
    public ICommand SaveRcaCommand { get; }
    public ICommand CreateDisputeCommand { get; }
    public ICommand SealDisputeMrmsCommand { get; }
    public ICommand CreateNewTechCommand { get; }
    public ICommand ApproveNewTechCommand { get; }

    public MedicalAffairsViewModel(WorkbenchModel workbench, ShellViewModel shell)
    {
        _shell = shell;
        RefreshAllCommand = new RelayCommand(async () => await RefreshAllAsync());
        GrantPrivilegeCommand = new RelayCommand(async () => await GrantPrivilegeAsync());
        SuspendPrivilegeCommand = new RelayCommand(async () => await SuspendPrivilegeAsync());
        ReportEventCommand = new RelayCommand(async () => await ReportEventAsync());
        SaveRcaCommand = new RelayCommand(async () => await SaveRcaAsync());
        CreateDisputeCommand = new RelayCommand(async () => await CreateDisputeAsync());
        SealDisputeMrmsCommand = new RelayCommand(async () => await SealDisputeMrmsAsync());
        CreateNewTechCommand = new RelayCommand(async () => await CreateNewTechAsync());
        ApproveNewTechCommand = new RelayCommand(async () => await ApproveNewTechAsync());
        _ = RefreshAllAsync();
    }

    private async Task RefreshAllAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            await LoadListAsync(http, "api/v4/medical-affairs/privileges", Privileges);
            await LoadListAsync(http, "api/v4/medical-affairs/safety-events", SafetyEvents);
            await LoadListAsync(http, "api/v4/medical-affairs/disputes", Disputes);
            await LoadListAsync(http, "api/v4/medical-affairs/new-tech", NewTechs);

            var d = await http.GetFromJsonAsync<ClientApiEnvelope<JsonElement>>("api/v4/medical-affairs/dashboard", JsonOpts);
            if (d?.Code == 0 && d.Data.ValueKind == JsonValueKind.Object)
            {
                DashSummary = $"有效医师授权 {d.Data.GetProperty("activePrivileges").GetInt32()} · 停权 {d.Data.GetProperty("suspendedPrivileges").GetInt32()} · Ⅰ/Ⅱ级警讯事件 {d.Data.GetProperty("openSentinelEvents").GetInt32()} · 在办纠纷 {d.Data.GetProperty("openDisputes").GetInt32()} · 准入新技术 {d.Data.GetProperty("approvedNewTechs").GetInt32()}";
            }
        }
        catch (Exception ex) { _shell.ShowToast($"刷新医务数据失败：{ex.Message}", "Error"); }
    }

    private static async Task LoadListAsync<T>(System.Net.Http.HttpClient http, string url, ObservableCollection<T> target)
    {
        var env = await http.GetFromJsonAsync<ClientApiEnvelope<List<T>>>(url, JsonOpts);
        target.Clear();
        if (env?.Data != null) foreach (var it in env.Data) target.Add(it);
    }

    private async Task GrantPrivilegeAsync()
    {
        if (string.IsNullOrWhiteSpace(PUserId) || string.IsNullOrWhiteSpace(PDocName)) { _shell.ShowToast("请填写医师账号与姓名", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/medical-affairs/privileges", new { UserId = PUserId, DoctorName = PDocName, DeptName = PDept, PrescribeLevel = PRxLevel, SurgeryLevel = PSurgLevel, NarcoticAuth = PNarcotic, ValidUntilInt = 20271231 });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "授权已生效", env?.Code == 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task SuspendPrivilegeAsync()
    {
        if (SelectedPrivilege == null) { _shell.ShowToast("请先选择一条授权记录", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PutAsJsonAsync($"api/v4/medical-affairs/privileges/{SelectedPrivilege.Id}/suspend", new { Reason = PSuspendReason });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "已停权", env?.Code == 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task ReportEventAsync()
    {
        if (string.IsNullOrWhiteSpace(ESum)) { _shell.ShowToast("请填写事件摘要", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/medical-affairs/safety-events", new { SeverityLevel = ESev, EventCategory = ECat, DeptName = EDept, EventSummary = ESum });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "上报成功", env?.Code == 0 ? "Success" : "Error");
        ESum = string.Empty;
        await RefreshAllAsync();
    }

    private async Task SaveRcaAsync()
    {
        if (SelectedEvent == null) { _shell.ShowToast("请先选择一条安全事件", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PutAsJsonAsync($"api/v4/medical-affairs/safety-events/{SelectedEvent.Id}/rca", new { RcaRootCause = ERca, PdcaActionPlan = EPdca, Status = "CLOSED" });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "RCA/PDCA 已归档", env?.Code == 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task CreateDisputeAsync()
    {
        if (string.IsNullOrWhiteSpace(DPat) || string.IsNullOrWhiteSpace(DSum)) { _shell.ShowToast("请填写患者与纠纷摘要", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/medical-affairs/disputes", new { PatientName = DPat, DeptName = DDept, MrmsCaseId = (string?)null, DisputeSummary = DSum, CompensationAmount = 0m });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "已立案", env?.Code == 0 ? "Success" : "Error");
        DPat = string.Empty; DSum = string.Empty;
        await RefreshAllAsync();
    }

    private async Task SealDisputeMrmsAsync()
    {
        if (SelectedDispute == null) { _shell.ShowToast("请先选择一条纠纷专案", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsync($"api/v4/medical-affairs/disputes/{SelectedDispute.Id}/seal-mrms", null);
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "已封存", env?.Code == 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task CreateNewTechAsync()
    {
        if (string.IsNullOrWhiteSpace(TName) || string.IsNullOrWhiteSpace(TLead)) { _shell.ShowToast("请填写新技术名称与负责人", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/medical-affairs/new-tech", new { ProjectName = TName, DeptName = TDept, LeadDoctorName = TLead, RiskGrade = TRisk, EthicsApproved = true });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "已申报", env?.Code == 0 ? "Success" : "Error");
        TName = string.Empty;
        await RefreshAllAsync();
    }

    private async Task ApproveNewTechAsync()
    {
        if (SelectedTech == null) { _shell.ShowToast("请先选择新技术项目", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PutAsJsonAsync($"api/v4/medical-affairs/new-tech/{SelectedTech.Id}/review", new { Status = "APPROVED", PerformedCases = SelectedTech.PerformedCases + 1, ComplicationCases = SelectedTech.ComplicationCases, ReviewComment = "医务科与伦理委员会准予临床开展" });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "已准入", env?.Code == 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }
}
