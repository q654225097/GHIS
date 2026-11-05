using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HIS.Contracts;
using Microsoft.IdentityModel.Tokens;

namespace HIS.BuildingBlocks.Security;

public class JwtTokenService
{
    private readonly byte[] _signingKey;
    private readonly string _issuer;
    private readonly string _audience;

    public JwtTokenService(string secret = "HIS_V4_SUPER_SECRET_JWT_SIGNING_KEY_2026_LONG_ENOUGH_BYTES!",
        string issuer = "HIS.V4", string audience = "HIS.Client")
    {
        _signingKey = Encoding.UTF8.GetBytes(secret);
        _issuer = issuer;
        _audience = audience;
    }

    public string GenerateAccessToken(
        string userId,
        string userName,
        string displayName,
        string campusId,
        DepartmentCategory activeDeptCategory,
        string activeDeptId,
        string? activeWardId,
        IEnumerable<string>? linkedWardIds,
        IEnumerable<string> roles,
        DataScope dataScope,
        int sessionVersion,
        string permHash,
        int prescribeLevel = 1,
        int surgeryLevel = 1,
        int emrSignLevel = 1,
        TimeSpan? expiry = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new("user_name", userName),
            new("display_name", displayName),
            new("campus_id", campusId),
            new("active_dept_category", activeDeptCategory.ToString()),
            new("active_dept_id", activeDeptId),
            new("active_ward_id", activeWardId ?? string.Empty),
            new("linked_ward_ids", string.Join(",", linkedWardIds ?? Array.Empty<string>())),
            new("roles", string.Join(",", roles)),
            new("data_scope", dataScope.ToString()),
            new("ver", sessionVersion.ToString()),
            new("perm_hash", permHash),
            new("prescribe_level", prescribeLevel.ToString()),
            new("surgery_level", surgeryLevel.ToString()),
            new("emr_sign_level", emrSignLevel.ToString())
        };

        var creds = new SigningCredentials(new SymmetricSecurityKey(_signingKey), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(expiry ?? TimeSpan.FromHours(8)),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        var validationParams = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(_signingKey),
            ValidateIssuer = true,
            ValidIssuer = _issuer,
            ValidateAudience = true,
            ValidAudience = _audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(5)
        };

        try
        {
            return handler.ValidateToken(token, validationParams, out _);
        }
        catch
        {
            return null;
        }
    }
}

public class UserSession : IUserSession
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string CampusId { get; set; } = string.Empty;
    public DepartmentCategory ActiveDeptCategory { get; set; } = DepartmentCategory.OPD;
    public string ActiveDeptId { get; set; } = string.Empty;
    public string ActiveWardId { get; set; } = string.Empty;
    public IReadOnlyList<string> LinkedWardIds { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
    public DataScope DataScope { get; set; } = DataScope.ALL_CAMPUS;
    public int SessionVersion { get; set; } = 1;
    public int PrescribeLevel { get; set; } = 1;
    public int SurgeryLevel { get; set; } = 1;
    public int EmrSignLevel { get; set; } = 1;
    public Dictionary<string, ControlAccessPolicy> Permissions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool HasPermission(string controlKey)
    {
        if (Roles.Contains("ADMIN", StringComparer.OrdinalIgnoreCase) ||
            Roles.Contains("SUPER_ADMIN", StringComparer.OrdinalIgnoreCase))
            return true;

        if (Permissions.TryGetValue(controlKey, out var policy))
            return policy == ControlAccessPolicy.FullAccess;

        return true; // Default permit if not explicitly configured
    }

    public ControlAccessPolicy GetControlPolicy(string controlKey)
    {
        if (Roles.Contains("ADMIN", StringComparer.OrdinalIgnoreCase) ||
            Roles.Contains("SUPER_ADMIN", StringComparer.OrdinalIgnoreCase))
            return ControlAccessPolicy.FullAccess;

        if (Permissions.TryGetValue(controlKey, out var policy))
            return policy;

        return ControlAccessPolicy.FullAccess;
    }

    public static UserSession FromClaims(ClaimsPrincipal principal, Dictionary<string, ControlAccessPolicy>? permissions = null)
    {
        var session = new UserSession();
        session.UserId = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        session.UserName = principal.FindFirst("user_name")?.Value ?? string.Empty;
        session.DisplayName = principal.FindFirst("display_name")?.Value ?? string.Empty;
        session.CampusId = principal.FindFirst("campus_id")?.Value ?? string.Empty;

        if (Enum.TryParse<DepartmentCategory>(principal.FindFirst("active_dept_category")?.Value, out var cat))
            session.ActiveDeptCategory = cat;

        session.ActiveDeptId = principal.FindFirst("active_dept_id")?.Value ?? string.Empty;
        session.ActiveWardId = principal.FindFirst("active_ward_id")?.Value ?? string.Empty;

        var linkedWards = principal.FindFirst("linked_ward_ids")?.Value;
        if (!string.IsNullOrEmpty(linkedWards))
            session.LinkedWardIds = linkedWards.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var rolesStr = principal.FindFirst("roles")?.Value;
        if (!string.IsNullOrEmpty(rolesStr))
            session.Roles = rolesStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (Enum.TryParse<DataScope>(principal.FindFirst("data_scope")?.Value, out var ds))
            session.DataScope = ds;

        if (int.TryParse(principal.FindFirst("ver")?.Value, out var ver))
            session.SessionVersion = ver;

        if (int.TryParse(principal.FindFirst("prescribe_level")?.Value, out var pl))
            session.PrescribeLevel = pl;

        if (int.TryParse(principal.FindFirst("surgery_level")?.Value, out var sl))
            session.SurgeryLevel = sl;

        if (int.TryParse(principal.FindFirst("emr_sign_level")?.Value, out var esl))
            session.EmrSignLevel = esl;

        if (permissions != null)
            session.Permissions = permissions;

        return session;
    }
}

