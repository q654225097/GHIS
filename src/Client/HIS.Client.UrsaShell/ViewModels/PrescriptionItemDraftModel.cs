using System;
using System.Collections.Generic;
using System.Linq;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class PrescriptionItemDraftModel : ClientModelBase
{
	private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	public string DrugName
	{
		get
		{
			return GetValue("DRUG_NAME");
		}
		set
		{
			SetValue("DRUG_NAME", value);
		}
	}

	public string Spec
	{
		get
		{
			return GetValue("SPEC");
		}
		set
		{
			SetValue("SPEC", value);
		}
	}

	public string Dose
	{
		get
		{
			return GetValue("DOSE");
		}
		set
		{
			SetValue("DOSE", value);
		}
	}

	public string Freq
	{
		get
		{
			return GetValue("FREQ");
		}
		set
		{
			SetValue("FREQ", value);
		}
	}

	public string Route
	{
		get
		{
			return GetValue("ROUTE");
		}
		set
		{
			SetValue("ROUTE", value);
		}
	}

	public string Quantity
	{
		get
		{
			return GetValue("QUANTITY");
		}
		set
		{
			SetValue("QUANTITY", value);
		}
	}

	public string Amount
	{
		get
		{
			return GetValue("AMOUNT");
		}
		set
		{
			SetValue("AMOUNT", value);
		}
	}

	public string Days
	{
		get
		{
			return GetValue("DAYS");
		}
		set
		{
			SetValue("DAYS", value);
		}
	}

	public IReadOnlyDictionary<string, string> AllValues => _values;

	public IReadOnlyDictionary<string, string> CustomValues => _values.Where((KeyValuePair<string, string> x) => !PrescriptionItemModel.IsBuiltInFieldCode(x.Key)).ToDictionary((KeyValuePair<string, string> x) => x.Key, (KeyValuePair<string, string> x) => x.Value, StringComparer.OrdinalIgnoreCase);

	public string GetValue(string fieldCode, string fallback = "")
	{
		if (string.IsNullOrWhiteSpace(fieldCode))
		{
			return fallback;
		}
		string value;
		return _values.TryGetValue(fieldCode, out value) ? value : fallback;
	}

	public void SetValue(string fieldCode, string? value)
	{
		if (string.IsNullOrWhiteSpace(fieldCode))
		{
			return;
		}
		string text = fieldCode.Trim();
		string text2 = value ?? string.Empty;
		if (!_values.TryGetValue(text, out string value2) || !string.Equals(value2, text2, StringComparison.Ordinal))
		{
			_values[text] = text2;
			OnPropertyChanged(text);
			OnPropertyChanged("AllValues");
			OnPropertyChanged("CustomValues");
			if (PrescriptionItemModel.TryGetBuiltInPropertyName(text, out string propertyName))
			{
				OnPropertyChanged(propertyName);
			}
		}
	}

	public void ReplaceValues(IEnumerable<KeyValuePair<string, string>> values)
	{
		_values.Clear();
		foreach (KeyValuePair<string, string> value in values)
		{
			if (!string.IsNullOrWhiteSpace(value.Key))
			{
				string key = value.Key.Trim();
				_values[key] = value.Value ?? string.Empty;
				if (PrescriptionItemModel.TryGetBuiltInPropertyName(key, out string propertyName))
				{
					OnPropertyChanged(propertyName);
				}
			}
		}
		OnPropertyChanged("AllValues");
		OnPropertyChanged("CustomValues");
	}

	public void ReplaceCustomValues(IEnumerable<KeyValuePair<string, string>> values)
	{
		string[] array = _values.Keys.Where(PrescriptionItemModel.IsBuiltInFieldCode).ToArray();
		foreach (string key in array)
		{
			_values.Remove(key);
		}
		foreach (KeyValuePair<string, string> value in values)
		{
			if (!string.IsNullOrWhiteSpace(value.Key))
			{
				_values[value.Key.Trim()] = value.Value ?? string.Empty;
			}
		}
		OnPropertyChanged("AllValues");
		OnPropertyChanged("CustomValues");
	}

	public void LoadFrom(PrescriptionItemModel item)
	{
		ReplaceValues(item.FieldValues);
		SetValue("DAYS", item.Days.ToString());
		OnPropertyChanged("DrugName");
		OnPropertyChanged("Spec");
		OnPropertyChanged("Dose");
		OnPropertyChanged("Freq");
		OnPropertyChanged("Route");
		OnPropertyChanged("Quantity");
		OnPropertyChanged("Amount");
		OnPropertyChanged("Days");
	}
}
