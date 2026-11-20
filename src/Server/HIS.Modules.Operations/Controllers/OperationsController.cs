using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
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
public partial class OperationsController : ControllerBase
{
	public sealed record AuditLogPagedResultDto(IReadOnlyList<OpsAuditEvent> Items, int TotalCount, int Page, int PageSize)
	{
		public int Total => TotalCount;
	}

	private readonly IRepository<OpsAuditEvent> _auditRepo;
	private readonly IRepository<OpsOutboxMessage> _outboxRepo;
	private readonly AuditLogService _auditService;
	private readonly OutboxService _outboxService;
	private readonly IUserSession? _userSession;
	private readonly SecurityIntrusionDetectionService? _idpsService;
	private readonly KmsKeyLifecycleService? _kmsService;
	private readonly IDbConnectionFactory? _connFactory;

	public OperationsController(
		IRepository<OpsAuditEvent> auditRepo,
		IRepository<OpsOutboxMessage> outboxRepo,
		AuditLogService auditService,
		OutboxService outboxService,
		IUserSession? userSession = null,
		SecurityIntrusionDetectionService? idpsService = null,
		KmsKeyLifecycleService? kmsService = null,
		IDbConnectionFactory? connFactory = null)
	{
		_auditRepo = auditRepo;
		_outboxRepo = outboxRepo;
		_auditService = auditService;
		_outboxService = outboxService;
		_userSession = userSession;
		_idpsService = idpsService;
		_kmsService = kmsService;
		_connFactory = connFactory;
	}

	[RequireAdmin]
	[HttpGet("audit-logs")]
	public async Task<ApiResponse<AuditLogPagedResultDto>> GetAuditLogs(
		[FromQuery] string? eventType = null,
		[FromQuery] string? targetEntity = null,
		[FromQuery] string? userId = null,
		[FromQuery] string? operatorName = null,
		[FromQuery(Name = "operator")] string? op = null,
		[FromQuery] long? startTimeMs = null,
		[FromQuery] long? endTimeMs = null,
		[FromQuery] int page = 1,
		[FromQuery] int pageSize = 50,
		CancellationToken ct = default)
	{
		int safePage = Math.Max(1, page);
		int safeSize = Math.Clamp(pageSize, 1, 200);
		int offset = (safePage - 1) * safeSize;
		string? effectiveOp = string.IsNullOrWhiteSpace(operatorName) ? op : operatorName;

		var whereClause = @"
WHERE (@EventType IS NULL OR event_type = @EventType)
  AND (@TargetEntity IS NULL OR target_entity = @TargetEntity)
  AND (@UserId IS NULL OR user_id = @UserId)
  AND (@OperatorName IS NULL OR operator_name LIKE '%' || @OperatorName || '%')
  AND (@StartTimeMs IS NULL OR created_at_ms >= @StartTimeMs)
  AND (@EndTimeMs IS NULL OR created_at_ms <= @EndTimeMs)";

		var p = new
		{
			EventType = string.IsNullOrWhiteSpace(eventType) ? null : eventType.Trim(),
			TargetEntity = string.IsNullOrWhiteSpace(targetEntity) ? null : targetEntity.Trim(),
			UserId = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim(),
			OperatorName = string.IsNullOrWhiteSpace(effectiveOp) ? null : effectiveOp.Trim(),
			StartTimeMs = startTimeMs,
			EndTimeMs = endTimeMs,
			SafeSize = safeSize,
			Offset = offset
		};

		int totalCount = 0;
		if (_connFactory != null)
		{
			using var conn = _connFactory.CreateConnection();
			totalCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
				"SELECT count(*) FROM ops_audit_events " + whereClause, p, cancellationToken: ct));
		}
		else
		{
			totalCount = (await _auditRepo.QueryAsync("SELECT * FROM ops_audit_events " + whereClause, p, ct)).Count;
		}

		var pagedItems = (await _auditRepo.QueryAsync(@"
SELECT * FROM ops_audit_events " + whereClause + @"
ORDER BY created_at_ms DESC, rowid DESC
LIMIT @SafeSize OFFSET @Offset", p, ct)).ToList();

		return ApiResponse<AuditLogPagedResultDto>.Success(new AuditLogPagedResultDto(pagedItems, totalCount, safePage, safeSize));
	}

	[RequireAdmin]
	[HttpGet("audit-logs/export")]
	public async Task<IActionResult> ExportAuditLogs(
		[FromQuery] string? eventType = null,
		[FromQuery] string? targetEntity = null,
		[FromQuery] long? startTimeMs = null,
		[FromQuery] long? endTimeMs = null,
		[FromQuery] string? reason = null)
	{
		if (string.IsNullOrWhiteSpace(reason))
		{
			return BadRequest(ApiResponse<string>.Fail(400, "导出系统安全审计日志必须提供审批事由与业务凭证"));
		}

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
		sb.AppendLine($"# GHIS 等保三级审计日志导出 | 导出人: {_userSession?.DisplayName ?? "系统管理员"} ({_userSession?.UserId}) | 导出时间: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC | 审批事由: {reason.Trim()}");
		sb.AppendLine("Id,EventType,TargetEntity,TargetId,ActionDetail,UserId,OperatorName,DeptId,CreatedAtMs,CurrHash,PrevHash");
		foreach (var l in logs)
		{
			sb.AppendLine($"\"{l.Id}\",\"{l.EventType}\",\"{l.TargetEntity}\",\"{l.TargetId}\",\"{l.ActionDetail?.Replace("\"", "\"\"")}\",\"{l.UserId}\",\"{l.OperatorName}\",\"{l.DeptId}\",{l.CreatedAtMs},\"{l.CurrHash}\",\"{l.PrevHash}\"");
		}

		byte[] csvBytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
		string digest = CryptoHelper.ComputeSm3(csvBytes);

		await _auditService.RecordAuditAsync(
			"AUDIT_LOG_EXPORT",
			"ops_audit_events",
			"EXPORT",
			$"管理员 [{_userSession?.UserId}] 导出 {logs.Count} 条安全审计日志，事由: {reason}，文件防伪指纹: {digest}",
			_userSession?.UserId,
			_userSession?.DisplayName,
			_userSession?.ActiveDeptId);

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
	[HttpGet("audit-logs/verify")]
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

	// ==========================================
	// M-019 集中安全审计与入侵防范 (IDPS & Security Alerting)
	// ==========================================

	[RequireAdmin]
	[HttpGet("security/posture")]
	public async Task<ApiResponse<SecurityPostureDto>> GetSecurityPosture()
	{
		if (_idpsService == null)
		{
			return ApiResponse<SecurityPostureDto>.Fail(500, "入侵防范与安全态势服务未就绪");
		}
		var posture = await _idpsService.GetSecurityPostureAsync();
		return ApiResponse<SecurityPostureDto>.Success(posture);
	}

	[RequireAdmin]
	[HttpGet("security/alerts")]
	public async Task<ApiResponse<IReadOnlyList<OpsSecurityAlert>>> GetSecurityAlerts(
		[FromQuery] string? severity = null,
		[FromQuery] string? status = null,
		[FromQuery] int limit = 50)
	{
		if (_idpsService == null)
		{
			return ApiResponse<IReadOnlyList<OpsSecurityAlert>>.Fail(500, "入侵防范与安全态势服务未就绪");
		}
		var alerts = await _idpsService.GetAlertsAsync(severity, status, limit);
		return ApiResponse<IReadOnlyList<OpsSecurityAlert>>.Success(alerts);
	}

	[RequireAdmin]
	[HttpPost("security/alerts/scan")]
	public async Task<ApiResponse<int>> ScanSecurityThreats()
	{
		if (_idpsService == null)
		{
			return ApiResponse<int>.Fail(500, "入侵防范与安全态势服务未就绪");
		}
		int newAlerts = await _idpsService.ScanAndGenerateThreatAlertsAsync();
		return ApiResponse<int>.Success(newAlerts, $"威胁与异常检测引擎扫描完成，新识别 {newAlerts} 条可疑安全威胁");
	}

	[RequireAdmin]
	[HttpPost("security/alerts/{id}/resolve")]
	public async Task<ApiResponse<OpsSecurityAlert>> ResolveSecurityAlert(
		[FromRoute] string id,
		[FromBody] ResolveAlertRequestDto req)
	{
		if (_idpsService == null)
		{
			return ApiResponse<OpsSecurityAlert>.Fail(500, "入侵防范与安全态势服务未就绪");
		}
		if (string.IsNullOrWhiteSpace(req.ResolutionNote))
		{
			return ApiResponse<OpsSecurityAlert>.Fail(400, "处置安全威胁告警必须填写核实与防范处置说明");
		}
		var alert = await _idpsService.ResolveAlertAsync(id, _userSession?.UserId ?? "USER-ADMIN", req.ResolutionNote);
		return alert != null
			? ApiResponse<OpsSecurityAlert>.Success(alert, "安全威胁告警已完成闭环处置")
			: ApiResponse<OpsSecurityAlert>.Fail(404, "未找到指定的告警记录");
	}

	// ==========================================
	// M-021 KMS/HSM 密钥全生命周期管理 (Key Management Service)
	// ==========================================

	[RequireAdmin]
	[HttpGet("kms/keys")]
	public async Task<ApiResponse<IReadOnlyList<OpsKmsKey>>> GetKmsKeys()
	{
		if (_kmsService == null)
		{
			return ApiResponse<IReadOnlyList<OpsKmsKey>>.Fail(500, "KMS 密钥全生命周期管理服务未就绪");
		}
		var keys = await _kmsService.GetKeysAsync();
		return ApiResponse<IReadOnlyList<OpsKmsKey>>.Success(keys);
	}

	[RequireAdmin]
	[HttpPost("kms/keys/rotate")]
	public async Task<ApiResponse<OpsKmsKey>> RotateKmsKey([FromBody] KeyRotationRequestDto req)
	{
		if (_kmsService == null)
		{
			return ApiResponse<OpsKmsKey>.Fail(500, "KMS 密钥全生命周期管理服务未就绪");
		}
		if (string.IsNullOrWhiteSpace(req.KeyName))
		{
			return ApiResponse<OpsKmsKey>.Fail(400, "轮换密钥必须指定目标密钥标识名称 (KeyName)");
		}
		var newKey = await _kmsService.RotateKeyAsync(req.KeyName, _userSession?.UserId ?? "USER-ADMIN", req.Reason);
		return ApiResponse<OpsKmsKey>.Success(newKey, $"密码学主密钥 [{req.KeyName}] 平滑轮换成功，已生成 v{newKey.Version} 新密钥");
	}

	[RequireAdmin]
	[HttpGet("kms/hsm-status")]
	public async Task<ApiResponse<HsmStatusDto>> GetHsmStatus()
	{
		if (_kmsService == null)
		{
			return ApiResponse<HsmStatusDto>.Fail(500, "KMS 密钥全生命周期管理服务未就绪");
		}
		var status = await _kmsService.GetHsmStatusAsync();
		return ApiResponse<HsmStatusDto>.Success(status);
	}
}
