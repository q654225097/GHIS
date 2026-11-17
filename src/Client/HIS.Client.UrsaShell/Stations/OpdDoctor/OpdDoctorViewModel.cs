using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.Shell.Controls;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.OpdDoctor;

public sealed class OpdDoctorViewModel : ClientModelBase, IShortcutTarget
{
    private readonly ShellViewModel? _shell;
    private Action? _focusDrugSearchCallback;
    private string _drugSearchQuery = string.Empty;
    private bool _isDrugDropdownOpen;
    private int _selectedCatalogIndex;

    public OpdDoctorViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;
        Workbench.PropertyChanged += OnWorkbenchPropertyChanged;

        CallNextCommand = new AsyncRelayCommand(CallNextPatientAsync);
        SelectPatientCommand = new RelayCommand(param =>
        {
            if (param is PatientQueueItemModel patient)
            {
                Workbench.SelectedPatient = patient;
            }
        });

        SelectStepTabCommand = new RelayCommand(param =>
        {
            if (param is int idx) Workbench.SelectedOpdTabIndex = idx;
            else if (param is string s && int.TryParse(s, out var parsed)) Workbench.SelectedOpdTabIndex = parsed;
        });

        SignEmrCommand = new AsyncRelayCommand(SignCurrentEmrAsync, () => Workbench.CanSignEmr && Workbench.HasSelectedPatient);

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

        AddMedTechOrderCommand = new RelayCommand(AddSampleMedTechOrder);
        OpenWhoSurgeryDrawerCommand = new RelayCommand(OpenWhoSurgeryDrawer);
        OpenRefundProofDrawerCommand = new RelayCommand(OpenRefundProofDrawer);
        OpenBorrowBedDrawerCommand = new RelayCommand(OpenBorrowBedDrawer);
        RequestFinishVisitCommand = new RelayCommand(
            OpenFinishVisitChecklistDrawer,
            () => Workbench.CanFinishOpdVisit || Workbench.CanCompleteOpdWithNoPrescription);

        RefreshDrugSearchResults();
        RefreshSteps();
    }

    public WorkbenchModel Workbench { get; }
    public ObservableCollection<DrugCatalogItemModel> FilteredDrugCatalog { get; } = new();
    public ObservableCollection<FlowStepItem> WorkflowSteps { get; } = new();

    public ICommand CallNextCommand { get; }
    public ICommand SelectPatientCommand { get; }
    public ICommand SelectStepTabCommand { get; }
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
    public ICommand AddMedTechOrderCommand { get; }
    public ICommand OpenWhoSurgeryDrawerCommand { get; }
    public ICommand OpenRefundProofDrawerCommand { get; }
    public ICommand OpenBorrowBedDrawerCommand { get; }
    public ICommand RequestFinishVisitCommand { get; }

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
            if (!Workbench.IsOpdEmrSigned) return "待完成步骤②：请填写初步诊断后点击“保存并提交审签 (Ctrl+S)”";
            if (!Workbench.IsOpdOrdersPushed) return "待完成步骤③：请开立处方并提交药师审方 (Ctrl+Enter)";
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
                    : string.IsNullOrWhiteSpace(Workbench.EmrWorkspace.Diagnosis)
                        ? "请先在“门诊病历”页填写初步诊断后再开立处方"
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

    private async Task SignCurrentEmrAsync()
    {
        var outcome = await Workbench.SaveAndSignOpdEmrAsync();
        NotifyToast(outcome.Message, outcome.Succeeded ? "Success" : "Danger");
        if (outcome.Succeeded)
        {
            Workbench.SelectedOpdTabIndex = 1;
        }
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

        panel.Children.Add(CreateChecklistRow("① 患者接诊状态", Workbench.IsOpdVisitActive ? "✓ 已接诊" : "未接诊"));
        panel.Children.Add(CreateChecklistRow("② 门诊病历审签", Workbench.IsOpdEmrSigned ? "✓ 已保存并审签" : "未审签"));
        panel.Children.Add(CreateChecklistRow("③ 处方与医技", Workbench.IsOpdOrdersPushed ? "✓ 已提交药师/医技" : "未推送"));
        panel.Children.Add(CreateChecklistRow("④ 用药安全检查", Workbench.HasBlockedPrescription ? "⚠ 存在过敏拦截项" : "✓ 无冲突"));

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

    private void RefreshSteps()
    {
        WorkflowSteps.Clear();
        var active = Workbench.IsOpdVisitActive;
        var emrDone = Workbench.IsOpdEmrSigned;
        var rxDone = Workbench.IsOpdOrdersPushed;
        var completed = Workbench.SelectedPatient?.IsCompleted == true;

        WorkflowSteps.Add(new FlowStepItem
        {
            Index = 1,
            Title = "接诊叫号",
            State = (active || completed) ? FlowStepState.Completed : FlowStepState.Active,
            Tooltip = "点击选中待诊患者或按 F2 叫号下一位"
        });
        WorkflowSteps.Add(new FlowStepItem
        {
            Index = 2,
            Title = "病历审签",
            State = (emrDone || completed) ? FlowStepState.Completed : (active ? FlowStepState.Active : FlowStepState.Pending),
            Tooltip = "填写主诉、现病史与初步诊断并审签 (Ctrl+S)",
            Command = new RelayCommand(() => Workbench.SelectedOpdTabIndex = 0)
        });
        WorkflowSteps.Add(new FlowStepItem
        {
            Index = 3,
            Title = "处方与检查",
            State = (rxDone || completed) ? FlowStepState.Completed : (emrDone ? FlowStepState.Active : FlowStepState.Pending),
            Tooltip = "行内检索加药开立处方或检查申请并推送 (Ctrl+Enter)",
            Command = new RelayCommand(() => Workbench.SelectedOpdTabIndex = 1)
        });
        WorkflowSteps.Add(new FlowStepItem
        {
            Index = 4,
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
        (SignEmrCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (PreCheckPrescriptionCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (SubmitPrescriptionCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (RequestFinishVisitCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void OnWorkbenchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkbenchModel.SelectedPatient)
            or nameof(WorkbenchModel.IsOpdVisitActive)
            or nameof(WorkbenchModel.IsOpdEmrSigned)
            or nameof(WorkbenchModel.IsOpdOrdersPushed)
            or nameof(WorkbenchModel.CanFinishOpdVisit)
            or nameof(WorkbenchModel.CanCompleteOpdWithNoPrescription)
            or nameof(WorkbenchModel.CanEditOpdPrescription)
            or nameof(WorkbenchModel.SelectedPrescriptionFilter)
            or nameof(WorkbenchModel.HasBlockedPrescription))
        {
            RefreshAllWorkflowState();
        }
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
}
