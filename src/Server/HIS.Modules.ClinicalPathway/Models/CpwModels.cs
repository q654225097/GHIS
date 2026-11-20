using HIS.BuildingBlocks.Database;

namespace HIS.Modules.ClinicalPathway.Models;

public class CpwPathway : BaseEntity
{
    public string PathwayCode { get; set; } = string.Empty;
    public string PathwayName { get; set; } = string.Empty;
    public string ApplicableIcd10Json { get; set; } = "[]";
    public int StandardDays { get; set; } = 7;
    public decimal MinCost { get; set; }
    public decimal MaxCost { get; set; }
    public string Version { get; set; } = "v1.0";
    public short Status { get; set; } = 1;
}

public class CpwStage : BaseEntity
{
    public string PathwayId { get; set; } = string.Empty;
    public int StageOrder { get; set; } = 1;
    public string StageName { get; set; } = string.Empty;
    public int DayFrom { get; set; } = 1;
    public int DayTo { get; set; } = 1;
    public string? EmrTemplateSnippetJson { get; set; }
}

public class CpwStageItem : BaseEntity
{
    public string StageId { get; set; } = string.Empty;
    public string ItemCategory { get; set; } = "DRUG"; // DRUG / LAB / EXAM / NURSING / EMR / TREATMENT
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public short IsMandatory { get; set; } = 1;
    public short IsDefaultSelected { get; set; } = 1;
}

public class CpwPatientEnrollment : BaseEntity
{
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string PathwayId { get; set; } = string.Empty;
    public string? CurrentStageId { get; set; }
    public long EnrolledAtMs { get; set; }
    public string EnrolledBy { get; set; } = string.Empty;
    public string Status { get; set; } = "ACTIVE"; // ACTIVE / COMPLETED / EXITED
    public string? ExitReason { get; set; }
}

public class CpwVariance : BaseEntity
{
    public string EnrollmentId { get; set; } = string.Empty;
    public string StageId { get; set; } = string.Empty;
    public string? StageItemId { get; set; }
    public string? RelatedOrderId { get; set; }
    public string VarianceType { get; set; } = "OMISSION"; // OMISSION / ADDITION / DELAY / EARLY_EXIT
    public string ReasonCategory { get; set; } = "CLINICAL"; // PATIENT / CLINICAL / HOSPITAL
    public string ReasonDetail { get; set; } = string.Empty;
    public string RecordedBy { get; set; } = string.Empty;
    public long RecordedAtMs { get; set; }
}
