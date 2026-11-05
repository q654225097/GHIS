using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Outpatient.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Outpatient.Controllers;

[ApiController]
[Route("api/v4/opd")]
public class OutpatientController : ControllerBase
{
    private readonly IRepository<OpdScheduleSlot> _slotRepo;
    private readonly IRepository<OpdAppointment> _appointRepo;
    private readonly IRepository<OpdVisit> _visitRepo;
    private readonly IRepository<OpdTriageQueue> _queueRepo;
    private readonly IRepository<OpdPrescription> _rxRepo;
    private readonly IRepository<OpdPrescriptionItem> _rxItemRepo;

    public OutpatientController(
        IRepository<OpdScheduleSlot> slotRepo,
        IRepository<OpdAppointment> appointRepo,
        IRepository<OpdVisit> visitRepo,
        IRepository<OpdTriageQueue> queueRepo,
        IRepository<OpdPrescription> rxRepo,
        IRepository<OpdPrescriptionItem> rxItemRepo)
    {
        _slotRepo = slotRepo;
        _appointRepo = appointRepo;
        _visitRepo = visitRepo;
        _queueRepo = queueRepo;
        _rxRepo = rxRepo;
        _rxItemRepo = rxItemRepo;
    }

    public sealed record LockSlotRequestDto(string PatientId, string ScheduleId);

    [HttpPost("appointments/lock")]
    public async Task<ApiResponse<OpdAppointment>> LockAppointmentSlot([FromBody] LockSlotRequestDto req)
    {
        var slot = await _slotRepo.GetByIdAsync(req.ScheduleId);
        if (slot == null || slot.SlotStatus != 1)
            return ApiResponse<OpdAppointment>.Fail(400, "号源不存在或不可用");

        if (slot.ReservedCount >= slot.TotalCapacity)
            return ApiResponse<OpdAppointment>.Fail(400, "该时段号源已满");

        var token = Guid.NewGuid().ToString("N");
        slot.ReservedCount++;
        if (slot.ReservedCount >= slot.TotalCapacity) slot.SlotStatus = 2; // Full
        await _slotRepo.UpdateAsync(slot);

        var appoint = new OpdAppointment
        {
            AppointmentNo = "APT" + DateTime.UtcNow.ToString("yyMMddHHmmssfff"),
            PatientId = req.PatientId,
            ScheduleId = req.ScheduleId,
            ActiveUniqueToken = token,
            QueueNo = slot.ReservedCount,
            Status = 0, // Reserved (locked)
            LockExpiresAtMs = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds()
        };
        await _appointRepo.InsertAsync(appoint);

        return ApiResponse<OpdAppointment>.Success(appoint, "锁号成功，请在10分钟内完成缴费确认");
    }

    [HttpPost("appointments/{appointId}/confirm-and-triage")]
    public async Task<ApiResponse<OpdVisit>> ConfirmAndTriage(string appointId)
    {
        var apt = await _appointRepo.GetByIdAsync(appointId);
        if (apt == null) return ApiResponse<OpdVisit>.Fail(404, "预约不存在");

        apt.Status = 1; // Confirmed
        await _appointRepo.UpdateAsync(apt);

        var slot = await _slotRepo.GetByIdAsync(apt.ScheduleId);

        // Generate visit
        var visit = new OpdVisit
        {
            VisitNo = "VIS" + DateTime.UtcNow.ToString("yyMMddHHmmssfff"),
            CampusId = slot?.CampusId ?? "CAMPUS-01",
            OpdDeptId = slot?.OpdDeptId ?? "DEPT-OPD-01",
            ClinicRoomId = slot?.ClinicRoomId,
            PatientId = apt.PatientId,
            ScheduleId = apt.ScheduleId,
            DoctorId = slot?.DoctorId ?? "DOC-01",
            Status = 1
        };
        await _visitRepo.InsertAsync(visit);

        // Enter triage queue
        var queue = new OpdTriageQueue
        {
            VisitId = visit.Id,
            OpdDeptId = visit.OpdDeptId,
            ClinicRoomId = visit.ClinicRoomId,
            PriorityScore = 100,
            QueueStatus = 1
        };
        await _queueRepo.InsertAsync(queue);

        return ApiResponse<OpdVisit>.Success(visit, "报到签到入候诊池成功");
    }

    public sealed record CreatePrescriptionRequestDto(
        string VisitId,
        string PatientId,
        string OpdDeptId,
        string DoctorId,
        string TargetPharmacyId,
        List<OpdPrescriptionItem> Items
    );

    [HttpPost("prescriptions")]
    public async Task<ApiResponse<OpdPrescription>> CreatePrescription([FromBody] CreatePrescriptionRequestDto req)
    {
        decimal total = req.Items.Sum(x => x.UnitPrice * x.TotalQuantity);
        var rx = new OpdPrescription
        {
            PrescriptionNo = "RX" + DateTime.UtcNow.ToString("yyMMddHHmmssfff"),
            VisitId = req.VisitId,
            PatientId = req.PatientId,
            OpdDeptId = req.OpdDeptId,
            DoctorId = req.DoctorId,
            TargetPharmacyId = req.TargetPharmacyId,
            TotalAmount = total,
            Status = 0 // Draft
        };
        await _rxRepo.InsertAsync(rx);

        foreach (var item in req.Items)
        {
            item.PrescriptionId = rx.Id;
            await _rxItemRepo.InsertAsync(item);
        }

        return ApiResponse<OpdPrescription>.Success(rx, "处方开立成功");
    }

    public sealed record PreAuditRequestDto(string PharmacistId, bool IsApproved, string? Remark = null);

    [HttpPost("prescriptions/{rxId}/pre-audit")]
    public async Task<ApiResponse<OpdPrescription>> PreAudit(string rxId, [FromBody] PreAuditRequestDto req)
    {
        var rx = await _rxRepo.GetByIdAsync(rxId);
        if (rx == null) return ApiResponse<OpdPrescription>.Fail(404, "处方不存在");

        if (req.IsApproved)
        {
            rx.Status = 1; // AuditedPassed
            rx.ReviewerPharmacistId = req.PharmacistId;
            rx.ReviewRemark = req.Remark ?? "前置审方通过";
        }
        else
        {
            rx.Status = 2; // Rejected
            rx.ReviewerPharmacistId = req.PharmacistId;
            rx.ReviewRemark = req.Remark ?? "审方不合格退回修改";
        }
        await _rxRepo.UpdateAsync(rx);

        return ApiResponse<OpdPrescription>.Success(rx, rx.Status == 1 ? "前置审方通过" : "前置审方驳回");
    }

    public sealed record SkinTestDualSignRequestDto(
        string ItemId,
        int Result, // 3=Negative(-), 4=Positive(+)
        string BatchNo,
        string CheckerId
    );

    [HttpPost("prescriptions/items/skin-test-sign")]
    public async Task<ApiResponse<OpdPrescriptionItem>> RecordSkinTestDualSign([FromBody] SkinTestDualSignRequestDto req)
    {
        var item = await _rxItemRepo.GetByIdAsync(req.ItemId);
        if (item == null) return ApiResponse<OpdPrescriptionItem>.Fail(404, "处方药品明细不存在");

        item.SkinTestFlag = req.Result;
        item.SkinTestBatchNo = req.BatchNo;
        item.SkinTestCheckerId = req.CheckerId;
        await _rxItemRepo.UpdateAsync(item);

        if (req.Result == 4) // Positive (+)
        {
            // Lock prescription immediately
            var rx = await _rxRepo.GetByIdAsync(item.PrescriptionId);
            if (rx != null)
            {
                rx.Status = 2; // Locked/Rejected
                rx.ReviewRemark = $"皮试阳性(+)强阻断，严禁执行！[药品：{item.DrugNameSnapshot}]";
                await _rxRepo.UpdateAsync(rx);
            }
            return ApiResponse<OpdPrescriptionItem>.Fail(400, "皮试阳性(+)，处方已强锁定，阻断发药并通知医生改方退药！", item);
        }

        return ApiResponse<OpdPrescriptionItem>.Success(item, "皮试阴性(-)双签核对完成，允许执行输液与发药");
    }
}

