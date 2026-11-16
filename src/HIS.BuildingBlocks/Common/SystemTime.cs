using System;

namespace HIS.BuildingBlocks.Common;

public static class SystemTime
{
	public static readonly TimeSpan ShanghaiOffset = TimeSpan.FromHours(8);

	public static long UtcNowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

	public static DateTimeOffset ShanghaiNow =>
		DateTimeOffset.UtcNow.ToOffset(ShanghaiOffset);

	public static int TodayInt =>
		ShanghaiNow.Year * 10000 + ShanghaiNow.Month * 100 + ShanghaiNow.Day;

	public static long TodayStartUtcMs =>
		new DateTimeOffset(ShanghaiNow.Date, ShanghaiOffset).ToUnixTimeMilliseconds();

	public static long TomorrowStartUtcMs =>
		new DateTimeOffset(ShanghaiNow.Date.AddDays(1), ShanghaiOffset).ToUnixTimeMilliseconds();
}
