using System;
using Avalonia;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class EmrPrintLayoutConfigModel : ClientModelBase
{
	private string _layoutId = "LAYOUT-OPD-B5";

	private string _layoutCode = "OPD_STD_B5";

	private string _layoutName = "门诊病历标准 B5 紧凑版式";

	private string _pageSizeCode = "B5";

	private decimal _pageWidthMm = 176.0m;

	private decimal _pageHeightMm = 250.0m;

	private decimal _marginTopMm = 16.0m;

	private decimal _marginBottomMm = 16.0m;

	private decimal _marginLeftMm = 18.0m;

	private decimal _marginRightMm = 16.0m;

	private string _hospitalName = "国家区域医疗中心 (三级甲等综合医院)";

	private string _subTitle = "门 诊 结 构 化 病 历";

	private string _accentColor = "#B71C1C";

	private string _watermarkText = "国家三甲医院门诊病历归档专章";

	private string _footerText = "遵循国家卫健委《电子病历基本数据集》与 GB/T 33190-2016 标准";

	private bool _showPatientBanner = true;

	private bool _showBedAndAdmissionNo = false;

	private bool _showVitalSignsBanner = false;

	private bool _showNursingScales = false;

	private bool _showTrackChanges = false;

	private int _signLevelCount = 1;

	private double _zoomScale = 1.0;

	public string LayoutId
	{
		get
		{
			return _layoutId;
		}
		set
		{
			SetField(ref _layoutId, value, "LayoutId");
		}
	}

	public string LayoutCode
	{
		get
		{
			return _layoutCode;
		}
		set
		{
			SetField(ref _layoutCode, value, "LayoutCode");
		}
	}

	public string LayoutName
	{
		get
		{
			return _layoutName;
		}
		set
		{
			SetField(ref _layoutName, value, "LayoutName");
		}
	}

	public string PageSizeCode
	{
		get
		{
			return _pageSizeCode;
		}
		set
		{
			if (SetField(ref _pageSizeCode, value, "PageSizeCode"))
			{
				NotifyGeometryChanged();
			}
		}
	}

	public decimal PageWidthMm
	{
		get
		{
			return _pageWidthMm;
		}
		set
		{
			if (SetField(ref _pageWidthMm, value, "PageWidthMm"))
			{
				NotifyGeometryChanged();
			}
		}
	}

	public decimal PageHeightMm
	{
		get
		{
			return _pageHeightMm;
		}
		set
		{
			if (SetField(ref _pageHeightMm, value, "PageHeightMm"))
			{
				NotifyGeometryChanged();
			}
		}
	}

	public decimal MarginTopMm
	{
		get
		{
			return _marginTopMm;
		}
		set
		{
			if (SetField(ref _marginTopMm, value, "MarginTopMm"))
			{
				NotifyGeometryChanged();
			}
		}
	}

	public decimal MarginBottomMm
	{
		get
		{
			return _marginBottomMm;
		}
		set
		{
			if (SetField(ref _marginBottomMm, value, "MarginBottomMm"))
			{
				NotifyGeometryChanged();
			}
		}
	}

	public decimal MarginLeftMm
	{
		get
		{
			return _marginLeftMm;
		}
		set
		{
			if (SetField(ref _marginLeftMm, value, "MarginLeftMm"))
			{
				NotifyGeometryChanged();
			}
		}
	}

	public decimal MarginRightMm
	{
		get
		{
			return _marginRightMm;
		}
		set
		{
			if (SetField(ref _marginRightMm, value, "MarginRightMm"))
			{
				NotifyGeometryChanged();
			}
		}
	}

	public string HospitalName
	{
		get
		{
			return _hospitalName;
		}
		set
		{
			SetField(ref _hospitalName, value, "HospitalName");
		}
	}

	public string SubTitle
	{
		get
		{
			return _subTitle;
		}
		set
		{
			SetField(ref _subTitle, value, "SubTitle");
		}
	}

	public string AccentColor
	{
		get
		{
			return _accentColor;
		}
		set
		{
			SetField(ref _accentColor, value, "AccentColor");
		}
	}

	public string WatermarkText
	{
		get
		{
			return _watermarkText;
		}
		set
		{
			SetField(ref _watermarkText, value, "WatermarkText");
		}
	}

	public string FooterText
	{
		get
		{
			return _footerText;
		}
		set
		{
			SetField(ref _footerText, value, "FooterText");
		}
	}

	public bool ShowPatientBanner
	{
		get
		{
			return _showPatientBanner;
		}
		set
		{
			SetField(ref _showPatientBanner, value, "ShowPatientBanner");
		}
	}

	public bool ShowBedAndAdmissionNo
	{
		get
		{
			return _showBedAndAdmissionNo;
		}
		set
		{
			SetField(ref _showBedAndAdmissionNo, value, "ShowBedAndAdmissionNo");
		}
	}

	public bool ShowVitalSignsBanner
	{
		get
		{
			return _showVitalSignsBanner;
		}
		set
		{
			SetField(ref _showVitalSignsBanner, value, "ShowVitalSignsBanner");
		}
	}

	public bool ShowNursingScales
	{
		get
		{
			return _showNursingScales;
		}
		set
		{
			SetField(ref _showNursingScales, value, "ShowNursingScales");
		}
	}

	public bool ShowTrackChanges
	{
		get
		{
			return _showTrackChanges;
		}
		set
		{
			SetField(ref _showTrackChanges, value, "ShowTrackChanges");
		}
	}

	public int SignLevelCount
	{
		get
		{
			return _signLevelCount;
		}
		set
		{
			SetField(ref _signLevelCount, value, "SignLevelCount");
		}
	}

	public double ZoomScale
	{
		get
		{
			return _zoomScale;
		}
		set
		{
			double value2 = Math.Clamp(value, 0.75, 1.35);
			if (SetField(ref _zoomScale, value2, "ZoomScale"))
			{
				NotifyGeometryChanged();
				OnPropertyChanged("ZoomPercentText");
			}
		}
	}

	public double CanvasWidthPx => Math.Round((double)PageWidthMm * 3.0 * ZoomScale);

	public double CanvasMinHeightPx => Math.Round((double)PageHeightMm * 2.85 * ZoomScale);

	public Thickness CanvasPadding => new Thickness(Math.Max(12.0, Math.Round((double)MarginLeftMm * 1.6 * ZoomScale)), Math.Max(12.0, Math.Round((double)MarginTopMm * 1.6 * ZoomScale)), Math.Max(12.0, Math.Round((double)MarginRightMm * 1.6 * ZoomScale)), Math.Max(12.0, Math.Round((double)MarginBottomMm * 1.6 * ZoomScale)));

	public string PaperSpecSummary => $"{PageSizeCode} ({PageWidthMm:0.#}×{PageHeightMm:0.#} mm) · 页边距 上{MarginTopMm:0.#}/下{MarginBottomMm:0.#}/左{MarginLeftMm:0.#}/右{MarginRightMm:0.#} mm";

	public string ZoomPercentText => $"{Math.Round(ZoomScale * 100.0)}%";

	public void NotifyGeometryChanged()
	{
		OnPropertyChanged("CanvasWidthPx");
		OnPropertyChanged("CanvasMinHeightPx");
		OnPropertyChanged("CanvasPadding");
		OnPropertyChanged("PaperSpecSummary");
	}
}
