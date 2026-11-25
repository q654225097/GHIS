using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Services;

namespace HIS.Client.UrsaShell.ViewModels;

public sealed partial class RegistrationCashierViewModel : ClientModelBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly List<RegistrationPatientDto> _allPatients = new();
    private string _patientKeyword = string.Empty;
    private RegistrationPatientItem? _selectedPatient;
    private string _queryDate;
    private string _departmentId = string.Empty;
    private RegistrationDepartmentChoice? _selectedDepartment;
    private string _doctorId = string.Empty;
    private RegistrationSlotItem? _selectedSlot;
    private PaymentChannelChoice? _selectedPaymentChannel;
    private string _registrationKeyword = string.Empty;
    private RegistrationRecordItem? _selectedRegistration;
    private string _refundReason = string.Empty;
    private bool _isBusy;
    private bool _isLoaded;
    private string _notice = "选择患者与今日可用号源，确认收款后生成挂号及缴费流水。";
    private string _errorMessage = string.Empty;
    private bool _isPatientSearchEmpty;
    private bool _isSlotListEmpty;
    private bool _isRegistrationListEmpty;

    // Patient registration form fields
    private bool _isPatientFormVisible;
    private string _newPatientName = string.Empty;
    private string _newPatientGender = "1";
    private string _newPatientBirthDate = string.Empty;
    private string _newPatientIdCard = string.Empty;
    private string _newPatientPhone = string.Empty;
    private string _newPatientGuardianName = string.Empty;
    private string _newPatientGuardianIdCard = string.Empty;
    private string _newPatientAllergy = string.Empty;
    private string _patientFormNotice = string.Empty;
    private string _patientFormError = string.Empty;

    public RegistrationCashierViewModel()
    {
        _queryDate = DateTime.Today.ToString("yyyy-MM-dd");
        PaymentChannels.Add(new PaymentChannelChoice("CASH", "现金"));
        PaymentChannels.Add(new PaymentChannelChoice("WECHAT", "微信"));
        PaymentChannels.Add(new PaymentChannelChoice("ALIPAY", "支付宝"));
        PaymentChannels.Add(new PaymentChannelChoice("MEDICAL_INSURANCE", "医保"));
        _selectedPaymentChannel = PaymentChannels[0];
    }

    public ObservableCollection<RegistrationPatientItem> Patients { get; } = new();
    public ObservableCollection<RegistrationDepartmentChoice> Departments { get; } = new();
    public ObservableCollection<RegistrationDoctorChoice> Doctors { get; } = new();
    public ObservableCollection<RegistrationSlotItem> Slots { get; } = new();
    public ObservableCollection<PaymentChannelChoice> PaymentChannels { get; } = new();
    public ObservableCollection<RegistrationRecordItem> Registrations { get; } = new();

    public string PatientKeyword
    {
        get => _patientKeyword;
        set
        {
            if (SetField(ref _patientKeyword, value ?? string.Empty))
            {
                ApplyPatientFilter();
            }
        }
    }

    public RegistrationPatientItem? SelectedPatient
    {
        get => _selectedPatient;
        set
        {
            if (SetField(ref _selectedPatient, value))
            {
                OnPropertyChanged(nameof(HasSelectedPatient));
                OnPropertyChanged(nameof(PatientDisplayName));
                OnPropertyChanged(nameof(PatientDemographics));
                OnPropertyChanged(nameof(PatientRecordNo));
                OnPropertyChanged(nameof(PatientPhone));
                OnPropertyChanged(nameof(PatientAllergy));
                OnPropertyChanged(nameof(HasPatientAllergy));
                OnPropertyChanged(nameof(CanSettle));
                OnPropertyChanged(nameof(SelectedPatientIsEmergency));
                OnPropertyChanged(nameof(PatientSelectionHint));
            }
        }
    }

    public string QueryDate
    {
        get => _queryDate;
        set
        {
            if (SetField(ref _queryDate, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(QueryDateDateTime));
            }
        }
    }

    public DateTime? QueryDateDateTime
    {
        get => DateTime.TryParse(QueryDate, out var date) ? date.Date : DateTime.Today;
        set
        {
            var next = (value ?? DateTime.Today).ToString("yyyy-MM-dd");
            if (QueryDate != next)
            {
                QueryDate = next;
            }
        }
    }

    public DateTimeOffset QueryDateValue
    {
        get => DateTime.TryParse(QueryDate, out var date)
            ? new DateTimeOffset(date.Date)
            : new DateTimeOffset(DateTime.Today);
        set
        {
            var next = value.ToString("yyyy-MM-dd");
            if (QueryDate != next)
            {
                QueryDate = next;
            }
        }
    }

    public string DepartmentId
    {
        get => _departmentId;
        set
        {
            if (SetField(ref _departmentId, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(SelectedDepartmentName));
            }
        }
    }

    public RegistrationDepartmentChoice? SelectedDepartment
    {
        get => _selectedDepartment;
        set
        {
            if (!SetField(ref _selectedDepartment, value))
            {
                return;
            }
            DepartmentId = value?.Id ?? string.Empty;
        }
    }

    public string DoctorId
    {
        get => _doctorId;
        set => SetField(ref _doctorId, value ?? string.Empty);
    }

    public RegistrationSlotItem? SelectedSlot
    {
        get => _selectedSlot;
        set
        {
            if (SetField(ref _selectedSlot, value))
            {
                OnPropertyChanged(nameof(SelectedSlotSummary));
                OnPropertyChanged(nameof(SelectedSlotTime));
                OnPropertyChanged(nameof(SelectedSlotDepartment));
                OnPropertyChanged(nameof(SelectedSlotDoctor));
                OnPropertyChanged(nameof(SelectedSlotRoom));
                OnPropertyChanged(nameof(SelectedSlotClinicLine));
                OnPropertyChanged(nameof(SelectedSlotLevel));
                OnPropertyChanged(nameof(SelectedSlotFee));
                OnPropertyChanged(nameof(SelectedSlotAvailability));
                OnPropertyChanged(nameof(SelectedSlotPercentText));
                OnPropertyChanged(nameof(SelectedSlotPercent));
                OnPropertyChanged(nameof(CanSettle));
                OnPropertyChanged(nameof(SelectedSlotHint));
            }
        }
    }

    public PaymentChannelChoice? SelectedPaymentChannel
    {
        get => _selectedPaymentChannel;
        set => SetField(ref _selectedPaymentChannel, value);
    }

    public string RegistrationKeyword
    {
        get => _registrationKeyword;
        set
        {
            if (SetField(ref _registrationKeyword, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasRegistrationKeyword));
                OnPropertyChanged(nameof(NotifyRegistrationFilteredEmpty));
                OnPropertyChanged(nameof(NotifyRegistrationDayEmpty));
            }
        }
    }

    public RegistrationRecordItem? SelectedRegistration
    {
        get => _selectedRegistration;
        set
        {
            if (SetField(ref _selectedRegistration, value))
            {
                OnPropertyChanged(nameof(CanRefundSelected));
                OnPropertyChanged(nameof(SelectedRegistrationHint));
                OnPropertyChanged(nameof(HasSelectedRegistration));
                OnPropertyChanged(nameof(SelectedRegistrationPatientLine));
                OnPropertyChanged(nameof(SelectedRegistrationClinicLine));
                OnPropertyChanged(nameof(SelectedRegistrationAmountLine));
            }
        }
    }

    public string RefundReason
    {
        get => _refundReason;
        set => SetField(ref _refundReason, value ?? string.Empty);
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
                OnPropertyChanged(nameof(CanRefundSelected));
                OnPropertyChanged(nameof(NotifyPatientEmpty));
                OnPropertyChanged(nameof(NotifySlotEmpty));
                OnPropertyChanged(nameof(NotifyRegistrationEmpty));
                OnPropertyChanged(nameof(NotifyRegistrationFilteredEmpty));
                OnPropertyChanged(nameof(NotifyRegistrationDayEmpty));
            }
        }
    }

    public bool IsNotBusy => !IsBusy;
    public string Notice { get => _notice; private set => SetField(ref _notice, value); }
    public string ErrorMessage { get => _errorMessage; private set => SetField(ref _errorMessage, value); }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasSelectedPatient => SelectedPatient != null;
    public string PatientDisplayName => SelectedPatient?.DisplayName ?? "尚未选择患者";
    public string PatientDemographics => SelectedPatient == null
        ? "姓名 · 性别 · 年龄"
        : $"{SelectedPatient.GenderLabel} · {SelectedPatient.AgeLabel}";
    public string PatientRecordNo => SelectedPatient == null
        ? "病历号 --"
        : "病历号 " + SelectedPatient.MedicalRecordNo;
    public string PatientPhone => SelectedPatient?.PhoneMasked ?? "联系电话 --";
    public string PatientAllergy => SelectedPatient == null || SelectedPatient.AllergyText.Length == 0
        ? string.Empty
        : "过敏 " + SelectedPatient.AllergyText;
    public bool HasPatientAllergy => SelectedPatient != null && SelectedPatient.AllergyText.Length > 0;
    public bool SelectedPatientIsEmergency => SelectedPatient?.IsTempEmergency == 1;
    public string PatientSelectionHint => SelectedPatient == null ? "请先选择患者" : "患者已选择";

    public bool IsPatientSearchEmpty
    {
        get => _isPatientSearchEmpty;
        private set => SetField(ref _isPatientSearchEmpty, value);
    }

    public bool NotifyPatientEmpty => IsPatientSearchEmpty && IsNotBusy && Patients.Count == 0;

    public bool IsSlotListEmpty
    {
        get => _isSlotListEmpty;
        private set => SetField(ref _isSlotListEmpty, value);
    }

    public bool NotifySlotEmpty => IsSlotListEmpty && IsNotBusy && Slots.Count == 0;

    public bool IsRegistrationListEmpty
    {
        get => _isRegistrationListEmpty;
        private set => SetField(ref _isRegistrationListEmpty, value);
    }

    public bool NotifyRegistrationEmpty => IsRegistrationListEmpty && IsNotBusy && Registrations.Count == 0;
    public bool NotifyRegistrationFilteredEmpty => HasRegistrationKeyword && NotifyRegistrationEmpty;
    public bool NotifyRegistrationDayEmpty => !HasRegistrationKeyword && NotifyRegistrationEmpty;

    // ── Patient registration form ──

    public bool IsPatientFormVisible
    {
        get => _isPatientFormVisible;
        set
        {
            if (SetField(ref _isPatientFormVisible, value))
            {
                OnPropertyChanged(nameof(IsPatientListVisible));
            }
        }
    }

    public bool IsPatientListVisible => !IsPatientFormVisible;

    public string NewPatientName
    {
        get => _newPatientName;
        set => SetField(ref _newPatientName, value ?? string.Empty);
    }

    public string NewPatientGender
    {
        get => _newPatientGender;
        set => SetField(ref _newPatientGender, value ?? "1");
    }

    public string NewPatientBirthDate
    {
        get => _newPatientBirthDate;
        set => SetField(ref _newPatientBirthDate, value ?? string.Empty);
    }

    public string NewPatientIdCard
    {
        get => _newPatientIdCard;
        set => SetField(ref _newPatientIdCard, value ?? string.Empty);
    }

    public string NewPatientPhone
    {
        get => _newPatientPhone;
        set => SetField(ref _newPatientPhone, value ?? string.Empty);
    }

    public string NewPatientGuardianName
    {
        get => _newPatientGuardianName;
        set => SetField(ref _newPatientGuardianName, value ?? string.Empty);
    }

    public string NewPatientGuardianIdCard
    {
        get => _newPatientGuardianIdCard;
        set => SetField(ref _newPatientGuardianIdCard, value ?? string.Empty);
    }

    public string NewPatientAllergy
    {
        get => _newPatientAllergy;
        set => SetField(ref _newPatientAllergy, value ?? string.Empty);
    }

    public string PatientFormNotice
    {
        get => _patientFormNotice;
        set => SetField(ref _patientFormNotice, value ?? string.Empty);
    }

    public string PatientFormError
    {
        get => _patientFormError;
        set
        {
            if (SetField(ref _patientFormError, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasPatientFormError));
            }
        }
    }

    public bool HasPatientFormError => !string.IsNullOrWhiteSpace(PatientFormError);
    public bool HasPatientFormNotice => !string.IsNullOrWhiteSpace(PatientFormNotice);

    public ObservableCollection<GenderChoice> GenderChoices { get; } = new()
    {
        new GenderChoice("1", "男"),
        new GenderChoice("2", "女"),
        new GenderChoice("0", "未知")
    };

    public sealed record GenderChoice(string Code, string Name);

    public void ShowPatientForm()
    {
        ResetPatientForm();
        IsPatientFormVisible = true;
    }

    public void HidePatientForm()
    {
        IsPatientFormVisible = false;
        PatientFormError = string.Empty;
        PatientFormNotice = string.Empty;
    }

    public void ResetPatientForm()
    {
        NewPatientName = string.Empty;
        NewPatientGender = "1";
        NewPatientBirthDate = string.Empty;
        NewPatientIdCard = string.Empty;
        NewPatientPhone = string.Empty;
        NewPatientGuardianName = string.Empty;
        NewPatientGuardianIdCard = string.Empty;
        NewPatientAllergy = string.Empty;
        PatientFormError = string.Empty;
        PatientFormNotice = string.Empty;
    }

    public async Task<string?> RegisterNewPatientAsync()
    {
        if (IsBusy)
        {
            return null;
        }

        // Client-side validation
        if (string.IsNullOrWhiteSpace(NewPatientName))
        {
            PatientFormError = "请填写患者姓名。";
            return null;
        }
        if (string.IsNullOrWhiteSpace(NewPatientIdCard))
        {
            PatientFormError = "请填写身份证号码。";
            return null;
        }
        if (string.IsNullOrWhiteSpace(NewPatientPhone))
        {
            PatientFormError = "请填写联系电话。";
            return null;
        }
        if (string.IsNullOrWhiteSpace(NewPatientBirthDate))
        {
            PatientFormError = "请填写出生日期。";
            return null;
        }

        if (!int.TryParse(NewPatientBirthDate.Trim().Replace("-", "").Replace("/", ""), out var birthDateInt)
            || birthDateInt < 19000101 || birthDateInt > 20991231)
        {
            PatientFormError = "出生日期格式无效，请输入 yyyyMMdd 或 yyyy-MM-dd。";
            return null;
        }

        // Parse allergy tags
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
        try
        {
            using var http = CreateHttpClient();
            using var response = await http.PostAsJsonAsync(
                "/api/v4/patients/register",
                new
                {
                    FullName = NewPatientName.Trim(),
                    GenderCode = NewPatientGender,
                    BirthDateInt = birthDateInt,
                    IdCardNo = NewPatientIdCard.Trim(),
                    Phone = NewPatientPhone.Trim(),
                    GuardianName = string.IsNullOrWhiteSpace(NewPatientGuardianName) ? (string?)null : NewPatientGuardianName.Trim(),
                    GuardianIdCardNo = string.IsNullOrWhiteSpace(NewPatientGuardianIdCard) ? (string?)null : NewPatientGuardianIdCard.Trim(),
                    AllergyTags = allergyTags
                },
                JsonOptions);

            var envelope = await ReadEnvelopeAsync<RegisteredPatientDto>(response);

            if (!response.IsSuccessStatusCode || envelope.Code is not 200 and not 201)
            {
                // 409 means duplicate – show the existing MRN
                PatientFormError = string.IsNullOrWhiteSpace(envelope.Message)
                    ? $"建档失败 (HTTP {(int)response.StatusCode})"
                    : envelope.Message;
                return null;
            }

            var newPatient = envelope.Data;
            if (newPatient == null)
            {
                PatientFormError = "建档响应异常，未返回患者信息。";
                return null;
            }

            // Refresh patient list and auto-select
            PatientKeyword = newPatient.MedicalRecordNo ?? NewPatientName.Trim();
            await SearchPatientsCoreAsync(PatientKeyword);
            var matchItem = Patients.FirstOrDefault(p => p.Id == newPatient.Id);
            if (matchItem != null)
            {
                await SelectPatientAsync(matchItem);
            }

            HidePatientForm();
            ClearError();
            var mrn = newPatient.MedicalRecordNo ?? "--";
            var fullName = newPatient.FullName ?? NewPatientName.Trim();
            Notice = $"实名建档成功：{fullName}（{mrn}），请继续选择号源完成挂号。";
            ClientDiagnosticsLog.Info("patient.register", $"Registered {fullName} as {mrn}.");
            return $"建档成功 · {mrn}";
        }
        catch (Exception ex)
        {
            PatientFormError = "建档请求失败：" + ex.Message;
            ClientDiagnosticsLog.Error("patient.register", "Patient registration failed", ex);
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<string?> CreateEmergencyPatientAsync()
    {
        if (IsBusy)
        {
            return null;
        }

        IsBusy = true;
        PatientFormError = string.Empty;
        try
        {
            using var http = CreateHttpClient();
            using var response = await http.PostAsync("/api/v4/patients/emergency-temp", null);
            var envelope = await ReadEnvelopeAsync<RegisteredPatientDto>(response);

            if (!response.IsSuccessStatusCode || envelope.Code is not 200 and not 201)
            {
                var msg = string.IsNullOrWhiteSpace(envelope.Message)
                    ? $"急诊建档失败 (HTTP {(int)response.StatusCode})"
                    : envelope.Message;
                PatientFormError = msg;
                Notice = msg;
                return null;
            }

            var newPatient = envelope.Data;
            if (newPatient == null)
            {
                PatientFormError = "急诊建档响应异常。";
                return null;
            }

            // Refresh patient list and auto-select
            PatientKeyword = newPatient.MedicalRecordNo ?? string.Empty;
            await SearchPatientsCoreAsync(PatientKeyword);
            var matchItem = Patients.FirstOrDefault(p => p.Id == newPatient.Id);
            if (matchItem != null)
            {
                await SelectPatientAsync(matchItem);
            }

            HidePatientForm();
            ClearError();
            var mrn = newPatient.MedicalRecordNo ?? "--";
            Notice = $"急诊三无临时建档成功：{mrn}，后续可归并至正式实名档案。请继续选择号源。";
            ClientDiagnosticsLog.Info("patient.emergency", $"Created emergency temp patient {mrn}.");
            return $"急诊建档 · {mrn}";
        }
        catch (Exception ex)
        {
            PatientFormError = "急诊建档失败：" + ex.Message;
            ClientDiagnosticsLog.Error("patient.emergency", "Emergency patient creation failed", ex);
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private sealed class RegisteredPatientDto
    {
        public string Id { get; set; } = string.Empty;
        public string MedicalRecordNo { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string GenderCode { get; set; } = string.Empty;
    }

    // ── End Patient registration form ──

    public string PatientCountText => $"{Patients.Count} 位患者";
    public string SlotCountText => $"{Slots.Count} 个可挂号源";
    public string RegistrationCountText => $"今日挂号 {Registrations.Count} 条";
    public string TodayRevenueText => "¥" + Registrations
        .Where(x => x.Status == 1 || x.Status == 2)
        .Sum(x => x.PaidAmount).ToString("N2");
    public string RefundedAmountText => "¥" + Registrations
        .Where(x => x.Status == 3)
        .Sum(x => x.PaidAmount).ToString("N2");
    public string EffectiveCountText => Registrations.Count(x => x.Status == 1 || x.Status == 2).ToString();
    public string RefundedCountText => Registrations.Count(x => x.Status == 3).ToString();
    public string SelectedDepartmentName => Departments
        .FirstOrDefault(x => x.Id == DepartmentId)?.Name ?? "全部门诊科室";
    public bool HasRegistrationKeyword => !string.IsNullOrWhiteSpace(RegistrationKeyword);

    public string SelectedSlotSummary => SelectedSlot == null
        ? "尚未选择号源"
        : $"{SelectedSlot.DeptName} · {SelectedSlot.DoctorName}";
    public string SelectedSlotTime => SelectedSlot?.TimeText ?? "--:-- - --:--";
    public string SelectedSlotDepartment => SelectedSlot?.DeptName ?? "--";
    public string SelectedSlotDoctor => SelectedSlot?.DoctorName ?? "--";
    public string SelectedSlotRoom => SelectedSlot?.RoomDisplay ?? "--";
    public string SelectedSlotClinicLine => SelectedSlot == null
        ? "科室 -- · 医生 -- · 诊室 --"
        : "科室 " + SelectedSlot.DeptName + " · 医生 " + SelectedSlot.DoctorName
            + " · " + SelectedSlot.RoomDisplay;
    public string SelectedSlotLevel => SelectedSlot?.LevelLabel ?? "--";
    public string SelectedSlotFee => SelectedSlot == null ? "¥0.00" : "¥" + SelectedSlot.RegistrationFee.ToString("N2");
    public string SelectedSlotAvailability => SelectedSlot?.AvailabilityText ?? "未选择号源";
    public string SelectedSlotPercentText => SelectedSlot == null ? "请选择号源" : SelectedSlot.OccupancyText;
    public double SelectedSlotPercent => SelectedSlot?.OccupancyPercent ?? 0;
    public bool CanSettle => IsNotBusy && SelectedPatient != null && SelectedSlot != null
        && SelectedPaymentChannel != null;
    public string SelectedSlotHint => SelectedSlot == null ? "请选择可用号源" : "号源已选择";

    public bool CanRefundSelected => IsNotBusy && SelectedRegistration?.CanRefund == true;
    public string SelectedRegistrationHint => SelectedRegistration == null
        ? "选择挂号记录后可办理退号"
        : SelectedRegistration.RefundHint;
    public bool HasSelectedRegistration => SelectedRegistration != null;
    public string SelectedRegistrationPatientLine => SelectedRegistration == null
        ? "选择挂号记录后办理退费"
        : SelectedRegistration.PatientLine;
    public string SelectedRegistrationClinicLine => SelectedRegistration == null
        ? "姓名 · 病历号 · 科室 · 收费方式"
        : SelectedRegistration.ClinicLine + " · " + SelectedRegistration.PaymentChannelLabel;
    public string SelectedRegistrationAmountLine => SelectedRegistration == null
        ? "收款金额 --"
        : "收款金额 " + SelectedRegistration.PaidAmountText + " · "
            + SelectedRegistration.PaidTimeText + " · " + SelectedRegistration.StatusLabel;

    public async Task EnsureLoadedAsync()
    {
        if (_isLoaded || IsBusy)
        {
            return;
        }
        try
        {
            await LoadDepartmentsAsync();
            await SearchPatientsCoreAsync(PatientKeyword);
            await LoadSlotsAsync();
            await LoadRegistrationsAsync();
            _isLoaded = true;
        }
        catch (Exception ex)
        {
            SetException("门诊挂号收费初始化失败", ex);
        }
    }

    public async Task SearchPatientsAsync()
    {
        if (IsBusy)
        {
            return;
        }
        try
        {
            await SearchPatientsCoreAsync(PatientKeyword);
            ClearError();
            Notice = Patients.Count == 0
                ? "未找到匹配患者，请核对姓名、病历号或拼音。"
                : $"找到 {Patients.Count} 位患者，请选择本次挂号患者。";
        }
        catch (Exception ex)
        {
            SetException("门诊患者检索失败", ex);
        }
    }

    public async Task SelectPatientAsync(RegistrationPatientItem item)
    {
        if (item == null)
        {
            return;
        }
        SelectedPatient = item;
        foreach (var patient in Patients)
        {
            patient.IsSelected = ReferenceEquals(patient, item);
        }
        Notice = $"已选择 {item.FullName}（{item.MedicalRecordNo}）。选择号源后可确认收费。";
        await Task.CompletedTask;
    }

    public async Task SearchSlotsAsync()
    {
        if (IsBusy)
        {
            return;
        }
        try
        {
            await LoadSlotsAsync();
            ClearError();
            Notice = Slots.Count == 0
                ? "所选日期没有余量号源，可调整日期或科室查询。"
                : $"已查询 {QueryDate}，共有 {Slots.Count} 个可挂号源。";
        }
        catch (Exception ex)
        {
            SetException("号源查询失败", ex);
        }
    }

    public async Task SelectSlotAsync(RegistrationSlotItem item)
    {
        if (item == null)
        {
            return;
        }
        SelectedSlot = item;
        foreach (var slot in Slots)
        {
            slot.IsSelected = ReferenceEquals(slot, item);
        }
        Notice = $"已选择 {item.DeptName} · {item.DoctorName}，{item.TimeText}。";
        await Task.CompletedTask;
    }

    public async Task ApplyFiltersAsync()
    {
        await SearchSlotsAsync();
    }

    public async Task LoadRegistrationsAsync()
    {
        if (IsBusy)
        {
            return;
        }
        try
        {
            await LoadRegistrationsCoreAsync();
            ClearError();
        }
        catch (Exception ex)
        {
            SetException("今日挂号记录加载失败", ex);
        }
    }

    public async Task<string?> SettleAsync()
    {
        if (IsBusy)
        {
            return null;
        }
        if (SelectedPatient == null)
        {
            SetValidation("请先选择本次挂号患者。");
            return null;
        }
        if (SelectedSlot == null)
        {
            SetValidation("请先选择可挂号的号源。");
            return null;
        }
        if (SelectedPaymentChannel == null)
        {
            SetValidation("请选择收款方式。");
            return null;
        }

        IsBusy = true;
        ClearError();
        var patient = SelectedPatient;
        var slot = SelectedSlot;
        var channel = SelectedPaymentChannel;
        var requestKey = "OPD-REG-" + Guid.NewGuid().ToString("N");
        try
        {
            using var http = CreateHttpClient();
            using var response = await http.PostAsJsonAsync(
                "/api/v4/opd/registration/registrations/settle",
                new
                {
                    PatientId = patient.Id,
                    ScheduleId = slot.Id,
                    PaymentChannel = channel.Code,
                    RequestKey = requestKey
                },
                JsonOptions);
            var envelope = await ReadEnvelopeAsync<RegistrationRecordDto>(response);
            if (!response.IsSuccessStatusCode || envelope.Code != 200 || envelope.Data == null)
            {
                var registrationFailure = envelope.Message;
                SetFailure(string.IsNullOrWhiteSpace(registrationFailure)
                    ? $"收费失败 (HTTP {(int)response.StatusCode})"
                    : registrationFailure);
                ClientDiagnosticsLog.Warn(
                    "opd.registration",
                    "Registration settlement was rejected: "
                    + (string.IsNullOrWhiteSpace(envelope.Message) ? response.StatusCode.ToString() : envelope.Message));
                return null;
            }

            var record = ToRecord(envelope.Data);
            Registrations.Insert(0, record);
            SelectedRegistration = record;
            PatientKeyword = string.Empty;
            RefundReason = string.Empty;
            await LoadSlotsCoreAsync();
            await LoadRegistrationsCoreAsync(record.Id);
            ClearError();
            Notice = $"{record.PatientName} 挂号收费成功，{record.DeptName}，排队号 {record.QueueNo:D3}，"
                + $"{record.PaymentChannelLabel} {record.PaidAmountText}。";
            ClientDiagnosticsLog.Info(
                "opd.registration",
                $"Settled {record.AppointmentNo} for {record.PatientName}, queue {record.QueueNo}, channel {record.PaymentChannelCode}.");
            return $"已挂号 · 排队号 {record.QueueNo:D3}";
        }
        catch (Exception ex)
        {
            SetException("挂号收费请求失败，请确认服务端连接正常", ex);
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<string?> RefundSelectedAsync()
    {
        if (IsBusy)
        {
            return null;
        }
        var record = SelectedRegistration;
        if (record == null)
        {
            SetValidation("请先在挂号记录中选择需要退费的记录。");
            return null;
        }
        if (!record.CanRefund)
        {
            SetValidation(record.RefundHint);
            return null;
        }

        IsBusy = true;
        ClearError();
        try
        {
            using var http = CreateHttpClient();
            using var response = await http.PostAsJsonAsync(
                $"/api/v4/opd/registration/registrations/{Uri.EscapeDataString(record.Id)}/refund",
                new { Reason = string.IsNullOrWhiteSpace(RefundReason) ? "患者申请退号" : RefundReason.Trim() },
                JsonOptions);
            var envelope = await ReadEnvelopeAsync<RegistrationRecordDto>(response);
            if (!response.IsSuccessStatusCode || envelope.Code != 200 || envelope.Data == null)
            {
                var failure = envelope.Message;
                SetFailure(string.IsNullOrWhiteSpace(failure)
                    ? $"退费失败 (HTTP {(int)response.StatusCode})"
                    : failure);
                ClientDiagnosticsLog.Warn(
                    "opd.registration",
                    $"Registration refund rejected: {failure}");
                return null;
            }

            var updated = ToRecord(envelope.Data);
            RefundReason = string.Empty;
            await LoadSlotsCoreAsync();
            await LoadRegistrationsCoreAsync(updated.Id);
            ClearError();
            Notice = $"{updated.PatientName} 的挂号已退费，退款 {updated.PaidAmountText}，号源已释放。";
            ClientDiagnosticsLog.Info(
                "opd.registration",
                $"Refunded {updated.AppointmentNo}, amount {updated.PaidAmount}.");
            return "退费成功";
        }
        catch (Exception ex)
        {
            SetException("挂号退费请求失败，请确认服务端连接正常", ex);
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadDepartmentsAsync()
    {
        using var http = CreateHttpClient();
        using var response = await http.GetAsync("/api/v4/platform/departments?category=OPD");
        var envelope = await ReadEnvelopeAsync<List<RegistrationDepartmentDto>>(response);
        if (!response.IsSuccessStatusCode || envelope.Code != 200)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(envelope.Message)
                ? "门诊科室读取失败"
                : envelope.Message);
        }

        Departments.Clear();
        Departments.Add(new RegistrationDepartmentChoice(string.Empty, "全部门诊科室"));
        foreach (var department in envelope.Data ?? new List<RegistrationDepartmentDto>())
        {
            Departments.Add(new RegistrationDepartmentChoice(department.Id,
                string.IsNullOrWhiteSpace(department.DeptName) ? department.Id : department.DeptName));
        }
        OnPropertyChanged(nameof(SelectedDepartmentName));
    }

    private async Task SearchPatientsCoreAsync(string keyword)
    {
        using var http = CreateHttpClient();
        var query = "/api/v4/opd/registration/patients?keyword=" + Uri.EscapeDataString(keyword?.Trim() ?? string.Empty);
        using var response = await http.GetAsync(query);
        var envelope = await ReadEnvelopeAsync<List<RegistrationPatientDto>>(response);
        if (!response.IsSuccessStatusCode || envelope.Code != 200)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(envelope.Message)
                ? $"患者检索失败 (HTTP {(int)response.StatusCode})"
                : envelope.Message);
        }

        _allPatients.Clear();
        _allPatients.AddRange(envelope.Data ?? new List<RegistrationPatientDto>());
        ApplyPatientFilter();
    }

    private void ApplyPatientFilter()
    {
        var query = PatientKeyword.Trim();
        var selectedId = SelectedPatient?.Id;
        Patients.Clear();
        foreach (var dto in _allPatients.Take(60))
        {
            var item = new RegistrationPatientItem(dto);
            item.IsSelected = item.Id == selectedId;
            Patients.Add(item);
        }
        IsPatientSearchEmpty = Patients.Count == 0;
        OnPropertyChanged(nameof(NotifyPatientEmpty));
        OnPropertyChanged(nameof(PatientCountText));
    }

    private async Task LoadSlotsAsync()
    {
        await LoadSlotsCoreAsync();
    }

    private async Task LoadSlotsCoreAsync()
    {
        if (!DateTime.TryParse(QueryDate, out var date))
        {
            throw new InvalidOperationException("号源日期格式无效，请使用 yyyy-MM-dd");
        }
        using var http = CreateHttpClient();
        var query = "/api/v4/opd/registration/slots?date=" + Uri.EscapeDataString(date.ToString("yyyy-MM-dd"))
            + "&opdDeptId=" + Uri.EscapeDataString(DepartmentId)
            + "&doctorId=" + Uri.EscapeDataString(DoctorId);
        using var response = await http.GetAsync(query);
        var envelope = await ReadEnvelopeAsync<List<RegistrationSlotDto>>(response);
        if (!response.IsSuccessStatusCode || envelope.Code != 200)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(envelope.Message)
                ? $"号源查询失败 (HTTP {(int)response.StatusCode})"
                : envelope.Message);
        }

        var selectedId = SelectedSlot?.Id;
        Slots.Clear();
        foreach (var dto in envelope.Data ?? new List<RegistrationSlotDto>())
        {
            var item = new RegistrationSlotItem(dto);
            item.IsSelected = item.Id == selectedId;
            Slots.Add(item);
        }
        SelectedSlot = Slots.FirstOrDefault(x => x.Id == selectedId);
        IsSlotListEmpty = Slots.Count == 0;
        OnPropertyChanged(nameof(NotifySlotEmpty));
        OnPropertyChanged(nameof(SlotCountText));
        OnPropertyChanged(nameof(SelectedDepartmentName));
    }

    private async Task LoadRegistrationsCoreAsync(string? selectId = null)
    {
        using var http = CreateHttpClient();
        var query = "/api/v4/opd/registration/registrations?date=" + Uri.EscapeDataString(QueryDate)
            + "&keyword=" + Uri.EscapeDataString(RegistrationKeyword.Trim());
        using var response = await http.GetAsync(query);
        var envelope = await ReadEnvelopeAsync<List<RegistrationRecordDto>>(response);
        if (!response.IsSuccessStatusCode || envelope.Code != 200)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(envelope.Message)
                ? $"挂号记录读取失败 (HTTP {(int)response.StatusCode})"
                : envelope.Message);
        }

        var keepId = selectId ?? SelectedRegistration?.Id;
        Registrations.Clear();
        foreach (var dto in envelope.Data ?? new List<RegistrationRecordDto>())
        {
            Registrations.Add(ToRecord(dto));
        }
        SelectedRegistration = Registrations.FirstOrDefault(x => x.Id == keepId);
        foreach (var registration in Registrations)
        {
            registration.IsSelected = ReferenceEquals(registration, SelectedRegistration);
        }
        IsRegistrationListEmpty = Registrations.Count == 0;
        OnPropertyChanged(nameof(NotifyRegistrationEmpty));
        OnPropertyChanged(nameof(NotifyRegistrationFilteredEmpty));
        OnPropertyChanged(nameof(NotifyRegistrationDayEmpty));
        NotifyRegistrationTotals();
    }

    private void NotifyRegistrationTotals()
    {
        OnPropertyChanged(nameof(RegistrationCountText));
        OnPropertyChanged(nameof(TodayRevenueText));
        OnPropertyChanged(nameof(RefundedAmountText));
        OnPropertyChanged(nameof(EffectiveCountText));
        OnPropertyChanged(nameof(RefundedCountText));
    }

    private void SetValidation(string message)
    {
        Notice = message;
        ErrorMessage = message;
        OnPropertyChanged(nameof(HasError));
    }

    private void SetFailure(string message)
    {
        ErrorMessage = message;
        Notice = message;
        OnPropertyChanged(nameof(HasError));
    }

    private void SetException(string context, Exception ex)
    {
        ErrorMessage = context + "：" + ex.Message;
        Notice = context + "，请检查服务端连接。";
        OnPropertyChanged(nameof(HasError));
        ClientDiagnosticsLog.Error("opd.registration", context, ex);
    }

    private void ClearError()
    {
        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(HasError));
    }

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { BaseAddress = new Uri(ApiEndpoints.BaseAddress) };
        if (!string.IsNullOrWhiteSpace(SessionContext.Current.AccessToken))
        {
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", SessionContext.Current.AccessToken);
        }
        return http;
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

    private static RegistrationRecordItem ToRecord(RegistrationRecordDto dto)
    {
        return new RegistrationRecordItem(dto);
    }

    public sealed record RegistrationDepartmentChoice(string Id, string Name);
    public sealed record RegistrationDoctorChoice(string Id, string Name);
    public sealed record PaymentChannelChoice(string Code, string Name);

    private sealed class RegistrationDepartmentDto
    {
        public string Id { get; set; } = string.Empty;
        public string DeptName { get; set; } = string.Empty;
    }

    public sealed class RegistrationPatientDto
    {
        public string Id { get; set; } = string.Empty;
        public string MedicalRecordNo { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string GenderCode { get; set; } = string.Empty;
        public int Age { get; set; }
        public string? PhoneMasked { get; set; }
        public int IsTempEmergency { get; set; }
        public string? AllergyTagsJson { get; set; }

        public bool IsServerSearched(string keyword)
        {
            return FullName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || MedicalRecordNo.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class RegistrationSlotDto
    {
        public string Id { get; set; } = string.Empty;
        public int ScheduleDateInt { get; set; }
        public string OpdDeptId { get; set; } = string.Empty;
        public string DeptName { get; set; } = string.Empty;
        public string DoctorId { get; set; } = string.Empty;
        public string DoctorName { get; set; } = string.Empty;
        public string? ClinicRoomId { get; set; }
        public string? RoomName { get; set; }
        public string SlotPeriodCode { get; set; } = string.Empty;
        public long StartsAtMs { get; set; }
        public long EndsAtMs { get; set; }
        public string RegLevelCode { get; set; } = string.Empty;
        public decimal RegistrationFee { get; set; }
        public int TotalCapacity { get; set; }
        public int ReservedCount { get; set; }
        public int SlotStatus { get; set; }
    }

    public sealed class RegistrationRecordDto
    {
        public string Id { get; set; } = string.Empty;
        public string AppointmentNo { get; set; } = string.Empty;
        public string PatientId { get; set; } = string.Empty;
        public string PatientName { get; set; } = string.Empty;
        public string MedicalRecordNo { get; set; } = string.Empty;
        public string GenderCode { get; set; } = string.Empty;
        public string? PhoneMasked { get; set; }
        public string ScheduleId { get; set; } = string.Empty;
        public int ScheduleDateInt { get; set; }
        public string DeptName { get; set; } = string.Empty;
        public string DoctorName { get; set; } = string.Empty;
        public string RegLevelCode { get; set; } = string.Empty;
        public string SlotPeriodCode { get; set; } = string.Empty;
        public long StartsAtMs { get; set; }
        public int QueueNo { get; set; }
        public decimal PaidAmount { get; set; }
        public string? PaymentChannel { get; set; }
        public long? PaidAtMs { get; set; }
        public int Status { get; set; }
        public int? VisitStatus { get; set; }
        public string? VisitId { get; set; }
        public string? PaymentId { get; set; }
        public string? RefundId { get; set; }
        public string? RefundReason { get; set; }
    }

    public static string FormatLocalTime(long? utcMillis)
    {
        if (utcMillis is null or <= 0)
        {
            return "--:--";
        }
        return DateTimeOffset.FromUnixTimeMilliseconds(utcMillis.Value)
            .ToOffset(TimeSpan.FromHours(8))
            .ToString("HH:mm");
    }

    public static string FormatLocalDateTime(long? utcMillis)
    {
        if (utcMillis is null or <= 0)
        {
            return "--";
        }
        return DateTimeOffset.FromUnixTimeMilliseconds(utcMillis.Value)
            .ToOffset(TimeSpan.FromHours(8))
            .ToString("yyyy-MM-dd HH:mm:ss");
    }

    public static string PaymentLabel(string? code) => code?.ToUpperInvariant() switch
    {
        "CASH" => "现金",
        "WECHAT" => "微信",
        "ALIPAY" => "支付宝",
        "MEDICAL_INSURANCE" => "医保",
        _ => string.IsNullOrWhiteSpace(code) ? "--" : code
    };

    public static string PeriodLabel(string? code) => ScheduleWorkstationViewModel.FormatPeriodCode(code);

    public static string LevelLabel(string? code) => code?.ToUpperInvariant() switch
    {
        "NORMAL" => "普通门诊",
        "EXPERT" => "专家门诊",
        "SPECIALIST" => "专科门诊",
        "EMERGENCY" => "急诊",
        _ => string.IsNullOrWhiteSpace(code) ? "普通门诊" : code
    };

    public static IBrush PatientCardBackground(bool selected, bool emergency)
    {
        if (selected)
        {
            return ResolveBrush("SemiColorPrimaryLight", Brushes.Transparent);
        }
        if (emergency)
        {
            return ResolveBrush("SemiColorWarningLight", Brushes.Transparent);
        }
        return Brushes.Transparent;
    }

    internal static IBrush ResolveStatusBrush(string resourceKey)
    {
        return ResolveBrush(resourceKey, Brushes.Transparent);
    }

    private static IBrush ResolveBrush(string resourceKey, IBrush fallback)
    {
        if (Application.Current?.TryGetResource(resourceKey, Application.Current.ActualThemeVariant, out var value) == true
            && value is IBrush brush)
        {
            return brush;
        }
        return fallback;
    }

}

public sealed class RegistrationPatientItem : ClientModelBase
{
    private bool _isSelected;

    public RegistrationPatientItem(RegistrationCashierViewModel.RegistrationPatientDto dto)
    {
        Id = dto.Id;
        FullName = dto.FullName;
        MedicalRecordNo = dto.MedicalRecordNo;
        GenderCode = dto.GenderCode;
        Age = dto.Age;
        PhoneMasked = dto.PhoneMasked;
        IsTempEmergency = dto.IsTempEmergency;
        AllergyText = ParseAllergy(dto.AllergyTagsJson);
    }

    public string Id { get; }
    public string FullName { get; }
    public string MedicalRecordNo { get; }
    public string GenderCode { get; }
    public int Age { get; }
    public string? PhoneMasked { get; }
    public int IsTempEmergency { get; }
    public string AllergyText { get; }
    public string DisplayName => string.IsNullOrWhiteSpace(FullName) ? MedicalRecordNo : FullName;
    public string GenderLabel => GenderCode switch
    {
        "F" => "女",
        "M" => "男",
        _ => string.IsNullOrWhiteSpace(GenderCode) ? "未知" : GenderCode
    };
    public string AgeLabel => Age > 0 ? Age + " 岁" : "年龄未知";
    public string Demographics => $"{GenderLabel} · {AgeLabel}";
    public string Initial => string.IsNullOrWhiteSpace(DisplayName) ? "患" : DisplayName[..1];
    public string PhoneText => string.IsNullOrWhiteSpace(PhoneMasked) ? "未登记电话" : PhoneMasked;
    public string EmergencyLabel => IsTempEmergency == 1 ? "急诊" : "普通";
    public string AllergyShort => string.IsNullOrWhiteSpace(AllergyText) ? "无明确过敏" : AllergyText;
    public IBrush CardBackground => RegistrationCashierViewModel.PatientCardBackground(IsSelected, IsTempEmergency == 1);

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

    private static string ParseAllergy(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
        {
            return string.Empty;
        }
        try
        {
            var options = new System.Text.Json.JsonDocumentOptions { AllowTrailingCommas = true };
            using var document = System.Text.Json.JsonDocument.Parse(json, options);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return string.Empty;
            }
            var values = document.RootElement.EnumerateArray().Select(element =>
                element.ValueKind == System.Text.Json.JsonValueKind.String
                    ? element.GetString()
                    : element.TryGetProperty("name", out var name) ? name.GetString() : null)
                .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()).ToList();
            return string.Join("、", values);
        }
        catch (System.Text.Json.JsonException)
        {
            return string.Empty;
        }
    }
}

public sealed class RegistrationSlotItem : ClientModelBase
{
    private bool _isSelected;

    public RegistrationSlotItem(RegistrationCashierViewModel.RegistrationSlotDto dto)
    {
        Id = dto.Id;
        DeptName = string.IsNullOrWhiteSpace(dto.DeptName) ? dto.OpdDeptId : dto.DeptName;
        DoctorName = string.IsNullOrWhiteSpace(dto.DoctorName) ? dto.DoctorId : dto.DoctorName;
        RoomName = string.IsNullOrWhiteSpace(dto.RoomName) ? "诊室待定" : dto.RoomName;
        SlotPeriodCode = dto.SlotPeriodCode;
        StartsAtMs = dto.StartsAtMs;
        EndsAtMs = dto.EndsAtMs;
        RegLevelCode = dto.RegLevelCode;
        RegistrationFee = dto.RegistrationFee;
        TotalCapacity = Math.Max(0, dto.TotalCapacity);
        ReservedCount = Math.Max(0, dto.ReservedCount);
    }

    public string Id { get; }
    public string DeptName { get; }
    public string DoctorName { get; }
    public string RoomName { get; }
    public string SlotPeriodCode { get; }
    public long StartsAtMs { get; }
    public long EndsAtMs { get; }
    public string RegLevelCode { get; }
    public decimal RegistrationFee { get; }
    public int TotalCapacity { get; }
    public int ReservedCount { get; }
    public string RoomDisplay => RoomName;
    public string LevelLabel => RegistrationCashierViewModel.LevelLabel(RegLevelCode);
    public string FeeText => "¥" + RegistrationFee.ToString("N2");
    public string TimeText => RegistrationCashierViewModel.FormatLocalTime(StartsAtMs)
        + " - " + RegistrationCashierViewModel.FormatLocalTime(EndsAtMs);
    public string PeriodText => RegistrationCashierViewModel.PeriodLabel(SlotPeriodCode) + " " + TimeText;
    public int AvailableCount => Math.Max(0, TotalCapacity - ReservedCount);
    public string AvailabilityText => AvailableCount <= 0
        ? "号源已满"
        : $"余 {AvailableCount} / {TotalCapacity}";
    public double OccupancyPercent => TotalCapacity <= 0
        ? 0
        : Math.Clamp(ReservedCount * 100.0 / TotalCapacity, 0, 100);
    public string OccupancyText => TotalCapacity <= 0
        ? "暂无容量"
        : $"已占用 {ReservedCount} / {TotalCapacity} 个号";
    public IBrush AvailabilityBrush => RegistrationCashierViewModel.ResolveStatusBrush(
        AvailableCount <= 3 ? "SemiColorWarning" : "SemiColorSuccess");
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }
}

public sealed class RegistrationRecordItem : ClientModelBase
{
    private bool _isSelected;

    public RegistrationRecordItem(RegistrationCashierViewModel.RegistrationRecordDto dto)
    {
        Id = dto.Id;
        AppointmentNo = dto.AppointmentNo;
        PatientName = string.IsNullOrWhiteSpace(dto.PatientName) ? dto.PatientId : dto.PatientName;
        MedicalRecordNo = dto.MedicalRecordNo;
        GenderCode = dto.GenderCode;
        DeptName = string.IsNullOrWhiteSpace(dto.DeptName) ? "--" : dto.DeptName;
        DoctorName = string.IsNullOrWhiteSpace(dto.DoctorName) ? "--" : dto.DoctorName;
        QueueNo = dto.QueueNo;
        PaidAmount = dto.PaidAmount;
        PaymentChannelCode = dto.PaymentChannel ?? string.Empty;
        PaidAtMs = dto.PaidAtMs;
        Status = dto.Status;
        VisitStatus = dto.VisitStatus;
        RefundReason = dto.RefundReason ?? string.Empty;
    }

    public string Id { get; }
    public string AppointmentNo { get; }
    public string PatientName { get; }
    public string MedicalRecordNo { get; }
    public string GenderCode { get; }
    public string DeptName { get; }
    public string DoctorName { get; }
    public int QueueNo { get; }
    public decimal PaidAmount { get; }
    public string PaymentChannelCode { get; }
    public long? PaidAtMs { get; }
    public int Status { get; }
    public int? VisitStatus { get; }
    public string RefundReason { get; }

    public string QueueText => QueueNo.ToString("D3");
    public string PaidAmountText => "¥" + PaidAmount.ToString("N2");
    public string PaymentChannelLabel => RegistrationCashierViewModel.PaymentLabel(PaymentChannelCode);
    public string PaidTimeText => RegistrationCashierViewModel.FormatLocalDateTime(PaidAtMs);
    public string StatusLabel => Status switch
    {
        1 => "已收费 · 待接诊",
        2 => "已接诊",
        3 => "已退费",
        _ => "状态未知"
    };
    public IBrush StatusBrush => RegistrationCashierViewModel.ResolveStatusBrush(Status switch
    {
        1 => "SemiColorPrimary",
        2 => "SemiColorSuccess",
        3 => "SemiColorText2",
        _ => "SemiColorText1"
    });
    public IBrush StatusBackground => RegistrationCashierViewModel.ResolveStatusBrush(Status switch
    {
        1 => "SemiColorPrimaryLight",
        2 => "SemiColorSuccessLight",
        3 => "SemiColorBackground2",
        _ => "SemiColorBackground2"
    });
    public bool CanRefund => Status is 1 or 2;
    public string RefundHint => Status == 3
        ? "该挂号已退费，不能重复退款"
        : Status == 1 || Status == 2
            ? "已缴费挂号可办理退号并原路退费"
            : "当前状态不能办理退费";
    public string PatientLine => PatientName + " · " + MedicalRecordNo;
    public string ClinicLine => DeptName + " · " + DoctorName;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }
}
