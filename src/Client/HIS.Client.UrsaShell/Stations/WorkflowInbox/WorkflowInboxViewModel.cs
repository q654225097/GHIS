using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.Shell.Controls;
using HIS.Client.UrsaShell.ViewModels;
using HIS.Contracts;

namespace HIS.Client.UrsaShell.Stations.WorkflowInbox;

public class WorkflowTaskDisplayItem : ClientModelBase
{
    public string TaskId { get; set; } = string.Empty;
    public string InstanceId { get; set; } = string.Empty;
    public string InstanceNo { get; set; } = string.Empty;
    public string DefKey { get; set; } = string.Empty;
    public string BoType { get; set; } = string.Empty;
    public string BusinessKey { get; set; } = string.Empty;
    public string NodeName { get; set; } = string.Empty;
    public string AssigneeUserId { get; set; } = string.Empty;
    public string AssigneeName { get; set; } = string.Empty;
    public string ApprovalMode { get; set; } = "SINGLE";
    public string Status { get; set; } = "PENDING";
    public string? Comment { get; set; }
    public long? ActedAtMs { get; set; }
    public string InitiatorName { get; set; } = string.Empty;
    public string InitiatorDeptName { get; set; } = string.Empty;
    public string? BoVariablesJson { get; set; }
    public long CreatedAtMs { get; set; }

    public string TimeString => CreatedAtMs > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtMs).ToLocalTime().ToString("yyyy-MM-dd HH:mm")
        : "--";

    public string ModeBadge => ApprovalMode switch
    {
        "AND_SIGN" => "会签 (需全员通过)",
        "OR_SIGN" => "或签 (任一人通过)",
        _ => "单人办理"
    };

    public string StatusBadge => Status switch
    {
        "PENDING" => "待审批",
        "APPROVED" => "已同意",
        "REJECTED" => "已拒绝",
        "RETURNED" => "已驳回",
        "TRANSFERRED" => "已转办",
        "CANCELLED" => "已取消",
        _ => Status
    };

    public string StatusBrush => Status switch
    {
        "PENDING" => "#D97706",
        "APPROVED" => "#10B981",
        "REJECTED" => "#DC2626",
        "RETURNED" => "#D97706",
        _ => "#64748B"
    };

    public string BoSummary => !string.IsNullOrWhiteSpace(BoVariablesJson)
        ? BoVariablesJson.Replace("{", "").Replace("}", "").Replace("\"", "").Trim()
        : $"单据: {BusinessKey}";
}

public class WorkflowNotificationDisplayItem : ClientModelBase
{
    public string Id { get; set; } = string.Empty;
    public string InstanceId { get; set; } = string.Empty;
    public string NotifyType { get; set; } = "CC";
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public long CreatedAtMs { get; set; }

    public string TimeString => CreatedAtMs > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtMs).ToLocalTime().ToString("yyyy-MM-dd HH:mm")
        : "--";
}

public sealed class WorkflowInboxViewModel : ClientModelBase, IShortcutTarget
{
    private readonly ShellViewModel? _shell;
    private int _selectedTabIndex;
    private bool _isBusy;
    private string _statusNotice = string.Empty;
    private string _actionComment = "同意";
    private string _delegateUserId = string.Empty;
    private WorkflowTaskDisplayItem? _selectedTask;
    private WorkflowNotificationDisplayItem? _selectedNotification;

    public WorkflowInboxViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        TimelineViewModel = new WorkflowTrackTimelineViewModel();

        RefreshCommand = new AsyncRelayCommand(LoadCurrentTabDataAsync);
        ApproveCommand = new AsyncRelayCommand(ApproveCurrentTaskAsync, () => HasSelectedPendingTask);
        RejectCommand = new AsyncRelayCommand(RejectCurrentTaskAsync, () => HasSelectedPendingTask);
        ReturnPrevCommand = new AsyncRelayCommand(ReturnPrevCurrentTaskAsync, () => HasSelectedPendingTask);
        ReturnStartCommand = new AsyncRelayCommand(ReturnStartCurrentTaskAsync, () => HasSelectedPendingTask);
        TransferCommand = new AsyncRelayCommand(TransferCurrentTaskAsync, () => HasSelectedPendingTask);
        WithdrawCommand = new AsyncRelayCommand(WithdrawCurrentTaskAsync);

        _ = LoadCurrentTabDataAsync();
    }

    public WorkbenchModel Workbench { get; }
    public WorkflowTrackTimelineViewModel TimelineViewModel { get; }

    public ObservableCollection<WorkflowTaskDisplayItem> TodoTasks { get; } = new();
    public ObservableCollection<WorkflowTaskDisplayItem> DoneTasks { get; } = new();
    public ObservableCollection<WorkflowNotificationDisplayItem> CcNotifications { get; } = new();
    public ObservableCollection<WorkflowTaskDisplayItem> MyInitiatedTasks { get; } = new();

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (SetField(ref _selectedTabIndex, value))
            {
                SelectedTask = null;
                SelectedNotification = null;
                _ = LoadCurrentTabDataAsync();
            }
        }
    }

    public WorkflowTaskDisplayItem? SelectedTask
    {
        get => _selectedTask;
        set
        {
            if (SetField(ref _selectedTask, value))
            {
                OnPropertyChanged(nameof(HasSelectedTask));
                OnPropertyChanged(nameof(HasSelectedPendingTask));
                (ApproveCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                (RejectCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                (ReturnPrevCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                (ReturnStartCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                (TransferCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();

                if (value != null)
                {
                    _ = LoadTracksForInstanceAsync(value.InstanceId);
                }
            }
        }
    }

    public bool HasSelectedTask => SelectedTask != null;
    public bool HasSelectedPendingTask => SelectedTask != null && SelectedTask.Status == "PENDING";

    public WorkflowNotificationDisplayItem? SelectedNotification
    {
        get => _selectedNotification;
        set
        {
            if (SetField(ref _selectedNotification, value))
            {
                if (value != null)
                {
                    _ = LoadTracksForInstanceAsync(value.InstanceId);
                }
            }
        }
    }

    public string ActionComment
    {
        get => _actionComment;
        set => SetField(ref _actionComment, value);
    }

    public string DelegateUserId
    {
        get => _delegateUserId;
        set => SetField(ref _delegateUserId, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    public string StatusNotice
    {
        get => _statusNotice;
        set => SetField(ref _statusNotice, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand ApproveCommand { get; }
    public ICommand RejectCommand { get; }
    public ICommand ReturnPrevCommand { get; }
    public ICommand ReturnStartCommand { get; }
    public ICommand TransferCommand { get; }
    public ICommand WithdrawCommand { get; }

    public async Task LoadCurrentTabDataAsync()
    {
        IsBusy = true;
        StatusNotice = "正在加载数据...";
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            switch (SelectedTabIndex)
            {
                case 0: // Todo
                    var todoRes = await http.GetFromJsonAsync<ClientApiEnvelope<List<WorkflowTaskItemDto>>>("/api/v4/workflow/tasks/my-todo");
                    if (todoRes?.Code == 200 && todoRes.Data != null)
                    {
                        TodoTasks.Clear();
                        foreach (var t in todoRes.Data)
                        {
                            TodoTasks.Add(MapTask(t));
                        }
                        StatusNotice = $"已加载 {TodoTasks.Count} 项待办审批任务";
                        SelectedTask = TodoTasks.FirstOrDefault();
                    }
                    break;
                case 1: // Done
                    var doneRes = await http.GetFromJsonAsync<ClientApiEnvelope<List<WorkflowTaskItemDto>>>("/api/v4/workflow/tasks/my-done");
                    if (doneRes?.Code == 200 && doneRes.Data != null)
                    {
                        DoneTasks.Clear();
                        foreach (var t in doneRes.Data)
                        {
                            DoneTasks.Add(MapTask(t));
                        }
                        StatusNotice = $"已加载 {DoneTasks.Count} 项已办审批记录";
                        SelectedTask = DoneTasks.FirstOrDefault();
                    }
                    break;
                case 2: // CC
                    var ccRes = await http.GetFromJsonAsync<ClientApiEnvelope<List<WorkflowNotificationDto>>>("/api/v4/workflow/notifications/my-cc");
                    if (ccRes?.Code == 200 && ccRes.Data != null)
                    {
                        CcNotifications.Clear();
                        foreach (var c in ccRes.Data)
                        {
                            CcNotifications.Add(new WorkflowNotificationDisplayItem
                            {
                                Id = c.Id,
                                InstanceId = c.InstanceId,
                                NotifyType = c.NotifyType,
                                Title = c.Title,
                                Content = c.Content,
                                IsRead = c.IsRead,
                                CreatedAtMs = c.CreatedAtMs
                            });
                        }
                        StatusNotice = $"已加载 {CcNotifications.Count} 条抄送传阅消息";
                        SelectedNotification = CcNotifications.FirstOrDefault();
                    }
                    break;
                case 3: // My Initiated
                    var myDoneRes = await http.GetFromJsonAsync<ClientApiEnvelope<List<WorkflowTaskItemDto>>>("/api/v4/workflow/tasks/my-done");
                    if (myDoneRes?.Code == 200 && myDoneRes.Data != null)
                    {
                        MyInitiatedTasks.Clear();
                        foreach (var t in myDoneRes.Data)
                        {
                            MyInitiatedTasks.Add(MapTask(t));
                        }
                        StatusNotice = $"已加载 {MyInitiatedTasks.Count} 项相关单据";
                        SelectedTask = MyInitiatedTasks.FirstOrDefault();
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            StatusNotice = "加载异常: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static WorkflowTaskDisplayItem MapTask(WorkflowTaskItemDto t)
    {
        return new WorkflowTaskDisplayItem
        {
            TaskId = t.TaskId,
            InstanceId = t.InstanceId,
            InstanceNo = t.InstanceNo,
            DefKey = t.DefKey,
            BoType = t.BoType,
            BusinessKey = t.BusinessKey,
            NodeName = t.NodeName,
            AssigneeUserId = t.AssigneeUserId,
            AssigneeName = t.AssigneeName ?? t.AssigneeUserId,
            ApprovalMode = t.ApprovalMode,
            Status = t.Status,
            Comment = t.Comment,
            ActedAtMs = t.ActedAtMs,
            InitiatorName = t.InitiatorName ?? t.InitiatorUserId ?? "系统",
            InitiatorDeptName = t.InitiatorDeptName ?? t.InitiatorDeptId ?? "",
            BoVariablesJson = t.BoVariablesJson,
            CreatedAtMs = t.CreatedAtMs
        };
    }

    private async Task LoadTracksForInstanceAsync(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.GetFromJsonAsync<ClientApiEnvelope<List<WorkflowTrackItemDto>>>($"/api/v4/workflow/instances/{instanceId}/tracks");
            if (res?.Code == 200 && res.Data != null)
            {
                TimelineViewModel.LoadTracks(res.Data, SelectedTask?.Status, SelectedTask?.NodeName);
            }
        }
        catch
        {
            // Ignore track loading errors
        }
    }

    public async Task ApproveCurrentTaskAsync()
    {
        await ExecuteTaskActionAsync("APPROVE", ActionComment ?? "同意");
    }

    public async Task RejectCurrentTaskAsync()
    {
        if (string.IsNullOrWhiteSpace(ActionComment))
        {
            _shell?.ShowToast("审批拒绝必须填写原因意见！", "Warning");
            return;
        }
        await ExecuteTaskActionAsync("REJECT", ActionComment);
    }

    public async Task ReturnPrevCurrentTaskAsync()
    {
        await ExecuteTaskActionAsync("RETURN_PREV", ActionComment ?? "驳回上一节点");
    }

    public async Task ReturnStartCurrentTaskAsync()
    {
        await ExecuteTaskActionAsync("RETURN_START", ActionComment ?? "驳回至发起人重新提交");
    }

    public async Task TransferCurrentTaskAsync()
    {
        if (string.IsNullOrWhiteSpace(DelegateUserId))
        {
            _shell?.ShowToast("转办必须指定目标人员工号/ID！", "Warning");
            return;
        }
        await ExecuteTaskActionAsync("TRANSFER", ActionComment ?? "转办", DelegateUserId);
    }

    public async Task WithdrawCurrentTaskAsync()
    {
        if (SelectedTask == null) return;
        IsBusy = true;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new { Comment = ActionComment ?? "发起人撤回" };
            var res = await http.PostAsJsonAsync($"/api/v4/workflow/instances/{SelectedTask.InstanceId}/withdraw", payload);
            var result = await res.Content.ReadFromJsonAsync<ClientApiEnvelope<WorkflowActionResultDto>>();
            if (result?.Code == 200)
            {
                _shell?.ShowToast("单据审批已成功撤回！", "Success");
                await LoadCurrentTabDataAsync();
            }
            else
            {
                _shell?.ShowToast(result?.Message ?? "撤回失败", "Danger");
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast("撤回异常: " + ex.Message, "Danger");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteTaskActionAsync(string action, string comment, string? delegateTo = null)
    {
        if (SelectedTask == null) return;
        IsBusy = true;
        StatusNotice = $"正在执行审批动作 [{action}]...";
        try
        {
            var payload = new
            {
                ActionType = action,
                Comment = comment,
                DelegateToUserId = delegateTo
            };

            using var http = AuthenticatedHttpClientFactory.Create();
            var res = await http.PostAsJsonAsync($"/api/v4/workflow/tasks/{SelectedTask.TaskId}/action", payload);
            var result = await res.Content.ReadFromJsonAsync<ClientApiEnvelope<WorkflowActionResultDto>>();
            if (result?.Code == 200 && result.Data?.Success == true)
            {
                _shell?.ShowToast($"审批办理成功！当前状态: {result.Data.NewStatus}", "Success");
                StatusNotice = $"任务办理完成，流程新状态: {result.Data.NewStatus}";
                ActionComment = "同意";
                DelegateUserId = string.Empty;
                await LoadCurrentTabDataAsync();
            }
            else
            {
                string errMsg = result?.Message ?? result?.Data?.Message ?? "办理失败";
                _shell?.ShowToast(errMsg, "Danger");
                StatusNotice = "办理失败: " + errMsg;
            }
        }
        catch (Exception ex)
        {
            _shell?.ShowToast("审批异常: " + ex.Message, "Danger");
            StatusNotice = "审批异常: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public bool OnShortcut(string keyGesture)
    {
        if (keyGesture == "F5")
        {
            _ = LoadCurrentTabDataAsync();
            return true;
        }
        return false;
    }
}
