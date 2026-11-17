using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.Patient.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace HIS.Modules.Patient.Controllers;

[ApiController]
[Route("api/v4/patients")]
public class PatientController : ControllerBase
{
    private readonly IRepository<PatPatient> _patientRepo;
    private readonly IDbConnectionFactory _connFactory;
    private readonly IAuditTrailContract? _auditTrail;
    private readonly IUserSession? _userSession;

    public PatientController(
        IRepository<PatPatient> patientRepo,
        IDbConnectionFactory? connFactory = null,
        IAuditTrailContract? auditTrail = null,
        IUserSession? userSession = null)
    {
        _patientRepo = patientRepo;
        _connFactory = connFactory ?? new SqliteConnectionFactory();
        _auditTrail = auditTrail;
        _userSession = userSession;
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

    public sealed record UpdatePatientRequestDto(
        string? FullName = null,
        string? Phone = null,
        string? GuardianName = null,
        string? GuardianIdCardNo = null,
        List<string>? AllergyTags = null
    );

    [RequireRole(RoleCode.Admin, RoleCode.DoctorOpd, RoleCode.DoctorIpd, RoleCode.NurseIpd, RoleCode.Cashier)]
    [HttpPost("register")]
    public async Task<ApiResponse<PatPatient>> Register([FromBody] RegisterPatientRequestDto req)
    {
        var birthYear = req.BirthDateInt / 10000;
        var currentYear = SystemTime.ShanghaiNow.Year;
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
            MedicalRecordNo = "MRN" + SystemTime.ShanghaiNow.ToString("yyMMddHHmmssfff"),
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

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync("PATIENT_REGISTER", "PatPatient", p.Id, $"建立实名患者档案 [{p.MedicalRecordNo} {p.FullName}]", _userSession?.UserId, _userSession?.DisplayName);
        }

        return ApiResponse<PatPatient>.Success(p, "实名档案建立成功");
    }

    [RequireRole(RoleCode.Admin, RoleCode.DoctorOpd, RoleCode.DoctorIpd, RoleCode.NurseIpd, RoleCode.Cashier)]
    [HttpPost("emergency-temp")]
    public async Task<ApiResponse<PatPatient>> CreateEmergencyTempPatient()
    {
        var today = SystemTime.ShanghaiNow.ToString("yyyyMMdd");
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

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync("PATIENT_EMERGENCY_TEMP", "PatPatient", p.Id, $"急诊三无临时建档 [{p.MedicalRecordNo}]", _userSession?.UserId, _userSession?.DisplayName);
        }

        return ApiResponse<PatPatient>.Success(p, "三无急诊临时档案创建成功");
    }

    [RequireRole(RoleCode.Admin, RoleCode.DoctorOpd, RoleCode.DoctorIpd, RoleCode.NurseIpd, RoleCode.Cashier)]
    [HttpPut("{id}")]
    public async Task<ApiResponse<PatPatient>> UpdatePatient(string id, [FromBody] UpdatePatientRequestDto req)
    {
        var p = await _patientRepo.GetByIdAsync(id);
        if (p == null || p.IsActive != 1)
        {
            return ApiResponse<PatPatient>.Fail(404, "患者档案不存在或已被注销");
        }

        if (!string.IsNullOrWhiteSpace(req.FullName))
        {
            p.FullName = req.FullName.Trim();
        }
        if (!string.IsNullOrWhiteSpace(req.Phone))
        {
            p.PhoneCipher = CryptoHelper.EncryptAesGcm(req.Phone.Trim());
            p.PhoneMasked = CryptoHelper.MaskPhone(req.Phone.Trim());
        }
        if (req.GuardianName != null)
        {
            p.GuardianName = req.GuardianName.Trim();
        }
        if (req.GuardianIdCardNo != null)
        {
            p.GuardianIdHash = CryptoHelper.ComputeBlindIndex(req.GuardianIdCardNo.Trim());
        }
        if (req.AllergyTags != null)
        {
            p.AllergyTagsJson = JsonSerializer.Serialize(req.AllergyTags);
        }

        await _patientRepo.UpdateAsync(p);

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync("PATIENT_UPDATE", "PatPatient", p.Id, $"更新患者档案基本信息 [{p.MedicalRecordNo} {p.FullName}]", _userSession?.UserId, _userSession?.DisplayName);
        }

        return ApiResponse<PatPatient>.Success(p, "患者档案更新成功");
    }

    [RequireRole(RoleCode.Admin, RoleCode.DoctorOpd, RoleCode.DoctorIpd, RoleCode.NurseIpd, RoleCode.Cashier, RoleCode.Pharmacist)]
    [HttpGet("search")]
    public async Task<ApiResponse<IReadOnlyList<PatPatient>>> SearchPatients([FromQuery] string? keyword = null, [FromQuery] int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return ApiResponse<IReadOnlyList<PatPatient>>.Success(Array.Empty<PatPatient>());
        }

        string kw = keyword.Trim();
        string idHash = CryptoHelper.ComputeBlindIndex(kw);
        int safeLimit = Math.Clamp(limit, 1, 100);

        var list = await _patientRepo.QueryAsync(@"
SELECT * FROM pat_patients
WHERE is_active = 1
  AND (medical_record_no LIKE '%' || @Kw || '%'
       OR full_name LIKE '%' || @Kw || '%'
       OR phone_masked LIKE '%' || @Kw || '%'
       OR id_card_hash = @IdHash)
ORDER BY updated_at_ms DESC
LIMIT @Limit", new
        {
            Kw = kw,
            IdHash = idHash,
            Limit = safeLimit
        });

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync(
                "PATIENT_SEARCH",
                "PatPatient",
                "SEARCH",
                $"检索患者档案，返回 {list.Count} 条结果",
                _userSession?.UserId,
                _userSession?.DisplayName,
                _userSession?.ActiveDeptId);
        }

        foreach (var p in list)
        {
            SanitizePatientPii(p);
        }

        return ApiResponse<IReadOnlyList<PatPatient>>.Success(list);
    }

    public sealed record MergeRequestDto(string TempPatientId, string TargetPatientId);

    [RequireRole(RoleCode.Admin, RoleCode.DoctorOpd, RoleCode.DoctorIpd)]
    [HttpPost("{tempId}/merge")]
    public async Task<ApiResponse<bool>> MergeTempPatientByRoute(string tempId, [FromBody] MergeRequestDto? req)
    {
        string targetId = !string.IsNullOrWhiteSpace(req?.TargetPatientId) ? req.TargetPatientId : (req?.TempPatientId ?? string.Empty);
        return await MergeTempPatient(new MergeRequestDto(tempId, targetId));
    }

    [RequireRole(RoleCode.Admin, RoleCode.DoctorOpd, RoleCode.DoctorIpd)]
    [HttpPost("merge")]
    public async Task<ApiResponse<bool>> MergeTempPatient([FromBody] MergeRequestDto req)
    {
        if (string.IsNullOrWhiteSpace(req.TempPatientId)
            || string.IsNullOrWhiteSpace(req.TargetPatientId)
            || string.Equals(req.TempPatientId.Trim(), req.TargetPatientId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<bool>.Fail(400, "临时档案与正式档案不能相同，且都必须指定");
        }

        using var conn = _connFactory.CreateConnection();
        var tempP = await _patientRepo.GetByIdAsync(req.TempPatientId.Trim());
        var targetP = await _patientRepo.GetByIdAsync(req.TargetPatientId.Trim());
        if (tempP == null || targetP == null)
            return ApiResponse<bool>.Fail(404, "档案不存在");
        if (tempP.IsTempEmergency != 1 || tempP.IsActive != 1)
            return ApiResponse<bool>.Fail(409, "仅可合并有效的急诊临时档案");
        if (targetP.IsTempEmergency != 0 || targetP.IsActive != 1)
            return ApiResponse<bool>.Fail(409, "目标档案必须是有效的正式实名档案");

        await _connFactory.ExecuteInTransactionAsync(async ct =>
        {
            var tempTags = ParseTags(tempP.AllergyTagsJson);
            var targetTags = ParseTags(targetP.AllergyTagsJson);
            var mergedTags = targetTags
                .Concat(tempTags)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (mergedTags.Count != targetTags.Count)
            {
                targetP.AllergyTagsJson = JsonSerializer.Serialize(mergedTags);
                await _patientRepo.UpdateAsync(targetP, ct);
            }

            foreach (var table in new[]
            {
                "opd_visits", "opd_appointments", "opd_prescriptions",
                "opd_triage_queues", "ipd_encounters", "ipd_admission_cards",
                "ipd_prepayments", "ord_medical_orders", "mtc_service_requests",
                "mtc_blood_requests", "emr_documents", "fin_payment_orders",
                "fin_charge_ledgers", "fin_refund_orders"
            })
            {
                try
                {
                    await conn.ExecuteAsync(new CommandDefinition(
                        $"UPDATE {table} SET patient_id = @TargetId WHERE patient_id = @TempId",
                        new { TargetId = targetP.Id, TempId = tempP.Id }, _connFactory.CurrentTransaction,
                        cancellationToken: ct));
                }
                catch (SqliteException ex) when (ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase)
                    || ex.Message.Contains("no such column", StringComparison.OrdinalIgnoreCase))
                {
                    // Legacy databases may not contain every optional business table.
                }
            }

            tempP.MergedToPatientId = targetP.Id;
            tempP.IsActive = 0;
            await _patientRepo.UpdateAsync(tempP, ct);
        });

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync(
                "PATIENT_MERGE",
                "PatPatient",
                targetP.Id,
                $"急诊临时档案 [{tempP.MedicalRecordNo}] 归并至正式实名档案 [{targetP.MedicalRecordNo} {targetP.FullName}]，历史单据已全部迁移",
                _userSession?.UserId,
                _userSession?.DisplayName);
        }

        return ApiResponse<bool>.Success(true, "临时档案及关联业务单据已合并归入正式档案，原档案保留审计痕迹");
    }

    private static List<string> ParseTags(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    [RequireRole(RoleCode.Admin, RoleCode.DoctorOpd, RoleCode.DoctorIpd, RoleCode.NurseIpd, RoleCode.Cashier, RoleCode.Pharmacist)]
    [HttpGet("{id}")]
    public async Task<ApiResponse<PatPatient>> GetById(string id)
    {
        var p = await _patientRepo.GetByIdAsync(id);
        if (p == null)
        {
            return ApiResponse<PatPatient>.Fail(404, "患者不存在");
        }

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync(
                "PATIENT_VIEW",
                "PatPatient",
                p.Id,
                $"查阅患者隐私档案 [{p.MedicalRecordNo} {p.FullName}]",
                _userSession?.UserId,
                _userSession?.DisplayName);
        }

        SanitizePatientPii(p);
        return ApiResponse<PatPatient>.Success(p);
    }

    public sealed record DecryptPiiRequestDto(string FieldName, string Reason);

    [HttpPost("{id}/decrypt-pii")]
    public async Task<ApiResponse<string>> DecryptPii(string id, [FromBody] DecryptPiiRequestDto req)
    {
        if (_userSession == null || string.IsNullOrWhiteSpace(_userSession.UserId))
        {
            return ApiResponse<string>.Fail(401, "请先登录");
        }
        if (string.IsNullOrWhiteSpace(req.Reason))
        {
            return ApiResponse<string>.Fail(400, "查阅未脱敏敏感个人信息必须说明业务原因");
        }

        var p = await _patientRepo.GetByIdAsync(id);
        if (p == null)
        {
            return ApiResponse<string>.Fail(404, "患者不存在");
        }

        string result;
        if (string.Equals(req.FieldName, "ID_CARD", StringComparison.OrdinalIgnoreCase))
        {
            result = DecryptCipher(p.IdCardCipher);
        }
        else if (string.Equals(req.FieldName, "PHONE", StringComparison.OrdinalIgnoreCase))
        {
            result = DecryptCipher(p.PhoneCipher);
        }
        else
        {
            return ApiResponse<string>.Fail(400, "仅支持解密 ID_CARD 或 PHONE");
        }

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync(
                "PATIENT_PII_DECRYPT",
                "PatPatient",
                p.Id,
                $"解密患者敏感字段 [{req.FieldName}]，原因: {req.Reason}",
                _userSession?.UserId,
                _userSession?.DisplayName,
                _userSession?.ActiveDeptId);
        }

        return ApiResponse<string>.Success(result, "敏感信息解密已审批留痕");
    }

    private static string DecryptCipher(string? cipher)
    {
        if (string.IsNullOrWhiteSpace(cipher))
            return string.Empty;
        try
        {
            return CryptoHelper.DecryptAesGcm(cipher);
        }
        catch
        {
            return CryptoHelper.DecryptSm4Gcm(cipher);
        }
    }

    private PatPatient SanitizePatientPii(PatPatient p)
    {
        bool canViewRawPii = _userSession?.IsAdmin == true;
        if (!canViewRawPii)
        {
            p.IdCardCipher = string.Empty;
            p.PhoneCipher = string.Empty;
            p.IdCardHash = string.Empty;
            p.GuardianIdHash = string.Empty;
        }
        return p;
    }
}
