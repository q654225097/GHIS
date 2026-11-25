using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class BedCardModel : ClientModelBase
{
	private bool _isSelected;

	public string BedCode { get; set; } = string.Empty;

	public string BedType { get; set; } = string.Empty;

	public string PatientName { get; set; } = string.Empty;

	public string Gender { get; set; } = string.Empty;

	public int Age { get; set; }

	public string AdmissionNo { get; set; } = string.Empty;

	public string AdmittingDeptName { get; set; } = string.Empty;

	public string NursingWardName { get; set; } = string.Empty;

	public string NursingLevel { get; set; } = string.Empty;

	public string Diagnosis { get; set; } = string.Empty;

	public string DailyFee { get; set; } = string.Empty;

	public bool IsBorrowed { get; set; }

	public bool IsOccupied { get; set; }

	public string CardTheme { get; set; } = "Standard";

	public bool IsSelected
	{
		get
		{
			return _isSelected;
		}
		set
		{
			if (SetField(ref _isSelected, value, "IsSelected"))
			{
				OnPropertyChanged("CardBorderBrush");
				OnPropertyChanged("CardBackground");
			}
		}
	}

	public global::Avalonia.Media.IBrush CardBorderBrush => IsSelected
		? global::HIS.Client.UrsaShell.Shell.ThemeBrushes.Primary
		: global::HIS.Client.UrsaShell.Shell.ThemeBrushes.Border;

	public global::Avalonia.Media.IBrush CardBackground => IsSelected
		? global::HIS.Client.UrsaShell.Shell.ThemeBrushes.PrimaryLight
		: global::HIS.Client.UrsaShell.Shell.ThemeBrushes.Surface;

	public void NotifyUpdated()
	{
		OnPropertyChanged("BedCode");
		OnPropertyChanged("BedType");
		OnPropertyChanged("PatientName");
		OnPropertyChanged("Gender");
		OnPropertyChanged("Age");
		OnPropertyChanged("AdmissionNo");
		OnPropertyChanged("AdmittingDeptName");
		OnPropertyChanged("NursingWardName");
		OnPropertyChanged("NursingLevel");
		OnPropertyChanged("Diagnosis");
		OnPropertyChanged("DailyFee");
		OnPropertyChanged("IsBorrowed");
		OnPropertyChanged("IsOccupied");
		OnPropertyChanged("CardTheme");
	}
}
