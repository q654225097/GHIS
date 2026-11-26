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
[RequireHisRole("ADMIN", "OPD_DOC", "PHARMACIST")]
[Route("api/v4/opd/tcm")]
public sealed class OpdTcmPrescriptionController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _userSession;
    private readonly IAuditTrailContract? _auditTrail;

    public OpdTcmPrescriptionController(
        IDbConnectionFactory connFactory,
        IUserSession? userSession = null,
        IAuditTrailContract? auditTrail = null)
    {
        _connFactory = connFactory;
        _userSession = userSession;
        _auditTrail = auditTrail;
    }

    public sealed record TcmPrescriptionDto(
        string Id,
        string PrescriptionNo,
        string VisitId,
        string PatientId,
        string DoctorId,
        string DeptId,
        string TcmDiagnosis,
        string? TcmSyndrome,
        int DoseCount,
        string UsageRoute,
        string Frequency,
        int IsDecoctionRequired,
        int DecoctionPackCount,
        decimal DecoctionUnitFee,
        decimal TotalAmount,
        string? PaymentOrderId,
        int Status, // 0: 草稿, 1: 已签署待审, 2: 审核通过待缴费, 3: 已缴费待调剂, 4: 调剂代煎完成
        long CreatedAtMs,
        List<TcmItemDto>? Items = null
    );

    public sealed record TcmItemDto(
        string Id,
        string TcmPrescriptionId,
        string HerbId,
        string HerbName,
        decimal SingleDoseG,
        decimal TotalWeightG,
        decimal UnitPricePerG,
        decimal TotalItemPrice,
        string? SpecialUsage,
        int SortOrder
    );

    public sealed record CreateTcmPrescriptionRequestDto(
        string VisitId,
        string PatientId,
        string TcmDiagnosis,
        string? TcmSyndrome,
        int DoseCount,
        string UsageRoute,
        string Frequency,
        bool IsDecoctionRequired,
        int DecoctionPackCount,
        decimal DecoctionUnitFee,
        List<CreateTcmItemDto> Items
    );

    public sealed record CreateTcmItemDto(
        string HerbId,
        string HerbName,
        decimal SingleDoseG,
        decimal UnitPricePerG,
        string? SpecialUsage
    );

    [HttpGet("prescriptions/by-visit/{visitId}")]
    public async Task<ApiResponse<IReadOnlyList<TcmPrescriptionDto>>> GetByVisit(string visitId)
    {
        using var conn = _connFactory.CreateConnection();
        var rxList = (await conn.QueryAsync<TcmPrescriptionDto>(@"
            SELECT id AS Id, prescription_no AS PrescriptionNo, visit_id AS VisitId,
                   patient_id AS PatientId, doctor_id AS DoctorId, dept_id AS DeptId,
                   tcm_diagnosis AS TcmDiagnosis, tcm_syndrome AS TcmSyndrome,
                   dose_count AS DoseCount, usage_route AS UsageRoute, frequency AS Frequency,
                   is_decoction_required AS IsDecoctionRequired, decoction_pack_count AS DecoctionPackCount,
                   decoction_unit_fee AS DecoctionUnitFee, total_amount AS TotalAmount,
                   payment_order_id AS PaymentOrderId, status AS Status, created_at_ms AS CreatedAtMs
            FROM opd_tcm_prescriptions
            WHERE visit_id = @VisitId
            ORDER BY created_at_ms DESC", new { VisitId = visitId })).ToList();

        if (rxList.Count > 0)
        {
            var rxIds = rxList.Select(x => x.Id).ToList();
            var items = (await conn.QueryAsync<TcmItemDto>(@"
                SELECT id AS Id, tcm_prescription_id AS TcmPrescriptionId, herb_id AS HerbId,
                       herb_name AS HerbName, single_dose_g AS SingleDoseG, total_weight_g AS TotalWeightG,
                       unit_price_per_g AS UnitPricePerG, total_item_price AS TotalItemPrice,
                       special_usage AS SpecialUsage, sort_order AS SortOrder
                FROM opd_tcm_prescription_items
                WHERE tcm_prescription_id IN @Ids
                ORDER BY sort_order ASC", new { Ids = rxIds })).ToList();

            var itemMap = items.GroupBy(x => x.TcmPrescriptionId).ToDictionary(g => g.Key, g => g.ToList());
            rxList = rxList.Select(r => r with
            {
                Items = itemMap.TryGetValue(r.Id, out var itms) ? itms : new List<TcmItemDto>()
            }).ToList();
        }

        return ApiResponse<IReadOnlyList<TcmPrescriptionDto>>.Success(rxList);
    }

    [HttpPost("prescriptions")]
    public async Task<ApiResponse<TcmPrescriptionDto>> CreatePrescription(
        [FromBody] CreateTcmPrescriptionRequestDto req,
        CancellationToken ct = default)
    {
        if (req.Items == null || req.Items.Count == 0)
            return ApiResponse<TcmPrescriptionDto>.Fail(400, "中药饮片处方必须包含至少一味中药");
        if (req.DoseCount <= 0 || req.DoseCount > 30)
            return ApiResponse<TcmPrescriptionDto>.Fail(400, "中药处方剂数必须在 1 到 30 付之间");
        if (string.IsNullOrWhiteSpace(req.TcmDiagnosis))
            return ApiResponse<TcmPrescriptionDto>.Fail(400, "中医诊断不能为空");

        string doctorId = _userSession?.UserId ?? "DOC-TCM";
        string deptId = _userSession?.ActiveDeptId ?? "DEPT-TCM";
        long now = SystemTime.UtcNowMs;
        string rxId = Guid.NewGuid().ToString("D");
        string rxNo = "TCM" + SystemTime.ShanghaiNow.ToString("yyyyMMddHHmmss") + Random.Shared.Next(10, 99);

        decimal herbsSubtotal = 0;
        var computedItems = new List<(string Id, CreateTcmItemDto Dto, decimal TotalWeight, decimal ItemPrice, int Seq)>();
        int seq = 1;
        foreach (var item in req.Items)
        {
            if (item.SingleDoseG <= 0)
                return ApiResponse<TcmPrescriptionDto>.Fail(400, $"中药味 [{item.HerbName}] 单剂克数必须大于0");
            decimal totalWeight = Math.Round(item.SingleDoseG * req.DoseCount, 2);
            decimal itemPrice = Math.Round(totalWeight * item.UnitPricePerG, 2);
            herbsSubtotal += itemPrice;
            computedItems.Add((Guid.NewGuid().ToString("D"), item, totalWeight, itemPrice, seq++));
        }

        decimal decoctionFee = req.IsDecoctionRequired ? Math.Round(req.DecoctionUnitFee * req.DoseCount, 2) : 0m;
        decimal totalAmount = herbsSubtotal + decoctionFee;

        using var conn = _connFactory.CreateConnection();
        if (!string.IsNullOrWhiteSpace(req.TcmDiagnosis))
        {
            var tcmMdm = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT is_active AS IsActive FROM cfg_tcm_diagnoses WHERE tcm_code = @Diag OR tcm_name = @Diag LIMIT 1",
                new { Diag = req.TcmDiagnosis.Trim() });
            if (tcmMdm != null && Convert.ToInt32(tcmMdm.IsActive) == 0)
                return ApiResponse<TcmPrescriptionDto>.Fail(409, $"中医病证字典 [{req.TcmDiagnosis}] 已停用，禁止开立草药处方");
        }
        await _connFactory.ExecuteInTransactionAsync(async transCt =>
        {
            await conn.ExecuteAsync(@"
                INSERT INTO opd_tcm_prescriptions
                (id, prescription_no, visit_id, patient_id, doctor_id, dept_id, tcm_diagnosis, tcm_syndrome,
                 dose_count, usage_route, frequency, is_decoction_required, decoction_pack_count, decoction_unit_fee,
                 total_amount, status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES
                (@Id, @No, @VisitId, @PatientId, @DocId, @DeptId, @Diag, @Syn,
                 @Count, @Route, @Freq, @DecReq, @PackCount, @DecFee,
                 @Total, 1, 1, @Now, @User, @Now, @User, 1)",
                new
                {
                    Id = rxId,
                    No = rxNo,
                    VisitId = req.VisitId,
                    PatientId = req.PatientId,
                    DocId = doctorId,
                    DeptId = deptId,
                    Diag = req.TcmDiagnosis,
                    Syn = req.TcmSyndrome,
                    Count = req.DoseCount,
                    Route = req.UsageRoute ?? "水煎服",
                    Freq = req.Frequency ?? "每日1剂早晚分服",
                    DecReq = req.IsDecoctionRequired ? 1 : 0,
                    PackCount = req.DecoctionPackCount,
                    DecFee = req.DecoctionUnitFee,
                    Total = totalAmount,
                    Now = now,
                    User = doctorId
                });

            foreach (var ci in computedItems)
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO opd_tcm_prescription_items
                    (id, tcm_prescription_id, herb_id, herb_name, single_dose_g, total_weight_g,
                     unit_price_per_g, total_item_price, special_usage, sort_order, schema_version,
                     created_at_ms, updated_at_ms, version_no)
                    VALUES
                    (@Id, @RxId, @HerbId, @HerbName, @SingleG, @TotalG,
                     @PricePerG, @ItemTotal, @Usage, @Sort, 1,
                     @Now, @Now, 1)",
                    new
                    {
                        Id = ci.Id,
                        RxId = rxId,
                        HerbId = ci.Dto.HerbId,
                        HerbName = ci.Dto.HerbName,
                        SingleG = ci.Dto.SingleDoseG,
                        TotalG = ci.TotalWeight,
                        PricePerG = ci.Dto.UnitPricePerG,
                        ItemTotal = ci.ItemPrice,
                        Usage = ci.Dto.SpecialUsage,
                        Sort = ci.Seq,
                        Now = now
                    });
            }
        });

        var created = (await GetByVisit(req.VisitId)).Data?.FirstOrDefault(x => x.Id == rxId);
        return ApiResponse<TcmPrescriptionDto>.Success(created!, $"中药处方开具成功，共 {req.Items.Count} 味，{req.DoseCount} 剂{(req.IsDecoctionRequired ? "（含代煎服务）" : "")}");
    }

    [HttpPost("prescriptions/{id}/dispense")]
    public async Task<ApiResponse<bool>> DispenseTcmPrescription(string id)
    {
        using var conn = _connFactory.CreateConnection();
        long now = SystemTime.UtcNowMs;
        string pharmacistId = _userSession?.UserId ?? "PHARMACIST-TCM";

        int affected = await conn.ExecuteAsync(@"
            UPDATE opd_tcm_prescriptions
            SET status = 4, updated_at_ms = @Now, updated_by = @User, version_no = version_no + 1
            WHERE id = @Id AND status IN (2, 3)",
            new { Id = id, Now = now, User = pharmacistId });

        if (affected == 0)
            return ApiResponse<bool>.Fail(409, "当前处方状态不支持调剂或已完成调剂");

        return ApiResponse<bool>.Success(true, "中药饮片调剂完成，煎药包装标签已生成");
    }
}
