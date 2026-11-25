using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
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

namespace HIS.Client.UrsaShell.Stations.PharmacyWarehouse;

public sealed class ForecastDrugItemVm
{
    public string DrugId { get; set; } = string.Empty;
    public string DrugCode { get; set; } = string.Empty;
    public string DrugName { get; set; } = string.Empty;
    public string Spec { get; set; } = string.Empty;
    public string UnitName { get; set; } = string.Empty;
    public int CurrentValidStock { get; set; }
    public int OutboundLast30Days { get; set; }
    public int OutboundLast90Days { get; set; }
    public decimal WeightedDailyUsage { get; set; }
    public int DaysOutOfStock { get; set; }
    public int SuggestedOrder30d { get; set; }
    public int SuggestedOrder60d { get; set; }
    public int SuggestedOrder90d { get; set; }
    public decimal EstimatedCost30d { get; set; }
    public string RiskLevel { get; set; } = string.Empty;

    public string RiskDisplay => RiskLevel switch
    {
        "CRITICAL_OUT_OF_STOCK" => "断货告警",
        "HIGH_SHORTAGE_RISK" => "高短缺风险",
        "MEDIUM_REORDER" => "建议补货",
        _ => "库存健康"
    };

    public string StockAndDaysText => $"现存 {CurrentValidStock} {UnitName} · 可用 {DaysOutOfStock} 天";
    public string DailyUsageText => $"加权日均 {WeightedDailyUsage:0.##} · 30/90日 {OutboundLast30Days}/{OutboundLast90Days}";
    public string SuggestionText => $"建议 30/60/90天: {SuggestedOrder30d} / {SuggestedOrder60d} / {SuggestedOrder90d} (¥{EstimatedCost30d:0.00})";
}

public sealed class SupplierItemVm
{
    public string Id { get; set; } = string.Empty;
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string LicenseNo { get; set; } = string.Empty;
    public int LicenseExpiryInt { get; set; }
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public int IsColdChainQualified { get; set; }
    public int IsActive { get; set; }

    public string DisplayName => $"[{SupplierCode}] {SupplierName} (证 {LicenseNo})"
        + (IsColdChainQualified == 1 ? " · 冷链合格" : "");
}

public sealed class PurchaseOrderLineVm
{
    public string Id { get; set; } = string.Empty;
    public string PoId { get; set; } = string.Empty;
    public string DrugId { get; set; } = string.Empty;
    public string DrugCode { get; set; } = string.Empty;
    public string DrugName { get; set; } = string.Empty;
    public string? Spec { get; set; }
    public int RequestedQty { get; set; }
    public int ReceivedQty { get; set; }
    public decimal UnitCostPrice { get; set; }
}

public sealed class PurchaseOrderItemVm
{
    public string Id { get; set; } = string.Empty;
    public string PoNo { get; set; } = string.Empty;
    public string WarehouseId { get; set; } = string.Empty;
    public string SupplierId { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal TotalEstimatedAmount { get; set; }
    public string? ApprovedBy { get; set; }
    public string? Remark { get; set; }
    public long CreatedAtMs { get; set; }
    public List<PurchaseOrderLineVm> Lines { get; set; } = new();

    public string PrimaryDrugSummary => Lines.Count > 0
        ? $"{Lines[0].DrugName} ({Lines[0].RequestedQty} / 已收 {Lines[0].ReceivedQty})"
        : "-";

    public string CreatedAtText => CreatedAtMs > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtMs).LocalDateTime.ToString("yyyy-MM-dd HH:mm")
        : "-";

    public string SummaryLine => $"{PoNo} | {SupplierName} | {PrimaryDrugSummary} | ¥{TotalEstimatedAmount:0.00} | {Status}";
}

public sealed class GspAcceptanceItemVm
{
    public string Id { get; set; } = string.Empty;
    public string AcceptanceNo { get; set; } = string.Empty;
    public string? PoId { get; set; }
    public string BatchId { get; set; } = string.Empty;
    public string WarehouseId { get; set; } = string.Empty;
    public string DrugId { get; set; } = string.Empty;
    public string BatchNo { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal? ColdChainTemp { get; set; }
    public string ReceiverId { get; set; } = string.Empty;
    public string CheckerId { get; set; } = string.Empty;
    public long AcceptedAtMs { get; set; }
    public string Conclusion { get; set; } = string.Empty;
    public string? Remark { get; set; }

    public string TimeText => AcceptedAtMs > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(AcceptedAtMs).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")
        : "-";

    public string SummaryLine => $"{AcceptanceNo} | 批号 {BatchNo} ×{Quantity} | 验收:{ReceiverId} / 复核:{CheckerId} | {Conclusion} | {TimeText}";
}

public sealed partial class PharmacyWarehouseViewModel
{
    private static readonly JsonSerializerOptions GspJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private ForecastDrugItemVm? _selectedForecastRow;
    private SupplierItemVm? _selectedSupplier;
    private PurchaseOrderItemVm? _selectedPurchaseOrder;
    private DrugRow? _selectedGspDrug;

    private string _forecastSafetyDays = "14";
    private string _poQuantity = "30";
    private bool _poAutoApprove = true;
    private string _poRemark = "药库常规补货采购计划";

    private string _gspBatchNo = string.Empty;
    private string _gspExpiryDate = DateTime.Today.AddYears(2).ToString("yyyy-MM-dd");
    private string _gspQuantity = "30";
    private string _gspReceiverId = string.Empty;
    private string _gspCheckerId = string.Empty;
    private bool _gspAppearanceOk = true;
    private bool _gspPackageIntact = true;
    private bool _gspLabelConsistent = true;
    private string _gspColdChainTemp = string.Empty;
    private string _gspMaxColdChainTemp = "8";
    private string _gspNearExpiryDays = "30";
    private string _gspStatusMessage = "支持 30/60/90 天智能补货测算、采购审批、GSP双人到货验收及效期自动冻结巡检。";

    private ICommand? _loadForecastCommand;
    private ICommand? _generateReplenishTransferCommand;
    private ICommand? _loadGspWorkspaceCommand;
    private ICommand? _createPurchaseOrderCommand;
    private ICommand? _approveSelectedPoCommand;
    private ICommand? _submitGspAcceptanceCommand;
    private ICommand? _autoFreezeExpiredCommand;

    public ObservableCollection<ForecastDrugItemVm> ForecastRows { get; } = new();
    public ObservableCollection<SupplierItemVm> Suppliers { get; } = new();
    public ObservableCollection<PurchaseOrderItemVm> PurchaseOrders { get; } = new();
    public ObservableCollection<GspAcceptanceItemVm> GspAcceptances { get; } = new();

    public ForecastDrugItemVm? SelectedForecastRow
    {
        get => _selectedForecastRow;
        set
        {
            if (SetField(ref _selectedForecastRow, value) && value != null)
            {
                var matched = Pharmacy.Drugs.FirstOrDefault(d => d.Id == value.DrugId);
                if (matched != null)
                {
                    SelectedGspDrug = matched;
                }
                if (value.SuggestedOrder30d > 0)
                {
                    PoQuantity = value.SuggestedOrder30d.ToString(CultureInfo.InvariantCulture);
                    GspQuantity = value.SuggestedOrder30d.ToString(CultureInfo.InvariantCulture);
                }
            }
        }
    }

    public SupplierItemVm? SelectedSupplier
    {
        get => _selectedSupplier;
        set => SetField(ref _selectedSupplier, value);
    }

    public PurchaseOrderItemVm? SelectedPurchaseOrder
    {
        get => _selectedPurchaseOrder;
        set
        {
            if (SetField(ref _selectedPurchaseOrder, value) && value != null)
            {
                var firstLine = value.Lines.FirstOrDefault();
                if (firstLine != null)
                {
                    var matched = Pharmacy.Drugs.FirstOrDefault(d => d.Id == firstLine.DrugId);
                    if (matched != null)
                    {
                        SelectedGspDrug = matched;
                    }
                    int remaining = Math.Max(1, firstLine.RequestedQty - firstLine.ReceivedQty);
                    GspQuantity = remaining.ToString(CultureInfo.InvariantCulture);
                }
            }
        }
    }

    public DrugRow? SelectedGspDrug
    {
        get => _selectedGspDrug;
        set => SetField(ref _selectedGspDrug, value);
    }

    public string ForecastSafetyDays { get => _forecastSafetyDays; set => SetField(ref _forecastSafetyDays, value); }
    public string PoQuantity { get => _poQuantity; set => SetField(ref _poQuantity, value); }
    public bool PoAutoApprove { get => _poAutoApprove; set => SetField(ref _poAutoApprove, value); }
    public string PoRemark { get => _poRemark; set => SetField(ref _poRemark, value); }

    public string GspBatchNo { get => _gspBatchNo; set => SetField(ref _gspBatchNo, value); }
    public string GspExpiryDate { get => _gspExpiryDate; set => SetField(ref _gspExpiryDate, value); }
    public string GspQuantity { get => _gspQuantity; set => SetField(ref _gspQuantity, value); }
    public string GspReceiverId { get => _gspReceiverId; set => SetField(ref _gspReceiverId, value); }
    public string GspCheckerId { get => _gspCheckerId; set => SetField(ref _gspCheckerId, value); }
    public bool GspAppearanceOk { get => _gspAppearanceOk; set => SetField(ref _gspAppearanceOk, value); }
    public bool GspPackageIntact { get => _gspPackageIntact; set => SetField(ref _gspPackageIntact, value); }
    public bool GspLabelConsistent { get => _gspLabelConsistent; set => SetField(ref _gspLabelConsistent, value); }
    public string GspColdChainTemp { get => _gspColdChainTemp; set => SetField(ref _gspColdChainTemp, value); }
    public string GspMaxColdChainTemp { get => _gspMaxColdChainTemp; set => SetField(ref _gspMaxColdChainTemp, value); }
    public string GspNearExpiryDays { get => _gspNearExpiryDays; set => SetField(ref _gspNearExpiryDays, value); }
    public string GspStatusMessage { get => _gspStatusMessage; set => SetField(ref _gspStatusMessage, value); }

    public ICommand LoadForecastCommand => _loadForecastCommand ??= new AsyncRelayCommand(LoadForecastAsync);
    public ICommand GenerateReplenishTransferCommand => _generateReplenishTransferCommand ??= new AsyncRelayCommand(GenerateReplenishTransferAsync);
    public ICommand LoadGspWorkspaceCommand => _loadGspWorkspaceCommand ??= new AsyncRelayCommand(LoadGspWorkspaceAsync);
    public ICommand CreatePurchaseOrderCommand => _createPurchaseOrderCommand ??= new AsyncRelayCommand(CreatePurchaseOrderAsync);
    public ICommand ApproveSelectedPoCommand => _approveSelectedPoCommand ??= new AsyncRelayCommand(ApproveSelectedPoAsync);
    public ICommand SubmitGspAcceptanceCommand => _submitGspAcceptanceCommand ??= new AsyncRelayCommand(SubmitGspAcceptanceAsync);
    public ICommand AutoFreezeExpiredCommand => _autoFreezeExpiredCommand ??= new AsyncRelayCommand(AutoFreezeExpiredAsync);

    public async Task LoadForecastAndGspAsync()
    {
        if (string.IsNullOrWhiteSpace(GspReceiverId))
        {
            GspReceiverId = SessionContext.Current.UserId ?? "PHARM-01";
        }
        if (string.IsNullOrWhiteSpace(GspBatchNo))
        {
            GspBatchNo = $"GSP-{DateTime.Now:yyyyMMdd}-01";
        }
        SelectedGspDrug ??= Pharmacy.Drugs.FirstOrDefault();
        await Task.WhenAll(LoadForecastAsync(), LoadGspWorkspaceAsync());
    }

    public async Task LoadForecastAsync()
    {
        try
        {
            int safetyDays = int.TryParse(ForecastSafetyDays, out int sd) ? Math.Clamp(sd, 3, 90) : 14;
            string? whId = Pharmacy.SelectedWarehouse?.Id;
            string url = $"/api/v4/inventory/forecast/replenishment?safetyDays={safetyDays}";
            if (!string.IsNullOrWhiteSpace(whId))
            {
                url += $"&warehouseId={Uri.EscapeDataString(whId)}";
            }

            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.GetFromJsonAsync<ApiResponse<List<ForecastDrugItemVm>>>(url, GspJsonOptions);
            if (res == null || res.Code is not (200 or 0) || res.Data == null)
            {
                GspStatusMessage = $"加载智能补货预测失败: {res?.Message ?? "服务无响应"}";
                return;
            }

            ForecastRows.Clear();
            foreach (var row in res.Data)
            {
                ForecastRows.Add(row);
            }
            int shortageCount = ForecastRows.Count(x => x.RiskLevel is "CRITICAL_OUT_OF_STOCK" or "HIGH_SHORTAGE_RISK");
            GspStatusMessage = $"智能补货测算已更新 {ForecastRows.Count} 个药品，其中高风险/断货 {shortageCount} 项。";
        }
        catch (Exception ex)
        {
            GspStatusMessage = $"加载智能补货预测异常: {ex.Message}";
        }
    }

    public async Task GenerateReplenishTransferAsync()
    {
        var sourceId = Pharmacy.SelectedWarehouse?.Id ?? Pharmacy.WarehouseChoices.FirstOrDefault()?.Id;
        var targetId = Pharmacy.TargetPharmacy?.Id;
        if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(targetId))
        {
            GspStatusMessage = "请先在「出库与调拨」页选定源药库与目标药房/库房";
            _shell?.ShowToast(GspStatusMessage, "Warning");
            return;
        }

        try
        {
            int safetyDays = int.TryParse(ForecastSafetyDays, out int sd) ? Math.Clamp(sd, 3, 90) : 14;
            using var http = AuthenticatedHttpClientFactory.Create();
            var resp = await http.PostAsJsonAsync("/api/v4/inventory/forecast/generate-transfer", new
            {
                SourceWarehouseId = sourceId,
                TargetWarehouseId = targetId,
                HorizonDays = 30,
                SafetyDays = safetyDays
            });
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                GspStatusMessage = $"生成调拨建议失败: {body}";
                _shell?.ShowToast(GspStatusMessage, "Warning");
                return;
            }

            GspStatusMessage = $"已根据30天预测生成调拨补货单（源库 {sourceId} -> 目标 {targetId}）。";
            _shell?.ShowToast(GspStatusMessage, "Success");
            await Pharmacy.RefreshActiveTabAsync();
        }
        catch (Exception ex)
        {
            GspStatusMessage = $"生成调拨建议异常: {ex.Message}";
        }
    }

    public async Task LoadGspWorkspaceAsync()
    {
        try
        {
            string? whId = Pharmacy.SelectedWarehouse?.Id;
            string poUrl = string.IsNullOrWhiteSpace(whId)
                ? "/api/v4/inventory/purchase-orders"
                : $"/api/v4/inventory/purchase-orders?warehouseId={Uri.EscapeDataString(whId)}";
            string gspUrl = string.IsNullOrWhiteSpace(whId)
                ? "/api/v4/inventory/gsp-acceptances"
                : $"/api/v4/inventory/gsp-acceptances?warehouseId={Uri.EscapeDataString(whId)}";

            using var http = AuthenticatedHttpClientFactory.Create();
            var supTask = http.GetFromJsonAsync<ApiResponse<List<SupplierItemVm>>>("/api/v4/inventory/suppliers", GspJsonOptions);
            var poTask = http.GetFromJsonAsync<ApiResponse<List<PurchaseOrderItemVm>>>(poUrl, GspJsonOptions);
            var accTask = http.GetFromJsonAsync<ApiResponse<List<GspAcceptanceItemVm>>>(gspUrl, GspJsonOptions);
            await Task.WhenAll(supTask, poTask, accTask);

            var supRes = await supTask;
            if (supRes?.Code is 200 or 0 && supRes.Data != null)
            {
                Suppliers.Clear();
                foreach (var s in supRes.Data) Suppliers.Add(s);
                SelectedSupplier ??= Suppliers.FirstOrDefault();
            }

            var poRes = await poTask;
            if (poRes?.Code is 200 or 0 && poRes.Data != null)
            {
                PurchaseOrders.Clear();
                foreach (var po in poRes.Data) PurchaseOrders.Add(po);
            }

            var accRes = await accTask;
            if (accRes?.Code is 200 or 0 && accRes.Data != null)
            {
                GspAcceptances.Clear();
                foreach (var a in accRes.Data) GspAcceptances.Add(a);
            }
        }
        catch (Exception ex)
        {
            GspStatusMessage = $"加载采购与GSP台账异常: {ex.Message}";
        }
    }

    public async Task CreatePurchaseOrderAsync()
    {
        string? whId = Pharmacy.SelectedWarehouse?.Id ?? Pharmacy.WarehouseChoices.FirstOrDefault()?.Id;
        var sup = SelectedSupplier ?? Suppliers.FirstOrDefault();
        var drug = SelectedGspDrug ?? Pharmacy.Drugs.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(whId) || sup == null || drug == null)
        {
            GspStatusMessage = "请选定入库药库、GSP供应商与采购药品";
            _shell?.ShowToast(GspStatusMessage, "Warning");
            return;
        }
        if (!int.TryParse(PoQuantity, out int qty) || qty <= 0)
        {
            GspStatusMessage = "采购数量必须为正整数";
            _shell?.ShowToast(GspStatusMessage, "Warning");
            return;
        }

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var resp = await http.PostAsJsonAsync("/api/v4/inventory/purchase-orders", new
            {
                WarehouseId = whId,
                SupplierId = sup.Id,
                Lines = new[]
                {
                    new
                    {
                        DrugId = drug.Id,
                        RequestedQty = qty,
                        UnitCostPrice = (decimal?)drug.CostPrice
                    }
                },
                AutoApprove = PoAutoApprove,
                Remark = PoRemark
            });
            var parsed = await resp.Content.ReadFromJsonAsync<ApiResponse<PurchaseOrderItemVm>>(GspJsonOptions);
            if (!resp.IsSuccessStatusCode || parsed == null || parsed.Code is not (200 or 0))
            {
                GspStatusMessage = $"创建采购单失败: {parsed?.Message ?? resp.ReasonPhrase}";
                _shell?.ShowToast(GspStatusMessage, "Error");
                return;
            }

            GspStatusMessage = $"采购订单 {parsed.Data?.PoNo} 已生成（状态 {parsed.Data?.Status}）。";
            _shell?.ShowToast(GspStatusMessage, "Success");
            await LoadGspWorkspaceAsync();
            if (parsed.Data != null)
            {
                SelectedPurchaseOrder = PurchaseOrders.FirstOrDefault(p => p.Id == parsed.Data.Id) ?? SelectedPurchaseOrder;
            }
        }
        catch (Exception ex)
        {
            GspStatusMessage = $"创建采购单异常: {ex.Message}";
        }
    }

    public async Task ApproveSelectedPoAsync()
    {
        if (SelectedPurchaseOrder == null)
        {
            GspStatusMessage = "请先在左侧列表选择待审批采购订单";
            return;
        }

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var resp = await http.PostAsync($"/api/v4/inventory/purchase-orders/{Uri.EscapeDataString(SelectedPurchaseOrder.Id)}/approve", null);
            var parsed = await resp.Content.ReadFromJsonAsync<ApiResponse<bool>>(GspJsonOptions);
            if (!resp.IsSuccessStatusCode || parsed == null || parsed.Code is not (200 or 0))
            {
                GspStatusMessage = $"审批采购单失败: {parsed?.Message ?? resp.ReasonPhrase}";
                _shell?.ShowToast(GspStatusMessage, "Warning");
                return;
            }

            GspStatusMessage = $"采购单 {SelectedPurchaseOrder.PoNo} 已审批通过，可执行到货GSP双人验收。";
            _shell?.ShowToast(GspStatusMessage, "Success");
            await LoadGspWorkspaceAsync();
        }
        catch (Exception ex)
        {
            GspStatusMessage = $"审批采购单异常: {ex.Message}";
        }
    }

    public async Task SubmitGspAcceptanceAsync()
    {
        string? whId = SelectedPurchaseOrder?.WarehouseId ?? Pharmacy.SelectedWarehouse?.Id ?? Pharmacy.WarehouseChoices.FirstOrDefault()?.Id;
        var drug = SelectedGspDrug ?? Pharmacy.Drugs.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(whId) || drug == null || string.IsNullOrWhiteSpace(GspBatchNo))
        {
            GspStatusMessage = "请确认药库、验收药品及批号已填写";
            _shell?.ShowToast(GspStatusMessage, "Warning");
            return;
        }
        if (!int.TryParse(GspQuantity, out int qty) || qty <= 0)
        {
            GspStatusMessage = "到货验收数量必须大于 0";
            return;
        }
        if (!TryParseDateInt(GspExpiryDate, out int expiryInt))
        {
            GspStatusMessage = "有效期格式需为 YYYY-MM-DD（如 2028-12-31）";
            return;
        }
        if (string.IsNullOrWhiteSpace(GspReceiverId) || string.IsNullOrWhiteSpace(GspCheckerId))
        {
            GspStatusMessage = "GSP 规范要求填写验收员与复核员双人签名";
            _shell?.ShowToast(GspStatusMessage, "Warning");
            return;
        }

        decimal? coldTemp = decimal.TryParse(GspColdChainTemp, NumberStyles.Any, CultureInfo.InvariantCulture, out var ctVal) ? ctVal : null;
        decimal? maxColdTemp = decimal.TryParse(GspMaxColdChainTemp, NumberStyles.Any, CultureInfo.InvariantCulture, out var mctVal) ? mctVal : null;

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var resp = await http.PostAsJsonAsync("/api/v4/inventory/gsp-acceptance", new
            {
                PoId = SelectedPurchaseOrder?.Id,
                WarehouseId = whId,
                DrugId = drug.Id,
                BatchNo = GspBatchNo.Trim(),
                ExpiryDateInt = expiryInt,
                Quantity = qty,
                ReceiverId = GspReceiverId.Trim(),
                CheckerId = GspCheckerId.Trim(),
                AppearanceOk = GspAppearanceOk,
                PackageIntact = GspPackageIntact,
                LabelConsistent = GspLabelConsistent,
                ColdChainTemp = coldTemp,
                MaxAllowedColdChainTemp = maxColdTemp,
                Remark = "药库GSP到货双人验收入库"
            });
            var parsed = await resp.Content.ReadFromJsonAsync<ApiResponse<GspAcceptanceItemVm>>(GspJsonOptions);
            if (!resp.IsSuccessStatusCode || parsed == null || parsed.Code is not (200 or 0) || parsed.Data == null)
            {
                GspStatusMessage = $"GSP验收失败: {parsed?.Message ?? resp.ReasonPhrase}";
                _shell?.ShowToast(GspStatusMessage, "Error");
                return;
            }

            GspStatusMessage = parsed.Data.Conclusion == "QUALIFIED"
                ? $"GSP验收合格 [{parsed.Data.AcceptanceNo}]：已入库批次 {parsed.Data.BatchNo} ×{parsed.Data.Quantity}。"
                : $"GSP验收不合格已自动冻结隔离 [{parsed.Data.AcceptanceNo}]：批次 {parsed.Data.BatchNo} 禁止出库。";
            _shell?.ShowToast(GspStatusMessage, parsed.Data.Conclusion == "QUALIFIED" ? "Success" : "Warning");

            await Task.WhenAll(LoadGspWorkspaceAsync(), Pharmacy.RefreshStockAsync());
        }
        catch (Exception ex)
        {
            GspStatusMessage = $"GSP验收异常: {ex.Message}";
        }
    }

    public async Task AutoFreezeExpiredAsync()
    {
        try
        {
            int nearDays = int.TryParse(GspNearExpiryDays, out int d) ? Math.Clamp(d, 0, 180) : 30;
            using var http = AuthenticatedHttpClientFactory.Create();
            var resp = await http.PostAsJsonAsync("/api/v4/inventory/batches/auto-freeze-expired", new
            {
                WarehouseOrPharmacyId = Pharmacy.SelectedWarehouse?.Id,
                NearExpiryDays = nearDays
            });
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                GspStatusMessage = $"效期巡检冻结失败: {body}";
                return;
            }
            var parsed = JsonSerializer.Deserialize<ApiResponse<JsonElement>>(body, GspJsonOptions);
            GspStatusMessage = parsed?.Message ?? "效期巡检已完成";
            _shell?.ShowToast(GspStatusMessage, "Info");
            await Pharmacy.RefreshStockAsync();
        }
        catch (Exception ex)
        {
            GspStatusMessage = $"效期巡检异常: {ex.Message}";
        }
    }

    private static bool TryParseDateInt(string text, out int dateInt)
    {
        dateInt = 0;
        var clean = (text ?? string.Empty).Trim().Replace("-", string.Empty);
        return clean.Length == 8 && int.TryParse(clean, out dateInt);
    }
}
