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
        if (group == null || group.Rules == null || group.Rules.Count == 0)
        {
            return true;
        }

        bool isAnd = !string.Equals(group.LogicalOp, "OR", StringComparison.OrdinalIgnoreCase);

        if (isAnd)
        {
            foreach (var rule in group.Rules)
            {
                if (!EvaluateRule(rule, variables))
                {
                    return false;
                }
            }
            return true;
        }
        else
        {
            foreach (var rule in group.Rules)
            {
                if (EvaluateRule(rule, variables))
                {
                    return true;
                }
            }
            return false;
        }
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
