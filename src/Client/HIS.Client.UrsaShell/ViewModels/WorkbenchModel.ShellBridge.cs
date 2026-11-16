using System;
using System.Collections.ObjectModel;
using System.Linq;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public sealed class NoticeHistoryItem
{
    public string Timestamp { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Severity { get; init; } = "Info";
}

public partial class WorkbenchModel
{
    public ObservableCollection<NoticeHistoryItem> SystemNoticeHistory { get; } = new();

    public event Action<NoticeHistoryItem>? NoticeRaised;

    private bool _isNoticeHistoryOpen;
    public bool IsNoticeHistoryOpen
    {
        get => _isNoticeHistoryOpen;
        set => SetField(ref _isNoticeHistoryOpen, value, nameof(IsNoticeHistoryOpen));
    }

    private void RecordSystemNotice(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var severity = InferNoticeSeverity(message);
        var item = new NoticeHistoryItem
        {
            Timestamp = DateTime.Now.ToString("HH:mm:ss"),
            Message = message,
            Severity = severity
        };

        SystemNoticeHistory.Insert(0, item);
        while (SystemNoticeHistory.Count > 20)
        {
            SystemNoticeHistory.RemoveAt(SystemNoticeHistory.Count - 1);
        }

        NoticeRaised?.Invoke(item);
    }

    private static string InferNoticeSeverity(string message)
    {
        if (message.Contains("失败") || message.Contains("错误") || message.Contains("阻断") ||
            message.Contains("拦截") || message.Contains("❌") || message.Contains("禁止") ||
            message.Contains("红线"))
        {
            return "Danger";
        }

        if (message.Contains("⚠") || message.Contains("警告") || message.Contains("注意") ||
            message.Contains("尚未") || message.Contains("不可") || message.Contains("锁单"))
        {
            return "Warning";
        }

        if (message.Contains("✓") || message.Contains("成功") || message.Contains("完成") ||
            message.Contains("已保存") || message.Contains("通过"))
        {
            return "Success";
        }

        return "Info";
    }

    public int PendingOpdQueueCount => PatientQueue.Count(p => !p.IsCompleted);

    public int PendingFinanceSettleCount => PharmacyRecords.Count(p => !p.IsPaidNotified && !p.IsOrderClosed);

    public int PendingCriticalValueCount => CriticalValueAlert.IsAcknowledged ? 0 : 1;

    public bool HasActiveCriticalValue => PendingCriticalValueCount > 0;

    public string CurrentRoleDisplayName
    {
        get
        {
            var ctx = SessionContext.Current;
            if (ctx.IsAdmin) return "系统管理员";
            if (ctx.IsOpdDoctor) return "门诊医生";
            if (ctx.IsIpdAttending) return "住院主治医师";
            if (ctx.IsIpdResident) return "住院医师";
            if (ctx.IsWardNurse) return "病区护士";
            if (ctx.IsPharmacist) return "药房/药库药师";
            if (ctx.IsCashier) return "收费/结算员";
            return string.IsNullOrWhiteSpace(ctx.RoleName) ? "临床工作员" : ctx.RoleName;
        }
    }
}
