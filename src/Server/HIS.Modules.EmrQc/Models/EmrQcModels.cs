using System;
using HIS.BuildingBlocks.Database;

namespace HIS.Modules.EmrQc.Models;

public class EmrQcRule : BaseEntity
{
    public string RuleCode { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string QcDimension { get; set; } = "TIMELINESS"; // TIMELINESS / FORMAT / CONTENT / SPECIALTY / DRUG_RATIONALITY / INFECTION_CONTROL / COST_CONTROL
    public string? SpecialtyDeptId { get; set; }
    public string? Icd10Pattern { get; set; }
    public string? DocTypeCode { get; set; }
    public int TimeLimitHours { get; set; } = 24;
    public decimal DeductPoints { get; set; } = 5.0m;
    public string? RuleExprJson { get; set; }
    public short IsActive { get; set; } = 1;
}

public class EmrQcTask : BaseEntity
{
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string TaskType { get; set; } = "AUTO_PROCESS"; // AUTO_PROCESS / MANUAL_PROCESS / TERMINAL / CROSS_ORG
    public decimal TotalScore { get; set; } = 100.0m;
    public string Grade { get; set; } = "A"; // A / B / C
    public string Status { get; set; } = "PENDING"; // PENDING / IN_REVIEW / DEFECT_ISSUED / CLOSED
    public string? ReviewerId { get; set; }
    public long? ReviewedAtMs { get; set; }
}

public class EmrQcDefect : BaseEntity
{
    public string TaskId { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public string? DocId { get; set; }
    public string RuleCode { get; set; } = string.Empty;
    public string QcDimension { get; set; } = "TIMELINESS";
    public string DefectDescription { get; set; } = string.Empty;
    public decimal DeductPoints { get; set; } = 5.0m;
    public string ResponsibleDoctorId { get; set; } = string.Empty;
    public long DeadlineMs { get; set; }
    public string? RectificationNote { get; set; }
    public string Status { get; set; } = "OPEN"; // OPEN / DOCTOR_FIXED / VERIFIED_CLOSED / REJECTED
    public string? VerifiedBy { get; set; }
    public long? VerifiedAtMs { get; set; }
}

public class EmrQcCrossOrgTrack : BaseEntity
{
    public string PatientId { get; set; } = string.Empty;
    public string SourceOrgCode { get; set; } = string.Empty;
    public string ExternalDocId { get; set; } = string.Empty;
    public string QcReviewerId { get; set; } = string.Empty;
    public string FindingSummary { get; set; } = string.Empty;
    public string TrackStatus { get; set; } = "REVIEWED";
}
