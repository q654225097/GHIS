using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HIS.Contracts;
using HIS.Modules.Outpatient.Models;

namespace HIS.Modules.Outpatient.Services;

public interface IPrescriptionReviewEngine
{
    Task<PrescriptionReviewResultDto> ReviewAsync(
        string patientId,
        int doctorPrescribeLevel,
        bool isAdmin,
        IReadOnlyList<OpdPrescriptionItem> items,
        IReadOnlyList<DrugCatalogSnapshotDto> drugs,
        string? agentIdCard = null,
        CancellationToken ct = default);
}
