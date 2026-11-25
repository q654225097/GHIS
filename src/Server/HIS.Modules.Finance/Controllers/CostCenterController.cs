using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.BuildingBlocks.Security;
using HIS.Modules.Finance.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Finance.Controllers;

[ApiController]
[Route("api/v4/finance/cost-centers")]
[RequireRole(RoleCode.Admin, RoleCode.Cashier, "FINANCE", "ACCOUNTANT")]
public class CostCenterController : ControllerBase
{
    private readonly IRepository<FinCostCenter> _costCenterRepo;
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _session;
    private readonly IAuditTrailContract? _auditTrail;

    public CostCenterController(
        IRepository<FinCostCenter> costCenterRepo,
        IDbConnectionFactory connFactory,
        IUserSession? session = null,
        IAuditTrailContract? auditTrail = null)
    {
        _costCenterRepo = costCenterRepo;
        _connFactory = connFactory;
        _session = session;
        _auditTrail = auditTrail;
    }

    private string CurrentUserId => _session?.UserId ?? "USER-FINANCE-ADMIN";

    [HttpPost("allocate")]
    public async Task<ApiResponse<FinCostCenter>> AllocateCost([FromBody] AllocateCostRequest req, CancellationToken ct = default)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.DeptId) || string.IsNullOrWhiteSpace(req.Period))
            return ApiResponse<FinCostCenter>.Fail(400, "核算科室与会计核算期间不能为空");

        if (req.DirectCost < 0 || req.AllocatedCost < 0)
            return ApiResponse<FinCostCenter>.Fail(400, "直接成本与间接分摊成本不可为负数");

        decimal allocatedCost = req.AllocatedCost;
        if (!string.IsNullOrWhiteSpace(req.AllocationMethod))
        {
            using var mdmConn = _connFactory.CreateConnection();
            var subj = await mdmConn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT allocation_weight AS AllocationWeight, is_active AS IsActive FROM mdm_accounting_subjects WHERE subject_code = @Code LIMIT 1",
                new { Code = req.AllocationMethod.Trim() });
            if (subj != null)
            {
                if (Convert.ToInt32(subj.IsActive) == 0)
                    return ApiResponse<FinCostCenter>.Fail(409, $"会计科目主数据 [{req.AllocationMethod}] 已停用，禁止成本分摊");
                if (allocatedCost == 0m && req.DirectCost > 0m)
                    allocatedCost = Math.Round(req.DirectCost * Convert.ToDecimal(subj.AllocationWeight), 2);
            }
        }

        decimal totalCost = req.DirectCost + allocatedCost;
        decimal profitMargin = req.Revenue > 0
            ? Math.Round((req.Revenue - totalCost) / req.Revenue * 100m, 2)
            : 0m;

        // Check if existing for dept & period
        var existing = (await _costCenterRepo.QueryAsync(
            "SELECT * FROM fin_cost_centers WHERE dept_id = @DeptId AND period = @Period",
            new { req.DeptId, req.Period })).FirstOrDefault();

        FinCostCenter record;
        if (existing != null)
        {
            existing.DirectCost = req.DirectCost;
            existing.AllocatedCost = allocatedCost;
            existing.TotalCost = totalCost;
            existing.Revenue = req.Revenue;
            existing.ProfitMargin = profitMargin;
            existing.AllocationMethod = req.AllocationMethod ?? "STEP_DOWN";
            existing.UpdatedBy = CurrentUserId;
            await _costCenterRepo.UpdateAsync(existing, ct);
            record = existing;
        }
        else
        {
            record = new FinCostCenter
            {
                DeptId = req.DeptId,
                Period = req.Period,
                DirectCost = req.DirectCost,
                AllocatedCost = allocatedCost,
                TotalCost = totalCost,
                Revenue = req.Revenue,
                ProfitMargin = profitMargin,
                AllocationMethod = req.AllocationMethod ?? "STEP_DOWN",
                CreatedBy = CurrentUserId,
                UpdatedBy = CurrentUserId
            };
            await _costCenterRepo.InsertAsync(record, ct);
        }

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync("FIN_COST_CENTER_ALLOCATE", nameof(FinCostCenter), record.Id,
                $"科室全成本分摊核算：科室=[{record.DeptId}]，期间=[{record.Period}]，总成本={record.TotalCost:F2}（直接={record.DirectCost:F2}，分摊={record.AllocatedCost:F2}），收入={record.Revenue:F2}，结余率={record.ProfitMargin}%",
                CurrentUserId, _session?.DisplayName, _session?.ActiveDeptId, ct);
        }

        return ApiResponse<FinCostCenter>.Success(record, "科室全成本分摊核算完成");
    }

    [HttpGet("report")]
    public async Task<ApiResponse<IReadOnlyList<CostCenterReportDto>>> GetReport(
        [FromQuery] string? period = null,
        [FromQuery] string? deptId = null)
    {
        var records = await _costCenterRepo.QueryAsync(@"
SELECT * FROM fin_cost_centers
WHERE (@Period IS NULL OR period = @Period)
  AND (@DeptId IS NULL OR dept_id = @DeptId)
ORDER BY period DESC, dept_id ASC", new { Period = period, DeptId = deptId });

        var report = records.Select(r => new CostCenterReportDto(
            DeptId: r.DeptId,
            Period: r.Period,
            DirectCost: r.DirectCost,
            AllocatedCost: r.AllocatedCost,
            TotalCost: r.TotalCost,
            Revenue: r.Revenue,
            ProfitMargin: r.ProfitMargin,
            AllocationMethod: r.AllocationMethod
        )).ToList();

        return ApiResponse<IReadOnlyList<CostCenterReportDto>>.Success(report);
    }

    [HttpGet]
    public async Task<ApiResponse<IReadOnlyList<FinCostCenter>>> GetCostCenters(
        [FromQuery] string? period = null,
        [FromQuery] string? deptId = null)
    {
        var records = await _costCenterRepo.QueryAsync(@"
SELECT * FROM fin_cost_centers
WHERE (@Period IS NULL OR period = @Period)
  AND (@DeptId IS NULL OR dept_id = @DeptId)
ORDER BY period DESC, dept_id ASC", new { Period = period, DeptId = deptId });

        return ApiResponse<IReadOnlyList<FinCostCenter>>.Success(records);
    }
}
