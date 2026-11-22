using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.Hr;

public sealed class HrSummaryDto
{
    public int TotalActive { get; set; }
    public int DoctorCount { get; set; }
    public int NurseCount { get; set; }
    public int ResignedOrRetiredCount { get; set; }
    public int ExpiringCredentials60d { get; set; }
    public int ExpiringContracts60d { get; set; }
}

public sealed class HrEmployeeRow
{
    public string Id { get; set; } = string.Empty;
    public string EmployeeNo { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public int? BirthDateInt { get; set; }
    public string? IdCardMasked { get; set; }
    public string? PhoneMasked { get; set; }
    public string? Email { get; set; }
    public string? Education { get; set; }
    public string? GraduatedSchool { get; set; }
    public string? MajorName { get; set; }
    public string? PoliticalStatus { get; set; }
    public int HireDateInt { get; set; }
    public int? ContractEndDateInt { get; set; }
    public string EmploymentType { get; set; } = string.Empty;
    public string StaffCategory { get; set; } = string.Empty;
    public string PrimaryDeptId { get; set; } = string.Empty;
    public string? PrimaryDeptName { get; set; }
    public string ProfessionalTitle { get; set; } = string.Empty;
    public string? JobPosition { get; set; }
    public string? DirectSupervisorId { get; set; }
    public string EmploymentStatus { get; set; } = string.Empty;
    public int? ResignDateInt { get; set; }
    public string? ResignReason { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? Address { get; set; }
    public string? Remark { get; set; }
    public string? LoginUserName { get; set; }
    public short? AccountActive { get; set; }

    public string DeptDisplay => string.IsNullOrWhiteSpace(PrimaryDeptName) ? PrimaryDeptId : PrimaryDeptName;
    public string AccountDisplay => string.IsNullOrWhiteSpace(LoginUserName)
        ? "未开通账号"
        : $"{LoginUserName} ({(AccountActive == 1 ? "启用" : "停用")})";
    public string StatusDisplay => EmploymentStatus switch
    {
        "ACTIVE" => "在职",
        "PROBATION" => "试用期",
        "ONBOARDING" => "待入职",
        "RESIGNED" => "已离职",
        "RETIRED" => "已退休",
        _ => EmploymentStatus
    };
}

public sealed class HrCredentialRow
{
    public string Id { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? EmployeeNo { get; set; }
    public string CredentialType { get; set; } = string.Empty;
    public string CertNo { get; set; } = string.Empty;
    public string? Remark { get; set; }
    public string? IssuingAuthority { get; set; }
    public int? IssueDateInt { get; set; }
    public int? ExpiryDateInt { get; set; }
    public int GrantedLevel { get; set; }
    public string VerifyStatus { get; set; } = string.Empty;
}

public sealed class HrChangeRow
{
    public string Id { get; set; } = string.Empty;
    public string ChangeNo { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeNo { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string ChangeType { get; set; } = string.Empty;
    public string? BeforeSnapshotJson { get; set; }
    public string? AfterSnapshotJson { get; set; }
    public string? Reason { get; set; }
    public int EffectiveDateInt { get; set; }
    public string? AppliedBy { get; set; }

    public string ChangeTypeDisplay => ChangeType switch
    {
        "ONBOARD" => "新员工入职",
        "INFO_UPDATE" => "人事信息修改",
        "TRANSFER" => "调科/调岗",
        "PROMOTION" => "职称晋升",
        "RESIGN" => "办理离职",
        "RETIRE" => "办理退休",
        _ => ChangeType
    };
}

public sealed class HrContractRow
{
    public string Id { get; set; } = string.Empty;
    public string ContractNo { get; set; } = string.Empty;
    public string ContractType { get; set; } = string.Empty;
    public int StartDateInt { get; set; }
    public int? EndDateInt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Remark { get; set; }
}

public sealed class HrDeptOption
{
    public string Id { get; set; } = string.Empty;
    public string DeptName { get; set; } = string.Empty;
    public string DeptCode { get; set; } = string.Empty;
    public override string ToString() => string.IsNullOrWhiteSpace(DeptCode) ? DeptName : $"{DeptName} ({DeptCode})";
}

public sealed class HrRoleOption
{
    public string Id { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string RoleCode { get; set; } = string.Empty;
    public override string ToString() => string.IsNullOrWhiteSpace(RoleCode) ? RoleName : $"{RoleName} ({RoleCode})";
}

public sealed class HrPiiDto
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? IdCardNo { get; set; }
    public string? Phone { get; set; }
}

public sealed class HrStationViewModel : ClientModelBase
{
    private readonly ShellViewModel? _shell;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private bool _isBusy;
    private string _statusMessage = "就绪";
    private int _selectedTabIndex;

    // KPI
    private int _totalActive;
    private int _probationCount;
    private int _doctorCount;
    private int _nurseCount;
    private int _resignedCount;
    private int _expiringCredentialsCount;
    private int _expiringContractsCount;

    // Filter
    private string _searchKeyword = string.Empty;
    private string _filterStatus = "ALL";
    private string _filterCategory = "ALL";

    // Selected employee & profile edit fields
    private HrEmployeeRow? _selectedEmployee;
    private string _editFullName = string.Empty;
    private string _editGender = "MALE";
    private string _editPhone = string.Empty;
    private string _editEmail = string.Empty;
    private string _editEducation = string.Empty;
    private string _editGraduatedSchool = string.Empty;
    private string _editMajorName = string.Empty;
    private string _editPoliticalStatus = string.Empty;
    private string _editProfessionalTitle = "RESIDENT";
    private string _editJobPosition = string.Empty;
    private string _editStaffCategory = "DOCTOR";
    private string _editEmploymentType = "CONTRACT";
    private string _editEmergencyContactName = string.Empty;
    private string _editEmergencyContactPhone = string.Empty;
    private string _editAddress = string.Empty;
    private string _editRemark = string.Empty;
    private string _editChangeReason = "人事档案例行维护";
    private string _decryptedPiiDisplay = string.Empty;

    // Transfer / Promote / Resign fields
    private HrDeptOption? _transferTargetDept;
    private HrRoleOption? _transferTargetRole;
    private string _transferReason = "院内工作调动";
    private string _promoteNewTitle = "ATTENDING";
    private int _promotePrescribeLevel = 2;
    private int _promoteSurgeryLevel = 2;
    private int _promoteEmrSignLevel = 2;
    private int _promoteSpecialExamLevel = 2;
    private string _promoteReason = "职称评审晋升";
    private string _resignType = "RESIGNED";
    private string _resignReason = "个人原因离职";

    // Onboard form fields
    private string _obFullName = string.Empty;
    private string _obEmployeeNo = string.Empty;
    private string _obGender = "MALE";
    private string _obIdCardNo = string.Empty;
    private string _obPhone = string.Empty;
    private string _obEmail = string.Empty;
    private string _obEducation = "本科";
    private string _obGraduatedSchool = string.Empty;
    private string _obMajorName = "临床医学";
    private string _obPoliticalStatus = "群众";
    private HrDeptOption? _obSelectedDept;
    private string _obStaffCategory = "DOCTOR";
    private string _obEmploymentType = "CONTRACT";
    private string _obProfessionalTitle = "RESIDENT";
    private string _obJobPosition = "住院医师";
    private string _obHireDateText = DateTime.Now.ToString("yyyyMMdd");
    private string _obContractEndDateText = DateTime.Now.AddYears(3).ToString("yyyyMMdd");
    private bool _obAutoCreateAccount = true;
    private string _obLoginUserName = string.Empty;
    private string _obInitialPassword = string.Empty;
    private HrRoleOption? _obSelectedRole;
    private int _obPrescribeLevel = 1;
    private int _obSurgeryLevel = 1;
    private int _obEmrSignLevel = 1;
    private int _obSpecialExamLevel = 1;
    private string _obRemark = string.Empty;

    // Credential form fields
    private string _credType = "PHYSICIAN_LICENSE";
    private string _credNo = string.Empty;
    private string _credScope = "临床类别";
    private string _credIssuer = "国家卫生健康委员会";
    private string _credIssueDateText = DateTime.Now.ToString("yyyyMMdd");
    private string _credExpiryDateText = DateTime.Now.AddYears(5).ToString("yyyyMMdd");
    private int _credPrivilegeLevel = 2;

    // Contract form fields
    private string _contractType = "FIXED_TERM";
    private string _contractStartText = DateTime.Now.ToString("yyyyMMdd");
    private string _contractEndText = DateTime.Now.AddYears(3).ToString("yyyyMMdd");
    private string _contractRemark = string.Empty;

    public HrStationViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        RefreshCommand = new RelayCommand(() => _ = LoadAllAsync());
        SearchCommand = new RelayCommand(() => _ = LoadEmployeesAsync());
        SaveProfileCommand = new RelayCommand(() => _ = SaveProfileAsync());
        DecryptPiiCommand = new RelayCommand(() => _ = DecryptPiiAsync());
        SubmitOnboardCommand = new RelayCommand(() => _ = SubmitOnboardAsync());
        TransferCommand = new RelayCommand(() => _ = TransferEmployeeAsync());
        PromoteCommand = new RelayCommand(() => _ = PromoteEmployeeAsync());
        ResignCommand = new RelayCommand(() => _ = ResignEmployeeAsync());
        AddCredentialCommand = new RelayCommand(() => _ = AddCredentialAsync());
        AddContractCommand = new RelayCommand(() => _ = AddContractAsync());

        _ = LoadAllAsync();
    }

    public WorkbenchModel Workbench { get; }

    public ObservableCollection<HrEmployeeRow> Employees { get; } = new();
    public ObservableCollection<HrCredentialRow> SelectedCredentials { get; } = new();
    public ObservableCollection<HrCredentialRow> ExpiringCredentials { get; } = new();
    public ObservableCollection<HrContractRow> SelectedContracts { get; } = new();
    public ObservableCollection<HrChangeRow> ChangeRecords { get; } = new();
    public ObservableCollection<HrDeptOption> Departments { get; } = new();
    public ObservableCollection<HrRoleOption> Roles { get; } = new();

    public IReadOnlyList<string> StatusFilterOptions { get; } = new[] { "ALL", "ACTIVE", "PROBATION", "RESIGNED", "RETIRED" };
    public IReadOnlyList<string> CategoryFilterOptions { get; } = new[] { "ALL", "DOCTOR", "NURSE", "PHARMACIST", "MEDTECH", "ADMIN" };
    public IReadOnlyList<string> StaffCategoryOptions { get; } = new[] { "DOCTOR", "NURSE", "PHARMACIST", "MEDTECH", "ADMIN" };
    public IReadOnlyList<string> EmploymentTypeOptions { get; } = new[] { "PERMANENT", "CONTRACT", "DISPATCH", "INTERN", "EXTERNAL_EXPERT" };
    public IReadOnlyList<string> GenderOptions { get; } = new[] { "MALE", "FEMALE" };
    public IReadOnlyList<string> TitleOptions { get; } = new[] { "RESIDENT", "ATTENDING", "ASSOCIATE_CHIEF", "CHIEF", "NURSE", "HEAD_NURSE", "PHARMACIST", "TECHNICIAN", "STAFF" };
    public IReadOnlyList<string> CredentialTypeOptions { get; } = new[] { "PHYSICIAN_LICENSE", "NURSE_LICENSE", "PHARMACIST_LICENSE", "ANTIBIOTIC_CERT", "NARCOTIC_CERT", "SURGERY_CERT" };
    public IReadOnlyList<string> ResignTypeOptions { get; } = new[] { "RESIGNED", "RETIRED" };
    public IReadOnlyList<string> ContractTypeOptions { get; } = new[] { "FIXED_TERM", "OPEN_ENDED", "PROBATION", "REHIRE" };

    public ICommand RefreshCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand SaveProfileCommand { get; }
    public ICommand DecryptPiiCommand { get; }
    public ICommand SubmitOnboardCommand { get; }
    public ICommand TransferCommand { get; }
    public ICommand PromoteCommand { get; }
    public ICommand ResignCommand { get; }
    public ICommand AddCredentialCommand { get; }
    public ICommand AddContractCommand { get; }

    public bool IsBusy { get => _isBusy; set => SetField(ref _isBusy, value); }
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
    public int SelectedTabIndex { get => _selectedTabIndex; set => SetField(ref _selectedTabIndex, value); }

    public int TotalActive { get => _totalActive; set => SetField(ref _totalActive, value); }
    public int ProbationCount { get => _probationCount; set => SetField(ref _probationCount, value); }
    public int DoctorCount { get => _doctorCount; set => SetField(ref _doctorCount, value); }
    public int NurseCount { get => _nurseCount; set => SetField(ref _nurseCount, value); }
    public int ResignedCount { get => _resignedCount; set => SetField(ref _resignedCount, value); }
    public int ExpiringCredentialsCount { get => _expiringCredentialsCount; set => SetField(ref _expiringCredentialsCount, value); }
    public int ExpiringContractsCount { get => _expiringContractsCount; set => SetField(ref _expiringContractsCount, value); }

    public string SearchKeyword { get => _searchKeyword; set => SetField(ref _searchKeyword, value); }
    public string FilterStatus
    {
        get => _filterStatus;
        set { if (SetField(ref _filterStatus, value)) _ = LoadEmployeesAsync(); }
    }
    public string FilterCategory
    {
        get => _filterCategory;
        set { if (SetField(ref _filterCategory, value)) _ = LoadEmployeesAsync(); }
    }

    public HrEmployeeRow? SelectedEmployee
    {
        get => _selectedEmployee;
        set
        {
            if (SetField(ref _selectedEmployee, value))
            {
                OnPropertyChanged(nameof(HasSelectedEmployee));
                PopulateEditorFromSelected();
                if (value != null) _ = LoadSelectedDetailsAsync(value.Id);
            }
        }
    }

    public bool HasSelectedEmployee => _selectedEmployee != null;

    // Edit form properties
    public string EditFullName { get => _editFullName; set => SetField(ref _editFullName, value); }
    public string EditGender { get => _editGender; set => SetField(ref _editGender, value); }
    public string EditPhone { get => _editPhone; set => SetField(ref _editPhone, value); }
    public string EditEmail { get => _editEmail; set => SetField(ref _editEmail, value); }
    public string EditEducation { get => _editEducation; set => SetField(ref _editEducation, value); }
    public string EditGraduatedSchool { get => _editGraduatedSchool; set => SetField(ref _editGraduatedSchool, value); }
    public string EditMajorName { get => _editMajorName; set => SetField(ref _editMajorName, value); }
    public string EditPoliticalStatus { get => _editPoliticalStatus; set => SetField(ref _editPoliticalStatus, value); }
    public string EditProfessionalTitle { get => _editProfessionalTitle; set => SetField(ref _editProfessionalTitle, value); }
    public string EditJobPosition { get => _editJobPosition; set => SetField(ref _editJobPosition, value); }
    public string EditStaffCategory { get => _editStaffCategory; set => SetField(ref _editStaffCategory, value); }
    public string EditEmploymentType { get => _editEmploymentType; set => SetField(ref _editEmploymentType, value); }
    public string EditEmergencyContactName { get => _editEmergencyContactName; set => SetField(ref _editEmergencyContactName, value); }
    public string EditEmergencyContactPhone { get => _editEmergencyContactPhone; set => SetField(ref _editEmergencyContactPhone, value); }
    public string EditAddress { get => _editAddress; set => SetField(ref _editAddress, value); }
    public string EditRemark { get => _editRemark; set => SetField(ref _editRemark, value); }
    public string EditChangeReason { get => _editChangeReason; set => SetField(ref _editChangeReason, value); }
    public string DecryptedPiiDisplay { get => _decryptedPiiDisplay; set => SetField(ref _decryptedPiiDisplay, value); }

    // Transfer / Promote / Resign
    public HrDeptOption? TransferTargetDept { get => _transferTargetDept; set => SetField(ref _transferTargetDept, value); }
    public HrRoleOption? TransferTargetRole { get => _transferTargetRole; set => SetField(ref _transferTargetRole, value); }
    public string TransferReason { get => _transferReason; set => SetField(ref _transferReason, value); }
    public string PromoteNewTitle { get => _promoteNewTitle; set => SetField(ref _promoteNewTitle, value); }
    public int PromotePrescribeLevel { get => _promotePrescribeLevel; set => SetField(ref _promotePrescribeLevel, value); }
    public int PromoteSurgeryLevel { get => _promoteSurgeryLevel; set => SetField(ref _promoteSurgeryLevel, value); }
    public int PromoteEmrSignLevel { get => _promoteEmrSignLevel; set => SetField(ref _promoteEmrSignLevel, value); }
    public int PromoteSpecialExamLevel { get => _promoteSpecialExamLevel; set => SetField(ref _promoteSpecialExamLevel, value); }
    public string PromoteReason { get => _promoteReason; set => SetField(ref _promoteReason, value); }
    public string ResignType { get => _resignType; set => SetField(ref _resignType, value); }
    public string ResignReason { get => _resignReason; set => SetField(ref _resignReason, value); }

    // Onboard form properties
    public string ObFullName { get => _obFullName; set => SetField(ref _obFullName, value); }
    public string ObEmployeeNo { get => _obEmployeeNo; set => SetField(ref _obEmployeeNo, value); }
    public string ObGender { get => _obGender; set => SetField(ref _obGender, value); }
    public string ObIdCardNo { get => _obIdCardNo; set => SetField(ref _obIdCardNo, value); }
    public string ObPhone { get => _obPhone; set => SetField(ref _obPhone, value); }
    public string ObEmail { get => _obEmail; set => SetField(ref _obEmail, value); }
    public string ObEducation { get => _obEducation; set => SetField(ref _obEducation, value); }
    public string ObGraduatedSchool { get => _obGraduatedSchool; set => SetField(ref _obGraduatedSchool, value); }
    public string ObMajorName { get => _obMajorName; set => SetField(ref _obMajorName, value); }
    public string ObPoliticalStatus { get => _obPoliticalStatus; set => SetField(ref _obPoliticalStatus, value); }
    public HrDeptOption? ObSelectedDept { get => _obSelectedDept; set => SetField(ref _obSelectedDept, value); }
    public string ObStaffCategory { get => _obStaffCategory; set => SetField(ref _obStaffCategory, value); }
    public string ObEmploymentType { get => _obEmploymentType; set => SetField(ref _obEmploymentType, value); }
    public string ObProfessionalTitle { get => _obProfessionalTitle; set => SetField(ref _obProfessionalTitle, value); }
    public string ObJobPosition { get => _obJobPosition; set => SetField(ref _obJobPosition, value); }
    public string ObHireDateText { get => _obHireDateText; set => SetField(ref _obHireDateText, value); }
    public string ObContractEndDateText { get => _obContractEndDateText; set => SetField(ref _obContractEndDateText, value); }
    public bool ObAutoCreateAccount { get => _obAutoCreateAccount; set => SetField(ref _obAutoCreateAccount, value); }
    public string ObLoginUserName { get => _obLoginUserName; set => SetField(ref _obLoginUserName, value); }
    public string ObInitialPassword { get => _obInitialPassword; set => SetField(ref _obInitialPassword, value); }
    public HrRoleOption? ObSelectedRole { get => _obSelectedRole; set => SetField(ref _obSelectedRole, value); }
    public int ObPrescribeLevel { get => _obPrescribeLevel; set => SetField(ref _obPrescribeLevel, value); }
    public int ObSurgeryLevel { get => _obSurgeryLevel; set => SetField(ref _obSurgeryLevel, value); }
    public int ObEmrSignLevel { get => _obEmrSignLevel; set => SetField(ref _obEmrSignLevel, value); }
    public int ObSpecialExamLevel { get => _obSpecialExamLevel; set => SetField(ref _obSpecialExamLevel, value); }
    public string ObRemark { get => _obRemark; set => SetField(ref _obRemark, value); }

    // Credential form properties
    public string CredType { get => _credType; set => SetField(ref _credType, value); }
    public string CredNo { get => _credNo; set => SetField(ref _credNo, value); }
    public string CredScope { get => _credScope; set => SetField(ref _credScope, value); }
    public string CredIssuer { get => _credIssuer; set => SetField(ref _credIssuer, value); }
    public string CredIssueDateText { get => _credIssueDateText; set => SetField(ref _credIssueDateText, value); }
    public string CredExpiryDateText { get => _credExpiryDateText; set => SetField(ref _credExpiryDateText, value); }
    public int CredPrivilegeLevel { get => _credPrivilegeLevel; set => SetField(ref _credPrivilegeLevel, value); }

    // Contract form properties
    public string ContractType { get => _contractType; set => SetField(ref _contractType, value); }
    public string ContractStartText { get => _contractStartText; set => SetField(ref _contractStartText, value); }
    public string ContractEndText { get => _contractEndText; set => SetField(ref _contractEndText, value); }
    public string ContractRemark { get => _contractRemark; set => SetField(ref _contractRemark, value); }

    private void PopulateEditorFromSelected()
    {
        DecryptedPiiDisplay = string.Empty;
        if (_selectedEmployee == null) return;
        EditFullName = _selectedEmployee.FullName;
        EditGender = string.IsNullOrWhiteSpace(_selectedEmployee.Gender) ? "MALE" : _selectedEmployee.Gender;
        EditPhone = string.Empty; // 留空表示不修改已加密手机号
        EditEmail = _selectedEmployee.Email ?? string.Empty;
        EditEducation = _selectedEmployee.Education ?? string.Empty;
        EditGraduatedSchool = _selectedEmployee.GraduatedSchool ?? string.Empty;
        EditMajorName = _selectedEmployee.MajorName ?? string.Empty;
        EditPoliticalStatus = _selectedEmployee.PoliticalStatus ?? string.Empty;
        EditProfessionalTitle = string.IsNullOrWhiteSpace(_selectedEmployee.ProfessionalTitle) ? "RESIDENT" : _selectedEmployee.ProfessionalTitle;
        EditJobPosition = _selectedEmployee.JobPosition ?? string.Empty;
        EditStaffCategory = string.IsNullOrWhiteSpace(_selectedEmployee.StaffCategory) ? "DOCTOR" : _selectedEmployee.StaffCategory;
        EditEmploymentType = string.IsNullOrWhiteSpace(_selectedEmployee.EmploymentType) ? "CONTRACT" : _selectedEmployee.EmploymentType;
        EditEmergencyContactName = _selectedEmployee.EmergencyContactName ?? string.Empty;
        EditEmergencyContactPhone = _selectedEmployee.EmergencyContactPhone ?? string.Empty;
        EditAddress = _selectedEmployee.Address ?? string.Empty;
        EditRemark = _selectedEmployee.Remark ?? string.Empty;
        PromoteNewTitle = EditProfessionalTitle;
        TransferTargetDept = Departments.FirstOrDefault(d => d.Id == _selectedEmployee.PrimaryDeptId) ?? Departments.FirstOrDefault();
    }

    public async Task LoadAllAsync()
    {
        IsBusy = true;
        StatusMessage = "正在加载全院人力资源档案...";
        try
        {
            await LoadReferenceDataAsync();
            await LoadSummaryAsync();
            await LoadEmployeesAsync();
            await LoadExpiringCredentialsAsync();
            await LoadChangeRecordsAsync();
            StatusMessage = $"已同步 {Employees.Count} 条职工档案，{ChangeRecords.Count} 条人事异动记录";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败：{ex.Message}";
            _shell?.ShowToast(StatusMessage, "Error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadReferenceDataAsync()
    {
        using var http = AuthenticatedHttpClientFactory.Create();
        try
        {
            var deptRes = await http.GetFromJsonAsync<ClientApiEnvelope<List<HrDeptOption>>>("/api/v4/platform/departments", JsonOpts);
            if (deptRes?.Data != null)
            {
                Departments.Clear();
                foreach (var d in deptRes.Data) Departments.Add(d);
                ObSelectedDept ??= Departments.FirstOrDefault();
                TransferTargetDept ??= Departments.FirstOrDefault();
            }
        }
        catch { /* 忽略非关键字典加载异常 */ }

        try
        {
            var roleRes = await http.GetFromJsonAsync<ClientApiEnvelope<List<HrRoleOption>>>("/api/v4/platform/roles", JsonOpts);
            if (roleRes?.Data != null)
            {
                Roles.Clear();
                foreach (var r in roleRes.Data) Roles.Add(r);
                ObSelectedRole ??= Roles.FirstOrDefault();
                TransferTargetRole ??= Roles.FirstOrDefault();
            }
        }
        catch { /* 忽略非关键字典加载异常 */ }
    }

    private async Task LoadSummaryAsync()
    {
        using var http = AuthenticatedHttpClientFactory.Create();
        var res = await http.GetFromJsonAsync<ClientApiEnvelope<HrSummaryDto>>("/api/v4/hr/summary", JsonOpts);
        if (res?.Data != null)
        {
            TotalActive = res.Data.TotalActive;
            DoctorCount = res.Data.DoctorCount;
            NurseCount = res.Data.NurseCount;
            ResignedCount = res.Data.ResignedOrRetiredCount;
            ExpiringCredentialsCount = res.Data.ExpiringCredentials60d;
            ExpiringContractsCount = res.Data.ExpiringContracts60d;
        }
    }

    private async Task LoadEmployeesAsync()
    {
        using var http = AuthenticatedHttpClientFactory.Create();
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(SearchKeyword))
            qs.Add("keyword=" + Uri.EscapeDataString(SearchKeyword.Trim()));
        if (!string.IsNullOrWhiteSpace(FilterStatus) && FilterStatus != "ALL")
            qs.Add("status=" + Uri.EscapeDataString(FilterStatus));
        if (!string.IsNullOrWhiteSpace(FilterCategory) && FilterCategory != "ALL")
            qs.Add("staffCategory=" + Uri.EscapeDataString(FilterCategory));

        string url = "/api/v4/hr/employees" + (qs.Count > 0 ? "?" + string.Join("&", qs) : string.Empty);
        var res = await http.GetFromJsonAsync<ClientApiEnvelope<List<HrEmployeeRow>>>(url, JsonOpts);
        string? prevId = SelectedEmployee?.Id;
        Employees.Clear();
        if (res?.Data != null)
        {
            foreach (var row in res.Data) Employees.Add(row);
        }
        SelectedEmployee = (prevId != null ? Employees.FirstOrDefault(e => e.Id == prevId) : null) ?? Employees.FirstOrDefault();
    }

    private async Task LoadSelectedDetailsAsync(string empId)
    {
        using var http = AuthenticatedHttpClientFactory.Create();
        try
        {
            var cRes = await http.GetFromJsonAsync<ClientApiEnvelope<List<HrCredentialRow>>>($"/api/v4/hr/employees/{empId}/credentials", JsonOpts);
            SelectedCredentials.Clear();
            if (cRes?.Data != null)
                foreach (var c in cRes.Data) SelectedCredentials.Add(c);

            var ctRes = await http.GetFromJsonAsync<ClientApiEnvelope<List<HrContractRow>>>($"/api/v4/hr/employees/{empId}/contracts", JsonOpts);
            SelectedContracts.Clear();
            if (ctRes?.Data != null)
                foreach (var c in ctRes.Data) SelectedContracts.Add(c);
        }
        catch { /* 保持已有列表 */ }
    }

    private async Task LoadExpiringCredentialsAsync()
    {
        using var http = AuthenticatedHttpClientFactory.Create();
        var res = await http.GetFromJsonAsync<ClientApiEnvelope<List<HrCredentialRow>>>("/api/v4/hr/credentials/expiring?days=90", JsonOpts);
        ExpiringCredentials.Clear();
        if (res?.Data != null)
            foreach (var item in res.Data) ExpiringCredentials.Add(item);
    }

    private async Task LoadChangeRecordsAsync()
    {
        using var http = AuthenticatedHttpClientFactory.Create();
        var res = await http.GetFromJsonAsync<ClientApiEnvelope<List<HrChangeRow>>>("/api/v4/hr/changes", JsonOpts);
        ChangeRecords.Clear();
        if (res?.Data != null)
            foreach (var item in res.Data) ChangeRecords.Add(item);
    }

    private async Task SubmitOnboardAsync()
    {
        if (string.IsNullOrWhiteSpace(ObFullName))
        {
            _shell?.ShowToast("请填写新员工姓名。", "Warning");
            return;
        }
        if (ObSelectedDept == null)
        {
            _shell?.ShowToast("请选择主执业/归属科室。", "Warning");
            return;
        }
        int.TryParse(ObHireDateText, out int hireDateInt);
        int? contractEndInt = int.TryParse(ObContractEndDateText, out int parsedEnd) && parsedEnd > 20000101 ? parsedEnd : null;

        IsBusy = true;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new
            {
                FullName = ObFullName.Trim(),
                EmployeeNo = string.IsNullOrWhiteSpace(ObEmployeeNo) ? null : ObEmployeeNo.Trim(),
                Gender = ObGender,
                IdCardNo = string.IsNullOrWhiteSpace(ObIdCardNo) ? null : ObIdCardNo.Trim(),
                Phone = string.IsNullOrWhiteSpace(ObPhone) ? null : ObPhone.Trim(),
                Email = string.IsNullOrWhiteSpace(ObEmail) ? null : ObEmail.Trim(),
                Education = ObEducation,
                GraduatedSchool = string.IsNullOrWhiteSpace(ObGraduatedSchool) ? null : ObGraduatedSchool.Trim(),
                MajorName = string.IsNullOrWhiteSpace(ObMajorName) ? null : ObMajorName.Trim(),
                PoliticalStatus = ObPoliticalStatus,
                HireDateInt = hireDateInt,
                ContractEndDateInt = contractEndInt,
                EmploymentType = ObEmploymentType,
                StaffCategory = ObStaffCategory,
                PrimaryDeptId = ObSelectedDept.Id,
                ProfessionalTitle = ObProfessionalTitle,
                JobPosition = string.IsNullOrWhiteSpace(ObJobPosition) ? null : ObJobPosition.Trim(),
                AutoCreateUserAccount = ObAutoCreateAccount,
                LoginUserName = string.IsNullOrWhiteSpace(ObLoginUserName) ? null : ObLoginUserName.Trim(),
                InitialPassword = string.IsNullOrWhiteSpace(ObInitialPassword) ? null : ObInitialPassword.Trim(),
                InitialRoleId = ObSelectedRole?.Id,
                PrescribeLevel = ObPrescribeLevel,
                SurgeryLevel = ObSurgeryLevel,
                EmrSignLevel = ObEmrSignLevel,
                SpecialExamLevel = ObSpecialExamLevel,
                Remark = string.IsNullOrWhiteSpace(ObRemark) ? null : ObRemark.Trim()
            };

            var resp = await http.PostAsJsonAsync("/api/v4/hr/employees/onboard", payload);
            var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
            if (env != null && env.Code == 0)
            {
                _shell?.ShowToast(env.Message ?? "新员工入职登记成功", "Success");
                ObFullName = string.Empty;
                ObEmployeeNo = string.Empty;
                ObIdCardNo = string.Empty;
                ObPhone = string.Empty;
                ObEmail = string.Empty;
                ObLoginUserName = string.Empty;
                ObInitialPassword = string.Empty;
                ObRemark = string.Empty;
                await LoadAllAsync();
                SelectedTabIndex = 0;
            }
            else
            {
                _shell?.ShowToast(env?.Message ?? $"入职登记失败（HTTP {(int)resp.StatusCode}）", "Error");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"入职请求异常：{ex.Message}", "Error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveProfileAsync()
    {
        if (SelectedEmployee == null) return;
        IsBusy = true;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new
            {
                FullName = string.IsNullOrWhiteSpace(EditFullName) ? null : EditFullName.Trim(),
                Gender = EditGender,
                Phone = string.IsNullOrWhiteSpace(EditPhone) ? null : EditPhone.Trim(),
                Email = string.IsNullOrWhiteSpace(EditEmail) ? null : EditEmail.Trim(),
                Education = string.IsNullOrWhiteSpace(EditEducation) ? null : EditEducation.Trim(),
                GraduatedSchool = string.IsNullOrWhiteSpace(EditGraduatedSchool) ? null : EditGraduatedSchool.Trim(),
                MajorName = string.IsNullOrWhiteSpace(EditMajorName) ? null : EditMajorName.Trim(),
                PoliticalStatus = string.IsNullOrWhiteSpace(EditPoliticalStatus) ? null : EditPoliticalStatus.Trim(),
                ProfessionalTitle = EditProfessionalTitle,
                JobPosition = string.IsNullOrWhiteSpace(EditJobPosition) ? null : EditJobPosition.Trim(),
                StaffCategory = EditStaffCategory,
                EmploymentType = EditEmploymentType,
                EmergencyContactName = string.IsNullOrWhiteSpace(EditEmergencyContactName) ? null : EditEmergencyContactName.Trim(),
                EmergencyContactPhone = string.IsNullOrWhiteSpace(EditEmergencyContactPhone) ? null : EditEmergencyContactPhone.Trim(),
                Address = string.IsNullOrWhiteSpace(EditAddress) ? null : EditAddress.Trim(),
                Remark = string.IsNullOrWhiteSpace(EditRemark) ? null : EditRemark.Trim(),
                ChangeReason = string.IsNullOrWhiteSpace(EditChangeReason) ? "人事档案信息修改" : EditChangeReason.Trim()
            };

            var resp = await http.PutAsJsonAsync($"/api/v4/hr/employees/{SelectedEmployee.Id}", payload);
            var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
            if (env != null && env.Code == 0)
            {
                _shell?.ShowToast("人事信息修改已保存，并已同步系统账号与变更轨迹。", "Success");
                await LoadAllAsync();
            }
            else
            {
                _shell?.ShowToast(env?.Message ?? "保存失败", "Error");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"保存异常：{ex.Message}", "Error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DecryptPiiAsync()
    {
        if (SelectedEmployee == null) return;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var resp = await http.PostAsync($"/api/v4/hr/employees/{SelectedEmployee.Id}/decrypt-pii?reason={Uri.EscapeDataString("人事档案核查")}", null);
            var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<HrPiiDto>>(JsonOpts);
            if ((env?.Code is 200 or 0) && env.Data != null)
            {
                DecryptedPiiDisplay = $"明文证件：{env.Data.IdCardNo ?? "未登记"} | 明文手机：{env.Data.Phone ?? "未登记"}（已记入不可篡改审计日志）";
                _shell?.ShowToast("已完成 SM4 解密并记录审计日志。", "Info");
            }
            else
            {
                _shell?.ShowToast(env?.Message ?? "解密失败", "Error");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"解密异常：{ex.Message}", "Error");
        }
    }

    private async Task TransferEmployeeAsync()
    {
        if (SelectedEmployee == null || TransferTargetDept == null)
        {
            _shell?.ShowToast("请选择目标科室。", "Warning");
            return;
        }
        IsBusy = true;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new
            {
                TargetDeptId = TransferTargetDept.Id,
                TargetRoleId = TransferTargetRole?.Id,
                NewDirectSupervisorId = (string?)null,
                EffectiveDateInt = int.Parse(DateTime.Now.ToString("yyyyMMdd")),
                Reason = TransferReason
            };
            var resp = await http.PostAsJsonAsync($"/api/v4/hr/employees/{SelectedEmployee.Id}/transfer", payload);
            var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
            if ((env?.Code is 200 or 0))
            {
                _shell?.ShowToast("调科/调岗已生效，默认科室权限已同步。", "Success");
                await LoadAllAsync();
            }
            else
            {
                _shell?.ShowToast(env?.Message ?? "调科失败", "Error");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"调科异常：{ex.Message}", "Error");
        }
        finally { IsBusy = false; }
    }

    private async Task PromoteEmployeeAsync()
    {
        if (SelectedEmployee == null) return;
        IsBusy = true;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new
            {
                NewProfessionalTitle = PromoteNewTitle,
                NewJobPosition = string.IsNullOrWhiteSpace(EditJobPosition) ? null : EditJobPosition.Trim(),
                NewPrescribeLevel = (int?)PromotePrescribeLevel,
                NewSurgeryLevel = (int?)PromoteSurgeryLevel,
                NewEmrSignLevel = (int?)PromoteEmrSignLevel,
                NewSpecialExamLevel = (int?)PromoteSpecialExamLevel,
                EffectiveDateInt = int.Parse(DateTime.Now.ToString("yyyyMMdd")),
                Reason = PromoteReason
            };
            var resp = await http.PostAsJsonAsync($"/api/v4/hr/employees/{SelectedEmployee.Id}/promote", payload);
            var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
            if ((env?.Code is 200 or 0))
            {
                _shell?.ShowToast("职称晋升与临床分级授权同步成功。", "Success");
                await LoadAllAsync();
            }
            else
            {
                _shell?.ShowToast(env?.Message ?? "晋升失败", "Error");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"晋升异常：{ex.Message}", "Error");
        }
        finally { IsBusy = false; }
    }

    private async Task ResignEmployeeAsync()
    {
        if (SelectedEmployee == null) return;
        IsBusy = true;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new
            {
                EmploymentStatus = ResignType,
                ResignDateInt = int.Parse(DateTime.Now.ToString("yyyyMMdd")),
                Reason = ResignReason,
                DisableSystemAccount = true
            };
            var resp = await http.PostAsJsonAsync($"/api/v4/hr/employees/{SelectedEmployee.Id}/resign", payload);
            var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
            if ((env?.Code is 200 or 0))
            {
                _shell?.ShowToast("离职/退休手续已办结，系统工号已停用并强制踢出在线会话。", "Warning");
                await LoadAllAsync();
            }
            else
            {
                _shell?.ShowToast(env?.Message ?? "办理离职失败", "Error");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"办理离职异常：{ex.Message}", "Error");
        }
        finally { IsBusy = false; }
    }

    private async Task AddCredentialAsync()
    {
        if (SelectedEmployee == null || string.IsNullOrWhiteSpace(CredNo))
        {
            _shell?.ShowToast("请先选择职工并填写证照编号。", "Warning");
            return;
        }
        int? issueInt = int.TryParse(CredIssueDateText, out int iVal) ? iVal : null;
        int? expiryInt = int.TryParse(CredExpiryDateText, out int eVal) ? eVal : null;
        IsBusy = true;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new
            {
                CredentialType = CredType,
                CertNo = CredNo.Trim(),
                IssuingAuthority = CredIssuer,
                IssueDateInt = issueInt,
                ExpiryDateInt = expiryInt,
                GrantedLevel = CredPrivilegeLevel,
                Remark = CredScope,
                SyncMedicalPrivilege = true
            };
            var resp = await http.PostAsJsonAsync($"/api/v4/hr/employees/{SelectedEmployee.Id}/credentials", payload);
            var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
            if ((env?.Code is 200 or 0))
            {
                _shell?.ShowToast("执业证照登记成功，已联动同步临床权限等级。", "Success");
                CredNo = string.Empty;
                await LoadSelectedDetailsAsync(SelectedEmployee.Id);
                await LoadExpiringCredentialsAsync();
                await LoadSummaryAsync();
            }
            else
            {
                _shell?.ShowToast(env?.Message ?? "登记证照失败", "Error");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"登记证照异常：{ex.Message}", "Error");
        }
        finally { IsBusy = false; }
    }

    private async Task AddContractAsync()
    {
        if (SelectedEmployee == null)
        {
            _shell?.ShowToast("请先选择职工。", "Warning");
            return;
        }
        int.TryParse(ContractStartText, out int startInt);
        int? endInt = int.TryParse(ContractEndText, out int eVal) && eVal > 20000101 ? eVal : null;
        IsBusy = true;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new
            {
                ContractType = ContractType,
                StartDateInt = startInt,
                EndDateInt = endInt,
                Remark = ContractRemark
            };
            var resp = await http.PostAsJsonAsync($"/api/v4/hr/employees/{SelectedEmployee.Id}/contracts", payload);
            var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
            if ((env?.Code is 200 or 0))
            {
                _shell?.ShowToast("劳动合同签订/续签记录已保存。", "Success");
                ContractRemark = string.Empty;
                await LoadSelectedDetailsAsync(SelectedEmployee.Id);
                await LoadSummaryAsync();
            }
            else
            {
                _shell?.ShowToast(env?.Message ?? "合同登记失败", "Error");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast($"合同登记异常：{ex.Message}", "Error");
        }
        finally { IsBusy = false; }
    }
}
