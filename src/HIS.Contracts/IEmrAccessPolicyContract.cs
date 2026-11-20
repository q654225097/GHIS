using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HIS.Contracts;

public sealed record EmrAccessRequest(
    string UserId,
    string UserName,
    string? UserRole,
    string? UserDeptId,
    string? ExternalOrgCode,
    bool IsExternalDoctor,
    string PatientId,
    string? EncounterId,
    string ResourceType,
    string ResourceId,
    IReadOnlyList<string>? SectionCodes = null,
    string ActionType = "READ",
    string? ClientIp = null
);

public sealed record EmrAccessDecision(
    bool Allowed,
    string? DenyReason = null,
    string? GrantId = null,
    bool RequiresSectionMasking = false,
    IReadOnlyList<string>? DeniedSectionCodes = null
);

public sealed record SecEmrAccessGrantDto(
    string Id,
    string PatientId,
    string? EncounterId,
    string GranteeUserId,
    string? GranteeOrgCode,
    bool IsExternalGrant,
    IReadOnlyList<string> AllowedScopes,
    long ValidFromMs,
    long ValidToMs,
    string GrantReason,
    string? PatientConsentId,
    string ApprovedBy,
    short Status
);

public sealed record SecEmrReadAuditLogDto(
    string Id,
    string AccessUserId,
    string AccessUserName,
    string? AccessOrgCode,
    bool IsExternal,
    string PatientId,
    string? EncounterId,
    string ResourceType,
    string ResourceId,
    string ActionType,
    string Decision,
    string? DenyReason,
    string PrevHash,
    string CurrHash,
    long CreatedAtMs
);

public interface IEmrAccessPolicyContract
{
    Task<EmrAccessDecision> AuthorizeAndAuditReadAsync(
        EmrAccessRequest request,
        CancellationToken ct = default);

    Task<string> FilterOrMaskSectionContentAsync(
        string templateId,
        string sectionCode,
        string roleCode,
        string rawContent,
        CancellationToken ct = default);
}
