using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using HIS.Contracts;

namespace HIS.BuildingBlocks.Security;

public class UserSession : IUserSession
{
	public string UserId { get; set; } = string.Empty;

	public string UserName { get; set; } = string.Empty;

	public string DisplayName { get; set; } = string.Empty;

	public string RoleId { get; set; } = string.Empty;

	public string RoleCode { get; set; } = string.Empty;

	public string RoleName { get; set; } = string.Empty;

	public string CampusId { get; set; } = string.Empty;

	public DepartmentCategory ActiveDeptCategory { get; set; } = DepartmentCategory.OPD;

	public string ActiveDeptId { get; set; } = string.Empty;

	public string ActivePostId { get; set; } = string.Empty;

	public string ActiveWardId { get; set; } = string.Empty;

	public IReadOnlyList<string> LinkedWardIds { get; set; } = Array.Empty<string>();

	public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();

	public DataScope DataScope { get; set; } = DataScope.ALL_CAMPUS;

	public bool IsAdmin => IsAdminRole();

	public int SessionVersion { get; set; } = 1;

	public int PrescribeLevel { get; set; } = 1;

	public int SurgeryLevel { get; set; } = 1;

	public int EmrSignLevel { get; set; } = 1;

	public Dictionary<string, ControlAccessPolicy> Permissions { get; set; } = new Dictionary<string, ControlAccessPolicy>(StringComparer.OrdinalIgnoreCase);

	public bool HasPermission(string controlKey)
	{
		if (string.IsNullOrWhiteSpace(UserId))
		{
			return false;
		}
		if (IsAdminRole())
		{
			return true;
		}
		if (Permissions.TryGetValue(controlKey, out var value))
		{
			return value == ControlAccessPolicy.FullAccess;
		}
		return false;
	}

	public ControlAccessPolicy GetControlPolicy(string controlKey)
	{
		if (string.IsNullOrWhiteSpace(UserId))
		{
			return ControlAccessPolicy.Hidden;
		}
		if (IsAdminRole())
		{
			return ControlAccessPolicy.FullAccess;
		}
		if (Permissions.TryGetValue(controlKey, out var value))
		{
			return value;
		}
		return ControlAccessPolicy.Hidden;
	}

	private bool IsAdminRole()
	{
		if (string.IsNullOrWhiteSpace(UserId))
		{
			return false;
		}
		return HIS.Contracts.RoleCode.IsAdmin(RoleCode) || HIS.Contracts.RoleCode.HasRole(Roles, HIS.Contracts.RoleCode.Admin);
	}

	public static UserSession FromClaims(ClaimsPrincipal principal, Dictionary<string, ControlAccessPolicy>? permissions = null)
	{
		UserSession userSession = new UserSession();
		userSession.UserId = principal.FindFirst("sub")?.Value ?? principal.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value ?? string.Empty;
		userSession.UserName = principal.FindFirst("user_name")?.Value ?? string.Empty;
		userSession.DisplayName = principal.FindFirst("display_name")?.Value ?? string.Empty;
		userSession.RoleId = principal.FindFirst("role_id")?.Value ?? string.Empty;
		userSession.RoleCode = principal.FindFirst("role_code")?.Value ?? string.Empty;
		userSession.RoleName = principal.FindFirst("role_name")?.Value ?? string.Empty;
		userSession.CampusId = principal.FindFirst("campus_id")?.Value ?? string.Empty;
		if (Enum.TryParse<DepartmentCategory>(principal.FindFirst("active_dept_category")?.Value, out var result))
		{
			userSession.ActiveDeptCategory = result;
		}
		userSession.ActiveDeptId = principal.FindFirst("active_dept_id")?.Value ?? string.Empty;
		userSession.ActivePostId = principal.FindFirst("active_post_id")?.Value ?? string.Empty;
		userSession.ActiveWardId = principal.FindFirst("active_ward_id")?.Value ?? string.Empty;
		string text = principal.FindFirst("linked_ward_ids")?.Value;
		if (!string.IsNullOrEmpty(text))
		{
			userSession.LinkedWardIds = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		}
		string text2 = principal.FindFirst("roles")?.Value
			?? principal.FindAll(ClaimTypes.Role).Select(x => x.Value)
				.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
		if (!string.IsNullOrEmpty(text2))
		{
			userSession.Roles = text2.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		}
		if (Enum.TryParse<DataScope>(principal.FindFirst("data_scope")?.Value, out var result2))
		{
			userSession.DataScope = result2;
		}
		if (int.TryParse(principal.FindFirst("ver")?.Value, out var result3))
		{
			userSession.SessionVersion = result3;
		}
		if (int.TryParse(principal.FindFirst("prescribe_level")?.Value, out var result4))
		{
			userSession.PrescribeLevel = result4;
		}
		if (int.TryParse(principal.FindFirst("surgery_level")?.Value, out var result5))
		{
			userSession.SurgeryLevel = result5;
		}
		if (int.TryParse(principal.FindFirst("emr_sign_level")?.Value, out var result6))
		{
			userSession.EmrSignLevel = result6;
		}
		if (permissions != null)
		{
			userSession.Permissions = permissions;
		}
		return userSession;
	}
}
