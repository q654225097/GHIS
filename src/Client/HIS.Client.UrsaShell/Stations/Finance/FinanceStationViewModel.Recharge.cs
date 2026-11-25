using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.BuildingBlocks.Common;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Services;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.Finance;

public sealed partial class FinanceStationViewModel
{
    private static readonly JsonSerializerOptions RechargeJsonOpts = new() { PropertyNameCaseInsensitive = true };

    private string _cardPatientId = "PAT85061801";
    private string _cardRechargeAmount = "200.00";
    private string _cardPaymentChannel = "CASH";
    private string _cardBalanceSummary = "输入患者 ID 后点击查询余额，支持门急诊诊间/药房预交金充值";

    public string CardPatientId { get => _cardPatientId; set => SetField(ref _cardPatientId, value ?? string.Empty); }
    public string CardRechargeAmount { get => _cardRechargeAmount; set => SetField(ref _cardRechargeAmount, value ?? "0"); }
    public string CardPaymentChannel { get => _cardPaymentChannel; set => SetField(ref _cardPaymentChannel, value ?? "CASH"); }
    public string CardBalanceSummary { get => _cardBalanceSummary; set => SetField(ref _cardBalanceSummary, value ?? string.Empty); }

    public IReadOnlyList<string> RechargeChannels { get; } = new[] { "CASH", "WECHAT", "ALIPAY", "BANK_CARD" };

    private ICommand? _queryCardBalanceCommand;
    private ICommand? _rechargeCardCommand;

    public ICommand QueryCardBalanceCommand => _queryCardBalanceCommand ??= new AsyncRelayCommand(QueryCardBalanceAsync);
    public ICommand RechargeCardCommand => _rechargeCardCommand ??= new AsyncRelayCommand(RechargeCardAsync);

    private async Task QueryCardBalanceAsync()
    {
        if (string.IsNullOrWhiteSpace(CardPatientId)) { _shell?.ShowToast("请输入患者ID", "Warning"); return; }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            using var resp = await http.GetAsync($"/api/v4/opd/accounts/{Uri.EscapeDataString(CardPatientId.Trim())}/balance");
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<OpdPatientAccountVm>>(RechargeJsonOpts);
            if (!resp.IsSuccessStatusCode || env?.Code is not (200 or 0) || env.Data == null)
            {
                _shell?.ShowToast(env?.Message ?? "查询余额失败", "Error");
                return;
            }
            CardBalanceSummary = $"账户 {env.Data.AccountNo} · 卡号 {env.Data.CardNo} · 当前可用余额：¥{env.Data.Balance:0.00}";
            _shell?.ShowToast(CardBalanceSummary, "Info");
        }
        catch (Exception ex)
        {
            _shell?.ShowToast("查询失败：" + ex.Message, "Error");
        }
    }

    private async Task RechargeCardAsync()
    {
        if (!Workbench.CanOperateFinance) { _shell?.ShowToast(FinanceBlockedReason, "Warning"); return; }
        if (string.IsNullOrWhiteSpace(CardPatientId) || !decimal.TryParse(CardRechargeAmount.Trim(), out decimal amount) || amount <= 0m)
        {
            _shell?.ShowToast("请输入有效的患者ID和充值金额", "Warning");
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            using var resp = await http.PostAsJsonAsync("/api/v4/opd/accounts/recharge", new
            {
                PatientId = CardPatientId.Trim(),
                Amount = amount,
                PaymentChannel = CardPaymentChannel,
                OperatorId = Workbench.CurrentOperatorId
            }, RechargeJsonOpts);
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<OpdPatientAccountVm>>(RechargeJsonOpts);
            if (!resp.IsSuccessStatusCode || env?.Code is not (200 or 0) || env.Data == null)
            {
                _shell?.ShowToast(env?.Message ?? "就诊卡充值失败", "Error");
                return;
            }
            CardBalanceSummary = $"✓ 已充值 ¥{amount:0.00} · 卡号 {env.Data.CardNo} · 最新余额：¥{env.Data.Balance:0.00}";
            _shell?.ShowToast(CardBalanceSummary, "Success");
        }
        catch (Exception ex)
        {
            _shell?.ShowToast("充值异常：" + ex.Message, "Error");
        }
    }
}
