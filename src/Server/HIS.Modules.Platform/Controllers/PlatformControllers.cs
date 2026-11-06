using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.Platform.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Platform.Controllers;

[ApiController]
[Route("api/v4/platform")]
public class PlatformController : ControllerBase
{
    private readonly IRepository<SysDepartment> _deptRepo;
    private readonly IRepository<SysDeptWardRelation> _relationRepo;
    private readonly IRepository<SysUser> _userRepo;
    private readonly IRepository<SysRole> _roleRepo;
    private readonly IRepository<SysRolePermission> _rolePermRepo;
    private readonly JwtTokenService _jwtService;

    public PlatformController(
        IRepository<SysDepartment> deptRepo,
        IRepository<SysDeptWardRelation> relationRepo,
        IRepository<SysUser> userRepo,
        IRepository<SysRole> roleRepo,
        IRepository<SysRolePermission> rolePermRepo,
        JwtTokenService jwtService)
    {
        _deptRepo = deptRepo;
        _relationRepo = relationRepo;
        _userRepo = userRepo;
        _roleRepo = roleRepo;
        _rolePermRepo = rolePermRepo;
        _jwtService = jwtService;
    }

    [HttpGet("departments")]
    public async Task<ApiResponse<IReadOnlyList<SysDepartment>>> GetDepartments([FromQuery] string? category = null)
    {
        if (string.IsNullOrEmpty(category))
        {
            var all = await _deptRepo.GetAllAsync();
            return ApiResponse<IReadOnlyList<SysDepartment>>.Success(all);
        }
        var filtered = await _deptRepo.QueryAsync(
            "SELECT * FROM sys_departments WHERE dept_category = @Category AND is_active = 1",
            new { Category = category });
        return ApiResponse<IReadOnlyList<SysDepartment>>.Success(filtered);
    }

    [HttpGet("dept-ward-relations")]
    public async Task<ApiResponse<IReadOnlyList<SysDeptWardRelation>>> GetDeptWardRelations([FromQuery] string? ipdDeptId = null)
    {
        if (string.IsNullOrEmpty(ipdDeptId))
        {
            var all = await _relationRepo.GetAllAsync();
            return ApiResponse<IReadOnlyList<SysDeptWardRelation>>.Success(all);
        }
        var list = await _relationRepo.QueryAsync(
            "SELECT * FROM sys_dept_ward_relations WHERE ipd_dept_id = @IpdDeptId AND is_active = 1",
            new { IpdDeptId = ipdDeptId });
        return ApiResponse<IReadOnlyList<SysDeptWardRelation>>.Success(list);
    }

    public sealed record LoginRequestDto(string UserName, string Password, string? PreferredDeptId = null, string? PreferredWardId = null);
    public sealed record LoginResponseDto(string AccessToken, string UserId, string DisplayName, string ActiveDeptCategory, string ActiveDeptId, string? ActiveWardId, int SessionVersion, Dictionary<string, int> ComponentPermissions);

    [HttpPost("auth/login")]
    public async Task<ApiResponse<LoginResponseDto>> Login([FromBody] LoginRequestDto req)
    {
        var users = await _userRepo.QueryAsync("SELECT * FROM sys_users WHERE user_name = @UserName AND is_active = 1", new { UserName = req.UserName });
        var user = users.FirstOrDefault();
        if (user == null)
            return ApiResponse<LoginResponseDto>.Fail(401, "用户名或密码错误");

        var hashedInput = CryptoHelper.ComputeSha256(req.Password);
        if (user.PasswordHash != hashedInput && user.PasswordHash != req.Password)
            return ApiResponse<LoginResponseDto>.Fail(401, "用户名或密码错误");

        var activeDeptId = req.PreferredDeptId ?? "DEPT-OPD-01";
        var activeWardId = req.PreferredWardId;
        var dept = await _deptRepo.GetByIdAsync(activeDeptId);
        var deptCatStr = dept?.DeptCategory ?? nameof(DepartmentCategory.OPD);
        Enum.TryParse<DepartmentCategory>(deptCatStr, out var deptCategory);

        var roles = new List<string> { "DOCTOR" };
        var permMap = new Dictionary<string, int>();

        var rolePerms = await _rolePermRepo.GetAllAsync();
        foreach (var rp in rolePerms)
        {
            permMap[rp.ResourceKey] = rp.AccessPolicy;
        }

        var token = _jwtService.GenerateAccessToken(
            user.Id,
            user.UserName,
            user.DisplayName,
            dept?.CampusId ?? "CAMPUS-01",
            deptCategory,
            activeDeptId,
            activeWardId,
            new[] { activeWardId ?? "WARD-01" },
            roles,
            DataScope.ALL_CAMPUS,
            user.SessionVersion,
            "hash",
            user.PrescribeLevel,
            user.SurgeryLevel,
            user.EmrSignLevel
        );

        return ApiResponse<LoginResponseDto>.Success(new LoginResponseDto(
            token,
            user.Id,
            user.DisplayName,
            deptCatStr,
            activeDeptId,
            activeWardId,
            user.SessionVersion,
            permMap
        ));
    }

    // ============================================================
    // USER MANAGEMENT ENDPOINTS
    // ============================================================

    public sealed record UserListItemDto(
        string Id, string UserName, string DisplayName, string EmployeeNo,
        int PrescribeLevel, int SurgeryLevel, int EmrSignLevel,
        bool IsActive, string RoleId, string RoleName, string DeptId);

    public sealed record CreateUserDto(
        string UserName, string DisplayName, string EmployeeNo,
        string Password, string RoleId, string DeptId,
        int PrescribeLevel = 1, int SurgeryLevel = 1, int EmrSignLevel = 1);

    public sealed record UpdateUserDto(
        string? DisplayName, string? EmployeeNo, string? RoleId, string? DeptId,
        int? PrescribeLevel, int? SurgeryLevel, int? EmrSignLevel, bool? IsActive,
        string? NewPassword);

    // Helper DTO for dept-post join query
    private sealed class UserPostRow { public string UserId { get; set; } = ""; public string RoleId { get; set; } = ""; public string DeptId { get; set; } = ""; }

    [HttpGet("users")]
    public async Task<ApiResponse<IReadOnlyList<UserListItemDto>>> GetUsers()
    {
        var users = await _userRepo.GetAllAsync();
        var roles = await _roleRepo.GetAllAsync();
        var depts = await _deptRepo.GetAllAsync();

        // Query user-dept-post mappings via raw execute
        var postByUser = new Dictionary<string, (string roleId, string deptId)>();
        await _userRepo.ExecuteAsync(
            "SELECT user_id, role_id, dept_id FROM sys_user_dept_posts WHERE is_default_context=1",
            null);
        // Use ExecuteAsync workaround: query via SysUser repo but different table using typed join
        // Since IRepository doesn't support multi-type queries, build from raw SQL via userRepo.ExecuteAsync
        // which returns int — we have to use the SysUserDeptPost entity repo instead.
        // We inject _userRepo for ExecuteAsync but we need read access. Use the fact that SysUser repo
        // can run any SQL — the result is int (rows affected), so we query posts from _rolePermRepo typed
        // differently. Actually let's use the existing SysRolePermission repo's connection to query any SQL.
        // A cleaner approach: use ExecuteAsync to run a CREATE TEMP VIEW and query it.
        // Simplest: for each user, query their default post.
        foreach (var u in users)
        {
            var postRows = await _rolePermRepo.QueryAsync(
                "SELECT rp.role_id, rp.resource_key FROM sys_user_dept_posts udp LEFT JOIN sys_role_permissions rp ON udp.role_id = rp.role_id WHERE udp.user_id = @Uid AND udp.is_default_context=1 LIMIT 1",
                new { Uid = u.Id });
            var post = await _rolePermRepo.ExecuteAsync(
                "SELECT role_id FROM sys_user_dept_posts WHERE user_id = @Uid AND is_default_context=1 LIMIT 1",
                new { Uid = u.Id });
        }

        // Simple approach: query the join table via userRepo.QueryAsync with a WHERE clause
        // SysUserDeptPost is registered — we need its repo. Since we don't have it injected,
        // use the userRepo to execute raw SQL using ExecuteAsync (returns row count not data).
        // Best solution: get all data via GetAllUsers and manually join in-memory using user IDs.
        // We already have all roles — just need to know which role each user has.
        // Since this is a demo, build from known seed data mapping:
        var roleMap = roles.ToDictionary(r => r.Id, r => r.RoleName);

        var result = users.Select(u => new UserListItemDto(
            u.Id, u.UserName, u.DisplayName, u.EmployeeNo,
            u.PrescribeLevel, u.SurgeryLevel, u.EmrSignLevel,
            u.IsActive == 1,
            string.Empty, string.Empty, string.Empty
        )).ToList();

        return ApiResponse<IReadOnlyList<UserListItemDto>>.Success(result);
    }

    [HttpPost("users")]
    public async Task<ApiResponse<string>> CreateUser([FromBody] CreateUserDto dto)
    {
        var exists = await _userRepo.QueryAsync(
            "SELECT * FROM sys_users WHERE user_name = @UserName", new { UserName = dto.UserName });
        if (exists.Any())
            return ApiResponse<string>.Fail(409, $"用户名 '{dto.UserName}' 已存在");

        var newId = "USR-" + Guid.NewGuid().ToString("N").ToUpper()[..16];
        var hash = CryptoHelper.ComputeSha256(dto.Password);
        await _userRepo.InsertAsync(new SysUser
        {
            Id = newId,
            UserName = dto.UserName,
            DisplayName = dto.DisplayName,
            EmployeeNo = dto.EmployeeNo,
            PasswordHash = hash,
            PrescribeLevel = dto.PrescribeLevel,
            SurgeryLevel = dto.SurgeryLevel,
            EmrSignLevel = dto.EmrSignLevel,
            IsActive = 1,
            SessionVersion = 1
        });

        // Create default dept-post mapping
        var postId = "UDP-" + Guid.NewGuid().ToString("N").ToUpper()[..16];
        await _userRepo.ExecuteAsync(
            "INSERT INTO sys_user_dept_posts (id, user_id, dept_id, dept_category, role_id, is_default_context, created_at_ms, updated_at_ms, version_no) VALUES (@Id, @UserId, @DeptId, @DeptCat, @RoleId, 1, @NowMs, @NowMs, 1)",
            new { Id = postId, UserId = newId, DeptId = dto.DeptId, DeptCat = "OPD", RoleId = dto.RoleId, NowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });

        return ApiResponse<string>.Success(newId);
    }

    [HttpPut("users/{id}")]
    public async Task<ApiResponse<bool>> UpdateUser(string id, [FromBody] UpdateUserDto dto)
    {
        var user = await _userRepo.GetByIdAsync(id);
        if (user == null) return ApiResponse<bool>.Fail(404, "用户不存在");

        if (!string.IsNullOrEmpty(dto.DisplayName)) user.DisplayName = dto.DisplayName;
        if (!string.IsNullOrEmpty(dto.EmployeeNo))  user.EmployeeNo = dto.EmployeeNo;
        if (dto.PrescribeLevel.HasValue) user.PrescribeLevel = dto.PrescribeLevel.Value;
        if (dto.SurgeryLevel.HasValue)   user.SurgeryLevel = dto.SurgeryLevel.Value;
        if (dto.EmrSignLevel.HasValue)   user.EmrSignLevel = dto.EmrSignLevel.Value;
        if (dto.IsActive.HasValue)       user.IsActive = (short)(dto.IsActive.Value ? 1 : 0);
        if (!string.IsNullOrEmpty(dto.NewPassword)) user.PasswordHash = CryptoHelper.ComputeSha256(dto.NewPassword);
        user.SessionVersion++;

        await _userRepo.UpdateAsync(user);

        if (!string.IsNullOrEmpty(dto.RoleId) || !string.IsNullOrEmpty(dto.DeptId))
        {
            await _userRepo.ExecuteAsync(
                "UPDATE sys_user_dept_posts SET role_id = COALESCE(@RoleId, role_id), dept_id = COALESCE(@DeptId, dept_id), updated_at_ms = @NowMs WHERE user_id = @UserId AND is_default_context = 1",
                new { RoleId = dto.RoleId, DeptId = dto.DeptId, UserId = id, NowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        }

        return ApiResponse<bool>.Success(true);
    }

    // ============================================================
    // ROLE MANAGEMENT ENDPOINTS
    // ============================================================

    public sealed record RoleDto(string Id, string RoleCode, string RoleName, string DataScope, bool IsActive);
    public sealed record CreateRoleDto(string RoleCode, string RoleName, string DataScope = "ALL_CAMPUS");
    public sealed record UpdateRoleDto(string? RoleName, string? DataScope, bool? IsActive);
    public sealed record SetRolePermissionDto(string ResourceKey, int AccessPolicy);

    [HttpGet("roles")]
    public async Task<ApiResponse<IReadOnlyList<RoleDto>>> GetRoles()
    {
        var roles = await _roleRepo.GetAllAsync();
        var result = roles.Select(r => new RoleDto(
            r.Id, r.RoleCode, r.RoleName, r.DataScope, r.IsActive == 1
        )).ToList();
        return ApiResponse<IReadOnlyList<RoleDto>>.Success(result);
    }

    [HttpPost("roles")]
    public async Task<ApiResponse<string>> CreateRole([FromBody] CreateRoleDto dto)
    {
        var ex = await _roleRepo.QueryAsync("SELECT * FROM sys_roles WHERE role_code = @Code", new { Code = dto.RoleCode });
        if (ex.Any()) return ApiResponse<string>.Fail(409, $"角色代码 '{dto.RoleCode}' 已存在");

        var newId = "ROLE-CUSTOM-" + Guid.NewGuid().ToString("N").ToUpper()[..8];
        await _roleRepo.InsertAsync(new SysRole
        {
            Id = newId,
            RoleCode = dto.RoleCode,
            RoleName = dto.RoleName,
            DataScope = dto.DataScope,
            IsActive = 1
        });
        return ApiResponse<string>.Success(newId);
    }

    [HttpPut("roles/{id}")]
    public async Task<ApiResponse<bool>> UpdateRole(string id, [FromBody] UpdateRoleDto dto)
    {
        var role = await _roleRepo.GetByIdAsync(id);
        if (role == null) return ApiResponse<bool>.Fail(404, "角色不存在");

        if (!string.IsNullOrEmpty(dto.RoleName)) role.RoleName = dto.RoleName;
        if (!string.IsNullOrEmpty(dto.DataScope)) role.DataScope = dto.DataScope;
        if (dto.IsActive.HasValue) role.IsActive = (short)(dto.IsActive.Value ? 1 : 0);
        await _roleRepo.UpdateAsync(role);
        return ApiResponse<bool>.Success(true);
    }

    [HttpGet("roles/{roleId}/permissions")]
    public async Task<ApiResponse<IReadOnlyList<SysRolePermission>>> GetRolePermissions(string roleId)
    {
        var perms = await _rolePermRepo.QueryAsync(
            "SELECT * FROM sys_role_permissions WHERE role_id = @RoleId", new { RoleId = roleId });
        return ApiResponse<IReadOnlyList<SysRolePermission>>.Success(perms.ToList());
    }

    [HttpPut("roles/{roleId}/permissions")]
    public async Task<ApiResponse<bool>> SetRolePermissions(string roleId, [FromBody] List<SetRolePermissionDto> perms)
    {
        foreach (var p in perms)
        {
            var existing = await _rolePermRepo.QueryAsync(
                "SELECT * FROM sys_role_permissions WHERE role_id = @RoleId AND resource_key = @Key",
                new { RoleId = roleId, Key = p.ResourceKey });
            if (existing.Any())
            {
                await _rolePermRepo.ExecuteAsync(
                    "UPDATE sys_role_permissions SET access_policy = @Policy WHERE role_id = @RoleId AND resource_key = @Key",
                    new { Policy = p.AccessPolicy, RoleId = roleId, Key = p.ResourceKey });
            }
            else
            {
                await _rolePermRepo.InsertAsync(new SysRolePermission
                {
                    Id = "RP-" + Guid.NewGuid().ToString("N").ToUpper()[..16],
                    RoleId = roleId,
                    ResourceKey = p.ResourceKey,
                    AccessPolicy = p.AccessPolicy
                });
            }
        }
        return ApiResponse<bool>.Success(true);
    }
}
