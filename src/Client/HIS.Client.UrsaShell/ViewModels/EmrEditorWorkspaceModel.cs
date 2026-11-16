using System.Collections.ObjectModel;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Stations.OpdDoctor;

namespace HIS.Client.UrsaShell.ViewModels;

public class EmrEditorWorkspaceModel : ClientModelBase
{
	// Kept at workspace level so replacing a template cannot leave old field handlers
	// holding the shared preview debounce state.
	internal System.ComponentModel.PropertyChangedEventHandler? DynamicFieldChanged { get; set; }

	private string _docNo = string.Empty;

	private string _docTitle = "门诊病历";

	private string _patientName = string.Empty;

	private string _gender = string.Empty;

	private int _age;

	private string _chiefComplaint = string.Empty;

	private string _presentIllness = string.Empty;

	private string _diagnosis = string.Empty;

	private string _prostateExam = string.Empty;

	private int _pregnancyCount = 0;

	private string _qcStatus = "未质控";

	private string _ofdStandard = "GB/T 33190-2016";

	private string _ofdStatus = "尚未审签";

	private string? _ofdNo;

	private bool _isBusy;

	private string? _selectedTemplateId;

	private string _templateName = "请选择病历模板";

	private int _requiredSignLevels = 1;

	private int _completedSignLevels;

	private string _templateHint = string.Empty;

	private string _signatureProgressText = "尚未审签";

	public string DocNo
	{
		get
		{
			return _docNo;
		}
		set
		{
			SetField(ref _docNo, value, "DocNo");
		}
	}

	public string DocTitle
	{
		get
		{
			return _docTitle;
		}
		set
		{
			SetField(ref _docTitle, value, "DocTitle");
		}
	}

	public string PatientName
	{
		get
		{
			return _patientName;
		}
		set
		{
			SetField(ref _patientName, value, "PatientName");
		}
	}

	public string Gender
	{
		get
		{
			return _gender;
		}
		set
		{
			SetField(ref _gender, value, "Gender");
		}
	}

	public int Age
	{
		get
		{
			return _age;
		}
		set
		{
			SetField(ref _age, value, "Age");
		}
	}

	public string ChiefComplaint
	{
		get
		{
			return _chiefComplaint;
		}
		set
		{
			SetField(ref _chiefComplaint, value, "ChiefComplaint");
		}
	}

	public string PresentIllness
	{
		get
		{
			return _presentIllness;
		}
		set
		{
			SetField(ref _presentIllness, value, "PresentIllness");
		}
	}

	public string Diagnosis
	{
		get
		{
			return _diagnosis;
		}
		set
		{
			SetField(ref _diagnosis, value, "Diagnosis");
		}
	}

	public string ProstateExam
	{
		get
		{
			return _prostateExam;
		}
		set
		{
			SetField(ref _prostateExam, value, "ProstateExam");
		}
	}

	public int PregnancyCount
	{
		get
		{
			return _pregnancyCount;
		}
		set
		{
			SetField(ref _pregnancyCount, value, "PregnancyCount");
		}
	}

	public string QcStatus
	{
		get
		{
			return _qcStatus;
		}
		set
		{
			SetField(ref _qcStatus, value, "QcStatus");
		}
	}

	public string OfdStandard
	{
		get
		{
			return _ofdStandard;
		}
		set
		{
			SetField(ref _ofdStandard, value, "OfdStandard");
		}
	}

	public string OfdStatus
	{
		get
		{
			return _ofdStatus;
		}
		set
		{
			SetField(ref _ofdStatus, value, "OfdStatus");
		}
	}

	public string? OfdNo
	{
		get
		{
			return _ofdNo;
		}
		set
		{
			SetField(ref _ofdNo, value, "OfdNo");
		}
	}

	public bool IsBusy
	{
		get
		{
			return _isBusy;
		}
		set
		{
			SetField(ref _isBusy, value, "IsBusy");
		}
	}

	public ObservableCollection<OpdEmrFieldModel> Fields { get; } = new();

	public ObservableCollection<OpdEmrSectionModel> Sections { get; } = new();

	public string? SelectedTemplateId
	{
		get => _selectedTemplateId;
		set => SetField(ref _selectedTemplateId, value, "SelectedTemplateId");
	}

	public string TemplateName
	{
		get => _templateName;
		set => SetField(ref _templateName, value, "TemplateName");
	}

	public int RequiredSignLevels
	{
		get => _requiredSignLevels;
		set
		{
			int normalized = Math.Clamp(value, 1, 3);
			if (SetField(ref _requiredSignLevels, normalized, "RequiredSignLevels"))
			{
				OnPropertyChanged("RequiredSignLevelsText");
			}
		}
	}

	public string RequiredSignLevelsText => $"需 {RequiredSignLevels} 级审签";

	public int CompletedSignLevels
	{
		get => _completedSignLevels;
		set
		{
			int normalized = Math.Clamp(value, 0, 3);
			if (SetField(ref _completedSignLevels, normalized, "CompletedSignLevels"))
			{
				OnPropertyChanged("SignProgressText");
				OnPropertyChanged("CanSaveDraft");
				OnPropertyChanged("NeedsRegistration");
			}
		}
	}

	public string SignProgressText
	{
		get => _signatureProgressText;
		set => SetField(ref _signatureProgressText, value, "SignProgressText");
	}

	public string TemplateHint
	{
		get => _templateHint;
		set => SetField(ref _templateHint, value, "TemplateHint");
	}

	public bool CanSaveDraft => CompletedSignLevels < RequiredSignLevels;

	public bool NeedsRegistration => CompletedSignLevels < RequiredSignLevels;

	public void ResetFields()
	{
		foreach (OpdEmrFieldModel field in Fields)
		{
			if (DynamicFieldChanged != null)
			{
				field.PropertyChanged -= DynamicFieldChanged;
			}
		}
		Fields.Clear();
		Sections.Clear();
	}
}
