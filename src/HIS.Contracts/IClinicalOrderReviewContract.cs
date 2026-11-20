using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HIS.Contracts;

public sealed record DrugItemToReviewDto(
    string DrugId,
    string DrugName,
    decimal DoseOnce,
    string DoseUnit,
    string FrequencyCode,
    string RouteCode,
    int Days,
    decimal TotalQuantity,
    bool IsSkinTestRequired = false,
    int? SkinTestFlag = 0
);

public sealed record OrderReviewContext(
    string EncounterOrVisitId,
    string PatientId,
    string Gender,
    int AgeYears,
    IReadOnlyList<string> DiagnosisIcdCodes,
    IReadOnlyList<string>? AllergyTags = null,
    IReadOnlyList<DrugItemToReviewDto>? ItemsToReview = null,
    IReadOnlyList<LabResultSnapshotDto>? RecentLabResults = null
);

public sealed record OrderReviewHitDto(
    string RuleCode,
    string RuleType,
    string Severity,
    string Message,
    string? SuggestedAction,
    bool IsBlock
);

public sealed record OrderReviewResult(
    bool Passed,
    bool HasBlockWarning,
    IReadOnlyList<OrderReviewHitDto> Hits
);

public interface IClinicalOrderReviewEngine
{
    Task<OrderReviewResult> ReviewOrdersAsync(
        OrderReviewContext context,
        CancellationToken ct = default);
}
