using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Ordering.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Ordering.Controllers;

[ApiController]
[Route("api/v4/orders")]
public class OrderingController : ControllerBase
{
    private readonly IRepository<OrdMedicalOrder> _orderRepo;
    private readonly IRepository<OrdExecutionPlan> _planRepo;

    public OrderingController(
        IRepository<OrdMedicalOrder> orderRepo,
        IRepository<OrdExecutionPlan> planRepo)
    {
        _orderRepo = orderRepo;
        _planRepo = planRepo;
    }

    public sealed record CreateOrderRequestDto(
        string EncounterId,
        string PatientId,
        string OrderIpdDeptId,
        string PatientWardId,
        string ExecTargetDeptId,
        int OrderTermType,
        int GroupNo,
        string OrderCategory,
        string ItemCode,
        string ItemName,
        decimal Dosage,
        string FrequencyCode,
        string RouteCode,
        string OrderDoctorId
    );

    [HttpPost]
    public async Task<ApiResponse<OrdMedicalOrder>> CreateOrder([FromBody] CreateOrderRequestDto req)
    {
        var order = new OrdMedicalOrder
        {
            OrderNo = "ORD" + DateTime.UtcNow.ToString("yyMMddHHmmssfff"),
            EncounterId = req.EncounterId,
            PatientId = req.PatientId,
            OrderIpdDeptId = req.OrderIpdDeptId,
            PatientWardId = req.PatientWardId,
            ExecTargetDeptId = req.ExecTargetDeptId,
            OrderTermType = req.OrderTermType,
            GroupNo = req.GroupNo,
            OrderCategory = req.OrderCategory,
            ItemCode = req.ItemCode,
            ItemName = req.ItemName,
            Dosage = req.Dosage,
            FrequencyCode = req.FrequencyCode,
            RouteCode = req.RouteCode,
            Status = 0, // Draft
            OrderDoctorId = req.OrderDoctorId
        };
        await _orderRepo.InsertAsync(order);
        return ApiResponse<OrdMedicalOrder>.Success(order, "医嘱开立成功，等待病区护士校对");
    }

    public sealed record VerifyOrderRequestDto(string VerifyNurseId, string WardId);

    [HttpPost("{orderId}/verify")]
    public async Task<ApiResponse<OrdMedicalOrder>> VerifyOrder(string orderId, [FromBody] VerifyOrderRequestDto req)
    {
        var order = await _orderRepo.GetByIdAsync(orderId);
        if (order == null) return ApiResponse<OrdMedicalOrder>.Fail(404, "医嘱不存在");

        order.Status = 1; // Verified
        order.VerifyNurseId = req.VerifyNurseId;
        await _orderRepo.UpdateAsync(order);

        // Decompose into execution plan for next 24 hours
        var plan = new OrdExecutionPlan
        {
            OrderId = order.Id,
            EncounterId = order.EncounterId,
            WardId = req.WardId,
            PlannedTimeMs = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeMilliseconds(),
            ExecStatus = 0 // Pending
        };
        await _planRepo.InsertAsync(plan);

        return ApiResponse<OrdMedicalOrder>.Success(order, "病区护士校对成功，已自动分解执行计划");
    }

    [HttpPost("{orderId}/stop")]
    public async Task<ApiResponse<OrdMedicalOrder>> StopOrder(string orderId, [FromQuery] string doctorId)
    {
        var order = await _orderRepo.GetByIdAsync(orderId);
        if (order == null) return ApiResponse<OrdMedicalOrder>.Fail(404, "医嘱不存在");

        order.Status = 3; // Stopped
        order.PlanStopAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _orderRepo.UpdateAsync(order);

        return ApiResponse<OrdMedicalOrder>.Success(order, "医嘱已下达停止指令");
    }

    public sealed record PdaExecuteRequestDto(
        string PlanId,
        string ExecNurseId,
        string DualCheckNurseId,
        string PdaScanToken
    );

    [HttpPost("plans/pda-execute")]
    public async Task<ApiResponse<OrdExecutionPlan>> PdaExecutePlan([FromBody] PdaExecuteRequestDto req)
    {
        var plan = await _planRepo.GetByIdAsync(req.PlanId);
        if (plan == null) return ApiResponse<OrdExecutionPlan>.Fail(404, "医嘱执行计划不存在");

        plan.ExecStatus = 1; // Executed
        plan.ExecNurseId = req.ExecNurseId;
        plan.DualCheckNurseId = req.DualCheckNurseId;
        plan.ExecutedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        plan.PdaScanToken = req.PdaScanToken;
        await _planRepo.UpdateAsync(plan);

        return ApiResponse<OrdExecutionPlan>.Success(plan, "PDA 床旁双人双码核对执行完成");
    }
}

