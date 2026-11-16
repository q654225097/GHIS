using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.Operations.Models;
using HIS.Modules.Operations.Services;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Operations.Controllers;

[ApiController]
[Route("api/v4/operations")]
public class OperationsController : ControllerBase
{
	public sealed record AuditLogPagedResultDto(IReadOnlyList<OpsAuditEvent> Items, int TotalCount, int Page, int PageSize);

	private readonly IRepository<OpsAuditEvent> _auditRepo;
	private readonly IRepository<OpsOutboxMessage> _outboxRepo;
	private readonly AuditLogService _auditService;
	private readonly OutboxService _outboxService;
	private readonly IUserSession? _userSession;

	public OperationsController(
		IRepository<OpsAuditEvent> auditRepo,
		IRepository<OpsOutboxMessage> outboxRepo,
		AuditLogService auditService,
		OutboxService outboxService,
		IUserSession? userSession = null)
	{
		_auditRepo = auditRepo;
		_outboxRepo = outboxRepo;
		_auditService = auditService;
		_outboxService = outboxService;
		_userSession = userSession;
	}

	[RequireAdmin]
	[HttpGet("audit-logs")]
	public async Task<ApiResponse<AuditLogPagedResultDto>> GetAuditLogs(
		[FromQuery] string? eventType = null,
		[FromQuery] string? targetEntity = null,
		[FromQuery] string? userId = null,
		[FromQuery] string? operatorName = null,
		[FromQuery] long? startTimeMs = null,
		[FromQuery] long? endTimeMs = null,
		[FromQuery] int page = 1,
		[FromQuery] int pageSize = 50)
	{
		int safePage = Math.Max(1, page);
		int safeSize = Math.Clamp(pageSize, 1, 200);
		int offset = (safePage - 1) * safeSize;

		var all = await _auditRepo.QueryAsync(@"
SELECT * FROM ops_audit_events
WHERE (@EventType IS NULL OR event_type = @EventType)
  AND (@TargetEntity IS NULL OR target_entity = @TargetEntity)
  AND (@UserId IS NULL OR user_id = @UserId)
  AND (@OperatorName IS NULL OR operator_name LIKE '%' || @OperatorName || '%')
  AND (@StartTimeMs IS NULL OR created_at_ms >= @StartTimeMs)
  AND (@EndTimeMs IS NULL OR created_at_ms <= @EndTimeMs)
ORDER BY created_at_ms DESC, rowid DESC", new
		{
			EventType = string.IsNullOrWhiteSpace(eventType) ? null : eventType.Trim(),
			TargetEntity = string.IsNullOrWhiteSpace(targetEntity) ? null : targetEntity.Trim(),
			UserId = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim(),
			OperatorName = string.IsNullOrWhiteSpace(operatorName) ? null : operatorName.Trim(),
			StartTimeMs = startTimeMs,
			EndTimeMs = endTimeMs
		});

		var pagedItems = all.Skip(offset).Take(safeSize).ToList();
		return ApiResponse<AuditLogPagedResultDto>.Success(new AuditLogPagedResultDto(pagedItems, all.Count, safePage, safeSize));
	}

	[RequireAdmin]
	[HttpGet("audit-logs/export")]
	public async Task<IActionResult> ExportAuditLogs(
		[FromQuery] string? eventType = null,
		[FromQuery] string? targetEntity = null,
		[FromQuery] long? startTimeMs = null,
		[FromQuery] long? endTimeMs = null)
	{
		var logs = await _auditRepo.QueryAsync(@"
SELECT * FROM ops_audit_events
WHERE (@EventType IS NULL OR event_type = @EventType)
  AND (@TargetEntity IS NULL OR target_entity = @TargetEntity)
  AND (@StartTimeMs IS NULL OR created_at_ms >= @StartTimeMs)
  AND (@EndTimeMs IS NULL OR created_at_ms <= @EndTimeMs)
ORDER BY created_at_ms DESC, rowid DESC
LIMIT 1000", new
		{
			EventType = string.IsNullOrWhiteSpace(eventType) ? null : eventType.Trim(),
			TargetEntity = string.IsNullOrWhiteSpace(targetEntity) ? null : targetEntity.Trim(),
			StartTimeMs = startTimeMs,
			EndTimeMs = endTimeMs
		});

		var sb = new StringBuilder();
		sb.AppendLine("Id,EventType,TargetEntity,TargetId,ActionDetail,UserId,OperatorName,DeptId,CreatedAtMs,CurrHash,PrevHash");
		foreach (var l in logs)
		{
			sb.AppendLine($"\"{l.Id}\",\"{l.EventType}\",\"{l.TargetEntity}\",\"{l.TargetId}\",\"{l.ActionDetail?.Replace("\"", "\"\"")}\",\"{l.UserId}\",\"{l.OperatorName}\",\"{l.DeptId}\",{l.CreatedAtMs},\"{l.CurrHash}\",\"{l.PrevHash}\"");
		}

		byte[] csvBytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
		return File(csvBytes, "text/csv; charset=utf-8", $"audit_logs_{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
	}

	[RequireAdmin]
	[HttpPost("audit-logs/anchor")]
	public async Task<ApiResponse<OpsAuditAnchor>> CreateAuditAnchor([FromQuery] string checkpointType = "MANUAL")
	{
		var anchor = await _auditService.RecordAnchorAsync(checkpointType, _userSession?.UserId);
		return anchor != null
			? ApiResponse<OpsAuditAnchor>.Success(anchor, $"成功固化安全锚点，当前已锚定区块高度: {anchor.TotalEventsCount}")
			: ApiResponse<OpsAuditAnchor>.Fail(400, "当前暂无可固化的审计区块");
	}

	[RequireAdmin]
	[HttpGet("audit-logs/verify-chain")]
	public async Task<ApiResponse<AuditChainVerificationResult>> VerifyAuditChain()
	{
		AuditChainVerificationResult result = await _auditService.VerifyChainIntegrityAsync();
		return result.IsIntact ? ApiResponse<AuditChainVerificationResult>.Success(result, result.Message) : ApiResponse<AuditChainVerificationResult>.Fail(409, result.Message, result);
	}

	[RequireAdmin]
	[HttpGet("outbox")]
	public async Task<ApiResponse<IReadOnlyList<OpsOutboxMessage>>> GetOutboxMessages([FromQuery] int? status = null, [FromQuery] int limit = 50)
	{
		int safeLimit = Math.Clamp(limit, 1, 200);
		return ApiResponse<IReadOnlyList<OpsOutboxMessage>>.Success(await _outboxRepo.QueryAsync("SELECT * FROM ops_outbox_messages\nWHERE (@Status IS NULL OR status = @Status)\nORDER BY created_at_ms DESC\nLIMIT @Limit", new
		{
			Status = status,
			Limit = safeLimit
		}));
	}

	[RequireAdmin]
	[HttpPost("outbox/dispatch")]
	public async Task<ApiResponse<int>> DispatchOutbox([FromQuery] int batchSize = 50)
	{
		int dispatched = await _outboxService.DispatchPendingMessagesAsync(batchSize);
		return ApiResponse<int>.Success(dispatched, $"已成功投递 {dispatched} 条可靠消息队列事件");
	}
}
