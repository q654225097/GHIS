using System;
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
    private int _selectedSubTabIndex = 0;

    public AuditLogStationViewModel(WorkbenchModel workbench, ShellViewModel? shell = null)
    {
        Workbench = workbench;
        _shell = shell;

        QueryLogsCommand = new RelayCommand(async () =>
        {
            await Workbench.LoadAuditLogsAsync();
            await Workbench.LoadSecurityPostureAsync();
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

        ScanThreatsCommand = new RelayCommand(async () =>
        {
            await Workbench.ScanSecurityThreatsAsync();
            _shell?.ShowToast("全网威胁与异常行为扫描已完成，已更新态势看板。", "Info");
        });

        ResolveAlertCommand = new RelayCommand(p => ConfirmResolveAlert(p as SecurityAlertModel));

        RotateKeyCommand = new RelayCommand(p => ConfirmRotateKey(p as KmsKeyModel));

        SwitchSubTabCommand = new RelayCommand(p =>
        {
            if (p is int idx)
            {
                SelectedSubTabIndex = idx;
            }
            else if (p is string s && int.TryParse(s, out int parsed))
            {
                SelectedSubTabIndex = parsed;
            }
        });
    }

    public WorkbenchModel Workbench { get; }
    public Action? FocusFilterAction { get; set; }

    public int SelectedSubTabIndex
    {
        get => _selectedSubTabIndex;
        set => SetField(ref _selectedSubTabIndex, value, nameof(SelectedSubTabIndex));
    }

    public ICommand QueryLogsCommand { get; }
    public ICommand VerifyChainCommand { get; }
    public ICommand CreateAnchorCommand { get; }
    public ICommand ClearFilterCommand { get; }
    public ICommand InspectEntryCommand { get; }
    public ICommand ScanThreatsCommand { get; }
    public ICommand ResolveAlertCommand { get; }
    public ICommand RotateKeyCommand { get; }
    public ICommand SwitchSubTabCommand { get; }

    public void Attach()
    {
        _ = Workbench.LoadAuditLogsAsync();
        _ = Workbench.LoadSecurityPostureAsync();
        _ = Workbench.LoadSecurityAlertsAsync();
        _ = Workbench.LoadKmsKeysAsync();
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

    private void ConfirmResolveAlert(SecurityAlertModel? alert)
    {
        if (alert == null) return;
        _shell?.RequestConfirm(
            "安全告警闭环处置",
            $"确定对告警 [{alert.AlertNo}] ({alert.AlertType}) 执行确认与闭环防范处置？",
            "确认处置闭环",
            async () =>
            {
                await Workbench.ResolveSecurityAlertAsync(alert.Id, "安全管理员已核实风险，执行拦截与安全加固处置");
                _shell?.ShowToast($"告警 [{alert.AlertNo}] 处置完成", "Success");
            });
    }

    private void ConfirmRotateKey(KmsKeyModel? key)
    {
        if (key == null) return;
        _shell?.RequestConfirm(
            "密码学主密钥平滑轮换",
            $"确定触发对 [{key.KeyName}] (当前版本 v{key.Version}) 的平滑密钥轮换？系统将生成新密钥版本，历史版本平滑保留用于验签，不会造成业务中断。",
            "确认执行轮换",
            async () =>
            {
                await Workbench.RotateKmsKeyAsync(key.KeyName, "安全管理员执行等保三级定期密钥轮换");
                _shell?.ShowToast($"主密钥 [{key.KeyName}] 已成功轮换至新版本", "Success");
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
        panel.Children.Add(CreateField("当前节点 SHA-256 / SM3", entry.CurrentHash, mono: true));
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
