using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public sealed class DynamicFormFieldModel : ClientModelBase
{
	private string _fieldCode = string.Empty;

	private string _fieldLabel = string.Empty;

	private string _dataType = "STRING";

	private string _ursaControlType = "u:TextBox";

	private string _controlType = "SINGLE_TEXT";

	private string _controlKey = string.Empty;

	private string _unitText = string.Empty;

	private string _visibilityExpr = "ALL";

	private bool _isRequired;

	private string _placeholder = string.Empty;

	private string _optionsText = string.Empty;

	private int _sortOrder;

	private decimal? _minimum;

	private decimal? _maximum;

	private decimal? _step;

	private string _runtimeValue = string.Empty;

	private int? _runtimeInt;

	private decimal? _runtimeDecimal;

	private bool _runtimeBoolean;

	private DateTimeOffset? _runtimeDate;

	private bool _isVisibleForCurrentPatient = true;

	private int _columnWidth = 120;

	private bool _isSystemField;

	private bool _isEditable = true;

	private string? _extDataJson;

	private int _schemaVersion = 1;

	public string Id { get; set; } = string.Empty;

	public string FormSchemaId { get; set; } = string.Empty;

	public string FieldCode
	{
		get
		{
			return _fieldCode;
		}
		set
		{
			if (SetField(ref _fieldCode, value, "FieldCode"))
			{
				OnPropertyChanged("IsProtectedField");
				OnPropertyChanged("CanRemove");
				OnPropertyChanged("RemoveBlockedReason");
				OnPropertyChanged("HideActionTip");
				OnPropertyChanged("RemoveButtonTip");
			}
		}
	}

	public string FieldLabel
	{
		get
		{
			return _fieldLabel;
		}
		set
		{
			SetField(ref _fieldLabel, value, "FieldLabel");
		}
	}

	public string DataType
	{
		get
		{
			return _dataType;
		}
		set
		{
			if (SetField(ref _dataType, value, "DataType"))
			{
				NotifyControlFlags();
			}
		}
	}

	public string UrsaControlType
	{
		get
		{
			return _ursaControlType;
		}
		set
		{
			if (SetField(ref _ursaControlType, value, "UrsaControlType"))
			{
				_controlType = MapUrsaToControlType(value);
				NotifyControlFlags();
				OnPropertyChanged("ControlType");
				OnPropertyChanged("ControlDisplayText");
			}
		}
	}

	public string ControlType
	{
		get => _controlType;
		set
		{
			if (SetField(ref _controlType, value, "ControlType"))
			{
				_ursaControlType = MapControlTypeToUrsa(value);
				NotifyControlFlags();
				OnPropertyChanged("UrsaControlType");
				OnPropertyChanged("ControlDisplayText");
			}
		}
	}

	public static string MapUrsaToControlType(string? ursaType) => ursaType switch
	{
		"u:TextArea" => "MULTI_TEXT",
		"u:NumericIntUpDown" => "INT_INPUT",
		"u:NumericDoubleUpDown" => "DECIMAL_INPUT",
		"ComboBox" => "SELECT",
		"DatePicker" => "DATE",
		"CheckBox" => "CHECKBOX",
		_ => "SINGLE_TEXT"
	};

	public static string MapControlTypeToUrsa(string? controlType) => controlType?.ToUpperInvariant() switch
	{
		"MULTI_TEXT" => "u:TextArea",
		"INT_INPUT" => "u:NumericIntUpDown",
		"DECIMAL_INPUT" => "u:NumericDoubleUpDown",
		"SELECT" => "ComboBox",
		"DATE" => "DatePicker",
		"CHECKBOX" => "CheckBox",
		_ => "u:TextBox"
	};

	public string ControlKey
	{
		get
		{
			return _controlKey;
		}
		set
		{
			SetField(ref _controlKey, value, "ControlKey");
		}
	}

	public string? DictCode { get; set; }

	public string UnitText
	{
		get
		{
			return _unitText;
		}
		set
		{
			if (SetField(ref _unitText, value, "UnitText"))
			{
				OnPropertyChanged("UnitDisplayText");
			}
		}
	}

	public string VisibilityExpr
	{
		get
		{
			return _visibilityExpr;
		}
		set
		{
			SetField(ref _visibilityExpr, value, "VisibilityExpr");
		}
	}

	public short IsIndexed { get; set; }

	public int SortOrder
	{
		get
		{
			return _sortOrder;
		}
		set
		{
			if (SetField(ref _sortOrder, value, "SortOrder"))
			{
				OnPropertyChanged("OrderNumber");
			}
		}
	}

	public int OrderNumber => SortOrder + 1;

	public bool IsProtectedField => string.Equals(Id, "FIELD-RX-ITEM-01", StringComparison.OrdinalIgnoreCase) || string.Equals(FieldCode, "DRUG_NAME", StringComparison.OrdinalIgnoreCase);

	public bool CanRemove => !IsProtectedField;

	public string RemoveBlockedReason => IsProtectedField ? "药品名称是处方明细的主字段，不能隐藏或删除" : string.Empty;

	public string HideActionTip => IsProtectedField ? RemoveBlockedReason : "从处方表格中隐藏该列，后台保留历史值";

	public string RemoveButtonTip => IsProtectedField ? RemoveBlockedReason : "从表格配置中移除该字段";

	public bool IsRequired
	{
		get
		{
			return _isRequired;
		}
		set
		{
			if (SetField(ref _isRequired, value, "IsRequired"))
			{
				OnPropertyChanged("RequirementText");
			}
		}
	}

	public string Placeholder
	{
		get
		{
			return _placeholder;
		}
		set
		{
			SetField(ref _placeholder, value, "Placeholder");
		}
	}

	public string OptionsText
	{
		get
		{
			return _optionsText;
		}
		set
		{
			if (SetField(ref _optionsText, value, "OptionsText"))
			{
				OnPropertyChanged("Options");
			}
		}
	}

	public decimal? Minimum
	{
		get
		{
			return _minimum;
		}
		set
		{
			SetField(ref _minimum, value, "Minimum");
		}
	}

	public decimal? Maximum
	{
		get
		{
			return _maximum;
		}
		set
		{
			SetField(ref _maximum, value, "Maximum");
		}
	}

	public decimal? Step
	{
		get
		{
			return _step;
		}
		set
		{
			SetField(ref _step, value, "Step");
		}
	}

	public string RuntimeValue
	{
		get
		{
			return _runtimeValue;
		}
		set
		{
			if (SetField(ref _runtimeValue, value, "RuntimeValue"))
			{
				if (int.TryParse(value, out var result))
				{
					_runtimeInt = result;
				}
				if (decimal.TryParse(value, out var result2))
				{
					_runtimeDecimal = result2;
				}
				if (bool.TryParse(value, out var result3))
				{
					_runtimeBoolean = result3;
				}
				if (DateTimeOffset.TryParse(value, out var result4))
				{
					_runtimeDate = result4;
				}
				OnPropertyChanged("RuntimeInt");
				OnPropertyChanged("RuntimeDecimal");
				OnPropertyChanged("RuntimeBoolean");
				OnPropertyChanged("RuntimeDate");
			}
		}
	}

	public int? RuntimeInt
	{
		get
		{
			return _runtimeInt;
		}
		set
		{
			if (SetField(ref _runtimeInt, value, "RuntimeInt"))
			{
				RuntimeValue = value?.ToString() ?? string.Empty;
			}
		}
	}

	public decimal? RuntimeDecimal
	{
		get
		{
			return _runtimeDecimal;
		}
		set
		{
			if (SetField(ref _runtimeDecimal, value, "RuntimeDecimal"))
			{
				RuntimeValue = value?.ToString() ?? string.Empty;
			}
		}
	}

	public bool RuntimeBoolean
	{
		get
		{
			return _runtimeBoolean;
		}
		set
		{
			if (SetField(ref _runtimeBoolean, value, "RuntimeBoolean"))
			{
				RuntimeValue = value.ToString();
			}
		}
	}

	public DateTimeOffset? RuntimeDate
	{
		get
		{
			return _runtimeDate;
		}
		set
		{
			if (SetField(ref _runtimeDate, value, "RuntimeDate"))
			{
				RuntimeValue = value?.ToString("yyyy-MM-dd") ?? string.Empty;
			}
		}
	}

	public bool IsVisibleForCurrentPatient
	{
		get
		{
			return _isVisibleForCurrentPatient;
		}
		set
		{
			SetField(ref _isVisibleForCurrentPatient, value, "IsVisibleForCurrentPatient");
		}
	}

	public int ColumnWidth
	{
		get
		{
			return _columnWidth;
		}
		set
		{
			int value2 = Math.Clamp(value, 48, 600);
			if (SetField(ref _columnWidth, value2, "ColumnWidth"))
			{
				OnPropertyChanged("ColumnWidthText");
				OnPropertyChanged("FieldModeText");
			}
		}
	}

	public string ColumnWidthText => $"{ColumnWidth}px";

	public bool IsSystemField
	{
		get
		{
			return _isSystemField;
		}
		set
		{
			if (SetField(ref _isSystemField, value, "IsSystemField"))
			{
				OnPropertyChanged("FieldModeText");
				OnPropertyChanged("FieldModeClass");
			}
		}
	}

	public bool IsEditable
	{
		get
		{
			return _isEditable;
		}
		set
		{
			if (SetField(ref _isEditable, value, "IsEditable"))
			{
				OnPropertyChanged("FieldModeText");
				OnPropertyChanged("EditableText");
			}
		}
	}

	public string? ExtDataJson
	{
		get
		{
			return _extDataJson;
		}
		set
		{
			SetField(ref _extDataJson, value, "ExtDataJson");
		}
	}

	public int SchemaVersion
	{
		get
		{
			return _schemaVersion;
		}
		set
		{
			SetField(ref _schemaVersion, value, "SchemaVersion");
		}
	}

	public string FieldModeText
	{
		get
		{
			string result;
			if (IsEditable)
			{
				result = (IsSystemField ? "系统字段" : "自定义字段");
			}
			else
			{
				result = "只读";
			}
			return result;
		}
	}

	public string FieldModeClass
	{
		get
		{
			string result;
			if (IsEditable)
			{
				result = (IsSystemField ? "Primary" : "Success");
			}
			else
			{
				result = "Secondary";
			}
			return result;
		}
	}

	public string ControlDisplayText => ControlType switch
	{
		"MULTI_TEXT" => "多行文本",
		"INT_INPUT" => "整数",
		"DECIMAL_INPUT" => "小数",
		"SELECT" => "下拉选择",
		"DATE" => "日期",
		"CHECKBOX" => "是 / 否",
		_ => UrsaControlType switch
		{
			"u:TextArea" => "多行文本",
			"u:NumericIntUpDown" => "整数",
			"u:NumericDoubleUpDown" => "小数",
			"ComboBox" => "下拉选择",
			"DatePicker" => "日期",
			"CheckBox" => "是 / 否",
			_ => "单行文本"
		}
	};

	public string UnitDisplayText => string.IsNullOrWhiteSpace(UnitText) ? "无单位" : UnitText;

	public string RequirementText => IsRequired ? "必填" : "选填";

	public string EditableText => IsEditable ? "可编辑" : "只读";

	public bool IsTextControl => ControlType == "SINGLE_TEXT" || ControlType == "MULTI_TEXT" || UrsaControlType == "u:TextBox" || UrsaControlType == "u:TextArea";

	public bool IsIntegerControl => ControlType == "INT_INPUT" || UrsaControlType == "u:NumericIntUpDown";

	public bool IsDecimalControl => ControlType == "DECIMAL_INPUT" || UrsaControlType == "u:NumericDoubleUpDown";

	public bool IsEnumControl => ControlType == "SELECT" || UrsaControlType == "ComboBox";

	public bool IsDateControl => ControlType == "DATE" || UrsaControlType == "DatePicker";

	public bool IsBooleanControl => ControlType == "CHECKBOX" || UrsaControlType == "CheckBox";

	public bool IsMultiLine => ControlType == "MULTI_TEXT" || UrsaControlType == "u:TextArea";

	public IEnumerable<string> Options => OptionsText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

	public DynamicFormFieldModel Clone()
	{
		return new DynamicFormFieldModel
		{
			Id = Id,
			FormSchemaId = FormSchemaId,
			FieldCode = FieldCode,
			FieldLabel = FieldLabel,
			DataType = DataType,
			UrsaControlType = UrsaControlType,
			ControlKey = ControlKey,
			DictCode = DictCode,
			UnitText = UnitText,
			VisibilityExpr = VisibilityExpr,
			IsIndexed = IsIndexed,
			SortOrder = SortOrder,
			IsRequired = IsRequired,
			Placeholder = Placeholder,
			OptionsText = OptionsText,
			Minimum = Minimum,
			Maximum = Maximum,
			Step = Step,
			RuntimeValue = RuntimeValue,
			IsVisibleForCurrentPatient = IsVisibleForCurrentPatient,
			ColumnWidth = ColumnWidth,
			IsSystemField = IsSystemField,
			IsEditable = IsEditable,
			ExtDataJson = ExtDataJson,
			SchemaVersion = SchemaVersion
		};
	}

	public void CopyEditablePropertiesFrom(DynamicFormFieldModel source)
	{
		FieldCode = source.FieldCode;
		FieldLabel = source.FieldLabel;
		DataType = source.DataType;
		UrsaControlType = source.UrsaControlType;
		ControlKey = source.ControlKey;
		DictCode = source.DictCode;
		UnitText = source.UnitText;
		VisibilityExpr = source.VisibilityExpr;
		IsIndexed = source.IsIndexed;
		SortOrder = source.SortOrder;
		IsRequired = source.IsRequired;
		Placeholder = source.Placeholder;
		OptionsText = source.OptionsText;
		Minimum = source.Minimum;
		Maximum = source.Maximum;
		Step = source.Step;
		RuntimeValue = source.RuntimeValue;
		IsVisibleForCurrentPatient = source.IsVisibleForCurrentPatient;
		ColumnWidth = source.ColumnWidth;
		IsSystemField = source.IsSystemField;
		IsEditable = source.IsEditable;
		ExtDataJson = source.ExtDataJson;
		SchemaVersion = source.SchemaVersion;
	}

	public string BuildValidationRuleJson()
	{
		Dictionary<string, object> dictionary = new Dictionary<string, object> { ["required"] = IsRequired };
		if (!string.IsNullOrWhiteSpace(Placeholder))
		{
			dictionary["placeholder"] = Placeholder;
		}
		if (Minimum.HasValue)
		{
			dictionary["min"] = Minimum.Value;
		}
		if (Maximum.HasValue)
		{
			dictionary["max"] = Maximum.Value;
		}
		if (Step.HasValue)
		{
			dictionary["step"] = Step.Value;
		}
		dictionary["width"] = ColumnWidth;
		dictionary["system"] = IsSystemField;
		dictionary["editable"] = IsEditable;
		string[] array = Options.ToArray();
		if (array.Length != 0)
		{
			dictionary["options"] = array;
		}
		return JsonSerializer.Serialize(dictionary);
	}

	public void LoadValidationRuleJson(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return;
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(json);
			JsonElement rootElement = jsonDocument.RootElement;
			if (rootElement.ValueKind != JsonValueKind.Object)
			{
				return;
			}
			IsRequired = rootElement.TryGetProperty("required", out var value) && value.ValueKind == JsonValueKind.True;
			Placeholder = ((rootElement.TryGetProperty("placeholder", out var value2) && value2.ValueKind == JsonValueKind.String) ? (value2.GetString() ?? string.Empty) : string.Empty);
			Minimum = ReadDecimal(rootElement, "min");
			Maximum = ReadDecimal(rootElement, "max");
			Step = ReadDecimal(rootElement, "step");
			ColumnWidth = ((rootElement.TryGetProperty("width", out var value3) && value3.TryGetInt32(out var value4)) ? value4 : 120);
			IsSystemField = rootElement.TryGetProperty("system", out var value5) && value5.ValueKind == JsonValueKind.True;
			IsEditable = !rootElement.TryGetProperty("editable", out var value6) || value6.ValueKind != JsonValueKind.False;
			if (rootElement.TryGetProperty("options", out var value7) && value7.ValueKind == JsonValueKind.Array)
			{
				OptionsText = string.Join(Environment.NewLine, from x in value7.EnumerateArray()
					where x.ValueKind == JsonValueKind.String
					select x.GetString());
			}
		}
		catch (Exception ex) when ((ex is JsonException || ex is InvalidOperationException) ? true : false)
		{
			OptionsText = string.Empty;
		}
	}

	private static decimal? ReadDecimal(JsonElement root, string property)
	{
		JsonElement value;
		decimal value2;
		return (root.TryGetProperty(property, out value) && value.TryGetDecimal(out value2)) ? new decimal?(value2) : ((decimal?)null);
	}

	private void NotifyControlFlags()
	{
		OnPropertyChanged("IsTextControl");
		OnPropertyChanged("IsIntegerControl");
		OnPropertyChanged("IsDecimalControl");
		OnPropertyChanged("IsEnumControl");
		OnPropertyChanged("IsDateControl");
		OnPropertyChanged("IsBooleanControl");
		OnPropertyChanged("IsMultiLine");
	}
}
