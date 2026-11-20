using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Platform.Models;

public class SecDigitalSignature : BaseEntity
{
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string BusinessStage { get; set; } = string.Empty;
    public string SignerUserId { get; set; } = string.Empty;
    public string SignerName { get; set; } = string.Empty;
    public string CaProvider { get; set; } = string.Empty;
    public string CaCertSn { get; set; } = string.Empty;
    public string CaCertIssuer { get; set; } = string.Empty;
    public string DataSm3Hash { get; set; } = string.Empty;
    public string SignatureAlg { get; set; } = "SM2-SM3";
    public string P7SignatureBase64 { get; set; } = string.Empty;
    public string TsaProviderUrl { get; set; } = string.Empty;
    public string TsaPolicyOid { get; set; } = string.Empty;
    public string TsaTokenBase64 { get; set; } = string.Empty;
    public long TsaGenTimeMs { get; set; }
    public short VerifyStatus { get; set; } = 1;
}
