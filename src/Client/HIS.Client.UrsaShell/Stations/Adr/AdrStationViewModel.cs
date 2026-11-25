using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.BuildingBlocks.Common;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Services;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.Adr;

public sealed class AdrReportVm
{
    public string Id { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string DrugName { get; set; } = string.Empty;
    public string ReactionName { get; set; } = string.Empty;
    public string Severity { get; set; } = "GENERAL";
    public string Outcome { get; set; } = string.Empty;
    public string CausalityGrade { get; set; } = string.Empty;
    public string ReportStatus { get; set; } = string.Empty;
    public string? ExternalReportNo { get; set; }
    public string? KbRuleGeneratedId { get; set; }
    public string LineSummary => $"[{Severity}] {DrugName} -> {ReactionName} · {ReportStatus}"
        + (string.IsNullOrWhiteSpace(ExternalReportNo) ? "" : $" · 国家号 {ExternalReportNo}");
}

public sealed class AdrStationViewModel : ClientModelBase, IStationLifecycle
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly WorkbenchModel _workbench;
    private readonly ShellViewModel? _shell;

    private string _patientId = "PAT85061801";
    private string _drugName = "注射用头孢曲松钠";
    private string _reactionName = "斑丘疹与皮肤瘙痒";
    private string _severity = "GENERAL";
    private string _outcome = "停药对症后好转";
    private string _causalityGrade = "很可能";
    private string _reactionDesc = "输注30分钟后全身泛发荨麻疹样斑丘疹，停药静注地塞米松缓解";
    private AdrReportVm? _selectedReport;
    private string _statusMessage = "药品不良反应监测 (ADR) · 自动回写临床规则并直报国家监测网关";

    public AdrStationViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        _workbench = workbench;
        _shell = shell;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        SubmitAdrCommand = new AsyncRelayCommand(SubmitAsync);
        SubmitExternalCommand = new AsyncRelayCommand(SubmitExternalAsync);
    }

    public ObservableCollection<AdrReportVm> Reports { get; } = new();
    public IReadOnlyList<string> SeverityOptions { get; } = new[] { "GENERAL", "SERIOUS" };
    public IReadOnlyList<string> CausalityOptions { get; } = new[] { "肯定", "很可能", "可能", "可能无关" };

    public string PatientId { get => _patientId; set => SetField(ref _patientId, value ?? string.Empty); }
    public string DrugName { get => _drugName; set => SetField(ref _drugName, value ?? string.Empty); }
    public string ReactionName { get => _reactionName; set => SetField(ref _reactionName, value ?? string.Empty); }
    public string Severity { get => _severity; set => SetField(ref _severity, value ?? "GENERAL"); }
    public string Outcome { get => _outcome; set => SetField(ref _outcome, value ?? string.Empty); }
    public string CausalityGrade { get => _causalityGrade; set => SetField(ref _causalityGrade, value ?? "很可能"); }
    public string ReactionDesc { get => _reactionDesc; set => SetField(ref _reactionDesc, value ?? string.Empty); }
    public AdrReportVm? SelectedReport { get => _selectedReport; set => SetField(ref _selectedReport, value); }
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value ?? string.Empty); }

    public ICommand RefreshCommand { get; }
    public ICommand SubmitAdrCommand { get; }
    public ICommand SubmitExternalCommand { get; }

    public void Activate() => _ = RefreshAsync();
    public void Deactivate() { }
    public void Dispose() { }

    private async Task RefreshAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            using var resp = await http.GetAsync("/api/v4/adr/reports");
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<List<AdrReportVm>>>(JsonOpts);
            Reports.Clear();
            if (env?.Data != null) foreach (var r in env.Data) Reports.Add(r);
            SelectedReport ??= Reports.FirstOrDefault();
            StatusMessage = $"已加载 {Reports.Count} 条药品不良反应上报记录";
        }
        catch (Exception ex) { StatusMessage = "加载失败：" + ex.Message; }
    }

    private async Task SubmitAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            using var resp = await http.PostAsJsonAsync("/api/v4/adr/reports", new
            {
                PatientId = PatientId.Trim(),
                EncounterOrVisitId = "ADM20261105001",
                OrderOrRxId = (string?)null,
                DrugId = "DRUG-01",
                DrugName = DrugName.Trim(),
                BatchNo = "B202611",
                ReactionName = ReactionName.Trim(),
                OnsetTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Severity,
                Outcome,
                CausalityGrade,
                ReactionDescription = ReactionDesc
            }, JsonOpts);
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<AdrReportVm>>(JsonOpts);
            _shell?.ShowToast(env?.Message ?? "上报完成", env?.Code == 200 ? "Success" : "Error");
            await RefreshAsync();
        }
        catch (Exception ex) { _shell?.ShowToast("提交异常：" + ex.Message, "Error"); }
    }

    private async Task SubmitExternalAsync()
    {
        if (SelectedReport == null) { _shell?.ShowToast("请先选择 ADR 报告单", "Warning"); return; }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            using var resp = await http.PostAsync($"/api/v4/adr/reports/{Uri.EscapeDataString(SelectedReport.Id)}/submit-external", null);
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>(JsonOpts);
            _shell?.ShowToast(env?.Message ?? "国家监管直报完成", env?.Code == 200 ? "Success" : "Error");
            await RefreshAsync();
        }
        catch (Exception ex) { _shell?.ShowToast("外报异常：" + ex.Message, "Error"); }
    }
}
