using System.Collections.ObjectModel;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class EmrVisualTemplateModel : ClientModelBase
{
	private string _templateId = string.Empty;

	private string _templateCode = string.Empty;

	private string _templateName = string.Empty;

	private string _emrScene = "OPD";

	private string _docTypeCode = string.Empty;

	private string _deptName = "心血管内科";

	private int _timeLimitHours = 24;

	private int _requiredSignLevels = 1;

	private string _boundLayoutCode = "OPD_STD_B5";

	private int _version = 1;

	private bool _isPublished = true;

	public string TemplateId
	{
		get
		{
			return _templateId;
		}
		set
		{
			SetField(ref _templateId, value, "TemplateId");
		}
	}

	public string TemplateCode
	{
		get
		{
			return _templateCode;
		}
		set
		{
			SetField(ref _templateCode, value, "TemplateCode");
		}
	}

	public string TemplateName
	{
		get
		{
			return _templateName;
		}
		set
		{
			SetField(ref _templateName, value, "TemplateName");
		}
	}

	public string EmrScene
	{
		get
		{
			return _emrScene;
		}
		set
		{
			if (SetField(ref _emrScene, value, "EmrScene"))
			{
				OnPropertyChanged("SceneBadgeText");
				OnPropertyChanged("SceneBrush");
			}
		}
	}

	public string DocTypeCode
	{
		get
		{
			return _docTypeCode;
		}
		set
		{
			SetField(ref _docTypeCode, value, "DocTypeCode");
		}
	}

	public string DeptName
	{
		get
		{
			return _deptName;
		}
		set
		{
			SetField(ref _deptName, value, "DeptName");
		}
	}

	public int TimeLimitHours
	{
		get
		{
			return _timeLimitHours;
		}
		set
		{
			SetField(ref _timeLimitHours, value, "TimeLimitHours");
		}
	}

	public int RequiredSignLevels
	{
		get
		{
			return _requiredSignLevels;
		}
		set
		{
			if (SetField(ref _requiredSignLevels, value, "RequiredSignLevels"))
			{
				OnPropertyChanged("SignLevelRequirementText");
			}
		}
	}

	public string BoundLayoutCode
	{
		get
		{
			return _boundLayoutCode;
		}
		set
		{
			SetField(ref _boundLayoutCode, value, "BoundLayoutCode");
		}
	}

	public int Version
	{
		get
		{
			return _version;
		}
		set
		{
			if (SetField(ref _version, value, "Version"))
			{
				OnPropertyChanged("VersionBadge");
			}
		}
	}

	public bool IsPublished
	{
		get
		{
			return _isPublished;
		}
		set
		{
			SetField(ref _isPublished, value, "IsPublished");
		}
	}

	public ObservableCollection<EmrVisualSectionModel> Sections { get; } = new ObservableCollection<EmrVisualSectionModel>();

	public string VersionBadge => $"v{Version}.0";

	public string SceneBadgeText
	{
		get
		{
			string emrScene = EmrScene;
			if (1 == 0)
			{
			}
			string result = emrScene switch
			{
				"OPD" => "门诊病历 (OPD)", 
				"IPD" => "住院病历 (IPD)", 
				"WARD" => "护理病历 (WARD)", 
				_ => EmrScene, 
			};
			if (1 == 0)
			{
			}
			return result;
		}
	}

	public string SceneBrush
	{
		get
		{
			string emrScene = EmrScene;
			if (1 == 0)
			{
			}
			string result = emrScene switch
			{
				"OPD" => "#B71C1C", 
				"IPD" => "#1565C0", 
				"WARD" => "#059669", 
				_ => "#2563EB", 
			};
			if (1 == 0)
			{
			}
			return result;
		}
	}

	public string SignLevelRequirementText
	{
		get
		{
			int requiredSignLevels = RequiredSignLevels;
			if (1 == 0)
			{
			}
			string result = requiredSignLevels switch
			{
				3 => "三级审签 (住院医师→主治→主任终审)", 
				2 => (EmrScene == "WARD") ? "双级审签 (责任护士→病区护士长质控)" : "二级审签 (住院医师→主治医师审签)", 
				_ => "一级审签 (接诊医师/当班护师独立审签)", 
			};
			if (1 == 0)
			{
			}
			return result;
		}
	}

	public string TimeLimitText => $"书写时限: ≤{TimeLimitHours}小时";
}
