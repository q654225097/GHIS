using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.Workbench;

public sealed class WorkbenchHomeViewModel : ClientModelBase, IStationLifecycle
{
    private readonly ShellViewModel _shell;
    private bool _isDisposed;
    private string _primaryActionLabel = "打开工作台";
    private string _primaryActionHint = string.Empty;
    private string _roleWorkspaceTitle = "我的待办";
    private string _roleWorkspaceSubtitle = string.Empty;
    private string _todoSummary = string.Empty;
    private string _lastActionMessage = string.Empty;
    private int _todoCount;

    public WorkbenchHomeViewModel(ShellViewModel shell)
    {
        _shell = shell;
        Workbench = shell.Workbench;

        PrimaryActionCommand = new AsyncRelayCommand(ExecutePrimaryActionAsync);
        OpenTodoCommand = new AsyncRelayCommand(OpenTodoAsync);
        OpenCriticalDrawerCommand = _shell.OpenCriticalValueDrawerCommand;
        RefreshCommand = new RelayCommand(Refresh);

        Workbench.PropertyChanged += OnWorkbenchPropertyChanged;
        _shell.ActiveNavChanged += OnActiveNavChanged;
        Refresh();
    }

    public WorkbenchModel Workbench { get; }

    public ObservableCollection<WorkbenchTodoItem> RoleTodoItems { get; } = new();

    public ICommand PrimaryActionCommand { get; }
    public ICommand OpenTodoCommand { get; }
    public ICommand OpenCriticalDrawerCommand { get; }
    public ICommand RefreshCommand { get; }

    public string GreetingTitle => $"欢迎回来，{Workbench.CurrentOperatorName}";

    public string ContextSubtitle => $"{Workbench.CurrentRoleDisplayName} · {Workbench.ActiveContextSummary}";

    public string RoleWorkspaceTitle
    {
        get => _roleWorkspaceTitle;
        private set => SetField(ref _roleWorkspaceTitle, value, nameof(RoleWorkspaceTitle));
    }

    public string RoleWorkspaceSubtitle
    {
        get => _roleWorkspaceSubtitle;
        private set => SetField(ref _roleWorkspaceSubtitle, value, nameof(RoleWorkspaceSubtitle));
    }

    public string TodoSummary
    {
        get => _todoSummary;
        private set => SetField(ref _todoSummary, value, nameof(TodoSummary));
    }

    public string PrimaryActionLabel
    {
        get => _primaryActionLabel;
        private set => SetField(ref _primaryActionLabel, value, nameof(PrimaryActionLabel));
    }

    public string PrimaryActionHint
    {
        get => _primaryActionHint;
        private set => SetField(ref _primaryActionHint, value, nameof(PrimaryActionHint));
    }

    public string LastActionMessage
    {
        get => _lastActionMessage;
        private set => SetField(ref _lastActionMessage, value, nameof(LastActionMessage));
    }

    public int TodoCount
    {
        get => _todoCount;
        private set => SetField(ref _todoCount, value, nameof(TodoCount));
    }

    public bool HasTodos => TodoCount > 0;

    public bool IsAllClear => TodoCount == 0;

    public bool HasActionMessage => !string.IsNullOrWhiteSpace(LastActionMessage);

    public string PrimaryMetricValue => CurrentRoleKind() switch
    {
        RoleKind.Opd => Workbench.WaitingPatientCount.ToString(),
        RoleKind.IpdDoctor => Workbench.PendingOrderReviewCount.ToString(),
        RoleKind.WardNurse => Workbench.WaitingDecomposeTotalCount.ToString(),
        RoleKind.Pharmacist => Workbench.PendingPharmacyDispenseCount.ToString(),
        RoleKind.Cashier => Workbench.PendingFinanceSettleCount.ToString(),
        RoleKind.Admin => Workbench.UsersList.Count.ToString(),
        _ => "0"
    };

    public string PrimaryMetricLabel => CurrentRoleKind() switch
    {
        RoleKind.Opd => "人候诊",
        RoleKind.IpdDoctor => "条医嘱待校对",
        RoleKind.WardNurse => "条长嘱待分解",
        RoleKind.Pharmacist => "张处方待发药",
        RoleKind.Cashier => "笔处方待收费",
        RoleKind.Admin => "个系统账号",
        _ => "项待办"
    };

    public string SecondaryMetricValue => CurrentRoleKind() switch
    {
        RoleKind.Opd => Workbench.InProgressPatientCount.ToString(),
        RoleKind.IpdDoctor => Workbench.DischargeReadyCount.ToString(),
        RoleKind.WardNurse => Workbench.PendingNursingTaskCount.ToString(),
        RoleKind.Pharmacist => Workbench.ReturnedPharmacyOrderCount.ToString(),
        RoleKind.Cashier => Workbench.PendingFinancialRefundCount.ToString(),
        RoleKind.Admin => Workbench.RolesList.Count.ToString(),
        _ => "0"
    };

    public string SecondaryMetricLabel => CurrentRoleKind() switch
    {
        RoleKind.Opd => "人就诊中",
        RoleKind.IpdDoctor => "位患者待出院",
        RoleKind.WardNurse => "项护理待执行",
        RoleKind.Pharmacist => "张已退药",
        RoleKind.Cashier => "笔退费待审理",
        RoleKind.Admin => "个角色",
        _ => "项待办"
    };

    public void Refresh()
    {
        RoleTodoItems.Clear();
        var todos = CurrentRoleKind() switch
        {
            RoleKind.Opd => BuildOpdTodos(),
            RoleKind.IpdDoctor => BuildIpdDoctorTodos(),
            RoleKind.WardNurse => BuildWardNurseTodos(),
            RoleKind.Pharmacist => BuildPharmacyTodos(),
            RoleKind.Cashier => BuildCashierTodos(),
            RoleKind.Admin => BuildAdminTodos(),
            _ => BuildUnknownRoleTodos()
        };

        foreach (var todo in todos)
        {
            RoleTodoItems.Add(todo);
        }

        TodoCount = RoleTodoItems.Count;
        RoleWorkspaceTitle = CurrentRoleKind() switch
        {
            RoleKind.Opd => "门诊接诊待办",
            RoleKind.IpdDoctor => "住院诊疗待办",
            RoleKind.WardNurse => "病区护理待办",
            RoleKind.Pharmacist => "药房发药待办",
            RoleKind.Cashier => "收费结算待办",
            RoleKind.Admin => "系统管理待办",
            _ => "岗位待办"
        };
        RoleWorkspaceSubtitle = CurrentRoleKind() switch
        {
            RoleKind.Opd => "候诊患者优先完成接诊，已接诊患者可继续病历与处方",
            RoleKind.IpdDoctor => "按患者处理待校对医嘱、病程审签与出院申请",
            RoleKind.WardNurse => "按患者处理医嘱校对、长期医嘱分解与护理执行",
            RoleKind.Pharmacist => "已收费处方方可核验发药，退药单进入退药质检",
            RoleKind.Cashier => "处理未收费处方与已退药待退费业务",
            RoleKind.Admin => "账号、角色、表单配置与审计链维护",
            _ => "当前账号未匹配岗位，未生成跨角色业务待办"
        };

        ConfigurePrimaryAction();
        TodoSummary = TodoCount == 0
            ? "当前岗位没有待处理项"
            : CurrentRoleKind() switch
            {
                RoleKind.Opd => $"候诊 {Workbench.WaitingPatientCount} 人 · 就诊中 {Workbench.InProgressPatientCount} 人",
                RoleKind.IpdDoctor => $"待校对 {Workbench.PendingOrderReviewCount} 条 · 待出院 {Workbench.DischargeReadyCount} 人",
                RoleKind.WardNurse => $"待分解 {Workbench.WaitingDecomposeTotalCount} 条 · 待执行 {Workbench.PendingNursingTaskCount} 项",
                RoleKind.Pharmacist => $"待发药 {Workbench.PendingPharmacyDispenseCount} 张 · 已退药 {Workbench.ReturnedPharmacyOrderCount} 张",
                RoleKind.Cashier => $"待收费 {Workbench.PendingFinanceSettleCount} 张 · 待退费 {Workbench.PendingFinancialRefundCount} 笔",
                RoleKind.Admin => $"账号 {Workbench.UsersList.Count} 个 · 角色 {Workbench.RolesList.Count} 个",
                _ => "当前岗位没有可执行的待办"
            };

        OnPropertyChanged(nameof(HasTodos));
        OnPropertyChanged(nameof(IsAllClear));
        OnPropertyChanged(nameof(HasActionMessage));
        OnPropertyChanged(nameof(PrimaryMetricValue));
        OnPropertyChanged(nameof(PrimaryMetricLabel));
        OnPropertyChanged(nameof(SecondaryMetricValue));
        OnPropertyChanged(nameof(SecondaryMetricLabel));
        OnPropertyChanged(nameof(GreetingTitle));
        OnPropertyChanged(nameof(ContextSubtitle));
    }

    public void Activate()
    {
        Workbench.ApplyPatientQueueFilter();
        Workbench.RefreshInpatientMetrics();
        Workbench.RefreshPrescriptionMetrics();
        Workbench.RefreshPharmacyWarehouseMetrics();
        Refresh();
    }

    public void Deactivate()
    {
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Workbench.PropertyChanged -= OnWorkbenchPropertyChanged;
        _shell.ActiveNavChanged -= OnActiveNavChanged;
    }

    private void OnWorkbenchPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        Refresh();
    }

    private void OnActiveNavChanged()
    {
        Refresh();
    }

    private void ConfigurePrimaryAction()
    {
        switch (CurrentRoleKind())
        {
            case RoleKind.Opd:
                var waiting = Workbench.PatientQueue.FirstOrDefault(p => p.IsWaiting);
                var current = Workbench.PatientQueue.FirstOrDefault(p => p.IsInProgress);
                if (Workbench.HasActiveCriticalValue)
                {
                    PrimaryActionLabel = "查看危急值提醒";
                    PrimaryActionHint = "打开本会话的危急值提醒";
                }
                else if (waiting != null)
                {
                    PrimaryActionLabel = "叫号下一位并接诊";
                    PrimaryActionHint = waiting.PatientName;
                }
                else if (current != null)
                {
                    PrimaryActionLabel = "继续接诊";
                    PrimaryActionHint = current.PatientName;
                }
                else
                {
                    PrimaryActionLabel = "进入门诊医生站";
                    PrimaryActionHint = "门诊队列为空";
                }
                break;
            case RoleKind.IpdDoctor:
            case RoleKind.WardNurse:
                var firstIpd = FirstActionableInpatient();
                if (Workbench.HasActiveCriticalValue)
                {
                    PrimaryActionLabel = "查看危急值提醒";
                    PrimaryActionHint = "打开本会话的危急值提醒";
                }
                else if (firstIpd != null)
                {
                    PrimaryActionLabel = "打开下一项住院待办";
                    PrimaryActionHint = $"{firstIpd.PatientName} · {firstIpd.BedCode}";
                }
                else
                {
                    PrimaryActionLabel = CurrentRoleKind() == RoleKind.WardNurse ? "进入病区护士站" : "进入住院医生站";
                    PrimaryActionHint = "当前没有待处理患者";
                }
                break;
            case RoleKind.Pharmacist:
                PrimaryActionLabel = "进入窗口发药";
                PrimaryActionHint = Workbench.PendingPharmacyDispenseCount > 0
                    ? $"{Workbench.PendingPharmacyDispenseCount} 张处方待发药"
                    : "当前没有待发药处方";
                break;
            case RoleKind.Cashier:
                PrimaryActionLabel = "进入收费结算";
                PrimaryActionHint = Workbench.PendingFinanceSettleCount > 0
                    ? $"{Workbench.PendingFinanceSettleCount} 张处方待收费"
                    : "当前没有待收费处方";
                break;
            case RoleKind.Admin:
                PrimaryActionLabel = "进入账号维护";
                PrimaryActionHint = $"共 {Workbench.UsersList.Count} 个账号";
                break;
            default:
                PrimaryActionLabel = "打开系统首页";
                PrimaryActionHint = "当前账号没有匹配岗位";
                break;
        }
    }

    private async Task ExecutePrimaryActionAsync()
    {
        if (Workbench.HasActiveCriticalValue)
        {
            _shell.OpenCriticalValueDrawerCommand.Execute(null);
            LastActionMessage = "已打开危急值提醒";
            return;
        }

        switch (CurrentRoleKind())
        {
            case RoleKind.Opd:
                var waiting = Workbench.PatientQueue.FirstOrDefault(p => p.IsWaiting);
                if (waiting != null)
                {
                    var outcome = await Workbench.CallNextOpdPatientAsync(waiting);
                    LastActionMessage = outcome.Message;
                    if (outcome.Succeeded)
                    {
                        _shell.NavigateByKey("opd");
                        Refresh();
                    }
                }
                else
                {
                    _shell.NavigateByKey("opd");
                    LastActionMessage = "已进入门诊医生站";
                }
                break;
            case RoleKind.IpdDoctor:
                OpenNextInpatient("ipd_doctor");
                break;
            case RoleKind.WardNurse:
                OpenNextInpatient("ipd_nurse");
                break;
            case RoleKind.Pharmacist:
                _shell.NavigateByKey("pharmacy_window");
                LastActionMessage = "已进入窗口发药";
                break;
            case RoleKind.Cashier:
                _shell.NavigateByKey("finance");
                LastActionMessage = "已进入收费结算";
                break;
            case RoleKind.Admin:
                _shell.NavigateByKey("user_admin");
                LastActionMessage = "已进入账号维护";
                break;
            default:
                _shell.NavigateByKey("workbench");
                LastActionMessage = "当前账号没有匹配岗位";
                break;
        }
    }

    private void OpenNextInpatient(string navigationKey)
    {
        var patient = FirstActionableInpatient();
        if (patient != null)
        {
            Workbench.SelectedInpatient = patient;
            LastActionMessage = $"已打开 {patient.PatientName} · {patient.BedCode}";
        }
        _shell.NavigateByKey(navigationKey);
    }

    private async Task OpenTodoAsync(object? parameter)
    {
        if (parameter is not WorkbenchTodoItem todo)
        {
            return;
        }

        if (todo.IsCritical)
        {
            _shell.OpenCriticalValueDrawerCommand.Execute(null);
            LastActionMessage = "已打开危急值提醒";
            return;
        }

        if (todo.OpdPatient != null)
        {
            if (todo.OpdPatient.IsWaiting)
            {
                var outcome = await Workbench.CallNextOpdPatientAsync(todo.OpdPatient);
                LastActionMessage = outcome.Message;
                if (!outcome.Succeeded)
                {
                    return;
                }

                _shell.NavigateByKey("opd");
                Refresh();
                return;
            }

            await Workbench.SelectPatientAsync(todo.OpdPatient);
            _shell.NavigateByKey("opd");
            LastActionMessage = $"已打开 {todo.OpdPatient.PatientName} 的门诊病历与处方";
            return;
        }

        if (todo.Inpatient != null)
        {
            Workbench.SelectedInpatient = todo.Inpatient;
            _shell.NavigateByKey(todo.NavigationKey);
            LastActionMessage = $"已打开 {todo.Inpatient.PatientName} · {todo.Inpatient.BedCode}";
            return;
        }

        _shell.NavigateByKey(todo.NavigationKey);
        LastActionMessage = $"已进入{todo.Title}";
    }

    private InpatientRecordModel? FirstActionableInpatient()
    {
        return Workbench.InpatientRecords
            .Where(p => p.IsActiveInpatient)
            .OrderByDescending(p => p.IsCritical)
            .ThenByDescending(p => p.PendingOrderCount + p.WaitingDecomposeOrderCount + p.PendingNursingTaskCount + p.UnverifiedNursingTaskCount + p.UnsignedNoteCount)
            .FirstOrDefault();
    }

    private void AddCriticalValueTodo(List<WorkbenchTodoItem> items)
    {
        if (!Workbench.HasActiveCriticalValue) return;
        items.Add(new WorkbenchTodoItem
        {
            Title = "危急值待闭环",
            Context = Workbench.CurrentRoleDisplayName,
            Detail = $"{Workbench.PendingCriticalValueCount} 条可查看记录",
            Status = "危急",
            ActionLabel = "查看签收",
            NavigationKey = "critical_values",
            IsCritical = true
        });
    }

    private List<WorkbenchTodoItem> BuildOpdTodos()
    {
        var result = new List<WorkbenchTodoItem>();
        AddCriticalValueTodo(result);

        foreach (var patient in Workbench.PatientQueue.Where(p => !p.IsCompleted).Take(8))
        {
            result.Add(new WorkbenchTodoItem
            {
                Title = patient.IsWaiting ? "候诊待接诊" : "就诊中",
                Context = patient.PatientName,
                Detail = patient.IsWaiting
                    ? $"#{patient.QueueNo} · {patient.ChiefComplaint}"
                    : patient.StepProgressText,
                Status = patient.Status,
                ActionLabel = patient.IsWaiting ? "接诊" : "继续",
                NavigationKey = "opd",
                OpdPatient = patient
            });
        }
        return result;
    }

    private List<WorkbenchTodoItem> BuildIpdDoctorTodos()
    {
        var result = new List<WorkbenchTodoItem>();
        AddCriticalValueTodo(result);

        foreach (var patient in Workbench.InpatientRecords.Where(p => p.IsActiveInpatient))
        {
            if (patient.UnsignedNoteCount > 0)
            {
                result.Add(BuildIpdTodo(patient, "病程待审签", $"{patient.UnsignedNoteCount} 份病程记录待主治审签", "审签", "ipd_doctor"));
            }
            if (patient.IsDischargeReady)
            {
                result.Add(BuildIpdTodo(patient, "待出院处理", $"出院条件已标记 · {patient.DischargeGateSummary}", "处理", "ipd_doctor"));
            }
            if (patient.PendingOrderCount > 0)
            {
                result.Add(BuildIpdTodo(patient, "医嘱待校对", $"{patient.PendingOrderCount} 条医嘱待护士校对", "查看", "ipd_doctor"));
            }
        }
        return result;
    }

    private List<WorkbenchTodoItem> BuildWardNurseTodos()
    {
        var result = new List<WorkbenchTodoItem>();
        AddCriticalValueTodo(result);

        foreach (var patient in Workbench.InpatientRecords.Where(p => p.IsActiveInpatient))
        {
            if (patient.PendingOrderCount > 0)
            {
                result.Add(BuildIpdTodo(patient, "医嘱待校对", $"{patient.PendingOrderCount} 条新医嘱待校对审签", "校对", "ipd_nurse"));
            }
            if (patient.WaitingDecomposeOrderCount > 0)
            {
                result.Add(BuildIpdTodo(patient, "长嘱待分解", $"{patient.WaitingDecomposeOrderCount} 条已校对医嘱待分解", "分解", "ipd_nurse"));
            }
            if (patient.PendingNursingTaskCount > 0)
            {
                result.Add(BuildIpdTodo(patient, "护理任务待执行", $"{patient.PendingNursingTaskCount} 项护理任务待扫码执行", "执行", "ipd_nurse"));
            }
            if (patient.UnverifiedNursingTaskCount > 0)
            {
                result.Add(BuildIpdTodo(patient, "护理记录待核对", $"{patient.UnverifiedNursingTaskCount} 项已执行任务待双人核对", "核对", "ipd_nurse"));
            }
        }
        return result;
    }

    private List<WorkbenchTodoItem> BuildPharmacyTodos()
    {
        var result = new List<WorkbenchTodoItem>();
        foreach (var item in Workbench.PharmacyRecords.Where(p => p.CanDispense).Take(10))
        {
            result.Add(new WorkbenchTodoItem
            {
                Title = "待核验发药",
                Context = item.PatientName,
                Detail = $"{item.PrescriptionNo} · {item.DrugSummary}",
                Status = "已收费",
                ActionLabel = "发药",
                NavigationKey = "pharmacy_window",
                Dispense = item
            });
        }
        foreach (var item in Workbench.PharmacyRecords.Where(p => p.CanReturnDrug).Take(6))
        {
            result.Add(new WorkbenchTodoItem
            {
                Title = "退药待质检",
                Context = item.PatientName,
                Detail = $"{item.PrescriptionNo} · 已关单，待退药入库",
                Status = "退药",
                ActionLabel = "查看",
                NavigationKey = "pharmacy_window",
                Dispense = item
            });
        }
        foreach (var item in Workbench.PharmacyRecords.Where(p => p.IsAllergyBlocked && !p.IsOrderClosed).Take(6))
        {
            result.Add(new WorkbenchTodoItem
            {
                Title = "用药安全阻断",
                Context = item.PatientName,
                Detail = $"{item.PrescriptionNo} · {item.SafetyAudit}",
                Status = "阻断",
                ActionLabel = "查看",
                NavigationKey = "pharmacy_window",
                Dispense = item
            });
        }
        return result;
    }

    private List<WorkbenchTodoItem> BuildCashierTodos()
    {
        var result = new List<WorkbenchTodoItem>();
        foreach (var item in Workbench.PharmacyRecords.Where(p => p.NeedsCashierSettlement).Take(10))
        {
            result.Add(new WorkbenchTodoItem
            {
                Title = "处方待收费",
                Context = item.PatientName,
                Detail = $"{item.PrescriptionNo} · ¥{item.TotalAmount:N2}",
                Status = "待收费",
                ActionLabel = "收费",
                NavigationKey = "finance",
                Dispense = item
            });
        }
        foreach (var item in Workbench.PharmacyRecords.Where(p => p.CanExecuteFinancialRefund).Take(10))
        {
            result.Add(new WorkbenchTodoItem
            {
                Title = "退药待退费",
                Context = item.PatientName,
                Detail = $"{item.PrescriptionNo} · ¥{item.TotalAmount:N2}",
                Status = "待退费",
                ActionLabel = "审理",
                NavigationKey = "finance",
                Dispense = item
            });
        }
        return result;
    }

    private List<WorkbenchTodoItem> BuildAdminTodos()
    {
        return new List<WorkbenchTodoItem>
        {
            new()
            {
                Title = "用户账号维护",
                Context = $"{Workbench.UsersList.Count} 个账号",
                Detail = $"{Workbench.UsersList.Count(u => u.IsActive)} 个启用 · 点击进入账号管理",
                Status = "系统",
                ActionLabel = "维护",
                NavigationKey = "user_admin"
            },
            new()
            {
                Title = "角色与权限配置",
                Context = $"{Workbench.RolesList.Count} 个角色",
                Detail = "角色权限矩阵与数据隔离策略",
                Status = "权限",
                ActionLabel = "配置",
                NavigationKey = "role_admin"
            },
            new()
            {
                Title = "动态表单模板",
                Context = $"{Workbench.DynamicFormTemplates.Count} 个模板",
                Detail = "维护专科扩展字段、处方明细与病历表单",
                Status = "配置",
                ActionLabel = "配置",
                NavigationKey = "dynamic_form"
            },
            new()
            {
                Title = "安全审计日志",
                Context = Workbench.AuditLogsList.Count > 0 ? $"{Workbench.AuditLogsList.Count} 条日志" : "待加载",
                Detail = Workbench.AuditSummaryInfo,
                Status = "审计",
                ActionLabel = "查看",
                NavigationKey = "audit_log"
            }
        };
    }

    private List<WorkbenchTodoItem> BuildUnknownRoleTodos()
    {
        return new List<WorkbenchTodoItem>
        {
            new()
            {
                Title = "当前岗位未匹配",
                Context = Workbench.CurrentRoleDisplayName,
                Detail = "系统没有为该角色配置工作台业务队列",
                Status = "提示",
                ActionLabel = "返回",
                NavigationKey = "workbench"
            }
        };
    }

    private static WorkbenchTodoItem BuildIpdTodo(InpatientRecordModel patient, string title, string detail, string action, string key)
    {
        return new WorkbenchTodoItem
        {
            Title = title,
            Context = $"{patient.PatientName} · {patient.BedCode}",
            Detail = detail,
            Status = patient.PatientStatus,
            ActionLabel = action,
            NavigationKey = key,
            Inpatient = patient
        };
    }

    private static RoleKind CurrentRoleKind()
    {
        var ctx = SessionContext.Current;
        if (ctx.IsAdmin) return RoleKind.Admin;
        if (ctx.IsOpdDoctor) return RoleKind.Opd;
        if (ctx.IsIpdAttending) return RoleKind.IpdDoctor;
        if (ctx.IsIpdResident) return RoleKind.IpdDoctor;
        if (ctx.IsWardNurse) return RoleKind.WardNurse;
        if (ctx.IsPharmacist) return RoleKind.Pharmacist;
        if (ctx.IsCashier) return RoleKind.Cashier;
        return RoleKind.Unknown;
    }

    private enum RoleKind
    {
        Unknown,
        Opd,
        IpdDoctor,
        WardNurse,
        Pharmacist,
        Cashier,
        Admin
    }
}
