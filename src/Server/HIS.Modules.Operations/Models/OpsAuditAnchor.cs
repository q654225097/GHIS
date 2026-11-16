using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Operations.Models;

public class OpsAuditAnchor : BaseEntity
{
    public string CheckpointType { get; set; } = "CHECKPOINT";
    public string LastEventId { get; set; } = string.Empty;
    public string LastEventHash { get; set; } = string.Empty;
    public int TotalEventsCount { get; set; }
}
