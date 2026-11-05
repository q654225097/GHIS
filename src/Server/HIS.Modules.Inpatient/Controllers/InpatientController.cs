using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Inpatient.Models;
using HIS.Modules.Inpatient.Services;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Inpatient.Controllers;

[ApiController]
[Route("api/v4/ipd")]
public class InpatientController : ControllerBase
{
    private readonly IRepository<IpdBed> _bedRepo;
    private readonly IRepository<IpdEncounter> _encounterRepo;
    private readonly IRepository<IpdTransferLog> _transferRepo;
    private readonly DischargeChecklistService _dischargeService;
    private readonly IUserSession? _userSession;

    public InpatientController(
        IRepository<IpdBed> bedRepo,
        IRepository<IpdEncounter> encounterRepo,
        IRepository<IpdTransferLog> transferRepo,
        DischargeChecklistService dischargeService,
        IUserSession? userSession = null)
    {
        _bedRepo = bedRepo;
        _encounterRepo = encounterRepo;
        _transferRepo = transferRepo;
        _dischargeService = dischargeService;
        _userSession = userSession;
    }

    public sealed record AdmitPatientRequestDto(
        string PatientId,
        string CampusId,
        string AdmitDeptId,
        string AdmitWardId,
        string BedCode,
        bool IsBorrowedBed,
        decimal PrepaymentAmount = 2000.00m
    );

    [HttpPost("admit")]
    public async Task<ApiResponse<IpdEncounter>> AdmitPatient([FromBody] AdmitPatientRequestDto req)
    {
        var token = Guid.NewGuid().ToString("N");
        var encounter = new IpdEncounter
        {
            AdmissionNo = "IPD" + DateTime.UtcNow.ToString("yyMMddHHmmssfff"),
            PatientId = req.PatientId,
            CampusId = req.CampusId,
            AdmitDeptId = req.AdmitDeptId,
            AdmitWardId = req.AdmitWardId,
            IsBorrowedBed = (short)(req.IsBorrowedBed ? 1 : 0),
            BedCode = req.BedCode,
            ActiveBedToken = token,
            PrepaymentBalance = req.PrepaymentAmount,
            Status = 1 // Admitted
        };
        await _encounterRepo.InsertAsync(encounter);

        // Update bed status
        var beds = await _bedRepo.QueryAsync(
            "SELECT * FROM ipd_beds WHERE ward_id = @WardId AND bed_code = @BedCode",
            new { WardId = req.AdmitWardId, BedCode = req.BedCode });
        if (beds.Count > 0)
        {
            var bed = beds[0];
            bed.BedStatus = 1; // Occupied
            bed.CurrentEncounterId = encounter.Id;
            await _bedRepo.UpdateAsync(bed);
        }

        return ApiResponse<IpdEncounter>.Success(encounter, req.IsBorrowedBed ? "跨病区借床入院登记成功" : "常规入院分床成功");
    }

    [HttpGet("encounters/{encounterId}/access-check")]
    public async Task<IActionResult> CheckClinicalAccess(string encounterId, [FromQuery] string deptCategory, [FromQuery] string deptOrWardId)
    {
        var enc = await _encounterRepo.GetByIdAsync(encounterId);
        if (enc == null) return NotFound(new { Code = 404, Message = "未找到住院记录" });

        // GATE-02:
        // Doctors verify against admit_dept_id
        // Nurses verify against admit_ward_id
        if (deptCategory.Equals(nameof(DepartmentCategory.IPD), StringComparison.OrdinalIgnoreCase))
        {
            if (!enc.AdmitDeptId.Equals(deptOrWardId, StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(403, new { Code = 403, Message = "无权限：非管床医疗科室医生禁止访问借床患者医疗档案" });
            }
        }
        else if (deptCategory.Equals(nameof(DepartmentCategory.WARD), StringComparison.OrdinalIgnoreCase))
        {
            if (!enc.AdmitWardId.Equals(deptOrWardId, StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(403, new { Code = 403, Message = "无权限：非所在物理护理病区护士禁止执行床旁操作" });
            }
        }

        return Ok(new { Code = 200, Message = "科室执业范围鉴权通过", Data = enc });
    }

    [HttpPost("encounters/{encounterId}/discharge-check")]
    public async Task<ApiResponse<DischargeCheckResult>> VerifyDischargeChecklist(string encounterId)
    {
        var result = await _dischargeService.PerformSixItemCheckAsync(encounterId);
        if (!result.AllPassed)
        {
            return ApiResponse<DischargeCheckResult>.Fail(400, "出院关床六项核查未通过，存在未闭环业务", result);
        }
        return ApiResponse<DischargeCheckResult>.Success(result, "出院关床六项核查全部通过，允许释放床位并转结算");
    }

    [HttpPost("encounters/{encounterId}/close-ward-bed")]
    public async Task<ApiResponse<IpdEncounter>> CloseWardBed(string encounterId)
    {
        var enc = await _encounterRepo.GetByIdAsync(encounterId);
        if (enc == null) return ApiResponse<IpdEncounter>.Fail(404, "住院记录不存在");

        var check = await _dischargeService.PerformSixItemCheckAsync(encounterId);
        if (!check.AllPassed)
        {
            return ApiResponse<IpdEncounter>.Fail(400, $"六项核查未通过，禁止关床：{string.Join("; ", check.FailedItems)}");
        }

        enc.Status = 2; // DischargePending
        await _encounterRepo.UpdateAsync(enc);

        // Free bed
        if (!string.IsNullOrEmpty(enc.BedCode))
        {
            var beds = await _bedRepo.QueryAsync(
                "SELECT * FROM ipd_beds WHERE ward_id = @WardId AND bed_code = @BedCode",
                new { WardId = enc.AdmitWardId, BedCode = enc.BedCode });
            if (beds.Count > 0)
            {
                var b = beds[0];
                b.BedStatus = 0; // Free
                b.CurrentEncounterId = null;
                await _bedRepo.UpdateAsync(b);
            }
        }

        return ApiResponse<IpdEncounter>.Success(enc, "出院关床成功，床位已释放，进入待财务结算状态");
    }

    [HttpPost("encounters/{encounterId}/recall")]
    public async Task<ApiResponse<IpdEncounter>> RecallDischargedEncounter(string encounterId)
    {
        var enc = await _encounterRepo.GetByIdAsync(encounterId);
        if (enc == null) return ApiResponse<IpdEncounter>.Fail(404, "住院记录不存在");

        enc.Status = 1; // Restore to Admitted
        await _encounterRepo.UpdateAsync(enc);

        return ApiResponse<IpdEncounter>.Success(enc, "住院出院召回（Recall）成功，原住院号与病历账务无损恢复在院状态");
    }
}

