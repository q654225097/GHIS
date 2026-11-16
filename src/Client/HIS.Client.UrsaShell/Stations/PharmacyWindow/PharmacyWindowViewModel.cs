using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.PharmacyWindow;

public sealed class PharmacyWindowViewModel : ClientModelBase, IShortcutTarget
{
    private readonly ShellViewModel? _shell;
    private int _localTabIndex;
    private string _prescriptionFilter = string.Empty;
    private bool _subscribed;

    public PharmacyWindowViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        DispenseOutpatientCommand = new AsyncRelayCommand(DispenseOutpatientWithFeedbackAsync, () => Pharmacy.CanDispenseSelected);
        FilterWardCommand = new AsyncRelayCommand(() => Pharmacy.FilterWardAsync());
        WardDispenseCommand = new AsyncRelayCommand(WardDispenseWithFeedbackAsync);
        RequestReturnCommand = new RelayCommand(ConfirmAndReturn, () => Pharmacy.CanReturnSelected);
        ClearFilterCommand = new RelayCommand(() => PrescriptionFilter = string.Empty);

        ApplyPrescriptionFilter();
    }

    public WorkbenchModel Workbench { get; }
    public PharmacyWorkstationViewModel Pharmacy => Workbench.Pharmacy;

    public ObservableCollection<ReadyPrescriptionRow> FilteredReadyPrescriptions { get; } = new();

    public Action? FocusSearchBoxAction { get; set; }

    public int LocalTabIndex
    {
        get => _localTabIndex;
        set
        {
            if (SetField(ref _localTabIndex, value))
            {
                Pharmacy.SelectedTabIndex = MapLocalTabToSharedIndex(value);
            }
        }
    }

    public string PrescriptionFilter
    {
        get => _prescriptionFilter;
        set
        {
            if (SetField(ref _prescriptionFilter, value))
            {
                ApplyPrescriptionFilter();
            }
        }
    }

    public string DispenseBlockedReason
    {
        get
        {
            if (Pharmacy.SelectedPharmacy == null)
            {
                return "请先在顶部选择当前发药窗口对应的发药药房";
            }
            if (Pharmacy.SelectedReadyPrescription == null)
            {
                return "请先在左侧列表选择一条已收费处方";
            }
            if (Pharmacy.SelectedReadyPrescription.IsOrderClosed)
            {
                return "该发药单已关单归档，禁止二次发药";
            }
            if (!Pharmacy.SelectedReadyPrescription.CanDispense)
            {
                return $"当前处方状态为“{Pharmacy.SelectedReadyPrescription.StateText}”，暂不可发药";
            }
            return Pharmacy.IsBusy ? "正在执行发药提交，请稍候…" : string.Empty;
        }
    }

    public string ReturnBlockedReason
    {
        get
        {
            if (Pharmacy.SelectedPharmacy == null)
            {
                return "请先在顶部选择发药药房";
            }
            if (Pharmacy.SelectedDispense == null)
            {
                return "请先在左侧选择一条历史发药单";
            }
            if (Pharmacy.SelectedDispense.IsReturned)
            {
                return "该发药单已完成退药质检入库，不可重复退药";
            }
            return Pharmacy.IsBusy ? "正在提交退药入库，请稍候…" : string.Empty;
        }
    }

    public ICommand RefreshCommand { get; }
    public ICommand DispenseOutpatientCommand { get; }
    public ICommand FilterWardCommand { get; }
    public ICommand WardDispenseCommand { get; }
    public ICommand RequestReturnCommand { get; }
    public ICommand ClearFilterCommand { get; }

    public async Task OnAttachedAsync()
    {
        if (!_subscribed)
        {
            Pharmacy.PropertyChanged += OnPharmacyPropertyChanged;
            Pharmacy.ReadyPrescriptions.CollectionChanged += OnReadyCollectionChanged;
            _subscribed = true;
        }
        Pharmacy.AttachSession();
        Pharmacy.SelectedTabIndex = MapLocalTabToSharedIndex(_localTabIndex);
        await Pharmacy.EnsureLoadedAsync();
        Pharmacy.SelectedTabIndex = MapLocalTabToSharedIndex(_localTabIndex);
        ApplyPrescriptionFilter();
        RaiseAllComputed();
    }

    public void OnDetached()
    {
        if (_subscribed)
        {
            Pharmacy.PropertyChanged -= OnPharmacyPropertyChanged;
            Pharmacy.ReadyPrescriptions.CollectionChanged -= OnReadyCollectionChanged;
            _subscribed = false;
        }
        Pharmacy.DetachSession();
    }

    public async Task OnScopePharmacyChangedAsync()
    {
        if (Pharmacy.SelectedPharmacy != null)
        {
            await Pharmacy.RefreshPharmacyScopeAsync();
            ApplyPrescriptionFilter();
            RaiseAllComputed();
        }
    }

    public async Task SelectDispenseAsync(DispenseRow? row)
    {
        await Pharmacy.SelectDispenseAsync(row);
        RaiseAllComputed();
    }

    public bool OnShortcut(string keyGesture)
    {
        switch (keyGesture)
        {
            case "F2":
                LocalTabIndex = 0;
                FocusSearchBoxAction?.Invoke();
                return true;
            case "F5":
                if (RefreshCommand.CanExecute(null)) RefreshCommand.Execute(null);
                return true;
            case "Ctrl+Enter":
                if (LocalTabIndex == 0 && DispenseOutpatientCommand.CanExecute(null))
                {
                    DispenseOutpatientCommand.Execute(null);
                    return true;
                }
                return false;
            default:
                return false;
        }
    }

    private async Task RefreshAsync()
    {
        Pharmacy.SelectedTabIndex = MapLocalTabToSharedIndex(LocalTabIndex);
        await Pharmacy.RefreshActiveTabAsync();
        ApplyPrescriptionFilter();
        RaiseAllComputed();
        if (!Pharmacy.HasError)
        {
            _shell?.ShowToast("已刷新当前发药队列", "Info");
        }
    }

    private async Task DispenseOutpatientWithFeedbackAsync()
    {
        await Pharmacy.DispenseOutpatientAsync();
        ApplyPrescriptionFilter();
        RaiseAllComputed();
        ForwardStatusToast();
    }

    private async Task WardDispenseWithFeedbackAsync()
    {
        await Pharmacy.WardDispenseAsync();
        RaiseAllComputed();
        ForwardStatusToast();
    }

    private void ConfirmAndReturn()
    {
        if (Pharmacy.SelectedDispense == null)
        {
            return;
        }

        async Task ExecuteAsync()
        {
            await Pharmacy.ConfirmReturnAsync();
            RaiseAllComputed();
            ForwardStatusToast();
        }

        if (_shell != null)
        {
            _shell.RequestConfirm(
                "确认退药质检入库？",
                $"发药单 {Pharmacy.SelectedDispense.DispenseNo} 完成退药入库后，库存将回补原发药批次并推送至财务待退费队列。",
                "确认退药入库",
                () => _ = ExecuteAsync());
            return;
        }

        _ = ExecuteAsync();
    }

    private void ForwardStatusToast()
    {
        if (Pharmacy.HasError)
        {
            _shell?.ShowToast(Pharmacy.ErrorMessage, "Error");
        }
        else if (Pharmacy.HasNotice)
        {
            _shell?.ShowToast(Pharmacy.Notice, "Success");
        }
    }

    private void ApplyPrescriptionFilter()
    {
        FilteredReadyPrescriptions.Clear();
        var kw = _prescriptionFilter.Trim();
        foreach (var row in Pharmacy.ReadyPrescriptions)
        {
            if (string.IsNullOrEmpty(kw)
                || row.PrescriptionNo.Contains(kw, StringComparison.OrdinalIgnoreCase)
                || row.PatientName.Contains(kw, StringComparison.OrdinalIgnoreCase)
                || row.MedicalRecordNo.Contains(kw, StringComparison.OrdinalIgnoreCase))
            {
                FilteredReadyPrescriptions.Add(row);
            }
        }
    }

    private static int MapLocalTabToSharedIndex(int localIndex) => localIndex switch
    {
        0 => 4, // 门诊发药
        1 => 5, // 住院发药
        2 => 3, // 发药记录与退药质检
        _ => 4
    };

    private void OnReadyCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        ApplyPrescriptionFilter();
    }

    private void OnPharmacyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PharmacyWorkstationViewModel.CanDispenseSelected)
            or nameof(PharmacyWorkstationViewModel.CanReturnSelected)
            or nameof(PharmacyWorkstationViewModel.SelectedPharmacy)
            or nameof(PharmacyWorkstationViewModel.SelectedReadyPrescription)
            or nameof(PharmacyWorkstationViewModel.SelectedDispense)
            or nameof(PharmacyWorkstationViewModel.IsBusy))
        {
            RaiseAllComputed();
        }
    }

    private void RaiseAllComputed()
    {
        OnPropertyChanged(nameof(DispenseBlockedReason));
        OnPropertyChanged(nameof(ReturnBlockedReason));
        (DispenseOutpatientCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (RequestReturnCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }
}
