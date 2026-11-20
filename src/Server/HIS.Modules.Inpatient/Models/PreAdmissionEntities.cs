using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Inpatient.Models;

public class IpdPreAdmissionConfig : BaseEntity
{
    public int DefaultValidDays { get; set; } = 7;
    public int ExpiryWarnHours { get; set; } = 48;
    public int CriticalEscalateMinutes { get; set; } = 30;
    public int EvalEscalateHours { get; set; } = 12;
    public string AllowedFeeCategories { get; set; } = "LAB,EXAM,PREOP_PREP";
    public string BlockedFeeCategories { get; set; } = "BED,NURSING,IPD_CONSULT";
    public short RequireInformedConsent { get; set; } = 1;
    public short AllowDoctorPreSplit { get; set; } = 1;
    public short RequireFinanceSplitAudit { get; set; } = 1;
}

public class IpdPreAdmission : BaseEntity
{
    public string PreAdmitNo { get; set; } = string.Empty;
    public string? PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string Gender { get; set; } = "男";
    public int Age { get; set; } = 45;
    public string? MedicalRecordNo { get; set; }
    public string? SourceOpdVisitId { get; set; }
    public string? TargetDeptId { get; set; }
    public string TargetDeptName { get; set; } = string.Empty;
    public string? PrimaryDiagCode { get; set; }
    public string PrimaryDiagName { get; set; } = string.Empty;
    public string? PlannedSurgeryName { get; set; }
    public short ConsentSigned { get; set; } = 1;
    public string? ConsentDocId { get; set; }
    public int ValidDays { get; set; } = 7;
    public long RegisteredAtMs { get; set; }
    public long ValidUntilMs { get; set; }
    public string Status { get; set; } = "CHECKING";
    public int TotalOrderCount { get; set; }
    public int DoneOrderCount { get; set; }
    public short HasCriticalAlert { get; set; }
    public string? CriticalDiagName { get; set; }
    public string? CriticalSummary { get; set; }
    public long? CriticalTriggeredAtMs { get; set; }
    public string? ConvertedEncounterId { get; set; }
    public string? ConvertedDeptName { get; set; }
    public string? ConvertedPrimaryDiag { get; set; }
    public long? AdmittedAtMs { get; set; }
    public string? Remark { get; set; }
}

public class IpdPreAdmissionOrder : BaseEntity
{
    public string PreAdmissionId { get; set; } = string.Empty;
    public string? BundleId { get; set; }
    public string? ServiceRequestId { get; set; }
    public string? ReportId { get; set; }
    public string FeeCategory { get; set; } = "LAB";
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? ExecDeptName { get; set; }
    public decimal Amount { get; set; }
    public string ExecStatus { get; set; } = "PENDING"; // PENDING, DONE, HOLD_BY_CRITICAL, CANCELLED
    public string BillingTrack { get; set; } = "PRE_ADMIT_HOLD"; // PRE_ADMIT_HOLD, MERGED_TO_IPD, SPLIT_TO_OPD, REFUNDED
    public string DoctorSplitSuggest { get; set; } = "KEEP_IPD"; // KEEP_IPD, SPLIT_OPD
    public string FinanceSplitConfirm { get; set; } = "PENDING"; // PENDING, KEEP_IPD, SPLIT_OPD
    public short IsCritical { get; set; }
    public string? ResultSummary { get; set; }
}

public class IpdPreAdmissionEvaluation : BaseEntity
{
    public string PreAdmissionId { get; set; } = string.Empty;
    public string EvalType { get; set; } = "ROUTINE_EVAL"; // ROUTINE_EVAL, CRITICAL_HANDLE, ANESTHESIA_CONSULT
    public string RuleLevel { get; set; } = "P2"; // P0, P1, P2
    public string Decision { get; set; } = "APPROVE_ADMIT";
    public string? NewPrimaryDiag { get; set; }
    public string? NewTargetDept { get; set; }
    public string? ReasonNote { get; set; }
    public string? ReferencedReportIdsJson { get; set; }
    public string? EvaluatorId { get; set; }
    public string? EvaluatorName { get; set; }
    public string? SignatureHash { get; set; }
    public long EvaluatedAtMs { get; set; }
}

public class IpdPreAdmissionRule : BaseEntity
{
    public string RuleCode { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string RuleLevel { get; set; } = "P0"; // P0, P1, P2
    public string SourceDiagPrefix { get; set; } = "*";
    public string MatchKeyword { get; set; } = string.Empty;
    public string TargetSpecialtyDept { get; set; } = string.Empty;
    public string OnHitAction { get; set; } = "INTERRUPT_AND_HOLD"; // INTERRUPT_AND_HOLD, REQUIRE_CONSULT, WARN_ONLY
    public short IsActive { get; set; } = 1;
    public string? Remark { get; set; }
}
