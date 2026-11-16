using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Outpatient.Models;

public class OpdScheduleSlot : BaseEntity
{
	public string CampusId { get; set; } = "CAMPUS-01";

	public string OpdDeptId { get; set; } = string.Empty;

	public string? ClinicRoomId { get; set; }

	public string DoctorId { get; set; } = string.Empty;

	public string? SubstituteDoctorId { get; set; }

	public int ScheduleDateInt { get; set; }

	public string SlotPeriodCode { get; set; } = "MORNING";

	public long StartsAtMs { get; set; }

	public long EndsAtMs { get; set; }

	public string RegLevelCode { get; set; } = "NORMAL";

	public decimal RegistrationFee { get; set; } = 15.00m;

	public int TotalCapacity { get; set; } = 30;

	public int ReservedCount { get; set; } = 0;

	public int SlotStatus { get; set; } = 1;
}
