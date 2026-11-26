using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Outpatient.Controllers;

/// <summary>
/// 角色一（挂号员 / 收费员）现实窗口闭环控制器：
/// 1) 同日换科/换医原子改签（释放旧号源 + 锁定新号源 + 自动核算差价多退少补）
/// 2) 预约超时未取号自动释号回池 + 30天爽约 >= 3 次自动冻结黑名单
/// 3) 医生临时停诊一键批量迁移已预约患者并生成通知留痕
/// 4) 三方退检/退费申请池（医生/窗口发起 -> 医技确认未执行 -> 收费员核销退费并作废原费用台账）
/// 5) 收费员交接班备用金与长短款核对
/// </summary>
[ApiController]
[RequireHisRole("ADMIN", "CASHIER", "OPD_DOC", "MEDTECH", "WARD_NURSE")]
[Route("api/v4/registrar-closure")]
public sealed class RegistrarWindowClosureController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _session;

    public RegistrarWindowClosureController(IDbConnectionFactory connFactory, IUserSession? session = null)
    {
        _connFactory = connFactory;
        _session = session;
    }

    private string ActorId => _session?.UserId?.Trim() ?? "SYSTEM";
    private string ActorName => _session?.DisplayName ?? _session?.UserName ?? ActorId;

    public sealed record RescheduleDto(string TargetScheduleId, string? Reason);
    public sealed record BatchMigrateDto(string TargetScheduleId, string Reason);
    public sealed record CreateRefundRequestDto(string PatientId, string PatientName, string VisitId, string ChargeLedgerId, string ItemName, decimal RefundAmount, string Reason, string? InitiatorDoctor);
    public sealed record ExecConfirmDto(string? ConfirmerName);
    public sealed record CashierShiftDto(string ShiftType, decimal OpeningPettyCash, decimal SystemCashTotal, decimal ActualCashCounted, string? ReceiverName, string? Note);

    [HttpPost("appointments/{appointmentId}/reschedule")]
    public async Task<ApiResponse<object>> RescheduleAppointment(string appointmentId, [FromBody] RescheduleDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.TargetScheduleId))
            return ApiResponse<object>.Fail(400, "目标排班ID不能为空。");

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var appt = await conn.QueryFirstOrDefaultAsync("SELECT * FROM opd_appointments WHERE id = @Id", new { Id = appointmentId });
        if (appt == null) return ApiResponse<object>.Fail(404, "原挂号/预约记录不存在。");

        string oldScheduleId = (string)appt.schedule_id;
        if (string.Equals(oldScheduleId, dto.TargetScheduleId.Trim(), StringComparison.OrdinalIgnoreCase))
            return ApiResponse<object>.Fail(400, "目标排班与原排班相同，无需改签。");

        var targetSlot = await conn.QueryFirstOrDefaultAsync("SELECT * FROM opd_schedule_slots WHERE id = @Id", new { Id = dto.TargetScheduleId.Trim() });
        if (targetSlot == null) return ApiResponse<object>.Fail(404, "目标排班号源不存在。");

        int totalCap = Convert.ToInt32(targetSlot.total_capacity);
        int usedCnt = Convert.ToInt32(targetSlot.used_count);
        if (usedCnt >= totalCap)
            return ApiResponse<object>.Fail(400, "目标排班号源已满，无法改签。");

        decimal oldFee = appt.paid_amount != null ? Convert.ToDecimal(appt.paid_amount) : 0m;
        decimal newFee = Convert.ToDecimal(targetSlot.registration_fee);
        decimal diffFee = Math.Round(newFee - oldFee, 2); // >0 需补差价，<0 应退差价
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await using var tx = await conn.BeginTransactionAsync(ct);

        // 1) 释放旧号源
        await conn.ExecuteAsync(@"
UPDATE opd_schedule_slots
SET used_count = CASE WHEN used_count > 0 THEN used_count - 1 ELSE 0 END,
    updated_at_ms = @Now, version_no = version_no + 1
WHERE id = @OldId;", new { Now = now, OldId = oldScheduleId }, tx);

        // 2) 占用新号源
        await conn.ExecuteAsync(@"
UPDATE opd_schedule_slots
SET used_count = used_count + 1,
    updated_at_ms = @Now, version_no = version_no + 1
WHERE id = @NewId AND used_count < total_capacity;", new { Now = now, NewId = dto.TargetScheduleId.Trim() }, tx);

        // 3) 原子更新挂号记录
        await conn.ExecuteAsync(@"
UPDATE opd_appointments
SET schedule_id = @NewId,
    paid_amount = @NewFee,
    updated_at_ms = @Now,
    updated_by = @Actor,
    version_no = version_no + 1
WHERE id = @ApptId;", new { NewId = dto.TargetScheduleId.Trim(), NewFee = newFee, Now = now, Actor = ActorId, ApptId = appointmentId }, tx);

        // 4) 通知患者留痕
        await conn.ExecuteAsync(@"
INSERT INTO pat_notification_logs (id, patient_id, channel, scene, title, content, sent_at_ms)
VALUES (@Nid, @PatId, 'WINDOW', 'RESCHEDULE', '挂号改签成功通知', @Content, @Now);", new
        {
            Nid = Guid.NewGuid().ToString("N"),
            PatId = (string)appt.patient_id,
            Content = $"您的挂号单已改签至新排班({dto.TargetScheduleId.Trim()})，原费 ¥{oldFee:F2}，新费 ¥{newFee:F2}，差价 ¥{diffFee:F2}。原因：{dto.Reason ?? "窗口改签"}",
            Now = now
        }, tx);

        await tx.CommitAsync(ct);

        return ApiResponse<object>.Success(new
        {
            appointmentId,
            oldScheduleId,
            newScheduleId = dto.TargetScheduleId.Trim(),
            oldFee,
            newFee,
            feeDifference = diffFee,
            settlementAction = diffFee > 0 ? "COLLECT_EXTRA_CASH" : diffFee < 0 ? "REFUND_DIFF_CASH" : "NO_DIFF"
        }, "改签已完成并同步调整号池与差价。");
    }

    [HttpPost("appointments/release-expired")]
    public async Task<ApiResponse<object>> ReleaseExpiredReservations(CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long window30d = now - (30L * 86400_000L);

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var expired = (await conn.QueryAsync(@"
SELECT id, patient_id, schedule_id
FROM opd_appointments
WHERE status IN ('RESERVED', 'LOCKED') AND checkin_deadline_ms IS NOT NULL AND checkin_deadline_ms < @Now;",
            new { Now = now })).ToList();

        int released = 0;
        int newlyBlacklisted = 0;

        await using var tx = await conn.BeginTransactionAsync(ct);
        foreach (var row in expired)
        {
            string apptId = (string)row.id;
            string patId = (string)row.patient_id;
            string slotId = (string)row.schedule_id;

            await conn.ExecuteAsync("UPDATE opd_appointments SET status = 'NO_SHOW_RELEASED', updated_at_ms = @Now WHERE id = @Id", new { Now = now, Id = apptId }, tx);
            await conn.ExecuteAsync("UPDATE opd_schedule_slots SET used_count = CASE WHEN used_count > 0 THEN used_count - 1 ELSE 0 END, updated_at_ms = @Now WHERE id = @SlotId", new { Now = now, SlotId = slotId }, tx);

            int recentMisses = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM pat_no_show_records WHERE patient_id = @PatId AND missed_at_ms >= @Win",
                new { PatId = patId, Win = window30d }, tx) + 1;

            bool shouldBlacklist = recentMisses >= 3;
            long? freezeUntil = shouldBlacklist ? now + (30L * 86400_000L) : null;
            if (shouldBlacklist) newlyBlacklisted++;

            await conn.ExecuteAsync(@"
INSERT INTO pat_no_show_records (id, patient_id, patient_name, appointment_id, missed_at_ms, blacklist_until_ms, is_blacklisted, remark, created_at_ms)
VALUES (@Id, @PatId, @PatId, @ApptId, @Now, @FreezeUntil, @IsBlk, @Remark, @Now);", new
            {
                Id = Guid.NewGuid().ToString("N"),
                PatId = patId,
                ApptId = apptId,
                Now = now,
                FreezeUntil = freezeUntil,
                IsBlk = shouldBlacklist ? 1 : 0,
                Remark = shouldBlacklist ? $"30天内累计爽约 {recentMisses} 次，自动冻结线上预约30天" : $"预约超时未取号自动释号（第 {recentMisses} 次）"
            }, tx);

            released++;
        }
        await tx.CommitAsync(ct);

        return ApiResponse<object>.Success(new { releasedCount = released, newlyBlacklistedCount = newlyBlacklisted });
    }

    [HttpGet("patients/{patientId}/blacklist-status")]
    public async Task<ApiResponse<object>> GetBlacklistStatus(string patientId, CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long window30d = now - (30L * 86400_000L);
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        int recent30d = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM pat_no_show_records WHERE patient_id = @PatId AND missed_at_ms >= @Win",
            new { PatId = patientId, Win = window30d });
        long? activeFreeze = await conn.ExecuteScalarAsync<long?>(
            "SELECT MAX(blacklist_until_ms) FROM pat_no_show_records WHERE patient_id = @PatId AND is_blacklisted = 1 AND blacklist_until_ms > @Now",
            new { PatId = patientId, Now = now });

        return ApiResponse<object>.Success(new
        {
            patientId,
            noShowCountLast30Days = recent30d,
            isBlacklisted = activeFreeze.HasValue,
            blacklistUntilMs = activeFreeze
        });
    }

    [HttpPost("schedules/{sourceScheduleId}/batch-migrate")]
    public async Task<ApiResponse<object>> BatchMigrateStoppedSchedule(string sourceScheduleId, [FromBody] BatchMigrateDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.TargetScheduleId))
            return ApiResponse<object>.Fail(400, "替诊目标排班ID不能为空。");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var appts = (await conn.QueryAsync(
            "SELECT id, patient_id FROM opd_appointments WHERE schedule_id = @SrcId AND status IN ('RESERVED', 'LOCKED', 'CONFIRMED', 'PAID')",
            new { SrcId = sourceScheduleId })).ToList();

        await using var tx = await conn.BeginTransactionAsync(ct);
        foreach (var a in appts)
        {
            await conn.ExecuteAsync(
                "UPDATE opd_appointments SET schedule_id = @TargetId, updated_at_ms = @Now, updated_by = @Actor WHERE id = @Id",
                new { TargetId = dto.TargetScheduleId.Trim(), Now = now, Actor = ActorId, Id = (string)a.id }, tx);

            await conn.ExecuteAsync(@"
INSERT INTO pat_notification_logs (id, patient_id, channel, scene, title, content, sent_at_ms)
VALUES (@Nid, @PatId, 'SMS', 'DOCTOR_SUSPEND_MIGRATE', '医生停诊改签通知', @Content, @Now);", new
            {
                Nid = Guid.NewGuid().ToString("N"),
                PatId = (string)a.patient_id,
                Content = $"因原医师停诊（{dto.Reason}），系统已自动为您迁移至替诊排班 {dto.TargetScheduleId.Trim()}，原号序保留。",
                Now = now
            }, tx);
        }
        await tx.CommitAsync(ct);

        return ApiResponse<object>.Success(new { sourceScheduleId, targetScheduleId = dto.TargetScheduleId.Trim(), migratedPatients = appts.Count });
    }

    [HttpGet("refund-requests")]
    public async Task<ApiResponse<object>> ListRefundRequests([FromQuery] string? status = null, CancellationToken ct = default)
    {
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        var sql = "SELECT * FROM opd_refund_requests WHERE 1=1";
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(status))
        {
            sql += " AND status = @St";
            p.Add("St", status.Trim().ToUpperInvariant());
        }
        sql += " ORDER BY created_at_ms DESC LIMIT 200";
        return ApiResponse<object>.Success((await conn.QueryAsync(sql, p)).ToList());
    }

    [HttpPost("refund-requests")]
    public async Task<ApiResponse<object>> CreateRefundRequest([FromBody] CreateRefundRequestDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.ChargeLedgerId) || dto.RefundAmount <= 0 || string.IsNullOrWhiteSpace(dto.Reason))
            return ApiResponse<object>.Fail(400, "费用明细ID、退费金额(>0)与退费原因不能为空。");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string id = Guid.NewGuid().ToString("N");
        string reqNo = $"RF{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(100, 999)}";

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        await conn.ExecuteAsync(@"
INSERT INTO opd_refund_requests (
    id, request_no, patient_id, patient_name, visit_id, charge_ledger_id, item_name,
    refund_amount, reason, initiator_doctor, exec_dept_confirmed, status, created_at_ms, updated_at_ms
) VALUES (
    @Id, @No, @PatId, @PatName, @VisitId, @LedgerId, @Item,
    @Amt, @Reason, @Doc, 0, 'PENDING_EXEC_CONFIRM', @Now, @Now
);", new
        {
            Id = id, No = reqNo, PatId = dto.PatientId.Trim(), PatName = dto.PatientName.Trim(),
            VisitId = dto.VisitId.Trim(), LedgerId = dto.ChargeLedgerId.Trim(), Item = dto.ItemName.Trim(),
            Amt = dto.RefundAmount, Reason = dto.Reason.Trim(), Doc = dto.InitiatorDoctor ?? ActorName, Now = now
        });

        return ApiResponse<object>.Success(new { id, requestNo = reqNo, status = "PENDING_EXEC_CONFIRM" });
    }

    [HttpPost("refund-requests/{id}/exec-confirm")]
    public async Task<ApiResponse<object>> ConfirmNotExecuted(string id, [FromBody] ExecConfirmDto dto, CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        int affected = await conn.ExecuteAsync(@"
UPDATE opd_refund_requests
SET exec_dept_confirmed = 1,
    exec_confirmer_name = @Confirmer,
    exec_confirmed_at_ms = @Now,
    status = 'READY_FOR_CASHIER',
    updated_at_ms = @Now
WHERE id = @Id AND status = 'PENDING_EXEC_CONFIRM';", new { Confirmer = dto.ConfirmerName ?? ActorName, Now = now, Id = id });

        if (affected == 0) return ApiResponse<object>.Fail(400, "退费申请单不存在或已确认。");
        return ApiResponse<object>.Success(new { id, status = "READY_FOR_CASHIER" });
    }

    [HttpPost("refund-requests/{id}/cashier-refund")]
    public async Task<ApiResponse<object>> ExecuteCashierRefund(string id, CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var req = await conn.QueryFirstOrDefaultAsync("SELECT * FROM opd_refund_requests WHERE id = @Id", new { Id = id });
        if (req == null) return ApiResponse<object>.Fail(404, "退费申请单不存在。");
        if (Convert.ToInt32(req.exec_dept_confirmed) != 1 || (string)req.status != "READY_FOR_CASHIER")
            return ApiResponse<object>.Fail(400, "严格执行三方退费门禁：必须先经医技/执行科室确认未执行，收费窗口方可退现金！");

        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync(@"
UPDATE opd_refund_requests
SET status = 'REFUNDED', cashier_name = @Cashier, refunded_at_ms = @Now, updated_at_ms = @Now
WHERE id = @Id;", new { Cashier = ActorName, Now = now, Id = id }, tx);

        await conn.ExecuteAsync(
            "UPDATE fin_charge_ledgers SET status = 'VOIDED', updated_at_ms = @Now, updated_by = @Actor WHERE id = @LedgerId",
            new { Now = now, Actor = ActorId, LedgerId = (string)req.charge_ledger_id }, tx);

        await tx.CommitAsync(ct);
        return ApiResponse<object>.Success(new { id, status = "REFUNDED", refundedAmount = Convert.ToDecimal(req.refund_amount), paymentChannel = "CASH" });
    }

    [HttpPost("shift-handovers")]
    public async Task<ApiResponse<object>> SubmitShiftHandover([FromBody] CashierShiftDto dto, CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        decimal diff = Math.Round(dto.ActualCashCounted - (dto.OpeningPettyCash + dto.SystemCashTotal), 2);
        string id = Guid.NewGuid().ToString("N");
        string shiftNo = $"SH{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(10, 99)}";

        await using var conn = _connFactory.CreateConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        await conn.ExecuteAsync(@"
INSERT INTO fin_cashier_shift_handovers (
    id, shift_no, cashier_id, cashier_name, shift_type, opening_petty_cash,
    system_cash_total, actual_cash_counted, diff_amount, receiver_name, status, note, handover_at_ms, created_at_ms
) VALUES (
    @Id, @No, @Cid, @Cname, @Shift, @OpenCash,
    @SysCash, @ActCash, @Diff, @Recv, @Status, @Note, @Now, @Now
);", new
        {
            Id = id, No = shiftNo, Cid = ActorId, Cname = ActorName, Shift = dto.ShiftType.Trim().ToUpperInvariant(),
            OpenCash = dto.OpeningPettyCash, SysCash = dto.SystemCashTotal, ActCash = dto.ActualCashCounted,
            Diff = diff, Recv = dto.ReceiverName, Status = diff == 0m ? "BALANCED" : "DIFF_ALERT", Note = dto.Note, Now = now
        });

        return ApiResponse<object>.Success(new { id, shiftNo, diffAmount = diff, status = diff == 0m ? "BALANCED" : "DIFF_ALERT" });
    }
}
