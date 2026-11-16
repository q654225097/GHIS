using System.Collections.Generic;
using System.Threading.Tasks;
using HIS.BuildingBlocks.OfdEngine;

namespace HIS.Client.UrsaShell.Services;

public sealed record EmrPrintSnapshotField(
    string Label,
    string Value,
    bool IsRequired);

public sealed record EmrPrintSnapshotSection(
    string Title,
    string RenderMode,
    IReadOnlyList<EmrPrintSnapshotField> Fields);

public sealed record EmrPrintSnapshot(
    string DocNo,
    string TemplateName,
    string HospitalName,
    string SubTitle,
    string PatientBanner,
    string BedAndWardText,
    string NursingScalesText,
    string FooterText,
    string WatermarkText,
    string Author,
    string SignerName,
    IReadOnlyList<EmrPrintSnapshotSection> Sections,
    IReadOnlyList<string> SignSlots,
    decimal PageWidthMm,
    decimal PageHeightMm,
    decimal MarginTopMm,
    decimal MarginBottomMm,
    decimal MarginLeftMm,
    decimal MarginRightMm,
    bool ShowPatientBanner,
    bool ShowBedAndAdmissionNo,
    bool ShowNursingScales,
    bool ShowTrackChanges);

public interface IDesktopPrintService
{
    bool IsSupported { get; }

    Task<string> PrintAsync(EmrPrintSnapshot snapshot, string ofdFilePath);
}

public static class DesktopPrintServiceLocator
{
    public static IDesktopPrintService? Current { get; set; }
}
