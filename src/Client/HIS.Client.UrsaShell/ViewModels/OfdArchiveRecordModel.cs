using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class OfdArchiveRecordModel : ClientModelBase
{
	public string ArchiveNo { get; set; } = string.Empty;

	public string DocType { get; set; } = string.Empty;

	public string PatientName { get; set; } = string.Empty;

	public string DeptName { get; set; } = string.Empty;

	public string OfdStandard { get; set; } = string.Empty;

	public string ZipEntries { get; set; } = string.Empty;

	public string HashSha256 { get; set; } = string.Empty;

	public string SignatureStatus { get; set; } = string.Empty;

	public string ArchiveTime { get; set; } = string.Empty;

	public bool IsVerified { get; set; }
}
