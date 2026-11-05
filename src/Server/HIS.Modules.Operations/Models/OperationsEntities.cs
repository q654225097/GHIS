using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Operations.Models;

public class OpsAuditEvent : BaseEntity
{
    public string EventType { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? OperatorName { get; set; }
    public string? DeptId { get; set; }
    public string TargetEntity { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string? ActionDetail { get; set; }
    public string PrevHash { get; set; } = "GENESIS_BLOCK_HASH_00000000000000000000000000000000000000000000";
    public string CurrHash { get; set; } = string.Empty;
}

public class OpsIdempotencyKey : BaseEntity
{
    public string RequestKey { get; set; } = string.Empty;
    public string RequestSha256 { get; set; } = string.Empty;
    public string? ResponseJson { get; set; }
    public long ExpiresAtMs { get; set; }
}

public class OpsOutboxMessage : BaseEntity
{
    public string EventType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public int Status { get; set; } = 0; // 0=Pending, 1=Published, 2=Failed
    public int RetryCount { get; set; } = 0;
    public string? LastError { get; set; }
}

