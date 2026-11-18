using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HIS.Contracts;

public sealed record LisOrderRequest(
    string OrderId,
    string VisitOrEncounterId,
    string PatientId,
    string PatientName,
    string Gender,
    int Age,
    string TestItemCode,
    string TestItemName,
    string SampleType,
    string OrderDeptId,
    string DoctorId,
    long OrderTimeMs
);

public sealed record LisTestOrder(
    string LisOrderId,
    string HisOrderId,
    string Barcode,
    string Status, // RECEIVED, TESTING, REPORTED, CANCELLED
    long CreatedAtMs
);

public sealed record LisResultItem(
    string ItemCode,
    string ItemName,
    string Value,
    string Unit,
    string ReferenceRange,
    string Flag // NORMAL, HIGH, LOW, CRITICAL
);

public sealed record LisResult(
    string LisOrderId,
    string HisOrderId,
    string PatientId,
    string SampleType,
    List<LisResultItem> Items,
    string ReporterId,
    string ReporterName,
    string VerifierId,
    string VerifierName,
    long ReportTimeMs,
    bool IsCritical
);

public interface ILisAdapter
{
    Task<LisTestOrder> SendTestOrderAsync(LisOrderRequest req, CancellationToken ct = default);
    Task<LisResult?> QueryResultAsync(string lisOrderId, CancellationToken ct = default);
    Task<List<LisResult>> BatchSyncResultsAsync(CancellationToken ct = default);
    Task<bool> CancelOrderAsync(string lisOrderId, CancellationToken ct = default);
}
