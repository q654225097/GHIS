using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Modules.Operations.Models;

namespace HIS.Modules.Operations.Services;

public class AuditLogService
{
    private readonly IRepository<OpsAuditEvent> _auditRepo;

    public AuditLogService(IRepository<OpsAuditEvent> auditRepo)
    {
        _auditRepo = auditRepo;
    }

    public async Task<OpsAuditEvent> AppendAuditLogAsync(
        string eventType,
        string targetEntity,
        string targetId,
        string? actionDetail = null,
        string? userId = null,
        string? operatorName = null,
        string? deptId = null,
        CancellationToken ct = default)
    {
        var latestEvents = await _auditRepo.QueryAsync(
            "SELECT * FROM ops_audit_events ORDER BY created_at_ms DESC LIMIT 1", null, ct);
        var prevHash = latestEvents.FirstOrDefault()?.CurrHash ?? "GENESIS_BLOCK_HASH_00000000000000000000000000000000000000000000";

        var payload = $"{prevHash}|{eventType}|{targetEntity}|{targetId}|{actionDetail}|{userId}|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var currHash = CryptoHelper.ComputeSha256(payload);

        var audit = new OpsAuditEvent
        {
            EventType = eventType,
            TargetEntity = targetEntity,
            TargetId = targetId,
            ActionDetail = actionDetail,
            UserId = userId,
            OperatorName = operatorName,
            DeptId = deptId,
            PrevHash = prevHash,
            CurrHash = currHash
        };

        await _auditRepo.InsertAsync(audit, ct);
        return audit;
    }
}
