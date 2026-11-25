using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Layout;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.IpdDoctor;

public class CommonOrderSetItem
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<(string Name, string Spec, string Dose, string Unit, string Route, string Freq, decimal Price, string SkinTest)> Items { get; set; } = new();
}

public sealed partial class IpdDoctorViewModel : ClientModelBase, IShortcutTarget
{
    private const int OrdersWorkspace = 0;
    private const int NotesWorkspace = 1;
    private const int DischargeWorkspace = 2;

    private readonly ShellViewModel? _shell;
    private int _selectedWorkspaceIndex;
    private bool _isLongTermOrder = true;
    private bool _subscribed;

    // Quick Order Bar Properties
    private string _selectedOrderTypeInput = "长期医嘱";
    private string _selectedItemCategory = "全部";
    private bool _isUrgentInput;
    private string _quickSearchCode = string.Empty;
    private string _orderContentInput = string.Empty;
    private string _specInput = string.Empty;
    private double _singleDoseInput = 1.0;
    private string _selectedDoseUnit = "片";
    private string _singleDoseEquivInput = "0.250";
    private string _singleDoseEquivUnitInput = "g";
    private int _quantityInput = 1;
    private string _selectedTotalUnit = "盒";
    private string _selectedRoute = "口服";
    private string _selectedDripRate = "--";
    private string _selectedFrequency = "qd (每日一次)";
    private int _daysInput = 1;
    private string _selectedExecDept = "普外二病区护理站";
    private string _selectedDeductDept = "西药库";
    private int _hospitalInjectionCountInput = 0;
    private int _prescriptionPacksInput = 1;
    private string _selectedSpecialAttr = "常规";
    private string _remarkInput = string.Empty;
    private string _unitPriceInput = "¥18.00";
    private bool _isDrugDropdownOpen;
    private int _selectedCatalogIndex;

    public IpdDoctorViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        OpenCriticalValueCommand = new RelayCommand(() => _shell?.OpenCriticalValueDrawerCommand.Execute(null));
        AdmitInpatientCommand = new RelayCommand(ExecuteAdmitInpatient);
        AddLongOrderCommand = new RelayCommand(() => ExecuteAddOrder(true), () => Workbench.CanCreateInpatientOrder);
        AddTempOrderCommand = new RelayCommand(() => ExecuteAddOrder(false), () => Workbench.CanCreateInpatientOrder);
        SubmitOrderCommand = new RelayCommand(ExecuteSubmitOrder, CanSubmitOrder);
        SelectWorkspaceCommand = new RelayCommand(p => SelectWorkspace(p));
        FilterOrdersCommand = new RelayCommand(p => ApplyOrderFilter(p as string ?? "ALL"));
        StopOrderCommand = new RelayCommand(p => ConfirmStopOrder(p as InpatientOrderModel));
        CancelOrderCommand = new RelayCommand(p => ConfirmCancelOrder(p as InpatientOrderModel));
        AddProgressNoteCommand = new RelayCommand(ExecuteAddProgressNote);
        SignLatestProgressNoteCommand = new RelayCommand(ExecuteSignLatestNote, () => Workbench.CanSignSelectedProgressNotes);
        SignNoteCommand = new RelayCommand(p => ExecuteSignNote(p as ProgressNoteModel));
        OpenDischargeCheckCommand = new RelayCommand(OpenDischargeCheckDrawer);
        ApproveDischargeCommand = new AsyncRelayCommand(ApproveDischargeAsync, () => Workbench.CanApproveSelectedDischarge);
        LoadUserTemplatesCommand = new AsyncRelayCommand(LoadUserTemplatesAsync);
        SaveAsUserTemplateCommand = new AsyncRelayCommand(SaveAsUserTemplateAsync);
        ApplyTemplateCommand = new RelayCommand(p => ExecuteApplyTemplate(p as EmrUserTemplateModel));

        // Clinical Quick Order & Table Toolbar Commands
        AddQuickOrderCommand = new RelayCommand(ExecuteAddQuickOrder, () => Workbench.CanCreateInpatientOrder);
        GroupSelectedOrdersCommand = new RelayCommand(ExecuteGroupSelectedOrders);
        UngroupSelectedOrdersCommand = new RelayCommand(ExecuteUngroupSelectedOrders);
        QuickSetSkinTestCommand = new RelayCommand(p => ExecuteQuickSetSkinTest(p as string ?? "免试"));
        ToggleUrgentCommand = new RelayCommand(ExecuteToggleUrgent);
        SelectAllOrdersCommand = new RelayCommand(p => ExecuteSelectAllOrders(p is bool b ? b : true));
        ToggleBannerExpandCommand = new RelayCommand(ExecuteToggleBannerExpand);
        ToggleOrderSetsDrawerCommand = new RelayCommand(() => IsOrderSetsDrawerOpen = !IsOrderSetsDrawerOpen);
        PreCheckOrdersCommand = new RelayCommand(ExecutePreCheckOrders);
        SubmitOrdersToNurseCommand = new RelayCommand(ExecuteSubmitOrdersToNurse);
        AddDrugCatalogItemCommand = new RelayCommand(p =>
        {
            if (p is DrugCatalogItemModel drug)
            {
                SelectCatalogDrug(drug);
            }
        });

        GoFirstPageCommand = new RelayCommand(() => _shell?.ShowToast("已跳转到医嘱单第 1 页", "Info"));
        GoPrevPageCommand = new RelayCommand(() => _shell?.ShowToast("当前已是第一页", "Info"));
        GoNextPageCommand = new RelayCommand(() => _shell?.ShowToast("医嘱记录已全部加载完成", "Info"));
        GoLastPageCommand = new RelayCommand(() => _shell?.ShowToast("已跳转到医嘱单最后一页", "Info"));
        OpenOrderSettingsCommand = new RelayCommand(() =>
        {
            if (SessionContext.Current.IsAdmin)
            {
                _shell?.NavigateToStation("dynamic_form");
                _shell?.ShowToast("已进入医嘱与动态表单配置中心", "Success");
            }
            else
            {
                _shell?.ShowToast("提示：全院医嘱字典与频次参数由医务处及系统管理员统一维护；临床医师请在左侧选用标准化常用医嘱方案。", "Info");
            }
        });
        ApplyCommonOrderSetCommand = new RelayCommand(p =>
        {
            if (p is CommonOrderSetItem set)
            {
                ExecuteApplyCommonOrderSet(set);
            }
        });
        InitCommonOrderSets();
        if (SessionContext.Current.IsLoggedIn)
        {
            _ = Workbench.RefreshInpatientsFromServerAsync();
        }
    }

    public WorkbenchModel Workbench { get; }

    public ObservableCollection<CommonOrderSetItem> CommonOrderSets { get; } = new();

    private CommonOrderSetItem? _selectedCommonOrderSet;
    public CommonOrderSetItem? SelectedCommonOrderSet
    {
        get => _selectedCommonOrderSet;
        set
        {
            if (SetField(ref _selectedCommonOrderSet, value) && value != null)
            {
                ExecuteApplyCommonOrderSet(value);
            }
        }
    }

    public ICommand GoFirstPageCommand { get; }
    public ICommand GoPrevPageCommand { get; }
    public ICommand GoNextPageCommand { get; }
    public ICommand GoLastPageCommand { get; }
    public ICommand OpenOrderSettingsCommand { get; }
    public ICommand ApplyCommonOrderSetCommand { get; }

    public ObservableCollection<EmrUserTemplateModel> UserTemplates { get; } = new();

    private EmrUserTemplateModel? _selectedUserTemplate;
    public EmrUserTemplateModel? SelectedUserTemplate
    {
        get => _selectedUserTemplate;
        set
        {
            if (SetField(ref _selectedUserTemplate, value) && value != null)
            {
                ExecuteApplyTemplate(value);
            }
        }
    }

    public ICommand LoadUserTemplatesCommand { get; }
    public ICommand SaveAsUserTemplateCommand { get; }
    public ICommand ApplyTemplateCommand { get; }

    // Quick Order & Toolbar Commands
    public ICommand AddQuickOrderCommand { get; }
    public ICommand GroupSelectedOrdersCommand { get; }
    public ICommand UngroupSelectedOrdersCommand { get; }
    public ICommand QuickSetSkinTestCommand { get; }
    public ICommand ToggleUrgentCommand { get; }
    public ICommand SelectAllOrdersCommand { get; }
    public ICommand ToggleBannerExpandCommand { get; }
    public ICommand PreCheckOrdersCommand { get; }
    public ICommand SubmitOrdersToNurseCommand { get; }
    public ICommand AddDrugCatalogItemCommand { get; }

    public Action? FocusOrderInputAction { get; set; }

    // Dropdown / Options Lists
    public ObservableCollection<string> OrderTypeOptions { get; } = new() { "长期医嘱", "临时医嘱" };
    public ObservableCollection<string> ItemCategoryOptions { get; } = new() { "全部", "西药", "中成药", "中草药", "处置", "检验", "检查", "材料" };
    public ObservableCollection<string> DoseUnitOptions { get; } = new() { "片", "支", "瓶", "袋", "ml", "mg", "g", "次", "套" };
    public ObservableCollection<string> TotalUnitOptions { get; } = new() { "盒", "瓶", "支", "袋", "片", "次", "套" };
    public ObservableCollection<string> RouteOptions { get; } = new() { "口服", "静脉滴注", "静脉注射", "肌肉注射", "皮下注射", "外用", "雾化吸入", "持续微量泵入", "留置引流" };
    public ObservableCollection<string> DripRateOptions { get; } = new() { "--", "40滴/分", "50滴/分", "60滴/分", "10ml/h", "20ml/h" };
    public ObservableCollection<string> FrequencyOptions { get; } = new() { "qd (每日一次)", "bid (每日两次)", "tid (每日三次)", "qid (每日四次)", "qn (每晚一次)", "q8h (每8小时)", "q12h (每12小时)", "st (立即执行)", "prn (必要时)" };
    public ObservableCollection<string> ExecDeptOptions { get; } = new() { "普外二病区护理站", "静脉用药调配中心(PIVAS)", "门诊西药房", "检验科", "放射科CT室", "超声科" };
    public ObservableCollection<string> DeductDeptOptions { get; } = new() { "西药库", "中成药房", "中草药房", "耗材库", "静配中心(PIVAS)" };
    public ObservableCollection<string> SingleDoseEquivUnitOptions { get; } = new() { "g", "mg", "ml", "万单位", "支", "片" };
    public ObservableCollection<string> SpecialAttrOptions { get; } = new() { "常规", "限制级抗生素", "特殊级抗生素", "高危药品", "自备药", "慢病用药" };

    public ObservableCollection<DrugCatalogItemModel> FilteredDrugCatalog { get; } = new();

    public string SelectedOrderTypeInput
    {
        get => _selectedOrderTypeInput;
        set
        {
            if (SetField(ref _selectedOrderTypeInput, value, nameof(SelectedOrderTypeInput)))
            {
                IsLongTermOrder = value.Contains("长");
            }
        }
    }

    public string SelectedItemCategory
    {
        get => _selectedItemCategory;
        set => SetField(ref _selectedItemCategory, value, nameof(SelectedItemCategory));
    }

    public bool IsUrgentInput
    {
        get => _isUrgentInput;
        set => SetField(ref _isUrgentInput, value, nameof(IsUrgentInput));
    }

    public string QuickSearchCode
    {
        get => _quickSearchCode;
        set
        {
            if (SetField(ref _quickSearchCode, value, nameof(QuickSearchCode)))
            {
                OnQuickSearchCodeChanged(value);
            }
        }
    }

    public string OrderContentInput
    {
        get => _orderContentInput;
        set
        {
            if (SetField(ref _orderContentInput, value, nameof(OrderContentInput)))
            {
                Workbench.NewOrderContent = value;
                RefreshDrugSearchResults(value);
            }
        }
    }

    public string SpecInput
    {
        get => _specInput;
        set
        {
            if (SetField(ref _specInput, value, nameof(SpecInput)))
            {
                OnPropertyChanged(nameof(QuickOrderSpecOrAmountText));
            }
        }
    }

    public double SingleDoseInput
    {
        get => _singleDoseInput;
        set
        {
            if (SetField(ref _singleDoseInput, value, nameof(SingleDoseInput)))
            {
                RecalculateEquivDose();
            }
        }
    }

    public string SelectedDoseUnit
    {
        get => _selectedDoseUnit;
        set => SetField(ref _selectedDoseUnit, value, nameof(SelectedDoseUnit));
    }

    public string SingleDoseEquivInput
    {
        get => _singleDoseEquivInput;
        set => SetField(ref _singleDoseEquivInput, value, nameof(SingleDoseEquivInput));
    }

    public string SingleDoseEquivUnitInput
    {
        get => _singleDoseEquivUnitInput;
        set => SetField(ref _singleDoseEquivUnitInput, value, nameof(SingleDoseEquivUnitInput));
    }

    public string SelectedDeductDept
    {
        get => _selectedDeductDept;
        set => SetField(ref _selectedDeductDept, value, nameof(SelectedDeductDept));
    }

    public int HospitalInjectionCountInput
    {
        get => _hospitalInjectionCountInput;
        set => SetField(ref _hospitalInjectionCountInput, value, nameof(HospitalInjectionCountInput));
    }

    public int PrescriptionPacksInput
    {
        get => _prescriptionPacksInput;
        set => SetField(ref _prescriptionPacksInput, value, nameof(PrescriptionPacksInput));
    }

    public int QuantityInput
    {
        get => _quantityInput;
        set => SetField(ref _quantityInput, value, nameof(QuantityInput));
    }

    public string SelectedTotalUnit
    {
        get => _selectedTotalUnit;
        set => SetField(ref _selectedTotalUnit, value, nameof(SelectedTotalUnit));
    }

    public string SelectedRoute
    {
        get => _selectedRoute;
        set => SetField(ref _selectedRoute, value, nameof(SelectedRoute));
    }

    public string SelectedDripRate
    {
        get => _selectedDripRate;
        set => SetField(ref _selectedDripRate, value, nameof(SelectedDripRate));
    }

    public string SelectedFrequency
    {
        get => _selectedFrequency;
        set => SetField(ref _selectedFrequency, value, nameof(SelectedFrequency));
    }

    public int DaysInput
    {
        get => _daysInput;
        set => SetField(ref _daysInput, value, nameof(DaysInput));
    }

    public string SelectedExecDept
    {
        get => _selectedExecDept;
        set => SetField(ref _selectedExecDept, value, nameof(SelectedExecDept));
    }

    public string SelectedSpecialAttr
    {
        get => _selectedSpecialAttr;
        set => SetField(ref _selectedSpecialAttr, value, nameof(SelectedSpecialAttr));
    }

    private bool _isOrderSetsDrawerOpen;
    public bool IsOrderSetsDrawerOpen
    {
        get => _isOrderSetsDrawerOpen;
        set => SetField(ref _isOrderSetsDrawerOpen, value, nameof(IsOrderSetsDrawerOpen));
    }

    public ICommand ToggleOrderSetsDrawerCommand { get; }

    public string QuickOrderSpecOrAmountText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(SpecInput))
                return SpecInput;
            if (!string.IsNullOrWhiteSpace(UnitPriceInput))
                return UnitPriceInput;
            return string.Empty;
        }
    }

    public string RemarkInput
    {
        get => _remarkInput;
        set => SetField(ref _remarkInput, value, nameof(RemarkInput));
    }

    public string UnitPriceInput
    {
        get => _unitPriceInput;
        set
        {
            if (SetField(ref _unitPriceInput, value, nameof(UnitPriceInput)))
            {
                OnPropertyChanged(nameof(QuickOrderSpecOrAmountText));
            }
        }
    }

    public bool IsDrugDropdownOpen
    {
        get => _isDrugDropdownOpen;
        set => SetField(ref _isDrugDropdownOpen, value, nameof(IsDrugDropdownOpen));
    }

    public int SelectedCatalogIndex
    {
        get => _selectedCatalogIndex;
        set => SetField(ref _selectedCatalogIndex, value, nameof(SelectedCatalogIndex));
    }

    public int SelectedWorkspaceIndex
    {
        get => _selectedWorkspaceIndex;
        set
        {
            if (SetField(ref _selectedWorkspaceIndex, value))
            {
                OnPropertyChanged(nameof(IsOrdersWorkspace));
                OnPropertyChanged(nameof(IsNotesWorkspace));
                OnPropertyChanged(nameof(IsDischargeWorkspace));
            }
        }
    }

    public bool IsOrdersWorkspace => SelectedWorkspaceIndex == OrdersWorkspace;
    public bool IsNotesWorkspace => SelectedWorkspaceIndex == NotesWorkspace;
    public bool IsDischargeWorkspace => SelectedWorkspaceIndex == DischargeWorkspace;

    public bool IsLongTermOrder
    {
        get => _isLongTermOrder;
        set
        {
            if (SetField(ref _isLongTermOrder, value))
            {
                OnPropertyChanged(nameof(SelectedOrderType));
                (SubmitOrderCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string SelectedOrderType
    {
        get => IsLongTermOrder ? "长期医嘱" : "临时医嘱";
        set
        {
            IsLongTermOrder = value != "临时医嘱";
        }
    }

    public string OrderCreateBlockedReason
    {
        get
        {
            if (Workbench.SelectedInpatient == null)
            {
                return "请先在左侧床位列表选择一位住院患者。";
            }
            if (!Workbench.CanCreateInpatientOrder)
            {
                return "当前登录身份无住院医生医嘱开立权限，请切换为住院医生或管理员。";
            }
            return string.Empty;
        }
    }

    public string DischargeBlockedReason
    {
        get
        {
            if (Workbench.SelectedInpatient == null)
            {
                return "未选中住院患者。";
            }
            if (!Workbench.CanApproveSelectedDischarge)
            {
                return "出院六严关尚未全部通过，需医嘱/医技/带药/病程/医保/预交金全绿灯后才可申请出院。";
            }
            return string.Empty;
        }
    }

    public ICommand OpenCriticalValueCommand { get; }
    public ICommand AdmitInpatientCommand { get; }
    public ICommand AddLongOrderCommand { get; }
    public ICommand AddTempOrderCommand { get; }
    public ICommand SubmitOrderCommand { get; }
    public ICommand SelectWorkspaceCommand { get; }
    public ICommand FilterOrdersCommand { get; }
    public ICommand StopOrderCommand { get; }
    public ICommand CancelOrderCommand { get; }
    public ICommand AddProgressNoteCommand { get; }
    public ICommand SignLatestProgressNoteCommand { get; }
    public ICommand SignNoteCommand { get; }
    public ICommand OpenDischargeCheckCommand { get; }
    public ICommand ApproveDischargeCommand { get; }

    public void Attach()
    {
        if (!_subscribed)
        {
            Workbench.PropertyChanged += OnWorkbenchPropertyChanged;
            _subscribed = true;
        }
        _ = LoadUserTemplatesAsync();
        RaiseComputed();
    }

    public void Detach()
    {
        if (_subscribed)
        {
            Workbench.PropertyChanged -= OnWorkbenchPropertyChanged;
            _subscribed = false;
        }
    }

    public bool OnShortcut(string keyGesture)
    {
        switch (keyGesture)
        {
            case "F2":
            case "/":
                SelectedWorkspaceIndex = OrdersWorkspace;
                FocusOrderInputAction?.Invoke();
                return true;
            case "F3":
                OpenDischargeCheckDrawer();
                return true;
            case "Ctrl+S":
                if (SelectedWorkspaceIndex == NotesWorkspace)
                {
                    ExecuteAddProgressNote();
                    return true;
                }
                return false;
            case "Ctrl+Enter":
                if (SelectedWorkspaceIndex == OrdersWorkspace)
                {
                    if (!string.IsNullOrWhiteSpace(OrderContentInput))
                    {
                        ExecuteAddQuickOrder();
                    }
                    else
                    {
                        ExecuteSubmitOrdersToNurse();
                    }
                    return true;
                }
                return false;
            default:
                return false;
        }
    }

    private void OnQuickSearchCodeChanged(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        var match = Workbench.AvailableDrugsCatalog.FirstOrDefault(d =>
            d.DrugCode.Contains(code, StringComparison.OrdinalIgnoreCase) ||
            d.DrugName.Contains(code, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            SelectCatalogDrug(match);
        }
        else
        {
            OrderContentInput = code;
        }
    }

    private void RefreshDrugSearchResults(string query)
    {
        FilteredDrugCatalog.Clear();
        var q = query?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(q))
        {
            IsDrugDropdownOpen = false;
            return;
        }

        var matches = Workbench.AvailableDrugsCatalog
            .Where(d => d.DrugName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        d.DrugCode.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        d.Spec.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Take(8);

        foreach (var d in matches)
        {
            FilteredDrugCatalog.Add(d);
        }
        IsDrugDropdownOpen = FilteredDrugCatalog.Count > 0;
        SelectedCatalogIndex = 0;
    }

    public void SelectCatalogDrug(DrugCatalogItemModel drug)
    {
        if (drug == null) return;
        OrderContentInput = drug.DrugName;
        SpecInput = drug.Spec;
        UnitPriceInput = drug.UnitPrice;
        if (drug.Spec.Contains("片"))
        {
            SelectedDoseUnit = "片";
            SelectedTotalUnit = "盒";
            SelectedRoute = "口服";
        }
        else if (drug.Spec.Contains("支"))
        {
            SelectedDoseUnit = "支";
            SelectedTotalUnit = "盒";
        }
        else if (drug.Spec.Contains("瓶") || drug.Spec.Contains("ml"))
        {
            SelectedDoseUnit = "ml";
            SelectedTotalUnit = "瓶";
            SelectedRoute = "静脉滴注";
            SelectedDripRate = "50滴/分";
        }
        RecalculateEquivDose();
        IsDrugDropdownOpen = false;
    }

    private void RecalculateEquivDose()
    {
        if (SpecInput.Contains("0.25g"))
        {
            SingleDoseEquivInput = (SingleDoseInput * 0.25).ToString("0.000");
            SingleDoseEquivUnitInput = "g";
        }
        else if (SpecInput.Contains("0.5g"))
        {
            SingleDoseEquivInput = (SingleDoseInput * 0.5).ToString("0.000");
            SingleDoseEquivUnitInput = "g";
        }
        else if (SpecInput.Contains("100ml"))
        {
            SingleDoseEquivInput = (SingleDoseInput * 100.0).ToString("0.000");
            SingleDoseEquivUnitInput = "ml";
        }
        else
        {
            SingleDoseEquivInput = (SingleDoseInput * 1.0).ToString("0.000");
        }
    }

    private void InitCommonOrderSets()
    {
        CommonOrderSets.Clear();
        CommonOrderSets.Add(new CommonOrderSetItem
        {
            Name = "常1: 补液支持常用组套 (输液3联)",
            Category = "补液",
            Description = "5%葡萄糖注射液 500ml + 氯化钾注射液 10ml + 维生素C注射液 2g",
            Items = new()
            {
                ("5%葡萄糖注射液", "500ml:25g/瓶", "500", "ml", "静脉滴注", "qd (每日一次)", 6.50m, "免试"),
                ("10%氯化钾注射液", "10ml:1.0g/支", "10", "ml", "静脉滴注", "qd (每日一次)", 3.20m, "免试"),
                ("维生素C注射液", "2ml:0.5g/支", "4", "支", "静脉滴注", "qd (每日一次)", 2.80m, "免试")
            }
        });
        CommonOrderSets.Add(new CommonOrderSetItem
        {
            Name = "常2: 围术期抗感染预防套",
            Category = "抗感染",
            Description = "注射用头孢唑林钠 1.0g + 0.9%氯化钠注射液 100ml",
            Items = new()
            {
                ("0.9%氯化钠注射液", "100ml:0.9g/袋", "100", "ml", "静脉滴注", "qd (每日一次)", 4.50m, "免试"),
                ("注射用头孢唑林钠", "1.0g/支", "1.0", "g", "静脉滴注", "qd (每日一次)", 24.80m, "需皮试")
            }
        });
        CommonOrderSets.Add(new CommonOrderSetItem
        {
            Name = "常3: 支气管雾化解痉组套",
            Category = "雾化",
            Description = "吸入用复方异丙托溴铵溶液 2.5ml + 吸入用乙酰半胱氨酸溶液 3ml",
            Items = new()
            {
                ("吸入用复方异丙托溴铵溶液", "2.5ml/支", "2.5", "ml", "雾化吸入", "bid (每日两次)", 18.50m, "免试"),
                ("吸入用乙酰半胱氨酸溶液", "3ml:0.3g/支", "3", "ml", "雾化吸入", "bid (每日两次)", 14.20m, "免试")
            }
        });
        CommonOrderSets.Add(new CommonOrderSetItem
        {
            Name = "常4: 术后镇痛消炎组套",
            Category = "镇痛",
            Description = "氟比洛芬酯注射液 50mg + 0.9%氯化钠注射液 100ml",
            Items = new()
            {
                ("0.9%氯化钠注射液", "100ml:0.9g/袋", "100", "ml", "静脉滴注", "qd (每日一次)", 4.50m, "免试"),
                ("氟比洛芬酯注射液", "5ml:50mg/支", "50", "mg", "静脉滴注", "qd (每日一次)", 38.00m, "免试")
            }
        });
    }

    private void ExecuteApplyCommonOrderSet(CommonOrderSetItem set)
    {
        var patient = Workbench.SelectedInpatient;
        if (patient == null)
        {
            _shell?.ShowToast("请先在左侧选择一位住院患者", "Warning");
            return;
        }

        var isLong = SelectedOrderTypeInput.Contains("长");
        var comboNo = $"11317{Random.Shared.Next(1000, 9999)}";
        var count = set.Items.Count;

        for (int i = 0; i < count; i++)
        {
            var item = set.Items[i];
            string groupIcon = count > 1 ? (i == 0 ? "┓" : (i == count - 1 ? "┛" : "┃")) : "-";

            var order = new InpatientOrderModel
            {
                OrderNo = Workbench.NextInpatientId("ORD", patient.Orders.Count),
                OrderType = isLong ? "长期医嘱" : "临时医嘱",
                ItemCategory = "西药",
                IsUrgent = false,
                StartTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                Content = item.Name,
                Spec = item.Spec,
                SingleDose = item.Dose,
                Dose = $"{item.Dose}{item.Unit}",
                DoseUnit = item.Unit,
                SingleDoseEquiv = item.Dose,
                SingleDoseEquivUnit = item.Unit,
                UsageAmount = "1",
                UsageUnit = item.Spec.Contains("袋") ? "袋" : (item.Spec.Contains("瓶") ? "瓶" : "支"),
                Days = 1,
                Quantity = 1,
                Unit = item.Spec.Contains("袋") ? "袋" : (item.Spec.Contains("瓶") ? "瓶" : "支"),
                PrescriptionPacks = 1,
                Route = item.Route,
                DripRate = item.Route.Contains("滴注") ? "50滴/分" : "--",
                Frequency = item.Freq,
                HospitalInjectionCount = 0,
                ExecDept = "普外二病区护理站",
                DeductDept = "西药库",
                UnitPrice = $"¥{item.Price:F2}",
                TotalPrice = $"¥{item.Price:F2}",
                Remark = set.Name,
                SpecialAttr = "常规",
                StartExecTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                DoctorName = Workbench.CurrentOperatorName,
                InputOperator = Workbench.CurrentOperatorName,
                OrderDept = "普外二病区",
                StopDoctor = "--",
                StopReason = "--",
                Specimen = "--",
                BodyPart = "--",
                ExamMethod = "--",
                SkinTestInfo = item.SkinTest,
                StopTime = "--",
                GroupComboNo = comboNo,
                GroupIcon = groupIcon,
                SeqNo = patient.Orders.Count + 1,
                Status = "待护士校对",
                NurseSign = "--",
                AlertStatus = "✓",
                AlertTooltip = "常用组套审方通过",
                AlertColor = "#10B981",
                ApprovalStatus = "APPROVED",
                ReviewInfo = $"[常用组套] 由医生 {Workbench.CurrentOperatorName} 调入，等待责任护士校对审签"
            };

            patient.Orders.Insert(0, order);
            _ = PostOrderToServerAsync(patient, order);
        }

        patient.ApplyOrderFilter();
        Workbench.SyncInpatientBed(patient);
        Workbench.NotifyInpatientChanged(patient);
        _shell?.ShowToast($"✓ 已成功调入常用医嘱组套「{set.Name}」（含 {count} 项医嘱，自动成组 ┓┃┛）！", "Success");
        RaiseComputed();
    }

    private void ExecuteAddQuickOrder()
    {
        var patient = Workbench.SelectedInpatient;
        if (patient == null)
        {
            _shell?.ShowToast("请先在左侧选择一位住院患者", "Warning");
            return;
        }
        if (string.IsNullOrWhiteSpace(OrderContentInput))
        {
            _shell?.ShowToast("请输入或选择医嘱项目名称", "Warning");
            return;
        }

        var isLong = SelectedOrderTypeInput.Contains("长");
        var priceText = UnitPriceInput.Replace("¥", "").Trim();
        decimal.TryParse(priceText, out var unitPriceVal);
        if (unitPriceVal <= 0) unitPriceVal = 18.00m;
        var totalPriceVal = unitPriceVal * QuantityInput;

        var matchedDrug = Workbench.AvailableDrugsCatalog.FirstOrDefault(d =>
            string.Equals(d.DrugName, OrderContentInput.Trim(), StringComparison.OrdinalIgnoreCase) ||
            OrderContentInput.Trim().Contains(d.DrugName, StringComparison.OrdinalIgnoreCase));

        var order = new InpatientOrderModel
        {
            OrderNo = Workbench.NextInpatientId("ORD", patient.Orders.Count),
            EncounterId = !string.IsNullOrWhiteSpace(patient.EncounterId) ? patient.EncounterId : patient.AdmissionNo,
            ItemCode = matchedDrug?.DrugCode ?? string.Empty,
            OrderSubType = SelectedOrderSubType.Code,
            OrderType = isLong ? "长期医嘱" : "临时医嘱",
            ItemCategory = SelectedItemCategory,
            IsUrgent = IsUrgentInput,
            StartTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            Content = SelectedOrderSubType.Code == "DISCHARGE_MED" ? $"[出院带药] {OrderContentInput.Trim()}" : OrderContentInput.Trim(),
            Spec = !string.IsNullOrWhiteSpace(SpecInput) ? SpecInput : (matchedDrug?.Spec ?? "常规规格"),
            SingleDose = SingleDoseInput.ToString("0.##"),
            Dose = $"{SingleDoseInput:0.##}{SelectedDoseUnit}",
            DoseUnit = SelectedDoseUnit,
            SingleDoseEquiv = SingleDoseEquivInput,
            SingleDoseEquivUnit = SingleDoseEquivUnitInput,
            UsageAmount = QuantityInput.ToString(),
            UsageUnit = SelectedTotalUnit,
            Days = DaysInput,
            Quantity = QuantityInput,
            Unit = SelectedTotalUnit,
            PrescriptionPacks = PrescriptionPacksInput,
            Route = SelectedRoute,
            DripRate = SelectedDripRate,
            Frequency = SelectedFrequency,
            HospitalInjectionCount = HospitalInjectionCountInput,
            ExecDept = SelectedExecDept,
            DeductDept = SelectedDeductDept,
            UnitPrice = $"¥{unitPriceVal:F2}",
            TotalPrice = $"¥{totalPriceVal:F2}",
            Remark = RemarkInput,
            SpecialAttr = SelectedSpecialAttr,
            StartExecTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            DoctorName = Workbench.CurrentOperatorName,
            InputOperator = Workbench.CurrentOperatorName,
            OrderDept = patient.AdmittingDept,
            StopDoctor = "--",
            StopReason = "--",
            Specimen = SelectedItemCategory.Contains("检") ? "全血" : "--",
            BodyPart = "--",
            ExamMethod = "--",
            SkinTestInfo = (OrderContentInput.Contains("青霉素") || OrderContentInput.Contains("头孢")) ? "需皮试" : "免试",
            StopTime = "--",
            GroupComboNo = $"11317{Random.Shared.Next(1000, 9999)}",
            SeqNo = patient.Orders.Count + 1,
            Status = "待护士校对",
            NurseSign = "--",
            AlertStatus = "✓",
            AlertTooltip = "审方与配伍禁忌校验通过",
            AlertColor = "#10B981",
            ApprovalStatus = "APPROVED",
            ReviewInfo = $"[步骤A] 由医生 {Workbench.CurrentOperatorName} 于 {DateTime.Now:HH:mm} 开立，等待责任护士校对审签"
        };

        patient.Orders.Insert(0, order);
        patient.ApplyOrderFilter();
        Workbench.SyncInpatientBed(patient);
        Workbench.NotifyInpatientChanged(patient);

        // Post to server ordering API
        _ = PostOrderToServerAsync(patient, order);

        OrderContentInput = string.Empty;
        QuickSearchCode = string.Empty;
        RemarkInput = string.Empty;
        RaiseComputed();
    }

    private async Task PostOrderToServerAsync(InpatientRecordModel patient, InpatientOrderModel order)
    {
        var (ok, msg) = await Workbench.PostOrderToServerVerifiedAsync(patient, order, (decimal)SingleDoseInput);
        if (!ok)
        {
            patient.Orders.Remove(order);
            patient.ApplyOrderFilter();
            Workbench.SyncInpatientBed(patient);
            Workbench.NotifyInpatientChanged(patient);
            _shell?.ShowToast($"医嘱被合规门禁拦截：{msg}", "Error");
            RaiseComputed();
            return;
        }
        _shell?.ShowToast($"✓ {msg}", "Success");
        RaiseComputed();
    }

    private void ExecuteGroupSelectedOrders()
    {
        var patient = Workbench.SelectedInpatient;
        if (patient == null) return;
        var selected = patient.Orders.Where(x => x.IsSelected).ToList();
        if (selected.Count < 2)
        {
            _shell?.ShowToast("请勾选列表中至少2条连续医嘱进行输液配伍成组", "Warning");
            return;
        }

        var comboNo = $"11317{Random.Shared.Next(1000, 9999)}";
        for (int i = 0; i < selected.Count; i++)
        {
            selected[i].GroupComboNo = comboNo;
            if (i == 0) selected[i].GroupIcon = "┓";
            else if (i == selected.Count - 1) selected[i].GroupIcon = "┛";
            else selected[i].GroupIcon = "┃";
        }
        _ = Workbench.PersistOrderGroupingAsync(patient, selected, isUngroup: false);
        _shell?.ShowToast($"✓ 成功将 {selected.Count} 条医嘱配伍成组 (组合号: {comboNo})", "Success");
    }

    private void ExecuteUngroupSelectedOrders()
    {
        var patient = Workbench.SelectedInpatient;
        if (patient == null) return;
        var selected = patient.Orders.Where(x => x.IsSelected).ToList();
        foreach (var item in selected)
        {
            item.GroupIcon = "-";
            item.GroupNo = string.Empty;
            item.GroupComboNo = string.Empty;
        }
        _ = Workbench.PersistOrderGroupingAsync(patient, selected, isUngroup: true);
        _shell?.ShowToast("已解除所选医嘱的成组标记", "Info");
    }

    private void ExecuteQuickSetSkinTest(string status)
    {
        var patient = Workbench.SelectedInpatient;
        if (patient == null) return;
        var selected = patient.Orders.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0)
        {
            _shell?.ShowToast("请先在列表中勾选要设置皮试状态的医嘱行", "Warning");
            return;
        }
        foreach (var item in selected)
        {
            item.SkinTestInfo = status;
        }
        _shell?.ShowToast($"✓ 已将 {selected.Count} 条医嘱的皮试状态更新为【{status}】", "Success");
    }

    private void ExecuteToggleUrgent()
    {
        var patient = Workbench.SelectedInpatient;
        if (patient == null) return;
        var selected = patient.Orders.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0)
        {
            IsUrgentInput = !IsUrgentInput;
            return;
        }
        foreach (var item in selected)
        {
            item.IsUrgent = !item.IsUrgent;
        }
        _shell?.ShowToast($"已切换 {selected.Count} 条医嘱的加急标记", "Info");
    }

    private void ExecuteSelectAllOrders(bool select)
    {
        var patient = Workbench.SelectedInpatient;
        if (patient == null) return;
        foreach (var item in patient.FilteredOrders)
        {
            item.IsSelected = select;
        }
    }

    private void ExecuteToggleBannerExpand()
    {
        if (Workbench.SelectedInpatient != null)
        {
            Workbench.SelectedInpatient.IsBannerExpanded = !Workbench.SelectedInpatient.IsBannerExpanded;
        }
    }

    private void ExecutePreCheckOrders()
    {
        var patient = Workbench.SelectedInpatient;
        if (patient == null) return;

        bool hasAllergy = false;
        string conflictMsg = string.Empty;

        foreach (var ord in patient.Orders.Where(x => x.IsActive))
        {
            if (patient.HasAllergyAlert && !string.IsNullOrWhiteSpace(patient.AllergyTags))
            {
                if (patient.AllergyTags.Contains("青霉素") && (ord.Content.Contains("青霉素") || ord.Content.Contains("阿莫西林")))
                {
                    hasAllergy = true;
                    conflictMsg = $"医嘱 [{ord.Content}] 与患者既往过敏史【{patient.AllergyTags}】冲突！";
                    break;
                }
            }
        }

        if (hasAllergy)
        {
            _shell?.ShowToast($"⚠ 6维度前置审方阻断：{conflictMsg}", "Danger");
        }
        else
        {
            _shell?.ShowToast("✓ 6维度前置审方通过：过敏史、性别适用、ICD适应症、高危药品预警、相互作用校验全绿灯！", "Success");
        }
    }

    private void ExecuteSubmitOrdersToNurse()
    {
        var patient = Workbench.SelectedInpatient;
        if (patient == null) return;
        var pending = patient.Orders.Where(x => x.IsPendingReview).ToList();
        if (pending.Count == 0)
        {
            _shell?.ShowToast("当前患者暂无待提交的初开医嘱", "Info");
            return;
        }
        _shell?.ShowToast($"✓ 已将 {pending.Count} 条医嘱成功提交给病区责任护士工作站进行【步骤B：校对审签】！", "Success");
    }

    private void ExecuteAdmitInpatient()
    {
        Workbench.AdmitInpatient();
        _shell?.ShowToast(Workbench.SystemNotice, "Success");
        RaiseComputed();
    }

    private void ExecuteAddOrder(bool isLongTerm)
    {
        Workbench.AddInpatientOrder(isLongTerm);
        _shell?.ShowToast(Workbench.SystemNotice, "Info");
        RaiseComputed();
    }

    private bool CanSubmitOrder() =>
        Workbench.CanCreateInpatientOrder
        && (!string.IsNullOrWhiteSpace(Workbench.NewOrderContent) || !string.IsNullOrWhiteSpace(OrderContentInput));

    private void ExecuteSubmitOrder()
    {
        if (!string.IsNullOrWhiteSpace(OrderContentInput))
        {
            ExecuteAddQuickOrder();
        }
        else
        {
            ExecuteAddOrder(IsLongTermOrder);
        }
    }

    private void SelectWorkspace(object? value)
    {
        if (value is int index)
        {
            SelectedWorkspaceIndex = index;
        }
    }

    private void ApplyOrderFilter(string filter)
    {
        if (Workbench.SelectedInpatient != null)
        {
            Workbench.SelectedInpatient.SelectedOrderFilter = filter;
        }
    }

    private void ConfirmStopOrder(InpatientOrderModel? order)
    {
        if (order == null || !Workbench.CanCreateInpatientOrder) return;

        void Execute()
        {
            Workbench.StopInpatientOrder(order);
            _shell?.ShowToast(Workbench.SystemNotice, "Success");
            RaiseComputed();
        }

        if (_shell != null)
        {
            _shell.RequestConfirm(
                "确认停止长期医嘱？",
                $"停止医嘱「{order.Content} ({order.Frequency})」后将不再参与后续护士分解排班。",
                "确认停止",
                Execute);
            return;
        }

        Execute();
    }

    private void ConfirmCancelOrder(InpatientOrderModel? order)
    {
        if (order == null || !Workbench.CanCreateInpatientOrder) return;

        void Execute()
        {
            Workbench.CancelInpatientOrder(order);
            _shell?.ShowToast(Workbench.SystemNotice, "Warning");
            RaiseComputed();
        }

        if (_shell != null)
        {
            _shell.RequestConfirm(
                "确认作废医嘱？",
                $"作废尚未审签的医嘱「{order.Content}」后该记录不可恢复。",
                "确认作废",
                Execute);
            return;
        }

        Execute();
    }

    private void ExecuteAddProgressNote()
    {
        Workbench.AddProgressNote();
        _shell?.ShowToast(Workbench.SystemNotice, "Success");
        RaiseComputed();
    }

    private void ExecuteSignLatestNote()
    {
        if (Workbench.SelectedInpatient?.ProgressNotes.FirstOrDefault(x => !x.IsSigned) is { } note)
        {
            Workbench.SignProgressNote(note);
            _shell?.ShowToast(Workbench.SystemNotice, "Success");
            RaiseComputed();
        }
    }

    private void ExecuteSignNote(ProgressNoteModel? note)
    {
        if (note != null && Workbench.CanFinalizeInpatientOrders)
        {
            Workbench.SignProgressNote(note);
            _shell?.ShowToast(Workbench.SystemNotice, "Success");
            RaiseComputed();
        }
    }

    private void OpenDischargeCheckDrawer()
    {
        Workbench.PrepareDischargeCheck();
        if (_shell == null)
        {
            Workbench.IsDischargeCheckDrawerOpen = true;
            return;
        }

        var d = Workbench.DischargeCheckDetails;
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(new TextBlock
        {
            Text = $"{d.PatientHeader} · {d.BedCode} · {d.AdmissionNo}",
            FontSize = 13,
            FontWeight = Avalonia.Media.FontWeight.Bold,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        AddGateRow(stack, "1  医嘱闭环", d.OrdersGateText);
        AddGateRow(stack, "2  医技报告归档", d.MedTechGateText);
        AddGateRow(stack, "3  出院带药配发", d.MedsGateText);
        AddGateRow(stack, "4  病程全部审签", d.EmrGateText);
        AddGateRow(stack, "5  医保结算核验", d.InsuranceGateText);
        AddGateRow(stack, "6  预交金结清", d.PrepaymentGateText);

        stack.Children.Add(new TextBlock
        {
            Text = $"预交金: ¥{d.PrepaymentAmount:N2}  |  自付: ¥{d.ActualSelfPay:N2}  |  应退: ¥{d.RefundAmount:N2}",
            FontSize = 12,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Margin = new Avalonia.Thickness(0, 6, 0, 0)
        });
        stack.Children.Add(new TextBlock
        {
            Text = d.AllPassedText,
            FontSize = 12,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        _shell.OpenDrawer("出院六严关核查详情", "任一环节未通过均阻断出院办理", stack, 500);
    }

    private static void AddGateRow(StackPanel container, string label, string statusText)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("150,*")
        };
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        var val = new TextBlock
        {
            Text = statusText,
            FontSize = 12,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(val, 1);
        row.Children.Add(val);
        container.Children.Add(row);
    }

    private Task ApproveDischargeAsync()
    {
        if (!Workbench.CanApproveSelectedDischarge || Workbench.SelectedInpatient == null)
        {
            return Task.CompletedTask;
        }

        async Task ExecuteAsync()
        {
            await Workbench.ApproveDischargeAsync();
            _shell?.ShowToast(Workbench.SystemNotice, "Success");
            RaiseComputed();
        }

        if (_shell != null)
        {
            _shell.RequestConfirm(
                "确认提交出院申请？",
                $"患者 {Workbench.SelectedInpatient.PatientName} ({Workbench.SelectedInpatient.BedCode}) 将提交出院申请，后续由收费处完成六严关核验与出院结算。",
                "确认提交",
                () => _ = ExecuteAsync());
            return Task.CompletedTask;
        }

        return ExecuteAsync();
    }

    private void OnWorkbenchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkbenchModel.SelectedInpatient))
        {
            _ = Workbench.LoadSelectedEncounterDetailsAsync(Workbench.SelectedInpatient);
        }
        if (e.PropertyName is nameof(WorkbenchModel.SelectedInpatient)
            or nameof(WorkbenchModel.CanCreateInpatientOrder)
            or nameof(WorkbenchModel.CanSignSelectedProgressNotes)
            or nameof(WorkbenchModel.CanFinalizeInpatientOrders)
            or nameof(WorkbenchModel.CanApproveSelectedDischarge))
        {
            RaiseComputed();
        }
    }

    private void RaiseComputed()
    {
        OnPropertyChanged(nameof(OrderCreateBlockedReason));
        OnPropertyChanged(nameof(DischargeBlockedReason));
        (AddLongOrderCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (AddTempOrderCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SubmitOrderCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (AddQuickOrderCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SignLatestProgressNoteCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ApproveDischargeCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    public async Task LoadUserTemplatesAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.GetFromJsonAsync<ClientApiEnvelope<List<UserTemplateDto>>>("/api/v4/emr/user-templates?emrScene=IPD");
            if (res?.Code == 200 && res.Data != null)
            {
                UserTemplates.Clear();
                foreach (var t in res.Data)
                {
                    UserTemplates.Add(new EmrUserTemplateModel
                    {
                        Id = t.Id,
                        UserId = t.UserId,
                        UserName = t.UserName,
                        DeptId = t.DeptId,
                        DeptName = t.DeptName,
                        BaseTemplateId = t.BaseTemplateId,
                        TemplateCode = t.TemplateCode,
                        TemplateName = t.TemplateName,
                        EmrScene = t.EmrScene,
                        DocTypeCode = t.DocTypeCode,
                        DefaultContentJson = t.DefaultContentJson,
                        IsSharedDept = t.IsSharedDept == 1,
                        SortOrder = t.SortOrder
                    });
                }
            }
        }
        catch { }
    }

    private void ExecuteApplyTemplate(EmrUserTemplateModel? tpl)
    {
        if (tpl == null || string.IsNullOrWhiteSpace(tpl.DefaultContentJson)) return;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(tpl.DefaultContentJson);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("PROGRESS_CONTENT", out var p) && p.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    Workbench.NewProgressNote = p.GetString() ?? string.Empty;
                }
                else if (doc.RootElement.TryGetProperty("CHIEF_COMPLAINT", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var text = $"【主诉】{c.GetString()}\n";
                    if (doc.RootElement.TryGetProperty("PRESENT_ILLNESS", out var pi))
                    {
                        text += $"【现病史】{pi.GetString()}\n";
                    }
                    if (doc.RootElement.TryGetProperty("ADMISSION_DIAGNOSIS", out var diag))
                    {
                        text += $"【初步诊断】{diag.GetString()}\n";
                    }
                    if (doc.RootElement.TryGetProperty("TREATMENT_PLAN", out var plan))
                    {
                        text += $"【诊疗计划】{plan.GetString()}";
                    }
                    Workbench.NewProgressNote = text;
                }
            }
            _shell?.ShowToast($"已调入模板 [{tpl.TemplateName}]", "Success");
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"调入模板失败: {ex.Message}", "Error");
        }
    }

    public async Task SaveAsUserTemplateAsync()
    {
        if (string.IsNullOrWhiteSpace(Workbench.NewProgressNote))
        {
            _shell?.ShowToast("当前病程记录内容为空，无法保存为常用模板", "Warning");
            return;
        }

        try
        {
            var contentObj = new Dictionary<string, string>
            {
                ["PROGRESS_CONTENT"] = Workbench.NewProgressNote.Trim()
            };
            var json = System.Text.Json.JsonSerializer.Serialize(contentObj);
            var doctorName = Workbench.SelectedInpatient?.AttendingDoctor ?? "临床医师";
            var tplName = $"{doctorName}常用查房模板 ({DateTime.Now:MM-dd HH:mm})";

            var req = new
            {
                BaseTemplateId = "TMPL-IPD-PROGRESS",
                TemplateCode = $"UTPL_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}",
                TemplateName = tplName,
                EmrScene = "IPD",
                DocTypeCode = "IPD_PROGRESS",
                DefaultContentJson = json,
                IsSharedDept = true,
                SortOrder = 1
            };

            using var http = AuthenticatedHttpClientFactory.Create();
            var response = await http.PostAsJsonAsync("/api/v4/emr/user-templates", req);
            var res = await response.Content.ReadFromJsonAsync<ClientApiEnvelope<UserTemplateDto>>();
            if (res?.Code == 200 && res.Data != null)
            {
                _shell?.ShowToast($"成功保存为常用模板: [{tplName}]", "Success");
                await LoadUserTemplatesAsync();
            }
            else
            {
                _shell?.ShowToast(res?.Message ?? "保存常用模板失败", "Warning");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"保存模板异常: {ex.Message}", "Error");
        }
    }

    public sealed record UserTemplateDto(
        string Id,
        string UserId,
        string UserName,
        string? DeptId,
        string? DeptName,
        string BaseTemplateId,
        string TemplateCode,
        string TemplateName,
        string EmrScene,
        string DocTypeCode,
        string DefaultContentJson,
        short IsSharedDept,
        int SortOrder
    );
}
