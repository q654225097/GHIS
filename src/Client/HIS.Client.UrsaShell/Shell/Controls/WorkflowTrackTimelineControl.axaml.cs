using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using HIS.Client.Core;
using HIS.Contracts;

namespace HIS.Client.UrsaShell.Shell.Controls;

public class WorkflowTrackDisplayModel : ClientModelBase
{
    public string TrackId { get; set; } = string.Empty;
    public string NodeName { get; set; } = string.Empty;
    public string OperatorUserId { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public long OccurredAtMs { get; set; }
    public bool IsLastItem { get; set; }

    public bool HasComment => !string.IsNullOrWhiteSpace(Comment);

    public string TimeString => OccurredAtMs > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(OccurredAtMs).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
        : "--";

    public string ActionTitle => ActionType switch
    {
        "START" => "发起流程",
        "APPROVE" => "审批通过",
        "REJECT" => "审批拒绝",
        "RETURN_PREV" => "驳回上一步",
        "RETURN_START" => "驳回发起人",
        "WITHDRAW" => "发起人撤回",
        "TRANSFER" => "转交处理",
        "CC" => "抄送传阅",
        "AUTO_ROUTE" => "自动流转",
        "FINISH" => "流程归档",
        _ => ActionType
    };

    public string ActionIcon => ActionType switch
    {
        "START" => "🚀",
        "APPROVE" => "✓",
        "REJECT" => "✕",
        "RETURN_PREV" or "RETURN_START" => "↩",
        "WITHDRAW" => "⏸",
        "TRANSFER" => "⇶",
        "CC" => "📋",
        "AUTO_ROUTE" => "➔",
        "FINISH" => "🏁",
        _ => "•"
    };

    public string ActionBrush => ActionType switch
    {
        "START" or "APPROVE" or "FINISH" => "#10B981",
        "REJECT" => "#DC2626",
        "RETURN_PREV" or "RETURN_START" => "#D97706",
        "TRANSFER" => "#2563EB",
        "CC" => "#0284C7",
        "WITHDRAW" => "#64748B",
        _ => "#0284C7"
    };
}

public class WorkflowTrackTimelineViewModel : ClientModelBase
{
    private string _currentStatusText = "流转中";
    private string _currentNodeText = "--";
    private string _statusBrush = "#2563EB";

    public ObservableCollection<WorkflowTrackDisplayModel> Tracks { get; } = new();

    public bool HasTracks => Tracks.Count > 0;
    public string TrackCountText => $"共 {Tracks.Count} 条审批记录";

    public string CurrentStatusText
    {
        get => _currentStatusText;
        set => SetField(ref _currentStatusText, value, nameof(CurrentStatusText));
    }

    public string CurrentNodeText
    {
        get => _currentNodeText;
        set => SetField(ref _currentNodeText, value, nameof(CurrentNodeText));
    }

    public string StatusBrush
    {
        get => _statusBrush;
        set => SetField(ref _statusBrush, value, nameof(StatusBrush));
    }

    public void LoadTracks(IReadOnlyList<WorkflowTrackItemDto> tracks, string? instanceStatus = null, string? currentNode = null)
    {
        Tracks.Clear();
        if (tracks != null && tracks.Count > 0)
        {
            for (int i = 0; i < tracks.Count; i++)
            {
                var t = tracks[i];
                Tracks.Add(new WorkflowTrackDisplayModel
                {
                    TrackId = t.Id,
                    NodeName = t.NodeName,
                    OperatorUserId = t.OperatorUserId,
                    OperatorName = !string.IsNullOrWhiteSpace(t.OperatorName) ? t.OperatorName : t.OperatorUserId,
                    ActionType = t.ActionType,
                    Comment = t.Comment,
                    OccurredAtMs = t.OccurredAtMs,
                    IsLastItem = i == tracks.Count - 1
                });
            }
        }

        CurrentNodeText = !string.IsNullOrWhiteSpace(currentNode) ? currentNode : (Tracks.LastOrDefault()?.NodeName ?? "--");
        if (!string.IsNullOrWhiteSpace(instanceStatus))
        {
            CurrentStatusText = instanceStatus switch
            {
                "RUNNING" => "审批流转中",
                "APPROVED" => "终审通过",
                "REJECTED" => "审批拒绝",
                "WITHDRAWN" => "已撤回",
                _ => instanceStatus
            };
            StatusBrush = instanceStatus switch
            {
                "APPROVED" => "#10B981",
                "REJECTED" => "#DC2626",
                "RUNNING" => "#2563EB",
                _ => "#64748B"
            };
        }

        OnPropertyChanged(nameof(HasTracks));
        OnPropertyChanged(nameof(TrackCountText));
    }
}

public partial class WorkflowTrackTimelineControl : UserControl
{
    public static readonly StyledProperty<WorkflowTrackTimelineViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<WorkflowTrackTimelineControl, WorkflowTrackTimelineViewModel?>(nameof(ViewModel));

    public WorkflowTrackTimelineViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public WorkflowTrackTimelineControl()
    {
        InitializeComponent();
        if (DataContext is not WorkflowTrackTimelineViewModel)
        {
            DataContext = new WorkflowTrackTimelineViewModel();
        }
    }
}
