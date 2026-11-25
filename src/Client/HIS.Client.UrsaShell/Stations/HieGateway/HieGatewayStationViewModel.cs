using System;
using System.Collections.ObjectModel;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.BuildingBlocks.Common;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Services;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.HieGateway;

public sealed class HieExchangeLogVm
{
    public string Id { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public string PeerOrgCode { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string SummaryLine => $"[{Direction}/{Protocol}] 对端 {PeerOrgCode} · 患者 {PatientId} · 资源 {ResourceType} · {Status}";
}

public sealed class HieGatewayStationViewModel : ClientModelBase, IStationLifecycle
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly WorkbenchModel _workbench;
    private readonly ShellViewModel? _shell;

    private string _patientId = "PAT85061801";
    private string _fullName = "周建强";
    private string _peerOrgCode = "HOSP-PROVINCIAL-01";
    private string _externalEhrText = "点击「拉取外院EHR」实时调阅区域医联体健康档案摘要...";
    private string _statusMessage = "区域卫生信息平台 (HIE) / EMPI 患者主索引 / FHIR 互联互通网关";

    public HieGatewayStationViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        _workbench = workbench;
        _shell = shell;
        RefreshLogsCommand = new AsyncRelayCommand(RefreshLogsAsync);
        ResolveEmpiCommand = new AsyncRelayCommand(ResolveEmpiAsync);
        FetchExternalEhrCommand = new AsyncRelayCommand(FetchEhrAsync);
        PublishOutboundCommand = new AsyncRelayCommand(PublishOutboundAsync);
    }

    public ObservableCollection<HieExchangeLogVm> Logs { get; } = new();

    public string PatientId { get => _patientId; set => SetField(ref _patientId, value ?? string.Empty); }
    public string FullName { get => _fullName; set => SetField(ref _fullName, value ?? string.Empty); }
    public string PeerOrgCode { get => _peerOrgCode; set => SetField(ref _peerOrgCode, value ?? string.Empty); }
    public string ExternalEhrText { get => _externalEhrText; set => SetField(ref _externalEhrText, value ?? string.Empty); }
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value ?? string.Empty); }

    public ICommand RefreshLogsCommand { get; }
    public ICommand ResolveEmpiCommand { get; }
    public ICommand FetchExternalEhrCommand { get; }
    public ICommand PublishOutboundCommand { get; }

    public void Activate() => _ = RefreshLogsAsync();
    public void Deactivate() { }
    public void Dispose() { }

    private async Task RefreshLogsAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var env = await http.GetFromJsonAsync<ApiResponse<System.Collections.Generic.List<HieExchangeLogVm>>>("/api/v4/hie/logs", JsonOpts);
            Logs.Clear();
            if (env?.Data != null) foreach (var l in env.Data) Logs.Add(l);
            StatusMessage = $"HIE 网关在线 · 共 {Logs.Count} 条跨境/跨院交互日志";
        }
        catch (Exception ex) { StatusMessage = "日志同步失败：" + ex.Message; }
    }

    private async Task ResolveEmpiAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            using var resp = await http.PostAsJsonAsync("/api/v4/hie/empi/resolve", new
            {
                LocalPatientId = PatientId.Trim(),
                FullName = FullName.Trim(),
                IdCardHash = "SM3-DEMO-" + PatientId.Trim(),
                GenderCode = "M",
                BirthDateInt = 19850618
            }, JsonOpts);
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>(JsonOpts);
            _shell?.ShowToast(env?.Message ?? "EMPI 索引解析完成", env?.Code == 200 ? "Success" : "Error");
            await RefreshLogsAsync();
        }
        catch (Exception ex) { _shell?.ShowToast("EMPI异常：" + ex.Message, "Error"); }
    }

    private async Task FetchEhrAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var env = await http.GetFromJsonAsync<ApiResponse<JsonElement>>(
                $"/api/v4/hie/patients/{Uri.EscapeDataString(PatientId.Trim())}/ehr-summary", JsonOpts);
            ExternalEhrText = env?.Code == 200 ? env.Data.ToString() : (env?.Message ?? "未获取");
            _shell?.ShowToast("跨院健康档案拉取成功", "Success");
        }
        catch (Exception ex) { ExternalEhrText = "拉取异常：" + ex.Message; }
    }

    private async Task PublishOutboundAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            using var resp = await http.PostAsJsonAsync("/api/v4/hie/outbound/publish", new
            {
                ResourceType = "PatientSummary",
                ResourceId = PatientId.Trim(),
                TargetOrgCode = PeerOrgCode.Trim(),
                Payload = new { PatientId, FullName, ExportTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
            }, JsonOpts);
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>(JsonOpts);
            _shell?.ShowToast(env?.Message ?? "FHIR发布完成", env?.Code == 200 ? "Success" : "Error");
            await RefreshLogsAsync();
        }
        catch (Exception ex) { _shell?.ShowToast("出站推送异常：" + ex.Message, "Error"); }
    }
}
