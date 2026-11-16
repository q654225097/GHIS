using HIS.BuildingBlocks.Database;

namespace HIS.Modules.MedTech.Models;

public class SysMedtechExecRouting : BaseEntity
{
	public string CampusId { get; set; } = "CAMPUS-01";

	public string SourceScene { get; set; } = "ALL";

	public string? OrderDeptId { get; set; }

	public string ServiceItemCode { get; set; } = string.Empty;

	public string ExecMedTechDeptId { get; set; } = string.Empty;

	public string? ExecRoomCode { get; set; }

	public string TimeWindowType { get; set; } = "ALL_DAY";

	public int PriorityWeight { get; set; } = 10;

	public short IsActive { get; set; } = 1;
}
