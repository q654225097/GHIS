using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Services;

namespace HIS.Client.UrsaShell.ViewModels;

public sealed class PharmacyWorkstationViewModel : ClientModelBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private bool _isLoaded;
    private bool _isBusy;
    private string _notice = string.Empty;
    private string _errorMessage = string.Empty;
    private int _selectedTabIndex;
    private PharmacyChoice? _selectedPharmacy;
    private WarehouseChoice? _selectedWarehouse;
    private WarehouseChoice? _targetPharmacy;
    private int _activeDrugCount;
    private int _totalBatchQuantity;
    private int _expiringBatchCount;
    private int _outpatientReadyCount;
    private int _wardOrderCount;
    private int _dispenseCount;
    private string _drugKeyword = string.Empty;
    private DrugRow? _selectedDrug;
    private string _editorCode = string.Empty;
    private string _editorName = string.Empty;
    private string _editorSpec = string.Empty;
    private string _editorUnit = "盒";
    private string _editorRetailPrice = "0.00";
    private string _editorCostPrice = "0.00";
    private int _editorAntibioticLevel;
    private bool _editorNarcotic;
    private bool _editorSkinTest;
    private bool _editorActive = true;
    private string _adjustReason = "药品主数据维护调价";
    private DrugRow? _selectedStockDrug;
    private string _batchNo = string.Empty;
    private string _expiryDate = string.Empty;
    private string _stockInQuantity = "1";
    private string _invoiceNo = string.Empty;
    private string _coldChainTemperature = string.Empty;
    private string _coldChainMaximum = "8";
    private DrugRow? _issueDrug;
    private string _issueQuantity = "1";
    private string _issueRemark = "药库按FEFO发药补货";
    private string _transferRemark = "药房间应急调拨";
    private ReadyPrescriptionRow? _selectedReadyPrescription;
    private DispenseRow? _selectedDispense;
    private string _returnReason = string.Empty;
    private bool _returnPackagingIntact = true;
    private bool _returnColdChainQualified = true;
    private WardOrderRow? _selectedWardOrder;
    private string _wardEncounterFilter = string.Empty;
    private int? _loadedTabIndex;
    private bool _sessionSubscribed;

    public PharmacyWorkstationViewModel()
    {
        _http = new HttpClient { BaseAddress = new Uri(ApiEndpoints.BaseAddress) };
        var today = DateTime.Today;
        _expiryDate = today.AddYears(2).ToString("yyyy-MM-dd");
        DispenseLines.CollectionChanged += (_, _) =>
            OnPropertyChanged(nameof(DispenseLinesEmpty));
        WardOrders.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasNoWardOrders));
            OnPropertyChanged(nameof(WardSelectionSummary));
        };
        ReadyPrescriptionLines.CollectionChanged += (_, _) =>
            OnPropertyChanged(nameof(PrescriptionLineSummary));
        ReadyPrescriptions.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ReadyQueueSummary));
            OnPropertyChanged(nameof(HasNoReadyPrescriptions));
        };
        DispenseOrders.CollectionChanged += (_, _) =>
            OnPropertyChanged(nameof(DispenseSummary));
    }

    public ObservableCollection<PharmacyChoice> PharmacyChoices { get; } = new();
    public ObservableCollection<WarehouseChoice> WarehouseChoices { get; } = new();
    public ObservableCollection<DrugRow> Drugs { get; } = new();
    public ObservableCollection<StockRow> StockRows { get; } = new();
    public ObservableCollection<BatchRow> Batches { get; } = new();
    public ObservableCollection<TransferRow> Transfers { get; } = new();
    public ObservableCollection<ReadyPrescriptionRow> ReadyPrescriptions { get; } = new();
    public ObservableCollection<ReadyPrescriptionLineRow> ReadyPrescriptionLines { get; } = new();
    public ObservableCollection<DispenseRow> DispenseOrders { get; } = new();
    public ObservableCollection<DispenseLineRow> DispenseLines { get; } = new();
    public ObservableCollection<WardOrderRow> WardOrders { get; } = new();

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (SetField(ref _selectedTabIndex, value) && _isLoaded && _loadedTabIndex != value)
            {
                _loadedTabIndex = value;
                _ = LoadSelectedTabSafeAsync(value);
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsNotBusy));
            }
        }
    }

    public bool IsNotBusy => !IsBusy;
    public bool IsAdmin => SessionContext.Current.IsAdmin;
    public bool IsPharmacist => SessionContext.Current.IsPharmacist || IsAdmin;
    public bool IsWarehouseKeeper => SessionContext.Current.IsWarehouseKeeper || IsAdmin;
    public bool CanUseWarehouse => IsPharmacist || IsWarehouseKeeper;
    public bool CanEditDrugs => IsAdmin;
    public bool ShowDrugReadOnly => !IsAdmin;
    public bool DispenseLinesEmpty => DispenseLines.Count == 0;
    public bool HasNoWardOrders => WardOrders.Count == 0;
    public bool HasNoReadyPrescriptions => ReadyPrescriptions.Count == 0;
    public IReadOnlyList<string> AntibioticLevelOptions { get; } = new[] { "非抗菌药", "限制级抗菌药", "特殊使用级抗菌药" };

    public void AttachSession()
    {
        if (_sessionSubscribed)
        {
            return;
        }
        SessionContext.Current.PropertyChanged += Session_PropertyChanged;
        _sessionSubscribed = true;
    }

    public void DetachSession()
    {
        if (!_sessionSubscribed)
        {
            return;
        }
        SessionContext.Current.PropertyChanged -= Session_PropertyChanged;
        _sessionSubscribed = false;
    }

    private void Session_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SessionContext.IsAdmin)
            && e.PropertyName != nameof(SessionContext.IsPharmacist)
            && e.PropertyName != nameof(SessionContext.IsWarehouseKeeper))
        {
            return;
        }
        OnPropertyChanged(nameof(IsAdmin));
        OnPropertyChanged(nameof(IsPharmacist));
        OnPropertyChanged(nameof(IsWarehouseKeeper));
        OnPropertyChanged(nameof(CanUseWarehouse));
        OnPropertyChanged(nameof(CanEditDrugs));
        OnPropertyChanged(nameof(ShowDrugReadOnly));
        OnPropertyChanged(nameof(OperatorText));
        if (!SessionContext.Current.IsLoggedIn)
        {
            return;
        }
        _ = ReloadForSessionAsync();
    }

    private async Task ReloadForSessionAsync()
    {
        _isLoaded = false;
        _loadedTabIndex = null;
        var tabIndex = SelectedTabIndex;
        await ReloadAsync(selectDefaults: true);
        SelectedTabIndex = tabIndex;
        await LoadSelectedTabSafeAsync(tabIndex);
    }

    public string Notice
    {
        get => _notice;
        private set
        {
            if (SetField(ref _notice, value))
            {
                OnPropertyChanged(nameof(HasNotice));
            }
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasNotice => !string.IsNullOrWhiteSpace(Notice);
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public PharmacyChoice? SelectedPharmacy
    {
        get => _selectedPharmacy;
        set
        {
            if (SetField(ref _selectedPharmacy, value))
            {
                OnPropertyChanged(nameof(CurrentPharmacyText));
                OnPropertyChanged(nameof(CanDispenseSelected));
                OnPropertyChanged(nameof(CanReturnSelected));
            }
        }
    }

    public WarehouseChoice? SelectedWarehouse
    {
        get => _selectedWarehouse;
        set
        {
            if (SetField(ref _selectedWarehouse, value))
            {
                OnPropertyChanged(nameof(CurrentWarehouseText));
            }
        }
    }

    public WarehouseChoice? TargetPharmacy
    {
        get => _targetPharmacy;
        set => SetField(ref _targetPharmacy, value);
    }

    public string CurrentPharmacyText => SelectedPharmacy == null ? "未选择药房" : SelectedPharmacy.Name;
    public string CurrentWarehouseText => SelectedWarehouse == null ? "未选择库房" : SelectedWarehouse.Name;
    public string OperatorText => SessionContext.Current.DisplayName.Length == 0
        ? SessionContext.Current.UserName
        : SessionContext.Current.DisplayName;

    public int ActiveDrugCount { get => _activeDrugCount; private set => SetField(ref _activeDrugCount, value); }
    public int TotalBatchQuantity { get => _totalBatchQuantity; private set => SetField(ref _totalBatchQuantity, value); }
    public int ExpiringBatchCount { get => _expiringBatchCount; private set => SetField(ref _expiringBatchCount, value); }
    public int OutpatientReadyCount { get => _outpatientReadyCount; private set => SetField(ref _outpatientReadyCount, value); }
    public int WardOrderCount { get => _wardOrderCount; private set => SetField(ref _wardOrderCount, value); }
    public int DispenseCount { get => _dispenseCount; private set => SetField(ref _dispenseCount, value); }

    public string DrugKeyword
    {
        get => _drugKeyword;
        set => SetField(ref _drugKeyword, value ?? string.Empty);
    }

    public DrugRow? SelectedDrug
    {
        get => _selectedDrug;
        set
        {
            if (!SetField(ref _selectedDrug, value))
            {
                return;
            }
            if (value != null)
            {
                EditorCode = value.DrugCode;
                EditorName = value.DrugName;
                EditorSpec = value.Spec;
                EditorUnit = value.UnitName;
                EditorRetailPrice = value.Price.ToString("0.00", CultureInfo.InvariantCulture);
                EditorCostPrice = value.CostPrice.ToString("0.00", CultureInfo.InvariantCulture);
                EditorAntibioticLevel = value.AntibioticLevel;
                EditorNarcotic = value.IsNarcotic;
                EditorSkinTest = value.IsSkinTestRequired;
                EditorActive = value.IsActive;
            }
            OnPropertyChanged(nameof(EditorTitle));
            OnPropertyChanged(nameof(SelectedDrugText));
            OnPropertyChanged(nameof(EditorActive));
            OnPropertyChanged(nameof(EditorNarcotic));
            OnPropertyChanged(nameof(EditorSkinTest));
        }
    }

    public string EditorCode { get => _editorCode; set => SetField(ref _editorCode, value ?? string.Empty); }
    public string EditorName { get => _editorName; set => SetField(ref _editorName, value ?? string.Empty); }
    public string EditorSpec { get => _editorSpec; set => SetField(ref _editorSpec, value ?? string.Empty); }
    public string EditorUnit { get => _editorUnit; set => SetField(ref _editorUnit, value ?? string.Empty); }
    public string EditorRetailPrice { get => _editorRetailPrice; set => SetField(ref _editorRetailPrice, value ?? string.Empty); }
    public string EditorCostPrice { get => _editorCostPrice; set => SetField(ref _editorCostPrice, value ?? string.Empty); }
    public int EditorAntibioticLevel
    {
        get => _editorAntibioticLevel;
        set
        {
            if (SetField(ref _editorAntibioticLevel, value))
            {
                OnPropertyChanged(nameof(EditorAntibioticText));
            }
        }
    }
    public bool EditorNarcotic { get => _editorNarcotic; set => SetField(ref _editorNarcotic, value); }
    public bool EditorSkinTest { get => _editorSkinTest; set => SetField(ref _editorSkinTest, value); }
    public bool EditorActive { get => _editorActive; set => SetField(ref _editorActive, value); }
    public string AdjustReason { get => _adjustReason; set => SetField(ref _adjustReason, value ?? string.Empty); }
    public string EditorTitle => SelectedDrug == null ? "新建药品" : "编辑药品";
    public string SelectedDrugText => SelectedDrug == null ? "尚未选择药品" : SelectedDrug.DrugName + " · " + SelectedDrug.DrugCode;
    public string EditorAntibioticText => EditorAntibioticLevel switch
    {
        1 => "限制级抗菌药",
        2 => "特殊使用级抗菌药",
        _ => "非抗菌药"
    };
    public int EditorAntibioticLevelIndex
    {
        get => Math.Clamp(EditorAntibioticLevel, 0, 2);
        set => EditorAntibioticLevel = Math.Clamp(value, 0, 2);
    }

    public DrugRow? SelectedStockDrug
    {
        get => _selectedStockDrug;
        set => SetField(ref _selectedStockDrug, value);
    }

    public string BatchNo { get => _batchNo; set => SetField(ref _batchNo, value ?? string.Empty); }
    public string ExpiryDate
    {
        get => _expiryDate;
        set
        {
            if (SetField(ref _expiryDate, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(ExpiryDateDateTime));
            }
        }
    }

    public DateTime? ExpiryDateDateTime
    {
        get => DateTime.TryParse(ExpiryDate, out var date) ? date.Date : DateTime.Today.AddYears(2);
        set
        {
            var next = (value ?? DateTime.Today.AddYears(2)).ToString("yyyy-MM-dd");
            if (ExpiryDate != next)
            {
                ExpiryDate = next;
            }
        }
    }
    public string StockInQuantity { get => _stockInQuantity; set => SetField(ref _stockInQuantity, value ?? string.Empty); }
    public string InvoiceNo { get => _invoiceNo; set => SetField(ref _invoiceNo, value ?? string.Empty); }
    public string ColdChainTemperature { get => _coldChainTemperature; set => SetField(ref _coldChainTemperature, value ?? string.Empty); }
    public string ColdChainMaximum { get => _coldChainMaximum; set => SetField(ref _coldChainMaximum, value ?? string.Empty); }

    public DrugRow? IssueDrug
    {
        get => _issueDrug;
        set => SetField(ref _issueDrug, value);
    }

    public string IssueQuantity { get => _issueQuantity; set => SetField(ref _issueQuantity, value ?? string.Empty); }
    public string IssueRemark { get => _issueRemark; set => SetField(ref _issueRemark, value ?? string.Empty); }
    public string TransferRemark { get => _transferRemark; set => SetField(ref _transferRemark, value ?? string.Empty); }

    public ReadyPrescriptionRow? SelectedReadyPrescription
    {
        get => _selectedReadyPrescription;
        set
        {
            if (SetField(ref _selectedReadyPrescription, value))
            {
                OnPropertyChanged(nameof(CanDispenseSelected));
                OnPropertyChanged(nameof(SelectedPrescriptionText));
                if (value != null)
                {
                    _ = LoadPrescriptionLinesAsync(value);
                }
                else
                {
                    ReadyPrescriptionLines.Clear();
                    OnPropertyChanged(nameof(PrescriptionLineSummary));
                }
            }
        }
    }

    public string SelectedPrescriptionText => SelectedReadyPrescription == null
        ? "尚未选择门诊处方"
        : SelectedReadyPrescription.PrescriptionNo + " · " + SelectedReadyPrescription.PatientName;

    public bool CanDispenseSelected =>
        SelectedReadyPrescription?.CanDispense == true
        && !string.IsNullOrWhiteSpace(SelectedPharmacy?.Id);

    public DispenseRow? SelectedDispense
    {
        get => _selectedDispense;
        set
        {
            if (SetField(ref _selectedDispense, value))
            {
                OnPropertyChanged(nameof(CanReturnSelected));
                OnPropertyChanged(nameof(SelectedDispenseText));
            }
        }
    }

    public bool CanReturnSelected =>
        SelectedDispense != null
        && SelectedDispense.Status == 1
        && !SelectedDispense.IsReturned
        && !string.IsNullOrWhiteSpace(SelectedPharmacy?.Id);

    public string SelectedDispenseText => SelectedDispense == null
        ? "尚未选择发药单"
        : SelectedDispense.DispenseNo + " · " + SelectedDispense.PatientLabel;
    public string ReturnReason { get => _returnReason; set => SetField(ref _returnReason, value ?? string.Empty); }
    public bool ReturnPackagingIntact { get => _returnPackagingIntact; set => SetField(ref _returnPackagingIntact, value); }
    public bool ReturnColdChainQualified { get => _returnColdChainQualified; set => SetField(ref _returnColdChainQualified, value); }

    public WardOrderRow? SelectedWardOrder
    {
        get => _selectedWardOrder;
        set => SetField(ref _selectedWardOrder, value);
    }

    public string WardEncounterFilter { get => _wardEncounterFilter; set => SetField(ref _wardEncounterFilter, value ?? string.Empty); }
    public string WardQueueSummary => WardOrders.Count == 0
        ? "当前住院药房没有待发药医嘱"
        : $"共 {WardOrders.Count} 条待发药医嘱 · 涉及 {WardOrders.Select(x => x.EncounterId).Distinct().Count()} 位住院患者";
    public string ReadyQueueSummary => ReadyPrescriptions.Count == 0
        ? "当前门诊药房没有待发药处方"
        : $"共 {ReadyPrescriptions.Count} 张门诊处方 · 可发药 {ReadyPrescriptions.Count(x => x.CanDispense)} 张";
    public string DispenseSummary => DispenseOrders.Count == 0
        ? "当前药房没有发药记录"
        : $"共 {DispenseOrders.Count} 张发药单 · 可退药 {DispenseOrders.Count(x => x.Status == 1 && !x.IsReturned)} 张";
    public string PrescriptionLineSummary => ReadyPrescriptionLines.Count == 0
        ? "暂无处方明细"
        : $"共 {ReadyPrescriptionLines.Count} 项药品 · 金额 ¥{SelectedReadyPrescription?.TotalAmount:0.00}";
    public string WardSelectionSummary => WardOrders.Count == 0
        ? "暂无住院药品医嘱"
        : $"已选 {WardOrders.Count(x => x.IsSelected && x.Quantity > 0)} 条 · 待发 {WardOrders.Count} 条";
    public string TransferSummary => Transfers.Count == 0
        ? "当前没有调拨台账"
        : $"共 {Transfers.Count} 笔出库/调拨 · 调拨数量 {Transfers.Sum(x => x.QuantityValue)}";

    public async Task EnsureLoadedAsync()
    {
        if (_isLoaded || IsBusy)
        {
            return;
        }
        await ReloadAsync(selectDefaults: true);
    }

    private async Task LoadSelectedTabSafeAsync(int tabIndex)
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        try
        {
            await LoadSelectedTabAsync(tabIndex);
        }
        catch (Exception ex)
        {
            SetError(ex, "加载药房页签失败");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadSelectedTabAsync(int tabIndex)
    {
        switch (tabIndex)
        {
            case 1:
                await LoadInventoryAsync(SelectedWarehouse?.Id ?? GetDefaultWarehouseId());
                break;
            case 2:
                await LoadInventoryAsync(SelectedWarehouse?.Id ?? GetDefaultWarehouseId());
                await LoadTransfersAsync();
                break;
            case 3:
                await LoadDispenseOrdersAsync();
                break;
            case 4:
                await LoadOutpatientAsync();
                break;
            case 5:
                await LoadWardAsync();
                break;
        }
    }

    public async Task RefreshAsync()
    {
        await ReloadAsync(selectDefaults: false);
    }

    public async Task RefreshPharmacyScopeAsync()
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        try
        {
            ClearFeedback();
            SelectedReadyPrescription = null;
            SelectedDispense = null;
            DispenseLines.Clear();
            await LoadOutpatientAsync();
            await LoadDispenseOrdersAsync();
            await LoadWardAsync();
            Notice = "已切换至 " + CurrentPharmacyText + "。";
        }
        catch (Exception ex)
        {
            SetError(ex, "切换药房失败");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RefreshActiveTabAsync()
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        try
        {
            ClearFeedback();
            if (SelectedTabIndex == 0)
            {
                await LoadDrugsAsync();
            }
            else
            {
                await LoadSelectedTabAsync(SelectedTabIndex);
            }
            Notice = "数据已从服务端刷新。";
        }
        catch (Exception ex)
        {
            SetError(ex, "刷新药房数据失败");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void NewDrug()
    {
        SelectedDrug = null;
        EditorCode = string.Empty;
        EditorName = string.Empty;
        EditorSpec = string.Empty;
        EditorUnit = "盒";
        EditorRetailPrice = "0.00";
        EditorCostPrice = "0.00";
        EditorAntibioticLevel = 0;
        EditorNarcotic = false;
        EditorSkinTest = false;
        EditorActive = true;
        AdjustReason = "药品主数据维护";
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(SelectedDrugText));
        ClearFeedback();
    }

    public async Task SaveDrugAsync()
    {
        if (!IsAdmin)
        {
            SetValidationError("仅系统管理员可以维护药品基础信息。");
            return;
        }
        if (string.IsNullOrWhiteSpace(EditorCode) || string.IsNullOrWhiteSpace(EditorName))
        {
            SetValidationError("药品编码和药品名称不能为空。");
            return;
        }
        if (!TryDecimal(EditorRetailPrice, out var retail) || !TryDecimal(EditorCostPrice, out var cost) || retail < 0 || cost < 0)
        {
            SetValidationError("零售价和成本价必须为不小于 0 的数字。");
            return;
        }
        var editingId = SelectedDrug?.Id;
        var priceChanged = SelectedDrug != null &&
            (Math.Abs(SelectedDrug.Price - retail) > 0.0001m || Math.Abs(SelectedDrug.CostPrice - cost) > 0.0001m);
        if (priceChanged && string.IsNullOrWhiteSpace(AdjustReason))
        {
            SetValidationError("调价时必须填写调价依据或原因。");
            return;
        }

        var request = new
        {
            id = editingId,
            drugCode = EditorCode.Trim(),
            drugName = EditorName.Trim(),
            spec = EditorSpec.Trim(),
            unitName = EditorUnit.Trim(),
            price = retail,
            costPrice = cost,
            antibioticLevel = EditorAntibioticLevel,
            isNarcotic = EditorNarcotic ? (short)1 : (short)0,
            isSkinTestRequired = EditorSkinTest ? (short)1 : (short)0,
            isActive = EditorActive ? (short)1 : (short)0,
            adjustReason = AdjustReason.Trim()
        };
        await ExecuteMutationAsync(
            "保存药品基础信息",
            async () =>
            {
                using var response = await PostAsync("/api/v4/inventory/drugs", request);
                await EnsureSuccessfulAsync(response);
                await LoadDrugsAsync();
            },
            editingId == null ? "药品基础信息已录入。" : "药品基础信息与价格已更新。");
    }

    public async Task ToggleDrugAsync()
    {
        if (!IsAdmin || SelectedDrug == null)
        {
            SetValidationError("请选择药品，并由系统管理员启停药品。");
            return;
        }
        var target = SelectedDrug;
        var nextActive = !target.IsActive;
        await ExecuteMutationAsync(
            "启停药品",
            async () =>
            {
                using var response = await PostAsync(
                    "/api/v4/inventory/drugs/" + Uri.EscapeDataString(target.Id) + "/toggle-active",
                    new { isActive = nextActive });
                await EnsureSuccessfulAsync(response);
                await LoadDrugsAsync();
                EditorActive = nextActive;
            },
            nextActive ? "药品已恢复启用。" : "药品已停用，历史数据保留。");
    }

    public async Task StockInAsync()
    {
        if (!CanUseWarehouse)
        {
            SetValidationError("当前身份没有药品入库权限，请使用药库或药房药师账号操作。");
            return;
        }
        if (SelectedStockDrug == null)
        {
            SetValidationError("请先选择入库药品。");
            return;
        }
        if (SelectedWarehouse == null)
        {
            SetValidationError("请选择采购入库库房。");
            return;
        }
        if (string.IsNullOrWhiteSpace(BatchNo))
        {
            SetValidationError("请填写生产批号。");
            return;
        }
        if (!DateTime.TryParse(ExpiryDate, out var expiry) || expiry.Date < DateTime.Today)
        {
            SetValidationError("有效期必须是不早于今天的有效日期。");
            return;
        }
        if (!int.TryParse(StockInQuantity, out var quantity) || quantity <= 0)
        {
            SetValidationError("入库数量必须是大于 0 的整数。");
            return;
        }
        decimal? temperature = TryDecimal(ColdChainTemperature, out var temp) ? temp : null;
        decimal? maximum = TryDecimal(ColdChainMaximum, out var max) ? max : null;
        var warehouseId = SelectedWarehouse?.Id ?? GetDefaultWarehouseId();
        var requestKey = Guid.NewGuid().ToString("N");
        await ExecuteMutationAsync(
            "采购入库",
            async () =>
            {
                using var response = await PostAsync(
                    "/api/v4/inventory/batches/stock-in",
                    new
                    {
                        warehouseOrPharmacyId = warehouseId,
                        drugId = SelectedStockDrug.Id,
                        batchNo = BatchNo.Trim(),
                        expiryDateInt = int.Parse(expiry.ToString("yyyyMMdd"), CultureInfo.InvariantCulture),
                        quantity,
                        invoiceNo = string.IsNullOrWhiteSpace(InvoiceNo) ? null : InvoiceNo.Trim(),
                        coldChainTempCelsius = temperature,
                        maxAllowedColdChainTempCelsius = maximum,
                        requestKey
                    });
                await EnsureSuccessfulAsync(response);
                BatchNo = string.Empty;
                StockInQuantity = "1";
                InvoiceNo = string.Empty;
                ColdChainTemperature = string.Empty;
                await LoadInventoryAsync(warehouseId);
            },
            $"入库完成：{SelectedStockDrug.DrugName} +{quantity}{SelectedStockDrug.UnitName}。");
    }

    public async Task WarehouseIssueAsync()
    {
        if (!CanUseWarehouse)
        {
            SetValidationError("当前身份没有库房发药权限，请使用药库或药房账号操作。");
            return;
        }
        if (SelectedWarehouse == null || TargetPharmacy == null || IssueDrug == null)
        {
            SetValidationError("请选择药库、目标药房和发出药品。");
            return;
        }
        if (!int.TryParse(IssueQuantity, out var quantity) || quantity <= 0)
        {
            SetValidationError("出库数量必须是大于 0 的整数。");
            return;
        }
        if (SelectedWarehouse.Id == TargetPharmacy.Id)
        {
            SetValidationError("药库和目标药房不能相同。");
            return;
        }
        var source = SelectedWarehouse.Id;
        var requestKey = Guid.NewGuid().ToString("N");
        await ExecuteMutationAsync(
            "药库发药",
            async () =>
            {
                using var response = await PostAsync(
                    "/api/v4/inventory/warehouse-issue",
                    new
                    {
                        sourceWarehouseId = source,
                        targetWarehouseId = TargetPharmacy.Id,
                        drugId = IssueDrug.Id,
                        quantity,
                        transferType = "WAREHOUSE_ISSUE",
                        remark = string.IsNullOrWhiteSpace(IssueRemark) ? "药库向药房发药" : IssueRemark.Trim(),
                        requestKey
                    });
                await EnsureSuccessfulAsync(response);
                await LoadInventoryAsync(source);
                await LoadTransfersAsync();
            },
            $"药库发药完成：{IssueDrug.DrugName} × {quantity} 已发往 {TargetPharmacy.Name}。");
    }

    public async Task TransferAsync()
    {
        if (!CanUseWarehouse)
        {
            SetValidationError("当前身份没有调拨权限，请使用药库或药房账号操作。");
            return;
        }
        if (SelectedWarehouse == null || TargetPharmacy == null || IssueDrug == null)
        {
            SetValidationError("请选择调出库房、调入库房和药品。");
            return;
        }
        if (SelectedWarehouse.Id == TargetPharmacy.Id)
        {
            SetValidationError("调出库房和调入库房不能相同。");
            return;
        }
        if (!int.TryParse(IssueQuantity, out var quantity) || quantity <= 0)
        {
            SetValidationError("调拨数量必须是大于 0 的整数。");
            return;
        }
        var source = SelectedWarehouse.Id;
        var requestKey = Guid.NewGuid().ToString("N");
        await ExecuteMutationAsync(
            "药品调拨",
            async () =>
            {
                using var response = await PostAsync(
                    "/api/v4/inventory/transfers",
                    new
                    {
                        sourceWarehouseId = source,
                        targetWarehouseId = TargetPharmacy.Id,
                        drugId = IssueDrug.Id,
                        quantity,
                        transferType = "INTER_WAREHOUSE",
                        remark = string.IsNullOrWhiteSpace(TransferRemark) ? "多库房药品调拨" : TransferRemark.Trim(),
                        requestKey
                    });
                await EnsureSuccessfulAsync(response);
                await LoadInventoryAsync(source);
                await LoadTransfersAsync();
            },
            $"药品调拨完成：{IssueDrug.DrugName} × {quantity}。");
    }

    public async Task DispenseOutpatientAsync()
    {
        if (!CanDispenseSelected || SelectedReadyPrescription == null || SelectedPharmacy == null)
        {
            SetValidationError("只有已收费、药师审方通过且当前药房可办理的门诊处方才能发药。");
            return;
        }
        var target = SelectedReadyPrescription;
        var pharmacyId = SelectedPharmacy.Id;
        await ExecuteMutationAsync(
            "门诊发药",
            async () =>
            {
                using var response = await PostAsync(
                    "/api/v4/pharmacy/dispense",
                    new
                    {
                        pharmacyId,
                        prescriptionId = target.PrescriptionId,
                        patientId = target.PatientId,
                        pharmacistId = SessionContext.Current.UserId,
                        requestKey = Guid.NewGuid().ToString("N")
                    });
                await EnsureSuccessfulAsync(response);
                await LoadOutpatientAsync();
                await LoadDispenseOrdersAsync();
            },
            $"门诊发药完成：{target.PrescriptionNo}，订单已关闭，不允许二次发药。");
    }

    public async Task SelectDispenseAsync(DispenseRow? row)
    {
        SelectedDispense = row;
        if (row == null)
        {
            DispenseLines.Clear();
            return;
        }
        await LoadDispenseLinesAsync(row);
    }

    private async Task LoadDispenseLinesAsync(DispenseRow row)
    {
        DispenseLines.Clear();
        try
        {
            var lines = await GetAsync<List<PhmDispenseOrderLineDto>>(
                "/api/v4/pharmacy/dispense/" + Uri.EscapeDataString(row.Id) + "/lines");
            foreach (var line in lines ?? new List<PhmDispenseOrderLineDto>())
            {
                DispenseLines.Add(new DispenseLineRow(line));
            }
        }
        catch (Exception ex)
        {
            SetError(ex, "加载发药明细失败");
        }
    }

    public async Task ConfirmReturnAsync()
    {
        if (!CanReturnSelected || SelectedDispense == null)
        {
            SetValidationError("请选择当前药房已完成发药、尚未退药的发药单。");
            return;
        }
        if (string.IsNullOrWhiteSpace(ReturnReason))
        {
            SetValidationError("请填写退药原因。");
            return;
        }
        if (!ReturnPackagingIntact || !ReturnColdChainQualified)
        {
            SetValidationError("包装完整性或冷链温控未通过质检，不能回补库存。");
            return;
        }
        var target = SelectedDispense;
        await ExecuteMutationAsync(
            "退药入库",
            async () =>
            {
                using var response = await PostAsync(
                    "/api/v4/pharmacy/confirm-return",
                    new
                    {
                        dispenseOrderId = target.Id,
                        pharmacistId = SessionContext.Current.UserId,
                        reason = ReturnReason.Trim(),
                        isPackagingIntact = ReturnPackagingIntact,
                        isColdChainQualified = ReturnColdChainQualified,
                        requestKey = Guid.NewGuid().ToString("N")
                    });
                await EnsureSuccessfulAsync(response);
                ReturnReason = string.Empty;
                await LoadDispenseOrdersAsync();
                await LoadOutpatientAsync();
            },
            $"退药完成：{target.DispenseNo}，库存已回补原批次。");
    }

    public async Task WardDispenseAsync()
    {
        if (SelectedPharmacy == null)
        {
            SetValidationError("请选择住院药房。");
            return;
        }
        var selected = WardOrders.Where(x => x.IsSelected && x.Quantity > 0).ToList();
        if (selected.Count == 0)
        {
            SetValidationError("请勾选住院医嘱并填写大于 0 的发药数量。");
            return;
        }
        var encounterGroups = selected.GroupBy(x => x.EncounterId).ToList();
        var pharmacyId = SelectedPharmacy.Id;
        await ExecuteMutationAsync(
            "住院汇总发药",
            async () =>
            {
                foreach (var group in encounterGroups)
                {
                    var encounterId = group.Key;
                    var lines = group.Select(x => new { medicalOrderId = x.MedicalOrderId, quantity = x.Quantity }).ToList();
                    using var response = await PostAsync(
                        "/api/v4/pharmacy/ward-dispense",
                        new
                        {
                            pharmacyId,
                            encounterId,
                            lines,
                            requestKey = Guid.NewGuid().ToString("N")
                        });
                    await EnsureSuccessfulAsync(response);
                }
                await LoadWardAsync();
                await LoadDispenseOrdersAsync();
            },
            $"住院汇总发药完成：共 {encounterGroups.Count} 位患者 {selected.Count} 条医嘱。");
    }

    public async Task FilterWardAsync()
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        try
        {
            ClearFeedback();
            await LoadWardAsync();
            Notice = "住院发药队列已刷新。";
        }
        catch (Exception ex)
        {
            SetError(ex, "加载住院发药队列失败");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadAsync(bool selectDefaults)
    {
        IsBusy = true;
        try
        {
            ClearFeedback();
            var previousPharmacyId = selectDefaults ? null : SelectedPharmacy?.Id;
            var previousWarehouseId = selectDefaults ? null : SelectedWarehouse?.Id;
            await LoadDepartmentsAsync(previousPharmacyId, previousWarehouseId);
            await LoadDrugsAsync();
            await LoadInventoryAsync(SelectedWarehouse?.Id ?? GetDefaultWarehouseId());
            await LoadTransfersAsync();
            await LoadOutpatientAsync();
            await LoadDispenseOrdersAsync();
            await LoadWardAsync();
            _isLoaded = true;
            _loadedTabIndex = SelectedTabIndex;
            Notice = "药房工作区已连接服务端。";
        }
        catch (Exception ex)
        {
            SetError(ex, "加载药房工作区失败");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadDepartmentsAsync(string? previousPharmacyId, string? previousWarehouseId)
    {
        var pharmacies = await GetAsync<List<AuthorizedPharmacyDto>>("/api/v4/pharmacy/authorized-pharmacies");
        var warehouses = await GetAsync<List<DepartmentDto>>("/api/v4/platform/departments?category=WAREHOUSE");
        var isAdmin = SessionContext.Current.IsAdmin;
        var activeDeptId = SessionContext.Current.ActiveDeptId;
        var allowedPharmacies = (pharmacies ?? new List<AuthorizedPharmacyDto>())
            .ToList();

        PharmacyChoices.Clear();
        foreach (var department in allowedPharmacies)
        {
            PharmacyChoices.Add(new PharmacyChoice(department.Id, department.DeptName, department.DeptCategory));
        }

        WarehouseChoices.Clear();
        foreach (var department in warehouses ?? new List<DepartmentDto>())
        {
            WarehouseChoices.Add(new WarehouseChoice(department.Id, department.DeptName, department.DeptCategory));
        }
        foreach (var pharmacy in allowedPharmacies)
        {
            if (WarehouseChoices.All(x => x.Id != pharmacy.Id))
            {
                WarehouseChoices.Add(new WarehouseChoice(pharmacy.Id, pharmacy.DeptName, pharmacy.DeptCategory));
            }
        }

        SelectedPharmacy = PharmacyChoices.FirstOrDefault(x => x.Id == previousPharmacyId)
            ?? PharmacyChoices.FirstOrDefault(x => x.Id == activeDeptId)
            ?? PharmacyChoices.FirstOrDefault();
        SelectedWarehouse = WarehouseChoices.FirstOrDefault(x => x.Id == previousWarehouseId)
            ?? WarehouseChoices.FirstOrDefault(x => x.Category == "WAREHOUSE" && x.Id == "DEPT-WH-01")
            ?? WarehouseChoices.FirstOrDefault(x => x.Category == "WAREHOUSE")
            ?? WarehouseChoices.FirstOrDefault();
        TargetPharmacy = WarehouseChoices.FirstOrDefault(x => x.Id == "DEPT-PHM-01")
            ?? WarehouseChoices.FirstOrDefault(x => x.Category == "PHARMACY");
        OnPropertyChanged(nameof(CurrentPharmacyText));
        OnPropertyChanged(nameof(CurrentWarehouseText));
    }

    private async Task LoadDrugsAsync()
    {
        var uri = "/api/v4/inventory/drugs?activeOnly=false";
        if (!string.IsNullOrWhiteSpace(DrugKeyword))
        {
            uri += "&keyword=" + Uri.EscapeDataString(DrugKeyword.Trim());
        }
        var drugs = await GetAsync<List<InvDrugDto>>(uri);
        var selectedId = SelectedDrug?.Id;
        Drugs.Clear();
        foreach (var drug in drugs ?? new List<InvDrugDto>())
        {
            Drugs.Add(new DrugRow(drug));
        }
        ActiveDrugCount = Drugs.Count(x => x.IsActive);
        SelectedDrug = Drugs.FirstOrDefault(x => x.Id == selectedId);
        var previousStockDrugId = SelectedStockDrug?.Id;
        var previousIssueDrugId = IssueDrug?.Id;
        SelectedStockDrug = Drugs.FirstOrDefault(x => x.Id == previousStockDrugId)
            ?? Drugs.FirstOrDefault(x => x.IsActive);
        IssueDrug = Drugs.FirstOrDefault(x => x.Id == previousIssueDrugId)
            ?? Drugs.FirstOrDefault(x => x.IsActive);
    }

    public async Task SearchDrugsAsync()
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        try
        {
            ClearFeedback();
            await LoadDrugsAsync();
            Notice = $"药品列表已刷新，共 {Drugs.Count} 条。";
        }
        catch (Exception ex)
        {
            SetError(ex, "查询药品失败");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RefreshStockAsync()
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        try
        {
            ClearFeedback();
            await LoadInventoryAsync(SelectedWarehouse?.Id ?? GetDefaultWarehouseId());
            Notice = "库存与批次账目已刷新。";
        }
        catch (Exception ex)
        {
            SetError(ex, "刷新库存失败");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadInventoryAsync(string? warehouseId)
    {
        var stock = await GetAsync<List<StockSummaryDto>>(
            "/api/v4/inventory/stock-summary?warehouseOrPharmacyId=" + Uri.EscapeDataString(warehouseId ?? string.Empty));
        var batches = await GetAsync<List<InvDrugBatchDto>>(
            "/api/v4/inventory/batches?warehouseOrPharmacyId=" + Uri.EscapeDataString(warehouseId ?? string.Empty));
        StockRows.Clear();
        foreach (var row in stock ?? new List<StockSummaryDto>())
        {
            StockRows.Add(new StockRow(row));
        }
        Batches.Clear();
        var today = DateTime.Today;
        foreach (var batch in batches ?? new List<InvDrugBatchDto>())
        {
            Batches.Add(new BatchRow(batch, Drugs));
        }
        TotalBatchQuantity = Batches.Sum(x => x.Quantity);
        ExpiringBatchCount = Batches.Count(x => x.IsExpiringSoon);
    }

    private async Task LoadTransfersAsync()
    {
        var transfers = await GetAsync<List<TransferDto>>("/api/v4/inventory/transfers");
        Transfers.Clear();
        foreach (var transfer in (transfers ?? new List<TransferDto>()).Take(80))
        {
            Transfers.Add(new TransferRow(transfer));
        }
        OnPropertyChanged(nameof(TransferSummary));
    }

    private async Task LoadOutpatientAsync()
    {
        if (SelectedPharmacy == null)
        {
            ReadyPrescriptions.Clear();
            return;
        }
        var queue = await GetAsync<List<ReadyPrescriptionDto>>(
            "/api/v4/pharmacy/ready-queue?includeClosed=true&pharmacyId=" + Uri.EscapeDataString(SelectedPharmacy.Id));
        var previousId = SelectedReadyPrescription?.PrescriptionId;
        ReadyPrescriptions.Clear();
        foreach (var row in queue ?? new List<ReadyPrescriptionDto>())
        {
            ReadyPrescriptions.Add(new ReadyPrescriptionRow(row));
        }
        OutpatientReadyCount = ReadyPrescriptions.Count(x => x.CanDispense);
        SelectedReadyPrescription = ReadyPrescriptions.FirstOrDefault(x => x.PrescriptionId == previousId);
        OnPropertyChanged(nameof(ReadyQueueSummary));
    }

    private async Task LoadDispenseOrdersAsync()
    {
        if (SelectedPharmacy == null)
        {
            DispenseOrders.Clear();
            return;
        }
        var orders = await GetAsync<List<PhmDispenseOrderDto>>(
            "/api/v4/pharmacy/dispense-list?pharmacyId=" + Uri.EscapeDataString(SelectedPharmacy.Id));
        var previousId = SelectedDispense?.Id;
        DispenseOrders.Clear();
        DispenseLines.Clear();
        foreach (var order in orders ?? new List<PhmDispenseOrderDto>())
        {
            DispenseOrders.Add(new DispenseRow(order));
        }
        DispenseCount = DispenseOrders.Count;
        SelectedDispense = DispenseOrders.FirstOrDefault(x => x.Id == previousId);
        if (SelectedDispense != null)
        {
            await LoadDispenseLinesAsync(SelectedDispense);
        }
        OnPropertyChanged(nameof(DispenseSummary));
    }

    private async Task LoadPrescriptionLinesAsync(ReadyPrescriptionRow prescription)
    {
        ClearFeedback();
        ReadyPrescriptionLines.Clear();
        try
        {
            var items = await GetAsync<List<PrescriptionLineDto>>(
                "/api/v4/pharmacy/prescriptions/" + Uri.EscapeDataString(prescription.PrescriptionId) +
                "/items?pharmacyId=" + Uri.EscapeDataString(SelectedPharmacy?.Id ?? string.Empty));
            if (!ReferenceEquals(SelectedReadyPrescription, prescription))
            {
                OnPropertyChanged(nameof(PrescriptionLineSummary));
                return;
            }
            foreach (var item in items ?? new List<PrescriptionLineDto>())
            {
                ReadyPrescriptionLines.Add(new ReadyPrescriptionLineRow(item));
            }
            OnPropertyChanged(nameof(PrescriptionLineSummary));
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(SelectedReadyPrescription, prescription))
            {
                SetError(ex, "查询处方明细失败");
            }
        }
    }

    private async Task LoadWardAsync()
    {
        var previousSelection = WardOrders
            .GroupBy(x => x.MedicalOrderId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IsSelected: group.First().IsSelected, Quantity: group.First().Quantity),
                StringComparer.OrdinalIgnoreCase);
        foreach (var order in WardOrders)
        {
            order.PropertyChanged -= WardOrder_PropertyChanged;
        }
        if (SelectedPharmacy == null)
        {
            WardOrders.Clear();
            OnPropertyChanged(nameof(WardSelectionSummary));
            return;
        }
        var uri = "/api/v4/pharmacy/ward-ready-queue?pharmacyId=" + Uri.EscapeDataString(SelectedPharmacy.Id);
        if (!string.IsNullOrWhiteSpace(WardEncounterFilter))
        {
            uri += "&encounterId=" + Uri.EscapeDataString(WardEncounterFilter.Trim());
        }
        var orders = await GetAsync<List<WardReadyOrderDto>>(uri);
        WardOrders.Clear();
        foreach (var order in orders ?? new List<WardReadyOrderDto>())
        {
            var row = new WardOrderRow(order);
            if (previousSelection.TryGetValue(row.MedicalOrderId, out var previous))
            {
                row.IsSelected = previous.IsSelected;
                row.Quantity = previous.Quantity;
            }
            row.PropertyChanged += WardOrder_PropertyChanged;
            WardOrders.Add(row);
        }
        WardOrderCount = WardOrders.Count;
        OnPropertyChanged(nameof(WardQueueSummary));
        OnPropertyChanged(nameof(WardSelectionSummary));
    }

    private void WardOrder_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WardOrderRow.IsSelected) or nameof(WardOrderRow.Quantity))
        {
            OnPropertyChanged(nameof(WardSelectionSummary));
        }
    }

    private async Task ExecuteMutationAsync(string operation, Func<Task> action, string success)
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        try
        {
            ClearFeedback();
            await action();
            Notice = success;
        }
        catch (Exception ex)
        {
            SetError(ex, operation);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<HttpResponseMessage> PostAsync(string uri, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        var token = SessionContext.Current.AccessToken;
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await _http.SendAsync(request);
    }

    private async Task EnsureSuccessfulAsync(HttpResponseMessage response)
    {
        ClientApiEnvelope<JsonElement>? envelope = null;
        try
        {
            envelope = await response.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOptions);
        }
        catch (JsonException)
        {
        }
        if (!response.IsSuccessStatusCode || envelope?.Code != 200)
        {
            throw new InvalidOperationException(
                envelope?.Message ?? $"请求失败 (HTTP {(int)response.StatusCode})");
        }
    }

    private async Task<T?> GetAsync<T>(string uri)
    {
        var token = SessionContext.Current.AccessToken;
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        using var response = await _http.SendAsync(request);
        ClientApiEnvelope<T>? envelope = null;
        try
        {
            envelope = await response.Content.ReadFromJsonAsync<ClientApiEnvelope<T>>(JsonOptions);
        }
        catch (JsonException)
        {
        }
        if (!response.IsSuccessStatusCode || envelope?.Code != 200)
        {
            throw new InvalidOperationException(envelope?.Message ?? $"请求失败 (HTTP {(int)response.StatusCode})");
        }
        return envelope.Data;
    }

    private static async Task<string> GetMessageAsync(HttpResponseMessage response)
    {
        try
        {
            var envelope = await response.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOptions);
            if (!string.IsNullOrWhiteSpace(envelope?.Message))
            {
                return envelope.Message;
            }
        }
        catch (JsonException)
        {
        }
        return response.IsSuccessStatusCode ? "操作成功。" : $"请求失败 (HTTP {(int)response.StatusCode})";
    }

    private void ClearFeedback()
    {
        Notice = string.Empty;
        ErrorMessage = string.Empty;
    }

    private void SetValidationError(string message)
    {
        ErrorMessage = message;
        Notice = message;
    }

    private void SetError(Exception ex, string operation)
    {
        ClientDiagnosticsLog.Error("PharmacyWorkstation", operation, ex);
        ErrorMessage = operation + "：" + ex.Message;
        Notice = ErrorMessage;
    }

    private string GetDefaultWarehouseId() => SelectedWarehouse?.Id ?? WarehouseChoices.FirstOrDefault()?.Id ?? "DEPT-WH-01";

    private static bool TryDecimal(string? text, out decimal value)
        => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value)
           || decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value);

    public static string DepartmentNameFromId(string? id) => id switch
    {
        "DEPT-WH-01" => "全院中心药库",
        "DEPT-WH-02" => "冷链生物制品专库",
        "DEPT-PHM-01" => "门诊西药房",
        "DEPT-PHM-02" => "住院中心药房",
        _ => id ?? string.Empty
    };

    private sealed class AuthorizedPharmacyDto
    {
        public string Id { get; set; } = string.Empty;
        public string DeptName { get; set; } = string.Empty;
        public string DeptCategory { get; set; } = "PHARMACY";
    }

    private sealed class DepartmentDto
    {
        public string Id { get; set; } = string.Empty;
        public string DeptName { get; set; } = string.Empty;
        public string DeptCategory { get; set; } = string.Empty;
    }

    public sealed class InvDrugDto
    {
        public string Id { get; set; } = string.Empty;
        public string DrugCode { get; set; } = string.Empty;
        public string DrugName { get; set; } = string.Empty;
        public string Spec { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public decimal CostPrice { get; set; }
        public int AntibioticLevel { get; set; }
        public short IsNarcotic { get; set; }
        public short IsSkinTestRequired { get; set; }
        public short IsActive { get; set; }
    }

    public sealed class StockSummaryDto
    {
        public string WarehouseOrPharmacyId { get; set; } = string.Empty;
        public string DrugId { get; set; } = string.Empty;
        public string DrugCode { get; set; } = string.Empty;
        public string DrugName { get; set; } = string.Empty;
        public string Spec { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public decimal RetailPrice { get; set; }
        public decimal CostPrice { get; set; }
        public int TotalQuantity { get; set; }
        public int AvailableFefoQuantity { get; set; }
        public int FrozenQuantity { get; set; }
        public int BatchCount { get; set; }
        public int EarliestExpiryDateInt { get; set; }
        public decimal TotalCostAmount { get; set; }
        public decimal TotalRetailAmount { get; set; }
    }

    public sealed class InvDrugBatchDto
    {
        public string Id { get; set; } = string.Empty;
        public string WarehouseOrPharmacyId { get; set; } = string.Empty;
        public string DrugId { get; set; } = string.Empty;
        public string BatchNo { get; set; } = string.Empty;
        public int ExpiryDateInt { get; set; }
        public string? InvoiceNo { get; set; }
        public decimal? ColdChainTempCelsius { get; set; }
        public int Quantity { get; set; }
        public short IsFrozen { get; set; }
    }

    public sealed class TransferDto
    {
        public string Id { get; set; } = string.Empty;
        public string TransferNo { get; set; } = string.Empty;
        public string TransferType { get; set; } = string.Empty;
        public string SourceWarehouseId { get; set; } = string.Empty;
        public string TargetWarehouseId { get; set; } = string.Empty;
        public string DrugId { get; set; } = string.Empty;
        public string DrugNameSnapshot { get; set; } = string.Empty;
        public string BatchNo { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public long CreatedAtMs { get; set; }
        public string? Remark { get; set; }
    }

    public sealed class ReadyPrescriptionDto
    {
        public string DispenseOrderId { get; set; } = string.Empty;
        public string DispenseNo { get; set; } = string.Empty;
        public string PrescriptionId { get; set; } = string.Empty;
        public string PrescriptionNo { get; set; } = string.Empty;
        public string VisitId { get; set; } = string.Empty;
        public string PatientId { get; set; } = string.Empty;
        public string? PatientName { get; set; }
        public string? MedicalRecordNo { get; set; }
        public string TargetPharmacyId { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public int PrescriptionStatus { get; set; }
        public int DispenseOrderStatus { get; set; }
        public string OrderStateText { get; set; } = string.Empty;
        public bool CanDispense { get; set; }
        public bool IsOrderClosed { get; set; }
    }

    public sealed class PhmDispenseOrderDto
    {
        public string Id { get; set; } = string.Empty;
        public string DispenseNo { get; set; } = string.Empty;
        public string PharmacyId { get; set; } = string.Empty;
        public string? PrescriptionId { get; set; }
        public string PatientId { get; set; } = string.Empty;
        public int Status { get; set; }
        public string? DispensedById { get; set; }
        public long? DispensedAtMs { get; set; }
        public short DrugReturnConfirmed { get; set; }
        public long CreatedAtMs { get; set; }
    }

    public sealed class PrescriptionLineDto
    {
        public string Id { get; set; } = string.Empty;
        public string PrescriptionId { get; set; } = string.Empty;
        public int GroupNo { get; set; }
        public string DrugId { get; set; } = string.Empty;
        public string DrugNameSnapshot { get; set; } = string.Empty;
        public decimal DoseOnce { get; set; }
        public string DoseUnit { get; set; } = string.Empty;
        public string FrequencyCode { get; set; } = string.Empty;
        public string RouteCode { get; set; } = string.Empty;
        public int Days { get; set; }
        public int TotalQuantity { get; set; }
        public decimal UnitPrice { get; set; }
        public int SkinTestFlag { get; set; }
        public short IsDeleted { get; set; }
    }

    public sealed class PhmDispenseOrderLineDto
    {
        public string Id { get; set; } = string.Empty;
        public string? MedicalOrderId { get; set; }
        public string DrugId { get; set; } = string.Empty;
        public string DrugCode { get; set; } = string.Empty;
        public string DrugName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal UnitCostPrice { get; set; }
    }

    public sealed class WardReadyOrderDto
    {
        public string MedicalOrderId { get; set; } = string.Empty;
        public string OrderNo { get; set; } = string.Empty;
        public string EncounterId { get; set; } = string.Empty;
        public string AdmissionNo { get; set; } = string.Empty;
        public string PatientId { get; set; } = string.Empty;
        public string PatientName { get; set; } = string.Empty;
        public string WardId { get; set; } = string.Empty;
        public string WardName { get; set; } = string.Empty;
        public string DrugId { get; set; } = string.Empty;
        public string DrugCode { get; set; } = string.Empty;
        public string DrugName { get; set; } = string.Empty;
        public decimal Dosage { get; set; }
        public string FrequencyCode { get; set; } = string.Empty;
        public string RouteCode { get; set; } = string.Empty;
        public int OrderTermType { get; set; }
        public string TargetPharmacyId { get; set; } = string.Empty;
        public long? PlanStartAtMs { get; set; }
    }
}

public abstract class PharmacyRowModel : ClientModelBase
{
    public static string FormatDateInt(int value)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        return text.Length == 8 ? text.Substring(0, 4) + "-" + text.Substring(4, 2) + "-" + text.Substring(6, 2) : text;
    }

    public static string FormatTime(long? utcMs)
    {
        if (utcMs is not long value || value <= 0)
        {
            return "-";
        }
        return DateTimeOffset.FromUnixTimeMilliseconds(value).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
    }
}

public sealed class DrugRow : PharmacyRowModel
{
    public DrugRow(PharmacyWorkstationViewModel.InvDrugDto dto)
    {
        Id = dto.Id;
        DrugCode = dto.DrugCode;
        DrugName = dto.DrugName;
        Spec = dto.Spec;
        UnitName = dto.UnitName;
        Price = dto.Price;
        CostPrice = dto.CostPrice;
        AntibioticLevel = dto.AntibioticLevel;
        IsNarcotic = dto.IsNarcotic == 1;
        IsSkinTestRequired = dto.IsSkinTestRequired == 1;
        IsActive = dto.IsActive == 1;
    }

    public string Id { get; }
    public string DrugCode { get; }
    public string DrugName { get; }
    public string Spec { get; }
    public string UnitName { get; }
    public decimal Price { get; }
    public decimal CostPrice { get; }
    public int AntibioticLevel { get; }
    public bool IsNarcotic { get; }
    public bool IsSkinTestRequired { get; }
    public bool IsActive { get; }
    public string PriceText => "¥" + Price.ToString("0.00");
    public string CostPriceText => "¥" + CostPrice.ToString("0.00");
    public string AntibioticText => AntibioticLevel switch { 1 => "限制级", 2 => "特殊级", _ => "普通" };
    public string SafetyText => IsNarcotic ? "麻醉药品" : IsSkinTestRequired ? "需皮试" : AntibioticLevel > 0 ? "抗菌药" : "-";
    public string StatusText => IsActive ? "启用" : "停用";
    public string DisplayName => DrugName + " · " + Spec;
}

public sealed class StockRow : PharmacyRowModel
{
    public StockRow(PharmacyWorkstationViewModel.StockSummaryDto dto)
    {
        DrugCode = dto.DrugCode;
        DrugName = dto.DrugName;
        Spec = dto.Spec;
        UnitName = dto.UnitName;
        TotalQuantity = dto.TotalQuantity;
        AvailableQuantity = dto.AvailableFefoQuantity;
        FrozenQuantity = dto.FrozenQuantity;
        BatchCount = dto.BatchCount;
        EarliestExpiry = FormatDateInt(dto.EarliestExpiryDateInt);
        CostAmount = dto.TotalCostAmount;
        RetailAmount = dto.TotalRetailAmount;
    }

    public string DrugCode { get; }
    public string DrugName { get; }
    public string Spec { get; }
    public string UnitName { get; }
    public int TotalQuantity { get; }
    public int AvailableQuantity { get; }
    public int FrozenQuantity { get; }
    public int BatchCount { get; }
    public string EarliestExpiry { get; }
    public decimal CostAmount { get; }
    public decimal RetailAmount { get; }
    public string QuantityText => TotalQuantity + UnitName;
    public string AvailableText => AvailableQuantity + UnitName;
    public string BatchText => BatchCount + " 批";
    public string FrozenText => FrozenQuantity > 0 ? "冻结 " + FrozenQuantity : "正常";
}

public sealed class BatchRow : PharmacyRowModel
{
    private readonly IEnumerable<DrugRow> _drugs;
    public BatchRow(PharmacyWorkstationViewModel.InvDrugBatchDto dto, IEnumerable<DrugRow> drugs)
    {
        _drugs = drugs;
        Id = dto.Id;
        DrugId = dto.DrugId;
        BatchNo = dto.BatchNo;
        ExpiryDateInt = dto.ExpiryDateInt;
        InvoiceNo = string.IsNullOrWhiteSpace(dto.InvoiceNo) ? "-" : dto.InvoiceNo;
        Quantity = dto.Quantity;
        IsFrozen = dto.IsFrozen == 1;
    }

    public string Id { get; }
    public string DrugId { get; }
    public string BatchNo { get; }
    public int ExpiryDateInt { get; }
    public string InvoiceNo { get; }
    public int Quantity { get; }
    public bool IsFrozen { get; }
    public string DrugName => _drugs.FirstOrDefault(x => x.Id == DrugId)?.DrugName ?? DrugId;
    public string ExpiryText => FormatDateInt(ExpiryDateInt);
    public bool IsExpiringSoon
    {
        get
        {
            if (ExpiryDateInt <= 0)
            {
                return false;
            }
            var expiry = DateTime.ParseExact(ExpiryDateInt.ToString(CultureInfo.InvariantCulture), "yyyyMMdd", CultureInfo.InvariantCulture);
            return expiry <= DateTime.Today.AddDays(180);
        }
    }
    public string StatusText => IsFrozen ? "已冻结" : IsExpiringSoon ? "近效期优先" : "正常";
    public string StatusColor => IsFrozen ? "#DC2626" : IsExpiringSoon ? "#D97706" : "#15803D";
}

public sealed class TransferRow : PharmacyRowModel
{
    public TransferRow(PharmacyWorkstationViewModel.TransferDto dto)
    {
        TransferNo = dto.TransferNo;
        TransferType = dto.TransferType;
        Source = PharmacyWorkstationViewModel.DepartmentNameFromId(dto.SourceWarehouseId);
        Target = PharmacyWorkstationViewModel.DepartmentNameFromId(dto.TargetWarehouseId);
        DrugName = dto.DrugNameSnapshot;
        BatchNo = dto.BatchNo;
        QuantityText = dto.Quantity.ToString(CultureInfo.InvariantCulture);
        QuantityValue = dto.Quantity;
        CreatedAt = FormatTime(dto.CreatedAtMs);
    }

    public string TransferNo { get; }
    public string TransferType { get; }
    public string Source { get; }
    public string Target { get; }
    public string DrugName { get; }
    public string BatchNo { get; }
    public string QuantityText { get; }
    public int QuantityValue { get; }
    public string CreatedAt { get; }
    public bool IsWarehouseIssue => string.Equals(TransferType, "WAREHOUSE_ISSUE", StringComparison.OrdinalIgnoreCase);
    public string TypeText => IsWarehouseIssue ? "药库发药" : "跨库调拨";
}

public sealed class ReadyPrescriptionRow : PharmacyRowModel
{
    public ReadyPrescriptionRow(PharmacyWorkstationViewModel.ReadyPrescriptionDto dto)
    {
        PrescriptionId = dto.PrescriptionId;
        PrescriptionNo = dto.PrescriptionNo;
        PatientId = dto.PatientId;
        PatientName = string.IsNullOrWhiteSpace(dto.PatientName) ? dto.PatientId : dto.PatientName;
        MedicalRecordNo = string.IsNullOrWhiteSpace(dto.MedicalRecordNo) ? "-" : dto.MedicalRecordNo;
        TotalAmount = dto.TotalAmount;
        StateText = dto.OrderStateText;
        DispenseNo = dto.DispenseNo;
        CanDispense = dto.CanDispense;
        IsOrderClosed = dto.IsOrderClosed;
    }

    public string PrescriptionId { get; }
    public string PrescriptionNo { get; }
    public string PatientId { get; }
    public string PatientName { get; }
    public string MedicalRecordNo { get; }
    public decimal TotalAmount { get; }
    public string StateText { get; }
    public string DispenseNo { get; }
    public bool CanDispense { get; }
    public bool IsOrderClosed { get; }
    public string AmountText => "¥" + TotalAmount.ToString("0.00");
    public string StateColor => CanDispense ? "#15803D" : IsOrderClosed ? "#6B7280" : "#B45309";
}

public sealed class DispenseRow : PharmacyRowModel
{
    public DispenseRow(PharmacyWorkstationViewModel.PhmDispenseOrderDto dto)
    {
        Id = dto.Id;
        DispenseNo = dto.DispenseNo;
        PrescriptionId = dto.PrescriptionId ?? string.Empty;
        PatientId = dto.PatientId;
        Status = dto.Status;
        IsReturned = dto.DrugReturnConfirmed == 1;
        CreatedAt = FormatTime(dto.CreatedAtMs);
    }

    public string Id { get; }
    public string DispenseNo { get; }
    public string PrescriptionId { get; }
    public string PatientId { get; }
    public int Status { get; }
    public bool IsReturned { get; }
    public string CreatedAt { get; }
    public string PatientLabel => string.IsNullOrWhiteSpace(PrescriptionId) ? PatientId : PrescriptionId;
    public string StatusText => IsReturned ? "已退药入库" : Status switch
    {
        1 => "已发药 · 订单关闭",
        2 => "已退药 · 待财务退费",
        _ => "未发药"
    };
    public string StatusColor => IsReturned ? "#B45309" : Status == 1 ? "#15803D" : "#6B7280";
}

public sealed class ReadyPrescriptionLineRow : PharmacyRowModel
{
    public ReadyPrescriptionLineRow(PharmacyWorkstationViewModel.PrescriptionLineDto dto)
    {
        DrugName = dto.DrugNameSnapshot;
        DoseText = dto.DoseOnce.ToString("0.##", CultureInfo.InvariantCulture) + " " + dto.DoseUnit;
        FrequencyText = dto.FrequencyCode.ToUpperInvariant() switch
        {
            "QD" => "每日一次",
            "BID" => "每日两次",
            "TID" => "每日三次",
            "QID" => "每日四次",
            "Q8H" => "每8小时",
            "Q12H" => "每12小时",
            _ => dto.FrequencyCode
        };
        RouteText = dto.RouteCode.ToUpperInvariant() switch
        {
            "PO" => "口服",
            "IV" => "静脉注射",
            "IVGTT" => "静脉滴注",
            "IM" => "肌内注射",
            _ => dto.RouteCode
        };
        DaysText = dto.Days + " 天";
        QuantityText = dto.TotalQuantity.ToString(CultureInfo.InvariantCulture);
        UnitPriceText = "¥" + dto.UnitPrice.ToString("0.00");
        AmountText = "¥" + (dto.TotalQuantity * dto.UnitPrice).ToString("0.00");
        SkinTestText = dto.SkinTestFlag switch
        {
            4 => "皮试阳性 · 禁止发药",
            3 => "皮试阴性",
            1 or 2 => "皮试待录入",
            _ => "无需皮试"
        };
        IsSkinTestAlert = dto.SkinTestFlag == 4;
    }

    public string DrugName { get; }
    public string DoseText { get; }
    public string FrequencyText { get; }
    public string RouteText { get; }
    public string DaysText { get; }
    public string QuantityText { get; }
    public string UnitPriceText { get; }
    public string AmountText { get; }
    public string SkinTestText { get; }
    public bool IsSkinTestAlert { get; }
}

public sealed class DispenseLineRow
{
    public DispenseLineRow(PharmacyWorkstationViewModel.PhmDispenseOrderLineDto dto)
    {
        DrugCode = dto.DrugCode;
        DrugName = dto.DrugName;
        QuantityText = dto.Quantity.ToString(CultureInfo.InvariantCulture);
        AmountText = "¥" + (dto.Quantity * dto.UnitPrice).ToString("0.00");
        SourceText = string.IsNullOrWhiteSpace(dto.MedicalOrderId) ? "门诊处方" : "住院医嘱 " + dto.MedicalOrderId;
    }

    public string DrugCode { get; }
    public string DrugName { get; }
    public string QuantityText { get; }
    public string AmountText { get; }
    public string SourceText { get; }
}

public sealed class WardOrderRow : ClientModelBase
{
    private bool _isSelected;
    private int _quantity = 1;

    public WardOrderRow(PharmacyWorkstationViewModel.WardReadyOrderDto dto)
    {
        MedicalOrderId = dto.MedicalOrderId;
        OrderNo = dto.OrderNo;
        EncounterId = dto.EncounterId;
        AdmissionNo = dto.AdmissionNo;
        PatientName = dto.PatientName;
        WardName = dto.WardName;
        DrugName = dto.DrugName;
        DrugCode = dto.DrugCode;
        DosageText = dto.Dosage.ToString("0.##", CultureInfo.InvariantCulture);
        FrequencyText = string.Equals(dto.FrequencyCode, "qd", StringComparison.OrdinalIgnoreCase) ? "每日一次" : dto.FrequencyCode;
        RouteText = string.Equals(dto.RouteCode, "PO", StringComparison.OrdinalIgnoreCase) ? "口服" : dto.RouteCode;
        StartTime = PharmacyRowModel.FormatTime(dto.PlanStartAtMs);
        _quantity = Math.Max(1, PrescriptionItemModel.ParseDailyFrequencyCount(dto.FrequencyCode));
    }

    public string MedicalOrderId { get; }
    public string OrderNo { get; }
    public string EncounterId { get; }
    public string AdmissionNo { get; }
    public string PatientName { get; }
    public string WardName { get; }
    public string DrugName { get; }
    public string DrugCode { get; }
    public string DosageText { get; }
    public string FrequencyText { get; }
    public string RouteText { get; }
    public string StartTime { get; }
    public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }
    public int Quantity { get => _quantity; set => SetField(ref _quantity, Math.Max(value, 0)); }
}

public sealed record PharmacyChoice(string Id, string Name, string Category);

public sealed record WarehouseChoice(string Id, string Name, string Category);
