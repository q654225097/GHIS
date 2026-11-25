using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HIS.BuildingBlocks.Common;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Services;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.OpdDoctor;

public sealed class PriceItemCatalogRow
{
    public string Id { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string FeeCategory { get; set; } = "EXAM";
    public string? Spec { get; set; }
    public string UnitName { get; set; } = "次";
    public decimal UnitPrice { get; set; }
    public string? DefaultExecDeptId { get; set; }
}

public sealed class OpdServerServiceReqRow
{
    public string Id { get; set; } = string.Empty;
    public string RequestNo { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string ServiceItemCode { get; set; } = string.Empty;
    public string ExecMedTechDeptId { get; set; } = string.Empty;
    public decimal FeeAmount { get; set; }
    public int Status { get; set; }
    public short IsFeeConfirmed { get; set; }
    public string? ReportSummary { get; set; }
    public short IsCriticalValue { get; set; }
}

public sealed partial class OpdDoctorViewModel
{
    private static readonly JsonSerializerOptions MedTechJsonOpts = new() { PropertyNameCaseInsensitive = true };

    private ICommand? _viewMedTechReportsCommand;
    public ICommand ViewMedTechReportsCommand => _viewMedTechReportsCommand ??= new AsyncRelayCommand(OpenMedTechReportsDrawerAsync);

    private async Task OpenRealMedTechCatalogDrawerAsync()
    {
        var patient = Workbench.SelectedPatient;
        if (patient == null || string.IsNullOrWhiteSpace(patient.VisitId))
        {
            NotifyToast("请先选择或叫号接诊门诊患者后开立检查/检验", "Warning");
            return;
        }
        if (_shell == null) return;

        var items = new List<PriceItemCatalogRow>();
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            using var resp = await http.GetAsync("/api/v4/config/master/price-items?activeOnly=true");
            if (resp.IsSuccessStatusCode)
            {
                var env = await resp.Content.ReadFromJsonAsync<ApiResponse<List<PriceItemCatalogRow>>>(MedTechJsonOpts);
                if (env?.Data != null)
                {
                    items = env.Data
                        .Where(x => x.FeeCategory is "EXAM" or "LAB" or "TREATMENT" or "检查" or "检验")
                        .ToList();
                }
            }
        }
        catch { }

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = $"接诊患者：{patient.PatientName} ({patient.VisitNo}) · 点击下方收费物价目录项目提交真实医技申请",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });

        var filterBox = new TextBox { PlaceholderText = "搜索物价编码或名称...", Height = 32 };
        var listPanel = new StackPanel { Spacing = 6 };

        void RebuildList(string kw)
        {
            listPanel.Children.Clear();
            var filtered = items.Where(x =>
                string.IsNullOrWhiteSpace(kw) ||
                x.ItemName.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                x.ItemCode.Contains(kw, StringComparison.OrdinalIgnoreCase)).Take(25);

            foreach (var catalogItem in filtered)
            {
                var rowBtn = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(10, 8),
                    Content = new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,85,Auto"),
                        Children =
                        {
                            new TextBlock { Text = $"{catalogItem.ItemName} ({catalogItem.ItemCode})", FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
                            BuildFeeTextBlock(catalogItem.UnitPrice),
                            BuildCategoryTextBlock(catalogItem.FeeCategory)
                        }
                    },
                    Command = new AsyncRelayCommand(async () => await SubmitRealMedTechRequestAsync(patient, catalogItem))
                };
                rowBtn.Classes.Add("Secondary");
                listPanel.Children.Add(rowBtn);
            }
        }

        static TextBlock BuildFeeTextBlock(decimal price)
        {
            var tb = new TextBlock { Text = $"¥{price:0.00}", FontSize = 12, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(tb, 1);
            return tb;
        }
        static TextBlock BuildCategoryTextBlock(string cat)
        {
            var tb = new TextBlock { Text = cat, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(tb, 2);
            return tb;
        }

        filterBox.TextChanged += (_, _) => RebuildList(filterBox.Text ?? string.Empty);
        RebuildList(string.Empty);

        panel.Children.Add(filterBox);
        panel.Children.Add(new ScrollViewer { MaxHeight = 480, Content = listPanel });
        _shell.OpenDrawer("门诊检查/检验项目目录 (物价直连)", "提交后将路由至 LIS/PACS 及收费队列", panel, 520);
    }

    private async Task SubmitRealMedTechRequestAsync(PatientQueueItemModel patient, PriceItemCatalogRow item)
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var dto = new
            {
                SourceType = "OPD",
                SourceVisitOrOrderId = patient.VisitId,
                PatientId = string.IsNullOrWhiteSpace(patient.PatientId) ? patient.MedicalRecordNo : patient.PatientId,
                OrderDeptId = string.IsNullOrWhiteSpace(patient.OpdDeptId) ? "DEPT-OPD-01" : patient.OpdDeptId,
                OrderWardId = (string?)null,
                ServiceItemCode = item.ItemCode,
                FeeAmount = item.UnitPrice
            };

            using var resp = await http.PostAsJsonAsync("/api/v4/medtech/requests", dto, MedTechJsonOpts);
            var env = await resp.Content.ReadFromJsonAsync<ApiResponse<OpdServerServiceReqRow>>(MedTechJsonOpts);
            if (!resp.IsSuccessStatusCode || env?.Code is not (200 or 0) || env.Data == null)
            {
                NotifyToast(env?.Message ?? $"医技开单被拦截 ({(int)resp.StatusCode})", "Danger");
                return;
            }

            Workbench.MedTechOrders.Add(new MedTechOrderItemModel
            {
                RequestId = env.Data.Id,
                PatientId = env.Data.PatientId,
                RequestNo = env.Data.RequestNo,
                ItemName = $"{item.ItemName} ({item.ItemCode})",
                DeptName = env.Data.ExecMedTechDeptId,
                ExecLocation = "已路由至医技科执行",
                Fee = $"¥{env.Data.FeeAmount:0.00}",
                Status = env.Data.IsFeeConfirmed == 1 ? "已确费待检" : "待收费确费",
                StatusClass = "Primary",
                IsFeeLocked = env.Data.IsFeeConfirmed == 1
            });
            Workbench.SystemNotice = $"✓ 已向医技服务端开立 [{item.ItemName}] ({env.Data.RequestNo})";
            NotifyToast(env.Message ?? "医技申请开立成功", "Success");
            _shell?.CloseDrawer();
            RefreshAllWorkflowState();
        }
        catch (Exception ex)
        {
            NotifyToast("开立医技申请异常：" + ex.Message, "Danger");
        }
    }

    private async Task OpenMedTechReportsDrawerAsync()
    {
        var patient = Workbench.SelectedPatient;
        if (patient == null || _shell == null) { NotifyToast("请先选择患者", "Warning"); return; }

        string patId = string.IsNullOrWhiteSpace(patient.PatientId) ? patient.MedicalRecordNo : patient.PatientId;
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = $"{patient.PatientName} · 检查检验结果与区域互认调阅", FontSize = 14, FontWeight = FontWeight.Bold });

        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var reqEnv = await http.GetFromJsonAsync<ApiResponse<List<OpdServerServiceReqRow>>>(
                $"/api/v4/medtech/requests?patientId={Uri.EscapeDataString(patId)}", MedTechJsonOpts);
            panel.Children.Add(new TextBlock { Text = $"院内医技申请及报告 ({reqEnv?.Data?.Count ?? 0} 条)", FontSize = 13, FontWeight = FontWeight.Bold });
            if (reqEnv?.Data != null)
            {
                foreach (var r in reqEnv.Data.Take(20))
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = $"[{r.RequestNo}] {r.ServiceItemCode} · 状态 {r.Status} · 报告：{(string.IsNullOrWhiteSpace(r.ReportSummary) ? "待出报告" : r.ReportSummary)}",
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap
                    });
                }
            }

            var dicomEnv = await http.GetFromJsonAsync<ApiResponse<List<JsonElement>>>(
                $"/api/v4/medtech/exam/studies?patientId={Uri.EscapeDataString(patId)}", MedTechJsonOpts);
            if (dicomEnv?.Data is { Count: > 0 })
            {
                panel.Children.Add(new TextBlock { Text = $"PACS DICOM 影像检查 ({dicomEnv.Data.Count} 项)", FontSize = 13, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 6, 0, 0) });
                foreach (var d in dicomEnv.Data)
                {
                    string mod = d.TryGetProperty("modality", out var m) ? m.GetString() ?? "" : "";
                    string uid = d.TryGetProperty("studyInstanceUid", out var u) ? u.GetString() ?? "" : "";
                    int sc = d.TryGetProperty("seriesCount", out var s) ? s.GetInt32() : 0;
                    int ic = d.TryGetProperty("instanceCount", out var i) ? i.GetInt32() : 0;
                    panel.Children.Add(new TextBlock { Text = $"[{mod}] StudyUID: {uid} · 序列 {sc} / 图像 {ic}", FontSize = 11, TextWrapping = TextWrapping.Wrap });
                }
            }

            var hieEnv = await http.GetFromJsonAsync<ApiResponse<JsonElement>>(
                $"/api/v4/hie/ehr/{Uri.EscapeDataString(patId)}", MedTechJsonOpts);
            if (hieEnv?.Code == 200)
            {
                panel.Children.Add(new TextBlock { Text = "医联体 HIE 跨院共享摘要 (互认调阅)", FontSize = 13, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 6, 0, 0) });
                panel.Children.Add(new TextBlock { Text = hieEnv.Data.ToString(), FontSize = 11, TextWrapping = TextWrapping.Wrap });
            }
        }
        catch (Exception ex)
        {
            panel.Children.Add(new TextBlock { Text = "加载医技报告失败：" + ex.Message, FontSize = 12 });
        }

        _shell.OpenDrawer("医技报告 / PACS 影像 / 跨院调阅", patient.PatientName, new ScrollViewer { Content = panel }, 600);
    }
}
