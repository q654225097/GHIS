using System;
using System.Collections.Generic;
using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Operations.Models;

public class IpcInfectionReport : BaseEntity
{
    public string ReportNo { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string WardId { get; set; } = string.Empty;
    public string InfectionSite { get; set; } = string.Empty; // 呼吸系统, 手术部位, 泌尿系统, 血液循环, 其他
    public string? Pathogen { get; set; }
    public short IsMdro { get; set; } = 0; // 多重耐药菌
    public long DiagnosedAtMs { get; set; }
    public string? ClinicalManifestation { get; set; }
    public int Status { get; set; } = 0; // 0=REPORTED, 1=VERIFIED, 2=INTERVENED, 3=CLOSED
    public string ReporterId { get; set; } = string.Empty;
    public string? ReviewerId { get; set; }
}

public class IpcIsolationOrder : BaseEntity
{
    public string OrderNo { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string WardId { get; set; } = string.Empty;
    public string IsolationType { get; set; } = "CONTACT"; // CONTACT, DROPLET, AIRBORNE, PROTECTIVE
    public string Reason { get; set; } = string.Empty;
    public long StartMs { get; set; }
    public long? EndMs { get; set; }
    public int Status { get; set; } = 0; // 0=ACTIVE, 1=ENDED
    public string? EndReason { get; set; }
    public string PrescribedBy { get; set; } = string.Empty;
    public string? EndedBy { get; set; }
}

public class IpcOccupationalExposure : BaseEntity
{
    public string ReportNo { get; set; } = string.Empty;
    public string StaffId { get; set; } = string.Empty;
    public string StaffName { get; set; } = string.Empty;
    public string DeptId { get; set; } = string.Empty;
    public string ExposureType { get; set; } = "SHARP_INJURY"; // SHARP_INJURY, MUCOUS_MEMBRANE, BROKEN_SKIN
    public string? SourcePatientId { get; set; }
    public string? SourcePathogen { get; set; } // HBV, HCV, HIV, SYPHILIS, UNKNOWN
    public long ExposedAtMs { get; set; }
    public string? EmergencyDisposition { get; set; }
    public string? PepTreatment { get; set; }
    public int Status { get; set; } = 0; // 0=REPORTED, 1=EVALUATED, 2=FOLLOWING_UP, 3=CLOSED
    public string ReporterId { get; set; } = string.Empty;
}

public class IpcAdverseEvent : BaseEntity
{
    public string EventNo { get; set; } = string.Empty;
    public string ReporterId { get; set; } = string.Empty;
    public string ReporterName { get; set; } = string.Empty;
    public string DeptId { get; set; } = string.Empty;
    public string? EncounterId { get; set; }
    public string? PatientId { get; set; }
    public string EventCategory { get; set; } = "FALL"; // FALL, MEDICATION_ERROR, PRESSURE_ULCER, EXTUBATION, OTHER
    public string SeverityLevel { get; set; } = "IV"; // I (警告), II (不良后果), III (未造成后果), IV (隐患)
    public string Description { get; set; } = string.Empty;
    public string? ImmediateAction { get; set; }
    public int Status { get; set; } = 0; // 0=SUBMITTED, 1=INVESTIGATING, 2=ROOT_CAUSE_ANALYZED, 3=CLOSED
    public string? ClosureComment { get; set; }
}

public class IpcHandHygieneAudit : BaseEntity
{
    public string WardId { get; set; } = string.Empty;
    public string ObserverId { get; set; } = string.Empty;
    public string AuditDate { get; set; } = string.Empty; // yyyy-MM-dd
    public string MomentType { get; set; } = "TOUCH_PATIENT"; // TOUCH_PATIENT, ASEPTIC, BODY_FLUID, TOUCH_PATIENT_AFTER, TOUCH_SURROUNDINGS
    public string StaffCategory { get; set; } = "NURSE"; // DOCTOR, NURSE, CAREGIVER, OTHER
    public int OpportunityCount { get; set; } = 0;
    public int PerformedCount { get; set; } = 0;
    public int CorrectCount { get; set; } = 0;
    public decimal ComplianceRate { get; set; } = 0m;
    public decimal AccuracyRate { get; set; } = 0m;
}

// DTOs
public sealed record CreateInfectionReportRequest(
    string EncounterId,
    string PatientId,
    string PatientName,
    string WardId,
    string InfectionSite,
    string? Pathogen,
    bool IsMdro,
    string? ClinicalManifestation
);

public sealed record UpdateInfectionReportStatusRequest(
    int Status,
    string? ReviewerId = null,
    string? Note = null
);

public sealed record CreateIsolationOrderRequest(
    string EncounterId,
    string PatientId,
    string WardId,
    string IsolationType,
    string Reason
);

public sealed record EndIsolationOrderRequest(
    string EndReason
);

public sealed record CreateOccupationalExposureRequest(
    string StaffId,
    string StaffName,
    string DeptId,
    string ExposureType,
    string? SourcePatientId,
    string? SourcePathogen,
    string? EmergencyDisposition,
    string? PepTreatment
);

public sealed record UpdateExposureDispositionRequest(
    int Status,
    string? EmergencyDisposition,
    string? PepTreatment
);

public sealed record CreateAdverseEventRequest(
    string DeptId,
    string? EncounterId,
    string? PatientId,
    string EventCategory,
    string SeverityLevel,
    string Description,
    string? ImmediateAction
);

public sealed record CloseAdverseEventRequest(
    string ClosureComment
);

public sealed record CreateHandHygieneAuditRequest(
    string WardId,
    string AuditDate,
    string MomentType,
    string StaffCategory,
    int OpportunityCount,
    int PerformedCount,
    int CorrectCount
);

public sealed record IpcSummaryDto(
    int ActiveIsolationCount,
    int TotalInfectionReports,
    int MdroInfectionCount,
    int OpenAdverseEventsCount,
    int OccupationalExposuresCount,
    decimal AverageHandHygieneComplianceRate
);
