using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Inpatient.Models;

public class IpdDischargeRequest : BaseEntity
{
    public string RequestNo { get; set; } = string.Empty;

    public string EncounterId { get; set; } = string.Empty;

    public string PatientId { get; set; } = string.Empty;

    public string? DischargeDiagnosis { get; set; }

    public string? DischargeInstructions { get; set; }

    public long RequestedAtMs { get; set; }

    public long? SettledAtMs { get; set; }

    public string? SettledBy { get; set; }

    public string Status { get; set; } = "PENDING";

    public string? SettlementRequestKey { get; set; }

    public string? SettlementPaymentId { get; set; }

    public decimal SettlementTotalAmount { get; set; }

    public decimal InsuranceAmount { get; set; }

    public decimal PrepaymentDeduct { get; set; }

    public decimal SelfPayAmount { get; set; }

    public decimal RefundAmount { get; set; }

    public string ApprovalStatus { get; set; } = "DRAFT";

    public string? WorkflowInstanceId { get; set; }

    public long? SubmittedAtMs { get; set; }

    public long? ApprovedAtMs { get; set; }

    public string? ApprovedBy { get; set; }
}
