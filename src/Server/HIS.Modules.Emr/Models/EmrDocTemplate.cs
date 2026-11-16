using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Emr.Models;

public class EmrDocTemplate : BaseEntity
{
	public string TemplateCode { get; set; } = string.Empty;

	public int TemplateVersion { get; set; } = 1;

	public string TemplateName { get; set; } = string.Empty;

	public string EmrScene { get; set; } = "OPD";

	public string DocTypeCode { get; set; } = string.Empty;

	public string? DeptCategory { get; set; }

	public string? TargetDeptId { get; set; }

	public string? DiseaseIcdPrefix { get; set; }

	public int TimeLimitHours { get; set; } = 24;

	public int RequiredSignLevels { get; set; } = 1;

	public string? OfdLayoutId { get; set; }

	public short IsPublished { get; set; } = 1;
}
