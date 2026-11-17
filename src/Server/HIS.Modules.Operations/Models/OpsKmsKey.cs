using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Operations.Models;

public class OpsKmsKey : BaseEntity
{
    public string KeyName { get; set; } = string.Empty;
    public string Algorithm { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public string State { get; set; } = "ACTIVE"; // ACTIVE, ROTATING, RETIRED, DESTROYED
    public short HsmProtected { get; set; } = 1;
    public string KeyFingerprint { get; set; } = string.Empty;
    public long ExpiresAtMs { get; set; }
    public long? RotatedAtMs { get; set; }
    public string? Description { get; set; }
}
