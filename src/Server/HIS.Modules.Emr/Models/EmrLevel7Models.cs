using System.Collections.Generic;
using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Emr.Models;

public class EmrDigitizedHistory : BaseEntity
{
    public string PatientId { get; set; } = string.Empty;
    public string LegacyMrn { get; set; } = string.Empty;
    public string VisitDate { get; set; } = string.Empty;
    public string DocTitle { get; set; } = string.Empty;
    public string FileStorageUri { get; set; } = string.Empty;
    public string? OcrText { get; set; }
    public string Sm3Hash { get; set; } = string.Empty;
}

public class EmrStoragePolicy : BaseEntity
{
    public string SceneType { get; set; } = "IPD"; // OPD / IPD / EMERGENCY
    public int RetentionYears { get; set; } = 30; // OPD: 15, IPD: 30
    public long RetainUntilMs { get; set; }
}

public class EmrExternalHealthRecord : BaseEntity
{
    public string PatientId { get; set; } = string.Empty;
    public string SourceOrgCode { get; set; } = string.Empty;
    public string SourceOrgName { get; set; } = string.Empty;
    public string RecordCategory { get; set; } = "EMR"; // EMR / LAB / EXAM / IMMUNIZATION / CHRONIC
    public string ExternalRecordId { get; set; } = string.Empty;
    public long ClinicalTimeMs { get; set; }
    public string CdaOrFhirJson { get; set; } = "{}";
    public string? SummaryText { get; set; }
    public long SyncedAtMs { get; set; }
}

public sealed record InsertableLabItemDto(
    string ItemCode,
    string ItemName,
    string ValueText,
    decimal? ValueNum,
    string Unit,
    string ReferenceRange,
    string AbnormalFlag,
    long ReportedAtMs,
    bool IsExternal
);

public sealed record InsertableExamItemDto(
    string RequestId,
    string ExamName,
    string Modality,
    string BodyPart,
    string Findings,
    string Conclusion,
    long ReportedAtMs,
    string? PacsStudyUid
);

public sealed record InsertableVitalSignDto(
    long RecordedAtMs,
    decimal? TemperatureC,
    int? PulseBpm,
    int? RespirationBpm,
    int? SysBp,
    int? DiaBp,
    decimal? Spo2
);

public sealed record InsertableIntakeOutputDto(
    long RecordedAtMs,
    string Category,
    string ItemName,
    decimal VolumeMl
);

public sealed record InsertableSurgeryDto(
    string SurgeryName,
    string SurgeonName,
    string AnesthesiaMethod,
    string AnesthesiologistName,
    long? StartTimeMs,
    long? EndTimeMs
);

public sealed record InsertableExternalEhrDto(
    string SourceOrgName,
    string RecordCategory,
    string SummaryText,
    long ClinicalTimeMs
);

public sealed record EmrInsertableClinicalDataDto(
    string EncounterOrVisitId,
    string PatientId,
    IReadOnlyList<InsertableLabItemDto> LabResults,
    IReadOnlyList<InsertableExamItemDto> ExamReports,
    IReadOnlyList<InsertableVitalSignDto> VitalSigns,
    IReadOnlyList<InsertableIntakeOutputDto> IntakeOutputs,
    IReadOnlyList<InsertableSurgeryDto> Surgeries,
    IReadOnlyList<InsertableExternalEhrDto> ExternalRecords
);

public sealed record EmrStorageGovernanceMetricsDto(
    int TotalDocuments,
    int ArchivedOfdCount,
    int RetentionProtectedCount,
    int OpdRetentionYears,
    int IpdRetentionYears,
    decimal ComplianceRate,
    long EstimatedStorageBytes
);
