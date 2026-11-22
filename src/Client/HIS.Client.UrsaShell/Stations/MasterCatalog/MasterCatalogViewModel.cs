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

namespace HIS.Client.UrsaShell.Stations.MasterCatalog;

public sealed class DrugCatalogRow
{
    public string Id { get; set; } = string.Empty;
    public string DrugCode { get; set; } = string.Empty;
    public string DrugName { get; set; } = string.Empty;
    public string Spec { get; set; } = string.Empty;
    public string UnitName { get; set; } = "盒";
    public decimal Price { get; set; }
    public decimal CostPrice { get; set; }
    public int AntibioticLevel { get; set; }
    public short IsNarcotic { get; set; }
    public short IsSkinTestRequired { get; set; }
    public short IsActive { get; set; } = 1;

    public string TagSummary =>
        $"¥{Price:0.00}/{UnitName} · 抗菌级:{AntibioticLevel}" +
        (IsNarcotic == 1 ? " · 毒麻" : "") +
        (IsSkinTestRequired == 1 ? " · 需皮试" : "");
}

public sealed class ConsumableCatalogRow
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Spec { get; set; } = string.Empty;
    public string Unit { get; set; } = "支";
    public string Category { get; set; } = string.Empty;
    public short IsSterile { get; set; }
    public short IsHighValue { get; set; }
    public decimal Price { get; set; }
    public short IsActive { get; set; } = 1;

    public string TagSummary =>
        $"¥{Price:0.00}/{Unit} · {Category}" +
        (IsHighValue == 1 ? " · 高值" : "") +
        (IsSterile == 1 ? " · 无菌" : "");
}

public sealed class PriceItemOption
{
    public string Id { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string FeeCategory { get; set; } = string.Empty;
    public string? Spec { get; set; }
    public string UnitName { get; set; } = "项";
    public decimal UnitPrice { get; set; }

    public override string ToString() => $"{ItemName} ({ItemCode}) - ¥{UnitPrice:0.00}";
}

public sealed class OrderBundleSummaryRow
{
    public string Id { get; set; } = string.Empty;
    public string BundleCode { get; set; } = string.Empty;
    public string BundleName { get; set; } = string.Empty;
    public string? PinyinCode { get; set; }
    public string BundleCategory { get; set; } = "LAB_PANEL";
    public string ApplicableScene { get; set; } = "ALL";
    public string GenderLimit { get; set; } = "ALL";
    public string? DeptName { get; set; }
    public string? SpecimenType { get; set; }
    public decimal PackagePrice { get; set; }
    public decimal SumItemPrice { get; set; }
    public short IsAllowSplit { get; set; }
    public string? Remark { get; set; }
    public int ItemCount { get; set; }

    public string CategoryDisplay => BundleCategory switch
    {
        "LAB_PANEL" => "检验组合",
        "EXAM_PANEL" => "检查组合",
        "TREATMENT_SET" => "治疗套餐",
        "HEALTH_CHECKUP" => "健康体检套餐",
        _ => BundleCategory
    };
}

public sealed class BundleItemRow
{
    public string ItemType { get; set; } = "PRICE_ITEM";
    public string? TargetItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Spec { get; set; }
    public string UnitName { get; set; } = "项";
    public decimal Quantity { get; set; } = 1m;
    public decimal UnitPrice { get; set; }
    public decimal SubtotalAmount => Quantity * UnitPrice;
    public string? ExecDeptName { get; set; }
    public int SortOrder { get; set; } = 10;
    public bool IsRequired { get; set; } = true;
}

public sealed class BundleDetailEnvelope
{
    public OrderBundleSummaryRow? Bundle { get; set; }
    public List<BundleItemRow>? Items { get; set; }
}

public sealed class MasterCatalogViewModel : ClientModelBase
{
    private readonly ShellViewModel? _shell;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private bool _isBusy;
    private string _statusMessage = "就绪";
    private int _selectedTabIndex;

    // Drug editor
    private string _drugSearch = string.Empty;
    private DrugCatalogRow? _selectedDrug;
    private string _editDrugId = string.Empty;
    private string _editDrugCode = string.Empty;
    private string _editDrugName = string.Empty;
    private string _editDrugSpec = string.Empty;
    private string _editDrugUnit = "盒";
    private decimal _editDrugPrice;
    private decimal _editDrugCostPrice;
    private int _editDrugAbxLevel;
    private bool _editDrugIsNarcotic;
    private bool _editDrugIsSkinTest;

    // Consumable editor
    private string _csmSearch = string.Empty;
    private ConsumableCatalogRow? _selectedConsumable;
    private string _editCsmId = string.Empty;
    private string _editCsmCode = string.Empty;
    private string _editCsmName = string.Empty;
    private string _editCsmSpec = string.Empty;
    private string _editCsmUnit = "支";
    private string _editCsmCategory = "普通医用耗材";
    private decimal _editCsmPrice;
    private bool _editCsmIsSterile;
    private bool _editCsmIsHighValue;

    // Bundle editor
    private string _bundleSearch = string.Empty;
    private OrderBundleSummaryRow? _selectedBundle;
    private string _editBundleId = string.Empty;
    private string _editBundleCode = string.Empty;
    private string _editBundleName = string.Empty;
    private string _editBundlePinyin = string.Empty;
    private string _editBundleCategory = "LAB_PANEL";
    private string _editBundleScene = "ALL";
    private string _editBundleGender = "ALL";
    private string _editBundleDept = "检验科";
    private string _editBundleSpecimen = "静脉血(黄头促凝管)";
    private decimal _editBundlePackagePrice;
    private string _editBundleRemark = string.Empty;
    private PriceItemOption? _selectedPriceItemToAdd;
    private string _addItemExecDept = "检验科";
    private decimal _addItemQty = 1m;
    private string _expandPreviewText = string.Empty;

    public MasterCatalogViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        RefreshCommand = new RelayCommand(() => _ = LoadAllAsync());

        SearchDrugsCommand = new RelayCommand(() => _ = LoadDrugsAsync());
        NewDrugCommand = new RelayCommand(ClearDrugEditor);
        SaveDrugCommand = new RelayCommand(() => _ = SaveDrugAsync());
        DisableDrugCommand = new RelayCommand(() => _ = DisableDrugAsync());

        SearchConsumablesCommand = new RelayCommand(() => _ = LoadConsumablesAsync());
        NewConsumableCommand = new RelayCommand(ClearConsumableEditor);
        SaveConsumableCommand = new RelayCommand(() => _ = SaveConsumableAsync());
        DisableConsumableCommand = new RelayCommand(() => _ = DisableConsumableAsync());

        SearchBundlesCommand = new RelayCommand(() => _ = LoadBundlesAsync());
        NewBundleCommand = new RelayCommand(ClearBundleEditor);
        AddPriceItemToBundleCommand = new RelayCommand(AddSelectedPriceItemToBundle);
        RemoveBundleItemCommand = new RelayCommand(p => RemoveBundleItem(p as BundleItemRow));
        SaveBundleCommand = new RelayCommand(() => _ = SaveBundleAsync());
        DisableBundleCommand = new RelayCommand(() => _ = DisableBundleAsync());
        TestExpandBundleCommand = new RelayCommand(() => _ = TestExpandBundleAsync());

        _ = LoadAllAsync();
    }

    public WorkbenchModel Workbench { get; }

    public ObservableCollection<DrugCatalogRow> Drugs { get; } = new();
    public ObservableCollection<ConsumableCatalogRow> Consumables { get; } = new();
    public ObservableCollection<PriceItemOption> PriceItems { get; } = new();
    public ObservableCollection<OrderBundleSummaryRow> Bundles { get; } = new();
    public ObservableCollection<BundleItemRow> EditingBundleItems { get; } = new();

    public IReadOnlyList<string> BundleCategoryOptions { get; } = new[] { "LAB_PANEL", "EXAM_PANEL", "TREATMENT_SET", "HEALTH_CHECKUP" };
    public IReadOnlyList<string> SceneOptions { get; } = new[] { "ALL", "OPD", "IPD", "CHECKUP" };
    public IReadOnlyList<string> GenderLimitOptions { get; } = new[] { "ALL", "MALE", "FEMALE" };

    public ICommand RefreshCommand { get; }
    public ICommand SearchDrugsCommand { get; }
    public ICommand NewDrugCommand { get; }
    public ICommand SaveDrugCommand { get; }
    public ICommand DisableDrugCommand { get; }
    public ICommand SearchConsumablesCommand { get; }
    public ICommand NewConsumableCommand { get; }
    public ICommand SaveConsumableCommand { get; }
    public ICommand DisableConsumableCommand { get; }
    public ICommand SearchBundlesCommand { get; }
    public ICommand NewBundleCommand { get; }
    public ICommand AddPriceItemToBundleCommand { get; }
    public ICommand RemoveBundleItemCommand { get; }
    public ICommand SaveBundleCommand { get; }
    public ICommand DisableBundleCommand { get; }
    public ICommand TestExpandBundleCommand { get; }

    public bool IsBusy { get => _isBusy; set => SetField(ref _isBusy, value); }
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
    public int SelectedTabIndex { get => _selectedTabIndex; set => SetField(ref _selectedTabIndex, value); }

    // Drug properties
    public string DrugSearch { get => _drugSearch; set => SetField(ref _drugSearch, value); }
    public DrugCatalogRow? SelectedDrug
    {
        get => _selectedDrug;
        set
        {
            if (SetField(ref _selectedDrug, value) && value != null)
            {
                _editDrugId = value.Id;
                EditDrugCode = value.DrugCode;
                EditDrugName = value.DrugName;
                EditDrugSpec = value.Spec;
                EditDrugUnit = value.UnitName;
                EditDrugPrice = value.Price;
                EditDrugCostPrice = value.CostPrice;
                EditDrugAbxLevel = value.AntibioticLevel;
                EditDrugIsNarcotic = value.IsNarcotic == 1;
                EditDrugIsSkinTest = value.IsSkinTestRequired == 1;
            }
        }
    }
    public string EditDrugCode { get => _editDrugCode; set => SetField(ref _editDrugCode, value); }
    public string EditDrugName { get => _editDrugName; set => SetField(ref _editDrugName, value); }
    public string EditDrugSpec { get => _editDrugSpec; set => SetField(ref _editDrugSpec, value); }
    public string EditDrugUnit { get => _editDrugUnit; set => SetField(ref _editDrugUnit, value); }
    public decimal EditDrugPrice { get => _editDrugPrice; set => SetField(ref _editDrugPrice, value); }
    public decimal EditDrugCostPrice { get => _editDrugCostPrice; set => SetField(ref _editDrugCostPrice, value); }
    public int EditDrugAbxLevel { get => _editDrugAbxLevel; set => SetField(ref _editDrugAbxLevel, value); }
    public bool EditDrugIsNarcotic { get => _editDrugIsNarcotic; set => SetField(ref _editDrugIsNarcotic, value); }
    public bool EditDrugIsSkinTest { get => _editDrugIsSkinTest; set => SetField(ref _editDrugIsSkinTest, value); }

    // Consumable properties
    public string CsmSearch { get => _csmSearch; set => SetField(ref _csmSearch, value); }
    public ConsumableCatalogRow? SelectedConsumable
    {
        get => _selectedConsumable;
        set
        {
            if (SetField(ref _selectedConsumable, value) && value != null)
            {
                _editCsmId = value.Id;
                EditCsmCode = value.Code;
                EditCsmName = value.Name;
                EditCsmSpec = value.Spec;
                EditCsmUnit = value.Unit;
                EditCsmCategory = value.Category;
                EditCsmPrice = value.Price;
                EditCsmIsSterile = value.IsSterile == 1;
                EditCsmIsHighValue = value.IsHighValue == 1;
            }
        }
    }
    public string EditCsmCode { get => _editCsmCode; set => SetField(ref _editCsmCode, value); }
    public string EditCsmName { get => _editCsmName; set => SetField(ref _editCsmName, value); }
    public string EditCsmSpec { get => _editCsmSpec; set => SetField(ref _editCsmSpec, value); }
    public string EditCsmUnit { get => _editCsmUnit; set => SetField(ref _editCsmUnit, value); }
    public string EditCsmCategory { get => _editCsmCategory; set => SetField(ref _editCsmCategory, value); }
    public decimal EditCsmPrice { get => _editCsmPrice; set => SetField(ref _editCsmPrice, value); }
    public bool EditCsmIsSterile { get => _editCsmIsSterile; set => SetField(ref _editCsmIsSterile, value); }
    public bool EditCsmIsHighValue { get => _editCsmIsHighValue; set => SetField(ref _editCsmIsHighValue, value); }

    // Bundle properties
    public string BundleSearch { get => _bundleSearch; set => SetField(ref _bundleSearch, value); }
    public OrderBundleSummaryRow? SelectedBundle
    {
        get => _selectedBundle;
        set
        {
            if (SetField(ref _selectedBundle, value) && value != null)
            {
                _ = LoadBundleDetailAsync(value.Id);
            }
        }
    }
    public string EditBundleCode { get => _editBundleCode; set => SetField(ref _editBundleCode, value); }
    public string EditBundleName { get => _editBundleName; set => SetField(ref _editBundleName, value); }
    public string EditBundlePinyin { get => _editBundlePinyin; set => SetField(ref _editBundlePinyin, value); }
    public string EditBundleCategory { get => _editBundleCategory; set => SetField(ref _editBundleCategory, value); }
    public string EditBundleScene { get => _editBundleScene; set => SetField(ref _editBundleScene, value); }
    public string EditBundleGender { get => _editBundleGender; set => SetField(ref _editBundleGender, value); }
    public string EditBundleDept { get => _editBundleDept; set => SetField(ref _editBundleDept, value); }
    public string EditBundleSpecimen { get => _editBundleSpecimen; set => SetField(ref _editBundleSpecimen, value); }
    public decimal EditBundlePackagePrice { get => _editBundlePackagePrice; set => SetField(ref _editBundlePackagePrice, value); }
    public string EditBundleRemark { get => _editBundleRemark; set => SetField(ref _editBundleRemark, value); }
    public PriceItemOption? SelectedPriceItemToAdd { get => _selectedPriceItemToAdd; set => SetField(ref _selectedPriceItemToAdd, value); }
    public string AddItemExecDept { get => _addItemExecDept; set => SetField(ref _addItemExecDept, value); }
    public decimal AddItemQty { get => _addItemQty; set => SetField(ref _addItemQty, value); }
    public string ExpandPreviewText { get => _expandPreviewText; set => SetField(ref _expandPreviewText, value); }
    public decimal BundleItemsSumPrice => EditingBundleItems.Sum(i => i.SubtotalAmount);

    public async Task LoadAllAsync()
    {
        IsBusy = true;
        StatusMessage = "正在加载全院药品、耗材字典与医疗组合套餐...";
        try
        {
            await LoadDrugsAsync();
            await LoadConsumablesAsync();
            await LoadPriceItemsAsync();
            await LoadBundlesAsync();
            StatusMessage = $"已同步药品 {Drugs.Count} 种、耗材 {Consumables.Count} 种、组合套餐 {Bundles.Count} 套";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败：{ex.Message}";
            _shell?.ShowToast(StatusMessage, "Error");
        }
        finally { IsBusy = false; }
    }

    private async Task LoadDrugsAsync()
    {
        using var http = AuthenticatedHttpClientFactory.Create();
        string url = "/api/v4/catalog/drugs" + (string.IsNullOrWhiteSpace(DrugSearch) ? "" : "?keyword=" + Uri.EscapeDataString(DrugSearch.Trim()));
        var res = await http.GetFromJsonAsync<ClientApiEnvelope<List<DrugCatalogRow>>>(url, JsonOpts);
        Drugs.Clear();
        if (res?.Data != null) foreach (var d in res.Data) Drugs.Add(d);
        SelectedDrug ??= Drugs.FirstOrDefault();
    }

    private async Task LoadConsumablesAsync()
    {
        using var http = AuthenticatedHttpClientFactory.Create();
        string url = "/api/v4/catalog/consumables" + (string.IsNullOrWhiteSpace(CsmSearch) ? "" : "?keyword=" + Uri.EscapeDataString(CsmSearch.Trim()));
        var res = await http.GetFromJsonAsync<ClientApiEnvelope<List<ConsumableCatalogRow>>>(url, JsonOpts);
        Consumables.Clear();
        if (res?.Data != null) foreach (var c in res.Data) Consumables.Add(c);
        SelectedConsumable ??= Consumables.FirstOrDefault();
    }

    private async Task LoadPriceItemsAsync()
    {
        using var http = AuthenticatedHttpClientFactory.Create();
        var res = await http.GetFromJsonAsync<ClientApiEnvelope<List<PriceItemOption>>>("/api/v4/catalog/price-items", JsonOpts);
        PriceItems.Clear();
        if (res?.Data != null) foreach (var p in res.Data) PriceItems.Add(p);
        SelectedPriceItemToAdd ??= PriceItems.FirstOrDefault();
    }

    private async Task LoadBundlesAsync()
    {
        using var http = AuthenticatedHttpClientFactory.Create();
        string url = "/api/v4/catalog/bundles" + (string.IsNullOrWhiteSpace(BundleSearch) ? "" : "?keyword=" + Uri.EscapeDataString(BundleSearch.Trim()));
        var res = await http.GetFromJsonAsync<ClientApiEnvelope<List<OrderBundleSummaryRow>>>(url, JsonOpts);
        string? prevId = _selectedBundle?.Id;
        Bundles.Clear();
        if (res?.Data != null) foreach (var b in res.Data) Bundles.Add(b);
        SelectedBundle = (prevId != null ? Bundles.FirstOrDefault(b => b.Id == prevId) : null) ?? Bundles.FirstOrDefault();
    }

    private async Task LoadBundleDetailAsync(string bundleId)
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.GetFromJsonAsync<ClientApiEnvelope<BundleDetailEnvelope>>($"/api/v4/catalog/bundles/{bundleId}", JsonOpts);
            if (res?.Data?.Bundle != null)
            {
                var b = res.Data.Bundle;
                _editBundleId = b.Id;
                EditBundleCode = b.BundleCode;
                EditBundleName = b.BundleName;
                EditBundlePinyin = b.PinyinCode ?? string.Empty;
                EditBundleCategory = b.BundleCategory;
                EditBundleScene = b.ApplicableScene;
                EditBundleGender = b.GenderLimit;
                EditBundleDept = b.DeptName ?? string.Empty;
                EditBundleSpecimen = b.SpecimenType ?? string.Empty;
                EditBundlePackagePrice = b.PackagePrice;
                EditBundleRemark = b.Remark ?? string.Empty;

                EditingBundleItems.Clear();
                if (res.Data.Items != null)
                    foreach (var it in res.Data.Items) EditingBundleItems.Add(it);
                OnPropertyChanged(nameof(BundleItemsSumPrice));
                ExpandPreviewText = string.Empty;
            }
        }
        catch { }
    }

    private void ClearDrugEditor()
    {
        _editDrugId = string.Empty;
        EditDrugCode = "DRG-" + DateTime.Now.ToString("HHmmss");
        EditDrugName = string.Empty;
        EditDrugSpec = string.Empty;
        EditDrugUnit = "盒";
        EditDrugPrice = 0m;
        EditDrugCostPrice = 0m;
        EditDrugAbxLevel = 0;
        EditDrugIsNarcotic = false;
        EditDrugIsSkinTest = false;
    }

    private async Task SaveDrugAsync()
    {
        if (string.IsNullOrWhiteSpace(EditDrugCode) || string.IsNullOrWhiteSpace(EditDrugName))
        {
            _shell?.ShowToast("请填写药品编码和药品名称。", "Warning");
            return;
        }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("/api/v4/catalog/drugs", new
        {
            Id = string.IsNullOrWhiteSpace(_editDrugId) ? null : _editDrugId,
            DrugCode = EditDrugCode,
            DrugName = EditDrugName,
            Spec = EditDrugSpec,
            UnitName = EditDrugUnit,
            Price = EditDrugPrice,
            CostPrice = EditDrugCostPrice,
            AntibioticLevel = EditDrugAbxLevel,
            IsNarcotic = EditDrugIsNarcotic,
            IsSkinTestRequired = EditDrugIsSkinTest,
            IsActive = true
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if ((env?.Code is 200 or 0))
        {
            _shell?.ShowToast(env.Message ?? "药品字典已保存", "Success");
            await LoadDrugsAsync();
        }
        else _shell?.ShowToast(env?.Message ?? "保存失败", "Error");
    }

    private async Task DisableDrugAsync()
    {
        if (string.IsNullOrWhiteSpace(_editDrugId)) return;
        using var http = AuthenticatedHttpClientFactory.Create();
        await http.DeleteAsync($"/api/v4/catalog/drugs/{_editDrugId}");
        _shell?.ShowToast("药品已停用。", "Warning");
        ClearDrugEditor();
        await LoadDrugsAsync();
    }

    private void ClearConsumableEditor()
    {
        _editCsmId = string.Empty;
        EditCsmCode = "CSM-" + DateTime.Now.ToString("HHmmss");
        EditCsmName = string.Empty;
        EditCsmSpec = string.Empty;
        EditCsmUnit = "支";
        EditCsmCategory = "普通医用耗材";
        EditCsmPrice = 0m;
        EditCsmIsSterile = false;
        EditCsmIsHighValue = false;
    }

    private async Task SaveConsumableAsync()
    {
        if (string.IsNullOrWhiteSpace(EditCsmCode) || string.IsNullOrWhiteSpace(EditCsmName))
        {
            _shell?.ShowToast("请填写耗材编码和耗材名称。", "Warning");
            return;
        }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("/api/v4/catalog/consumables", new
        {
            Id = string.IsNullOrWhiteSpace(_editCsmId) ? null : _editCsmId,
            Code = EditCsmCode,
            Name = EditCsmName,
            Spec = EditCsmSpec,
            Unit = EditCsmUnit,
            Category = EditCsmCategory,
            Price = EditCsmPrice,
            IsSterile = EditCsmIsSterile,
            IsHighValue = EditCsmIsHighValue,
            IsActive = true
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if ((env?.Code is 200 or 0))
        {
            _shell?.ShowToast(env.Message ?? "耗材字典已保存", "Success");
            await LoadConsumablesAsync();
        }
        else _shell?.ShowToast(env?.Message ?? "保存失败", "Error");
    }

    private async Task DisableConsumableAsync()
    {
        if (string.IsNullOrWhiteSpace(_editCsmId)) return;
        using var http = AuthenticatedHttpClientFactory.Create();
        await http.DeleteAsync($"/api/v4/catalog/consumables/{_editCsmId}");
        _shell?.ShowToast("耗材已停用。", "Warning");
        ClearConsumableEditor();
        await LoadConsumablesAsync();
    }

    private void ClearBundleEditor()
    {
        _editBundleId = string.Empty;
        EditBundleCode = "PANEL-" + DateTime.Now.ToString("HHmm");
        EditBundleName = string.Empty;
        EditBundlePinyin = string.Empty;
        EditBundleCategory = "LAB_PANEL";
        EditBundleScene = "ALL";
        EditBundleGender = "ALL";
        EditBundleDept = "检验科";
        EditBundleSpecimen = "静脉血";
        EditBundlePackagePrice = 0m;
        EditBundleRemark = string.Empty;
        EditingBundleItems.Clear();
        OnPropertyChanged(nameof(BundleItemsSumPrice));
        ExpandPreviewText = string.Empty;
    }

    private void AddSelectedPriceItemToBundle()
    {
        if (SelectedPriceItemToAdd == null) return;
        EditingBundleItems.Add(new BundleItemRow
        {
            ItemType = "PRICE_ITEM",
            TargetItemId = SelectedPriceItemToAdd.Id,
            ItemCode = SelectedPriceItemToAdd.ItemCode,
            ItemName = SelectedPriceItemToAdd.ItemName,
            Spec = SelectedPriceItemToAdd.Spec,
            UnitName = SelectedPriceItemToAdd.UnitName,
            Quantity = AddItemQty <= 0 ? 1 : AddItemQty,
            UnitPrice = SelectedPriceItemToAdd.UnitPrice,
            ExecDeptName = string.IsNullOrWhiteSpace(AddItemExecDept) ? EditBundleDept : AddItemExecDept.Trim(),
            SortOrder = (EditingBundleItems.Count + 1) * 10
        });
        OnPropertyChanged(nameof(BundleItemsSumPrice));
        if (EditBundlePackagePrice <= 0) EditBundlePackagePrice = BundleItemsSumPrice;
    }

    private void RemoveBundleItem(BundleItemRow? row)
    {
        if (row == null) return;
        EditingBundleItems.Remove(row);
        OnPropertyChanged(nameof(BundleItemsSumPrice));
    }

    private async Task SaveBundleAsync()
    {
        if (string.IsNullOrWhiteSpace(EditBundleName) || EditingBundleItems.Count == 0)
        {
            _shell?.ShowToast("请填写套餐名称并至少添加 1 项底层收费明细。", "Warning");
            return;
        }
        using var http = AuthenticatedHttpClientFactory.Create();
        var payload = new
        {
            Id = string.IsNullOrWhiteSpace(_editBundleId) ? null : _editBundleId,
            BundleCode = EditBundleCode,
            BundleName = EditBundleName.Trim(),
            PinyinCode = EditBundlePinyin,
            BundleCategory = EditBundleCategory,
            ApplicableScene = EditBundleScene,
            GenderLimit = EditBundleGender,
            DeptName = EditBundleDept,
            SpecimenType = EditBundleSpecimen,
            PackagePrice = EditBundlePackagePrice > 0 ? (decimal?)EditBundlePackagePrice : BundleItemsSumPrice,
            IsAllowSplit = false,
            IsActive = true,
            Remark = EditBundleRemark,
            Items = EditingBundleItems.Select(i => new
            {
                i.ItemType, i.TargetItemId, i.ItemCode, i.ItemName, i.Spec,
                i.UnitName, i.Quantity, i.UnitPrice, i.ExecDeptName, i.SortOrder, i.IsRequired
            }).ToList()
        };
        var resp = await http.PostAsJsonAsync("/api/v4/catalog/bundles", payload);
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if ((env?.Code is 200 or 0))
        {
            _shell?.ShowToast(env.Message ?? "组合套餐已保存", "Success");
            await LoadBundlesAsync();
        }
        else _shell?.ShowToast(env?.Message ?? "保存组合套餐失败", "Error");
    }

    private async Task DisableBundleAsync()
    {
        if (string.IsNullOrWhiteSpace(_editBundleId)) return;
        using var http = AuthenticatedHttpClientFactory.Create();
        await http.DeleteAsync($"/api/v4/catalog/bundles/{_editBundleId}");
        _shell?.ShowToast("组合套餐已停用。", "Warning");
        ClearBundleEditor();
        await LoadBundlesAsync();
    }

    private async Task TestExpandBundleAsync()
    {
        if (string.IsNullOrWhiteSpace(_editBundleId))
        {
            _shell?.ShowToast("请先保存套餐再模拟前台拆解。", "Warning");
            return;
        }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsync($"/api/v4/catalog/bundles/{_editBundleId}/expand", null);
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        if ((env?.Code is 200 or 0))
        {
            ExpandPreviewText = $"前台医生开立「{EditBundleName}」→ 后台自动拆解为 {EditingBundleItems.Count} 项底层收费项目（原价合计 ¥{BundleItemsSumPrice:0.00}，套餐执行价 ¥{EditBundlePackagePrice:0.00}）：" +
                                string.Join(" + ", EditingBundleItems.Select(i => $"{i.ItemName}(¥{i.UnitPrice:0.00})"));
            _shell?.ShowToast("组合医嘱拆解校验通过。", "Success");
        }
    }
}
