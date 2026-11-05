using System.Text.Json;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Modules.Patient.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Patient.Controllers;

[ApiController]
[Route("api/v4/patients")]
public class PatientController : ControllerBase
{
    private readonly IRepository<PatPatient> _patientRepo;

    public PatientController(IRepository<PatPatient> patientRepo)
    {
        _patientRepo = patientRepo;
    }

    public sealed record RegisterPatientRequestDto(
        string FullName,
        string GenderCode,
        int BirthDateInt,
        string IdCardNo,
        string Phone,
        string? GuardianName = null,
        string? GuardianIdCardNo = null,
        List<string>? AllergyTags = null
    );

    [HttpPost("register")]
    public async Task<ApiResponse<PatPatient>> Register([FromBody] RegisterPatientRequestDto req)
    {
        var birthYear = req.BirthDateInt / 10000;
        var currentYear = DateTime.UtcNow.Year;
        var age = currentYear - birthYear;

        // WF-PAT-03: Guardian check for age < 14
        if (age < 14 && string.IsNullOrWhiteSpace(req.GuardianName))
        {
            return ApiResponse<PatPatient>.Fail(400, "年龄小于 14 周岁患者强制要求录入法定监护人信息");
        }

        var idCardHash = CryptoHelper.ComputeBlindIndex(req.IdCardNo);
        // Check duplicate
        var existing = await _patientRepo.QueryAsync(
            "SELECT * FROM pat_patients WHERE id_card_hash = @Hash AND is_active = 1",
            new { Hash = idCardHash });

        if (existing.Count > 0)
        {
            return ApiResponse<PatPatient>.Fail(409, $"该证件号码已建档，病历号为：{existing[0].MedicalRecordNo}", existing[0]);
        }

        var p = new PatPatient
        {
            MedicalRecordNo = "MRN" + DateTime.UtcNow.ToString("yyMMddHHmmssfff"),
            FullName = req.FullName,
            GenderCode = req.GenderCode,
            BirthDateInt = req.BirthDateInt,
            IdCardType = "RESIDENT_ID",
            IdCardCipher = CryptoHelper.EncryptAesGcm(req.IdCardNo),
            IdCardHash = idCardHash,
            PhoneCipher = CryptoHelper.EncryptAesGcm(req.Phone),
            PhoneMasked = CryptoHelper.MaskPhone(req.Phone),
            GuardianName = req.GuardianName,
            GuardianIdHash = req.GuardianIdCardNo != null ? CryptoHelper.ComputeBlindIndex(req.GuardianIdCardNo) : null,
            AllergyTagsJson = req.AllergyTags != null ? JsonSerializer.Serialize(req.AllergyTags) : null,
            IsTempEmergency = 0,
            IsActive = 1
        };

        await _patientRepo.InsertAsync(p);
        return ApiResponse<PatPatient>.Success(p, "实名档案建立成功");
    }

    [HttpPost("emergency-temp")]
    public async Task<ApiResponse<PatPatient>> CreateEmergencyTempPatient()
    {
        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        var p = new PatPatient
        {
            MedicalRecordNo = $"TEMP-{today}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}",
            FullName = "急诊三无患者(待实名)",
            GenderCode = "0",
            BirthDateInt = 20000101,
            IdCardType = "TEMP",
            IdCardHash = Guid.NewGuid().ToString("N"),
            IsTempEmergency = 1,
            IsActive = 1
        };

        await _patientRepo.InsertAsync(p);
        return ApiResponse<PatPatient>.Success(p, "三无急诊临时档案创建成功");
    }

    public sealed record MergeRequestDto(string TempPatientId, string TargetPatientId);

    [HttpPost("merge")]
    public async Task<ApiResponse<bool>> MergeTempPatient([FromBody] MergeRequestDto req)
    {
        var tempP = await _patientRepo.GetByIdAsync(req.TempPatientId);
        var targetP = await _patientRepo.GetByIdAsync(req.TargetPatientId);
        if (tempP == null || targetP == null)
            return ApiResponse<bool>.Fail(404, "档案不存在");

        tempP.MergedToPatientId = targetP.Id;
        tempP.IsActive = 0;
        await _patientRepo.UpdateAsync(tempP);

        return ApiResponse<bool>.Success(true, "临时档案合并归入正式档案成功");
    }

    [HttpGet("{id}")]
    public async Task<ApiResponse<PatPatient>> GetById(string id)
    {
        var p = await _patientRepo.GetByIdAsync(id);
        return p != null ? ApiResponse<PatPatient>.Success(p) : ApiResponse<PatPatient>.Fail(404, "患者不存在");
    }
}

