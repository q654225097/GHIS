using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Common;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public sealed class OpdPatientAccountVm
{
    public string Id { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string AccountNo { get; set; } = string.Empty;
    public string CardNo { get; set; } = string.Empty;
    public decimal Balance { get; set; }
}

public sealed partial class RegistrationCashierViewModel
{
    private decimal _selectedPatientCardBalance;
    private string _selectedPatientCardNo = "--";
    private string _rechargeAmountInput = "100";
    private RegistrationRecordItem? _selectedReservedAppointment;

    public ObservableCollection<RegistrationRecordItem> ReservedAppointments { get; } = new();

    public decimal SelectedPatientCardBalance
    {
        get => _selectedPatientCardBalance;
        private set
        {
            if (SetField(ref _selectedPatientCardBalance, value))
            {
                OnPropertyChanged(nameof(SelectedPatientCardBalanceText));
            }
        }
    }

    public string SelectedPatientCardNo
    {
        get => _selectedPatientCardNo;
        private set => SetField(ref _selectedPatientCardNo, value ?? "--");
    }

    public string SelectedPatientCardBalanceText => SelectedPatient == null
        ? "未选择患者"
        : $"就诊卡号 {SelectedPatientCardNo} · 余额 ¥{SelectedPatientCardBalance:0.00}";

    public string RechargeAmountInput
    {
        get => _rechargeAmountInput;
        set => SetField(ref _rechargeAmountInput, value ?? "100");
    }

    public RegistrationRecordItem? SelectedReservedAppointment
    {
        get => _selectedReservedAppointment;
        set => SetField(ref _selectedReservedAppointment, value);
    }

    public async Task RefreshSelectedPatientBalanceAsync()
    {
        if (SelectedPatient == null)
        {
            SelectedPatientCardBalance = 0m;
            SelectedPatientCardNo = "--";
            return;
        }
        try
        {
            using var http = CreateHttpClient();
            using var resp = await http.GetAsync($"/api/v4/opd/accounts/{Uri.EscapeDataString(SelectedPatient.Id)}/balance");
            if (!resp.IsSuccessStatusCode) return;
            var env = await ReadEnvelopeAsync<OpdPatientAccountVm>(resp);
            if (env.Code is 200 or 0 && env.Data != null)
            {
                SelectedPatientCardBalance = env.Data.Balance;
                SelectedPatientCardNo = env.Data.CardNo;
            }
        }
        catch { }
    }

    public async Task LoadReservedAppointmentsAsync()
    {
        try
        {
            using var http = CreateHttpClient();
            string query = $"/api/v4/opd/registration/registrations/reserved?date={Uri.EscapeDataString(QueryDate)}";
            using var resp = await http.GetAsync(query);
            if (!resp.IsSuccessStatusCode) return;
            var env = await ReadEnvelopeAsync<List<RegistrationRecordDto>>(resp);
            if (env.Code is 200 or 0 && env.Data != null)
            {
                ReservedAppointments.Clear();
                foreach (var dto in env.Data)
                {
                    ReservedAppointments.Add(ToRecord(dto));
                }
                SelectedReservedAppointment = ReservedAppointments.FirstOrDefault();
            }
        }
        catch { }
    }

    public async Task<string?> RechargeSelectedPatientAsync()
    {
        if (SelectedPatient == null)
        {
            SetValidation("请先选择需要充值的门诊患者。");
            return null;
        }
        if (!decimal.TryParse(RechargeAmountInput.Trim(), out decimal amt) || amt <= 0m)
        {
            SetValidation("请输入有效的充值金额（大于 0 元）。");
            return null;
        }

        IsBusy = true;
        ClearError();
        try
        {
            using var http = CreateHttpClient();
            var channel = SelectedPaymentChannel?.Code ?? "CASH";
            using var resp = await http.PostAsJsonAsync("/api/v4/opd/accounts/recharge", new
            {
                PatientId = SelectedPatient.Id,
                Amount = amt,
                PaymentChannel = channel,
                CardNo = SelectedPatientCardNo == "--" ? null : SelectedPatientCardNo,
                OperatorId = SessionContext.Current.UserId ?? "CASHIER"
            }, JsonOptions);
            var env = await ReadEnvelopeAsync<OpdPatientAccountVm>(resp);
            if (!resp.IsSuccessStatusCode || env.Code is not (200 or 0) || env.Data == null)
            {
                SetFailure(string.IsNullOrWhiteSpace(env.Message) ? "就诊卡充值失败" : env.Message);
                return null;
            }
            SelectedPatientCardBalance = env.Data.Balance;
            SelectedPatientCardNo = env.Data.CardNo;
            Notice = $"✓ 就诊卡充值 ¥{amt:0.00} 成功，当前余额 ¥{env.Data.Balance:0.00}";
            return Notice;
        }
        catch (Exception ex)
        {
            SetException("就诊卡充值失败", ex);
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<string?> ReserveSelectedSlotAsync()
    {
        if (IsBusy) return null;
        if (SelectedPatient == null || SelectedSlot == null)
        {
            SetValidation("请先选择患者和预约号源。");
            return null;
        }

        IsBusy = true;
        ClearError();
        try
        {
            using var http = CreateHttpClient();
            using var resp = await http.PostAsJsonAsync("/api/v4/opd/registration/registrations/reserve", new
            {
                PatientId = SelectedPatient.Id,
                ScheduleId = SelectedSlot.Id,
                RequestKey = "OPD-RES-" + Guid.NewGuid().ToString("N")
            }, JsonOptions);
            var env = await ReadEnvelopeAsync<RegistrationRecordDto>(resp);
            if (!resp.IsSuccessStatusCode || env.Code is not (200 or 0) || env.Data == null)
            {
                SetFailure(string.IsNullOrWhiteSpace(env.Message) ? "预约锁号失败" : env.Message);
                return null;
            }
            await LoadSlotsCoreAsync();
            await LoadReservedAppointmentsAsync();
            Notice = env.Message ?? $"预约锁号成功：{env.Data.AppointmentNo}";
            return Notice;
        }
        catch (Exception ex)
        {
            SetException("预约锁号请求失败", ex);
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<string?> CheckinReservedAsync(RegistrationRecordItem? target)
    {
        var item = target ?? SelectedReservedAppointment;
        if (item == null)
        {
            SetValidation("请先在待签到预约列表中选择记录。");
            return null;
        }

        IsBusy = true;
        ClearError();
        try
        {
            using var http = CreateHttpClient();
            string channel = SelectedPaymentChannel?.Code ?? "CASH";
            using var resp = await http.PostAsJsonAsync(
                $"/api/v4/opd/registration/registrations/{Uri.EscapeDataString(item.Id)}/checkin",
                new { PaymentChannel = channel, RequestKey = "OPD-CHK-" + Guid.NewGuid().ToString("N") },
                JsonOptions);
            var env = await ReadEnvelopeAsync<RegistrationRecordDto>(resp);
            if (!resp.IsSuccessStatusCode || env.Code is not (200 or 0) || env.Data == null)
            {
                SetFailure(string.IsNullOrWhiteSpace(env.Message) ? "到院取号签到失败" : env.Message);
                return null;
            }
            await LoadReservedAppointmentsAsync();
            await LoadRegistrationsCoreAsync(env.Data.Id);
            Notice = env.Message ?? $"到院签到取号成功，排队号 {env.Data.QueueNo:D3}";
            return Notice;
        }
        catch (Exception ex)
        {
            SetException("到院取号请求失败", ex);
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
