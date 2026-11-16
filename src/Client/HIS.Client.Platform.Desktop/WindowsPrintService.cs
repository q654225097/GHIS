using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Runtime.InteropServices;
using System.Text;
using HIS.BuildingBlocks.OfdEngine;
using HIS.Client.UrsaShell.Services;

namespace HIS.Client.Platform.Desktop;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class WindowsPrintService : IDesktopPrintService
{
    private const int PdNoPageNums = 0x00000008;
    private const int PdReturnDc = 0x00000100;
    private const int PdUseDevModeCopies = 0x00040000;
    private const int PrintDialogPrint = 1;
    private const int PrintDialogApply = 2;
    private const int StartPageGeneral = unchecked((int)0xFFFFFFFF);
    private const int MaxPrinterNameBytes = 8192;

    public bool IsSupported => OperatingSystem.IsWindows();

    public Task<string> PrintAsync(EmrPrintSnapshot snapshot, string ofdFilePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("系统打印对话框目前仅在 Windows 客户端提供。");
        }

        ClientDiagnosticsLog.Info(
            "Print.Desktop",
            $"PrintAsync started: docNo={snapshot.DocNo}, ofd={ofdFilePath}, pid={Environment.ProcessId}.");
        var printerName = ShowPrintDialog();
        if (printerName == null)
        {
            ClientDiagnosticsLog.Info("Print.Desktop", "Print dialog cancelled.");
            throw new OperationCanceledException("用户取消了打印。");
        }

        var documentName = $"{snapshot.TemplateName} - {snapshot.DocNo}";
        using var document = new PrintDocument
        {
            DocumentName = documentName,
            OriginAtMargins = false,
            PrintController = new StandardPrintController()
        };
        document.PrinterSettings.PrinterName = printerName;
        if (!document.PrinterSettings.IsValid)
        {
            ClientDiagnosticsLog.Warn(
                "Print.Desktop",
                $"Selected printer is unavailable: printer={printerName}, document={documentName}.");
            throw new InvalidOperationException($"打印机“{printerName}”当前不可用，请检查系统打印机连接。");
        }

        ConfigurePage(document.DefaultPageSettings, snapshot);

        var pageState = new PageState(snapshot);
        document.PrintPage += (_, e) =>
        {
            e.HasMorePages = DrawPage(e, pageState);
        };
        ClientDiagnosticsLog.Info(
            "Print.Desktop",
            $"PrintDocument.Print starting: printer={printerName}, document={documentName}, " +
            $"paper={snapshot.PageWidthMm}x{snapshot.PageHeightMm}mm.");
        try
        {
            document.Print();
            ClientDiagnosticsLog.Info(
                "Print.Desktop",
                $"PrintDocument.Print completed: printer={printerName}, pages={pageState.PageNumber}, document={documentName}.");
        }
        catch (Exception ex)
        {
            ClientDiagnosticsLog.Error(
                "Print.Desktop",
                $"PrintDocument.Print failed: printer={printerName}, document={documentName}.",
                ex);
            throw;
        }

        return Task.FromResult(printerName);
    }

    private static string? ShowPrintDialog()
    {
        var dialog = new PRINTDLGEX
        {
            lStructSize = Marshal.SizeOf<PRINTDLGEX>(),
            hwndOwner = GetForegroundWindow(),
            Flags = PdReturnDc | PdUseDevModeCopies | PdNoPageNums,
            nMinPage = 1,
            nMaxPage = 1,
            nStartPage = StartPageGeneral,
            nCopies = 1
        };

        ClientDiagnosticsLog.Info(
            "Print.Dialog",
            $"PrintDlgExW opening: owner=0x{dialog.hwndOwner.ToInt64():X}, flags=0x{dialog.Flags:X8}, " +
            $"structSize={dialog.lStructSize}.");
        var hr = PrintDlgExW(ref dialog);
        var win32Error = Marshal.GetLastWin32Error();
        ClientDiagnosticsLog.Info(
            "Print.Dialog",
            $"PrintDlgExW returned: hr=0x{hr:X8}, win32Error={win32Error}, resultAction={dialog.dwResultAction}, " +
            $"hDevMode=0x{dialog.hDevMode.ToInt64():X}, hDevNames=0x{dialog.hDevNames.ToInt64():X}, " +
            $"hDC=0x{dialog.hDC.ToInt64():X}.");
        try
        {
            if (hr != 0)
            {
                throw new InvalidOperationException(
                    $"无法打开系统打印对话框 (HRESULT: 0x{hr:X8}, Win32: {win32Error})。");
            }

            if (dialog.dwResultAction is 0)
            {
                return null;
            }

            if (dialog.dwResultAction is not (PrintDialogPrint or PrintDialogApply))
            {
                throw new InvalidOperationException("系统打印对话框未确认打印操作。");
            }

            if (dialog.hDevNames == IntPtr.Zero)
            {
                throw new InvalidOperationException("系统打印对话框没有返回所选打印机。");
            }

            var printerName = ReadPrinterName(dialog.hDevNames, win32Error);
            if (string.IsNullOrWhiteSpace(printerName))
            {
                throw new InvalidOperationException("系统打印对话框没有返回打印机名称。");
            }

            ClientDiagnosticsLog.Info("Print.Dialog", $"Selected printer resolved: {printerName}.");
            return printerName;
        }
        catch (Exception ex)
        {
            ClientDiagnosticsLog.Error(
                "Print.Dialog",
                $"Print dialog result handling failed: hr=0x{hr:X8}, win32Error={win32Error}, " +
                $"resultAction={dialog.dwResultAction}.",
                ex);
            throw;
        }
        finally
        {
            ReleaseDialogHandles(ref dialog);
        }
    }

    private static string ReadPrinterName(IntPtr hDevNames, int dialogWin32Error)
    {
        var size = GlobalSize(hDevNames);
        var sizeError = Marshal.GetLastWin32Error();
        if (size == UIntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"无法读取打印机内存块大小 (GlobalSize Win32: {sizeError}, " +
                $"dialog Win32: {dialogWin32Error}, hDevNames: 0x{hDevNames.ToInt64():X})。");
        }

        var blockSize = size.ToUInt64();
        if (blockSize < (ulong)Marshal.SizeOf<DEVNAMES>())
        {
            throw new InvalidOperationException(
                $"打印机内存块过小 (GlobalSize: {blockSize}, 结构大小: {Marshal.SizeOf<DEVNAMES>()})。");
        }

        var locked = GlobalLock(hDevNames);
        var lockError = Marshal.GetLastWin32Error();
        if (locked == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"无法锁定打印机内存块 (GlobalLock Win32: {lockError}, hDevNames: 0x{hDevNames.ToInt64():X})。");
        }

        try
        {
            var names = Marshal.PtrToStructure<DEVNAMES>(locked);
            var deviceOffset = (ulong)names.wDeviceOffset * sizeof(char);
            ClientDiagnosticsLog.Info(
                "Print.Dialog",
                $"DEVNAMES locked: handle=0x{hDevNames.ToInt64():X}, address=0x{locked.ToInt64():X}, " +
                $"size={blockSize}, offsets={names.wDriverOffset}/{names.wDeviceOffset}/{names.wOutputOffset}, " +
                $"deviceByteOffset={deviceOffset}.");

            if (deviceOffset < (ulong)Marshal.SizeOf<DEVNAMES>() || deviceOffset >= blockSize)
            {
                throw new InvalidOperationException(
                    $"打印机名称偏移越界 (deviceByteOffset: {deviceOffset}, GlobalSize: {blockSize})。");
            }

            var maxBytes = Math.Min(
                (int)Math.Min(blockSize - deviceOffset, (ulong)MaxPrinterNameBytes),
                MaxPrinterNameBytes);
            maxBytes -= maxBytes % sizeof(char);
            if (maxBytes <= 0)
            {
                throw new InvalidOperationException("打印机名称内存范围为空。");
            }

            var name = ReadNullTerminatedUnicodeString(locked, (int)deviceOffset, maxBytes / sizeof(char));
            ClientDiagnosticsLog.Info(
                "Print.Dialog",
                $"Printer name read from DEVNAMES: length={name.Length}, boundedBytes={maxBytes}.");
            return name;
        }
        finally
        {
            var unlocked = GlobalUnlock(hDevNames);
            var unlockError = unlocked ? 0 : Marshal.GetLastWin32Error();
            ClientDiagnosticsLog.Info(
                "Print.Dialog",
                $"GlobalUnlock completed: hDevNames=0x{hDevNames.ToInt64():X}, returned={unlocked}, " +
                $"win32Error={unlockError}.");
        }
    }

    private static string ReadNullTerminatedUnicodeString(IntPtr baseAddress, int byteOffset, int maxChars)
    {
        var builder = new StringBuilder(Math.Min(maxChars, 256));
        for (var index = 0; index < maxChars; index++)
        {
            var character = (char)Marshal.ReadInt16(baseAddress, byteOffset + (index * sizeof(char)));
            if (character == '\0')
            {
                return builder.ToString();
            }

            builder.Append(character);
        }

        throw new InvalidOperationException(
            $"打印机名称超过安全读取上限 ({MaxPrinterNameBytes} 字节)，已在未终止状态下中止读取。");
    }

    private static void ReleaseDialogHandles(ref PRINTDLGEX dialog)
    {
        if (dialog.hDevMode != IntPtr.Zero)
        {
            var freed = GlobalFree(dialog.hDevMode);
            var error = freed == IntPtr.Zero ? 0 : Marshal.GetLastWin32Error();
            ClientDiagnosticsLog.Info(
                "Print.Dialog",
                $"hDevMode released: handle=0x{dialog.hDevMode.ToInt64():X}, result=0x{freed.ToInt64():X}, win32Error={error}.");
        }

        if (dialog.hDevNames != IntPtr.Zero)
        {
            var freed = GlobalFree(dialog.hDevNames);
            var error = freed == IntPtr.Zero ? 0 : Marshal.GetLastWin32Error();
            ClientDiagnosticsLog.Info(
                "Print.Dialog",
                $"hDevNames released: handle=0x{dialog.hDevNames.ToInt64():X}, result=0x{freed.ToInt64():X}, win32Error={error}.");
        }

        if (dialog.hDC != IntPtr.Zero)
        {
            var deleted = DeleteDC(dialog.hDC);
            var error = deleted ? 0 : Marshal.GetLastWin32Error();
            ClientDiagnosticsLog.Info(
                "Print.Dialog",
                $"hDC released: handle=0x{dialog.hDC.ToInt64():X}, result={deleted}, win32Error={error}.");
        }
    }

    private static void ConfigurePage(PageSettings page, EmrPrintSnapshot snapshot)
    {
        const double hundredthsPerMillimeter = 100d / 25.4d;
        page.Landscape = false;
        page.Margins = new Margins(0, 0, 0, 0);
        page.PaperSize = new PaperSize(
            snapshot.PageWidthMm >= snapshot.PageHeightMm ? "A4 Landscape" : "A4",
            (int)Math.Round((double)snapshot.PageWidthMm * hundredthsPerMillimeter),
            (int)Math.Round((double)snapshot.PageHeightMm * hundredthsPerMillimeter));
    }

    private static bool DrawPage(PrintPageEventArgs e, PageState state)
    {
        var snapshot = state.Snapshot;
        var g = e.Graphics;
        if (g == null) return false;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.PageUnit = GraphicsUnit.Millimeter;

        var layout = new PrintLayout(
            (float)snapshot.PageWidthMm,
            (float)snapshot.PageHeightMm,
            (float)snapshot.MarginLeftMm,
            (float)snapshot.MarginTopMm,
            Math.Max(20f, (float)(snapshot.PageWidthMm - snapshot.MarginLeftMm - snapshot.MarginRightMm)),
            Math.Max(20f, (float)(snapshot.PageHeightMm - snapshot.MarginTopMm - snapshot.MarginBottomMm)));

        using var titleFont = new Font("Microsoft YaHei", 15, FontStyle.Bold, GraphicsUnit.Point);
        using var subtitleFont = new Font("Microsoft YaHei", 13, FontStyle.Bold, GraphicsUnit.Point);
        using var sectionFont = new Font("Microsoft YaHei", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
        using var labelFont = new Font("Microsoft YaHei", 8.5f, FontStyle.Bold, GraphicsUnit.Point);
        using var bodyFont = new Font("Microsoft YaHei", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
        using var smallFont = new Font("Microsoft YaHei", 7.5f, FontStyle.Regular, GraphicsUnit.Point);
        using var textBrush = new SolidBrush(Color.FromArgb(30, 41, 59));
        using var mutedBrush = new SolidBrush(Color.FromArgb(100, 116, 139));
        using var sectionBrush = new SolidBrush(Color.FromArgb(241, 245, 249));
        using var rulePen = new Pen(Color.FromArgb(185, 28, 28), 0.5f);
        using var borderPen = new Pen(Color.FromArgb(210, 218, 228), 0.15f);

        DrawWatermark(g, snapshot, layout);

        var x = layout.MarginLeft;
        var y = layout.MarginTop;
        g.DrawString(snapshot.HospitalName, titleFont, textBrush,
            new RectangleF(x, y, layout.ContentWidth, 9), CenterFormat());
        y += 8;
        g.DrawString(snapshot.SubTitle, subtitleFont, textBrush,
            new RectangleF(x, y, layout.ContentWidth, 8), CenterFormat());
        y += 8;
        g.DrawLine(rulePen, x, y, x + layout.ContentWidth, y);
        y += 3;

        if (snapshot.ShowPatientBanner)
        {
            var bannerHeight = snapshot.ShowBedAndAdmissionNo ? 13f : 9f;
            g.DrawRectangle(borderPen, x, y, layout.ContentWidth, bannerHeight);
            g.DrawString(snapshot.PatientBanner, bodyFont, textBrush,
                new RectangleF(x + 2, y + 1.5f, layout.ContentWidth - 4, 5));
            if (snapshot.ShowBedAndAdmissionNo)
            {
                g.DrawString(snapshot.BedAndWardText, smallFont, mutedBrush,
                    new RectangleF(x + 2, y + 7, layout.ContentWidth - 4, 4.5f));
            }
            y += bannerHeight + 3;
        }

        if (snapshot.ShowNursingScales)
        {
            g.DrawString(snapshot.NursingScalesText, smallFont, mutedBrush,
                new RectangleF(x, y, layout.ContentWidth, 8));
            y += 8;
        }

        var bottom = layout.MarginTop + layout.ContentHeight - 18;
        var drewContent = false;
        while (state.SectionIndex < snapshot.Sections.Count)
        {
            var section = snapshot.Sections[state.SectionIndex];
            if (state.FieldIndex == 0)
            {
                if (y + 13 > bottom)
                {
                    return DrawPageBreak(g, snapshot, smallFont, mutedBrush, layout, state);
                }

                g.FillRectangle(sectionBrush, x, y, layout.ContentWidth, 6);
                g.DrawString(section.Title, sectionFont, textBrush,
                    new RectangleF(x + 2, y + 1, layout.ContentWidth - 4, 5));
                y += 8;
                drewContent = true;
            }

            while (state.FieldIndex < section.Fields.Count)
            {
                var field = section.Fields[state.FieldIndex];
                var label = field.Label + (field.IsRequired ? " *" : "") + ":";
                var labelSize = g.MeasureString(label, labelFont);
                var valueX = x + Math.Min(48f, Math.Max(30f, labelSize.Width + 2f));
                var valueWidth = Math.Max(20f, layout.ContentWidth - (valueX - x));
                var valueHeight = g.MeasureString(field.Value, bodyFont,
                    new SizeF(valueWidth, 1000), StringFormat.GenericDefault).Height;
                var rowHeight = Math.Max(5f, valueHeight + 1.5f);

                if (y + Math.Min(rowHeight, 10) > bottom)
                {
                    if (!drewContent)
                    {
                        rowHeight = Math.Min(rowHeight, Math.Max(5f, bottom - y));
                    }
                    else
                    {
                        return DrawPageBreak(g, snapshot, smallFont, mutedBrush, layout, state);
                    }
                }

                g.DrawString(label, labelFont, textBrush, new RectangleF(x, y, valueX - x - 1, rowHeight));
                g.DrawString(field.Value, bodyFont, textBrush,
                    new RectangleF(valueX, y, valueWidth, rowHeight), StringFormat.GenericDefault);
                y += rowHeight;
                state.FieldIndex++;
                drewContent = true;
            }

            state.SectionIndex++;
            state.FieldIndex = 0;
            y += 2.5f;
        }

        if (!state.SignaturesDrawn)
        {
            if (snapshot.ShowTrackChanges)
            {
                if (y + 7 > bottom && drewContent)
                {
                    return DrawPageBreak(g, snapshot, smallFont, mutedBrush, layout, state);
                }
                g.DrawString("三级医师审签修改留痕: 住院医师初写 → 主治医师修订并审签锁定",
                    smallFont, mutedBrush, new RectangleF(x, y, layout.ContentWidth, 5));
                y += 6;
            }

            if (y + 10 > bottom && drewContent)
            {
                return DrawPageBreak(g, snapshot, smallFont, mutedBrush, layout, state);
            }

            DrawSignatures(g, snapshot, bodyFont, smallFont, textBrush, mutedBrush, layout, ref y, bottom);
            state.SignaturesDrawn = true;
        }

        DrawFooter(g, snapshot, smallFont, mutedBrush, layout, state.PageNumber);
        return false;
    }

    private static void DrawWatermark(Graphics g, EmrPrintSnapshot snapshot, PrintLayout layout)
    {
        if (string.IsNullOrWhiteSpace(snapshot.WatermarkText)) return;

        using var watermarkBrush = new SolidBrush(Color.FromArgb(20, 80, 80, 80));
        using var watermarkFont = new Font("Microsoft YaHei", 28, FontStyle.Bold, GraphicsUnit.Point);
        var state = g.Save();
        g.TranslateTransform(layout.MarginLeft + layout.ContentWidth / 2, layout.MarginTop + layout.ContentHeight / 2);
        g.RotateTransform(-32);
        var size = g.MeasureString(snapshot.WatermarkText, watermarkFont);
        g.DrawString(snapshot.WatermarkText, watermarkFont, watermarkBrush, -size.Width / 2, -size.Height / 2);
        g.Restore(state);
    }

    private static void DrawSignatures(Graphics g, EmrPrintSnapshot snapshot, Font bodyFont, Font smallFont,
        Brush textBrush, Brush mutedBrush, PrintLayout layout, ref float y, float bottom)
    {
        if (snapshot.SignSlots.Count == 0 || y + 8 > bottom) return;

        g.DrawString("电子签章", bodyFont, textBrush, layout.MarginLeft, y);
        y += 6;
        foreach (var slot in snapshot.SignSlots)
        {
            if (y + 5 > bottom) break;
            g.DrawString(slot, smallFont, mutedBrush,
                new RectangleF(layout.MarginLeft, y, layout.ContentWidth, 4.5f));
            y += 4.5f;
        }

        using var sealPen = new Pen(Color.FromArgb(211, 47, 47), 0.7f);
        var sealX = layout.MarginLeft + layout.ContentWidth - 29;
        var sealY = Math.Min(bottom - 28, y - 2);
        g.DrawEllipse(sealPen, sealX, sealY, 26, 26);
        using var sealFont = new Font("Microsoft YaHei", 7.5f, FontStyle.Bold, GraphicsUnit.Point);
        using var sealBrush = new SolidBrush(Color.FromArgb(211, 47, 47));
        g.DrawString("三甲医院\n病历归档专用", sealFont, sealBrush,
            new RectangleF(sealX + 2, sealY + 9, 22, 10), CenterFormat());
    }

    private static bool DrawPageBreak(Graphics g, EmrPrintSnapshot snapshot, Font smallFont, Brush mutedBrush,
        PrintLayout layout, PageState state)
    {
        DrawFooter(g, snapshot, smallFont, mutedBrush, layout, state.PageNumber);
        state.PageNumber++;
        return true;
    }

    private static void DrawFooter(Graphics g, EmrPrintSnapshot snapshot, Font smallFont, Brush mutedBrush,
        PrintLayout layout, int pageNumber)
    {
        var footerY = layout.MarginTop + layout.ContentHeight - 4;
        using var footerPen = new Pen(Color.FromArgb(210, 218, 228), 0.15f);
        g.DrawLine(footerPen, layout.MarginLeft, footerY - 1.5f,
            layout.MarginLeft + layout.ContentWidth, footerY - 1.5f);
        var footer = string.IsNullOrWhiteSpace(snapshot.FooterText) ? snapshot.DocNo : snapshot.FooterText;
        g.DrawString(footer, smallFont, mutedBrush,
            new RectangleF(layout.MarginLeft, footerY, layout.ContentWidth - 22, 4.5f));
        g.DrawString($"第 {pageNumber} 页", smallFont, mutedBrush,
            new RectangleF(layout.MarginLeft + layout.ContentWidth - 22, footerY, 22, 4.5f), RightFormat());
    }

    private static StringFormat CenterFormat() =>
        new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };

    private static StringFormat RightFormat() =>
        new() { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Near };

    private sealed class PageState(EmrPrintSnapshot snapshot)
    {
        public EmrPrintSnapshot Snapshot { get; } = snapshot;
        public int SectionIndex { get; set; }
        public int FieldIndex { get; set; }
        public bool SignaturesDrawn { get; set; }
        public int PageNumber { get; set; } = 1;
    }

    private sealed record PrintLayout(
        float Width,
        float Height,
        float MarginLeft,
        float MarginTop,
        float ContentWidth,
        float ContentHeight);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVNAMES
    {
        public ushort wDriverOffset;
        public ushort wDeviceOffset;
        public ushort wOutputOffset;
        public ushort wDefault;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PRINTDLGEX
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hDevMode;
        public IntPtr hDevNames;
        public IntPtr hDC;
        public int Flags;
        public int Flags2;
        public int ExclusionFlags;
        public int nPageRanges;
        public int nMaxPageRanges;
        public IntPtr lpPageRanges;
        public int nMinPage;
        public int nMaxPage;
        public int nCopies;
        public IntPtr hInstance;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpPrintTemplateName;
        public IntPtr lpCallback;
        public int nPropertyPages;
        public IntPtr lphPropertyPages;
        public int nStartPage;
        public int dwResultAction;
    }

    [DllImport("comdlg32.dll", EntryPoint = "PrintDlgExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int PrintDlgExW(ref PRINTDLGEX printDlg);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr GlobalSize(IntPtr hMem);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
