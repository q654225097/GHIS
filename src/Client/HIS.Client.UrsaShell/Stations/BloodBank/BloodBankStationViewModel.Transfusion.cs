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
using HIS.Client.UrsaShell.Shell;

namespace HIS.Client.UrsaShell.Stations.BloodBank;

public sealed class BloodExecutionItem : ClientModelBase
{
    public string Id { get; set; } = string.Empty;
    public string BloodRequestId { get; set; } = string.Empty;
    public string BagBarcode { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string Status { get; set; } = "TRANSFUSING";
    public long StartedAtMs { get; set; }
    public long? FifteenMinCheckedAtMs { get; set; }
    public long? EmptyBagReturnedAtMs { get; set; }
    public string? BagStorageLocation { get; set; }
    public int RetentionHours { get; set; }

    public string DisplayLine => $"血袋 {BagBarcode} · {Status}"
        + (FifteenMinCheckedAtMs is > 0 ? " · ✓已做15min监测" : " · ⏳待15min监测")
        + (EmptyBagReturnedAtMs is > 0 ? $" · ✓空袋入{BagStorageLocation}({RetentionHours}h)" : string.Empty);
}

public sealed partial class BloodBankStationViewModel
{
    private static readonly JsonSerializerOptions BloodExecJsonOpts = new() { PropertyNameCaseInsensitive = true };

    private BloodExecutionItem? _selectedExecution;
    private string _transfusionWristbandInput = string.Empty;
    private string _transfusionDualNurseInput = "RN-WARD-02";
    private decimal _mid15Temp = 36.8m;
    private int _mid15Pulse = 78;
    private int _mid15Resp = 18;
    private string _mid15Bp = "120/78";
    private int _mid15DripRate = 18;
    private bool _mid15Adverse;
    private string _mid15Notes = "输注前15分钟慢滴（<=20滴/分）平稳，无皮疹寒战";
    private string _bagStorageLocation = "专用2-6℃留样冰箱-01";
    private int _bagRetentionHours = 24;

    public ObservableCollection<BloodExecutionItem> Executions { get; } = new();

    public BloodExecutionItem? SelectedExecution { get => _selectedExecution; set => SetField(ref _selectedExecution, value); }
    public string TransfusionWristbandInput { get => _transfusionWristbandInput; set => SetField(ref _transfusionWristbandInput, value ?? string.Empty); }
    public string TransfusionDualNurseInput { get => _transfusionDualNurseInput; set => SetField(ref _transfusionDualNurseInput, value ?? string.Empty); }
    public decimal Mid15Temp { get => _mid15Temp; set => SetField(ref _mid15Temp, value); }
    public int Mid15Pulse { get => _mid15Pulse; set => SetField(ref _mid15Pulse, value); }
    public int Mid15Resp { get => _mid15Resp; set => SetField(ref _mid15Resp, value); }
    public string Mid15Bp { get => _mid15Bp; set => SetField(ref _mid15Bp, value ?? "120/78"); }
    public int Mid15DripRate { get => _mid15DripRate; set => SetField(ref _mid15DripRate, value); }
    public bool Mid15Adverse { get => _mid15Adverse; set => SetField(ref _mid15Adverse, value); }
    public string Mid15Notes { get => _mid15Notes; set => SetField(ref _mid15Notes, value ?? string.Empty); }
    public string BagStorageLocation { get => _bagStorageLocation; set => SetField(ref _bagStorageLocation, value ?? string.Empty); }
    public int BagRetentionHours { get => _bagRetentionHours; set => SetField(ref _bagRetentionHours, value); }

    private ICommand? _refreshExecutionsCommand;
    private ICommand? _startTransfusionCommand;
    private ICommand? _record15MinCheckCommand;
    private ICommand? _finishTransfusionCommand;
    private ICommand? _recoverBagCommand;

    public ICommand RefreshExecutionsCommand => _refreshExecutionsCommand ??= new AsyncRelayCommand(LoadExecutionsAsync);
    public ICommand StartTransfusionCommand => _startTransfusionCommand ??= new AsyncRelayCommand(StartTransfusionAsync);
    public ICommand Record15MinCheckCommand => _record15MinCheckCommand ??= new AsyncRelayCommand(Record15MinCheckAsync);
    public ICommand FinishTransfusionCommand => _finishTransfusionCommand ??= new AsyncRelayCommand(FinishTransfusionAsync);
    public ICommand RecoverBagCommand => _recoverBagCommand ??= new AsyncRelayCommand(RecoverBagAsync);

    public async Task LoadExecutionsAsync()
    {
        try
        {
            using var resp = await _http.GetAsync("/api/v4/blood/executions");
            if (!resp.IsSuccessStatusCode) return;
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<List<BloodExecutionItem>>>(BloodExecJsonOpts);
            if (env?.Code is 200 or 0 && env.Data != null)
            {
                string? keepId = SelectedExecution?.Id;
                Executions.Clear();
                foreach (var ex in env.Data) Executions.Add(ex);
                SelectedExecution = (!string.IsNullOrWhiteSpace(keepId) ? Executions.FirstOrDefault(x => x.Id == keepId) : null)
                    ?? Executions.FirstOrDefault();
            }
        }
        catch { }
    }

    private async Task StartTransfusionAsync()
    {
        if (SelectedRequest == null) { _shell?.ShowToast("请先选中一条已双签发血申请单", "Warning"); return; }
        string bag = SelectedRequest.BagBarcode ?? SelectedBagForMatch?.BagBarcode ?? string.Empty;
        if (string.IsNullOrWhiteSpace(bag)) { _shell?.ShowToast("请先选中配血/发血血袋条码", "Warning"); return; }

        string wristband = string.IsNullOrWhiteSpace(TransfusionWristbandInput) ? SelectedRequest.PatientId : TransfusionWristbandInput.Trim();
        var dto = new
        {
            BloodRequestId = SelectedRequest.Id,
            BagBarcode = bag,
            EncounterId = SelectedRequest.EncounterId,
            PatientId = SelectedRequest.PatientId,
            PatientWristbandCode = wristband,
            DualCheckNurseId = TransfusionDualNurseInput.Trim(),
            PreVitals = new Dictionary<string, object> { ["temperature"] = 36.6m, ["bp"] = "118/76", ["pulse"] = 76 }
        };
        using var resp = await _http.PostAsJsonAsync("/api/v4/blood/executions/start", dto);
        var env = await resp.Content.ReadFromJsonAsync<ApiResponse<BloodExecutionItem>>(BloodExecJsonOpts);
        if (!resp.IsSuccessStatusCode || env?.Code is not (200 or 0))
        {
            _shell?.ShowToast(env?.Message ?? "启动床旁输血失败", "Danger");
            return;
        }
        _shell?.ShowToast(env.Message ?? "床旁输血已启动", "Success");
        await LoadDataAsync();
        await LoadExecutionsAsync();
    }

    private async Task Record15MinCheckAsync()
    {
        if (SelectedExecution == null) { _shell?.ShowToast("请先选择一条正在输血记录", "Warning"); return; }
        var dto = new
        {
            Temperature = Mid15Temp,
            Pulse = Mid15Pulse,
            Respiration = Mid15Resp,
            BloodPressure = Mid15Bp.Trim(),
            DripRatePerMin = Mid15DripRate,
            AdverseReactionObserved = Mid15Adverse,
            Notes = Mid15Notes.Trim()
        };
        using var resp = await _http.PostAsJsonAsync($"/api/v4/blood/executions/{Uri.EscapeDataString(SelectedExecution.Id)}/fifteen-minute-check", dto);
        var env = await resp.Content.ReadFromJsonAsync<ApiResponse<BloodExecutionItem>>(BloodExecJsonOpts);
        if (!resp.IsSuccessStatusCode || env?.Code is not (200 or 0))
        {
            _shell?.ShowToast(env?.Message ?? "15分钟监测提交失败", "Danger");
            return;
        }
        _shell?.ShowToast(env.Message ?? "15分钟慢滴监测已归档", "Success");
        await LoadExecutionsAsync();
    }

    private async Task FinishTransfusionAsync()
    {
        if (SelectedExecution == null) { _shell?.ShowToast("请先选择输血执行单", "Warning"); return; }
        var dto = new
        {
            Mid15Vitals = new Dictionary<string, object> { ["dripRate"] = Mid15DripRate },
            PostVitals = new Dictionary<string, object> { ["temperature"] = Mid15Temp, ["bp"] = Mid15Bp },
            EmptyBagReturned = false
        };
        using var resp = await _http.PostAsJsonAsync($"/api/v4/blood/executions/{Uri.EscapeDataString(SelectedExecution.Id)}/finish", dto);
        var env = await resp.Content.ReadFromJsonAsync<ApiResponse<BloodExecutionItem>>(BloodExecJsonOpts);
        if (!resp.IsSuccessStatusCode || env?.Code is not (200 or 0))
        {
            _shell?.ShowToast(env?.Message ?? "结束输血失败", "Danger");
            return;
        }
        _shell?.ShowToast(env.Message ?? "输血完成", "Success");
        await LoadExecutionsAsync();
    }

    private async Task RecoverBagAsync()
    {
        if (SelectedExecution == null) { _shell?.ShowToast("请先选择待回收空血袋的输血执行单", "Warning"); return; }
        var dto = new
        {
            BagBarcode = SelectedExecution.BagBarcode,
            StorageLocation = BagStorageLocation.Trim(),
            RetentionHours = BagRetentionHours,
            Remark = "空血袋已回收交接血库2-6℃留样保存24h"
        };
        using var resp = await _http.PostAsJsonAsync($"/api/v4/blood/executions/{Uri.EscapeDataString(SelectedExecution.Id)}/bag-recovery", dto);
        var env = await resp.Content.ReadFromJsonAsync<ApiResponse<BloodExecutionItem>>(BloodExecJsonOpts);
        if (!resp.IsSuccessStatusCode || env?.Code is not (200 or 0))
        {
            _shell?.ShowToast(env?.Message ?? "空血袋回收失败", "Danger");
            return;
        }
        _shell?.ShowToast(env.Message ?? "空血袋回收留样完成", "Success");
        await LoadDataAsync();
        await LoadExecutionsAsync();
    }
}
