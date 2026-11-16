using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Config.Models;

public class CfgPriceItem : BaseEntity
{
	public string ItemCode { get; set; } = string.Empty;

	public string? NationalInsCode { get; set; }

	public string ItemName { get; set; } = string.Empty;

	public string? PinyinCode { get; set; }

	public string FeeCategory { get; set; } = "EXAM";

	public string? Spec { get; set; }

	public string UnitName { get; set; } = "次";

	public decimal UnitPrice { get; set; }

	public string ReimburseType { get; set; } = "CLASS_A";

	public string? DefaultExecDeptId { get; set; }

	public short IsActive { get; set; } = 1;
}
