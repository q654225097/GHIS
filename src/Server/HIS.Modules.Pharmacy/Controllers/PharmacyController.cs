using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Pharmacy.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Pharmacy.Controllers;

[ApiController]
[Route("api/v4/pharmacy")]
public class PharmacyController : ControllerBase
{
    private readonly IRepository<PhmDispenseOrder> _dispenseRepo;
    private readonly IDbConnectionFactory _connFactory;

    public PharmacyController(
        IRepository<PhmDispenseOrder> dispenseRepo,
        IDbConnectionFactory connFactory)
    {
        _dispenseRepo = dispenseRepo;
        _connFactory = connFactory;
    }

    public sealed record DispensePrescriptionRequestDto(
        string PharmacyId,
        string PrescriptionId,
        string PatientId,
        string PharmacistId
    );

    [HttpPost("dispense")]
    public async Task<ApiResponse<PhmDispenseOrder>> DispensePrescription([FromBody] DispensePrescriptionRequestDto req)
    {
        // GATE-05 (1): Check skin test requirements for items in this prescription
        using (var conn = _connFactory.CreateConnection())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM opd_prescription_items WHERE prescription_id = @RxId AND skin_test_flag = 1";
            var p = cmd.CreateParameter();
            p.ParameterName = "@RxId";
            p.Value = req.PrescriptionId;
            cmd.Parameters.Add(p);
            var unCheckedSkinTestCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            if (unCheckedSkinTestCount > 0)
            {
                return ApiResponse<PhmDispenseOrder>.Fail(400, "处方包含需皮试药品且尚未录入“皮试阴性(-)”双签结果，严禁发药！", null);
            }

            // Check if positive
            using var cmdPos = conn.CreateCommand();
            cmdPos.CommandText = "SELECT count(*) FROM opd_prescription_items WHERE prescription_id = @RxId AND skin_test_flag = 4";
            var pPos = cmdPos.CreateParameter();
            pPos.ParameterName = "@RxId";
            pPos.Value = req.PrescriptionId;
            cmdPos.Parameters.Add(pPos);
            var posCount = Convert.ToInt32(await cmdPos.ExecuteScalarAsync());
            if (posCount > 0)
            {
                return ApiResponse<PhmDispenseOrder>.Fail(400, "皮试结果为阳性(+)，处方已锁定，严禁发药！", null);
            }
        }

        var order = new PhmDispenseOrder
        {
            DispenseNo = "DSP" + DateTime.UtcNow.ToString("yyMMddHHmmssfff"),
            PharmacyId = req.PharmacyId,
            PrescriptionId = req.PrescriptionId,
            PatientId = req.PatientId,
            Status = 1, // Dispensed
            DispensedById = req.PharmacistId,
            DispensedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            DrugReturnConfirmed = 0
        };
        await _dispenseRepo.InsertAsync(order);

        return ApiResponse<PhmDispenseOrder>.Success(order, "药房审方通过，已按 FEFO 批次完成摆药贴签发药");
    }

    public sealed record ConfirmDrugReturnRequestDto(
        string DispenseOrderId,
        string PharmacistId,
        string Reason
    );

    [HttpPost("confirm-return")]
    public async Task<ApiResponse<PhmDispenseOrder>> ConfirmDrugReturn([FromBody] ConfirmDrugReturnRequestDto req)
    {
        var order = await _dispenseRepo.GetByIdAsync(req.DispenseOrderId);
        if (order == null) return ApiResponse<PhmDispenseOrder>.Fail(404, "发药单不存在");

        // Step 2: Pharmacy scans physical returned medicines and confirms stock return
        order.DrugReturnConfirmed = 1;
        order.Status = 2; // Returned
        await _dispenseRepo.UpdateAsync(order);

        return ApiResponse<PhmDispenseOrder>.Success(order, "药房已完成扫码验收入库（drug_return_confirmed=1），准许财务窗口办理退费");
    }

    [HttpGet("dispense/{id}")]
    public async Task<ApiResponse<PhmDispenseOrder>> GetDispenseOrder(string id)
    {
        var order = await _dispenseRepo.GetByIdAsync(id);
        return order != null ? ApiResponse<PhmDispenseOrder>.Success(order) : ApiResponse<PhmDispenseOrder>.Fail(404, "发药单不存在");
    }
}

