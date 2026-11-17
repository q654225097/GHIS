using System;
using System.Globalization;

namespace HIS.BuildingBlocks.Common;

public static class IdCardValidator
{
	private static readonly int[] Weights = { 7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2 };
	private static readonly char[] CheckCodes = { '1', '0', 'X', '9', '8', '7', '6', '5', '4', '3', '2' };

	/// <summary>
	/// 校验 GB 11643-1999 (ISO 7064 MOD 11-2) 18 位居民身份证校验码，并提取出生日期与性别代码
	/// </summary>
	public static bool IsValid(string? idCard) => Validate(idCard).IsValid;

	public static (bool IsValid, string? ErrorMessage, int? BirthDateInt, string? GenderCode) Validate(string? idCard)
	{
		if (string.IsNullOrWhiteSpace(idCard))
		{
			return (false, "身份证号不能为空", null, null);
		}

		idCard = idCard.Trim().ToUpperInvariant();
		if (idCard.Length != 18)
		{
			return (false, "居民身份证必须为18位", null, null);
		}

		for (int i = 0; i < 17; i++)
		{
			if (idCard[i] < '0' || idCard[i] > '9')
			{
				return (false, "居民身份证前17位必须为纯数字", null, null);
			}
		}

		string birthStr = idCard.Substring(6, 8);
		if (!DateTime.TryParseExact(birthStr, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime birthDate))
		{
			return (false, "居民身份证包含无效出生日期", null, null);
		}

		if (birthDate < new DateTime(1890, 1, 1) || birthDate > DateTime.Today)
		{
			return (false, "出生日期超出合理范围", null, null);
		}

		int sum = 0;
		for (int i = 0; i < 17; i++)
		{
			sum += (idCard[i] - '0') * Weights[i];
		}

		char expectedCheckCode = CheckCodes[sum % 11];
		if (idCard[17] != expectedCheckCode)
		{
			return (false, $"居民身份证校验码错误，第18位应为'{expectedCheckCode}'", null, null);
		}

		int birthDateInt = int.Parse(birthStr);
		int seqDigit = idCard[16] - '0';
		string genderCode = (seqDigit % 2 == 1) ? "1" : "2"; // 1=男, 2=女 (GB/T 2261.1)

		return (true, null, birthDateInt, genderCode);
	}
}
