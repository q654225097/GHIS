using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public sealed class PrescriptionColumnModel : ClientModelBase
{
	private string _fieldCode = string.Empty;

	private string _fieldLabel = string.Empty;

	private string _dataType = "STRING";

	private string _controlType = "u:TextBox";

	private string _placeholder = string.Empty;

	private string _unitText = string.Empty;

	private string? _dictCode;

	private int _width = 120;

	private bool _isSystemField;

	private bool _isEditable = true;

	private bool _isRequired;

	private decimal? _minimum;

	private decimal? _maximum;

	private decimal? _step;

	private string _optionsText = string.Empty;

	public string Id { get; set; } = string.Empty;

	public string FieldCode
	{
		get
		{
			return _fieldCode;
		}
		set
		{
			SetField(ref _fieldCode, value, "FieldCode");
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
			if (SetField(ref _dataType, string.IsNullOrWhiteSpace(value) ? "STRING" : value, "DataType"))
			{
				NotifyControlFlags();
			}
		}
	}

	public string ControlType
	{
		get
		{
			return _controlType;
		}
		set
		{
			if (SetField(ref _controlType, string.IsNullOrWhiteSpace(value) ? "u:TextBox" : value, "ControlType"))
			{
				NotifyControlFlags();
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

	public string UnitText
	{
		get
		{
			return _unitText;
		}
		set
		{
			SetField(ref _unitText, value, "UnitText");
		}
	}

	public string? DictCode
	{
		get
		{
			return _dictCode;
		}
		set
		{
			SetField(ref _dictCode, value, "DictCode");
		}
	}

	public int SortOrder { get; set; }

	public bool IsRequired
	{
		get
		{
			return _isRequired;
		}
		set
		{
			SetField(ref _isRequired, value, "IsRequired");
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

	public IReadOnlyList<string> Options => OptionsText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

	public bool IsTextControl
	{
		get
		{
			string controlType = ControlType;
			if (controlType == "u:TextBox" || controlType == "u:TextArea")
			{
				return true;
			}
			return false;
		}
	}

	public bool IsIntegerControl => ControlType == "u:NumericIntUpDown";

	public bool IsDecimalControl => ControlType == "u:NumericDoubleUpDown";

	public bool IsEnumControl => ControlType == "ComboBox";

	public bool IsDateControl => ControlType == "DatePicker";

	public bool IsBooleanControl => ControlType == "CheckBox";

	public bool IsMultiLine => ControlType == "u:TextArea";

	public int Width
	{
		get
		{
			return _width;
		}
		set
		{
			if (SetField(ref _width, Math.Clamp(value, 48, 600), "Width"))
			{
				OnPropertyChanged("WidthText");
			}
		}
	}

	public string WidthText => $"{Width}px";

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
				OnPropertyChanged("ModeText");
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
				OnPropertyChanged("ModeText");
			}
		}
	}

	public string ModeText
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
			if (rootElement.ValueKind == JsonValueKind.Object)
			{
				Placeholder = ((rootElement.TryGetProperty("placeholder", out var value) && value.ValueKind == JsonValueKind.String) ? (value.GetString() ?? string.Empty) : string.Empty);
				IsRequired = rootElement.TryGetProperty("required", out var value2) && value2.ValueKind == JsonValueKind.True;
				Minimum = ReadDecimal(rootElement, "min");
				Maximum = ReadDecimal(rootElement, "max");
				Step = ReadDecimal(rootElement, "step");
				Width = ((rootElement.TryGetProperty("width", out var value3) && value3.TryGetInt32(out var value4)) ? value4 : 120);
				IsSystemField = rootElement.TryGetProperty("system", out var value5) && value5.ValueKind == JsonValueKind.True;
				IsEditable = !rootElement.TryGetProperty("editable", out var value6) || value6.ValueKind != JsonValueKind.False;
				OptionsText = ((rootElement.TryGetProperty("options", out var value7) && value7.ValueKind == JsonValueKind.Array) ? string.Join(Environment.NewLine, from x in value7.EnumerateArray()
					where x.ValueKind == JsonValueKind.String
					select x.GetString() into x
					where !string.IsNullOrWhiteSpace(x)
					select x) : string.Empty);
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
