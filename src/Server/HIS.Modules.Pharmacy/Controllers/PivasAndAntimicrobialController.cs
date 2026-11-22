using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Pharmacy.Controllers;

/// <summary>
/// 抗菌药物 DDDs 监测与 PIVAS 静配中心全闭环：
/// 1) 抗菌药物分级权限强校验（联动 sys_users.prescribe_level 与 abx_ddd_standards.antibiotic_level）
/// 2) 住院抗菌药物 DDDs / 使用强度 AUD（每百人天 DDDs，国家红线 <= 40）实时核算
/// 3) PIVAS 静配中心全流程闭环：审方排批 -> 贴签 -> 调配 -> 双人独立核对(禁止同人) -> 出仓签收
/// </summary>
[ApiController]
[Route("api/v4/pivas-abx")]
public sealed class PivasAndAntimicrobialController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _session;

    public PivasAndAntimicrobialController(IDbConnectionFactory connFactory, IUserSession? session = null)
    {
        _connFactory = connFactory;
        _session = session;
    }

    private string ActorId => _session?.UserId?.Trim() ?? "SYSTEM";
    private string ActorName => _session?.DisplayName ?? _session?.UserName ?? ActorId;

    public sealed record CheckAbxPrivilegeDto(string DoctorUserId, string DrugCode);

    public sealed record CreatePivasOrderDto(
        string OrderId,
        string EncounterId,
        string PatientId,
        string PatientName,
        string? WardName,
        string DrugCode,
        string DrugName,
        string SolventName,
        string DosageText,
        string? BatchCode);

    public sealed record ReviewPivasDto(bool Approved, string? BatchCode, string? RejectReason, string? ReviewerName);
    public sealed record CompoundPivasDto(string? CompounderName);
    public sealed record CheckPivasDto(string CheckerName);

    [HttpPost("abx/check-privilege")]
    public async Task<IActionResult> CheckAntibioticPrivilege([FromBody] CheckAbxPrivilegeDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.DoctorUserId) || string.IsNullOrWhiteSpace(dto.DrugCode))
            return BadRequest(new { message = "医师ID与药品编码不能为空。" });

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var user = await conn.QueryFirstOrDefaultAsync(
            "SELECT id, display_name, prescribe_level FROM sys_users WHERE id = @Id",
            new { Id = dto.DoctorUserId.Trim() });
        if (user == null) return NotFound(new { message = "未找到对应医师账号。" });

        var abx = await conn.QueryFirstOrDefaultAsync(
            "SELECT drug_code, drug_name, antibiotic_level, ddd_value_grams FROM abx_ddd_standards WHERE drug_code = @Code",
            new { Code = dto.DrugCode.Trim().ToUpperInvariant() });
        if (abx == null)
        {
            return Ok(new { allowed = true, isAntibiotic = false, message = "非抗菌药物监测目录品种，允许开具。" });
        }

        int doctorLevel = Convert.ToInt32(user.prescribe_level);
        int reqLevel = Convert.ToInt32(abx.antibiotic_level);
        bool allowed = doctorLevel >= reqLevel;

        string levelName = reqLevel switch { 3 => "特殊使用级(3级)", 2 => "限制使用级(2级)", _ => "非限制使用级(1级)" };
        return Ok(new
        {
            allowed,
            isAntibiotic = true,
            doctorName = (string?)user.display_name,
            doctorPrescribeLevel = doctorLevel,
            requiredAntibioticLevel = reqLevel,
            requiredLevelName = levelName,
            message = allowed
                ? $"权限校验通过（医师处方级别 {doctorLevel} >= {levelName}）。"
                : $"越级拦截：该药为{levelName}抗菌药物，当前医师处方权限仅 {doctorLevel} 级，需上级医师会诊授权。"
        });
    }

    [HttpGet("abx/aud-report")]
    public async Task<IActionResult> GetAntibioticAudReport([FromQuery] int totalPatientBedDays = 100, CancellationToken ct = default)
    {
        if (totalPatientBedDays <= 0) totalPatientBedDays = 100;

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var rows = (await conn.QueryAsync(@"
SELECT s.drug_code,
       s.drug_name,
       s.antibiotic_level,
       s.ddd_value_grams,
       s.spec_grams_per_unit,
       COALESCE(SUM(l.quantity), 0) AS total_units
FROM abx_ddd_standards s
LEFT JOIN fin_charge_ledgers l ON UPPER(l.item_code) = UPPER(s.drug_code) AND l.status <> 'VOIDED'
WHERE s.is_active = 1
GROUP BY s.drug_code, s.drug_name, s.antibiotic_level, s.ddd_value_grams, s.spec_grams_per_unit
ORDER BY s.antibiotic_level DESC, s.drug_code;")).ToList();

        var items = new List<object>();
        decimal grandTotalDdds = 0m;
        foreach (var r in rows)
        {
            decimal units = Convert.ToDecimal(r.total_units);
            decimal specG = Convert.ToDecimal(r.spec_grams_per_unit);
            decimal dddG = Convert.ToDecimal(r.ddd_value_grams);
            decimal totalGrams = Math.Round(units * specG, 4);
            decimal ddds = dddG > 0 ? Math.Round(totalGrams / dddG, 2) : 0m;
            grandTotalDdds += ddds;

            items.Add(new
            {
                drugCode = (string)r.drug_code,
                drugName = (string)r.drug_name,
                antibioticLevel = Convert.ToInt32(r.antibiotic_level),
                totalUnits = units,
                totalGrams,
                dddValueGrams = dddG,
                ddds
            });
        }

        decimal aud = Math.Round((grandTotalDdds / totalPatientBedDays) * 100m, 2);
        return Ok(new
        {
            totalPatientBedDays,
            grandTotalDdds = Math.Round(grandTotalDdds, 2),
            audPer100BedDays = aud,
            nationalRedlineAud = 40.0m,
            isOverRedline = aud > 40.0m,
            items
        });
    }

    [HttpGet("pivas/orders")]
    public async Task<IActionResult> ListPivasOrders([FromQuery] string? status = null, [FromQuery] string? batchCode = null, CancellationToken ct = default)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var sql = "SELECT * FROM pivas_orders WHERE 1=1";
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(status))
        {
            sql += " AND status = @Status";
            p.Add("Status", status.Trim().ToUpperInvariant());
        }
        if (!string.IsNullOrWhiteSpace(batchCode))
        {
            sql += " AND batch_code = @Batch";
            p.Add("Batch", batchCode.Trim().ToUpperInvariant());
        }
        sql += " ORDER BY created_at_ms DESC LIMIT 200";
        return Ok(await conn.QueryAsync(sql, p));
    }

    [HttpPost("pivas/orders")]
    public async Task<IActionResult> CreatePivasOrder([FromBody] CreatePivasOrderDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.OrderId) || string.IsNullOrWhiteSpace(dto.DrugName) || string.IsNullOrWhiteSpace(dto.SolventName))
            return BadRequest(new { message = "医嘱ID、主药名称与溶媒名称不能为空。" });

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var id = Guid.NewGuid().ToString("N");
        var pivasNo = $"PV{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(100, 999)}";

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        await conn.ExecuteAsync(@"
INSERT INTO pivas_orders (
    id, pivas_no, order_id, encounter_id, patient_id, patient_name, ward_name,
    drug_code, drug_name, solvent_name, dosage_text, batch_code, status,
    created_at_ms, created_by, updated_at_ms, updated_by, version_no
) VALUES (
    @Id, @PivasNo, @OrderId, @EncId, @PatId, @PatName, @Ward,
    @DrugCode, @DrugName, @Solvent, @Dosage, @Batch, 'PENDING_REVIEW',
    @Now, @Actor, @Now, @Actor, 1
);", new
        {
            Id = id,
            PivasNo = pivasNo,
            OrderId = dto.OrderId.Trim(),
            EncId = dto.EncounterId.Trim(),
            PatId = dto.PatientId.Trim(),
            PatName = dto.PatientName.Trim(),
            Ward = dto.WardName ?? "住院病区",
            DrugCode = dto.DrugCode.Trim().ToUpperInvariant(),
            DrugName = dto.DrugName.Trim(),
            Solvent = dto.SolventName.Trim(),
            Dosage = dto.DosageText.Trim(),
            Batch = string.IsNullOrWhiteSpace(dto.BatchCode) ? "BATCH_1" : dto.BatchCode.Trim().ToUpperInvariant(),
            Now = now,
            Actor = ActorId
        });

        return Ok(new { id, pivasNo, status = "PENDING_REVIEW" });
    }

    [HttpPost("pivas/orders/{id}/review")]
    public async Task<IActionResult> ReviewPivasOrder(string id, [FromBody] ReviewPivasDto dto, CancellationToken ct)
    {
        if (!dto.Approved && string.IsNullOrWhiteSpace(dto.RejectReason))
            return BadRequest(new { message = "静配审方打回时必须填写配伍禁忌或不合理原因。" });

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var nextStatus = dto.Approved ? "REVIEWED" : "REJECTED";
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        int affected = await conn.ExecuteAsync(@"
UPDATE pivas_orders
SET status = @Status,
    batch_code = COALESCE(@Batch, batch_code),
    reviewer_name = @Reviewer,
    reviewed_at_ms = @Now,
    reject_reason = @Reason,
    updated_at_ms = @Now,
    updated_by = @Actor,
    version_no = version_no + 1
WHERE id = @Id AND status = 'PENDING_REVIEW';", new
        {
            Status = nextStatus,
            Batch = string.IsNullOrWhiteSpace(dto.BatchCode) ? null : dto.BatchCode.Trim().ToUpperInvariant(),
            Reviewer = string.IsNullOrWhiteSpace(dto.ReviewerName) ? ActorName : dto.ReviewerName.Trim(),
            Reason = dto.RejectReason,
            Now = now,
            Actor = ActorId,
            Id = id
        });

        if (affected == 0) return BadRequest(new { message = "静配工单不存在或当前非待审方状态。" });
        return Ok(new { id, status = nextStatus });
    }

    [HttpPost("pivas/orders/{id}/label")]
    public async Task<IActionResult> PrintLabel(string id, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var barcode = $"PVL-{DateTime.UtcNow:yyyyMMdd}-{id[..8].ToUpperInvariant()}";
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        int affected = await conn.ExecuteAsync(@"
UPDATE pivas_orders
SET status = 'LABELED', label_barcode = @Barcode, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1
WHERE id = @Id AND status = 'REVIEWED';", new { Barcode = barcode, Now = now, Actor = ActorId, Id = id });

        if (affected == 0) return BadRequest(new { message = "仅已审方(REVIEWED)的静配工单允许贴签。" });
        return Ok(new { id, status = "LABELED", labelBarcode = barcode });
    }

    [HttpPost("pivas/orders/{id}/compound")]
    public async Task<IActionResult> CompoundOrder(string id, [FromBody] CompoundPivasDto dto, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var compounder = string.IsNullOrWhiteSpace(dto.CompounderName) ? ActorName : dto.CompounderName.Trim();
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        int affected = await conn.ExecuteAsync(@"
UPDATE pivas_orders
SET status = 'COMPOUNDED', compounder_name = @Compounder, compounded_at_ms = @Now, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1
WHERE id = @Id AND status = 'LABELED';", new { Compounder = compounder, Now = now, Actor = ActorId, Id = id });

        if (affected == 0) return BadRequest(new { message = "仅已贴签(LABELED)的静配工单允许进仓调配。" });
        return Ok(new { id, status = "COMPOUNDED", compounderName = compounder });
    }

    [HttpPost("pivas/orders/{id}/check")]
    public async Task<IActionResult> DoubleCheckOrder(string id, [FromBody] CheckPivasDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.CheckerName))
            return BadRequest(new { message = "核对人姓名不能为空。" });

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync("SELECT status, compounder_name FROM pivas_orders WHERE id = @Id", new { Id = id });
        if (row == null) return NotFound(new { message = "静配工单不存在。" });
        if ((string)row.status != "COMPOUNDED")
            return BadRequest(new { message = "仅已调配(COMPOUNDED)的静配工单允许出仓核对。" });

        if (string.Equals((string?)row.compounder_name, dto.CheckerName.Trim(), StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "PIVAS 严格执行双人核对制度：核对人不能与调配人相同。" });

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await conn.ExecuteAsync(@"
UPDATE pivas_orders
SET status = 'CHECKED', checker_name = @Checker, checked_at_ms = @Now, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1
WHERE id = @Id;", new { Checker = dto.CheckerName.Trim(), Now = now, Actor = ActorId, Id = id });

        return Ok(new { id, status = "CHECKED", checkerName = dto.CheckerName.Trim() });
    }

    [HttpPost("pivas/orders/{id}/dispatch")]
    public async Task<IActionResult> DispatchOrder(string id, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        int affected = await conn.ExecuteAsync(@"
UPDATE pivas_orders
SET status = 'DISPATCHED', dispatched_at_ms = @Now, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1
WHERE id = @Id AND status = 'CHECKED';", new { Now = now, Actor = ActorId, Id = id });

        if (affected == 0) return BadRequest(new { message = "仅已完成双人核对(CHECKED)的成品输液允许出仓配送。" });
        return Ok(new { id, status = "DISPATCHED", dispatchedAtMs = now });
    }
}
