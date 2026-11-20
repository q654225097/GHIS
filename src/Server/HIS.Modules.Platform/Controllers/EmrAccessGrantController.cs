using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.Platform.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Platform.Controllers;

[ApiController]
[Route("api/v4/security")]
public class EmrAccessGrantController : ControllerBase
{
    private readonly IRepository<SecEmrAccessGrant> _grantRepo;
    private readonly IRepository<SecEmrReadAuditLog> _auditRepo;
    private readonly IRepository<SysUser> _userRepo;
    private readonly JwtTokenService _jwtService;
    private readonly IWorkflowEngineContract? _workflowEngine;

    public record CreateGrantDto(
        string PatientId,
        string? EncounterId,
        string GranteeUserId,
        string? GranteeOrgCode,
        bool IsExternalGrant,
        IReadOnlyList<string> AllowedScopes,
        long ValidFromMs,
        long ValidToMs,
        string GrantReason,
        string? PatientConsentId,
        string ApprovedBy
    );

    public record ExternalTokenExchangeDto(
        string ExternalDoctorId,
        string ExternalDoctorName,
        string ExternalOrgCode,
        string ExternalOrgName,
        string GrantId
    );

    public record AuditChainVerificationDto(
        bool ChainIntact,
        int TotalChecked,
        string? BrokenLogId,
        string? Message
    );

    public EmrAccessGrantController(
        IRepository<SecEmrAccessGrant> grantRepo,
        IRepository<SecEmrReadAuditLog> auditRepo,
        IRepository<SysUser> userRepo,
        JwtTokenService jwtService,
        IWorkflowEngineContract? workflowEngine = null)
    {
        _grantRepo = grantRepo;
        _auditRepo = auditRepo;
        _userRepo = userRepo;
        _jwtService = jwtService;
        _workflowEngine = workflowEngine;
    }

    [HttpPost("access-grants")]
    public async Task<ActionResult<ApiResponse<SecEmrAccessGrantDto>>> CreateGrant(
        [FromBody] CreateGrantDto req,
        CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string grantId = Guid.NewGuid().ToString();
        string approvalStatus = "APPROVED";
        string? wfInstanceId = null;
        short status = 1;

        if (_workflowEngine != null)
        {
            var boVars = new Dictionary<string, object?>
            {
                ["patientId"] = req.PatientId,
                ["granteeUserId"] = req.GranteeUserId,
                ["isExternalGrant"] = req.IsExternalGrant,
                ["grantReason"] = req.GrantReason
            };
            var wfRes = await _workflowEngine.StartWorkflowAsync("EMR_ACCESS_GRANT", grantId, req.ApprovedBy, null, boVars, ct);
            if (wfRes.IsWorkflowTriggered && wfRes.InstanceId != null)
            {
                wfInstanceId = wfRes.InstanceId;
                approvalStatus = wfRes.Status == "APPROVED" ? "APPROVED" : "PENDING_APPROVAL";
                status = (short)(wfRes.Status == "APPROVED" ? 1 : 0);
            }
        }

        var entity = new SecEmrAccessGrant
        {
            Id = grantId,
            PatientId = req.PatientId,
            EncounterId = req.EncounterId,
            GranteeUserId = req.GranteeUserId,
            GranteeOrgCode = req.GranteeOrgCode,
            IsExternalGrant = (short)(req.IsExternalGrant ? 1 : 0),
            AllowedScopesJson = JsonSerializer.Serialize(req.AllowedScopes),
            ValidFromMs = req.ValidFromMs <= 0 ? now : req.ValidFromMs,
            ValidToMs = req.ValidToMs <= 0 ? now + 86400000 : req.ValidToMs,
            GrantReason = req.GrantReason,
            PatientConsentId = req.PatientConsentId,
            ApprovedBy = req.ApprovedBy,
            ApprovalStatus = approvalStatus,
            WorkflowInstanceId = wfInstanceId,
            SubmittedAtMs = now,
            Status = status,
            CreatedAtMs = now,
            CreatedBy = req.ApprovedBy,
            UpdatedAtMs = now,
            UpdatedBy = req.ApprovedBy,
            VersionNo = 1
        };

        await _grantRepo.InsertAsync(entity);

        var dto = new SecEmrAccessGrantDto(
            entity.Id, entity.PatientId, entity.EncounterId, entity.GranteeUserId,
            entity.GranteeOrgCode, entity.IsExternalGrant == 1, req.AllowedScopes,
            entity.ValidFromMs, entity.ValidToMs, entity.GrantReason,
            entity.PatientConsentId, entity.ApprovedBy, entity.Status);

        return Ok(ApiResponse<SecEmrAccessGrantDto>.Success(dto, "病历访问授权创建成功"));
    }

    [HttpPost("access-grants/{id}/revoke")]
    public async Task<ActionResult<ApiResponse<bool>>> RevokeGrant(string id, CancellationToken ct)
    {
        var grant = await _grantRepo.GetByIdAsync(id);
        if (grant == null)
        {
            return NotFound(ApiResponse<bool>.Fail(404, "授权记录不存在"));
        }

        grant.Status = 0; // REVOKED
        grant.UpdatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _grantRepo.UpdateAsync(grant);

        return Ok(ApiResponse<bool>.Success(true, "病历访问授权已立即回收"));
    }

    [HttpGet("access-grants")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SecEmrAccessGrantDto>>>> GetGrants(
        [FromQuery] string? patientId,
        [FromQuery] string? granteeUserId,
        CancellationToken ct)
    {
        string sql = "WHERE 1=1";
        if (!string.IsNullOrEmpty(patientId)) sql += " AND patient_id = @PatId";
        if (!string.IsNullOrEmpty(granteeUserId)) sql += " AND grantee_user_id = @UserId";
        sql += " ORDER BY created_at_ms DESC LIMIT 100";

        var list = await _grantRepo.QueryAsync(sql, new { PatId = patientId, UserId = granteeUserId });
        var dtos = list.Select(g => new SecEmrAccessGrantDto(
            g.Id, g.PatientId, g.EncounterId, g.GranteeUserId, g.GranteeOrgCode,
            g.IsExternalGrant == 1,
            string.IsNullOrWhiteSpace(g.AllowedScopesJson) ? new List<string>() : JsonSerializer.Deserialize<List<string>>(g.AllowedScopesJson) ?? new List<string>(),
            g.ValidFromMs, g.ValidToMs, g.GrantReason, g.PatientConsentId, g.ApprovedBy, g.Status
        )).ToList();

        return Ok(ApiResponse<IReadOnlyList<SecEmrAccessGrantDto>>.Success(dtos, "获取授权列表成功"));
    }

    [HttpGet("read-audits")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SecEmrReadAuditLogDto>>>> GetReadAudits(
        [FromQuery] string? patientId,
        [FromQuery] string? accessUserId,
        CancellationToken ct)
    {
        string sql = "WHERE 1=1";
        if (!string.IsNullOrEmpty(patientId)) sql += " AND patient_id = @PatId";
        if (!string.IsNullOrEmpty(accessUserId)) sql += " AND access_user_id = @UserId";
        sql += " ORDER BY created_at_ms DESC LIMIT 100";

        var list = await _auditRepo.QueryAsync(sql, new { PatId = patientId, UserId = accessUserId });
        var dtos = list.Select(a => new SecEmrReadAuditLogDto(
            a.Id, a.AccessUserId, a.AccessUserName, a.AccessOrgCode, a.IsExternal == 1,
            a.PatientId, a.EncounterId, a.ResourceType, a.ResourceId, a.ActionType,
            a.Decision, a.DenyReason, a.PrevHash, a.CurrHash, a.CreatedAtMs
        )).ToList();

        return Ok(ApiResponse<IReadOnlyList<SecEmrReadAuditLogDto>>.Success(dtos, "获取病历查阅审计成功"));
    }

    [HttpGet("read-audits/verify-chain")]
    public async Task<ActionResult<ApiResponse<AuditChainVerificationDto>>> VerifyAuditChain(CancellationToken ct)
    {
        var logs = await _auditRepo.QueryAsync("SELECT * FROM sec_emr_read_audit_logs ORDER BY created_at_ms ASC LIMIT 500");
        if (logs.Count == 0)
        {
            return Ok(ApiResponse<AuditChainVerificationDto>.Success(
                new AuditChainVerificationDto(true, 0, null, "审计链为空，状态正常")));
        }

        string expectedPrev = "GENESIS_EMR_READ_CHAIN_2026";
        for (int i = 0; i < logs.Count; i++)
        {
            var log = logs[i];
            if (i > 0 && log.PrevHash != expectedPrev)
            {
                return Ok(ApiResponse<AuditChainVerificationDto>.Success(
                    new AuditChainVerificationDto(false, i, log.Id, $"第 {i} 条记录 PrevHash 校验失败，可能被篡改或断链")));
            }

            string expectedData = $"{log.PrevHash}|{log.AccessUserId}|{log.PatientId}|{log.ResourceType}|{log.ResourceId}|{log.ActionType}|{log.Decision}|{log.CreatedAtMs}";
            string calculatedHash = CryptoHelper.ComputeSm3(expectedData);
            if (!string.Equals(calculatedHash, log.CurrHash, StringComparison.OrdinalIgnoreCase))
            {
                return Ok(ApiResponse<AuditChainVerificationDto>.Success(
                    new AuditChainVerificationDto(false, i, log.Id, $"第 {i} 条记录 CurrHash 不匹配，记录内容已被篡改")));
            }

            expectedPrev = log.CurrHash;
        }

        return Ok(ApiResponse<AuditChainVerificationDto>.Success(
            new AuditChainVerificationDto(true, logs.Count, null, $"全部 {logs.Count} 条阅读审计记录 SM3 哈希链防篡改验真通过")));
    }

    [HttpPost("external-auth/exchange-token")]
    public async Task<ActionResult<ApiResponse<object>>> ExchangeExternalToken(
        [FromBody] ExternalTokenExchangeDto req,
        CancellationToken ct)
    {
        var grant = await _grantRepo.GetByIdAsync(req.GrantId);
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (grant == null || grant.Status != 1 || grant.ValidFromMs > now || grant.ValidToMs < now || (!string.IsNullOrWhiteSpace(grant.ApprovalStatus) && !string.Equals(grant.ApprovalStatus, "APPROVED", StringComparison.OrdinalIgnoreCase)))
        {
            return BadRequest(ApiResponse<object>.Fail(400, "外院授权凭证无效、尚未审批通过或已过期"));
        }

        // 生成短期受限 JWT
        string token = _jwtService.GenerateAccessToken(
            userId: req.ExternalDoctorId,
            userName: req.ExternalDoctorName,
            displayName: $"{req.ExternalDoctorName}(外院:{req.ExternalOrgName})",
            campusId: "CAMPUS-01",
            activeDeptCategory: DepartmentCategory.OPD,
            activeDeptId: "DEPT-EXTERNAL",
            activeWardId: null,
            linkedWardIds: Array.Empty<string>(),
            roles: new[] { "EXTERNAL_DOCTOR" },
            dataScope: DataScope.PATIENT_SELF_SCOPE,
            sessionVersion: 1,
            permHash: "EXTERNAL_RESTRICTED",
            prescribeLevel: 1,
            surgeryLevel: 1,
            emrSignLevel: 1,
            expiry: TimeSpan.FromHours(2),
            roleCode: "EXTERNAL_DOCTOR",
            activeDeptName: "外院合作专家科"
        );

        return Ok(ApiResponse<object>.Success(new
        {
            AccessToken = token,
            GrantId = grant.Id,
            AllowedScopes = JsonSerializer.Deserialize<List<string>>(grant.AllowedScopesJson),
            ExpiresAtMs = grant.ValidToMs
        }, "外院受限会话换取成功"));
    }
}
