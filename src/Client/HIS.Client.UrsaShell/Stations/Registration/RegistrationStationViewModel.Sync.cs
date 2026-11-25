using System.Threading.Tasks;
using System.Windows.Input;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.Registration;

public sealed partial class RegistrationStationViewModel
{
    private ICommand? _reserveSlotCommand;
    private ICommand? _checkinReservedCommand;
    private ICommand? _rechargeAccountCommand;

    public ICommand ReserveSlotCommand => _reserveSlotCommand ??= new AsyncRelayCommand(ExecuteReserveSlotAsync);
    public ICommand CheckinReservedCommand => _checkinReservedCommand ??= new AsyncRelayCommand(p => ExecuteCheckinReservedAsync(p as RegistrationRecordItem));
    public ICommand RechargeAccountCommand => _rechargeAccountCommand ??= new AsyncRelayCommand(ExecuteRechargeAccountAsync);

    private async Task ExecuteReserveSlotAsync()
    {
        var res = await Registration.ReserveSelectedSlotAsync();
        SyncMessages();
        RaiseComputed();
        if (res != null) _shell?.ShowToast(res, "Success");
    }

    private async Task ExecuteCheckinReservedAsync(RegistrationRecordItem? item)
    {
        var res = await Registration.CheckinReservedAsync(item);
        SyncMessages();
        RaiseComputed();
        if (res != null) _shell?.ShowToast(res, "Success");
    }

    private async Task ExecuteRechargeAccountAsync()
    {
        var res = await Registration.RechargeSelectedPatientAsync();
        SyncMessages();
        RaiseComputed();
        if (res != null) _shell?.ShowToast(res, "Success");
    }
}
