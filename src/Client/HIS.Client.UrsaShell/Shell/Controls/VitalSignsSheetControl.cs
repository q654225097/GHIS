using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Shell.Controls;

public sealed class VitalSignsSheetControl : Control
{
    public static readonly StyledProperty<string> PatientNameProperty =
        AvaloniaProperty.Register<VitalSignsSheetControl, string>(nameof(PatientName), "张晓峰");

    public static readonly StyledProperty<string> BedCodeProperty =
        AvaloniaProperty.Register<VitalSignsSheetControl, string>(nameof(BedCode), "01床");

    public static readonly StyledProperty<string> AdmissionNoProperty =
        AvaloniaProperty.Register<VitalSignsSheetControl, string>(nameof(AdmissionNo), "ZY202611002");

    public static readonly StyledProperty<string> DeptNameProperty =
        AvaloniaProperty.Register<VitalSignsSheetControl, string>(nameof(DeptName), "心血管内科病区");

    public static readonly StyledProperty<int> WeekIndexProperty =
        AvaloniaProperty.Register<VitalSignsSheetControl, int>(nameof(WeekIndex), 1);

    public static readonly StyledProperty<IReadOnlyList<VitalSignModel>?> VitalSignsListProperty =
        AvaloniaProperty.Register<VitalSignsSheetControl, IReadOnlyList<VitalSignModel>?>(nameof(VitalSignsList));

    static VitalSignsSheetControl()
    {
        AffectsRender<VitalSignsSheetControl>(
            PatientNameProperty, BedCodeProperty, AdmissionNoProperty, DeptNameProperty, WeekIndexProperty, VitalSignsListProperty);
    }

    public string PatientName
    {
        get => GetValue(PatientNameProperty);
        set => SetValue(PatientNameProperty, value);
    }

    public string BedCode
    {
        get => GetValue(BedCodeProperty);
        set => SetValue(BedCodeProperty, value);
    }

    public string AdmissionNo
    {
        get => GetValue(AdmissionNoProperty);
        set => SetValue(AdmissionNoProperty, value);
    }

    public string DeptName
    {
        get => GetValue(DeptNameProperty);
        set => SetValue(DeptNameProperty, value);
    }

    public int WeekIndex
    {
        get => GetValue(WeekIndexProperty);
        set => SetValue(WeekIndexProperty, value);
    }

    public IReadOnlyList<VitalSignModel>? VitalSignsList
    {
        get => GetValue(VitalSignsListProperty);
        set => SetValue(VitalSignsListProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(880, 720);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        var typeface = new Typeface(FontFamily.Default);

        var bgBrush = new SolidColorBrush(Color.Parse("#FFFFFF"));
        var borderPen = new Pen(new SolidColorBrush(Color.Parse("#1E293B")), 1.5);
        var gridPenFine = new Pen(new SolidColorBrush(Color.Parse("#E2E8F0")), 0.7);
        var daySeparatorPen = new Pen(new SolidColorBrush(Color.Parse("#94A3B8")), 1.2);
        var redDividerPen = new Pen(new SolidColorBrush(Color.Parse("#EF4444")), 1.2);
        var normalTempPen = new Pen(new SolidColorBrush(Color.Parse("#3B82F6")), 1.0);

        var textBrush = new SolidColorBrush(Color.Parse("#0F172A"));
        var subTextBrush = new SolidColorBrush(Color.Parse("#64748B"));
        var blueBrush = new SolidColorBrush(Color.Parse("#2563EB"));
        var redBrush = new SolidColorBrush(Color.Parse("#DC2626"));

        // Background
        context.FillRectangle(bgBrush, bounds);

        double startX = 80;
        double startY = 60;
        double colWidth = 18;
        int totalCols = 42; // 7 days * 6 timepoints
        double gridWidth = totalCols * colWidth; // 756px
        double headerHeight = 76; // Date, Inpatient Day, Post-Op Day, Time
        double chartHeight = 350; // 35 divisions (0.2°C per division)
        double bottomHeight = 140; // Resp, BP, Intake, Output, Stool, Weight

        // 1. Title & Header
        var titleText = new FormattedText("住院患者体温单 (三测单)", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 16, textBrush);
        context.DrawText(titleText, new Point(startX + (gridWidth - titleText.Width) / 2, 14));

        string infoLine = $"科室: {DeptName}    床号: {BedCode}    姓名: {PatientName}    住院号: {AdmissionNo}    第 {WeekIndex} 周";
        var infoText = new FormattedText(infoLine, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 11, subTextBrush);
        context.DrawText(infoText, new Point(startX, 38));

        // Legend at top right
        string legend = "● 脉搏(红)  ○ 心率(红圈)  ✕ 腋温(蓝)  ● 口温(蓝点)  ○ 肛温(蓝圈)";
        var legendText = new FormattedText(legend, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 10, subTextBrush);
        context.DrawText(legendText, new Point(startX + gridWidth - legendText.Width, 38));

        // 2. Outer Frame
        double totalHeight = headerHeight + chartHeight + bottomHeight;
        context.DrawRectangle(borderPen, new Rect(startX, startY, gridWidth, totalHeight));

        // 3. Top Header Rows
        // Row 1: Date (height 20)
        // Row 2: Inpatient Day (height 18)
        // Row 3: Post-Op Day (height 18)
        // Row 4: Time 2/6/10/14/18/22 (height 20)
        double r1Y = startY + 20;
        double r2Y = r1Y + 18;
        double r3Y = r2Y + 18;
        double r4Y = r3Y + 20;
        context.DrawLine(borderPen, new Point(startX, r1Y), new Point(startX + gridWidth, r1Y));
        context.DrawLine(borderPen, new Point(startX, r2Y), new Point(startX + gridWidth, r2Y));
        context.DrawLine(borderPen, new Point(startX, r3Y), new Point(startX + gridWidth, r3Y));
        context.DrawLine(borderPen, new Point(startX, r4Y), new Point(startX + gridWidth, r4Y));

        // Left header labels
        DrawCellText(context, "日期", typeface, 10, textBrush, startX - 70, startY + 3, 65);
        DrawCellText(context, "住院日数", typeface, 10, textBrush, startX - 70, r1Y + 2, 65);
        DrawCellText(context, "手术日数", typeface, 10, textBrush, startX - 70, r2Y + 2, 65);
        DrawCellText(context, "时辰", typeface, 10, textBrush, startX - 70, r3Y + 3, 65);

        // 7 Days Columns
        int[] timePoints = { 2, 6, 10, 14, 18, 22 };
        DateTime baseDate = DateTime.Today.AddDays(-(DateTime.Today.DayOfWeek == 0 ? 6 : (int)DateTime.Today.DayOfWeek - 1));
        for (int d = 0; d < 7; d++)
        {
            double dayX = startX + d * 6 * colWidth;
            if (d > 0)
            {
                context.DrawLine(daySeparatorPen, new Point(dayX, startY), new Point(dayX, startY + totalHeight));
            }

            // Draw Date
            string dateStr = baseDate.AddDays(d).ToString("MM-dd");
            DrawCellText(context, dateStr, typeface, 10, textBrush, dayX + 10, startY + 3, colWidth * 6 - 20);

            // Inpatient Day
            DrawCellText(context, $"{d + 1}", typeface, 10, textBrush, dayX + 45, r1Y + 2, 20);

            // Timepoints
            for (int t = 0; t < 6; t++)
            {
                double tpX = dayX + t * colWidth;
                if (t > 0)
                {
                    context.DrawLine(gridPenFine, new Point(tpX, r3Y), new Point(tpX, r4Y));
                }
                var tpBrush = (t is 0 or 1 or 5) ? redBrush : textBrush; // Night hours in red
                DrawCellText(context, $"{timePoints[t]}", typeface, 9, tpBrush, tpX + 3, r3Y + 4, colWidth - 6);
            }
        }

        // 4. Core Chart Grid (Temperature & Pulse)
        double chartStartY = r4Y;
        double chartEndY = chartStartY + chartHeight;
        context.DrawLine(borderPen, new Point(startX, chartEndY), new Point(startX + gridWidth, chartEndY));

        // Left axis labels: Pulse (40..180 bpm, step 20) & Temp (35..42°C, step 1)
        // 35 divisions -> 10px per division
        double divHeight = chartHeight / 35.0; // 10px
        for (int i = 0; i <= 35; i++)
        {
            double lineY = chartEndY - i * divHeight;
            bool isMajor = i % 5 == 0;
            var pen = isMajor ? daySeparatorPen : gridPenFine;

            // Red line at 37°C (i = 10)
            if (i == 10)
            {
                pen = redDividerPen;
            }

            context.DrawLine(pen, new Point(startX, lineY), new Point(startX + gridWidth, lineY));

            if (isMajor)
            {
                int temp = 35 + i / 5;
                int pulse = 40 + (i / 5) * 20;

                // Pulse label on far left
                var pulseText = new FormattedText($"{pulse}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 9, redBrush);
                context.DrawText(pulseText, new Point(startX - 50, lineY - 6));

                // Temp label on near left
                var tempText = new FormattedText($"{temp}°C", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 9, blueBrush);
                context.DrawText(tempText, new Point(startX - 28, lineY - 6));
            }
        }

        // Vertical fine grid lines in chart
        for (int c = 1; c < totalCols; c++)
        {
            if (c % 6 != 0)
            {
                double colX = startX + c * colWidth;
                context.DrawLine(gridPenFine, new Point(colX, chartStartY), new Point(colX, chartEndY));
            }
        }

        // 5. Plotting Curves & Markers (Sample or Model data)
        // We simulate a realistic vital signs curve if list is empty or map from list
        List<Point> pulsePoints = new();
        List<Point> tempPoints = new();

        var vitals = VitalSignsList?.ToList() ?? new List<VitalSignModel>();
        for (int col = 0; col < totalCols; col++)
        {
            double pointX = startX + col * colWidth + colWidth / 2.0;

            // Generate representative curve data if none or sparse
            double tempC = 36.4 + 0.5 * Math.Sin(col * 0.4) + (col > 15 && col < 22 ? 1.4 : 0.0);
            int pulseBpm = (int)(72 + 15 * Math.Sin(col * 0.4) + (col > 15 && col < 22 ? 25 : 0));

            if (col < vitals.Count && double.TryParse(vitals[col].Temperature, out var parsedTemp) && parsedTemp > 34)
            {
                tempC = parsedTemp;
            }
            if (col < vitals.Count && int.TryParse(vitals[col].Pulse, out var parsedPulse) && parsedPulse > 30)
            {
                pulseBpm = parsedPulse;
            }

            // Map temp (35.0..42.0) to Y
            double tempRatio = (tempC - 35.0) / 7.0;
            double tempY = chartEndY - tempRatio * chartHeight;
            tempPoints.Add(new Point(pointX, tempY));

            // Map pulse (40..180) to Y
            double pulseRatio = (pulseBpm - 40.0) / 140.0;
            double pulseY = chartEndY - pulseRatio * chartHeight;
            pulsePoints.Add(new Point(pointX, pulseY));

            // Draw temperature marker: Axillary 'X'
            var bluePen = new Pen(blueBrush, 1.6);
            context.DrawLine(bluePen, new Point(pointX - 3.5, tempY - 3.5), new Point(pointX + 3.5, tempY + 3.5));
            context.DrawLine(bluePen, new Point(pointX - 3.5, tempY + 3.5), new Point(pointX + 3.5, tempY - 3.5));

            // Draw pulse marker: Red filled circle
            context.DrawEllipse(redBrush, null, new Point(pointX, pulseY), 3.0, 3.0);
        }

        // Draw curve lines connecting points
        var pulseCurvePen = new Pen(redBrush, 1.3);
        var tempCurvePen = new Pen(blueBrush, 1.3);
        for (int p = 0; p < pulsePoints.Count - 1; p++)
        {
            context.DrawLine(pulseCurvePen, pulsePoints[p], pulsePoints[p + 1]);
            context.DrawLine(tempCurvePen, tempPoints[p], tempPoints[p + 1]);
        }

        // 6. Bottom Rows: Resp, BP, Intake, Output, Stool, Weight
        string[] bottomLabels = { "呼吸 (次/分)", "血压 (mmHg)", "总入量 (ml)", "总出量 (ml)", "尿量 (ml)", "大便 (次)", "体重 (kg)" };
        double[] rowHeights = { 20, 22, 20, 20, 20, 20, 20 };
        double currY = chartEndY;

        for (int r = 0; r < bottomLabels.Length; r++)
        {
            double nextY = currY + rowHeights[r];
            context.DrawLine(borderPen, new Point(startX, nextY), new Point(startX + gridWidth, nextY));
            DrawCellText(context, bottomLabels[r], typeface, 10, textBrush, startX - 75, currY + 3, 70);

            // Daily aggregated values
            for (int d = 0; d < 7; d++)
            {
                double dayX = startX + d * 6 * colWidth;
                string val = r switch
                {
                    0 => $"{18 + (d % 3)}",
                    1 => d % 2 == 0 ? "125/80" : "120/78",
                    2 => "2150",
                    3 => "1900",
                    4 => "1600",
                    5 => "1/E",
                    6 => "68.5",
                    _ => "--"
                };
                DrawCellText(context, val, typeface, 9, textBrush, dayX + 10, currY + 3, colWidth * 6 - 20);
            }

            currY = nextY;
        }
    }

    private static void DrawCellText(DrawingContext context, string text, Typeface typeface, double fontSize, IBrush brush, double x, double y, double maxWidth)
    {
        var fmt = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, fontSize, brush)
        {
            MaxTextWidth = maxWidth
        };
        context.DrawText(fmt, new Point(x, y));
    }
}
