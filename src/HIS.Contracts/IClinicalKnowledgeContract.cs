using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HIS.Contracts;

public sealed record VitalSignItemDto(
    string VitalType,
    decimal Value,
    string? Unit
);

public sealed record LabResultSnapshotDto(
    string ItemCode,
    string ItemName,
    decimal? ValueNum,
    string? ValueText,
    string AbnormalFlag
);

public sealed record ActiveOrderItemDto(
    string OrderId,
    string ItemCode,
    string ItemName,
    string Category,
    decimal Dosage,
    string DosageUnit
);

public sealed record CdssEvaluationRequestDto(
    string PatientId,
    string Gender,
    int AgeYears,
    IReadOnlyList<string> DiagnosisIcdCodes,
    IReadOnlyList<string>? AllergyTags = null,
    IReadOnlyList<VitalSignItemDto>? VitalSigns = null,
    IReadOnlyList<LabResultSnapshotDto>? RecentLabResults = null,
    IReadOnlyList<ActiveOrderItemDto>? ActiveOrders = null
);

public sealed record CdssHitRuleDto(
    string RuleCode,
    string RuleDomain,
    string Severity,
    string Explanation,
    string Suggestion,
    string? EvidenceDocId
);

public sealed record CdssEvaluationResultDto(
    IReadOnlyList<CdssHitRuleDto> Hits,
    bool HasBlockingRules
);

public sealed record KnowledgeDocSummaryDto(
    string Id,
    string DocCode,
    string Category,
    string SpecialtyDept,
    string Title,
    string? SourceOrg,
    string VersionTag
);

public interface IClinicalKnowledgeContract
{
    Task<CdssEvaluationResultDto> EvaluateAsync(
        CdssEvaluationRequestDto request,
        CancellationToken ct = default);

    Task<IReadOnlyList<KnowledgeDocSummaryDto>> SearchKnowledgeAsync(
        string? keyword,
        string? category,
        string? icdCode,
        string? itemCode = null,
        CancellationToken ct = default);
}
