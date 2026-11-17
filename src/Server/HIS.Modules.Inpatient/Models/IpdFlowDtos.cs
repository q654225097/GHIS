namespace HIS.Modules.Inpatient.Models;

public sealed class IpdAdmissionCardView : IpdAdmissionCard
{
    public string PatientName { get; set; } = string.Empty;

    public string GenderName { get; set; } = string.Empty;

    public string MedicalRecordNo { get; set; } = string.Empty;

    public string SourceDoctorName { get; set; } = string.Empty;

    public string SuggestedIpdDeptName { get; set; } = string.Empty;

    public string SuggestedWardName { get; set; } = string.Empty;

    public string EncounterNo { get; set; } = string.Empty;

    public decimal PrepaymentBalance { get; set; }

    public string BedCode { get; set; } = string.Empty;

    public int TriageStatus { get; set; }

    public bool AlreadyRegistered => !string.IsNullOrWhiteSpace(EncounterId);
}

public sealed class IpdPrepaymentView : IpdPrepayment
{
    public string CashierName { get; set; } = string.Empty;
}

public sealed class IpdWardView
{
    public string Id { get; set; } = string.Empty;

    public string DeptCode { get; set; } = string.Empty;

    public string DeptName { get; set; } = string.Empty;

    public string RelationType { get; set; } = "PRIMARY";

    public bool IsBorrowed => RelationType == "BORROW_ALLOWED";
}

public sealed class IpdDeptView
{
    public string Id { get; set; } = string.Empty;

    public string DeptCode { get; set; } = string.Empty;

    public string DeptName { get; set; } = string.Empty;

    public IReadOnlyList<IpdWardView> Wards { get; set; } = Array.Empty<IpdWardView>();
}

public sealed class IpdStaffView
{
    public string Id { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string EmployeeNo { get; set; } = string.Empty;

    public string RoleCode { get; set; } = string.Empty;

    public string RoleName { get; set; } = string.Empty;

    public string DeptId { get; set; } = string.Empty;

    public string DeptCategory { get; set; } = string.Empty;
}

public sealed class IpdRegistrationOptionsView
{
    public IReadOnlyList<IpdDeptView> Depts { get; set; } = Array.Empty<IpdDeptView>();
}

public sealed class CreateAdmissionCardRequest
{
    public string PatientId { get; set; } = string.Empty;

    public string? VisitId { get; set; }

    public string SuggestedIpdDeptId { get; set; } = string.Empty;

    public string? SuggestedWardId { get; set; }

    public string? AdmissionDiagnosis { get; set; }

    public string? ContactPhone { get; set; }

    public string? Remark { get; set; }

    public string? RequestKey { get; set; }
}

public sealed class RegisterAdmissionCardRequest
{
    public string IpdDeptId { get; set; } = string.Empty;

    public string WardId { get; set; } = string.Empty;

    public string BedCode { get; set; } = string.Empty;

    public bool IsBorrowedBed { get; set; }

    public decimal InitialPrepayment { get; set; }

    public string PaymentChannel { get; set; } = "CASH";

    public string? RequestKey { get; set; }

    public string? Remark { get; set; }
}

public sealed class AddIpdPrepaymentRequest
{
    public decimal Amount { get; set; }

    public string PaymentChannel { get; set; } = "CASH";

    public string? RequestKey { get; set; }

    public string? Remark { get; set; }
}

public sealed class AcceptIpdPatientRequest
{
    public string AttendingDoctorId { get; set; } = string.Empty;

    public string PrimaryNurseId { get; set; } = string.Empty;

    public string NursingLevelCode { get; set; } = "LEVEL_1";
}

public sealed class UpdateIpdAssignmentRequest
{
    public string AttendingDoctorId { get; set; } = string.Empty;

    public string PrimaryNurseId { get; set; } = string.Empty;

    public string NursingLevelCode { get; set; } = "LEVEL_1";
}
