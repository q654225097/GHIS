using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Emr.Models;

public class EmrDocumentElement : BaseEntity
{
	public string DocumentId { get; set; } = string.Empty;

	public string PatientId { get; set; } = string.Empty;

	public string EmrScene { get; set; } = "OPD";

	public string DocTypeCode { get; set; } = string.Empty;

	public string SectionCode { get; set; } = string.Empty;

	public string ElementCode { get; set; } = string.Empty;

	public string? NhcStdCode { get; set; }

	public string? ValString { get; set; }

	public long? ValInt { get; set; }

	public decimal? ValDecimal { get; set; }

	public long? ValTimeMs { get; set; }
}
