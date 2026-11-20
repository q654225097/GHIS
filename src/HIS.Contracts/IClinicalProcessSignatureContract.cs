using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HIS.Contracts;

public sealed record SecDigitalSignatureDto(
    string Id,
    string EntityType,
    string EntityId,
    string BusinessStage,
    string SignerUserId,
    string SignerName,
    string CaProvider,
    string CaCertSn,
    string CaCertIssuer,
    string DataSm3Hash,
    string SignatureAlg,
    string P7SignatureBase64,
    string TsaProviderUrl,
    string TsaPolicyOid,
    string TsaTokenBase64,
    long TsaGenTimeMs,
    short VerifyStatus,
    long CreatedAtMs
);

public sealed record SignatureVerificationReportDto(
    string SignatureId,
    string EntityType,
    string EntityId,
    string BusinessStage,
    bool OverallValid,
    bool DataIntegrityPassed,
    bool CaSignatureValid,
    bool TsaTokenValid,
    string? SignerName,
    string? CaCertSn,
    long TsaGenTimeMs,
    string? ErrorMessage
);

public interface IClinicalProcessSignatureContract
{
    Task<SecDigitalSignatureDto> SignClinicalRecordAsync(
        string entityType,
        string entityId,
        string stage,
        object canonicalPayload,
        IUserSession signer,
        CancellationToken ct = default);

    Task<IReadOnlyList<SecDigitalSignatureDto>> GetSignaturesAsync(
        string entityType,
        string entityId,
        CancellationToken ct = default);

    Task<SignatureVerificationReportDto> VerifySignatureAsync(
        string signatureId,
        CancellationToken ct = default);
}
