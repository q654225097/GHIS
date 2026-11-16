using System.Collections.Generic;
using System.Collections.ObjectModel;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.Stations.OpdDoctor;

public sealed class OpdEmrTemplateModel : ClientModelBase
{
    public string Id { get; set; } = string.Empty;
    public string TemplateCode { get; set; } = string.Empty;
    public int TemplateVersion { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public string EmrScene { get; set; } = "OPD";
    public string DocTypeCode { get; set; } = string.Empty;
    public string? DeptCategory { get; set; }
    public string? TargetDeptId { get; set; }
    public int TimeLimitHours { get; set; }
    public int RequiredSignLevels { get; set; } = 1;
    public string? OfdLayoutId { get; set; }
    public string LayoutName { get; set; } = "国标默认版式";
    public string PageSizeCode { get; set; } = "A4";
    public int SectionCount { get; set; }
    public short IsPublished { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(DeptCategory)
        ? TemplateName
        : $"{TemplateName} · {DeptCategory}";

    public string Summary => $"{DisplayName} ({RequiredSignLevels} 级审签)";
}

public sealed class OpdEmrSectionModel : ClientModelBase
{
    private bool _isVisible = true;

    public string SectionId { get; set; } = string.Empty;
    public string SectionCode { get; set; } = string.Empty;
    public string SectionTitle { get; set; } = string.Empty;
    public string RenderMode { get; set; } = "GRID_FORM";
    public bool IsRequired { get; set; }
    public string VisibilityExpr { get; set; } = "ALL";
    public int SortOrder { get; set; }
    public ObservableCollection<OpdEmrFieldModel> Fields { get; } = new();

    public string DisplayTitle => IsRequired ? $"{SectionTitle} *" : SectionTitle;

    public bool IsVisible
    {
        get => _isVisible;
        set => SetField(ref _isVisible, value, nameof(IsVisible));
    }

    public bool HasVisibleFields => Fields.Any(item => item.IsVisible);

    public bool CanRenderUngrouped => IsVisible && Fields.Count == 0;

    public void NotifyVisibility()
    {
        OnPropertyChanged(nameof(HasVisibleFields));
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(DisplayTitle));
    }

    public void NotifyFieldsChanged()
    {
        OnPropertyChanged(nameof(HasVisibleFields));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(Fields));
    }
}

public sealed class OpdEmrFieldModel : ClientModelBase
{
    private string _value = string.Empty;
    private bool _isVisible = true;
    private string _errorMessage = string.Empty;
    private bool _syncingValues;

    public string ElementCode { get; set; } = string.Empty;
    public string ElementName { get; set; } = string.Empty;
    public string DataType { get; set; } = "STRING";
    public string ControlType { get; set; } = "u:TextBox";
    public string? DictCode { get; set; }
    public string? UnitText { get; set; }
    public string GenderExclusive { get; set; } = "ALL";
    public string SectionCode { get; set; } = string.Empty;
    public string SectionTitle { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public bool IsReadOnly { get; set; }
    public int SortOrder { get; set; }
    public IReadOnlyList<string> Options { get; set; } = Array.Empty<string>();
    public string? Placeholder { get; set; }
    public int? Minimum { get; set; }
    public int? Maximum { get; set; }
    public decimal? DecimalStep { get; set; }
    public string ValidationRuleJson { get; set; } = string.Empty;
    public string PatientGender { get; set; } = string.Empty;
    public string ControlKind { get; private set; } = "text";

    public string Value
    {
        get => _value;
        set
        {
            if (SetField(ref _value, value ?? string.Empty, nameof(Value)))
            {
                SyncTypedValuesFromValue();
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(DisplayValue));
                ErrorMessage = string.Empty;
            }
        }
    }

    public int? IntegerValue
    {
        get => int.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        set
        {
            if (_syncingValues) return;
            Value = value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }

    public decimal? DecimalValue
    {
        get => decimal.TryParse(Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        set
        {
            if (_syncingValues) return;
            Value = value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }

    public DateTimeOffset? DateValue
    {
        get => DateTimeOffset.TryParse(Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed) ? parsed : null;
        set
        {
            if (_syncingValues) return;
            Value = value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }

    public bool BooleanValue
    {
        get => string.Equals(Value, "true", StringComparison.OrdinalIgnoreCase) || Value == "1";
        set
        {
            if (_syncingValues) return;
            Value = value ? "true" : "false";
        }
    }

    public string? EnumValue
    {
        get => string.IsNullOrWhiteSpace(Value) ? null : Value;
        set
        {
            if (_syncingValues) return;
            Value = value ?? string.Empty;
        }
    }

    public string DisplayValue => UnitText is null || UnitText.Length == 0 || Value.Length == 0
        ? Value
        : $"{Value} {UnitText}";

    public string Label => IsRequired ? $"{ElementName} *" : ElementName;

    public bool HasError => IsVisible && !string.IsNullOrWhiteSpace(ErrorMessage);

    public string ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetField(ref _errorMessage, value ?? string.Empty, nameof(ErrorMessage)))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetField(ref _isVisible, value, nameof(IsVisible));
    }

    public DynamicFieldControlKind ResolvedControlKind =>
        DynamicFieldControlResolver.ResolveControlKind(ControlType, DataType, Options);

    public bool IsTextControl => ResolvedControlKind == DynamicFieldControlKind.Text;
    public bool IsIntegerControl => ResolvedControlKind == DynamicFieldControlKind.Integer;
    public bool IsDecimalControl => ResolvedControlKind == DynamicFieldControlKind.Decimal;
    public bool IsEnumControl => ResolvedControlKind == DynamicFieldControlKind.Enum;
    public bool IsDateControl => ResolvedControlKind == DynamicFieldControlKind.Date;
    public bool IsBooleanControl => ResolvedControlKind == DynamicFieldControlKind.Boolean;
    public bool IsMultiLine => ResolvedControlKind == DynamicFieldControlKind.Multiline;
    public bool ShowTextEditor => IsTextControl && IsVisible;
    public bool ShowMultilineEditor => IsMultiLine && IsVisible;
    public bool ShowIntegerEditor => IsIntegerControl && IsVisible;
    public bool ShowDecimalEditor => IsDecimalControl && IsVisible;
    public bool ShowDateEditor => IsDateControl && IsVisible;
    public bool ShowBooleanEditor => IsBooleanControl && IsVisible;
    public bool ShowEnumEditor => IsEnumControl && IsVisible;
    public decimal Step => DecimalStep ?? (IsIntegerControl ? 1m : 0.1m);

    public void ApplyVisibility(IReadOnlyDictionary<string, string>? allValues = null, string? sectionExpression = null)
    {
        IReadOnlyDictionary<string, string> values = allValues ?? new Dictionary<string, string>();
        bool genderVisible = DynamicFieldControlResolver.IsGenderVisible(GenderExclusive, PatientGender);
        bool sectionVisible = string.IsNullOrWhiteSpace(sectionExpression) ||
                              DynamicFieldControlResolver.EvaluateVisibility(
                                  sectionExpression,
                                  values,
                                  rule => DynamicFieldControlResolver.IsGenderVisible(rule, PatientGender));
        IsVisible = genderVisible &&
                    sectionVisible &&
                    DynamicFieldControlResolver.EvaluateVisibility(
                        GenderExclusive,
                        values,
                        rule => DynamicFieldControlResolver.IsGenderVisible(rule, PatientGender));
        NotifyValidation();
    }

    public bool Validate()
    {
        ErrorMessage = string.Empty;
        if (!IsVisible)
        {
            return true;
        }

        string value = Value.Trim();
        if (IsRequired && string.IsNullOrWhiteSpace(value))
        {
            ErrorMessage = $"{ElementName}为必填项";
            return false;
        }
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if ((IsIntegerControl || DataType.Equals("INT", StringComparison.OrdinalIgnoreCase)
                || DataType.Equals("INTEGER", StringComparison.OrdinalIgnoreCase))
            && (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue)
                || (Minimum.HasValue && intValue < Minimum.Value)
                || (Maximum.HasValue && intValue > Maximum.Value)))
        {
            ErrorMessage = BuildRangeError("整数");
            return false;
        }

        if ((IsDecimalControl || DataType.Equals("DECIMAL", StringComparison.OrdinalIgnoreCase)
                || DataType.Equals("NUMBER", StringComparison.OrdinalIgnoreCase))
            && (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal decimalValue)
                || (Minimum.HasValue && decimalValue < Minimum.Value)
                || (Maximum.HasValue && decimalValue > Maximum.Value)))
        {
            ErrorMessage = BuildRangeError("数值");
            return false;
        }

        if ((IsDateControl || DataType.Equals("DATE", StringComparison.OrdinalIgnoreCase)
                || DataType.Equals("DATETIME", StringComparison.OrdinalIgnoreCase))
            && !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out _))
        {
            ErrorMessage = $"{ElementName}日期格式不正确";
            return false;
        }

        if (IsEnumControl && Options.Count > 0 && !Options.Contains(value))
        {
            ErrorMessage = $"{ElementName}须选择有效选项";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(ValidationRuleJson) && !ValidateRules(value))
        {
            return false;
        }

        return true;
    }

    public void NotifyValidation()
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(DisplayValue));
        OnPropertyChanged(nameof(ShowTextEditor));
        OnPropertyChanged(nameof(ShowMultilineEditor));
        OnPropertyChanged(nameof(ShowIntegerEditor));
        OnPropertyChanged(nameof(ShowDecimalEditor));
        OnPropertyChanged(nameof(ShowDateEditor));
        OnPropertyChanged(nameof(ShowBooleanEditor));
        OnPropertyChanged(nameof(ShowEnumEditor));
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(ControlKind));
    }

    private string BuildRangeError(string type)
    {
        if (Minimum.HasValue && Maximum.HasValue)
        {
            return $"{ElementName}须为 {Minimum.Value} 至 {Maximum.Value} 之间的{type}";
        }
        if (Minimum.HasValue)
        {
            return $"{ElementName}不得小于 {Minimum.Value}";
        }
        if (Maximum.HasValue)
        {
            return $"{ElementName}不得大于 {Maximum.Value}";
        }
        return $"{ElementName}须填写有效{type}";
    }

    private bool ValidateRules(string value)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(ValidationRuleJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return true;
            }

            if (document.RootElement.TryGetProperty("maxLength", out JsonElement maxLength)
                && maxLength.TryGetInt32(out int max)
                && value.Length > max)
            {
                ErrorMessage = $"{ElementName}不能超过 {max} 个字符";
                return false;
            }
            if (document.RootElement.TryGetProperty("pattern", out JsonElement pattern)
                && pattern.ValueKind == JsonValueKind.String
                && pattern.GetString() is { Length: > 0 } regex)
            {
                var match = System.Text.RegularExpressions.Regex.Match(value, regex);
                if (!match.Success || match.Index != 0 || match.Length != value.Length)
                {
                    ErrorMessage = $"{ElementName}格式不正确";
                    return false;
                }
            }
        }
        catch (JsonException)
        {
            // A malformed optional rule should not make the clinical form unusable.
        }
        catch (ArgumentException)
        {
        }
        return true;
    }

    private void SyncTypedValuesFromValue()
    {
        _syncingValues = true;
        try
        {
            OnPropertyChanged(nameof(IntegerValue));
            OnPropertyChanged(nameof(DecimalValue));
            OnPropertyChanged(nameof(DateValue));
            OnPropertyChanged(nameof(BooleanValue));
            OnPropertyChanged(nameof(EnumValue));
        }
        finally
        {
            _syncingValues = false;
        }
    }

    public void RefreshResolvedControl()
    {
        ControlKind = ResolvedControlKind switch
        {
            DynamicFieldControlKind.Integer => "integer",
            DynamicFieldControlKind.Decimal => "decimal",
            DynamicFieldControlKind.Enum => "enum",
            DynamicFieldControlKind.Date => "date",
            DynamicFieldControlKind.Boolean => "boolean",
            DynamicFieldControlKind.Multiline => "multiline",
            _ => "text"
        };
        if (Options.Count > 0 && ResolvedControlKind != DynamicFieldControlKind.Enum)
        {
            ControlKind = "enum";
        }
        NotifyValidation();
    }
}
