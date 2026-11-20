using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Platform.Models;

public class SecEmrAccessGrant : BaseEntity
{
    public string PatientId { get; set; } = string.Empty;
    public string? EncounterId { get; set; }
    public string GranteeUserId { get; set; } = string.Empty;
    public string? GranteeOrgCode { get; set; }
    public short IsExternalGrant { get; set; } = 0;
    public string AllowedScopesJson { get; set; } = "[\"EMR\",\"ORDER\",\"LAB\",\"EXAM\",\"IMAGE\",\"NURSING\",\"PRESCRIPTION\"]";
    public long ValidFromMs { get; set; }
    public long ValidToMs { get; set; }
    public string GrantReason { get; set; } = "CONSULTATION";
    public string? PatientConsentId { get; set; }
    public string ApprovedBy { get; set; } = string.Empty;
    public string ApprovalStatus { get; set; } = "DRAFT";
    public string? WorkflowInstanceId { get; set; }
    public long? SubmittedAtMs { get; set; }
    public long? ApprovedAtMs { get; set; }
    public short Status { get; set; } = 1;
}

public class EmrSectionAcl : BaseEntity
{
    public string TemplateId { get; set; } = string.Empty;
    public string SectionCode { get; set; } = string.Empty;
    public string RoleCode { get; set; } = string.Empty;
    public short CanRead { get; set; } = 1;
    public short CanWrite { get; set; } = 1;
    public short MaskSensitive { get; set; } = 0;
}

public class SecEmrReadAuditLog : BaseEntity
{
    public string AccessUserId { get; set; } = string.Empty;
    public string AccessUserName { get; set; } = string.Empty;
    public string? AccessOrgCode { get; set; }
    public short IsExternal { get; set; } = 0;
    public string PatientId { get; set; } = string.Empty;
    public string? EncounterId { get; set; }
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string? SectionCodesJson { get; set; }
    public string ActionType { get; set; } = "READ";
    public string? GrantId { get; set; }
    public string? ClientIp { get; set; }
    public string Decision { get; set; } = "ALLOW";
    public string? DenyReason { get; set; }
    public string PrevHash { get; set; } = "GENESIS";
    public string CurrHash { get; set; } = string.Empty;
}
