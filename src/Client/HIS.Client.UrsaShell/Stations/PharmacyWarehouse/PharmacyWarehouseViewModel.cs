using System.ComponentModel;
using System.Windows.Input;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.PharmacyWarehouse;

public sealed class PharmacyWarehouseViewModel : ClientModelBase, IShortcutTarget
{
    private readonly ShellViewModel? _shell;
    private int _localTabIndex;
    private bool _subscribed;

    public PharmacyWarehouseViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        RefreshStockCommand = new AsyncRelayCommand(() => Pharmacy.RefreshStockAsync());
        StockInCommand = new AsyncRelayCommand(StockInWithFeedbackAsync);
        WarehouseIssueCommand = new AsyncRelayCommand(WarehouseIssueWithFeedbackAsync);
        TransferCommand = new RelayCommand(ConfirmAndTransfer);
        SearchDrugsCommand = new AsyncRelayCommand(() => Pharmacy.SearchDrugsAsync());
        NewDrugCommand = new RelayCommand(ExecuteNewDrug, () => Pharmacy.CanEditDrugs);
        SaveDrugCommand = new AsyncRelayCommand(SaveDrugWithFeedbackAsync, () => Pharmacy.CanEditDrugs);
        ToggleDrugCommand = new RelayCommand(ConfirmAndToggleDrug, () => Pharmacy.CanEditDrugs);
    }

    public WorkbenchModel Workbench { get; }
    public PharmacyWorkstationViewModel Pharmacy => Workbench.Pharmacy;

    public Action? FocusDrugSearchAction { get; set; }
    public Action? ClearDrugListSelectionAction { get; set; }

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

    public string DrugEditBlockedReason =>
        Pharmacy.CanEditDrugs ? string.Empty : "当前账号只读：药品新增、调价与停用操作需系统管理员权限。";

    public ICommand RefreshCommand { get; }
    public ICommand RefreshStockCommand { get; }
    public ICommand StockInCommand { get; }
    public ICommand WarehouseIssueCommand { get; }
    public ICommand TransferCommand { get; }
    public ICommand SearchDrugsCommand { get; }
    public ICommand NewDrugCommand { get; }
    public ICommand SaveDrugCommand { get; }
    public ICommand ToggleDrugCommand { get; }

    public async Task OnAttachedAsync()
    {
        if (!_subscribed)
        {
            Pharmacy.PropertyChanged += OnPharmacyPropertyChanged;
            _subscribed = true;
        }
        Pharmacy.AttachSession();
        Pharmacy.SelectedTabIndex = MapLocalTabToSharedIndex(_localTabIndex);
        await Pharmacy.EnsureLoadedAsync();
        Pharmacy.SelectedTabIndex = MapLocalTabToSharedIndex(_localTabIndex);
        RaiseAllComputed();
    }

    public void OnDetached()
    {
        if (_subscribed)
        {
            Pharmacy.PropertyChanged -= OnPharmacyPropertyChanged;
            _subscribed = false;
        }
        Pharmacy.DetachSession();
    }

    public async Task OnWarehouseChangedAsync()
    {
        if (Pharmacy.SelectedWarehouse != null)
        {
            await Pharmacy.RefreshStockAsync();
            RaiseAllComputed();
        }
    }

    public bool OnShortcut(string keyGesture)
    {
        switch (keyGesture)
        {
            case "F2":
                LocalTabIndex = 2;
                FocusDrugSearchAction?.Invoke();
                return true;
            case "F5":
                if (RefreshCommand.CanExecute(null)) RefreshCommand.Execute(null);
                return true;
            default:
                return false;
        }
    }

    private async Task RefreshAsync()
    {
        Pharmacy.SelectedTabIndex = MapLocalTabToSharedIndex(LocalTabIndex);
        await Pharmacy.RefreshActiveTabAsync();
        RaiseAllComputed();
        if (!Pharmacy.HasError)
        {
            _shell?.ShowToast("已刷新当前药库数据", "Info");
        }
    }

    private async Task StockInWithFeedbackAsync()
    {
        await Pharmacy.StockInAsync();
        RaiseAllComputed();
        ForwardToast();
    }

    private async Task WarehouseIssueWithFeedbackAsync()
    {
        await Pharmacy.WarehouseIssueAsync();
        RaiseAllComputed();
        ForwardToast();
    }

    private void ConfirmAndTransfer()
    {
        async Task ExecuteAsync()
        {
            await Pharmacy.TransferAsync();
            RaiseAllComputed();
            ForwardToast();
        }

        if (_shell != null)
        {
            var drugName = Pharmacy.IssueDrug?.DisplayName ?? "所选药品";
            var target = Pharmacy.TargetPharmacy?.Name ?? "目标库房";
            _shell.RequestConfirm(
                "确认执行跨库调拨？",
                $"将 {drugName} × {Pharmacy.IssueQuantity} 从「{Pharmacy.CurrentWarehouseText}」按 FEFO 批次调拨至「{target}」。",
                "确认调拨",
                () => _ = ExecuteAsync());
            return;
        }

        _ = ExecuteAsync();
    }

    private void ExecuteNewDrug()
    {
        Pharmacy.NewDrug();
        ClearDrugListSelectionAction?.Invoke();
    }

    private async Task SaveDrugWithFeedbackAsync()
    {
        await Pharmacy.SaveDrugAsync();
        RaiseAllComputed();
        ForwardToast();
    }

    private void ConfirmAndToggleDrug()
    {
        async Task ExecuteAsync()
        {
            await Pharmacy.ToggleDrugAsync();
            RaiseAllComputed();
            ForwardToast();
        }

        if (_shell != null && Pharmacy.SelectedDrug != null)
        {
            var action = Pharmacy.SelectedDrug.IsActive ? "停用" : "启用";
            _shell.RequestConfirm(
                $"确认{action}药品？",
                $"即将对药品「{Pharmacy.SelectedDrug.DrugName} ({Pharmacy.SelectedDrug.DrugCode})」执行{action}状态切换。",
                $"确认{action}",
                () => _ = ExecuteAsync());
            return;
        }

        _ = ExecuteAsync();
    }

    private void ForwardToast()
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

    private static int MapLocalTabToSharedIndex(int localIndex) => localIndex switch
    {
        0 => 1, // 采购入库与结存
        1 => 2, // 药库出库与调拨
        2 => 0, // 药品基础目录
        _ => 1
    };

    private void OnPharmacyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PharmacyWorkstationViewModel.CanEditDrugs)
            or nameof(PharmacyWorkstationViewModel.IsBusy))
        {
            RaiseAllComputed();
        }
    }

    private void RaiseAllComputed()
    {
        OnPropertyChanged(nameof(DrugEditBlockedReason));
        (NewDrugCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SaveDrugCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ToggleDrugCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }
}
