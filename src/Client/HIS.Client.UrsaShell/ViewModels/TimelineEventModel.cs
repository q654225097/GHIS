using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class TimelineEventModel : ClientModelBase
{
	public string EventTime { get; set; } = string.Empty;

	public string EventType { get; set; } = string.Empty;

	public string EventTitle { get; set; } = string.Empty;

	public string Department { get; set; } = string.Empty;

	public string DoctorOrOperator { get; set; } = string.Empty;

	public string Summary { get; set; } = string.Empty;

	public string BadgeClass { get; set; } = "Primary";

	public string ResultIcon { get; set; } = "\ud83d\udcc4";
}
