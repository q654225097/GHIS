using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Layout;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.MedTech;

public sealed class MedTechStationViewModel : ClientModelBase, IShortcutTarget
{
    private readonly ShellViewModel? _shell;
    private string _searchKeyword = string.Empty;
    private string _statusFilter = "ALL";
    private bool _subscribed;

    public MedTechStationViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        OpenCriticalValueCommand = new RelayCommand(() => _shell?.OpenCriticalValueDrawerCommand.Execute(null));
        OpenWhoSurgeryDrawerCommand = new RelayCommand(OpenWhoSurgeryDrawer);
        OpenRefundProofDrawerCommand = new RelayCommand(OpenRefundProofDrawer);
        FilterByStatusCommand = new RelayCommand(p => ApplyFilter(p as string ?? "ALL"));
        ConfirmFeeCommand = new RelayCommand(p => ExecuteConfirmFee(p as MedTechOrderItemModel));
        PublishReportCommand = new RelayCommand(p => ExecutePublishReport(p as MedTechOrderItemModel));
        CancelOrderCommand = new RelayCommand(p => ConfirmCancelOrder(p as MedTechOrderItemModel));
        RefreshCommand = new RelayCommand(RefreshList);

        RebuildFiltered();
    }

    public WorkbenchModel Workbench { get; }

    public ObservableCollection<MedTechOrderItemModel> FilteredOrders { get; } = new();

    public Action? FocusSearchAction { get; set; }

    public string SearchKeyword
    {
        get => _searchKeyword;
        set
        {
            if (SetField(ref _searchKeyword, value))
            {
                RebuildFiltered();
            }
        }
    }

    public string StatusFilter
    {
        get => _statusFilter;
        private set
        {
            if (SetField(ref _statusFilter, value))
            {
                OnPropertyChanged(nameof(IsAllFilter));
                OnPropertyChanged(nameof(IsPendingFeeFilter));
                OnPropertyChanged(nameof(IsLockedFilter));
                OnPropertyChanged(nameof(IsReportedFilter));
                RebuildFiltered();
            }
        }
    }

    public bool IsAllFilter => StatusFilter == "ALL";
    public bool IsPendingFeeFilter => StatusFilter == "PENDING";
    public bool IsLockedFilter => StatusFilter == "LOCKED";
    public bool IsReportedFilter => StatusFilter == "REPORTED";

    public int TotalCount => Workbench.MedTechOrders.Count;
    public int PendingFeeCount => Workbench.MedTechOrders.Count(o => o.CanConfirmFee);
    public int LockedCount => Workbench.MedTechOrders.Count(o => o.IsFeeLocked && o.Status != "已出报告");
    public int ReportedCount => Workbench.MedTechOrders.Count(o => o.Status == "已出报告");

    public ICommand OpenCriticalValueCommand { get; }
    public ICommand OpenWhoSurgeryDrawerCommand { get; }
    public ICommand OpenRefundProofDrawerCommand { get; }
    public ICommand FilterByStatusCommand { get; }
    public ICommand ConfirmFeeCommand { get; }
    public ICommand PublishReportCommand { get; }
    public ICommand CancelOrderCommand { get; }
    public ICommand RefreshCommand { get; }

    public void Attach()
    {
        if (!_subscribed)
        {
            Workbench.PropertyChanged += OnWorkbenchPropertyChanged;
            Workbench.MedTechOrders.CollectionChanged += OnOrdersCollectionChanged;
            _subscribed = true;
        }
        RebuildFiltered();
        RaiseCounts();
    }

    public void Detach()
    {
        if (_subscribed)
        {
            Workbench.PropertyChanged -= OnWorkbenchPropertyChanged;
            Workbench.MedTechOrders.CollectionChanged -= OnOrdersCollectionChanged;
            _subscribed = false;
        }
    }

    public bool OnShortcut(string keyGesture)
    {
        switch (keyGesture)
        {
            case "F2":
                OpenWhoSurgeryDrawer();
                return true;
            case "F3":
            case "Ctrl+F":
                FocusSearchAction?.Invoke();
                return true;
            case "F5":
                RefreshList();
                return true;
            case "Escape" when !string.IsNullOrEmpty(SearchKeyword):
                SearchKeyword = string.Empty;
                return true;
            default:
                return false;
        }
    }

    private void OnWorkbenchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkbenchModel.MedTechOrders)
            or nameof(WorkbenchModel.HasFeeLockedMedTechOrder)
            or nameof(WorkbenchModel.FeeLockedMedTechOrderText))
        {
            RebuildFiltered();
            RaiseCounts();
        }
    }

    private void OnOrdersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildFiltered();
        RaiseCounts();
    }

    private void ApplyFilter(string mode) => StatusFilter = mode;

    private void RefreshList()
    {
        SearchKeyword = string.Empty;
        StatusFilter = "ALL";
        RebuildFiltered();
        RaiseCounts();
        _shell?.ShowToast($"已刷新医技申请列表（共 {TotalCount} 条）。", "Info");
    }

    private void RebuildFiltered()
    {
        FilteredOrders.Clear();
        string kw = (SearchKeyword ?? string.Empty).Trim();

        foreach (var item in Workbench.MedTechOrders)
        {
            if (StatusFilter == "PENDING" && !item.CanConfirmFee) continue;
            if (StatusFilter == "LOCKED" && !(item.IsFeeLocked && item.Status != "已出报告")) continue;
            if (StatusFilter == "REPORTED" && item.Status != "已出报告") continue;

            if (!string.IsNullOrEmpty(kw))
            {
                bool hit = item.RequestNo.Contains(kw, StringComparison.OrdinalIgnoreCase)
                    || item.ItemName.Contains(kw, StringComparison.OrdinalIgnoreCase)
                    || item.DeptName.Contains(kw, StringComparison.OrdinalIgnoreCase)
                    || item.ExecLocation.Contains(kw, StringComparison.OrdinalIgnoreCase);
                if (!hit) continue;
            }

            FilteredOrders.Add(item);
        }
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(PendingFeeCount));
        OnPropertyChanged(nameof(LockedCount));
        OnPropertyChanged(nameof(ReportedCount));
    }

    private void ExecuteConfirmFee(MedTechOrderItemModel? item)
    {
        if (item == null) return;
        Workbench.ConfirmMedTechFee(item);
        RebuildFiltered();
        RaiseCounts();
        _shell?.ShowToast($"已确认收费并锁单：{item.ItemName} ({item.RequestNo})", "Success");
    }

    private void ExecutePublishReport(MedTechOrderItemModel? item)
    {
        if (item == null) return;
        if (!item.CanPublishReport)
        {
            _shell?.ShowToast($"{item.ItemName} 需先完成确费锁单后才可出具报告。", "Warning");
            return;
        }
        Workbench.PublishMedTechReport(item);
        RebuildFiltered();
        RaiseCounts();
        _shell?.ShowToast($"已签发医技报告：{item.ItemName} ({item.RequestNo})", "Success");
    }

    private void ConfirmCancelOrder(MedTechOrderItemModel? item)
    {
        if (item == null) return;
        if (item.IsFeeLocked)
        {
            Workbench.TryCancelMedTechOrder(item);
            OpenRefundProofDrawer();
            _shell?.ShowToast("该申请已完成确费锁单，不可直接撤单。", "Warning");
            return;
        }

        _shell?.RequestConfirm(
            "确认撤销医技申请",
            $"确定撤销 {item.ItemName}（申请单 {item.RequestNo}）？撤单后需临床医生重新开单。",
            "确认撤单",
            () =>
            {
                Workbench.TryCancelMedTechOrder(item);
                RebuildFiltered();
                RaiseCounts();
                _shell?.ShowToast($"已撤回医技申请：{item.RequestNo}", "Info");
            });
    }

    private void OpenRefundProofDrawer()
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(Workbench.FeeLockedMedTechOrderText)
                ? "当前无可撤单锁单异常项；已完成确费的申请单不可直接撤单。"
                : Workbench.FeeLockedMedTechOrderText,
            FontSize = 13,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        panel.Children.Add(new Border
        {
            Padding = new Avalonia.Thickness(12),
            CornerRadius = new Avalonia.CornerRadius(6),
            Background = ThemeBrushes.WarningLight,
            BorderBrush = ThemeBrushes.Warning,
            BorderThickness = new Avalonia.Thickness(1),
            Child = new TextBlock
            {
                Text = "冲正规定：已确费锁单项目不得单向撤销，须凭医技科室主任签章的《医技冲正退费证明单》前往收费处办理冲正退费。",
                FontSize = 12,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            }
        });
        _shell?.OpenDrawer("医技确费锁单与退费依据", "防误退费审计规范", panel, 460);
    }

    private void OpenWhoSurgeryDrawer()
    {
        var who = Workbench.WhoSurgeryCheck;
        var panel = new StackPanel { Spacing = 12, DataContext = who };

        panel.Children.Add(new TextBlock
        {
            Text = $"{who.SurgeryName} · {who.AnesthesiaType}",
            FontSize = 15,
            FontWeight = Avalonia.Media.FontWeight.Bold,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"患者: {who.PatientName} ({who.AdmissionNo}) | 状态: {who.CheckStatus}",
            FontSize = 12,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"主刀: {who.Surgeon} | 麻醉: {who.Anesthesiologist} | 巡回护士: {who.CirculatingNurse}",
            FontSize = 12,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        panel.Children.Add(CreateCheckCard($"阶段 A · 麻醉实施前 ({who.Stage1Badge})",
            $"身份核对={who.SignInIdentityOk} | 手术部位标识={who.SignInSiteMarked} | 麻醉安全检查={who.SignInAnesthesiaSafetyCheck}"));
        panel.Children.Add(CreateCheckCard($"阶段 B · 切开皮肤前 ({who.Stage2Badge})",
            $"三方共同确认={who.TimeOutAllTeamAgreed} | 预防抗菌药={who.TimeOutAntibioticGiven} | 影像资料到位={who.TimeOutImagingDisplayed}"));
        panel.Children.Add(CreateCheckCard($"阶段 C · 患者离室前 ({who.Stage3Badge})",
            $"器械敷料清点一致={who.SignOutInstrumentCountMatched} | 病理标本标记={who.SignOutSpecimenLabeled}"));

        var btn = new Button
        {
            Content = "三方核验并签发允许手术",
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Command = new RelayCommand(() =>
            {
                who.Stage1Confirmed = true;
                who.Stage2Confirmed = true;
                who.Stage3Confirmed = true;
                who.CheckStatus = "✓ 三方全员核验签署完毕 · 允许实施切开与器械离室";
                Workbench.SystemNotice = "✓ WHO 手术安全核查三方签署完毕！";
                _shell?.CloseDrawer();
                _shell?.ShowToast("WHO 手术安全三方核查已签发。", "Success");
            })
        };
        btn.Classes.Add("Primary");
        panel.Children.Add(btn);

        _shell?.OpenDrawer("WHO 手术安全三方核查", $"{who.PatientName} ({who.AdmissionNo})", panel, 520);
    }

    private static Border CreateCheckCard(string title, string detail)
    {
        return new Border
        {
            Padding = new Avalonia.Thickness(12),
            CornerRadius = new Avalonia.CornerRadius(6),
            BorderBrush = ThemeBrushes.Border,
            BorderThickness = new Avalonia.Thickness(1),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = title, FontWeight = Avalonia.Media.FontWeight.Bold, FontSize = 13 },
                    new TextBlock { Text = detail, FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap }
                }
            }
        };
    }
}
