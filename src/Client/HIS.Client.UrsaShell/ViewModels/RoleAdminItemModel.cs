using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class RoleAdminItemModel : ClientModelBase
{
	private string _roleName = string.Empty;

	private string _dataScope = string.Empty;

	private bool _isActive = true;

	private int _userCount;

	public string RoleId { get; set; } = string.Empty;

	public string RoleCode { get; set; } = string.Empty;

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

	public string Description { get; set; } = string.Empty;

	public int UserCount
	{
		get
		{
			return _userCount;
		}
		set
		{
			SetField(ref _userCount, value, "UserCount");
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

	public string StatusText => IsActive ? "正常启用" : "已禁用";

	public string StatusClass => IsActive ? "Success" : "Danger";

	public string ScopeDescription
	{
		get
		{
			string dataScope = DataScope;
			if (1 == 0)
			{
			}
			string result = dataScope switch
			{
				"ALL_CAMPUS" => "全院",
				"OPD_DEPT_SCOPE" => "门诊科室",
				"IPD_DEPT_AND_BORROWED" => "住院科室及借床病区",
				"NURSING_WARD_SCOPE" => "护理病区",
				"MEDTECH_EXEC_SCOPE" => "医技执行科室",
				"PHARMACY_SCOPE" => "药房与药库",
				"DEPT_ONLY" => "所属科室",
				"WARD_ONLY" => "所属病区",
				"MED_GROUP" => "所属医疗组",
				"PHARMACY_ONLY" => "药房库区",
				"FINANCE_ONLY" => "财务结算中心",
				"CAMPUS_ONLY" => "所属院区",
				_ => DataScope, 
			};
			if (1 == 0)
			{
			}
			return result;
		}
	}
}
