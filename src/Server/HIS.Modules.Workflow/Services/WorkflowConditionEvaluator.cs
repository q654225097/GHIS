using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HIS.Modules.Workflow.Models;

namespace HIS.Modules.Workflow.Services;

public static class WorkflowConditionEvaluator
{
    public static bool EvaluateGroup(WorkflowConditionGroupDto? group, IReadOnlyDictionary<string, object?> variables)
    {
        bool hasRules = group?.Rules != null && group.Rules.Count > 0;
        bool hasSubGroups = group?.SubGroups != null && group.SubGroups.Count > 0;
        if (!hasRules && !hasSubGroups)
        {
            return true;
        }

        bool isAnd = !string.Equals(group!.LogicalOp, "OR", StringComparison.OrdinalIgnoreCase);
        var results = new List<bool>();
        if (hasRules)
        {
            foreach (var rule in group.Rules)
            {
                results.Add(EvaluateRule(rule, variables));
            }
        }
        if (hasSubGroups)
        {
            foreach (var sub in group.SubGroups!)
            {
                results.Add(EvaluateGroup(sub, variables));
            }
        }
        return isAnd ? results.All(r => r) : results.Any(r => r);
    }

    public static bool EvaluateRule(WorkflowConditionRuleDto rule, IReadOnlyDictionary<string, object?> variables)
    {
        if (string.IsNullOrWhiteSpace(rule.Field))
        {
            return true;
        }

        // Case-insensitive lookup in variables
        object? actualVal = null;
        foreach (var kv in variables)
        {
            if (string.Equals(kv.Key, rule.Field, StringComparison.OrdinalIgnoreCase))
            {
                actualVal = kv.Value;
                break;
            }
        }

        string actualStr = actualVal?.ToString() ?? string.Empty;
        string expectedStr = rule.Value?.Trim() ?? string.Empty;
        string op = rule.Operator?.Trim() ?? "==";

        // Try numeric comparison if applicable
        if (decimal.TryParse(actualStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal actualNum) &&
            decimal.TryParse(expectedStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal expectedNum))
        {
            return op switch
            {
                ">" => actualNum > expectedNum,
                ">=" => actualNum >= expectedNum,
                "<" => actualNum < expectedNum,
                "<=" => actualNum <= expectedNum,
                "==" or "=" => actualNum == expectedNum,
                "!=" or "<>" => actualNum != expectedNum,
                "IN" => ParseNumbers(expectedStr).Contains(actualNum),
                "NOT_IN" => !ParseNumbers(expectedStr).Contains(actualNum),
                _ => false
            };
        }

        // String / collection comparison
        return op switch
        {
            "==" or "=" => string.Equals(actualStr, expectedStr, StringComparison.OrdinalIgnoreCase),
            "!=" or "<>" => !string.Equals(actualStr, expectedStr, StringComparison.OrdinalIgnoreCase),
            "IN" => SplitValues(expectedStr).Any(v => string.Equals(v, actualStr, StringComparison.OrdinalIgnoreCase)),
            "NOT_IN" => !SplitValues(expectedStr).Any(v => string.Equals(v, actualStr, StringComparison.OrdinalIgnoreCase)),
            "CONTAINS" => actualStr.Contains(expectedStr, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static HashSet<decimal> ParseNumbers(string s)
    {
        var set = new HashSet<decimal>();
        foreach (var part in SplitValues(s))
        {
            if (decimal.TryParse(part, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal d))
            {
                set.Add(d);
            }
        }
        return set;
    }

    private static string[] SplitValues(string s)
    {
        return s.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
