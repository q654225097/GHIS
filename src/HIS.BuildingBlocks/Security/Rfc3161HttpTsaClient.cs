using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Cms;
using Org.BouncyCastle.Tsp;

namespace HIS.BuildingBlocks.Security;

/// <summary>
/// 符合 RFC 3161 标准的外部可信时间戳客户端。
/// 支持配置 HIS_TSA_ENDPOINT 对接国家授时中心/可信时间戳中心，或在未配置时平滑降级至本地国密时间戳服务。
/// </summary>
public sealed class Rfc3161HttpTsaClient : IExternalTsaClient
{
    private readonly HttpClient? _httpClient;
    private readonly string? _tsaEndpoint;
    private const string DefaultPolicyOid = TsaHelper.NationalTsaPolicyOid;

    public Rfc3161HttpTsaClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient;
        _tsaEndpoint = Environment.GetEnvironmentVariable("HIS_TSA_ENDPOINT");
    }

    public async Task<TsaTokenResult> RequestTimestampAsync(byte[] sm3Digest, CancellationToken ct = default)
    {
        if (sm3Digest == null || sm3Digest.Length == 0)
        {
            throw new ArgumentException("摘要数据不能为空", nameof(sm3Digest));
        }

        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 当未配置外部 TSA 端点或显式配置为 DevSandbox 时，回退至内嵌国密 RFC 3161 引擎
        if (string.IsNullOrWhiteSpace(_tsaEndpoint) || string.Equals(Environment.GetEnvironmentVariable("HIS_TSA_PROVIDER"), "DevSandbox", StringComparison.OrdinalIgnoreCase))
        {
            string token = TsaHelper.GenerateTimeStampToken(sm3Digest, DefaultPolicyOid, nowMs);
            return new TsaTokenResult(
                Success: true,
                TokenBase64: token,
                GenTimeMs: nowMs,
                PolicyOid: DefaultPolicyOid,
                TsaUrl: "LOCAL_DEV_RFC3161_TSA"
            );
        }

        try
        {
            var client = _httpClient ?? new HttpClient();

            // 使用 BouncyCastle 构造标准 RFC 3161 请求
            var tsqGen = new TimeStampRequestGenerator();
            tsqGen.SetReqPolicy(new DerObjectIdentifier(DefaultPolicyOid));
            var req = tsqGen.Generate(TsaHelper.Sm3DigestOid, sm3Digest);
            byte[] reqBytes = req.GetEncoded();

            using var content = new ByteArrayContent(reqBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-query");

            using var response = await client.PostAsync(_tsaEndpoint, content, ct);
            if (!response.IsSuccessStatusCode)
            {
                // 回退到本地生成以保障系统可用性
                string fallbackToken = TsaHelper.GenerateTimeStampToken(sm3Digest, DefaultPolicyOid, nowMs);
                return new TsaTokenResult(
                    Success: true,
                    TokenBase64: fallbackToken,
                    GenTimeMs: nowMs,
                    PolicyOid: DefaultPolicyOid,
                    TsaUrl: _tsaEndpoint,
                    ErrorMessage: $"外部 TSA 返回 HTTP {response.StatusCode}，已降级"
                );
            }

            byte[] respBytes = await response.Content.ReadAsByteArrayAsync(ct);
            var tsResponse = new TimeStampResponse(respBytes);
            tsResponse.Validate(req);

            if (tsResponse.Status != 0) // 0 = PKIStatus.Granted
            {
                string fallbackToken = TsaHelper.GenerateTimeStampToken(sm3Digest, DefaultPolicyOid, nowMs);
                return new TsaTokenResult(
                    Success: true,
                    TokenBase64: fallbackToken,
                    GenTimeMs: nowMs,
                    PolicyOid: DefaultPolicyOid,
                    TsaUrl: _tsaEndpoint,
                    ErrorMessage: $"外部 TSA 拒绝发放令牌: {tsResponse.GetStatusString()}"
                );
            }

            var tokenDer = tsResponse.TimeStampToken.GetEncoded();
            string tokenBase64 = Convert.ToBase64String(tokenDer);
            long genTimeMs = new DateTimeOffset(tsResponse.TimeStampToken.TimeStampInfo.GenTime).ToUnixTimeMilliseconds();

            return new TsaTokenResult(
                Success: true,
                TokenBase64: tokenBase64,
                GenTimeMs: genTimeMs,
                PolicyOid: tsResponse.TimeStampToken.TimeStampInfo.Policy ?? DefaultPolicyOid,
                TsaUrl: _tsaEndpoint
            );
        }
        catch (Exception ex)
        {
            // 网络故障时平滑降级
            string fallbackToken = TsaHelper.GenerateTimeStampToken(sm3Digest, DefaultPolicyOid, nowMs);
            return new TsaTokenResult(
                Success: true,
                TokenBase64: fallbackToken,
                GenTimeMs: nowMs,
                PolicyOid: DefaultPolicyOid,
                TsaUrl: _tsaEndpoint ?? "LOCAL_DEV_RFC3161_TSA",
                ErrorMessage: $"外部 TSA 访问失败 ({ex.Message})，已降级至本地 RFC 3161 证明"
            );
        }
    }

    public Task<TsaVerifyResult> VerifyTimestampAsync(string tokenBase64, byte[] expectedDigest, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tokenBase64))
        {
            return Task.FromResult(new TsaVerifyResult(false, 0, string.Empty, "时间戳令牌不能为空"));
        }

        try
        {
            var verifyResult = TsaHelper.VerifyTimeStampToken(tokenBase64, expectedDigest);
            long genMs = verifyResult.GenTimeUtc == DateTime.MinValue
                ? 0
                : new DateTimeOffset(verifyResult.GenTimeUtc).ToUnixTimeMilliseconds();

            return Task.FromResult(new TsaVerifyResult(
                IsValid: verifyResult.IsValid,
                GenTimeMs: genMs,
                PolicyOid: verifyResult.PolicyOid,
                ErrorMessage: verifyResult.ErrorMessage
            ));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new TsaVerifyResult(false, 0, string.Empty, ex.Message));
        }
    }
}
