using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Controls;

public class TemperatureChartControl : Control
{
    public static readonly StyledProperty<IEnumerable<VitalSignModel>?> ItemsSourceProperty =
        AvaloniaProperty.Register<TemperatureChartControl, IEnumerable<VitalSignModel>?>(nameof(ItemsSource));

    public IEnumerable<VitalSignModel>? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    static TemperatureChartControl()
    {
        AffectsRender<TemperatureChartControl>(ItemsSourceProperty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsSourceProperty)
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width < 100 || height < 100) return;

        double padLeft = 45;
        double padRight = 45;
        double padTop = 30;
        double padBottom = 30;

        double chartW = width - padLeft - padRight;
        double chartH = height - padTop - padBottom;
        if (chartW <= 0 || chartH <= 0) return;

        // Background
        var bgBrush = new SolidColorBrush(Color.Parse("#F8FAFC"));
        var borderPen = new Pen(new SolidColorBrush(Color.Parse("#E2E8F0")), 1);
        context.DrawRectangle(bgBrush, borderPen, new Rect(padLeft, padTop, chartW, chartH));

        var gridPen = new Pen(new SolidColorBrush(Color.Parse("#E2E8F0")), 0.8);
        var baselinePen = new Pen(new SolidColorBrush(Color.Parse("#FCA5A5")), 1);
        var labelBrush = new SolidColorBrush(Color.Parse("#64748B"));
        var typeface = new Typeface(FontFamily.Default);

        // Draw Temperature (35~42°C) grid & labels on Left; Pulse (40~180) on Right
        for (int t = 35; t <= 42; t++)
        {
            double y = padTop + (42.0 - t) / 7.0 * chartH;
            var penToUse = (t == 37) ? baselinePen : gridPen;
            context.DrawLine(penToUse, new Point(padLeft, y), new Point(padLeft + chartW, y));

            // Left label: 35~42°C
            var tempLabel = new FormattedText(
                $"{t}℃",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                10,
                t == 37 ? Brushes.Red : labelBrush);
            context.DrawText(tempLabel, new Point(padLeft - 32, y - 6));

            // Right label: pulse corresponding: 40 + (t - 35) * 20 (40 to 180)
            int pulseVal = 40 + (t - 35) * 20;
            var pulseLabel = new FormattedText(
                $"{pulseVal}",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                10,
                labelBrush);
            context.DrawText(pulseLabel, new Point(padLeft + chartW + 8, y - 6));
        }

        // Draw Legend at top
        var tempLegend = new FormattedText("● 体温 (35~42℃)", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 11, new SolidColorBrush(Color.Parse("#2563EB")));
        var pulseLegend = new FormattedText("▲ 脉搏 (40~180次)", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 11, new SolidColorBrush(Color.Parse("#DC2626")));
        context.DrawText(tempLegend, new Point(padLeft + 10, 8));
        context.DrawText(pulseLegend, new Point(padLeft + 140, 8));

        var items = ItemsSource?.Reverse().ToList();
        if (items == null || items.Count == 0)
        {
            var emptyText = new FormattedText("暂无体征折线数据", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 12, labelBrush);
            context.DrawText(emptyText, new Point(padLeft + chartW / 2 - 40, padTop + chartH / 2 - 8));
            return;
        }

        int count = items.Count;
        double stepX = count > 1 ? chartW / (count - 1) : chartW / 2;

        var tempPen = new Pen(new SolidColorBrush(Color.Parse("#2563EB")), 2);
        var tempBrush = new SolidColorBrush(Color.Parse("#2563EB"));
        var pulsePen = new Pen(new SolidColorBrush(Color.Parse("#DC2626")), 2);
        var pulseBrush = new SolidColorBrush(Color.Parse("#DC2626"));

        Point? prevTempPt = null;
        Point? prevPulsePt = null;

        for (int i = 0; i < count; i++)
        {
            var item = items[i];
            double x = count > 1 ? padLeft + i * stepX : padLeft + chartW / 2;

            // Draw vertical grid line
            context.DrawLine(gridPen, new Point(x, padTop), new Point(x, padTop + chartH));

            // Time label
            string timeStr = item.RecordedAt;
            if (timeStr.Length > 5) timeStr = timeStr[^5..];
            var timeLabel = new FormattedText(timeStr, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 9, labelBrush);
            context.DrawText(timeLabel, new Point(x - 14, padTop + chartH + 6));

            // Temperature point
            double tVal = item.TemperatureValue;
            if (tVal >= 35.0 && tVal <= 42.0)
            {
                double yt = padTop + (42.0 - tVal) / 7.0 * chartH;
                var pt = new Point(x, yt);
                if (prevTempPt.HasValue)
                {
                    context.DrawLine(tempPen, prevTempPt.Value, pt);
                }
                context.DrawEllipse(tempBrush, null, pt, 3.5, 3.5);
                prevTempPt = pt;
            }

            // Pulse point
            double pVal = item.PulseValue;
            if (pVal >= 40.0 && pVal <= 180.0)
            {
                double yp = padTop + (180.0 - pVal) / 140.0 * chartH;
                var pt = new Point(x, yp);
                if (prevPulsePt.HasValue)
                {
                    context.DrawLine(pulsePen, prevPulsePt.Value, pt);
                }
                context.DrawEllipse(pulseBrush, null, pt, 3.5, 3.5);
                prevPulsePt = pt;
            }
        }
    }
}
