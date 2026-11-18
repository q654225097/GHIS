using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Operations.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Operations.Controllers;

[ApiController]
[Route("api/v4/equipment")]
public class EquipmentController : ControllerBase
{
    private readonly IRepository<EqpEquipment> _eqpRepo;
    private readonly IRepository<EqpMaintenanceRecord> _maintRepo;
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _session;
    private readonly IAuditTrailContract? _auditTrail;

    public EquipmentController(
        IRepository<EqpEquipment> eqpRepo,
        IRepository<EqpMaintenanceRecord> maintRepo,
        IDbConnectionFactory connFactory,
        IUserSession? session = null,
        IAuditTrailContract? auditTrail = null)
    {
        _eqpRepo = eqpRepo;
        _maintRepo = maintRepo;
        _connFactory = connFactory;
        _session = session;
        _auditTrail = auditTrail;
    }

    private string CurrentUserId => _session?.UserId ?? "USER-EQUIPMENT-ADMIN";

    [HttpPost("equipments")]
    public async Task<ApiResponse<EqpEquipment>> CreateEquipment([FromBody] CreateEquipmentRequest req, CancellationToken ct = default)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.Code) || string.IsNullOrWhiteSpace(req.Name))
            return ApiResponse<EqpEquipment>.Fail(400, "设备唯一编码与设备名称不能为空");

        var existing = (await _eqpRepo.QueryAsync("SELECT * FROM eqp_equipments WHERE code = @Code", new { req.Code })).FirstOrDefault();
        if (existing != null)
            return ApiResponse<EqpEquipment>.Fail(409, $"设备编码 [{req.Code}] 已存在");

        var eqp = new EqpEquipment
        {
            Code = req.Code,
            Name = req.Name,
            ModelSpec = req.ModelSpec,
            DeptId = req.DeptId,
            PurchaseDate = req.PurchaseDate ?? DateTime.Today.ToString("yyyy-MM-dd"),
            Price = req.Price,
            WarrantyEnd = req.WarrantyEnd,
            IsMandatoryInspection = (short)(req.IsMandatoryInspection ? 1 : 0),
            NextInspectionDate = req.NextInspectionDate,
            Status = "NORMAL",
            CreatedBy = CurrentUserId,
            UpdatedBy = CurrentUserId
        };

        await _eqpRepo.InsertAsync(eqp, ct);

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync("EQP_ASSET_REGISTER", nameof(EqpEquipment), eqp.Id,
                $"登记医疗设备资产：编码=[{eqp.Code}]，名称=[{eqp.Name}]，规格=[{eqp.ModelSpec}]，科室=[{eqp.DeptId}]，原值={eqp.Price:F2}元",
                CurrentUserId, _session?.DisplayName, _session?.ActiveDeptId, ct);
        }

        return ApiResponse<EqpEquipment>.Success(eqp, "设备资产已完成档案登记");
    }

    [HttpGet("equipments")]
    public async Task<ApiResponse<IReadOnlyList<EqpEquipment>>> GetEquipments(
        [FromQuery] string? deptId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? keyword = null)
    {
        var list = await _eqpRepo.QueryAsync(@"
SELECT * FROM eqp_equipments
WHERE (@DeptId IS NULL OR dept_id = @DeptId)
  AND (@Status IS NULL OR status = @Status)
  AND (@Keyword IS NULL OR code LIKE '%' || @Keyword || '%' OR name LIKE '%' || @Keyword || '%')
ORDER BY created_at_ms DESC", new { DeptId = deptId, Status = status, Keyword = keyword });

        return ApiResponse<IReadOnlyList<EqpEquipment>>.Success(list);
    }

    [HttpGet("equipments/{id}")]
    public async Task<ApiResponse<EqpEquipment>> GetEquipmentById(string id, CancellationToken ct = default)
    {
        var eqp = await _eqpRepo.GetByIdAsync(id, ct);
        if (eqp == null)
            return ApiResponse<EqpEquipment>.Fail(404, "设备不存在");

        return ApiResponse<EqpEquipment>.Success(eqp);
    }

    [HttpPost("equipments/{id}/maintenance")]
    public async Task<ApiResponse<EqpMaintenanceRecord>> RecordMaintenance(string id, [FromBody] RecordMaintenanceRequest req, CancellationToken ct = default)
    {
        var eqp = await _eqpRepo.GetByIdAsync(id, ct);
        if (eqp == null)
            return ApiResponse<EqpMaintenanceRecord>.Fail(404, "设备不存在");

        if (req == null || string.IsNullOrWhiteSpace(req.Type))
            return ApiResponse<EqpMaintenanceRecord>.Fail(400, "维护保养类型不能为空");

        var record = new EqpMaintenanceRecord
        {
            EquipmentId = id,
            Type = req.Type,
            MaintenanceDate = string.IsNullOrWhiteSpace(req.MaintenanceDate) ? DateTime.Today.ToString("yyyy-MM-dd") : req.MaintenanceDate,
            Cost = req.Cost,
            Vendor = req.Vendor,
            EngineerName = req.EngineerName,
            Result = req.Result ?? "QUALIFIED",
            Remark = req.Remark,
            CreatedBy = CurrentUserId,
            UpdatedBy = CurrentUserId
        };

        await _maintRepo.InsertAsync(record, ct);

        // Update equipment status based on result
        if (record.Result == "FAILED")
        {
            eqp.Status = "OUT_OF_SERVICE";
        }
        else if (record.Result == "QUALIFIED" || record.Result == "REPAIRED")
        {
            eqp.Status = "NORMAL";
            if (eqp.IsMandatoryInspection == 1 && record.Type == "CALIBRATION")
            {
                // Advance next inspection date by 1 year
                eqp.NextInspectionDate = DateTime.Today.AddYears(1).ToString("yyyy-MM-dd");
            }
        }
        eqp.UpdatedBy = CurrentUserId;
        await _eqpRepo.UpdateAsync(eqp, ct);

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync("EQP_MAINTENANCE_RECORD", nameof(EqpMaintenanceRecord), record.Id,
                $"设备 [{eqp.Name}({eqp.Code})] 完成 [{record.Type}] 维保/强检：结果={record.Result}，花费={record.Cost:F2}元，工程师={record.EngineerName}",
                CurrentUserId, _session?.DisplayName, _session?.ActiveDeptId, ct);
        }

        return ApiResponse<EqpMaintenanceRecord>.Success(record, "维保与计量强检记录已归档更新");
    }

    [HttpGet("equipments/{id}/maintenance")]
    public async Task<ApiResponse<IReadOnlyList<EqpMaintenanceRecord>>> GetMaintenanceRecords(string id)
    {
        var list = await _maintRepo.QueryAsync(@"
SELECT * FROM eqp_maintenance_records
WHERE equipment_id = @EquipmentId
ORDER BY maintenance_date DESC, created_at_ms DESC", new { EquipmentId = id });

        return ApiResponse<IReadOnlyList<EqpMaintenanceRecord>>.Success(list);
    }

    [HttpGet("report")]
    public async Task<ApiResponse<EquipmentReportDto>> GetReport()
    {
        using var conn = _connFactory.CreateConnection();
        int total = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM eqp_equipments");
        int normal = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM eqp_equipments WHERE status = 'NORMAL'");
        int maint = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM eqp_equipments WHERE status = 'MAINTENANCE'");
        int outOfService = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM eqp_equipments WHERE status = 'OUT_OF_SERVICE'");
        decimal totalValue = await conn.ExecuteScalarAsync<decimal>("SELECT COALESCE(SUM(price), 0.0) FROM eqp_equipments");
        string today = DateTime.Today.ToString("yyyy-MM-dd");
        int pendingCalibration = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM eqp_equipments WHERE is_mandatory_inspection = 1 AND (next_inspection_date <= @Today OR status != 'NORMAL')",
            new { Today = today });

        var report = new EquipmentReportDto(
            TotalEquipmentCount: total,
            NormalCount: normal,
            MaintenanceCount: maint,
            OutOfServiceCount: outOfService,
            TotalAssetValue: totalValue,
            PendingCalibrationCount: pendingCalibration
        );

        return ApiResponse<EquipmentReportDto>.Success(report);
    }
}
