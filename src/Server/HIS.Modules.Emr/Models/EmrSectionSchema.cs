using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Emr.Models;

public class EmrSectionSchema : BaseEntity
{
	public string TemplateId { get; set; } = string.Empty;

	public string SectionCode { get; set; } = string.Empty;

	public string SectionTitle { get; set; } = string.Empty;

	public string RenderMode { get; set; } = "GRID_FORM";

	public string? ContentTemplateJson { get; set; }

	public string? SectionControlKey { get; set; }

	public short IsRequired { get; set; } = 1;

	public string? VisibilityExpr { get; set; }

	public int SortOrder { get; set; } = 0;
}
