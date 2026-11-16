using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Config.Models;

public class CfgClinicalDictionary : BaseEntity
{
	public string DictType { get; set; } = string.Empty;

	public string ItemCode { get; set; } = string.Empty;

	public string ItemName { get; set; } = string.Empty;

	public string? PinyinCode { get; set; }

	public string? ParentCode { get; set; }

	public int SortOrder { get; set; } = 0;

	public short IsDefault { get; set; } = 0;

	public short IsActive { get; set; } = 1;
}
