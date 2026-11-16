using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Emr.Models;

public class EmrOfdLayoutSchema : BaseEntity
{
	public string LayoutCode { get; set; } = string.Empty;

	public string LayoutName { get; set; } = string.Empty;

	public string PageSizeCode { get; set; } = "A4";

	public decimal PageWidthMm { get; set; } = 210.0m;

	public decimal PageHeightMm { get; set; } = 297.0m;

	public decimal MarginTopMm { get; set; } = 20.0m;

	public decimal MarginBottomMm { get; set; } = 20.0m;

	public decimal MarginLeftMm { get; set; } = 20.0m;

	public decimal MarginRightMm { get; set; } = 20.0m;

	public short IsContinuousFlow { get; set; } = 0;

	public string? HeaderLayoutJson { get; set; }

	public string? FooterLayoutJson { get; set; }

	public string? SignSlotsJson { get; set; }

	public string? WatermarkPolicy { get; set; }

	public short IsActive { get; set; } = 1;
}
