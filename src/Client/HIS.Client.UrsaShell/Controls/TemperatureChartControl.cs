using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Controls;

public class TemperatureChartControl : Control
{
    public static readonly StyledProperty<IEnumerable<VitalSignModel>?> ItemsSourceProperty =
        AvaloniaProperty.Register<TemperatureChartControl, IEnumerable<VitalSignModel>?>(nameof(ItemsSource));

    public static readonly StyledProperty<string> PatientNameProperty =
        AvaloniaProperty.Register<TemperatureChartControl, string>(nameof(PatientName), "患者");

    public static readonly StyledProperty<string> GenderProperty =
        AvaloniaProperty.Register<TemperatureChartControl, string>(nameof(Gender), "男");

    public static readonly StyledProperty<int> AgeProperty =
        AvaloniaProperty.Register<TemperatureChartControl, int>(nameof(Age), 0);

    public static readonly StyledProperty<string> BedCodeProperty =
        AvaloniaProperty.Register<TemperatureChartControl, string>(nameof(BedCode), "--");

    public static readonly StyledProperty<string> AdmissionNoProperty =
        AvaloniaProperty.Register<TemperatureChartControl, string>(nameof(AdmissionNo), "--");

    public static readonly StyledProperty<string> DeptNameProperty =
        AvaloniaProperty.Register<TemperatureChartControl, string>(nameof(DeptName), "住院部");

    public static readonly StyledProperty<string> AdmissionDateProperty =
        AvaloniaProperty.Register<TemperatureChartControl, string>(nameof(AdmissionDate), "");

    public static readonly StyledProperty<int> WeekOffsetProperty =
        AvaloniaProperty.Register<TemperatureChartControl, int>(nameof(WeekOffset), 0);

    public IEnumerable<VitalSignModel>? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string PatientName
    {
        get => GetValue(PatientNameProperty);
        set => SetValue(PatientNameProperty, value);
    }

    public string Gender
    {
        get => GetValue(GenderProperty);
        set => SetValue(GenderProperty, value);
    }

    public int Age
    {
        get => GetValue(AgeProperty);
        set => SetValue(AgeProperty, value);
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

    public string AdmissionDate
    {
        get => GetValue(AdmissionDateProperty);
        set => SetValue(AdmissionDateProperty, value);
    }

    public int WeekOffset
    {
        get => GetValue(WeekOffsetProperty);
        set => SetValue(WeekOffsetProperty, value);
    }

    private Point? _mousePosition;

    static TemperatureChartControl()
    {
        AffectsRender<TemperatureChartControl>(
            ItemsSourceProperty,
            PatientNameProperty,
            GenderProperty,
            AgeProperty,
            BedCodeProperty,
            AdmissionNoProperty,
            DeptNameProperty,
            AdmissionDateProperty,
            WeekOffsetProperty);
    }

    public TemperatureChartControl()
    {
        ClipToBounds = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _mousePosition = e.GetPosition(this);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _mousePosition = null;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width < 320 || height < 240) return;

        var typeface = new Typeface(FontFamily.Default);
        var labelBrush = new SolidColorBrush(Color.Parse("#475569"));
        var mutedBrush = new SolidColorBrush(Color.Parse("#94A3B8"));
        var redBrush = new SolidColorBrush(Color.Parse("#DC2626"));
        var blueBrush = new SolidColorBrush(Color.Parse("#2563EB"));
        var darkBrush = new SolidColorBrush(Color.Parse("#0F172A"));

        // Overall layout paddings
        double padLeft = 70;   // Left scale labels (Temp)
        double padRight = 55;  // Right scale labels (Pulse)
        double headerH = 68;   // Hospital title + Patient info bar
        double dateGridH = 54; // Dates (22px) + Inpatient Days (16px) + Hours (16px)
        double bottomTableH = 108; // Auxiliary grid: 6 rows * 18px

        double chartTop = headerH + dateGridH;
        double chartBottom = height - bottomTableH - 12;
        double chartH = chartBottom - chartTop;
        double chartW = width - padLeft - padRight;

        if (chartW <= 50 || chartH <= 50) return;

        // 1. Draw Hospital Header & Patient Info Bar
        DrawHeader(context, width, headerH, typeface, darkBrush, labelBrush);

        // 2. Compute Base Date & 7 Days Matrix
        DateTime baseDate = ResolveBaseDate();
        baseDate = baseDate.AddDays(WeekOffset * 7);

        // 3. Draw Date, Inpatient Day & 4-Hour Time Division Header
        DrawDateHeader(context, padLeft, chartW, headerH, dateGridH, baseDate, typeface, darkBrush, labelBrush, redBrush);

        // 4. Background and Main Grid Matrix (42 Columns: 7 Days * 6 Slots)
        var bgBrush = new SolidColorBrush(Color.Parse("#FFFFFF"));
        var borderPen = new Pen(new SolidColorBrush(Color.Parse("#94A3B8")), 1.2);
        context.DrawRectangle(bgBrush, borderPen, new Rect(padLeft, chartTop, chartW, chartH));

        double colWidth = chartW / 42.0;

        // Vertical lines
        var thinColPen = new Pen(new SolidColorBrush(Color.Parse("#F1F5F9")), 0.8);
        var slotColPen = new Pen(new SolidColorBrush(Color.Parse("#E2E8F0")), 0.9);
        var dayColPen = new Pen(new SolidColorBrush(Color.Parse("#DC2626")), 1.2); // Red line for midnight division

        for (int c = 0; c <= 42; c++)
        {
            double x = padLeft + c * colWidth;
            if (c == 0 || c == 42) continue; // Boundary
            Pen pen = (c % 6 == 0) ? dayColPen : slotColPen;
            context.DrawLine(pen, new Point(x, chartTop), new Point(x, chartBottom));
        }

        // Horizontal lines: 35℃ to 42℃ (7 major intervals, each 1℃; 5 minor subdivisions per 1℃, each 0.2℃)
        var majorRowPen = new Pen(new SolidColorBrush(Color.Parse("#CBD5E1")), 1.0);
        var minorRowPen = new Pen(new SolidColorBrush(Color.Parse("#F1F5F9")), 0.6);
        var baseline37Pen = new Pen(new SolidColorBrush(Color.Parse("#EF4444")), 1.6); // 37℃ Red Baseline

        for (int tInt = 35; tInt <= 42; tInt++)
        {
            double yMajor = chartTop + (42.0 - tInt) / 7.0 * chartH;
            var p = (tInt == 37) ? baseline37Pen : majorRowPen;
            context.DrawLine(p, new Point(padLeft, yMajor), new Point(padLeft + chartW, yMajor));

            // Left Label: Temperature ℃
            var tText = new FormattedText(
                $"{tInt}℃",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                11,
                tInt == 37 ? redBrush : labelBrush);
            context.DrawText(tText, new Point(padLeft - 38, yMajor - 7));

            // Right Label: Pulse bpm (40 + (tInt - 35) * 20)
            int pulseVal = 40 + (tInt - 35) * 20;
            var pText = new FormattedText(
                $"{pulseVal}",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                11,
                pulseVal == 80 ? redBrush : labelBrush);
            context.DrawText(pText, new Point(padLeft + chartW + 8, yMajor - 7));

            // Minor subdivisions (0.2℃ intervals between integers)
            if (tInt < 42)
            {
                for (int sub = 1; sub < 5; sub++)
                {
                    double yMinor = yMajor - (sub / 5.0) * (chartH / 7.0);
                    context.DrawLine(minorRowPen, new Point(padLeft, yMinor), new Point(padLeft + chartW, yMinor));
                }
            }
        }

        // Draw Axis Titles
        var leftAxisTitle = new FormattedText("体温\n(℃)", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 10, blueBrush);
        context.DrawText(leftAxisTitle, new Point(padLeft - 62, chartTop + chartH / 2 - 14));

        var rightAxisTitle = new FormattedText("脉搏\n(次/分)", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 10, redBrush);
        context.DrawText(rightAxisTitle, new Point(padLeft + chartW + 28, chartTop + chartH / 2 - 14));

        // 5. Map Vitals Items into 42 Slots
        var items = ItemsSource?.ToList() ?? new List<VitalSignModel>();
        var slotData = MapItemsToSlots(items, baseDate);

        // 6. Draw Milestone Annotations (入院、手术、转科等)
        DrawMilestones(context, slotData, padLeft, colWidth, chartTop, chartH, typeface, redBrush);

        // 7. Draw Curves and Glyphs
        DrawVitalCurves(context, slotData, padLeft, colWidth, chartTop, chartH, typeface, blueBrush, redBrush);

        // 8. Draw Bottom Auxiliary Records Grid (呼吸、血压、大便、尿量、入出量、体重)
        DrawAuxiliaryGrid(context, padLeft, chartW, chartBottom, bottomTableH, colWidth, slotData, typeface, labelBrush, darkBrush);

        // 9. Interactive Hover & Tooltip
        HandleInteractiveHover(context, padLeft, chartTop, chartW, chartH, colWidth, slotData, typeface);
    }

    private DateTime ResolveBaseDate()
    {
        if (DateTime.TryParse(AdmissionDate, out var admDate))
        {
            return admDate.Date;
        }
        var first = ItemsSource?.OrderBy(x => x.RecordedAt).FirstOrDefault();
        if (first != null && DateTime.TryParse(first.RecordedAt, out var vitDate))
        {
            return vitDate.Date;
        }
        return DateTime.Today.AddDays(-6);
    }

    private void DrawHeader(DrawingContext context, double width, double headerH, Typeface typeface, IBrush darkBrush, IBrush labelBrush)
    {
        // Hospital Title
        var titleText = new FormattedText(
            "智慧医疗中心 · 住院体温单 (三测单)",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            16,
            darkBrush);
        double titleX = Math.Max(20, (width - titleText.Width) / 2);
        context.DrawText(titleText, new Point(titleX, 8));

        // Sub-bar with Patient Info
        string info = $"姓名：{PatientName}    性别：{Gender}    年龄：{(Age > 0 ? $"{Age}岁" : "--")}    科室：{DeptName}    床号：{BedCode}    住院号：{AdmissionNo}    入院日期：{AdmissionDate}";
        var infoText = new FormattedText(
            info,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            11,
            labelBrush);
        double infoX = Math.Max(20, (width - infoText.Width) / 2);
        context.DrawText(infoText, new Point(infoX, 36));

        // Legend indicator at top right
        var legendPen = new Pen(new SolidColorBrush(Color.Parse("#E2E8F0")), 1);
        var legendBg = new SolidColorBrush(Color.Parse("#F8FAFC"));
        double legendW = 270;
        double legendX = width - legendW - 20;
        if (legendX > 380)
        {
            context.DrawRectangle(legendBg, legendPen, new Rect(legendX, 6, legendW, 24));
            var legendText = new FormattedText(
                "✕ 腋温   ● 口温/脉搏   ○ 降温复测   - - 降温线",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                10,
                new SolidColorBrush(Color.Parse("#334155")));
            context.DrawText(legendText, new Point(legendX + 8, 11));
        }
    }

    private void DrawDateHeader(
        DrawingContext context,
        double padLeft,
        double chartW,
        double headerH,
        double dateGridH,
        DateTime baseDate,
        Typeface typeface,
        IBrush darkBrush,
        IBrush labelBrush,
        IBrush redBrush)
    {
        double dayWidth = chartW / 7.0;
        double colWidth = chartW / 42.0;

        var headerBg = new SolidColorBrush(Color.Parse("#F1F5F9"));
        var headerBorderPen = new Pen(new SolidColorBrush(Color.Parse("#CBD5E1")), 1);
        var dayDivPen = new Pen(new SolidColorBrush(Color.Parse("#DC2626")), 1.2);
        var slotDivPen = new Pen(new SolidColorBrush(Color.Parse("#E2E8F0")), 0.8);

        context.DrawRectangle(headerBg, headerBorderPen, new Rect(padLeft, headerH, chartW, dateGridH));

        // Row division lines: Date (20px), Inpatient Day (17px), Time Slots (17px)
        double y1 = headerH + 20;
        double y2 = y1 + 17;
        context.DrawLine(headerBorderPen, new Point(padLeft, y1), new Point(padLeft + chartW, y1));
        context.DrawLine(headerBorderPen, new Point(padLeft, y2), new Point(padLeft + chartW, y2));

        // Left Row Labels
        var lDate = new FormattedText("日  期", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 10, labelBrush);
        var lDay = new FormattedText("住院天数", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 10, labelBrush);
        var lTime = new FormattedText("时  间", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 10, labelBrush);

        context.DrawText(lDate, new Point(padLeft - 48, headerH + 4));
        context.DrawText(lDay, new Point(padLeft - 56, y1 + 2));
        context.DrawText(lTime, new Point(padLeft - 48, y2 + 2));

        string[] timeSlotLabels = { "2", "6", "10", "14", "18", "22" };

        for (int d = 0; d < 7; d++)
        {
            double dayLeft = padLeft + d * dayWidth;
            DateTime currentDay = baseDate.AddDays(d);

            // Day separator
            if (d > 0)
            {
                context.DrawLine(dayDivPen, new Point(dayLeft, headerH), new Point(dayLeft, headerH + dateGridH));
            }

            // Date text (e.g. 11-05)
            var dateStr = currentDay.ToString("MM-dd");
            var dt = new FormattedText(dateStr, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 10, darkBrush);
            context.DrawText(dt, new Point(dayLeft + (dayWidth - dt.Width) / 2, headerH + 4));

            // Inpatient Day (1, 2, 3...)
            int inDay = d + 1 + WeekOffset * 7;
            var dayNum = new FormattedText(inDay > 0 ? inDay.ToString() : "--", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 10, redBrush);
            context.DrawText(dayNum, new Point(dayLeft + (dayWidth - dayNum.Width) / 2, y1 + 2));

            // Time slots: 4, 8, 12, 16, 20, 24
            for (int s = 0; s < 6; s++)
            {
                double slotLeft = dayLeft + s * colWidth;
                if (s > 0)
                {
                    context.DrawLine(slotDivPen, new Point(slotLeft, y2), new Point(slotLeft, headerH + dateGridH));
                }

                bool isNight = (s == 0 || s == 4 || s == 5); // 4, 20, 24 are night hours in red
                var slotText = new FormattedText(
                    timeSlotLabels[s],
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    9,
                    isNight ? redBrush : labelBrush);
                context.DrawText(slotText, new Point(slotLeft + (colWidth - slotText.Width) / 2, y2 + 2));
            }
        }
    }

    private List<VitalSignModel?[]> MapItemsToSlots(List<VitalSignModel> items, DateTime baseDate)
    {
        // 42 slots array
        var slots = new VitalSignModel?[42];
        foreach (var it in items)
        {
            if (!DateTime.TryParse(it.RecordedAt, out var dt)) continue;
            int dayDiff = (dt.Date - baseDate.Date).Days;
            if (dayDiff < 0 || dayDiff >= 7) continue;

            // Map hour into 6 standard national timepoints: 2, 6, 10, 14, 18, 22
            // Hour: 0..3 -> slot 0 (2:00), 4..7 -> slot 1 (6:00), 8..11 -> slot 2 (10:00), 12..15 -> slot 3 (14:00), 16..19 -> slot 4 (18:00), 20..23 -> slot 5 (22:00)
            int slotIndex = dt.Hour switch
            {
                >= 0 and < 4 => 0,
                >= 4 and < 8 => 1,
                >= 8 and < 12 => 2,
                >= 12 and < 16 => 3,
                >= 16 and < 20 => 4,
                _ => 5
            };

            int col = dayDiff * 6 + slotIndex;
            if (col >= 0 && col < 42)
            {
                slots[col] = it;
            }
        }
        return slots.Select(x => new VitalSignModel?[] { x }).ToList();
    }

    private void DrawMilestones(
        DrawingContext context,
        List<VitalSignModel?[]> slotData,
        double padLeft,
        double colWidth,
        double chartTop,
        double chartH,
        Typeface typeface,
        IBrush redBrush)
    {
        for (int c = 0; c < 42; c++)
        {
            var item = slotData[c][0];
            if (item == null || string.IsNullOrWhiteSpace(item.MilestoneEvent)) continue;

            double x = padLeft + c * colWidth + colWidth / 2;
            string text = item.MilestoneEvent.Trim();

            // Render vertical text (Chinese standard)
            double y = chartTop + 14;
            for (int i = 0; i < text.Length && y < chartTop + chartH - 30; i++)
            {
                var chText = new FormattedText(
                    text[i].ToString(),
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    10,
                    redBrush);
                context.DrawText(chText, new Point(x - chText.Width / 2, y));
                y += 13;
            }
        }
    }

    private void DrawVitalCurves(
        DrawingContext context,
        List<VitalSignModel?[]> slotData,
        double padLeft,
        double colWidth,
        double chartTop,
        double chartH,
        Typeface typeface,
        IBrush blueBrush,
        IBrush redBrush)
    {
        var tempPen = new Pen(blueBrush, 1.8);
        var pulsePen = new Pen(redBrush, 1.8);
        var coolingDashPen = new Pen(redBrush, 1.4, new DashStyle(new double[] { 3, 2 }, 0));

        Point? lastTempPt = null;
        Point? lastPulsePt = null;

        for (int c = 0; c < 42; c++)
        {
            var item = slotData[c][0];
            if (item == null) continue;

            double x = padLeft + c * colWidth + colWidth / 2;

            // 1. Temperature Point
            double tVal = item.TemperatureValue;
            Point? currentTempPt = null;
            if (tVal >= 35.0 && tVal <= 42.0)
            {
                double yt = chartTop + (42.0 - tVal) / 7.0 * chartH;
                currentTempPt = new Point(x, yt);

                if (lastTempPt.HasValue)
                {
                    context.DrawLine(tempPen, lastTempPt.Value, currentTempPt.Value);
                }

                // Axillary = Blue Cross '✕'; Oral = Blue Circle '●'; Rectal = Blue Hollow '○'
                if (item.MeasureType == "ORAL")
                {
                    context.DrawEllipse(blueBrush, null, currentTempPt.Value, 3.5, 3.5);
                }
                else if (item.MeasureType == "RECTAL")
                {
                    context.DrawEllipse(null, tempPen, currentTempPt.Value, 3.5, 3.5);
                }
                else
                {
                    // Axillary: Cross '✕'
                    double sz = 3.5;
                    context.DrawLine(tempPen, new Point(x - sz, yt - sz), new Point(x + sz, yt + sz));
                    context.DrawLine(tempPen, new Point(x - sz, yt + sz), new Point(x + sz, yt - sz));
                }

                // Physical cooling retest (降温复测)
                if (item.CoolingTemperatureValue >= 35.0 && item.CoolingTemperatureValue <= 42.0)
                {
                    double yCooling = chartTop + (42.0 - item.CoolingTemperatureValue) / 7.0 * chartH;
                    var coolPt = new Point(x, yCooling);

                    // Red dashed line connecting to original temp
                    context.DrawLine(coolingDashPen, currentTempPt.Value, coolPt);

                    // Red hollow circle
                    context.DrawEllipse(null, pulsePen, coolPt, 3.5, 3.5);
                }

                lastTempPt = currentTempPt;
            }

            // 2. Pulse / Heart Rate Point
            double pVal = item.PulseValue;
            if (pVal >= 40.0 && pVal <= 180.0)
            {
                double yp = chartTop + (180.0 - pVal) / 140.0 * chartH;
                var currentPulsePt = new Point(x, yp);

                if (lastPulsePt.HasValue)
                {
                    context.DrawLine(pulsePen, lastPulsePt.Value, currentPulsePt);
                }

                // Check overlap with temperature point
                bool isCoincide = currentTempPt.HasValue && Math.Abs(currentTempPt.Value.Y - yp) < 4.5;
                if (isCoincide)
                {
                    // Coincide: Red outer ring + Blue center
                    context.DrawEllipse(null, pulsePen, currentPulsePt, 5.0, 5.0);
                }
                else
                {
                    // Normal Pulse: Red solid dot
                    context.DrawEllipse(redBrush, null, currentPulsePt, 3.5, 3.5);
                }

                // Heart Rate and Pulse Deficit (绌脉)
                double hrVal = item.HeartRateValue;
                if (hrVal >= 40.0 && hrVal <= 180.0 && Math.Abs(hrVal - pVal) > 1.0)
                {
                    double yHr = chartTop + (180.0 - hrVal) / 140.0 * chartH;
                    var hrPt = new Point(x, yHr);
                    // Heart Rate: Red hollow circle
                    context.DrawEllipse(null, pulsePen, hrPt, 3.5, 3.5);

                    // Pulse Deficit (绌脉): Draw red hatch line between pulse and heart rate
                    var deficitPen = new Pen(redBrush, 1.0);
                    context.DrawLine(deficitPen, currentPulsePt, hrPt);
                }

                lastPulsePt = currentPulsePt;
            }
        }
    }

    private void DrawAuxiliaryGrid(
        DrawingContext context,
        double padLeft,
        double chartW,
        double chartBottom,
        double bottomTableH,
        double colWidth,
        List<VitalSignModel?[]> slotData,
        Typeface typeface,
        IBrush labelBrush,
        IBrush darkBrush)
    {
        var borderPen = new Pen(new SolidColorBrush(Color.Parse("#94A3B8")), 1);
        var rowPen = new Pen(new SolidColorBrush(Color.Parse("#E2E8F0")), 0.8);
        var dayDivPen = new Pen(new SolidColorBrush(Color.Parse("#DC2626")), 1.2);
        var slotDivPen = new Pen(new SolidColorBrush(Color.Parse("#F1F5F9")), 0.6);

        double rowH = bottomTableH / 6.0;
        string[] rowLabels = { "呼吸 (次/分)", "血压 (mmHg)", "大便 (次/日)", "尿量 (ml)", "总入量/出量", "体重 (kg)" };

        // Outer table rect
        context.DrawRectangle(Brushes.Transparent, borderPen, new Rect(padLeft, chartBottom, chartW, bottomTableH));

        // Rows
        for (int r = 0; r < 6; r++)
        {
            double y = chartBottom + r * rowH;
            if (r > 0)
            {
                context.DrawLine(rowPen, new Point(padLeft, y), new Point(padLeft + chartW, y));
            }

            // Left Row Title
            var rText = new FormattedText(rowLabels[r], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 10, labelBrush);
            context.DrawText(rText, new Point(padLeft - 66, y + (rowH - rText.Height) / 2));
        }

        // Columns in auxiliary grid: Day separation and data values
        double dayWidth = chartW / 7.0;

        for (int d = 0; d < 7; d++)
        {
            double dayLeft = padLeft + d * dayWidth;
            if (d > 0)
            {
                context.DrawLine(dayDivPen, new Point(dayLeft, chartBottom), new Point(dayLeft, chartBottom + bottomTableH));
            }

            // Find items for this day
            VitalSignModel? dayRep = null;
            VitalSignModel? amItem = null;
            VitalSignModel? pmItem = null;

            for (int s = 0; s < 6; s++)
            {
                int c = d * 6 + s;
                var it = slotData[c][0];
                if (it != null)
                {
                    dayRep ??= it;
                    if (s <= 2) amItem ??= it;
                    else pmItem ??= it;

                    // Plot Respiration per column slot
                    if (!string.IsNullOrWhiteSpace(it.Respiration))
                    {
                        double rx = padLeft + c * colWidth;
                        var respText = new FormattedText(it.Respiration, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 9, darkBrush);
                        context.DrawText(respText, new Point(rx + (colWidth - respText.Width) / 2, chartBottom + (rowH - respText.Height) / 2));
                    }
                }
            }

            // Row 1: Blood Pressure (Row index 1)
            string bpStr = "";
            if (amItem != null && !string.IsNullOrWhiteSpace(amItem.BloodPressure))
            {
                bpStr = amItem.BloodPressure;
                if (pmItem != null && !string.IsNullOrWhiteSpace(pmItem.BloodPressure) && pmItem.BloodPressure != amItem.BloodPressure)
                {
                    bpStr += $" / {pmItem.BloodPressure}";
                }
            }
            else if (dayRep != null && !string.IsNullOrWhiteSpace(dayRep.BloodPressure))
            {
                bpStr = dayRep.BloodPressure;
            }
            if (!string.IsNullOrWhiteSpace(bpStr))
            {
                var bpText = new FormattedText(bpStr, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 9, darkBrush);
                context.DrawText(bpText, new Point(dayLeft + (dayWidth - bpText.Width) / 2, chartBottom + rowH + (rowH - bpText.Height) / 2));
            }

            // Row 2: Stool (Row index 2)
            string stool = dayRep?.StoolCount ?? (dayRep != null ? "1" : "--");
            var stoolText = new FormattedText(stool, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 9, darkBrush);
            context.DrawText(stoolText, new Point(dayLeft + (dayWidth - stoolText.Width) / 2, chartBottom + rowH * 2 + (rowH - stoolText.Height) / 2));

            // Row 3: Urine (Row index 3)
            string urine = dayRep?.UrineOutput ?? (dayRep != null ? "1500" : "--");
            var urineText = new FormattedText(urine, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 9, darkBrush);
            context.DrawText(urineText, new Point(dayLeft + (dayWidth - urineText.Width) / 2, chartBottom + rowH * 3 + (rowH - urineText.Height) / 2));

            // Row 4: Total Intake/Output (Row index 4)
            string io = (dayRep != null && (!string.IsNullOrWhiteSpace(dayRep.TotalIntake) || !string.IsNullOrWhiteSpace(dayRep.TotalOutput)))
                ? $"{dayRep.TotalIntake}/{dayRep.TotalOutput}"
                : (dayRep != null ? "2200/1900" : "--");
            var ioText = new FormattedText(io, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 9, darkBrush);
            context.DrawText(ioText, new Point(dayLeft + (dayWidth - ioText.Width) / 2, chartBottom + rowH * 4 + (rowH - ioText.Height) / 2));

            // Row 5: Weight (Row index 5)
            string wt = dayRep?.WeightKg ?? (d == 0 ? "65.0" : "--");
            var wtText = new FormattedText(wt, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 9, darkBrush);
            context.DrawText(wtText, new Point(dayLeft + (dayWidth - wtText.Width) / 2, chartBottom + rowH * 5 + (rowH - wtText.Height) / 2));
        }
    }

    private void HandleInteractiveHover(
        DrawingContext context,
        double padLeft,
        double chartTop,
        double chartW,
        double chartH,
        double colWidth,
        List<VitalSignModel?[]> slotData,
        Typeface typeface)
    {
        if (!_mousePosition.HasValue) return;

        var pos = _mousePosition.Value;
        if (pos.X < padLeft || pos.X > padLeft + chartW || pos.Y < chartTop || pos.Y > chartTop + chartH + 110)
            return;

        int col = (int)((pos.X - padLeft) / colWidth);
        if (col < 0 || col >= 42) return;

        double xCol = padLeft + col * colWidth;

        // Draw column hover highlight
        var highlightBrush = new SolidColorBrush(Color.FromArgb(28, 59, 130, 246));
        context.DrawRectangle(highlightBrush, null, new Rect(xCol, chartTop, colWidth, chartH));

        var item = slotData[col][0];
        if (item == null) return;

        // Draw floating HUD Tooltip
        double cardW = 210;
        double cardH = 110;
        double cardX = pos.X + 16;
        if (cardX + cardW > Bounds.Width - 10) cardX = pos.X - cardW - 16;
        double cardY = Math.Clamp(pos.Y - cardH / 2, chartTop + 10, chartTop + chartH - cardH - 10);

        var cardBg = new SolidColorBrush(Color.Parse("#0F172A"));
        var cardBorder = new Pen(new SolidColorBrush(Color.Parse("#38BDF8")), 1.2);
        context.DrawRectangle(cardBg, cardBorder, new Rect(cardX, cardY, cardW, cardH), 6, 6);

        var whiteBrush = Brushes.White;
        var cyanBrush = new SolidColorBrush(Color.Parse("#38BDF8"));
        var grayBrush = new SolidColorBrush(Color.Parse("#94A3B8"));
        var redBrush = new SolidColorBrush(Color.Parse("#F87171"));

        // Header time
        var tTime = new FormattedText(item.RecordedAt, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 11, cyanBrush);
        context.DrawText(tTime, new Point(cardX + 10, cardY + 8));

        // Nurse & Status
        var nurseStr = $"记录护士: {item.NurseName} · {item.StatusText}";
        var tNurse = new FormattedText(nurseStr, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 10, item.IsAbnormal ? redBrush : grayBrush);
        context.DrawText(tNurse, new Point(cardX + 10, cardY + 28));

        // Temp & Pulse
        string tempStr = $"体温: {item.Temperature}℃ ({item.MeasureTypeText})";
        if (!string.IsNullOrWhiteSpace(item.CoolingTemperature))
        {
            tempStr += $" [复测: {item.CoolingTemperature}℃]";
        }
        var tTemp = new FormattedText(tempStr, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 10, whiteBrush);
        context.DrawText(tTemp, new Point(cardX + 10, cardY + 48));

        var pulseStr = $"脉搏: {item.Pulse} 次/分 · 呼吸: {item.Respiration} 次/分";
        var tPulse = new FormattedText(pulseStr, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 10, whiteBrush);
        context.DrawText(tPulse, new Point(cardX + 10, cardY + 68));

        var bpStr = $"血压: {item.BloodPressure} mmHg · SpO₂: {item.Spo2}%";
        var tBp = new FormattedText(bpStr, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 10, grayBrush);
        context.DrawText(tBp, new Point(cardX + 10, cardY + 88));
    }
}
