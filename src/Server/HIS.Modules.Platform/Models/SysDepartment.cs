using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Platform.Models;

public class SysDepartment : BaseEntity
{
	public string CampusId { get; set; } = "CAMPUS-01";

	public string DeptCode { get; set; } = string.Empty;

	public string DeptName { get; set; } = string.Empty;

	public string? PinyinCode { get; set; }

	public string DeptCategory { get; set; } = "OPD";

	public string? SubSpecialtyType { get; set; }

	public string? ParentAdminId { get; set; }

	public string? GenderLimit { get; set; }

	public int? MinAgeDays { get; set; }

	public int? MaxAgeYears { get; set; }

	public string? DefaultPharmacyId { get; set; }

	public int RatedBedCount { get; set; }

	public string? LocationAddress { get; set; }

	public short IsAllowAppointment { get; set; } = 1;

	public short IsActive { get; set; } = 1;
}
