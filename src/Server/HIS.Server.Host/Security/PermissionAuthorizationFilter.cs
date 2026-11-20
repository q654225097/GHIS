using System;
using System.Linq;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace HIS.BuildingBlocks.Security;

/// <summary>
/// 集中式权限与访问控制过滤器：自动读取 Controller 和 Action 上的
/// [AllowAnonymous]、[RequireAdmin]、[RequireRole]、[RequirePermission] 特性，
/// 进行统一的 RBAC 权限与职责分离 (Separation of Duties) 校验。
/// 实现 IAsyncAuthorizationFilter 以在模型绑定和验证前即刻拦截无权限请求（403 Forbidden）。
/// </summary>
public class PermissionAuthorizationFilter : IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // 1. 如果标有 [AllowAnonymous]，跳过权限检查
        var endpoint = context.HttpContext.GetEndpoint();
        var actionDescriptor = context.ActionDescriptor as ControllerActionDescriptor;

        bool hasAllowAnonymous = endpoint?.Metadata.GetMetadata<IAllowAnonymous>() != null ||
            (actionDescriptor != null && (
                actionDescriptor.MethodInfo.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Any() ||
                actionDescriptor.ControllerTypeInfo.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Any()));

        if (hasAllowAnonymous)
        {
            return Task.CompletedTask;
        }

        // 2. 获取当前用户会话
        var userSession = context.HttpContext.RequestServices.GetService<IUserSession>();

        // 3. 检查 [RequireAdmin]
        bool requireAdmin = endpoint?.Metadata.GetMetadata<RequireAdminAttribute>() != null ||
            (actionDescriptor != null && (
                actionDescriptor.MethodInfo.GetCustomAttributes(typeof(RequireAdminAttribute), true).Any() ||
                actionDescriptor.ControllerTypeInfo.GetCustomAttributes(typeof(RequireAdminAttribute), true).Any()));

        if (requireAdmin)
        {
            if (userSession == null || string.IsNullOrWhiteSpace(userSession.UserId))
            {
                context.Result = new ObjectResult(ApiResponse<object>.Fail(401, "请先登录"))
                {
                    StatusCode = StatusCodes.Status401Unauthorized
                };
                return Task.CompletedTask;
            }

            if (!userSession.IsAdmin)
            {
                context.Result = new ObjectResult(ApiResponse<object>.Fail(403, "操作被拒绝：仅系统管理员有权执行此操作"))
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
                return Task.CompletedTask;
            }
        }

        // 4. 检查 [RequireRole]
        var roleAttrs = actionDescriptor?.MethodInfo.GetCustomAttributes(typeof(RequireRoleAttribute), true)
            .Concat(actionDescriptor.ControllerTypeInfo.GetCustomAttributes(typeof(RequireRoleAttribute), true))
            .Cast<RequireRoleAttribute>()
            .ToList();

        if (roleAttrs != null && roleAttrs.Count > 0)
        {
            if (userSession == null || string.IsNullOrWhiteSpace(userSession.UserId))
            {
                context.Result = new ObjectResult(ApiResponse<object>.Fail(401, "请先登录"))
                {
                    StatusCode = StatusCodes.Status401Unauthorized
                };
                return Task.CompletedTask;
            }

            // 系统管理员具备完全治理权限
            if (!userSession.IsAdmin)
            {
                var allowedNormalized = roleAttrs.SelectMany(a => a.Roles)
                    .Select(RoleCode.Normalize)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                string normUserRole = RoleCode.Normalize(userSession.RoleCode);
                bool hasRole = allowedNormalized.Contains(normUserRole) ||
                               userSession.Roles.Any(r => allowedNormalized.Contains(RoleCode.Normalize(r)));

                if (!hasRole)
                {
                    string roleDisplayName = (userSession is UserSession concreteSession && !string.IsNullOrWhiteSpace(concreteSession.RoleName))
                        ? concreteSession.RoleName
                        : userSession.RoleCode;
                    context.Result = new ObjectResult(ApiResponse<object>.Fail(403, $"操作被拒绝：当前身份 [{roleDisplayName}] 无权访问该业务接口"))
                    {
                        StatusCode = StatusCodes.Status403Forbidden
                    };
                    return Task.CompletedTask;
                }
            }
        }

        // 5. 检查 [RequirePermission]
        var permAttrs = actionDescriptor?.MethodInfo.GetCustomAttributes(typeof(RequirePermissionAttribute), true)
            .Concat(actionDescriptor.ControllerTypeInfo.GetCustomAttributes(typeof(RequirePermissionAttribute), true))
            .Cast<RequirePermissionAttribute>()
            .ToList();

        if (permAttrs != null && permAttrs.Count > 0)
        {
            if (userSession == null || string.IsNullOrWhiteSpace(userSession.UserId))
            {
                context.Result = new ObjectResult(ApiResponse<object>.Fail(401, "请先登录"))
                {
                    StatusCode = StatusCodes.Status401Unauthorized
                };
                return Task.CompletedTask;
            }

            if (!userSession.IsAdmin)
            {
                var requiredPerms = permAttrs.SelectMany(a => a.Permissions).ToList();
                bool hasAnyPerm = requiredPerms.Any(p => userSession.HasPermission(p));
                if (!hasAnyPerm)
                {
                    context.Result = new ObjectResult(ApiResponse<object>.Fail(403, $"操作被拒绝：缺少所需的操作权限 [{string.Join(", ", requiredPerms)}]"))
                    {
                        StatusCode = StatusCodes.Status403Forbidden
                    };
                    return Task.CompletedTask;
                }
            }
        }

        return Task.CompletedTask;
    }
}
