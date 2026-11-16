using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class VitalSignModel : ClientModelBase
{
	public string RecordedAt { get; set; } = string.Empty;

	public string Temperature { get; set; } = string.Empty;

	public string Pulse { get; set; } = string.Empty;

	public string Respiration { get; set; } = string.Empty;

	public string BloodPressure { get; set; } = string.Empty;

	public string Spo2 { get; set; } = string.Empty;

	public string NurseName { get; set; } = string.Empty;

	public bool IsAbnormal { get; set; }

	public string StatusText => IsAbnormal ? "异常" : "正常";

	public string StatusClass => IsAbnormal ? "Danger" : "Success";

	public string StatusBrush => IsAbnormal ? "#DC2626" : "#059669";

	public string Summary => $"{Temperature}℃ · P {Pulse} · R {Respiration} · BP {BloodPressure} · SpO₂ {Spo2}%";
}
