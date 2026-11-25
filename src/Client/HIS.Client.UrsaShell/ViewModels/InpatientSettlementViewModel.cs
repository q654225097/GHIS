using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.Services;

namespace HIS.Client.UrsaShell.ViewModels;

public sealed partial class InpatientSettlementViewModel : ClientModelBase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly Func<HttpClient> _createHttpClient;

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
    private long _previewRevision;
    private bool _isLoadingPreview;
    private bool _isLoaded;
    private string _confirmationText = string.Empty;

    public InpatientSettlementViewModel() : this(() => AuthenticatedHttpClientFactory.Create()) { }

    internal InpatientSettlementViewModel(Func<HttpClient> createHttpClient)
    {
        _createHttpClient = createHttpClient;
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
            () => SelectedRequest != null && IsNotBusy);
        SettleCommand = new RelayCommand(OpenConfirmation, () => CanSettle);
        ConfirmSettlementCommand = new AsyncRelayCommand(async () =>
        {
            ConfirmationText = string.Empty;
            await SettleAsync();
        }, () => IsConfirmationOpen && CanSettle);
        CancelSettlementCommand = new RelayCommand(() => ConfirmationText = string.Empty);
    }

    public ICommand RefreshCommand { get; }
    public ICommand SearchCommand { get; }
    public AsyncRelayCommand RefreshPreviewCommand { get; }
    public RelayCommand SettleCommand { get; }
    public AsyncRelayCommand ConfirmSettlementCommand { get; }
    public ICommand CancelSettlementCommand { get; }

    public string ConfirmationText
    {
        get => _confirmationText;
        private set
        {
            if (SetField(ref _confirmationText, value))
            {
                OnPropertyChanged(nameof(IsConfirmationOpen));
                OnPropertyChanged(nameof(IsConfirmationClosed));
                ConfirmSettlementCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsConfirmationOpen => ConfirmationText.Length > 0;
    public bool IsConfirmationClosed => !IsConfirmationOpen;

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
            if (value != null)
            {
                _ = LoadPreviewAsync(value);
            }
            else
            {
                ResetPreview();
                Notice = "请选择出院结算申请。";
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
                ConfirmationText = string.Empty;
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
                ConfirmationText = string.Empty;
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
                ConfirmationText = string.Empty;
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
                RefreshPreviewCommand.RaiseCanExecuteChanged();
                SettleCommand.RaiseCanExecuteChanged();
                ConfirmSettlementCommand.RaiseCanExecuteChanged();
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
        && !_isLoadingPreview
        && Preview is { Status: "PENDING" } preview
        && !string.IsNullOrWhiteSpace(preview.PreviewVersion)
        && SelectedRequest is { Status: "PENDING" } request
        && MatchesRequest(request, preview.RequestId, preview.EncounterId, preview.PatientId)
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
    public decimal AvailableBalance => (Preview?.PrepaymentBalance ?? 0m) - (Preview?.ChargeTotal ?? 0m);
    public string AvailableBalanceText => "¥" + AvailableBalance.ToString("N2");
    public bool HasArrearsWarning => Preview != null && AvailableBalance <= 500m;
    public string ArrearsWarningText => Preview == null ? string.Empty : (AvailableBalance < 0
        ? $"⚠ 欠费催缴预警：当前已欠费 ¥{Math.Abs(AvailableBalance):N2}，请及时通知患者家属补缴住院押金"
        : (AvailableBalance <= 500m
            ? $"⚡ 预交金偏低预警：剩余可用 ¥{AvailableBalance:N2} (低于 500 元警戒线)，建议催缴押金"
            : $"✓ 预交金充足：可用结余 ¥{AvailableBalance:N2}"));
    public string ArrearsBrush => AvailableBalance < 0 ? "#EF4444" : (AvailableBalance <= 500m ? "#F59E0B" : "#10B981");
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
            using var http = _createHttpClient();
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
            _isLoaded = true;
            Requests.Clear();
            foreach (var dto in envelope.Data ?? new List<IpdDischargeRequestDto>())
            {
                Requests.Add(new IpdDischargeQueueItem(dto));
            }
            OnPropertyChanged(nameof(QueueCountText));
            SelectedRequest = Requests.FirstOrDefault(x => x.RequestId == keepId) ?? Requests.FirstOrDefault();
            if (SelectedRequest == null)
            {
                Notice = "当前筛选条件下没有出院结算申请。";
            }
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
        if (!ReferenceEquals(SelectedRequest, item))
        {
            return;
        }
        long revision = ResetPreview(isLoading: true);
        Notice = "正在加载当前出院申请的费用预览。";
        try
        {
            using var http = _createHttpClient();
            using var response = await http.GetAsync(
                "/api/v4/ipd/discharge-requests/" + Uri.EscapeDataString(item.RequestId) + "/settlement-preview");
            var envelope = await ReadEnvelopeAsync<IpdSettlementPreviewDto>(response);
            if (revision != _previewRevision)
            {
                return;
            }
            if (!response.IsSuccessStatusCode || envelope.Code != 200 || envelope.Data == null)
            {
                SetFailure(string.IsNullOrWhiteSpace(envelope.Message) ? "住院费用预览加载失败。" : envelope.Message);
                return;
            }

            var preview = envelope.Data;
            if (!MatchesRequest(item, preview.RequestId, preview.EncounterId, preview.PatientId))
            {
                SetFailure("费用预览与当前出院申请或患者不一致，请刷新队列后重试。");
                return;
            }

            foreach (var charge in preview.ChargeItems ?? new List<IpdSettlementChargeDto>())
            {
                Charges.Add(new IpdSettlementChargeItem(charge));
            }
            foreach (string blocker in preview.ClinicalBlockers ?? new List<string>())
            {
                ClinicalBlockers.Add(blocker);
            }

            Preview = preview;
            PrepaymentDeductText = Math.Min(preview.PrepaymentBalance, preview.ChargeTotal).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            RecalculateSelfPay();
            Notice = preview.Status switch
            {
                "SETTLED" => string.IsNullOrWhiteSpace(item.PaymentNo) ? "该出院申请已结算。" : "该出院申请已结算，收费单号：" + item.PaymentNo,
                "CANCELLED" => "该出院申请已撤回，不能结算。",
                _ => ClinicalBlockers.Count == 0 ? "请核对当前患者和结算金额。" : "六项核查存在阻断项，收费处不能直接结算。"
            };
        }
        catch (Exception ex)
        {
            if (revision == _previewRevision)
            {
                SetException("住院费用预览加载失败", ex);
            }
        }
        finally
        {
            if (revision == _previewRevision)
            {
                _isLoadingPreview = false;
                NotifyPreview();
            }
        }
    }

    private long ResetPreview(bool isLoading = false)
    {
        long revision = ++_previewRevision;
        ConfirmationText = string.Empty;
        _isLoadingPreview = isLoading;
        Preview = null;
        Charges.Clear();
        ClinicalBlockers.Clear();
        InsuranceAmountText = "0.00";
        PrepaymentDeductText = "0.00";
        SelfPayAmountText = "0.00";
        RefundAmountText = "0.00";
        ClearError();
        NotifyPreview();
        return revision;
    }

    public Task RefreshAsync() => LoadAsync(Keyword, StatusFilter);

    private static bool MatchesRequest(IpdDischargeQueueItem request, string id, string encounterId, string patientId) =>
        !string.IsNullOrWhiteSpace(request.PatientId) && request.RequestId == id
        && request.EncounterId == encounterId && request.PatientId == patientId;

    private void OpenConfirmation()
    {
        if (Preview is { } preview && SelectedRequest is { } request)
        {
            ConfirmationText = $"患者：{preview.PatientName}\n住院号：{preview.AdmissionNo}\n出院申请：{request.RequestNo}\n"
                + $"住院总费用：{ChargeTotalText}，医保统筹：¥{InsuranceAmountText}，预交金抵扣：¥{PrepaymentDeductText}\n"
                + $"个人自付：¥{SelfPayAmountText}，应退预交金：¥{RefundAmountText}\n"
                + $"支付方式：{SelectedPaymentChannel?.Name}。确认后将办理结算并释放床位。";
        }
    }

    internal async Task SettleAsync()
    {
        if (IsBusy)
        {
            return;
        }
        if (!CanSettle || SelectedRequest is not { } request || Preview is not { } preview
            || SelectedPaymentChannel is not { } channel)
        {
            SetValidation("请加载当前患者的待结算费用，确认临床核查通过并选择支付方式。");
            return;
        }
        if (!TryParseMoney(InsuranceAmountText, out decimal insurance)
            || !TryParseMoney(PrepaymentDeductText, out decimal prepaymentDeduct))
        {
            SetValidation("医保统筹和预交金抵扣必须是有效金额。");
            return;
        }
        decimal total = preview.ChargeTotal;
        decimal balance = preview.PrepaymentBalance;
        if (insurance < 0 || insurance > total)
        {
            SetValidation($"医保报销金额必须在 0 ~ ¥{total:F2} 之间。");
            return;
        }
        decimal maxDeduct = Math.Min(balance, Math.Max(0m, total - insurance));
        if (prepaymentDeduct < 0 || prepaymentDeduct > maxDeduct)
        {
            SetValidation($"预交金抵扣金额必须在 0 ~ ¥{maxDeduct:F2} 之间。");
            return;
        }
        if (insurance + prepaymentDeduct > total)
        {
            SetValidation("医保报销加预交金抵扣不能超过住院总费用。");
            return;
        }
        if (!TryParseMoney(SelfPayAmountText, out decimal selfPay)
            || !TryParseMoney(RefundAmountText, out decimal refund)
            || selfPay < 0 || refund < 0)
        {
            SetValidation("结算金额计算失败，自付或退款不能为负数。");
            return;
        }

        long revision = _previewRevision;
        var settlement = new
        {
            preview.PreviewVersion,
            TotalAmount = total,
            InsuranceAmount = insurance,
            PrepaymentDeduct = prepaymentDeduct,
            SelfPayAmount = selfPay,
            RefundAmount = refund,
            PaymentChannel = channel.Code,
            RequestKey = "IPD-DISCHARGE-" + request.RequestId
        };
        IsBusy = true;
        ClearError();
        try
        {
            using var http = _createHttpClient();
            using var response = await http.PostAsJsonAsync(
                "/api/v4/ipd/discharge-requests/" + Uri.EscapeDataString(request.RequestId) + "/settle",
                settlement, JsonOptions);
            var envelope = await ReadEnvelopeAsync<IpdDischargeRequestDto>(response);
            var receipt = envelope.Data;
            if (!response.IsSuccessStatusCode || envelope.Code != 200 || receipt is not { Status: "SETTLED" }
                || !MatchesRequest(request, receipt.Id, receipt.EncounterId, receipt.PatientId)
                || string.IsNullOrWhiteSpace(receipt.PaymentNo))
            {
                if (revision == _previewRevision)
                {
                    ResetPreview();
                    SetFailure(envelope.Code != 200 && !string.IsNullOrWhiteSpace(envelope.Message)
                        ? envelope.Message + "。请重新核对结算状态。"
                        : "未收到可确认的出院结算单，请查询该申请的结算状态。");
                }
                return;
            }

            request.MarkSettled(receipt.PaymentNo, receipt.SettlementTotalAmount);
            if (revision == _previewRevision)
            {
                Preview = preview with { Status = receipt.Status };
                Notice = $"{preview.PatientName}（住院号 {preview.AdmissionNo}）出院结算成功，收费单号：{receipt.PaymentNo}。";
            }
        }
        catch (Exception ex)
        {
            if (revision == _previewRevision)
            {
                ResetPreview();
                SetException("未能确认出院结算结果，请先查询原申请再重试", ex);
            }
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

        if (insurance < 0) insurance = 0;
        if (insurance > total) insurance = total;

        decimal maxPrepaymentDeduct = Math.Min(balance, Math.Max(0m, total - insurance));
        if (prepaymentDeduct < 0) prepaymentDeduct = 0;
        if (prepaymentDeduct > maxPrepaymentDeduct) prepaymentDeduct = maxPrepaymentDeduct;

        decimal selfPay = total - insurance - prepaymentDeduct;
        decimal refund = balance - prepaymentDeduct;
        SelfPayAmountText = selfPay.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        RefundAmountText = refund.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
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
        OnPropertyChanged(nameof(AvailableBalance));
        OnPropertyChanged(nameof(AvailableBalanceText));
        OnPropertyChanged(nameof(HasArrearsWarning));
        OnPropertyChanged(nameof(ArrearsWarningText));
        OnPropertyChanged(nameof(ArrearsBrush));
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
        List<string> ClinicalBlockers,
        string? PreviewVersion);

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
            PatientId = dto.PatientId;
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
        public string PatientId { get; }
        public string PatientName { get; }
        public string MedicalRecordNo { get; }
        public string AdmissionNo { get; }
        public string DeptName { get; }
        public string WardName { get; }
        public string BedCode { get; }
        public string DoctorName { get; }
        public string Status { get; private set; }
        public long RequestedAtMs { get; }
        public decimal SettlementTotalAmount { get; private set; }
        public string PaymentNo { get; private set; }
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
            ? DateTimeOffset.FromUnixTimeMilliseconds(RequestedAtMs).ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm:ss")
            : "--";
        public string StatusLabel => Status switch
        {
            "PENDING" => "待财务结算",
            "SETTLED" => "已结算",
            "CANCELLED" => "已撤回",
            _ => Status
        };
        public string BlockerText => BlockerCount == 0 ? "临床核查通过" : $"临床阻断 {BlockerCount} 项";

        internal void MarkSettled(string paymentNo, decimal total)
        {
            Status = "SETTLED";
            PaymentNo = paymentNo;
            SettlementTotalAmount = total;
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusLabel));
            OnPropertyChanged(nameof(PaymentNo));
            OnPropertyChanged(nameof(SettlementTotalAmount));
        }
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
