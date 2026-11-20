using System;
using System.Threading;
using System.Threading.Tasks;

namespace HIS.BuildingBlocks.Security;

public record CaSignResult(
    bool Success,
    string P7SignatureBase64,
    string CertSn,
    string CertIssuer,
    string CaProvider,
    string? ErrorMessage = null
);

public record CaVerifyResult(
    bool IsValid,
    string CertSn,
    string CertSubject,
    string? ErrorMessage = null
);

public interface ICaSignatureProvider
{
    Task<CaSignResult> SignAsync(string userId, string certId, byte[] sm3Digest, CancellationToken ct = default);
    Task<CaVerifyResult> VerifyAsync(byte[] sm3Digest, string p7SignatureBase64, string certSn, CancellationToken ct = default);
}
