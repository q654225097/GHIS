using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Threading;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell.Controls;
using HIS.Client.UrsaShell.ViewModels;
using HIS.Client.UrsaShell.Views;

namespace HIS.Client.UrsaShell.Shell;

public sealed class ClinicalAlertRecordItem : ClientModelBase
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string EventType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string Severity { get; set; } = "Info";
    public string TimeText { get; set; } = DateTime.Now.ToString("HH:mm:ss");
}

public sealed class ShellViewModel : ClientModelBase
{
    private readonly Dictionary<string, Control> _stationViewCache = new();
    private readonly Dictionary<string, IStationLifecycle> _stationLifecycles = new();
    private readonly Dictionary<string, (DispatcherTimer Timer, ToastMessage Toast)> _toastTimers = new();
    private Action<int>? _openLegacyWindowCallback;
    private Action? _logoutCallback;
    private Action? _focusSearchCallback;

    private bool _isAlertHubConnected;
    private int _unreadAlertCount;

    private ShellNavItem? _activeNavItem;
    private object? _currentStationView;
    private string _globalSearchQuery = string.Empty;
    private bool _isSearchDropdownOpen;

    private bool _isDrawerOpen;
    private string _drawerTitle = string.Empty;
    private string? _drawerSubtitle;
    private double _drawerWidth = 520;
    private object? _drawerContent;

    private bool _isConfirmOpen;
    private string _confirmTitle = string.Empty;
    private string _confirmMessage = string.Empty;
    private string _confirmButtonText = "确认";
    private Action? _confirmCallback;

    public ShellViewModel(WorkbenchModel? workbench = null)
    {
        Workbench = workbench ?? new WorkbenchModel();
        Workbench.PropertyChanged += OnWorkbenchPropertyChanged;
        Workbench.NoticeRaised += OnWorkbenchNoticeRaised;

        NavigateCommand = new RelayCommand(param =>
        {
            if (param is ShellNavItem item)
            {
                ActivateNavItem(item);
            }
            else if (param is string key)
            {
                NavigateByKey(key);
            }
        });

        SelectSearchHitCommand = new RelayCommand(param =>
        {
            if (param is GlobalPatientHit hit)
            {
                SelectGlobalPatient(hit);
            }
        });

        OpenTimelineDrawerCommand = new RelayCommand(OpenTimelineDrawer);
        OpenCriticalValueDrawerCommand = new RelayCommand(OpenCriticalValueDrawer);
        OpenContextSwitchDrawerCommand = new RelayCommand(OpenContextSwitchDrawer);
        FocusGlobalSearchCommand = new RelayCommand(() => _focusSearchCallback?.Invoke());
        CloseDrawerCommand = new RelayCommand(CloseDrawer);
        ConfirmAcceptCommand = new RelayCommand(() =>
        {
            var cb = _confirmCallback;
            CloseConfirm();
            cb?.Invoke();
        });
        ConfirmCancelCommand = new RelayCommand(CloseConfirm);
        LogoutCommand = new RelayCommand(() =>
        {
            StopAlertHub();
            _logoutCallback?.Invoke();
        });
        OpenAlertHistoryDrawerCommand = new RelayCommand(OpenAlertHistoryDrawer);
        ClearAlertHistoryCommand = new RelayCommand(() =>
        {
            AlertHistory.Clear();
            UnreadAlertCount = 0;
        });
        SwitchToContextCommand = new AsyncRelayCommand(async param =>
        {
            if (param is string ctxId)
            {
                await Workbench.SwitchToContextAsync(ctxId);
                RebuildNavigation();
                RefreshBannerBindings();
                CloseDrawer();
            }
        });

        DoctorAckCriticalCommand = new RelayCommand(() =>
        {
            Workbench.CriticalValueAlert.DoctorAck = $"✓ 已签收 ({Workbench.CurrentOperatorName} {DateTime.Now:HH:mm:ss})";
            ShowToast("危急值经治医生已签收，现可由病区护士复核。", "Success");
            RefreshNavBadges();
        });

        NurseAckCriticalCommand = new RelayCommand(() =>
        {
            Workbench.CriticalValueAlert.NurseAck = $"✓ 已签收 ({Workbench.CurrentOperatorName} {DateTime.Now:HH:mm:ss})";
            ShowToast("危急值护士已复核签收，双签闭环完成。", "Success");
            RefreshNavBadges();
        });

        RebuildNavigation();
        StartAlertHub();
    }

    public WorkbenchModel Workbench { get; }

    public event Action? ActiveNavChanged;

    public ObservableCollection<ShellNavGroup> NavGroups { get; } = new();
    public ObservableCollection<ToastMessage> Toasts { get; } = new();
    public ObservableCollection<GlobalPatientHit> GlobalSearchResults { get; } = new();
    public ObservableCollection<ClinicalAlertRecordItem> AlertHistory { get; } = new();

    public bool IsAlertHubConnected
    {
        get => _isAlertHubConnected;
        private set
        {
            if (SetField(ref _isAlertHubConnected, value))
            {
                OnPropertyChanged(nameof(AlertConnectionText));
            }
        }
    }

    public string AlertConnectionText => _isAlertHubConnected ? "实时告警: 已连接" : "实时告警: 离线重连中";

    public int UnreadAlertCount
    {
        get => _unreadAlertCount;
        set
        {
            if (SetField(ref _unreadAlertCount, value))
            {
                OnPropertyChanged(nameof(UnreadAlertCountText));
                OnPropertyChanged(nameof(HasUnreadAlerts));
            }
        }
    }

    public string UnreadAlertCountText => _unreadAlertCount > 99 ? "99+" : _unreadAlertCount.ToString();
    public bool HasUnreadAlerts => _unreadAlertCount > 0;

    public ICommand NavigateCommand { get; }
    public ICommand SelectSearchHitCommand { get; }
    public ICommand OpenTimelineDrawerCommand { get; }
    public ICommand OpenCriticalValueDrawerCommand { get; }
    public ICommand OpenAlertHistoryDrawerCommand { get; }
    public ICommand ClearAlertHistoryCommand { get; }
    public ICommand OpenContextSwitchDrawerCommand { get; }
    public ICommand FocusGlobalSearchCommand { get; }
    public ICommand CloseDrawerCommand { get; }
    public ICommand ConfirmAcceptCommand { get; }
    public ICommand ConfirmCancelCommand { get; }
    public ICommand LogoutCommand { get; }
    public ICommand SwitchToContextCommand { get; }
    public ICommand DoctorAckCriticalCommand { get; }
    public ICommand NurseAckCriticalCommand { get; }

    public void AttachWindowCallbacks(
        Action<int> openLegacyWindow,
        Action logoutCallback,
        Action focusSearchCallback)
    {
        _openLegacyWindowCallback = openLegacyWindow;
        _logoutCallback = logoutCallback;
        _focusSearchCallback = focusSearchCallback;
    }

    public ShellNavItem? ActiveNavItem
    {
        get => _activeNavItem;
        private set
        {
            if (_activeNavItem != null) _activeNavItem.IsSelected = false;
            SetField(ref _activeNavItem, value, nameof(ActiveNavItem));
            if (_activeNavItem != null) _activeNavItem.IsSelected = true;
            OnPropertyChanged(nameof(IsClinicalStation));
            OnPropertyChanged(nameof(ActiveStationTitle));
            OnPropertyChanged(nameof(IsAdminSupervisionMode));
        }
    }

    public string ActiveStationTitle => ActiveNavItem?.Title ?? "工作台";

    public bool IsClinicalStation => ActiveNavItem?.IsClinical ?? false;

    public bool IsAdminSupervisionMode => SessionContext.Current.IsAdmin && (ActiveNavItem?.IsClinical == true);

    public object? CurrentStationView
    {
        get => _currentStationView;
        private set => SetField(ref _currentStationView, value, nameof(CurrentStationView));
    }

    public string GlobalSearchQuery
    {
        get => _globalSearchQuery;
        set
        {
            if (SetField(ref _globalSearchQuery, value, nameof(GlobalSearchQuery)))
            {
                RefreshGlobalSearchResults();
            }
        }
    }

    public bool IsSearchDropdownOpen
    {
        get => _isSearchDropdownOpen;
        set => SetField(ref _isSearchDropdownOpen, value, nameof(IsSearchDropdownOpen));
    }

    public bool IsDrawerOpen
    {
        get => _isDrawerOpen;
        private set => SetField(ref _isDrawerOpen, value, nameof(IsDrawerOpen));
    }

    public string DrawerTitle
    {
        get => _drawerTitle;
        private set => SetField(ref _drawerTitle, value, nameof(DrawerTitle));
    }

    public string? DrawerSubtitle
    {
        get => _drawerSubtitle;
        private set => SetField(ref _drawerSubtitle, value, nameof(DrawerSubtitle));
    }

    public double DrawerWidth
    {
        get => _drawerWidth;
        private set => SetField(ref _drawerWidth, value, nameof(DrawerWidth));
    }

    public object? DrawerContent
    {
        get => _drawerContent;
        private set => SetField(ref _drawerContent, value, nameof(DrawerContent));
    }

    public bool IsConfirmOpen
    {
        get => _isConfirmOpen;
        private set => SetField(ref _isConfirmOpen, value, nameof(IsConfirmOpen));
    }

    public string ConfirmTitle
    {
        get => _confirmTitle;
        private set => SetField(ref _confirmTitle, value, nameof(ConfirmTitle));
    }

    public string ConfirmMessage
    {
        get => _confirmMessage;
        private set => SetField(ref _confirmMessage, value, nameof(ConfirmMessage));
    }

    public string ConfirmButtonText
    {
        get => _confirmButtonText;
        private set => SetField(ref _confirmButtonText, value, nameof(ConfirmButtonText));
    }

    // Unified global PatientBanner projections
    public bool BannerHasPatient => ResolveActivePatientKind() != ActivePatientKind.None;

    public string BannerEncounterBadge => ResolveActivePatientKind() switch
    {
        ActivePatientKind.Outpatient => Workbench.SelectedPatient?.Status ?? "门诊",
        ActivePatientKind.Inpatient => $"住院 · {Workbench.SelectedInpatient?.BedCode ?? "在院"}",
        _ => string.Empty
    };

    public string BannerPatientName => ResolveActivePatientKind() switch
    {
        ActivePatientKind.Outpatient => Workbench.SelectedPatient?.PatientName ?? string.Empty,
        ActivePatientKind.Inpatient => Workbench.SelectedInpatient?.PatientName ?? string.Empty,
        _ => string.Empty
    };

    public string BannerGenderAndAge => ResolveActivePatientKind() switch
    {
        ActivePatientKind.Outpatient => $"{Workbench.SelectedPatient?.Gender} · {Workbench.SelectedPatient?.Age}岁",
        ActivePatientKind.Inpatient => $"{Workbench.SelectedInpatient?.Gender} · {Workbench.SelectedInpatient?.Age}岁",
        _ => string.Empty
    };

    public string BannerIdentifier => ResolveActivePatientKind() switch
    {
        ActivePatientKind.Outpatient => $"就诊号 {Workbench.SelectedPatient?.VisitNo}",
        ActivePatientKind.Inpatient => $"住院号 {Workbench.SelectedInpatient?.AdmissionNo}",
        _ => string.Empty
    };

    public string BannerDiagnosis => ResolveActivePatientKind() switch
    {
        ActivePatientKind.Outpatient => Workbench.EmrWorkspace?.Diagnosis ?? string.Empty,
        ActivePatientKind.Inpatient => Workbench.SelectedInpatient?.Diagnosis ?? string.Empty,
        _ => string.Empty
    };

    public bool BannerHasAllergy => ResolveActivePatientKind() switch
    {
        ActivePatientKind.Outpatient => Workbench.HasSelectedPatientAllergy,
        ActivePatientKind.Inpatient => Workbench.SelectedInpatient?.HasAllergyAlert == true,
        _ => false
    };

    public string BannerAllergyText => ResolveActivePatientKind() switch
    {
        ActivePatientKind.Outpatient => Workbench.SelectedPatientAllergyText ?? string.Empty,
        ActivePatientKind.Inpatient => Workbench.SelectedInpatient?.AllergySummary ?? string.Empty,
        _ => string.Empty
    };

    private enum ActivePatientKind { None, Outpatient, Inpatient }

    private ActivePatientKind ResolveActivePatientKind()
    {
        var key = ActiveNavItem?.Key ?? string.Empty;
        if (key is "ipd_doctor" or "ipd_nurse")
        {
            if (Workbench.SelectedInpatient != null) return ActivePatientKind.Inpatient;
            if (Workbench.SelectedPatient != null) return ActivePatientKind.Outpatient;
            return ActivePatientKind.None;
        }

        if (Workbench.SelectedPatient != null) return ActivePatientKind.Outpatient;
        if (Workbench.SelectedInpatient != null) return ActivePatientKind.Inpatient;
        return ActivePatientKind.None;
    }

    public void RebuildNavigation()
    {
        var prevKey = ActiveNavItem?.Key;
        NavGroups.Clear();
        var ctx = SessionContext.Current;

        var overview = new ShellNavGroup("overview", "工作台", isExpanded: true);
        overview.Items.Add(new ShellNavItem
        {
            Key = "workbench",
            LegacyNavIndex = -1,
            Title = "我的待办",
            Icon = "📋",
            IsClinical = false,
            Description = "按当前岗位聚合待处理队列、医嘱、处方与危急值"
        });
        overview.Items.Add(new ShellNavItem
        {
            Key = "workflow_inbox",
            LegacyNavIndex = -1,
            Title = "审批中心",
            Icon = "📥",
            IsClinical = false,
            Description = "我的待办、已办、抄送与全院单据审批中心"
        });
        NavGroups.Add(overview);

        var clinical = new ShellNavGroup("clinical", "临床诊疗", isExpanded: true);
        if (ctx.CanSeeOpdStation)
        {
            clinical.Items.Add(new ShellNavItem
            {
                Key = "opd",
                LegacyNavIndex = 0,
                Title = "门诊医生站",
                Icon = "🩺",
                IsClinical = true,
                Description = "叫号接诊、门诊病历书写、处方开立与检验检查"
            });
        }
        if (ctx.CanSeeIpdStation)
        {
            clinical.Items.Add(new ShellNavItem
            {
                Key = "ipd_doctor",
                LegacyNavIndex = 1,
                Title = "住院医生站",
                Icon = "🛏️",
                IsClinical = true,
                Description = "在院患者床位视图、长短期医嘱与病程管理"
            });
        }
        if (ctx.CanSeeWardStation)
        {
            clinical.Items.Add(new ShellNavItem
            {
                Key = "ipd_nurse",
                LegacyNavIndex = 2,
                Title = "病区护士站",
                Icon = "💉",
                IsClinical = true,
                Description = "医嘱校对、分解、执行与借床/床位护理"
            });
        }
        if (ctx.CanSeeMedTech)
        {
            clinical.Items.Add(new ShellNavItem
            {
                Key = "medtech",
                LegacyNavIndex = 3,
                Title = "医技与检查",
                Icon = "🔬",
                IsClinical = true,
                Description = "检查申请确费锁单、结果报告与危急值双签"
            });
        }
        clinical.Items.Add(new ShellNavItem
        {
            Key = "treatment",
            LegacyNavIndex = -1,
            Title = "一般治疗",
            Icon = "🩹",
            IsClinical = true,
            Description = "透析、化疗、理疗与高压氧等治疗项目与参数联动"
        });
        clinical.Items.Add(new ShellNavItem
        {
            Key = "monitoring",
            LegacyNavIndex = -1,
            Title = "重症监护",
            Icon = "📈",
            IsClinical = true,
            Description = "ICU 连续生命体征采集、生理报警与重症风险评分"
        });
        clinical.Items.Add(new ShellNavItem
        {
            Key = "blood_bank",
            LegacyNavIndex = -1,
            Title = "血库管理",
            Icon = "🩸",
            IsClinical = true,
            Description = "全院血液库存、备血建议、交叉配血与双签发血闭环"
        });
        if (clinical.Items.Count > 0) NavGroups.Add(clinical);

        if (ctx.CanSeePharmacy)
        {
            var med = new ShellNavGroup("medication", "药事管理", isExpanded: true);
            if (ctx.CanSeePharmacyWindow)
            {
                med.Items.Add(new ShellNavItem
                {
                    Key = "pharmacy_window",
                    LegacyNavIndex = 4,
                    Title = "窗口发药",
                    Icon = "💊",
                    IsClinical = false,
                    Description = "门诊收费处方核验发药、住院发药与退药质检"
                });
            }
            if (ctx.CanSeeWarehouse)
            {
                med.Items.Add(new ShellNavItem
                {
                    Key = "pharmacy_warehouse",
                    LegacyNavIndex = 4,
                    Title = "药库与目录",
                    Icon = "📦",
                    IsClinical = false,
                    Description = "采购入库、效期批次、跨库调拨与药品主目录维护"
                });
            }
            if (med.Items.Count > 0) NavGroups.Add(med);
        }

        var logistics = new ShellNavGroup("logistics", "物资与耗材", isExpanded: true);
        if (ctx.CanSeeConsumablesWarehouse)
        {
            logistics.Items.Add(new ShellNavItem
            {
                Key = "consumables_warehouse",
                LegacyNavIndex = 4,
                Title = "耗材库与供应室",
                Icon = "📦",
                IsClinical = false,
                Description = "耗材主目录维护、批次效期、出库与消毒供应室(CSSD)器械包追溯"
            });
        }
        if (ctx.CanSeeConsumablesWard)
        {
            logistics.Items.Add(new ShellNavItem
            {
                Key = "consumables_ward",
                LegacyNavIndex = 2,
                Title = "病区耗材请领",
                Icon = "📑",
                IsClinical = true,
                Description = "科室耗材物资请领与高值植入耗材UDI扫码核销绑定"
            });
        }
        if (logistics.Items.Count > 0) NavGroups.Add(logistics);

        if (ctx.CanSeeAnalytics)
        {
            var bi = new ShellNavGroup("bi", "运营与决策", isExpanded: true);
            bi.Items.Add(new ShellNavItem
            {
                Key = "analytics",
                LegacyNavIndex = -1,
                Title = "统计分析工作站",
                Icon = "📊",
                IsClinical = false,
                Description = "全院门诊住院运营、药占比、医疗质控与等保安全决策看板"
            });
            NavGroups.Add(bi);
        }

        var finance = new ShellNavGroup("finance", "挂号与结算", isExpanded: true);
        if (ctx.CanSeeFinance)
        {
            finance.Items.Add(new ShellNavItem
            {
                Key = "registration",
                LegacyNavIndex = 11,
                Title = "门诊挂号",
                Icon = "🎫",
                IsClinical = false,
                Description = "门诊患者检索、号源收费挂号、退号与退款"
            });
        }
        if (ctx.IsAdmin)
        {
            finance.Items.Add(new ShellNavItem
            {
                Key = "schedule",
                LegacyNavIndex = 10,
                Title = "排班与挂号",
                Icon = "📅",
                IsClinical = false,
                Description = "号源排班、预约挂号与分诊"
            });
        }
        if (ctx.CanSeeFinance)
        {
            finance.Items.Add(new ShellNavItem
            {
                Key = "finance",
                LegacyNavIndex = 11,
                Title = "收费结算",
                Icon = "💳",
                IsClinical = false,
                Description = "门诊收费、退费审核、住院预交与出院结算"
            });
        }
        if (finance.Items.Count > 0) NavGroups.Add(finance);

        if (ctx.CanSeeEmrOfd)
        {
            var records = new ShellNavGroup("records", "病历文书", isExpanded: true);
            records.Items.Add(new ShellNavItem
            {
                Key = "emr_archive",
                LegacyNavIndex = 5,
                Title = "病历归档",
                Icon = "📁",
                IsClinical = true,
                Description = "OFD 电子病历版式归档与完整性验签"
            });
            records.Items.Add(new ShellNavItem
            {
                Key = "emr_designer",
                LegacyNavIndex = -1,
                Title = "文书设计器",
                Icon = "📐",
                IsClinical = false,
                Description = "填写模式组件布局与打印模式纸张防超限文书设计"
            });
            records.Items.Add(new ShellNavItem
            {
                Key = "emr_qc",
                LegacyNavIndex = -1,
                Title = "病历质控",
                Icon = "🛡️",
                IsClinical = true,
                Description = "全过程时效性监控、内涵质控缺陷下发与整改复核闭环"
            });
            NavGroups.Add(records);
        }

        if (ctx.CanSeeAdminPanel)
        {
            var sys = new ShellNavGroup("system", "系统管理", isExpanded: false);
            if (ctx.CanSeeDynFormConfig)
            {
                sys.Items.Add(new ShellNavItem { Key = "dynamic_form", LegacyNavIndex = 6, Title = "动态表单配置", Icon = "🧩", Description = "专科扩展字段与表单模板设计" });
            }
            sys.Items.Add(new ShellNavItem { Key = "hr_management", LegacyNavIndex = -1, Title = "人力资源管理", Icon = "🪪", Description = "入职登记、人事档案修改、调科晋升、证照与合同管理" });
            sys.Items.Add(new ShellNavItem { Key = "user_admin", LegacyNavIndex = 7, Title = "用户账号维护", Icon = "👤", Description = "全院用户账号、科室岗位绑定" });
            sys.Items.Add(new ShellNavItem { Key = "role_admin", LegacyNavIndex = 8, Title = "角色与权限", Icon = "🔐", Description = "角色权限矩阵与数据隔离策略" });
            sys.Items.Add(new ShellNavItem { Key = "audit_log", LegacyNavIndex = 9, Title = "安全审计日志", Icon = "📜", Description = "关键操作不可篡改审计与链校验" });
            sys.Items.Add(new ShellNavItem { Key = "workflow_designer", LegacyNavIndex = -1, Title = "流程设计器", Icon = "🔀", Description = "低代码可视化状态机审批流设计与发布" });
            sys.Items.Add(new ShellNavItem { Key = "print_designer", LegacyNavIndex = -1, Title = "打印模板设计", Icon = "🖨️", Description = "自定义纸张长宽、拖拽条码单据排版与页面触发位" });
            sys.Items.Add(new ShellNavItem { Key = "master_catalog", LegacyNavIndex = -1, Title = "药品耗材与套餐字典", Icon = "📦", Description = "药品字典、耗材字典与医疗组合套餐（如肾功能全套）维护" });
            sys.Items.Add(new ShellNavItem { Key = "checkup", LegacyNavIndex = -1, Title = "健康体检管理", Icon = "🩺", Description = "个人/团体体检登记、套餐自动拆解、分科录入与主检总检" });
            sys.Items.Add(new ShellNavItem { Key = "medtech_report", LegacyNavIndex = -1, Title = "医技报告与审签", Icon = "📋", Description = "待书写/待审签队列、机器直出直审与分科室自定义报告模板" });
            sys.Items.Add(new ShellNavItem { Key = "pre_admission", LegacyNavIndex = -1, Title = "预住院管理中心", Icon = "🏥", Description = "医保预住院登记、P0危急值抢占中断、准入评估与复合费用拆分" });
            sys.Items.Add(new ShellNavItem { Key = "er_opd_pro", LegacyNavIndex = -1, Title = "急诊分诊与门诊增强", Icon = "🚑", Description = "急诊MEWS五级分诊抢救留观、临床优先级候诊、慢病续方与SM3证明书" });
            sys.Items.Add(new ShellNavItem { Key = "ipd_pro", LegacyNavIndex = -1, Title = "住院全流程与专科中心", Icon = "🛏️", Description = "图形化床位看板、MAR三测单、全院会诊、每日自动滚费、退药冲正、产科与日间手术" });
            sys.Items.Add(new ShellNavItem { Key = "medical_affairs", LegacyNavIndex = -1, Title = "医务管理中心", Icon = "⚖️", Description = "医师动态分级授权与停权、Ⅰ~Ⅳ级安全事件RCA/PDCA、医疗纠纷联动封存病案与新技术准入" });
            sys.Items.Add(new ShellNavItem { Key = "nursing_mgmt", LegacyNavIndex = -1, Title = "护理管理中心", Icon = "👩‍⚕️", Description = "护士能级N0-N4档案、病区安全约束排班与Acuity护患比测算、三级护理质控与SBAR双签交接" });
            sys.Items.Add(new ShellNavItem { Key = "critical_care_control", LegacyNavIndex = -1, Title = "危急重症质控塔(P0)", Icon = "🚨", Description = "急诊五大中心时间轴质控、ICU重症CIS(APACHE II/SOFA/三管)、DRG事中实时控费、抗菌药DDDs与PIVAS静配" });
            sys.Items.Add(new ShellNavItem { Key = "public_health_supply", LegacyNavIndex = -1, Title = "公卫随访与UDI营养(P1)", Icon = "🛡️", Description = "公卫ICD-10多报卡强触发、院后专病随访与30天再入院追踪、高值耗材UDI消耗记账代销对账、NRS-2002营养配餐" });
            sys.Items.Add(new ShellNavItem { Key = "aims", LegacyNavIndex = -1, Title = "手术麻醉中心 (AIMS)", Icon = "🩺", Description = "手术排台急诊插台、术前访视极量计算、术中出入量/MH应急、毒麻空安瓿双人销毁、PACU门禁与PCA随访" });
            sys.Items.Add(new ShellNavItem { Key = "master_dict", LegacyNavIndex = -1, Title = "主数据字典中心", Icon = "📖", Description = "全院标准计量单位与临床主数据字典维护，供动态表单直接引用限制输入" });
            sys.Items.Add(new ShellNavItem { Key = "mrms", LegacyNavIndex = -1, Title = "病案室与临床编码", Icon = "📚", Description = "出院病案回收、ICD-10/ICD-9编码、DRG预分组、归档封存与借阅复印" });
            NavGroups.Add(sys);
        }

        RefreshNavBadges();

        var allItems = NavGroups.SelectMany(g => g.Items).ToList();
        var target = (prevKey != null ? allItems.FirstOrDefault(i => i.Key == prevKey) : null)
                     ?? allItems.FirstOrDefault(i => i.Key == "workbench")
                     ?? allItems.FirstOrDefault();

        if (target != null)
        {
            ActivateNavItem(target);
        }
    }

    public void RefreshNavBadges()
    {
        foreach (var item in NavGroups.SelectMany(g => g.Items))
        {
            item.BadgeCount = item.Key switch
            {
                "opd" => Workbench.WaitingPatientCount,
                "ipd_doctor" => Workbench.PendingOrderReviewCount,
                "ipd_nurse" => Workbench.WaitingDecomposeTotalCount,
                "medtech" => Workbench.PendingCriticalValueCount,
                "pharmacy_window" => Workbench.PendingPharmacyDispenseCount,
                "finance" => Workbench.PendingFinanceSettleCount + Workbench.PendingFinancialRefundCount,
                "workbench" => ComputeTotalRoleTodos(),
                _ => 0
            };
        }
    }

    private int ComputeTotalRoleTodos()
    {
        var ctx = SessionContext.Current;
        var sum = Workbench.PendingCriticalValueCount;
        if (ctx.CanSeeOpdStation) sum += Workbench.WaitingPatientCount;
        if (ctx.CanSeeIpdStation) sum += Workbench.PendingOrderReviewCount;
        if (ctx.CanSeeWardStation) sum += Workbench.WaitingDecomposeTotalCount;
        if (ctx.CanSeePharmacy) sum += Workbench.PendingPharmacyDispenseCount;
        if (ctx.CanSeeFinance) sum += Workbench.PendingFinanceSettleCount + Workbench.PendingFinancialRefundCount;
        return sum;
    }

    public void NavigateByKey(string key)
    {
        var item = NavGroups.SelectMany(g => g.Items).FirstOrDefault(i => i.Key == key);
        if (item != null)
        {
            ActivateNavItem(item);
        }
    }

    public void ActivateNavItem(ShellNavItem item)
    {
        DeactivateCurrentStation();
        ActiveNavItem = item;
        if (item.LegacyNavIndex >= 0)
        {
            Workbench.SelectedNavIndex = item.LegacyNavIndex;
        }
        CurrentStationView = ResolveStationView(item);
        if (CurrentStationView is Control { DataContext: IStationLifecycle lifecycle })
        {
            _stationLifecycles[item.Key] = lifecycle;
            lifecycle.Activate();
        }
        RefreshBannerBindings();
        ActiveNavChanged?.Invoke();
    }

    private object ResolveStationView(ShellNavItem item)
    {
        if (_stationViewCache.TryGetValue(item.Key, out var cached))
        {
            return cached;
        }

        Control view = item.Key switch
        {
            "workbench" => new Stations.Workbench.WorkbenchHomeView { DataContext = new Stations.Workbench.WorkbenchHomeViewModel(this) },
            "opd" => new Stations.OpdDoctor.OpdDoctorView { DataContext = new Stations.OpdDoctor.OpdDoctorViewModel(Workbench, this) },
            "ipd_doctor" => new Stations.IpdDoctor.IpdDoctorView { DataContext = new Stations.IpdDoctor.IpdDoctorViewModel(Workbench, this) },
            "ipd_nurse" => new Stations.IpdNurse.IpdNurseView { DataContext = new Stations.IpdNurse.IpdNurseViewModel(Workbench, this) },
            "medtech" => new Stations.MedTech.MedTechStationView { DataContext = new Stations.MedTech.MedTechStationViewModel(Workbench, this) },
            "pharmacy_window" => new Stations.PharmacyWindow.PharmacyWindowView { DataContext = new Stations.PharmacyWindow.PharmacyWindowViewModel(Workbench, this) },
            "pharmacy_warehouse" => new Stations.PharmacyWarehouse.PharmacyWarehouseView { DataContext = new Stations.PharmacyWarehouse.PharmacyWarehouseViewModel(Workbench, this) },
            "registration" => new Stations.Registration.RegistrationStationView { DataContext = new Stations.Registration.RegistrationStationViewModel(Workbench, this) },
            "finance" => new Stations.Finance.FinanceStationView { DataContext = new Stations.Finance.FinanceStationViewModel(Workbench, this) },
            "emr_archive" => new Stations.EmrArchive.EmrArchiveView { DataContext = new Stations.EmrArchive.EmrArchiveViewModel(Workbench, this) },
            "emr_designer" => new Stations.EmrDesigner.EmrDesignerStationView { DataContext = new Stations.EmrDesigner.EmrDesignerStationViewModel(Workbench, this) },
            "dynamic_form" => new Stations.Admin.DynamicFormStationView { DataContext = new Stations.Admin.DynamicFormStationViewModel(Workbench, this) },
            "hr_management" => new Stations.Hr.HrStationView { DataContext = new Stations.Hr.HrStationViewModel(Workbench, this) },
            "user_admin" => new Stations.Admin.UserAdminStationView { DataContext = new Stations.Admin.UserAdminStationViewModel(Workbench, this) },
            "role_admin" => new Stations.Admin.RoleAdminStationView { DataContext = new Stations.Admin.RoleAdminStationViewModel(Workbench, this) },
            "audit_log" => new Stations.Admin.AuditLogStationView { DataContext = new Stations.Admin.AuditLogStationViewModel(Workbench, this) },
            "schedule" => new Stations.Schedule.ScheduleStationView { DataContext = new Stations.Schedule.ScheduleStationViewModel(Workbench, this) },
            "consumables_warehouse" => new Stations.Consumables.ConsumablesWarehouseView { DataContext = new Stations.Consumables.ConsumablesWarehouseViewModel(Workbench, this) },
            "consumables_ward" => new Stations.Consumables.ConsumablesWardView { DataContext = new Stations.Consumables.ConsumablesWardViewModel(Workbench, this) },
            "analytics" => new Stations.Analytics.AnalyticsStationView { DataContext = new Stations.Analytics.AnalyticsStationViewModel(Workbench, this) },
            "treatment" => new Stations.Treatment.TreatmentStationView { DataContext = new Stations.Treatment.TreatmentStationViewModel(Workbench, this) },
            "monitoring" => new Stations.Monitoring.MonitoringStationView { DataContext = new Stations.Monitoring.MonitoringStationViewModel(Workbench, this) },
            "blood_bank" => new Stations.BloodBank.BloodBankStationView { DataContext = new Stations.BloodBank.BloodBankStationViewModel(Workbench, this) },
            "emr_qc" => new Stations.EmrQc.EmrQcStationView { DataContext = new Stations.EmrQc.EmrQcStationViewModel(Workbench, this) },
            "workflow_designer" => new Stations.WorkflowDesigner.WorkflowDesignerStationView { DataContext = new Stations.WorkflowDesigner.WorkflowDesignerViewModel(Workbench, this) },
            "print_designer" => new Stations.PrintDesigner.PrintDesignerStationView { DataContext = new Stations.PrintDesigner.PrintDesignerViewModel(Workbench, this) },
            "master_catalog" => new Stations.MasterCatalog.MasterCatalogStationView { DataContext = new Stations.MasterCatalog.MasterCatalogViewModel(Workbench, this) },
            "checkup" => new Stations.Checkup.CheckupStationView { DataContext = new Stations.Checkup.CheckupStationViewModel(Workbench, this) },
            "medtech_report" => new Stations.MedTechReport.MedTechReportStationView { DataContext = new Stations.MedTechReport.MedTechReportViewModel(Workbench, this) },
            "pre_admission" => new Stations.PreAdmission.PreAdmissionStationView { DataContext = new Stations.PreAdmission.PreAdmissionViewModel(Workbench, this) },
            "er_opd_pro" => new Stations.Emergency.EmergencyStationView { DataContext = new Stations.Emergency.EmergencyViewModel(Workbench, this) },
            "ipd_pro" => new Stations.InpatientPro.InpatientProStationView { DataContext = new Stations.InpatientPro.InpatientProViewModel(Workbench, this) },
            "medical_affairs" => new Stations.MedicalAffairs.MedicalAffairsStationView { DataContext = new Stations.MedicalAffairs.MedicalAffairsViewModel(Workbench, this) },
            "nursing_mgmt" => new Stations.NursingMgmt.NursingMgmtStationView { DataContext = new Stations.NursingMgmt.NursingMgmtViewModel(Workbench, this) },
            "critical_care_control" => new Stations.CriticalCareControl.CriticalCareControlStationView { DataContext = new Stations.CriticalCareControl.CriticalCareControlViewModel(Workbench, this) },
            "public_health_supply" => new Stations.PublicHealthSupply.PublicHealthSupplyStationView { DataContext = new Stations.PublicHealthSupply.PublicHealthSupplyViewModel(Workbench, this) },
            "aims" => new Stations.Aims.AimsStationView { DataContext = new Stations.Aims.AimsViewModel(Workbench, this) },
            "master_dict" => new Stations.MasterDict.MasterDictStationView { DataContext = new Stations.MasterDict.MasterDictViewModel(Workbench, this) },
            "mrms" => new Stations.Mrms.MrmsStationView { DataContext = new Stations.Mrms.MrmsViewModel(Workbench, this) },
            "workflow_inbox" => new Stations.WorkflowInbox.WorkflowInboxStationView { DataContext = new Stations.WorkflowInbox.WorkflowInboxViewModel(Workbench, this) },
            _ => new LegacyStationBridgeView(
                item.Title,
                item.Description,
                () => _openLegacyWindowCallback?.Invoke(Math.Max(0, item.LegacyNavIndex)))
        };

        if (view is Stations.Workbench.WorkbenchHomeView wbView && wbView.DataContext is Stations.Workbench.WorkbenchHomeViewModel wbVm)
        {
            wbVm.Refresh();
        }

        _stationViewCache[item.Key] = view;
        if (view is Control { DataContext: IStationLifecycle lifecycle })
        {
            _stationLifecycles[item.Key] = lifecycle;
        }
        return view;
    }

    private void DeactivateCurrentStation()
    {
        if (ActiveNavItem != null
            && _stationLifecycles.TryGetValue(ActiveNavItem.Key, out var lifecycle))
        {
            lifecycle.Deactivate();
        }
    }

    public void RefreshGlobalSearchResults()
    {
        GlobalSearchResults.Clear();
        var q = GlobalSearchQuery?.Trim() ?? string.Empty;

        var opdMatches = Workbench.PatientQueue
            .Where(p => string.IsNullOrEmpty(q) ||
                        p.PatientName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        p.VisitNo.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        p.MedicalRecordNo.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Take(5);

        foreach (var p in opdMatches)
        {
            GlobalSearchResults.Add(new GlobalPatientHit
            {
                ScopeLabel = "门诊",
                DisplayTitle = $"{p.PatientName} ({p.Gender} · {p.Age}岁)",
                Subtitle = $"流水号 {p.VisitNo} · {p.Status}",
                IsInpatient = false,
                PatientModel = p
            });
        }

        var ipdMatches = Workbench.InpatientRecords
            .Where(i => string.IsNullOrEmpty(q) ||
                        i.PatientName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        i.AdmissionNo.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        i.BedCode.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Take(5);

        foreach (var i in ipdMatches)
        {
            GlobalSearchResults.Add(new GlobalPatientHit
            {
                ScopeLabel = "住院",
                DisplayTitle = $"{i.PatientName} ({i.BedCode})",
                Subtitle = $"住院号 {i.AdmissionNo} · {i.Diagnosis}",
                IsInpatient = true,
                PatientModel = i
            });
        }
    }

    public void SelectGlobalPatient(GlobalPatientHit hit)
    {
        if (hit.IsInpatient && hit.PatientModel is InpatientRecordModel ipd)
        {
            Workbench.SelectedInpatient = ipd;
            if (ActiveNavItem?.Key != "ipd_nurse" && SessionContext.Current.CanSeeIpdStation)
            {
                NavigateByKey("ipd_doctor");
            }
            ShowToast($"已切换当前住院患者: {ipd.PatientName} ({ipd.BedCode})", "Info");
        }
        else if (!hit.IsInpatient && hit.PatientModel is PatientQueueItemModel opd)
        {
            Workbench.SelectedPatient = opd;
            if (SessionContext.Current.CanSeeOpdStation)
            {
                NavigateByKey("opd");
            }
            ShowToast($"已切换当前门诊患者: {opd.PatientName}", "Info");
        }

        IsSearchDropdownOpen = false;
        GlobalSearchQuery = string.Empty;
        RefreshBannerBindings();
    }

    public void OpenTimelineDrawer()
    {
        DrawerTitle = $"患者 360° 临床时间轴 · {BannerPatientName}";
        DrawerSubtitle = BannerIdentifier;
        DrawerWidth = 560;
        DrawerContent = CreateTimelineDrawerView();
        IsDrawerOpen = true;
    }

    public void OpenCriticalValueDrawer()
    {
        DrawerTitle = "危急值预警与双人签收";
        DrawerSubtitle = $"{Workbench.CriticalValueAlert.PatientName} · {Workbench.CriticalValueAlert.TestItem}";
        DrawerWidth = 500;
        DrawerContent = CreateCriticalValueDrawerView();
        IsDrawerOpen = true;
    }

    public void OpenContextSwitchDrawer()
    {
        DrawerTitle = "切换工作岗位与科室上下文";
        DrawerSubtitle = $"{Workbench.CurrentOperatorName} · 当前: {Workbench.CurrentRoleDisplayName}";
        DrawerWidth = 460;
        DrawerContent = CreateContextSwitchDrawerView();
        IsDrawerOpen = true;
    }

    public void OpenDrawer(string title, string? subtitle, object content, double width = 520)
    {
        DrawerTitle = title;
        DrawerSubtitle = subtitle;
        DrawerWidth = width;
        DrawerContent = content;
        IsDrawerOpen = true;
    }

    public void CloseDrawer()
    {
        IsDrawerOpen = false;
        DrawerContent = null;
    }

    public void RequestConfirm(string title, string message, string confirmButtonText, Action onConfirmed)
    {
        ConfirmTitle = title;
        ConfirmMessage = message;
        ConfirmButtonText = confirmButtonText;
        _confirmCallback = onConfirmed;
        IsConfirmOpen = true;
    }

    public void CloseConfirm()
    {
        IsConfirmOpen = false;
        _confirmCallback = null;
    }

    public void ShowToast(
        string message,
        string severity = "Info",
        string? actionText = null,
        Action? onAction = null,
        double durationSeconds = 4.5)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        var id = Guid.NewGuid().ToString("N");
        var toast = new ToastMessage
        {
            Id = id,
            Message = message,
            Severity = severity,
            Timestamp = DateTime.Now.ToString("HH:mm:ss"),
            ActionText = actionText,
            ActionCommand = onAction == null ? null : new RelayCommand(() =>
            {
                RemoveToast(id);
                onAction();
            }),
            DismissCommand = new RelayCommand(() => RemoveToast(id))
        };

        Toasts.Insert(0, toast);
        while (Toasts.Count > 3)
        {
            RemoveToast(Toasts[Toasts.Count - 1].Id);
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(durationSeconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            RemoveToast(id);
        };
        _toastTimers[id] = (timer, toast);
        timer.Start();
    }

    private void RemoveToast(string id)
    {
        if (_toastTimers.Remove(id, out var entry))
        {
            entry.Timer.Stop();
        }

        var existing = Toasts.FirstOrDefault(t => t.Id == id);
        if (existing != null)
        {
            Toasts.Remove(existing);
        }
    }

    private Control CreateTimelineDrawerView()
    {
        var stack = new StackPanel { Spacing = 10 };
        foreach (var ev in Workbench.TimelineEvents)
        {
            var card = new Border
            {
                Padding = new Avalonia.Thickness(12),
                CornerRadius = new Avalonia.CornerRadius(8),
                BorderThickness = new Avalonia.Thickness(1)
            };
            card[!Border.BackgroundProperty] = new Avalonia.Data.Binding { Source = Avalonia.Application.Current?.FindResource("SemiColorBackground1") };
            card[!Border.BorderBrushProperty] = new Avalonia.Data.Binding { Source = Avalonia.Application.Current?.FindResource("SemiColorBorder") };

            var inner = new StackPanel { Spacing = 4 };
            inner.Children.Add(new TextBlock { Text = $"{ev.ResultIcon} {ev.EventTitle}", FontWeight = Avalonia.Media.FontWeight.Bold, FontSize = 13 });
            inner.Children.Add(new TextBlock { Text = $"{ev.EventTime} · {ev.Department} · {ev.DoctorOrOperator}", FontSize = 11 });
            inner.Children.Add(new TextBlock { Text = ev.Summary, FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            card.Child = inner;
            stack.Children.Add(card);
        }
        return stack;
    }

    private Control CreateCriticalValueDrawerView()
    {
        var alert = Workbench.CriticalValueAlert;
        var root = new StackPanel { Spacing = 14, DataContext = alert };

        var summary = new Border
        {
            Padding = new Avalonia.Thickness(14),
            CornerRadius = new Avalonia.CornerRadius(8),
            BorderThickness = new Avalonia.Thickness(1)
        };
        if (Avalonia.Application.Current?.TryGetResource("SemiColorDangerLight", null, out var bg) == true && bg is Avalonia.Media.IBrush bgBrush)
        {
            summary.Background = bgBrush;
        }
        if (Avalonia.Application.Current?.TryGetResource("SemiColorDanger", null, out var bd) == true && bd is Avalonia.Media.IBrush bdBrush)
        {
            summary.BorderBrush = bdBrush;
        }

        var sInner = new StackPanel { Spacing = 4 };
        sInner.Children.Add(new TextBlock { Text = $"{alert.AlertLevel} · {alert.TestItem}", FontSize = 15, FontWeight = Avalonia.Media.FontWeight.Bold });
        sInner.Children.Add(new TextBlock { Text = $"测定值: {alert.MeasuredValue} (参考范围: {alert.ReferenceRange})", FontSize = 13, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        sInner.Children.Add(new TextBlock { Text = $"报告科室: {alert.ReportingDept} · {alert.ReportingTime}", FontSize = 11 });
        summary.Child = sInner;
        root.Children.Add(summary);

        root.Children.Add(new TextBlock { Text = alert.ClinicalAction, FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap });

        var docStatus = new TextBlock { FontSize = 12, FontWeight = Avalonia.Media.FontWeight.SemiBold };
        docStatus[!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(CriticalValueAlertModel.DoctorAck)) { StringFormat = "医生签收: {0}" };
        root.Children.Add(docStatus);

        var docBtn = new Button { Content = "经治医生首接签收", Height = 34, Command = DoctorAckCriticalCommand };
        docBtn.Classes.Add("Primary");
        docBtn[!Button.IsEnabledProperty] = new Avalonia.Data.Binding(nameof(CriticalValueAlertModel.CanDoctorAck));
        root.Children.Add(docBtn);

        var nurseStatus = new TextBlock { FontSize = 12, FontWeight = Avalonia.Media.FontWeight.SemiBold };
        nurseStatus[!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(CriticalValueAlertModel.NurseAck)) { StringFormat = "护士复核: {0}" };
        root.Children.Add(nurseStatus);

        var nurseBtn = new Button { Content = "责任护士复核签收", Height = 34, Command = NurseAckCriticalCommand };
        nurseBtn.Classes.Add("Warning");
        nurseBtn[!Button.IsEnabledProperty] = new Avalonia.Data.Binding(nameof(CriticalValueAlertModel.CanNurseAck));
        root.Children.Add(nurseBtn);

        return root;
    }

    private Control CreateContextSwitchDrawerView()
    {
        var stack = new StackPanel { Spacing = 10 };
        if (Workbench.AvailableContexts.Count == 0)
        {
            stack.Children.Add(new TextBlock { Text = "当前账号仅绑定单一工作岗位与科室上下文。", FontSize = 12 });
            return stack;
        }

        foreach (var ctx in Workbench.AvailableContexts)
        {
            var btn = new Button
            {
                Content = $"{ctx.DisplayName} · {ctx.RoleName} ({ctx.DeptName})",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                Height = 40,
                Command = SwitchToContextCommand,
                CommandParameter = ctx.PostId
            };
            if (ctx.IsSelected) btn.Classes.Add("Primary");
            stack.Children.Add(btn);
        }
        return stack;
    }

    private void OnWorkbenchNoticeRaised(NoticeHistoryItem item)
    {
        ShowToast(item.Message, item.Severity);
    }

    private void OnWorkbenchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkbenchModel.SelectedPatient)
            or nameof(WorkbenchModel.SelectedInpatient)
            or nameof(WorkbenchModel.HasSelectedPatientAllergy)
            or nameof(WorkbenchModel.SelectedPatientAllergyText))
        {
            RefreshBannerBindings();
        }

        if (e.PropertyName is nameof(WorkbenchModel.WaitingPatientCount)
            or nameof(WorkbenchModel.PendingOrderReviewCount)
            or nameof(WorkbenchModel.WaitingDecomposeTotalCount)
            or nameof(WorkbenchModel.PendingPharmacyDispenseCount)
            or nameof(WorkbenchModel.PendingFinancialRefundCount))
        {
            RefreshNavBadges();
        }
    }

    private void RefreshBannerBindings()
    {
        OnPropertyChanged(nameof(BannerHasPatient));
        OnPropertyChanged(nameof(BannerEncounterBadge));
        OnPropertyChanged(nameof(BannerPatientName));
        OnPropertyChanged(nameof(BannerGenderAndAge));
        OnPropertyChanged(nameof(BannerIdentifier));
        OnPropertyChanged(nameof(BannerDiagnosis));
        OnPropertyChanged(nameof(BannerHasAllergy));
        OnPropertyChanged(nameof(BannerAllergyText));
    }

    public void OpenAlertHistoryDrawer()
    {
        UnreadAlertCount = 0;
        OpenDrawer("临床实时告警中心 (SignalR)", $"实时推送通道: /hubs/clinical-alerts (累计接收: {AlertHistory.Count} 条)", CreateAlertHistoryDrawerView(), 520);
    }

    private Control CreateAlertHistoryDrawerView()
    {
        var root = new StackPanel { Spacing = 12 };

        var topRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var summaryText = new TextBlock
        {
            Text = $"实时告警消息流 (当前: {(IsAlertHubConnected ? "已连接" : "离线")})",
            FontSize = 13,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        topRow.Children.Add(summaryText);

        var clearBtn = new Button
        {
            Content = "清空历史",
            Height = 28,
            Command = ClearAlertHistoryCommand
        };
        clearBtn.Classes.Add("Tertiary");
        Grid.SetColumn(clearBtn, 1);
        topRow.Children.Add(clearBtn);
        root.Children.Add(topRow);

        var scroll = new ScrollViewer { MaxHeight = 650 };
        var listStack = new StackPanel { Spacing = 8 };

        if (AlertHistory.Count == 0)
        {
            var empty = new Border
            {
                Padding = new Avalonia.Thickness(20),
                CornerRadius = new Avalonia.CornerRadius(8),
                Background = Avalonia.Media.Brushes.Transparent
            };
            empty.Child = new TextBlock
            {
                Text = "暂无未确认或新到达的临床实时告警。",
                FontSize = 12,
                Foreground = Avalonia.Media.Brushes.Gray,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };
            listStack.Children.Add(empty);
        }
        else
        {
            foreach (var a in AlertHistory)
            {
                var card = new Border
                {
                    Padding = new Avalonia.Thickness(12),
                    CornerRadius = new Avalonia.CornerRadius(8),
                    BorderThickness = new Avalonia.Thickness(1)
                };
                card[!Border.BackgroundProperty] = new Avalonia.Data.Binding { Source = Avalonia.Application.Current?.FindResource("SemiColorBackground1") };
                card[!Border.BorderBrushProperty] = new Avalonia.Data.Binding { Source = Avalonia.Application.Current?.FindResource("SemiColorBorder") };

                var inner = new StackPanel { Spacing = 4 };
                var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                headerGrid.Children.Add(new TextBlock { Text = a.Title, FontWeight = Avalonia.Media.FontWeight.Bold, FontSize = 13 });
                var timeBlock = new TextBlock { Text = a.TimeText, FontSize = 11, Foreground = Avalonia.Media.Brushes.Gray };
                Grid.SetColumn(timeBlock, 1);
                headerGrid.Children.Add(timeBlock);
                inner.Children.Add(headerGrid);

                inner.Children.Add(new TextBlock { Text = $"患者/对象: {a.PatientName}", FontSize = 12, FontWeight = Avalonia.Media.FontWeight.SemiBold });
                inner.Children.Add(new TextBlock { Text = a.Message, FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                card.Child = inner;
                listStack.Children.Add(card);
            }
        }

        scroll.Content = listStack;
        root.Children.Add(scroll);
        return root;
    }

    private void StartAlertHub()
    {
        ClinicalAlertHubClient.Current.ConnectionStateChanged -= OnAlertHubConnectionChanged;
        ClinicalAlertHubClient.Current.ConnectionStateChanged += OnAlertHubConnectionChanged;

        ClinicalAlertHubClient.Current.OnCriticalValue -= HandleCriticalValue;
        ClinicalAlertHubClient.Current.OnCriticalValue += HandleCriticalValue;

        ClinicalAlertHubClient.Current.OnOrderChanged -= HandleOrderChanged;
        ClinicalAlertHubClient.Current.OnOrderChanged += HandleOrderChanged;

        ClinicalAlertHubClient.Current.OnTimelinessAlert -= HandleTimelinessAlert;
        ClinicalAlertHubClient.Current.OnTimelinessAlert += HandleTimelinessAlert;

        ClinicalAlertHubClient.Current.OnMonitoringAlarm -= HandleMonitoringAlarm;
        ClinicalAlertHubClient.Current.OnMonitoringAlarm += HandleMonitoringAlarm;

        ClinicalAlertHubClient.Current.OnPrescriptionComment -= HandlePrescriptionComment;
        ClinicalAlertHubClient.Current.OnPrescriptionComment += HandlePrescriptionComment;

        ClinicalAlertHubClient.Current.OnCarePlanReminder -= HandleCarePlanReminder;
        ClinicalAlertHubClient.Current.OnCarePlanReminder += HandleCarePlanReminder;

        ClinicalAlertHubClient.Current.Start();
        IsAlertHubConnected = ClinicalAlertHubClient.Current.IsConnected;
    }

    private void StopAlertHub()
    {
        ClinicalAlertHubClient.Current.ConnectionStateChanged -= OnAlertHubConnectionChanged;
        ClinicalAlertHubClient.Current.OnCriticalValue -= HandleCriticalValue;
        ClinicalAlertHubClient.Current.OnOrderChanged -= HandleOrderChanged;
        ClinicalAlertHubClient.Current.OnTimelinessAlert -= HandleTimelinessAlert;
        ClinicalAlertHubClient.Current.OnMonitoringAlarm -= HandleMonitoringAlarm;
        ClinicalAlertHubClient.Current.OnPrescriptionComment -= HandlePrescriptionComment;
        ClinicalAlertHubClient.Current.OnCarePlanReminder -= HandleCarePlanReminder;

        ClinicalAlertHubClient.Current.Stop();
        IsAlertHubConnected = false;
    }

    private void OnAlertHubConnectionChanged(bool connected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsAlertHubConnected = connected;
        });
    }

    private void HandleCriticalValue(ClinicalAlertPayload payload)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UnreadAlertCount++;
            var item = new ClinicalAlertRecordItem
            {
                EventType = "CRITICAL_VALUE",
                Title = "⚠ 检验危急值急迫告警",
                PatientName = payload.PatientName ?? "急救/病区患者",
                Message = $"{payload.Item ?? "检验项"}: {payload.Value ?? "超出极值"} - {payload.Message ?? "需经治医师在 10 分钟内双人签收闭环"}",
                Severity = "Danger",
                TimeText = DateTime.Now.ToString("HH:mm:ss")
            };
            AlertHistory.Insert(0, item);
            Workbench.CriticalValueAlert.PatientName = item.PatientName;
            Workbench.CriticalValueAlert.TestItem = $"{payload.Item}: {payload.Value}";
            Workbench.CriticalValueAlert.DoctorAck = "待经治医师签收";
            Workbench.CriticalValueAlert.NurseAck = "待病区护士复核";
            ShowToast($"【危急值突发强告警】{item.PatientName} - {item.Message}", "Danger", "立即签收", () => OpenCriticalValueDrawer(), 15);
            OpenCriticalValueDrawer();
        });
    }

    private void HandleOrderChanged(ClinicalAlertPayload payload)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UnreadAlertCount++;
            var item = new ClinicalAlertRecordItem
            {
                EventType = "ORDER_CHANGED",
                Title = "📋 医嘱状态变更通知",
                PatientName = payload.PatientName ?? "在院患者",
                Message = payload.Message ?? $"{payload.Item}: 医嘱已更新状态",
                Severity = "Info",
                TimeText = DateTime.Now.ToString("HH:mm:ss")
            };
            AlertHistory.Insert(0, item);
            ShowToast($"【医嘱变更】{item.PatientName}：{item.Message}", "Info");
        });
    }

    private void HandleTimelinessAlert(ClinicalAlertPayload payload)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UnreadAlertCount++;
            var item = new ClinicalAlertRecordItem
            {
                EventType = "TIMELINESS_ALERT",
                Title = "⏰ 电子病历书写时效性预警",
                PatientName = payload.PatientName ?? "在院患者",
                Message = payload.Message ?? $"{payload.Item} 接近书写时限，请及时完成！",
                Severity = "Warning",
                TimeText = DateTime.Now.ToString("HH:mm:ss")
            };
            AlertHistory.Insert(0, item);
            ShowToast($"【时效预警】{item.PatientName}：{item.Message}", "Warning");
        });
    }

    private void HandleMonitoringAlarm(ClinicalAlertPayload payload)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UnreadAlertCount++;
            var item = new ClinicalAlertRecordItem
            {
                EventType = "MONITORING_ALARM",
                Title = $"🚨 ICU 床旁监护仪报警 ({payload.BedCode ?? "监护床"})",
                PatientName = payload.PatientName ?? "重症监护患者",
                Message = payload.Message ?? $"{payload.Item ?? "生理体征"} 异常: {payload.Value}",
                Severity = "Danger",
                TimeText = DateTime.Now.ToString("HH:mm:ss")
            };
            AlertHistory.Insert(0, item);
            ShowToast($"【监护报警】床位 {payload.BedCode} ({item.PatientName})：{item.Message}", "Danger", durationSeconds: 12);
        });
    }

    private void HandlePrescriptionComment(ClinicalAlertPayload payload)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UnreadAlertCount++;
            var item = new ClinicalAlertRecordItem
            {
                EventType = "PRESCRIPTION_COMMENT",
                Title = "💊 临床药师前置点评与不合理用药拦截",
                PatientName = payload.PatientName ?? "门诊/住院患者",
                Message = payload.Message ?? $"{payload.Item}: 处方经药师点评存在用药风险，请确认",
                Severity = "Warning",
                TimeText = DateTime.Now.ToString("HH:mm:ss")
            };
            AlertHistory.Insert(0, item);
            ShowToast($"【处方点评】{item.PatientName}：{item.Message}", "Warning");
        });
    }

    private void HandleCarePlanReminder(ClinicalAlertPayload payload)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UnreadAlertCount++;
            var item = new ClinicalAlertRecordItem
            {
                EventType = "CARE_PLAN_REMINDER",
                Title = "🩺 责任护理计划执行与巡视提醒",
                PatientName = payload.PatientName ?? "病区患者",
                Message = payload.Message ?? $"{payload.Item}: 护理评估/给药计划已到预定执行时间",
                Severity = "Info",
                TimeText = DateTime.Now.ToString("HH:mm:ss")
            };
            AlertHistory.Insert(0, item);
            ShowToast($"【护理提醒】{item.PatientName}：{item.Message}", "Info");
        });
    }

    public void Dispose()
    {
        StopAlertHub();
        DeactivateCurrentStation();
        Workbench.PropertyChanged -= OnWorkbenchPropertyChanged;
        Workbench.NoticeRaised -= OnWorkbenchNoticeRaised;

        foreach (var lifecycle in _stationLifecycles.Values.Distinct())
        {
            lifecycle.Dispose();
        }
        _stationLifecycles.Clear();
        _stationViewCache.Clear();

        foreach (var entry in _toastTimers.Values)
        {
            entry.Timer.Stop();
        }
        _toastTimers.Clear();
        Toasts.Clear();
    }
}
