using System;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class PrescriptionExtraFieldModel : ClientModelBase
{
	private string _name = string.Empty;

	private string _value = string.Empty;

	private string _placeholder = "请输入";

	private string _dataType = "STRING";

	private bool _isDeleted;

	private bool _isEditing;

	private int _sortOrder;

	public string Id { get; set; } = Guid.NewGuid().ToString("D");

	public string Name
	{
		get
		{
			return _name;
		}
		set
		{
			SetField(ref _name, value, "Name");
		}
	}

	public string Value
	{
		get
		{
			return _value;
		}
		set
		{
			SetField(ref _value, value, "Value");
		}
	}

	public string Placeholder
	{
		get
		{
			return _placeholder;
		}
		set
		{
			SetField(ref _placeholder, value, "Placeholder");
		}
	}

	public string DataType
	{
		get
		{
			return _dataType;
		}
		set
		{
			SetField(ref _dataType, value, "DataType");
		}
	}

	public int SortOrder
	{
		get
		{
			return _sortOrder;
		}
		set
		{
			SetField(ref _sortOrder, value, "SortOrder");
		}
	}

	public bool IsEditing
	{
		get
		{
			return _isEditing;
		}
		set
		{
			SetField(ref _isEditing, value, "IsEditing");
		}
	}

	public long? DeletedAtMs { get; set; }

	public string? DeletedBy { get; set; }

	public bool IsDeleted
	{
		get
		{
			return _isDeleted;
		}
		set
		{
			if (SetField(ref _isDeleted, value, "IsDeleted"))
			{
				OnPropertyChanged("IsVisible");
			}
		}
	}

	public bool IsVisible => !IsDeleted;
}
