using System;

namespace HIS.BuildingBlocks.Security;

/// <summary>
/// 要求调用方具备系统管理员权限（ADMIN 或 is_admin=true）
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class RequireAdminAttribute : Attribute
{
}

/// <summary>
/// 要求调用方具备指定角色中的至少一种角色代码
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = true)]
public sealed class RequireRoleAttribute : Attribute
{
    public string[] Roles { get; }

    public RequireRoleAttribute(params string[] roles)
    {
        Roles = roles ?? Array.Empty<string>();
    }
}

/// <summary>
/// 要求调用方具备指定的权限资源键
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : Attribute
{
    public string[] Permissions { get; }

    public RequirePermissionAttribute(params string[] permissions)
    {
        Permissions = permissions ?? Array.Empty<string>();
    }
}

