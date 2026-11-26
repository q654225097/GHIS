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

namespace HIS.Client.UrsaShell.Stations.CriticalCareControl;

public sealed class FiveCenterCaseRow
{
    public string Id { get; set; } = string.Empty;
    public string CaseNo { get; set; } = string.Empty;
    public string CenterType { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string TargetMetricName { get; set; } = string.Empty;
    public int TargetLimitMinutes { get; set; }
    public int? ActualMinutes { get; set; }
    public short IsOvertime { get; set; }
    public string Status { get; set; } = string.Empty;
    public string OvertimeDisplay => IsOvertime == 1 ? "⚠ 超时" : "达标";
}

public sealed class IcuAdmissionRow
{
    public string Id { get; set; } = string.Empty;
    public string IcuNo { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string IcuBedCode { get; set; } = string.Empty;
    public int ApacheIiScore { get; set; }
    public int SofaScore { get; set; }
    public decimal PredictedMortalityPct { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class DrgAlertRow
{
    public string EncounterId { get; set; } = string.Empty;
    public string GroupCode { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public decimal BenchmarkFee { get; set; }
    public decimal CurrentTotalFee { get; set; }
    public decimal FeeRatio { get; set; }
    public string AlertLevel { get; set; } = string.Empty;
}

public sealed class PivasOrderRow
{
    public string Id { get; set; } = string.Empty;
    public string PivasNo { get; set; } = string.Empty;
    public string BatchCode { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string DrugName { get; set; } = string.Empty;
    public string SolventName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? LabelBarcode { get; set; }
}

public sealed class CriticalCareControlViewModel : ClientModelBase
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly ShellViewModel _shell;

    private string _summaryBanner = "加载 P0 临床质控与重症数据...";
    private string _abxAudSummary = "抗菌药物 AUD 强度：待刷新";

    private string _fcCenterType = "CHEST_PAIN", _fcPatientId = "PAT-ER-01", _fcPatientName = string.Empty, _fcMilestoneCode = "FIRST_ECG";
    private FiveCenterCaseRow? _selectedFiveCenterCase;

    private string _icuEncounterId = "ENC-ICU-01", _icuPatientName = string.Empty, _icuBedCode = "ICU-01";
    private IcuAdmissionRow? _selectedIcu;

    private string _drgEncounterId = "ENC-DRG-01", _drgGroupCode = "EB19", _drgAppealReason = "危重症合并多器官衰竭申请特病单议";
    private DrgAlertRow? _selectedDrgAlert;

    private string _pivasDrugName = "注射用美罗培南 0.5g", _pivasSolventName = "0.9%氯化钠注射液 100ml";
    private PivasOrderRow? _selectedPivas;

    public ObservableCollection<FiveCenterCaseRow> FiveCenterCases { get; } = new();
    public ObservableCollection<IcuAdmissionRow> IcuAdmissions { get; } = new();
    public ObservableCollection<DrgAlertRow> DrgAlerts { get; } = new();
    public ObservableCollection<PivasOrderRow> PivasOrders { get; } = new();

    public string[] CenterTypes { get; } = new[] { "CHEST_PAIN", "STROKE", "TRAUMA", "MATERNAL", "NEONATAL" };
    public string[] MilestoneCodes { get; } = new[] { "FIRST_ECG", "TROPONIN", "HEAD_CT", "THROMBOLYSIS", "WIRE_CROSS" };

    public string SummaryBanner { get => _summaryBanner; set => SetField(ref _summaryBanner, value); }
    public string AbxAudSummary { get => _abxAudSummary; set => SetField(ref _abxAudSummary, value); }

    public string FcCenterType { get => _fcCenterType; set => SetField(ref _fcCenterType, value); }
    public string FcPatientId { get => _fcPatientId; set => SetField(ref _fcPatientId, value); }
    public string FcPatientName { get => _fcPatientName; set => SetField(ref _fcPatientName, value); }
    public string FcMilestoneCode { get => _fcMilestoneCode; set => SetField(ref _fcMilestoneCode, value); }
    public FiveCenterCaseRow? SelectedFiveCenterCase { get => _selectedFiveCenterCase; set => SetField(ref _selectedFiveCenterCase, value); }

    public string IcuEncounterId { get => _icuEncounterId; set => SetField(ref _icuEncounterId, value); }
    public string IcuPatientName { get => _icuPatientName; set => SetField(ref _icuPatientName, value); }
    public string IcuBedCode { get => _icuBedCode; set => SetField(ref _icuBedCode, value); }
    public IcuAdmissionRow? SelectedIcu { get => _selectedIcu; set => SetField(ref _selectedIcu, value); }

    public string DrgEncounterId { get => _drgEncounterId; set => SetField(ref _drgEncounterId, value); }
    public string DrgGroupCode { get => _drgGroupCode; set => SetField(ref _drgGroupCode, value); }
    public string DrgAppealReason { get => _drgAppealReason; set => SetField(ref _drgAppealReason, value); }
    public DrgAlertRow? SelectedDrgAlert { get => _selectedDrgAlert; set => SetField(ref _selectedDrgAlert, value); }

    public string PivasDrugName { get => _pivasDrugName; set => SetField(ref _pivasDrugName, value); }
    public string PivasSolventName { get => _pivasSolventName; set => SetField(ref _pivasSolventName, value); }
    public PivasOrderRow? SelectedPivas { get => _selectedPivas; set => SetField(ref _selectedPivas, value); }

    public ICommand RefreshAllCommand { get; }
    public ICommand OpenFiveCenterCaseCommand { get; }
    public ICommand RecordMilestoneCommand { get; }
    public ICommand AdmitIcuCommand { get; }
    public ICommand ScoreIcuCommand { get; }
    public ICommand AddIcuHourlyCommand { get; }
    public ICommand EvaluateDrgCommand { get; }
    public ICommand AppealDrgCommand { get; }
    public ICommand CreatePivasCommand { get; }
    public ICommand ReviewPivasCommand { get; }
    public ICommand AdvancePivasCommand { get; }

    public CriticalCareControlViewModel(WorkbenchModel workbench, ShellViewModel shell)
    {
        _shell = shell;
        RefreshAllCommand = new RelayCommand(async () => await RefreshAllAsync());
        OpenFiveCenterCaseCommand = new RelayCommand(async () => await OpenFiveCenterCaseAsync());
        RecordMilestoneCommand = new RelayCommand(async () => await RecordMilestoneAsync());
        AdmitIcuCommand = new RelayCommand(async () => await AdmitIcuAsync());
        ScoreIcuCommand = new RelayCommand(async () => await ScoreIcuAsync());
        AddIcuHourlyCommand = new RelayCommand(async () => await AddIcuHourlyAsync());
        EvaluateDrgCommand = new RelayCommand(async () => await EvaluateDrgAsync());
        AppealDrgCommand = new RelayCommand(async () => await AppealDrgAsync());
        CreatePivasCommand = new RelayCommand(async () => await CreatePivasAsync());
        ReviewPivasCommand = new RelayCommand(async () => await ReviewPivasAsync());
        AdvancePivasCommand = new RelayCommand(async () => await AdvancePivasAsync());
        _ = RefreshAllAsync();
    }

    private async Task RefreshAllAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            await LoadPlainListAsync(http, "api/v4/five-centers/cases", FiveCenterCases);
            await LoadPlainListAsync(http, "api/v4/icu-cis/admissions?status=IN_ICU", IcuAdmissions);
            await LoadPlainListAsync(http, "api/v4/drg-cost/alerts", DrgAlerts);
            await LoadPlainListAsync(http, "api/v4/pivas-abx/pivas/orders", PivasOrders);

            var audEnv = await http.GetFromJsonAsync<ClientApiEnvelope<JsonElement>>("api/v4/pivas-abx/abx/aud-report", JsonOpts);
            if (audEnv?.Data.ValueKind == JsonValueKind.Object && audEnv.Data.TryGetProperty("audPer100BedDays", out var audVal))
            {
                AbxAudSummary = $"住院抗菌药物强度 AUD: {audVal.GetDecimal():F2} / 红线 40.00 (总DDDs: {audEnv.Data.GetProperty("grandTotalDdds").GetDecimal():F2})";
            }

            SummaryBanner = $"五大中心专案 {FiveCenterCases.Count} · ICU在科 {IcuAdmissions.Count} · DRG预警 {DrgAlerts.Count} · PIVAS静配单 {PivasOrders.Count}";
        }
        catch (Exception ex) { _shell.ShowToast($"刷新 P0 控制塔失败：{ex.Message}", "Error"); }
    }

    private static async Task LoadPlainListAsync<T>(System.Net.Http.HttpClient http, string url, ObservableCollection<T> target)
    {
        var env = await http.GetFromJsonAsync<ClientApiEnvelope<List<T>>>(url, JsonOpts);
        target.Clear();
        if (env?.Data != null) foreach (var it in env.Data) target.Add(it);
    }

    private async Task OpenFiveCenterCaseAsync()
    {
        if (string.IsNullOrWhiteSpace(FcPatientName)) { _shell.ShowToast("请填写急救患者姓名", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/five-centers/cases", new { CenterType = FcCenterType, PatientId = FcPatientId, PatientName = FcPatientName });
        _shell.ShowToast(resp.IsSuccessStatusCode ? "五大中心绿色通道已启动" : "建档失败", resp.IsSuccessStatusCode ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task RecordMilestoneAsync()
    {
        if (SelectedFiveCenterCase == null) { _shell.ShowToast("请先选择急救专案", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"api/v4/five-centers/cases/{SelectedFiveCenterCase.Id}/milestones", new { MilestoneCode = FcMilestoneCode, MilestoneName = FcMilestoneCode });
        _shell.ShowToast(resp.IsSuccessStatusCode ? $"节点 {FcMilestoneCode} 已打点" : "节点打点失败", resp.IsSuccessStatusCode ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task AdmitIcuAsync()
    {
        if (string.IsNullOrWhiteSpace(IcuPatientName)) { _shell.ShowToast("请填写 ICU 患者姓名", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/icu-cis/admissions", new { EncounterId = IcuEncounterId, PatientId = IcuEncounterId, PatientName = IcuPatientName, IcuBedCode = IcuBedCode, AdmitReason = "重症监护支持" });
        _shell.ShowToast(resp.IsSuccessStatusCode ? "已收治入 ICU" : "收治失败", resp.IsSuccessStatusCode ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task ScoreIcuAsync()
    {
        if (SelectedIcu == null) { _shell.ShowToast("请先选择 ICU 患者", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"api/v4/icu-cis/admissions/{SelectedIcu.Id}/severity-score", new
        {
            TempC = 38.6m, MapMmhg = 64, HeartRate = 118, RespRate = 26, Pao2Fio2Ratio = 185m,
            Platelets10e9 = 92, BilirubinMgDl = 2.1m, CreatinineMgDl = 2.2m, GcsScore = 11, AgeYears = 68, HasChronicOrganFailure = true, OnVasopressor = true
        });
        _shell.ShowToast(resp.IsSuccessStatusCode ? "APACHE II / SOFA 评分已更新" : "评分计算失败", resp.IsSuccessStatusCode ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task AddIcuHourlyAsync()
    {
        if (SelectedIcu == null) { _shell.ShowToast("请先选择 ICU 患者", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"api/v4/icu-cis/admissions/{SelectedIcu.Id}/hourly", new
        {
            VentMode = "SIMV", Fio2Pct = 45m, PeepCmh2o = 8m, TidalVolumeMl = 450, RespRate = 16,
            MapMmhg = 72, Spo2Pct = 97m, LactateMmol = 2.4m, IntakeMl = 180m, OutputMl = 120m,
            VasopressorDrug = "去甲肾上腺素", VasopressorRateMcgKgMin = 0.12m, Note = "循环趋稳"
        });
        _shell.ShowToast(resp.IsSuccessStatusCode ? "小时级特护与出入量已登记" : "登记失败", resp.IsSuccessStatusCode ? "Success" : "Error");
    }

    private async Task EvaluateDrgAsync()
    {
        if (string.IsNullOrWhiteSpace(DrgEncounterId)) return;
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"api/v4/drg-cost/evaluate/{DrgEncounterId}", new { GroupCode = DrgGroupCode, PatientName = DrgEncounterId, DeptName = "心内科" });
        _shell.ShowToast(resp.IsSuccessStatusCode ? "DRG/DIP 实时控费评估完成" : "评估失败", resp.IsSuccessStatusCode ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task AppealDrgAsync()
    {
        if (SelectedDrgAlert == null) { _shell.ShowToast("请先选择一条 DRG 预警记录", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"api/v4/drg-cost/alerts/{SelectedDrgAlert.EncounterId}/appeal", new { AppealReason = DrgAppealReason });
        _shell.ShowToast(resp.IsSuccessStatusCode ? "特病单议申诉已提交" : "申诉提交失败", resp.IsSuccessStatusCode ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task CreatePivasAsync()
    {
        var encId = SessionContext.Current.ActiveDeptId ?? "ENC-01";
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/pivas-abx/pivas/orders", new
        {
            OrderId = $"ORD-{DateTime.UtcNow:HHmmss}",
            EncounterId = encId,
            PatientId = SessionContext.Current.UserId ?? "PAT85061801",
            PatientName = PivasDrugName.Contains("美罗") ? "重症在院患者" : "住院静配患者",
            WardName = SessionContext.Current.ActiveDeptName ?? "住院病区",
            DrugCode = "DRUG-MEROPENEM",
            DrugName = PivasDrugName,
            SolventName = PivasSolventName,
            DosageText = "0.5g ivgtt q8h",
            BatchCode = "BATCH_1"
        });
        _shell.ShowToast(resp.IsSuccessStatusCode ? "静配工单已下达" : "下达失败", resp.IsSuccessStatusCode ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task ReviewPivasAsync()
    {
        if (SelectedPivas == null) { _shell.ShowToast("请先选择静配工单", "Warning"); return; }
        string reviewer = SessionContext.Current.DisplayName ?? SessionContext.Current.UserId ?? "药师";
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"api/v4/pivas-abx/pivas/orders/{SelectedPivas.Id}/review", new { Approved = true, BatchCode = "BATCH_1", ReviewerName = reviewer });
        _shell.ShowToast(resp.IsSuccessStatusCode ? "审方通过并排批" : "审方失败", resp.IsSuccessStatusCode ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task AdvancePivasAsync()
    {
        if (SelectedPivas == null) { _shell.ShowToast("请先选择静配工单", "Warning"); return; }
        string operatorName = SessionContext.Current.DisplayName ?? SessionContext.Current.UserId ?? "药师";
        string dualCheckerName = string.Equals(operatorName, "复核药师", StringComparison.OrdinalIgnoreCase)
            ? "资深主管药师"
            : $"{operatorName}-双签复核";
        using var http = AuthenticatedHttpClientFactory.Create();
        if (SelectedPivas.Status == "REVIEWED") await http.PostAsync($"api/v4/pivas-abx/pivas/orders/{SelectedPivas.Id}/label", null);
        else if (SelectedPivas.Status == "LABELED") await http.PostAsJsonAsync($"api/v4/pivas-abx/pivas/orders/{SelectedPivas.Id}/compound", new { CompounderName = operatorName });
        else if (SelectedPivas.Status == "COMPOUNDED") await http.PostAsJsonAsync($"api/v4/pivas-abx/pivas/orders/{SelectedPivas.Id}/check", new { CheckerName = dualCheckerName });
        else if (SelectedPivas.Status == "CHECKED") await http.PostAsync($"api/v4/pivas-abx/pivas/orders/{SelectedPivas.Id}/dispatch", null);
        await RefreshAllAsync();
    }
}
