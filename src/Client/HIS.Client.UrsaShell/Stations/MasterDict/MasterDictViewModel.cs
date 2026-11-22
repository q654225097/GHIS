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

namespace HIS.Client.UrsaShell.Stations.MasterDict;

public sealed class DictCategoryRow
{
    public string Id { get; set; } = string.Empty;
    public string DictType { get; set; } = string.Empty;
    public string DictName { get; set; } = string.Empty;
    public string CategoryGroup { get; set; } = "CLINICAL";
    public short IsSystem { get; set; }
    public string? Description { get; set; }
    public int ActiveItemCount { get; set; }

    public string BadgeText => IsSystem == 1 ? $"内置 · {ActiveItemCount}项" : $"自定义 · {ActiveItemCount}项";
}

public sealed class DictItemRow
{
    public string Id { get; set; } = string.Empty;
    public string DictType { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? PinyinCode { get; set; }
    public int SortOrder { get; set; }
    public short IsDefault { get; set; }
    public short IsActive { get; set; }

    public string StatusDisplay => IsActive == 1 ? "✅ 启用" : "⏸ 已停用";
    public string DefaultDisplay => IsDefault == 1 ? "默认" : "-";
}

public sealed class MasterDictViewModel : ClientModelBase
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly ShellViewModel _shell;

    private string _searchKeyword = string.Empty;
    private DictCategoryRow? _selectedCategory;
    private DictItemRow? _selectedItem;

    // 新增分类表单
    private string _newCatType = string.Empty;
    private string _newCatName = string.Empty;
    private string _newCatGroup = "CUSTOM";
    private string _newCatDesc = string.Empty;

    // 新增/编辑字典项表单
    private string _itemCode = string.Empty;
    private string _itemName = string.Empty;
    private string _itemPinyin = string.Empty;
    private int _itemSortOrder = 10;
    private bool _itemIsDefault;

    public ObservableCollection<DictCategoryRow> Categories { get; } = new();
    public ObservableCollection<DictItemRow> Items { get; } = new();
    public string[] CategoryGroups { get; } = new[] { "BASIC", "CLINICAL", "INPATIENT", "MEDTECH", "FINANCE", "CUSTOM" };

    public string SearchKeyword { get => _searchKeyword; set => SetField(ref _searchKeyword, value); }
    public DictCategoryRow? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetField(ref _selectedCategory, value) && value != null)
            {
                _ = LoadItemsAsync(value.DictType);
            }
        }
    }
    public DictItemRow? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetField(ref _selectedItem, value) && value != null)
            {
                ItemCode = value.ItemCode;
                ItemName = value.ItemName;
                ItemPinyin = value.PinyinCode ?? string.Empty;
                ItemSortOrder = value.SortOrder;
                ItemIsDefault = value.IsDefault == 1;
            }
        }
    }

    public string NewCatType { get => _newCatType; set => SetField(ref _newCatType, value); }
    public string NewCatName { get => _newCatName; set => SetField(ref _newCatName, value); }
    public string NewCatGroup { get => _newCatGroup; set => SetField(ref _newCatGroup, value); }
    public string NewCatDesc { get => _newCatDesc; set => SetField(ref _newCatDesc, value); }

    public string ItemCode { get => _itemCode; set => SetField(ref _itemCode, value); }
    public string ItemName { get => _itemName; set => SetField(ref _itemName, value); }
    public string ItemPinyin { get => _itemPinyin; set => SetField(ref _itemPinyin, value); }
    public int ItemSortOrder { get => _itemSortOrder; set => SetField(ref _itemSortOrder, value); }
    public bool ItemIsDefault { get => _itemIsDefault; set => SetField(ref _itemIsDefault, value); }

    public ICommand RefreshCommand { get; }
    public ICommand SaveCategoryCommand { get; }
    public ICommand SaveItemCommand { get; }
    public ICommand ToggleItemCommand { get; }

    public MasterDictViewModel(WorkbenchModel workbench, ShellViewModel shell)
    {
        _shell = shell;
        RefreshCommand = new RelayCommand(async () => await LoadCategoriesAsync());
        SaveCategoryCommand = new RelayCommand(async () => await SaveCategoryAsync());
        SaveItemCommand = new RelayCommand(async () => await SaveItemAsync());
        ToggleItemCommand = new RelayCommand(async () => await ToggleItemAsync());
        _ = LoadCategoriesAsync();
    }

    private async Task LoadCategoriesAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            string url = $"api/v4/config/dictionaries/categories?keyword={Uri.EscapeDataString(SearchKeyword ?? string.Empty)}";
            var env = await http.GetFromJsonAsync<ClientApiEnvelope<List<DictCategoryRow>>>(url, JsonOpts);
            string? prevType = SelectedCategory?.DictType;
            Categories.Clear();
            if (env?.Data != null) foreach (var c in env.Data) Categories.Add(c);
            SelectedCategory = Categories.FirstOrDefault(c => c.DictType == prevType) ?? Categories.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _shell.ShowToast($"加载主数据字典分类失败：{ex.Message}", "Error");
        }
    }

    private async Task LoadItemsAsync(string dictType)
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var env = await http.GetFromJsonAsync<ClientApiEnvelope<List<DictItemRow>>>($"api/v4/config/dictionaries/items/{Uri.EscapeDataString(dictType)}?includeInactive=true", JsonOpts);
            Items.Clear();
            if (env?.Data != null) foreach (var it in env.Data) Items.Add(it);
        }
        catch (Exception ex)
        {
            _shell.ShowToast($"加载字典项失败：{ex.Message}", "Error");
        }
    }

    private async Task SaveCategoryAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCatType) || string.IsNullOrWhiteSpace(NewCatName))
        {
            _shell.ShowToast("请填写字典分类编码与名称", "Warning");
            return;
        }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/config/dictionaries/categories", new
        {
            DictType = NewCatType,
            DictName = NewCatName,
            CategoryGroup = NewCatGroup,
            Description = NewCatDesc
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "已保存分类", (env?.Code is 200 or 0) ? "Success" : "Error");
        NewCatType = string.Empty; NewCatName = string.Empty; NewCatDesc = string.Empty;
        await LoadCategoriesAsync();
    }

    private async Task SaveItemAsync()
    {
        if (SelectedCategory == null || string.IsNullOrWhiteSpace(ItemCode) || string.IsNullOrWhiteSpace(ItemName))
        {
            _shell.ShowToast("请选择字典分类并填写项编码与项名称", "Warning");
            return;
        }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PostAsJsonAsync("api/v4/config/dictionaries/items", new
        {
            DictType = SelectedCategory.DictType,
            ItemCode,
            ItemName,
            PinyinCode = ItemPinyin,
            SortOrder = ItemSortOrder,
            IsDefault = ItemIsDefault
        });
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "已保存字典项", (env?.Code is 200 or 0) ? "Success" : "Error");
        ItemCode = string.Empty; ItemName = string.Empty; ItemPinyin = string.Empty;
        await LoadItemsAsync(SelectedCategory.DictType);
        await LoadCategoriesAsync();
    }

    private async Task ToggleItemAsync()
    {
        if (SelectedItem == null || SelectedCategory == null)
        {
            _shell.ShowToast("请先选择一条字典项", "Warning");
            return;
        }
        using var http = AuthenticatedHttpClientFactory.Create();
        var resp = await http.PutAsync($"api/v4/config/dictionaries/items/{SelectedItem.Id}/toggle", null);
        var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
        _shell.ShowToast(env?.Message ?? "已切换状态", (env?.Code is 200 or 0) ? "Success" : "Error");
        await LoadItemsAsync(SelectedCategory.DictType);
    }
}
