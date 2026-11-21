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

namespace HIS.Client.UrsaShell.Stations.Emergency;

public sealed class ErVisitRow
{
    public string Id { get; set; } = string.Empty;
    public string ErNo { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string Gender { get; set; } = "男";
    public int Age { get; set; }
    public string ChiefComplaint { get; set; } = string.Empty;
    public decimal TempC { get; set; }
    public int HeartRate { get; set; }
    public int RespRate { get; set; }
    public int Sbp { get; set; }
    public int Spo2 { get; set; }
    public int GcsScore { get; set; }
    public int MewsScore { get; set; }
    public int TriageLevel { get; set; }
    public string DestinationZone { get; set; } = string.Empty;
    public string? BedCode { get; set; }
    public short IsGreenChannel { get; set; }
    public decimal UnpaidCreditAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? TriageNurseName { get; set; }

    public string LevelTag => TriageLevel switch
    {
        1 => "Ⅰ级·濒危(抢救)",
        2 => "Ⅱ级·危重(抢救)",
        3 => "Ⅲ级·急症(留观)",
        4 => "Ⅳ级·次急(急诊)",
        _ => "Ⅴ级·非急(急诊)"
    };

    public string GreenTag => IsGreenChannel == 1 ? "绿通" : "常规";
    public string VitalsSummary => $"T:{TempC:F1}℃ HR:{HeartRate} RR:{RespRate} SBP:{Sbp} SpO2:{Spo2}% GCS:{GcsScore}";
}

public sealed class OpdPriorityQueueRow
{
    public string Id { get; set; } = string.Empty;
    public string QueueNo { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public int PriorityLevel { get; set; } = 4;
    public string PriorityTag { get; set; } = "普通";
    public int Status { get; set; }
    public int EstimatedWaitMinutes { get; set; }
    public string WaitDisplay => EstimatedWaitMinutes <= 0 ? "即将叫号" : $"约 {EstimatedWaitMinutes} 分钟";
}

public sealed class OpdCertificateRow
{
    public string Id { get; set; } = string.Empty;
    public string CertNo { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string Gender { get; set; } = "男";
    public int Age { get; set; }
    public string DeptName { get; set; } = string.Empty;
    public string CertType { get; set; } = "DIAGNOSIS_CERT";
    public string DiagnosisText { get; set; } = string.Empty;
    public int RestDays { get; set; }
    public string? AdviceText { get; set; }
    public string? DoctorName { get; set; }
    public string? VerifyHash { get; set; }

    public string CertTypeDisplay => CertType == "SICK_LEAVE" ? "病假条" : "诊断证明书";
    public string ShortHash => string.IsNullOrWhiteSpace(VerifyHash) ? "-" : VerifyHash[..Math.Min(16, VerifyHash.Length)] + "...";
}

public sealed class CopiedRxItemRow
{
    public string DrugId { get; set; } = string.Empty;
    public string DrugName { get; set; } = string.Empty;
    public decimal Dose { get; set; }
    public string Freq { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public int Qty { get; set; }
    public decimal UnitPrice { get; set; }
}

public sealed class EmergencyViewModel : ClientModelBase
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly ShellViewModel _shell;

    // 急诊分诊表单
    private string _erPatientName = string.Empty;
    private string _erGender = "男";
    private int _erAge = 45;
    private string _erComplaint = string.Empty;
    private decimal _erTempC = 37.2m;
    private int _erHr = 92;
    private int _erRr = 18;
    private int _erSbp = 122;
    private int _erSpo2 = 98;
    private int _erGcs = 15;
    private bool _erGreenChannel;
    private string _erBedCode = string.Empty;
    private ErVisitRow? _selectedErVisit;
    private decimal _erAddCredit = 500m;

    // 门诊优先级队列 & 续方
    private OpdPriorityQueueRow? _selectedQueue;
    private int _newPriorityLevel = 2;
    private string _newPriorityTag = "孕产/高龄优先";
    private string _copyRxPatientId = string.Empty;
    private string _copyRxSourceInfo = "输入患者ID点击提取最近一次慢病处方";

    // 诊断证明/病假条表单
    private string _certPatientName = string.Empty;
    private string _certGender = "男";
    private int _certAge = 35;
    private string _certDeptName = "内科门诊";
    private string _certType = "DIAGNOSIS_CERT";
    private string _certDiagnosis = string.Empty;
    private int _certRestDays = 3;
    private string _certAdvice = "建议居家休息，按时服药，不适随诊。";

    public ObservableCollection<ErVisitRow> ErVisits { get; } = new();
    public ObservableCollection<OpdPriorityQueueRow> PriorityQueue { get; } = new();
    public ObservableCollection<OpdCertificateRow> Certificates { get; } = new();
    public ObservableCollection<CopiedRxItemRow> CopiedRxItems { get; } = new();

    public string[] GenderOptions { get; } = new[] { "男", "女" };
    public string[] CertTypeOptions { get; } = new[] { "DIAGNOSIS_CERT", "SICK_LEAVE" };

    public string ErPatientName { get => _erPatientName; set => SetField(ref _erPatientName, value); }
    public string ErGender { get => _erGender; set => SetField(ref _erGender, value); }
    public int ErAge { get => _erAge; set => SetField(ref _erAge, value); }
    public string ErComplaint { get => _erComplaint; set => SetField(ref _erComplaint, value); }
    public decimal ErTempC { get => _erTempC; set => SetField(ref _erTempC, value); }
    public int ErHr { get => _erHr; set => SetField(ref _erHr, value); }
    public int ErRr { get => _erRr; set => SetField(ref _erRr, value); }
    public int ErSbp { get => _erSbp; set => SetField(ref _erSbp, value); }
    public int ErSpo2 { get => _erSpo2; set => SetField(ref _erSpo2, value); }
    public int ErGcs { get => _erGcs; set => SetField(ref _erGcs, value); }
    public bool ErGreenChannel { get => _erGreenChannel; set => SetField(ref _erGreenChannel, value); }
    public string ErBedCode { get => _erBedCode; set => SetField(ref _erBedCode, value); }
    public ErVisitRow? SelectedErVisit { get => _selectedErVisit; set => SetField(ref _selectedErVisit, value); }
    public decimal ErAddCredit { get => _erAddCredit; set => SetField(ref _erAddCredit, value); }

    public OpdPriorityQueueRow? SelectedQueue { get => _selectedQueue; set => SetField(ref _selectedQueue, value); }
    public int NewPriorityLevel { get => _newPriorityLevel; set => SetField(ref _newPriorityLevel, value); }
    public string NewPriorityTag { get => _newPriorityTag; set => SetField(ref _newPriorityTag, value); }
    public string CopyRxPatientId { get => _copyRxPatientId; set => SetField(ref _copyRxPatientId, value); }
    public string CopyRxSourceInfo { get => _copyRxSourceInfo; set => SetField(ref _copyRxSourceInfo, value); }

    public string CertPatientName { get => _certPatientName; set => SetField(ref _certPatientName, value); }
    public string CertGender { get => _certGender; set => SetField(ref _certGender, value); }
    public int CertAge { get => _certAge; set => SetField(ref _certAge, value); }
    public string CertDeptName { get => _certDeptName; set => SetField(ref _certDeptName, value); }
    public string CertType { get => _certType; set => SetField(ref _certType, value); }
    public string CertDiagnosis { get => _certDiagnosis; set => SetField(ref _certDiagnosis, value); }
    public int CertRestDays { get => _certRestDays; set => SetField(ref _certRestDays, value); }
    public string CertAdvice { get => _certAdvice; set => SetField(ref _certAdvice, value); }

    public ICommand RefreshAllCommand { get; }
    public ICommand SubmitErTriageCommand { get; }
    public ICommand MoveToResusCommand { get; }
    public ICommand MoveToObsCommand { get; }
    public ICommand ConvertToIpdCommand { get; }
    public ICommand AddGreenCreditCommand { get; }
    public ICommand UpdateQueuePriorityCommand { get; }
    public ICommand CopyLastRxCommand { get; }
    public ICommand IssueCertificateCommand { get; }

    public EmergencyViewModel(WorkbenchModel workbench, ShellViewModel shell)
    {
        _shell = shell;
        RefreshAllCommand = new RelayCommand(async () => await RefreshAllAsync());
        SubmitErTriageCommand = new RelayCommand(async () => await SubmitErTriageAsync());
        MoveToResusCommand = new RelayCommand(async () => await UpdateErStatusAsync("RESUSCITATING", "RESUS_ROOM", null));
        MoveToObsCommand = new RelayCommand(async () => await UpdateErStatusAsync("OBSERVING", "OBS_WARD", null));
        ConvertToIpdCommand = new RelayCommand(async () => await UpdateErStatusAsync("ADMITTED_IPD", "IPD_WARD", null));
        AddGreenCreditCommand = new RelayCommand(async () => await UpdateErStatusAsync(SelectedErVisit?.Status ?? "RESUSCITATING", null, ErAddCredit));
        UpdateQueuePriorityCommand = new RelayCommand(async () => await UpdateQueuePriorityAsync());
        CopyLastRxCommand = new RelayCommand(async () => await CopyLastRxAsync());
        IssueCertificateCommand = new RelayCommand(async () => await IssueCertificateAsync());

        _ = RefreshAllAsync();
    }

    private async Task RefreshAllAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var erEnv = await http.GetFromJsonAsync<ClientApiEnvelope<List<ErVisitRow>>>("api/v4/er-opd/er/visits", JsonOpts);
            ErVisits.Clear();
            if (erEnv?.Data != null) foreach (var r in erEnv.Data) ErVisits.Add(r);

            var qEnv = await http.GetFromJsonAsync<ClientApiEnvelope<List<OpdPriorityQueueRow>>>("api/v4/er-opd/opd/priority-queue", JsonOpts);
            PriorityQueue.Clear();
            if (qEnv?.Data != null) foreach (var r in qEnv.Data) PriorityQueue.Add(r);

            var cEnv = await http.GetFromJsonAsync<ClientApiEnvelope<List<OpdCertificateRow>>>("api/v4/er-opd/opd/certificates", JsonOpts);
            Certificates.Clear();
            if (cEnv?.Data != null) foreach (var r in cEnv.Data) Certificates.Add(r);
        }
        catch (Exception ex)
        {
            _shell.ShowToast($"刷新急诊与门诊数据失败：{ex.Message}", "Error");
        }
    }

    private async Task SubmitErTriageAsync()
    {
        if (string.IsNullOrWhiteSpace(ErPatientName) || string.IsNullOrWhiteSpace(ErComplaint))
        {
            _shell.ShowToast("请填写患者姓名与急诊主诉", "Warning");
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var resp = await http.PostAsJsonAsync("api/v4/er-opd/er/triage", new
            {
                PatientName = ErPatientName,
                Gender = ErGender,
                Age = ErAge,
                ChiefComplaint = ErComplaint,
                TempC = ErTempC,
                HeartRate = ErHr,
                RespRate = ErRr,
                Sbp = ErSbp,
                Spo2 = ErSpo2,
                GcsScore = ErGcs,
                IsGreenChannel = ErGreenChannel,
                BedCode = string.IsNullOrWhiteSpace(ErBedCode) ? null : ErBedCode
            });
            var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
            if (env?.Code == 0)
            {
                _shell.ShowToast(env.Message ?? "急诊分诊完成", "Success");
                ErPatientName = string.Empty;
                ErComplaint = string.Empty;
                ErBedCode = string.Empty;
                await RefreshAllAsync();
            }
            else
            {
                _shell.ShowToast(env?.Message ?? "急诊分诊失败", "Error");
            }
        }
        catch (Exception ex)
        {
            _shell.ShowToast($"急诊分诊异常：{ex.Message}", "Error");
        }
    }

    private async Task UpdateErStatusAsync(string status, string? zone, decimal? addCredit)
    {
        if (SelectedErVisit == null)
        {
            _shell.ShowToast("请先在列表中选择一名急诊患者", "Warning");
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var resp = await http.PutAsJsonAsync($"api/v4/er-opd/er/visits/{SelectedErVisit.Id}/status", new
            {
                Status = status,
                DestinationZone = zone,
                BedCode = string.IsNullOrWhiteSpace(ErBedCode) ? null : ErBedCode,
                AddCreditAmount = addCredit
            });
            var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
            _shell.ShowToast(env?.Message ?? "已更新状态", env?.Code == 0 ? "Success" : "Error");
            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            _shell.ShowToast($"操作失败：{ex.Message}", "Error");
        }
    }

    private async Task UpdateQueuePriorityAsync()
    {
        if (SelectedQueue == null)
        {
            _shell.ShowToast("请先选择候诊队列中的患者", "Warning");
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var resp = await http.PutAsJsonAsync($"api/v4/er-opd/opd/priority-queue/{SelectedQueue.Id}", new
            {
                PriorityLevel = NewPriorityLevel,
                PriorityTag = NewPriorityTag
            });
            var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
            _shell.ShowToast(env?.Message ?? "优先级别已更新", env?.Code == 0 ? "Success" : "Error");
            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            _shell.ShowToast($"调整优先级失败：{ex.Message}", "Error");
        }
    }

    private async Task CopyLastRxAsync()
    {
        if (string.IsNullOrWhiteSpace(CopyRxPatientId))
        {
            _shell.ShowToast("请输入患者ID以查询最近历史处方", "Warning");
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var resp = await http.PostAsync($"api/v4/er-opd/opd/copy-last-rx/{Uri.EscapeDataString(CopyRxPatientId.Trim())}", null);
            var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
            CopiedRxItems.Clear();
            if (env?.Code == 0 && env.Data.ValueKind == JsonValueKind.Object)
            {
                CopyRxSourceInfo = env.Message ?? "已提取历史处方";
                if (env.Data.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    var items = JsonSerializer.Deserialize<List<CopiedRxItemRow>>(arr.GetRawText(), JsonOpts);
                    if (items != null) foreach (var it in items) CopiedRxItems.Add(it);
                }
                _shell.ShowToast(CopyRxSourceInfo, "Success");
            }
            else
            {
                CopyRxSourceInfo = env?.Message ?? "未查到历史处方";
                _shell.ShowToast(CopyRxSourceInfo, "Warning");
            }
        }
        catch (Exception ex)
        {
            _shell.ShowToast($"提取历史处方失败：{ex.Message}", "Error");
        }
    }

    private async Task IssueCertificateAsync()
    {
        if (string.IsNullOrWhiteSpace(CertPatientName) || string.IsNullOrWhiteSpace(CertDiagnosis))
        {
            _shell.ShowToast("请填写患者姓名与临床诊断", "Warning");
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var resp = await http.PostAsJsonAsync("api/v4/er-opd/opd/certificates", new
            {
                VisitId = (string?)null,
                PatientName = CertPatientName,
                Gender = CertGender,
                Age = CertAge,
                DeptName = CertDeptName,
                CertType = CertType,
                DiagnosisText = CertDiagnosis,
                RestDays = CertRestDays,
                AdviceText = CertAdvice
            });
            var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
            if (env?.Code == 0)
            {
                _shell.ShowToast(env.Message ?? "证明书签发成功", "Success");
                CertPatientName = string.Empty;
                CertDiagnosis = string.Empty;
                await RefreshAllAsync();
            }
            else
            {
                _shell.ShowToast(env?.Message ?? "证明书签发失败", "Error");
            }
        }
        catch (Exception ex)
        {
            _shell.ShowToast($"签发异常：{ex.Message}", "Error");
        }
    }
}
