using System.Collections.ObjectModel;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class EmrVisualSectionModel : ClientModelBase
{
	private string _sectionCode = string.Empty;

	private string _sectionTitle = string.Empty;

	private string _renderMode = "GRID_FORM";

	private bool _isRequired = true;

	private string _visibilityExpr = "ALL";

	private int _sortOrder = 1;

	public string SectionCode
	{
		get
		{
			return _sectionCode;
		}
		set
		{
			SetField(ref _sectionCode, value, "SectionCode");
		}
	}

	public string SectionTitle
	{
		get
		{
			return _sectionTitle;
		}
		set
		{
			SetField(ref _sectionTitle, value, "SectionTitle");
		}
	}

	public string RenderMode
	{
		get
		{
			return _renderMode;
		}
		set
		{
			if (SetField(ref _renderMode, value, "RenderMode"))
			{
				OnPropertyChanged("RenderModeDisplay");
				OnPropertyChanged("IsTableGridMode");
				OnPropertyChanged("IsParagraphMode");
			}
		}
	}

	public bool IsRequired
	{
		get
		{
			return _isRequired;
		}
		set
		{
			SetField(ref _isRequired, value, "IsRequired");
		}
	}

	public string VisibilityExpr
	{
		get
		{
			return _visibilityExpr;
		}
		set
		{
			SetField(ref _visibilityExpr, value, "VisibilityExpr");
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

	public ObservableCollection<EmrVisualFieldItemModel> Fields { get; } = new ObservableCollection<EmrVisualFieldItemModel>();

	public bool IsTableGridMode => RenderMode == "TABLE_GRID";

	public bool IsParagraphMode => RenderMode == "INLINE_PARAGRAPH";

	public string RenderModeDisplay
	{
		get
		{
			string renderMode = RenderMode;
			if (1 == 0)
			{
			}
			string result = renderMode switch
			{
				"GRID_FORM" => "结构化双列表单 (GRID_FORM)", 
				"INLINE_PARAGRAPH" => "连续病程段落 (INLINE_PARAGRAPH)", 
				"TABLE_GRID" => "护理评估量表网格 (TABLE_GRID)", 
				"VECTOR_CANVAS" => "专科矢量图画布 (VECTOR_CANVAS)", 
				_ => RenderMode, 
			};
			if (1 == 0)
			{
			}
			return result;
		}
	}
}
