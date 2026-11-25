using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Consumables.Models;
using HIS.Modules.Consumables.Services;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Consumables.Controllers;

[ApiController]
[Route("api/v4/csm")]
public class ConsumablesController : ControllerBase
{
    private readonly ConsumableSupplyService _service;
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _userSession;
    private readonly IWorkflowEngineContract? _workflowEngine;

    public ConsumablesController(
        ConsumableSupplyService service,
        IDbConnectionFactory connFactory,
        IUserSession? userSession = null,
        IWorkflowEngineContract? workflowEngine = null)
    {
        _service = service;
        _connFactory = connFactory;
        _userSession = userSession;
        _workflowEngine = workflowEngine;
    }

    [HttpGet("consumables")]
    public async Task<ApiResponse<IReadOnlyList<CsmConsumable>>> GetConsumables(
        [FromQuery] string? keyword = null,
        [FromQuery] string? category = null,
        [FromQuery] bool? isSterile = null,
        [FromQuery] bool? isHighValue = null,
        [FromQuery] bool? activeOnly = null)
    {
        using var conn = _connFactory.CreateConnection();
        string sql = "SELECT * FROM csm_consumables WHERE 1=1";
        var dp = new DynamicParameters();

        if (activeOnly == true)
        {
            sql += " AND is_active = 1";
        }
        if (!string.IsNullOrWhiteSpace(category))
        {
            sql += " AND category = @Category";
            dp.Add("Category", category.Trim());
        }
        if (isSterile.HasValue)
        {
            sql += " AND is_sterile = @IsSterile";
            dp.Add("IsSterile", isSterile.Value ? 1 : 0);
        }
        if (isHighValue.HasValue)
        {
            sql += " AND is_high_value = @IsHighValue";
            dp.Add("IsHighValue", isHighValue.Value ? 1 : 0);
        }
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            sql += " AND (code LIKE @Kw OR name LIKE @Kw OR spec LIKE @Kw)";
            dp.Add("Kw", "%" + keyword.Trim() + "%");
        }
        sql += " ORDER BY code ASC";

        var list = (await conn.QueryAsync<CsmConsumable>(sql, dp)).ToList();
        return ApiResponse<IReadOnlyList<CsmConsumable>>.Success(list);
    }

    [HttpPost("consumables")]
    public async Task<ApiResponse<CsmConsumable>> CreateConsumable([FromBody] CreateConsumableRequestDto req, CancellationToken ct = default)
    {
        if (!IsAdmin())
        {
            return ApiResponse<CsmConsumable>.Fail(403, "仅系统管理员可以维护耗材目录");
        }
        if (string.IsNullOrWhiteSpace(req.Code) || string.IsNullOrWhiteSpace(req.Name))
        {
            return ApiResponse<CsmConsumable>.Fail(400, "耗材编码和名称不能为空");
        }
        if (req.Price < 0)
        {
            return ApiResponse<CsmConsumable>.Fail(400, "单价不能为负数");
        }

        try
        {
            var result = await _service.CreateConsumableAsync(req, CurrentUserId(), _userSession?.DisplayName, ct);
            return ApiResponse<CsmConsumable>.Success(result, "耗材已添加");
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<CsmConsumable>.Fail(409, ex.Message);
        }
    }

    [HttpPost("consumables/{id}/toggle-active")]
    public async Task<ApiResponse<CsmConsumable>> ToggleActive(string id, CancellationToken ct = default)
    {
        if (!IsAdmin())
        {
            return ApiResponse<CsmConsumable>.Fail(403, "仅系统管理员可以修改耗材启用状态");
        }
        try
        {
            var result = await _service.ToggleActiveAsync(id, CurrentUserId(), _userSession?.DisplayName, ct);
            return ApiResponse<CsmConsumable>.Success(result);
        }
        catch (KeyNotFoundException)
        {
            return ApiResponse<CsmConsumable>.Fail(404, "耗材不存在");
        }
    }

    [HttpPost("consumables/{id}/adjust-price")]
    public async Task<ApiResponse<CsmConsumable>> AdjustPrice(string id, [FromBody] AdjustConsumablePriceRequestDto req, CancellationToken ct = default)
    {
        if (!IsAdmin())
        {
            return ApiResponse<CsmConsumable>.Fail(403, "仅系统管理员可以调整耗材单价");
        }
        if (req.NewPrice < 0)
        {
            return ApiResponse<CsmConsumable>.Fail(400, "耗材新单价不能为负数");
        }
        if (string.IsNullOrWhiteSpace(req.Reason))
        {
            return ApiResponse<CsmConsumable>.Fail(400, "调价必须填写依据原因");
        }

        try
        {
            var result = await _service.AdjustPriceAsync(id, req.NewPrice, req.Reason, CurrentUserId(), _userSession?.DisplayName, ct);
            return ApiResponse<CsmConsumable>.Success(result, "耗材单价调整成功");
        }
        catch (KeyNotFoundException)
        {
            return ApiResponse<CsmConsumable>.Fail(404, "耗材不存在");
        }
    }

    [HttpGet("stock-summary")]
    [HttpGet("stocks")]
    public async Task<ApiResponse<IReadOnlyList<ConsumableStockSummaryView>>> GetStockSummary(
        [FromQuery] string? warehouseId = null,
        [FromQuery] string? keyword = null)
    {
        using var conn = _connFactory.CreateConnection();
        string sql = @"
SELECT c.id AS ConsumableId, c.code AS Code, c.name AS Name, c.spec AS Spec,
       c.unit AS Unit, c.category AS Category, (c.is_high_value = 1) AS IsHighValue,
       (c.is_sterile = 1) AS IsSterile, c.price AS Price,
       s.warehouse_id AS WarehouseId, s.quantity AS Quantity, s.reserved_qty AS ReservedQty
FROM csm_stocks s
JOIN csm_consumables c ON c.id = s.consumable_id
WHERE 1=1";
        var dp = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(warehouseId))
        {
            sql += " AND s.warehouse_id = @WhId";
            dp.Add("WhId", warehouseId.Trim());
        }
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            sql += " AND (c.code LIKE @Kw OR c.name LIKE @Kw OR c.spec LIKE @Kw)";
            dp.Add("Kw", "%" + keyword.Trim() + "%");
        }
        sql += " ORDER BY c.code ASC";

        var list = (await conn.QueryAsync<ConsumableStockSummaryView>(sql, dp)).ToList();
        return ApiResponse<IReadOnlyList<ConsumableStockSummaryView>>.Success(list);
    }

    [HttpGet("batches")]
    public async Task<ApiResponse<IReadOnlyList<CsmBatch>>> GetBatches([FromQuery] string consumableId)
    {
        using var conn = _connFactory.CreateConnection();
        var list = (await conn.QueryAsync<CsmBatch>(
            "SELECT * FROM csm_batches WHERE consumable_id = @CsmId ORDER BY created_at_ms DESC",
            new { CsmId = consumableId })).ToList();
        return ApiResponse<IReadOnlyList<CsmBatch>>.Success(list);
    }

    [HttpPost("batches/stock-in")]
    [HttpPost("stock-in")]
    public async Task<ApiResponse<CsmBatch>> StockIn([FromBody] StockInRequestDto req, CancellationToken ct = default)
    {
        if (!HasAnyRole("WAREHOUSE", "WAREHOUSE_KEEPER", "ADMIN", RoleCode.Admin))
        {
            return ApiResponse<CsmBatch>.Fail(403, "仅库房管理员或系统管理员可以执行耗材入库");
        }
        if (req.Quantity <= 0)
        {
            return ApiResponse<CsmBatch>.Fail(400, "入库数量必须大于 0");
        }
        if (string.IsNullOrWhiteSpace(req.BatchNo) || string.IsNullOrWhiteSpace(req.ExpiryDate))
        {
            return ApiResponse<CsmBatch>.Fail(400, "批号与有效期不能为空");
        }

        try
        {
            var batch = await _service.StockInAsync(req, CurrentUserId(), _userSession?.DisplayName, ct);
            return ApiResponse<CsmBatch>.Success(batch, "耗材入库成功");
        }
        catch (KeyNotFoundException ex)
        {
            return ApiResponse<CsmBatch>.Fail(404, ex.Message);
        }
    }

    [HttpPost("batches/{id}/freeze")]
    public async Task<ApiResponse<bool>> FreezeBatch(string id, [FromQuery] bool isFrozen, CancellationToken ct = default)
    {
        if (!HasAnyRole("WAREHOUSE", "WAREHOUSE_KEEPER", "ADMIN", RoleCode.Admin))
        {
            return ApiResponse<bool>.Fail(403, "仅库房管理员可以冻结批次");
        }
        try
        {
            await _service.FreezeBatchAsync(id, isFrozen, CurrentUserId(), _userSession?.DisplayName, ct);
            return ApiResponse<bool>.Success(true, isFrozen ? "批次已冻结" : "批次已解冻");
        }
        catch (KeyNotFoundException)
        {
            return ApiResponse<bool>.Fail(404, "批次不存在");
        }
    }

    [HttpPost("requisitions")]
    public async Task<ApiResponse<CsmRequisition>> CreateRequisition([FromBody] CreateRequisitionRequestDto req, CancellationToken ct = default)
    {
        if (!HasAnyRole("WARD_NURSE", RoleCode.NurseIpd, RoleCode.DoctorIpd, RoleCode.DoctorOpd, "ADMIN", RoleCode.Admin))
        {
            return ApiResponse<CsmRequisition>.Fail(403, "仅临床科室或病区人员可以提交耗材请领");
        }
        if (req.Items == null || req.Items.Count == 0)
        {
            return ApiResponse<CsmRequisition>.Fail(400, "请领明细不能为空");
        }

        var result = await _service.CreateRequisitionAsync(req, CurrentUserId(), _userSession?.DisplayName, ct);

        if (_workflowEngine != null)
        {
            decimal totalAmount = req.Items.Sum(i => i.Quantity * 10m);
            var startRes = await _workflowEngine.StartWorkflowAsync("CSM_REQUISITION", result.Id, CurrentUserId(), result.WardId, new Dictionary<string, object?>
            {
                ["requisitionNo"] = result.RequisitionNo,
                ["deptId"] = result.WardId,
                ["totalAmount"] = totalAmount,
                ["urgency"] = "NORMAL"
            }, ct);

            using var conn = _connFactory.CreateConnection();
            if (startRes.IsWorkflowTriggered)
            {
                await conn.ExecuteAsync(@"
                    UPDATE csm_requisitions 
                    SET approval_status = 'PENDING_APPROVAL', workflow_instance_id = @InstId, submitted_at_ms = @Now 
                    WHERE id = @Id",
                    new { InstId = startRes.InstanceId, Now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Id = result.Id });
                result.ApprovalStatus = "PENDING_APPROVAL";
                result.WorkflowInstanceId = startRes.InstanceId;
            }
            else
            {
                await conn.ExecuteAsync(@"
                    UPDATE csm_requisitions 
                    SET approval_status = 'APPROVED', approved_at_ms = @Now, approved_by = 'SYSTEM' 
                    WHERE id = @Id",
                    new { Now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Id = result.Id });
                result.ApprovalStatus = "APPROVED";
            }
        }

        return ApiResponse<CsmRequisition>.Success(result, "请领单已提交");
    }

    [HttpGet("requisitions")]
    public async Task<ApiResponse<IReadOnlyList<CsmRequisition>>> GetRequisitions(
        [FromQuery] string? wardId = null,
        [FromQuery] string? status = null)
    {
        using var conn = _connFactory.CreateConnection();
        string sql = "SELECT * FROM csm_requisitions WHERE 1=1";
        var dp = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(wardId))
        {
            sql += " AND ward_id = @WardId";
            dp.Add("WardId", wardId.Trim());
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            sql += " AND status = @Status";
            dp.Add("Status", status.Trim());
        }
        sql += " ORDER BY created_at_ms DESC";

        var list = (await conn.QueryAsync<CsmRequisition>(sql, dp)).ToList();
        return ApiResponse<IReadOnlyList<CsmRequisition>>.Success(list);
    }

    [HttpPost("requisitions/{id}/approve")]
    public async Task<ApiResponse<CsmRequisition>> ApproveRequisition(string id, CancellationToken ct = default)
    {
        if (!HasAnyRole("WAREHOUSE", "WAREHOUSE_KEEPER", "ADMIN", RoleCode.Admin))
        {
            return ApiResponse<CsmRequisition>.Fail(403, "仅库房管理员或管理员可以审批请领单");
        }
        try
        {
            var result = await _service.ApproveRequisitionAsync(id, CurrentUserId(), _userSession?.DisplayName, ct);
            using (var conn = _connFactory.CreateConnection())
            {
                await conn.ExecuteAsync(@"
                    UPDATE csm_requisitions 
                    SET approval_status = 'APPROVED', approved_at_ms = @Now, approved_by = @User 
                    WHERE id = @Id",
                    new { Now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), User = CurrentUserId(), Id = id });
            }
            result.ApprovalStatus = "APPROVED";
            return ApiResponse<CsmRequisition>.Success(result, "请领单已审批");
        }
        catch (Exception ex)
        {
            return ApiResponse<CsmRequisition>.Fail(400, ex.Message);
        }
    }

    [HttpPost("requisitions/{id}/issue")]
    public async Task<ApiResponse<CsmRequisition>> IssueRequisition(string id, [FromQuery] string? warehouseId = null, CancellationToken ct = default)
    {
        if (!HasAnyRole("WAREHOUSE", "WAREHOUSE_KEEPER", "ADMIN", RoleCode.Admin))
        {
            return ApiResponse<CsmRequisition>.Fail(403, "仅库房管理员可以发放耗材");
        }

        using (var checkConn = _connFactory.CreateConnection())
        {
            var currentReq = await checkConn.QueryFirstOrDefaultAsync<CsmRequisition>("SELECT * FROM csm_requisitions WHERE id = @Id", new { Id = id });
            if (currentReq != null && !string.IsNullOrWhiteSpace(currentReq.ApprovalStatus) && !string.Equals(currentReq.ApprovalStatus, "APPROVED", StringComparison.OrdinalIgnoreCase))
            {
                return ApiResponse<CsmRequisition>.Fail(409, $"耗材请领单 [{currentReq.RequisitionNo}] 审批状态为 [{currentReq.ApprovalStatus}]，未审批通过禁止出库发放！");
            }
        }

        string actualWarehouseId = string.IsNullOrWhiteSpace(warehouseId) || warehouseId == "WH-CSM-01" ? "DEPT-WH-CSM" : warehouseId;
        try
        {
            var result = await _service.IssueRequisitionAsync(id, actualWarehouseId, CurrentUserId(), _userSession?.DisplayName, ct);
            return ApiResponse<CsmRequisition>.Success(result, "耗材已出库发放");
        }
        catch (Exception ex)
        {
            return ApiResponse<CsmRequisition>.Fail(400, ex.Message);
        }
    }

    [HttpPost("transfers")]
    public async Task<ApiResponse<bool>> TransferStock([FromBody] TransferConsumableRequestDto req, CancellationToken ct = default)
    {
        if (!HasAnyRole("WAREHOUSE", "WAREHOUSE_KEEPER", "ADMIN", RoleCode.Admin))
        {
            return ApiResponse<bool>.Fail(403, "仅库房管理员可以调拨耗材");
        }
        if (req.Quantity <= 0)
        {
            return ApiResponse<bool>.Fail(400, "调拨数量必须大于 0");
        }
        try
        {
            await _service.TransferStockAsync(req, CurrentUserId(), _userSession?.DisplayName, ct);
            return ApiResponse<bool>.Success(true, "调拨完成");
        }
        catch (Exception ex)
        {
            return ApiResponse<bool>.Fail(400, ex.Message);
        }
    }

    [HttpGet("sterile-packages")]
    [HttpGet("cssd/packages")]
    public async Task<ApiResponse<IReadOnlyList<CsmSterilePackage>>> GetSterilePackages(
        [FromQuery] string? status = null,
        [FromQuery] string? location = null)
    {
        using var conn = _connFactory.CreateConnection();
        string sql = "SELECT * FROM csm_sterile_packages WHERE 1=1";
        var dp = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(status))
        {
            sql += " AND status = @Status";
            dp.Add("Status", status.Trim());
        }
        if (!string.IsNullOrWhiteSpace(location))
        {
            sql += " AND current_location = @Loc";
            dp.Add("Loc", location.Trim());
        }
        sql += " ORDER BY created_at_ms DESC";

        var list = (await conn.QueryAsync<CsmSterilePackage>(sql, dp)).ToList();
        return ApiResponse<IReadOnlyList<CsmSterilePackage>>.Success(list);
    }

    [HttpPost("sterile-packages")]
    [HttpPost("cssd/packages")]
    public async Task<ApiResponse<CsmSterilePackage>> CreateSterilePackage([FromBody] CreateSterilePackageRequestDto req, CancellationToken ct = default)
    {
        if (!HasAnyRole("WAREHOUSE", "WAREHOUSE_KEEPER", "ADMIN", RoleCode.Admin))
        {
            return ApiResponse<CsmSterilePackage>.Fail(403, "仅供应室或管理员可以组装灭菌包");
        }
        if (string.IsNullOrWhiteSpace(req.PackageName))
        {
            return ApiResponse<CsmSterilePackage>.Fail(400, "灭菌包名称不能为空");
        }
        if (req.Contents == null || req.Contents.Count == 0)
        {
            return ApiResponse<CsmSterilePackage>.Fail(400, "灭菌包不能是空包，请至少包含一件器械或辅料明细");
        }
        using (var mdmConn = _connFactory.CreateConnection())
        {
            var packMdm = await mdmConn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT pack_code AS PackCode, is_active AS IsActive FROM mdm_cssd_packs WHERE pack_code = @Key OR pack_name = @Key LIMIT 1",
                new { Key = req.PackageName.Trim() });
            if (packMdm != null && Convert.ToInt32(packMdm.IsActive) == 0)
                return ApiResponse<CsmSterilePackage>.Fail(409, $"CSSD 器械包主数据 [{req.PackageName}] 已停用，禁止组装");
        }
        var pkg = await _service.CreateSterilePackageAsync(req, CurrentUserId(), _userSession?.DisplayName, ct);
        return ApiResponse<CsmSterilePackage>.Success(pkg, "灭菌包已创建");
    }

    public sealed record CssdSterilizeRequestDto(
        string PackageId,
        string BatchNo,
        string Method = "HIGH_PRESSURE_STEAM",
        long ValidDays = 180
    );

    public sealed record CssdDispatchRequestDto(
        string PackageId,
        string ToLocation,
        string MovementType = "ISSUE",
        string? Remark = null
    );

    [HttpPost("cssd/sterilize")]
    public async Task<ApiResponse<CsmSterilePackage>> CssdSterilize([FromBody] CssdSterilizeRequestDto req, CancellationToken ct = default)
    {
        return await SterilizePackage(req.PackageId, new SterilizePackageRequestDto(req.BatchNo, req.Method, req.ValidDays), ct);
    }

    [HttpPost("cssd/dispatch")]
    public async Task<ApiResponse<CsmSterilePackage>> CssdDispatch([FromBody] CssdDispatchRequestDto req, CancellationToken ct = default)
    {
        return await IssueSterilePackage(req.PackageId, new IssueSterilePackageRequestDto(req.ToLocation, req.Remark), ct);
    }

    [HttpPost("sterile-packages/{id}/sterilize")]
    public async Task<ApiResponse<CsmSterilePackage>> SterilizePackage(string id, [FromBody] SterilizePackageRequestDto req, CancellationToken ct = default)
    {
        if (!HasAnyRole("WAREHOUSE", "WAREHOUSE_KEEPER", "ADMIN", RoleCode.Admin))
        {
            return ApiResponse<CsmSterilePackage>.Fail(403, "仅供应室可以登记灭菌");
        }
        if (string.IsNullOrWhiteSpace(req.SterilizationBatchNo))
        {
            return ApiResponse<CsmSterilePackage>.Fail(400, "灭菌批号不能为空");
        }
        try
        {
            var pkg = await _service.SterilizePackageAsync(id, req, CurrentUserId(), _userSession?.DisplayName, ct);
            return ApiResponse<CsmSterilePackage>.Success(pkg, "灭菌记录完成");
        }
        catch (Exception ex)
        {
            return ApiResponse<CsmSterilePackage>.Fail(400, ex.Message);
        }
    }

    [HttpPost("sterile-packages/{id}/issue")]
    public async Task<ApiResponse<CsmSterilePackage>> IssueSterilePackage(string id, [FromBody] IssueSterilePackageRequestDto req, CancellationToken ct = default)
    {
        if (!HasAnyRole("WAREHOUSE", "WAREHOUSE_KEEPER", "ADMIN", RoleCode.Admin))
        {
            return ApiResponse<CsmSterilePackage>.Fail(403, "仅供应室可以发放灭菌包");
        }
        try
        {
            var pkg = await _service.IssueSterilePackageAsync(id, req, CurrentUserId(), _userSession?.DisplayName, ct);
            return ApiResponse<CsmSterilePackage>.Success(pkg, "灭菌包已发放");
        }
        catch (Exception ex)
        {
            return ApiResponse<CsmSterilePackage>.Fail(400, ex.Message);
        }
    }

    [HttpPost("sterile-packages/{id}/return")]
    public async Task<ApiResponse<CsmSterilePackage>> ReturnSterilePackage(string id, [FromBody] ReturnSterilePackageRequestDto req, CancellationToken ct = default)
    {
        if (!HasAnyRole("WAREHOUSE", "WAREHOUSE_KEEPER", RoleCode.NurseIpd, "ADMIN", RoleCode.Admin))
        {
            return ApiResponse<CsmSterilePackage>.Fail(403, "仅病区或供应室可以回收灭菌包");
        }
        try
        {
            var pkg = await _service.ReturnSterilePackageAsync(id, req, CurrentUserId(), _userSession?.DisplayName, ct);
            return ApiResponse<CsmSterilePackage>.Success(pkg, "灭菌包回收成功");
        }
        catch (Exception ex)
        {
            return ApiResponse<CsmSterilePackage>.Fail(400, ex.Message);
        }
    }

    [HttpGet("trace-codes")]
    public async Task<ApiResponse<IReadOnlyList<CsmTraceCode>>> GetTraceCodes(
        [FromQuery] string? consumableId = null,
        [FromQuery] string? patientId = null,
        [FromQuery] string? status = null)
    {
        using var conn = _connFactory.CreateConnection();
        string sql = "SELECT * FROM csm_trace_codes WHERE 1=1";
        var dp = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(consumableId))
        {
            sql += " AND consumable_id = @CsmId";
            dp.Add("CsmId", consumableId.Trim());
        }
        if (!string.IsNullOrWhiteSpace(patientId))
        {
            sql += " AND patient_id = @PatId";
            dp.Add("PatId", patientId.Trim());
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            sql += " AND status = @Status";
            dp.Add("Status", status.Trim());
        }
        sql += " ORDER BY created_at_ms DESC";

        var list = (await conn.QueryAsync<CsmTraceCode>(sql, dp)).ToList();
        return ApiResponse<IReadOnlyList<CsmTraceCode>>.Success(list);
    }

    [HttpPost("trace-codes/scan-use")]
    [HttpPost("trace/use")]
    public async Task<ApiResponse<CsmTraceCode>> ScanUseTraceCode([FromBody] ScanUseTraceCodeRequestDto req, CancellationToken ct = default)
    {
        if (!HasAnyRole("WARD_NURSE", RoleCode.NurseIpd, RoleCode.DoctorIpd, "ADMIN", RoleCode.Admin))
        {
            return ApiResponse<CsmTraceCode>.Fail(403, "仅病区医护人员可以登记耗材使用");
        }
        if (string.IsNullOrWhiteSpace(req.TraceCode) || string.IsNullOrWhiteSpace(req.PatientId))
        {
            return ApiResponse<CsmTraceCode>.Fail(400, "追溯码与患者ID不能为空");
        }
        try
        {
            var result = await _service.ScanUseTraceCodeAsync(req, CurrentUserId(), _userSession?.DisplayName, ct);
            return ApiResponse<CsmTraceCode>.Success(result, "高值耗材扫码使用登记完成");
        }
        catch (Exception ex)
        {
            return ApiResponse<CsmTraceCode>.Fail(400, ex.Message);
        }
    }

    [HttpPost("trace-codes/scan-recall")]
    public async Task<ApiResponse<CsmTraceCode>> ScanRecallTraceCode([FromBody] ScanRecallTraceCodeRequestDto req, CancellationToken ct = default)
    {
        if (!HasAnyRole("WAREHOUSE", "WAREHOUSE_KEEPER", "ADMIN", RoleCode.Admin))
        {
            return ApiResponse<CsmTraceCode>.Fail(403, "仅库房管理员可以执行耗材召回");
        }
        if (string.IsNullOrWhiteSpace(req.TraceCode) || string.IsNullOrWhiteSpace(req.RecallReason))
        {
            return ApiResponse<CsmTraceCode>.Fail(400, "追溯码与召回原因不能为空");
        }
        try
        {
            var result = await _service.ScanRecallTraceCodeAsync(req, CurrentUserId(), _userSession?.DisplayName, ct);
            return ApiResponse<CsmTraceCode>.Success(result, "追溯码已召回");
        }
        catch (Exception ex)
        {
            return ApiResponse<CsmTraceCode>.Fail(400, ex.Message);
        }
    }

    [HttpGet("finance-report")]
    [HttpGet("finance/report")]
    public async Task<ApiResponse<ConsumableFinanceReportDto>> GetFinanceReport(CancellationToken ct = default)
    {
        if (!HasAnyRole("ADMIN", RoleCode.Admin, "CASHIER", RoleCode.Cashier))
        {
            return ApiResponse<ConsumableFinanceReportDto>.Fail(403, "仅管理员与财务人员可查看对账报表");
        }
        var report = await _service.GetFinanceReportAsync(ct);
        return ApiResponse<ConsumableFinanceReportDto>.Success(report);
    }

    private string CurrentUserId() => _userSession?.UserId ?? "USER-ADMIN";

    private bool IsAdmin() =>
        _userSession != null && (RoleCode.IsAdmin(_userSession.RoleCode) || RoleCode.HasRole(_userSession.Roles, RoleCode.Admin));

    private bool HasAnyRole(params string[] roles) =>
        IsAdmin() || (_userSession != null && RoleCode.HasAnyRole(_userSession.Roles, roles));
}
