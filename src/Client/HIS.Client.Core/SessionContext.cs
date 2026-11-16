using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HIS.Client.Core;

public sealed class SessionContext : INotifyPropertyChanged
{
	private static SessionContext? _instance;

	private string _userId = string.Empty;

	private string _userName = string.Empty;

	private string _displayName = string.Empty;

	private string _employeeNo = string.Empty;

	private string _roleCode = string.Empty;

	private string _roleName = string.Empty;

	private string _roleId = string.Empty;

	private string _activePostId = string.Empty;

	private string _activeDeptId = string.Empty;

	private string _activeDeptName = string.Empty;

	private string _activeDeptCategory = string.Empty;

	private string? _activeWardId;

	private string _accessToken = string.Empty;

	private string _refreshToken = string.Empty;

	private long _accessTokenExpiresAtMs;

	private long _refreshTokenExpiresAtMs;

	private int _prescribeLevel;

	private int _surgeryLevel;

	private int _emrSignLevel;

	private bool _isLoggedIn;

	public static SessionContext Current => _instance ?? (_instance = new SessionContext());

	public string UserId
	{
		get
		{
			return _userId;
		}
		set
		{
			Set(ref _userId, value, "UserId");
		}
	}

	public string UserName
	{
		get
		{
			return _userName;
		}
		set
		{
			Set(ref _userName, value, "UserName");
		}
	}

	public string DisplayName
	{
		get
		{
			return _displayName;
		}
		set
		{
			Set(ref _displayName, value, "DisplayName");
		}
	}

	public string EmployeeNo
	{
		get
		{
			return _employeeNo;
		}
		set
		{
			Set(ref _employeeNo, value, "EmployeeNo");
		}
	}

	public string RoleCode
	{
		get
		{
			return _roleCode;
		}
		set
		{
			Set(ref _roleCode, NormalizeRoleCode(value), "RoleCode");
			NotifyRoleFlags();
		}
	}

	public string RoleName
	{
		get
		{
			return _roleName;
		}
		set
		{
			Set(ref _roleName, value, "RoleName");
		}
	}

	public string RoleId
	{
		get
		{
			return _roleId;
		}
		set
		{
			Set(ref _roleId, value, "RoleId");
		}
	}

	public string ActivePostId
	{
		get
		{
			return _activePostId;
		}
		set
		{
			Set(ref _activePostId, value, "ActivePostId");
		}
	}

	public string ActiveDeptId
	{
		get
		{
			return _activeDeptId;
		}
		set
		{
			Set(ref _activeDeptId, value, "ActiveDeptId");
		}
	}

	public string ActiveDeptName
	{
		get
		{
			return _activeDeptName;
		}
		set
		{
			Set(ref _activeDeptName, value, "ActiveDeptName");
		}
	}

	public string ActiveDeptCategory
	{
		get
		{
			return _activeDeptCategory;
		}
		set
		{
			if (Set(ref _activeDeptCategory, value, "ActiveDeptCategory"))
			{
				NotifyRoleFlags();
			}
		}
	}

	public string? ActiveWardId
	{
		get
		{
			return _activeWardId;
		}
		set
		{
			Set(ref _activeWardId, value, "ActiveWardId");
		}
	}

	public string AccessToken
	{
		get
		{
			return _accessToken;
		}
		set
		{
			Set(ref _accessToken, value, "AccessToken");
		}
	}

	public string RefreshToken
	{
		get
		{
			return _refreshToken;
		}
		set
		{
			Set(ref _refreshToken, value, "RefreshToken");
		}
	}

	public long AccessTokenExpiresAtMs
	{
		get
		{
			return _accessTokenExpiresAtMs;
		}
		set
		{
			Set(ref _accessTokenExpiresAtMs, value, "AccessTokenExpiresAtMs");
		}
	}

	public long RefreshTokenExpiresAtMs
	{
		get
		{
			return _refreshTokenExpiresAtMs;
		}
		set
		{
			Set(ref _refreshTokenExpiresAtMs, value, "RefreshTokenExpiresAtMs");
		}
	}

	public int PrescribeLevel
	{
		get
		{
			return _prescribeLevel;
		}
		set
		{
			if (Set(ref _prescribeLevel, value, "PrescribeLevel"))
			{
				NotifyRoleFlags();
			}
		}
	}

	public int SurgeryLevel
	{
		get
		{
			return _surgeryLevel;
		}
		set
		{
			Set(ref _surgeryLevel, value, "SurgeryLevel");
		}
	}

	public int EmrSignLevel
	{
		get
		{
			return _emrSignLevel;
		}
		set
		{
			if (Set(ref _emrSignLevel, value, "EmrSignLevel"))
			{
				NotifyRoleFlags();
			}
		}
	}

	public bool IsLoggedIn
	{
		get
		{
			return _isLoggedIn;
		}
		set
		{
			Set(ref _isLoggedIn, value, "IsLoggedIn");
		}
	}

	public bool IsAdmin => IsRole("ADMIN");

	public bool IsOpdDoctor => IsActiveOpdClinical && !IsWardNurse && !IsPharmacist && !IsCashier;

	public bool IsIpdResident => IsRole("IPD-RESIDENT");

	public bool IsIpdAttending => IsRole("IPD-ATTENDING");

	public bool IsWardNurse => IsRole("WARD-NURSE");

	public bool IsPharmacist => IsRole("PHARMACIST");

	public bool IsCashier => IsRole("CASHIER");

	public bool CanPrescribe => PrescribeLevel >= 1 && (IsOpdDoctor || IsIpdResident || IsIpdAttending || IsAdmin);

	public bool CanSignEmr => (EmrSignLevel >= 1 && (IsOpdDoctor || IsIpdResident || IsIpdAttending || IsAdmin)) || IsWardNurse;

	public bool CanSurgery => SurgeryLevel >= 1 && (IsIpdAttending || IsAdmin);

	public bool CanManageUsers => IsAdmin;

	public bool CanSeeOpdStation => IsAdmin || IsOpdDoctor;

	public bool CanSeeIpdStation => IsAdmin || IsIpdResident || IsIpdAttending;

	public bool CanSeeWardStation => IsAdmin || IsWardNurse || IsIpdResident || IsIpdAttending;

	public bool CanSeeMedTech => IsAdmin || IsOpdDoctor || IsIpdResident || IsIpdAttending;

	public bool CanSeePharmacy => IsAdmin || IsPharmacist;

	public bool CanSeeFinance => IsAdmin || IsCashier;

	public bool CanSeeEmrOfd => IsAdmin || IsOpdDoctor || IsIpdResident || IsIpdAttending || IsWardNurse;

	public bool CanSeeDynFormConfig => IsAdmin;

	public bool CanSeeAdminPanel => IsAdmin;

	public ObservableCollection<LoginContextSnapshot> AvailableContexts { get; } = new ObservableCollection<LoginContextSnapshot>();

	public event PropertyChangedEventHandler? PropertyChanged;

	public event EventHandler? SessionExpired;

	public void SetFromLogin(string userId, string userName, string displayName, string roleCode, string roleName, string accessToken, string activeDeptId, string activeDeptName, string activeDeptCategory, string? activeWardId, int prescribeLevel, int surgeryLevel, int emrSignLevel, string? roleId = null, string? postId = null, string? employeeNo = null, IReadOnlyList<LoginContextSnapshot>? availableContexts = null, string? refreshToken = null, long accessTokenExpiresAtMs = 0, long refreshTokenExpiresAtMs = 0)
	{
		UserId = userId;
		UserName = userName;
		DisplayName = displayName;
		RoleCode = roleCode;
		RoleName = roleName;
		RoleId = roleId ?? string.Empty;
		ActivePostId = postId ?? string.Empty;
		EmployeeNo = employeeNo ?? string.Empty;
		AccessToken = accessToken;
		RefreshToken = refreshToken ?? string.Empty;
		AccessTokenExpiresAtMs = accessTokenExpiresAtMs;
		RefreshTokenExpiresAtMs = refreshTokenExpiresAtMs;
		ActiveDeptId = activeDeptId;
		ActiveDeptName = activeDeptName;
		ActiveDeptCategory = activeDeptCategory;
		ActiveWardId = activeWardId;
		PrescribeLevel = prescribeLevel;
		SurgeryLevel = surgeryLevel;
		EmrSignLevel = emrSignLevel;
		AvailableContexts.Clear();
		foreach (LoginContextSnapshot item in availableContexts ?? Array.Empty<LoginContextSnapshot>())
		{
			AvailableContexts.Add(item);
		}
		IsLoggedIn = true;
		NotifyRoleFlags();
	}

	public void UpdateTokens(string accessToken, string refreshToken, long accessTokenExpiresAtMs, long refreshTokenExpiresAtMs)
	{
		AccessToken = accessToken;
		RefreshToken = refreshToken;
		AccessTokenExpiresAtMs = accessTokenExpiresAtMs;
		RefreshTokenExpiresAtMs = refreshTokenExpiresAtMs;
	}

	public void NotifySessionExpired()
	{
		SessionExpired?.Invoke(this, EventArgs.Empty);
	}

	public void Logout()
	{
		UserId = string.Empty;
		UserName = string.Empty;
		DisplayName = string.Empty;
		RoleCode = string.Empty;
		RoleName = string.Empty;
		RoleId = string.Empty;
		ActivePostId = string.Empty;
		EmployeeNo = string.Empty;
		AccessToken = string.Empty;
		RefreshToken = string.Empty;
		AccessTokenExpiresAtMs = 0;
		RefreshTokenExpiresAtMs = 0;
		ActiveDeptId = string.Empty;
		ActiveDeptName = string.Empty;
		ActiveDeptCategory = string.Empty;
		ActiveWardId = null;
		PrescribeLevel = 0;
		SurgeryLevel = 0;
		EmrSignLevel = 0;
		IsLoggedIn = false;
		NotifyRoleFlags();
	}

	private bool IsActiveOpdClinical =>
		string.Equals(_activeDeptCategory, "OPD", StringComparison.OrdinalIgnoreCase)
		&& (_prescribeLevel >= 1 || _emrSignLevel >= 1);

	private void NotifyRoleFlags()
	{
		string[] array = new string[20]
		{
			"IsAdmin", "IsOpdDoctor", "IsIpdResident", "IsIpdAttending", "IsWardNurse", "IsPharmacist", "IsCashier", "CanPrescribe", "CanSignEmr", "CanSurgery",
			"CanManageUsers", "CanSeeOpdStation", "CanSeeIpdStation", "CanSeeWardStation", "CanSeeMedTech", "CanSeePharmacy", "CanSeeFinance", "CanSeeEmrOfd", "CanSeeDynFormConfig", "CanSeeAdminPanel"
		};
		string[] array2 = array;
		foreach (string propertyName in array2)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}

	private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, value))
		{
			return false;
		}

		field = value;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
		return true;
	}

	public static string NormalizeRoleCode(string? roleCode)
	{
		string text = (roleCode ?? string.Empty).Trim().ToUpperInvariant();
		while (text.StartsWith("ROLE-", StringComparison.Ordinal) || text.StartsWith("ROLE_", StringComparison.Ordinal))
		{
			text = text.Substring(5);
		}
		text = text.Replace('_', '-');
		if (text is "SUPER-ADMIN" or "SYSTEM-ADMIN")
		{
			text = "ADMIN";
		}
		return string.IsNullOrWhiteSpace(text) ? string.Empty : ("ROLE-" + text);
	}

	private bool IsRole(string role) =>
		string.Equals(NormalizeRoleCode(role), NormalizeRoleCode(RoleCode), StringComparison.Ordinal);
}
