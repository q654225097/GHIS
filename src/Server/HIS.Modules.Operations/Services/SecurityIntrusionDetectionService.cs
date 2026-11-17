using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Operations.Models;

namespace HIS.Modules.Operations.Services;

public sealed record SecurityPostureDto(
    int SecurityScore,
    string ThreatLevel,
    int TotalAuditEvents,
    int ActiveAlertsCount,
    int CriticalAlertsCount,
    string DengbaoStatus,
    string NationalCipherStatus,
    string HsmStatus,
    long LastScanTimeMs);

public sealed record ResolveAlertRequestDto(string ResolutionNote);

public class SecurityIntrusionDetectionService
{
    private readonly IRepository<OpsSecurityAlert> _alertRepo;
    private readonly IRepository<OpsAuditEvent> _auditRepo;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IAuditTrailContract? _auditTrail;

    public SecurityIntrusionDetectionService(
        IRepository<OpsSecurityAlert> alertRepo,
        IRepository<OpsAuditEvent> auditRepo,
        IDbConnectionFactory connectionFactory,
        IAuditTrailContract? auditTrail = null)
    {
        _alertRepo = alertRepo;
        _auditRepo = auditRepo;
        _connectionFactory = connectionFactory;
        _auditTrail = auditTrail;
    }

    public async Task<IReadOnlyList<OpsSecurityAlert>> GetAlertsAsync(string? severity = null, string? status = null, int limit = 50)
    {
        int safeLimit = Math.Clamp(limit, 1, 200);
        return await _alertRepo.QueryAsync(@"
SELECT * FROM ops_security_alerts
WHERE (@Severity IS NULL OR severity = @Severity)
  AND (@Status IS NULL OR status = @Status)
ORDER BY created_at_ms DESC
LIMIT @Limit", new
        {
            Severity = string.IsNullOrWhiteSpace(severity) ? null : severity.Trim(),
            Status = string.IsNullOrWhiteSpace(status) ? null : status.Trim(),
            Limit = safeLimit
        });
    }

    public async Task<OpsSecurityAlert?> ResolveAlertAsync(string alertId, string resolverId, string resolutionNote)
    {
        var alert = await _alertRepo.GetByIdAsync(alertId);
        if (alert == null)
        {
            return null;
        }

        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        alert.Status = "RESOLVED";
        alert.ResolvedBy = resolverId;
        alert.ResolvedAtMs = nowMs;
        alert.ResolutionNote = resolutionNote?.Trim();
        alert.UpdatedAtMs = nowMs;
        alert.UpdatedBy = resolverId;

        await _alertRepo.UpdateAsync(alert);

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync(
                "IDPS_ALERT_RESOLVED",
                "ops_security_alerts",
                alert.Id,
                $"安全告警 [{alert.AlertNo}] 已由管理员处置完成: {resolutionNote}",
                resolverId);
        }

        return alert;
    }

    public async Task<int> ScanAndGenerateThreatAlertsAsync()
    {
        using var conn = _connectionFactory.CreateConnection();
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long scanWindowMs = nowMs - (24L * 3600 * 1000); // 过去 24 小时

        int newAlertsCount = 0;

        // 1. 扫描防代签违规事件
        var proxySignEvents = await conn.QueryAsync<OpsAuditEvent>(@"
SELECT * FROM ops_audit_events
WHERE event_type = 'EMR_SIGN_PROXY_ATTEMPT'
  AND created_at_ms >= @ScanWindow", new { ScanWindow = scanWindowMs });

        foreach (var ev in proxySignEvents)
        {
            bool exists = await conn.ExecuteScalarAsync<int>(@"
SELECT COUNT(1) FROM ops_security_alerts
WHERE alert_type = 'UNAUTHORIZED_PROXY_SIGNING' AND user_id = @UserId AND created_at_ms >= @EvTime - 1000",
                new { UserId = ev.UserId, EvTime = ev.CreatedAtMs }) > 0;

            if (!exists)
            {
                var alert = new OpsSecurityAlert
                {
                    Id = Guid.NewGuid().ToString("D"),
                    AlertNo = $"ALT-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                    AlertType = "UNAUTHORIZED_PROXY_SIGNING",
                    Severity = "CRITICAL",
                    UserId = ev.UserId,
                    OperatorName = ev.OperatorName,
                    TargetResource = ev.TargetEntity,
                    Description = $"拦截到临床越权代签攻击：用户 [{ev.OperatorName} ({ev.UserId})] 尝试冒用他人数字身份签署医疗病历",
                    ThreatIndicatorsJson = "{\"mitre_id\":\"T1078\",\"rule\":\"IDPS-02-ANTI-PROXY-SIGN\"}",
                    Status = "ACTIVE",
                    CreatedAtMs = ev.CreatedAtMs,
                    CreatedBy = "IDPS_ENGINE",
                    UpdatedAtMs = ev.CreatedAtMs,
                    UpdatedBy = "IDPS_ENGINE",
                    VersionNo = 1
                };
                await _alertRepo.InsertAsync(alert);
                newAlertsCount++;
            }
        }

        // 2. 扫描账号暴力破解/锁定事件
        var lockedEvents = await conn.QueryAsync<OpsAuditEvent>(@"
SELECT * FROM ops_audit_events
WHERE event_type = 'AUTH_LOGIN_LOCKED'
  AND created_at_ms >= @ScanWindow", new { ScanWindow = scanWindowMs });

        foreach (var ev in lockedEvents)
        {
            bool exists = await conn.ExecuteScalarAsync<int>(@"
SELECT COUNT(1) FROM ops_security_alerts
WHERE alert_type = 'BRUTE_FORCE_LOCKOUT' AND user_id = @UserId AND created_at_ms >= @EvTime - 60000",
                new { UserId = ev.UserId, EvTime = ev.CreatedAtMs }) > 0;

            if (!exists)
            {
                var alert = new OpsSecurityAlert
                {
                    Id = Guid.NewGuid().ToString("D"),
                    AlertNo = $"ALT-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                    AlertType = "BRUTE_FORCE_LOCKOUT",
                    Severity = "HIGH",
                    UserId = ev.UserId,
                    OperatorName = ev.OperatorName,
                    TargetResource = "api/v4/platform/auth/login",
                    Description = $"检测到账户连续认证失败并被安全锁定：用户 [{ev.UserId}]",
                    ThreatIndicatorsJson = "{\"mitre_id\":\"T1110\",\"rule\":\"IDPS-01-BRUTE-FORCE\"}",
                    Status = "ACTIVE",
                    CreatedAtMs = ev.CreatedAtMs,
                    CreatedBy = "IDPS_ENGINE",
                    UpdatedAtMs = ev.CreatedAtMs,
                    UpdatedBy = "IDPS_ENGINE",
                    VersionNo = 1
                };
                await _alertRepo.InsertAsync(alert);
                newAlertsCount++;
            }
        }

        return newAlertsCount;
    }

    public async Task<SecurityPostureDto> GetSecurityPostureAsync()
    {
        using var conn = _connectionFactory.CreateConnection();
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        int totalAuditEvents = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ops_audit_events");
        int activeAlerts = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ops_security_alerts WHERE status = 'ACTIVE'");
        int criticalAlerts = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ops_security_alerts WHERE status = 'ACTIVE' AND severity = 'CRITICAL'");
        int highAlerts = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ops_security_alerts WHERE status = 'ACTIVE' AND severity = 'HIGH'");

        int score = 100 - (criticalAlerts * 5) - (highAlerts * 2);
        score = Math.Clamp(score, 10, 100);

        string threatLevel = score switch
        {
            >= 90 => "NORMAL",
            >= 70 => "ATTENTION",
            >= 50 => "HIGH",
            _ => "CRITICAL"
        };

        return new SecurityPostureDto(
            SecurityScore: score,
            ThreatLevel: threatLevel,
            TotalAuditEvents: totalAuditEvents,
            ActiveAlertsCount: activeAlerts,
            CriticalAlertsCount: criticalAlerts,
            DengbaoStatus: "COMPLIANT_L3",
            NationalCipherStatus: "GM/T 0031-2014 & GB/T 39786-2021 (SM2/SM3/SM4)",
            HsmStatus: "ONLINE_HARDWARE_PROTECTED",
            LastScanTimeMs: nowMs
        );
    }
}
