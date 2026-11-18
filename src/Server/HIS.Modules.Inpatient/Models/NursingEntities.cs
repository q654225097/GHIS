using System;
using System.Collections.Generic;
using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Inpatient.Models;

public class NurCarePlan : BaseEntity
{
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string DiagnosisOrProblem { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public string? InterventionsJson { get; set; }
    public long? TargetDateMs { get; set; }
    public int Status { get; set; } = 0; // 0=IN_PROGRESS, 1=ACHIEVED, 2=DISCONTINUED
}

public class NurCarePlanEvaluation : BaseEntity
{
    public string PlanId { get; set; } = string.Empty;
    public string EvaluationResult { get; set; } = string.Empty;
    public short IsGoalMet { get; set; } = 0;
    public long EvaluatedAtMs { get; set; }
    public string EvaluatorId { get; set; } = string.Empty;
}

public class NurPressureUlcerAssessment : BaseEntity
{
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public int SensoryPerception { get; set; } = 4; // 1-4
    public int Moisture { get; set; } = 4;          // 1-4
    public int Activity { get; set; } = 4;          // 1-4
    public int Mobility { get; set; } = 4;          // 1-4
    public int Nutrition { get; set; } = 4;         // 1-4
    public int FrictionShear { get; set; } = 3;     // 1-3
    public int TotalScore { get; set; } = 23;       // 6-23
    public string RiskLevel { get; set; } = "NONE"; // VERY_HIGH (<=9), HIGH (10-12), MODERATE (13-14), MILD (15-18), NONE (19-23)
    public string? PreventiveMeasuresJson { get; set; }
    public string AssessorId { get; set; } = string.Empty;
    public long AssessedAtMs { get; set; }
}

public class NurFallAssessment : BaseEntity
{
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public int HistoryOfFalling { get; set; } = 0;   // 0 or 25
    public int SecondaryDiagnosis { get; set; } = 0;  // 0 or 15
    public int AmbulatoryAid { get; set; } = 0;       // 0, 15, or 30
    public int IvOrSalineLock { get; set; } = 0;      // 0 or 20
    public int Gait { get; set; } = 0;                // 0, 10, or 20
    public int MentalStatus { get; set; } = 0;        // 0 or 15
    public int TotalScore { get; set; } = 0;          // 0-125
    public string RiskLevel { get; set; } = "LOW";    // HIGH (>=45), MEDIUM (25-44), LOW (0-24)
    public string? PreventiveMeasuresJson { get; set; }
    public string AssessorId { get; set; } = string.Empty;
    public long AssessedAtMs { get; set; }
}

public class NurQualityIndicator : BaseEntity
{
    public string WardId { get; set; } = string.Empty;
    public string MonthStr { get; set; } = string.Empty; // yyyy-MM
    public int PatientDays { get; set; } = 0;
    public int FallCount { get; set; } = 0;
    public int PressureUlcerCount { get; set; } = 0;
    public int UnplannedExtubationCount { get; set; } = 0;
    public int CautiCount { get; set; } = 0;
    public int ClabsiCount { get; set; } = 0;
    public string ReporterId { get; set; } = string.Empty;
}

// DTOs
public sealed record CreateCarePlanRequest(
    string EncounterId,
    string PatientId,
    string DiagnosisOrProblem,
    string Goal,
    List<string>? Interventions = null,
    long? TargetDateMs = null
);

public sealed record UpdateCarePlanRequest(
    string? DiagnosisOrProblem,
    string? Goal,
    List<string>? Interventions,
    long? TargetDateMs,
    int? Status
);

public sealed record EvaluateCarePlanRequest(
    string EvaluationResult,
    bool IsGoalMet
);

public sealed record AssessBradenRequest(
    string EncounterId,
    string PatientId,
    int SensoryPerception,
    int Moisture,
    int Activity,
    int Mobility,
    int Nutrition,
    int FrictionShear,
    List<string>? PreventiveMeasures = null
);

public sealed record AssessMorseRequest(
    string EncounterId,
    string PatientId,
    int HistoryOfFalling,
    int SecondaryDiagnosis,
    int AmbulatoryAid,
    int IvOrSalineLock,
    int Gait,
    int MentalStatus,
    List<string>? PreventiveMeasures = null
);

public sealed record SaveQualityIndicatorRequest(
    string WardId,
    string MonthStr,
    int PatientDays,
    int FallCount,
    int PressureUlcerCount,
    int UnplannedExtubationCount,
    int CautiCount,
    int ClabsiCount
);

public sealed record NursingQualityReportDto(
    string WardId,
    string MonthStr,
    int TotalPatientDays,
    int TotalFalls,
    decimal FallRatePer1000Days,
    int TotalPressureUlcers,
    decimal PressureUlcerRatePer1000Days,
    int TotalUnplannedExtubations,
    int TotalCauti,
    int TotalClabsi
);
