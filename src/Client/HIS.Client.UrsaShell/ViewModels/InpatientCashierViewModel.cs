using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Services;
using HIS.Client.UrsaShell.Shell;

namespace HIS.Client.UrsaShell.ViewModels;

public sealed class InpatientCashierViewModel : ClientModelBase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private string _keyword = string.Empty;
    private string _statusFilter = "PENDING";
    private AdmissionCardItem? _selectedCard;
    private InpatientDeptChoice? _selectedDept;
    private InpatientWardChoice? _selectedWard;
    private InpatientBedItem? _selectedBed;
    private decimal? _initialPrepayment = 5000m;
    private decimal? _additionalPrepayment = 2000m;
    private PrepaymentChannel? _selectedPaymentChannel;
    private PrepaymentChannel? _additionalPaymentChannel;
    private string _registrationRemark = string.Empty;
    private string _prepaymentRemark = string.Empty;
    private IpdEncounterDto? _selectedEncounter;
    private bool _isBusy;
    private bool _isLoaded;
    private bool _hasChanges;
    private string _notice = "待登记住院卡会显示在这里，选择患者后可分配病区、床位并收取押金。";
    private string _errorMessage = string.Empty;
    private int _pendingCount;
    private int _registeredCount;
    private decimal _todayPrepaymentAmount;
    private string _selectedCardId = string.Empty;
    private string _loadedWardId = string.Empty;

    // 新建档表单字段
    private string _newPatientName = string.Empty;
    private string _newPatientGender = "1";
    private string _newPatientBirthDate = string.Empty;
    private string _newPatientIdCard = string.Empty;
    private string _newPatientPhone = string.Empty;
    private string _newPatientGuardianName = string.Empty;
    private string _newPatientGuardianIdCard = string.Empty;
    private string _newPatientAllergy = string.Empty;
    private string _newCardDiagnosis = string.Empty;
    private string _newCardRemark = string.Empty;
    private InpatientDeptChoice? _newCardDept;
    private InpatientWardChoice? _newCardWard;
    private bool _showNewPatientForm;
    private string _patientFormError = string.Empty;
    private string _patientFormNotice = string.Empty;
    private bool _allowReusePatient;

    public InpatientCashierViewModel()
    {
        PaymentChannels.Add(new("CASH", "现金"));
        PaymentChannels.Add(new("BANK_CARD", "银行卡"));
        PaymentChannels.Add(new("WECHAT", "微信"));
        PaymentChannels.Add(new("ALIPAY", "支付宝"));
        PaymentChannels.Add(new("INSURANCE", "医保"));
        _selectedPaymentChannel = PaymentChannels[0];
        _additionalPaymentChannel = PaymentChannels[0];

        RefreshCommand = new AsyncRelayCommand(LoadAllAsync);
        SearchCommand = new AsyncRelayCommand(LoadAllAsync);
        RegisterCommand = new AsyncRelayCommand(RegisterAsync, () => CanRegister);
        AddPrepaymentCommand = new AsyncRelayCommand(AddPrepaymentAsync, () => CanTakePrepayment);
        RegisterAndCreateCardCommand = new AsyncRelayCommand(RegisterAndCreateCardAsync);
        ToggleNewPatientFormCommand = new RelayCommand(ToggleNewPatientForm);
    }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SearchCommand { get; }
    public AsyncRelayCommand RegisterCommand { get; }
    public AsyncRelayCommand AddPrepaymentCommand { get; }
    public AsyncRelayCommand RegisterAndCreateCardCommand { get; }
    public RelayCommand ToggleNewPatientFormCommand { get; }

    public ObservableCollection<AdmissionCardItem> Cards { get; } = new();
    public ObservableCollection<InpatientRecordItem> Encounters { get; } = new();
    public ObservableCollection<InpatientDeptChoice> Departments { get; } = new();
    public ObservableCollection<InpatientWardChoice> Wards { get; } = new();
    public ObservableCollection<InpatientBedItem> Beds { get; } = new();
    public ObservableCollection<PrepaymentChannel> PaymentChannels { get; } = new();
    public ObservableCollection<PrepaymentRecordItem> Prepayments { get; } = new();
    public sealed record GenderChoice(string Code, string Name);
    public ObservableCollection<GenderChoice> GenderChoices { get; } = new()
    {
        new("1", "男"),
        new("2", "女")
    };
    public ObservableCollection<InpatientWardChoice> NewCardWards { get; } = new();

    public string Keyword
    {
        get => _keyword;
        set => SetField(ref _keyword, value ?? string.Empty);
    }

    public string StatusFilter
    {
        get => _statusFilter;
        set
        {
            if (SetField(ref _statusFilter, value ?? string.Empty))
            {
                HasChanges = true;
            }
        }
    }

    public AdmissionCardItem? SelectedCard
    {
        get => _selectedCard;
        set
        {
            if (SetField(ref _selectedCard, value))
            {
                NotifySelection();
                foreach (var card in Cards)
                {
                    card.IsSelected = ReferenceEquals(card, value);
                }
                if (value != null && value.Id != _selectedCardId)
                {
                    _selectedCardId = value.Id;
                    _ = PrepareCardAsync(value);
                }
            }
        }
    }

    public InpatientDeptChoice? SelectedDept
    {
        get => _selectedDept;
        set
        {
            if (SetField(ref _selectedDept, value))
            {
                Wards.Clear();
                if (value != null)
                {
                    foreach (var ward in value.Wards)
                    {
                        Wards.Add(ward);
                    }
                }
                SelectedWard = Wards.FirstOrDefault();
                NotifySelection();
            }
        }
    }

    public InpatientWardChoice? SelectedWard
    {
        get => _selectedWard;
        set
        {
            if (SetField(ref _selectedWard, value))
            {
                SelectedBed = null;
                Beds.Clear();
                _loadedWardId = string.Empty;
                NotifySelection();
                if (value != null)
                {
                    _ = LoadBedsAsync(value.Id);
                }
            }
        }
    }

    public InpatientBedItem? SelectedBed
    {
        get => _selectedBed;
        set
        {
            if (SetField(ref _selectedBed, value))
            {
                OnPropertyChanged(nameof(SelectedBedSummary));
                OnPropertyChanged(nameof(CanRegister));
            }
        }
    }

    public decimal? InitialPrepayment
    {
        get => _initialPrepayment;
        set
        {
            if (SetField(ref _initialPrepayment, value))
            {
                OnPropertyChanged(nameof(CanRegister));
                OnPropertyChanged(nameof(InitialPrepaymentText));
            }
        }
    }

    public decimal? AdditionalPrepayment
    {
        get => _additionalPrepayment;
        set
        {
            if (SetField(ref _additionalPrepayment, value))
            {
                OnPropertyChanged(nameof(CanTakePrepayment));
                OnPropertyChanged(nameof(AdditionalPrepaymentText));
            }
        }
    }

    public PrepaymentChannel? SelectedPaymentChannel
    {
        get => _selectedPaymentChannel;
        set
        {
            if (SetField(ref _selectedPaymentChannel, value))
            {
                OnPropertyChanged(nameof(CanRegister));
            }
        }
    }

    public PrepaymentChannel? AdditionalPaymentChannel
    {
        get => _additionalPaymentChannel;
        set
        {
            if (SetField(ref _additionalPaymentChannel, value))
            {
                OnPropertyChanged(nameof(CanTakePrepayment));
            }
        }
    }

    public string RegistrationRemark
    {
        get => _registrationRemark;
        set => SetField(ref _registrationRemark, value ?? string.Empty);
    }

    public string PrepaymentRemark
    {
        get => _prepaymentRemark;
        set => SetField(ref _prepaymentRemark, value ?? string.Empty);
    }

    public IpdEncounterDto? SelectedEncounter
    {
        get => _selectedEncounter;
        set
        {
            if (SetField(ref _selectedEncounter, value))
            {
                NotifySelection();
                if (value != null)
                {
                    _ = LoadPrepaymentsAsync(value.Id);
                }
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsNotBusy));
                OnPropertyChanged(nameof(CanRegister));
                OnPropertyChanged(nameof(CanTakePrepayment));
                RegisterCommand.RaiseCanExecuteChanged();
                AddPrepaymentCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsNotBusy => !IsBusy;
    public bool HasChanges
    {
        get => _hasChanges;
        set => SetField(ref _hasChanges, value);
    }

    public string Notice { get => _notice; private set => SetField(ref _notice, value); }
    public string ErrorMessage { get => _errorMessage; private set => SetField(ref _errorMessage, value); }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public int PendingCount { get => _pendingCount; private set => SetField(ref _pendingCount, value); }
    public int RegisteredCount { get => _registeredCount; private set => SetField(ref _registeredCount, value); }
    public decimal TodayPrepaymentAmount { get => _todayPrepaymentAmount; private set => SetField(ref _todayPrepaymentAmount, value); }
    public string PendingCountText => PendingCount + " 张";
    public string RegisteredCountText => RegisteredCount + " 人次";
    public string TodayPrepaymentText => "¥" + TodayPrepaymentAmount.ToString("N2");
    public string CardCountText => Cards.Count + " 张住院卡";
    public string EncounterCountText => Encounters.Count + " 位在院患者";

    public bool HasSelectedCard => SelectedCard != null;
    public bool HasSelectedEncounter => SelectedEncounter != null;
    public bool PendingCardNeedsRegistration => SelectedCard?.IsPending == true;
    public bool ShowRegistrationForm => IsNotBusy && SelectedCard?.IsPending == true;
    public bool ShowPrepaymentForm => IsNotBusy && SelectedEncounter != null;
    public string SelectedCardNumber => SelectedCard?.CardNo ?? "请选择住院卡";
    public string SelectedCardPatient => SelectedCard == null
        ? "从左侧选择患者，查看门诊开具的住院信息。"
        : SelectedCard.PatientName + "  " + SelectedCard.GenderLabel + "  " + SelectedCard.MedicalRecordNo;
    public string SelectedCardDoctor => "开单医生 " + (SelectedCard?.SourceDoctorName ?? "--");
    public string SelectedCardDepartment => SelectedCard?.SuggestedIpdDeptName ?? "--";
    public string SelectedCardWard => string.IsNullOrWhiteSpace(SelectedCard?.SuggestedWardName) ? "由收费处安排" : SelectedCard!.SuggestedWardName;
    public string SelectedCardDiagnosis => string.IsNullOrWhiteSpace(SelectedCard?.AdmissionDiagnosis) ? "未填写" : SelectedCard!.AdmissionDiagnosis!;
    public string SelectedCardPhone => string.IsNullOrWhiteSpace(SelectedCard?.ContactPhone) ? "未登记" : SelectedCard!.ContactPhone!;
    public string SelectedCardRemark => string.IsNullOrWhiteSpace(SelectedCard?.Remark) ? "无" : SelectedCard!.Remark!;
    public string SelectedCardBalance => SelectedCard == null ? "¥0.00" : "¥" + SelectedCard.PrepaymentBalance.ToString("N2");
    public string SelectedBedSummary => SelectedBed == null ? "尚未选择可选床位" : "已选 " + SelectedBed.Label;
    public string InitialPrepaymentText => "¥" + (InitialPrepayment ?? 0).ToString("N2");
    public string AdditionalPrepaymentText => "¥" + (AdditionalPrepayment ?? 0).ToString("N2");
    public string EncounterAdmissionNo => SelectedEncounter?.AdmissionNo ?? "请选择在院患者";
    public string EncounterPatientName => SelectedEncounter?.PatientName ?? "登记患者后即可追缴押金";
    public string EncounterBedLine => SelectedEncounter == null ? "--" : SelectedEncounter.DeptName + "  " + SelectedEncounter.WardName + "  床位 " + SelectedEncounter.BedCode;
    public string EncounterBalanceText => "当前押金余额 ¥" + (SelectedEncounter?.PrepaymentBalance ?? 0).ToString("N2");
    public string NewBalanceText => "本次操作后余额 ¥" + ((SelectedEncounter?.PrepaymentBalance ?? 0) + (AdditionalPrepayment ?? 0)).ToString("N2");
    public bool CanRegister => IsNotBusy && SelectedCard?.IsPending == true && SelectedDept != null
        && SelectedWard != null && SelectedBed?.IsAvailable == true && SelectedPaymentChannel != null
        && (InitialPrepayment ?? 0) >= 0;
    public bool CanTakePrepayment => IsNotBusy && SelectedEncounter != null && AdditionalPaymentChannel != null
        && (AdditionalPrepayment ?? 0) > 0;
    public bool HasPrepaymentRecords => Prepayments.Count > 0;

    // 新建档表单属性
    public string NewPatientName { get => _newPatientName; set => SetField(ref _newPatientName, value ?? string.Empty); }
    public string NewPatientGender { get => _newPatientGender; set => SetField(ref _newPatientGender, value ?? "1"); }
    public string NewPatientBirthDate { get => _newPatientBirthDate; set => SetField(ref _newPatientBirthDate, value ?? string.Empty); }
    public string NewPatientIdCard { get => _newPatientIdCard; set => SetField(ref _newPatientIdCard, value ?? string.Empty); }
    public string NewPatientPhone { get => _newPatientPhone; set => SetField(ref _newPatientPhone, value ?? string.Empty); }
    public string NewPatientGuardianName { get => _newPatientGuardianName; set => SetField(ref _newPatientGuardianName, value ?? string.Empty); }
    public string NewPatientGuardianIdCard { get => _newPatientGuardianIdCard; set => SetField(ref _newPatientGuardianIdCard, value ?? string.Empty); }
    public string NewPatientAllergy { get => _newPatientAllergy; set => SetField(ref _newPatientAllergy, value ?? string.Empty); }

    public string NewCardDiagnosis { get => _newCardDiagnosis; set => SetField(ref _newCardDiagnosis, value ?? string.Empty); }
    public string NewCardRemark { get => _newCardRemark; set => SetField(ref _newCardRemark, value ?? string.Empty); }
    public InpatientDeptChoice? NewCardDept
    {
        get => _newCardDept;
        set
        {
            if (SetField(ref _newCardDept, value))
            {
                NewCardWards.Clear();
                if (value != null)
                {
                    foreach (var w in value.Wards)
                    {
                        NewCardWards.Add(w);
                    }
                }
                NewCardWard = NewCardWards.FirstOrDefault();
            }
        }
    }
    public InpatientWardChoice? NewCardWard { get => _newCardWard; set => SetField(ref _newCardWard, value); }

    public bool ShowNewPatientForm { get => _showNewPatientForm; set => SetField(ref _showNewPatientForm, value); }
    public string PatientFormError { get => _patientFormError; set => SetField(ref _patientFormError, value ?? string.Empty); }
    public string PatientFormNotice { get => _patientFormNotice; set => SetField(ref _patientFormNotice, value ?? string.Empty); }
    public bool AllowReusePatient { get => _allowReusePatient; set => SetField(ref _allowReusePatient, value); }

    public void ToggleNewPatientForm()
    {
        ShowNewPatientForm = !ShowNewPatientForm;
        PatientFormError = string.Empty;
        PatientFormNotice = string.Empty;
        AllowReusePatient = false;
        if (ShowNewPatientForm && NewCardDept == null && Departments.Count > 0)
        {
            NewCardDept = Departments.FirstOrDefault();
        }
    }

    public async Task RegisterAndCreateCardAsync()
    {
        if (IsBusy) return;

        if (string.IsNullOrWhiteSpace(NewPatientName))
        {
            PatientFormError = "请填写患者姓名。";
            return;
        }
        if (string.IsNullOrWhiteSpace(NewPatientIdCard))
        {
            PatientFormError = "请填写身份证号码。";
            return;
        }
        if (string.IsNullOrWhiteSpace(NewPatientBirthDate))
        {
            PatientFormError = "请填写出生日期。";
            return;
        }
        if (string.IsNullOrWhiteSpace(NewPatientPhone))
        {
            PatientFormError = "请填写联系电话。";
            return;
        }
        if (NewCardDept == null)
        {
            PatientFormError = "请选择建议住院科室。";
            return;
        }

        if (!int.TryParse(NewPatientBirthDate.Trim().Replace("-", "").Replace("/", ""), out var birthDateInt)
            || birthDateInt < 19000101 || birthDateInt > 20991231)
        {
            PatientFormError = "出生日期格式无效，请输入 yyyyMMdd 或 yyyy-MM-dd。";
            return;
        }

        List<string>? allergyTags = null;
        if (!string.IsNullOrWhiteSpace(NewPatientAllergy))
        {
            allergyTags = NewPatientAllergy.Split(new[] { ',', '，', '、', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        IsBusy = true;
        PatientFormError = string.Empty;
        PatientFormNotice = string.Empty;
        try
        {
            using var http = CreateHttpClient();
            using var response = await http.PostAsJsonAsync(
                "/api/v4/ipd/admission/register-with-patient",
                new
                {
                    FullName = NewPatientName.Trim(),
                    GenderCode = NewPatientGender,
                    BirthDateInt = birthDateInt,
                    IdCardNo = NewPatientIdCard.Trim(),
                    Phone = NewPatientPhone.Trim(),
                    GuardianName = string.IsNullOrWhiteSpace(NewPatientGuardianName) ? null : NewPatientGuardianName.Trim(),
                    GuardianIdCardNo = string.IsNullOrWhiteSpace(NewPatientGuardianIdCard) ? null : NewPatientGuardianIdCard.Trim(),
                    AllergyTags = allergyTags,
                    SuggestedIpdDeptId = NewCardDept.Id,
                    SuggestedWardId = NewCardWard?.Id,
                    AdmissionDiagnosis = string.IsNullOrWhiteSpace(NewCardDiagnosis) ? null : NewCardDiagnosis.Trim(),
                    Remark = string.IsNullOrWhiteSpace(NewCardRemark) ? null : NewCardRemark.Trim(),
                    RequestKey = "IPD_CASHIER_ADC_" + Guid.NewGuid().ToString("N"),
                    ReuseExistingPatient = AllowReusePatient
                },
                JsonOptions);

            var envelope = await ReadEnvelopeAsync<AdmissionCardDto>(response);

            if (!response.IsSuccessStatusCode || envelope.Code != 200 || envelope.Data == null)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.Conflict && !AllowReusePatient && envelope.Message?.Contains("已建档") == true)
                {
                    AllowReusePatient = true;
                    PatientFormError = envelope.Message + "（已允许使用已有患者档案，再次点击按钮即可直接开立住院卡）";
                }
                else
                {
                    PatientFormError = string.IsNullOrWhiteSpace(envelope.Message)
                        ? $"建档与开卡失败 (HTTP {(int)response.StatusCode})"
                        : envelope.Message;
                }
                return;
            }

            var cardDto = envelope.Data;
            Notice = "建档并开卡成功，请继续分配床位并收取押金。住院卡号: " + cardDto.CardNo;
            ShowNewPatientForm = false;
            AllowReusePatient = false;
            ClearNewPatientForm();

            await LoadCardsAsync();
            var matchCard = Cards.FirstOrDefault(c => c.Id == cardDto.Id || c.CardNo == cardDto.CardNo);
            if (matchCard != null)
            {
                SelectedCard = matchCard;
            }
        }
        catch (Exception ex)
        {
            SetException("建档并开立住院卡异常", ex);
            PatientFormError = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ClearNewPatientForm()
    {
        NewPatientName = string.Empty;
        NewPatientGender = "1";
        NewPatientBirthDate = string.Empty;
        NewPatientIdCard = string.Empty;
        NewPatientPhone = string.Empty;
        NewPatientGuardianName = string.Empty;
        NewPatientGuardianIdCard = string.Empty;
        NewPatientAllergy = string.Empty;
        NewCardDiagnosis = string.Empty;
        NewCardRemark = string.Empty;
        PatientFormError = string.Empty;
        PatientFormNotice = string.Empty;
        AllowReusePatient = false;
    }

    public async Task EnsureLoadedAsync()
    {
        if (_isLoaded || IsBusy)
        {
            return;
        }
        try
        {
            await LoadOptionsAsync();
            await LoadCardsAsync();
            await LoadEncountersAsync();
            _isLoaded = true;
        }
        catch (Exception ex)
        {
            SetException("住院收费工作台加载失败", ex);
        }
    }

    public async Task LoadAllAsync()
    {
        if (IsBusy)
        {
            return;
        }
        try
        {
            await LoadCardsAsync();
            await LoadEncountersAsync();
            ClearError();
            Notice = "住院卡和在院患者信息已刷新。";
        }
        catch (Exception ex)
        {
            SetException("住院信息刷新失败", ex);
        }
    }

    public async Task RegisterAsync()
    {
        if (IsBusy)
        {
            return;
        }
        var card = SelectedCard;
        if (card == null || !card.IsPending)
        {
            SetValidation("请选择一张待登记的住院卡。");
            return;
        }
        if (SelectedDept == null || SelectedWard == null || SelectedBed == null)
        {
            SetValidation("请选择住院科室、病区和可用床位。");
            return;
        }
        if (SelectedBed.IsAvailable != true)
        {
            SetValidation("该床位已被占用，请重新选择。");
            return;
        }
        if (SelectedPaymentChannel == null)
        {
            SetValidation("请选择押金支付方式。");
            return;
        }
        IsBusy = true;
        ClearError();
        try
        {
            using var http = CreateHttpClient();
            using var response = await http.PostAsJsonAsync("/api/v4/ipd/cards/" + Uri.EscapeDataString(card.Id) + "/register",
                new
                {
                    IpdDeptId = SelectedDept.Id,
                    WardId = SelectedWard.Id,
                    BedCode = SelectedBed.BedCode,
                    IsBorrowedBed = SelectedWard.IsBorrowed,
                    InitialPrepayment = InitialPrepayment ?? 0,
                    PaymentChannel = SelectedPaymentChannel.Code,
                    RequestKey = "IPD-REGISTER-" + Guid.NewGuid().ToString("N"),
                    Remark = string.IsNullOrWhiteSpace(RegistrationRemark) ? null : RegistrationRemark.Trim()
                }, JsonOptions);
            var envelope = await ReadEnvelopeAsync<AdmissionCardDto>(response);
            if (!response.IsSuccessStatusCode || envelope.Code != 200 || envelope.Data == null)
            {
                SetFailure(string.IsNullOrWhiteSpace(envelope.Message) ? "住院登记失败，请检查床位和权限。" : envelope.Message);
                return;
            }
            Notice = card.PatientName + " 已办理住院登记，住院号 " + (string.IsNullOrWhiteSpace(envelope.Data.EncounterNo) ? "生成中" : envelope.Data.EncounterNo)
                + "。押金已入账，可通知病区护士接诊。";
            RegistrationRemark = string.Empty;
            await LoadCardsAsync();
            await LoadEncountersAsync(envelope.Data.EncounterId);
            SelectedCard = Cards.FirstOrDefault(x => x.Id == card.Id);
            SelectedEncounter = Encounters.FirstOrDefault(x => x.Id == envelope.Data.EncounterId)?.Encounter;
        }
        catch (Exception ex)
        {
            SetException("住院登记失败", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task AddPrepaymentAsync()
    {
        if (IsBusy)
        {
            return;
        }
        var encounter = SelectedEncounter;
        if (encounter == null)
        {
            SetValidation("请选择需要交纳押金的在院患者。");
            return;
        }
        if (AdditionalPaymentChannel == null || (AdditionalPrepayment ?? 0) <= 0)
        {
            SetValidation("请输入大于零的押金金额并选择支付方式。");
            return;
        }
        IsBusy = true;
        ClearError();
        try
        {
            using var http = CreateHttpClient();
            using var response = await http.PostAsJsonAsync(
                "/api/v4/ipd/encounters/" + Uri.EscapeDataString(encounter.Id) + "/prepayments",
                new
                {
                    Amount = AdditionalPrepayment,
                    PaymentChannel = AdditionalPaymentChannel.Code,
                    RequestKey = "IPD-PREPAY-" + Guid.NewGuid().ToString("N"),
                    Remark = string.IsNullOrWhiteSpace(PrepaymentRemark) ? "住院押金补缴" : PrepaymentRemark.Trim()
                }, JsonOptions);
            var envelope = await ReadEnvelopeAsync<PrepaymentDto>(response);
            if (!response.IsSuccessStatusCode || envelope.Code != 200 || envelope.Data == null)
            {
                SetFailure(string.IsNullOrWhiteSpace(envelope.Message) ? "押金收取失败。" : envelope.Message);
                return;
            }
            Notice = "押金收取成功，单号 " + envelope.Data.PrepaymentNo + "，当前余额 ¥" + envelope.Data.BalanceAfter.ToString("N2") + "。";
            PrepaymentRemark = string.Empty;
            await LoadEncountersAsync(encounter.Id);
            await LoadPrepaymentsAsync(encounter.Id);
        }
        catch (Exception ex)
        {
            SetException("押金收取失败", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PrepareCardAsync(AdmissionCardItem item)
    {
        try
        {
            var preferredDept = Departments.FirstOrDefault(x => x.Id == item.SuggestedIpdDeptId)
                ?? Departments.FirstOrDefault();
            SelectedDept = preferredDept;
            if (preferredDept != null && !string.IsNullOrWhiteSpace(item.SuggestedWardId))
            {
                SelectedWard = preferredDept.Wards.FirstOrDefault(x => x.Id == item.SuggestedWardId)
                    ?? preferredDept.Wards.FirstOrDefault();
            }
            if (SelectedWard != null)
            {
                await LoadBedsAsync(SelectedWard.Id, force: true);
            }
        }
        catch (Exception ex)
        {
            SetException("床位信息加载失败", ex);
        }
    }

    private async Task LoadOptionsAsync()
    {
        using var http = CreateHttpClient();
        using var response = await http.GetAsync("/api/v4/ipd/registration/options");
        var envelope = await ReadEnvelopeAsync<RegistrationOptionsDto>(response);
        if (!response.IsSuccessStatusCode || envelope.Code != 200)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(envelope.Message) ? "住院科室加载失败。" : envelope.Message);
        }
        Departments.Clear();
        foreach (var dept in envelope.Data?.Depts ?? new List<InpatientDeptDto>())
        {
            Departments.Add(new InpatientDeptChoice(dept));
        }
    }

    private async Task LoadBedsAsync(string wardId, bool force = false)
    {
        if (!force && wardId == _loadedWardId)
        {
            return;
        }
        using var http = CreateHttpClient();
        using var response = await http.GetAsync("/api/v4/ipd/wards/" + Uri.EscapeDataString(wardId) + "/beds");
        var envelope = await ReadEnvelopeAsync<List<InpatientBedDto>>(response);
        if (!response.IsSuccessStatusCode || envelope.Code != 200)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(envelope.Message) ? "床位加载失败。" : envelope.Message);
        }
        Beds.Clear();
        foreach (var bed in (envelope.Data ?? new List<InpatientBedDto>()).OrderBy(x => x.RoomCode).ThenBy(x => x.BedCode))
        {
            Beds.Add(new InpatientBedItem(bed));
        }
        _loadedWardId = wardId;
        SelectedBed = Beds.FirstOrDefault(x => x.IsAvailable);
        OnPropertyChanged(nameof(SelectedBedSummary));
        OnPropertyChanged(nameof(CanRegister));
    }

    private async Task LoadCardsAsync()
    {
        using var http = CreateHttpClient();
        var query = "/api/v4/ipd/cards?status=" + Uri.EscapeDataString(StatusFilter)
            + "&keyword=" + Uri.EscapeDataString(Keyword.Trim());
        using var response = await http.GetAsync(query);
        var envelope = await ReadEnvelopeAsync<List<AdmissionCardDto>>(response);
        if (!response.IsSuccessStatusCode || envelope.Code != 200)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(envelope.Message) ? "住院卡加载失败。" : envelope.Message);
        }
        var keepId = SelectedCard?.Id;
        Cards.Clear();
        foreach (var dto in envelope.Data ?? new List<AdmissionCardDto>())
        {
            Cards.Add(new AdmissionCardItem(dto));
        }
        _selectedCardId = keepId ?? string.Empty;
        SelectedCard = Cards.FirstOrDefault(x => x.Id == keepId) ?? Cards.FirstOrDefault();
        PendingCount = Cards.Count(x => x.IsPending);
        HasChanges = false;
        OnPropertyChanged(nameof(CardCountText));
    }

    private async Task LoadEncountersAsync(string? selectId = null)
    {
        using var http = CreateHttpClient();
        using var response = await http.GetAsync("/api/v4/ipd/encounters/active");
        var envelope = await ReadEnvelopeAsync<List<IpdEncounterDto>>(response);
        if (!response.IsSuccessStatusCode || envelope.Code != 200)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(envelope.Message) ? "在院患者加载失败。" : envelope.Message);
        }
        var keepId = selectId ?? SelectedEncounter?.Id;
        Encounters.Clear();
        foreach (var dto in envelope.Data ?? new List<IpdEncounterDto>())
        {
            Encounters.Add(new InpatientRecordItem(dto));
        }
        SelectedEncounter = Encounters.FirstOrDefault(x => x.Id == keepId)?.Encounter ?? Encounters.FirstOrDefault()?.Encounter;
        RegisteredCount = Encounters.Count;
        OnPropertyChanged(nameof(EncounterCountText));
        OnPropertyChanged(nameof(SelectedEncounter));
    }

    private async Task LoadPrepaymentsAsync(string encounterId)
    {
        using var http = CreateHttpClient();
        using var response = await http.GetAsync("/api/v4/ipd/prepayments?encounterId=" + Uri.EscapeDataString(encounterId));
        var envelope = await ReadEnvelopeAsync<List<PrepaymentDto>>(response);
        if (!response.IsSuccessStatusCode || envelope.Code != 200)
        {
            SetFailure(string.IsNullOrWhiteSpace(envelope.Message) ? "押金流水加载失败。" : envelope.Message);
            return;
        }
        Prepayments.Clear();
        decimal todayTotal = 0m;
        foreach (var dto in envelope.Data ?? new List<PrepaymentDto>())
        {
            Prepayments.Add(new PrepaymentRecordItem(dto));
            if (dto.PaidAtMs is long ms && DateTimeOffset.FromUnixTimeMilliseconds(ms).ToOffset(TimeSpan.FromHours(8)).Date == DateTime.Today)
            {
                todayTotal += dto.Amount;
            }
        }
        TodayPrepaymentAmount = todayTotal;
        OnPropertyChanged(nameof(HasPrepaymentRecords));
    }

    private void NotifySelection()
    {
        OnPropertyChanged(nameof(HasSelectedCard));
        OnPropertyChanged(nameof(HasSelectedEncounter));
        OnPropertyChanged(nameof(PendingCardNeedsRegistration));
        OnPropertyChanged(nameof(ShowRegistrationForm));
        OnPropertyChanged(nameof(ShowPrepaymentForm));
        OnPropertyChanged(nameof(SelectedCardNumber));
        OnPropertyChanged(nameof(SelectedCardPatient));
        OnPropertyChanged(nameof(SelectedCardDoctor));
        OnPropertyChanged(nameof(SelectedCardDepartment));
        OnPropertyChanged(nameof(SelectedCardWard));
        OnPropertyChanged(nameof(SelectedCardDiagnosis));
        OnPropertyChanged(nameof(SelectedCardPhone));
        OnPropertyChanged(nameof(SelectedCardRemark));
        OnPropertyChanged(nameof(SelectedCardBalance));
        OnPropertyChanged(nameof(EncounterAdmissionNo));
        OnPropertyChanged(nameof(EncounterPatientName));
        OnPropertyChanged(nameof(EncounterBedLine));
        OnPropertyChanged(nameof(EncounterBalanceText));
        OnPropertyChanged(nameof(NewBalanceText));
        OnPropertyChanged(nameof(CanRegister));
        OnPropertyChanged(nameof(CanTakePrepayment));
        RegisterCommand.RaiseCanExecuteChanged();
        AddPrepaymentCommand.RaiseCanExecuteChanged();
    }

    private void SetValidation(string message)
    {
        Notice = message;
        ErrorMessage = message;
        OnPropertyChanged(nameof(HasError));
    }

    private void SetFailure(string message)
    {
        Notice = message;
        ErrorMessage = message;
        OnPropertyChanged(nameof(HasError));
        ClientDiagnosticsLog.Warn("ipd.cashier", message);
    }

    private void SetException(string context, Exception ex)
    {
        Notice = context + "，请检查服务连接。";
        ErrorMessage = context + "：" + ex.Message;
        OnPropertyChanged(nameof(HasError));
        ClientDiagnosticsLog.Error("ipd.cashier", context, ex);
    }

    private void ClearError()
    {
        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(HasError));
    }

    private static HttpClient CreateHttpClient()
    {
        return AuthenticatedHttpClientFactory.Create();
    }

    private static async Task<ClientApiEnvelope<T>> ReadEnvelopeAsync<T>(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ClientApiEnvelope<T>>(JsonOptions) ?? new ClientApiEnvelope<T>();
        }
        catch (JsonException)
        {
            return new ClientApiEnvelope<T>();
        }
    }

    public sealed class AdmissionCardDto
    {
        public string Id { get; set; } = string.Empty;
        public string CardNo { get; set; } = string.Empty;
        public string PatientId { get; set; } = string.Empty;
        public string PatientName { get; set; } = string.Empty;
        public string GenderName { get; set; } = string.Empty;
        public string MedicalRecordNo { get; set; } = string.Empty;
        public string SourceDoctorName { get; set; } = string.Empty;
        public string SuggestedIpdDeptId { get; set; } = string.Empty;
        public string SuggestedIpdDeptName { get; set; } = string.Empty;
        public string? SuggestedWardId { get; set; }
        public string SuggestedWardName { get; set; } = string.Empty;
        public string? AdmissionDiagnosis { get; set; }
        public string? ContactPhone { get; set; }
        public string? Remark { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? EncounterId { get; set; }
        public string EncounterNo { get; set; } = string.Empty;
        public decimal PrepaymentBalance { get; set; }
        public string BedCode { get; set; } = string.Empty;
        public int TriageStatus { get; set; }
        public long CreatedAtMs { get; set; }
    }

    public sealed class InpatientBedDto
    {
        public string Id { get; set; } = string.Empty;
        public string WardId { get; set; } = string.Empty;
        public string RoomCode { get; set; } = string.Empty;
        public string BedCode { get; set; } = string.Empty;
        public int BedType { get; set; }
        public int BedStatus { get; set; }
        public string? CurrentEncounterId { get; set; }
    }

    public sealed class IpdEncounterDto
    {
        public string Id { get; set; } = string.Empty;
        public string AdmissionNo { get; set; } = string.Empty;
        public string PatientId { get; set; } = string.Empty;
        public string PatientName { get; set; } = string.Empty;
        public string? MedicalRecordNo { get; set; }
        public string AdmitDeptId { get; set; } = string.Empty;
        public string DeptName { get; set; } = string.Empty;
        public string AdmitWardId { get; set; } = string.Empty;
        public string WardName { get; set; } = string.Empty;
        public string BedCode { get; set; } = string.Empty;
        public decimal PrepaymentBalance { get; set; }
        public int Status { get; set; }
        public int TriageStatus { get; set; }
        public long? AdmittedAtMs { get; set; }
        public string? AttendingDoctorName { get; set; }
        public string? PrimaryNurseName { get; set; }
    }

    public sealed class PrepaymentDto
    {
        public string Id { get; set; } = string.Empty;
        public string PrepaymentNo { get; set; } = string.Empty;
        public string EncounterId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public decimal BalanceAfter { get; set; }
        public string PaymentChannel { get; set; } = string.Empty;
        public string CashierName { get; set; } = string.Empty;
        public long? PaidAtMs { get; set; }
        public string? Remark { get; set; }
    }

    private sealed class RegistrationOptionsDto
    {
        public List<InpatientDeptDto> Depts { get; set; } = new();
    }

    public sealed class InpatientDeptDto
    {
        public string Id { get; set; } = string.Empty;
        public string DeptCode { get; set; } = string.Empty;
        public string DeptName { get; set; } = string.Empty;
        public List<InpatientWardDto> Wards { get; set; } = new();
    }

    public sealed class InpatientWardDto
    {
        public string Id { get; set; } = string.Empty;
        public string DeptCode { get; set; } = string.Empty;
        public string DeptName { get; set; } = string.Empty;
        public string RelationType { get; set; } = "PRIMARY";
    }

    public sealed class InpatientDeptChoice
    {
        public InpatientDeptChoice(InpatientDeptDto dto)
        {
            Id = dto.Id;
            DeptCode = dto.DeptCode;
            Name = dto.DeptName;
            Wards = dto.Wards.Select(x => new InpatientWardChoice(x)).ToList();
        }
        public string Id { get; }
        public string DeptCode { get; }
        public string Name { get; }
        public IReadOnlyList<InpatientWardChoice> Wards { get; }
        public override string ToString() => Name;
    }

    public sealed class InpatientWardChoice
    {
        public InpatientWardChoice(InpatientWardDto dto)
        {
            Id = dto.Id;
            DeptCode = dto.DeptCode;
            Name = dto.DeptName;
            RelationType = string.IsNullOrWhiteSpace(dto.RelationType) ? "PRIMARY" : dto.RelationType;
        }
        public string Id { get; }
        public string DeptCode { get; }
        public string Name { get; }
        public string RelationType { get; }
        public bool IsBorrowed => RelationType.Equals("BORROW_ALLOWED", StringComparison.OrdinalIgnoreCase);
        public override string ToString() => IsBorrowed ? $"{Name} (借床)" : Name;
    }

    public sealed record PrepaymentChannel(string Code, string Name);

    public sealed class AdmissionCardItem : ClientModelBase
    {
        private bool _isSelected;
        public AdmissionCardItem(AdmissionCardDto dto)
        {
            Id = dto.Id;
            CardNo = dto.CardNo;
            PatientId = dto.PatientId;
            PatientName = string.IsNullOrWhiteSpace(dto.PatientName) ? dto.PatientId : dto.PatientName;
            GenderLabel = dto.GenderName;
            MedicalRecordNo = dto.MedicalRecordNo;
            SourceDoctorName = string.IsNullOrWhiteSpace(dto.SourceDoctorName) ? "--" : dto.SourceDoctorName;
            SuggestedIpdDeptId = dto.SuggestedIpdDeptId;
            SuggestedIpdDeptName = string.IsNullOrWhiteSpace(dto.SuggestedIpdDeptName) ? "--" : dto.SuggestedIpdDeptName;
            SuggestedWardId = dto.SuggestedWardId;
            SuggestedWardName = dto.SuggestedWardName;
            AdmissionDiagnosis = dto.AdmissionDiagnosis;
            ContactPhone = dto.ContactPhone;
            Remark = dto.Remark;
            Status = dto.Status;
            EncounterId = dto.EncounterId;
            EncounterNo = dto.EncounterNo;
            PrepaymentBalance = dto.PrepaymentBalance;
            BedCode = dto.BedCode;
            TriageStatus = dto.TriageStatus;
            CreatedAtMs = dto.CreatedAtMs;
        }
        public string Id { get; }
        public string CardNo { get; }
        public string PatientId { get; }
        public string PatientName { get; }
        public string GenderLabel { get; }
        public string MedicalRecordNo { get; }
        public string SourceDoctorName { get; }
        public string SuggestedIpdDeptId { get; }
        public string SuggestedIpdDeptName { get; }
        public string? SuggestedWardId { get; }
        public string SuggestedWardName { get; }
        public string? AdmissionDiagnosis { get; }
        public string? ContactPhone { get; }
        public string? Remark { get; }
        public string Status { get; }
        public string? EncounterId { get; }
        public string EncounterNo { get; }
        public decimal PrepaymentBalance { get; }
        public string BedCode { get; }
        public int TriageStatus { get; }
        public long CreatedAtMs { get; }
        public bool IsPending => Status.Equals("PENDING", StringComparison.OrdinalIgnoreCase);
        public bool IsRegistered => Status.Equals("REGISTERED", StringComparison.OrdinalIgnoreCase);
        public string StatusLabel => Status switch
        {
            "PENDING" => "待登记",
            "REGISTERED" => "已登记",
            "CANCELLED" => "已取消",
            _ => Status
        };
        public string CreatedAtText => CreatedAtMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtMs).ToOffset(TimeSpan.FromHours(8)).ToString("MM-dd HH:mm")
            : "--";
        public string PatientLine => MedicalRecordNo + "  " + GenderLabel;
        public string SuggestedLine => "建议 " + SuggestedIpdDeptName + (string.IsNullOrWhiteSpace(SuggestedWardName) ? string.Empty : "  " + SuggestedWardName);
        public string RegistrationLine => EncounterNo.Length > 0 ? "住院号 " + EncounterNo : "待收费处登记";
        public IBrush StatusForeground => new ImmutableSolidColorBrush(Color.Parse(IsPending ? "#B54708" : "#067647"));
        public IBrush CardBackground => new ImmutableSolidColorBrush(Color.Parse(IsSelected ? "#EAF3FF" : "#FFFFFF"));
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
    }

    public sealed class InpatientBedItem
    {
        public InpatientBedItem(InpatientBedDto dto)
        {
            BedCode = dto.BedCode;
            RoomCode = dto.RoomCode;
            BedStatus = dto.BedStatus;
            HasEncounter = !string.IsNullOrWhiteSpace(dto.CurrentEncounterId);
        }
        public string BedCode { get; }
        public string RoomCode { get; }
        public int BedStatus { get; }
        public bool HasEncounter { get; }
        public bool IsAvailable => BedStatus == 0 && !HasEncounter;
        public string Label => RoomCode + " 床 " + BedCode;
        public string StateLabel => IsAvailable ? "可入住" : "已占用";
    }

    public sealed class InpatientRecordItem : ClientModelBase
    {
        private bool _isSelected;
        public InpatientRecordItem(IpdEncounterDto dto)
        {
            Encounter = dto;
        }
        public IpdEncounterDto Encounter { get; }
        public string Id => Encounter.Id;
        public string PatientName => Encounter.PatientName;
        public string AdmissionNo => Encounter.AdmissionNo;
        public string BedLine => Encounter.WardName + " 床位 " + Encounter.BedCode;
        public string BalanceText => "余额 ¥" + Encounter.PrepaymentBalance.ToString("N2");
        public string TriageText => Encounter.TriageStatus == 1 ? "已接诊" : "待病区接诊";
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
        public IBrush CardBackground => new ImmutableSolidColorBrush(Color.Parse(IsSelected ? "#EAF3FF" : "#FFFFFF"));
    }

    public sealed class PrepaymentRecordItem
    {
        public PrepaymentRecordItem(PrepaymentDto dto)
        {
            PrepaymentNo = dto.PrepaymentNo;
            Amount = dto.Amount;
            BalanceAfter = dto.BalanceAfter;
            PaymentChannel = dto.PaymentChannel;
            CashierName = dto.CashierName;
            PaidAtMs = dto.PaidAtMs;
            Remark = dto.Remark ?? string.Empty;
        }
        public string PrepaymentNo { get; }
        public decimal Amount { get; }
        public decimal BalanceAfter { get; }
        public string PaymentChannel { get; }
        public string CashierName { get; }
        public long? PaidAtMs { get; }
        public string Remark { get; }
        public string AmountText => "¥" + Amount.ToString("N2");
        public string BalanceText => "余额 ¥" + BalanceAfter.ToString("N2");
        public string PaymentChannelLabel => PaymentChannel switch
        {
            "CASH" => "现金",
            "WECHAT" => "微信",
            "ALIPAY" => "支付宝",
            "BANK_CARD" => "银行卡",
            "INSURANCE" => "医保",
            _ => PaymentChannel
        };
        public string PaidAtText => PaidAtMs is long ms && ms > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms).ToOffset(TimeSpan.FromHours(8)).ToString("MM-dd HH:mm")
            : "--";
    }
}
