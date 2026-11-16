using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public sealed class DynamicFormTemplateModel : ClientModelBase
{
	private string _templateCode = string.Empty;

	private string _templateName = string.Empty;

	private string _entityType = "OPD_VISIT";

	private string _deptCategory = "OPD";

	private string _targetDeptId = string.Empty;

	private int _gridColumns = 2;

	private bool _isPublished = true;

	private string _version = "v1.0";

	private string _status = "草稿";

	private string? _extDataJson;

	private int _schemaVersion = 1;

	public string Id { get; set; } = string.Empty;

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

	public string EntityType
	{
		get
		{
			return _entityType;
		}
		set
		{
			SetField(ref _entityType, value, "EntityType");
		}
	}

	public string DeptCategory
	{
		get
		{
			return _deptCategory;
		}
		set
		{
			if (SetField(ref _deptCategory, value, "DeptCategory"))
			{
				OnPropertyChanged("DeptScope");
			}
		}
	}

	public string TargetDeptId
	{
		get
		{
			return _targetDeptId;
		}
		set
		{
			if (SetField(ref _targetDeptId, value, "TargetDeptId"))
			{
				OnPropertyChanged("DeptScope");
			}
		}
	}

	public int GridColumns
	{
		get
		{
			return _gridColumns;
		}
		set
		{
			SetField(ref _gridColumns, value, "GridColumns");
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
			if (SetField(ref _isPublished, value, "IsPublished"))
			{
				OnPropertyChanged("Status");
			}
		}
	}

	public string Version
	{
		get
		{
			return _version;
		}
		set
		{
			SetField(ref _version, value, "Version");
		}
	}

	public string Status
	{
		get
		{
			return _status;
		}
		set
		{
			SetField(ref _status, value, "Status");
		}
	}

	public string? ExtDataJson
	{
		get
		{
			return _extDataJson;
		}
		set
		{
			SetField(ref _extDataJson, value, "ExtDataJson");
		}
	}

	public int SchemaVersion
	{
		get
		{
			return _schemaVersion;
		}
		set
		{
			SetField(ref _schemaVersion, value, "SchemaVersion");
		}
	}

	public string DeptScope => string.IsNullOrWhiteSpace(TargetDeptId) ? DeptCategory : (DeptCategory + " · " + TargetDeptId);

	public ObservableCollection<DynamicFormFieldModel> Fields { get; } = new ObservableCollection<DynamicFormFieldModel>();

	public int FieldCount => Fields.Count;

	public int GenderRuleCount => Enumerable.Count(Fields, (DynamicFormFieldModel x) => !string.Equals(x.VisibilityExpr, "ALL", StringComparison.OrdinalIgnoreCase));

	public string GenderRuleDescription => (GenderRuleCount == 0) ? "无性别排他限制 (ALL)" : string.Join("；", from x in Fields
		where !string.Equals(x.VisibilityExpr, "ALL", StringComparison.OrdinalIgnoreCase)
		select x.FieldLabel + ": " + x.VisibilityExpr);

	public void ReplaceFields(IEnumerable<DynamicFormFieldModel> fields)
	{
		Fields.Clear();
		DynamicFormFieldModel[] array = fields.OrderBy((DynamicFormFieldModel x) => x.SortOrder).ThenBy((DynamicFormFieldModel x) => x.FieldCode, StringComparer.OrdinalIgnoreCase).ToArray();
		for (int num = 0; num < array.Length; num++)
		{
			array[num].SortOrder = num;
			Fields.Add(array[num]);
		}
		NotifyDerived();
	}

	public void NotifyDerived()
	{
		OnPropertyChanged("DeptScope");
		OnPropertyChanged("FieldCount");
		OnPropertyChanged("GenderRuleCount");
		OnPropertyChanged("GenderRuleDescription");
	}
}
