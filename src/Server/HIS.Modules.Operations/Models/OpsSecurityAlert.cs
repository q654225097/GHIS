using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Operations.Models;

public class OpsSecurityAlert : BaseEntity
{
    public string AlertNo { get; set; } = string.Empty;
    public string AlertType { get; set; } = string.Empty;
    public string Severity { get; set; } = "HIGH"; // CRITICAL, HIGH, WARNING, INFO
    public string? SourceIp { get; set; }
    public string? UserId { get; set; }
    public string? OperatorName { get; set; }
    public string? TargetResource { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? ThreatIndicatorsJson { get; set; }
    public string Status { get; set; } = "ACTIVE"; // ACTIVE, INVESTIGATING, RESOLVED
    public string? ResolvedBy { get; set; }
    public long? ResolvedAtMs { get; set; }
    public string? ResolutionNote { get; set; }
}
