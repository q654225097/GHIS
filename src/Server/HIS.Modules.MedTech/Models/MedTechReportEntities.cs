using HIS.BuildingBlocks.Database;

namespace HIS.Modules.MedTech.Models;

public class MtcReportTemplate : BaseEntity
{
    public string TemplateCode { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string MedtechDeptCode { get; set; } = "RAD"; // RAD, US, LAB, ECG, ENDO
    public string MedtechDeptName { get; set; } = "放射科";
    public string ModalityType { get; set; } = "CT/DR";
    public string EditorSchemaJson { get; set; } = "[]";
    public decimal PrintPaperWidthMm { get; set; } = 210m;
    public decimal PrintPaperHeightMm { get; set; } = 297m;
    public string PrintHeaderTitle { get; set; } = "GHIS 示范医院 医技诊断报告单";
    public string? PrintSubTitle { get; set; }
    public string? PrintFooterNote { get; set; }
    public string? PrintTemplateId { get; set; }
    public short IsDefault { get; set; }
    public short IsActive { get; set; } = 1;
    public string? Remark { get; set; }
}

public class MtcDiagnosticReport : BaseEntity
{
    public string ReportNo { get; set; } = string.Empty;
    public string? ServiceRequestId { get; set; }
    public string? RequestNo { get; set; }
    public string? PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string Gender { get; set; } = "男";
    public int Age { get; set; } = 35;
    public string? MedicalRecordNo { get; set; }
    public string? OrderDeptName { get; set; }
    public string MedtechDeptCode { get; set; } = "RAD";
    public string MedtechDeptName { get; set; } = "放射科";
    public string? ExamItemCode { get; set; }
    public string ExamItemName { get; set; } = string.Empty;
    public string? TemplateId { get; set; }
    public string? TemplateName { get; set; }
    public string ResultSource { get; set; } = "MANUAL"; // MANUAL, DEVICE_AUTO
    public string? DeviceName { get; set; }
    public string Status { get; set; } = "PENDING_WRITE"; // PENDING_WRITE, DRAFT, PENDING_REVIEW, REJECTED, SIGNED
    public string ContentJson { get; set; } = "{}";
    public string? FindingsSummary { get; set; }
    public string? DiagnosisImpression { get; set; }
    public short IsCriticalValue { get; set; }
    public string? WriterId { get; set; }
    public string? WriterName { get; set; }
    public long? WrittenAtMs { get; set; }
    public string? ReviewerId { get; set; }
    public string? ReviewerName { get; set; }
    public long? ReviewedAtMs { get; set; }
    public string? RejectReason { get; set; }
    public string? SignatureHash { get; set; }
}
