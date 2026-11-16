using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Config.Models;

public class CfgOrderFrequency : BaseEntity
{
	public string FreqCode { get; set; } = string.Empty;

	public string FreqName { get; set; } = string.Empty;

	public int DailyTimes { get; set; } = 1;

	public string TimePointsText { get; set; } = "08:00";

	public string ApplicableTerm { get; set; } = "STANDING";

	public string? Description { get; set; }

	public int SortOrder { get; set; } = 0;

	public short IsActive { get; set; } = 1;
}
