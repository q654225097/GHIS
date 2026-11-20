using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Database;
using HIS.Modules.Workflow.Models;

namespace HIS.Modules.Workflow.Services;

public record ResolvedAssignee(string UserId, string DisplayName, bool IsFallback = false, string? FallbackReason = null);

public class WorkflowAssigneeResolver
{
    private readonly IDbConnectionFactory _connFactory;

    public WorkflowAssigneeResolver(IDbConnectionFactory connFactory)
    {
        _connFactory = connFactory;
    }

    public async Task<List<ResolvedAssignee>> ResolveAssigneesAsync(
        AssigneeConfigDto? config,
        WfDefinition definition,
        WorkflowNodeDto node,
        string initiatorUserId,
        string? initiatorDeptId,
        IReadOnlyDictionary<string, object?> boVariables,
        CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();
        var result = new List<ResolvedAssignee>();

        if (config != null)
        {
            string strategy = config.Strategy?.ToUpperInvariant() ?? "FIXED_USER";
            switch (strategy)
            {
                case "FIXED_USER":
                    if (config.FixedUserIds != null && config.FixedUserIds.Count > 0)
                    {
                        var users = await conn.QueryAsync<(string Id, string DisplayName)>(
                            "SELECT id, display_name FROM sys_users WHERE id IN @Ids AND is_active = 1",
                            new { Ids = config.FixedUserIds });
                        result.AddRange(users.Select(u => new ResolvedAssignee(u.Id, u.DisplayName)));
                    }
                    break;

                case "ROLE":
                    if (!string.IsNullOrWhiteSpace(config.RoleCode))
                    {
                        string sql = @"
                            SELECT DISTINCT u.id, u.display_name 
                            FROM sys_users u
                            INNER JOIN sys_user_dept_posts p ON p.user_id = u.id
                            INNER JOIN sys_roles r ON r.id = p.role_id
                            WHERE r.role_code = @RoleCode AND u.is_active = 1";

                        if (config.DeptScoped == true && !string.IsNullOrWhiteSpace(initiatorDeptId))
                        {
                            sql += " AND p.dept_id = @DeptId";
                        }

                        var roleUsers = await conn.QueryAsync<(string Id, string DisplayName)>(
                            sql, new { RoleCode = config.RoleCode, DeptId = initiatorDeptId });

                        result.AddRange(roleUsers.Select(u => new ResolvedAssignee(u.Id, u.DisplayName)));
                    }
                    break;

                case "DEPT_MANAGER":
                    if (!string.IsNullOrWhiteSpace(initiatorDeptId))
                    {
                        var mgr = await conn.QueryFirstOrDefaultAsync<(string Id, string DisplayName)>(@"
                            SELECT u.id, u.display_name 
                            FROM sys_departments d
                            INNER JOIN sys_users u ON u.id = d.manager_user_id
                            WHERE d.id = @DeptId AND u.is_active = 1",
                            new { DeptId = initiatorDeptId });

                        if (mgr.Id != null)
                        {
                            result.Add(new ResolvedAssignee(mgr.Id, mgr.DisplayName));
                        }
                    }
                    break;

                case "DIRECT_SUPERVISOR":
                    if (!string.IsNullOrWhiteSpace(initiatorUserId))
                    {
                        var sup = await conn.QueryFirstOrDefaultAsync<(string Id, string DisplayName)>(@"
                            SELECT s.id, s.display_name 
                            FROM sys_users u
                            INNER JOIN sys_users s ON s.id = u.direct_supervisor_id
                            WHERE u.id = @UserId AND s.is_active = 1",
                            new { UserId = initiatorUserId });

                        if (sup.Id != null)
                        {
                            result.Add(new ResolvedAssignee(sup.Id, sup.DisplayName));
                        }
                    }
                    break;

                case "ESCALATE_N":
                    int levels = Math.Max(1, config.EscalateLevels ?? 1);
                    string? currentDeptId = initiatorDeptId;
                    (string Id, string DisplayName)? escalatedMgr = null;

                    for (int i = 0; i < levels && !string.IsNullOrWhiteSpace(currentDeptId); i++)
                    {
                        var deptInfo = await conn.QueryFirstOrDefaultAsync<(string? ParentId, string? ManagerId)>(
                            "SELECT parent_dept_id, manager_user_id FROM sys_departments WHERE id = @Id",
                            new { Id = currentDeptId });

                        if (deptInfo.ManagerId != null)
                        {
                            var mgrUser = await conn.QueryFirstOrDefaultAsync<(string Id, string DisplayName)>(
                                "SELECT id, display_name FROM sys_users WHERE id = @Id AND is_active = 1",
                                new { Id = deptInfo.ManagerId });
                            if (mgrUser.Id != null)
                            {
                                escalatedMgr = mgrUser;
                            }
                        }
                        currentDeptId = deptInfo.ParentId;
                    }

                    if (escalatedMgr.HasValue)
                    {
                        result.Add(new ResolvedAssignee(escalatedMgr.Value.Id, escalatedMgr.Value.DisplayName));
                    }
                    break;

                case "BO_EXPRESSION":
                    string expr = config.BoExpression ?? string.Empty;
                    string? targetUserId = ResolveBoExpression(expr, boVariables);
                    if (!string.IsNullOrWhiteSpace(targetUserId))
                    {
                        var boUser = await conn.QueryFirstOrDefaultAsync<(string Id, string DisplayName)>(
                            "SELECT id, display_name FROM sys_users WHERE id = @Id AND is_active = 1",
                            new { Id = targetUserId });
                        if (boUser.Id != null)
                        {
                            result.Add(new ResolvedAssignee(boUser.Id, boUser.DisplayName));
                        }
                    }
                    break;
            }
        }

        // Fallback mechanism if no assignees resolved
        if (result.Count == 0)
        {
            string? fallbackId = node.FallbackApproverId;
            if (string.IsNullOrWhiteSpace(fallbackId))
            {
                fallbackId = definition.FallbackApproverId;
            }

            if (!string.IsNullOrWhiteSpace(fallbackId))
            {
                var fbUser = await conn.QueryFirstOrDefaultAsync<(string Id, string DisplayName)>(
                    "SELECT id, display_name FROM sys_users WHERE id = @Id AND is_active = 1",
                    new { Id = fallbackId });
                if (fbUser.Id != null)
                {
                    result.Add(new ResolvedAssignee(fbUser.Id, fbUser.DisplayName, true, "策略计算审批人为空，自动转交指定兜底审批人"));
                    return result;
                }
            }

            // Fallback to Administrator
            var adminUser = await conn.QueryFirstOrDefaultAsync<(string Id, string DisplayName)>(
                "SELECT id, display_name FROM sys_users WHERE (user_name = 'admin' OR id = 'USR-ADMIN') AND is_active = 1 ORDER BY user_name LIMIT 1");
            if (adminUser.Id != null)
            {
                result.Add(new ResolvedAssignee(adminUser.Id, adminUser.DisplayName, true, "策略计算审批人为空且无指定兜底人，自动转交系统管理员"));
            }
        }

        return result;
    }

    private static string? ResolveBoExpression(string expr, IReadOnlyDictionary<string, object?> boVariables)
    {
        if (string.IsNullOrWhiteSpace(expr)) return null;

        // Pattern 1: Ternary condition, e.g.: "amount > 100000 ? vpId : deptManagerId"
        var match = Regex.Match(expr, @"^\s*([a-zA-Z0-9_]+)\s*(>|>=|<|<=|==|!=)\s*([0-9.]+)\s*\?\s*([a-zA-Z0-9_]+)\s*:\s*([a-zA-Z0-9_]+)\s*$");
        if (match.Success)
        {
            string field = match.Groups[1].Value;
            string op = match.Groups[2].Value;
            string thresholdStr = match.Groups[3].Value;
            string trueField = match.Groups[4].Value;
            string falseField = match.Groups[5].Value;

            bool isTrue = WorkflowConditionEvaluator.EvaluateRule(new WorkflowConditionRuleDto
            {
                Field = field,
                Operator = op,
                Value = thresholdStr
            }, boVariables);

            string chosenField = isTrue ? trueField : falseField;
            return ExtractVariableValue(chosenField, boVariables);
        }

        // Pattern 2: Direct variable name
        return ExtractVariableValue(expr.Trim(), boVariables);
    }

    private static string? ExtractVariableValue(string key, IReadOnlyDictionary<string, object?> boVariables)
    {
        if (key.StartsWith("bo.", StringComparison.OrdinalIgnoreCase))
        {
            key = key.Substring(3);
        }

        foreach (var kv in boVariables)
        {
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return kv.Value?.ToString();
            }
        }
        return null;
    }
}
