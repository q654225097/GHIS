using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class HandoverNoteModel : ClientModelBase
{
	private bool _isConfirmed;

	public string Shift { get; set; } = string.Empty;

	public string NurseName { get; set; } = string.Empty;

	public string RecordTime { get; set; } = string.Empty;

	public string Content { get; set; } = string.Empty;

	public bool IsConfirmed
	{
		get
		{
			return _isConfirmed;
		}
		set
		{
			if (SetField(ref _isConfirmed, value, "IsConfirmed"))
			{
				OnPropertyChanged("StatusText");
				OnPropertyChanged("StatusClass");
				OnPropertyChanged("StatusBrush");
			}
		}
	}

	public string StatusText => IsConfirmed ? "步骤B: 接班护士已确认" : "步骤A: 交班已提交·待接班确认";

	public string StatusClass => IsConfirmed ? "Success" : "Warning";

	public string StatusBrush => IsConfirmed ? "#059669" : "#D97706";
}
