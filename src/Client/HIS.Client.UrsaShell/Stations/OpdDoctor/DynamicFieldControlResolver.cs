using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace HIS.Client.UrsaShell.Stations.OpdDoctor;

public enum DynamicFieldControlKind
{
    Text,
    Multiline,
    Integer,
    Decimal,
    Enum,
    Date,
    Boolean
}

public static class DynamicFieldControlResolver
{
    public static DynamicFieldControlKind ResolveControlKind(string? controlType, string? dataType, IReadOnlyList<string> options)
    {
        string normalized = NormalizeToken(controlType);
        if (normalized.Length == 0)
        {
            normalized = NormalizeToken(dataType);
        }

        return normalized switch
        {
            "textarea" or "multiline" or "textareabox" or "rich text" or "richtext" => DynamicFieldControlKind.Multiline,
            "numericintupdown" or "numericupdown" or "integer" or "int" or "int32" or "long" or "int64" => DynamicFieldControlKind.Integer,
            "numericdoubleupdown" or "decimal" or "number" or "double" or "float" or "money" => DynamicFieldControlKind.Decimal,
            "combobox" or "select" or "enum" or "dropdown" => DynamicFieldControlKind.Enum,
            "datepicker" or "date" or "datetime" or "datetimeoffset" => DynamicFieldControlKind.Date,
            "checkbox" or "boolean" or "bool" or "switch" => DynamicFieldControlKind.Boolean,
            "textbox" or "text" or "string" => DynamicFieldControlKind.Text,
            _ => options.Count > 0 ? DynamicFieldControlKind.Enum : DynamicFieldControlKind.Text
        };
    }

    public static IReadOnlyList<string> ParseOptions(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        string text = raw.Trim();
        if (text.StartsWith("[", StringComparison.Ordinal))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(text);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    List<string> jsonOptions = new();
                    foreach (JsonElement item in document.RootElement.EnumerateArray())
                    {
                        string? value = item.ValueKind switch
                        {
                            JsonValueKind.String => item.GetString(),
                            JsonValueKind.Number => item.GetRawText(),
                            JsonValueKind.True => "true",
                            JsonValueKind.False => "false",
                            _ => null
                        };
                        AddOption(jsonOptions, value);
                    }
                    if (jsonOptions.Count > 0)
                    {
                        return jsonOptions;
                    }
                }
            }
            catch (JsonException)
            {
                // Fall through to the common delimiter parser.
            }
        }

        List<string> options = new();
        foreach (string part in text.Split(new[] { '\r', '\n', ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries))
        {
            AddOption(options, part);
        }
        return options;
    }

    public static bool IsGenderVisible(string? genderRule, string? patientGender)
    {
        string rule = NormalizeGenderRule(genderRule);
        if (rule.Length == 0 || rule == "ALL" || rule == "ANY" || rule == "BOTH")
        {
            return true;
        }

        bool isMale = IsGenderToken(patientGender, "MALE_ONLY", "MAN", "MALE", "M", "男", "1");
        bool isFemale = IsGenderToken(patientGender, "FEMALE_ONLY", "WOMAN", "FEMALE", "F", "女", "2");
        return rule switch
        {
            "MALE_ONLY" or "MALE" or "MAN" or "M" or "1" => isMale,
            "FEMALE_ONLY" or "FEMALE" or "WOMAN" or "F" or "2" => isFemale,
            _ => true
        };
    }

    public static bool EvaluateVisibility(
        string? expression,
        IReadOnlyDictionary<string, string> values,
        Func<string, bool>? genderVisible = null)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return true;
        }

        string text = expression.Trim();
        if (text.StartsWith("(", StringComparison.Ordinal) && text.EndsWith(")", StringComparison.Ordinal) &&
            IsWholeParenthesized(text))
        {
            text = text[1..^1].Trim();
        }
        if (text.Length == 0 || text.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        int orIndex = FindTopLevelOperator(text, "OR");
        if (orIndex >= 0)
        {
            return EvaluateVisibility(text[..orIndex], values, genderVisible) ||
                   EvaluateVisibility(text[(orIndex + 2)..], values, genderVisible);
        }

        int andIndex = FindTopLevelOperator(text, "AND");
        if (andIndex >= 0)
        {
            return EvaluateVisibility(text[..andIndex], values, genderVisible) &&
                   EvaluateVisibility(text[(andIndex + 3)..], values, genderVisible);
        }

        string normalized = NormalizeGenderRule(text);
        if (normalized is "MALE_ONLY" or "FEMALE_ONLY")
        {
            return genderVisible?.Invoke(normalized) ?? true;
        }

        int equalsIndex = text.IndexOf("==", StringComparison.Ordinal);
        if (equalsIndex < 0)
        {
            equalsIndex = text.IndexOf('=');
            if (equalsIndex >= 0)
            {
                string fieldName = text[..equalsIndex].Trim().Trim('{', '}', '[', ']', '"', '\'');
                string expected = text[(equalsIndex + 1)..].Trim().Trim('"', '\'');
                return values.TryGetValue(fieldName, out string? actual) &&
                       string.Equals(actual?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
            }
        }

        if (equalsIndex >= 0)
        {
            string fieldName = text[..equalsIndex].Trim().Trim('{', '}', '[', ']', '"', '\'');
            string expected = text[(equalsIndex + 2)..].Trim().Trim('"', '\'');
            return values.TryGetValue(fieldName, out string? actual) &&
                   string.Equals(actual?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    public static string NormalizeElementCode(string? code)
    {
        return string.IsNullOrWhiteSpace(code) ? string.Empty : code.Trim().ToUpperInvariant();
    }

    private static void AddOption(List<string> options, string? raw)
    {
        string value = raw?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            return;
        }

        int separator = value.IndexOfAny(new[] { ':', '=', '|' });
        if (separator > 0 && separator < value.Length - 1)
        {
            value = value[(separator + 1)..].Trim();
        }
        if (value.Length > 0 && !options.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            options.Add(value);
        }
    }

    private static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }
        string token = value.Trim().ToLowerInvariant();
        if (token.StartsWith("u:", StringComparison.Ordinal))
        {
            token = token[2..];
        }
        return string.Concat(token.Where(c => !char.IsWhiteSpace(c) && c != '_' && c != '-'));
    }

    private static string NormalizeGenderRule(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Trim('"', '\'').ToUpperInvariant().Replace('-', '_');
    }

    private static bool IsGenderToken(string? patientGender, params string[] candidates)
    {
        string value = patientGender?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            return false;
        }
        return candidates.Any(candidate => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase))
               || (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int numeric) &&
                   candidates.Contains(numeric.ToString(CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase));
    }

    private static int FindTopLevelOperator(string text, string token)
    {
        int depth = 0;
        for (int i = 0; i <= text.Length - token.Length; i++)
        {
            char c = text[i];
            if (c == '(')
            {
                depth++;
                continue;
            }
            if (c == ')')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }
            if (depth == 0 &&
                text.AsSpan(i, token.Length).Equals(token, StringComparison.OrdinalIgnoreCase) &&
                IsTokenBoundary(text, i - 1) &&
                IsTokenBoundary(text, i + token.Length))
            {
                return i;
            }
        }
        return -1;
    }

    private static bool IsTokenBoundary(string text, int index)
    {
        if (index < 0 || index >= text.Length)
        {
            return true;
        }
        char c = text[index];
        return char.IsWhiteSpace(c) || c is '(' or ')' or '[' or ']';
    }

    private static bool IsWholeParenthesized(string text)
    {
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')') depth--;
            if (depth == 0 && i < text.Length - 1)
            {
                return false;
            }
        }
        return depth == 0;
    }
}
