using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Platform.Models;

public class SysUser : BaseEntity
{
	public string UserName { get; set; } = string.Empty;

	public string DisplayName { get; set; } = string.Empty;

	public string EmployeeNo { get; set; } = string.Empty;

	public string PasswordHash { get; set; } = string.Empty;

	public string? BoundPatientId { get; set; }

	public int PrescribeLevel { get; set; } = 1;

	public int SurgeryLevel { get; set; } = 1;

	public int EmrSignLevel { get; set; } = 1;

	public string? CaCertId { get; set; }

	public int SessionVersion { get; set; } = 1;

	public int FailedLoginCount { get; set; } = 0;

	public long? LockoutUntilMs { get; set; }

	public long? PasswordChangedAtMs { get; set; }

	public short MustChangePassword { get; set; } = 0;

	public short IsActive { get; set; } = 1;
}
