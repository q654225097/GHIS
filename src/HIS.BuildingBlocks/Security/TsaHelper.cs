using System;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Cms;
using Org.BouncyCastle.Asn1.X509;

namespace HIS.BuildingBlocks.Security;

/// <summary>
/// 符合 RFC 3161 标准的可信时间戳服务 (TSA) 令牌构造与验证器。
/// 采用国密 SM3 算法摘要标识 (1.2.156.10197.1.401) 与国家可信时间戳策略 (1.2.156.10197.1.501)。
/// </summary>
public static class TsaHelper
{
    public const string NationalTsaPolicyOid = "1.2.156.10197.1.501";
    public const string Sm3DigestOid = "1.2.156.10197.1.401";

    /// <summary>
    /// 为给定的数据/摘要生成符合 RFC 3161 规范的标准 TimeStampToken (Base64 ASN.1 DER)。
    /// </summary>
    public static string GenerateTimeStampToken(byte[] digest, string? policyOid = null, long? timestampMs = null)
    {
        if (digest == null || digest.Length == 0)
        {
            throw new ArgumentException("摘要数据不能为空", nameof(digest));
        }

        long nowMs = timestampMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        DateTime utcDate = DateTimeOffset.FromUnixTimeMilliseconds(nowMs).UtcDateTime;
        string policy = policyOid ?? NationalTsaPolicyOid;

        // 构造 ASN.1 MessageImprint
        var algId = new AlgorithmIdentifier(new DerObjectIdentifier(Sm3DigestOid), DerNull.Instance);
        var messageImprint = new DerSequence(algId, new DerOctetString(digest));

        // 构造 TSTInfo (RFC 3161)
        // TSTInfo ::= SEQUENCE {
        //    version               INTEGER  { v1(1) },
        //    policy                TSAPolicyId,
        //    messageImprint        MessageImprint,
        //    serialNumber          INTEGER,
        //    genTime               GeneralizedTime,
        //    accuracy              Accuracy                 OPTIONAL,
        //    ordering              BOOLEAN                  DEFAULT FALSE,
        //    nonce                 INTEGER                  OPTIONAL,
        //    tsa                   [0] GeneralName          OPTIONAL,
        //    extensions            [1] IMPLICIT Extensions  OPTIONAL }
        var serial = DerInteger.ValueOf(nowMs);
        var genTime = new DerGeneralizedTime(utcDate);
        var tstInfoSeq = new DerSequence(
            DerInteger.ValueOf(1), // v1
            new DerObjectIdentifier(policy),
            messageImprint,
            serial,
            genTime
        );

        byte[] tstInfoDer = tstInfoSeq.GetDerEncoded();

        // 对 TSTInfo 进行国密签名封装
        string signature = CryptoHelper.SignSm2(tstInfoDer);
        byte[] sigBytes = Convert.FromBase64String(signature);

        // 封装为 ContentInfo
        // id-ct-TSTInfo: 1.2.840.113549.1.9.16.1.4
        var contentInfo = new DerSequence(
            new DerObjectIdentifier("1.2.840.113549.1.9.16.1.4"),
            new DerTaggedObject(true, 0, new DerSequence(
                tstInfoSeq,
                new DerOctetString(sigBytes)
            ))
        );

        return Convert.ToBase64String(contentInfo.GetDerEncoded());
    }

    /// <summary>
    /// 校验 RFC 3161 时间戳令牌的完整性与防抵赖摘要
    /// </summary>
    public static (bool IsValid, DateTime GenTimeUtc, string PolicyOid, string? ErrorMessage) VerifyTimeStampToken(string tokenBase64, byte[] expectedDigest)
    {
        try
        {
            byte[] derBytes = Convert.FromBase64String(tokenBase64);
            using var asn1 = new Asn1InputStream(derBytes);
            var rootSeq = (Asn1Sequence)asn1.ReadObject();
            if (rootSeq.Count < 2)
            {
                return (false, DateTime.MinValue, string.Empty, "RFC 3161 令牌格式错误：缺少内容节点");
            }

            var tagged = (Asn1TaggedObject)rootSeq[1];
            var innerSeq = (Asn1Sequence)tagged.GetExplicitBaseObject();
            var tstInfo = (Asn1Sequence)innerSeq[0];

            string policyOid = ((DerObjectIdentifier)tstInfo[1]).Id;
            var msgImprint = (Asn1Sequence)tstInfo[2];
            byte[] recordedDigest = ((DerOctetString)msgImprint[1]).GetOctets();
            var genTime = Asn1GeneralizedTime.GetInstance(tstInfo[4]).ToDateTime();

            if (!CryptographicOperations.FixedTimeEquals(recordedDigest, expectedDigest))
            {
                return (false, genTime, policyOid, "时间戳绑定的哈希与原始数据摘要不一致");
            }

            if (innerSeq.Count < 2)
            {
                return (false, DateTime.MinValue, string.Empty, "RFC 3161 令牌缺少 SM2 签名节点");
            }
            byte[] sigBytes = ((DerOctetString)innerSeq[1]).GetOctets();
            byte[] tstInfoBytes = tstInfo.GetDerEncoded();
            if (!CryptoHelper.VerifySm2(tstInfoBytes, Convert.ToBase64String(sigBytes)))
            {
                return (false, DateTime.MinValue, string.Empty, "RFC 3161 时间戳 SM2 数字签名验证失败，Token 疑似伪造");
            }

            return (true, genTime, policyOid, null);
        }
        catch (Exception ex)
        {
            return (false, DateTime.MinValue, string.Empty, $"时间戳令牌解析失败: {ex.Message}");
        }
    }
}
