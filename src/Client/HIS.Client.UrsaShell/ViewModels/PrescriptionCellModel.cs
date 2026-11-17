using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public sealed class PrescriptionCellModel : ClientModelBase
{
	private static readonly HashSet<string> StatusCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SKIN_TEST_STATUS", "AUDIT_STATUS" };

	public PrescriptionItemModel Item { get; }

	public PrescriptionColumnModel Column { get; }

	public string FieldCode => Column.FieldCode;

	public string FieldLabel => Column.FieldLabel;

	public int Width => Column.Width;

	public string Placeholder => string.IsNullOrWhiteSpace(Column.Placeholder) ? FieldLabel : Column.Placeholder;

	public string UnitText => Column.UnitText;

	public decimal? Minimum => Column.Minimum;

	public decimal? Maximum => Column.Maximum;

	public decimal? Step => Column.Step;

	public IReadOnlyList<string> Options => Column.Options;

	public bool IsEditable => Column.IsEditable;

	public bool ShowEditor => Item.IsEditing && IsEditable;

	public bool ShowText => !ShowEditor;

	public bool ShowTextEditor => ShowEditor && Column.IsTextControl;

	public bool ShowIntegerEditor => ShowEditor && Column.IsIntegerControl;

	public bool ShowDecimalEditor => ShowEditor && Column.IsDecimalControl;

	public bool ShowEnumEditor => ShowEditor && Column.IsEnumControl;

	public bool ShowDateEditor => ShowEditor && Column.IsDateControl;

	public bool ShowBooleanEditor => ShowEditor && Column.IsBooleanControl;

	public bool AcceptsReturn => Column.IsMultiLine;

	public bool IsStatusColumn => StatusCodes.Contains(Column.FieldCode);

	public bool IsDrugName => Column.FieldCode.Equals("DRUG_NAME", StringComparison.OrdinalIgnoreCase);

	public bool ShowBlockedBadge => IsDrugName && Item.IsBlocked;

	public string Value
	{
		get
		{
			return Item.IsEditing ? Item.EditDraft.GetValue(Column.FieldCode, Item.GetColumnValue(Column.FieldCode)) : Item.GetColumnValue(Column.FieldCode);
		}
		set
		{
			if (value == null)
			{
				value = string.Empty;
			}
			if (!string.Equals(Value, value, StringComparison.Ordinal))
			{
				Item.SetEditingValue(Column.FieldCode, value);
				OnPropertyChanged("Value");
				NotifyTypedValues();
			}
		}
	}

	public int? IntegerValue
	{
		get
		{
			string s = ExtractNumber(Value);
			int result;
			return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? new int?(result) : ((int?)null);
		}
		set
		{
			Value = (value.HasValue ? ComposeNumericValue(value.Value.ToString(CultureInfo.InvariantCulture)) : string.Empty);
		}
	}

	public decimal? DecimalValue
	{
		get
		{
			string s = ExtractNumber(Value);
			decimal result;
			return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out result) ? new decimal?(result) : ((decimal?)null);
		}
		set
		{
			Value = (value.HasValue ? ComposeNumericValue(value.Value.ToString("0.####", CultureInfo.InvariantCulture)) : string.Empty);
		}
	}

	public DateTimeOffset? DateValue
	{
		get
		{
			DateTimeOffset result;
			return DateTimeOffset.TryParse(Value, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out result) ? new DateTimeOffset?(result) : ((DateTimeOffset?)null);
		}
		set
		{
			Value = value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
		}
	}

	public bool BooleanValue
	{
		get
		{
			return IsTruthy(Value);
		}
		set
		{
			Value = (value ? "是" : "否");
		}
	}

	public string? EnumValue
	{
		get
		{
			string text = Options.FirstOrDefault((string x) => string.Equals(x, Value, StringComparison.OrdinalIgnoreCase));
			return text ?? ((Options.Count == 0) ? Value : null);
		}
		set
		{
			if (value != null)
			{
				Value = value;
			}
		}
	}

	public string DisplayValue
	{
		get
		{
			string value = Value;
			if (!string.IsNullOrWhiteSpace(value))
			{
				return (!string.IsNullOrWhiteSpace(Column.UnitText) && (Column.IsIntegerControl || Column.IsDecimalControl) && !value.Contains(Column.UnitText, StringComparison.OrdinalIgnoreCase)) ? (value + Column.UnitText) : value;
			}
			return IsStatusColumn ? "—" : string.Empty;
		}
	}

	public string ValueForeground
	{
		get
		{
			string fieldCode = Column.FieldCode;
			if (1 == 0)
			{
			}
			string result;
			switch (fieldCode)
			{
			case "DRUG_NAME":
				if (!Item.IsBlocked)
				{
					goto default;
				}
				result = "#CB2634";
				break;
			case "SKIN_TEST_STATUS":
				if (Item.SkinTestClass == "Danger")
				{
					result = "#CB2634";
					break;
				}
				if (!(Item.SkinTestClass == "Warning"))
				{
					goto default;
				}
				result = "#D25F00";
				break;
			case "AUDIT_STATUS":
				if (Item.AuditClass == "Danger")
				{
					result = "#CB2634";
					break;
				}
				if (!(Item.AuditClass == "Success"))
				{
					goto default;
				}
				result = "#008A3E";
				break;
			case "AMOUNT":
				result = "#165DFF";
				break;
			default:
				result = "#1D2129";
				break;
			}
			if (1 == 0)
			{
			}
			return result;
		}
	}

	public string ValueBackground
	{
		get
		{
			string fieldCode = Column.FieldCode;
			if (1 == 0)
			{
			}
			string result;
			switch (fieldCode)
			{
			case "DRUG_NAME":
				if (!Item.IsBlocked)
				{
					goto default;
				}
				result = "#FFECE8";
				break;
			case "SKIN_TEST_STATUS":
				if (Item.SkinTestClass == "Danger")
				{
					result = "#FFECE8";
					break;
				}
				if (!(Item.SkinTestClass == "Warning"))
				{
					goto default;
				}
				result = "#FFF7E8";
				break;
			case "AUDIT_STATUS":
				if (Item.AuditClass == "Danger")
				{
					result = "#FFECE8";
					break;
				}
				if (!(Item.AuditClass == "Success"))
				{
					goto default;
				}
				result = "#E8FFEA";
				break;
			default:
				result = "Transparent";
				break;
			}
			if (1 == 0)
			{
			}
			return result;
		}
	}

	public string EditorBackground => IsStatusColumn ? ValueBackground : "Transparent";

	public string EditorBorderBrush => IsStatusColumn ? "Transparent" : "#C9CDD4";

	public bool ShowStatusBorder => IsStatusColumn && ValueBackground != "Transparent";

	public string ValueFontWeight
	{
		get
		{
			bool flag;
			switch (Column.FieldCode)
			{
			case "DRUG_NAME":
			case "AMOUNT":
			case "SKIN_TEST_STATUS":
			case "AUDIT_STATUS":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			return flag ? "SemiBold" : "Normal";
		}
	}

	public IBrush ValueForegroundBrush => Brush.Parse(ValueForeground);

	public IBrush ValueBackgroundBrush => Brush.Parse(ValueBackground);

	public IBrush EditorBackgroundBrush => Brush.Parse(EditorBackground);

	public IBrush EditorBorderBrushValue => Brush.Parse(EditorBorderBrush);

	public IBrush CellBorderBrush
	{
		get
		{
			IBrush result;
			if (!ShowStatusBorder)
			{
				IBrush transparent = Brushes.Transparent;
				result = transparent;
			}
			else
			{
				result = Brush.Parse(ValueForeground);
			}
			return result;
		}
	}

	public Thickness CellBorderThickness => new Thickness(ShowStatusBorder ? 1 : 0);

	public FontWeight ValueFontWeightTyped => (ValueFontWeight == "SemiBold") ? FontWeight.DemiBold : FontWeight.Normal;

	public PrescriptionCellModel(PrescriptionItemModel item, PrescriptionColumnModel column)
	{
		Item = item;
		Column = column;
	}

	public void NotifyItemChanged()
	{
		OnPropertyChanged("Value");
		NotifyTypedValues();
		OnPropertyChanged("DisplayValue");
		OnPropertyChanged("ShowEditor");
		OnPropertyChanged("ShowText");
		OnPropertyChanged("ShowTextEditor");
		OnPropertyChanged("ShowIntegerEditor");
		OnPropertyChanged("ShowDecimalEditor");
		OnPropertyChanged("ShowEnumEditor");
		OnPropertyChanged("ShowDateEditor");
		OnPropertyChanged("ShowBooleanEditor");
		OnPropertyChanged("AcceptsReturn");
		OnPropertyChanged("Placeholder");
		OnPropertyChanged("UnitText");
		OnPropertyChanged("Minimum");
		OnPropertyChanged("Maximum");
		OnPropertyChanged("Step");
		OnPropertyChanged("Options");
		OnPropertyChanged("IsDrugName");
		OnPropertyChanged("ShowBlockedBadge");
		OnPropertyChanged("ValueForeground");
		OnPropertyChanged("ValueBackground");
		OnPropertyChanged("ValueFontWeight");
		OnPropertyChanged("ValueForegroundBrush");
		OnPropertyChanged("ValueBackgroundBrush");
		OnPropertyChanged("EditorBackgroundBrush");
		OnPropertyChanged("EditorBorderBrushValue");
		OnPropertyChanged("CellBorderBrush");
		OnPropertyChanged("CellBorderThickness");
		OnPropertyChanged("ValueFontWeightTyped");
		OnPropertyChanged("EditorBackground");
		OnPropertyChanged("EditorBorderBrush");
		OnPropertyChanged("ShowStatusBorder");
	}

	private void NotifyTypedValues()
	{
		OnPropertyChanged("IntegerValue");
		OnPropertyChanged("DecimalValue");
		OnPropertyChanged("DateValue");
		OnPropertyChanged("BooleanValue");
		OnPropertyChanged("EnumValue");
		OnPropertyChanged("DisplayValue");
	}

	private string ComposeNumericValue(string formattedNumber)
	{
		string text = Value.Trim();
		string text2 = ExtractNumber(text);
		if (string.IsNullOrWhiteSpace(text2))
		{
			return formattedNumber + Column.UnitText;
		}
		int num = text.IndexOf(text2, StringComparison.Ordinal);
		string text3 = ((num > 0) ? text.Substring(0, num) : string.Empty);
		string text4 = ((num >= 0) ? text.Substring(num + text2.Length).Trim() : string.Empty);
		if (!string.IsNullOrWhiteSpace(Column.UnitText))
		{
			text4 = Column.UnitText;
		}
		return text3 + formattedNumber + text4;
	}

	private static string ExtractNumber(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return string.Empty;
		}
		text = text.Trim();
		if (decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _))
		{
			return text;
		}
		// Strip percentage concentration tokens like 0.9%, 5%, 10%
		string filtered = System.Text.RegularExpressions.Regex.Replace(text, @"\d+(?:\.\d+)?%", " ");
		// Match number adjacent to dose/quantity unit or at the end
		var match = System.Text.RegularExpressions.Regex.Match(
			filtered,
			@"(?<!\S)(\d+(?:\.\d+)?)\s*(?:mg|g|ml|l|u|单位|片|粒|支|袋|瓶|丸|次|盒)?(?!\S)",
			System.Text.RegularExpressions.RegexOptions.RightToLeft | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
		if (match.Success)
		{
			return match.Groups[1].Value;
		}
		var fallback = System.Text.RegularExpressions.Regex.Match(filtered, @"\d+(?:\.\d+)?");
		return fallback.Success ? fallback.Value : string.Empty;
	}

	private static bool IsTruthy(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		string text = value.Trim();
		if (1 == 0)
		{
		}
		bool result;
		switch (text)
		{
		case "1":
		case "是":
		case "已确认":
		case "true":
		case "TRUE":
		case "True":
			result = true;
			break;
		default:
		{
			result = bool.TryParse(value, out var result2) & result2;
			break;
		}
		}
		if (1 == 0)
		{
		}
		return result;
	}
}
