using System;
using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Hr.Models;

public class HrEmployee : BaseEntity
{
    public string EmployeeNo { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Gender { get; set; } = "UNKNOWN";
    public int? BirthDateInt { get; set; }
    public string? IdCardCipher { get; set; }
    public string? IdCardBlindIndex { get; set; }
    public string? IdCardMasked { get; set; }
    public string? PhoneCipher { get; set; }
    public string? PhoneMasked { get; set; }
    public string? Email { get; set; }
    public string? Education { get; set; }
    public string? GraduatedSchool { get; set; }
    public string? MajorName { get; set; }
    public string? PoliticalStatus { get; set; }
    public int HireDateInt { get; set; }
    public int? ContractEndDateInt { get; set; }
    public string EmploymentType { get; set; } = "CONTRACT";
    public string StaffCategory { get; set; } = "DOCTOR";
    public string PrimaryDeptId { get; set; } = string.Empty;
    public string? DirectSupervisorId { get; set; }
    public string ProfessionalTitle { get; set; } = "住院医师";
    public string? JobPosition { get; set; }
    public string EmploymentStatus { get; set; } = "ACTIVE";
    public int? ResignDateInt { get; set; }
    public string? ResignReason { get; set; }
    public string ApprovalStatus { get; set; } = "APPROVED";
    public string? WorkflowInstanceId { get; set; }
    public string? Remark { get; set; }
}

public class HrEmployeeCredential : BaseEntity
{
    public string EmployeeId { get; set; } = string.Empty;
    public string CredentialType { get; set; } = "PHYSICIAN_LICENSE";
    public string CertNo { get; set; } = string.Empty;
    public string? IssuingAuthority { get; set; }
    public int? IssueDateInt { get; set; }
    public int? ExpiryDateInt { get; set; }
    public int GrantedLevel { get; set; } = 1;
    public string VerifyStatus { get; set; } = "VALID";
    public string? AttachmentUri { get; set; }
    public string? Remark { get; set; }
}

public class HrChangeRecord : BaseEntity
{
    public string ChangeNo { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeNo { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string ChangeType { get; set; } = "INFO_UPDATE";
    public string? BeforeSnapshotJson { get; set; }
    public string? AfterSnapshotJson { get; set; }
    public int EffectiveDateInt { get; set; }
    public string? Reason { get; set; }
    public string ApprovalStatus { get; set; } = "APPROVED";
    public string? WorkflowInstanceId { get; set; }
    public long? AppliedAtMs { get; set; }
    public string? AppliedBy { get; set; }
}

public class HrContract : BaseEntity
{
    public string ContractNo { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string ContractType { get; set; } = "FIXED_TERM";
    public int StartDateInt { get; set; }
    public int? EndDateInt { get; set; }
    public int ProbationMonths { get; set; } = 3;
    public string Status { get; set; } = "ACTIVE";
    public string? Remark { get; set; }
}
