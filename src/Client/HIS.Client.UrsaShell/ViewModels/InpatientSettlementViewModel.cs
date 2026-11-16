using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.Services;

namespace HIS.Client.UrsaShell.ViewModels;

public sealed class InpatientSettlementViewModel : ClientModelBase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private string _keyword = string.Empty;
    private string _statusFilter = "PENDING";
    private IpdDischargeQueueItem? _selectedRequest;
    private IpdSettlementPreviewDto? _preview;
    private string _insuranceAmountText = "0.00";
    private string _prepaymentDeductText = "0.00";
    private string _selfPayAmountText = "0.00";
    private string _refundAmountText = "0.00";
    private SettlementPaymentChannel? _selectedPaymentChannel;
    private bool _isBusy;
    private string _notice = "选择待结算出院申请，核对费用和六项临床核查后进行结算。";
    private string _errorMessage = string.Empty;
    private string _loadedRequestId = string.Empty;
    private bool _isLoaded;

    public InpatientSettlementViewModel()
    {
        PaymentChannels.Add(new("CASH", "现金"));
        PaymentChannels.Add(new("BANK_CARD", "银行卡"));
        PaymentChannels.Add(new("WECHAT", "微信"));
        PaymentChannels.Add(new("ALIPAY", "支付宝"));
        PaymentChannels.Add(new("INSURANCE", "医保"));
        _selectedPaymentChannel = PaymentChannels[0];
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        SearchCommand = new AsyncRelayCommand(SearchAsync);
        RefreshPreviewCommand = new AsyncRelayCommand(
            async () =>
            {
                if (SelectedRequest != null)
                {
                    await LoadPreviewAsync(SelectedRequest);
                }
            },
            () => SelectedRequest != null);
        SettleCommand = new AsyncRelayCommand(SettleAsync, () => CanSettle);
    }

    public ICommand RefreshCommand { get; }
    public ICommand SearchCommand { get; }
    public AsyncRelayCommand RefreshPreviewCommand { get; }
    public AsyncRelayCommand SettleCommand { get; }

    public ObservableCollection<IpdDischargeQueueItem> Requests { get; } = new();
    public ObservableCollection<IpdSettlementChargeItem> Charges { get; } = new();
    public ObservableCollection<SettlementPaymentChannel> PaymentChannels { get; } = new();
    public ObservableCollection<string> ClinicalBlockers { get; } = new();

    public string Keyword
    {
        get => _keyword;
        set => SetField(ref _keyword, value ?? string.Empty);
    }

    public string StatusFilter
    {
        get => _statusFilter;
        set => SetField(ref _statusFilter, value ?? string.Empty);
    }

    public IReadOnlyList<SettlementStatusFilter> StatusFilters { get; } = new[]
    {
        new SettlementStatusFilter("PENDING", "待结算"),
        new SettlementStatusFilter("SETTLED", "已结算"),
        new SettlementStatusFilter("CANCELLED", "已撤回"),
        new SettlementStatusFilter("", "全部申请")
    };

    public SettlementStatusFilter? SelectedStatusFilter
    {
        get => StatusFilters.FirstOrDefault(x => x.Code == StatusFilter) ?? StatusFilters[0];
        set
        {
            if (value != null)
            {
                StatusFilter = value.Code;
                OnPropertyChanged();
            }
        }
    }

    public IpdDischargeQueueItem? SelectedRequest
    {
        get => _selectedRequest;
        set
        {
            if (!SetField(ref _selectedRequest, value))
            {
                return;
            }
            foreach (var row in Requests)
            {
                row.IsSelected = ReferenceEquals(row, value);
            }
            NotifySelection();
            if (value != null && !string.Equals(_loadedRequestId, value.RequestId, StringComparison.Ordinal))
            {
                _loadedRequestId = value.RequestId;
                _ = LoadPreviewAsync(value);
            }
        }
    }

    public IpdSettlementPreviewDto? Preview
    {
        get => _preview;
        private set
        {
            if (SetField(ref _preview, value))
            {
                NotifyPreview();
            }
        }
    }

    public SettlementPaymentChannel? SelectedPaymentChannel
    {
        get => _selectedPaymentChannel;
        set
        {
            if (SetField(ref _selectedPaymentChannel, value))
            {
                NotifyPreview();
            }
        }
    }

    public string InsuranceAmountText
    {
        get => _insuranceAmountText;
        set
        {
            if (SetField(ref _insuranceAmountText, value ?? string.Empty))
            {
                RecalculateSelfPay();
            }
        }
    }

    public string PrepaymentDeductText
    {
        get => _prepaymentDeductText;
        set
        {
            if (SetField(ref _prepaymentDeductText, value ?? string.Empty))
            {
                RecalculateSelfPay();
            }
        }
    }

    public string SelfPayAmountText
    {
        get => _selfPayAmountText;
        private set => SetField(ref _selfPayAmountText, value ?? string.Empty);
    }

    public string RefundAmountText
    {
        get => _refundAmountText;
        private set => SetField(ref _refundAmountText, value ?? string.Empty);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsNotBusy));
                OnPropertyChanged(nameof(CanSettle));
                SettleCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsNotBusy => !IsBusy;

    public string Notice
    {
        get => _notice;
        private set => SetField(ref _notice, value ?? string.Empty);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasSelectedRequest => SelectedRequest != null;
    public bool HasPreview => Preview != null;
    public bool HasCharges => Charges.Count > 0;
    public bool HasClinicalBlockers => ClinicalBlockers.Count > 0;
    public bool CanSettle => _isLoaded
        && IsNotBusy
        && Preview != null
        && Preview.Status == "PENDING"
        && ClinicalBlockers.Count == 0
        && SelectedPaymentChannel != null;
    public string QueueCountText => $"{Requests.Count} 条申请";
    public string ChargeCountText => $"{Charges.Count} 个费用项目";
    public string PatientTitle => Preview == null
        ? "尚未选择患者"
        : $"{Preview.PatientName} · {Preview.MedicalRecordNo}";
    public string EncounterLine => Preview == null
        ? "选择出院申请后加载住院信息"
        : $"住院号 {Preview.AdmissionNo} · {Preview.DeptName} · {Preview.WardName} · {Preview.BedCode}";
    public string ChargeTotalText => "¥" + (Preview?.ChargeTotal ?? 0m).ToString("N2");
    public string PrepaymentBalanceText => "¥" + (Preview?.PrepaymentBalance ?? 0m).ToString("N2");
    public string StatusText => Preview?.Status switch
    {
        "PENDING" => "待结算",
        "SETTLED" => "已结算",
        "CANCELLED" => "已撤回",
        _ => "待处理"
    };
    public bool IsSettled => Preview?.Status == "SETTLED";

    public async Task LoadAsync(string? keyword = null, string? status = null)
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        ClearError();
        try
        {
            Keyword = keyword ?? Keyword;
            StatusFilter = status ?? StatusFilter;
            using var http = CreateHttpClient();
            string url = "/api/v4/ipd/discharge-requests?status=" + Uri.EscapeDataString(StatusFilter)
                + "&keyword=" + Uri.EscapeDataString(Keyword.Trim());
            using var response = await http.GetAsync(url);
            var envelope = await ReadEnvelopeAsync<List<IpdDischargeRequestDto>>(response);
            if (!response.IsSuccessStatusCode || envelope.Code != 200)
            {
                SetFailure(string.IsNullOrWhiteSpace(envelope.Message) ? "出院结算队列加载失败。" : envelope.Message);
                return;
            }

            string? keepId = SelectedRequest?.RequestId;
            Requests.Clear();
            foreach (var dto in envelope.Data ?? new List<IpdDischargeRequestDto>())
            {
                Requests.Add(new IpdDischargeQueueItem(dto));
            }
            OnPropertyChanged(nameof(QueueCountText));
            SelectedRequest = Requests.FirstOrDefault(x => x.RequestId == keepId) ?? Requests.FirstOrDefault();
            if (SelectedRequest == null)
            {
                Preview = null;
                Charges.Clear();
                ClinicalBlockers.Clear();
                _loadedRequestId = string.Empty;
                Notice = "当前筛选条件下没有出院结算申请。";
                NotifyPreview();
            }
            _isLoaded = true;
            Notice = Requests.Count == 0 ? "当前没有待结算出院申请。" : "已加载出院结算队列，选择申请核对金额。";
        }
        catch (Exception ex)
        {
            SetException("出院结算队列加载失败", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task SearchAsync() => LoadAsync(Keyword, StatusFilter);

    public async Task LoadPreviewAsync(IpdDischargeQueueItem item)
    {
        if (item == null || IsBusy)
        {
            return;
        }
        IsBusy = true;
        ClearError();
        try
        {
            using var http = CreateHttpClient();
            using var response = await http.GetAsync(
                "/api/v4/ipd/encounters/" + Uri.EscapeDataString(item.EncounterId) + "/settlement-preview");
            var envelope = await ReadEnvelopeAsync<IpdSettlementPreviewDto>(response);
            if (!response.IsSuccessStatusCode || envelope.Code != 200 || envelope.Data == null)
            {
                SetFailure(string.IsNullOrWhiteSpace(envelope.Message) ? "住院费用预览加载失败。" : envelope.Message);
                return;
            }

            Preview = envelope.Data;
            Charges.Clear();
            foreach (var charge in Preview.ChargeItems ?? new List<IpdSettlementChargeDto>())
            {
                Charges.Add(new IpdSettlementChargeItem(charge));
            }
            ClinicalBlockers.Clear();
            foreach (string blocker in Preview.ClinicalBlockers ?? new List<string>())
            {
                ClinicalBlockers.Add(blocker);
            }

            InsuranceAmountText = "0.00";
            PrepaymentDeductText = Preview.PrepaymentBalance > 0m && Preview.PrepaymentBalance < Preview.ChargeTotal
                ? Preview.PrepaymentBalance.ToString("F2")
                : Preview.PrepaymentBalance >= Preview.ChargeTotal ? Preview.ChargeTotal.ToString("F2") : "0.00";
            RecalculateSelfPay();
            Notice = ClinicalBlockers.Count == 0
                ? "六项核查已通过，可继续核对结算金额。"
                : "六项核查存在阻断项，收费处不能直接结算。";
        }
        catch (Exception ex)
        {
            SetException("住院费用预览加载失败", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RefreshAsync()
    {
        string? keepId = SelectedRequest?.RequestId;
        await LoadAsync(Keyword, StatusFilter);
        if (keepId != null)
        {
            SelectedRequest = Requests.FirstOrDefault(x => x.RequestId == keepId) ?? SelectedRequest;
        }
    }

    public async Task SettleAsync()
    {
        if (IsBusy)
        {
            return;
        }
        if (SelectedRequest == null || Preview == null)
        {
            SetValidation("请先选择待结算患者并加载费用预览。");
            return;
        }
        if (ClinicalBlockers.Count > 0)
        {
            SetValidation("六项临床核查未通过，不能办理财务结算。");
            return;
        }
        if (SelectedPaymentChannel == null)
        {
            SetValidation("请选择支付方式。");
            return;
        }
        if (!TryParseMoney(InsuranceAmountText, out decimal insurance)
            || !TryParseMoney(PrepaymentDeductText, out decimal prepaymentDeduct))
        {
            SetValidation("医保统筹和预交金抵扣必须是有效金额。");
            return;
        }
        if (!TryParseMoney(SelfPayAmountText, out decimal selfPay)
            || !TryParseMoney(RefundAmountText, out decimal refund))
        {
            SetValidation("结算金额计算失败，请检查输入。");
            return;
        }

        IsBusy = true;
        ClearError();
        try
        {
            using var http = CreateHttpClient();
            using var response = await http.PostAsJsonAsync(
                "/api/v4/ipd/discharge-requests/" + Uri.EscapeDataString(SelectedRequest.RequestId) + "/settle",
                new
                {
                    TotalAmount = Preview.ChargeTotal,
                    InsuranceAmount = insurance,
                    PrepaymentDeduct = prepaymentDeduct,
                    SelfPayAmount = selfPay,
                    RefundAmount = refund,
                    PaymentChannel = SelectedPaymentChannel.Code,
                    RequestKey = "IPD-DISCHARGE-" + SelectedRequest.RequestId,
                    ChargeItems = Charges.Select(x => new
                    {
                        ItemCode = x.ItemCode,
                        ItemName = x.ItemName,
                        Quantity = x.Quantity,
                        UnitPrice = x.UnitPrice,
                        TotalAmount = x.TotalAmount
                    }).ToList()
                }, JsonOptions);
            var envelope = await ReadEnvelopeAsync<IpdDischargeRequestDto>(response);
            if (!response.IsSuccessStatusCode || envelope.Code != 200)
            {
                SetFailure(string.IsNullOrWhiteSpace(envelope.Message) ? "出院结算失败。" : envelope.Message);
                if (envelope.Data != null)
                {
                    Preview = Preview with { Status = envelope.Data.Status };
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(IsSettled));
                }
                return;
            }

            Notice = string.IsNullOrWhiteSpace(envelope.Message)
                ? "出院结算成功，费用已结清，床位已释放。"
                : envelope.Message;
            OnPropertyChanged(nameof(CanSettle));
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            SetException("出院结算失败", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RecalculateSelfPay()
    {
        if (!TryParseMoney(InsuranceAmountText, out decimal insurance)
            || !TryParseMoney(PrepaymentDeductText, out decimal prepaymentDeduct))
        {
            SelfPayAmountText = "0.00";
            RefundAmountText = "0.00";
            NotifyPreview();
            return;
        }
        decimal total = Preview?.ChargeTotal ?? 0m;
        decimal balance = Preview?.PrepaymentBalance ?? 0m;
        decimal selfPay = total - insurance - prepaymentDeduct;
        decimal refund = balance - prepaymentDeduct;
        SelfPayAmountText = selfPay.ToString("F2");
        RefundAmountText = refund.ToString("F2");
        NotifyPreview();
    }

    private static bool TryParseMoney(string? text, out decimal value) =>
        decimal.TryParse((text ?? string.Empty).Trim(), System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out value);

    private void NotifySelection()
    {
        OnPropertyChanged(nameof(HasSelectedRequest));
        OnPropertyChanged(nameof(CanSettle));
        RefreshPreviewCommand.RaiseCanExecuteChanged();
        SettleCommand.RaiseCanExecuteChanged();
    }

    private void NotifyPreview()
    {
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(HasCharges));
        OnPropertyChanged(nameof(HasClinicalBlockers));
        OnPropertyChanged(nameof(CanSettle));
        OnPropertyChanged(nameof(ChargeCountText));
        OnPropertyChanged(nameof(PatientTitle));
        OnPropertyChanged(nameof(EncounterLine));
        OnPropertyChanged(nameof(ChargeTotalText));
        OnPropertyChanged(nameof(PrepaymentBalanceText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsSettled));
        SettleCommand.RaiseCanExecuteChanged();
    }

    private void SetValidation(string message)
    {
        Notice = message;
        ErrorMessage = message;
    }

    private void SetFailure(string message)
    {
        Notice = message;
        ErrorMessage = message;
        ClientDiagnosticsLog.Warn("ipd.settlement", message);
    }

    private void SetException(string context, Exception ex)
    {
        Notice = context + "，请检查服务连接。";
        ErrorMessage = context + "：" + ex.Message;
        ClientDiagnosticsLog.Error("ipd.settlement", context, ex);
    }

    private void ClearError() => ErrorMessage = string.Empty;

    private static HttpClient CreateHttpClient()
    {
        return AuthenticatedHttpClientFactory.Create();
    }

    private static async Task<ClientApiEnvelope<T>> ReadEnvelopeAsync<T>(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ClientApiEnvelope<T>>(JsonOptions)
                ?? new ClientApiEnvelope<T>();
        }
        catch (JsonException)
        {
            return new ClientApiEnvelope<T>();
        }
    }

    public sealed class IpdDischargeRequestDto
    {
        public string Id { get; set; } = string.Empty;
        public string RequestNo { get; set; } = string.Empty;
        public string EncounterId { get; set; } = string.Empty;
        public string PatientId { get; set; } = string.Empty;
        public string PatientName { get; set; } = string.Empty;
        public string MedicalRecordNo { get; set; } = string.Empty;
        public string AdmissionNo { get; set; } = string.Empty;
        public string DeptName { get; set; } = string.Empty;
        public string WardName { get; set; } = string.Empty;
        public string BedCode { get; set; } = string.Empty;
        public string? DoctorName { get; set; }
        public string Status { get; set; } = string.Empty;
        public long RequestedAtMs { get; set; }
        public long? SettledAtMs { get; set; }
        public decimal SettlementTotalAmount { get; set; }
        public string? PaymentNo { get; set; }
        public List<string> ClinicalBlockers { get; set; } = new();
    }

    public sealed class IpdSettlementChargeDto
    {
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalAmount { get; set; }
    }

    public sealed record IpdSettlementPreviewDto(
        string RequestId,
        string EncounterId,
        string PatientId,
        string PatientName,
        string MedicalRecordNo,
        string AdmissionNo,
        string DeptName,
        string WardName,
        string BedCode,
        string Status,
        decimal PrepaymentBalance,
        decimal ChargeTotal,
        List<IpdSettlementChargeDto> ChargeItems,
        List<string> ClinicalBlockers);

    public sealed record SettlementPaymentChannel(string Code, string Name)
    {
        public override string ToString() => Name;
    }

    public sealed record SettlementStatusFilter(string Code, string Name);

    public sealed class IpdDischargeQueueItem : ClientModelBase
    {
        private bool _isSelected;
        public IpdDischargeQueueItem(IpdDischargeRequestDto dto)
        {
            RequestId = dto.Id;
            RequestNo = dto.RequestNo;
            EncounterId = dto.EncounterId;
            PatientName = string.IsNullOrWhiteSpace(dto.PatientName) ? dto.PatientId : dto.PatientName;
            MedicalRecordNo = dto.MedicalRecordNo;
            AdmissionNo = dto.AdmissionNo;
            DeptName = dto.DeptName;
            WardName = dto.WardName;
            BedCode = dto.BedCode;
            DoctorName = dto.DoctorName ?? string.Empty;
            Status = dto.Status;
            RequestedAtMs = dto.RequestedAtMs;
            SettlementTotalAmount = dto.SettlementTotalAmount;
            PaymentNo = dto.PaymentNo ?? string.Empty;
            BlockerCount = dto.ClinicalBlockers?.Count ?? 0;
        }
        public string RequestId { get; }
        public string RequestNo { get; }
        public string EncounterId { get; }
        public string PatientName { get; }
        public string MedicalRecordNo { get; }
        public string AdmissionNo { get; }
        public string DeptName { get; }
        public string WardName { get; }
        public string BedCode { get; }
        public string DoctorName { get; }
        public string Status { get; }
        public long RequestedAtMs { get; }
        public decimal SettlementTotalAmount { get; }
        public string PaymentNo { get; }
        public int BlockerCount { get; }
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetField(ref _isSelected, value))
                {
                    OnPropertyChanged(nameof(CardBackground));
                }
            }
        }
        public IBrush CardBackground => IsSelected ? ThemeBrushes.PrimaryLight : Brushes.Transparent;
        public string PatientLine => PatientName + " · " + MedicalRecordNo;
        public string AdmissionLine => AdmissionNo + " · " + DeptName + " · " + BedCode;
        public string RequestedText => RequestedAtMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(RequestedAtMs).ToOffset(TimeSpan.FromHours(8)).ToString("MM-dd HH:mm")
            : "--";
        public string StatusLabel => Status switch
        {
            "PENDING" => "待财务结算",
            "SETTLED" => "已结算",
            "CANCELLED" => "已撤回",
            _ => Status
        };
        public string BlockerText => BlockerCount == 0 ? "临床核查通过" : $"临床阻断 {BlockerCount} 项";
    }

    public sealed class IpdSettlementChargeItem
    {
        public IpdSettlementChargeItem(IpdSettlementChargeDto dto)
        {
            ItemCode = dto.ItemCode;
            ItemName = dto.ItemName;
            Quantity = dto.Quantity;
            UnitPrice = dto.UnitPrice;
            TotalAmount = dto.TotalAmount;
        }
        public string ItemCode { get; }
        public string ItemName { get; }
        public decimal Quantity { get; }
        public decimal UnitPrice { get; }
        public decimal TotalAmount { get; }
        public string QuantityText => Quantity.ToString("0.##");
        public string UnitPriceText => "¥" + UnitPrice.ToString("N2");
        public string TotalText => "¥" + TotalAmount.ToString("N2");
    }
}
