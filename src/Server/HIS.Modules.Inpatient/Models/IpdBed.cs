using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Inpatient.Models;

public class IpdBed : BaseEntity
{
	public string CampusId { get; set; } = "CAMPUS-01";

	public string WardId { get; set; } = string.Empty;

	public string RoomCode { get; set; } = string.Empty;

	public string BedCode { get; set; } = string.Empty;

	public string BedType { get; set; } = "STANDARD";

	public decimal DailyFee { get; set; } = 60.00m;

	public int BedStatus { get; set; } = 0;

	public string? CurrentEncounterId { get; set; }
}
