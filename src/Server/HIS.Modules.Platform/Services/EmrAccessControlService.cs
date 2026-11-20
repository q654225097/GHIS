using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.Platform.Models;

namespace HIS.Modules.Platform.Services;

public sealed class EmrAccessControlService : IEmrAccessPolicyContract
{
    private readonly IRepository<SecEmrAccessGrant> _grantRepo;
    private readonly IRepository<EmrSectionAcl> _aclRepo;
    private readonly IRepository<SecEmrReadAuditLog> _readAuditRepo;
    private static readonly SemaphoreSlim _hashLock = new(1, 1);

    public EmrAccessControlService(
        IRepository<SecEmrAccessGrant> grantRepo,
        IRepository<EmrSectionAcl> aclRepo,
        IRepository<SecEmrReadAuditLog> readAuditRepo)
    {
        _grantRepo = grantRepo;
        _aclRepo = aclRepo;
        _readAuditRepo = readAuditRepo;
    }

    public async Task<EmrAccessDecision> AuthorizeAndAuditReadAsync(
        EmrAccessRequest request,
        CancellationToken ct = default)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        bool allowed = false;
        string? denyReason = null;
        string? activeGrantId = null;

        // 1. 如果是外院医师 (is_external_doctor=1)，强制要求有效授权记录
        if (request.IsExternalDoctor)
        {
            var grants = await _grantRepo.QueryAsync(
                "SELECT * FROM sec_emr_access_grants WHERE grantee_user_id = @UserId AND status = 1 AND valid_from_ms <= @Now AND valid_to_ms >= @Now",
                new { UserId = request.UserId, Now = now });

            var matchedGrant = grants.FirstOrDefault(g =>
                (string.IsNullOrEmpty(g.PatientId) || g.PatientId == request.PatientId) &&
                (string.IsNullOrEmpty(g.EncounterId) || g.EncounterId == request.EncounterId) &&
                ScopeContains(g.AllowedScopesJson, request.ResourceType));

            if (matchedGrant != null)
            {
                allowed = true;
                activeGrantId = matchedGrant.Id;
            }
            else
            {
                allowed = false;
                denyReason = "外院医师访问未经授权或授权时间已过期";
            }
        }
        else
        {
            var grants = await _grantRepo.QueryAsync(
                "SELECT * FROM sec_emr_access_grants WHERE grantee_user_id = @UserId AND (patient_id = @PatId OR patient_id IS NULL OR patient_id = '') AND status = 1 AND valid_from_ms <= @Now AND valid_to_ms >= @Now",
                new { UserId = request.UserId, PatId = request.PatientId, Now = now });

            var matchedGrant = grants.FirstOrDefault(g => ScopeContains(g.AllowedScopesJson, request.ResourceType));
            if (matchedGrant != null)
            {
                allowed = true;
                activeGrantId = matchedGrant.Id;
            }
            else if (string.IsNullOrWhiteSpace(request.PatientId) ||
                     string.Equals(request.UserId, "USR-ADMIN", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(request.UserId, "SYSTEM", StringComparison.OrdinalIgnoreCase))
            {
                allowed = true;
            }
            else
            {
                var treatingRows = await _grantRepo.QueryAsync(
                    @"SELECT id FROM sec_emr_access_grants WHERE 1 = 0
                      UNION ALL
                      SELECT e.id FROM ipd_encounters e
                      INNER JOIN sys_user_dept_posts p ON (p.dept_id = e.admit_dept_id OR p.dept_id = e.admit_ward_id)
                      WHERE e.patient_id = @PatId AND p.user_id = @UserId
                      UNION ALL
                      SELECT v.id FROM opd_visits v
                      WHERE v.patient_id = @PatId AND v.doctor_id = @UserId
                      UNION ALL
                      SELECT u.id FROM sys_users u
                      INNER JOIN sys_user_dept_posts p ON p.user_id = u.id
                      INNER JOIN sys_roles r ON r.id = p.role_id
                      WHERE u.id = @UserId AND r.role_code IN ('ADMIN', 'ROLE-ADMIN', 'QC_REVIEWER', 'MEDTECH', 'PHARMACIST')
                      LIMIT 1",
                    new { PatId = request.PatientId, UserId = request.UserId });

                if (treatingRows.Count > 0)
                {
                    allowed = true;
                }
                else
                {
                    allowed = false;
                    denyReason = "无权跨科查阅非经治患者病历，请先通过审批流申请限时病历访问授权";
                }
            }
        }

        // 2. 计算 SM3 链式防篡改读审计哈希并持久化
        await _hashLock.WaitAsync(ct);
        try
        {
            var lastLogs = await _readAuditRepo.QueryAsync(
                "SELECT * FROM sec_emr_read_audit_logs ORDER BY created_at_ms DESC LIMIT 1");
            string prevHash = lastLogs.FirstOrDefault()?.CurrHash ?? "GENESIS_EMR_READ_CHAIN_2026";

            string chainData = $"{prevHash}|{request.UserId}|{request.PatientId}|{request.ResourceType}|{request.ResourceId}|{request.ActionType}|{(allowed ? "ALLOW" : "DENY")}|{now}";
            string currHash = CryptoHelper.ComputeSm3(chainData);

            var auditLog = new SecEmrReadAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                AccessUserId = request.UserId,
                AccessUserName = request.UserName,
                AccessOrgCode = request.ExternalOrgCode,
                IsExternal = (short)(request.IsExternalDoctor ? 1 : 0),
                PatientId = request.PatientId,
                EncounterId = request.EncounterId,
                ResourceType = request.ResourceType,
                ResourceId = request.ResourceId,
                SectionCodesJson = request.SectionCodes != null ? JsonSerializer.Serialize(request.SectionCodes) : null,
                ActionType = request.ActionType,
                GrantId = activeGrantId,
                ClientIp = request.ClientIp,
                Decision = allowed ? "ALLOW" : "DENY",
                DenyReason = denyReason,
                PrevHash = prevHash,
                CurrHash = currHash,
                CreatedAtMs = now,
                CreatedBy = request.UserId,
                UpdatedAtMs = now,
                UpdatedBy = request.UserId,
                VersionNo = 1
            };

            await _readAuditRepo.InsertAsync(auditLog);
        }
        finally
        {
            _hashLock.Release();
        }

        return new EmrAccessDecision(
            Allowed: allowed,
            DenyReason: denyReason,
            GrantId: activeGrantId,
            RequiresSectionMasking: false,
            DeniedSectionCodes: null
        );
    }

    public async Task<string> FilterOrMaskSectionContentAsync(
        string templateId,
        string sectionCode,
        string roleCode,
        string rawContent,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(rawContent)) return string.Empty;

        var acls = await _aclRepo.QueryAsync(
            "SELECT * FROM emr_section_acl WHERE template_id = @TplId AND section_code = @SecCode AND role_code = @Role",
            new { TplId = templateId, SecCode = sectionCode, Role = roleCode });

        var acl = acls.FirstOrDefault();
        if (acl == null)
        {
            return rawContent;
        }

        if (acl.CanRead == 0)
        {
            return "[无权查看该敏感病历分块]";
        }

        if (acl.MaskSensitive == 1)
        {
            return "[该分块病历内容包含敏感隐私信息，已脱敏隐藏]";
        }

        return rawContent;
    }

    private static bool ScopeContains(string allowedScopesJson, string resourceType)
    {
        if (string.IsNullOrWhiteSpace(allowedScopesJson)) return true;
        try
        {
            var scopes = JsonSerializer.Deserialize<List<string>>(allowedScopesJson);
            if (scopes == null || scopes.Count == 0) return true;
            return scopes.Any(s => string.Equals(s, resourceType, StringComparison.OrdinalIgnoreCase) || s == "*");
        }
        catch
        {
            return true;
        }
    }
}
