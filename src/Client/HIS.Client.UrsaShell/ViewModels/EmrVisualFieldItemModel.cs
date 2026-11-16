using System;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class EmrVisualFieldItemModel : ClientModelBase
{
	private string _elementCode = string.Empty;

	private string _nhcStdCode = "DE04.01.119.00";

	private string _elementName = string.Empty;

	private string _dataType = "STRING";

	private string _ursaControlType = "u:TextBox";

	private string? _unitText;

	private string _genderExclusive = "ALL";

	private string _valueText = string.Empty;

	private string _placeholderText = "请输入结构化临床内容...";

	private bool _isRequired = true;

	private bool _isVisibleForPatient = true;

	public string ElementCode
	{
		get
		{
			return _elementCode;
		}
		set
		{
			SetField(ref _elementCode, value, "ElementCode");
		}
	}

	public string NhcStdCode
	{
		get
		{
			return _nhcStdCode;
		}
		set
		{
			SetField(ref _nhcStdCode, value, "NhcStdCode");
		}
	}

	public string ElementName
	{
		get
		{
			return _elementName;
		}
		set
		{
			if (SetField(ref _elementName, value, "ElementName"))
			{
				OnPropertyChanged("DisplayLabel");
			}
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
				OnPropertyChanged("IsMultiline");
				OnPropertyChanged("IsSingleLine");
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
				OnPropertyChanged("IsMultiline");
				OnPropertyChanged("IsSingleLine");
			}
		}
	}

	public string? UnitText
	{
		get
		{
			return _unitText;
		}
		set
		{
			if (SetField(ref _unitText, value, "UnitText"))
			{
				OnPropertyChanged("DisplayLabel");
				OnPropertyChanged("FormattedPrintValue");
			}
		}
	}

	public string GenderExclusive
	{
		get
		{
			return _genderExclusive;
		}
		set
		{
			if (SetField(ref _genderExclusive, value, "GenderExclusive"))
			{
				OnPropertyChanged("GenderBadgeText");
				OnPropertyChanged("HasGenderConstraint");
			}
		}
	}

	public string ValueText
	{
		get
		{
			return _valueText;
		}
		set
		{
			if (SetField(ref _valueText, value, "ValueText"))
			{
				OnPropertyChanged("FormattedPrintValue");
			}
		}
	}

	public string PlaceholderText
	{
		get
		{
			return _placeholderText;
		}
		set
		{
			SetField(ref _placeholderText, value, "PlaceholderText");
		}
	}

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
				OnPropertyChanged("RequiredBadgeText");
			}
		}
	}

	public bool IsVisibleForPatient
	{
		get
		{
			return _isVisibleForPatient;
		}
		set
		{
			SetField(ref _isVisibleForPatient, value, "IsVisibleForPatient");
		}
	}

	public bool IsMultiline => DataType.Equals("TEXT", StringComparison.OrdinalIgnoreCase) || UrsaControlType.Contains("TextArea", StringComparison.OrdinalIgnoreCase);

	public bool IsSingleLine => !IsMultiline;

	public bool HasGenderConstraint => !string.Equals(GenderExclusive, "ALL", StringComparison.OrdinalIgnoreCase);

	public string DisplayLabel => string.IsNullOrWhiteSpace(UnitText) ? (ElementName + " (" + ElementCode + ")") : (ElementName + " (" + UnitText + ")");

	public string GenderBadgeText
	{
		get
		{
			string genderExclusive = GenderExclusive;
			if (1 == 0)
			{
			}
			string result;
			if (genderExclusive == "MALE_ONLY")
			{
				result = "仅限男性";
			}
			else
			{
				result = ((!(genderExclusive == "FEMALE_ONLY")) ? "全性别通用" : "仅限女性");
			}
			if (1 == 0)
			{
			}
			return result;
		}
	}

	public string RequiredBadgeText => IsRequired ? "必填" : "选填";

	public string FormattedPrintValue
	{
		get
		{
			string result;
			if (string.IsNullOrWhiteSpace(ValueText))
			{
				result = "—";
			}
			else
			{
				result = (string.IsNullOrWhiteSpace(UnitText) ? ValueText : (ValueText + " " + UnitText));
			}
			return result;
		}
	}
}
