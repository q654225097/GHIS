using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Media;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.Admin;

public sealed class AuditLogStationViewModel : ClientModelBase, IShortcutTarget
{
    private readonly ShellViewModel? _shell;

    public AuditLogStationViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        QueryLogsCommand = new RelayCommand(async () =>
        {
            await Workbench.LoadAuditLogsAsync();
            _shell?.ShowToast(Workbench.AuditSummaryInfo, "Info");
        });
        VerifyChainCommand = new RelayCommand(async () =>
        {
            await Workbench.VerifyAuditChainAsync();
            _shell?.ShowToast(
                Workbench.AuditVerificationStatus,
                Workbench.AuditVerificationBadgeClass == "Success" ? "Success" : "Warning");
        });
        CreateAnchorCommand = new RelayCommand(ConfirmCreateAnchor);
        ClearFilterCommand = new RelayCommand(async () =>
        {
            Workbench.AuditEventTypeFilter = string.Empty;
            Workbench.AuditTargetEntityFilter = string.Empty;
            Workbench.AuditOperatorFilter = string.Empty;
            await Workbench.LoadAuditLogsAsync();
            _shell?.ShowToast("已清空过滤条件并刷新审计台账。", "Info");
        });
        InspectEntryCommand = new RelayCommand(p => OpenEntryDrawer(p as AuditLogEntryModel));
    }

    public WorkbenchModel Workbench { get; }
    public Action? FocusFilterAction { get; set; }

    public ICommand QueryLogsCommand { get; }
    public ICommand VerifyChainCommand { get; }
    public ICommand CreateAnchorCommand { get; }
    public ICommand ClearFilterCommand { get; }
    public ICommand InspectEntryCommand { get; }

    public void Attach()
    {
        if (Workbench.AuditLogsList.Count == 0)
        {
            _ = Workbench.LoadAuditLogsAsync();
        }
    }

    public void Detach()
    {
    }

    public bool OnShortcut(string keyGesture)
    {
        switch (keyGesture)
        {
            case "F2":
                VerifyChainCommand.Execute(null);
                return true;
            case "F3":
                ConfirmCreateAnchor();
                return true;
            case "F5":
                QueryLogsCommand.Execute(null);
                return true;
            case "Ctrl+F":
                FocusFilterAction?.Invoke();
                return true;
            default:
                return false;
        }
    }

    private void ConfirmCreateAnchor()
    {
        _shell?.RequestConfirm(
            "固化防截断外部锚点",
            "确定立即基于当前最新审计哈希链节点生成外部校验锚点？生成后可有效防止数据库尾部截断攻击。",
            "确认固化锚点",
            async () =>
            {
                await Workbench.CreateAuditAnchorAsync();
                _shell?.ShowToast(
                    Workbench.AuditVerificationStatus,
                    Workbench.AuditVerificationBadgeClass == "Success" ? "Success" : "Warning");
            });
    }

    private void OpenEntryDrawer(AuditLogEntryModel? entry)
    {
        if (entry == null) return;
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(CreateField("事件编号 / 类型", $"{entry.Id} · {entry.EventType}"));
        panel.Children.Add(CreateField("发生时间", entry.FormattedTime));
        panel.Children.Add(CreateField("操作人 / 科室", $"{entry.OperatorName} ({entry.UserId}) | 科室 {entry.DeptId}"));
        panel.Children.Add(CreateField("目标实体 / ID", $"{entry.TargetEntity} -> {entry.TargetId}"));
        panel.Children.Add(CreateField("操作明细载荷", entry.ActionDetail));
        panel.Children.Add(CreateField("当前节点 SHA-256", entry.CurrentHash, mono: true));
        panel.Children.Add(CreateField("前驱节点 PrevHash", entry.PrevHash, mono: true));

        _shell?.OpenDrawer("安全审计节点密码学存证详情", entry.EventType, panel, 520);
    }

    private static Border CreateField(string label, string value, bool mono = false)
    {
        return new Border
        {
            Padding = new Avalonia.Thickness(12),
            CornerRadius = new Avalonia.CornerRadius(6),
            BorderBrush = ThemeBrushes.Border,
            BorderThickness = new Avalonia.Thickness(1),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = ThemeBrushes.Text2 },
                    new SelectableTextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(value) ? "-" : value,
                        FontSize = 12,
                        FontFamily = mono ? new FontFamily("Consolas") : FontFamily.Default,
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };
    }
}
