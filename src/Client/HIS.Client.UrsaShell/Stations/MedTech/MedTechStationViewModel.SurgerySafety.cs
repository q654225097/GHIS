using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using HIS.BuildingBlocks.Common;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.Stations.MedTech;

public sealed partial class MedTechStationViewModel
{
    private long _scheduleLoadGeneration;
    private long _anesthesiaSelectionGeneration;

    private sealed record SafetyCheck(string Code, string Label);
    private sealed record SafetyDuty(string Code, string Name, bool HasPassed);
    private sealed record SafetyStep(string Code, string Name, bool IsCount, SafetyCheck[] Checks, SafetyDuty[] Duties,
        bool Completed, bool HasCountConflict);
    private sealed record SafetyCounts(int Gauze, int Needles, int Instruments, int AddedGauze, int AddedNeedles, int AddedInstruments);
    private sealed record SafetyEvidence(Dictionary<string, bool>? Checks, SafetyCounts? Counts);
    private sealed record SafetySignature(string StepCode, string DutyCode, string OperatorName, long RecordedAtMs,
        bool Passed, string? Note, SafetyEvidence Evidence);
    private sealed record SafetyState(string ScheduleId, string ScheduleNo, string PatientId, string PatientName, string EncounterNo,
        string SurgeryName, string OrRoomId, int Status, int Version, string? CurrentStepCode, string? CurrentDutyCode, string? BlockReason,
        bool CanStart, bool CanFinish, bool HasArchivedAnesthesia, SafetyStep[] Steps, SafetySignature[] History);
    private sealed record AnesthesiaReceipt(string Id, string ScheduleId, string AsaGrade, string AnesthesiaType,
        decimal BloodLossMl, decimal UrineOutputMl, decimal FluidInputMl, int Status);

    private static (string UserId, string Role, string Post, string Dept, string? Ward) SurgeryContext()
    {
        var session = SessionContext.Current;
        return (session.UserId, session.RoleCode, session.ActivePostId, session.ActiveDeptId, session.ActiveWardId);
    }

    private async Task OpenSurgerySafetyAsync(SurgeryScheduleItemModel? item)
    {
        if (_shell == null) return;
        if (item == null)
        {
            _shell.ShowToast("请先在手术排班中选择一台手术。", "Warning");
            return;
        }

        var context = SurgeryContext();
        var panel = new StackPanel { Spacing = 10 };
        _shell.OpenDrawer("手术安全核查", item.ScheduleNo, panel, 540);
        bool Current() => ReferenceEquals(_shell.DrawerContent, panel) && SurgeryContext() == context;
        string route = $"/api/v4/medtech/surgeries/{Uri.EscapeDataString(item.Id)}";

        async Task LoadAsync(string? notice = null)
        {
            if (!Current()) return;
            panel.Children.Clear();
            panel.Children.Add(SafetyText("正在读取本台手术的核查记录…"));
            try
            {
                using var http = AuthenticatedHttpClientFactory.Create();
                using var response = await http.GetAsync(route + "/safety");
                var result = await response.Content.ReadFromJsonAsync<ApiResponse<SafetyState>>(JsonOptions);
                if (!Current()) return;
                if (response.IsSuccessStatusCode && result is { Code: 200, Data: { } state } && state.ScheduleId == item.Id)
                {
                    item.Status = state.Status;
                    Render(state, notice);
                }
                else Render(null, result?.Code != 200 ? result?.Message ?? "未取得核查记录。" : "返回的排班与当前排班不一致。");
            }
            catch
            {
                if (Current()) Render(null, notice == null ? "暂时无法读取，请重试。" : notice + "\n暂时无法读取，请稍后刷新。");
            }
        }

        void Render(SafetyState? state, string? notice)
        {
            panel.Children.Clear();
            var refresh = new Button { Content = "刷新核查" };
            refresh.Click += async (_, _) => await LoadAsync();
            panel.Children.Add(refresh);
            if (notice != null) panel.Children.Add(SafetyText(notice));
            if (state == null) return;

            panel.Children.Add(SafetyText($"{state.PatientName}  /  住院号 {state.EncounterNo}", true));
            panel.Children.Add(SafetyText($"{state.SurgeryName}  /  手术间 {state.OrRoomId}"));
            if (state.BlockReason != null) panel.Children.Add(SafetyText(state.BlockReason, true));
            foreach (var step in state.Steps)
            {
                string status = step.Completed ? "已核查" : step.HasCountConflict ? "双人清点不一致" :
                    step.Code == state.CurrentStepCode ? "当前阶段" : "未完成";
                panel.Children.Add(SafetyText($"{step.Name} · {status}    {string.Join("、", step.Duties.Select(x => $"{x.Name}{(x.HasPassed ? "已签" : "待确认")}"))}"));
            }

            var current = state.Steps.FirstOrDefault(s => s.Code == state.CurrentStepCode);
            if (state.BlockReason == null && current != null && current.Duties.FirstOrDefault(d => d.Code == state.CurrentDutyCode) is { } duty)
                AddSignForm(state, current, duty);
            else if (state.BlockReason == null && current != null)
                panel.Children.Add(SafetyText("当前阶段由排班指定的对应岗位签署。"));

            if (state.Status == 1 && state.Steps.All(s => s.Completed) && !state.HasArchivedAnesthesia)
                panel.Children.Add(SafetyText("离室核查已通过，正在等待本台麻醉医师完成归档。", true));
            if (state.CanStart || state.CanFinish)
            {
                var advance = new Button { Content = state.CanFinish ? "确认结束本台手术" : "确认开台" };
                advance.Classes.Add("Primary");
                advance.Click += async (_, _) =>
                {
                    if (!Current()) return;
                    panel.IsEnabled = false;
                    string outcome;
                    try
                    {
                        using var http = AuthenticatedHttpClientFactory.Create();
                        using var response = await http.PostAsync(route + (state.CanFinish ? "/finish-surgery" : "/start"), null);
                        var result = await response.Content.ReadFromJsonAsync<ApiResponse<SurgeryScheduleItemModel>>(JsonOptions);
                        if (!Current()) return;
                        if (response.IsSuccessStatusCode && result is { Code: 200, Data: { } saved } &&
                            saved.Id == item.Id && saved.Status == (state.CanFinish ? 2 : 1))
                        {
                            item.Status = saved.Status;
                            outcome = state.CanFinish ? "后台已确认本台手术结束。" : "后台已确认本台手术开台。";
                        }
                        else outcome = result?.Code is >= 400 and < 500 ? result.Message : "结果待核对，请先查询本台手术状态。";
                    }
                    catch { outcome = "结果待核对，请先查询本台手术状态。"; }
                    finally { panel.IsEnabled = true; }
                    await LoadAsync(outcome);
                };
                panel.Children.Add(advance);
            }

            if (state.History.Length > 0) panel.Children.Add(SafetyText("签署记录", true));
            foreach (var entry in state.History)
            {
                var step = state.Steps.FirstOrDefault(x => x.Code == entry.StepCode);
                var dutyLabel = step?.Duties.FirstOrDefault(d => d.Code == entry.DutyCode)?.Name ?? entry.DutyCode;
                var time = DateTimeOffset.FromUnixTimeMilliseconds(entry.RecordedAtMs).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                var count = entry.Evidence.Counts;
                string detail = count != null
                    ? $"纱布 {count.Gauze}（追加 {count.AddedGauze}），缝针 {count.Needles}（追加 {count.AddedNeedles}），器械 {count.Instruments}（追加 {count.AddedInstruments}）"
                    : string.Join("；", step?.Checks.Select(c => $"{c.Label}：{(entry.Evidence.Checks?.GetValueOrDefault(c.Code) == true ? "通过" : "未通过")}") ?? []);
                panel.Children.Add(SafetyText($"{step?.Name ?? entry.StepCode} · {dutyLabel} {entry.OperatorName} · {(entry.Passed ? "本人通过" : "本人未通过")}\n{time}\n{detail}" +
                    (string.IsNullOrWhiteSpace(entry.Note) ? string.Empty : $"\n{entry.Note}")));
            }
        }

        void AddSignForm(SafetyState state, SafetyStep step, SafetyDuty duty)
        {
            panel.Children.Add(SafetyText($"{step.Name} · {duty.Name}签署", true));
            var checks = new List<(SafetyCheck Check, ComboBox Input)>();
            var quantities = new TextBox[step.IsCount ? (step.Code == "PRE_INCISION_COUNT" ? 3 : 6) : 0];
            if (step.IsCount)
            {
                string[] labels = ["实际纱布数量", "实际缝针数量", "实际器械数量", "累计追加纱布", "累计追加缝针", "累计追加器械"];
                var grid = new UniformGrid { Columns = 3 };
                for (int i = 0; i < quantities.Length; i++)
                {
                    var cell = new StackPanel { Spacing = 4, Margin = new Avalonia.Thickness(0, 0, 6, 8) };
                    cell.Children.Add(SafetyText(labels[i]));
                    quantities[i] = new TextBox { PlaceholderText = "填实际数量" };
                    cell.Children.Add(quantities[i]);
                    grid.Children.Add(cell);
                }
                panel.Children.Add(grid);
            }
            else foreach (var check in step.Checks)
            {
                var input = new ComboBox { ItemsSource = new[] { "通过", "未通过" }, PlaceholderText = "选择核查结果" };
                panel.Children.Add(SafetyText(check.Label));
                panel.Children.Add(input);
                checks.Add((check, input));
            }
            var note = new TextBox { PlaceholderText = "情况说明（未通过时必填）", MaxLength = 500, TextWrapping = TextWrapping.Wrap };
            var confirmed = new CheckBox { Content = "确认由本人核对并签署" };
            var hint = SafetyText(string.Empty);
            var sign = new Button { Content = "提交本人签署" };
            sign.Classes.Add("Primary");
            panel.Children.Add(note);
            panel.Children.Add(confirmed);
            panel.Children.Add(sign);
            panel.Children.Add(hint);
            sign.Click += async (_, _) =>
            {
                if (!Current()) return;
                if (confirmed.IsChecked != true || checks.Any(c => c.Input.SelectedIndex < 0))
                {
                    hint.Text = "须逐项核查并勾选本人签署。";
                    return;
                }
                var numbers = new int[6];
                for (int i = 0; i < quantities.Length; i++)
                    if (!int.TryParse(quantities[i].Text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]) || numbers[i] > 100000)
                    {
                        hint.Text = "每项清点必须填写 0 至 100000 的整数。";
                        return;
                    }
                SafetyCounts? counts = step.IsCount ? new(numbers[0], numbers[1], numbers[2], numbers[3], numbers[4], numbers[5]) : null;
                var payload = new { ExpectedVersion = state.Version, Counts = counts,
                    Checks = step.IsCount ? null : checks.ToDictionary(x => x.Check.Code, x => x.Input.SelectedIndex == 0), Note = note.Text };
                panel.IsEnabled = false;
                string outcome;
                try
                {
                    using var http = AuthenticatedHttpClientFactory.Create();
                    using var response = await http.PostAsJsonAsync(route + "/safety/" + step.Code, payload);
                    var result = await response.Content.ReadFromJsonAsync<ApiResponse<SafetyState>>(JsonOptions);
                    if (!Current()) return;
                    if (response.IsSuccessStatusCode && result is { Code: 200, Data: { } saved } && saved.ScheduleId == state.ScheduleId && saved.Version >= state.Version)
                    {
                        Render(saved, result.Message);
                        return;
                    }
                    if (result?.Code == 400)
                    {
                        hint.Text = result.Message;
                        return;
                    }
                    outcome = result?.Code is >= 400 and < 500 ? result.Message : "签署结果待核对，请先刷新记录。";
                }
                catch { outcome = "签署结果待核对，请先刷新记录。"; }
                finally { panel.IsEnabled = true; }
                await LoadAsync(outcome);
            };
        }

        await LoadAsync();
    }

    private static TextBlock SafetyText(string text, bool heading = false) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, FontSize = heading ? 14 : 13,
        FontWeight = heading ? FontWeight.SemiBold : FontWeight.Normal
    };
}
