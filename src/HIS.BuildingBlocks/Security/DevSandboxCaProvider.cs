using System;
using System.Threading;
using System.Threading.Tasks;

namespace HIS.BuildingBlocks.Security;

/// <summary>
/// 开发者沙箱国密 SM2/SM3 协同签名提供者。
/// 仅用于开发与测试阶段。
/// </summary>
public sealed class DevSandboxCaProvider : ICaSignatureProvider
{
    private static bool _warningLogged = false;

    public DevSandboxCaProvider()
    {
        if (!_warningLogged)
        {
            Console.WriteLine("[合规警示 - GB/T 39786] 当前正在使用 DEV_SANDBOX_SM2 沙箱签名提供者，生产环境必须配置 HIS_CA_ENDPOINT、HIS_CA_APP_ID 接入经国家密码管理局认证的合法第三方 CA 服务机构！");
            _warningLogged = true;
        }
    }

    public Task<CaSignResult> SignAsync(string userId, string certId, byte[] sm3Digest, CancellationToken ct = default)
    {
        try
        {
            string certSn = string.IsNullOrWhiteSpace(certId) ? $"DEV-CERT-{userId}" : certId;
            string sigBase64 = CryptoHelper.SignSm2(sm3Digest, userId: null);
            var result = new CaSignResult(
                Success: true,
                P7SignatureBase64: sigBase64,
                CertSn: certSn,
                CertIssuer: "CN=GHIS Dev SM2 CA, O=GHIS Health, C=CN",
                CaProvider: "DEV_SANDBOX_SM2"
            );
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            return Task.FromResult(new CaSignResult(
                Success: false,
                P7SignatureBase64: string.Empty,
                CertSn: string.Empty,
                CertIssuer: string.Empty,
                CaProvider: "DEV_SANDBOX_SM2",
                ErrorMessage: ex.Message
            ));
        }
    }

    public Task<CaVerifyResult> VerifyAsync(byte[] sm3Digest, string p7SignatureBase64, string certSn, CancellationToken ct = default)
    {
        try
        {
            bool isValid = CryptoHelper.VerifySm2(sm3Digest, p7SignatureBase64);
            var result = new CaVerifyResult(
                IsValid: isValid,
                CertSn: certSn,
                CertSubject: $"CN=Doctor, SN={certSn}",
                ErrorMessage: isValid ? null : "SM2 数字签名校验失败：数据可能已被篡改"
            );
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            return Task.FromResult(new CaVerifyResult(
                IsValid: false,
                CertSn: certSn,
                CertSubject: string.Empty,
                ErrorMessage: ex.Message
            ));
        }
    }
}
