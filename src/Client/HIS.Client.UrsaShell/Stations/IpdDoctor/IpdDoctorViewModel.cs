using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Layout;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.IpdDoctor;

public sealed class IpdDoctorViewModel : ClientModelBase, IShortcutTarget
{
    private const int OrdersWorkspace = 0;
    private const int NotesWorkspace = 1;
    private const int DischargeWorkspace = 2;

    private readonly ShellViewModel? _shell;
    private int _selectedWorkspaceIndex;
    private bool _isLongTermOrder = true;
    private bool _subscribed;

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
    }

    public WorkbenchModel Workbench { get; }

    public Action? FocusOrderInputAction { get; set; }

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
                if (SelectedWorkspaceIndex == OrdersWorkspace && CanSubmitOrder())
                {
                    SubmitOrderCommand.Execute(null);
                    return true;
                }
                return false;
            default:
                return false;
        }
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
        && !string.IsNullOrWhiteSpace(Workbench.NewOrderContent);

    private void ExecuteSubmitOrder()
    {
        ExecuteAddOrder(IsLongTermOrder);
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
        (SignLatestProgressNoteCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ApproveDischargeCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }
}

