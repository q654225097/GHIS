using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Consumables.Controllers;

/// <summary>
/// 手术高值耗材 UDI 全流程闭环（GS1 解析入库 -> 手术扫码消耗自动扣库存+写 fin_charge_ledgers 记账 -> 供应商代销月结对账）：
/// 1) 支持 GS1 UDI-DI/PI 解析，防重复入库与过期拦截
/// 2) 一物一码消耗与住院计费强绑定（单事务写 fin_charge_ledgers 并回填 charge_ledger_id）
/// 3) 供应商代销月结对账单自动汇总与核销
/// </summary>
[ApiController]
[Route("api/v4/udi")]
public sealed class UdiConsumablesController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _session;

    public UdiConsumablesController(IDbConnectionFactory connFactory, IUserSession? session = null)
    {
        _connFactory = connFactory;
        _session = session;
    }

    private string ActorId => _session?.UserId?.Trim() ?? "SYSTEM";
    private string ActorName => _session?.DisplayName ?? _session?.UserName ?? ActorId;

    public sealed record ReceiveUdiDto(
        string UdiFull,
        string MaterialCode,
        string MaterialName,
        string? Spec,
        string SupplierName,
        bool IsConsignment,
        decimal CostPrice,
        decimal UnitPrice,
        int? OverrideExpiryDateInt);

    public sealed record ConsumeUdiDto(
        string UdiFull,
        string EncounterId,
        string PatientId,
        string? SurgeryId,
        string? OrderDeptId,
        string? ExecDeptId);

    public sealed record GenerateStatementDto(string SupplierName, string PeriodYm);

    private static (string Di, int ExpiryDateInt, string? Lot, string? Serial) ParseGs1Udi(string udiFull, int? overrideExpiry)
    {
        string di = Regex.Match(udiFull, @"\(01\)(\d{14})").Groups[1].Value;
        string exp6 = Regex.Match(udiFull, @"\(17\)(\d{6})").Groups[1].Value;
        string lot = Regex.Match(udiFull, @"\(10\)([^(]+)").Groups[1].Value;
        string serial = Regex.Match(udiFull, @"\(21\)([^(]+)").Groups[1].Value;

        if (string.IsNullOrWhiteSpace(di))
            di = udiFull.Length >= 14 ? udiFull[..14] : udiFull;

        int expInt = overrideExpiry ?? (exp6.Length == 6 ? int.Parse("20" + exp6) : int.Parse(DateTime.UtcNow.AddYears(2).ToString("yyyyMMdd")));
        return (di, expInt, string.IsNullOrWhiteSpace(lot) ? null : lot.Trim(), string.IsNullOrWhiteSpace(serial) ? null : serial.Trim());
    }

    [HttpGet("inventory")]
    public async Task<IActionResult> ListInventory([FromQuery] string? status = null, [FromQuery] string? supplierName = null, CancellationToken ct = default)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var sql = "SELECT * FROM udi_implant_inventory WHERE 1=1";
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(status))
        {
            sql += " AND status = @Status";
            p.Add("Status", status.Trim().ToUpperInvariant());
        }
        if (!string.IsNullOrWhiteSpace(supplierName))
        {
            sql += " AND supplier_name = @Sup";
            p.Add("Sup", supplierName.Trim());
        }
        sql += " ORDER BY created_at_ms DESC LIMIT 200";
        return Ok(await conn.QueryAsync(sql, p));
    }

    [HttpPost("inventory/receive")]
    public async Task<IActionResult> ReceiveUdi([FromBody] ReceiveUdiDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.UdiFull) || string.IsNullOrWhiteSpace(dto.MaterialName) || string.IsNullOrWhiteSpace(dto.SupplierName))
            return BadRequest(new { message = "UDI 条码、耗材名称与供应商不能为空。" });
        if (dto.UnitPrice <= 0 || dto.CostPrice < 0)
            return BadRequest(new { message = "高值耗材计费单价必须大于 0。" });

        var udi = dto.UdiFull.Trim();
        var (di, expInt, lot, serial) = ParseGs1Udi(udi, dto.OverrideExpiryDateInt);
        int todayInt = int.Parse(DateTime.UtcNow.ToString("yyyyMMdd"));
        if (expInt < todayInt)
            return BadRequest(new { message = $"该高值耗材效期({expInt})已过期，禁止入库！" });

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var dup = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM udi_implant_inventory WHERE udi_full = @Udi", new { Udi = udi });
        if (dup > 0)
            return BadRequest(new { message = $"UDI 唯一码 [{udi}] 已存在，严禁重复入库。" });

        var mdmConsumable = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT code AS Code, name AS Name, spec AS Spec, is_active AS IsActive FROM csm_consumables WHERE udi_di = @Di OR code = @Code LIMIT 1",
            new { Di = di, Code = dto.MaterialCode?.Trim() ?? string.Empty });
        if (mdmConsumable != null && Convert.ToInt32(mdmConsumable.IsActive) == 0)
            return BadRequest(new { message = $"耗材主数据 [{mdmConsumable.Code}] 已停用，禁止 UDI 入库！" });

        string resolvedCode = !string.IsNullOrWhiteSpace(dto.MaterialCode) ? dto.MaterialCode.Trim()
            : (mdmConsumable != null ? (string)mdmConsumable.Code : di);
        string resolvedName = !string.IsNullOrWhiteSpace(dto.MaterialName) ? dto.MaterialName.Trim()
            : (mdmConsumable != null ? (string)mdmConsumable.Name : dto.MaterialName.Trim());
        string? resolvedSpec = !string.IsNullOrWhiteSpace(dto.Spec) ? dto.Spec
            : (mdmConsumable != null ? (string?)mdmConsumable.Spec : dto.Spec);

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var id = Guid.NewGuid().ToString("N");
        await conn.ExecuteAsync(@"
INSERT INTO udi_implant_inventory (
    id, udi_full, udi_di, udi_pi_lot, udi_pi_serial, expiry_date_int,
    material_code, material_name, spec, supplier_name, is_consignment,
    cost_price, unit_price, status, created_at_ms, updated_at_ms
) VALUES (
    @Id, @Udi, @Di, @Lot, @Serial, @Exp,
    @Code, @Name, @Spec, @Sup, @Consign,
    @Cost, @Price, 'IN_STOCK', @Now, @Now
);", new
        {
            Id = id,
            Udi = udi,
            Di = di,
            Lot = lot,
            Serial = serial,
            Exp = expInt,
            Code = resolvedCode,
            Name = resolvedName,
            Spec = resolvedSpec,
            Sup = dto.SupplierName.Trim(),
            Consign = dto.IsConsignment ? 1 : 0,
            Cost = dto.CostPrice,
            Price = dto.UnitPrice,
            Now = now
        });

        return Ok(new { id, udiFull = udi, udiDi = di, udiPiLot = lot, udiPiSerial = serial, expiryDateInt = expInt, status = "IN_STOCK" });
    }

    [HttpPost("inventory/consume")]
    public async Task<IActionResult> ConsumeAndBillUdi([FromBody] ConsumeUdiDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.UdiFull) || string.IsNullOrWhiteSpace(dto.EncounterId) || string.IsNullOrWhiteSpace(dto.PatientId))
            return BadRequest(new { message = "UDI 唯一码、住院就诊ID与患者ID不能为空。" });

        var udi = dto.UdiFull.Trim();
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var item = await conn.QueryFirstOrDefaultAsync("SELECT * FROM udi_implant_inventory WHERE udi_full = @Udi", new { Udi = udi });
        if (item == null) return NotFound(new { message = $"未找到 UDI 耗材台账：{udi}" });
        if ((string)item.status != "IN_STOCK")
            return BadRequest(new { message = $"该高值耗材当前状态为 {item.status}，无法重复消耗记账。" });

        int todayInt = int.Parse(DateTime.UtcNow.ToString("yyyyMMdd"));
        if (Convert.ToInt32(item.expiry_date_int) < todayInt)
            return BadRequest(new { message = $"该高值耗材已过效期 ({item.expiry_date_int})，严禁临床使用！" });

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var ledgerId = Guid.NewGuid().ToString("N");
        decimal unitPrice = Convert.ToDecimal(item.unit_price);

        await using var tx = await conn.BeginTransactionAsync(ct);

        // 1) 原子写入住院费用台账 fin_charge_ledgers
        await conn.ExecuteAsync(@"
INSERT INTO fin_charge_ledgers (
    id, patient_id, visit_or_encounter_id, order_dept_id, patient_ward_id, exec_dept_id,
    item_code, item_name, quantity, unit_price, total_amount, status,
    ext_data_json, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no
) VALUES (
    @LedgerId, @PatId, @EncId, @OrderDept, @OrderDept, @ExecDept,
    @ItemCode, @ItemName, 1, @UnitPrice, @UnitPrice, 'UNSETTLED',
    @ExtJson, 1, @Now, @Actor, @Now, @Actor, 1
);", new
        {
            LedgerId = ledgerId,
            PatId = dto.PatientId.Trim(),
            EncId = dto.EncounterId.Trim(),
            OrderDept = dto.OrderDeptId ?? "OR-DEPT",
            ExecDept = dto.ExecDeptId ?? "OR-DEPT",
            ItemCode = (string)item.material_code,
            ItemName = $"{item.material_name} [UDI:{item.udi_di}]",
            UnitPrice = unitPrice,
            ExtJson = $"{{\"udi_full\":\"{udi}\",\"surgery_id\":\"{dto.SurgeryId ?? ""}\"}}",
            Now = now,
            Actor = ActorId
        }, tx);

        // 2) 原子更新高值耗材状态为 CONSUMED 并绑定记账单号
        await conn.ExecuteAsync(@"
UPDATE udi_implant_inventory
SET status = 'CONSUMED',
    encounter_id = @EncId,
    patient_id = @PatId,
    surgery_id = @SurgId,
    charge_ledger_id = @LedgerId,
    consumed_at_ms = @Now,
    consumed_by = @ActorName,
    updated_at_ms = @Now
WHERE id = @Id AND status = 'IN_STOCK';", new
        {
            EncId = dto.EncounterId.Trim(),
            PatId = dto.PatientId.Trim(),
            SurgId = dto.SurgeryId,
            LedgerId = ledgerId,
            Now = now,
            ActorName,
            Id = (string)item.id
        }, tx);

        await tx.CommitAsync(ct);

        return Ok(new
        {
            udiInventoryId = (string)item.id,
            udiFull = udi,
            status = "CONSUMED",
            chargeLedgerId = ledgerId,
            billedAmount = unitPrice
        });
    }

    [HttpPost("consignment/statements/generate")]
    public async Task<IActionResult> GenerateConsignmentStatement([FromBody] GenerateStatementDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.SupplierName) || string.IsNullOrWhiteSpace(dto.PeriodYm))
            return BadRequest(new { message = "供应商名称与对账账期(YYYY-MM)不能为空。" });

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var items = (await conn.QueryAsync(@"
SELECT id, cost_price, unit_price
FROM udi_implant_inventory
WHERE supplier_name = @Sup AND is_consignment = 1 AND status = 'CONSUMED' AND statement_id IS NULL;",
            new { Sup = dto.SupplierName.Trim() })).ToList();

        if (items.Count == 0)
            return BadRequest(new { message = $"供应商 [{dto.SupplierName}] 当前没有待对账的已消耗代销高值耗材。" });

        decimal totalCost = 0m;
        decimal totalCharge = 0m;
        foreach (var x in items)
        {
            totalCost += Convert.ToDecimal((object)x.cost_price);
            totalCharge += Convert.ToDecimal((object)x.unit_price);
        }
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var stmtId = Guid.NewGuid().ToString("N");
        var stmtNo = $"CS{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(10, 99)}";

        await using var tx = await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(@"
INSERT INTO udi_consignment_statements (
    id, statement_no, supplier_name, period_ym, item_count, total_cost_amount, total_charge_amount,
    status, created_at_ms, updated_at_ms
) VALUES (
    @Id, @No, @Sup, @Period, @Cnt, @Cost, @Charge, 'DRAFT', @Now, @Now
);", new
        {
            Id = stmtId,
            No = stmtNo,
            Sup = dto.SupplierName.Trim(),
            Period = dto.PeriodYm.Trim(),
            Cnt = items.Count,
            Cost = totalCost,
            Charge = totalCharge,
            Now = now
        }, tx);

        await conn.ExecuteAsync(@"
UPDATE udi_implant_inventory
SET statement_id = @StmtId, status = 'RECONCILED', updated_at_ms = @Now
WHERE supplier_name = @Sup AND is_consignment = 1 AND status = 'CONSUMED' AND statement_id IS NULL;",
            new { StmtId = stmtId, Now = now, Sup = dto.SupplierName.Trim() }, tx);

        await tx.CommitAsync(ct);

        return Ok(new
        {
            statementId = stmtId,
            statementNo = stmtNo,
            supplierName = dto.SupplierName.Trim(),
            periodYm = dto.PeriodYm.Trim(),
            itemCount = items.Count,
            totalCostAmount = totalCost,
            totalChargeAmount = totalCharge,
            status = "DRAFT"
        });
    }

    [HttpGet("consignment/statements")]
    public async Task<IActionResult> ListStatements(CancellationToken ct)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        return Ok(await conn.QueryAsync("SELECT * FROM udi_consignment_statements ORDER BY created_at_ms DESC LIMIT 100"));
    }

    [HttpPost("consignment/statements/{statementId}/confirm")]
    public async Task<IActionResult> ConfirmStatement(string statementId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        int affected = await conn.ExecuteAsync(@"
UPDATE udi_consignment_statements
SET status = 'CONFIRMED', confirmed_by = @ActorName, confirmed_at_ms = @Now, updated_at_ms = @Now
WHERE id = @Id AND status = 'DRAFT';", new { ActorName, Now = now, Id = statementId });

        if (affected == 0) return BadRequest(new { message = "代销对账单不存在或已确认。" });
        return Ok(new { statementId, status = "CONFIRMED", confirmedBy = ActorName });
    }
}
