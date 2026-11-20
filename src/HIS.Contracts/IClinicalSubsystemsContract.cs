using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HIS.Contracts;

// ==================== TASK-07: Treatment & Monitoring ====================

public sealed record TreatmentParamSuggestionDto(
    string CatalogId,
    string ParameterName,
    string SuggestedValue,
    string LabBasis,
    string WarningNotice
);

public interface ITreatmentContract
{
    Task<IReadOnlyList<TreatmentParamSuggestionDto>> SuggestParametersAsync(
        string catalogId,
        string patientId,
        CancellationToken ct = default);
}

public sealed record RiskScoreEvaluationResultDto(
    string EncounterId,
    string ScoreType,
    int TotalScore,
    string RiskLevel,
    string GuidanceText,
    IReadOnlyDictionary<string, int> ComponentScores
);

public interface IMonitoringContract
{
    Task<RiskScoreEvaluationResultDto> EvaluateRiskScoreAsync(
        string encounterId,
        string scoreType,
        CancellationToken ct = default);
}

// ==================== TASK-08: Blood Bank ====================

public sealed record BloodStockAdvisoryDto(
    string BloodCode,
    string BloodName,
    string AboType,
    string RhType,
    int CurrentStock,
    int SafeStock,
    int PendingDemand,
    string AdvisoryStatus
);

public sealed record TransfusionSafetyCheckResultDto(
    bool SafeToTransfuse,
    string? WarningMessage,
    bool HardBlocked
);

public interface IBloodBankContract
{
    Task<TransfusionSafetyCheckResultDto> CheckSafetyAsync(
        string bloodRequestId,
        CancellationToken ct = default);

    Task<IReadOnlyList<BloodStockAdvisoryDto>> GetStockAdvisoryAsync(
        CancellationToken ct = default);
}

// ==================== TASK-09: EMR Quality Control ====================

public sealed record EmrQcDefectDto(
    string DefectId,
    string EncounterId,
    string? DocId,
    string RuleCode,
    string QcDimension,
    string Description,
    decimal DeductPoints,
    string Status
);

public sealed record EmrQcReportDto(
    string TaskId,
    string EncounterId,
    decimal TotalScore,
    string Grade,
    IReadOnlyList<EmrQcDefectDto> Defects
);

public interface IEmrQcContract
{
    Task<EmrQcReportDto> RunAutoQcAsync(
        string encounterId,
        CancellationToken ct = default);
}

// ==================== TASK-10: HIE Gateway ====================

public sealed record EmpiResolveResultDto(
    string EmpiId,
    string LocalPatientId,
    string ExternalOrgCode,
    string ExternalPatientId,
    decimal MatchConfidence
);

public sealed record ExternalEhrRecordDto(
    string SourceOrgCode,
    string SourceOrgName,
    string RecordCategory,
    string ExternalRecordId,
    long ClinicalTimeMs,
    string SummaryText
);

public interface IHieGatewayContract
{
    Task<EmpiResolveResultDto?> ResolveEmpiAsync(
        string localPatientId,
        string fullName,
        string idCardHash,
        string genderCode,
        int birthDateInt,
        CancellationToken ct = default);

    Task<IReadOnlyList<ExternalEhrRecordDto>> FetchExternalEhrSummaryAsync(
        string patientId,
        CancellationToken ct = default);

    Task<bool> PublishOutboundResourceAsync(
        string resourceType,
        string resourceId,
        string targetOrgCode,
        object payload,
        CancellationToken ct = default);
}
