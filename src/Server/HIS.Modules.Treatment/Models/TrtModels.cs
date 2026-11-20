using System;
using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Treatment.Models;

public class TrtTreatmentCatalog : BaseEntity
{
    public string CatalogCode { get; set; } = string.Empty;
    public string CatalogName { get; set; } = string.Empty;
    public string Category { get; set; } = "REHAB"; // REHAB / CHEMO / RADIOTHERAPY / DIALYSIS / PHYSIO / HBO
    public string ExecDeptId { get; set; } = string.Empty;
    public short IsHighRisk { get; set; }
    public string? ChecklistSchemaJson { get; set; }
    public decimal UnitPrice { get; set; }
    public short IsActive { get; set; } = 1;
}

public class TrtRequest : BaseEntity
{
    public string RequestNo { get; set; } = string.Empty;
    public string SourceType { get; set; } = "IPD"; // OPD / IPD / EXTERNAL
    public string? SourceOrgCode { get; set; }
    public string? ExternalPatientId { get; set; }
    public string PatientId { get; set; } = string.Empty;
    public string EncounterOrVisitId { get; set; } = string.Empty;
    public string CatalogId { get; set; } = string.Empty;
    public string? DiagnosisIcd { get; set; }
    public int PrescribedSessions { get; set; } = 1;
    public int CompletedSessions { get; set; }
    public string Status { get; set; } = "REQUESTED"; // REQUESTED / SCHEDULED / IN_PROGRESS / COMPLETED / CANCELLED
    public string? Remark { get; set; }
}

public class TrtAppointment : BaseEntity
{
    public string RequestId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string TreatmentRoom { get; set; } = string.Empty;
    public long ScheduledStartMs { get; set; }
    public long ScheduledEndMs { get; set; }
    public short ReminderSent { get; set; }
    public string Status { get; set; } = "BOOKED"; // BOOKED / ARRIVED / EXECUTED / CANCELLED
}

public class TrtParameterRule : BaseEntity
{
    public string CatalogId { get; set; } = string.Empty;
    public string LabItemCode { get; set; } = string.Empty;
    public string ParameterName { get; set; } = string.Empty;
    public string FormulaOrRangeJson { get; set; } = string.Empty;
    public string ParamSuggestionTemplate { get; set; } = string.Empty;
    public string? WarningNotice { get; set; }
    public short IsActive { get; set; } = 1;
}

public class TrtExecutionRecord : BaseEntity
{
    public string RequestId { get; set; } = string.Empty;
    public string? AppointmentId { get; set; }
    public string PatientId { get; set; } = string.Empty;
    public int SessionNo { get; set; } = 1;
    public long ActualStartMs { get; set; }
    public long ActualEndMs { get; set; }
    public string? SuggestedParamsJson { get; set; }
    public string? ActualParamsJson { get; set; }
    public short HighRiskCheckPassed { get; set; }
    public string? DualCheckOperatorId { get; set; }
    public string? PatientReaction { get; set; }
    public short FeeCharged { get; set; }
    public string? CaSignatureId { get; set; }
    public string Status { get; set; } = "FINISHED";
}
