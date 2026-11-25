using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HIS.BuildingBlocks.Common;
using HIS.Client.Core;
using HIS.Contracts;

namespace HIS.Client.UrsaShell.Shell;

public sealed partial class ShellViewModel
{
    private const string CriticalValueRoute = "/api/v4/medtech/critical-values";
    private long _criticalQueueRevision;

    private static (string UserId, string RoleId, string PostId, string DeptId, string? WardId) CriticalContext()
    {
        var s = SessionContext.Current;
        return (s.UserId, s.RoleId, s.ActivePostId, s.ActiveDeptId, s.ActiveWardId);
    }

    public bool CanOpenCriticalValueQueue => RoleCode.Normalize(SessionContext.Current.RoleCode) is
        RoleCode.OpdDoctor or RoleCode.IpdResident or RoleCode.IpdAttending or RoleCode.WardNurse or "OPD-NURSE";

    public string CriticalQueueTitle => Workbench.PendingCriticalValueCount == 0
        ? "危急值签收" : $"危急值待闭环 {Workbench.PendingCriticalValueCount}";

    private async Task RefreshCriticalValueQueueAsync(StackPanel? panel = null, string? alertId = null, string? notice = null)
    {
        var context = CriticalContext();
        var revision = ++_criticalQueueRevision;
        bool Current() => context == CriticalContext() && revision == _criticalQueueRevision &&
            (panel == null || ReferenceEquals(DrawerContent, panel));

        if (!CanOpenCriticalValueQueue || context.UserId.Length == 0)
        {
            SetCriticalCount(0);
            if (panel != null)
            {
                var latest = AlertHistory.FirstOrDefault(x => x.EventType == "CRITICAL_VALUE");
                panel.Children.Add(CriticalText(latest == null ? "当前岗位不能办理危急值签收。" :
                    $"{latest.PatientName}\n{latest.Message}\n{latest.TimeText}", 13));
            }
            return;
        }

        if (panel != null)
        {
            panel.Children.Clear();
            var reload = new Button { Content = "刷新" };
            reload.Click += async (_, _) => await RefreshCriticalValueQueueAsync(panel);
            panel.Children.Add(reload);
            if (!string.IsNullOrWhiteSpace(notice)) panel.Children.Add(CriticalText(notice, 13));
            panel.Children.Add(CriticalText("正在查询后台签收记录…", 13));
        }

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            using var response = await http.GetAsync(CriticalValueRoute);
            var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<List<CriticalValueRow>>>();
            if (!Current()) return;
            if (panel != null) panel.Children.RemoveAt(panel.Children.Count - 1);
            if (!response.IsSuccessStatusCode || envelope?.Code != 200 || envelope.Data == null)
            {
                panel?.Children.Add(CriticalText(envelope?.Message ?? "无法查询危急值记录。", 13));
                return;
            }

            var rows = envelope.Data;
            SetCriticalCount(rows.Count(x => x.Status != "ACKNOWLEDGED"));
            if (panel == null) return;

            if (alertId != null && rows.All(x => x.Id != alertId))
            {
                using var selected = await http.GetAsync(CriticalValueRoute + "?alertId=" + Uri.EscapeDataString(alertId));
                var match = await selected.Content.ReadFromJsonAsync<ApiResponse<List<CriticalValueRow>>>();
                if (!Current()) return;
                if (selected.IsSuccessStatusCode && match?.Code == 200 && match.Data is { Count: 1 } && match.Data[0].Id == alertId)
                    rows.Insert(0, match.Data[0]);
                else
                    panel.Children.Add(CriticalText("当前岗位未查到这条提醒对应的报告。", 13));
            }

            if (rows.Count == 0) panel.Children.Add(CriticalText("当前岗位没有可查看的危急值签收记录。", 13));
            foreach (var row in rows.OrderByDescending(x => x.Id == alertId))
                panel.Children.Add(BuildCriticalValueRow(row, panel));
        }
        catch (Exception)
        {
            if (!Current() || panel == null) return;
            if (panel.Children.LastOrDefault() is TextBlock { Text: "正在查询后台签收记录…" } loading)
                panel.Children.Remove(loading);
            panel.Children.Add(CriticalText("危急值记录查询失败，请刷新后重试。", 13));
        }
    }

    private Control BuildCriticalValueRow(CriticalValueRow row, StackPanel owner)
    {
        var card = new StackPanel { Spacing = 5 };
        card.Children.Add(new TextBlock { Text = $"{row.PatientName} · {row.EncounterNo}", FontWeight = FontWeight.SemiBold,
            FontSize = 15, TextWrapping = TextWrapping.Wrap });
        card.Children.Add(CriticalText($"报告/申请号：{row.ReportNo} · {row.ItemName}", 13));
        card.Children.Add(CriticalText(row.CriticalValueText, 13));
        card.Children.Add(CriticalText($"医师：{SignText(row.DocAckedBy, row.DocAckedAtMs)}\n护士：{SignText(row.NurseAckedBy, row.NurseAckedAtMs)}", 12));
        if (row.CanSign)
        {
            var note = new TextBox { PlaceholderText = "临床处置记录（选填）", MaxLength = 1000, TextWrapping = TextWrapping.Wrap };
            var button = new Button { Content = row.SigningRole == "DOCTOR" ? "医师签收" : "护士签收" };
            var context = CriticalContext();
            var revision = _criticalQueueRevision;
            bool Current() => context == CriticalContext() && ReferenceEquals(DrawerContent, owner) && revision == _criticalQueueRevision;
            button.Click += (_, _) =>
            {
                if (!Current()) return;
                string? actionNote = note.Text?.Trim();
                RequestConfirm("核对危急值签收", $"{row.PatientName} · {row.EncounterNo}\n报告/申请号：{row.ReportNo}\n{row.CriticalValueText}",
                    "确认本人签收", () =>
                    {
                        if (!Current()) return;
                        button.IsEnabled = false;
                        _ = SignCriticalValueAsync(row, owner, actionNote);
                    });
            };
            card.Children.Add(note);
            card.Children.Add(button);
        }
        return new Border { Padding = new Thickness(0, 8), Child = card };
    }

    private async Task SignCriticalValueAsync(CriticalValueRow row, StackPanel owner, string? note)
    {
        var context = CriticalContext();
        var user = context.UserId;
        string result;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            using var response = await http.PostAsJsonAsync(CriticalValueRoute + "/" + Uri.EscapeDataString(row.Id) + "/acknowledge",
                new { ClinicalActionNote = note });
            var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<CriticalValueRow>>();
            var saved = envelope?.Data;
            bool confirmed = response.IsSuccessStatusCode && envelope?.Code == 200 && saved?.Id == row.Id && saved.ReportId == row.ReportId &&
                (row.SigningRole == "DOCTOR" ? saved.DocAckedBy == user : saved.NurseAckedBy == user);
            result = confirmed ? $"{row.ReportNo}：{envelope!.Message}" :
                envelope is { Code: not 200 } ? envelope.Message : "签收结果待核对，请查看原报告的签收状态。";
        }
        catch (Exception)
        {
            result = "签收结果待核对，请查看原报告的签收状态。";
        }
        if (context == CriticalContext() && ReferenceEquals(DrawerContent, owner))
            await RefreshCriticalValueQueueAsync(owner, row.Id, result);
    }

    private void SetCriticalCount(int count)
    {
        Workbench.SetPendingCriticalValues(count);
        OnPropertyChanged(nameof(CriticalQueueTitle));
        RefreshNavBadges();
    }

    private static TextBlock CriticalText(string text, double size) => new()
        { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };

    private static string SignText(string? user, long? time) => string.IsNullOrWhiteSpace(user) ? "待签收" :
        $"{user} {((time != null) ? DateTimeOffset.FromUnixTimeMilliseconds(time.Value).ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm") : string.Empty)}";

    private sealed record CriticalValueRow(string Id, string ReportId, string ReportNo, string EncounterNo, string PatientName,
        string ItemName, string CriticalValueText, string? DocAckedBy, long? DocAckedAtMs, string? NurseAckedBy,
        long? NurseAckedAtMs, string Status, string SigningRole, bool CanSign);
}
