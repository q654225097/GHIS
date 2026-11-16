using System;
using System.ComponentModel;
using System.Windows.Input;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.Registration;

public sealed class RegistrationStationViewModel : ClientModelBase, IStationLifecycle, IShortcutTarget
{
    private readonly ShellViewModel? _shell;
    private string _notice = string.Empty;
    private string _errorMessage = string.Empty;

    public RegistrationStationViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;
        Registration.PropertyChanged += OnRegistrationPropertyChanged;
        Workbench.PropertyChanged += OnWorkbenchPropertyChanged;

        RefreshCommand = new AsyncRelayCommand(
            () => LoadAsync(),
            () => Registration.IsNotBusy);
        SearchPatientsCommand = new AsyncRelayCommand(
            ExecuteSearchPatientsAsync,
            () => Registration.IsNotBusy);
        SelectPatientCommand = new AsyncRelayCommand(
            p => ExecuteSelectPatientAsync(p as RegistrationPatientItem),
            p => p is RegistrationPatientItem && Registration.IsNotBusy);
        SearchSlotsCommand = new AsyncRelayCommand(
            ExecuteSearchSlotsAsync,
            () => Registration.IsNotBusy);
        SelectSlotCommand = new AsyncRelayCommand(
            p => ExecuteSelectSlotAsync(p as RegistrationSlotItem),
            p => p is RegistrationSlotItem item && item.AvailableCount > 0 && Registration.IsNotBusy);
        SettleCommand = new AsyncRelayCommand(
            ExecuteSettleAsync,
            () => Registration.CanSettle);
        SearchRegistrationsCommand = new AsyncRelayCommand(
            ExecuteSearchRegistrationsAsync,
            () => Registration.IsNotBusy);
        SelectRegistrationCommand = new RelayCommand(
            p => ExecuteSelectRegistration(p as RegistrationRecordItem),
            p => p is RegistrationRecordItem);
        RefundCommand = new RelayCommand(
            ConfirmRefund,
            () => Registration.CanRefundSelected);
        OpenFinanceCommand = new RelayCommand(
            () => _shell?.NavigateByKey("finance"));
        OpenScheduleCommand = new RelayCommand(
            () => _shell?.NavigateByKey("schedule"));
    }

    public WorkbenchModel Workbench { get; }
    public RegistrationCashierViewModel Registration { get; } = new();
    public bool IsAdmin => SessionContext.Current.IsAdmin;

    public ICommand RefreshCommand { get; }
    public ICommand SearchPatientsCommand { get; }
    public ICommand SelectPatientCommand { get; }
    public ICommand SearchSlotsCommand { get; }
    public ICommand SelectSlotCommand { get; }
    public ICommand SettleCommand { get; }
    public ICommand SearchRegistrationsCommand { get; }
    public ICommand SelectRegistrationCommand { get; }
    public ICommand RefundCommand { get; }
    public ICommand OpenFinanceCommand { get; }
    public ICommand OpenScheduleCommand { get; }

    public string Notice
    {
        get => _notice;
        set => SetField(ref _notice, value ?? string.Empty);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetField(ref _errorMessage, value ?? string.Empty);
    }

    public bool HasErrorMessage => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasNotice => !string.IsNullOrWhiteSpace(Notice);
    public string PermissionBlockedReason => Workbench.CanOperateFinance
        ? string.Empty
        : "当前岗位无挂号收费权限，请使用顶栏切换为收费员或管理员身份。";
    public bool IsPermissionBlocked => !Workbench.CanOperateFinance;
    public string SettleBlockedReason
    {
        get
        {
            if (!Workbench.CanOperateFinance)
            {
                return PermissionBlockedReason;
            }
            if (Registration.IsBusy)
            {
                return "正在处理上一笔业务，请稍候。";
            }
            if (Registration.SelectedPatient == null)
            {
                return "请先在左侧选择本次挂号患者。";
            }
            if (Registration.SelectedSlot == null)
            {
                return "请选择仍有余额的医生号源。";
            }
            if (Registration.SelectedPaymentChannel == null)
            {
                return "请选择本次挂号收款方式。";
            }
            return string.Empty;
        }
    }

    public bool CanSettle => string.IsNullOrEmpty(SettleBlockedReason);
    public string RefundBlockedReason => Registration.SelectedRegistration == null
        ? "请先在今日挂号记录中选择需要退号的记录。"
        : Registration.SelectedRegistration.CanRefund
            ? string.Empty
            : Registration.SelectedRegistration.RefundHint;
    public string PatientEmptyHint => Registration.IsBusy
        ? "正在检索患者..."
        : "未找到匹配患者，请核对姓名或病历号。";
    public string SlotEmptyHint => Registration.IsBusy
        ? "正在查询号源..."
        : "所选日期没有可挂号源，请调整日期或科室。";
    public string RegistrationEmptyHint => string.IsNullOrWhiteSpace(Registration.RegistrationKeyword)
        ? "今日尚无挂号记录。"
        : "没有匹配当前关键词的挂号记录。";

    public void Activate()
    {
        _ = LoadAsync();
    }

    public void Deactivate()
    {
    }

    public void Dispose()
    {
        Registration.PropertyChanged -= OnRegistrationPropertyChanged;
        Workbench.PropertyChanged -= OnWorkbenchPropertyChanged;
    }

    public bool OnShortcut(string keyGesture)
    {
        switch (keyGesture)
        {
            case "Ctrl+Enter":
                if (SettleCommand.CanExecute(null))
                {
                    SettleCommand.Execute(null);
                    return true;
                }
                return false;
            case "F3":
            case "F5":
                if (SearchSlotsCommand.CanExecute(null))
                {
                    SearchSlotsCommand.Execute(null);
                    return true;
                }
                return false;
            default:
                return false;
        }
    }

    private async System.Threading.Tasks.Task LoadAsync()
    {
        await Registration.EnsureLoadedAsync();
        SyncMessages();
        RaiseComputed();
    }

    private async System.Threading.Tasks.Task ExecuteSearchPatientsAsync()
    {
        await Registration.SearchPatientsAsync();
        SyncMessages();
        RaiseComputed();
    }

    private async System.Threading.Tasks.Task ExecuteSelectPatientAsync(RegistrationPatientItem? patient)
    {
        if (patient == null)
        {
            return;
        }
        await Registration.SelectPatientAsync(patient);
        SyncMessages();
        RaiseComputed();
    }

    private async System.Threading.Tasks.Task ExecuteSearchSlotsAsync()
    {
        await Registration.SearchSlotsAsync();
        SyncMessages();
        RaiseComputed();
    }

    private async System.Threading.Tasks.Task ExecuteSelectSlotAsync(RegistrationSlotItem? slot)
    {
        if (slot == null || slot.AvailableCount <= 0)
        {
            return;
        }
        await Registration.SelectSlotAsync(slot);
        SyncMessages();
        RaiseComputed();
    }

    private async System.Threading.Tasks.Task ExecuteSettleAsync()
    {
        if (!CanSettle)
        {
            _shell?.ShowToast(SettleBlockedReason, "Warning");
            return;
        }

        var result = await Registration.SettleAsync();
        SyncMessages();
        RaiseComputed();
        if (result != null)
        {
            _shell?.ShowToast(result + "，已推送门诊医生站候诊队列。", "Success");
        }
    }

    private async System.Threading.Tasks.Task ExecuteSearchRegistrationsAsync()
    {
        await Registration.LoadRegistrationsAsync();
        SyncMessages();
        RaiseComputed();
    }

    private void ExecuteSelectRegistration(RegistrationRecordItem? record)
    {
        if (record == null)
        {
            return;
        }
        Registration.SelectedRegistration = record;
        RaiseComputed();
    }

    private void ConfirmRefund()
    {
        var record = Registration.SelectedRegistration;
        if (record == null)
        {
            _shell?.ShowToast(RefundBlockedReason, "Warning");
            return;
        }
        if (!record.CanRefund)
        {
            _shell?.ShowToast(record.RefundHint, "Warning");
            return;
        }

        void Execute()
        {
            _ = ExecuteRefundAsync(record);
        }

        if (_shell != null)
        {
            _shell.RequestConfirm(
                "确认办理退号并原路退款？",
                $"患者 {record.PatientName}，{record.DeptName} · {record.DoctorName}，"
                + $"退款 {record.PaidAmountText}。退款后号源将释放，此操作不可撤销。",
                "确认退号退款",
                Execute);
            return;
        }
        Execute();
    }

    private async System.Threading.Tasks.Task ExecuteRefundAsync(RegistrationRecordItem record)
    {
        var result = await Registration.RefundSelectedAsync();
        SyncMessages();
        RaiseComputed();
        if (result != null)
        {
            _shell?.ShowToast($"{record.PatientName} {result}，号源已释放。", "Success");
        }
    }

    private void OnRegistrationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RegistrationCashierViewModel.SelectedPatient)
            or nameof(RegistrationCashierViewModel.SelectedSlot)
            or nameof(RegistrationCashierViewModel.SelectedPaymentChannel)
            or nameof(RegistrationCashierViewModel.SelectedRegistration)
            or nameof(RegistrationCashierViewModel.IsBusy)
            or nameof(RegistrationCashierViewModel.Notice)
            or nameof(RegistrationCashierViewModel.ErrorMessage)
            or nameof(RegistrationCashierViewModel.CanSettle)
            or nameof(RegistrationCashierViewModel.CanRefundSelected))
        {
            SyncMessages();
            RaiseComputed();
        }
    }

    private void OnWorkbenchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkbenchModel.CanOperateFinance)
            or nameof(WorkbenchModel.CurrentRoleDisplayName))
        {
            RaiseComputed();
        }
    }

    private void SyncMessages()
    {
        if (Registration.HasError)
        {
            ErrorMessage = Registration.ErrorMessage;
            Notice = string.Empty;
            return;
        }
        ErrorMessage = string.Empty;
        Notice = Registration.Notice;
    }

    private void RaiseComputed()
    {
        OnPropertyChanged(nameof(HasErrorMessage));
        OnPropertyChanged(nameof(HasNotice));
        OnPropertyChanged(nameof(PermissionBlockedReason));
        OnPropertyChanged(nameof(IsPermissionBlocked));
        OnPropertyChanged(nameof(SettleBlockedReason));
        OnPropertyChanged(nameof(CanSettle));
        OnPropertyChanged(nameof(RefundBlockedReason));
        OnPropertyChanged(nameof(PatientEmptyHint));
        OnPropertyChanged(nameof(SlotEmptyHint));
        OnPropertyChanged(nameof(RegistrationEmptyHint));

        (RefreshCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (SearchPatientsCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (SelectPatientCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (SearchSlotsCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (SelectSlotCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (SettleCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (SearchRegistrationsCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (RefundCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }
}
