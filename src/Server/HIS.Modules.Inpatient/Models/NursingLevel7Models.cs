using System;
using System.Collections.Generic;
using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Inpatient.Models;

public class NurVitalSign : BaseEntity
{
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public long MeasuredAtMs { get; set; }
    public decimal TemperatureC { get; set; }
    public int PulseBpm { get; set; }
    public int HeartRateBpm { get; set; }
    public int RespRate { get; set; }
    public int SysBp { get; set; }
    public int DiaBp { get; set; }
    public decimal Spo2 { get; set; }
    public decimal BloodGlucose { get; set; }
    public int PainScore { get; set; }
    public decimal IntakeMl { get; set; }
    public decimal OutputMl { get; set; }
    public string CaptureSource { get; set; } = "MANUAL";
    public string? DeviceId { get; set; }
    public int MewsScore { get; set; }
    public string? AutoTriggeredMeasuresJson { get; set; }
    public short IsExternal { get; set; } = 0;
    public string? SourceOrgCode { get; set; }
}

public class NurNursingRecord : BaseEntity
{
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string RecordType { get; set; } = "GENERAL";
    public long RecordedAtMs { get; set; }
    public string? VitalSignId { get; set; }
    public string? CpwStageId { get; set; }
    public string? ContentJson { get; set; }
    public string? NursingActionsJson { get; set; }
    public string? GeneratedFeeItemIdsJson { get; set; }
    public string NurseId { get; set; } = string.Empty;
    public string? CaSignatureId { get; set; }
}

public class NurAdmissionAssessment : BaseEntity
{
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public string? AssessmentJson { get; set; }
    public string? AutoRiskSummaryJson { get; set; }
    public string? ReferencedHistoryIdsJson { get; set; }
    public string AssessorId { get; set; } = string.Empty;
    public long AssessedAtMs { get; set; }
}

public class IpdPatientActivityTrack : BaseEntity
{
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string ActivityType { get; set; } = "ADMIT";
    public string FromLocation { get; set; } = string.Empty;
    public string ToLocation { get; set; } = string.Empty;
    public string? ScanBarcode { get; set; }
    public string? HandoverFromUser { get; set; }
    public string? HandoverToUser { get; set; }
    public long OccurredAtMs { get; set; }
}
