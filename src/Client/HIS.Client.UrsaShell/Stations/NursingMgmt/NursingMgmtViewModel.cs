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

namespace HIS.Client.UrsaShell.Stations.NursingMgmt;

public sealed class CompetencyRow
{
    public string Id { get; set; } = string.Empty;
    public string NurseUserId { get; set; } = string.Empty;
    public string NurseName { get; set; } = string.Empty;
    public string WardName { get; set; } = string.Empty;
    public string NLevel { get; set; } = "N1";
    public string? SpecialtyCerts { get; set; }
    public decimal TheoryScore { get; set; }
    public decimal SkillScore { get; set; }
    public decimal CmeCredits { get; set; }
}

public sealed class ShiftRow
{
    public string Id { get; set; } = string.Empty;
    public string WardName { get; set; } = string.Empty;
    public int ShiftDateInt { get; set; }
    public string NurseUserId { get; set; } = string.Empty;
    public string NurseName { get; set; } = string.Empty;
    public string NLevel { get; set; } = "N1";
    public string ShiftCode { get; set; } = "DAY";
    public decimal ShiftHours { get; set; }
}

public sealed class NurQcRow
{
    public string Id { get; set; } = string.Empty;
    public string InspectNo { get; set; } = string.Empty;
    public string WardName { get; set; } = string.Empty;
    public string InspectLevel { get; set; } = string.Empty;
    public int InspectDateInt { get; set; }
    public decimal Score { get; set; }
    public string? DefectSummary { get; set; }
    public string? RectificationPlan { get; set; }
    public string Status { get; set; } = "OPEN";
}

public sealed class HandoverRow
{
    public string Id { get; set; } = string.Empty;
    public string HandoverNo { get; set; } = string.Empty;
    public string WardName { get; set; } = string.Empty;
    public int ShiftDateInt { get; set; }
    public string FromShiftCode { get; set; } = string.Empty;
    public string ToShiftCode { get; set; } = string.Empty;
    public int TotalPatients { get; set; }
    public int CriticalPatients { get; set; }
    public string SbarSummary { get; set; } = string.Empty;
    public string FromNurseName { get; set; } = string.Empty;
    public string? ToNurseName { get; set; }
    public string Status { get; set; } = "PENDING_ACK";
}

public sealed class NursingMgmtViewModel : ClientModelBase
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly ShellViewModel _shell;

    private string _acuityBanner = "正在测算全院在院患者 Acuity 护患比与人力缺口...";

    private string _cUid = string.Empty, _cName = string.Empty, _cWard = "心内一病区", _cLevel = "N2", _cCerts = "ICU/PICC";
    private decimal _cTheory = 92m, _cSkill = 95m;

    private string _sWard = "心内一病区", _sUid = string.Empty, _sName = string.Empty, _sCode = "DAY";
    private int _sDateInt = int.Parse(DateTime.Now.ToString("yyyyMMdd"));

    private NurQcRow? _selectedQc;
    private string _qWard = "心内一病区", _qLevel = "NURSING_DEPT", _qDefect = string.Empty, _qPlan = string.Empty;
    private decimal _qScore = 94m;

    private HandoverRow? _selectedHandover;
    private string _hWard = "心内一病区", _hFrom = "DAY", _hTo = "EVENING", _hSbar = string.Empty, _hAckNurse = string.Empty;

    public ObservableCollection<CompetencyRow> Competencies { get; } = new();
    public ObservableCollection<ShiftRow> Shifts { get; } = new();
    public ObservableCollection<NurQcRow> QcInspections { get; } = new();
    public ObservableCollection<HandoverRow> Handovers { get; } = new();

    public string[] NLevels { get; } = new[] { "N0", "N1", "N2", "N3", "N4" };
    public string[] ShiftCodes { get; } = new[] { "DAY", "EVENING", "NIGHT", "OFF" };
    public string[] QcLevels { get; } = new[] { "WARD_SELF", "DEPT_HEAD", "NURSING_DEPT" };

    public string AcuityBanner { get => _acuityBanner; set => SetField(ref _acuityBanner, value); }
    public string CUid { get => _cUid; set => SetField(ref _cUid, value); }
    public string CName { get => _cName; set => SetField(ref _cName, value); }
    public string CWard { get => _cWard; set => SetField(ref _cWard, value); }
    public string CLevel { get => _cLevel; set => SetField(ref _cLevel, value); }
    public string CCerts { get => _cCerts; set => SetField(ref _cCerts, value); }
    public decimal CTheory { get => _cTheory; set => SetField(ref _cTheory, value); }
    public decimal CSkill { get => _cSkill; set => SetField(ref _cSkill, value); }

    public string SWard { get => _sWard; set => SetField(ref _sWard, value); }
    public int SDateInt { get => _sDateInt; set => SetField(ref _sDateInt, value); }
    public string SUid { get => _sUid; set => SetField(ref _sUid, value); }
    public string SName { get => _sName; set => SetField(ref _sName, value); }
    public string SCode { get => _sCode; set => SetField(ref _sCode, value); }

    public NurQcRow? SelectedQc { get => _selectedQc; set => SetField(ref _selectedQc, value); }
    public string QWard { get => _qWard; set => SetField(ref _qWard, value); }
    public string QLevel { get => _qLevel; set => SetField(ref _qLevel, value); }
    public decimal QScore { get => _qScore; set => SetField(ref _qScore, value); }
    public string QDefect { get => _qDefect; set => SetField(ref _qDefect, value); }
    public string QPlan { get => _qPlan; set => SetField(ref _qPlan, value); }

    public HandoverRow? SelectedHandover { get => _selectedHandover; set => SetField(ref _selectedHandover, value); }
    public string HWard { get => _hWard; set => SetField(ref _hWard, value); }
    public string HFrom { get => _hFrom; set => SetField(ref _hFrom, value); }
    public string HTo { get => _hTo; set => SetField(ref _hTo, value); }
    public string HSbar { get => _hSbar; set => SetField(ref _hSbar, value); }
    public string HAckNurse { get => _hAckNurse; set => SetField(ref _hAckNurse, value); }

    public ICommand RefreshAllCommand { get; }
    public ICommand SaveCompetencyCommand { get; }
    public ICommand ScheduleShiftCommand { get; }
    public ICommand CreateQcCommand { get; }
    public ICommand CloseQcCommand { get; }
    public ICommand CreateHandoverCommand { get; }
    public ICommand AckHandoverCommand { get; }

    public NursingMgmtViewModel(WorkbenchModel workbench, ShellViewModel shell)
    {
        _shell = shell;
        RefreshAllCommand = new RelayCommand(async () => await RefreshAllAsync());
        SaveCompetencyCommand = new RelayCommand(async () => await SaveCompetencyAsync());
        ScheduleShiftCommand = new RelayCommand(async () => await ScheduleShiftAsync());
        CreateQcCommand = new RelayCommand(async () => await CreateQcAsync());
        CloseQcCommand = new RelayCommand(async () => await CloseQcAsync());
        CreateHandoverCommand = new RelayCommand(async () => await CreateHandoverAsync());
        AckHandoverCommand = new RelayCommand(async () => await AckHandoverAsync());
        _ = RefreshAllAsync();
    }

    private async Task RefreshAllAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            await LoadListAsync(http, "api/v4/nursing-mgmt/competencies", Competencies);
            await LoadListAsync(http, "api/v4/nursing-mgmt/shifts", Shifts);
            await LoadListAsync(http, "api/v4/nursing-mgmt/qc-inspections", QcInspections);
            await LoadListAsync(http, "api/v4/nursing-mgmt/handovers", Handovers);

            var ac = await http.GetFromJsonAsync<ClientApiEnvelope<JsonElement>>($"api/v4/nursing-mgmt/staffing-acuity?shiftDateInt={SDateInt}", JsonOpts);
            if (ac?.Code == 0 && ac.Data.ValueKind == JsonValueKind.Object)
            {
                AcuityBanner = $"在院 {ac.Data.GetProperty("activeInpatients").GetInt32()} 人（特级 {ac.Data.GetProperty("specialCareCount").GetInt32()} / 一级 {ac.Data.GetProperty("level1Count").GetInt32()}） · Acuity 需配护士 {ac.Data.GetProperty("requiredNursesByAcuity").GetInt32()} 人 · 当日已排 {ac.Data.GetProperty("scheduledActiveNurses").GetInt32()} 人 · {ac.Data.GetProperty("status").GetString()}";
            }
        }
        catch (Exception ex) { _shell.ShowToast($"刷新护理管理数据失败：{ex.Message}", "Error"); }
    }

    private static async Task LoadListAsync<T>(System.Net.Http.HttpClient http, string url, ObservableCollection<T> target)
    {
        var env = await http.GetFromJsonAsync<ClientApiEnvelope<List<T>>>(url, JsonOpts);
        target.Clear();
        if (env?.Data != null) foreach (var it in env.Data) target.Add(it);
    }

    private async Task SaveCompetencyAsync()
    {
        if (string.IsNullOrWhiteSpace(CUid) || string.IsNullOrWhiteSpace(CName)) { _shell.ShowToast("请填写护士工号与姓名", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/nursing-mgmt/competencies", new { NurseUserId = CUid, NurseName = CName, WardName = CWard, NLevel = CLevel, SpecialtyCerts = CCerts, TheoryScore = CTheory, SkillScore = CSkill, CmeCredits = 20m });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "已保存", env?.Code == 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task ScheduleShiftAsync()
    {
        if (string.IsNullOrWhiteSpace(SUid) || string.IsNullOrWhiteSpace(SName)) { _shell.ShowToast("请填写护士工号与姓名", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/nursing-mgmt/shifts", new { WardName = SWard, ShiftDateInt = SDateInt, NurseUserId = SUid, NurseName = SName, NLevel = CLevel, ShiftCode = SCode, ShiftHours = SCode == "NIGHT" ? 10m : 8m, Remark = "" });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "排班完成", env?.Code == 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task CreateQcAsync()
    {
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/nursing-mgmt/qc-inspections", new { WardName = QWard, InspectLevel = QLevel, Score = QScore, DefectSummary = QDefect, RectificationPlan = QPlan });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "已提交督查", env?.Code == 0 ? "Success" : "Error");
        QDefect = string.Empty;
        await RefreshAllAsync();
    }

    private async Task CloseQcAsync()
    {
        if (SelectedQc == null) { _shell.ShowToast("请先选择质控记录", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PutAsJsonAsync($"api/v4/nursing-mgmt/qc-inspections/{SelectedQc.Id}/rectify", new { RectificationPlan = string.IsNullOrWhiteSpace(QPlan) ? "已完成PDCA整改复查合格" : QPlan, Status = "CLOSED" });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "已整改关闭", env?.Code == 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }

    private async Task CreateHandoverAsync()
    {
        if (string.IsNullOrWhiteSpace(HSbar)) { _shell.ShowToast("请填写 SBAR 交接重点", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/nursing-mgmt/handovers", new { WardName = HWard, FromShiftCode = HFrom, ToShiftCode = HTo, TotalPatients = 30, CriticalPatients = 2, SurgeryPatients = 3, HighRiskFallUlcer = 4, SbarSummary = HSbar });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "交接已发起", env?.Code == 0 ? "Success" : "Error");
        HSbar = string.Empty;
        await RefreshAllAsync();
    }

    private async Task AckHandoverAsync()
    {
        if (SelectedHandover == null || string.IsNullOrWhiteSpace(HAckNurse)) { _shell.ShowToast("请选择交接单并填写接班护士", "Warning"); return; }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"api/v4/nursing-mgmt/handovers/{SelectedHandover.Id}/ack", new { ToNurseName = HAckNurse });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "双签完成", env?.Code == 0 ? "Success" : "Error");
        await RefreshAllAsync();
    }
}
