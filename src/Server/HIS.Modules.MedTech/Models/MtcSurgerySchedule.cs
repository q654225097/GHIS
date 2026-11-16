using HIS.BuildingBlocks.Database;

namespace HIS.Modules.MedTech.Models;

public class MtcSurgerySchedule : BaseEntity
{
	public string EncounterId { get; set; } = string.Empty;

	public string OrDeptId { get; set; } = string.Empty;

	public string OrRoomCode { get; set; } = string.Empty;

	public int TableSeq { get; set; } = 1;

	public string SurgeryCode { get; set; } = string.Empty;

	public string SurgeryName { get; set; } = string.Empty;

	public int SurgeryLevel { get; set; } = 1;

	public string MainSurgeonId { get; set; } = string.Empty;

	public string AnesthesiologistId { get; set; } = string.Empty;

	public string CirculatingNurseId { get; set; } = string.Empty;

	public short SignInOk { get; set; } = 0;

	public short TimeOutOk { get; set; } = 0;

	public short SignOutOk { get; set; } = 0;

	public short InstrumentCountBalanced { get; set; } = 0;

	public int Status { get; set; } = 0;
}
