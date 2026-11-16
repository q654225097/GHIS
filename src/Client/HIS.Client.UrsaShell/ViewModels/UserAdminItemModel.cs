using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class UserAdminItemModel : ClientModelBase
{
	private bool _isActive = true;

	private string _displayName = string.Empty;

	private string _roleName = string.Empty;

	private string _deptName = string.Empty;

	private int _prescribeLevel = 1;

	private int _surgeryLevel = 1;

	private int _emrSignLevel = 1;

	public string UserId { get; set; } = string.Empty;

	public string UserName { get; set; } = string.Empty;

	public string DisplayName
	{
		get
		{
			return _displayName;
		}
		set
		{
			SetField(ref _displayName, value, "DisplayName");
		}
	}

	public string EmployeeNo { get; set; } = string.Empty;

	public string RoleCode { get; set; } = string.Empty;

	public string DeptId { get; set; } = string.Empty;

	public string RoleName
	{
		get
		{
			return _roleName;
		}
		set
		{
			SetField(ref _roleName, value, "RoleName");
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

	public int ContextCount { get; set; } = 1;

	public string ContextSummary { get; set; } = string.Empty;

	public string DefaultContextSummary => "默认：" + RoleName + " · " + DeptName;

	public string ContextCountText => $"{ContextCount} 个身份";

	public int PrescribeLevel
	{
		get
		{
			return _prescribeLevel;
		}
		set
		{
			SetField(ref _prescribeLevel, value, "PrescribeLevel");
		}
	}

	public int SurgeryLevel
	{
		get
		{
			return _surgeryLevel;
		}
		set
		{
			SetField(ref _surgeryLevel, value, "SurgeryLevel");
		}
	}

	public int EmrSignLevel
	{
		get
		{
			return _emrSignLevel;
		}
		set
		{
			SetField(ref _emrSignLevel, value, "EmrSignLevel");
		}
	}

	public string PrescribeLevelText
	{
		get
		{
			int prescribeLevel = PrescribeLevel;
			if (1 == 0)
			{
			}
			string result = prescribeLevel switch
			{
				3 => "III级 (特殊抗生素)", 
				2 => "II级 (限制级)", 
				1 => "I级 (普通)", 
				_ => "无处方权", 
			};
			if (1 == 0)
			{
			}
			return result;
		}
	}

	public string SurgeryLevelText
	{
		get
		{
			int surgeryLevel = SurgeryLevel;
			if (1 == 0)
			{
			}
			string result = surgeryLevel switch
			{
				4 => "IV级 (重大手术)", 
				3 => "III级 (大型手术)", 
				2 => "II级 (中型手术)", 
				1 => "I级 (微创/门诊)", 
				_ => "无手术权", 
			};
			if (1 == 0)
			{
			}
			return result;
		}
	}

	public string EmrSignLevelText
	{
		get
		{
			int emrSignLevel = EmrSignLevel;
			if (1 == 0)
			{
			}
			string result = emrSignLevel switch
			{
				3 => "III级 (终审归档)", 
				2 => "II级 (主治审签)", 
				1 => "I级 (住院书写)", 
				_ => "无审签权", 
			};
			if (1 == 0)
			{
			}
			return result;
		}
	}

	public bool IsActive
	{
		get
		{
			return _isActive;
		}
		set
		{
			if (SetField(ref _isActive, value, "IsActive"))
			{
				OnPropertyChanged("StatusText");
				OnPropertyChanged("StatusClass");
			}
		}
	}

	public string StatusText => IsActive ? "正常" : "已停用";

	public string StatusClass => IsActive ? "Success" : "Danger";
}
