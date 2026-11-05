using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Modules.Operations.Models;
using HIS.Modules.Operations.Services;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Operations.Controllers;

[ApiController]
[Route("api/v4/operations")]
public class OperationsController : ControllerBase
{
    private readonly IRepository<OpsAuditEvent> _auditRepo;
    private readonly AuditLogService _auditService;

    public OperationsController(
        IRepository<OpsAuditEvent> auditRepo,
        AuditLogService auditService)
    {
        _auditRepo = auditRepo;
        _auditService = auditService;
    }

    [HttpGet("audit-logs")]
    public async Task<ApiResponse<IReadOnlyList<OpsAuditEvent>>> GetAuditLogs([FromQuery] int limit = 50)
    {
        var list = await _auditRepo.QueryAsync(
            $"SELECT * FROM ops_audit_events ORDER BY created_at_ms DESC LIMIT {Math.Min(100, limit)}");
        return ApiResponse<IReadOnlyList<OpsAuditEvent>>.Success(list);
    }
}

