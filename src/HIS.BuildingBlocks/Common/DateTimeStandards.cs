using System;

namespace HIS.BuildingBlocks.Common;

/// <summary>
/// 医院数字化系统（GHIS）统一时间与日期格式化规范
/// 规范约束：
/// 1. 所有纯日期统一使用 YYYY-MM-DD (yyyy-MM-dd)
/// 2. 所有日期时间统一使用 YYYY-MM-DD HH24:MI:SS (yyyy-MM-dd HH:mm:ss, 24小时制)
/// 3. 所有需要毫秒精度的场景统一使用时间戳 (Unix Epoch in milliseconds, long)
/// </summary>
public static class DateTimeStandards
{
	/// <summary>
	/// 标准日期格式：yyyy-MM-dd
	/// </summary>
	public const string DateFormat = "yyyy-MM-dd";

	/// <summary>
	/// 标准日期时间格式：yyyy-MM-dd HH:mm:ss (24小时制)
	/// </summary>
	public const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";

	/// <summary>
	/// 当前毫秒级时间戳 (Unix Epoch in milliseconds)
	/// </summary>
	public static long NowTimestampMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

	/// <summary>
	/// 格式化为标准日期 YYYY-MM-DD
	/// </summary>
	public static string ToStandardDate(this DateTime dt) => dt.ToString(DateFormat);

	/// <summary>
	/// 格式化为标准日期 YYYY-MM-DD
	/// </summary>
	public static string ToStandardDate(this DateTimeOffset dto) => dto.ToLocalTime().ToString(DateFormat);

	/// <summary>
	/// 格式化为标准日期时间 YYYY-MM-DD HH24:MI:SS
	/// </summary>
	public static string ToStandardDateTime(this DateTime dt) => dt.ToString(DateTimeFormat);

	/// <summary>
	/// 格式化为标准日期时间 YYYY-MM-DD HH24:MI:SS
	/// </summary>
	public static string ToStandardDateTime(this DateTimeOffset dto) => dto.ToLocalTime().ToString(DateTimeFormat);

	/// <summary>
	/// 将毫秒时间戳转换为标准日期时间字符串 YYYY-MM-DD HH24:MI:SS (若 <= 0 则返回默认占位符)
	/// </summary>
	public static string FormatEpochMsToDateTime(long epochMs, string fallback = "-") =>
		epochMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(epochMs).ToLocalTime().ToString(DateTimeFormat) : fallback;

	/// <summary>
	/// 将毫秒时间戳转换为标准日期时间字符串 YYYY-MM-DD HH24:MI:SS (可空类型重载)
	/// </summary>
	public static string FormatEpochMsToDateTime(long? epochMs, string fallback = "-") =>
		epochMs.HasValue && epochMs.Value > 0
			? DateTimeOffset.FromUnixTimeMilliseconds(epochMs.Value).ToLocalTime().ToString(DateTimeFormat)
			: fallback;

	/// <summary>
	/// 将毫秒时间戳转换为标准日期字符串 YYYY-MM-DD (若 <= 0 则返回默认占位符)
	/// </summary>
	public static string FormatEpochMsToDate(long epochMs, string fallback = "-") =>
		epochMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(epochMs).ToLocalTime().ToString(DateFormat) : fallback;

	/// <summary>
	/// 将毫秒时间戳转换为标准日期字符串 YYYY-MM-DD (可空类型重载)
	/// </summary>
	public static string FormatEpochMsToDate(long? epochMs, string fallback = "-") =>
		epochMs.HasValue && epochMs.Value > 0
			? DateTimeOffset.FromUnixTimeMilliseconds(epochMs.Value).ToLocalTime().ToString(DateFormat)
			: fallback;
}
