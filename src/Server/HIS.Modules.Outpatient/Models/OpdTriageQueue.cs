using System;
using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Outpatient.Models;

public class OpdTriageQueue : BaseEntity
{
	public string VisitId { get; set; } = string.Empty;

	public string OpdDeptId { get; set; } = string.Empty;

	public string? ClinicRoomId { get; set; }

	public int PriorityScore { get; set; } = 100;

	public int PassCount { get; set; } = 0;

	public int QueueStatus { get; set; } = 1;

	public long CheckinAtMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

	public long? CalledAtMs { get; set; }
}
