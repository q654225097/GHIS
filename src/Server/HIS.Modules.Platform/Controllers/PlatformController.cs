using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.Platform.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace HIS.Modules.Platform.Controllers;

[ApiController]
[Route("api/v4/platform")]
public class PlatformController : ControllerBase
{
	public sealed record LoginContextDto(string PostId, string RoleId, string RoleCode, string RoleName, string DeptId, string DeptName, string DeptCategory, string? WardId, string DataScope, bool IsDefault, string UserId = "", string UserName = "", string DisplayName = "", string EmployeeNo = "", int PrescribeLevel = 0, int SurgeryLevel = 0, int EmrSignLevel = 0);

	public sealed record LoginRequestDto(string UserName, string Password, string? PreferredDeptId = null, string? PreferredWardId = null, string? ContextId = null);

	public sealed record LoginResponseDto(string AccessToken, string UserId, string UserName, string DisplayName, string ActiveDeptCategory, string ActiveDeptId, string? ActiveWardId, int SessionVersion, Dictionary<string, int> ComponentPermissions, IReadOnlyList<LoginContextDto>? Contexts = null, string? PostId = null, string? RoleId = null, string? RoleCode = null, string? RoleName = null, string? ActiveDeptName = null, string? EmployeeNo = null, int PrescribeLevel = 0, int SurgeryLevel = 0, int EmrSignLevel = 0, string? RefreshToken = null, long AccessTokenExpiresAtMs = 0, long RefreshTokenExpiresAtMs = 0);

	public sealed record SwitchContextRequestDto(string ContextId);

	public sealed record RefreshTokenRequestDto(string RefreshToken);

	public sealed record ChangePasswordRequestDto(string? OldPassword, string NewPassword);

	public sealed record UserListItemDto(string Id, string UserName, string DisplayName, string EmployeeNo, int PrescribeLevel, int SurgeryLevel, int EmrSignLevel, bool IsActive, string RoleId, string RoleName, string DeptId);

	public sealed record CreateUserDto(string UserName, string DisplayName, string EmployeeNo, string Password, string RoleId, string DeptId, int PrescribeLevel = 1, int SurgeryLevel = 1, int EmrSignLevel = 1);

	public sealed record UpdateUserDto(string? DisplayName, string? EmployeeNo, string? RoleId, string? DeptId, int? PrescribeLevel, int? SurgeryLevel, int? EmrSignLevel, bool? IsActive, string? NewPassword);

	public sealed record RoleDto(string Id, string RoleCode, string RoleName, string DataScope, bool IsActive);

	public sealed record CreateRoleDto(string RoleCode, string RoleName, string DataScope = "ALL_CAMPUS");

	public sealed record UpdateRoleDto(string? RoleName, string? DataScope, bool? IsActive);

	public sealed record SetRolePermissionDto(string ResourceKey, int AccessPolicy);

	private readonly IRepository<SysDepartment> _deptRepo;
	private readonly IRepository<SysDeptWardRelation> _relationRepo;
	private readonly IRepository<SysUser> _userRepo;
	private readonly IRepository<SysUserDeptPost> _postRepo;
	private readonly IRepository<SysRole> _roleRepo;
	private readonly IRepository<SysRolePermission> _rolePermRepo;
	private readonly IRepository<SysRefreshToken> _refreshTokenRepo;
	private readonly IDbConnectionFactory _connectionFactory;
	private readonly JwtTokenService _jwtService;
	private readonly IAuditTrailContract? _auditTrail;
	private readonly IUserSession? _userSession;
	private readonly LoginAttemptLockoutService? _lockoutService;
	private readonly IWebHostEnvironment? _environment;

	public PlatformController(
		IRepository<SysDepartment> deptRepo,
		IRepository<SysDeptWardRelation> relationRepo,
		IRepository<SysUser> userRepo,
		IRepository<SysUserDeptPost> postRepo,
		IRepository<SysRole> roleRepo,
		IRepository<SysRolePermission> rolePermRepo,
		IRepository<SysRefreshToken> refreshTokenRepo,
		IDbConnectionFactory connectionFactory,
		JwtTokenService jwtService,
		IAuditTrailContract? auditTrail = null,
		IUserSession? userSession = null,
		LoginAttemptLockoutService? lockoutService = null,
		IWebHostEnvironment? environment = null)
	{
		_deptRepo = deptRepo;
		_relationRepo = relationRepo;
		_userRepo = userRepo;
		_postRepo = postRepo;
		_roleRepo = roleRepo;
		_rolePermRepo = rolePermRepo;
		_refreshTokenRepo = refreshTokenRepo;
		_connectionFactory = connectionFactory;
		_jwtService = jwtService;
		_auditTrail = auditTrail;
		_userSession = userSession;
		_lockoutService = lockoutService;
		_environment = environment;
	}

	[AllowAnonymous]
	[HttpGet("departments")]
	public async Task<ApiResponse<IReadOnlyList<SysDepartment>>> GetDepartments([FromQuery] string? category = null)
	{
		if (string.IsNullOrEmpty(category))
		{
			return ApiResponse<IReadOnlyList<SysDepartment>>.Success(await _deptRepo.GetAllAsync());
		}
		return ApiResponse<IReadOnlyList<SysDepartment>>.Success(await _deptRepo.QueryAsync("SELECT * FROM sys_departments WHERE dept_category = @Category AND is_active = 1", new
		{
			Category = category
		}));
	}

	[AllowAnonymous]
	[HttpGet("dept-ward-relations")]
	public async Task<ApiResponse<IReadOnlyList<SysDeptWardRelation>>> GetDeptWardRelations([FromQuery] string? ipdDeptId = null)
	{
		if (string.IsNullOrEmpty(ipdDeptId))
		{
			return ApiResponse<IReadOnlyList<SysDeptWardRelation>>.Success(await _relationRepo.GetAllAsync());
		}
		return ApiResponse<IReadOnlyList<SysDeptWardRelation>>.Success(await _relationRepo.QueryAsync("SELECT * FROM sys_dept_ward_relations WHERE ipd_dept_id = @IpdDeptId AND is_active = 1", new
		{
			IpdDeptId = ipdDeptId
		}));
	}

	[HttpGet("auth/contexts")]
	public async Task<ApiResponse<IReadOnlyList<LoginContextDto>>> GetLoginContexts([FromQuery] string? userName = null)
	{
		string targetUser = string.IsNullOrWhiteSpace(userName) ? (_userSession?.UserName ?? "") : userName.Trim();
		if (string.IsNullOrWhiteSpace(targetUser))
		{
			return ApiResponse<IReadOnlyList<LoginContextDto>>.Fail(400, "必须指定用户名或先登录");
		}

		if (!string.IsNullOrWhiteSpace(_userSession?.UserId)
			&& !_userSession.IsAdmin
			&& !string.Equals(_userSession.UserName, targetUser, StringComparison.OrdinalIgnoreCase))
		{
			return ApiResponse<IReadOnlyList<LoginContextDto>>.Fail(403, "非管理员仅可查询本人的执业身份列表");
		}

		SysUser? user = await FindUserAsync(targetUser);
		if (user == null)
		{
			return ApiResponse<IReadOnlyList<LoginContextDto>>.Fail(404, "用户不存在");
		}
		return ApiResponse<IReadOnlyList<LoginContextDto>>.Success(await LoadContextsAsync(user));
	}

	[AllowAnonymous]
	[HttpPost("auth/login")]
	public async Task<ActionResult<ApiResponse<LoginResponseDto>>> Login([FromBody] LoginRequestDto req)
	{
		if (req == null || string.IsNullOrWhiteSpace(req.UserName))
		{
			return BadRequest(ApiResponse<LoginResponseDto>.Fail(400, "用户名与密码不能为空"));
		}

		long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		if (_lockoutService != null)
		{
			var (isLocked, remaining) = _lockoutService.CheckLockout(req.UserName);
			if (isLocked)
			{
				int minutesLeft = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
				if (_auditTrail != null)
				{
					await _auditTrail.RecordAuditAsync("AUTH_LOGIN_LOCKED", "SysUser", req.UserName, $"账户已被锁定，剩余锁定时间: {minutesLeft} 分钟");
				}
				return StatusCode(423, ApiResponse<LoginResponseDto>.Fail(423, $"账户因多次登录失败已被锁定，请 {minutesLeft} 分钟后再试"));
			}
		}

		SysUser? user = await FindUserAsync(req.UserName);
		if (user == null)
		{
			_lockoutService?.RecordFailedAttempt(req.UserName);
			if (_auditTrail != null)
			{
				await _auditTrail.RecordAuditAsync("AUTH_LOGIN_FAILED", "SysUser", req.UserName, "用户不存在或未激活");
			}
			return Unauthorized(ApiResponse<LoginResponseDto>.Fail(401, "用户名或密码错误"));
		}

		if (user.LockoutUntilMs.HasValue && user.LockoutUntilMs.Value > nowMs)
		{
			int minutesLeft = Math.Max(1, (int)Math.Ceiling((user.LockoutUntilMs.Value - nowMs) / 60000.0));
			if (_auditTrail != null)
			{
				await _auditTrail.RecordAuditAsync("AUTH_LOGIN_LOCKED", "SysUser", user.Id, $"账户处于持久化安全锁定状态，剩余 {minutesLeft} 分钟", user.Id, user.DisplayName);
			}
			return StatusCode(423, ApiResponse<LoginResponseDto>.Fail(423, $"账户已被安全锁定，请 {minutesLeft} 分钟后再试"));
		}

		var (isValid, needsUpgrade) = PasswordSecurityHelper.VerifyPassword(req.Password, user.PasswordHash);
		if (!isValid)
		{
			user.FailedLoginCount++;
			bool locked = false;
			int lockMinutes = 15;
			if (user.FailedLoginCount >= 5)
			{
				user.LockoutUntilMs = nowMs + lockMinutes * 60 * 1000;
				locked = true;
			}
			await _userRepo.UpdateAsync(user);
			_lockoutService?.RecordFailedAttempt(req.UserName);

			if (locked)
			{
				if (_auditTrail != null)
				{
					await _auditTrail.RecordAuditAsync("AUTH_LOGIN_LOCKED", "SysUser", user.Id, $"连续 {user.FailedLoginCount} 次密码错误，账户已被持久化锁定 {lockMinutes} 分钟", user.Id, user.DisplayName);
				}
				return StatusCode(423, ApiResponse<LoginResponseDto>.Fail(423, $"连续 {user.FailedLoginCount} 次密码错误，账户已被安全锁定 {lockMinutes} 分钟"));
			}

			if (_auditTrail != null)
			{
				await _auditTrail.RecordAuditAsync("AUTH_LOGIN_FAILED", "SysUser", user.Id, $"密码错误（第 {user.FailedLoginCount} 次尝试）", user.Id, user.DisplayName);
			}
			return Unauthorized(ApiResponse<LoginResponseDto>.Fail(401, $"用户名或密码错误，剩余尝试次数: {Math.Max(0, 5 - user.FailedLoginCount)}"));
		}

		// 验证成功：重置锁定与错误计数
		if (user.FailedLoginCount > 0 || user.LockoutUntilMs.HasValue)
		{
			user.FailedLoginCount = 0;
			user.LockoutUntilMs = null;
			await _userRepo.UpdateAsync(user);
		}
		_lockoutService?.ResetAttempts(req.UserName);

		// 旧版无盐密码平滑升级为加盐 PBKDF2
		if (needsUpgrade)
		{
			user.PasswordHash = PasswordSecurityHelper.HashPassword(req.Password);
			await _userRepo.UpdateAsync(user);
		}

		IReadOnlyList<LoginContextDto> contexts = await LoadContextsAsync(user);
		LoginContextDto? selected = SelectContext(contexts, req.ContextId, req.PreferredDeptId, req.PreferredWardId);
		if (selected == null)
		{
			return StatusCode(403, ApiResponse<LoginResponseDto>.Fail(403, "账号未配置可用的执业身份"));
		}

		var session = await BuildLoginSessionAsync(user, selected, contexts);
		if (_auditTrail != null)
		{
			await _auditTrail.RecordAuditAsync("AUTH_LOGIN", "SysUser", user.Id, $"用户 [{user.UserName} - {user.DisplayName}] 成功登录并激活身份 [{selected.RoleName}]", user.Id, user.DisplayName, selected.DeptId);
		}

		return Ok(ApiResponse<LoginResponseDto>.Success(session.Response));
	}

	[HttpPost("auth/switch-context")]
	public async Task<ActionResult<ApiResponse<LoginResponseDto>>> SwitchContext([FromBody] SwitchContextRequestDto req)
	{
		string bearer = Request.Headers.Authorization.ToString();
		if (!bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
		{
			return Unauthorized(ApiResponse<LoginResponseDto>.Fail(401, "缺少登录令牌"));
		}
		ClaimsPrincipal? principal = _jwtService.ValidateToken(bearer.Substring(7).Trim());
		if (principal == null)
		{
			return Unauthorized(ApiResponse<LoginResponseDto>.Fail(401, "登录令牌无效或已过期"));
		}
		string? userId = principal.FindFirst("sub")?.Value ?? principal.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;
		if (string.IsNullOrWhiteSpace(userId))
		{
			return Unauthorized(ApiResponse<LoginResponseDto>.Fail(401, "登录令牌缺少用户信息"));
		}
		SysUser? user = await _userRepo.GetByIdAsync(userId);
		if (user == null || user.IsActive != 1)
		{
			return Unauthorized(ApiResponse<LoginResponseDto>.Fail(401, "登录会话已失效"));
		}
		string? claimVersion = principal.FindFirst("ver")?.Value;
		if (!int.TryParse(claimVersion, out var version) || version != user.SessionVersion)
		{
			return Unauthorized(ApiResponse<LoginResponseDto>.Fail(401, "登录会话已失效，请重新登录"));
		}
		IReadOnlyList<LoginContextDto> contexts = await LoadContextsAsync(user);
		LoginContextDto? selected = contexts.FirstOrDefault((LoginContextDto x) => x.PostId == req.ContextId);
		if (selected == null)
		{
			return StatusCode(403, ApiResponse<LoginResponseDto>.Fail(403, "无权切换到该执业身份"));
		}

		var session = await BuildLoginSessionAsync(user, selected, contexts);
		if (_auditTrail != null)
		{
			await _auditTrail.RecordAuditAsync("AUTH_SWITCH_CONTEXT", "SysUser", user.Id, $"用户 [{user.UserName}] 切换执业身份至 [{selected.RoleName} ({selected.DeptName})]", user.Id, user.DisplayName, selected.DeptId);
		}

		return Ok(ApiResponse<LoginResponseDto>.Success(session.Response));
	}

	[AllowAnonymous]
	[HttpPost("auth/refresh")]
	public async Task<ActionResult<ApiResponse<LoginResponseDto>>> Refresh([FromBody] RefreshTokenRequestDto req)
	{
		if (req == null || string.IsNullOrWhiteSpace(req.RefreshToken))
		{
			return Unauthorized(ApiResponse<LoginResponseDto>.Fail(401, "刷新令牌不能为空"));
		}

		string tokenHash = HashRefreshToken(req.RefreshToken.Trim());
		string? rejectedMessage = null;
		LoginResponseDto? response = null;
		string? auditEvent = null;
		string? auditUserId = null;
		string? auditUserName = null;
		string? auditDeptId = null;

		await _connectionFactory.ExecuteInTransactionAsync(async _ =>
		{
			SysRefreshToken? token = await _refreshTokenRepo.QuerySingleOrDefaultAsync(
				"SELECT * FROM sys_refresh_tokens WHERE token_hash = @TokenHash",
				new { TokenHash = tokenHash });
			if (token == null)
			{
				rejectedMessage = "刷新令牌无效";
				return;
			}

			SysUser? user = await _userRepo.GetByIdAsync(token.UserId);
			if (user == null || user.IsActive != 1)
			{
				rejectedMessage = "登录会话已失效，请重新登录";
				return;
			}
			auditUserId = user.Id;
			auditUserName = user.DisplayName;

			if (token.RotatedAtMs.HasValue || token.RevokedAtMs.HasValue)
			{
				await RevokeAllRefreshTokensAsync(user.Id);
				user.SessionVersion++;
				await _userRepo.UpdateAsync(user);
				rejectedMessage = "检测到刷新令牌重复使用，全部会话已撤销，请重新登录";
				auditEvent = "AUTH_REFRESH_REPLAY";
				return;
			}

			long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			if (token.ExpiresAtMs <= nowMs)
			{
				token.RevokedAtMs = nowMs;
				await _refreshTokenRepo.UpdateAsync(token);
				rejectedMessage = "刷新令牌已过期，请重新登录";
				return;
			}
			if (token.SessionVersion != user.SessionVersion)
			{
				rejectedMessage = "登录会话已失效，请重新登录";
				return;
			}

			IReadOnlyList<LoginContextDto> contexts = await LoadContextsAsync(user);
			LoginContextDto? selected = contexts.FirstOrDefault(x => x.PostId == token.PostId);
			if (selected == null)
			{
				rejectedMessage = "原执业身份已失效，请重新登录";
				return;
			}

			var session = await BuildLoginSessionAsync(user, selected, contexts);
			token.RotatedAtMs = nowMs;
			token.ReplacedByTokenHash = session.RefreshTokenHash;
			await _refreshTokenRepo.UpdateAsync(token);
			response = session.Response;
			auditEvent = "AUTH_REFRESH";
			auditDeptId = selected.DeptId;
		});

		if (_auditTrail != null && auditEvent != null)
		{
			string details = auditEvent == "AUTH_REFRESH_REPLAY"
				? "刷新令牌重放，已撤销全部有效会话"
				: "访问令牌已续期";
			await _auditTrail.RecordAuditAsync(auditEvent, "SysUser", auditUserId ?? tokenHash, details, auditUserId, auditUserName, auditDeptId);
		}

		if (response == null)
		{
			return Unauthorized(ApiResponse<LoginResponseDto>.Fail(401, rejectedMessage ?? "刷新令牌无效"));
		}
		return Ok(ApiResponse<LoginResponseDto>.Success(response));
	}

	[HttpPost("auth/logout")]
	public async Task<ApiResponse<bool>> Logout()
	{
		if (_userSession != null && !string.IsNullOrWhiteSpace(_userSession.UserId))
		{
			SysUser? user = await _userRepo.GetByIdAsync(_userSession.UserId);
			if (user != null)
			{
				await RevokeAllRefreshTokensAsync(user.Id);
				user.SessionVersion++;
				await _userRepo.UpdateAsync(user);
				if (_auditTrail != null)
				{
					await _auditTrail.RecordAuditAsync("AUTH_LOGOUT", "SysUser", user.Id, $"用户 [{user.UserName} - {user.DisplayName}] 登出系统，会话已终止", user.Id, user.DisplayName);
				}
			}
		}
		return ApiResponse<bool>.Success(true, "已安全退出登录");
	}

	[HttpPost("users/{id}/change-password")]
	public async Task<ApiResponse<bool>> ChangePassword(string id, [FromBody] ChangePasswordRequestDto dto)
	{
		int minimumPasswordLength = _environment?.IsDevelopment() == true ? 3 : 6;
		if (dto == null || string.IsNullOrWhiteSpace(dto.NewPassword) || dto.NewPassword.Length < minimumPasswordLength)
		{
			return ApiResponse<bool>.Fail(400, $"新密码长度不得少于 {minimumPasswordLength} 位");
		}

		SysUser? user = await _userRepo.GetByIdAsync(id);
		if (user == null)
		{
			return ApiResponse<bool>.Fail(404, "用户不存在");
		}

		bool isSelf = string.Equals(_userSession?.UserId, id, StringComparison.OrdinalIgnoreCase);
		bool isAdmin = _userSession?.IsAdmin == true;

		if (!isSelf && !isAdmin)
		{
			return ApiResponse<bool>.Fail(403, "无权修改其他用户的密码");
		}

		if (!isAdmin)
		{
			if (string.IsNullOrWhiteSpace(dto.OldPassword))
			{
				return ApiResponse<bool>.Fail(400, "修改本人密码必须提供原密码");
			}
			var (isOldValid, _) = PasswordSecurityHelper.VerifyPassword(dto.OldPassword, user.PasswordHash);
			if (!isOldValid)
			{
				return ApiResponse<bool>.Fail(400, "原密码不正确");
			}
		}

		user.PasswordHash = PasswordSecurityHelper.HashPassword(dto.NewPassword);
		await RevokeAllRefreshTokensAsync(user.Id);
		user.SessionVersion++;
		await _userRepo.UpdateAsync(user);

		if (_auditTrail != null)
		{
			await _auditTrail.RecordAuditAsync("USER_CHANGE_PASSWORD", "SysUser", user.Id, $"用户 [{user.UserName}] 密码已修改，旧会话已全部作废", _userSession?.UserId, _userSession?.DisplayName);
		}

		return ApiResponse<bool>.Success(true, "密码修改成功，请使用新密码重新登录");
	}

	private async Task<SysUser?> FindUserAsync(string userName)
	{
		string input = userName?.Trim() ?? string.Empty;
		return (await _userRepo.QueryAsync(
			"SELECT * FROM sys_users WHERE (user_name = @Input OR employee_no = @Input) AND is_active = 1 LIMIT 1", new
		{
			Input = input
		})).FirstOrDefault();
	}

	private async Task<IReadOnlyList<LoginContextDto>> LoadContextsAsync(SysUser user)
	{
		IReadOnlyList<SysUserDeptPost> posts = await _postRepo.QueryAsync("SELECT udp.*\nFROM sys_user_dept_posts udp\nINNER JOIN sys_roles r ON r.id = udp.role_id AND r.is_active = 1\nINNER JOIN sys_departments d ON d.id = udp.dept_id AND d.is_active = 1\nWHERE udp.user_id = @UserId\nORDER BY udp.is_default_context DESC, d.dept_name, r.role_name", new
		{
			UserId = user.Id
		});
		Dictionary<string, SysRole> roles = (await _roleRepo.GetAllAsync()).ToDictionary((SysRole x) => x.Id, StringComparer.OrdinalIgnoreCase);
		Dictionary<string, SysDepartment> depts = (await _deptRepo.GetAllAsync()).ToDictionary((SysDepartment x) => x.Id, StringComparer.OrdinalIgnoreCase);
		return posts.Where((SysUserDeptPost x) => roles.ContainsKey(x.RoleId) && depts.ContainsKey(x.DeptId)).Select((SysUserDeptPost x) =>
		{
			SysRole sysRole = roles[x.RoleId];
			SysDepartment sysDepartment = depts[x.DeptId];
			return new LoginContextDto(x.Id, sysRole.Id, sysRole.RoleCode, sysRole.RoleName, sysDepartment.Id, sysDepartment.DeptName, x.DeptCategory, x.DeptCategory.Equals("WARD", StringComparison.OrdinalIgnoreCase) ? sysDepartment.Id : null, sysRole.DataScope, x.IsDefaultContext == 1, user.Id, user.UserName, user.DisplayName, user.EmployeeNo, user.PrescribeLevel, user.SurgeryLevel, user.EmrSignLevel);
		}).ToList();
	}

	private static LoginContextDto? SelectContext(IReadOnlyList<LoginContextDto> contexts, string? contextId, string? preferredDeptId, string? preferredWardId)
	{
		if (!string.IsNullOrWhiteSpace(contextId))
		{
			return contexts.FirstOrDefault((LoginContextDto x) => x.PostId == contextId);
		}
		IReadOnlyList<LoginContextDto> readOnlyList2;
		if (!string.IsNullOrWhiteSpace(preferredDeptId))
		{
			IReadOnlyList<LoginContextDto> readOnlyList = contexts.Where((LoginContextDto x) => x.DeptId == preferredDeptId).ToList();
			readOnlyList2 = readOnlyList;
		}
		else
		{
			readOnlyList2 = contexts;
		}
		IReadOnlyList<LoginContextDto> source = readOnlyList2;
		return source.FirstOrDefault((LoginContextDto x) => x.IsDefault) ?? source.FirstOrDefault();
	}

	private async Task<(LoginResponseDto Response, string RefreshTokenHash)> BuildLoginSessionAsync(SysUser user, LoginContextDto context, IReadOnlyList<LoginContextDto> contexts)
	{
		Enum.TryParse<DepartmentCategory>(context.DeptCategory, ignoreCase: true, out var category);
		Enum.TryParse<DataScope>(context.DataScope, ignoreCase: true, out var dataScope);
		Dictionary<string, int> permMap = (await _rolePermRepo.QueryAsync("SELECT * FROM sys_role_permissions WHERE role_id = @RoleId", new { context.RoleId })).ToDictionary((SysRolePermission x) => x.ResourceKey, (SysRolePermission x) => x.AccessPolicy, StringComparer.OrdinalIgnoreCase);
		List<string> list = ((category != DepartmentCategory.IPD) ? new List<string>() : (await _relationRepo.QueryAsync("SELECT ward_dept_id FROM sys_dept_ward_relations WHERE ipd_dept_id = @DeptId AND is_active = 1", new { context.DeptId })).Select((SysDeptWardRelation x) => x.WardDeptId).ToList());
		List<string> linkedWardIds = list;
		string? activeWardId = ((category == DepartmentCategory.WARD) ? context.DeptId : null);
		SysDepartment? dept = await _deptRepo.GetByIdAsync(context.DeptId);
		JwtTokenService jwtService = _jwtService;
		string id = user.Id;
		string userName = user.UserName;
		string displayName = user.DisplayName;
		string campusId = dept?.CampusId ?? "CAMPUS-01";
		DepartmentCategory activeDeptCategory = category;
		string deptId = context.DeptId;
		string[] roles = new string[1] { context.RoleCode };
		DataScope dataScope2 = dataScope;
		int sessionVersion = user.SessionVersion;
		int prescribeLevel = user.PrescribeLevel;
		int surgeryLevel = user.SurgeryLevel;
		int emrSignLevel = user.EmrSignLevel;
		string postId = context.PostId;
		string roleId = context.RoleId;
		string roleCode = context.RoleCode;
		string roleName = context.RoleName;
		string deptName = context.DeptName;
		long accessExpiresAtMs = DateTimeOffset.UtcNow.Add(AccessTokenLifetime).ToUnixTimeMilliseconds();
		long refreshExpiresAtMs = DateTimeOffset.UtcNow.Add(RefreshTokenLifetime).ToUnixTimeMilliseconds();
		string token = jwtService.GenerateAccessToken(id, userName, displayName, campusId, activeDeptCategory, deptId, activeWardId, linkedWardIds, roles, dataScope2, sessionVersion, "hash", prescribeLevel, surgeryLevel, emrSignLevel, AccessTokenLifetime, postId, roleId, roleCode, roleName, deptName);
		string refreshToken = CreateRefreshToken();
		string refreshTokenHash = HashRefreshToken(refreshToken);
		await _refreshTokenRepo.InsertAsync(new SysRefreshToken
		{
			UserId = user.Id,
			PostId = context.PostId,
			TokenHash = refreshTokenHash,
			SessionVersion = sessionVersion,
			ExpiresAtMs = refreshExpiresAtMs,
			CreatedBy = user.Id
		});
		var response = new LoginResponseDto(token, user.Id, user.UserName, user.DisplayName, context.DeptCategory, context.DeptId, activeWardId, user.SessionVersion, permMap, contexts, context.PostId, context.RoleId, context.RoleCode, context.RoleName, context.DeptName, user.EmployeeNo, user.PrescribeLevel, user.SurgeryLevel, user.EmrSignLevel, refreshToken, accessExpiresAtMs, refreshExpiresAtMs);
		return (response, refreshTokenHash);
	}

	private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(30);

	private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

	private static string CreateRefreshToken()
	{
		return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
			.TrimEnd('=')
			.Replace('+', '-')
			.Replace('/', '_');
	}

	private static string HashRefreshToken(string token)
	{
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
	}

	private async Task RevokeAllRefreshTokensAsync(string userId)
	{
		await _refreshTokenRepo.ExecuteAsync(
			"UPDATE sys_refresh_tokens SET revoked_at_ms = @NowMs, updated_at_ms = @NowMs, version_no = version_no + 1 WHERE user_id = @UserId AND revoked_at_ms IS NULL",
			new { UserId = userId, NowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
	}

	[RequireAdmin]
	[HttpGet("users")]
	public async Task<ApiResponse<IReadOnlyList<UserListItemDto>>> GetUsers()
	{
		IReadOnlyList<SysUser> users = await _userRepo.GetAllAsync();
		Dictionary<string, string> roles = (await _roleRepo.GetAllAsync()).ToDictionary((SysRole r) => r.Id, (SysRole r) => r.RoleName, StringComparer.OrdinalIgnoreCase);
		Dictionary<string, SysUserDeptPost> defaultPostByUser = (await _postRepo.GetAllAsync()).GroupBy((SysUserDeptPost p) => p.UserId, StringComparer.OrdinalIgnoreCase).ToDictionary((IGrouping<string, SysUserDeptPost> g) => g.Key, (IGrouping<string, SysUserDeptPost> g) => g.OrderByDescending((SysUserDeptPost x) => x.IsDefaultContext).First(), StringComparer.OrdinalIgnoreCase);
		List<UserListItemDto> result = users.Select((SysUser u) =>
		{
			defaultPostByUser.TryGetValue(u.Id, out var value);
			string text = value?.RoleId ?? string.Empty;
			string deptId = value?.DeptId ?? string.Empty;
			string roleName = ((!string.IsNullOrEmpty(text) && roles.TryGetValue(text, out var value2)) ? value2 : string.Empty);
			return new UserListItemDto(u.Id, u.UserName, u.DisplayName, u.EmployeeNo, u.PrescribeLevel, u.SurgeryLevel, u.EmrSignLevel, u.IsActive == 1, text, roleName, deptId);
		}).ToList();
		return ApiResponse<IReadOnlyList<UserListItemDto>>.Success(result);
	}

	[RequireAdmin]
	[HttpPost("users")]
	public async Task<ApiResponse<string>> CreateUser([FromBody] CreateUserDto dto)
	{
		if (string.IsNullOrWhiteSpace(dto.UserName) || string.IsNullOrWhiteSpace(dto.DisplayName))
		{
			return ApiResponse<string>.Fail(400, "用户名和姓名不能为空");
		}
		if ((await _userRepo.QueryAsync("SELECT * FROM sys_users WHERE user_name = @UserName", new { dto.UserName })).Any())
		{
			return ApiResponse<string>.Fail(409, "用户名 '" + dto.UserName + "' 已存在");
		}
		string newId = "USR-" + Guid.NewGuid().ToString("N").ToUpper().Substring(0, 16);
		string hash = PasswordSecurityHelper.HashPassword(dto.Password);
		await _userRepo.InsertAsync(new SysUser
		{
			Id = newId,
			UserName = dto.UserName.Trim(),
			DisplayName = dto.DisplayName.Trim(),
			EmployeeNo = dto.EmployeeNo.Trim(),
			PasswordHash = hash,
			PrescribeLevel = dto.PrescribeLevel,
			SurgeryLevel = dto.SurgeryLevel,
			EmrSignLevel = dto.EmrSignLevel,
			IsActive = 1,
			SessionVersion = 1
		});
		string deptCategory = (await _deptRepo.GetByIdAsync(dto.DeptId))?.DeptCategory ?? "OPD";
		string postId = "UDP-" + Guid.NewGuid().ToString("N").ToUpper().Substring(0, 16);
		await _postRepo.InsertAsync(new SysUserDeptPost
		{
			Id = postId,
			UserId = newId,
			DeptId = dto.DeptId,
			DeptCategory = deptCategory,
			RoleId = dto.RoleId,
			IsDefaultContext = 1
		});

		if (_auditTrail != null)
		{
			await _auditTrail.RecordAuditAsync("USER_CREATE", "SysUser", newId, $"管理员创建用户 [{dto.UserName} - {dto.DisplayName}]，工号: {dto.EmployeeNo}", _userSession?.UserId, _userSession?.DisplayName, dto.DeptId);
		}

		return ApiResponse<string>.Success(newId);
	}

	[RequireAdmin]
	[HttpPut("users/{id}")]
	public async Task<ApiResponse<bool>> UpdateUser(string id, [FromBody] UpdateUserDto dto)
	{
		SysUser? user = await _userRepo.GetByIdAsync(id);
		if (user == null)
		{
			return ApiResponse<bool>.Fail(404, "用户不存在", data: false);
		}
		if (!string.IsNullOrEmpty(dto.DisplayName))
		{
			user.DisplayName = dto.DisplayName;
		}
		if (!string.IsNullOrEmpty(dto.EmployeeNo))
		{
			user.EmployeeNo = dto.EmployeeNo;
		}
		if (dto.PrescribeLevel.HasValue)
		{
			user.PrescribeLevel = dto.PrescribeLevel.Value;
		}
		if (dto.SurgeryLevel.HasValue)
		{
			user.SurgeryLevel = dto.SurgeryLevel.Value;
		}
		if (dto.EmrSignLevel.HasValue)
		{
			user.EmrSignLevel = dto.EmrSignLevel.Value;
		}
		if (dto.IsActive.HasValue)
		{
			user.IsActive = (short)(dto.IsActive.Value ? 1 : 0);
		}
		if (!string.IsNullOrEmpty(dto.NewPassword))
		{
			user.PasswordHash = PasswordSecurityHelper.HashPassword(dto.NewPassword);
		}
		user.SessionVersion++;
		await RevokeAllRefreshTokensAsync(user.Id);
		await _userRepo.UpdateAsync(user);
		if (!string.IsNullOrEmpty(dto.RoleId) || !string.IsNullOrEmpty(dto.DeptId))
		{
			string? deptCategory = null;
			if (!string.IsNullOrEmpty(dto.DeptId))
			{
				deptCategory = (await _deptRepo.GetByIdAsync(dto.DeptId))?.DeptCategory;
			}
			await _postRepo.ExecuteAsync("UPDATE sys_user_dept_posts\nSET role_id = COALESCE(@RoleId, role_id),\n    dept_id = COALESCE(@DeptId, dept_id),\n    dept_category = COALESCE(@DeptCat, dept_category),\n    updated_at_ms = @NowMs,\n    version_no = version_no + 1\nWHERE user_id = @UserId AND is_default_context = 1", new
			{
				RoleId = dto.RoleId,
				DeptId = dto.DeptId,
				DeptCat = deptCategory,
				UserId = id,
				NowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			});
		}

		if (_auditTrail != null)
		{
			await _auditTrail.RecordAuditAsync("USER_UPDATE", "SysUser", id, $"管理员更新用户 [{user.UserName} - {user.DisplayName}] 资料", _userSession?.UserId, _userSession?.DisplayName);
		}

		return ApiResponse<bool>.Success(data: true);
	}

	[RequireAdmin]
	[HttpGet("roles")]
	public async Task<ApiResponse<IReadOnlyList<RoleDto>>> GetRoles()
	{
		List<RoleDto> result = (await _roleRepo.GetAllAsync()).Select((SysRole r) => new RoleDto(r.Id, r.RoleCode, r.RoleName, r.DataScope, r.IsActive == 1)).ToList();
		return ApiResponse<IReadOnlyList<RoleDto>>.Success(result);
	}

	[RequireAdmin]
	[HttpPost("roles")]
	public async Task<ApiResponse<string>> CreateRole([FromBody] CreateRoleDto dto)
	{
		if ((await _roleRepo.QueryAsync("SELECT * FROM sys_roles WHERE role_code = @Code", new
		{
			Code = dto.RoleCode
		})).Any())
		{
			return ApiResponse<string>.Fail(409, "角色代码 '" + dto.RoleCode + "' 已存在");
		}
		string newId = "ROLE-CUSTOM-" + Guid.NewGuid().ToString("N").ToUpper().Substring(0, 8);
		await _roleRepo.InsertAsync(new SysRole
		{
			Id = newId,
			RoleCode = dto.RoleCode,
			RoleName = dto.RoleName,
			DataScope = dto.DataScope,
			IsActive = 1
		});

		if (_auditTrail != null)
		{
			await _auditTrail.RecordAuditAsync("ROLE_CREATE", "SysRole", newId, $"管理员创建角色 [{dto.RoleCode} - {dto.RoleName}]", _userSession?.UserId, _userSession?.DisplayName);
		}

		return ApiResponse<string>.Success(newId);
	}

	[RequireAdmin]
	[HttpPut("roles/{id}")]
	public async Task<ApiResponse<bool>> UpdateRole(string id, [FromBody] UpdateRoleDto dto)
	{
		SysRole? role = await _roleRepo.GetByIdAsync(id);
		if (role == null)
		{
			return ApiResponse<bool>.Fail(404, "角色不存在", data: false);
		}
		if (!string.IsNullOrEmpty(dto.RoleName))
		{
			role.RoleName = dto.RoleName;
		}
		if (!string.IsNullOrEmpty(dto.DataScope))
		{
			role.DataScope = dto.DataScope;
		}
		if (dto.IsActive.HasValue)
		{
			role.IsActive = (short)(dto.IsActive.Value ? 1 : 0);
		}
		await _roleRepo.UpdateAsync(role);

		if (_auditTrail != null)
		{
			await _auditTrail.RecordAuditAsync("ROLE_UPDATE", "SysRole", id, $"管理员更新角色 [{role.RoleCode} - {role.RoleName}]", _userSession?.UserId, _userSession?.DisplayName);
		}

		return ApiResponse<bool>.Success(data: true);
	}

	[RequireAdmin]
	[HttpGet("roles/{roleId}/permissions")]
	public async Task<ApiResponse<IReadOnlyList<SysRolePermission>>> GetRolePermissions(string roleId)
	{
		return ApiResponse<IReadOnlyList<SysRolePermission>>.Success((await _rolePermRepo.QueryAsync("SELECT * FROM sys_role_permissions WHERE role_id = @RoleId", new
		{
			RoleId = roleId
		})).ToList());
	}

	[RequireAdmin]
	[HttpPut("roles/{roleId}/permissions")]
	public async Task<ApiResponse<bool>> SetRolePermissions(string roleId, [FromBody] List<SetRolePermissionDto> perms)
	{
		foreach (SetRolePermissionDto p in perms)
		{
			IReadOnlyList<SysRolePermission> existing = await _rolePermRepo.QueryAsync("SELECT * FROM sys_role_permissions WHERE role_id = @RoleId AND resource_key = @Key", new
			{
				RoleId = roleId,
				Key = p.ResourceKey
			});
			if (existing.Any())
			{
				SysRolePermission cur = existing[0];
				cur.AccessPolicy = p.AccessPolicy;
				await _rolePermRepo.UpdateAsync(cur);
			}
			else
			{
				await _rolePermRepo.InsertAsync(new SysRolePermission
				{
					Id = "RP-" + Guid.NewGuid().ToString("N").ToUpper().Substring(0, 16),
					RoleId = roleId,
					ResourceKey = p.ResourceKey,
					AccessPolicy = p.AccessPolicy
				});
			}
		}

		if (_auditTrail != null)
		{
			await _auditTrail.RecordAuditAsync("ROLE_UPDATE_PERMISSIONS", "SysRole", roleId, $"管理员配置角色 [{roleId}] 权限矩阵，更新项数: {perms.Count}", _userSession?.UserId, _userSession?.DisplayName);
		}

		return ApiResponse<bool>.Success(data: true);
	}
}
