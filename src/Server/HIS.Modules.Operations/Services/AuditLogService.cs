using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.Operations.Models;

namespace HIS.Modules.Operations.Services;

public class AuditLogService : IAuditTrailContract
{
	public const string GenesisBlockHash = "GENESIS_BLOCK_HASH_00000000000000000000000000000000000000000000";

	private static readonly SemaphoreSlim HashChainLock = new SemaphoreSlim(1, 1);

	private readonly IRepository<OpsAuditEvent> _auditRepo;
	private readonly IRepository<OpsAuditAnchor>? _anchorRepo;

	public AuditLogService(IRepository<OpsAuditEvent> auditRepo, IRepository<OpsAuditAnchor>? anchorRepo = null)
	{
		_auditRepo = auditRepo;
		_anchorRepo = anchorRepo;
	}

	public async Task RecordAuditAsync(string eventType, string targetEntity, string targetId, string? actionDetail = null, string? userId = null, string? operatorName = null, string? deptId = null, CancellationToken ct = default(CancellationToken))
	{
		await AppendAuditLogAsync(eventType, targetEntity, targetId, actionDetail, userId, operatorName, deptId, ct);
	}

	public async Task<OpsAuditEvent> AppendAuditLogAsync(string eventType, string targetEntity, string targetId, string? actionDetail = null, string? userId = null, string? operatorName = null, string? deptId = null, CancellationToken ct = default(CancellationToken))
	{
		await HashChainLock.WaitAsync(ct);
		try
		{
			string prevHash = (await _auditRepo.QueryAsync("SELECT * FROM ops_audit_events ORDER BY created_at_ms DESC, rowid DESC LIMIT 1", null, ct)).FirstOrDefault()?.CurrHash ?? GenesisBlockHash;
			long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			string payload = ComputeAuditPayload(prevHash, eventType, targetEntity, targetId, actionDetail, userId, operatorName, deptId, nowMs);
			string currHash = CryptoHelper.ComputeSha256(payload);
			OpsAuditEvent audit = new OpsAuditEvent
			{
				EventType = eventType,
				TargetEntity = targetEntity,
				TargetId = targetId,
				ActionDetail = actionDetail,
				UserId = userId,
				OperatorName = operatorName,
				DeptId = deptId,
				PrevHash = prevHash,
				CurrHash = currHash,
				CreatedAtMs = nowMs,
				UpdatedAtMs = nowMs
			};
			await _auditRepo.InsertAsync(audit, ct);
			return audit;
		}
		finally
		{
			HashChainLock.Release();
		}
	}

	/// <summary>
	/// 固化安全锚点：将当前审计链的最新区块哈希和记录总数写入锚点表，防范尾部截断（Truncation Attack）
	/// </summary>
	public async Task<OpsAuditAnchor?> RecordAnchorAsync(string checkpointType = "CHECKPOINT", string? operatorId = null, CancellationToken ct = default(CancellationToken))
	{
		if (_anchorRepo == null) return null;

		await HashChainLock.WaitAsync(ct);
		try
		{
			var latestEvent = (await _auditRepo.QueryAsync("SELECT * FROM ops_audit_events ORDER BY created_at_ms DESC, rowid DESC LIMIT 1", null, ct)).FirstOrDefault();
			if (latestEvent == null) return null;

			var allEvents = await _auditRepo.QueryAsync("SELECT id FROM ops_audit_events", null, ct);
			long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

			var anchor = new OpsAuditAnchor
			{
				CheckpointType = checkpointType,
				LastEventId = latestEvent.Id,
				LastEventHash = latestEvent.CurrHash,
				TotalEventsCount = allEvents.Count,
				CreatedAtMs = nowMs,
				CreatedBy = operatorId
			};

			await _anchorRepo.InsertAsync(anchor, ct);
			return anchor;
		}
		finally
		{
			HashChainLock.Release();
		}
	}

	public async Task<AuditChainVerificationResult> VerifyChainIntegrityAsync(CancellationToken ct = default(CancellationToken))
	{
		IReadOnlyList<OpsAuditEvent> events = await _auditRepo.QueryAsync("SELECT * FROM ops_audit_events ORDER BY created_at_ms ASC, rowid ASC", null, ct);
		string expectedPrevHash = GenesisBlockHash;
		foreach (OpsAuditEvent ev in events)
		{
			if (!string.Equals(ev.PrevHash, expectedPrevHash, StringComparison.OrdinalIgnoreCase))
			{
				return new AuditChainVerificationResult(IsIntact: false, events.Count, ev.Id, $"哈希链断裂：事件 [{ev.Id}] 的前序哈希 ({ev.PrevHash}) 与期望值 ({expectedPrevHash}) 不一致");
			}

			// 支持包含 operatorName 和 deptId 的最新哈希规范，并向后兼容旧版无此字段的哈希
			string payloadFull = ComputeAuditPayload(ev.PrevHash, ev.EventType, ev.TargetEntity, ev.TargetId, ev.ActionDetail, ev.UserId, ev.OperatorName, ev.DeptId, ev.CreatedAtMs);
			string recomputedFullHash = CryptoHelper.ComputeSha256(payloadFull);

			string payloadLegacy = ComputeAuditPayloadLegacy(ev.PrevHash, ev.EventType, ev.TargetEntity, ev.TargetId, ev.ActionDetail, ev.UserId, ev.CreatedAtMs);
			string recomputedLegacyHash = CryptoHelper.ComputeSha256(payloadLegacy);

			bool matches = string.Equals(ev.CurrHash, recomputedFullHash, StringComparison.OrdinalIgnoreCase) ||
						   string.Equals(ev.CurrHash, recomputedLegacyHash, StringComparison.OrdinalIgnoreCase);

			if (!matches)
			{
				return new AuditChainVerificationResult(IsIntact: false, events.Count, ev.Id, $"防篡改校验失败：事件 [{ev.Id}] 内容或操作人被篡改，计算哈希 ({recomputedFullHash}) 与存储哈希 ({ev.CurrHash}) 不符");
			}
			expectedPrevHash = ev.CurrHash;
		}

		// 尾部截断（Truncation Attack）核验：检查安全锚点
		if (_anchorRepo != null)
		{
			var latestAnchor = (await _anchorRepo.QueryAsync("SELECT * FROM ops_audit_anchors ORDER BY created_at_ms DESC, rowid DESC LIMIT 1", null, ct)).FirstOrDefault();
			if (latestAnchor != null)
			{
				if (events.Count < latestAnchor.TotalEventsCount)
				{
					return new AuditChainVerificationResult(
						IsIntact: false,
						events.Count,
						null,
						$"检测到审计链尾部截断篡改：数据库最新记录数 ({events.Count}) 少于已固化的安全锚点记录数 ({latestAnchor.TotalEventsCount})，末尾记录已被恶意删除！");
				}

				bool anchorFound = events.Any(e => e.Id == latestAnchor.LastEventId && string.Equals(e.CurrHash, latestAnchor.LastEventHash, StringComparison.OrdinalIgnoreCase));
				if (!anchorFound)
				{
					return new AuditChainVerificationResult(
						IsIntact: false,
						events.Count,
						null,
						$"检测到审计链尾部截断或篡改：安全锚点事件 [{latestAnchor.LastEventId}] 的哈希不存在于当前审计链中！");
				}
			}
		}

		return new AuditChainVerificationResult(IsIntact: true, events.Count, null, $"区块链式防篡改审计日志校验通过，共核验 {events.Count} 个区块节点");
	}

	public static string ComputeAuditPayload(string prevHash, string eventType, string targetEntity, string targetId, string? actionDetail, string? userId, string? operatorName, string? deptId, long timestampMs)
	{
		return $"{prevHash}|{eventType}|{targetEntity}|{targetId}|{actionDetail}|{userId}|{operatorName}|{deptId}|{timestampMs}";
	}

	public static string ComputeAuditPayloadLegacy(string prevHash, string eventType, string targetEntity, string targetId, string? actionDetail, string? userId, long timestampMs)
	{
		return $"{prevHash}|{eventType}|{targetEntity}|{targetId}|{actionDetail}|{userId}|{timestampMs}";
	}
}
