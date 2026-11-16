using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class OrderFrequencyDefinition : ClientModelBase
{
	private string _code = string.Empty;

	private string _name = string.Empty;

	private int _dailyTimes = 1;

	private string _timePointsText = "08:00";

	private string _description = string.Empty;

	private bool _isLongTermApplicable = true;

	private bool _isCustom;

	public string Code
	{
		get
		{
			return _code;
		}
		set
		{
			if (SetField(ref _code, value, "Code"))
			{
				OnPropertyChanged("DisplayText");
			}
		}
	}

	public string Name
	{
		get
		{
			return _name;
		}
		set
		{
			if (SetField(ref _name, value, "Name"))
			{
				OnPropertyChanged("DisplayText");
			}
		}
	}

	public int DailyTimes
	{
		get
		{
			return _dailyTimes;
		}
		set
		{
			SetField(ref _dailyTimes, value, "DailyTimes");
		}
	}

	public string TimePointsText
	{
		get
		{
			return _timePointsText;
		}
		set
		{
			if (SetField(ref _timePointsText, value, "TimePointsText"))
			{
				OnPropertyChanged("ParsedTimePoints");
				OnPropertyChanged("DisplayText");
				OnPropertyChanged("TimePointsBadge");
				OnPropertyChanged("TimePointsList");
				int count = ParsedTimePoints.Count;
				if (count > 0 && count != DailyTimes)
				{
					DailyTimes = count;
				}
			}
		}
	}

	public string Description
	{
		get
		{
			return _description;
		}
		set
		{
			SetField(ref _description, value, "Description");
		}
	}

	public bool IsLongTermApplicable
	{
		get
		{
			return _isLongTermApplicable;
		}
		set
		{
			if (SetField(ref _isLongTermApplicable, value, "IsLongTermApplicable"))
			{
				OnPropertyChanged("ApplicableScopeBadge");
				OnPropertyChanged("ApplicableScopeBrush");
			}
		}
	}

	public bool IsCustom
	{
		get
		{
			return _isCustom;
		}
		set
		{
			SetField(ref _isCustom, value, "IsCustom");
		}
	}

	public string ApplicableScopeBadge => IsLongTermApplicable ? "长期医嘱" : "临时医嘱";

	public string ApplicableScopeBrush => IsLongTermApplicable ? "#2563EB" : "#D97706";

	public string DisplayText => $"{Code} ({Name} · {TimePointsBadge})";

	public string TimePointsBadge => string.IsNullOrWhiteSpace(TimePointsText) ? "--" : TimePointsText;

	public IReadOnlyList<string> ParsedTimePoints
	{
		get
		{
			if (string.IsNullOrWhiteSpace(TimePointsText))
			{
				return Array.Empty<string>();
			}
			return (from x in (from x in TimePointsText.Split(new char[5] { ',', '，', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(FormatTimeSegment)
					where !string.IsNullOrEmpty(x)
					select x).Distinct()
				orderby x
				select x).ToList();
		}
	}

	public ObservableCollection<string> TimePointsList => new ObservableCollection<string>(ParsedTimePoints);

	private static string FormatTimeSegment(string raw)
	{
		string text = raw.Trim();
		if (TimeOnly.TryParse(text, out var result))
		{
			return result.ToString("HH:mm");
		}
		if (int.TryParse(text, out var result2) && result2 >= 0 && result2 <= 23)
		{
			return $"{result2:D2}:00";
		}
		return text;
	}
}
