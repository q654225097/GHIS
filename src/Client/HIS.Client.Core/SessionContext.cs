using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HIS.Client.Core;

/// <summary>
/// Singleton session context holding the currently logged-in user's identity and permissions.
/// Set after successful login; consumed throughout the client for role-aware UI gating.
/// </summary>
public sealed class SessionContext : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private static SessionContext? _instance;
    public static SessionContext Current => _instance ??= new SessionContext();

    private string _userId = string.Empty;
    private string _userName = string.Empty;
    private string _displayName = string.Empty;
    private string _employeeNo = string.Empty;
    private string _roleCode = string.Empty;
    private string _roleName = string.Empty;
    private string _activeDeptId = string.Empty;
    private string _activeDeptName = string.Empty;
    private string _activeDeptCategory = string.Empty;
    private string? _activeWardId;
    private string _accessToken = string.Empty;
    private int _prescribeLevel;
    private int _surgeryLevel;
    private int _emrSignLevel;
    private bool _isLoggedIn;

    public string UserId        { get => _userId;      set => Set(ref _userId, value); }
    public string UserName      { get => _userName;    set => Set(ref _userName, value); }
    public string DisplayName   { get => _displayName; set => Set(ref _displayName, value); }
    public string EmployeeNo    { get => _employeeNo;  set => Set(ref _employeeNo, value); }
    public string RoleCode      { get => _roleCode;    set { Set(ref _roleCode, value); NotifyRoleFlags(); } }
    public string RoleName      { get => _roleName;    set => Set(ref _roleName, value); }
    public string ActiveDeptId       { get => _activeDeptId;       set => Set(ref _activeDeptId, value); }
    public string ActiveDeptName     { get => _activeDeptName;     set => Set(ref _activeDeptName, value); }
    public string ActiveDeptCategory { get => _activeDeptCategory; set => Set(ref _activeDeptCategory, value); }
    public string? ActiveWardId      { get => _activeWardId;       set => Set(ref _activeWardId, value); }
    public string AccessToken        { get => _accessToken;        set => Set(ref _accessToken, value); }
    public int PrescribeLevel  { get => _prescribeLevel;  set => Set(ref _prescribeLevel, value); }
    public int SurgeryLevel    { get => _surgeryLevel;    set => Set(ref _surgeryLevel, value); }
    public int EmrSignLevel    { get => _emrSignLevel;    set => Set(ref _emrSignLevel, value); }
    public bool IsLoggedIn     { get => _isLoggedIn;      set => Set(ref _isLoggedIn, value); }

    // --- Role Gate Properties ---
    public bool IsAdmin       => RoleCode == "ROLE-ADMIN";
    public bool IsOpdDoctor   => RoleCode == "ROLE-OPD-DOC";
    public bool IsIpdResident => RoleCode == "ROLE-IPD-RESIDENT";
    public bool IsIpdAttending=> RoleCode == "ROLE-IPD-ATTENDING";
    public bool IsWardNurse   => RoleCode == "ROLE-WARD-NURSE";
    public bool IsPharmacist  => RoleCode == "ROLE-PHARMACIST";
    public bool IsCashier     => RoleCode == "ROLE-CASHIER";

    public bool CanPrescribe  => PrescribeLevel >= 1 && (IsOpdDoctor || IsIpdResident || IsIpdAttending || IsAdmin);
    public bool CanSignEmr    => EmrSignLevel >= 1 && (IsOpdDoctor || IsIpdResident || IsIpdAttending || IsAdmin);
    public bool CanSurgery    => SurgeryLevel >= 1 && (IsIpdAttending || IsAdmin);
    public bool CanManageUsers=> IsAdmin;

    public bool CanSeeOpdStation    => IsAdmin || IsOpdDoctor;
    public bool CanSeeIpdStation    => IsAdmin || IsIpdResident || IsIpdAttending;
    public bool CanSeeWardStation   => IsAdmin || IsWardNurse || IsIpdResident || IsIpdAttending;
    public bool CanSeeMedTech       => IsAdmin || IsOpdDoctor || IsIpdResident || IsIpdAttending;
    public bool CanSeePharmacy      => IsAdmin || IsPharmacist;
    public bool CanSeeEmrOfd        => IsAdmin || IsOpdDoctor || IsIpdResident || IsIpdAttending;
    public bool CanSeeDynFormConfig => IsAdmin;
    public bool CanSeeAdminPanel    => IsAdmin;

    /// <summary>Populate session from a successful login response.</summary>
    public void SetFromLogin(
        string userId, string userName, string displayName,
        string roleCode, string roleName,
        string accessToken,
        string activeDeptId, string activeDeptName, string activeDeptCategory,
        string? activeWardId,
        int prescribeLevel, int surgeryLevel, int emrSignLevel)
    {
        UserId = userId;
        UserName = userName;
        DisplayName = displayName;
        RoleCode = roleCode;
        RoleName = roleName;
        AccessToken = accessToken;
        ActiveDeptId = activeDeptId;
        ActiveDeptName = activeDeptName;
        ActiveDeptCategory = activeDeptCategory;
        ActiveWardId = activeWardId;
        PrescribeLevel = prescribeLevel;
        SurgeryLevel = surgeryLevel;
        EmrSignLevel = emrSignLevel;
        IsLoggedIn = true;
    }

    public void Logout()
    {
        UserId = string.Empty;
        UserName = string.Empty;
        DisplayName = string.Empty;
        RoleCode = string.Empty;
        RoleName = string.Empty;
        AccessToken = string.Empty;
        ActiveDeptId = string.Empty;
        ActiveDeptName = string.Empty;
        ActiveDeptCategory = string.Empty;
        ActiveWardId = null;
        PrescribeLevel = 0;
        SurgeryLevel = 0;
        EmrSignLevel = 0;
        IsLoggedIn = false;
    }

    private void NotifyRoleFlags()
    {
        var flags = new[]
        {
            nameof(IsAdmin), nameof(IsOpdDoctor), nameof(IsIpdResident), nameof(IsIpdAttending),
            nameof(IsWardNurse), nameof(IsPharmacist), nameof(IsCashier),
            nameof(CanPrescribe), nameof(CanSignEmr), nameof(CanSurgery), nameof(CanManageUsers),
            nameof(CanSeeOpdStation), nameof(CanSeeIpdStation), nameof(CanSeeWardStation),
            nameof(CanSeeMedTech), nameof(CanSeePharmacy), nameof(CanSeeEmrOfd),
            nameof(CanSeeDynFormConfig), nameof(CanSeeAdminPanel)
        };
        foreach (var f in flags)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(f));
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

