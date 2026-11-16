using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Emr.Models;

public class EmrDataElement : BaseEntity
{
	public string ElementCode { get; set; } = string.Empty;

	public string? NhcStdCode { get; set; }

	public string ElementName { get; set; } = string.Empty;

	public string DataType { get; set; } = "STRING";

	public string UrsaControlType { get; set; } = "u:TextBox";

	public string? DictCode { get; set; }

	public string? UnitText { get; set; }

	public string? MacroSource { get; set; }

	public string GenderExclusive { get; set; } = "ALL";

	public string? ValidationRuleJson { get; set; }

	public string? ControlKey { get; set; }

	public short IsIndexed { get; set; } = 1;

	public short IsActive { get; set; } = 1;
}
