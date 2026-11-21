using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.InpatientPro;

public sealed class BedCardRow
{
    public string BedId { get; set; } = string.Empty;
    public string WardId { get; set; } = string.Empty;
    public string WardName { get; set; } = string.Empty;
    public string RoomCode { get; set; } = string.Empty;
    public string BedCode { get; set; } = string.Empty;
    public string BedType { get; set; } = string.Empty;
    public decimal DailyFee { get; set; }
    public int BedStatus { get; set; }
    public short IsIsolation { get; set; }
    public string? EncounterId { get; set; }
    public string? AdmissionNo { get; set; }
    public string? NursingLevelCode { get; set; }
    public decimal? PrepaymentBalance { get; set; }
    public string? PatientName { get; set; }
    public string? Gender { get; set; }

    public string StatusBadge => IsIsolation == 1
        ? "🟡 隔离床位"
        : string.IsNullOrWhiteSpace(EncounterId) ? "🟢 空床可用" : "🔵 在院占用";
    public string PatientDisplay => string.IsNullOrWhiteSpace(PatientName) ? "（空床）" : $"{PatientName} ({Gender})";
}

public sealed class MarPlanRow
{
    public string PlanId { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public long PlannedTimeMs { get; set; }
    public int ExecStatus { get; set; }
    public string? ExecNurseId { get; set; }
    public long? ExecutedAtMs { get; set; }
    public string? ItemName { get; set; }
    public decimal Dosage { get; set; }
    public string? FrequencyCode { get; set; }
    public string? RouteCode { get; set; }

    public string PlannedTimeText => PlannedTimeMs > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(PlannedTimeMs).ToLocalTime().ToString("MM-dd HH:mm")
        : "-";
    public string ExecStatusText => ExecStatus == 1 ? "✅ 已执行" : "⏳ 待执行";
}

public sealed class VitalsPointRow
{
    public string Id { get; set; } = string.Empty;
    public long RecordedAtMs { get; set; }
    public decimal TempC { get; set; }
    public int PulseBpm { get; set; }
    public int RespRate { get; set; }
    public int Sbp { get; set; }
    public int Dbp { get; set; }
    public int Spo2 { get; set; }
    public string TimeText => RecordedAtMs > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(RecordedAtMs).ToLocalTime().ToString("MM-dd HH:mm")
        : "-";
}

public sealed class ConsultRow
{
    public string Id { get; set; } = string.Empty;
    public string ConsultNo { get; set; } = string.Empty;
    public string? EncounterId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string? BedCode { get; set; }
    public string RequestDeptName { get; set; } = string.Empty;
    public string? RequestDoctorName { get; set; }
    public string TargetDeptName { get; set; } = string.Empty;
    public string? AssignedDoctorName { get; set; }
    public string ConsultType { get; set; } = "ROUTINE";
    public string PurposeSummary { get; set; } = string.Empty;
    public long DeadlineMs { get; set; }
    public string Status { get; set; } = "REQUESTED";
    public string? ConsultOpinion { get; set; }
    public short SyncedToEmr { get; set; }

    public string TypeDisplay => ConsultType switch
    {
        "EMERGENCY" => "🔴 急会诊(10min)",
        "MDT" => "🟣 MDT多学科",
        _ => "🔵 普通会诊(24h)"
    };
    public string DeadlineText => DeadlineMs > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(DeadlineMs).ToLocalTime().ToString("MM-dd HH:mm")
        : "-";
    public string EmrSyncDisplay => SyncedToEmr == 1 ? "已回写EMR" : "未回写";
}

public sealed class DailyBillingRunRow
{
    public string Id { get; set; } = string.Empty;
    public int BillDateInt { get; set; }
    public string TriggerMode { get; set; } = string.Empty;
    public int PatientCount { get; set; }
    public int BedFeeCount { get; set; }
    public int NursingFeeCount { get; set; }
    public int LongOrderCount { get; set; }
    public decimal TotalAmount { get; set; }
    public string? ExecutedBy { get; set; }
}

public sealed class DrugReturnRow
{
    public string Id { get; set; } = string.Empty;
    public string ReturnNo { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string? BedCode { get; set; }
    public string DrugName { get; set; } = string.Empty;
    public decimal ReturnQty { get; set; }
    public decimal RefundAmount { get; set; }
    public string? Reason { get; set; }
    public string Status { get; set; } = "APPLIED";
    public string? ApplicantName { get; set; }
    public string? ReviewerName { get; set; }
}

public sealed class DischargeMedRow
{
    public string Id { get; set; } = string.Empty;
    public string RxNo { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string MedicationsSummary { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string? PrescribedBy { get; set; }
    public string Status { get; set; } = "PRESCRIBED";
}

public sealed class MaternityRow
{
    public string Id { get; set; } = string.Empty;
    public string DeliveryNo { get; set; } = string.Empty;
    public string MotherName { get; set; } = string.Empty;
    public string GestationalWeeks { get; set; } = string.Empty;
    public string DeliveryMode { get; set; } = string.Empty;
    public string NewbornGender { get; set; } = string.Empty;
    public int BirthWeightG { get; set; }
    public decimal BirthLengthCm { get; set; }
    public string ApgarScores { get; set; } = string.Empty;
    public string? BirthCertPreNo { get; set; }
}

public sealed class DaySurgeryRow
{
    public string Id { get; set; } = string.Empty;
    public string CaseNo { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public int Age { get; set; }
    public string DeptName { get; set; } = string.Empty;
    public string SurgeryName { get; set; } = string.Empty;
    public string? SurgeonName { get; set; }
    public int PadssScore { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Remark { get; set; }
}

public sealed class VisitorPassRow
{
    public string Id { get; set; } = string.Empty;
    public string PassNo { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string? BedCode { get; set; }
    public string VisitorName { get; set; } = string.Empty;
    public string RelationName { get; set; } = string.Empty;
    public string? PhoneMasked { get; set; }
    public string PassType { get; set; } = "ESCORT";
    public int ValidUntilInt { get; set; }
    public string Status { get; set; } = "ACTIVE";
}

public sealed class InpatientProViewModel : ClientModelBase
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly ShellViewModel _shell;

    // 1. 床位看板 / MAR / 三测单
    private BedCardRow? _selectedBed;
    private BedCardRow? _swapTargetBed;
    private MarPlanRow? _selectedMarPlan;

    // 2. 会诊中心
    private ConsultRow? _selectedConsult;
    private string _csPatientName = string.Empty;
    private string _csBedCode = string.Empty;
    private string _csRequestDept = "心内科一病区";
    private string _csTargetDept = "呼吸内科";
    private string _csType = "ROUTINE";
    private string _csPurpose = string.Empty;
    private string _csOpinion = string.Empty;

    // 3. 滚费 / 退药 / 出院带药
    private int _manualBillDateInt = int.Parse(DateTime.Now.ToString("yyyyMMdd"));
    private DrugReturnRow? _selectedDrugReturn;
    private string _drPatientName = string.Empty;
    private string _drBedCode = string.Empty;
    private string _drDrugName = string.Empty;
    private decimal _drQty = 1m;
    private decimal _drRefundAmt = 36m;
    private string _drReason = "医嘱停用余药退回";
    private DischargeMedRow? _selectedDischargeMed;
    private string _dmPatientName = string.Empty;
    private string _dmSummary = string.Empty;
    private decimal _dmTotalAmount = 120m;

    // 4. 专科扩展：产科 / 日间手术 / 陪护证
    private string _matMotherName = string.Empty;
    private string _matWeeks = "39+2周";
    private string _matMode = "顺产";
    private string _matGender = "男";
    private int _matWeight = 3250;
    private decimal _matLength = 50.0m;
    private string _matApgar = "10-10-10";

    private DaySurgeryRow? _selectedDaySurgery;
    private string _dsPatientName = string.Empty;
    private string _dsSurgeryName = string.Empty;
    private int _dsPadssScore = 10;
    private string _dsRemark = string.Empty;

    private VisitorPassRow? _selectedVisitorPass;
    private string _vpPatientName = string.Empty;
    private string _vpBedCode = string.Empty;
    private string _vpVisitorName = string.Empty;
    private string _vpRelation = "配偶";
    private string _vpPhone = string.Empty;
    private int _vpValidDays = 7;

    public ObservableCollection<BedCardRow> Beds { get; } = new();
    public ObservableCollection<MarPlanRow> MarPlans { get; } = new();
    public ObservableCollection<VitalsPointRow> VitalsPoints { get; } = new();
    public ObservableCollection<ConsultRow> Consultations { get; } = new();
    public ObservableCollection<DailyBillingRunRow> BillingRuns { get; } = new();
    public ObservableCollection<DrugReturnRow> DrugReturns { get; } = new();
    public ObservableCollection<DischargeMedRow> DischargeMeds { get; } = new();
    public ObservableCollection<MaternityRow> MaternityRecords { get; } = new();
    public ObservableCollection<DaySurgeryRow> DaySurgeryCases { get; } = new();
    public ObservableCollection<VisitorPassRow> VisitorPasses { get; } = new();

    public string[] ConsultTypeOptions { get; } = new[] { "ROUTINE", "EMERGENCY", "MDT" };
    public string[] DeliveryModeOptions { get; } = new[] { "顺产", "剖宫产", "产钳助产" };
    public string[] GenderOptions { get; } = new[] { "男", "女" };

    public BedCardRow? SelectedBed
    {
        get => _selectedBed;
        set
        {
            if (SetField(ref _selectedBed, value) && value != null && !string.IsNullOrWhiteSpace(value.EncounterId))
            {
                _ = LoadBedClinicalDetailAsync(value.EncounterId);
            }
        }
    }
    public BedCardRow? SwapTargetBed { get => _swapTargetBed; set => SetField(ref _swapTargetBed, value); }
    public MarPlanRow? SelectedMarPlan { get => _selectedMarPlan; set => SetField(ref _selectedMarPlan, value); }

    public ConsultRow? SelectedConsult { get => _selectedConsult; set => SetField(ref _selectedConsult, value); }
    public string CsPatientName { get => _csPatientName; set => SetField(ref _csPatientName, value); }
    public string CsBedCode { get => _csBedCode; set => SetField(ref _csBedCode, value); }
    public string CsRequestDept { get => _csRequestDept; set => SetField(ref _csRequestDept, value); }
    public string CsTargetDept { get => _csTargetDept; set => SetField(ref _csTargetDept, value); }
    public string CsType { get => _csType; set => SetField(ref _csType, value); }
    public string CsPurpose { get => _csPurpose; set => SetField(ref _csPurpose, value); }
    public string CsOpinion { get => _csOpinion; set => SetField(ref _csOpinion, value); }

    public int ManualBillDateInt { get => _manualBillDateInt; set => SetField(ref _manualBillDateInt, value); }
    public DrugReturnRow? SelectedDrugReturn { get => _selectedDrugReturn; set => SetField(ref _selectedDrugReturn, value); }
    public string DrPatientName { get => _drPatientName; set => SetField(ref _drPatientName, value); }
    public string DrBedCode { get => _drBedCode; set => SetField(ref _drBedCode, value); }
    public string DrDrugName { get => _drDrugName; set => SetField(ref _drDrugName, value); }
    public decimal DrQty { get => _drQty; set => SetField(ref _drQty, value); }
    public decimal DrRefundAmt { get => _drRefundAmt; set => SetField(ref _drRefundAmt, value); }
    public string DrReason { get => _drReason; set => SetField(ref _drReason, value); }
    public DischargeMedRow? SelectedDischargeMed { get => _selectedDischargeMed; set => SetField(ref _selectedDischargeMed, value); }
    public string DmPatientName { get => _dmPatientName; set => SetField(ref _dmPatientName, value); }
    public string DmSummary { get => _dmSummary; set => SetField(ref _dmSummary, value); }
    public decimal DmTotalAmount { get => _dmTotalAmount; set => SetField(ref _dmTotalAmount, value); }

    public string MatMotherName { get => _matMotherName; set => SetField(ref _matMotherName, value); }
    public string MatWeeks { get => _matWeeks; set => SetField(ref _matWeeks, value); }
    public string MatMode { get => _matMode; set => SetField(ref _matMode, value); }
    public string MatGender { get => _matGender; set => SetField(ref _matGender, value); }
    public int MatWeight { get => _matWeight; set => SetField(ref _matWeight, value); }
    public decimal MatLength { get => _matLength; set => SetField(ref _matLength, value); }
    public string MatApgar { get => _matApgar; set => SetField(ref _matApgar, value); }

    public DaySurgeryRow? SelectedDaySurgery { get => _selectedDaySurgery; set => SetField(ref _selectedDaySurgery, value); }
    public string DsPatientName { get => _dsPatientName; set => SetField(ref _dsPatientName, value); }
    public string DsSurgeryName { get => _dsSurgeryName; set => SetField(ref _dsSurgeryName, value); }
    public int DsPadssScore { get => _dsPadssScore; set => SetField(ref _dsPadssScore, value); }
    public string DsRemark { get => _dsRemark; set => SetField(ref _dsRemark, value); }

    public VisitorPassRow? SelectedVisitorPass { get => _selectedVisitorPass; set => SetField(ref _selectedVisitorPass, value); }
    public string VpPatientName { get => _vpPatientName; set => SetField(ref _vpPatientName, value); }
    public string VpBedCode { get => _vpBedCode; set => SetField(ref _vpBedCode, value); }
    public string VpVisitorName { get => _vpVisitorName; set => SetField(ref _vpVisitorName, value); }
    public string VpRelation { get => _vpRelation; set => SetField(ref _vpRelation, value); }
    public string VpPhone { get => _vpPhone; set => SetField(ref _vpPhone, value); }
    public int VpValidDays { get => _vpValidDays; set => SetField(ref _vpValidDays, value); }

    public ICommand RefreshAllCommand { get; }
    public ICommand ToggleIsolationCommand { get; }
    public ICommand SwapBedsCommand { get; }
    public ICommand ExecuteMarCommand { get; }
    public ICommand CreateConsultCommand { get; }
    public ICommand AcceptConsultCommand { get; }
    public ICommand CompleteConsultCommand { get; }
    public ICommand TriggerDailyBillingCommand { get; }
    public ICommand CreateDrugReturnCommand { get; }
    public ICommand ApproveDrugReturnCommand { get; }
    public ICommand RejectDrugReturnCommand { get; }
    public ICommand CreateDischargeMedCommand { get; }
    public ICommand DispenseDischargeMedCommand { get; }
    public ICommand CreateMaternityCommand { get; }
    public ICommand CreateDaySurgeryCommand { get; }
    public ICommand DischargeDaySurgeryCommand { get; }
    public ICommand ConvertDaySurgeryIpdCommand { get; }
    public ICommand CreateVisitorPassCommand { get; }
    public ICommand RevokeVisitorPassCommand { get; }

    public InpatientProViewModel(WorkbenchModel workbench, ShellViewModel shell)
    {
        _shell = shell;
        RefreshAllCommand = new RelayCommand(async () => await RefreshAllAsync());
        ToggleIsolationCommand = new RelayCommand(async () => await ToggleIsolationAsync());
        SwapBedsCommand = new RelayCommand(async () => await SwapBedsAsync());
        ExecuteMarCommand = new RelayCommand(async () => await ExecuteMarAsync());
        CreateConsultCommand = new RelayCommand(async () => await CreateConsultAsync());
        AcceptConsultCommand = new RelayCommand(async () => await AcceptConsultAsync());
        CompleteConsultCommand = new RelayCommand(async () => await CompleteConsultAsync());
        TriggerDailyBillingCommand = new RelayCommand(async () => await TriggerDailyBillingAsync());
        CreateDrugReturnCommand = new RelayCommand(async () => await CreateDrugReturnAsync());
        ApproveDrugReturnCommand = new RelayCommand(async () => await ReviewDrugReturnAsync("APPROVED"));
        RejectDrugReturnCommand = new RelayCommand(async () => await ReviewDrugReturnAsync("REJECTED"));
        CreateDischargeMedCommand = new RelayCommand(async () => await CreateDischargeMedAsync());
        DispenseDischargeMedCommand = new RelayCommand(async () => await DispenseDischargeMedAsync());
        CreateMaternityCommand = new RelayCommand(async () => await CreateMaternityAsync());
        CreateDaySurgeryCommand = new RelayCommand(async () => await CreateDaySurgeryAsync());
        DischargeDaySurgeryCommand = new RelayCommand(async () => await UpdateDaySurgeryAsync("DISCHARGED"));
        ConvertDaySurgeryIpdCommand = new RelayCommand(async () => await UpdateDaySurgeryAsync("CONVERTED_IPD"));
        CreateVisitorPassCommand = new RelayCommand(async () => await CreateVisitorPassAsync());
        RevokeVisitorPassCommand = new RelayCommand(async () => await RevokeVisitorPassAsync());

        _ = RefreshAllAsync();
    }

    private async Task RefreshAllAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            await LoadListAsync(http, "api/v4/ipd-pro/beds/board", Beds);
            await LoadListAsync(http, "api/v4/ipd-pro/consultations", Consultations);
            await LoadListAsync(http, "api/v4/ipd-pro/daily-billing/runs", BillingRuns);
            await LoadListAsync(http, "api/v4/ipd-pro/drug-returns", DrugReturns);
            await LoadListAsync(http, "api/v4/ipd-pro/discharge-meds", DischargeMeds);
            await LoadListAsync(http, "api/v4/ipd-pro/maternity", MaternityRecords);
            await LoadListAsync(http, "api/v4/ipd-pro/day-surgery", DaySurgeryCases);
            await LoadListAsync(http, "api/v4/ipd-pro/visitor-passes", VisitorPasses);
        }
        catch (Exception ex)
        {
            _shell.ShowToast($"刷新住院增强数据失败：{ex.Message}", "Error");
        }
    }

    private static async Task LoadListAsync<T>(System.Net.Http.HttpClient http, string url, ObservableCollection<T> target)
    {
        var env = await http.GetFromJsonAsync<ClientApiEnvelope<List<T>>>(url, JsonOpts);
        target.Clear();
        if (env?.Data != null) foreach (var item in env.Data) target.Add(item);
    }

    private async Task LoadBedClinicalDetailAsync(string encounterId)
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            await LoadListAsync(http, $"api/v4/ipd-pro/mar/{Uri.EscapeDataString(encounterId)}", MarPlans);
            await LoadListAsync(http, $"api/v4/ipd-pro/vitals-chart/{Uri.EscapeDataString(encounterId)}", VitalsPoints);
        }
        catch { }
    }

    private async Task ToggleIsolationAsync()
    {
        if (SelectedBed == null) { _shell.ShowToast("请先选择一张床位", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PutAsJsonAsync($"api/v4/ipd-pro/beds/{SelectedBed.BedId}/isolation", new { IsIsolation = SelectedBed.IsIsolation == 0 });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "已更新隔离标志", env?.Code == 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task SwapBedsAsync()
    {
        if (SelectedBed == null || SwapTargetBed == null) { _shell.ShowToast("请选择源床位与目标换床床位", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/ipd-pro/beds/swap", new { SourceBedId = SelectedBed.BedId, TargetBedId = SwapTargetBed.BedId });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "换床完成", env?.Code == 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task ExecuteMarAsync()
    {
        if (SelectedMarPlan == null) { _shell.ShowToast("请先选择一条 MAR 给药执行计划", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsync($"api/v4/ipd-pro/mar/{SelectedMarPlan.PlanId}/execute", null);
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "执行完成", env?.Code == 0 ? "Success" : "Error");
        if (SelectedBed?.EncounterId != null) await LoadBedClinicalDetailAsync(SelectedBed.EncounterId);
    }

    private async Task CreateConsultAsync()
    {
        if (string.IsNullOrWhiteSpace(CsPatientName) || string.IsNullOrWhiteSpace(CsTargetDept)) { _shell.ShowToast("请填写患者姓名与被邀科室", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/ipd-pro/consultations", new
        {
            EncounterId = SelectedBed?.EncounterId,
            PatientName = CsPatientName,
            BedCode = CsBedCode,
            RequestDeptName = CsRequestDept,
            TargetDeptName = CsTargetDept,
            ConsultType = CsType,
            PurposeSummary = CsPurpose
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "已发起会诊", env?.Code == 0 ? "Success" : "Error");
        CsPatientName = string.Empty; CsPurpose = string.Empty;
        await RefreshAllAsync();
    }

    private async Task AcceptConsultAsync()
    {
        if (SelectedConsult == null) { _shell.ShowToast("请先选择会诊单", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsync($"api/v4/ipd-pro/consultations/{SelectedConsult.Id}/accept", null);
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "已签收", env?.Code == 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task CompleteConsultAsync()
    {
        if (SelectedConsult == null || string.IsNullOrWhiteSpace(CsOpinion)) { _shell.ShowToast("请选择会诊单并填写会诊意见", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"api/v4/ipd-pro/consultations/{SelectedConsult.Id}/complete", new { ConsultOpinion = CsOpinion });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "会诊完成并回写EMR", env?.Code == 0 ? "Success" : "Error");
        CsOpinion = string.Empty;
        await RefreshAllAsync();
    }

    private async Task TriggerDailyBillingAsync()
    {
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/ipd-pro/daily-billing/run", new { BillDateInt = ManualBillDateInt });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "每日滚费完成", env?.Code == 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task CreateDrugReturnAsync()
    {
        if (string.IsNullOrWhiteSpace(DrPatientName) || string.IsNullOrWhiteSpace(DrDrugName)) { _shell.ShowToast("请填写患者与退药名称", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/ipd-pro/drug-returns", new
        {
            EncounterId = SelectedBed?.EncounterId,
            PatientName = DrPatientName,
            BedCode = DrBedCode,
            DrugName = DrDrugName,
            ReturnQty = DrQty,
            RefundAmount = DrRefundAmt,
            Reason = DrReason
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "退药申请已提交", env?.Code == 0 ? "Success" : "Error");
        DrDrugName = string.Empty;
        await RefreshAllAsync();
    }

    private async Task ReviewDrugReturnAsync(string decision)
    {
        if (SelectedDrugReturn == null) { _shell.ShowToast("请先选择一条退药申请", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"api/v4/ipd-pro/drug-returns/{SelectedDrugReturn.Id}/review", new { Decision = decision });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "已审批", env?.Code == 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task CreateDischargeMedAsync()
    {
        if (string.IsNullOrWhiteSpace(DmPatientName) || string.IsNullOrWhiteSpace(DmSummary)) { _shell.ShowToast("请填写出院患者姓名与带药明细", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/ipd-pro/discharge-meds", new
        {
            EncounterId = SelectedBed?.EncounterId,
            PatientName = DmPatientName,
            MedicationsSummary = DmSummary,
            TotalAmount = DmTotalAmount
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "已开立出院带药", env?.Code == 0 ? "Success" : "Error");
        DmSummary = string.Empty;
        await RefreshAllAsync();
    }

    private async Task DispenseDischargeMedAsync()
    {
        if (SelectedDischargeMed == null) { _shell.ShowToast("请先选择出院带药单", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsync($"api/v4/ipd-pro/discharge-meds/{SelectedDischargeMed.Id}/dispense", null);
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "已发药核对", env?.Code == 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task CreateMaternityAsync()
    {
        if (string.IsNullOrWhiteSpace(MatMotherName)) { _shell.ShowToast("请填写产妇姓名", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/ipd-pro/maternity", new
        {
            MotherEncounterId = SelectedBed?.EncounterId,
            MotherName = MatMotherName,
            GestationalWeeks = MatWeeks,
            DeliveryMode = MatMode,
            NewbornGender = MatGender,
            BirthWeightG = MatWeight,
            BirthLengthCm = MatLength,
            ApgarScores = MatApgar
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "分娩登记完成", env?.Code == 0 ? "Success" : "Error");
        MatMotherName = string.Empty;
        await RefreshAllAsync();
    }

    private async Task CreateDaySurgeryAsync()
    {
        if (string.IsNullOrWhiteSpace(DsPatientName) || string.IsNullOrWhiteSpace(DsSurgeryName)) { _shell.ShowToast("请填写日间手术患者与术式", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/ipd-pro/day-surgery", new
        {
            PatientName = DsPatientName,
            Gender = "男",
            Age = 38,
            DeptName = "日间手术中心",
            SurgeryName = DsSurgeryName,
            SurgeonName = (string?)null
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "已登记日间手术", env?.Code == 0 ? "Success" : "Error");
        DsPatientName = string.Empty; DsSurgeryName = string.Empty;
        await RefreshAllAsync();
    }

    private async Task UpdateDaySurgeryAsync(string status)
    {
        if (SelectedDaySurgery == null) { _shell.ShowToast("请先选择一例日间手术", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PutAsJsonAsync($"api/v4/ipd-pro/day-surgery/{SelectedDaySurgery.Id}/status", new
        {
            Status = status,
            PadssScore = DsPadssScore,
            Remark = DsRemark
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "已更新日间手术状态", env?.Code == 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task CreateVisitorPassAsync()
    {
        if (string.IsNullOrWhiteSpace(VpPatientName) || string.IsNullOrWhiteSpace(VpVisitorName)) { _shell.ShowToast("请填写患者与陪护人姓名", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/ipd-pro/visitor-passes", new
        {
            EncounterId = SelectedBed?.EncounterId,
            PatientName = VpPatientName,
            BedCode = VpBedCode,
            VisitorName = VpVisitorName,
            RelationName = VpRelation,
            Phone = VpPhone,
            PassType = "ESCORT",
            ValidDays = VpValidDays
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "陪护证已签发", env?.Code == 0 ? "Success" : "Error");
        VpVisitorName = string.Empty;
        await RefreshAllAsync();
    }

    private async Task RevokeVisitorPassAsync()
    {
        if (SelectedVisitorPass == null) { _shell.ShowToast("请先选择一张陪护证", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PutAsync($"api/v4/ipd-pro/visitor-passes/{SelectedVisitorPass.Id}/revoke", null);
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "已注销", env?.Code == 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }
}
