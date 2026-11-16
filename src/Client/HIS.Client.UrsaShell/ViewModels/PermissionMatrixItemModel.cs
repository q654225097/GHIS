using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class PermissionMatrixItemModel : ClientModelBase
{
	private int _accessPolicy;

	public string ResourceKey { get; set; } = string.Empty;

	public string ResourceName { get; set; } = string.Empty;

	public bool HasDisplayName => !string.IsNullOrWhiteSpace(ResourceName)
		&& !string.Equals(ResourceName, ResourceKey, StringComparison.OrdinalIgnoreCase);

	public int AccessPolicy
	{
		get
		{
			return _accessPolicy;
		}
		set
		{
			if (SetField(ref _accessPolicy, value, nameof(AccessPolicy)))
			{
				OnPropertyChanged(nameof(PolicyLabel));
				OnPropertyChanged(nameof(PolicyClass));
				OnPropertyChanged(nameof(IsFullAccess));
				OnPropertyChanged(nameof(IsReadOnly));
				OnPropertyChanged(nameof(IsMasked));
				OnPropertyChanged(nameof(IsHidden));
			}
		}
	}

	public string PolicyLabel => AccessPolicy switch
	{
		3 => "完全授权",
		2 => "只读",
		1 => "脱敏",
		_ => "仅隐藏"
	};

	public string PolicyClass => AccessPolicy switch
	{
		3 => "Success",
		2 => "Info",
		1 => "Warning",
		_ => "Secondary"
	};

	public bool IsFullAccess => AccessPolicy == 3;

	public bool IsReadOnly => AccessPolicy == 2;

	public bool IsMasked => AccessPolicy == 1;

	public bool IsHidden => AccessPolicy == 0;
}
