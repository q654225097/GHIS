using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.Platform.Models;

namespace HIS.Modules.Platform.Services;

public sealed class ClinicalProcessSignatureService : IClinicalProcessSignatureContract
{
    private readonly IRepository<SecDigitalSignature> _signatureRepo;
    private readonly ICaSignatureProvider _caProvider;
    private readonly IExternalTsaClient _tsaClient;

    public ClinicalProcessSignatureService(
        IRepository<SecDigitalSignature> signatureRepo,
        ICaSignatureProvider caProvider,
        IExternalTsaClient tsaClient)
    {
        _signatureRepo = signatureRepo;
        _caProvider = caProvider;
        _tsaClient = tsaClient;
    }

    public async Task<SecDigitalSignatureDto> SignClinicalRecordAsync(
        string entityType,
        string entityId,
        string stage,
        object canonicalPayload,
        IUserSession signer,
        CancellationToken ct = default)
    {
        // 1. 规范化 JSON 序列化 (按属性排序确保确定性摘要)
        string canonicalJson = SerializeCanonical(canonicalPayload);
        byte[] payloadBytes = Encoding.UTF8.GetBytes(canonicalJson);
        string sm3Hex = CryptoHelper.ComputeSm3(payloadBytes);
        byte[] sm3Digest = Convert.FromHexString(sm3Hex);

        // 2. 调用第三方/沙箱 CA 协同签名
        var caResult = await _caProvider.SignAsync(signer.UserId, signer.UserId, sm3Digest, ct);

        // 3. 调用 RFC 3161 可信时间戳客户端
        var tsaResult = await _tsaClient.RequestTimestampAsync(sm3Digest, ct);

        // 4. 持久化全过程数字签名与时间戳记录
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var entity = new SecDigitalSignature
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = entityType.ToUpperInvariant(),
            EntityId = entityId,
            BusinessStage = stage.ToUpperInvariant(),
            SignerUserId = signer.UserId,
            SignerName = string.IsNullOrWhiteSpace(signer.DisplayName) ? signer.UserName : signer.DisplayName,
            CaProvider = caResult.CaProvider,
            CaCertSn = caResult.CertSn,
            CaCertIssuer = caResult.CertIssuer,
            DataSm3Hash = sm3Hex,
            SignatureAlg = "SM2-SM3",
            P7SignatureBase64 = caResult.P7SignatureBase64,
            TsaProviderUrl = tsaResult.TsaUrl,
            TsaPolicyOid = tsaResult.PolicyOid,
            TsaTokenBase64 = tsaResult.TokenBase64,
            TsaGenTimeMs = tsaResult.GenTimeMs,
            VerifyStatus = (short)(caResult.Success && tsaResult.Success ? 1 : 0),
            CreatedAtMs = now,
            CreatedBy = signer.UserId,
            UpdatedAtMs = now,
            UpdatedBy = signer.UserId,
            VersionNo = 1
        };

        await _signatureRepo.InsertAsync(entity);

        return ToDto(entity);
    }

    public async Task<IReadOnlyList<SecDigitalSignatureDto>> GetSignaturesAsync(
        string entityType,
        string entityId,
        CancellationToken ct = default)
    {
        string upperType = entityType.ToUpperInvariant();
        var list = await _signatureRepo.QueryAsync(
            "SELECT * FROM sec_digital_signatures WHERE entity_type = @Type AND entity_id = @Id ORDER BY created_at_ms ASC",
            new { Type = upperType, Id = entityId });

        return list.Select(ToDto).ToList();
    }

    public async Task<SignatureVerificationReportDto> VerifySignatureAsync(
        string signatureId,
        CancellationToken ct = default)
    {
        var sig = await _signatureRepo.GetByIdAsync(signatureId);
        if (sig == null)
        {
            return new SignatureVerificationReportDto(
                SignatureId: signatureId,
                EntityType: string.Empty,
                EntityId: string.Empty,
                BusinessStage: string.Empty,
                OverallValid: false,
                DataIntegrityPassed: false,
                CaSignatureValid: false,
                TsaTokenValid: false,
                SignerName: null,
                CaCertSn: null,
                TsaGenTimeMs: 0,
                ErrorMessage: "签名记录不存在"
            );
        }

        byte[] sm3Digest = Convert.FromHexString(sig.DataSm3Hash);

        // 验证 CA 签名
        var caVerify = await _caProvider.VerifyAsync(sm3Digest, sig.P7SignatureBase64, sig.CaCertSn, ct);

        // 验证 TSA 时间戳令牌
        var tsaVerify = await _tsaClient.VerifyTimestampAsync(sig.TsaTokenBase64, sm3Digest, ct);

        bool overall = caVerify.IsValid && tsaVerify.IsValid;

        return new SignatureVerificationReportDto(
            SignatureId: sig.Id,
            EntityType: sig.EntityType,
            EntityId: sig.EntityId,
            BusinessStage: sig.BusinessStage,
            OverallValid: overall,
            DataIntegrityPassed: true,
            CaSignatureValid: caVerify.IsValid,
            TsaTokenValid: tsaVerify.IsValid,
            SignerName: sig.SignerName,
            CaCertSn: sig.CaCertSn,
            TsaGenTimeMs: sig.TsaGenTimeMs,
            ErrorMessage: overall ? null : $"{caVerify.ErrorMessage} {tsaVerify.ErrorMessage}".Trim()
        );
    }

    private static string SerializeCanonical(object payload)
    {
        if (payload is string str) return str;
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        return JsonSerializer.Serialize(payload, options);
    }

    private static SecDigitalSignatureDto ToDto(SecDigitalSignature e) =>
        new(
            Id: e.Id,
            EntityType: e.EntityType,
            EntityId: e.EntityId,
            BusinessStage: e.BusinessStage,
            SignerUserId: e.SignerUserId,
            SignerName: e.SignerName,
            CaProvider: e.CaProvider,
            CaCertSn: e.CaCertSn,
            CaCertIssuer: e.CaCertIssuer,
            DataSm3Hash: e.DataSm3Hash,
            SignatureAlg: e.SignatureAlg,
            P7SignatureBase64: e.P7SignatureBase64,
            TsaProviderUrl: e.TsaProviderUrl,
            TsaPolicyOid: e.TsaPolicyOid,
            TsaTokenBase64: e.TsaTokenBase64,
            TsaGenTimeMs: e.TsaGenTimeMs,
            VerifyStatus: e.VerifyStatus,
            CreatedAtMs: e.CreatedAtMs
        );
}
