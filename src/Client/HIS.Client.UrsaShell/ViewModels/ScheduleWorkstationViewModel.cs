using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Avalonia.Media;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public sealed class ScheduleWorkstationViewModel : ClientModelBase
{
    private static readonly string[] WeekdayNames = { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };

    private static readonly string[] SlotPeriodCodes = { "MORNING", "AFTERNOON", "EVENING", "ALL_DAY" };
    private static readonly string[] RegLevelCodes = { "NORMAL", "EXPERT", "SPECIALIST", "EMERGENCY" };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly List<DepartmentChoice> _allDepartments = new();
    private readonly List<DepartmentChoice> _roomChoices = new();
    private readonly List<DoctorChoice> _departmentDoctors = new();
    private string _filterDeptId = string.Empty;
    private string _queryDate = string.Empty;
    private string _templateName = string.Empty;
    private DepartmentChoice? _selectedDepartment;
    private DoctorChoice? _selectedDoctor;
    private string _roomId = string.Empty;
    private bool _isMondaySelected = true;
    private bool _isTuesdaySelected = true;
    private bool _isWednesdaySelected = true;
    private bool _isThursdaySelected = true;
    private bool _isFridaySelected = true;
    private bool _isSaturdaySelected;
    private bool _isSundaySelected;
    private PeriodChoice? _selectedPeriod;
    private string _startTime = "08:00";
    private string _endTime = "12:00";
    private RegistrationLevelChoice? _selectedRegistrationLevel;
    private string _registrationFee = "15.00";
    private string _capacity = "30";
    private bool _isTemplateActive = true;
    private string _generateStartDate = string.Empty;
    private string _generateEndDate = string.Empty;
    private ScheduleTemplateItem? _selectedTemplate;
    private bool _isBusy;
    private string _notice = "选择模板并指定日期范围后发布号源。";
    private string _errorMessage = string.Empty;
    private bool _isLoaded;
    private bool _isApplyingPeriodDefaults;

    public ScheduleWorkstationViewModel()
    {
        var today = DateTime.Today;
        _queryDate = today.ToString("yyyy-MM-dd");
        _generateStartDate = today.ToString("yyyy-MM-dd");
        _generateEndDate = today.AddDays(13).ToString("yyyy-MM-dd");
        foreach (var (code, label) in SlotPeriodCodes.Zip(
            new[] { "上午", "下午", "夜间", "全天" }, (code, label) => (code, label)))
        {
            PeriodOptions.Add(new PeriodChoice(code, label));
        }
        foreach (var (code, label) in RegLevelCodes.Zip(
            new[] { "普通门诊", "专家门诊", "专科门诊", "急诊" }, (code, label) => (code, label)))
        {
            RegistrationLevelOptions.Add(new RegistrationLevelChoice(code, label));
        }
        _selectedPeriod = PeriodOptions[0];
        _selectedRegistrationLevel = RegistrationLevelOptions[0];
    }

    public ObservableCollection<DepartmentChoice> DepartmentOptions { get; } = new();
    public ObservableCollection<DepartmentChoice> AllDepartmentOptions { get; } = new();
    public ObservableCollection<DoctorChoice> DoctorOptions { get; } = new();
    public ObservableCollection<DoctorChoice> DepartmentDoctorOptions { get; } = new();
    public ObservableCollection<DepartmentChoice> RoomOptions { get; } = new();
    public ObservableCollection<PeriodChoice> PeriodOptions { get; } = new();
    public ObservableCollection<RegistrationLevelChoice> RegistrationLevelOptions { get; } = new();
    public ObservableCollection<ScheduleTemplateItem> Templates { get; } = new();
    public ObservableCollection<ScheduleSlotItem> Slots { get; } = new();

    public string FilterDeptId
    {
        get => _filterDeptId;
        set
        {
            var next = value ?? string.Empty;
            if (SetField(ref _filterDeptId, next))
            {
                OnPropertyChanged(nameof(FilterDepartment));
                OnPropertyChanged(nameof(CurrentFilterSummary));
            }
        }
    }
    public DepartmentChoice? FilterDepartment => AllDepartmentOptions.FirstOrDefault(x => x.Id == FilterDeptId);

    public string QueryDate
    {
        get => _queryDate;
        set => SetField(ref _queryDate, value);
    }

    public string TemplateName
    {
        get => _templateName;
        set => SetField(ref _templateName, value ?? string.Empty);
    }

    public DepartmentChoice? SelectedDepartment
    {
        get => _selectedDepartment;
        set
        {
            if (SetField(ref _selectedDepartment, value) && value != null)
            {
                OnPropertyChanged(nameof(CurrentFilterSummary));
                OnPropertyChanged(nameof(GenerateTemplateHint));
                LoadDoctors(value.Id);
                LoadRooms(value.Id);
            }
        }
    }

    public DoctorChoice? SelectedDoctor
    {
        get => _selectedDoctor;
        set => SetField(ref _selectedDoctor, value);
    }

    public string RoomId
    {
        get => _roomId;
        set => SetField(ref _roomId, value);
    }

    public bool IsMondaySelected { get => _isMondaySelected; set => SetWeekday(ref _isMondaySelected, value); }
    public bool IsTuesdaySelected { get => _isTuesdaySelected; set => SetWeekday(ref _isTuesdaySelected, value); }
    public bool IsWednesdaySelected { get => _isWednesdaySelected; set => SetWeekday(ref _isWednesdaySelected, value); }
    public bool IsThursdaySelected { get => _isThursdaySelected; set => SetWeekday(ref _isThursdaySelected, value); }
    public bool IsFridaySelected { get => _isFridaySelected; set => SetWeekday(ref _isFridaySelected, value); }
    public bool IsSaturdaySelected { get => _isSaturdaySelected; set => SetWeekday(ref _isSaturdaySelected, value); }
    public bool IsSundaySelected { get => _isSundaySelected; set => SetWeekday(ref _isSundaySelected, value); }

    public PeriodChoice? SelectedPeriod
    {
        get => _selectedPeriod;
        set
        {
            if (SetField(ref _selectedPeriod, value) && !_isApplyingPeriodDefaults)
            {
                ApplyPeriodDefaultTimes();
            }
        }
    }

    public string StartTime { get => _startTime; set => SetField(ref _startTime, value); }
    public string EndTime { get => _endTime; set => SetField(ref _endTime, value); }

    public RegistrationLevelChoice? SelectedRegistrationLevel
    {
        get => _selectedRegistrationLevel;
        set => SetField(ref _selectedRegistrationLevel, value);
    }

    public string RegistrationFee { get => _registrationFee; set => SetField(ref _registrationFee, value); }
    public string Capacity { get => _capacity; set => SetField(ref _capacity, value); }
    public bool IsTemplateActive { get => _isTemplateActive; set => SetField(ref _isTemplateActive, value); }
    public string GenerateStartDate { get => _generateStartDate; set => SetField(ref _generateStartDate, value); }
    public string GenerateEndDate { get => _generateEndDate; set => SetField(ref _generateEndDate, value); }

    public ScheduleTemplateItem? SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            if (!SetField(ref _selectedTemplate, value))
            {
                return;
            }
            if (value == null)
            {
                OnPropertyChanged(nameof(EditorTitle));
                OnPropertyChanged(nameof(SaveTemplateText));
                OnPropertyChanged(nameof(GenerateTemplateHint));
                OnPropertyChanged(nameof(HasTemplateSelection));
                foreach (var item in Templates)
                {
                    item.IsSelected = false;
                }
                return;
            }
            LoadTemplateIntoEditor(value.Model);
            OnPropertyChanged(nameof(EditorTitle));
            OnPropertyChanged(nameof(SaveTemplateText));
            OnPropertyChanged(nameof(GenerateTemplateHint));
            OnPropertyChanged(nameof(HasTemplateSelection));
            foreach (var item in Templates)
            {
                item.IsSelected = ReferenceEquals(item, value);
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
                OnPropertyChanged(nameof(CanEditTemplate));
                OnPropertyChanged(nameof(IsNotBusy));
            }
        }
    }

    public bool CanEditTemplate => !IsBusy;
    public bool IsNotBusy => !IsBusy;
    public string Notice { get => _notice; private set => SetField(ref _notice, value); }
    public string ErrorMessage { get => _errorMessage; private set => SetField(ref _errorMessage, value); }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasTemplateSelection => SelectedTemplate != null;
    public bool IsSlotListEmpty => Slots.Count == 0;
    public string CurrentFilterSummary => FilterDepartment == null ? "全部门诊科室" : FilterDepartment.Name;
    public string EditorTitle => SelectedTemplate == null ? "新建排班模板" : "编辑排班模板";
    public string SaveTemplateText => SelectedTemplate == null ? "创建模板" : "保存模板";
    public string GenerateTemplateHint => SelectedTemplate == null
        ? "从左侧选择模板后可发布号源"
        : "将发布：" + SelectedTemplate.Summary;
    public string TemplateCountText => $"{Templates.Count} 个模板";
    public string SlotCountText => Slots.Count.ToString();

    public async Task EnsureLoadedAsync()
    {
        if (_isLoaded || IsBusy)
        {
            return;
        }
        IsBusy = true;
        try
        {
            using var http = CreateHttpClient();
            var departments = await GetAsync<List<DepartmentDto>>(http, "/api/v4/platform/departments?category=OPD");
            var rooms = await GetAsync<List<DepartmentDto>>(http, "/api/v4/platform/departments?category=ROOM");
            _allDepartments.Clear();
            foreach (var department in departments ?? new List<DepartmentDto>())
            {
                _allDepartments.Add(new DepartmentChoice(department.Id, department.DeptName));
            }
            var staff = await GetAsync<List<ScheduleStaffDto>>(http, "/api/v4/opd/schedule-staff");
            foreach (var person in staff ?? new List<ScheduleStaffDto>())
            {
                _departmentDoctors.Add(new DoctorChoice(
                    person.UserId,
                    person.DisplayName + " · " + person.EmployeeNo,
                    person.DeptId,
                    person.DeptName,
                    person.RoleCode));
                if (!_allDepartments.Any(x => x.Id == person.DeptId))
                {
                    _allDepartments.Add(new DepartmentChoice(person.DeptId, person.DeptName));
                }
            }
            AllDepartmentOptions.Clear();
            DepartmentOptions.Clear();
            foreach (var department in _allDepartments)
            {
                AllDepartmentOptions.Add(department);
                DepartmentOptions.Add(department);
            }
            _roomChoices.Clear();
            foreach (var room in rooms ?? new List<DepartmentDto>())
            {
                _roomChoices.Add(new DepartmentChoice(room.Id, room.DeptName));
            }
            _selectedDepartment = DepartmentOptions.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedDepartment));
            if (_selectedDepartment != null)
            {
                LoadDoctors(_selectedDepartment.Id);
            }
            await LoadTemplatesAsync();
            await LoadSlotsAsync();
            _isLoaded = true;
        }
        catch (Exception ex)
        {
            SetError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task FilterAsync()
    {
        if (IsBusy)
        {
            return;
        }
        if (!DateTime.TryParse(QueryDate, out _))
        {
            ErrorMessage = "查询日期格式无效，请使用 yyyy-MM-dd";
            OnPropertyChanged(nameof(HasError));
            return;
        }
        IsBusy = true;
        try
        {
            if (FilterDepartment != null)
            {
                var department = DepartmentOptions.FirstOrDefault(x => x.Id == FilterDepartment.Id);
                if (department != null)
                {
                    SelectedDepartment = department;
                }
            }
            await LoadTemplatesAsync();
            await LoadSlotsAsync();
            Notice = $"已刷新 {QueryDate} 的排班与号源。";
            ErrorMessage = string.Empty;
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(TemplateCountText));
            OnPropertyChanged(nameof(SlotCountText));
            OnPropertyChanged(nameof(IsSlotListEmpty));
            OnPropertyChanged(nameof(CurrentFilterSummary));
        }
        catch (Exception ex)
        {
            SetError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void NewTemplate()
    {
        SelectedTemplate = null;
        TemplateName = string.Empty;
        if (SelectedDepartment != null)
        {
            LoadDoctors(SelectedDepartment.Id);
            SelectedDoctor = DoctorOptions.FirstOrDefault(d => d.Id == "USER-DOC-OPD") ?? DoctorOptions.FirstOrDefault();
        }
        RoomId = RoomOptions.FirstOrDefault()?.Id ?? string.Empty;
        TemplateName = SelectedDepartment == null ? "门诊排班模板" : SelectedDepartment.Name + "·常规门诊";
        SetWeekdays("1,2,3,4,5");
        SelectedPeriod = PeriodOptions[0];
        StartTime = "08:00";
        EndTime = "12:00";
        SelectedRegistrationLevel = RegistrationLevelOptions[0];
        RegistrationFee = "15.00";
        Capacity = "30";
        IsTemplateActive = true;
        SelectedTemplate = null;
        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(SaveTemplateText));
        OnPropertyChanged(nameof(GenerateTemplateHint));
    }

    public async Task SaveTemplateAsync()
    {
        if (IsBusy)
        {
            return;
        }
        if (SelectedDepartment == null || SelectedDoctor == null)
        {
            SetValidationError("请选择门诊科室和出诊医生。");
            return;
        }
        if (!TryParseTime(StartTime, out var startMinute) || !TryParseTime(EndTime, out var endMinute))
        {
            SetValidationError("开始和结束时间须为 HH:mm 格式。");
            return;
        }
        if (startMinute >= endMinute)
        {
            SetValidationError("结束时间必须晚于开始时间。");
            return;
        }
        if (!decimal.TryParse(RegistrationFee, out var fee) || fee < 0)
        {
            SetValidationError("挂号费必须为不小于 0 的数字。");
            return;
        }
        if (!int.TryParse(Capacity, out var capacity) || capacity < 1)
        {
            SetValidationError("号源容量必须是大于 0 的整数。");
            return;
        }
        var weekdays = GetWeekdayCodes();
        if (weekdays.Count == 0)
        {
            SetValidationError("请至少选择一个出诊日。");
            return;
        }
        if (string.IsNullOrWhiteSpace(TemplateName))
        {
            SetValidationError("请填写模板名称。");
            return;
        }

        IsBusy = true;
        try
        {
            using var http = CreateHttpClient();
            var body = new
            {
                templateName = TemplateName.Trim(),
                opdDeptId = SelectedDepartment.Id,
                clinicRoomId = string.IsNullOrWhiteSpace(RoomId) ? null : RoomId,
                doctorId = SelectedDoctor.Id,
                weekdayCodes = string.Join(",", weekdays),
                slotPeriodCode = SelectedPeriod?.Code ?? "MORNING",
                startMinute,
                endMinute,
                regLevelCode = SelectedRegistrationLevel?.Code ?? "NORMAL",
                registrationFee = fee,
                totalCapacity = capacity,
                isActive = IsTemplateActive ? 1 : 0,
                campusId = "CAMPUS-01"
            };
            var editingId = SelectedTemplate?.Model.Id;
            var endpoint = editingId == null
                ? "/api/v4/opd/schedule-templates"
                : "/api/v4/opd/schedule-templates/" + Uri.EscapeDataString(editingId);
            var response = editingId == null
                ? await http.PostAsJsonAsync(endpoint, body, JsonOptions)
                : await http.PutAsJsonAsync(endpoint, body, JsonOptions);
            var saved = await ReadEnvelopeAsync<ScheduleTemplateDto>(response);
            Notice = response.IsSuccessStatusCode && saved != null
                ? (editingId == null ? "排班模板已创建。" : "排班模板已保存，已发布号源不受影响。")
                : await ReadFailureAsync(response);
            ErrorMessage = response.IsSuccessStatusCode ? string.Empty : Notice;
            OnPropertyChanged(nameof(HasError));
            await LoadTemplatesAsync(saved?.Id);
        }
        catch (Exception ex)
        {
            SetError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task GenerateAsync()
    {
        if (IsBusy)
        {
            return;
        }
        if (SelectedTemplate == null)
        {
            SetValidationError("请先从左侧选择排班模板。");
            return;
        }
        if (!DateTime.TryParse(GenerateStartDate, out var start) || !DateTime.TryParse(GenerateEndDate, out var end))
        {
            SetValidationError("请填写有效的起始和结束日期。");
            return;
        }
        if (end.Date < start.Date)
        {
            SetValidationError("结束日期不能早于起始日期。");
            return;
        }
        IsBusy = true;
        try
        {
            using var http = CreateHttpClient();
            var response = await http.PostAsJsonAsync(
                "/api/v4/opd/schedule-templates/" + Uri.EscapeDataString(SelectedTemplate.Model.Id) + "/generate",
                new { templateId = SelectedTemplate.Model.Id, startDate = start.Date.ToString("yyyy-MM-dd"), endDate = end.Date.ToString("yyyy-MM-dd") },
                JsonOptions);
            var result = await ReadEnvelopeAsync<ScheduleGenerationDto>(response);
            if (!response.IsSuccessStatusCode || result == null)
            {
                Notice = await ReadFailureAsync(response);
                ErrorMessage = Notice;
            }
            else
            {
                Notice = $"新增 {result.CreatedCount} 个号源；跳过 {result.SkippedCount} 个已有班次；匹配 {result.DaysMatched} 个出诊日。";
                if (DateTime.TryParse(GenerateStartDate, out var rangeStart))
                {
                    QueryDate = rangeStart.ToString("yyyy-MM-dd");
                }
                await LoadSlotsAsync();
                ErrorMessage = string.Empty;
            }
            OnPropertyChanged(nameof(HasError));
        }
        catch (Exception ex)
        {
            SetError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ToggleSlotAsync(ScheduleSlotItem? slot)
    {
        if (IsBusy || slot == null)
        {
            return;
        }
        if (!slot.IsStopped && slot.ReservedCount > 0)
        {
            SetValidationError("该号源已有预约，不能直接停诊。");
            return;
        }
        IsBusy = true;
        try
        {
            using var http = CreateHttpClient();
            var uri = "/api/v4/opd/schedules/" + Uri.EscapeDataString(slot.Model.Id);
            using var request = new HttpRequestMessage(HttpMethod.Patch, uri)
            {
                Content = JsonContent.Create(new { isStopped = !slot.IsStopped, totalCapacity = slot.Capacity })
            };
            var response = await http.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                Notice = slot.IsStopped ? "号源已恢复。" : "号源已停诊。";
                ErrorMessage = string.Empty;
                await LoadSlotsAsync();
            }
            else
            {
                Notice = await ReadFailureAsync(response);
                ErrorMessage = Notice;
            }
            OnPropertyChanged(nameof(HasError));
        }
        catch (Exception ex)
        {
            SetError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task UpdateSlotCapacityAsync(ScheduleSlotItem? slot, string? capacityText)
    {
        if (IsBusy || slot == null)
        {
            return;
        }
        if (!int.TryParse(capacityText, out var capacity) || capacity < 1)
        {
            SetValidationError("号源容量必须是大于 0 的整数。");
            return;
        }
        if (capacity < slot.ReservedCount)
        {
            SetValidationError($"号源容量不能低于已预约数量 {slot.ReservedCount}。");
            return;
        }
        IsBusy = true;
        try
        {
            using var http = CreateHttpClient();
            var uri = "/api/v4/opd/schedules/" + Uri.EscapeDataString(slot.Model.Id);
            using var request = new HttpRequestMessage(HttpMethod.Patch, uri)
            {
                Content = JsonContent.Create(new { isStopped = slot.IsStopped, totalCapacity = capacity })
            };
            var response = await http.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                Notice = $"号源容量已调整为 {capacity}。";
                ErrorMessage = string.Empty;
                await LoadSlotsAsync();
            }
            else
            {
                Notice = await ReadFailureAsync(response);
                ErrorMessage = Notice;
            }
            OnPropertyChanged(nameof(HasError));
        }
        catch (Exception ex)
        {
            SetError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadTemplatesAsync(string? selectId = null)
    {
        using var http = CreateHttpClient();
        var templates = await GetAsync<List<ScheduleTemplateDto>>(http, "/api/v4/opd/schedule-templates?includeInactive=true");
        var previousId = selectId ?? SelectedTemplate?.Model.Id;
        Templates.Clear();
        foreach (var template in templates ?? new List<ScheduleTemplateDto>())
        {
            if (FilterDepartment == null || string.Equals(template.OpdDeptId, FilterDepartment.Id, StringComparison.OrdinalIgnoreCase))
            {
                Templates.Add(new ScheduleTemplateItem(this, template));
            }
        }
        var replacement = Templates.FirstOrDefault(x => string.Equals(x.Model.Id, previousId, StringComparison.OrdinalIgnoreCase))
            ?? (Templates.Count > 0 ? Templates[0] : null);
        if (replacement != null)
        {
            SelectedTemplate = replacement;
        }
        else
        {
            SelectedTemplate = null;
        }
        OnPropertyChanged(nameof(TemplateCountText));
    }

    private async Task LoadSlotsAsync()
    {
        using var http = CreateHttpClient();
        var dateText = DateTime.TryParse(QueryDate, out var date)
            ? date.ToString("yyyyMMdd")
            : string.Empty;
        var query = "/api/v4/opd/schedules?scheduleDateInt=" + dateText;
        if (FilterDepartment != null)
        {
            query += "&opdDeptId=" + Uri.EscapeDataString(FilterDepartment.Id);
        }
        var schedules = await GetAsync<List<ScheduleSlotDto>>(http, query);
        Slots.Clear();
        foreach (var schedule in schedules ?? new List<ScheduleSlotDto>())
        {
            Slots.Add(new ScheduleSlotItem(this, schedule));
        }
        OnPropertyChanged(nameof(SlotCountText));
        OnPropertyChanged(nameof(IsSlotListEmpty));
    }

    private void LoadTemplateIntoEditor(ScheduleTemplateDto template)
    {
        TemplateName = template.TemplateName;
        SelectedDepartment = DepartmentOptions.FirstOrDefault(x => x.Id == template.OpdDeptId);
        LoadDoctors(template.OpdDeptId);
        SelectedDoctor = DoctorOptions.FirstOrDefault(x => x.Id == template.DoctorId);
        RoomId = template.ClinicRoomId ?? string.Empty;
        SetWeekdays(template.WeekdayCodes);
        _isApplyingPeriodDefaults = true;
        try
        {
            SelectedPeriod = PeriodOptions.FirstOrDefault(x => x.Code == template.SlotPeriodCode);
            StartTime = FormatMinutes(template.StartMinute);
            EndTime = FormatMinutes(template.EndMinute);
        }
        finally
        {
            _isApplyingPeriodDefaults = false;
        }
        SelectedRegistrationLevel = RegistrationLevelOptions.FirstOrDefault(x => x.Code == template.RegLevelCode);
        RegistrationFee = template.RegistrationFee.ToString("0.00");
        Capacity = template.TotalCapacity.ToString();
        IsTemplateActive = template.IsActive == 1;
    }

    public string DepartmentName(string id) =>
        _allDepartments.FirstOrDefault(x => x.Id == id)?.Name ?? id;

    public string DoctorName(string id) =>
        _departmentDoctors.FirstOrDefault(x => x.Id == id)?.Name ?? id;

    private void LoadDoctors(string deptId)
    {
        DoctorOptions.Clear();
        foreach (var doctor in _departmentDoctors.Where(x => x.DeptId == deptId))
        {
            DoctorOptions.Add(doctor);
        }
        _selectedDoctor = DoctorOptions.FirstOrDefault(x => x.Id == _selectedDoctor?.Id) ?? DoctorOptions.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedDoctor));
    }

    private void LoadRooms(string deptId)
    {
        var previousId = RoomId;
        RoomOptions.Clear();
        foreach (var room in _roomChoices)
        {
            RoomOptions.Add(room);
        }
        RoomId = RoomOptions.FirstOrDefault(x => x.Id == previousId)?.Id
            ?? RoomOptions.FirstOrDefault()?.Id
            ?? string.Empty;
    }

    private void ApplyPeriodDefaultTimes()
    {
        switch (SelectedPeriod?.Code)
        {
            case "AFTERNOON":
                StartTime = "14:00";
                EndTime = "17:00";
                break;
            case "EVENING":
                StartTime = "18:00";
                EndTime = "21:00";
                break;
            case "ALL_DAY":
                StartTime = "08:00";
                EndTime = "17:00";
                break;
            default:
                StartTime = "08:00";
                EndTime = "12:00";
                break;
        }
    }

    private void SetWeekdays(string codes)
    {
        var days = new HashSet<int>((codes ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => int.TryParse(x, out var day) ? day : 0));
        IsMondaySelected = days.Contains(1);
        IsTuesdaySelected = days.Contains(2);
        IsWednesdaySelected = days.Contains(3);
        IsThursdaySelected = days.Contains(4);
        IsFridaySelected = days.Contains(5);
        IsSaturdaySelected = days.Contains(6);
        IsSundaySelected = days.Contains(7);
    }

    private List<int> GetWeekdayCodes()
    {
        var result = new List<int>();
        if (IsMondaySelected) result.Add(1);
        if (IsTuesdaySelected) result.Add(2);
        if (IsWednesdaySelected) result.Add(3);
        if (IsThursdaySelected) result.Add(4);
        if (IsFridaySelected) result.Add(5);
        if (IsSaturdaySelected) result.Add(6);
        if (IsSundaySelected) result.Add(7);
        return result;
    }

    private void SetWeekday(ref bool field, bool value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (SetField(ref field, value, name))
        {
            OnPropertyChanged(nameof(WeekdaySummary));
        }
    }

    public string WeekdaySummary
    {
        get
        {
            var codes = GetWeekdayCodes();
            return codes.Count == 0
                ? "尚未选择出诊日"
                : string.Join("、", codes.Select(x => WeekdayNames[x - 1]));
        }
    }

    private void SetValidationError(string message)
    {
        Notice = message;
        ErrorMessage = message;
        OnPropertyChanged(nameof(HasError));
    }

    private void SetError(Exception ex)
    {
        Notice = "排班服务暂时不可用，请检查服务端连接。";
        ErrorMessage = ex.Message;
        OnPropertyChanged(nameof(HasError));
    }

    public bool IsTemplateSelected(ScheduleTemplateItem item) =>
        item != null && ReferenceEquals(SelectedTemplate, item);

    public static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    public void SelectTemplate(ScheduleTemplateItem item)
    {
        if (item != null)
        {
            SelectedTemplate = item;
        }
    }

    private static async Task<T?> GetAsync<T>(HttpClient http, string uri)
    {
        var response = await http.GetAsync(uri);
        return await ReadEnvelopeAsync<T>(response);
    }

    private static async Task<T?> ReadEnvelopeAsync<T>(HttpResponseMessage response)
    {
        try
        {
            var envelope = await response.Content.ReadFromJsonAsync<ClientApiEnvelope<T>>(JsonOptions);
            return response.IsSuccessStatusCode && envelope?.Code == 200 ? envelope.Data : default;
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static async Task<string> ReadFailureAsync(HttpResponseMessage response)
    {
        try
        {
            var envelope = await response.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOptions);
            if (!string.IsNullOrWhiteSpace(envelope?.Message))
            {
                return envelope.Message;
            }
        }
        catch (JsonException)
        {
        }
        return $"请求失败 (HTTP {(int)response.StatusCode})";
    }

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { BaseAddress = new Uri(ApiEndpoints.BaseAddress) };
        if (!string.IsNullOrWhiteSpace(SessionContext.Current.AccessToken))
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SessionContext.Current.AccessToken);
        }
        return http;
    }

    private static bool TryParseTime(string? text, out int minute)
    {
        minute = 0;
        if (!TimeOnly.TryParse(text, out var time))
        {
            return false;
        }
        minute = time.Hour * 60 + time.Minute;
        return true;
    }

    private static string FormatMinutes(int minute) => $"{minute / 60:00}:{minute % 60:00}";

    public sealed record DepartmentChoice(string Id, string Name);
    public sealed record DoctorChoice(string Id, string Name, string DeptId = "", string DeptName = "", string RoleCode = "");
    public sealed record PeriodChoice(string Code, string Name);
    public sealed record RegistrationLevelChoice(string Code, string Name);

    public sealed class ScheduleTemplateDto
    {
        public string Id { get; set; } = string.Empty;
        public string CampusId { get; set; } = "CAMPUS-01";
        public string OpdDeptId { get; set; } = string.Empty;
        public string? ClinicRoomId { get; set; }
        public string DoctorId { get; set; } = string.Empty;
        public string TemplateName { get; set; } = string.Empty;
        public string WeekdayCodes { get; set; } = string.Empty;
        public string SlotPeriodCode { get; set; } = string.Empty;
        public int StartMinute { get; set; }
        public int EndMinute { get; set; }
        public string RegLevelCode { get; set; } = string.Empty;
        public decimal RegistrationFee { get; set; }
        public int TotalCapacity { get; set; }
        public int IsActive { get; set; }
    }

    public sealed class ScheduleSlotDto
    {
        public string Id { get; set; } = string.Empty;
        public string OpdDeptId { get; set; } = string.Empty;
        public string? ClinicRoomId { get; set; }
        public string DoctorId { get; set; } = string.Empty;
        public int ScheduleDateInt { get; set; }
        public string SlotPeriodCode { get; set; } = string.Empty;
        public long StartsAtMs { get; set; }
        public long EndsAtMs { get; set; }
        public string RegLevelCode { get; set; } = string.Empty;
        public decimal RegistrationFee { get; set; }
        public int TotalCapacity { get; set; }
        public int ReservedCount { get; set; }
        public int SlotStatus { get; set; }
    }

    private sealed class DepartmentDto
    {
        public string Id { get; set; } = string.Empty;
        public string DeptName { get; set; } = string.Empty;
    }

    private sealed class ScheduleStaffDto
    {
        public string UserId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string EmployeeNo { get; set; } = string.Empty;
        public string DeptId { get; set; } = string.Empty;
        public string DeptName { get; set; } = string.Empty;
        public string RoleCode { get; set; } = string.Empty;
    }

    private sealed class ScheduleGenerationDto
    {
        public int CreatedCount { get; set; }
        public int SkippedCount { get; set; }
        public int DaysMatched { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}

public sealed class ScheduleTemplateItem : ClientModelBase
{
    private readonly ScheduleWorkstationViewModel _owner;
    private bool _isSelected;
    private bool _isActive;

    public ScheduleTemplateItem(ScheduleWorkstationViewModel owner, ScheduleWorkstationViewModel.ScheduleTemplateDto model)
    {
        _owner = owner;
        Model = model;
        _isActive = model.IsActive == 1;
    }

    public ScheduleWorkstationViewModel.ScheduleTemplateDto Model { get; }
    public string TemplateName => Model.TemplateName;
    public bool IsActive => _isActive;
    public string StatusText => IsActive ? "启用" : "停用";
    public string WeekdayText => string.Join("、", (Model.WeekdayCodes ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(x => int.TryParse(x, out var day) && day >= 1 && day <= 7
            ? new[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" }[day - 1]
            : x));
    public string TimeText => $"{Model.StartMinute / 60:00}:{Model.StartMinute % 60:00} - {Model.EndMinute / 60:00}:{Model.EndMinute % 60:00}";
    public string LevelText => Model.RegLevelCode switch
    {
        "EXPERT" => "专家门诊",
        "SPECIALIST" => "专科门诊",
        "EMERGENCY" => "急诊",
        _ => "普通门诊"
    };
    public string CapacityText => $"容量 {Model.TotalCapacity}";
    public string DeptName => _owner.DepartmentName(Model.OpdDeptId);
    public string DoctorName => _owner.DoctorName(Model.DoctorId);
    public string Summary => $"{Model.TemplateName} · {WeekdayText} · {TimeText}";
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetField(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(CardBackground));
                OnPropertyChanged(nameof(CardBorderBrush));
                OnPropertyChanged(nameof(CardBorderThickness));
            }
        }
    }
    public IBrush CardBackground => ScheduleWorkstationViewModel.Brush(IsSelected ? "#E6F4FF" : "#FFFFFF");
    public IBrush CardBorderBrush => ScheduleWorkstationViewModel.Brush(IsSelected ? "#1677FF" : "#D9D9D9");
    public Avalonia.Thickness CardBorderThickness => IsSelected ? new Avalonia.Thickness(2) : new Avalonia.Thickness(1);
    public IBrush StatusBackground => ScheduleWorkstationViewModel.Brush(IsActive ? "#E8F5E9" : "#F0F0F0");
    public IBrush StatusForeground => ScheduleWorkstationViewModel.Brush(IsActive ? "#237804" : "#8C8C8C");

    public void Select() => _owner.SelectedTemplate = this;
}

public sealed class ScheduleSlotItem : ClientModelBase
{
    private readonly ScheduleWorkstationViewModel _owner;
    private string _capacityText;

    public ScheduleSlotItem(ScheduleWorkstationViewModel owner, ScheduleWorkstationViewModel.ScheduleSlotDto model)
    {
        _owner = owner;
        Model = model;
        _capacityText = model.TotalCapacity.ToString();
    }

    public ScheduleWorkstationViewModel.ScheduleSlotDto Model { get; }
    public string DateText => Model.ScheduleDateInt >= 10000101
        ? $"{Model.ScheduleDateInt / 10000:0000}-{Model.ScheduleDateInt / 100 % 100:00}-{Model.ScheduleDateInt % 100:00}"
        : string.Empty;
    public string WeekdayText
    {
        get
        {
            if (Model.ScheduleDateInt < 10000101
                || !DateTime.TryParseExact(Model.ScheduleDateInt.ToString(), "yyyyMMdd", null,
                    System.Globalization.DateTimeStyles.None, out var date))
            {
                return string.Empty;
            }
            return new[] { "周日", "周一", "周二", "周三", "周四", "周五", "周六" }[(int)date.DayOfWeek];
        }
    }
    public string TimeText => $"{FormatMs(Model.StartsAtMs)} - {FormatMs(Model.EndsAtMs)}";
    public string DeptName => _owner.DepartmentName(Model.OpdDeptId);
    public string DoctorName => _owner.DoctorName(Model.DoctorId);
    public string RoomText => Model.ClinicRoomId ?? "未设诊室";
    public string LevelText => Model.RegLevelCode switch
    {
        "EXPERT" => "专家",
        "SPECIALIST" => "专科",
        "EMERGENCY" => "急诊",
        _ => "普通"
    };
    public string FeeText => "¥" + Model.RegistrationFee.ToString("0.00");
    public int Capacity => Model.TotalCapacity;
    public int ReservedCount => Model.ReservedCount;
    public string CapacityText => $"{Model.ReservedCount} / {Model.TotalCapacity}";
    public bool HasCapacityChanged => _capacityText != Model.TotalCapacity.ToString();
    public bool IsStopped => Model.SlotStatus == 3;
    public bool IsFull => !IsStopped && Model.ReservedCount >= Model.TotalCapacity;
    public string PeriodText => Model.SlotPeriodCode switch
    {
        "MORNING" => "上午",
        "AFTERNOON" => "下午",
        "EVENING" => "夜间",
        "ALL_DAY" => "全天",
        _ => Model.SlotPeriodCode
    };
    public IBrush StatusForeground => Model.SlotStatus switch
    {
        3 => ScheduleWorkstationViewModel.Brush("#D4380D"),
        2 => ScheduleWorkstationViewModel.Brush("#AD6800"),
        _ => ScheduleWorkstationViewModel.Brush("#237804")
    };
    public string StatusText => IsStopped ? "已停诊" : IsFull ? "已约满" : "开放";
    public string ToggleActionText => IsStopped ? "恢复" : "停诊";
    public string ToggleActionGlyph => IsStopped ? "+" : "-";
    public bool CanStop => IsStopped || Model.ReservedCount == 0;

    public Task ToggleAsync() => _owner.ToggleSlotAsync(this);
    public Task UpdateCapacityAsync() => _owner.UpdateSlotCapacityAsync(this, _capacityText);
    public bool CanChangeCapacity => !_owner.IsBusy;
    public string CapacityEditText
    {
        get => _capacityText;
        set
        {
            if (SetField(ref _capacityText, value))
            {
                OnPropertyChanged(nameof(HasCapacityChanged));
            }
        }
    }

    private static string FormatMs(long value)
    {
        if (value <= 0)
        {
            return "--:--";
        }
        return DateTimeOffset.FromUnixTimeMilliseconds(value).ToOffset(TimeSpan.FromHours(8)).ToString("HH:mm");
    }
}
