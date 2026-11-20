using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Pharmacy.Models;

public class OrdOrderReviewLog : BaseEntity
{
    public string EncounterOrVisitId { get; set; } = string.Empty;
    public string OrderOrRxId { get; set; } = string.Empty;
    public string RuleType { get; set; } = string.Empty;
    public string Severity { get; set; } = "WARNING"; // INFO / WARNING / BLOCK
    public string RuleCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public short IsBlocked { get; set; } = 0;
    public short IsOverridden { get; set; } = 0;
    public string? OverrideReason { get; set; }
    public long ReviewedAtMs { get; set; }
}

public class PhmPrescriptionComment : BaseEntity
{
    public string SourceType { get; set; } = "OPD"; // OPD / IPD
    public string TargetId { get; set; } = string.Empty;
    public string DoctorId { get; set; } = string.Empty;
    public string PharmacistId { get; set; } = string.Empty;
    public short IsRational { get; set; } = 1;
    public string? DefectCodesJson { get; set; }
    public string CommentText { get; set; } = string.Empty;
    public string? ErrorCategory { get; set; }
    public string DoctorAckStatus { get; set; } = "PENDING"; // PENDING / ACKED / APPEALED
    public string? DoctorReply { get; set; }
}

public class AdrReport : BaseEntity
{
    public string PatientId { get; set; } = string.Empty;
    public string EncounterOrVisitId { get; set; } = string.Empty;
    public string? OrderOrRxId { get; set; }
    public string DrugId { get; set; } = string.Empty;
    public string DrugName { get; set; } = string.Empty;
    public string? BatchNo { get; set; }
    public string ReactionName { get; set; } = string.Empty;
    public long OnsetTimeMs { get; set; }
    public string Severity { get; set; } = "GENERAL"; // GENERAL / SERIOUS
    public string Outcome { get; set; } = "RECOVERED";
    public string CausalityGrade { get; set; } = "PROBABLE";
    public string ReportStatus { get; set; } = "DRAFT"; // DRAFT / SUBMITTED_INTERNAL / REPORTED_EXTERNAL
    public string? ExternalReportNo { get; set; }
    public string? KbRuleGeneratedId { get; set; }
}

public class PhmWardPivasBatch : BaseEntity
{
    public string? EncounterId { get; set; }
    public string WardId { get; set; } = string.Empty;
    public string BatchNo { get; set; } = string.Empty;
    public string PrepType { get; set; } = "UNIT_DOSE"; // UNIT_DOSE / PIVAS
    public string PharmacistAuditBy { get; set; } = string.Empty;
    public long? LabelPrintedAtMs { get; set; }
    public string? CompoundedBy { get; set; }
    public string? RecheckedBy { get; set; }
    public long? DispatchedAtMs { get; set; }
    public string Status { get; set; } = "CREATED";
}

public class PhmUnitDoseBarcode : BaseEntity
{
    public string Barcode { get; set; } = string.Empty;
    public string? DispenseOrderId { get; set; }
    public string? PivasBatchId { get; set; }
    public string MedicalOrderId { get; set; } = string.Empty;
    public string DrugId { get; set; } = string.Empty;
    public string DoseText { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string Status { get; set; } = "PRINTED"; // PRINTED / DISPENSED / EXECUTED / RETURNED
}

public class PhmMedicationReconciliation : BaseEntity
{
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string Source { get; set; } = "PATIENT_REPORT"; // PATIENT_REPORT / INTERNAL_OPD / EXTERNAL_HIE
    public string DrugName { get; set; } = string.Empty;
    public string DosageText { get; set; } = string.Empty;
    public string Decision { get; set; } = "CONTINUE"; // CONTINUE / MODIFY / STOP
    public string ReconciledBy { get; set; } = string.Empty;
}

public class PhmExternalPrescription : BaseEntity
{
    public string ExternalRxNo { get; set; } = string.Empty;
    public string SourceOrgCode { get; set; } = string.Empty;
    public string SourceOrgName { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string RxPayloadJson { get; set; } = "{}";
    public short CaSignatureVerified { get; set; } = 0;
    public string DispenseStatus { get; set; } = "PENDING"; // PENDING / DISPENSED / REJECTED
    public string? DispensedBy { get; set; }
    public long? DispensedAtMs { get; set; }
}
