using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace HIS.BuildingBlocks.Security;

/// <summary>
/// 声明式服务端 RBAC 角色授权特性（零 ASP.NET 外部依赖，由 HIS.Server.Host 中间件统一拦截并返回 HTTP 403）。
/// 超级管理员 (ADMIN / ROLE-ADMIN) 默认拥有全院监管权限；其他岗位须命中 AllowedRoles 中任一角色编码。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequireHisRoleAttribute : Attribute
{
    public IReadOnlyList<string> AllowedRoles { get; }

    public RequireHisRoleAttribute(params string[] allowedRoles)
    {
        AllowedRoles = (allowedRoles ?? Array.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(NormalizeRoleCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string NormalizeRoleCode(string raw)
    {
        string s = (raw ?? string.Empty).Trim().ToUpperInvariant();
        if (s.StartsWith("ROLE-", StringComparison.Ordinal))
            s = s.Substring(5);
        return s.Replace('-', '_');
    }

    public static bool IsAuthorized(ClaimsPrincipal? principal, IEnumerable<RequireHisRoleAttribute> requirements)
    {
        var reqList = requirements?.ToList() ?? new List<RequireHisRoleAttribute>();
        if (reqList.Count == 0)
            return true;
        if (principal == null || principal.Identity?.IsAuthenticated != true)
            return false;

        var userRoles = principal.Claims
            .Where(c => c.Type == ClaimTypes.Role
                     || string.Equals(c.Type, "role", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(c.Type, "role_code", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(c.Type, "roleId", StringComparison.OrdinalIgnoreCase))
            .SelectMany(c => c.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(NormalizeRoleCode)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (userRoles.Contains("ADMIN"))
            return true;

        foreach (var req in reqList)
        {
            if (req.AllowedRoles.Count == 0)
                continue;
            if (!req.AllowedRoles.Any(userRoles.Contains))
                return false;
        }
        return true;
    }
}
