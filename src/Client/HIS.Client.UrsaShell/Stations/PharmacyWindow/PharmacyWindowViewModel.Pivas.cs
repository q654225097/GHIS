using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Services;
using HIS.Client.UrsaShell.Shell;

namespace HIS.Client.UrsaShell.Stations.PharmacyWindow;

public sealed class WindowPivasOrderVm
{
    public string Id { get; set; } = string.Empty;
    public string PivasNo { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string WardName { get; set; } = string.Empty;
    public string DrugName { get; set; } = string.Empty;
    public string SolventName { get; set; } = string.Empty;
    public string DosageText { get; set; } = string.Empty;
    public string BatchCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ReviewerName { get; set; }
    public string? CompounderName { get; set; }
    public string? CheckerName { get; set; }

    public string StageLabel => Status switch
    {
        "PENDING_REVIEW" => "待审方",
        "REVIEWED" => "待贴签",
        "LABELED" => "待调配",
        "COMPOUNDED" => "待异人核对",
        "CHECKED" => "待出仓配送",
        "DISPATCHED" => "已出仓签收",
        "REJECTED" => "已打回",
        _ => Status
    };

    public string NextActionTitle => Status switch
    {
        "PENDING_REVIEW" => "1. 药师审方通过",
        "REVIEWED" => "2. 打印静配贴签",
        "LABELED" => "3. 进仓冲配执行",
        "COMPOUNDED" => "4. 异人双签核对",
        "CHECKED" => "5. 成品出仓配送",
        _ => "流程已终结"
    };

    public string SummaryLine => $"{PivasNo} · {PatientName}({WardName}) · {DrugName} + {SolventName} ({DosageText}) · [{BatchCode}] · {StageLabel}";
    public string ActorLine => $"审方:{ReviewerName ?? "-"} | 调配:{CompounderName ?? "-"} | 核对:{CheckerName ?? "-"}";
}

public sealed partial class PharmacyWindowViewModel
{
    private static readonly JsonSerializerOptions PivasJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private WindowPivasOrderVm? _selectedPivasOrder;
    private string _pivasDoubleCheckerName = string.Empty;
    private string _pivasRejectReason = string.Empty;
    private string _pivasOrdersStatusText = "PIVAS 闭环：审方 -> 贴签 -> 调配 -> 异人双签核对 -> 出仓配送";

    private ICommand? _loadPivasOrdersCommand;
    private ICommand? _advancePivasOrderCommand;
    private ICommand? _rejectPivasOrderCommand;

    public ObservableCollection<WindowPivasOrderVm> PivasOrders { get; } = new();

    public WindowPivasOrderVm? SelectedPivasOrder
    {
        get => _selectedPivasOrder;
        set
        {
            if (SetField(ref _selectedPivasOrder, value))
            {
                OnPropertyChanged(nameof(SelectedPivasNextActionText));
            }
        }
    }

    public string PivasDoubleCheckerName
    {
        get => _pivasDoubleCheckerName;
        set => SetField(ref _pivasDoubleCheckerName, value);
    }

    public string PivasRejectReason
    {
        get => _pivasRejectReason;
        set => SetField(ref _pivasRejectReason, value);
    }

    public string PivasOrdersStatusText
    {
        get => _pivasOrdersStatusText;
        set => SetField(ref _pivasOrdersStatusText, value);
    }

    public string SelectedPivasNextActionText => SelectedPivasOrder?.NextActionTitle ?? "推进下一环节";

    public ICommand LoadPivasOrdersCommand => _loadPivasOrdersCommand ??= new AsyncRelayCommand(LoadPivasOrdersAsync);
    public ICommand AdvancePivasOrderCommand => _advancePivasOrderCommand ??= new AsyncRelayCommand(AdvancePivasOrderAsync);
    public ICommand RejectPivasOrderCommand => _rejectPivasOrderCommand ??= new AsyncRelayCommand(RejectPivasOrderAsync);

    public async Task LoadPivasOrdersAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var list = await http.GetFromJsonAsync<List<JsonElement>>("/api/v4/pivas-abx/pivas/orders", PivasJsonOptions);
            PivasOrders.Clear();
            if (list != null)
            {
                foreach (var el in list)
                {
                    PivasOrders.Add(new WindowPivasOrderVm
                    {
                        Id = ReadStr(el, "id"),
                        PivasNo = ReadStr(el, "pivas_no"),
                        OrderId = ReadStr(el, "order_id"),
                        EncounterId = ReadStr(el, "encounter_id"),
                        PatientId = ReadStr(el, "patient_id"),
                        PatientName = ReadStr(el, "patient_name"),
                        WardName = ReadStr(el, "ward_name"),
                        DrugName = ReadStr(el, "drug_name"),
                        SolventName = ReadStr(el, "solvent_name"),
                        DosageText = ReadStr(el, "dosage_text"),
                        BatchCode = ReadStr(el, "batch_code"),
                        Status = ReadStr(el, "status"),
                        ReviewerName = ReadNullableStr(el, "reviewer_name"),
                        CompounderName = ReadNullableStr(el, "compounder_name"),
                        CheckerName = ReadNullableStr(el, "checker_name")
                    });
                }
            }
            SelectedPivasOrder ??= PivasOrders.Count > 0 ? PivasOrders[0] : null;
            PivasOrdersStatusText = $"已同步 PIVAS 静配工单 {PivasOrders.Count} 条";
        }
        catch (Exception ex)
        {
            PivasOrdersStatusText = $"加载 PIVAS 工单异常: {ex.Message}";
        }
    }

    public async Task AdvancePivasOrderAsync()
    {
        var order = SelectedPivasOrder;
        if (order == null)
        {
            PivasOrdersStatusText = "请先选择需要推进的 PIVAS 工单";
            return;
        }

        string currentOperator = SessionContext.Current.DisplayName
            ?? SessionContext.Current.UserId
            ?? "当前药师";

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            System.Net.Http.HttpResponseMessage resp;
            switch (order.Status)
            {
                case "PENDING_REVIEW":
                    resp = await http.PostAsJsonAsync($"/api/v4/pivas-abx/pivas/orders/{Uri.EscapeDataString(order.Id)}/review", new
                    {
                        Approved = true,
                        BatchCode = string.IsNullOrWhiteSpace(PivasBatchNo) ? order.BatchCode : PivasBatchNo.Trim(),
                        RejectReason = (string?)null,
                        ReviewerName = currentOperator
                    });
                    break;
                case "REVIEWED":
                    resp = await http.PostAsync($"/api/v4/pivas-abx/pivas/orders/{Uri.EscapeDataString(order.Id)}/label", null);
                    break;
                case "LABELED":
                    resp = await http.PostAsJsonAsync($"/api/v4/pivas-abx/pivas/orders/{Uri.EscapeDataString(order.Id)}/compound", new
                    {
                        CompounderName = currentOperator
                    });
                    break;
                case "COMPOUNDED":
                    if (string.IsNullOrWhiteSpace(PivasDoubleCheckerName))
                    {
                        PivasOrdersStatusText = "出仓复核必须填写异人核对药师姓名（不可与调配人相同）";
                        _shell?.ShowToast(PivasOrdersStatusText, "Warning");
                        return;
                    }
                    resp = await http.PostAsJsonAsync($"/api/v4/pivas-abx/pivas/orders/{Uri.EscapeDataString(order.Id)}/check", new
                    {
                        CheckerName = PivasDoubleCheckerName.Trim()
                    });
                    break;
                case "CHECKED":
                    resp = await http.PostAsync($"/api/v4/pivas-abx/pivas/orders/{Uri.EscapeDataString(order.Id)}/dispatch", null);
                    break;
                default:
                    PivasOrdersStatusText = $"工单 {order.PivasNo} 当前状态 {order.StageLabel} 无需推进";
                    return;
            }

            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                PivasOrdersStatusText = $"推进失败: {body}";
                _shell?.ShowToast(PivasOrdersStatusText, "Warning");
                return;
            }

            PivasOrdersStatusText = $"✓ PIVAS 工单 {order.PivasNo} 已推进至下一环节（操作人: {currentOperator}）";
            _shell?.ShowToast(PivasOrdersStatusText, "Success");
            string selectedId = order.Id;
            await LoadPivasOrdersAsync();
            foreach (var item in PivasOrders)
            {
                if (item.Id == selectedId)
                {
                    SelectedPivasOrder = item;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            PivasOrdersStatusText = $"推进异常: {ex.Message}";
        }
    }

    public async Task RejectPivasOrderAsync()
    {
        var order = SelectedPivasOrder;
        if (order == null || order.Status != "PENDING_REVIEW")
        {
            PivasOrdersStatusText = "仅待审方(PENDING_REVIEW)的静配工单可打回";
            return;
        }
        if (string.IsNullOrWhiteSpace(PivasRejectReason))
        {
            PivasOrdersStatusText = "打回静配医嘱时必须填写配伍禁忌或不合理原因";
            _shell?.ShowToast(PivasOrdersStatusText, "Warning");
            return;
        }

        string currentOperator = SessionContext.Current.DisplayName ?? SessionContext.Current.UserId ?? "审方药师";
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var resp = await http.PostAsJsonAsync($"/api/v4/pivas-abx/pivas/orders/{Uri.EscapeDataString(order.Id)}/review", new
            {
                Approved = false,
                BatchCode = order.BatchCode,
                RejectReason = PivasRejectReason.Trim(),
                ReviewerName = currentOperator
            });
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                PivasOrdersStatusText = $"打回失败: {body}";
                return;
            }
            PivasOrdersStatusText = $"已打回静配工单 {order.PivasNo}：{PivasRejectReason.Trim()}";
            _shell?.ShowToast(PivasOrdersStatusText, "Info");
            await LoadPivasOrdersAsync();
        }
        catch (Exception ex)
        {
            PivasOrdersStatusText = $"打回异常: {ex.Message}";
        }
    }

    private static string ReadStr(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : string.Empty;

    private static string? ReadNullableStr(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : null;
}
