using System.Collections.Generic;

namespace HIS.Modules.Inpatient.Models;

public sealed class IpdDischargeRequestView : IpdDischargeRequest
{
    public string PatientName { get; set; } = string.Empty;

    public string MedicalRecordNo { get; set; } = string.Empty;

    public string AdmissionNo { get; set; } = string.Empty;

    public string DeptName { get; set; } = string.Empty;

    public string WardName { get; set; } = string.Empty;

    public string BedCode { get; set; } = string.Empty;

    public string? DoctorName { get; set; }

    public string? PaymentNo { get; set; }

    public string? InvoiceOfdId { get; set; }

    public List<string> ClinicalBlockers { get; set; } = new();
}

public sealed class IpdSettlementChargeView
{
    public string ItemCode { get; set; } = string.Empty;

    public string ItemName { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal TotalAmount { get; set; }
}

public sealed class CreateIpdDischargeRequest
{
    public string? DischargeDiagnosis { get; set; }

    public string? DischargeInstructions { get; set; }
}

public sealed class SettleIpdDischargeRequest
{
    public decimal TotalAmount { get; set; }

    public decimal InsuranceAmount { get; set; }

    public decimal PrepaymentDeduct { get; set; }

    public decimal SelfPayAmount { get; set; }

    public decimal RefundAmount { get; set; }

    public string PaymentChannel { get; set; } = "CASH";

    public string? RequestKey { get; set; }

    public List<IpdSettlementChargeView> ChargeItems { get; set; } = new();
}

public sealed class IpdSettlementPreviewView
{
    public string RequestId { get; set; } = string.Empty;

    public string EncounterId { get; set; } = string.Empty;

    public string PatientId { get; set; } = string.Empty;

    public string PatientName { get; set; } = string.Empty;

    public string MedicalRecordNo { get; set; } = string.Empty;

    public string AdmissionNo { get; set; } = string.Empty;

    public string DeptName { get; set; } = string.Empty;

    public string WardName { get; set; } = string.Empty;

    public string BedCode { get; set; } = string.Empty;

    public string Status { get; set; } = "PENDING";

    public decimal PrepaymentBalance { get; set; }

    public decimal ChargeTotal { get; set; }

    public List<IpdSettlementChargeView> ChargeItems { get; set; } = new();

    public List<string> ClinicalBlockers { get; set; } = new();

    public bool CanSettle => Status == "PENDING" && ClinicalBlockers.Count == 0;
}
