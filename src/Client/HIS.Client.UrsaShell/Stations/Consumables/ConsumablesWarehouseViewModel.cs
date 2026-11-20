using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.BuildingBlocks.Common;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.Consumables;

public sealed class ConsumableItemModel : ClientModelBase
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Spec { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsSterile { get; set; }
    public bool IsHighValue { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;

    public string HighValueTag => IsHighValue ? "★ 高值" : "普耗";
    public string SterileTag => IsSterile ? "无菌" : "非无菌";
}

public sealed class ConsumableStockItemModel : ClientModelBase
{
    public string ConsumableId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Spec { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int ReservedQty { get; set; }
    public int AvailableQty => Quantity - ReservedQty;
    public decimal Price { get; set; }
    public decimal StockValue => Quantity * Price;
    public string BatchNo { get; set; } = string.Empty;
    public string ExpiryDate { get; set; } = string.Empty;
    public bool IsFrozen { get; set; }
}

public sealed class ConsumableRequisitionItemModel : ClientModelBase
{
    public string Id { get; set; } = string.Empty;
    public string RequisitionNo { get; set; } = string.Empty;
    public string WardId { get; set; } = string.Empty;
    public string DeptId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public string? ApprovedBy { get; set; }
    public string? IssuedBy { get; set; }
    public string? Remark { get; set; }
    public string ApprovalStatus { get; set; } = "APPROVED";
    public string WorkflowInstanceId { get; set; } = string.Empty;
    public string ApprovalStatusBadge => ApprovalStatus switch
    {
        "PENDING_APPROVAL" => "审批流转中",
        "APPROVED" => "已审批核准",
        "REJECTED" => "审批已否决",
        _ => ApprovalStatus
    };
    public string ApprovalStatusBrush => ApprovalStatus switch
    {
        "PENDING_APPROVAL" => "#D97706",
        "APPROVED" => "#059669",
        "REJECTED" => "#DC2626",
        _ => "#64748B"
    };
    public long CreatedAtMs { get; set; }
    public string CreatedAtText => DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtMs).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string StatusLabel => Status switch
    {
        "PENDING" => "待审核",
        "APPROVED" => "已审批·待出库",
        "ISSUED" => "已发货出库",
        "REJECTED" => "已驳回",
        _ => Status
    };
}

public sealed class SterilePackageItemModel : ClientModelBase
{
    public string Id { get; set; } = string.Empty;
    public string PackageCode { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CurrentLocation { get; set; } = string.Empty;
    public long? ValidUntilMs { get; set; }
    public string ValidUntilText => ValidUntilMs.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(ValidUntilMs.Value).ToLocalTime().ToString("yyyy-MM-dd") : "-";
    public string StatusLabel => Status switch
    {
        "CREATED" => "已组包·待灭菌",
        "STERILIZED" => "已灭菌合格",
        "ISSUED" => "已发放病区",
        "USED" => "术中已使用",
        "EXPIRED" => "已过期",
        _ => Status
    };
}

public sealed class ConsumablesWarehouseViewModel : ClientModelBase, IShortcutTarget
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly ShellViewModel? _shell;

    private int _selectedTabIndex;
    private string _searchKeyword = string.Empty;
    private bool _isLoading;

    // Form fields for catalog
    private string _newCode = string.Empty;
    private string _newName = string.Empty;
    private string _newSpec = string.Empty;
    private string _newUnit = "支";
    private string _newCategory = "GENERAL";
    private bool _newIsSterile = true;
    private bool _newIsHighValue;
    private decimal _newPrice = 1.00m;

    // Form fields for stock-in
    private string _stockInConsumableId = string.Empty;
    private int _stockInQty = 100;
    private decimal _stockInCost = 0.80m;
    private string _stockInBatchNo = string.Empty;
    private string _stockInExpiry = DateTime.Now.AddYears(2).ToString("yyyy-MM-dd");

    // CSSD form
    private string _newPackageCode = string.Empty;
    private string _newPackageName = string.Empty;

    public ConsumablesWarehouseViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        CreateConsumableCommand = new AsyncRelayCommand(CreateConsumableAsync);
        StockInCommand = new AsyncRelayCommand(StockInAsync);
        ApproveRequisitionCommand = new AsyncRelayCommand(p => ApproveRequisitionAsync(p as ConsumableRequisitionItemModel));
        IssueRequisitionCommand = new AsyncRelayCommand(p => IssueRequisitionAsync(p as ConsumableRequisitionItemModel));
        CreatePackageCommand = new AsyncRelayCommand(CreatePackageAsync);
        SterilizePackageCommand = new AsyncRelayCommand(p => SterilizePackageAsync(p as SterilePackageItemModel));
        DispatchPackageCommand = new AsyncRelayCommand(p => DispatchPackageAsync(p as SterilePackageItemModel));

        _ = RefreshAsync();
    }

    public WorkbenchModel Workbench { get; }
    public ObservableCollection<ConsumableItemModel> Consumables { get; } = new();
    public ObservableCollection<ConsumableStockItemModel> Stocks { get; } = new();
    public ObservableCollection<ConsumableRequisitionItemModel> Requisitions { get; } = new();
    public ObservableCollection<SterilePackageItemModel> SterilePackages { get; } = new();

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetField(ref _selectedTabIndex, value);
    }

    public string SearchKeyword
    {
        get => _searchKeyword;
        set => SetField(ref _searchKeyword, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetField(ref _isLoading, value);
    }

    // New Consumable Form Properties
    public string NewCode { get => _newCode; set => SetField(ref _newCode, value); }
    public string NewName { get => _newName; set => SetField(ref _newName, value); }
    public string NewSpec { get => _newSpec; set => SetField(ref _newSpec, value); }
    public string NewUnit { get => _newUnit; set => SetField(ref _newUnit, value); }
    public string NewCategory { get => _newCategory; set => SetField(ref _newCategory, value); }
    public bool NewIsSterile { get => _newIsSterile; set => SetField(ref _newIsSterile, value); }
    public bool NewIsHighValue { get => _newIsHighValue; set => SetField(ref _newIsHighValue, value); }
    public decimal NewPrice { get => _newPrice; set => SetField(ref _newPrice, value); }

    // Stock-In Form Properties
    public string StockInConsumableId { get => _stockInConsumableId; set => SetField(ref _stockInConsumableId, value); }
    public int StockInQty { get => _stockInQty; set => SetField(ref _stockInQty, value); }
    public decimal StockInCost { get => _stockInCost; set => SetField(ref _stockInCost, value); }
    public string StockInBatchNo { get => _stockInBatchNo; set => SetField(ref _stockInBatchNo, value); }
    public string StockInExpiry { get => _stockInExpiry; set => SetField(ref _stockInExpiry, value); }

    // CSSD Form Properties
    public string NewPackageCode { get => _newPackageCode; set => SetField(ref _newPackageCode, value); }
    public string NewPackageName { get => _newPackageName; set => SetField(ref _newPackageName, value); }

    // Commands
    public ICommand RefreshCommand { get; }
    public ICommand CreateConsumableCommand { get; }
    public ICommand StockInCommand { get; }
    public ICommand ApproveRequisitionCommand { get; }
    public ICommand IssueRequisitionCommand { get; }
    public ICommand CreatePackageCommand { get; }
    public ICommand SterilizePackageCommand { get; }
    public ICommand DispatchPackageCommand { get; }

    public bool OnShortcut(string keyGesture)
    {
        if (keyGesture is "F5")
        {
            _ = RefreshAsync();
            return true;
        }
        return false;
    }

    public async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();

            // Load catalog
            var catRes = await http.GetFromJsonAsync<ApiResponse<List<ConsumableItemModel>>>("/api/v4/csm/consumables", JsonOptions);
            if (catRes?.Data != null)
            {
                Consumables.Clear();
                foreach (var c in catRes.Data) Consumables.Add(c);
            }

            // Load stocks
            var stockRes = await http.GetFromJsonAsync<ApiResponse<List<ConsumableStockItemModel>>>("/api/v4/csm/stocks", JsonOptions);
            if (stockRes?.Data != null)
            {
                Stocks.Clear();
                foreach (var s in stockRes.Data) Stocks.Add(s);
            }

            // Load requisitions
            var reqRes = await http.GetFromJsonAsync<ApiResponse<List<ConsumableRequisitionItemModel>>>("/api/v4/csm/requisitions", JsonOptions);
            if (reqRes?.Data != null)
            {
                Requisitions.Clear();
                foreach (var r in reqRes.Data) Requisitions.Add(r);
            }

            // Load packages
            var pkgRes = await http.GetFromJsonAsync<ApiResponse<List<SterilePackageItemModel>>>("/api/v4/csm/cssd/packages", JsonOptions);
            if (pkgRes?.Data != null)
            {
                SterilePackages.Clear();
                foreach (var p in pkgRes.Data) SterilePackages.Add(p);
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"加载耗材物流数据失败: {ex.Message}", "Error");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task CreateConsumableAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCode) || string.IsNullOrWhiteSpace(NewName))
        {
            _shell?.ShowToast("耗材编码与品名不能为空", "Warning");
            return;
        }

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new
            {
                Code = NewCode.Trim(),
                Name = NewName.Trim(),
                Spec = NewSpec.Trim(),
                Unit = NewUnit.Trim(),
                Category = NewCategory,
                IsSterile = (short)(NewIsSterile ? 1 : 0),
                IsHighValue = (short)(NewIsHighValue ? 1 : 0),
                Price = NewPrice
            };
            var res = await http.PostAsJsonAsync("/api/v4/csm/consumables", payload, JsonOptions);
            if (res.IsSuccessStatusCode)
            {
                _shell?.ShowToast($"耗材 [{NewName}] 已成功维护到全院目录", "Success");
                NewCode = string.Empty;
                NewName = string.Empty;
                NewSpec = string.Empty;
                await RefreshAsync();
            }
            else
            {
                _shell?.ShowToast("维护耗材失败，请检查编码是否冲突", "Error");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"新增耗材异常: {ex.Message}", "Error");
        }
    }

    private async Task StockInAsync()
    {
        if (string.IsNullOrWhiteSpace(StockInConsumableId) || StockInQty <= 0)
        {
            _shell?.ShowToast("请选择入库耗材并填写大于0的入库数量", "Warning");
            return;
        }

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new
            {
                ConsumableId = StockInConsumableId,
                WarehouseId = "DEPT-WH-CSM",
                Quantity = StockInQty,
                UnitCost = StockInCost,
                BatchNo = string.IsNullOrWhiteSpace(StockInBatchNo) ? $"B{DateTime.Now:yyyyMMddHHmmss}" : StockInBatchNo,
                ExpiryDate = StockInExpiry,
                ManufactureDate = DateTime.Now.ToString("yyyy-MM-dd")
            };
            var res = await http.PostAsJsonAsync("/api/v4/csm/stock-in", payload, JsonOptions);
            if (res.IsSuccessStatusCode)
            {
                _shell?.ShowToast("耗材采购入库登记成功", "Success");
                await RefreshAsync();
            }
            else
            {
                _shell?.ShowToast("耗材入库失败", "Error");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"入库请求异常: {ex.Message}", "Error");
        }
    }

    private async Task ApproveRequisitionAsync(ConsumableRequisitionItemModel? req)
    {
        if (req == null) return;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.PostAsync($"/api/v4/csm/requisitions/{req.Id}/approve", null);
            if (res.IsSuccessStatusCode)
            {
                _shell?.ShowToast($"请领单 [{req.RequisitionNo}] 审核通过", "Success");
                await RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"审批异常: {ex.Message}", "Error");
        }
    }

    private async Task IssueRequisitionAsync(ConsumableRequisitionItemModel? req)
    {
        if (req == null) return;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.PostAsync($"/api/v4/csm/requisitions/{req.Id}/issue", null);
            if (res.IsSuccessStatusCode)
            {
                _shell?.ShowToast($"请领单 [{req.RequisitionNo}] 已出库发货", "Success");
                await RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"出库异常: {ex.Message}", "Error");
        }
    }

    private async Task CreatePackageAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPackageCode) || string.IsNullOrWhiteSpace(NewPackageName))
        {
            _shell?.ShowToast("消毒包编号与名称必填", "Warning");
            return;
        }

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new
            {
                PackageCode = NewPackageCode.Trim(),
                PackageName = NewPackageName.Trim(),
                ContentsJson = "[]"
            };
            var res = await http.PostAsJsonAsync("/api/v4/csm/cssd/packages", payload, JsonOptions);
            if (res.IsSuccessStatusCode)
            {
                _shell?.ShowToast($"无菌器械包 [{NewPackageName}] 组包成功", "Success");
                NewPackageCode = string.Empty;
                NewPackageName = string.Empty;
                await RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"组包异常: {ex.Message}", "Error");
        }
    }

    private async Task SterilizePackageAsync(SterilePackageItemModel? pkg)
    {
        if (pkg == null) return;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new
            {
                PackageId = pkg.Id,
                BatchNo = $"ST-{DateTime.Now:yyyyMMdd}-{pkg.Id[..Math.Min(4, pkg.Id.Length)]}",
                Method = "HIGH_PRESSURE_STEAM",
                ValidDays = 180
            };
            var res = await http.PostAsJsonAsync("/api/v4/csm/cssd/sterilize", payload, JsonOptions);
            if (res.IsSuccessStatusCode)
            {
                _shell?.ShowToast($"器械包 [{pkg.PackageName}] 高压蒸汽灭菌合格，有效期 180 天", "Success");
                await RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"灭菌登记异常: {ex.Message}", "Error");
        }
    }

    private async Task DispatchPackageAsync(SterilePackageItemModel? pkg)
    {
        if (pkg == null) return;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new
            {
                PackageId = pkg.Id,
                ToLocation = "DEPT-WARD-01",
                MovementType = "ISSUE",
                Remark = "手术室/病区借用领发"
            };
            var res = await http.PostAsJsonAsync("/api/v4/csm/cssd/dispatch", payload, JsonOptions);
            if (res.IsSuccessStatusCode)
            {
                _shell?.ShowToast($"器械包 [{pkg.PackageName}] 已成功调拨发放到病区", "Success");
                await RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"器械包发放异常: {ex.Message}", "Error");
        }
    }
}
