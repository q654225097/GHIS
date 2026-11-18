using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Consumables.Models;

namespace HIS.Modules.Consumables.Services;

public class ConsumableSupplyService
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IAuditTrailContract? _auditTrail;

    public ConsumableSupplyService(
        IDbConnectionFactory connFactory,
        IAuditTrailContract? auditTrail = null)
    {
        _connFactory = connFactory;
        _auditTrail = auditTrail;
    }

    public async Task<CsmConsumable> CreateConsumableAsync(CreateConsumableRequestDto req, string userId, string? displayName, CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();
        var existing = await conn.QuerySingleOrDefaultAsync<CsmConsumable>(
            "SELECT * FROM csm_consumables WHERE code = @Code", new { Code = req.Code.Trim() });
        if (existing != null)
        {
            throw new InvalidOperationException($"耗材编码 '{req.Code}' 已存在");
        }

        long now = SystemTime.UtcNowMs;
        var entity = new CsmConsumable
        {
            Id = "CSM-" + Guid.NewGuid().ToString("N")[..8].ToUpper(),
            Code = req.Code.Trim(),
            Name = req.Name.Trim(),
            Spec = req.Spec.Trim(),
            Unit = req.Unit.Trim(),
            Category = req.Category.Trim(),
            IsSterile = (short)(req.IsSterile ? 1 : 0),
            IsHighValue = (short)(req.IsHighValue ? 1 : 0),
            Price = req.Price,
            IsActive = 1,
            CreatedAtMs = now,
            CreatedBy = userId,
            UpdatedAtMs = now,
            UpdatedBy = userId,
            VersionNo = 1
        };

        const string sql = @"
INSERT INTO csm_consumables (
    id, code, name, spec, unit, category, is_sterile, is_high_value,
    price, is_active, ext_data_json, schema_version, created_at_ms, created_by,
    updated_at_ms, updated_by, version_no
) VALUES (
    @Id, @Code, @Name, @Spec, @Unit, @Category, @IsSterile, @IsHighValue,
    @Price, @IsActive, @ExtDataJson, @SchemaVersion, @CreatedAtMs, @CreatedBy,
    @UpdatedAtMs, @UpdatedBy, @VersionNo
)";
        await conn.ExecuteAsync(sql, entity);

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync("CSM_CONSUMABLE_CREATE", "CsmConsumable", entity.Id,
                $"新增耗材主数据 [{entity.Code}] {entity.Name} 规格:{entity.Spec} 单价:¥{entity.Price}", userId, displayName, ct: ct);
        }

        return entity;
    }

    public async Task<CsmConsumable> ToggleActiveAsync(string id, string userId, string? displayName, CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();
        var item = await conn.QuerySingleOrDefaultAsync<CsmConsumable>(
            "SELECT * FROM csm_consumables WHERE id = @Id", new { Id = id });
        if (item == null) throw new KeyNotFoundException("耗材不存在");

        short newStatus = (short)(item.IsActive == 1 ? 0 : 1);
        long now = SystemTime.UtcNowMs;
        await conn.ExecuteAsync(
            "UPDATE csm_consumables SET is_active = @Status, updated_at_ms = @Now, updated_by = @UserId, version_no = version_no + 1 WHERE id = @Id",
            new { Status = newStatus, Now = now, UserId = userId, Id = id });

        item.IsActive = newStatus;
        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync("CSM_CONSUMABLE_TOGGLE", "CsmConsumable", id,
                $"耗材 [{item.Code}] {item.Name} 状态变更为 {(newStatus == 1 ? "启用" : "停用")}", userId, displayName, ct: ct);
        }

        return item;
    }

    public async Task<CsmConsumable> AdjustPriceAsync(string id, decimal newPrice, string reason, string userId, string? displayName, CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();
        var item = await conn.QuerySingleOrDefaultAsync<CsmConsumable>(
            "SELECT * FROM csm_consumables WHERE id = @Id", new { Id = id });
        if (item == null) throw new KeyNotFoundException("耗材不存在");

        decimal oldPrice = item.Price;
        long now = SystemTime.UtcNowMs;
        await conn.ExecuteAsync(
            "UPDATE csm_consumables SET price = @Price, updated_at_ms = @Now, updated_by = @UserId, version_no = version_no + 1 WHERE id = @Id",
            new { Price = newPrice, Now = now, UserId = userId, Id = id });

        item.Price = newPrice;
        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync("CSM_PRICE_ADJUST", "CsmConsumable", id,
                $"耗材 [{item.Code}] {item.Name} 调价: 原价 ¥{oldPrice} -> 现价 ¥{newPrice}，原因: {reason}", userId, displayName, ct: ct);
        }

        return item;
    }

    public async Task<CsmBatch> StockInAsync(StockInRequestDto req, string userId, string? displayName, CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();
        var csm = await conn.QuerySingleOrDefaultAsync<CsmConsumable>(
            "SELECT * FROM csm_consumables WHERE id = @Id AND is_active = 1", new { Id = req.ConsumableId });
        if (csm == null) throw new KeyNotFoundException("耗材不存在或已停用");

        long now = SystemTime.UtcNowMs;
        string batchId = "BAT-" + Guid.NewGuid().ToString("N")[..8].ToUpper();
        var batch = new CsmBatch
        {
            Id = batchId,
            ConsumableId = csm.Id,
            BatchNo = req.BatchNo.Trim(),
            ExpiryDate = req.ExpiryDate.Trim(),
            ManufactureDate = req.ManufactureDate?.Trim(),
            Quantity = req.Quantity,
            UnitCost = req.UnitCost,
            IsFrozen = 0,
            CreatedAtMs = now,
            CreatedBy = userId,
            UpdatedAtMs = now,
            UpdatedBy = userId,
            VersionNo = 1
        };

        const string insertBatchSql = @"
INSERT INTO csm_batches (
    id, consumable_id, batch_no, expiry_date, manufacture_date,
    quantity, unit_cost, is_frozen, schema_version, created_at_ms,
    created_by, updated_at_ms, updated_by, version_no
) VALUES (
    @Id, @ConsumableId, @BatchNo, @ExpiryDate, @ManufactureDate,
    @Quantity, @UnitCost, @IsFrozen, @SchemaVersion, @CreatedAtMs,
    @CreatedBy, @UpdatedAtMs, @UpdatedBy, @VersionNo
)";
        await conn.ExecuteAsync(insertBatchSql, batch);

        // Update or insert stock
        var stock = await conn.QuerySingleOrDefaultAsync<CsmStock>(
            "SELECT * FROM csm_stocks WHERE consumable_id = @ConsumableId AND warehouse_id = @WarehouseId",
            new { ConsumableId = csm.Id, WarehouseId = req.WarehouseId });

        if (stock == null)
        {
            string stockId = "STK-" + Guid.NewGuid().ToString("N")[..8].ToUpper();
            const string insertStockSql = @"
INSERT INTO csm_stocks (
    id, consumable_id, warehouse_id, quantity, reserved_qty,
    schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no
) VALUES (
    @Id, @ConsumableId, @WarehouseId, @Quantity, 0,
    1, @Now, @UserId, @Now, @UserId, 1
)";
            await conn.ExecuteAsync(insertStockSql, new
            {
                Id = stockId,
                ConsumableId = csm.Id,
                WarehouseId = req.WarehouseId,
                Quantity = req.Quantity,
                Now = now,
                UserId = userId
            });
        }
        else
        {
            await conn.ExecuteAsync(
                "UPDATE csm_stocks SET quantity = quantity + @Qty, updated_at_ms = @Now, updated_by = @UserId, version_no = version_no + 1 WHERE id = @Id",
                new { Qty = req.Quantity, Now = now, UserId = userId, Id = stock.Id });
        }

        // Record movement
        string movementId = "MVT-" + Guid.NewGuid().ToString("N")[..8].ToUpper();
        const string insertMvtSql = @"
INSERT INTO csm_stock_movements (
    id, consumable_id, batch_id, from_warehouse_id, to_warehouse_id,
    movement_type, quantity, unit_price, reference_id, remark,
    schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no
) VALUES (
    @Id, @ConsumableId, @BatchId, NULL, @ToWarehouseId,
    'PURCHASE_IN', @Quantity, @UnitPrice, @ReferenceId, @Remark,
    1, @Now, @UserId, @Now, @UserId, 1
)";
        await conn.ExecuteAsync(insertMvtSql, new
        {
            Id = movementId,
            ConsumableId = csm.Id,
            BatchId = batchId,
            ToWarehouseId = req.WarehouseId,
            Quantity = req.Quantity,
            UnitPrice = req.UnitCost,
            ReferenceId = req.BatchNo,
            Remark = $"采购入库 批号:{req.BatchNo}",
            Now = now,
            UserId = userId
        });

        // If high value, generate trace codes for tracking
        if (csm.IsHighValue == 1)
        {
            for (int i = 1; i <= Math.Min(req.Quantity, 50); i++)
            {
                string traceCode = $"{csm.Code}-{req.BatchNo}-{i:D4}";
                await conn.ExecuteAsync(@"
INSERT INTO csm_trace_codes (
    id, consumable_id, trace_code, batch_no, status,
    schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no
) VALUES (
    @Id, @ConsumableId, @TraceCode, @BatchNo, 'IN_STOCK',
    1, @Now, @UserId, @Now, @UserId, 1
)", new
                {
                    Id = "TRC-" + Guid.NewGuid().ToString("N")[..8].ToUpper(),
                    ConsumableId = csm.Id,
                    TraceCode = traceCode,
                    BatchNo = req.BatchNo,
                    Now = now,
                    UserId = userId
                });
            }
        }

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync("CSM_STOCK_IN", "CsmStock", batchId,
                $"耗材 [{csm.Code}] {csm.Name} 采购入库 数量: {req.Quantity}{csm.Unit} 批号: {req.BatchNo} 库房: {req.WarehouseId}", userId, displayName, ct: ct);
        }

        return batch;
    }

    public async Task<bool> FreezeBatchAsync(string batchId, bool isFrozen, string userId, string? displayName, CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();
        var batch = await conn.QuerySingleOrDefaultAsync<CsmBatch>(
            "SELECT * FROM csm_batches WHERE id = @Id", new { Id = batchId });
        if (batch == null) throw new KeyNotFoundException("批次不存在");

        short freezeVal = (short)(isFrozen ? 1 : 0);
        long now = SystemTime.UtcNowMs;
        await conn.ExecuteAsync(
            "UPDATE csm_batches SET is_frozen = @IsFrozen, updated_at_ms = @Now, updated_by = @UserId, version_no = version_no + 1 WHERE id = @Id",
            new { IsFrozen = freezeVal, Now = now, UserId = userId, Id = batchId });

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync("CSM_BATCH_FREEZE", "CsmBatch", batchId,
                $"耗材批次 [{batch.BatchNo}] 状态设置为: {(isFrozen ? "冻结" : "解冻")}", userId, displayName, ct: ct);
        }
        return true;
    }

    public async Task<CsmRequisition> CreateRequisitionAsync(CreateRequisitionRequestDto req, string userId, string? displayName, CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();
        long now = SystemTime.UtcNowMs;
        string reqId = "REQ-" + Guid.NewGuid().ToString("N")[..8].ToUpper();
        string reqNo = "CRQ" + DateTime.UtcNow.ToString("yyMMddHHmmssfff");

        var entity = new CsmRequisition
        {
            Id = reqId,
            RequisitionNo = reqNo,
            WardId = req.WardId.Trim(),
            DeptId = req.DeptId.Trim(),
            Status = "PENDING",
            RequestedBy = userId,
            Remark = req.Remark?.Trim(),
            CreatedAtMs = now,
            CreatedBy = userId,
            UpdatedAtMs = now,
            UpdatedBy = userId,
            VersionNo = 1
        };

        const string insertReqSql = @"
INSERT INTO csm_requisitions (
    id, requisition_no, ward_id, dept_id, status, requested_by,
    remark, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no
) VALUES (
    @Id, @RequisitionNo, @WardId, @DeptId, @Status, @RequestedBy,
    @Remark, 1, @CreatedAtMs, @CreatedBy, @UpdatedAtMs, @UpdatedBy, 1
)";
        await conn.ExecuteAsync(insertReqSql, entity);

        foreach (var item in req.Items)
        {
            var csm = await conn.QuerySingleOrDefaultAsync<CsmConsumable>(
                "SELECT * FROM csm_consumables WHERE id = @Id", new { Id = item.ConsumableId });
            decimal price = csm?.Price ?? 0m;

            string lineId = "RLN-" + Guid.NewGuid().ToString("N")[..8].ToUpper();
            const string insertLineSql = @"
INSERT INTO csm_requisition_lines (
    id, requisition_id, consumable_id, quantity, issued_quantity,
    unit_price, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no
) VALUES (
    @Id, @RequisitionId, @ConsumableId, @Quantity, 0,
    @UnitPrice, 1, @Now, @UserId, @Now, @UserId, 1
)";
            await conn.ExecuteAsync(insertLineSql, new
            {
                Id = lineId,
                RequisitionId = reqId,
                ConsumableId = item.ConsumableId,
                Quantity = item.Quantity,
                UnitPrice = price,
                Now = now,
                UserId = userId
            });
        }

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync("CSM_REQUISITION_CREATE", "CsmRequisition", reqId,
                $"科室/病区 [{req.WardId}] 提交耗材请领单 {reqNo}，明细项数: {req.Items.Count}", userId, displayName, ct: ct);
        }

        return entity;
    }

    public async Task<CsmRequisition> ApproveRequisitionAsync(string requisitionId, string userId, string? displayName, CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();
        var req = await conn.QuerySingleOrDefaultAsync<CsmRequisition>(
            "SELECT * FROM csm_requisitions WHERE id = @Id", new { Id = requisitionId });
        if (req == null) throw new KeyNotFoundException("请领单不存在");
        if (req.Status != "PENDING") throw new InvalidOperationException($"仅待审批单据可审批，当前状态: {req.Status}");

        long now = SystemTime.UtcNowMs;
        await conn.ExecuteAsync(
            "UPDATE csm_requisitions SET status = 'APPROVED', approved_by = @UserId, updated_at_ms = @Now, updated_by = @UserId, version_no = version_no + 1 WHERE id = @Id",
            new { UserId = userId, Now = now, Id = requisitionId });

        req.Status = "APPROVED";
        req.ApprovedBy = userId;

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync("CSM_REQUISITION_APPROVE", "CsmRequisition", requisitionId,
                $"审批耗材请领单 {req.RequisitionNo}", userId, displayName, ct: ct);
        }

        return req;
    }

    public async Task<CsmRequisition> IssueRequisitionAsync(string requisitionId, string warehouseId, string userId, string? displayName, CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();
        var req = await conn.QuerySingleOrDefaultAsync<CsmRequisition>(
            "SELECT * FROM csm_requisitions WHERE id = @Id", new { Id = requisitionId });
        if (req == null) throw new KeyNotFoundException("请领单不存在");
        if (req.Status != "APPROVED") throw new InvalidOperationException($"仅已审批单据可发放，当前状态: {req.Status}");

        var lines = (await conn.QueryAsync<CsmRequisitionLine>(
            "SELECT * FROM csm_requisition_lines WHERE requisition_id = @ReqId", new { ReqId = requisitionId })).ToList();

        long now = SystemTime.UtcNowMs;

        // Verify stock for all lines
        foreach (var line in lines)
        {
            var stock = await conn.QuerySingleOrDefaultAsync<CsmStock>(
                "SELECT * FROM csm_stocks WHERE consumable_id = @CsmId AND warehouse_id = @WhId",
                new { CsmId = line.ConsumableId, WhId = warehouseId });
            if (stock == null || stock.Quantity < line.Quantity)
            {
                throw new InvalidOperationException($"库房库存不足，无法发放耗材 {line.ConsumableId} (当前库存: {stock?.Quantity ?? 0})");
            }
        }

        // Deduct stock and record movements
        foreach (var line in lines)
        {
            await conn.ExecuteAsync(
                "UPDATE csm_stocks SET quantity = quantity - @Qty, updated_at_ms = @Now, updated_by = @UserId, version_no = version_no + 1 WHERE consumable_id = @CsmId AND warehouse_id = @WhId",
                new { Qty = line.Quantity, Now = now, UserId = userId, CsmId = line.ConsumableId, WhId = warehouseId });

            await conn.ExecuteAsync(
                "UPDATE csm_requisition_lines SET issued_quantity = @Qty, updated_at_ms = @Now, updated_by = @UserId, version_no = version_no + 1 WHERE id = @Id",
                new { Qty = line.Quantity, Now = now, UserId = userId, Id = line.Id });

            string mvtId = "MVT-" + Guid.NewGuid().ToString("N")[..8].ToUpper();
            const string insertMvtSql = @"
INSERT INTO csm_stock_movements (
    id, consumable_id, batch_id, from_warehouse_id, to_warehouse_id,
    movement_type, quantity, unit_price, reference_id, remark,
    schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no
) VALUES (
    @Id, @ConsumableId, NULL, @FromWarehouseId, @ToWarehouseId,
    'DEPT_ISSUE', @Quantity, @UnitPrice, @ReferenceId, @Remark,
    1, @Now, @UserId, @Now, @UserId, 1
)";
            await conn.ExecuteAsync(insertMvtSql, new
            {
                Id = mvtId,
                ConsumableId = line.ConsumableId,
                FromWarehouseId = warehouseId,
                ToWarehouseId = req.WardId,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                ReferenceId = req.RequisitionNo,
                Remark = $"请领出库至病区 {req.WardId}",
                Now = now,
                UserId = userId
            });
        }

        await conn.ExecuteAsync(
            "UPDATE csm_requisitions SET status = 'ISSUED', issued_by = @UserId, updated_at_ms = @Now, updated_by = @UserId, version_no = version_no + 1 WHERE id = @Id",
            new { UserId = userId, Now = now, Id = requisitionId });

        req.Status = "ISSUED";
        req.IssuedBy = userId;

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync("CSM_REQUISITION_ISSUE", "CsmRequisition", requisitionId,
                $"库房 [{warehouseId}] 发放耗材请领单 {req.RequisitionNo} 至病区 [{req.WardId}]", userId, displayName, ct: ct);
        }

        return req;
    }

    public async Task<bool> TransferStockAsync(TransferConsumableRequestDto req, string userId, string? displayName, CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();
        var srcStock = await conn.QuerySingleOrDefaultAsync<CsmStock>(
            "SELECT * FROM csm_stocks WHERE consumable_id = @CsmId AND warehouse_id = @WhId",
            new { CsmId = req.ConsumableId, WhId = req.FromWarehouseId });
        if (srcStock == null || srcStock.Quantity < req.Quantity)
        {
            throw new InvalidOperationException("调出库房库存不足");
        }

        long now = SystemTime.UtcNowMs;
        // Deduct source
        await conn.ExecuteAsync(
            "UPDATE csm_stocks SET quantity = quantity - @Qty, updated_at_ms = @Now, updated_by = @UserId, version_no = version_no + 1 WHERE id = @Id",
            new { Qty = req.Quantity, Now = now, UserId = userId, Id = srcStock.Id });

        // Add target
        var targetStock = await conn.QuerySingleOrDefaultAsync<CsmStock>(
            "SELECT * FROM csm_stocks WHERE consumable_id = @CsmId AND warehouse_id = @WhId",
            new { CsmId = req.ConsumableId, WhId = req.ToWarehouseId });

        if (targetStock == null)
        {
            string stockId = "STK-" + Guid.NewGuid().ToString("N")[..8].ToUpper();
            await conn.ExecuteAsync(@"
INSERT INTO csm_stocks (id, consumable_id, warehouse_id, quantity, reserved_qty, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES (@Id, @ConsumableId, @WarehouseId, @Quantity, 0, 1, @Now, @UserId, @Now, @UserId, 1)",
                new { Id = stockId, ConsumableId = req.ConsumableId, WarehouseId = req.ToWarehouseId, Quantity = req.Quantity, Now = now, UserId = userId });
        }
        else
        {
            await conn.ExecuteAsync(
                "UPDATE csm_stocks SET quantity = quantity + @Qty, updated_at_ms = @Now, updated_by = @UserId, version_no = version_no + 1 WHERE id = @Id",
                new { Qty = req.Quantity, Now = now, UserId = userId, Id = targetStock.Id });
        }

        // Record movement
        string mvtId = "MVT-" + Guid.NewGuid().ToString("N")[..8].ToUpper();
        await conn.ExecuteAsync(@"
INSERT INTO csm_stock_movements (id, consumable_id, batch_id, from_warehouse_id, to_warehouse_id, movement_type, quantity, unit_price, reference_id, remark, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES (@Id, @ConsumableId, NULL, @FromWarehouseId, @ToWarehouseId, 'WAREHOUSE_TRANSFER', @Quantity, 0, NULL, @Remark, 1, @Now, @UserId, @Now, @UserId, 1)",
            new { Id = mvtId, ConsumableId = req.ConsumableId, FromWarehouseId = req.FromWarehouseId, ToWarehouseId = req.ToWarehouseId, Quantity = req.Quantity, Remark = req.Remark ?? "库房调拨", Now = now, UserId = userId });

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync("CSM_STOCK_TRANSFER", "CsmStock", req.ConsumableId,
                $"库间调拨 耗材 {req.ConsumableId}: 从 [{req.FromWarehouseId}] 调往 [{req.ToWarehouseId}] 数量: {req.Quantity}", userId, displayName, ct: ct);
        }

        return true;
    }

    public async Task<CsmSterilePackage> CreateSterilePackageAsync(CreateSterilePackageRequestDto req, string userId, string? displayName, CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();
        long now = SystemTime.UtcNowMs;
        string id = "PKG-" + Guid.NewGuid().ToString("N")[..8].ToUpper();
        string pkgCode = "STP" + DateTime.UtcNow.ToString("yyMMddHHmmssfff");

        var pkg = new CsmSterilePackage
        {
            Id = id,
            PackageCode = pkgCode,
            PackageName = req.PackageName.Trim(),
            ContentsJson = JsonSerializer.Serialize(req.Contents),
            Status = "CREATED",
            CurrentLocation = req.CurrentLocation,
            CreatedAtMs = now,
            CreatedBy = userId,
            UpdatedAtMs = now,
            UpdatedBy = userId,
            VersionNo = 1
        };

        const string sql = @"
INSERT INTO csm_sterile_packages (
    id, package_code, package_name, contents_json, status, current_location,
    schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no
) VALUES (
    @Id, @PackageCode, @PackageName, @ContentsJson, @Status, @CurrentLocation,
    1, @CreatedAtMs, @CreatedBy, @UpdatedAtMs, @UpdatedBy, 1
)";
        await conn.ExecuteAsync(sql, pkg);

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync("CSM_STERILE_PKG_CREATE", "CsmSterilePackage", id,
                $"供应室组装灭菌包 [{pkgCode}] {pkg.PackageName} 包含物品数: {req.Contents.Count}", userId, displayName, ct: ct);
        }

        return pkg;
    }

    public async Task<CsmSterilePackage> SterilizePackageAsync(string packageId, SterilizePackageRequestDto req, string userId, string? displayName, CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();
        var pkg = await conn.QuerySingleOrDefaultAsync<CsmSterilePackage>(
            "SELECT * FROM csm_sterile_packages WHERE id = @Id", new { Id = packageId });
        if (pkg == null) throw new KeyNotFoundException("灭菌包不存在");

        long now = SystemTime.UtcNowMs;
        long validUntil = now + (req.ValidDays * 86400000L);

        // Record sterilization batch
        string batchId = "STB-" + Guid.NewGuid().ToString("N")[..8].ToUpper();
        await conn.ExecuteAsync(@"
INSERT INTO csm_sterilization_batches (
    id, batch_no, method, operator_id, started_at_ms, completed_at_ms, result, remark,
    schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no
) VALUES (
    @Id, @BatchNo, @Method, @UserId, @Now, @Now, 'QUALIFIED', '灭菌合格',
    1, @Now, @UserId, @Now, @UserId, 1
)", new { Id = batchId, BatchNo = req.SterilizationBatchNo, Method = req.Method, UserId = userId, Now = now });

        await conn.ExecuteAsync(@"
UPDATE csm_sterile_packages
SET status = 'STERILIZED', sterilization_batch_no = @BatchNo, valid_until_ms = @ValidUntil,
    updated_at_ms = @Now, updated_by = @UserId, version_no = version_no + 1
WHERE id = @Id", new { BatchNo = req.SterilizationBatchNo, ValidUntil = validUntil, Now = now, UserId = userId, Id = packageId });

        pkg.Status = "STERILIZED";
        pkg.SterilizationBatchNo = req.SterilizationBatchNo;
        pkg.ValidUntilMs = validUntil;

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync("CSM_PACKAGE_STERILIZE", "CsmSterilePackage", packageId,
                $"灭菌包 [{pkg.PackageCode}] 完成灭菌 批号: {req.SterilizationBatchNo} 方式: {req.Method} 有效期至: {DateTimeOffset.FromUnixTimeMilliseconds(validUntil):yyyy-MM-dd}", userId, displayName, ct: ct);
        }

        return pkg;
    }

    public async Task<CsmSterilePackage> IssueSterilePackageAsync(string packageId, IssueSterilePackageRequestDto req, string userId, string? displayName, CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();
        var pkg = await conn.QuerySingleOrDefaultAsync<CsmSterilePackage>(
            "SELECT * FROM csm_sterile_packages WHERE id = @Id", new { Id = packageId });
        if (pkg == null) throw new KeyNotFoundException("灭菌包不存在");
        if (pkg.Status != "STERILIZED") throw new InvalidOperationException($"仅已灭菌包可发放，当前状态: {pkg.Status}");

        long now = SystemTime.UtcNowMs;
        string fromLoc = pkg.CurrentLocation;
        string toLoc = req.TargetLocation;

        await conn.ExecuteAsync(@"
UPDATE csm_sterile_packages
SET status = 'ISSUED', current_location = @ToLoc, updated_at_ms = @Now, updated_by = @UserId, version_no = version_no + 1
WHERE id = @Id", new { ToLoc = toLoc, Now = now, UserId = userId, Id = packageId });

        // Record movement
        string mvtId = "PMV-" + Guid.NewGuid().ToString("N")[..8].ToUpper();
        await conn.ExecuteAsync(@"
INSERT INTO csm_package_movements (
    id, package_id, from_location, to_location, movement_type, operator_id, remark,
    schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no
) VALUES (
    @Id, @PackageId, @FromLoc, @ToLoc, 'ISSUE', @UserId, @Remark,
    1, @Now, @UserId, @Now, @UserId, 1
)", new { Id = mvtId, PackageId = packageId, FromLoc = fromLoc, ToLoc = toLoc, UserId = userId, Remark = req.Remark ?? "供应室发放出库", Now = now });

        pkg.Status = "ISSUED";
        pkg.CurrentLocation = toLoc;

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync("CSM_PACKAGE_ISSUE", "CsmSterilePackage", packageId,
                $"供应室发放灭菌包 [{pkg.PackageCode}] 至 [{toLoc}]", userId, displayName, ct: ct);
        }

        return pkg;
    }

    public async Task<CsmSterilePackage> ReturnSterilePackageAsync(string packageId, ReturnSterilePackageRequestDto req, string userId, string? displayName, CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();
        var pkg = await conn.QuerySingleOrDefaultAsync<CsmSterilePackage>(
            "SELECT * FROM csm_sterile_packages WHERE id = @Id", new { Id = packageId });
        if (pkg == null) throw new KeyNotFoundException("灭菌包不存在");

        long now = SystemTime.UtcNowMs;
        string fromLoc = pkg.CurrentLocation;
        string toLoc = req.TargetLocation;

        await conn.ExecuteAsync(@"
UPDATE csm_sterile_packages
SET status = 'RETURNED', current_location = @ToLoc, updated_at_ms = @Now, updated_by = @UserId, version_no = version_no + 1
WHERE id = @Id", new { ToLoc = toLoc, Now = now, UserId = userId, Id = packageId });

        string mvtId = "PMV-" + Guid.NewGuid().ToString("N")[..8].ToUpper();
        await conn.ExecuteAsync(@"
INSERT INTO csm_package_movements (
    id, package_id, from_location, to_location, movement_type, operator_id, remark,
    schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no
) VALUES (
    @Id, @PackageId, @FromLoc, @ToLoc, 'RETURN', @UserId, @Remark,
    1, @Now, @UserId, @Now, @UserId, 1
)", new { Id = mvtId, PackageId = packageId, FromLoc = fromLoc, ToLoc = toLoc, UserId = userId, Remark = req.Reason, Now = now });

        pkg.Status = "RETURNED";
        pkg.CurrentLocation = toLoc;

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync("CSM_PACKAGE_RETURN", "CsmSterilePackage", packageId,
                $"灭菌包 [{pkg.PackageCode}] 回收至 [{toLoc}]，原因: {req.Reason}", userId, displayName, ct: ct);
        }

        return pkg;
    }

    public async Task<CsmTraceCode> ScanUseTraceCodeAsync(ScanUseTraceCodeRequestDto req, string userId, string? displayName, CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();
        var trace = await conn.QuerySingleOrDefaultAsync<CsmTraceCode>(
            "SELECT * FROM csm_trace_codes WHERE trace_code = @Code", new { Code = req.TraceCode.Trim() });
        if (trace == null) throw new KeyNotFoundException("追溯码不存在");
        if (trace.Status != "IN_STOCK") throw new InvalidOperationException($"该追溯码不可使用，当前状态: {trace.Status}");

        long now = SystemTime.UtcNowMs;
        await conn.ExecuteAsync(@"
UPDATE csm_trace_codes
SET patient_id = @PatientId, encounter_id = @EncounterId, status = 'USED',
    used_at_ms = @Now, used_by = @UserId, updated_at_ms = @Now, updated_by = @UserId, version_no = version_no + 1
WHERE id = @Id", new { PatientId = req.PatientId, EncounterId = req.EncounterId, Now = now, UserId = userId, Id = trace.Id });

        trace.PatientId = req.PatientId;
        trace.EncounterId = req.EncounterId;
        trace.Status = "USED";
        trace.UsedAtMs = now;
        trace.UsedBy = userId;

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync("CSM_TRACE_USE", "CsmTraceCode", trace.Id,
                $"高值耗材扫码使用: 追溯码 [{trace.TraceCode}] 用于患者 [{req.PatientId}] 住院号 [{req.EncounterId}]", userId, displayName, ct: ct);
        }

        return trace;
    }

    public async Task<CsmTraceCode> ScanRecallTraceCodeAsync(ScanRecallTraceCodeRequestDto req, string userId, string? displayName, CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();
        var trace = await conn.QuerySingleOrDefaultAsync<CsmTraceCode>(
            "SELECT * FROM csm_trace_codes WHERE trace_code = @Code", new { Code = req.TraceCode.Trim() });
        if (trace == null) throw new KeyNotFoundException("追溯码不存在");

        long now = SystemTime.UtcNowMs;
        await conn.ExecuteAsync(@"
UPDATE csm_trace_codes
SET status = 'RECALLED', recalled_at_ms = @Now, recall_reason = @Reason,
    updated_at_ms = @Now, updated_by = @UserId, version_no = version_no + 1
WHERE id = @Id", new { Now = now, Reason = req.RecallReason, UserId = userId, Id = trace.Id });

        trace.Status = "RECALLED";
        trace.RecalledAtMs = now;
        trace.RecallReason = req.RecallReason;

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync("CSM_TRACE_RECALL", "CsmTraceCode", trace.Id,
                $"耗材追溯召回: 追溯码 [{trace.TraceCode}] 批号 [{trace.BatchNo}] 原因: {req.RecallReason}", userId, displayName, ct: ct);
        }

        return trace;
    }

    public async Task<ConsumableFinanceReportDto> GetFinanceReportAsync(CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();
        int totalConsumables = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM csm_consumables");
        int totalStockQty = await conn.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0) FROM csm_stocks");
        decimal totalValuation = await conn.ExecuteScalarAsync<decimal>(@"
SELECT COALESCE(SUM(s.quantity * c.price), 0)
FROM csm_stocks s
JOIN csm_consumables c ON c.id = s.consumable_id");
        int requisitionsCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM csm_requisitions");
        int totalSterilePkgs = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM csm_sterile_packages");
        int usedHighValue = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM csm_trace_codes WHERE status = 'USED'");

        return new ConsumableFinanceReportDto(
            TotalConsumablesCount: totalConsumables,
            TotalStockQuantity: totalStockQty,
            TotalStockValuation: totalValuation,
            RequisitionsCount: requisitionsCount,
            TotalSterilePackages: totalSterilePkgs,
            TotalUsedHighValueItems: usedHighValue
        );
    }
}
