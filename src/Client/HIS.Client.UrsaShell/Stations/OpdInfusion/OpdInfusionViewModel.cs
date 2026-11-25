using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.BuildingBlocks.Common;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Services;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.OpdInfusion;

public sealed class InfusionSeatTile : ClientModelBase
{
    public string SeatNo { get; set; } = string.Empty;
    public bool IsOccupied { get; set; }
    public string? OrderId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string DetailText { get; set; } = "空闲";
    public string BadgeKind => IsOccupied ? "Primary" : "Secondary";
}

public sealed class InfusionBottleVm
{
    public string Id { get; set; } = string.Empty;
    public string InfusionOrderId { get; set; } = string.Empty;
    public string BottleBarcode { get; set; } = string.Empty;
    public int SequenceNo { get; set; }
    public string DrugNames { get; set; } = string.Empty;
    public string? Spec { get; set; }
    public string? DosageText { get; set; }
    public int Status { get; set; }
    public long? StartTimeMs { get; set; }
    public long? EndTimeMs { get; set; }

    public string StatusLabel => Status switch
    {
        0 => "待输注",
        1 => "输注中",
        2 => "已输完",
        _ => "未知"
    };
}

public sealed class InfusionInspectionVm
{
    public string Id { get; set; } = string.Empty;
    public string InfusionOrderId { get; set; } = string.Empty;
    public string? BottleId { get; set; }
    public string InspectNurseId { get; set; } = string.Empty;
    public string InspectNurseName { get; set; } = string.Empty;
    public int DripSpeed { get; set; }
    public int IsSwollen { get; set; }
    public int HasAdverseReaction { get; set; }
    public string? Notes { get; set; }
    public long InspectedAtMs { get; set; }

    public string TimeText => InspectedAtMs > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(InspectedAtMs).ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm:ss")
        : "-";

    public string AlertSummary
    {
        get
        {
            var flags = new List<string> { $"滴速 {DripSpeed} 滴/分" };
            if (IsSwollen == 1) flags.Add("穿刺点肿胀/渗液");
            if (HasAdverseReaction == 1) flags.Add("输液不良反应");
            if (!string.IsNullOrWhiteSpace(Notes)) flags.Add(Notes!);
            return string.Join(" · ", flags);
        }
    }
}

public sealed class InfusionOrderVm
{
    public string Id { get; set; } = string.Empty;
    public string InfusionNo { get; set; } = string.Empty;
    public string PrescriptionId { get; set; } = string.Empty;
    public string VisitId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string? SeatNo { get; set; }
    public int Status { get; set; }
    public string? CompoundNurseId { get; set; }
    public string? CompoundCheckNurseId { get; set; }
    public long? CompoundedAtMs { get; set; }
    public string? PunctureNurseId { get; set; }
    public long? PuncturedAtMs { get; set; }
    public int DripRate { get; set; }
    public int TotalBottles { get; set; }
    public int CompletedBottles { get; set; }
    public long? CompletedAtMs { get; set; }
    public List<InfusionBottleVm> Bottles { get; set; } = new();

    public string StatusLabel => Status switch
    {
        0 => "待配药复核",
        1 => "已配待穿刺",
        2 => "正在输液",
        3 => "输液换瓶中",
        4 => "已拔针完成",
        5 => "异常终止",
        _ => "处理中"
    };

    public string ProgressText => $"第 {CompletedBottles}/{Math.Max(1, TotalBottles)} 瓶 · {(string.IsNullOrWhiteSpace(SeatNo) ? "待分配座位" : SeatNo)}";
}

public sealed class OpdInfusionViewModel : ClientModelBase, IStationLifecycle
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly WorkbenchModel _workbench;
    private readonly ShellViewModel? _shell;

    private string _searchKeyword = string.Empty;
    private int? _statusFilter;
    private InfusionOrderVm? _selectedOrder;
    private string _prescriptionIdInput = string.Empty;
    private string _compoundNurseInput = string.Empty;
    private string _compoundCheckNurseInput = "USER-NURSE-W1";
    private string _patientWristbandInput = string.Empty;
    private string _bottleBarcodeInput = string.Empty;
    private string _seatNoInput = "INF-01";
    private int _dripRateInput = 40;
    private int _inspectDripSpeed = 40;
    private bool _inspectIsSwollen;
    private bool _inspectHasAdverse;
    private string _inspectNotes = "局部无红肿渗液，患者诉无不适";
    private string _nextBottleBarcodeInput = string.Empty;
    private bool _isBusy;
    private string _statusMessage = "就绪 · 实时连接门诊输液执行库";

    public OpdInfusionViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        _workbench = workbench;
        _shell = shell;
        _compoundNurseInput = string.IsNullOrWhiteSpace(SessionContext.Current.UserId)
            ? "USER-NURSE-W2"
            : SessionContext.Current.UserId!;

        RefreshCommand = new AsyncRelayCommand(RefreshOrdersAsync);
        FilterStatusCommand = new AsyncRelayCommand(async p =>
        {
            if (p is string s && int.TryParse(s, out int st)) _statusFilter = st;
            else _statusFilter = null;
            await RefreshOrdersAsync();
        });
        GenerateFromPrescriptionCommand = new AsyncRelayCommand(GenerateFromPrescriptionAsync);
        CompoundCheckCommand = new AsyncRelayCommand(ExecuteCompoundCheckAsync);
        PunctureCommand = new AsyncRelayCommand(ExecutePunctureAsync);
        InspectCommand = new AsyncRelayCommand(ExecuteInspectAsync);
        ReplaceBottleCommand = new AsyncRelayCommand(ExecuteReplaceBottleAsync);
        CompleteInfusionCommand = new AsyncRelayCommand(ExecuteCompleteInfusionAsync);
        SelectSeatCommand = new RelayCommand(p =>
        {
            if (p is not InfusionSeatTile tile) return;
            SeatNoInput = tile.SeatNo;
            if (!string.IsNullOrWhiteSpace(tile.OrderId))
            {
                var found = Orders.FirstOrDefault(o => o.Id == tile.OrderId);
                if (found != null) SelectedOrder = found;
            }
        });
        PickBottleCommand = new RelayCommand(p =>
        {
            if (p is not InfusionBottleVm b) return;
            if (SelectedOrder?.Status == 1) BottleBarcodeInput = b.BottleBarcode;
            else NextBottleBarcodeInput = b.BottleBarcode;
        });

        RebuildSeatMap();
    }

    public ObservableCollection<InfusionOrderVm> Orders { get; } = new();
    public ObservableCollection<InfusionSeatTile> SeatMap { get; } = new();
    public ObservableCollection<InfusionBottleVm> SelectedBottles { get; } = new();
    public ObservableCollection<InfusionInspectionVm> Inspections { get; } = new();

    public string SearchKeyword { get => _searchKeyword; set => SetField(ref _searchKeyword, value ?? string.Empty); }
    public string PrescriptionIdInput { get => _prescriptionIdInput; set => SetField(ref _prescriptionIdInput, value ?? string.Empty); }
    public string CompoundNurseInput { get => _compoundNurseInput; set => SetField(ref _compoundNurseInput, value ?? string.Empty); }
    public string CompoundCheckNurseInput { get => _compoundCheckNurseInput; set => SetField(ref _compoundCheckNurseInput, value ?? string.Empty); }
    public string PatientWristbandInput { get => _patientWristbandInput; set => SetField(ref _patientWristbandInput, value ?? string.Empty); }
    public string BottleBarcodeInput { get => _bottleBarcodeInput; set => SetField(ref _bottleBarcodeInput, value ?? string.Empty); }
    public string SeatNoInput { get => _seatNoInput; set => SetField(ref _seatNoInput, value ?? string.Empty); }
    public int DripRateInput { get => _dripRateInput; set => SetField(ref _dripRateInput, value); }
    public int InspectDripSpeed { get => _inspectDripSpeed; set => SetField(ref _inspectDripSpeed, value); }
    public bool InspectIsSwollen { get => _inspectIsSwollen; set => SetField(ref _inspectIsSwollen, value); }
    public bool InspectHasAdverse { get => _inspectHasAdverse; set => SetField(ref _inspectHasAdverse, value); }
    public string InspectNotes { get => _inspectNotes; set => SetField(ref _inspectNotes, value ?? string.Empty); }
    public string NextBottleBarcodeInput { get => _nextBottleBarcodeInput; set => SetField(ref _nextBottleBarcodeInput, value ?? string.Empty); }
    public bool IsBusy { get => _isBusy; set => SetField(ref _isBusy, value); }
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value ?? string.Empty); }

    public InfusionOrderVm? SelectedOrder
    {
        get => _selectedOrder;
        set
        {
            if (SetField(ref _selectedOrder, value))
            {
                OnPropertyChanged(nameof(HasSelectedOrder));
                OnSelectedOrderChanged(value);
            }
        }
    }

    public bool HasSelectedOrder => SelectedOrder != null;
    public int PendingCompoundCount => Orders.Count(o => o.Status == 0);
    public int InfusingCount => Orders.Count(o => o.Status is 2 or 3);
    public int OccupiedSeatCount => SeatMap.Count(s => s.IsOccupied);

    public ICommand RefreshCommand { get; }
    public ICommand FilterStatusCommand { get; }
    public ICommand GenerateFromPrescriptionCommand { get; }
    public ICommand CompoundCheckCommand { get; }
    public ICommand PunctureCommand { get; }
    public ICommand InspectCommand { get; }
    public ICommand ReplaceBottleCommand { get; }
    public ICommand CompleteInfusionCommand { get; }
    public ICommand SelectSeatCommand { get; }
    public ICommand PickBottleCommand { get; }

    public void Activate() => _ = LoadAsync();
    public void Deactivate() { }
    public void Dispose() { }
    public Task LoadAsync() => RefreshOrdersAsync();

    private void OnSelectedOrderChanged(InfusionOrderVm? order)
    {
        SelectedBottles.Clear();
        Inspections.Clear();
        if (order == null) return;

        foreach (var b in order.Bottles.OrderBy(x => x.SequenceNo))
        {
            SelectedBottles.Add(b);
        }

        PatientWristbandInput = order.PatientId;
        var firstPending = order.Bottles.FirstOrDefault(b => b.Status == 0);
        if (order.Status == 1 && firstPending != null)
        {
            BottleBarcodeInput = firstPending.BottleBarcode;
        }
        else if (order.Status is 2 or 3 && firstPending != null)
        {
            NextBottleBarcodeInput = firstPending.BottleBarcode;
        }
        if (!string.IsNullOrWhiteSpace(order.SeatNo))
        {
            SeatNoInput = order.SeatNo!;
        }
        _ = LoadInspectionsAsync(order.Id);
    }

    private void RebuildSeatMap()
    {
        SeatMap.Clear();
        for (int i = 1; i <= 12; i++)
        {
            string seatCode = $"INF-{i:D2}";
            var occupant = Orders.FirstOrDefault(o =>
                o.Status is 2 or 3 && string.Equals(o.SeatNo, seatCode, StringComparison.OrdinalIgnoreCase));
            SeatMap.Add(new InfusionSeatTile
            {
                SeatNo = seatCode,
                IsOccupied = occupant != null,
                OrderId = occupant?.Id,
                PatientName = occupant?.PatientName ?? "空座",
                DetailText = occupant == null
                    ? "可分配"
                    : $"{occupant.PatientName} · {occupant.CompletedBottles}/{occupant.TotalBottles}瓶 · {occupant.DripRate}滴/分"
            });
        }
        OnPropertyChanged(nameof(OccupiedSeatCount));
    }

    public async Task RefreshOrdersAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var queryParts = new List<string>();
            if (_statusFilter.HasValue) queryParts.Add($"status={_statusFilter.Value}");
            if (!string.IsNullOrWhiteSpace(SearchKeyword)) queryParts.Add($"keyword={Uri.EscapeDataString(SearchKeyword.Trim())}");
            string url = "/api/v4/opd/infusion/orders" + (queryParts.Count > 0 ? "?" + string.Join("&", queryParts) : "");

            using var resp = await http.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
            {
                StatusMessage = $"查询输液队列失败 ({(int)resp.StatusCode})";
                return;
            }

            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<List<InfusionOrderVm>>>(JsonOpts);
            if (env?.Code is not (200 or 0))
            {
                StatusMessage = env?.Message ?? "加载输液列表失败";
                return;
            }

            string? prevId = SelectedOrder?.Id;
            Orders.Clear();
            if (env.Data != null)
            {
                foreach (var item in env.Data)
                {
                    Orders.Add(item);
                }
            }

            RebuildSeatMap();
            OnPropertyChanged(nameof(PendingCompoundCount));
            OnPropertyChanged(nameof(InfusingCount));

            SelectedOrder = (!string.IsNullOrWhiteSpace(prevId) ? Orders.FirstOrDefault(x => x.Id == prevId) : null)
                ?? Orders.FirstOrDefault();

            StatusMessage = Orders.Count > 0
                ? $"已同步 {Orders.Count} 张门诊输液执行单（在座输液 {OccupiedSeatCount} 人）"
                : "当前暂无符合条件的已缴费门诊输液单（患者缴费开立静滴处方后将自动生成）";
        }
        catch (Exception ex)
        {
            StatusMessage = "连接输液服务失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadInspectionsAsync(string orderId)
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            using var resp = await http.GetAsync($"/api/v4/opd/infusion/orders/{Uri.EscapeDataString(orderId)}/inspections");
            if (!resp.IsSuccessStatusCode) return;
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<List<InfusionInspectionVm>>>(JsonOpts);
            Inspections.Clear();
            if (env?.Data != null)
            {
                foreach (var it in env.Data) Inspections.Add(it);
            }
        }
        catch { }
    }

    private async Task GenerateFromPrescriptionAsync()
    {
        string rxId = PrescriptionIdInput.Trim();
        if (string.IsNullOrWhiteSpace(rxId))
        {
            _shell?.ShowToast("请输入已收费处方 ID (可从收费列表复制)", "Warning");
            return;
        }

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            using var resp = await http.PostAsync($"/api/v4/opd/infusion/from-prescription/{Uri.EscapeDataString(rxId)}", null);
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<InfusionOrderVm>>(JsonOpts);
            if (!resp.IsSuccessStatusCode || env?.Code is not (200 or 0) || env.Data == null)
            {
                _shell?.ShowToast(env?.Message ?? "生成输液单失败", "Error");
                return;
            }
            PrescriptionIdInput = string.Empty;
            _shell?.ShowToast(env.Message ?? "输液单生成成功", "Success");
            await RefreshOrdersAsync();
            SelectedOrder = Orders.FirstOrDefault(x => x.Id == env.Data.Id) ?? SelectedOrder;
        }
        catch (Exception ex)
        {
            _shell?.ShowToast("生成输液单失败：" + ex.Message, "Error");
        }
    }

    private async Task ExecuteCompoundCheckAsync()
    {
        if (SelectedOrder == null) { _shell?.ShowToast("请先选择输液单", "Warning"); return; }
        await PostActionAsync("/api/v4/opd/infusion/compound-check", new
        {
            InfusionOrderId = SelectedOrder.Id,
            CompoundNurseId = CompoundNurseInput.Trim(),
            CompoundCheckNurseId = CompoundCheckNurseInput.Trim()
        }, "配药双人核对");
    }

    private async Task ExecutePunctureAsync()
    {
        if (SelectedOrder == null) { _shell?.ShowToast("请先选择输液单", "Warning"); return; }
        await PostActionAsync("/api/v4/opd/infusion/puncture", new
        {
            InfusionOrderId = SelectedOrder.Id,
            BottleBarcode = BottleBarcodeInput.Trim(),
            PatientWristbandOrId = PatientWristbandInput.Trim(),
            SeatNo = SeatNoInput.Trim(),
            DripRate = DripRateInput
        }, "穿刺上座");
    }

    private async Task ExecuteInspectAsync()
    {
        if (SelectedOrder == null) { _shell?.ShowToast("请先选择输液单", "Warning"); return; }
        var activeBottle = SelectedBottles.FirstOrDefault(b => b.Status == 1) ?? SelectedBottles.FirstOrDefault();
        await PostActionAsync("/api/v4/opd/infusion/inspect", new
        {
            InfusionOrderId = SelectedOrder.Id,
            BottleId = activeBottle?.Id,
            DripSpeed = InspectDripSpeed,
            IsSwollen = InspectIsSwollen,
            HasAdverseReaction = InspectHasAdverse,
            Notes = InspectNotes.Trim()
        }, "输液巡视");
        if (SelectedOrder != null) await LoadInspectionsAsync(SelectedOrder.Id);
    }

    private async Task ExecuteReplaceBottleAsync()
    {
        if (SelectedOrder == null) { _shell?.ShowToast("请先选择输液单", "Warning"); return; }
        await PostActionAsync("/api/v4/opd/infusion/replace-bottle", new
        {
            InfusionOrderId = SelectedOrder.Id,
            NextBottleBarcode = NextBottleBarcodeInput.Trim(),
            PatientWristbandOrId = PatientWristbandInput.Trim()
        }, "扫码换瓶");
    }

    private async Task ExecuteCompleteInfusionAsync()
    {
        if (SelectedOrder == null) { _shell?.ShowToast("请先选择输液单", "Warning"); return; }
        await PostActionAsync("/api/v4/opd/infusion/complete", new
        {
            InfusionOrderId = SelectedOrder.Id,
            Notes = "输液完毕拔针，患者无不适离室"
        }, "拔针完结");
    }

    private async Task PostActionAsync(string url, object payload, string actionLabel)
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            using var resp = await http.PostAsJsonAsync(url, payload, JsonOpts);
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<bool>>(JsonOpts);
            if (!resp.IsSuccessStatusCode || env?.Code is not (200 or 0) || !env.Data)
            {
                string err = env?.Message ?? $"{actionLabel}被服务端拦截 ({(int)resp.StatusCode})";
                StatusMessage = "❌ " + err;
                _shell?.ShowToast(err, "Error");
                return;
            }
            string okMsg = env.Message ?? $"{actionLabel}成功";
            StatusMessage = "✓ " + okMsg;
            _shell?.ShowToast(okMsg, "Success");
            await RefreshOrdersAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ {actionLabel}异常：{ex.Message}";
            _shell?.ShowToast(StatusMessage, "Error");
        }
    }
}
