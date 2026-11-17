using System;
using System.Globalization;

namespace HIS.BuildingBlocks.Common;

public static class PatientAgeHelper
{
	/// <summary>
	/// 卫健委标准年龄格式化：
	/// 满 1 周岁显示 X岁；
	/// 1 个月~1 周岁显示 X月X天；
	/// 出生 28 天内新生儿显示 X天。
	/// </summary>
	public static string FormatAge(int birthDateInt, DateTime? asOf = null)
	{
		if (birthDateInt <= 0) return "--";
		string s = birthDateInt.ToString();
		if (s.Length != 8 || !DateTime.TryParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime birthDate))
		{
			return "--";
		}

		DateTime now = (asOf ?? DateTime.Today).Date;
		if (birthDate > now) return "0天";

		int years = now.Year - birthDate.Year;
		if (now < birthDate.AddYears(years))
		{
			years--;
		}

		if (years >= 1)
		{
			return $"{years}岁";
		}

		DateTime pastYearDate = birthDate;
		int months = 0;
		while (pastYearDate.AddMonths(1) <= now)
		{
			months++;
			pastYearDate = pastYearDate.AddMonths(1);
		}
		int days = (now - pastYearDate).Days;

		if (months >= 1)
		{
			return days > 0 ? $"{months}月{days}天" : $"{months}月";
		}

		int totalDays = (now - birthDate).Days;
		return totalDays <= 0 ? "1天" : $"{totalDays}天";
	}
}
