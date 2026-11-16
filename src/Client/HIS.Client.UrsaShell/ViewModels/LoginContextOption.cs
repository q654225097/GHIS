using Avalonia.Media;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public sealed class LoginContextOption : ClientModelBase
{
	private bool _isSelected;

	public string PostId { get; init; } = string.Empty;

	public string UserId { get; init; } = string.Empty;

	public string UserName { get; init; } = string.Empty;

	public string DisplayName { get; init; } = string.Empty;

	public string EmployeeNo { get; init; } = string.Empty;

	public int PrescribeLevel { get; init; }

	public int SurgeryLevel { get; init; }

	public int EmrSignLevel { get; init; }

	public string RoleId { get; init; } = string.Empty;

	public string RoleCode { get; init; } = string.Empty;

	public string RoleName { get; init; } = string.Empty;

	public string DeptId { get; init; } = string.Empty;

	public string DeptName { get; init; } = string.Empty;

	public string DeptCategory { get; init; } = string.Empty;

	public string? WardId { get; init; }

	public string DataScope { get; init; } = string.Empty;

	public bool IsDefault { get; init; }

	public LoginContextSnapshot ToSnapshot(bool isDefault = false)
	{
		return new LoginContextSnapshot(
			PostId,
			UserId,
			UserName,
			DisplayName,
			EmployeeNo,
			PrescribeLevel,
			SurgeryLevel,
			EmrSignLevel,
			RoleId,
			RoleCode,
			RoleName,
			DeptId,
			DeptName,
			DeptCategory,
			WardId,
			DataScope,
			isDefault);
	}

	public string CategoryLabel
	{
		get
		{
			string deptCategory = DeptCategory;
			if (1 == 0)
			{
			}
			string result = deptCategory switch
			{
				"OPD" => "门诊", 
				"IPD" => "住院", 
				"WARD" => "病区", 
				"MEDTECH" => "医技", 
				"PHARMACY" => "药房", 
				"FINANCE" => "财务", 
				"ADMIN" => "管理", 
				_ => DeptCategory, 
			};
			if (1 == 0)
			{
			}
			return result;
		}
	}

	public IBrush CardBackground
	{
		get
		{
			IBrush result;
			if (!IsSelected)
			{
				IBrush transparent = Brushes.Transparent;
				result = transparent;
			}
			else
			{
				IBrush transparent = new SolidColorBrush(Color.Parse("#E8F3FF"));
				result = transparent;
			}
			return result;
		}
	}

	public IBrush CardBorderBrush => IsSelected ? new SolidColorBrush(Color.Parse("#1677FF")) : new SolidColorBrush(Color.Parse("#D9E2EC"));

	public string SelectionLabel
	{
		get
		{
			string result;
			if (IsSelected)
			{
				result = "当前身份";
			}
			else
			{
				result = (IsDefault ? "默认身份" : string.Empty);
			}
			return result;
		}
	}

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
				OnPropertyChanged("CardBackground");
				OnPropertyChanged("CardBorderBrush");
				OnPropertyChanged("SelectionLabel");
			}
		}
	}
}
