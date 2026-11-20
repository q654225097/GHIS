using System.Collections.ObjectModel;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public class EmrVisualTemplateModel : ClientModelBase
{
	private string _templateId = string.Empty;

	private string _templateCode = string.Empty;

	private string _templateName = string.Empty;

	private string _emrScene = "OPD";

	private string _docTypeCode = string.Empty;

	private string _deptName = "心血管内科";

	private int _timeLimitHours = 24;

	private int _requiredSignLevels = 1;

	private string _boundLayoutCode = "OPD_STD_B5";

	private int _version = 1;

	private bool _isPublished = true;

	public string TemplateId
	{
		get
		{
			return _templateId;
		}
		set
		{
			SetField(ref _templateId, value, "TemplateId");
		}
	}

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

	public string EmrScene
	{
		get
		{
			return _emrScene;
		}
		set
		{
			if (SetField(ref _emrScene, value, "EmrScene"))
			{
				OnPropertyChanged("SceneBadgeText");
				OnPropertyChanged("SceneBrush");
			}
		}
	}

	public string DocTypeCode
	{
		get
		{
			return _docTypeCode;
		}
		set
		{
			SetField(ref _docTypeCode, value, "DocTypeCode");
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

	public int TimeLimitHours
	{
		get
		{
			return _timeLimitHours;
		}
		set
		{
			SetField(ref _timeLimitHours, value, "TimeLimitHours");
		}
	}

	public int RequiredSignLevels
	{
		get
		{
			return _requiredSignLevels;
		}
		set
		{
			if (SetField(ref _requiredSignLevels, value, "RequiredSignLevels"))
			{
				OnPropertyChanged("SignLevelRequirementText");
			}
		}
	}

	public string BoundLayoutCode
	{
		get
		{
			return _boundLayoutCode;
		}
		set
		{
			SetField(ref _boundLayoutCode, value, "BoundLayoutCode");
		}
	}

	public int Version
	{
		get
		{
			return _version;
		}
		set
		{
			if (SetField(ref _version, value, "Version"))
			{
				OnPropertyChanged("VersionBadge");
			}
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
			SetField(ref _isPublished, value, "IsPublished");
		}
	}

	public ObservableCollection<EmrVisualSectionModel> Sections { get; } = new ObservableCollection<EmrVisualSectionModel>();

	public string VersionBadge => $"v{Version}.0";

	public string SceneBadgeText
	{
		get
		{
			string emrScene = EmrScene;
			if (1 == 0)
			{
			}
			string result = emrScene switch
			{
				"OPD" => "门诊病历 (OPD)", 
				"IPD" => "住院病历 (IPD)", 
				"WARD" => "护理病历 (WARD)", 
				_ => EmrScene, 
			};
			if (1 == 0)
			{
			}
			return result;
		}
	}

	public string SceneBrush
	{
		get
		{
			string emrScene = EmrScene;
			if (1 == 0)
			{
			}
			string result = emrScene switch
			{
				"OPD" => "#B71C1C", 
				"IPD" => "#1565C0", 
				"WARD" => "#059669", 
				_ => "#2563EB", 
			};
			if (1 == 0)
			{
			}
			return result;
		}
	}

	public string SignLevelRequirementText
	{
		get
		{
			int requiredSignLevels = RequiredSignLevels;
			if (1 == 0)
			{
			}
			string result = requiredSignLevels switch
			{
				3 => "三级审签 (住院医师→主治→主任终审)", 
				2 => (EmrScene == "WARD") ? "双级审签 (责任护士→病区护士长质控)" : "二级审签 (住院医师→主治医师审签)", 
				_ => "一级审签 (接诊医师/当班护师独立审签)", 
			};
			if (1 == 0)
			{
			}
			return result;
		}
	}

	public string TimeLimitText => $"书写时限: ≤{TimeLimitHours}小时";

	private string _designMode = "FILL"; // "FILL" (填写模式) / "PRINT" (打印模式)
	private string _pageSizeCode = "A4";
	private decimal _pageWidthMm = 210.0m;
	private decimal _pageHeightMm = 297.0m;
	private decimal _marginTopMm = 20.0m;
	private decimal _marginBottomMm = 20.0m;
	private decimal _marginLeftMm = 20.0m;
	private decimal _marginRightMm = 20.0m;

	public string DesignMode
	{
		get => _designMode;
		set
		{
			if (SetField(ref _designMode, value))
			{
				OnPropertyChanged(nameof(IsFillMode));
				OnPropertyChanged(nameof(IsPrintMode));
				NotifyPrintMetrics();
			}
		}
	}

	public bool IsFillMode => DesignMode == "FILL";
	public bool IsPrintMode => DesignMode == "PRINT";

	public string PageSizeCode
	{
		get => _pageSizeCode;
		set
		{
			if (SetField(ref _pageSizeCode, value))
			{
				switch (value)
				{
					case "A4":
						PageWidthMm = 210.0m;
						PageHeightMm = 297.0m;
						break;
					case "B5":
						PageWidthMm = 176.0m;
						PageHeightMm = 250.0m;
						break;
					case "A5":
						PageWidthMm = 148.0m;
						PageHeightMm = 210.0m;
						break;
					case "16K":
						PageWidthMm = 185.0m;
						PageHeightMm = 260.0m;
						break;
				}
				NotifyPrintMetrics();
			}
		}
	}

	public decimal PageWidthMm
	{
		get => _pageWidthMm;
		set
		{
			if (SetField(ref _pageWidthMm, value))
			{
				NotifyPrintMetrics();
			}
		}
	}

	public decimal PageHeightMm
	{
		get => _pageHeightMm;
		set
		{
			if (SetField(ref _pageHeightMm, value))
			{
				NotifyPrintMetrics();
			}
		}
	}

	public decimal MarginTopMm
	{
		get => _marginTopMm;
		set
		{
			if (SetField(ref _marginTopMm, value))
			{
				NotifyPrintMetrics();
			}
		}
	}

	public decimal MarginBottomMm
	{
		get => _marginBottomMm;
		set
		{
			if (SetField(ref _marginBottomMm, value))
			{
				NotifyPrintMetrics();
			}
		}
	}

	public decimal MarginLeftMm
	{
		get => _marginLeftMm;
		set
		{
			if (SetField(ref _marginLeftMm, value))
			{
				NotifyPrintMetrics();
			}
		}
	}

	public decimal MarginRightMm
	{
		get => _marginRightMm;
		set
		{
			if (SetField(ref _marginRightMm, value))
			{
				NotifyPrintMetrics();
			}
		}
	}

	public decimal AvailablePrintHeightMm => Math.Max(0, PageHeightMm - MarginTopMm - MarginBottomMm);
	public decimal AvailablePrintWidthMm => Math.Max(0, PageWidthMm - MarginLeftMm - MarginRightMm);

	public decimal TotalPrintContentHeightMm
	{
		get
		{
			decimal fixedOverheadMm = 66.0m; // Header(22) + Banner(14) + Footer(12) + SignSlots(18)
			decimal sectionsTotal = 0;
			foreach (var sec in Sections)
			{
				sectionsTotal += (8.0m + sec.EstimatedHeightMm);
			}
			return fixedOverheadMm + sectionsTotal;
		}
	}

	public bool IsPrintExceeded => IsPrintMode && (TotalPrintContentHeightMm > AvailablePrintHeightMm);

	public decimal PrintOverflowMm => Math.Max(0, TotalPrintContentHeightMm - AvailablePrintHeightMm);

	public decimal SafetyMarginMm => Math.Max(0, AvailablePrintHeightMm - TotalPrintContentHeightMm);

	public string PrintSafetyStatusText
	{
		get
		{
			if (!IsPrintMode) return "填写模式已启用 (自由栅格流式排版)";
			if (IsPrintExceeded)
			{
				return $"⚠️ 内容超出纸张大小！有效高度: {AvailablePrintHeightMm:0.#}mm | 当前占用: {TotalPrintContentHeightMm:0.#}mm (超限 {PrintOverflowMm:0.#}mm，禁止保存)";
			}
			return $"✔ 打印尺寸合规 | 纸张有效高度: {AvailablePrintHeightMm:0.#}mm | 当前占用: {TotalPrintContentHeightMm:0.#}mm (安全余量: {SafetyMarginMm:0.#}mm)";
		}
	}

	public string PrintSafetyBrush
	{
		get
		{
			if (!IsPrintMode) return "#2563EB";
			return IsPrintExceeded ? "#DC2626" : "#059669";
		}
	}

	public void NotifyPrintMetrics()
	{
		OnPropertyChanged(nameof(AvailablePrintHeightMm));
		OnPropertyChanged(nameof(AvailablePrintWidthMm));
		OnPropertyChanged(nameof(TotalPrintContentHeightMm));
		OnPropertyChanged(nameof(IsPrintExceeded));
		OnPropertyChanged(nameof(PrintOverflowMm));
		OnPropertyChanged(nameof(SafetyMarginMm));
		OnPropertyChanged(nameof(PrintSafetyStatusText));
		OnPropertyChanged(nameof(PrintSafetyBrush));
	}
}
