using System.ComponentModel;
using System.Windows.Input;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.Finance;

public sealed class FinanceStationViewModel : ClientModelBase, IShortcutTarget
{
    private readonly ShellViewModel? _shell;
    private bool _subscribed;

    public FinanceStationViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        CashierSettleCommand = new RelayCommand(() => ExecuteSettle(null), () => Workbench.CanOperateFinance);
        FinancialRefundCommand = new RelayCommand(() => ConfirmAndRefund(null), () => Workbench.CanOperateFinance);
        DailyClosingCommand = new RelayCommand(ConfirmAndDailyClosing, () => Workbench.CanOperateFinance);
        SettleRowCommand = new RelayCommand(p => ExecuteSettle(p as PharmacyDispenseModel), p => Workbench.CanOperateFinance && p is PharmacyDispenseModel m && m.NeedsCashierSettlement);
        RefundRowCommand = new RelayCommand(p => ConfirmAndRefund(p as PharmacyDispenseModel), p => Workbench.CanOperateFinance && p is PharmacyDispenseModel m && m.CanExecuteFinancialRefund);
    }

    public WorkbenchModel Workbench { get; }

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

    private void ExecuteSettle(PharmacyDispenseModel? item)
    {
        Workbench.CashierSettleAndNotifyPharmacy(item);
        _shell?.ShowToast(Workbench.SystemNotice, "Success");
    }

    private void ConfirmAndRefund(PharmacyDispenseModel? item)
    {
        void Execute()
        {
            Workbench.ExecuteFinancialRefundForOrder(item);
            _shell?.ShowToast(Workbench.SystemNotice, "Success");
        }

        if (_shell != null)
        {
            var targetDesc = item != null
                ? $"处方 {item.PrescriptionNo}（患者 {item.PatientName}，退款金额 ¥{item.TotalAmount:0.00}）"
                : "当前待退费冲红订单";
            _shell.RequestConfirm(
                "确认执行原路退费冲红？",
                $"将对 {targetDesc} 办理原路退款并记录红字退费凭证，操作后不可撤销。",
                "确认退费冲红",
                Execute);
            return;
        }

        Execute();
    }

    private void ConfirmAndDailyClosing()
    {
        void Execute()
        {
            Workbench.GenerateCashierDailyClosing();
            _shell?.ShowToast(Workbench.SystemNotice, "Success");
        }

        if (_shell != null)
        {
            _shell.RequestConfirm(
                "确认生成收费员日结轧账单？",
                "系统将汇总今日门急诊收费、退费冲红与住院结算流水，生成日结凭证。",
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
        (CashierSettleCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (FinancialRefundCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DailyClosingCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }
}
