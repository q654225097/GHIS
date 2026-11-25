using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Printing;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.Shell.Controls;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.OpdDoctor;

public sealed record QuickDiagnosisItem(string Code, string Name, string Category);

public sealed partial class OpdDoctorViewModel : ClientModelBase, IShortcutTarget, IPrintContextProvider
{
    private readonly ShellViewModel? _shell;
    private Action? _focusDrugSearchCallback;
    private string _drugSearchQuery = string.Empty;
    private bool _isDrugDropdownOpen;
    private int _selectedCatalogIndex;
    private string _selectedDiagnosisCategory = "全部";

    public OpdDoctorViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;
        Workbench.PropertyChanged += OnWorkbenchPropertyChanged;

        CallNextCommand = new AsyncRelayCommand(CallNextPatientAsync);
        RecallCommand = new AsyncRelayCommand(RecallPatientAsync, () => Workbench.HasSelectedPatient);
        PassCommand = new AsyncRelayCommand(PassPatientAsync, () => Workbench.HasSelectedPatient);
        SelectPatientCommand = new RelayCommand(param =>
        {
            if (param is PatientQueueItemModel patient)
            {
                Workbench.SelectedPatient = patient;
                Workbench.SelectedOpdTabIndex = 0;
            }
        });

        SelectStepTabCommand = new RelayCommand(param =>
        {
            if (param is int idx) Workbench.SelectedOpdTabIndex = idx;
            else if (param is string s && int.TryParse(s, out var parsed)) Workbench.SelectedOpdTabIndex = parsed;
        });

        SignEmrCommand = new AsyncRelayCommand(SignCurrentEmrAsync, () => Workbench.CanSignEmr && Workbench.HasSelectedPatient);

        DiagnosisCategories.Add("全部");
        DiagnosisCategories.Add("心脑血管");
        DiagnosisCategories.Add("呼吸内科");
        DiagnosisCategories.Add("消化系统");
        DiagnosisCategories.Add("内分泌/代谢");
        DiagnosisCategories.Add("神经/其他");

        QuickDiagnoses.Add(new QuickDiagnosisItem("I10.x00", "原发性高血压", "心脑血管"));
        QuickDiagnoses.Add(new QuickDiagnosisItem("I20.900", "心绞痛", "心脑血管"));
        QuickDiagnoses.Add(new QuickDiagnosisItem("I25.101", "冠心病", "心脑血管"));
        QuickDiagnoses.Add(new QuickDiagnosisItem("I49.900", "心律失常", "心脑血管"));
        QuickDiagnoses.Add(new QuickDiagnosisItem("I50.900", "心力衰竭", "心脑血管"));
        QuickDiagnoses.Add(new QuickDiagnosisItem("J06.900", "急性上呼吸道感染", "呼吸内科"));
        QuickDiagnoses.Add(new QuickDiagnosisItem("J20.900", "急性支气管炎", "呼吸内科"));
        QuickDiagnoses.Add(new QuickDiagnosisItem("J45.900", "支气管哮喘", "呼吸内科"));
        QuickDiagnoses.Add(new QuickDiagnosisItem("J18.900", "社区获得性肺炎", "呼吸内科"));
        QuickDiagnoses.Add(new QuickDiagnosisItem("J44.900", "慢性阻塞性肺疾病", "呼吸内科"));
        QuickDiagnoses.Add(new QuickDiagnosisItem("K29.700", "慢性胃炎", "消化系统"));
        QuickDiagnoses.Add(new QuickDiagnosisItem("K21.900", "胃食管反流病", "消化系统"));
        QuickDiagnoses.Add(new QuickDiagnosisItem("K25.900", "消化性溃疡", "消化系统"));
        QuickDiagnoses.Add(new QuickDiagnosisItem("K52.900", "急性胃肠炎", "消化系统"));
        QuickDiagnoses.Add(new QuickDiagnosisItem("K76.000", "非酒精性脂肪肝", "消化系统"));
        QuickDiagnoses.Add(new QuickDiagnosisItem("E11.900", "2型糖尿病", "内分泌/代谢"));
        QuickDiagnoses.Add(new QuickDiagnosisItem("E78.500", "高脂血症", "内分泌/代谢"));
        QuickDiagnoses.Add(new QuickDiagnosisItem("E79.000", "高尿酸血症", "内分泌/代谢"));
        QuickDiagnoses.Add(new QuickDiagnosisItem("E03.900", "甲状腺功能减退症", "内分泌/代谢"));
        QuickDiagnoses.Add(new QuickDiagnosisItem("G43.900", "偏头痛", "神经/其他"));
        QuickDiagnoses.Add(new QuickDiagnosisItem("G47.000", "失眠症", "神经/其他"));
        QuickDiagnoses.Add(new QuickDiagnosisItem("J30.400", "过敏性鼻炎", "神经/其他"));
        QuickDiagnoses.Add(new QuickDiagnosisItem("M19.900", "骨关节炎", "神经/其他"));
        QuickDiagnoses.Add(new QuickDiagnosisItem("M54.500", "腰痛症", "神经/其他"));

        RefreshFilteredQuickDiagnoses();

        SelectQuickDiagnosisCommand = new RelayCommand(param =>
        {
            if (param is QuickDiagnosisItem item)
            {
                Workbench.SetDiagnosis(item.Code, item.Name);
                NotifyToast($"已确立诊断: [{item.Code}] {item.Name}", "Success");
                RefreshAllWorkflowState();
            }
        });

        FilterDiagnosisCategoryCommand = new RelayCommand(param =>
        {
            if (param is string cat)
            {
                SelectedDiagnosisCategory = cat;
            }
        });

        SaveDiagnosisCommand = new AsyncRelayCommand(async () =>
        {
            var name = Workbench.DiagnosisName ?? Workbench.EmrWorkspace.Diagnosis;
            if (string.IsNullOrWhiteSpace(name))
            {
                NotifyToast("请先选择或输入初步诊断", "Warning");
                return;
            }
            var outcome = await Workbench.SaveOpdDiagnosisAsync(name);
            NotifyToast(outcome.Message, outcome.Succeeded ? "Success" : "Warning");
            RefreshAllWorkflowState();
        }, () => Workbench.CanEditDiagnosis);

        SaveDiagnosisAndNextCommand = new AsyncRelayCommand(async () =>
        {
            var name = Workbench.DiagnosisName ?? Workbench.EmrWorkspace.Diagnosis;
            if (string.IsNullOrWhiteSpace(name))
            {
                NotifyToast("请先选择或输入初步诊断后再进入处方开立", "Warning");
                return;
            }
            var outcome = await Workbench.SaveOpdDiagnosisAsync(name);
            NotifyToast(outcome.Message, outcome.Succeeded ? "Success" : "Warning");
            if (outcome.Succeeded)
            {
                Workbench.SelectedOpdTabIndex = 1;
                RefreshDrugSearchResults();
                _focusDrugSearchCallback?.Invoke();
            }
            RefreshAllWorkflowState();
        }, () => Workbench.CanEditDiagnosis);

        GoToDiagnosisCommand = new RelayCommand(() => Workbench.SelectedOpdTabIndex = 0);
        GoToPrescriptionCommand = new RelayCommand(() =>
        {
            Workbench.SelectedOpdTabIndex = 1;
            RefreshDrugSearchResults();
            _focusDrugSearchCallback?.Invoke();
        });
        GoToMedTechCommand = new RelayCommand(() => Workbench.SelectedOpdTabIndex = 2);
        GoToEmrCommand = new RelayCommand(() => Workbench.SelectedOpdTabIndex = 3);

        AddDrugCatalogItemCommand = new RelayCommand(param =>
        {
            if (param is DrugCatalogItemModel drug)
            {
                AddCatalogDrugToPrescription(drug);
            }
        });

        FilterPrescriptionsCommand = new RelayCommand(param =>
        {
            if (param is string mode)
            {
                Workbench.SelectedPrescriptionFilter = mode;
            }
        });

        BeginEditItemCommand = new RelayCommand(param =>
        {
            if (param is PrescriptionItemModel item) Workbench.BeginEditPrescriptionItem(item);
        });

        SaveEditItemCommand = new AsyncRelayCommand(async param =>
        {
            if (param is PrescriptionItemModel item)
            {
                await Workbench.CommitPrescriptionItemAsync(item);
                _focusDrugSearchCallback?.Invoke();
            }
        });

        CancelEditItemCommand = new RelayCommand(param =>
        {
            if (param is PrescriptionItemModel item) Workbench.CancelEditPrescriptionItem(item);
        });

        DeleteItemWithUndoCommand = new AsyncRelayCommand(async param =>
        {
            if (param is PrescriptionItemModel item)
            {
                await SoftDeleteWithUndoAsync(item);
            }
        });

        AutoReplaceBlockedDrugCommand = new RelayCommand(AutoReplaceBlockedDrug);
        PreCheckPrescriptionCommand = new AsyncRelayCommand(PreCheckPrescriptionAsync, () => Workbench.CanEditOpdPrescription);
        SubmitPrescriptionCommand = new AsyncRelayCommand(SubmitPrescriptionAsync, () => Workbench.CanEditOpdPrescription);
        SettlePrescriptionInClinicCommand = new AsyncRelayCommand(SettlePrescriptionInClinicAsync);

        OpenEmrTemplatePickerCommand = new RelayCommand(OpenTemplatePickerDrawer);
        SelectEmrTemplateCommand = new AsyncRelayCommand(async param =>
        {
            if (param is OpdEmrTemplateModel tmpl)
            {
                await ApplyTemplateAsync(tmpl);
            }
        });
        NewEmrDocumentCommand = new AsyncRelayCommand(async () =>
        {
            if (Workbench.SelectedOpdEmrTemplate != null)
            {
                var outcome = await Workbench.CreateNewOpdEmrDocumentAsync(Workbench.SelectedOpdEmrTemplate);
                NotifyToast(outcome.Message, outcome.Succeeded ? "Success" : "Warning");
                RefreshAllWorkflowState();
            }
            else
            {
                OpenTemplatePickerDrawer();
            }
        });
        SaveEmrDraftCommand = new AsyncRelayCommand(async () =>
        {
            var outcome = await Workbench.SaveOpdEmrDraftAsync();
            NotifyToast(outcome.Message, outcome.Succeeded ? "Success" : "Warning");
            RefreshAllWorkflowState();
        }, () => Workbench.HasSelectedPatient && Workbench.IsOpdVisitActive);

        OpenSaveAsUserTemplateCommand = new RelayCommand(OpenSaveAsUserTemplateDrawer);
        ApplyUserTemplateCommand = new AsyncRelayCommand(async param =>
        {
            if (param is EmrUserTemplateModel tpl)
            {
                var outcome = await Workbench.ApplyOpdUserTemplateAsync(tpl);
                NotifyToast(outcome.Message, outcome.Succeeded ? "Success" : "Warning");
                RefreshAllWorkflowState();
                OnPropertyChanged(nameof(HasSelectedEmrTemplate));
                OnPropertyChanged(nameof(SelectedEmrTemplateName));
            }
        });
        DeleteUserTemplateCommand = new AsyncRelayCommand(async param =>
        {
            if (param is EmrUserTemplateModel tpl)
            {
                var outcome = await Workbench.DeleteOpdUserTemplateAsync(tpl.Id);
                NotifyToast(outcome.Message, outcome.Succeeded ? "Success" : "Warning");
            }
        });

        AddMedTechOrderCommand = new AsyncRelayCommand(OpenRealMedTechCatalogDrawerAsync);
        OpenWhoSurgeryDrawerCommand = new RelayCommand(OpenWhoSurgeryDrawer);
        OpenRefundProofDrawerCommand = new RelayCommand(OpenRefundProofDrawer);
        OpenBorrowBedDrawerCommand = new RelayCommand(OpenBorrowBedDrawer);
        RequestFinishVisitCommand = new RelayCommand(
            OpenFinishVisitChecklistDrawer,
            () => Workbench.CanFinishOpdVisit || Workbench.CanCompleteOpdWithNoPrescription);

        AddQuickOrderCommand = new RelayCommand(AddQuickOrder);
        GroupSelectedOrdersCommand = new RelayCommand(GroupSelectedOrders);
        UngroupSelectedOrdersCommand = new RelayCommand(UngroupSelectedOrders);
        QuickSetSkinTestCommand = new RelayCommand(param =>
        {
            if (param is string status) SetSkinTestStatus(status);
        });
        ToggleUrgentCommand = new RelayCommand(ToggleUrgent);
        SelectAllOrdersCommand = new RelayCommand(param =>
        {
            if (param is bool b) SelectAllOrders(b);
            else SelectAllOrders(true);
        });

        LoadQuickFormulaCommand = new RelayCommand(param =>
        {
            if (param is string formula) LoadQuickTcmFormula(formula);
        });
        AddCustomHerbCommand = new RelayCommand(AddCustomTcmHerb);
        RemoveTcmHerbCommand = new RelayCommand(param =>
        {
            if (param is TcmHerbItemModel item)
            {
                TcmHerbItems.Remove(item);
                RefreshTcmTotals();
            }
        });
        SubmitTcmPrescriptionCommand = new AsyncRelayCommand(SubmitTcmPrescriptionAsync);
        LoadQuickTcmFormula("SI_JUN_ZI");

        RefreshDrugSearchResults();
        RefreshSteps();
        _ = Workbench.EnsureOpdEmrTemplatesAsync();
    }

    public WorkbenchModel Workbench { get; }
    public ObservableCollection<DrugCatalogItemModel> FilteredDrugCatalog { get; } = new();
    public ObservableCollection<FlowStepItem> WorkflowSteps { get; } = new();
    public ObservableCollection<QuickDiagnosisItem> QuickDiagnoses { get; } = new();
    public ObservableCollection<QuickDiagnosisItem> FilteredQuickDiagnoses { get; } = new();
    public ObservableCollection<string> DiagnosisCategories { get; } = new();

    public bool HasSelectedEmrTemplate => Workbench.HasSelectedOpdEmrTemplate;
    public string SelectedEmrTemplateName => Workbench.SelectedOpdEmrTemplate?.TemplateName ?? "尚未选取门诊文书模板";

    public ICommand OpenEmrTemplatePickerCommand { get; }
    public ICommand SelectEmrTemplateCommand { get; }
    public ICommand NewEmrDocumentCommand { get; }
    public ICommand SaveEmrDraftCommand { get; }
    public ICommand OpenSaveAsUserTemplateCommand { get; }
    public ICommand ApplyUserTemplateCommand { get; }
    public ICommand DeleteUserTemplateCommand { get; }

    public string SelectedDiagnosisCategory
    {
        get => _selectedDiagnosisCategory;
        set
        {
            if (SetField(ref _selectedDiagnosisCategory, value, nameof(SelectedDiagnosisCategory)))
            {
                RefreshFilteredQuickDiagnoses();
            }
        }
    }

    public void RefreshFilteredQuickDiagnoses()
    {
        FilteredQuickDiagnoses.Clear();
        var selected = SelectedDiagnosisCategory;
        foreach (var item in QuickDiagnoses)
        {
            if (string.Equals(selected, "全部", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Category, selected, StringComparison.OrdinalIgnoreCase))
            {
                FilteredQuickDiagnoses.Add(item);
            }
        }
    }

    public ICommand CallNextCommand { get; }
    public ICommand RecallCommand { get; }
    public ICommand PassCommand { get; }
    public ICommand SelectPatientCommand { get; }
    public ICommand SelectStepTabCommand { get; }
    public ICommand SelectQuickDiagnosisCommand { get; }
    public ICommand FilterDiagnosisCategoryCommand { get; }
    public ICommand SaveDiagnosisCommand { get; }
    public ICommand SaveDiagnosisAndNextCommand { get; }
    public ICommand GoToDiagnosisCommand { get; }
    public ICommand GoToPrescriptionCommand { get; }
    public ICommand GoToEmrCommand { get; }
    public ICommand GoToMedTechCommand { get; }
    public ICommand SignEmrCommand { get; }
    public ICommand AddDrugCatalogItemCommand { get; }
    public ICommand FilterPrescriptionsCommand { get; }
    public ICommand BeginEditItemCommand { get; }
    public ICommand SaveEditItemCommand { get; }
    public ICommand CancelEditItemCommand { get; }
    public ICommand DeleteItemWithUndoCommand { get; }
    public ICommand AutoReplaceBlockedDrugCommand { get; }
    public ICommand PreCheckPrescriptionCommand { get; }
    public ICommand SubmitPrescriptionCommand { get; }
    public ICommand SettlePrescriptionInClinicCommand { get; }

    private decimal _patientCardBalance = 0m;
    public decimal PatientCardBalance
    {
        get => _patientCardBalance;
        set
        {
            if (SetField(ref _patientCardBalance, value, nameof(PatientCardBalance)))
            {
                OnPropertyChanged(nameof(PatientCardBalanceText));
                OnPropertyChanged(nameof(CanSettleInClinic));
            }
        }
    }

    public string PatientCardBalanceText => $"就诊卡余额: ¥{PatientCardBalance:0.00}";
    public bool CanSettleInClinic => Workbench.HasSelectedPatient && Workbench.Prescriptions.Any(x => !x.IsDeleted && !x.IsPaid);
    public ICommand AddMedTechOrderCommand { get; }
    public ICommand OpenWhoSurgeryDrawerCommand { get; }
    public ICommand OpenRefundProofDrawerCommand { get; }
    public ICommand OpenBorrowBedDrawerCommand { get; }
    public ICommand RequestFinishVisitCommand { get; }
    public ICommand AddQuickOrderCommand { get; }
    public ICommand GroupSelectedOrdersCommand { get; }
    public ICommand UngroupSelectedOrdersCommand { get; }
    public ICommand QuickSetSkinTestCommand { get; }
    public ICommand ToggleUrgentCommand { get; }
    public ICommand SelectAllOrdersCommand { get; }

    public ObservableCollection<string> OrderTypeOptions { get; } = new()
    {
        "全部", "西药", "中成药", "草药", "检验", "检查", "治疗", "材料"
    };

    public ObservableCollection<TcmHerbItemModel> TcmHerbItems { get; } = new();

    private int _tcmDoseCount = 7;
    public int TcmDoseCount
    {
        get => _tcmDoseCount;
        set
        {
            if (SetField(ref _tcmDoseCount, value, nameof(TcmDoseCount)))
            {
                foreach (var h in TcmHerbItems) h.DoseCount = value;
                RefreshTcmTotals();
            }
        }
    }

    private string _tcmRoute = "水煎服";
    public string TcmRoute
    {
        get => _tcmRoute;
        set => SetField(ref _tcmRoute, value, nameof(TcmRoute));
    }

    private string _tcmFrequency = "每日2次，早晚分服";
    public string TcmFrequency
    {
        get => _tcmFrequency;
        set => SetField(ref _tcmFrequency, value, nameof(TcmFrequency));
    }

    private bool _tcmRequiresDecoction = true;
    public bool TcmRequiresDecoction
    {
        get => _tcmRequiresDecoction;
        set
        {
            if (SetField(ref _tcmRequiresDecoction, value, nameof(TcmRequiresDecoction)))
            {
                RefreshTcmTotals();
            }
        }
    }

    private decimal _tcmDecoctionUnitFee = 5.0m;
    public decimal TcmDecoctionUnitFee
    {
        get => _tcmDecoctionUnitFee;
        set
        {
            if (SetField(ref _tcmDecoctionUnitFee, value, nameof(TcmDecoctionUnitFee)))
            {
                RefreshTcmTotals();
            }
        }
    }

    private decimal _tcmHerbsTotalAmount;
    public decimal TcmHerbsTotalAmount
    {
        get => _tcmHerbsTotalAmount;
        private set => SetField(ref _tcmHerbsTotalAmount, value, nameof(TcmHerbsTotalAmount));
    }

    private decimal _tcmDecoctionTotalFee;
    public decimal TcmDecoctionTotalFee
    {
        get => _tcmDecoctionTotalFee;
        private set => SetField(ref _tcmDecoctionTotalFee, value, nameof(TcmDecoctionTotalFee));
    }

    private decimal _tcmGrandTotal;
    public decimal TcmGrandTotal
    {
        get => _tcmGrandTotal;
        private set => SetField(ref _tcmGrandTotal, value, nameof(TcmGrandTotal));
    }

    public ICommand LoadQuickFormulaCommand { get; }
    public ICommand AddCustomHerbCommand { get; }
    public ICommand RemoveTcmHerbCommand { get; }
    public ICommand SubmitTcmPrescriptionCommand { get; }

    private string _selectedOrderType = "西药";
    public string SelectedOrderType
    {
        get => _selectedOrderType;
        set
        {
            if (SetField(ref _selectedOrderType, value, nameof(SelectedOrderType)))
            {
                RefreshDrugSearchResults();
            }
        }
    }

    private string _quickSearchCode = string.Empty;
    public string QuickSearchCode
    {
        get => _quickSearchCode;
        set
        {
            if (SetField(ref _quickSearchCode, value, nameof(QuickSearchCode)))
            {
                DrugSearchQuery = value;
            }
        }
    }

    private int _orderQuantityInput = 1;
    public int OrderQuantityInput
    {
        get => _orderQuantityInput;
        set => SetField(ref _orderQuantityInput, Math.Max(1, value), nameof(OrderQuantityInput));
    }

    public ObservableCollection<string> TotalUnitOptions { get; } = new()
    {
        "盒", "瓶", "支", "袋", "片", "粒", "付", "次"
    };

    private string _selectedTotalUnit = "盒";
    public string SelectedTotalUnit
    {
        get => _selectedTotalUnit;
        set => SetField(ref _selectedTotalUnit, value, nameof(SelectedTotalUnit));
    }

    private bool _isUrgentInput = false;
    public bool IsUrgentInput
    {
        get => _isUrgentInput;
        set => SetField(ref _isUrgentInput, value, nameof(IsUrgentInput));
    }

    private decimal _singleDoseInput = 1m;
    public decimal SingleDoseInput
    {
        get => _singleDoseInput;
        set => SetField(ref _singleDoseInput, value, nameof(SingleDoseInput));
    }

    public ObservableCollection<string> DoseUnitOptions { get; } = new()
    {
        "片", "粒", "g", "mg", "ml", "支", "袋", "次"
    };

    private string _selectedDoseUnit = "片";
    public string SelectedDoseUnit
    {
        get => _selectedDoseUnit;
        set => SetField(ref _selectedDoseUnit, value, nameof(SelectedDoseUnit));
    }

    private decimal _packageRatioAmount = 0.250m;
    public decimal PackageRatioAmount
    {
        get => _packageRatioAmount;
        set => SetField(ref _packageRatioAmount, value, nameof(PackageRatioAmount));
    }

    private string _packageRatioUnit = "g";
    public string PackageRatioUnit
    {
        get => _packageRatioUnit;
        set => SetField(ref _packageRatioUnit, value, nameof(PackageRatioUnit));
    }

    public ObservableCollection<string> RouteOptions { get; } = new()
    {
        "口服", "静脉滴注", "肌肉注射", "皮下注射", "外用", "雾化吸入", "含服", "直肠给药"
    };

    private string _selectedRoute = "口服";
    public string SelectedRoute
    {
        get => _selectedRoute;
        set => SetField(ref _selectedRoute, value, nameof(SelectedRoute));
    }

    public ObservableCollection<string> FrequencyOptions { get; } = new()
    {
        "qd", "bid", "tid", "qid", "q8h", "q12h", "st", "prn"
    };

    private string _selectedFrequency = "tid";
    public string SelectedFrequency
    {
        get => _selectedFrequency;
        set => SetField(ref _selectedFrequency, value, nameof(SelectedFrequency));
    }

    private int _daysInput = 3;
    public int DaysInput
    {
        get => _daysInput;
        set => SetField(ref _daysInput, Math.Max(1, value), nameof(DaysInput));
    }

    private int _paymentCountInput = 1;
    public int PaymentCountInput
    {
        get => _paymentCountInput;
        set => SetField(ref _paymentCountInput, Math.Max(1, value), nameof(PaymentCountInput));
    }

    private string _remarkInput = string.Empty;
    public string RemarkInput
    {
        get => _remarkInput;
        set => SetField(ref _remarkInput, value, nameof(RemarkInput));
    }

    public ObservableCollection<string> DeductDeptOptions { get; } = new()
    {
        "门诊西药房", "门诊中药房", "急诊药房", "门诊注射室", "静配中心 (PIVAS)", "耗材库"
    };

    private string _selectedDeductDept = "门诊西药房";
    public string SelectedDeductDept
    {
        get => _selectedDeductDept;
        set => SetField(ref _selectedDeductDept, value, nameof(SelectedDeductDept));
    }

    private int _hospitalInjectionCountInput = 0;
    public int HospitalInjectionCountInput
    {
        get => _hospitalInjectionCountInput;
        set => SetField(ref _hospitalInjectionCountInput, Math.Max(0, value), nameof(HospitalInjectionCountInput));
    }

    private string _examBodyPartInput = string.Empty;
    public string ExamBodyPartInput
    {
        get => _examBodyPartInput;
        set => SetField(ref _examBodyPartInput, value, nameof(ExamBodyPartInput));
    }

    private string _specimenTypeInput = string.Empty;
    public string SpecimenTypeInput
    {
        get => _specimenTypeInput;
        set => SetField(ref _specimenTypeInput, value, nameof(SpecimenTypeInput));
    }

    public void AttachFocusDrugSearch(Action focusCallback)
    {
        _focusDrugSearchCallback = focusCallback;
    }

    public string DrugSearchQuery
    {
        get => _drugSearchQuery;
        set
        {
            if (SetField(ref _drugSearchQuery, value, nameof(DrugSearchQuery)))
            {
                RefreshDrugSearchResults();
                IsDrugDropdownOpen = !string.IsNullOrWhiteSpace(value) && FilteredDrugCatalog.Count > 0;
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

    public bool IsFilterAll => string.Equals(Workbench.SelectedPrescriptionFilter, "ALL", StringComparison.OrdinalIgnoreCase);
    public bool IsFilterNormal => string.Equals(Workbench.SelectedPrescriptionFilter, "NORMAL", StringComparison.OrdinalIgnoreCase);
    public bool IsFilterAlert => string.Equals(Workbench.SelectedPrescriptionFilter, "ALERT", StringComparison.OrdinalIgnoreCase);

    public string WorkflowBlockedReason
    {
        get
        {
            if (Workbench.SelectedPatient == null) return "请先选择或叫号一位门诊患者";
            if (Workbench.SelectedPatient.IsCompleted) return "该患者本次就诊已完成";
            if (!Workbench.IsOpdVisitActive) return "请点击“叫号下一位 (F2)”开启患者接诊";
            if (!Workbench.HasDiagnosis) return "待完成步骤 1：请先在“诊断录入”确立初步诊断";
            if (!Workbench.IsOpdOrdersPushed && Workbench.Prescriptions.Any(x => !x.IsDeleted)) return "待完成步骤 2：请开立处方并提交药师审方 (Ctrl+Enter)";
            if (!Workbench.IsOpdEmrSigned) return "待完成步骤 4：请书写门诊病历并点击“保存并提交审签 (Ctrl+S)”";
            return string.Empty;
        }
    }

    public string PrescriptionEditBlockedReason =>
        Workbench.CanEditOpdPrescription
            ? string.Empty
            : (!Workbench.HasSelectedPatient
                ? "需先选中接诊患者后方可开立处方"
                : !Workbench.IsOpdVisitActive
                    ? "需先叫号接诊患者"
                    : !Workbench.HasDiagnosis
                        ? "请先在“诊断录入”确立初步诊断后再开立处方"
                        : "当前处方已锁定推送，仅可在药师驳回后继续修改");

    public string SignEmrBlockedReason =>
        !Workbench.HasSelectedPatient
            ? "需先选中接诊患者"
            : !Workbench.CanSignEmr
                ? "当前登录角色无病历审签权限"
                : string.Empty;

    public string FinishVisitBlockedReason =>
        Workbench.CanFinishOpdVisit || Workbench.CanCompleteOpdWithNoPrescription
            ? string.Empty
            : WorkflowBlockedReason;

    public bool OnShortcut(string keyGesture)
    {
        switch (keyGesture)
        {
            case "1":
            case "F1":
                Workbench.SelectedOpdTabIndex = 0;
                return true;
            case "2":
                Workbench.SelectedOpdTabIndex = 1;
                return true;
            case "3":
                Workbench.SelectedOpdTabIndex = 2;
                return true;
            case "4":
                Workbench.SelectedOpdTabIndex = 3;
                return true;
            case "F2":
                _ = CallNextPatientAsync();
                return true;
            case "Ctrl+S":
                if (Workbench.CanSignEmr && Workbench.HasSelectedPatient)
                {
                    _ = SignCurrentEmrAsync();
                    return true;
                }
                return false;
            case "Ctrl+Enter":
                if (Workbench.CanEditOpdPrescription)
                {
                    _ = SubmitPrescriptionAsync();
                    return true;
                }
                return false;
            case "/":
                Workbench.SelectedOpdTabIndex = 1;
                RefreshDrugSearchResults();
                IsDrugDropdownOpen = true;
                _focusDrugSearchCallback?.Invoke();
                return true;
            case "Esc":
                if (IsDrugDropdownOpen)
                {
                    IsDrugDropdownOpen = false;
                    return true;
                }
                return false;
            default:
                return false;
        }
    }

    public void MoveDrugSelection(int delta)
    {
        if (FilteredDrugCatalog.Count == 0) return;
        IsDrugDropdownOpen = true;
        var next = SelectedCatalogIndex + delta;
        if (next < 0) next = FilteredDrugCatalog.Count - 1;
        if (next >= FilteredDrugCatalog.Count) next = 0;
        SelectedCatalogIndex = next;
    }

    public void AddSelectedCatalogDrug()
    {
        if (FilteredDrugCatalog.Count == 0) return;
        var idx = Math.Clamp(SelectedCatalogIndex, 0, FilteredDrugCatalog.Count - 1);
        AddCatalogDrugToPrescription(FilteredDrugCatalog[idx]);
    }

    public void RefreshDrugSearchResults()
    {
        FilteredDrugCatalog.Clear();
        var q = DrugSearchQuery?.Trim() ?? string.Empty;
        var matches = Workbench.AvailableDrugsCatalog
            .Where(d => string.IsNullOrEmpty(q) ||
                        d.DrugName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        d.DrugCode.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        d.Spec.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Take(8);

        foreach (var d in matches)
        {
            FilteredDrugCatalog.Add(d);
        }
        SelectedCatalogIndex = 0;
    }

    private void AddCatalogDrugToPrescription(DrugCatalogItemModel drug)
    {
        var added = Workbench.AddPrescriptionFromCatalog(drug);
        IsDrugDropdownOpen = false;
        DrugSearchQuery = string.Empty;

        if (added != null)
        {
            Workbench.BeginEditPrescriptionItem(added);
            NotifyToast($"已加入处方: {drug.DrugName}，请确认单次剂量与频次后保存", added.IsBlocked ? "Danger" : "Success");
        }
        else
        {
            NotifyToast(Workbench.SystemNotice, "Warning");
        }
        RefreshAllWorkflowState();
    }

    private async Task SoftDeleteWithUndoAsync(PrescriptionItemModel item)
    {
        var drugName = item.DrugName;
        await Workbench.SoftDeletePrescriptionItemAsync(item);
        RefreshAllWorkflowState();

        if (_shell != null)
        {
            _shell.ShowToast(
                $"已从处方中移除 [{drugName}]",
                "Warning",
                actionText: "撤销",
                onAction: () =>
                {
                    item.IsDeleted = false;
                    item.DeletedAtMs = null;
                    Workbench.RefreshPrescriptionMetrics();
                    Workbench.SystemNotice = $"已撤销删除 [{drugName}]";
                    RefreshAllWorkflowState();
                },
                durationSeconds: 5.0);
        }
    }

    public void AddQuickOrder()
    {
        if (!Workbench.HasSelectedPatient)
        {
            NotifyToast("需先选择并接诊患者后方可开立医嘱", "Warning");
            return;
        }
        if (!Workbench.IsOpdVisitActive)
        {
            NotifyToast("请先点击“叫号下一位 (F2)”进入接诊状态", "Warning");
            return;
        }
        if (!Workbench.HasDiagnosis)
        {
            NotifyToast("国家医保合规要求：必须先在第 1 步确立初步诊断后方可开立处方医嘱", "Warning");
            return;
        }

        string orderName = (DrugSearchQuery ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(orderName))
        {
            NotifyToast("请输入医嘱/药品名称或从目录检索选择", "Warning");
            return;
        }

        var drug = Workbench.AvailableDrugsCatalog.FirstOrDefault(d =>
            string.Equals(d.DrugName, orderName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(d.DrugCode, orderName, StringComparison.OrdinalIgnoreCase));

        int nextSeq = Workbench.Prescriptions.Count(x => !x.IsDeleted) + 1;
        var item = new PrescriptionItemModel
        {
            Seq = nextSeq,
            DrugId = drug?.DrugId ?? Guid.NewGuid().ToString("N"),
            DrugName = orderName,
            Spec = drug?.Spec ?? $"{SingleDoseInput}{SelectedDoseUnit}",
            OrderType = SelectedOrderType == "全部" ? (drug != null ? "西药" : "治疗") : SelectedOrderType,
            Quantity = OrderQuantityInput.ToString(),
            QuantityValue = OrderQuantityInput,
            TotalUnit = SelectedTotalUnit,
            SingleDose = SingleDoseInput.ToString(),
            Dose = SingleDoseInput.ToString(),
            DoseUnit = SelectedDoseUnit,
            PackageRatio = $"{PackageRatioAmount:0.000} {PackageRatioUnit}",
            PaymentCount = PaymentCountInput,
            Freq = SelectedFrequency,
            FrequencyCode = SelectedFrequency,
            Route = SelectedRoute,
            RouteName = SelectedRoute,
            Days = DaysInput,
            DaysValue = DaysInput,
            HospitalInjectionCount = HospitalInjectionCountInput,
            DoctorName = Workbench.SelectedPatient?.DoctorName ?? SessionContext.Current.DisplayName ?? SessionContext.Current.UserId ?? "接诊医师",
            ExecDeptName = SelectedDeductDept,
            IsUrgent = IsUrgentInput,
            ExamBodyPart = ExamBodyPartInput,
            SpecimenType = SpecimenTypeInput,
            DeductStockDept = SelectedDeductDept,
            MedicationNote = RemarkInput,
            OperatorName = SessionContext.Current.DisplayName ?? SessionContext.Current.UserId ?? "接诊医师",
            DeptName = Workbench.SelectedPatient?.OpdDeptName ?? "普通门诊",
            CreatedAtText = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            StopTimeText = "--",
            StopDoctor = "--",
            SkinTest = (drug?.IsSkinTestRequired == true ? "需皮试" : "免试"),
            SkinTestStatus = (drug?.IsSkinTestRequired == true ? "待皮试" : "免试"),
            SkinTestClass = (drug?.IsSkinTestRequired == true ? "Warning" : "Info"),
            Amount = drug != null ? drug.UnitPrice : "¥0.00",
            AuditStatus = "待审方",
            AuditClass = "Info",
            IsPersisted = true,
            IsBlocked = false
        };

        if (Workbench.SelectedPatient?.AllergyTags?.Contains(orderName) == true ||
            (drug != null && drug.IsSkinTestRequired && Workbench.SelectedPatient?.AllergyTags?.Contains("青霉素") == true && drug.DrugName.Contains("青霉素")))
        {
            item.IsBlocked = true;
            item.AuditStatus = "已拦截 (过敏史冲突)";
            item.AuditClass = "Danger";
            item.SkinTestStatus = "❌ 皮试阳性 (强阻断)";
            item.SkinTestClass = "Danger";
            Workbench.SystemNotice = $"⚠ 用药安全拦截：药品 [{orderName}] 与患者过敏史冲突，不允许提交！";
        }

        Workbench.Prescriptions.Add(item);
        Workbench.RefreshPrescriptionMetrics();
        IsDrugDropdownOpen = false;
        DrugSearchQuery = string.Empty;
        RemarkInput = string.Empty;
        IsUrgentInput = false;
        QuickSearchCode = string.Empty;

        NotifyToast($"已开立门诊医嘱 [{orderName}] (数量: {OrderQuantityInput}{SelectedTotalUnit})", item.IsBlocked ? "Danger" : "Success");
        RefreshAllWorkflowState();
        _focusDrugSearchCallback?.Invoke();
    }

    public void GroupSelectedOrders()
    {
        var selected = Workbench.Prescriptions.Where(x => !x.IsDeleted && x.IsSelected).ToList();
        if (selected.Count < 2)
        {
            NotifyToast("请在下方医嘱列表中至少勾选 2 条医嘱进行成组", "Warning");
            return;
        }

        int maxGroup = 0;
        foreach (var item in Workbench.Prescriptions)
        {
            if (int.TryParse(item.GroupNo, out int g) && g > maxGroup) maxGroup = g;
        }
        int nextGroup = maxGroup + 1;

        for (int i = 0; i < selected.Count; i++)
        {
            var item = selected[i];
            item.GroupNo = nextGroup.ToString();
            if (i == 0) item.GroupNoText = $"┓ 组{nextGroup}";
            else if (i == selected.Count - 1) item.GroupNoText = $"┛ 组{nextGroup}";
            else item.GroupNoText = $"┃ 组{nextGroup}";
            item.IsSelected = false;
        }
        NotifyToast($"已将选中的 {selected.Count} 条医嘱合并为第 {nextGroup} 输液组", "Success");
        RefreshAllWorkflowState();
    }

    public void UngroupSelectedOrders()
    {
        var selected = Workbench.Prescriptions.Where(x => !x.IsDeleted && x.IsSelected).ToList();
        if (selected.Count == 0) selected = Workbench.Prescriptions.Where(x => !x.IsDeleted && !string.IsNullOrWhiteSpace(x.GroupNo)).ToList();
        foreach (var item in selected)
        {
            item.GroupNo = string.Empty;
            item.GroupNoText = "-";
            item.IsSelected = false;
        }
        NotifyToast("已解除所选医嘱的成组标记", "Success");
        RefreshAllWorkflowState();
    }

    public void SetSkinTestStatus(string status)
    {
        var selected = Workbench.Prescriptions.Where(x => !x.IsDeleted && x.IsSelected).ToList();
        if (selected.Count == 0)
        {
            NotifyToast("请先在下方列表中勾选需要修改皮试状态的医嘱", "Warning");
            return;
        }
        foreach (var item in selected)
        {
            item.SkinTest = status;
            item.SkinTestStatus = status;
            item.SkinTestClass = status.Contains("阳") ? "Danger" : (status.Contains("需") || status.Contains("待") ? "Warning" : "Success");
        }
        NotifyToast($"已将勾选医嘱皮试状态标记为 [{status}]", "Success");
        RefreshAllWorkflowState();
    }

    public void ToggleUrgent()
    {
        var selected = Workbench.Prescriptions.Where(x => !x.IsDeleted && x.IsSelected).ToList();
        if (selected.Count == 0)
        {
            NotifyToast("请先在列表中勾选医嘱", "Warning");
            return;
        }
        foreach (var item in selected)
        {
            item.IsUrgent = !item.IsUrgent;
        }
        NotifyToast("已批量切换所选医嘱的加急状态", "Success");
        RefreshAllWorkflowState();
    }

    public void SelectAllOrders(bool select)
    {
        foreach (var item in Workbench.FilteredPrescriptions)
        {
            item.IsSelected = select;
        }
    }

    private void AutoReplaceBlockedDrug()
    {
        var blocked = Workbench.Prescriptions.FirstOrDefault(x => x.IsBlocked && !x.IsDeleted);
        if (blocked == null)
        {
            NotifyToast("当前处方中无被拦截的高危药品。", "Info");
            return;
        }

        blocked.DrugName = "阿奇霉素分散片 (替代抗感染)";
        blocked.Spec = "0.25g*6片/盒";
        blocked.Dose = "0.5g";
        blocked.Freq = "qd (每日一次)";
        blocked.Route = "口服";
        blocked.Quantity = "2盒";
        blocked.Amount = "¥32.50";
        blocked.SkinTestStatus = "无需皮试";
        blocked.SkinTestClass = "Secondary";
        blocked.AuditStatus = "安全替换通过";
        blocked.AuditClass = "Success";
        blocked.IsBlocked = false;
        blocked.RefreshCells();
        Workbench.RefreshPrescriptionMetrics();
        Workbench.SystemNotice = "🛡️ 用药安全闭环: 已将青霉素阳性强阻断药物替换为安全替代药物 [阿奇霉素分散片]！";
        _ = Workbench.SyncReplacedPrescriptionItemAsync(blocked);
        RefreshAllWorkflowState();
    }

    private async Task PreCheckPrescriptionAsync()
    {
        var outcome = await Workbench.PreCheckCurrentPrescriptionAsync();
        NotifyToast(outcome.Message, outcome.Succeeded ? "Success" : "Warning");
        RefreshAllWorkflowState();
    }

    private async Task SubmitPrescriptionAsync()
    {
        var outcome = await Workbench.SubmitCurrentPrescriptionAsync();
        NotifyToast(outcome.Message, outcome.Succeeded ? "Success" : "Danger");
        RefreshAllWorkflowState();
    }

    private async Task CallNextPatientAsync()
    {
        var outcome = await Workbench.CallNextOpdPatientAsync();
        NotifyToast(outcome.Message, outcome.Succeeded ? "Success" : "Warning");
        RefreshAllWorkflowState();
    }

    private async Task RecallPatientAsync()
    {
        var outcome = await Workbench.RecallOpdPatientAsync();
        NotifyToast(outcome.Message, outcome.Succeeded ? "Success" : "Warning");
        RefreshAllWorkflowState();
    }

    private async Task PassPatientAsync()
    {
        var outcome = await Workbench.PassOpdPatientAsync();
        NotifyToast(outcome.Message, outcome.Succeeded ? "Success" : "Warning");
        RefreshAllWorkflowState();
    }

    public void LoadQuickTcmFormula(string formula)
    {
        TcmHerbItems.Clear();
        switch (formula)
        {
            case "BU_ZHONG":
                TcmHerbItems.Add(new TcmHerbItemModel { HerbName = "黄芪 (蜜炙)", SingleDoseG = 18, DoseCount = TcmDoseCount, UnitPricePerG = 0.42m });
                TcmHerbItems.Add(new TcmHerbItemModel { HerbName = "党参", SingleDoseG = 12, DoseCount = TcmDoseCount, UnitPricePerG = 0.50m });
                TcmHerbItems.Add(new TcmHerbItemModel { HerbName = "白术 (炒)", SingleDoseG = 10, DoseCount = TcmDoseCount, UnitPricePerG = 0.38m });
                TcmHerbItems.Add(new TcmHerbItemModel { HerbName = "当归", SingleDoseG = 10, DoseCount = TcmDoseCount, UnitPricePerG = 0.65m });
                TcmHerbItems.Add(new TcmHerbItemModel { HerbName = "升麻", SingleDoseG = 6, DoseCount = TcmDoseCount, UnitPricePerG = 0.28m });
                TcmHerbItems.Add(new TcmHerbItemModel { HerbName = "柴胡", SingleDoseG = 6, DoseCount = TcmDoseCount, UnitPricePerG = 0.32m });
                TcmHerbItems.Add(new TcmHerbItemModel { HerbName = "陈皮", SingleDoseG = 8, DoseCount = TcmDoseCount, UnitPricePerG = 0.25m });
                TcmHerbItems.Add(new TcmHerbItemModel { HerbName = "炙甘草", SingleDoseG = 6, DoseCount = TcmDoseCount, UnitPricePerG = 0.20m });
                break;
            case "SHENG_MAI":
                TcmHerbItems.Add(new TcmHerbItemModel { HerbName = "红参 (另炖冲服)", SingleDoseG = 9, DoseCount = TcmDoseCount, UnitPricePerG = 1.80m, SpecialInstructions = "另炖兑服" });
                TcmHerbItems.Add(new TcmHerbItemModel { HerbName = "麦冬", SingleDoseG = 15, DoseCount = TcmDoseCount, UnitPricePerG = 0.45m });
                TcmHerbItems.Add(new TcmHerbItemModel { HerbName = "五味子", SingleDoseG = 6, DoseCount = TcmDoseCount, UnitPricePerG = 0.55m });
                break;
            default: // SI_JUN_ZI
                TcmHerbItems.Add(new TcmHerbItemModel { HerbName = "党参", SingleDoseG = 15, DoseCount = TcmDoseCount, UnitPricePerG = 0.50m });
                TcmHerbItems.Add(new TcmHerbItemModel { HerbName = "白术", SingleDoseG = 12, DoseCount = TcmDoseCount, UnitPricePerG = 0.38m });
                TcmHerbItems.Add(new TcmHerbItemModel { HerbName = "茯苓", SingleDoseG = 15, DoseCount = TcmDoseCount, UnitPricePerG = 0.32m });
                TcmHerbItems.Add(new TcmHerbItemModel { HerbName = "炙甘草", SingleDoseG = 6, DoseCount = TcmDoseCount, UnitPricePerG = 0.20m });
                break;
        }
        RefreshTcmTotals();
    }

    public void AddCustomTcmHerb()
    {
        TcmHerbItems.Add(new TcmHerbItemModel
        {
            HerbName = "金银花",
            SingleDoseG = 10,
            DoseCount = TcmDoseCount,
            UnitPricePerG = 0.48m
        });
        RefreshTcmTotals();
    }

    public void RefreshTcmTotals()
    {
        TcmHerbsTotalAmount = TcmHerbItems.Sum(x => x.ItemTotal);
        TcmDecoctionTotalFee = TcmRequiresDecoction ? TcmDecoctionUnitFee * TcmDoseCount : 0m;
        TcmGrandTotal = TcmHerbsTotalAmount + TcmDecoctionTotalFee;
    }

    private async Task SubmitTcmPrescriptionAsync()
    {
        var patient = Workbench.SelectedPatient;
        if (patient == null)
        {
            NotifyToast("请先选择接诊患者", "Warning");
            return;
        }

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new
            {
                visitId = patient.VisitId,
                patientId = patient.PatientId,
                doctorUserId = SessionContext.Current.UserId,
                doseCount = TcmDoseCount,
                route = TcmRoute,
                frequency = TcmFrequency,
                requiresDecoction = (short)(TcmRequiresDecoction ? 1 : 0),
                decoctionUnitFee = TcmDecoctionUnitFee,
                items = TcmHerbItems.Select(h => new
                {
                    herbCode = "HERB-01",
                    herbName = h.HerbName,
                    singleDoseG = h.SingleDoseG,
                    totalWeightG = h.TotalWeightG,
                    specialInstructions = h.SpecialInstructions,
                    unitPricePerG = h.UnitPricePerG,
                    itemTotalAmount = h.ItemTotal
                }).ToArray()
            };

            var res = await http.PostAsJsonAsync("/api/v4/opd/tcm/prescriptions", payload);
            NotifyToast($"中药饮片处方已开立！共 {TcmDoseCount} 剂，总金额 ￥{TcmGrandTotal:F2}（含代煎费 ￥{TcmDecoctionTotalFee:F2}）", "Success");
            RefreshAllWorkflowState();
        }
        catch (Exception ex)
        {
            NotifyToast("开立中药处方失败: " + ex.Message, "Warning");
        }
    }

    private async Task SignCurrentEmrAsync()
    {
        var outcome = await Workbench.SaveAndSignOpdEmrAsync();
        NotifyToast(outcome.Message, outcome.Succeeded ? "Success" : "Danger");
        RefreshAllWorkflowState();
    }

    private void AddSampleMedTechOrder()
    {
        var count = Workbench.MedTechOrders.Count + 1;
        Workbench.MedTechOrders.Add(new MedTechOrderItemModel
        {
            RequestNo = $"MTC202611050{count:D2}",
            ItemName = "心脏高分辨三维彩超 (UCG)",
            DeptName = "超声医学科",
            ExecLocation = "门诊3楼 彩超1室",
            Fee = "¥220.00",
            Status = "已开单待采样",
            StatusClass = "Warning",
            IsFeeLocked = false
        });
        Workbench.SystemNotice = "医技检查申请: 已新增 [心脏高分辨三维彩超 (UCG)]";
        RefreshAllWorkflowState();
    }

    private void OpenFinishVisitChecklistDrawer()
    {
        var patient = Workbench.SelectedPatient;
        if (patient == null) return;

        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(new TextBlock
        {
            Text = $"即将完成患者 [{patient.PatientName}] 本次门诊诊疗，请核对以下完成状态：",
            FontSize = 13,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        panel.Children.Add(CreateChecklistRow("1. 诊断录入状态", Workbench.HasDiagnosis ? "✓ 已确立初步诊断" : "未录入诊断"));
        panel.Children.Add(CreateChecklistRow("2. 处方开立与审方", Workbench.IsOpdOrdersPushed ? "✓ 已提交药师审方" : (!Workbench.Prescriptions.Any(x => !x.IsDeleted) ? "✓ 本次无处方" : "未推送")));
        panel.Children.Add(CreateChecklistRow("3. 检查检验开单", Workbench.MedTechOrders.Count > 0 ? "✓ 已开立医技申请" : "本次无医技检查"));
        panel.Children.Add(CreateChecklistRow("4. 门诊病历审签", Workbench.IsOpdEmrSigned ? "✓ 已审签归档 (OFD)" : "未审签"));
        panel.Children.Add(CreateChecklistRow("5. 用药安全检查", Workbench.HasBlockedPrescription ? "⚠ 存在过敏拦截项" : "✓ 无冲突"));

        var noPrescriptionCheck = new CheckBox
        {
            Content = "本次就诊无处方（未开立药品）",
            IsChecked = false,
            Margin = new Avalonia.Thickness(0, 2, 0, 0)
        };
        panel.Children.Add(noPrescriptionCheck);

        var outcomeText = new TextBlock
        {
            FontSize = 12,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            IsVisible = false
        };
        panel.Children.Add(outcomeText);

        var confirmBtn = new Button
        {
            Content = "确认完成诊疗",
            Height = 36,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Command = new AsyncRelayCommand(async () =>
            {
                var outcome = await Workbench.CompleteCurrentOpdVisitAsync(noPrescriptionCheck.IsChecked == true);
                outcomeText.Text = outcome.Message;
                outcomeText.Foreground = outcome.Succeeded
                    ? Avalonia.Media.Brushes.SeaGreen
                    : Avalonia.Media.Brushes.IndianRed;
                outcomeText.IsVisible = true;
                if (outcome.Succeeded)
                {
                    NotifyToast(outcome.Message, "Success");
                    _shell?.CloseDrawer();
                }
                else
                {
                    NotifyToast(outcome.Message, "Danger");
                }
                RefreshAllWorkflowState();
            })
        };
        confirmBtn.Classes.Add("Primary");
        panel.Children.Add(confirmBtn);

        _shell?.OpenDrawer("完成诊疗核对清单", $"{patient.PatientName} · {patient.VisitNo}", panel, 460);
    }

    private static Control CreateChecklistRow(string label, string status)
    {
        var border = new Border
        {
            Padding = new Avalonia.Thickness(12, 8),
            CornerRadius = new Avalonia.CornerRadius(6),
            BorderThickness = new Avalonia.Thickness(1)
        };
        if (Avalonia.Application.Current?.TryGetResource("SemiColorBackground1", null, out var bg) == true && bg is Avalonia.Media.IBrush bgBrush)
            border.Background = bgBrush;
        if (Avalonia.Application.Current?.TryGetResource("SemiColorBorder", null, out var bd) == true && bd is Avalonia.Media.IBrush bdBrush)
            border.BorderBrush = bdBrush;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var left = new TextBlock { Text = label, FontWeight = Avalonia.Media.FontWeight.SemiBold, FontSize = 13 };
        var right = new TextBlock { Text = status, FontSize = 12 };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        border.Child = grid;
        return border;
    }

    private void OpenWhoSurgeryDrawer()
    {
        var who = Workbench.WhoSurgeryCheck;
        var panel = new StackPanel { Spacing = 12, DataContext = who };
        panel.Children.Add(new TextBlock { Text = $"{who.SurgeryName} · {who.AnesthesiaType}", FontSize = 15, FontWeight = Avalonia.Media.FontWeight.Bold, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = $"主刀: {who.Surgeon} | 麻醉: {who.Anesthesiologist} | 巡回: {who.CirculatingNurse}", FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = $"阶段A ({who.Stage1Badge}): 身份={who.SignInIdentityOk}, 部位标识={who.SignInSiteMarked}, 麻醉安全={who.SignInAnesthesiaSafetyCheck}", FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = $"阶段B ({who.Stage2Badge}): 团队确认={who.TimeOutAllTeamAgreed}, 预防抗菌药={who.TimeOutAntibioticGiven}, 影像显示={who.TimeOutImagingDisplayed}", FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = $"阶段C ({who.Stage3Badge}): 器械清点={who.SignOutInstrumentCountMatched}, 标本标记={who.SignOutSpecimenLabeled}", FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap });

        var btn = new Button
        {
            Content = "签署三方核查单",
            Height = 34,
            Command = new RelayCommand(() =>
            {
                who.Stage1Confirmed = true;
                who.Stage2Confirmed = true;
                who.Stage3Confirmed = true;
                who.CheckStatus = "✓ 三方全员核验签署完毕 · 允许实施手术";
                Workbench.SystemNotice = "WHO 手术安全核查三方已签署完毕。";
                _shell?.CloseDrawer();
            })
        };
        btn.Classes.Add("Primary");
        panel.Children.Add(btn);

        _shell?.OpenDrawer("WHO 手术安全三方核查", $"{who.PatientName} ({who.AdmissionNo})", panel, 520);
    }

    private void OpenRefundProofDrawer()
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = Workbench.FeeLockedMedTechOrderText,
            FontSize = 13,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = "已确费医技申请单不可由门诊或财务单向强制撤单，需凭医技科室主任签章的《医技冲正退费证明单》办理退费冲正。",
            FontSize = 12
        });
        _shell?.OpenDrawer("医技已确费锁单说明", "不可直接撤单提示", panel, 460);
    }

    private void OpenBorrowBedDrawer()
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(CreateChecklistRow("当前患者", Workbench.BorrowBedPatientSummary));
        panel.Children.Add(CreateChecklistRow("就诊/住院号", Workbench.BorrowBedAdmissionSummary));
        panel.Children.Add(CreateChecklistRow("主管科室", Workbench.BorrowBedAdmittingDept));
        panel.Children.Add(CreateChecklistRow("借住病区 / 床位", Workbench.BorrowBedAccommodationSummary));
        panel.Children.Add(CreateChecklistRow("主管医师", Workbench.BorrowBedAttendingDoctor));

        var btn = new Button
        {
            Content = "办理借床入科",
            Height = 34,
            IsEnabled = Workbench.IsInpatientContext,
            Command = new RelayCommand(() =>
            {
                Workbench.SystemNotice = "✓ 跨病区借床入科办理完成！医疗与护理职责已独立路由。";
                _shell?.CloseDrawer();
            })
        };
        btn.Classes.Add("Primary");
        panel.Children.Add(btn);

        _shell?.OpenDrawer("跨病区借床入科", "主管科室负责诊疗 · 借住病区负责护理", panel, 480);
    }

    private void OpenTemplatePickerDrawer()
    {
        _ = Workbench.EnsureOpdEmrTemplatesAsync();
        _ = Workbench.EnsureOpdUserTemplatesAsync();
        var panel = new StackPanel { Spacing = 10 };

        var intro = new TextBlock
        {
            Text = "门诊文书库由【文书设计器】统一规范。医生可直接选用系统主模板建立空白病历，或选用科室/个人模板一键填充预定义的常用语书写内容。",
            FontSize = 12,
            Foreground = Avalonia.Media.Brushes.DimGray,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        panel.Children.Add(intro);

        // Scope Category Filter Row
        var categoryPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 6
        };

        string currentCategory = "ALL"; // ALL, BASE, DEPT, PERSONAL

        var btnAll = new Button { Content = "全部模板", Height = 28, Padding = new Avalonia.Thickness(12, 0), FontSize = 12 };
        btnAll.Classes.Add("Primary");

        var btnBase = new Button { Content = "系统主模板", Height = 28, Padding = new Avalonia.Thickness(12, 0), FontSize = 12 };
        btnBase.Classes.Add("Tertiary");

        var btnDept = new Button { Content = "科室常用模板", Height = 28, Padding = new Avalonia.Thickness(12, 0), FontSize = 12 };
        btnDept.Classes.Add("Tertiary");

        var btnPersonal = new Button { Content = "个人常用模板", Height = 28, Padding = new Avalonia.Thickness(12, 0), FontSize = 12 };
        btnPersonal.Classes.Add("Tertiary");

        categoryPanel.Children.Add(btnAll);
        categoryPanel.Children.Add(btnBase);
        categoryPanel.Children.Add(btnDept);
        categoryPanel.Children.Add(btnPersonal);
        panel.Children.Add(categoryPanel);

        var searchBox = new TextBox
        {
            PlaceholderText = "输入模板名称、科室、基底模板或常用语关键字筛选...",
            Height = 34,
            Text = Workbench.OpdTemplateSearch
        };
        panel.Children.Add(searchBox);

        var cardsPanel = new StackPanel { Spacing = 8 };
        var cardsScrollViewer = new ScrollViewer
        {
            Content = cardsPanel,
            MaxHeight = 460
        };

        void UpdateFilterStyles()
        {
            void SetBtnStyle(Button btn, bool active)
            {
                btn.Classes.Clear();
                btn.Classes.Add(active ? "Primary" : "Tertiary");
            }
            SetBtnStyle(btnAll, currentCategory == "ALL");
            SetBtnStyle(btnBase, currentCategory == "BASE");
            SetBtnStyle(btnDept, currentCategory == "DEPT");
            SetBtnStyle(btnPersonal, currentCategory == "PERSONAL");
        }

        void RepopulateCards()
        {
            cardsPanel.Children.Clear();

            bool showBase = currentCategory == "ALL" || currentCategory == "BASE";
            bool showUser = currentCategory == "ALL" || currentCategory == "DEPT" || currentCategory == "PERSONAL";

            var baseTemplates = showBase ? Workbench.FilteredOpdEmrTemplates : new ObservableCollection<OpdEmrTemplateModel>();
            var userTemplates = showUser ? Workbench.FilteredOpdUserTemplates.Where(t =>
            {
                if (currentCategory == "DEPT") return t.IsSharedDept;
                if (currentCategory == "PERSONAL") return !t.IsSharedDept;
                return true;
            }).ToList() : new List<EmrUserTemplateModel>();

            if (baseTemplates.Count == 0 && userTemplates.Count == 0)
            {
                cardsPanel.Children.Add(new TextBlock
                {
                    Text = "未找到匹配的门诊病历模板或常用模板",
                    Margin = new Avalonia.Thickness(0, 20, 0, 0),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Foreground = Avalonia.Media.Brushes.Gray,
                    FontSize = 12
                });
                return;
            }

            // 1. Render User Templates First (Predefined common phrases are most frequently needed during OPD visits)
            if (userTemplates.Count > 0)
            {
                if (showBase && baseTemplates.Count > 0)
                {
                    cardsPanel.Children.Add(new TextBlock
                    {
                        Text = "⭐ 常用语书写模板（科室共享与个人专属）",
                        FontWeight = Avalonia.Media.FontWeight.Bold,
                        FontSize = 12,
                        Foreground = Avalonia.Media.Brushes.DarkSlateGray,
                        Margin = new Avalonia.Thickness(0, 4, 0, 2)
                    });
                }

                foreach (var tpl in userTemplates)
                {
                    var card = new Border
                    {
                        Background = Avalonia.Media.Brushes.Transparent,
                        BorderBrush = Avalonia.Media.Brushes.LightGray,
                        BorderThickness = new Avalonia.Thickness(1),
                        CornerRadius = new Avalonia.CornerRadius(6),
                        Padding = new Avalonia.Thickness(12, 10),
                        Margin = new Avalonia.Thickness(0, 2)
                    };

                    var grid = new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,Auto")
                    };

                    var infoStack = new StackPanel { Spacing = 4 };

                    var titleStack = new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        Spacing = 8
                    };

                    titleStack.Children.Add(new TextBlock
                    {
                        Text = tpl.TemplateName,
                        FontWeight = Avalonia.Media.FontWeight.Bold,
                        FontSize = 13,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    });

                    // Badge: Dept shared vs Personal
                    var scopeBadge = new Border
                    {
                        CornerRadius = new Avalonia.CornerRadius(4),
                        Padding = new Avalonia.Thickness(6, 1),
                        Background = tpl.IsSharedDept ? ThemeBrushes.PrimaryLight : ThemeBrushes.SuccessLight,
                        Child = new TextBlock
                        {
                            Text = tpl.ScopeBadgeText,
                            FontSize = 11,
                            FontWeight = Avalonia.Media.FontWeight.SemiBold,
                            Foreground = tpl.IsSharedDept ? ThemeBrushes.Primary : ThemeBrushes.Success
                        }
                    };
                    titleStack.Children.Add(scopeBadge);

                    // Base template name badge
                    if (!string.IsNullOrWhiteSpace(tpl.BaseTemplateName))
                    {
                        var baseBadge = new Border
                        {
                            CornerRadius = new Avalonia.CornerRadius(4),
                            Padding = new Avalonia.Thickness(6, 1),
                            Background = ThemeBrushes.Background0,
                            BorderBrush = ThemeBrushes.Border,
                            BorderThickness = new Avalonia.Thickness(1),
                            Child = new TextBlock
                            {
                                Text = $"基底: 《{tpl.BaseTemplateName}》",
                                FontSize = 11,
                                Foreground = ThemeBrushes.Text2
                            }
                        };
                        titleStack.Children.Add(baseBadge);
                    }
                    infoStack.Children.Add(titleStack);

                    // Creator and Dept info
                    var authorText = new TextBlock
                    {
                        Text = $"创建人: {tpl.UserName} · 科室: {tpl.DeptName ?? "心血管内科门诊"}",
                        FontSize = 11,
                        Foreground = ThemeBrushes.Text2
                    };
                    infoStack.Children.Add(authorText);

                    // Predefined Common Phrases Preview Box
                    var previewBox = new Border
                    {
                        Background = ThemeBrushes.Surface,
                        BorderBrush = ThemeBrushes.Border,
                        BorderThickness = new Avalonia.Thickness(1),
                        CornerRadius = new Avalonia.CornerRadius(4),
                        Padding = new Avalonia.Thickness(8, 4),
                        Margin = new Avalonia.Thickness(0, 2, 0, 0),
                        Child = new TextBlock
                        {
                            Text = $"💬 预设常用语: {tpl.PreviewSummary}",
                            FontSize = 11,
                            Foreground = ThemeBrushes.Text1,
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap
                        }
                    };
                    infoStack.Children.Add(previewBox);

                    grid.Children.Add(infoStack);

                    var btnStack = new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        Spacing = 6,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    };

                    if (!tpl.IsSharedDept)
                    {
                        var delBtn = new Button
                        {
                            Content = "删除",
                            Height = 28,
                            Padding = new Avalonia.Thickness(8, 0),
                            FontSize = 11,
                            Foreground = Avalonia.Media.Brushes.Crimson,
                            Command = new AsyncRelayCommand(async () =>
                            {
                                var delRes = await Workbench.DeleteOpdUserTemplateAsync(tpl.Id);
                                NotifyToast(delRes.Message, delRes.Succeeded ? "Success" : "Warning");
                                RepopulateCards();
                            })
                        };
                        delBtn.Classes.Add("Tertiary");
                        btnStack.Children.Add(delBtn);
                    }

                    var applyBtn = new Button
                    {
                        Content = "选用并填入常用语",
                        Height = 30,
                        Padding = new Avalonia.Thickness(12, 0),
                        FontSize = 12,
                        FontWeight = Avalonia.Media.FontWeight.Bold,
                        Command = new AsyncRelayCommand(async () =>
                        {
                            var applyRes = await Workbench.ApplyOpdUserTemplateAsync(tpl);
                            NotifyToast(applyRes.Message, applyRes.Succeeded ? "Success" : "Warning");
                            RefreshAllWorkflowState();
                            OnPropertyChanged(nameof(HasSelectedEmrTemplate));
                            OnPropertyChanged(nameof(SelectedEmrTemplateName));
                            _shell?.CloseDrawer();
                        })
                    };
                    applyBtn.Classes.Add("Primary");
                    btnStack.Children.Add(applyBtn);

                    Grid.SetColumn(btnStack, 1);
                    grid.Children.Add(btnStack);

                    card.Child = grid;
                    cardsPanel.Children.Add(card);
                }
            }

            // 2. Render Base Templates
            if (baseTemplates.Count > 0)
            {
                if (showUser && userTemplates.Count > 0)
                {
                    cardsPanel.Children.Add(new TextBlock
                    {
                        Text = "📐 系统主模板（文书设计器发布基础结构）",
                        FontWeight = Avalonia.Media.FontWeight.Bold,
                        FontSize = 12,
                        Foreground = ThemeBrushes.Text2,
                        Margin = new Avalonia.Thickness(0, 10, 0, 2)
                    });
                }

                foreach (var tpl in baseTemplates)
                {
                    var card = new Border
                    {
                        Background = ThemeBrushes.Surface,
                        BorderBrush = ThemeBrushes.Border,
                        BorderThickness = new Avalonia.Thickness(1),
                        CornerRadius = new Avalonia.CornerRadius(6),
                        Padding = new Avalonia.Thickness(12, 10),
                        Margin = new Avalonia.Thickness(0, 2)
                    };

                    var grid = new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,Auto")
                    };

                    var infoStack = new StackPanel { Spacing = 4 };
                    var titleStack = new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        Spacing = 8
                    };
                    titleStack.Children.Add(new TextBlock
                    {
                        Text = tpl.TemplateName,
                        FontWeight = Avalonia.Media.FontWeight.Bold,
                        FontSize = 13,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    });

                    var mainBadge = new Border
                    {
                        CornerRadius = new Avalonia.CornerRadius(4),
                        Padding = new Avalonia.Thickness(6, 1),
                        Background = ThemeBrushes.PrimaryLight,
                        Child = new TextBlock
                        {
                            Text = "系统主模板",
                            FontSize = 11,
                            FontWeight = Avalonia.Media.FontWeight.SemiBold,
                            Foreground = ThemeBrushes.Primary
                        }
                    };
                    titleStack.Children.Add(mainBadge);

                    if (!string.IsNullOrWhiteSpace(tpl.DeptCategory))
                    {
                        var catTag = new Border
                        {
                            CornerRadius = new Avalonia.CornerRadius(4),
                            Padding = new Avalonia.Thickness(5, 1),
                            Background = ThemeBrushes.SuccessLight,
                            Child = new TextBlock
                            {
                                Text = tpl.DeptCategory,
                                FontSize = 11,
                                Foreground = ThemeBrushes.Success
                            }
                        };
                        titleStack.Children.Add(catTag);
                    }
                    infoStack.Children.Add(titleStack);

                    var descText = new TextBlock
                    {
                        Text = $"{tpl.TemplateCode} · {tpl.SectionCount} 个章节 · 需 {tpl.RequiredSignLevels} 级审签 · {tpl.PageSizeCode} 版式 · 文书设计器统一定义",
                        FontSize = 11,
                        Foreground = ThemeBrushes.Text2
                    };
                    infoStack.Children.Add(descText);
                    grid.Children.Add(infoStack);

                    var selectBtn = new Button
                    {
                        Content = "选用空白模板并新建",
                        Height = 30,
                        Padding = new Avalonia.Thickness(12, 0),
                        FontSize = 12,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Command = new AsyncRelayCommand(async () =>
                        {
                            await ApplyTemplateAsync(tpl);
                            _shell?.CloseDrawer();
                        })
                    };
                    selectBtn.Classes.Add("Secondary");
                    Grid.SetColumn(selectBtn, 1);
                    grid.Children.Add(selectBtn);

                    card.Child = grid;
                    cardsPanel.Children.Add(card);
                }
            }
        }

        btnAll.Click += (s, e) => { currentCategory = "ALL"; UpdateFilterStyles(); RepopulateCards(); };
        btnBase.Click += (s, e) => { currentCategory = "BASE"; UpdateFilterStyles(); RepopulateCards(); };
        btnDept.Click += (s, e) => { currentCategory = "DEPT"; UpdateFilterStyles(); RepopulateCards(); };
        btnPersonal.Click += (s, e) => { currentCategory = "PERSONAL"; UpdateFilterStyles(); RepopulateCards(); };

        searchBox.PropertyChanged += (s, e) =>
        {
            if (e.Property.Name == nameof(TextBox.Text))
            {
                Workbench.OpdTemplateSearch = searchBox.Text ?? string.Empty;
                RepopulateCards();
            }
        };

        RepopulateCards();
        panel.Children.Add(cardsScrollViewer);

        _shell?.OpenDrawer("门诊文书模板库", "选取系统主模板或科室/个人预定义常用语模板", panel, 580);
    }

    private void OpenSaveAsUserTemplateDrawer()
    {
        if (Workbench.SelectedOpdEmrTemplate == null)
        {
            NotifyToast("请先在门诊病历中选取主模板后再存为常用模板", "Warning");
            return;
        }

        var elements = Workbench.CollectOpdEmrElements();
        bool hasContent = elements.Values.Any(v => !string.IsNullOrWhiteSpace(v));
        if (!hasContent)
        {
            NotifyToast("当前门诊病历各字段均为空，请录入常用语内容后再存为模板", "Warning");
            return;
        }

        var panel = new StackPanel { Spacing = 14 };

        var intro = new TextBlock
        {
            Text = "将当前门诊病历中已书写的常用语内容（主诉、现病史、既往史、查体、诊断、处置等）另存为常用模板。科室共享模板供本科室医生选用，个人模板仅供您本人使用。",
            FontSize = 12,
            Foreground = Avalonia.Media.Brushes.DimGray,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        panel.Children.Add(intro);

        // Base Template info
        var baseCard = new Border
        {
            Background = ThemeBrushes.Surface,
            BorderBrush = ThemeBrushes.Border,
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(12, 8)
        };
        var baseStack = new StackPanel { Spacing = 2 };
        baseStack.Children.Add(new TextBlock
        {
            Text = $"基底主模板: 《{Workbench.SelectedOpdEmrTemplate.TemplateName}》",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            FontSize = 13,
            Foreground = ThemeBrushes.Text0
        });
        baseStack.Children.Add(new TextBlock
        {
            Text = $"文书编码: {Workbench.SelectedOpdEmrTemplate.TemplateCode} · 类型: {Workbench.SelectedOpdEmrTemplate.DocTypeCode} · 版式: {Workbench.SelectedOpdEmrTemplate.PageSizeCode}",
            FontSize = 11,
            Foreground = ThemeBrushes.Text2
        });
        baseCard.Child = baseStack;
        panel.Children.Add(baseCard);

        // Template name
        var nameLabel = new TextBlock
        {
            Text = "常用模板名称 (*):",
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            FontSize = 12
        };
        panel.Children.Add(nameLabel);

        string defaultName = $"{Workbench.CurrentOperatorName}-常用病历模板 ({DateTime.Now:MM-dd HH:mm})";
        var nameBox = new TextBox
        {
            Text = defaultName,
            Height = 34
        };
        panel.Children.Add(nameBox);

        // Scope options
        var scopeLabel = new TextBlock
        {
            Text = "模板共享范围 (*):",
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            FontSize = 12
        };
        panel.Children.Add(scopeLabel);

        var rbDept = new RadioButton
        {
            Content = "科室共享模板 (心血管内科门诊全体医生共享)",
            IsChecked = true,
            GroupName = "UserTplScope"
        };
        var rbPersonal = new RadioButton
        {
            Content = "个人专属模板 (仅我自己可见)",
            GroupName = "UserTplScope"
        };
        panel.Children.Add(rbDept);
        panel.Children.Add(rbPersonal);

        // Captured Phrases Preview
        var previewLabel = new TextBlock
        {
            Text = "当前捕获的预定义常用语内容预览:",
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            FontSize = 12
        };
        panel.Children.Add(previewLabel);

        var phrasesListPanel = new StackPanel { Spacing = 4 };
        foreach (var field in Workbench.EmrWorkspace.Fields.Where(x => !string.IsNullOrWhiteSpace(x.Value)))
        {
            var itemText = new TextBlock
            {
                Text = $"• {field.ElementName}: {field.Value?.Trim()}",
                FontSize = 11,
                Foreground = ThemeBrushes.Text1,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            };
            phrasesListPanel.Children.Add(itemText);
        }

        var phrasesScroll = new ScrollViewer
        {
            Content = phrasesListPanel,
            MaxHeight = 160
        };
        var phrasesBorder = new Border
        {
            Background = ThemeBrushes.Background0,
            BorderBrush = ThemeBrushes.Border,
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(10, 8),
            Child = phrasesScroll
        };
        panel.Children.Add(phrasesBorder);

        // Action buttons
        var btnGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            Margin = new Avalonia.Thickness(0, 8, 0, 0)
        };

        var cancelBtn = new Button
        {
            Content = "取消",
            Height = 34,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Avalonia.Thickness(0, 0, 4, 0)
        };
        cancelBtn.Classes.Add("Tertiary");
        cancelBtn.Click += (s, e) => _shell?.CloseDrawer();
        Grid.SetColumn(cancelBtn, 0);
        btnGrid.Children.Add(cancelBtn);

        var saveBtn = new Button
        {
            Content = "确认保存为常用模板",
            Height = 34,
            FontWeight = Avalonia.Media.FontWeight.Bold,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Avalonia.Thickness(4, 0, 0, 0)
        };
        saveBtn.Classes.Add("Primary");
        saveBtn.Click += async (s, e) =>
        {
            string name = nameBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                NotifyToast("模板名称不能为空", "Warning");
                return;
            }
            bool isShared = rbDept.IsChecked == true;
            var outcome = await Workbench.SaveAsOpdUserTemplateAsync(name, isShared);
            NotifyToast(outcome.Message, outcome.Succeeded ? "Success" : "Warning");
            if (outcome.Succeeded)
            {
                _shell?.CloseDrawer();
            }
        };
        Grid.SetColumn(saveBtn, 1);
        btnGrid.Children.Add(saveBtn);

        panel.Children.Add(btnGrid);

        _shell?.OpenDrawer("存为门诊常用病历模板", "基于主模板保存预定义常用语书写内容", panel, 500);
    }

    private async Task ApplyTemplateAsync(OpdEmrTemplateModel template)
    {
        var outcome = await Workbench.CreateNewOpdEmrDocumentAsync(template);
        NotifyToast($"已成功选用门诊文书模板：【{template.TemplateName}】", outcome.Succeeded ? "Success" : "Warning");
        RefreshAllWorkflowState();
        OnPropertyChanged(nameof(HasSelectedEmrTemplate));
        OnPropertyChanged(nameof(SelectedEmrTemplateName));
    }

    private void RefreshSteps()
    {
        WorkflowSteps.Clear();
        var active = Workbench.IsOpdVisitActive;
        var hasDiag = Workbench.HasDiagnosis;
        var rxDone = Workbench.IsOpdOrdersPushed;
        var emrDone = Workbench.IsOpdEmrSigned;
        var completed = Workbench.SelectedPatient?.IsCompleted == true;

        WorkflowSteps.Add(new FlowStepItem
        {
            Index = 1,
            Title = "诊断录入",
            State = (hasDiag || completed) ? FlowStepState.Completed : (active ? FlowStepState.Active : FlowStepState.Pending),
            Tooltip = "确立并录入初步诊断 (ICD-10) 推进就诊",
            Command = GoToDiagnosisCommand
        });
        WorkflowSteps.Add(new FlowStepItem
        {
            Index = 2,
            Title = "处方开立",
            State = (rxDone || completed) ? FlowStepState.Completed : ((active && hasDiag) ? FlowStepState.Active : FlowStepState.Pending),
            Tooltip = "行内检索加药开立处方并提交药师审方 (Ctrl+Enter)",
            Command = GoToPrescriptionCommand
        });
        WorkflowSteps.Add(new FlowStepItem
        {
            Index = 3,
            Title = "检查检验",
            State = (Workbench.MedTechOrders.Count > 0 || completed) ? FlowStepState.Completed : ((active && hasDiag) ? FlowStepState.Active : FlowStepState.Pending),
            Tooltip = "医技项目开单、采样与执行路由",
            Command = GoToMedTechCommand
        });
        WorkflowSteps.Add(new FlowStepItem
        {
            Index = 4,
            Title = "门诊病历",
            State = (emrDone || completed) ? FlowStepState.Completed : ((active && hasDiag) ? FlowStepState.Active : FlowStepState.Pending),
            Tooltip = "书写主诉现病史并完成病历审签 (Ctrl+S)",
            Command = GoToEmrCommand
        });
        WorkflowSteps.Add(new FlowStepItem
        {
            Index = 5,
            Title = "完成诊疗",
            IsLast = true,
            State = completed ? FlowStepState.Completed
                : (Workbench.CanFinishOpdVisit || Workbench.CanCompleteOpdWithNoPrescription)
                    ? FlowStepState.Active
                    : FlowStepState.Pending,
            Tooltip = string.IsNullOrEmpty(FinishVisitBlockedReason) ? "核对无误后点击完成诊疗" : FinishVisitBlockedReason,
            Command = RequestFinishVisitCommand
        });
    }

    private void RefreshAllWorkflowState()
    {
        RefreshSteps();
        OnPropertyChanged(nameof(WorkflowBlockedReason));
        OnPropertyChanged(nameof(PrescriptionEditBlockedReason));
        OnPropertyChanged(nameof(SignEmrBlockedReason));
        OnPropertyChanged(nameof(FinishVisitBlockedReason));
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterNormal));
        OnPropertyChanged(nameof(IsFilterAlert));
        OnPropertyChanged(nameof(HasSelectedEmrTemplate));
        OnPropertyChanged(nameof(SelectedEmrTemplateName));
        (SaveDiagnosisCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (SaveDiagnosisAndNextCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (SignEmrCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (SaveEmrDraftCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (PreCheckPrescriptionCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (SubmitPrescriptionCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (RequestFinishVisitCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void OnWorkbenchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkbenchModel.SelectedPatient)
            or nameof(WorkbenchModel.IsOpdVisitActive)
            or nameof(WorkbenchModel.HasDiagnosis)
            or nameof(WorkbenchModel.DiagnosisCode)
            or nameof(WorkbenchModel.DiagnosisName)
            or nameof(WorkbenchModel.IsOpdEmrSigned)
            or nameof(WorkbenchModel.IsOpdOrdersPushed)
            or nameof(WorkbenchModel.CanFinishOpdVisit)
            or nameof(WorkbenchModel.CanCompleteOpdWithNoPrescription)
            or nameof(WorkbenchModel.CanEditOpdPrescription)
            or nameof(WorkbenchModel.SelectedPrescriptionFilter)
            or nameof(WorkbenchModel.HasBlockedPrescription)
            or nameof(WorkbenchModel.SelectedOpdEmrTemplate)
            or nameof(WorkbenchModel.HasSelectedOpdEmrTemplate))
        {
            RefreshAllWorkflowState();
            if (e.PropertyName is nameof(WorkbenchModel.SelectedPatient))
            {
                _ = LoadPatientAccountBalanceAsync();
            }
        }
    }

    private async Task SettlePrescriptionInClinicAsync()
    {
        var pat = Workbench.SelectedPatient;
        if (pat == null)
        {
            _shell?.ShowToast("请先选择一位门诊就诊患者", "Warning");
            return;
        }

        var activeItems = Workbench.Prescriptions.Where(x => !x.IsDeleted).ToList();
        if (activeItems.Count == 0)
        {
            _shell?.ShowToast("当前处方无有效药品项", "Warning");
            return;
        }

        decimal totalAmount = activeItems.Sum(x => x.AmountNumeric);
        if (totalAmount <= 0m) totalAmount = 35.00m;
        if (PatientCardBalance < totalAmount)
        {
            _shell?.ShowToast($"就诊卡可用余额不足！处方总计 ¥{totalAmount:0.00}，当前卡余额 ¥{PatientCardBalance:0.00}，请先充值或前往窗口结算", "Warning");
            return;
        }

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new
            {
                PatientId = pat.PatientId,
                PrescriptionId = pat.VisitId,
                DoctorId = SessionContext.Current.UserId ?? "DOCTOR"
            };
            var res = await http.PostAsJsonAsync("/api/v4/opd/accounts/settle-in-clinic", payload);
            if (res.IsSuccessStatusCode)
            {
                foreach (var item in activeItems) item.IsPaid = true;
                PatientCardBalance -= totalAmount;
                _shell?.ShowToast($"✓ 诊间就诊卡划扣成功！实扣 ¥{totalAmount:0.00}，就诊卡剩余 ¥{PatientCardBalance:0.00}。患者可直接前往药房窗口取药", "Success");
                OnPropertyChanged(nameof(CanSettleInClinic));
            }
            else
            {
                var err = await res.Content.ReadAsStringAsync();
                _shell?.ShowToast($"诊间划扣失败: {err}", "Error");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"划扣异常: {ex.Message}", "Error");
        }
    }

    private async Task LoadPatientAccountBalanceAsync()
    {
        var pat = Workbench.SelectedPatient;
        if (pat == null || string.IsNullOrWhiteSpace(pat.PatientId))
        {
            PatientCardBalance = 0m;
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var patientId = pat.PatientId;
            var res = await http.GetFromJsonAsync<ApiAccountEnvelope>($"/api/v4/opd/accounts/{patientId}/balance");
            if (res?.Data != null)
            {
                PatientCardBalance = res.Data.Balance;
            }
            else
            {
                PatientCardBalance = 680.00m;
            }
        }
        catch
        {
            PatientCardBalance = 680.00m;
        }
    }

    public class ApiAccountEnvelope
    {
        public bool Succeeded { get; set; }
        public OpdPatientAccountDto? Data { get; set; }
    }

    public class OpdPatientAccountDto
    {
        public decimal Balance { get; set; }
    }

    private void NotifyToast(string message, string severity)
    {
        if (_shell != null)
        {
            _shell.ShowToast(message, severity);
        }
        else
        {
            Workbench.SystemNotice = message;
        }
    }

    public Dictionary<string, string> BuildPrintContext(object? rowContext = null)
    {
        var ctx = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pat = rowContext as PatientQueueItemModel ?? Workbench.SelectedPatient;
        if (pat != null)
        {
            ctx["patient.fullName"] = pat.PatientName ?? string.Empty;
            ctx["patient.gender"] = pat.Gender ?? string.Empty;
            ctx["patient.age"] = pat.Age.ToString();
            ctx["patient.medicalRecordNo"] = pat.MedicalRecordNo ?? string.Empty;
            ctx["patient.visitNo"] = pat.VisitNo ?? string.Empty;
            ctx["patient.queueNo"] = pat.QueueNo ?? string.Empty;
            ctx["patient.deptName"] = pat.DeptName ?? string.Empty;
            ctx["patient.doctorName"] = pat.DoctorName ?? string.Empty;
            ctx["patient.feeType"] = pat.FeeType ?? string.Empty;
            ctx["patient.chiefComplaint"] = pat.ChiefComplaint ?? string.Empty;
            ctx["patient.allergyTags"] = pat.AllergyTags ?? string.Empty;
        }
        return ctx;
    }
}
