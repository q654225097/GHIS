using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Printing;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Shell.Controls;

/// <summary>
/// 动态打印触发位控件：挂载在业务页面的任意位置（顶部工具栏 / 列表行操作区 / 详情底部），
/// 自动拉取管理员为该页面+该插槽配置的打印按钮，点击时实时提取当前页面的选中患者与业务上下文执行打印。
/// </summary>
public sealed class DynamicPrintTriggerBar : UserControl
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static readonly StyledProperty<string> PageKeyProperty =
        AvaloniaProperty.Register<DynamicPrintTriggerBar, string>(nameof(PageKey), string.Empty);

    public static readonly StyledProperty<string> TriggerSlotProperty =
        AvaloniaProperty.Register<DynamicPrintTriggerBar, string>(nameof(TriggerSlot), "TOP_TOOLBAR");

    public static readonly StyledProperty<object?> RowContextProperty =
        AvaloniaProperty.Register<DynamicPrintTriggerBar, object?>(nameof(RowContext));

    public static readonly StyledProperty<bool> CompactProperty =
        AvaloniaProperty.Register<DynamicPrintTriggerBar, bool>(nameof(Compact), false);

    private readonly StackPanel _panel = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        VerticalAlignment = VerticalAlignment.Center
    };

    public DynamicPrintTriggerBar()
    {
        Content = _panel;
    }

    public string PageKey
    {
        get => GetValue(PageKeyProperty);
        set => SetValue(PageKeyProperty, value);
    }

    public string TriggerSlot
    {
        get => GetValue(TriggerSlotProperty);
        set => SetValue(TriggerSlotProperty, value);
    }

    public object? RowContext
    {
        get => GetValue(RowContextProperty);
        set => SetValue(RowContextProperty, value);
    }

    public bool Compact
    {
        get => GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        PrintEngineService.TriggersInvalidated += OnTriggersInvalidated;
        _ = LoadTriggersAsync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        PrintEngineService.TriggersInvalidated -= OnTriggersInvalidated;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnTriggersInvalidated()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = LoadTriggersAsync());
    }

    private async Task LoadTriggersAsync()
    {
        if (string.IsNullOrWhiteSpace(PageKey) || string.IsNullOrWhiteSpace(TriggerSlot)) return;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            string url = $"/api/v4/print/triggers?pageKey={Uri.EscapeDataString(PageKey)}&slot={Uri.EscapeDataString(TriggerSlot)}";
            var res = await http.GetFromJsonAsync<ClientApiEnvelope<List<PrintTriggerItemDto>>>(url, JsonOpts);
            _panel.Children.Clear();
            if (res?.Data == null || res.Data.Count == 0) return;

            foreach (var item in res.Data)
            {
                var btn = new Button
                {
                    Content = string.IsNullOrWhiteSpace(item.ButtonIcon) ? item.ButtonLabel : $"{item.ButtonIcon} {item.ButtonLabel}",
                    Height = Compact ? 26 : 32,
                    Padding = Compact ? new Thickness(8, 0) : new Thickness(12, 0),
                    FontSize = Compact ? 11 : 12
                };
                btn.Classes.Add("Secondary");
                ToolTip.SetTip(btn, $"模板：{item.TemplateName}（{item.PaperWidthMm:0.#}×{item.PaperHeightMm:0.#}mm）");
                var captured = item;
                btn.Click += (_, _) => ExecuteTrigger(captured);
                _panel.Children.Add(btn);
            }
        }
        catch
        {
            // 网络异常时静默不阻塞主业务页面
        }
    }

    private void ExecuteTrigger(PrintTriggerItemDto trigger)
    {
        IPrintContextProvider? provider = FindContextProvider();
        var ctx = provider != null
            ? provider.BuildPrintContext(RowContext)
            : new Dictionary<string, string>();

        var tpl = new PrintTemplateDto
        {
            Id = trigger.TemplateId,
            TemplateCode = trigger.TemplateCode,
            TemplateName = trigger.TemplateName,
            Category = trigger.Category,
            PageKey = trigger.PageKey,
            PaperWidthMm = trigger.PaperWidthMm,
            PaperHeightMm = trigger.PaperHeightMm,
            Orientation = trigger.Orientation,
            MarginTopMm = trigger.MarginTopMm,
            MarginRightMm = trigger.MarginRightMm,
            MarginBottomMm = trigger.MarginBottomMm,
            MarginLeftMm = trigger.MarginLeftMm,
            LayoutJson = trigger.LayoutJson
        };

        var owner = TopLevel.GetTopLevel(this) as Window;
        PrintEngineService.OpenPreviewWindow(tpl, trigger.TriggerSlot, ctx, owner);
    }

    private IPrintContextProvider? FindContextProvider()
    {
        if (DataContext is IPrintContextProvider direct) return direct;
        foreach (var anc in this.GetVisualAncestors())
        {
            if (anc is Control ctrl && ctrl.DataContext is IPrintContextProvider p)
                return p;
        }
        return null;
    }
}
