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
}
