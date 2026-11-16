using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Config.Models;

public class CfgIcd10Diagnosis : BaseEntity
{
	public string IcdCode { get; set; } = string.Empty;

	public string DiseaseName { get; set; } = string.Empty;

	public string? PinyinCode { get; set; }

	public string CategoryName { get; set; } = "GENERAL";

	public string GenderExclusive { get; set; } = "ALL";

	public short IsChronicSpecial { get; set; } = 0;

	public short IsInfectiousReport { get; set; } = 0;

	public short IsActive { get; set; } = 1;
}
