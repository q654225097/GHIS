using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Inpatient.Models;

public class IpdAdmissionCard : BaseEntity
{
    public string CardNo { get; set; } = string.Empty;

    public string RequestKey { get; set; } = string.Empty;

    public string PatientId { get; set; } = string.Empty;

    public string? VisitId { get; set; }

    public string SourceDoctorId { get; set; } = string.Empty;

    public string SourceDeptId { get; set; } = string.Empty;

    public string SuggestedIpdDeptId { get; set; } = string.Empty;

    public string? SuggestedWardId { get; set; }

    public string? AdmissionDiagnosis { get; set; }

    public string? ContactPhone { get; set; }

    public string? Remark { get; set; }

    public string Status { get; set; } = "PENDING";

    public string? EncounterId { get; set; }

    public string? RegisteredBy { get; set; }

    public long? RegisteredAtMs { get; set; }
}
