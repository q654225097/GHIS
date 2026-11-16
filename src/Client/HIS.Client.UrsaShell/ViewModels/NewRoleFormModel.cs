using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class NewRoleFormModel : ClientModelBase
{
	private string _roleCode = "";

	private string _roleName = "";

	private string _dataScope = "ALL_CAMPUS";

	public string RoleCode
	{
		get
		{
			return _roleCode;
		}
		set
		{
			SetField(ref _roleCode, value, "RoleCode");
		}
	}

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

	public string DataScope
	{
		get
		{
			return _dataScope;
		}
		set
		{
			SetField(ref _dataScope, value, "DataScope");
		}
	}

	public void Reset()
	{
		RoleCode = "";
		RoleName = "";
		DataScope = "ALL_CAMPUS";
	}
}
