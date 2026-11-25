using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.Finance;

public sealed partial class FinanceStationViewModel : ClientModelBase, IShortcutTarget
{
    private readonly ShellViewModel? _shell;
    private bool _subscribed;
    private PharmacyDispenseModel? _selectedRecord;

    public FinanceStationViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        CashierSettleCommand = new AsyncRelayCommand(async () => await ExecuteSettleAsync(null), () => Workbench.CanOperateFinance);
        FinancialRefundCommand = new RelayCommand(() => ConfirmAndRefund(null), () => Workbench.CanOperateFinance);
        DailyClosingCommand = new RelayCommand(ConfirmAndDailyClosing, () => Workbench.CanOperateFinance);
        SettleRowCommand = new AsyncRelayCommand(async p => await ExecuteSettleAsync(p as PharmacyDispenseModel), p => Workbench.CanOperateFinance && p is PharmacyDispenseModel m && m.NeedsCashierSettlement);
        RefundRowCommand = new RelayCommand(p => ConfirmAndRefund(p as PharmacyDispenseModel), p => Workbench.CanOperateFinance && p is PharmacyDispenseModel m && m.CanExecuteFinancialRefund);
    }

    public WorkbenchModel Workbench { get; }

    public PharmacyDispenseModel? SelectedRecord
    {
        get => _selectedRecord;
        set => SetField(ref _selectedRecord, value);
    }

    public string FinanceBlockedReason =>
        Workbench.CanOperateFinance ? string.Empty : "当前身份无收费权限，请切换为收费员或管理员身份后操作。";

    public ICommand CashierSettleCommand { get; }
    public ICommand FinancialRefundCommand { get; }
    public ICommand DailyClosingCommand { get; }
    public ICommand SettleRowCommand { get; }
    public ICommand RefundRowCommand { get; }

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
        if (keyGesture == "Ctrl+Enter" && Workbench.SelectedFinanceTabIndex == 0 && CashierSettleCommand.CanExecute(null))
        {
            CashierSettleCommand.Execute(null);
            return true;
        }
        return false;
    }

    private async Task ExecuteSettleAsync(PharmacyDispenseModel? item)
    {
        var target = item ?? SelectedRecord;
        if (target == null)
        {
            _shell?.ShowToast("请先在列表中选中待收费的处方订单，或点击处方条目右侧的【收费】按钮", "Warning");
            return;
        }
        SelectedRecord = target;
        bool ok = await Workbench.CashierSettleAndNotifyPharmacyAsync(target);
        _shell?.ShowToast(Workbench.SystemNotice, ok ? "Success" : "Error");
    }

    private void ConfirmAndRefund(PharmacyDispenseModel? item)
    {
        var target = item ?? SelectedRecord;
        if (target == null)
        {
            _shell?.ShowToast("请先在列表中选中待退费冲红的订单，或点击订单条目右侧的【退费】按钮", "Warning");
            return;
        }
        SelectedRecord = target;

        async void Execute()
        {
            bool ok = await Workbench.ExecuteFinancialRefundForOrderAsync(target);
            _shell?.ShowToast(Workbench.SystemNotice, ok ? "Success" : "Error");
        }

        if (_shell != null)
        {
            var targetDesc = $"处方 {target.PrescriptionNo}（患者 {target.PatientName}，退款金额 ¥{target.TotalAmount:0.00}）";
            _shell.RequestConfirm(
                "确认执行原路退费冲红？",
                $"将对 {targetDesc} 办理原路退款并向服务端记录红字退费凭证，操作后不可撤销。",
                "确认退费冲红",
                Execute);
            return;
        }

        Execute();
    }

    private void ConfirmAndDailyClosing()
    {
        async void Execute()
        {
            bool ok = await Workbench.GenerateCashierDailyClosingAsync();
            _shell?.ShowToast(Workbench.SystemNotice, ok ? "Success" : "Error");
        }

        if (_shell != null)
        {
            _shell.RequestConfirm(
                "确认生成收费员日结轧账单？",
                "系统将向服务端提交并汇总今日门急诊收费、退费冲红与住院结算流水，锁定账实一致日结凭证。",
                "确认日结",
                Execute);
            return;
        }

        Execute();
    }

    private void OnWorkbenchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkbenchModel.CanOperateFinance)
            or nameof(WorkbenchModel.UnpaidPrescriptionCount)
            or nameof(WorkbenchModel.PendingFinancialRefundCount))
        {
            RaiseComputed();
        }
    }

    private void RaiseComputed()
    {
        OnPropertyChanged(nameof(FinanceBlockedReason));
        (CashierSettleCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (FinancialRefundCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DailyClosingCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SettleRowCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (RefundRowCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }
}
