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

namespace HIS.Client.UrsaShell.Stations.Aims;

public sealed class OrBoardRow
{
    public string Id { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public string OrRoomCode { get; set; } = string.Empty;
    public int TableSeq { get; set; }
    public string SurgeryCode { get; set; } = string.Empty;
    public string SurgeryName { get; set; } = string.Empty;
    public int SurgeryLevel { get; set; }
    public string MainSurgeon { get; set; } = string.Empty;
    public string Anesthesiologist { get; set; } = string.Empty;
    public int Status { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string FamilyScreenName { get; set; } = string.Empty;
    public string FamilyStage { get; set; } = string.Empty;
}

public sealed class PreopRow
{
    public string Id { get; set; } = string.Empty;
    public string AssessmentNo { get; set; } = string.Empty;
    public string ScheduleId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public decimal WeightKg { get; set; }
    public string AsaClass { get; set; } = string.Empty;
    public int MallampatiClass { get; set; }
    public int StopBangScore { get; set; }
    public string PlannedAnesthesiaMethod { get; set; } = string.Empty;
    public string? TidalVolumeRangeMl { get; set; }
    public decimal LidocaineMaxMg { get; set; }
    public decimal RopivacaineMaxMg { get; set; }
    public string? RiskAlerts { get; set; }
}

public sealed class IntraopEventRow
{
    public string Id { get; set; } = string.Empty;
    public string RecordType { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public decimal VolumeMl { get; set; }
    public string? EventDetail { get; set; }
    public string? RecordedBy { get; set; }
}

public sealed class NarcoticLedgerRow
{
    public string Id { get; set; } = string.Empty;
    public string LedgerNo { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string DrugName { get; set; } = string.Empty;
    public string? BatchNo { get; set; }
    public int IssuedAmpoules { get; set; }
    public int UsedAmpoules { get; set; }
    public int ReturnedIntactAmpoules { get; set; }
    public int EmptyAmpoulesRecovered { get; set; }
    public decimal WasteVolumeMl { get; set; }
    public string? AnesthesiologistName { get; set; }
    public string? WitnessName { get; set; }
    public string Status { get; set; } = "ISSUED";
}

public sealed class PacuRow
{
    public string Id { get; set; } = string.Empty;
    public string ScheduleId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public int AldreteTotal { get; set; }
    public string? SbarHandoffSummary { get; set; }
    public string? ReceivingWardNurse { get; set; }
    public string Status { get; set; } = "IN_PACU";
    public string GateText => AldreteTotal >= 9 ? $"✅ {AldreteTotal}/10 (可转出)" : $"⛔ {AldreteTotal}/10 (<9禁出)";
}

public sealed class PcaRow
{
    public string Id { get; set; } = string.Empty;
    public string PcaNo { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string PcaMode { get; set; } = "PCIA";
    public string FormulaSummary { get; set; } = string.Empty;
    public int? Vas24h { get; set; }
    public int? Vas48h { get; set; }
    public int? Vas72h { get; set; }
    public string? AdverseEvents { get; set; }
    public string Status { get; set; } = "ACTIVE";
}

public sealed class AimsViewModel : ClientModelBase
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly ShellViewModel _shell;

    private string _qcSummaryText = "加载手术室质控指标...";
    private OrBoardRow? _selectedOr;

    // 急诊插台
    private string _emgRoom = "OR-01";
    private string _emgSurgName = string.Empty;
    private string _emgSurgeon = string.Empty;

    // 术前访视
    private string _prePatient = string.Empty;
    private decimal _preWeight = 65m;
    private string _preAsa = "II";
    private int _preMallampati = 2;
    private int _preStopBang = 2;
    private int _preFasting = 8;
    private string _preMethod = "全身麻醉(气管插管)";

    // 术中出入量/危机
    private string _intraType = "FLUID_IN";
    private string _intraCat = "乳酸钠林格液";
    private decimal _intraVol = 500m;
    private string _intraDetail = string.Empty;
    private string _fluidBalanceText = "入量 0 ml | 出量 0 ml | 净平衡 0 ml";

    // 毒麻精药
    private NarcoticLedgerRow? _selectedNarcotic;
    private string _narcPatient = string.Empty;
    private string _narcDrug = "舒芬太尼注射液";
    private int _narcIssued = 2;
    private int _recUsed = 1;
    private int _recReturned = 1;
    private int _recEmpty = 1;
    private decimal _recWaste = 0.2m;
    private string _recWitness = string.Empty;

    // PACU & PCA
    private PacuRow? _selectedPacu;
    private string _pacuPatient = string.Empty;
    private int _pAct = 2, _pResp = 2, _pCirc = 2, _pCons = 2, _pSpo2 = 2;
    private string _pacuNurse = string.Empty;
    private string _pacuSbar = "术中平稳，拔管清醒，PCA运行良好";

    private PcaRow? _selectedPca;
    private string _pcaPatient = string.Empty;
    private string _pcaMode = "PCIA";
    private string _pcaFormula = "舒芬太尼100ug + 托烷司琼10mg + 生理盐水至100ml";
    private int _pcaVas = 2;

    public ObservableCollection<OrBoardRow> OrBoard { get; } = new();
    public ObservableCollection<PreopRow> PreopList { get; } = new();
    public ObservableCollection<IntraopEventRow> IntraopEvents { get; } = new();
    public ObservableCollection<NarcoticLedgerRow> NarcoticLedgers { get; } = new();
    public ObservableCollection<PacuRow> PacuList { get; } = new();
    public ObservableCollection<PcaRow> PcaList { get; } = new();

    public string[] AsaOptions { get; } = new[] { "I", "II", "III", "IV", "V", "IE", "IIE", "IIIE" };
    public string[] IntraTypeOptions { get; } = new[] { "FLUID_IN", "FLUID_OUT", "CRISIS_EVENT" };
    public string[] PcaModeOptions { get; } = new[] { "PCIA", "PCEA", "PCNA" };

    public string QcSummaryText { get => _qcSummaryText; set => SetField(ref _qcSummaryText, value); }
    public OrBoardRow? SelectedOr
    {
        get => _selectedOr;
        set
        {
            if (SetField(ref _selectedOr, value) && value != null)
            {
                PrePatient = value.PatientName;
                NarcPatient = value.PatientName;
                PacuPatient = value.PatientName;
                PcaPatient = value.PatientName;
                _ = LoadIntraopAsync(value.Id);
            }
        }
    }

    public string EmgRoom { get => _emgRoom; set => SetField(ref _emgRoom, value); }
    public string EmgSurgName { get => _emgSurgName; set => SetField(ref _emgSurgName, value); }
    public string EmgSurgeon { get => _emgSurgeon; set => SetField(ref _emgSurgeon, value); }

    public string PrePatient { get => _prePatient; set => SetField(ref _prePatient, value); }
    public decimal PreWeight { get => _preWeight; set => SetField(ref _preWeight, value); }
    public string PreAsa { get => _preAsa; set => SetField(ref _preAsa, value); }
    public int PreMallampati { get => _preMallampati; set => SetField(ref _preMallampati, value); }
    public int PreStopBang { get => _preStopBang; set => SetField(ref _preStopBang, value); }
    public int PreFasting { get => _preFasting; set => SetField(ref _preFasting, value); }
    public string PreMethod { get => _preMethod; set => SetField(ref _preMethod, value); }

    public string IntraType { get => _intraType; set => SetField(ref _intraType, value); }
    public string IntraCat { get => _intraCat; set => SetField(ref _intraCat, value); }
    public decimal IntraVol { get => _intraVol; set => SetField(ref _intraVol, value); }
    public string IntraDetail { get => _intraDetail; set => SetField(ref _intraDetail, value); }
    public string FluidBalanceText { get => _fluidBalanceText; set => SetField(ref _fluidBalanceText, value); }

    public NarcoticLedgerRow? SelectedNarcotic { get => _selectedNarcotic; set => SetField(ref _selectedNarcotic, value); }
    public string NarcPatient { get => _narcPatient; set => SetField(ref _narcPatient, value); }
    public string NarcDrug { get => _narcDrug; set => SetField(ref _narcDrug, value); }
    public int NarcIssued { get => _narcIssued; set => SetField(ref _narcIssued, value); }
    public int RecUsed { get => _recUsed; set => SetField(ref _recUsed, value); }
    public int RecReturned { get => _recReturned; set => SetField(ref _recReturned, value); }
    public int RecEmpty { get => _recEmpty; set => SetField(ref _recEmpty, value); }
    public decimal RecWaste { get => _recWaste; set => SetField(ref _recWaste, value); }
    public string RecWitness { get => _recWitness; set => SetField(ref _recWitness, value); }

    public PacuRow? SelectedPacu { get => _selectedPacu; set => SetField(ref _selectedPacu, value); }
    public string PacuPatient { get => _pacuPatient; set => SetField(ref _pacuPatient, value); }
    public int PAct { get => _pAct; set => SetField(ref _pAct, value); }
    public int PResp { get => _pResp; set => SetField(ref _pResp, value); }
    public int PCirc { get => _pCirc; set => SetField(ref _pCirc, value); }
    public int PCons { get => _pCons; set => SetField(ref _pCons, value); }
    public int PSpo2 { get => _pSpo2; set => SetField(ref _pSpo2, value); }
    public string PacuNurse { get => _pacuNurse; set => SetField(ref _pacuNurse, value); }
    public string PacuSbar { get => _pacuSbar; set => SetField(ref _pacuSbar, value); }

    public PcaRow? SelectedPca { get => _selectedPca; set => SetField(ref _selectedPca, value); }
    public string PcaPatient { get => _pcaPatient; set => SetField(ref _pcaPatient, value); }
    public string PcaMode { get => _pcaMode; set => SetField(ref _pcaMode, value); }
    public string PcaFormula { get => _pcaFormula; set => SetField(ref _pcaFormula, value); }
    public int PcaVas { get => _pcaVas; set => SetField(ref _pcaVas, value); }

    public ICommand RefreshAllCommand { get; }
    public ICommand EmergencyInsertCommand { get; }
    public ICommand SavePreopCommand { get; }
    public ICommand AddIntraopCommand { get; }
    public ICommand IssueNarcoticCommand { get; }
    public ICommand ReconcileNarcoticCommand { get; }
    public ICommand SavePacuCommand { get; }
    public ICommand HandoffPacuCommand { get; }
    public ICommand CreatePcaCommand { get; }
    public ICommand StopPcaCommand { get; }

    public AimsViewModel(WorkbenchModel workbench, ShellViewModel shell)
    {
        _shell = shell;
        RefreshAllCommand = new RelayCommand(async () => await RefreshAllAsync());
        EmergencyInsertCommand = new RelayCommand(async () => await EmergencyInsertAsync());
        SavePreopCommand = new RelayCommand(async () => await SavePreopAsync());
        AddIntraopCommand = new RelayCommand(async () => await AddIntraopAsync());
        IssueNarcoticCommand = new RelayCommand(async () => await IssueNarcoticAsync());
        ReconcileNarcoticCommand = new RelayCommand(async () => await ReconcileNarcoticAsync());
        SavePacuCommand = new RelayCommand(async () => await SavePacuAsync());
        HandoffPacuCommand = new RelayCommand(async () => await HandoffPacuAsync());
        CreatePcaCommand = new RelayCommand(async () => await CreatePcaAsync());
        StopPcaCommand = new RelayCommand(async () => await StopPcaAsync());
        _ = RefreshAllAsync();
    }

    private async Task RefreshAllAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            await LoadListAsync(http, "api/v4/aims/or-board", OrBoard);
            await LoadListAsync(http, "api/v4/aims/preop-assessments", PreopList);
            await LoadListAsync(http, "api/v4/aims/narcotics", NarcoticLedgers);
            await LoadListAsync(http, "api/v4/aims/pacu", PacuList);
            await LoadListAsync(http, "api/v4/aims/pca", PcaList);

            var qc = await http.GetFromJsonAsync<ClientApiEnvelope<JsonElement>>("api/v4/aims/or-board/qc-metrics", JsonOpts);
            if (qc?.Code == 0 && qc.Data.ValueKind == JsonValueKind.Object)
            {
                QcSummaryText = $"总手术 {qc.Data.GetProperty("totalSurgeries").GetInt32()} 台 · 急诊 {qc.Data.GetProperty("emergencySurgeries").GetInt32()} 台 · WHO三方核查率 {qc.Data.GetProperty("whoChecklistRatePct").GetDecimal():F1}% · 未核销毒麻单 {qc.Data.GetProperty("unreconciledNarcotics").GetInt32()} 笔 · PACU在室 {qc.Data.GetProperty("patientsInPacu").GetInt32()} 人";
            }
        }
        catch (Exception ex) { _shell.ShowToast($"刷新手麻数据失败：{ex.Message}", "Error"); }
    }

    private static async Task LoadListAsync<T>(System.Net.Http.HttpClient http, string url, ObservableCollection<T> target)
    {
        var env = await http.GetFromJsonAsync<ClientApiEnvelope<List<T>>>(url, JsonOpts);
        target.Clear();
        if (env?.Data != null) foreach (var it in env.Data) target.Add(it);
    }

    private async Task LoadIntraopAsync(string scheduleId)
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var env = await http.GetFromJsonAsync<ClientApiEnvelope<JsonElement>>($"api/v4/aims/intraop-events/{Uri.EscapeDataString(scheduleId)}", JsonOpts);
            IntraopEvents.Clear();
            if ((env?.Code is 200 or 0) && env.Data.ValueKind == JsonValueKind.Object)
            {
                decimal fIn = env.Data.GetProperty("totalFluidInMl").GetDecimal();
                decimal fOut = env.Data.GetProperty("totalFluidOutMl").GetDecimal();
                decimal net = env.Data.GetProperty("netBalanceMl").GetDecimal();
                FluidBalanceText = $"总入量 {fIn:F0} ml | 总出量 {fOut:F0} ml | 净平衡 {(net >= 0 ? "+" : "")}{net:F0} ml";
                if (env.Data.TryGetProperty("events", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    var list = JsonSerializer.Deserialize<List<IntraopEventRow>>(arr.GetRawText(), JsonOpts);
                    if (list != null) foreach (var e in list) IntraopEvents.Add(e);
                }
            }
        }
        catch { }
    }

    private async Task EmergencyInsertAsync()
    {
        if (string.IsNullOrWhiteSpace(EmgSurgName)) { _shell.ShowToast("请填写急诊手术名称", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/aims/or-board/emergency-insert", new { EncounterId = "", OrRoomCode = EmgRoom, SurgeryCode = "EMG", SurgeryName = EmgSurgName, SurgeryLevel = 3, SurgeonName = EmgSurgeon, AnesthesiologistName = "" });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "急诊插台成功", (env?.Code is 200 or 0) ? "Success" : "Error");
        EmgSurgName = string.Empty;
        await RefreshAllAsync();
    }

    private async Task SavePreopAsync()
    {
        if (string.IsNullOrWhiteSpace(PrePatient)) { _shell.ShowToast("请先选择手术或填写患者姓名", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/aims/preop-assessments", new { ScheduleId = SelectedOr?.Id ?? "MANUAL", EncounterId = SelectedOr?.EncounterId, PatientName = PrePatient, WeightKg = PreWeight, AsaClass = PreAsa, MallampatiClass = PreMallampati, StopBangScore = PreStopBang, FastingHours = PreFasting, PlannedAnesthesiaMethod = PreMethod });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "术前访视已保存", (env?.Code is 200 or 0) ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task AddIntraopAsync()
    {
        string sid = SelectedOr?.Id ?? "MANUAL";
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/aims/intraop-events", new { ScheduleId = sid, RecordType = IntraType, CategoryName = IntraCat, VolumeMl = IntraVol, EventDetail = IntraDetail, PatientWeightKg = PreWeight });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "术中记录已保存", (env?.Code is 200 or 0) ? "Success" : "Error");
        IntraDetail = string.Empty;
        await LoadIntraopAsync(sid);
    }

    private async Task IssueNarcoticAsync()
    {
        if (string.IsNullOrWhiteSpace(NarcDrug)) return;
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/aims/narcotics/issue", new { ScheduleId = SelectedOr?.Id ?? "MANUAL", PatientName = string.IsNullOrWhiteSpace(NarcPatient) ? "手术患者" : NarcPatient, DrugName = NarcDrug, BatchNo = "LOT2026", IssuedAmpoules = NarcIssued });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "领用已登记", (env?.Code is 200 or 0) ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task ReconcileNarcoticAsync()
    {
        if (SelectedNarcotic == null || string.IsNullOrWhiteSpace(RecWitness)) { _shell.ShowToast("请选择毒麻领用单并填写第二见证人姓名", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"api/v4/aims/narcotics/{SelectedNarcotic.Id}/reconcile", new { UsedAmpoules = RecUsed, ReturnedIntactAmpoules = RecReturned, EmptyAmpoulesRecovered = RecEmpty, WasteVolumeMl = RecWaste, WitnessName = RecWitness });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "核销完成", (env?.Code is 200 or 0) ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task SavePacuAsync()
    {
        if (string.IsNullOrWhiteSpace(PacuPatient)) { _shell.ShowToast("请填写 PACU 复苏患者姓名", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/aims/pacu", new { ScheduleId = SelectedOr?.Id ?? "MANUAL", PatientName = PacuPatient, ActivityScore = PAct, RespirationScore = PResp, CirculationScore = PCirc, ConsciousnessScore = PCons, Spo2Score = PSpo2, SbarHandoffSummary = PacuSbar });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "PACU 评分已保存", (env?.Code is 200 or 0) ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task HandoffPacuAsync()
    {
        if (SelectedPacu == null || string.IsNullOrWhiteSpace(PacuNurse)) { _shell.ShowToast("请选择 PACU 患者并填写病区接班护士", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"api/v4/aims/pacu/{SelectedPacu.Id}/handoff", new { ReceivingWardNurse = PacuNurse, SbarHandoffSummary = PacuSbar });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "交接完成", (env?.Code is 200 or 0) ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task CreatePcaAsync()
    {
        if (string.IsNullOrWhiteSpace(PcaPatient)) { _shell.ShowToast("请填写术后镇痛患者姓名", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/aims/pca", new { ScheduleId = SelectedOr?.Id ?? "MANUAL", PatientName = PcaPatient, PcaMode, FormulaSummary = PcaFormula, BackgroundRateMlH = 2.0m, BolusDoseMl = 0.5m, LockoutMinutes = 15 });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "PCA 已启用", (env?.Code is 200 or 0) ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task StopPcaAsync()
    {
        if (SelectedPca == null) { _shell.ShowToast("请选择一条 PCA 镇痛泵记录", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PutAsJsonAsync($"api/v4/aims/pca/{SelectedPca.Id}/followup", new { Vas24h = PcaVas, Vas48h = Math.Max(0, PcaVas - 1), Vas72h = 0, AdverseEvents = "无恶心呕吐及呼吸抑制", Status = "STOPPED" });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "已停泵并记录随访", (env?.Code is 200 or 0) ? "Success" : "Error");
        await RefreshAllAsync();
    }
}
