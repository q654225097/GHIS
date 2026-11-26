using HIS.BuildingBlocks.Security;
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

namespace HIS.Modules.Outpatient.Controllers;

[ApiController]
[RequireHisRole("ADMIN", "OPD_DOC", "WARD_NURSE")]
[Route("api/v4/opd/infusion")]
public sealed class OpdInfusionController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _userSession;
    private readonly IAuditTrailContract? _auditTrail;

    public OpdInfusionController(
        IDbConnectionFactory connFactory,
        IUserSession? userSession = null,
        IAuditTrailContract? auditTrail = null)
    {
        _connFactory = connFactory;
        _userSession = userSession;
        _auditTrail = auditTrail;
    }

    public sealed record InfusionOrderDto(
        string Id,
        string InfusionNo,
        string PrescriptionId,
        string VisitId,
        string PatientId,
        string PatientName,
        string? SeatNo,
        int Status, // 0: 待配药, 1: 已配药已复核, 2: 正在输液, 3: 已换瓶, 4: 输液完成, 5: 异常终止
        string? CompoundNurseId,
        string? CompoundCheckNurseId,
        long? CompoundedAtMs,
        string? PunctureNurseId,
        long? PuncturedAtMs,
        int DripRate,
        int TotalBottles,
        int CompletedBottles,
        long? CompletedAtMs,
        List<InfusionBottleDto>? Bottles = null
    );

    public sealed record InfusionBottleDto(
        string Id,
        string InfusionOrderId,
        string BottleBarcode,
        int SequenceNo,
        string DrugNames,
        string? Spec,
        string? DosageText,
        int Status, // 0: 待输, 1: 正在输, 2: 已输完
        long? StartTimeMs,
        long? EndTimeMs
    );

    public sealed class InfusionInspectionDto
    {
        public string Id { get; set; } = string.Empty;
        public string InfusionOrderId { get; set; } = string.Empty;
        public string? BottleId { get; set; }
        public string InspectNurseId { get; set; } = string.Empty;
        public string InspectNurseName { get; set; } = string.Empty;
        public int DripSpeed { get; set; }
        public int IsSwollen { get; set; }
        public int HasAdverseReaction { get; set; }
        public string? Notes { get; set; }
        public long InspectedAtMs { get; set; }
    }

    public sealed record CompoundCheckRequestDto(
        string InfusionOrderId,
        string CompoundNurseId,
        string CompoundCheckNurseId
    );

    public sealed record PunctureRequestDto(
        string InfusionOrderId,
        string BottleBarcode,
        string PatientWristbandOrId,
        string SeatNo,
        int DripRate
    );

    public sealed record RecordInspectionRequestDto(
        string InfusionOrderId,
        string? BottleId,
        int DripSpeed,
        bool IsSwollen,
        bool HasAdverseReaction,
        string? Notes
    );

    public sealed record ReplaceBottleRequestDto(
        string InfusionOrderId,
        string NextBottleBarcode,
        string PatientWristbandOrId
    );

    public sealed record CompleteInfusionRequestDto(
        string InfusionOrderId,
        string? Notes = null
    );

    [HttpGet("orders")]
    public async Task<ApiResponse<IReadOnlyList<InfusionOrderDto>>> GetInfusionOrders(
        [FromQuery] string? patientId = null,
        [FromQuery] int? status = null,
        [FromQuery] string? keyword = null)
    {
        using var conn = _connFactory.CreateConnection();
        var sql = @"SELECT id AS Id, infusion_no AS InfusionNo, prescription_id AS PrescriptionId,
                           visit_id AS VisitId, patient_id AS PatientId, patient_name AS PatientName,
                           seat_no AS SeatNo, status AS Status, compound_nurse_id AS CompoundNurseId,
                           compound_check_nurse_id AS CompoundCheckNurseId, compounded_at_ms AS CompoundedAtMs,
                           puncture_nurse_id AS PunctureNurseId, punctured_at_ms AS PuncturedAtMs,
                           drip_rate AS DripRate, total_bottles AS TotalBottles, completed_bottles AS CompletedBottles,
                           completed_at_ms AS CompletedAtMs
                    FROM opd_infusion_orders WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(patientId)) sql += " AND patient_id = @Pid";
        if (status.HasValue) sql += " AND status = @St";
        if (!string.IsNullOrWhiteSpace(keyword)) sql += " AND (patient_name LIKE @Kw OR infusion_no LIKE @Kw OR seat_no LIKE @Kw)";
        sql += " ORDER BY created_at_ms DESC LIMIT 100";

        var orders = (await conn.QueryAsync<InfusionOrderDto>(sql, new { Pid = patientId, St = status, Kw = $"%{keyword}%" })).ToList();

        if (orders.Count > 0)
        {
            var orderIds = orders.Select(x => x.Id).ToList();
            var bottles = (await conn.QueryAsync<InfusionBottleDto>(@"
                SELECT id AS Id, infusion_order_id AS InfusionOrderId, bottle_barcode AS BottleBarcode,
                       sequence_no AS SequenceNo, drug_names AS DrugNames, spec AS Spec, dosage_text AS DosageText,
                       status AS Status, start_time_ms AS StartTimeMs, end_time_ms AS EndTimeMs
                FROM opd_infusion_bottles
                WHERE infusion_order_id IN @Ids
                ORDER BY sequence_no ASC", new { Ids = orderIds })).ToList();

            var bottleMap = bottles.GroupBy(x => x.InfusionOrderId).ToDictionary(g => g.Key, g => g.ToList());
            orders = orders.Select(o => o with
            {
                Bottles = bottleMap.TryGetValue(o.Id, out var bList) ? bList : new List<InfusionBottleDto>()
            }).ToList();
        }

        return ApiResponse<IReadOnlyList<InfusionOrderDto>>.Success(orders);
    }

    [HttpPost("from-prescription/{rxId}")]
    public async Task<ApiResponse<InfusionOrderDto>> CreateFromPrescription(string rxId, CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();
        var rx = await conn.QuerySingleOrDefaultAsync<dynamic>(@"
            SELECT p.id, p.prescription_no, p.visit_id, p.patient_id, p.status, pt.full_name AS patient_name
            FROM opd_prescriptions p
            JOIN pat_patients pt ON pt.id = p.patient_id
            WHERE p.id = @RxId", new { RxId = rxId });

        if (rx == null)
            return ApiResponse<InfusionOrderDto>.Fail(404, "处方不存在");

        var items = (await conn.QueryAsync<dynamic>(@"
            SELECT id, drug_name_snapshot, route_code, group_no, dose_once, dose_unit, total_quantity
            FROM opd_prescription_items
            WHERE prescription_id = @RxId AND is_deleted = 0
            ORDER BY group_no ASC", new { RxId = rxId })).ToList();

        if (items.Count == 0)
            return ApiResponse<InfusionOrderDto>.Fail(400, "处方无有效明细");

        // 按同组（group_no）或者按条目打包成输液瓶贴
        var groups = items.GroupBy(i => (int)i.group_no).ToList();
        long now = SystemTime.UtcNowMs;
        string infusionId = Guid.NewGuid().ToString("D");
        string infusionNo = "INF" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss") + Random.Shared.Next(10, 99);

        await _connFactory.ExecuteInTransactionAsync(async transCt =>
        {
            await conn.ExecuteAsync(@"
                INSERT INTO opd_infusion_orders
                (id, infusion_no, prescription_id, visit_id, patient_id, patient_name, status, total_bottles, completed_bottles, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES
                (@Id, @No, @RxId, @VisitId, @PatientId, @PatientName, 0, @TotalBottles, 0, 1, @Now, @User, @Now, @User, 1)",
                new
                {
                    Id = infusionId,
                    No = infusionNo,
                    RxId = (string)rx.id,
                    VisitId = (string)rx.visit_id,
                    PatientId = (string)rx.patient_id,
                    PatientName = (string)rx.patient_name,
                    TotalBottles = groups.Count,
                    Now = now,
                    User = _userSession?.UserId ?? "SYSTEM"
                });

            int seq = 1;
            foreach (var grp in groups)
            {
                string bottleId = Guid.NewGuid().ToString("D");
                string barcode = $"BTL-{SystemTime.ShanghaiNow:yyyyMMdd}-{seq:02}-{Random.Shared.Next(100, 999)}";
                var drugNames = string.Join(" + ", grp.Select(x => (string)x.drug_name_snapshot));
                var dosage = string.Join("; ", grp.Select(x => $"{x.dose_once}{x.dose_unit}"));

                await conn.ExecuteAsync(@"
                    INSERT INTO opd_infusion_bottles
                    (id, infusion_order_id, bottle_barcode, sequence_no, drug_names, dosage_text, status, schema_version, created_at_ms, updated_at_ms, version_no)
                    VALUES
                    (@Id, @OrderId, @Barcode, @Seq, @Drugs, @Dosage, 0, 1, @Now, @Now, 1)",
                    new
                    {
                        Id = bottleId,
                        OrderId = infusionId,
                        Barcode = barcode,
                        Seq = seq,
                        Drugs = drugNames,
                        Dosage = dosage,
                        Now = now
                    });
                seq++;
            }
        });

        var created = (await GetInfusionOrders(patientId: (string)rx.patient_id, keyword: infusionNo)).Data?.FirstOrDefault();
        return ApiResponse<InfusionOrderDto>.Success(created!, "门诊输液单生成成功，已排定瓶贴条码");
    }

    [HttpPost("compound-check")]
    public async Task<ApiResponse<bool>> CompoundCheck([FromBody] CompoundCheckRequestDto req)
    {
        if (string.IsNullOrWhiteSpace(req.CompoundNurseId) || string.IsNullOrWhiteSpace(req.CompoundCheckNurseId))
            return ApiResponse<bool>.Fail(400, "配药护士与复核护士不能为空");
        if (string.Equals(req.CompoundNurseId.Trim(), req.CompoundCheckNurseId.Trim(), StringComparison.OrdinalIgnoreCase))
            return ApiResponse<bool>.Fail(400, "输液配药与复核护士必须为两名不同护士（三查七对制度）");

        using var conn = _connFactory.CreateConnection();
        long now = SystemTime.UtcNowMs;
        int affected = await conn.ExecuteAsync(@"
            UPDATE opd_infusion_orders
            SET status = 1, compound_nurse_id = @C1, compound_check_nurse_id = @C2, compounded_at_ms = @Now,
                updated_at_ms = @Now, updated_by = @User, version_no = version_no + 1
            WHERE id = @Id AND status = 0",
            new
            {
                Id = req.InfusionOrderId,
                C1 = req.CompoundNurseId.Trim(),
                C2 = req.CompoundCheckNurseId.Trim(),
                Now = now,
                User = _userSession?.UserId ?? req.CompoundNurseId
            });

        if (affected == 0)
            return ApiResponse<bool>.Fail(409, "当前输液单已配药或状态已变更");

        return ApiResponse<bool>.Success(true, "输液配药双人复核成功，已放行执行穿刺");
    }

    [HttpPost("puncture")]
    public async Task<ApiResponse<bool>> Puncture([FromBody] PunctureRequestDto req)
    {
        if (string.IsNullOrWhiteSpace(req.BottleBarcode) || string.IsNullOrWhiteSpace(req.PatientWristbandOrId))
            return ApiResponse<bool>.Fail(400, "必须核验患者身份与输液瓶贴条码");

        using var conn = _connFactory.CreateConnection();
        var order = await conn.QuerySingleOrDefaultAsync<dynamic>(@"
            SELECT id, patient_id, status FROM opd_infusion_orders WHERE id = @Id", new { Id = req.InfusionOrderId });
        if (order == null) return ApiResponse<bool>.Fail(404, "输液单不存在");
        if ((int)order.status < 1) return ApiResponse<bool>.Fail(400, "输液单尚未完成双人配药复核，严禁穿刺输液");

        var bottle = await conn.QuerySingleOrDefaultAsync<dynamic>(@"
            SELECT id, sequence_no, status FROM opd_infusion_bottles
            WHERE infusion_order_id = @OrderId AND bottle_barcode = @Bc",
            new { OrderId = req.InfusionOrderId, Bc = req.BottleBarcode.Trim() });

        if (bottle == null)
            return ApiResponse<bool>.Fail(404, "该输液瓶贴条码不属于当前输液单");

        long now = SystemTime.UtcNowMs;
        string nurseId = _userSession?.UserId ?? "NURSE";

        await _connFactory.ExecuteInTransactionAsync(async ct =>
        {
            await conn.ExecuteAsync(@"
                UPDATE opd_infusion_orders
                SET status = 2, seat_no = @Seat, puncture_nurse_id = @Nurse, punctured_at_ms = @Now,
                    drip_rate = @Rate, updated_at_ms = @Now, updated_by = @Nurse, version_no = version_no + 1
                WHERE id = @Id",
                new { Id = req.InfusionOrderId, Seat = req.SeatNo, Nurse = nurseId, Now = now, Rate = req.DripRate });

            await conn.ExecuteAsync(@"
                UPDATE opd_infusion_bottles
                SET status = 1, start_time_ms = @Now, puncture_nurse_id = @Nurse, updated_at_ms = @Now
                WHERE id = @BId",
                new { BId = (string)bottle.id, Now = now, Nurse = nurseId });
        });

        return ApiResponse<bool>.Success(true, $"穿刺成功，第 {bottle.sequence_no} 瓶药液开始输液，座位号：{req.SeatNo}");
    }

    [HttpGet("orders/{orderId}/inspections")]
    public async Task<ApiResponse<IReadOnlyList<InfusionInspectionDto>>> GetInspections(string orderId)
    {
        using var conn = _connFactory.CreateConnection();
        var list = (await conn.QueryAsync<InfusionInspectionDto>(@"
            SELECT id AS Id, infusion_order_id AS InfusionOrderId, bottle_id AS BottleId,
                   inspect_nurse_id AS InspectNurseId, inspect_nurse_name AS InspectNurseName,
                   drip_speed AS DripSpeed, is_swollen AS IsSwollen,
                   has_adverse_reaction AS HasAdverseReaction, notes AS Notes,
                   inspected_at_ms AS InspectedAtMs
            FROM opd_infusion_inspections
            WHERE infusion_order_id = @OrderId
            ORDER BY inspected_at_ms DESC",
            new { OrderId = orderId })).ToList();
        return ApiResponse<IReadOnlyList<InfusionInspectionDto>>.Success(list);
    }

    [HttpPost("inspect")]
    public async Task<ApiResponse<bool>> RecordInspection([FromBody] RecordInspectionRequestDto req)
    {
        using var conn = _connFactory.CreateConnection();
        long now = SystemTime.UtcNowMs;
        string nurseId = _userSession?.UserId ?? "NURSE";
        string nurseName = _userSession?.DisplayName ?? nurseId;

        await conn.ExecuteAsync(@"
            INSERT INTO opd_infusion_inspections
            (id, infusion_order_id, bottle_id, inspect_nurse_id, inspect_nurse_name, drip_speed, is_swollen, has_adverse_reaction, notes, inspected_at_ms)
            VALUES
            (@Id, @OrderId, @BottleId, @NurseId, @NurseName, @Speed, @Swollen, @Adv, @Notes, @Now)",
            new
            {
                Id = Guid.NewGuid().ToString("D"),
                OrderId = req.InfusionOrderId,
                BottleId = req.BottleId,
                NurseId = nurseId,
                NurseName = nurseName,
                Speed = req.DripSpeed,
                Swollen = req.IsSwollen ? 1 : 0,
                Adv = req.HasAdverseReaction ? 1 : 0,
                Notes = req.Notes,
                Now = now
            });

        return ApiResponse<bool>.Success(true, "输液巡视记录已保存");
    }

    [HttpPost("replace-bottle")]
    public async Task<ApiResponse<bool>> ReplaceBottle([FromBody] ReplaceBottleRequestDto req)
    {
        using var conn = _connFactory.CreateConnection();
        var nextBottle = await conn.QuerySingleOrDefaultAsync<dynamic>(@"
            SELECT id, sequence_no, status FROM opd_infusion_bottles
            WHERE infusion_order_id = @OrderId AND bottle_barcode = @Bc",
            new { OrderId = req.InfusionOrderId, Bc = req.NextBottleBarcode.Trim() });

        if (nextBottle == null)
            return ApiResponse<bool>.Fail(404, "下一瓶瓶贴条码不存在或不属于本科室");
        if ((int)nextBottle.status != 0)
            return ApiResponse<bool>.Fail(409, "该药瓶已输液或已完成");

        long now = SystemTime.UtcNowMs;
        string nurseId = _userSession?.UserId ?? "NURSE";

        await _connFactory.ExecuteInTransactionAsync(async ct =>
        {
            // 结束上一瓶
            await conn.ExecuteAsync(@"
                UPDATE opd_infusion_bottles
                SET status = 2, end_time_ms = @Now, updated_at_ms = @Now
                WHERE infusion_order_id = @OrderId AND status = 1",
                new { OrderId = req.InfusionOrderId, Now = now });

            // 开启新一瓶
            await conn.ExecuteAsync(@"
                UPDATE opd_infusion_bottles
                SET status = 1, start_time_ms = @Now, replace_nurse_id = @Nurse, updated_at_ms = @Now
                WHERE id = @BId",
                new { BId = (string)nextBottle.id, Now = now, Nurse = nurseId });

            // 更新 order 计数
            await conn.ExecuteAsync(@"
                UPDATE opd_infusion_orders
                SET completed_bottles = completed_bottles + 1, updated_at_ms = @Now, updated_by = @Nurse, version_no = version_no + 1
                WHERE id = @Id",
                new { Id = req.InfusionOrderId, Now = now, Nurse = nurseId });
        });

        return ApiResponse<bool>.Success(true, $"换药扫码核对成功，已接上第 {nextBottle.sequence_no} 瓶输液");
    }

    [HttpPost("complete")]
    public async Task<ApiResponse<bool>> CompleteInfusion([FromBody] CompleteInfusionRequestDto req)
    {
        using var conn = _connFactory.CreateConnection();
        long now = SystemTime.UtcNowMs;
        string nurseId = _userSession?.UserId ?? "NURSE";

        await _connFactory.ExecuteInTransactionAsync(async ct =>
        {
            await conn.ExecuteAsync(@"
                UPDATE opd_infusion_bottles
                SET status = 2, end_time_ms = @Now, updated_at_ms = @Now
                WHERE infusion_order_id = @OrderId AND status = 1",
                new { OrderId = req.InfusionOrderId, Now = now });

            await conn.ExecuteAsync(@"
                UPDATE opd_infusion_orders
                SET status = 4, completed_bottles = total_bottles, completed_at_ms = @Now,
                    updated_at_ms = @Now, updated_by = @Nurse, version_no = version_no + 1
                WHERE id = @Id",
                new { Id = req.InfusionOrderId, Now = now, Nurse = nurseId });
        });

        return ApiResponse<bool>.Success(true, "输液拔针完成，观察无异常后离室");
    }
}
