using System;
using System.Threading;
using System.Threading.Tasks;

namespace HIS.BuildingBlocks.Security;

public record TsaTokenResult(
    bool Success,
    string TokenBase64,
    long GenTimeMs,
    string PolicyOid,
    string TsaUrl,
    string? ErrorMessage = null
);

public record TsaVerifyResult(
    bool IsValid,
    long GenTimeMs,
    string PolicyOid,
    string? ErrorMessage = null
);

public interface IExternalTsaClient
{
    Task<TsaTokenResult> RequestTimestampAsync(byte[] sm3Digest, CancellationToken ct = default);
    Task<TsaVerifyResult> VerifyTimestampAsync(string tokenBase64, byte[] expectedDigest, CancellationToken ct = default);
}
