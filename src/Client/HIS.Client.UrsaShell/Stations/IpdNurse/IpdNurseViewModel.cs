using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.IpdNurse;

public sealed class IpdNurseViewModel : ClientModelBase, IShortcutTarget
{
    private readonly ShellViewModel? _shell;
    private bool _subscribed;

    public IpdNurseViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        OpenCriticalValueCommand = new RelayCommand(() => _shell?.OpenCriticalValueDrawerCommand.Execute(null));
        CycleNursingLevelCommand = new RelayCommand(ExecuteCycleNursingLevel);
        ReviewOrdersCommand = new RelayCommand(ExecuteReviewOrders, () => Workbench.CanReviewSelectedInpatientOrders);
        ReviewSingleOrderCommand = new RelayCommand(p => ExecuteReviewSingleOrder(p as InpatientOrderModel));
        DecomposeTodayCommand = new RelayCommand(ExecuteDecomposeToday);
        DecomposeTomorrowCommand = new RelayCommand(ExecuteDecomposeTomorrow);
        OpenFrequencyConfigCommand = new RelayCommand(OpenFrequencyConfigDrawer);
        FilterOrdersCommand = new RelayCommand(p => ApplyOrderFilter(p as string ?? "ALL"));
        ExecuteAllTasksCommand = new RelayCommand(ConfirmAndExecuteAllTasks, () => Workbench.CanExecuteSelectedNursingTasks);
        ExecuteTaskCommand = new RelayCommand(p => ExecuteTask(p as NursingTaskModel));
        VerifyTaskCommand = new RelayCommand(p => VerifyTask(p as NursingTaskModel));
        SaveVitalsCommand = new RelayCommand(ExecuteSaveVitals);
        AddHandoverCommand = new RelayCommand(ExecuteAddHandover);
        ConfirmHandoverCommand = new RelayCommand(p => ExecuteConfirmHandover(p as HandoverNoteModel));
        OpenBorrowBedCommand = new RelayCommand(OpenBorrowBedDrawer);
    }

    public WorkbenchModel Workbench { get; }

    public Action? FocusVitalsInputAction { get; set; }
    public Action? FocusHandoverInputAction { get; set; }

    public string ReviewBlockedReason
    {
        get
        {
            if (Workbench.SelectedInpatient == null)
            {
                return "请先选择左侧病区患者。";
            }
            if (!Workbench.CanExecuteNursingTasks)
            {
                return "当前登录身份无护士站校对/执行权限，请切换为护士或管理员。";
            }
            if (!Workbench.CanReviewSelectedInpatientOrders)
            {
                return "当前患者暂无待校对医嘱。";
            }
            return string.Empty;
        }
    }

    public string ExecuteTasksBlockedReason
    {
        get
        {
            if (Workbench.SelectedInpatient == null)
            {
                return "请先选择左侧病区患者。";
            }
            if (!Workbench.CanExecuteNursingTasks)
            {
                return "当前登录身份无护理执行权限。";
            }
            if (!Workbench.CanExecuteSelectedNursingTasks)
            {
                return "当前患者暂无待执行的护理任务（可先点击“自动分解今日长期医嘱”）。";
            }
            return string.Empty;
        }
    }

    public ICommand OpenCriticalValueCommand { get; }
    public ICommand CycleNursingLevelCommand { get; }
    public ICommand ReviewOrdersCommand { get; }
    public ICommand ReviewSingleOrderCommand { get; }
    public ICommand DecomposeTodayCommand { get; }
    public ICommand DecomposeTomorrowCommand { get; }
    public ICommand OpenFrequencyConfigCommand { get; }
    public ICommand FilterOrdersCommand { get; }
    public ICommand ExecuteAllTasksCommand { get; }
    public ICommand ExecuteTaskCommand { get; }
    public ICommand VerifyTaskCommand { get; }
    public ICommand SaveVitalsCommand { get; }
    public ICommand AddHandoverCommand { get; }
    public ICommand ConfirmHandoverCommand { get; }
    public ICommand OpenBorrowBedCommand { get; }

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
                if (Workbench.SelectedNurseTabIndex == 3)
                {
                    FocusHandoverInputAction?.Invoke();
                }
                else
                {
                    Workbench.SelectedNurseTabIndex = 2;
                    FocusVitalsInputAction?.Invoke();
                }
                return true;
            case "F3":
                OpenFrequencyConfigDrawer();
                return true;
            case "Ctrl+S":
                if (Workbench.SelectedNurseTabIndex == 2)
                {
                    ExecuteSaveVitals();
                    return true;
                }
                if (Workbench.SelectedNurseTabIndex == 3)
                {
                    ExecuteAddHandover();
                    return true;
                }
                return false;
            case "Ctrl+Enter":
                if (Workbench.SelectedNurseTabIndex == 0 && ReviewOrdersCommand.CanExecute(null))
                {
                    ReviewOrdersCommand.Execute(null);
                    return true;
                }
                return false;
            default:
                return false;
        }
    }

    private void ExecuteCycleNursingLevel()
    {
        Workbench.CycleNursingLevel();
        _shell?.ShowToast(Workbench.SystemNotice, "Info");
    }

    private void ExecuteReviewOrders()
    {
        Workbench.ReviewSelectedInpatientOrders();
        _shell?.ShowToast(Workbench.SystemNotice, "Success");
        RaiseComputed();
    }

    private void ExecuteReviewSingleOrder(InpatientOrderModel? order)
    {
        if (order == null || !Workbench.CanExecuteNursingTasks) return;
        Workbench.ReviewSingleInpatientOrder(order);
        _shell?.ShowToast(Workbench.SystemNotice, "Success");
        RaiseComputed();
    }

    private void ExecuteDecomposeToday()
    {
        Workbench.DecomposeTodayOrders();
        _shell?.ShowToast(Workbench.SystemNotice, "Success");
        RaiseComputed();
    }

    private void ExecuteDecomposeTomorrow()
    {
        Workbench.DecomposeTomorrowOrders();
        _shell?.ShowToast(Workbench.SystemNotice, "Success");
        RaiseComputed();
    }

    private void ApplyOrderFilter(string filter)
    {
        if (Workbench.SelectedInpatient != null)
        {
            Workbench.SelectedInpatient.SelectedOrderFilter = filter;
        }
    }

    private void ConfirmAndExecuteAllTasks()
    {
        if (!Workbench.CanExecuteSelectedNursingTasks || Workbench.SelectedInpatient == null) return;

        void Execute()
        {
            Workbench.ExecuteAllNursingTasks();
            _shell?.ShowToast(Workbench.SystemNotice, "Success");
            RaiseComputed();
        }

        if (_shell != null)
        {
            _shell.RequestConfirm(
                "确认批量执行待办护理任务？",
                $"将为患者 {Workbench.SelectedInpatient.PatientName} ({Workbench.SelectedInpatient.BedCode}) 批量执行当前所有待执行任务。",
                "确认批量执行",
                Execute);
            return;
        }

        Execute();
    }

    private void ExecuteTask(NursingTaskModel? task)
    {
        if (task == null || !Workbench.CanExecuteNursingTasks) return;
        Workbench.ExecuteNursingTask(task);
        _shell?.ShowToast(Workbench.SystemNotice, "Success");
        RaiseComputed();
    }

    private void VerifyTask(NursingTaskModel? task)
    {
        if (task == null || !Workbench.CanExecuteNursingTasks) return;
        Workbench.VerifyNursingTask(task);
        _shell?.ShowToast(Workbench.SystemNotice, "Success");
        RaiseComputed();
    }

    private void ExecuteSaveVitals()
    {
        Workbench.SaveVitalSigns();
        _shell?.ShowToast(Workbench.SystemNotice, "Success");
    }

    private void ExecuteAddHandover()
    {
        Workbench.AddHandoverNote();
        _shell?.ShowToast(Workbench.SystemNotice, "Success");
    }

    private void ExecuteConfirmHandover(HandoverNoteModel? note)
    {
        if (note == null || !Workbench.CanExecuteNursingTasks) return;
        Workbench.ConfirmHandoverNote(note);
        _shell?.ShowToast(Workbench.SystemNotice, "Success");
    }

    private void OpenFrequencyConfigDrawer()
    {
        if (_shell == null)
        {
            Workbench.OpenFrequencyConfigModal();
            return;
        }

        var container = new StackPanel { Spacing = 10, DataContext = Workbench };
        container.Children.Add(new TextBlock
        {
            Text = "编辑各频次分解时间点（逗号分隔如 08:00,16:00），关闭并同步后长期医嘱分解引擎即时生效。",
            FontSize = 12,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        var list = new StackPanel { Spacing = 6 };
        foreach (var def in Workbench.FrequencyDefinitions)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("70,110,*"), ColumnSpacing = 8 };
            row.Children.Add(new TextBlock { Text = def.Code.ToUpperInvariant(), FontWeight = Avalonia.Media.FontWeight.Bold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            var nameTb = new TextBlock { Text = def.Name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(nameTb, 1);
            row.Children.Add(nameTb);

            var tpBox = new TextBox { Height = 28, Text = def.TimePointsText };
            tpBox.TextChanged += (_, _) =>
            {
                def.TimePointsText = tpBox.Text ?? string.Empty;
            };
            Grid.SetColumn(tpBox, 2);
            row.Children.Add(tpBox);
            list.Children.Add(row);
        }
        container.Children.Add(list);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Avalonia.Thickness(0, 8, 0, 0) };
        var resetBtn = new Button
        {
            Content = "恢复三甲推荐",
            Height = 32,
            Command = new RelayCommand(() =>
            {
                Workbench.ResetFrequencyDefinitionsToDefault();
                _shell.CloseDrawer();
                _shell.ShowToast(Workbench.SystemNotice, "Info");
            })
        };
        var saveBtn = new Button
        {
            Content = "同步分解配置",
            Height = 32,
            Command = new RelayCommand(() =>
            {
                Workbench.CloseFrequencyConfigModal();
                _shell.CloseDrawer();
                _shell.ShowToast(Workbench.SystemNotice, "Success");
            })
        };
        saveBtn.Classes.Add("Primary");
        actions.Children.Add(resetBtn);
        actions.Children.Add(saveBtn);
        container.Children.Add(actions);

        _shell.OpenDrawer("医嘱频次与分解时间点规则", "按频次定义时间点自动生成护理执行单", container, 520);
    }

    private void OpenBorrowBedDrawer()
    {
        if (_shell == null)
        {
            Workbench.IsBorrowBedDialogOpen = true;
            return;
        }

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = $"住院患者: {Workbench.BorrowBedPatientSummary}", FontSize = 13, FontWeight = Avalonia.Media.FontWeight.Bold });
        panel.Children.Add(new TextBlock { Text = $"住院记录: {Workbench.BorrowBedAdmissionSummary}", FontSize = 12 });
        panel.Children.Add(new TextBlock { Text = $"医疗归属科室: {Workbench.BorrowBedAdmittingDept}", FontSize = 12 });
        panel.Children.Add(new TextBlock { Text = $"实际物理护理病区: {Workbench.BorrowBedAccommodationSummary}", FontSize = 12 });
        panel.Children.Add(new TextBlock { Text = $"主管医师: {Workbench.BorrowBedAttendingDoctor}", FontSize = 12 });

        var confirmBtn = new Button
        {
            Content = "办理跨病区借床入科",
            Height = 34,
            IsEnabled = Workbench.IsInpatientContext,
            Margin = new Avalonia.Thickness(0, 8, 0, 0),
            Command = new RelayCommand(() =>
            {
                Workbench.SystemNotice = "✓ 跨病区借床入科办理完成！医疗归属与护理职责已独立路由。";
                _shell.ShowToast(Workbench.SystemNotice, "Success");
                _shell.CloseDrawer();
            })
        };
        confirmBtn.Classes.Add("Primary");
        panel.Children.Add(confirmBtn);

        _shell.OpenDrawer("跨病区借床入科", "主管科室负责诊疗 · 借住病区负责护理", panel, 480);
    }

    private void OnWorkbenchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkbenchModel.SelectedInpatient)
            or nameof(WorkbenchModel.CanExecuteNursingTasks)
            or nameof(WorkbenchModel.CanReviewSelectedInpatientOrders)
            or nameof(WorkbenchModel.CanExecuteSelectedNursingTasks))
        {
            RaiseComputed();
        }
    }

    private void RaiseComputed()
    {
        OnPropertyChanged(nameof(ReviewBlockedReason));
        OnPropertyChanged(nameof(ExecuteTasksBlockedReason));
        (ReviewOrdersCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ExecuteAllTasksCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }
}
