using System;
using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Emr.Models;

public class EmrDocument : BaseEntity
{
	public string DocNo { get; set; } = string.Empty;

	public string? RequestKey { get; set; }

	public string? CampusId { get; set; }

	public string EmrScene { get; set; } = "OPD";

	public string VisitOrEncounterId { get; set; } = string.Empty;

	public string PatientId { get; set; } = string.Empty;

	public string DeptId { get; set; } = string.Empty;

	public string? WardId { get; set; }

	public string TemplateId { get; set; } = string.Empty;

	public int TemplateVersion { get; set; } = 1;

	public string DocTypeCode { get; set; } = string.Empty;

	public string DocTitle { get; set; } = string.Empty;

	public long ClinicalTimeMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

	public string? StructuredAstJson { get; set; }

	public string? PlainTextSummary { get; set; }

	public string? TrackChangesJson { get; set; }

	public int Status { get; set; } = 0;

	public string AuthorId { get; set; } = string.Empty;

	public long? ResidentSignedAtMs { get; set; }

	public string? AttendingDoctorId { get; set; }

	public long? AttendingSignedAtMs { get; set; }

	public string? ChiefDoctorId { get; set; }

	public long? ChiefSignedAtMs { get; set; }

	public string? PatientSignSvg { get; set; }

	public string? LatestOfdArchiveId { get; set; }
}
