using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Printing;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.Checkup;

public sealed class CheckupSummaryDto
{
    public int TotalOrders { get; set; }
    public int InProgressCount { get; set; }
    public int PendingSummaryCount { get; set; }
    public int CompletedCount { get; set; }
    public int TotalAbnormalFindings { get; set; }
}

public sealed class CheckupPackageOption
{
    public string Id { get; set; } = string.Empty;
    public string BundleCode { get; set; } = string.Empty;
    public string BundleName { get; set; } = string.Empty;
    public decimal PackagePrice { get; set; }
    public decimal SumItemPrice { get; set; }
    public int ItemCount { get; set; }
    public override string ToString() => $"{BundleName}（含{ItemCount}项 · ¥{PackagePrice:0.00}）";
}

public sealed class CheckupOrderRow
{
    public string Id { get; set; } = string.Empty;
    public string CheckupNo { get; set; } = string.Empty;
    public string GuideBarcode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public int Age { get; set; }
    public string? IdCardMasked { get; set; }
    public string? PhoneMasked { get; set; }
    public string CheckupType { get; set; } = "PERSONAL";
    public string? CompanyName { get; set; }
    public string PackageName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string PaymentStatus { get; set; } = "UNPAID";
    public string Status { get; set; } = "REGISTERED";
    public int TotalItemCount { get; set; }
    public int DoneItemCount { get; set; }
    public int AbnormalCount { get; set; }
    public string? SummaryDoctorName { get; set; }
    public string? OverallConclusion { get; set; }
    public string? HealthAdvice { get; set; }

    public string StatusDisplay => Status switch
    {
        "REGISTERED" => "已登记待检",
        "IN_PROGRESS" => $"体检中 ({DoneItemCount}/{TotalItemCount})",
        "PENDING_SUMMARY" => "待主检总检",
        "COMPLETED" => "已出总检报告",
        _ => Status
    };
    public string PayDisplay => PaymentStatus == "PAID" ? "已缴费" : "未缴费";
    public string TypeDisplay => CheckupType == "GROUP" ? $"团检·{CompanyName}" : "个人体检";
}

public sealed class CheckupOrderItemRow
{
    public string Id { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string DeptName { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public string? ResultValue { get; set; }
    public string? ReferenceRange { get; set; }
    public short IsAbnormal { get; set; }
    public string AbnormalFlag { get; set; } = "NORMAL";
    public string? ExamDoctorName { get; set; }
    public string Status { get; set; } = "PENDING";

    public string StatusText => Status == "DONE" ? (IsAbnormal == 1 ? $"已检 [异常:{AbnormalFlag}]" : "已检 [正常]") : "待检";
}

public sealed class CheckupStationViewModel : ClientModelBase, IPrintContextProvider
{
    private readonly ShellViewModel? _shell;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private bool _isBusy;
    private string _statusMessage = "就绪";
    private int _selectedTabIndex;

    // KPI
    private int _totalOrders;
    private int _inProgressCount;
    private int _pendingSummaryCount;
    private int _completedCount;
    private int _totalAbnormalFindings;

    // Filter & selection
    private string _searchKeyword = string.Empty;
    private CheckupOrderRow? _selectedOrder;
    private CheckupOrderItemRow? _selectedOrderItem;

    // Registration form
    private string _regFullName = string.Empty;
    private string _regGender = "男";
    private int _regAge = 32;
    private string _regIdCardNo = string.Empty;
    private string _regPhone = string.Empty;
    private string _regCheckupType = "PERSONAL";
    private string _regCompanyName = string.Empty;
    private CheckupPackageOption? _regSelectedPackage;
    private bool _regAutoPay = true;

    // Dept result entry
    private string _entryResultValue = string.Empty;
    private string _entryRefRange = string.Empty;
    private string _entryAbnormalFlag = "NORMAL";

    // Summary doctor conclusion
    private string _summaryConclusion = string.Empty;
    private string _summaryAdvice = string.Empty;

    public CheckupStationViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        RefreshCommand = new RelayCommand(() => _ = LoadAllAsync());
        SearchCommand = new RelayCommand(() => _ = LoadOrdersAsync());
        RegisterCommand = new RelayCommand(() => _ = RegisterAsync());
        ConfirmPayCommand = new RelayCommand(() => _ = ConfirmPayAsync());
        SaveItemResultCommand = new RelayCommand(() => _ = SaveItemResultAsync());
        GenerateSummaryDraftCommand = new RelayCommand(GenerateSummaryDraft);
        CompleteSummaryCommand = new RelayCommand(() => _ = CompleteSummaryAsync());

        _ = LoadAllAsync();
    }

    public WorkbenchModel Workbench { get; }

    public ObservableCollection<CheckupPackageOption> Packages { get; } = new();
    public ObservableCollection<CheckupOrderRow> Orders { get; } = new();
    public ObservableCollection<CheckupOrderItemRow> SelectedOrderItems { get; } = new();

    public IReadOnlyList<string> GenderOptions { get; } = new[] { "男", "女" };
    public IReadOnlyList<string> CheckupTypeOptions { get; } = new[] { "PERSONAL", "GROUP" };
    public IReadOnlyList<string> AbnormalFlagOptions { get; } = new[] { "NORMAL", "HIGH", "LOW", "POSITIVE" };

    public ICommand RefreshCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand RegisterCommand { get; }
    public ICommand ConfirmPayCommand { get; }
    public ICommand SaveItemResultCommand { get; }
    public ICommand GenerateSummaryDraftCommand { get; }
    public ICommand CompleteSummaryCommand { get; }

    public bool IsBusy { get => _isBusy; set => SetField(ref _isBusy, value); }
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
    public int SelectedTabIndex { get => _selectedTabIndex; set => SetField(ref _selectedTabIndex, value); }

    public int TotalOrders { get => _totalOrders; set => SetField(ref _totalOrders, value); }
    public int InProgressCount { get => _inProgressCount; set => SetField(ref _inProgressCount, value); }
    public int PendingSummaryCount { get => _pendingSummaryCount; set => SetField(ref _pendingSummaryCount, value); }
    public int CompletedCount { get => _completedCount; set => SetField(ref _completedCount, value); }
    public int TotalAbnormalFindings { get => _totalAbnormalFindings; set => SetField(ref _totalAbnormalFindings, value); }

    public string SearchKeyword { get => _searchKeyword; set => SetField(ref _searchKeyword, value); }

    public CheckupOrderRow? SelectedOrder
    {
        get => _selectedOrder;
        set
        {
            if (SetField(ref _selectedOrder, value))
            {
                OnPropertyChanged(nameof(HasSelectedOrder));
                SummaryConclusion = value?.OverallConclusion ?? string.Empty;
                SummaryAdvice = value?.HealthAdvice ?? string.Empty;
                if (value != null) _ = LoadOrderItemsAsync(value.Id);
            }
        }
    }
    public bool HasSelectedOrder => _selectedOrder != null;

    public CheckupOrderItemRow? SelectedOrderItem
    {
        get => _selectedOrderItem;
        set
        {
            if (SetField(ref _selectedOrderItem, value) && value != null)
            {
                EntryResultValue = value.ResultValue ?? string.Empty;
                EntryRefRange = value.ReferenceRange ?? string.Empty;
                EntryAbnormalFlag = string.IsNullOrWhiteSpace(value.AbnormalFlag) ? "NORMAL" : value.AbnormalFlag;
            }
        }
    }

    public string RegFullName { get => _regFullName; set => SetField(ref _regFullName, value); }
    public string RegGender { get => _regGender; set => SetField(ref _regGender, value); }
    public int RegAge { get => _regAge; set => SetField(ref _regAge, value); }
    public string RegIdCardNo { get => _regIdCardNo; set => SetField(ref _regIdCardNo, value); }
    public string RegPhone { get => _regPhone; set => SetField(ref _regPhone, value); }
    public string RegCheckupType { get => _regCheckupType; set => SetField(ref _regCheckupType, value); }
    public string RegCompanyName { get => _regCompanyName; set => SetField(ref _regCompanyName, value); }
    public CheckupPackageOption? RegSelectedPackage { get => _regSelectedPackage; set => SetField(ref _regSelectedPackage, value); }
    public bool RegAutoPay { get => _regAutoPay; set => SetField(ref _regAutoPay, value); }

    public string EntryResultValue { get => _entryResultValue; set => SetField(ref _entryResultValue, value); }
    public string EntryRefRange { get => _entryRefRange; set => SetField(ref _entryRefRange, value); }
    public string EntryAbnormalFlag { get => _entryAbnormalFlag; set => SetField(ref _entryAbnormalFlag, value); }

    public string SummaryConclusion { get => _summaryConclusion; set => SetField(ref _summaryConclusion, value); }
    public string SummaryAdvice { get => _summaryAdvice; set => SetField(ref _summaryAdvice, value); }

    public Dictionary<string, string> BuildPrintContext(object? rowContext = null)
    {
        var ctx = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var order = rowContext as CheckupOrderRow ?? SelectedOrder;
        if (order != null)
        {
            ctx["checkup.checkupNo"] = order.CheckupNo;
            ctx["checkup.guideBarcode"] = order.GuideBarcode;
            ctx["checkup.fullName"] = order.FullName;
            ctx["checkup.gender"] = order.Gender;
            ctx["checkup.age"] = order.Age.ToString();
            ctx["checkup.companyName"] = string.IsNullOrWhiteSpace(order.CompanyName) ? "个人体检" : order.CompanyName;
            ctx["checkup.packageName"] = order.PackageName;
            ctx["checkup.totalAmount"] = order.TotalAmount.ToString("0.00");
            ctx["checkup.overallConclusion"] = order.OverallConclusion ?? string.Empty;
            ctx["patient.fullName"] = order.FullName;
        }
        return ctx;
    }

    public async Task LoadAllAsync()
    {
        IsBusy = true;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var sumRes = await http.GetFromJsonAsync<ClientApiEnvelope<CheckupSummaryDto>>("/api/v4/checkup/summary", JsonOpts);
            if (sumRes?.Data != null)
            {
                TotalOrders = sumRes.Data.TotalOrders;
                InProgressCount = sumRes.Data.InProgressCount;
                PendingSummaryCount = sumRes.Data.PendingSummaryCount;
                CompletedCount = sumRes.Data.CompletedCount;
                TotalAbnormalFindings = sumRes.Data.TotalAbnormalFindings;
            }

            var pkgRes = await http.GetFromJsonAsync<ClientApiEnvelope<List<CheckupPackageOption>>>("/api/v4/checkup/packages", JsonOpts);
            Packages.Clear();
            if (pkgRes?.Data != null) foreach (var p in pkgRes.Data) Packages.Add(p);
            RegSelectedPackage ??= Packages.FirstOrDefault();

            await LoadOrdersAsync();
            StatusMessage = $"已加载 {Orders.Count} 份体检登记单，{Packages.Count} 个可用体检套餐";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败：{ex.Message}";
            _shell?.ShowToast(StatusMessage, "Error");
        }
        finally { IsBusy = false; }
    }

    private async Task LoadOrdersAsync()
    {
        using var http = AuthenticatedHttpClientFactory.Create();
        string url = "/api/v4/checkup/orders" + (string.IsNullOrWhiteSpace(SearchKeyword) ? "" : "?keyword=" + Uri.EscapeDataString(SearchKeyword.Trim()));
        var res = await http.GetFromJsonAsync<ClientApiEnvelope<List<CheckupOrderRow>>>(url, JsonOpts);
        string? prevId = SelectedOrder?.Id;
        Orders.Clear();
        if (res?.Data != null) foreach (var o in res.Data) Orders.Add(o);
        SelectedOrder = (prevId != null ? Orders.FirstOrDefault(o => o.Id == prevId) : null) ?? Orders.FirstOrDefault();
    }

    private async Task LoadOrderItemsAsync(string orderId)
    {
        using var http = AuthenticatedHttpClientFactory.Create();
        var res = await http.GetFromJsonAsync<ClientApiEnvelope<List<CheckupOrderItemRow>>>($"/api/v4/checkup/orders/{orderId}/items", JsonOpts);
        SelectedOrderItems.Clear();
        if (res?.Data != null) foreach (var it in res.Data) SelectedOrderItems.Add(it);
        SelectedOrderItem = SelectedOrderItems.FirstOrDefault();
    }

    private async Task RegisterAsync()
    {
        if (string.IsNullOrWhiteSpace(RegFullName) || RegSelectedPackage == null)
        {
            _shell?.ShowToast("请填写体检人姓名并选择体检套餐。", "Warning");
            return;
        }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("/api/v4/checkup/orders/register", new
        {
            FullName = RegFullName.Trim(),
            Gender = RegGender,
            Age = RegAge,
            IdCardNo = string.IsNullOrWhiteSpace(RegIdCardNo) ? null : RegIdCardNo.Trim(),
            Phone = string.IsNullOrWhiteSpace(RegPhone) ? null : RegPhone.Trim(),
            CheckupType = RegCheckupType,
            CompanyName = string.IsNullOrWhiteSpace(RegCompanyName) ? null : RegCompanyName.Trim(),
            PackageId = RegSelectedPackage.Id,
            AutoPay = RegAutoPay
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if (env?.Code == 0)
        {
            _shell?.ShowToast(env.Message ?? "体检登记成功", "Success");
            RegFullName = string.Empty;
            RegIdCardNo = string.Empty;
            RegPhone = string.Empty;
            await LoadAllAsync();
        }
        else _shell?.ShowToast(env?.Message ?? "登记失败", "Error");
    }

    private async Task ConfirmPayAsync()
    {
        if (SelectedOrder == null) return;
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsync($"/api/v4/checkup/orders/{SelectedOrder.Id}/pay", null);
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if (env?.Code == 0)
        {
            _shell?.ShowToast("体检缴费已确认。", "Success");
            await LoadAllAsync();
        }
    }

    private async Task SaveItemResultAsync()
    {
        if (SelectedOrderItem == null)
        {
            _shell?.ShowToast("请先选择需要录入结果的分科项目。", "Warning");
            return;
        }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PutAsJsonAsync($"/api/v4/checkup/items/{SelectedOrderItem.Id}/result", new
        {
            ResultValue = EntryResultValue,
            ReferenceRange = EntryRefRange,
            AbnormalFlag = EntryAbnormalFlag
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if (env?.Code == 0)
        {
            _shell?.ShowToast($"[{SelectedOrderItem.ItemName}] 分科结果已保存。", "Success");
            if (SelectedOrder != null) await LoadOrderItemsAsync(SelectedOrder.Id);
            await LoadAllAsync();
        }
        else _shell?.ShowToast(env?.Message ?? "保存分科结果失败", "Error");
    }

    private void GenerateSummaryDraft()
    {
        var abnormals = SelectedOrderItems.Where(i => i.IsAbnormal == 1).ToList();
        if (abnormals.Count == 0)
        {
            SummaryConclusion = "本次健康体检各分科检查及实验室检验指标均未见明显异常。";
            SummaryAdvice = "建议保持规律作息与均衡饮食，每周坚持有氧运动，定期复查年度体检。";
        }
        else
        {
            SummaryConclusion = "本次体检共发现异常指标 " + abnormals.Count + " 项：\n" +
                                string.Join("\n", abnormals.Select((a, idx) => $"{idx + 1}. [{a.DeptName}] {a.ItemName}：{a.ResultValue}（参考范围：{a.ReferenceRange ?? "-"}，标记：{a.AbnormalFlag}）"));
            SummaryAdvice = "请针对上述异常项目携本报告前往相应专科门诊进一步复查随访，注意低盐低脂饮食并监测相关指标变化。";
        }
    }

    private async Task CompleteSummaryAsync()
    {
        if (SelectedOrder == null || string.IsNullOrWhiteSpace(SummaryConclusion))
        {
            _shell?.ShowToast("请先选择体检单并填写主检总检结论。", "Warning");
            return;
        }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync($"/api/v4/checkup/orders/{SelectedOrder.Id}/complete-summary", new
        {
            OverallConclusion = SummaryConclusion,
            HealthAdvice = SummaryAdvice
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if (env?.Code == 0)
        {
            _shell?.ShowToast("主检总检报告已审核发布。", "Success");
            await LoadAllAsync();
        }
        else _shell?.ShowToast(env?.Message ?? "发布失败", "Error");
    }
}
