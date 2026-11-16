using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class ProgressNoteModel : ClientModelBase
{
	private bool _isSigned;

	public string RecordTime { get; set; } = string.Empty;

	public string Author { get; set; } = string.Empty;

	public string RecordType { get; set; } = "日常病程记录";

	public string Content { get; set; } = string.Empty;

	public bool IsSigned
	{
		get
		{
			return _isSigned;
		}
		set
		{
			if (SetField(ref _isSigned, value, "IsSigned"))
			{
				OnPropertyChanged("Status");
				OnPropertyChanged("StatusClass");
				OnPropertyChanged("StatusBrush");
			}
		}
	}

	public string Status => IsSigned ? "步骤B: 主治已审签" : "步骤A: 待主治审签";

	public string StatusClass => IsSigned ? "Success" : "Warning";

	public string StatusBrush => IsSigned ? "#059669" : "#D97706";
}
