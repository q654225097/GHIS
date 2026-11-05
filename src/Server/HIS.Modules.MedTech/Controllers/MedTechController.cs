using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.MedTech.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.MedTech.Controllers;

[ApiController]
[Route("api/v4/medtech")]
public class MedTechController : ControllerBase
{
    private readonly IRepository<MtcServiceRequest> _requestRepo;
    private readonly IRepository<MtcSurgerySchedule> _surgeryRepo;
    private readonly IRepository<MtcBloodRequest> _bloodRepo;
    private readonly IMedTechRoutingContract _routingService;

    public MedTechController(
        IRepository<MtcServiceRequest> requestRepo,
        IRepository<MtcSurgerySchedule> surgeryRepo,
        IRepository<MtcBloodRequest> bloodRepo,
        IMedTechRoutingContract routingService)
    {
        _requestRepo = requestRepo;
        _surgeryRepo = surgeryRepo;
        _bloodRepo = bloodRepo;
        _routingService = routingService;
    }

    public sealed record CreateRequestDto(
        string SourceType,
        string SourceVisitOrOrderId,
        string PatientId,
        string OrderDeptId,
        string? OrderWardId,
        string ServiceItemCode,
        decimal FeeAmount
    );

    [HttpPost("requests")]
    public async Task<ApiResponse<MtcServiceRequest>> CreateRequest([FromBody] CreateRequestDto req)
    {
        var routing = await _routingService.ResolveRoutingAsync("CAMPUS-01", req.SourceType, req.OrderDeptId, req.ServiceItemCode, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        var item = new MtcServiceRequest
        {
            RequestNo = "MTC" + DateTime.UtcNow.ToString("yyMMddHHmmssfff"),
            SourceType = req.SourceType,
            SourceVisitId = req.SourceType == "OPD" ? req.SourceVisitOrOrderId : null,
            SourceOrderId = req.SourceType == "IPD" ? req.SourceVisitOrOrderId : null,
            PatientId = req.PatientId,
            OrderDeptId = req.OrderDeptId,
            OrderWardId = req.OrderWardId,
            ExecMedTechDeptId = routing?.ExecMedTechDeptId ?? "DEPT-MTC-LAB",
            MedTechSubType = routing?.MedTechSubType ?? "LAB",
            ExecRoomCode = routing?.ExecRoomCode,
            ServiceItemCode = req.ServiceItemCode,
            FeeAmount = req.FeeAmount,
            IsFeeConfirmed = 0,
            Status = 0
        };
        await _requestRepo.InsertAsync(item);

        return ApiResponse<MtcServiceRequest>.Success(item, "医技申请单开立并完成智能路由分发");
    }

    [HttpPost("requests/{reqId}/confirm-fee-and-receive")]
    public async Task<ApiResponse<MtcServiceRequest>> ReceiveSampleAndConfirmFee(string reqId)
    {
        var req = await _requestRepo.GetByIdAsync(reqId);
        if (req == null) return ApiResponse<MtcServiceRequest>.Fail(404, "医技申请单不存在");

        req.IsFeeConfirmed = 1; // Locked from refund
        req.Status = 2;         // Received
        req.ReceivedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _requestRepo.UpdateAsync(req);

        return ApiResponse<MtcServiceRequest>.Success(req, "标本已核收并完成确费，该项目已锁定禁止直接退费");
    }

    [HttpPost("requests/{reqId}/cancel")]
    public async Task<ApiResponse<bool>> CancelRequest(string reqId)
    {
        var req = await _requestRepo.GetByIdAsync(reqId);
        if (req == null) return ApiResponse<bool>.Fail(404, "医技申请单不存在");

        // GATE-06: Check if fee confirmed
        if (req.IsFeeConfirmed == 1)
        {
            return ApiResponse<bool>.Fail(400, "医技单已扫码核收确费（is_fee_confirmed=1），禁止直接撤单退费！需医技科室出具冲正退检证明");
        }

        req.Status = 4; // Cancelled
        await _requestRepo.UpdateAsync(req);
        return ApiResponse<bool>.Success(true, "医技申请单撤销成功");
    }

    public sealed record SurgeryCheckDto(
        string ScheduleId,
        bool SignInOk,
        bool TimeOutOk,
        bool SignOutOk,
        bool InstrumentCountBalanced
    );

    [HttpPost("surgeries/safety-check")]
    public async Task<ApiResponse<MtcSurgerySchedule>> RecordSurgerySafetyCheck([FromBody] SurgeryCheckDto req)
    {
        var surg = await _surgeryRepo.GetByIdAsync(req.ScheduleId);
        if (surg == null) return ApiResponse<MtcSurgerySchedule>.Fail(404, "手术排班不存在");

        surg.SignInOk = (short)(req.SignInOk ? 1 : 0);
        surg.TimeOutOk = (short)(req.TimeOutOk ? 1 : 0);
        surg.SignOutOk = (short)(req.SignOutOk ? 1 : 0);
        surg.InstrumentCountBalanced = (short)(req.InstrumentCountBalanced ? 1 : 0);
        await _surgeryRepo.UpdateAsync(surg);

        return ApiResponse<MtcSurgerySchedule>.Success(surg, "手术三方安全核查记录已保存");
    }

    [HttpPost("surgeries/{scheduleId}/finish")]
    public async Task<ApiResponse<MtcSurgerySchedule>> FinishSurgery(string scheduleId)
    {
        var surg = await _surgeryRepo.GetByIdAsync(scheduleId);
        if (surg == null) return ApiResponse<MtcSurgerySchedule>.Fail(404, "手术排班不存在");

        // GATE-05 (2):
        if (surg.SignInOk != 1 || surg.TimeOutOk != 1 || surg.SignOutOk != 1 || surg.InstrumentCountBalanced != 1)
        {
            return ApiResponse<MtcSurgerySchedule>.Fail(400, "WHO 三方安全核查（Sign-in/Time-out/Sign-out）未全部通过或器械清点不平衡，严禁结束手术转出！");
        }

        surg.Status = 2; // Finished
        await _surgeryRepo.UpdateAsync(surg);
        return ApiResponse<MtcSurgerySchedule>.Success(surg, "手术已顺利完成并准予转入 PACU 复苏");
    }

    public sealed record DualCheckBloodDispenseDto(
        string RequestId,
        string DispenserId,
        string DualCheckDispenserId,
        string BagBarcode
    );

    [HttpPost("blood/dual-dispense")]
    public async Task<ApiResponse<MtcBloodRequest>> DualCheckBloodDispense([FromBody] DualCheckBloodDispenseDto req)
    {
        var item = await _bloodRepo.GetByIdAsync(req.RequestId);
        if (item == null) return ApiResponse<MtcBloodRequest>.Fail(404, "输血申请不存在");

        if (string.IsNullOrWhiteSpace(req.DispenserId) || string.IsNullOrWhiteSpace(req.DualCheckDispenserId))
        {
            return ApiResponse<MtcBloodRequest>.Fail(400, "输血发血必须具备双人电子签名核验");
        }

        item.DispensedById = req.DispenserId;
        item.DualCheckDispenserId = req.DualCheckDispenserId;
        item.BagBarcode = req.BagBarcode;
        item.Status = 2; // Dispensed
        await _bloodRepo.UpdateAsync(item);

        return ApiResponse<MtcBloodRequest>.Success(item, "双人扫码核对发血完成");
    }
}

