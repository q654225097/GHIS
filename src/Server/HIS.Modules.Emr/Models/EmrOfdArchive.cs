using System;
using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Emr.Models;

public class EmrOfdArchive : BaseEntity
{
	public string OfdNo { get; set; } = string.Empty;

	public string ArchiveScope { get; set; } = "SINGLE_DOC";

	public string VisitOrEncounterId { get; set; } = string.Empty;

	public string? DocumentId { get; set; }

	public string PatientId { get; set; } = string.Empty;

	public string OfdVersionStandard { get; set; } = "GB/T 33190-2016";

	public int PageCount { get; set; } = 1;

	public string FileStorageKey { get; set; } = string.Empty;

	public long FileSizeBytes { get; set; }

	public string ContentSha256 { get; set; } = string.Empty;

	public string? Sm3Digest { get; set; }

	public string? CaSignSerialNo { get; set; }

	public string? TsaTimestampToken { get; set; }

	public short HasEmbeddedSemanticXml { get; set; } = 1;

	public int ArchiveStatus { get; set; } = 0;

	public long GeneratedAtMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
