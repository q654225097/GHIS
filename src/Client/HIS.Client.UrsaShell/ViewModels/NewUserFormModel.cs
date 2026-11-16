using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class NewUserFormModel : ClientModelBase
{
	private string _userName = "";

	private string _displayName = "";

	private string _employeeNo = "";

	private string _password = "";

	private string _selectedRole = "ROLE-OPD-DOC";

	private string _selectedDept = "心血管内科门诊";

	private string _selectedDeptId = "DEPT-OPD-01";

	private int _prescribeLevel = 2;

	private int _surgeryLevel = 1;

	private int _emrSignLevel = 2;

	public string UserName
	{
		get
		{
			return _userName;
		}
		set
		{
			SetField(ref _userName, value, "UserName");
		}
	}

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

	public string EmployeeNo
	{
		get
		{
			return _employeeNo;
		}
		set
		{
			SetField(ref _employeeNo, value, "EmployeeNo");
		}
	}

	public string Password
	{
		get
		{
			return _password;
		}
		set
		{
			SetField(ref _password, value, "Password");
		}
	}

	public string SelectedRole
	{
		get
		{
			return _selectedRole;
		}
		set
		{
			SetField(ref _selectedRole, value, "SelectedRole");
		}
	}

	public string SelectedDept
	{
		get
		{
			return _selectedDept;
		}
		set
		{
			SetField(ref _selectedDept, value, "SelectedDept");
		}
	}

	public string SelectedDeptId
	{
		get
		{
			return _selectedDeptId;
		}
		set
		{
			SetField(ref _selectedDeptId, value, "SelectedDeptId");
		}
	}

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

	public void Reset()
	{
		UserName = "";
		DisplayName = "";
		EmployeeNo = "";
		Password = "";
		SelectedRole = "ROLE-OPD-DOC";
		SelectedDept = "心血管内科门诊";
		SelectedDeptId = "DEPT-OPD-01";
		PrescribeLevel = 2;
		SurgeryLevel = 1;
		EmrSignLevel = 2;
	}
}
