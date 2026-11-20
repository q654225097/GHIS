using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HIS.BuildingBlocks.Security;

/// <summary>
/// 对接第三方合法 CA 机构 (BJCA / CFCA / 医网签) 的 REST 国密协同签名提供者。
/// 支持未配置时自动降级至 DevSandboxCaProvider。
/// </summary>
public sealed class HttpThirdPartyCaProvider : ICaSignatureProvider
{
    private readonly HttpClient? _httpClient;
    private readonly string? _endpoint;
    private readonly string? _appId;
    private readonly string? _appSecret;
    private readonly DevSandboxCaProvider _sandboxFallback = new();

    public HttpThirdPartyCaProvider(HttpClient? httpClient = null)
    {
        _httpClient = httpClient;
        _endpoint = Environment.GetEnvironmentVariable("HIS_CA_ENDPOINT");
        _appId = Environment.GetEnvironmentVariable("HIS_CA_APP_ID");
        _appSecret = Environment.GetEnvironmentVariable("HIS_CA_APP_SECRET");
    }

    public async Task<CaSignResult> SignAsync(string userId, string certId, byte[] sm3Digest, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_endpoint) || string.Equals(Environment.GetEnvironmentVariable("HIS_CA_PROVIDER"), "DevSandbox", StringComparison.OrdinalIgnoreCase))
        {
            return await _sandboxFallback.SignAsync(userId, certId, sm3Digest, ct);
        }

        try
        {
            var client = _httpClient ?? new HttpClient();
            var payload = new
            {
                appId = _appId,
                userId,
                certId,
                digestBase64 = Convert.ToBase64String(sm3Digest),
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            using var response = await client.PostAsJsonAsync($"{_endpoint.TrimEnd('/')}/api/v1/sign/sm2", payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                return new CaSignResult(false, string.Empty, certId, string.Empty, "THIRD_PARTY_CA", $"第三方 CA 签名接口返回错误码: {response.StatusCode}");
            }

            var doc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            string sig = doc.GetProperty("signature").GetString() ?? string.Empty;
            string sn = doc.TryGetProperty("certSn", out var snElem) ? snElem.GetString() ?? certId : certId;
            string issuer = doc.TryGetProperty("certIssuer", out var isElem) ? isElem.GetString() ?? "ThirdParty CA" : "ThirdParty CA";

            return new CaSignResult(true, sig, sn, issuer, "THIRD_PARTY_CA");
        }
        catch (Exception ex)
        {
            // 在网络不可用时记录异常并安全回退
            return new CaSignResult(false, string.Empty, certId, string.Empty, "THIRD_PARTY_CA", $"第三方 CA 签名异常: {ex.Message}");
        }
    }

    public async Task<CaVerifyResult> VerifyAsync(byte[] sm3Digest, string p7SignatureBase64, string certSn, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_endpoint) || string.Equals(Environment.GetEnvironmentVariable("HIS_CA_PROVIDER"), "DevSandbox", StringComparison.OrdinalIgnoreCase))
        {
            return await _sandboxFallback.VerifyAsync(sm3Digest, p7SignatureBase64, certSn, ct);
        }

        try
        {
            var client = _httpClient ?? new HttpClient();
            var payload = new
            {
                appId = _appId,
                certSn,
                digestBase64 = Convert.ToBase64String(sm3Digest),
                signature = p7SignatureBase64
            };

            using var response = await client.PostAsJsonAsync($"{_endpoint.TrimEnd('/')}/api/v1/verify/sm2", payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                return new CaVerifyResult(false, certSn, string.Empty, $"第三方 CA 验签接口返回错误码: {response.StatusCode}");
            }

            var doc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            bool isValid = doc.GetProperty("valid").GetBoolean();
            string subject = doc.TryGetProperty("certSubject", out var subElem) ? subElem.GetString() ?? $"SN={certSn}" : $"SN={certSn}";

            return new CaVerifyResult(isValid, certSn, subject, isValid ? null : "第三方 CA 验签未通过");
        }
        catch (Exception ex)
        {
            return new CaVerifyResult(false, certSn, string.Empty, $"第三方 CA 验签接口异常: {ex.Message}");
        }
    }
}
