using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.Printing;

public static class PrintEngineService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private static readonly Regex TokenRegex = new(@"\{\{\s*([a-zA-Z0-9_.\-]+)\s*\}\}", RegexOptions.Compiled);

    /// <summary>
    /// 模板变更事件：管理员保存/修改触发位或模板后通知全院已打开页面的 DynamicPrintTriggerBar 自动刷新
    /// </summary>
    public static event Action? TriggersInvalidated;

    public static void NotifyTriggersChanged() => TriggersInvalidated?.Invoke();

    public static List<PrintElementModel> ParseLayout(string? layoutJson)
    {
        if (string.IsNullOrWhiteSpace(layoutJson)) return new List<PrintElementModel>();
        try
        {
            return JsonSerializer.Deserialize<List<PrintElementModel>>(layoutJson, JsonOpts) ?? new List<PrintElementModel>();
        }
        catch
        {
            return new List<PrintElementModel>();
        }
    }

    public static string ResolveText(PrintElementModel el, IReadOnlyDictionary<string, string> context)
    {
        string raw = string.IsNullOrWhiteSpace(el.StaticText)
            ? (string.IsNullOrWhiteSpace(el.BindingPath) ? string.Empty : "{{" + el.BindingPath + "}}")
            : el.StaticText;

        string resolved = TokenRegex.Replace(raw, m =>
        {
            string key = m.Groups[1].Value;
            if (context.TryGetValue(key, out string? val) && !string.IsNullOrWhiteSpace(val))
                return val;
            return string.Empty;
        });

        if (string.IsNullOrWhiteSpace(resolved) && !string.IsNullOrWhiteSpace(el.BindingPath) &&
            context.TryGetValue(el.BindingPath, out string? directVal))
        {
            resolved = directVal;
        }
        return resolved;
    }

    public static void EnrichSystemFields(Dictionary<string, string> ctx, string? operatorName = null)
    {
        ctx.TryAdd("system.printTime", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        ctx.TryAdd("system.printDate", DateTime.Now.ToString("yyyy-MM-dd"));
        ctx.TryAdd("system.operatorName", string.IsNullOrWhiteSpace(operatorName) ? "当前用户" : operatorName);
        ctx.TryAdd("system.hospitalName", "GHIS 医院信息系统");
    }

    /// <summary>
    /// 将打印模板与实时页面数据渲染为精确毫米尺寸的 Avalonia 画布（用于打印预览）
    /// </summary>
    public static Canvas BuildPreviewCanvas(PrintTemplateDto template, IReadOnlyDictionary<string, string> context, double scalePxPerMm = 4.2)
    {
        double wMm = (double)(template.Orientation == "LANDSCAPE" ? template.PaperHeightMm : template.PaperWidthMm);
        double hMm = (double)(template.Orientation == "LANDSCAPE" ? template.PaperWidthMm : template.PaperHeightMm);

        var canvas = new Canvas
        {
            Width = wMm * scalePxPerMm,
            Height = hMm * scalePxPerMm,
            Background = Brushes.White,
            ClipToBounds = true
        };

        var elements = ParseLayout(template.LayoutJson);
        foreach (var el in elements)
        {
            double x = el.XMm * scalePxPerMm;
            double y = el.YMm * scalePxPerMm;
            double w = Math.Max(4, el.WidthMm * scalePxPerMm);
            double h = Math.Max(2, el.HeightMm * scalePxPerMm);
            string val = ResolveText(el, context);

            if (el.IsLine)
            {
                var line = new Border
                {
                    Width = w,
                    Height = Math.Max(1.2, h * 0.4),
                    Background = Brushes.Black
                };
                Canvas.SetLeft(line, x);
                Canvas.SetTop(line, y);
                canvas.Children.Add(line);
            }
            else if (el.IsRect)
            {
                var rect = new Border
                {
                    Width = w,
                    Height = h,
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(1)
                };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                canvas.Children.Add(rect);
            }
            else if (el.IsBarcode)
            {
                var bcContainer = new Grid { Width = w, Height = h, RowDefinitions = new RowDefinitions("*,Auto") };
                var barCanvas = new Canvas { ClipToBounds = true };
                string bits = Code128BarcodeRenderer.EncodeToBits(val);
                double modW = w / Math.Max(1, bits.Length);
                double barH = el.ShowBarcodeText ? Math.Max(8, h - 14) : h;
                int i = 0;
                while (i < bits.Length)
                {
                    if (bits[i] == '1')
                    {
                        int s = i;
                        while (i < bits.Length && bits[i] == '1') i++;
                        var r = new Rectangle { Width = Math.Max(0.8, (i - s) * modW), Height = barH, Fill = Brushes.Black };
                        Canvas.SetLeft(r, s * modW);
                        Canvas.SetTop(r, 0);
                        barCanvas.Children.Add(r);
                    }
                    else i++;
                }
                Grid.SetRow(barCanvas, 0);
                bcContainer.Children.Add(barCanvas);
                if (el.ShowBarcodeText)
                {
                    var lbl = new TextBlock
                    {
                        Text = val,
                        FontSize = Math.Max(8, el.FontSize * (scalePxPerMm / 4.2)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Foreground = Brushes.Black,
                        FontFamily = new FontFamily("Consolas, monospace")
                    };
                    Grid.SetRow(lbl, 1);
                    bcContainer.Children.Add(lbl);
                }
                Canvas.SetLeft(bcContainer, x);
                Canvas.SetTop(bcContainer, y);
                canvas.Children.Add(bcContainer);
            }
            else if (el.IsQrCode)
            {
                var qrBox = new Border
                {
                    Width = w,
                    Height = h,
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(1.5),
                    Background = Brushes.White,
                    Child = new TextBlock
                    {
                        Text = "QR\n" + (val.Length > 10 ? val[..10] : val),
                        FontSize = 8,
                        TextAlignment = TextAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = Brushes.Black
                    }
                };
                Canvas.SetLeft(qrBox, x);
                Canvas.SetTop(qrBox, y);
                canvas.Children.Add(qrBox);
            }
            else
            {
                var tb = new TextBlock
                {
                    Text = val,
                    Width = w,
                    Height = h,
                    FontSize = Math.Max(7, el.FontSize * (scalePxPerMm / 3.78)),
                    FontWeight = el.IsBold ? FontWeight.Bold : FontWeight.Normal,
                    Foreground = Brushes.Black,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = el.TextAlign switch
                    {
                        "CENTER" => TextAlignment.Center,
                        "RIGHT" => TextAlignment.Right,
                        _ => TextAlignment.Left
                    }
                };
                Canvas.SetLeft(tb, x);
                Canvas.SetTop(tb, y);
                canvas.Children.Add(tb);
            }
        }
        return canvas;
    }

    /// <summary>
    /// 生成精确毫米分页的 HTML 打印单据并调起系统打印预览/打印机，同时写入后端打印审计日志
    /// </summary>
    public static async Task<string> ExportAndPrintAsync(
        PrintTemplateDto template,
        string triggerSlot,
        Dictionary<string, string> context,
        int copies = 1)
    {
        double wMm = (double)(template.Orientation == "LANDSCAPE" ? template.PaperHeightMm : template.PaperWidthMm);
        double hMm = (double)(template.Orientation == "LANDSCAPE" ? template.PaperWidthMm : template.PaperHeightMm);
        var elements = ParseLayout(template.LayoutJson);

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\"/>");
        sb.AppendLine($"<title>{WebUtility.HtmlEncode(template.TemplateName)}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine($"@page {{ size: {wMm:0.##}mm {hMm:0.##}mm; margin: 0; }}");
        sb.AppendLine("html, body { margin: 0; padding: 0; background: #e5e7eb; font-family: 'Microsoft YaHei', 'PingFang SC', sans-serif; }");
        sb.AppendLine($".sheet {{ position: relative; width: {wMm:0.##}mm; height: {hMm:0.##}mm; background: #fff; overflow: hidden; page-break-after: always; margin: 4mm auto; box-shadow: 0 2px 8px rgba(0,0,0,.15); }}");
        sb.AppendLine("@media print { html, body { background: #fff; } .sheet { margin: 0; box-shadow: none; } }");
        sb.AppendLine(".el { position: absolute; box-sizing: border-box; color: #000; line-height: 1.25; overflow: hidden; }");
        sb.AppendLine("</style></head><body>");

        int totalCopies = Math.Clamp(copies, 1, 20);
        for (int c = 0; c < totalCopies; c++)
        {
            sb.AppendLine("<div class=\"sheet\">");
            foreach (var el in elements)
            {
                string val = ResolveText(el, context);
                string pos = $"left:{el.XMm:0.##}mm;top:{el.YMm:0.##}mm;width:{el.WidthMm:0.##}mm;height:{el.HeightMm:0.##}mm;";
                if (el.IsLine)
                {
                    sb.AppendLine($"<div class=\"el\" style=\"{pos}border-top:0.35mm solid #000;\"></div>");
                }
                else if (el.IsRect)
                {
                    sb.AppendLine($"<div class=\"el\" style=\"{pos}border:0.3mm solid #000;\"></div>");
                }
                else if (el.IsBarcode)
                {
                    string svg = Code128BarcodeRenderer.RenderSvg(val, el.WidthMm, el.HeightMm, el.ShowBarcodeText);
                    sb.AppendLine($"<div class=\"el\" style=\"{pos}\">{svg}</div>");
                }
                else if (el.IsQrCode)
                {
                    string safe = WebUtility.HtmlEncode(val);
                    sb.AppendLine($"<div class=\"el\" style=\"{pos}border:0.4mm solid #000;display:flex;align-items:center;justify-content:center;font-size:6pt;text-align:center;\">QR<br/>{safe}</div>");
                }
                else
                {
                    string align = el.TextAlign.ToLowerInvariant();
                    string weight = el.IsBold ? "bold" : "normal";
                    string safe = WebUtility.HtmlEncode(val);
                    sb.AppendLine($"<div class=\"el\" style=\"{pos}font-size:{el.FontSize:0.#}pt;font-weight:{weight};text-align:{align};\">{safe}</div>");
                }
            }
            sb.AppendLine("</div>");
        }

        sb.AppendLine("<script>window.onload = function(){ setTimeout(function(){ window.print(); }, 200); };</script>");
        sb.AppendLine("</body></html>");

        string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GHIS_PrintSpool");
        Directory.CreateDirectory(dir);
        string filePath = System.IO.Path.Combine(dir, $"PRT_{template.TemplateCode}_{DateTime.Now:yyyyMMdd_HHmmss}.html");
        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);

        try
        {
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch { /* 无默认浏览器环境时仍保留输出文件 */ }

        // 上报打印留痕日志到后端
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            context.TryGetValue("patient.fullName", out string? patName);
            if (string.IsNullOrWhiteSpace(patName)) context.TryGetValue("ipd.patientName", out patName);
            if (string.IsNullOrWhiteSpace(patName)) context.TryGetValue("pharmacy.patientName", out patName);
            if (string.IsNullOrWhiteSpace(patName)) context.TryGetValue("finance.patientName", out patName);

            context.TryGetValue("medtech.requestNo", out string? bizKey);
            if (string.IsNullOrWhiteSpace(bizKey)) context.TryGetValue("patient.visitNo", out bizKey);
            if (string.IsNullOrWhiteSpace(bizKey)) context.TryGetValue("ipd.admissionNo", out bizKey);
            if (string.IsNullOrWhiteSpace(bizKey)) context.TryGetValue("pharmacy.prescriptionNo", out bizKey);

            await http.PostAsJsonAsync("/api/v4/print/jobs/record", new
            {
                TemplateId = template.Id,
                TemplateCode = template.TemplateCode,
                TemplateName = template.TemplateName,
                PageKey = template.PageKey,
                TriggerSlot = triggerSlot,
                BusinessKey = bizKey,
                PatientName = patName,
                RenderedContextJson = JsonSerializer.Serialize(context),
                Copies = totalCopies,
                PrinterName = "系统默认打印机"
            });
        }
        catch { /* 打印留痕失败不阻断前端出纸 */ }

        return filePath;
    }

    /// <summary>
    /// 弹出带实时上下文展示与毫米级预览的打印窗口
    /// </summary>
    public static void OpenPreviewWindow(PrintTemplateDto template, string triggerSlot, Dictionary<string, string> context, Window? owner = null)
    {
        EnrichSystemFields(context);
        double wMm = (double)(template.Orientation == "LANDSCAPE" ? template.PaperHeightMm : template.PaperWidthMm);
        double hMm = (double)(template.Orientation == "LANDSCAPE" ? template.PaperWidthMm : template.PaperHeightMm);
        double scale = wMm > 120 ? 2.6 : 4.5;

        var previewCanvas = BuildPreviewCanvas(template, context, scale);
        var copiesBox = new NumericUpDown { Minimum = 1, Maximum = 20, Value = 1, FormatString = "0", Width = 90 };
        var statusTxt = new TextBlock { FontSize = 12, Foreground = Brushes.Gray, Text = $"纸张尺寸：{wMm:0.#}mm × {hMm:0.#}mm（{template.Orientation}）" };

        var ctxSummary = string.Join("  |  ", context.Where(kv => !kv.Key.StartsWith("system.") && !string.IsNullOrWhiteSpace(kv.Value)).Take(6).Select(kv => $"{kv.Key}={kv.Value}"));
        var ctxBanner = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(ctxSummary) ? "当前页面未选中具体记录（仅含公共字段）" : "当前页面数据上下文：" + ctxSummary,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#1D4ED8"))
        };

        var win = new Window
        {
            Title = $"打印预览 — {template.TemplateName}",
            Width = Math.Clamp(wMm * scale + 120, 540, 960),
            Height = Math.Clamp(hMm * scale + 220, 420, 820),
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };

        var printBtn = new Button { Content = "🖨️ 立即打印（调起系统打印）", Height = 34, Padding = new Thickness(16, 0) };
        printBtn.Classes.Add("Primary");
        printBtn.Click += async (_, _) =>
        {
            printBtn.IsEnabled = false;
            int copies = (int)(copiesBox.Value ?? 1);
            string outPath = await ExportAndPrintAsync(template, triggerSlot, context, copies);
            statusTxt.Text = $"已发送至系统打印队列并归档：{outPath}";
            printBtn.IsEnabled = true;
        };

        var closeBtn = new Button { Content = "关闭", Height = 34, Padding = new Thickness(14, 0) };
        closeBtn.Click += (_, _) => win.Close();

        var topBar = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = $"{template.TemplateName} [{template.TemplateCode}]", FontSize = 15, FontWeight = FontWeight.Bold },
                ctxBanner
            }
        };

        var paperFrame = new Border
        {
            Padding = new Thickness(12),
            Background = new SolidColorBrush(Color.Parse("#E5E7EB")),
            CornerRadius = new CornerRadius(8),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    BorderBrush = new SolidColorBrush(Color.Parse("#9CA3AF")),
                    BorderThickness = new Thickness(1),
                    Child = previewCanvas
                }
            }
        };

        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(statusTxt, 0);
        var copiesLbl = new TextBlock { Text = "份数：", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(copiesLbl, 1);
        Grid.SetColumn(copiesBox, 2);
        Grid.SetColumn(printBtn, 3);
        Grid.SetColumn(closeBtn, 4);
        footer.Children.Add(statusTxt);
        footer.Children.Add(copiesLbl);
        footer.Children.Add(copiesBox);
        footer.Children.Add(printBtn);
        footer.Children.Add(closeBtn);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(16), RowSpacing = 12 };
        Grid.SetRow(topBar, 0);
        Grid.SetRow(paperFrame, 1);
        Grid.SetRow(footer, 2);
        root.Children.Add(topBar);
        root.Children.Add(paperFrame);
        root.Children.Add(footer);

        win.Content = root;
        if (owner != null) win.Show(owner);
        else win.Show();
    }
}
