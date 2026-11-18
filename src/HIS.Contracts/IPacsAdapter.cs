using System;
using System.Threading;
using System.Threading.Tasks;

namespace HIS.Contracts;

public sealed record PacsOrderRequest(
    string AccessionNo,
    string VisitOrEncounterId,
    string PatientId,
    string PatientName,
    string Modality, // CT, MR, DR, US
    string StudyBodyPart,
    string OrderDeptId,
    string DoctorId,
    long OrderTimeMs
);

public sealed record PacsStudyOrder(
    string AccessionNo,
    string Modality,
    string Status, // SCHEDULED, ARRIVED, COMPLETED, CANCELLED
    long ScheduledTimeMs
);

public sealed record PacsReport(
    string AccessionNo,
    string PatientId,
    string Modality,
    string StudyBodyPart,
    string Findings,
    string Impression,
    string DicomViewerUrl,
    string RadiologistId,
    string RadiologistName,
    string VerifierId,
    string VerifierName,
    long ReportTimeMs
);

public interface IPacsAdapter
{
    Task<PacsStudyOrder> SendStudyOrderAsync(PacsOrderRequest req, CancellationToken ct = default);
    Task<PacsReport?> QueryReportAsync(string accessionNo, CancellationToken ct = default);
    Task<byte[]> GetImageAsync(string imageId, CancellationToken ct = default);
    Task<bool> CancelStudyAsync(string accessionNo, CancellationToken ct = default);
}
