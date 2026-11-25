using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HIS.Contracts;

public sealed record CpwMatchResultDto(
    string PathwayId,
    string PathwayCode,
    string PathwayName,
    int StandardDays,
    decimal MinCost,
    decimal MaxCost
);

public sealed record CpwEnrollmentDto(
    string Id,
    string EncounterId,
    string PatientId,
    string PathwayId,
    string PathwayName,
    string CurrentStageId,
    string CurrentStageName,
    string Status
);

public sealed record CpwOrderComparisonResultDto(
    IReadOnlyList<string> MissingMandatoryItems,
    IReadOnlyList<string> VarianceCandidateOrders
)
{
    public IReadOnlyList<string> PotentialVariances => VarianceCandidateOrders;
}

public sealed record CpwEmrFusionSnippetDto(
    string StageName,
    string RecommendedOrdersMarkdown,
    string NursingGuidelinesMarkdown,
    string TreatmentGoalsMarkdown
);

public interface IClinicalPathwayContract
{
    Task<IReadOnlyList<CpwMatchResultDto>> MatchPathwaysAsync(
        string icdCode,
        CancellationToken ct = default);

    Task<CpwEnrollmentDto> EnrollPatientAsync(
        string encounterId,
        string patientId,
        string pathwayId,
        string enrolledBy,
        CancellationToken ct = default);

    Task<CpwOrderComparisonResultDto> CompareOrdersAsync(
        string enrollmentId,
        CancellationToken ct = default);

    Task<CpwEmrFusionSnippetDto> GetEmrFusionSnippetAsync(
        string enrollmentId,
        CancellationToken ct = default);
}
