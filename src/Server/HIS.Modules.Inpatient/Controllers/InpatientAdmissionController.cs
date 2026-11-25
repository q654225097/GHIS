using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.Inpatient.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Inpatient.Controllers;

[ApiController]
[Route("api/v4/ipd")]
public class InpatientAdmissionController : ControllerBase
{
    private readonly IRepository<IpdAdmissionCard> _cardRepo;
    private readonly IRepository<IpdPrepayment> _prepaymentRepo;
    private readonly IRepository<IpdEncounter> _encounterRepo;
    private readonly IRepository<IpdBed> _bedRepo;
    private readonly IDbConnectionFactory _connFactory;
    private readonly IDepartmentReadOnlyContract? _deptContract;
    private readonly IAuditTrailContract? _auditTrail;
    private readonly IUserSession? _userSession;

    public InpatientAdmissionController(
        IRepository<IpdAdmissionCard> cardRepo,
        IRepository<IpdPrepayment> prepaymentRepo,
        IRepository<IpdEncounter> encounterRepo,
        IRepository<IpdBed> bedRepo,
        IDbConnectionFactory connFactory,
        IDepartmentReadOnlyContract? deptContract = null,
        IAuditTrailContract? auditTrail = null,
        IUserSession? userSession = null)
    {
        _cardRepo = cardRepo;
        _prepaymentRepo = prepaymentRepo;
        _encounterRepo = encounterRepo;
        _bedRepo = bedRepo;
        _connFactory = connFactory;
        _deptContract = deptContract;
        _auditTrail = auditTrail;
        _userSession = userSession;
    }

    [HttpGet("cards")]
    public async Task<ApiResponse<IReadOnlyList<IpdAdmissionCardView>>> GetCards(
        [FromQuery] string? status = null,
        [FromQuery] string? keyword = null)
    {
        string? search = string.IsNullOrWhiteSpace(keyword) ? null : "%" + keyword.Trim() + "%";
        using var conn = _connFactory.CreateConnection();
        const string sql = @"
SELECT c.*, p.full_name AS patient_name, p.gender_code AS gender_name,
       p.medical_record_no AS medical_record_no,
       COALESCE(d1.display_name, c.source_doctor_id) AS source_doctor_name,
       COALESCE(d2.dept_name, c.suggested_ipd_dept_id) AS suggested_ipd_dept_name,
       COALESCE(d3.dept_name, '') AS suggested_ward_name,
       COALESCE(e.admission_no, '') AS encounter_no,
       COALESCE(e.prepayment_balance, 0) AS prepayment_balance,
       COALESCE(e.bed_code, '') AS bed_code,
       COALESCE(e.triage_status, 0) AS triage_status
FROM ipd_admission_cards c
LEFT JOIN pat_patients p ON p.id = c.patient_id
LEFT JOIN sys_users d1 ON d1.id = c.source_doctor_id
LEFT JOIN sys_departments d2 ON d2.id = c.suggested_ipd_dept_id
LEFT JOIN sys_departments d3 ON d3.id = c.suggested_ward_id
LEFT JOIN ipd_encounters e ON e.id = c.encounter_id
WHERE (@Status IS NULL OR c.status = @Status)
  AND (@Search IS NULL OR p.full_name LIKE @Search OR p.medical_record_no LIKE @Search
       OR c.card_no LIKE @Search OR c.admission_diagnosis LIKE @Search)
ORDER BY CASE c.status WHEN 'PENDING' THEN 0 ELSE 1 END, c.created_at_ms DESC";
        var cards = (await conn.QueryAsync<IpdAdmissionCardView>(sql, new { Status = status, Search = search })).ToList();
        foreach (var card in cards)
        {
            card.GenderName = card.GenderName switch
            {
                "1" or "M" or "MALE" => "男",
                "2" or "F" or "FEMALE" => "女",
                _ => string.IsNullOrWhiteSpace(card.GenderName) ? "未知" : card.GenderName
            };
        }
        return ApiResponse<IReadOnlyList<IpdAdmissionCardView>>.Success(cards);
    }

    [HttpGet("cards/{cardId}")]
    public async Task<ApiResponse<IpdAdmissionCardView>> GetCard(string cardId)
    {
        using var conn = _connFactory.CreateConnection();
        var card = await LoadCardAsync(conn, cardId);
        if (card == null)
        {
            return ApiResponse<IpdAdmissionCardView>.Fail(404, "住院卡不存在");
        }
        card.GenderName = card.GenderName switch
        {
            "1" or "M" or "MALE" => "男",
            "2" or "F" or "FEMALE" => "女",
            _ => string.IsNullOrWhiteSpace(card.GenderName) ? "未知" : card.GenderName
        };
        return ApiResponse<IpdAdmissionCardView>.Success(card);
    }

    [HttpPost("cards")]
    public async Task<ApiResponse<IpdAdmissionCardView>> CreateCard([FromBody] CreateAdmissionCardRequest req)
    {
        if (!HasAnyRole("OPD_DOC", "CASHIER", RoleCode.Cashier, "ADMIN") || (!IsCurrentUserAdmin()
            && _userSession?.Roles?.Any(r => string.Equals(r, "CASHIER", StringComparison.OrdinalIgnoreCase)) != true
            && (_userSession?.ActiveDeptCategory == DepartmentCategory.IPD
                || _userSession?.ActiveDeptCategory == DepartmentCategory.WARD)))
        {
            return ApiResponse<IpdAdmissionCardView>.Fail(403, "仅门诊医生或收费员可以为患者开立住院卡");
        }
        if (string.IsNullOrWhiteSpace(req.PatientId) || string.IsNullOrWhiteSpace(req.SuggestedIpdDeptId))
        {
            return ApiResponse<IpdAdmissionCardView>.Fail(400, "请选择患者和建议住院科室");
        }
        string requestKey = string.IsNullOrWhiteSpace(req.RequestKey)
            ? "ADMISSION_CARD_" + Guid.NewGuid().ToString("N")
            : req.RequestKey.Trim();
        if (requestKey.Length > 100)
        {
            return ApiResponse<IpdAdmissionCardView>.Fail(400, "请求编号不能超过 100 个字符");
        }
        try
        {
            using var conn = _connFactory.CreateConnection();
            var oldCard = await conn.QuerySingleOrDefaultAsync<IpdAdmissionCard>(
                "SELECT * FROM ipd_admission_cards WHERE request_key = @RequestKey", new { RequestKey = requestKey });
            if (oldCard != null)
            {
                var existingCard = await LoadCardAsync(conn, oldCard.Id);
                return ApiResponse<IpdAdmissionCardView>.Success(existingCard!);
            }
            var pending = await conn.QuerySingleOrDefaultAsync<IpdAdmissionCard>(
                "SELECT * FROM ipd_admission_cards WHERE patient_id = @PatientId AND status = 'PENDING'",
                new { PatientId = req.PatientId });
            if (pending != null)
            {
                return ApiResponse<IpdAdmissionCardView>.Fail(409,
                    $"该患者已有待登记住院卡 {pending.CardNo}，请勿重复开立");
            }
            var activeEncounter = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(1) FROM ipd_encounters WHERE patient_id = @PatientId AND status IN (1, 4)",
                new { PatientId = req.PatientId });
            if (activeEncounter > 0)
            {
                return ApiResponse<IpdAdmissionCardView>.Fail(400, "该患者已有在院记录，禁止重复办理入院登记");
            }
            var patient = await conn.QuerySingleOrDefaultAsync<IdNameRow>(
                "SELECT id, full_name FROM pat_patients WHERE id = @Id", new { Id = req.PatientId });
            if (patient == null)
            {
                return ApiResponse<IpdAdmissionCardView>.Fail(404, "未找到患者，请先完成建档");
            }
            var ipdDept = await conn.QuerySingleOrDefaultAsync<IdCodeRow>(
                "SELECT id, dept_code FROM sys_departments WHERE id = @Id AND dept_category = 'IPD' AND is_active = 1",
                new { Id = req.SuggestedIpdDeptId });
            if (ipdDept == null)
            {
                return ApiResponse<IpdAdmissionCardView>.Fail(400, "建议住院科室不存在或已停用");
            }
            if (!string.IsNullOrWhiteSpace(req.SuggestedWardId) && _deptContract != null
                && !await _deptContract.IsDeptWardRelationValidAsync(req.SuggestedIpdDeptId, req.SuggestedWardId!))
            {
                return ApiResponse<IpdAdmissionCardView>.Fail(400, "建议病区不属于该住院科室");
            }
            string userId = CurrentUserId();
            string deptId = string.IsNullOrWhiteSpace(_userSession?.ActiveDeptId) ? "DEPT-OPD-01" : _userSession!.ActiveDeptId;
            var card = new IpdAdmissionCard
            {
                CardNo = "ADC" + DateTime.UtcNow.ToString("yyMMddHHmmssfff"),
                RequestKey = requestKey,
                PatientId = req.PatientId.Trim(),
                VisitId = NullIfBlank(req.VisitId),
                SourceDoctorId = userId,
                SourceDeptId = deptId,
                SuggestedIpdDeptId = req.SuggestedIpdDeptId.Trim(),
                SuggestedWardId = NullIfBlank(req.SuggestedWardId),
                AdmissionDiagnosis = NullIfBlank(req.AdmissionDiagnosis),
                ContactPhone = NullIfBlank(req.ContactPhone),
                Remark = NullIfBlank(req.Remark),
                Status = "PENDING",
                CreatedBy = userId,
                UpdatedBy = userId
            };
            await _cardRepo.InsertAsync(card);
            var creatorRole = IsCurrentUserAdmin() ? "管理员" :
                _userSession?.Roles?.Any(r => string.Equals(r, "CASHIER", StringComparison.OrdinalIgnoreCase)) == true ? "收费员" : "门诊医生";
            await Audit("IPD_ADMISSION_CARD_CREATE", card.Id,
                $"{creatorRole}为患者 {patient.FullName} 开立住院卡 {card.CardNo}，建议科室 {ipdDept.DeptCode}");
            var createdCard = await LoadCardAsync(conn, card.Id);
            return ApiResponse<IpdAdmissionCardView>.Success(createdCard!, "住院卡已开立");
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return ApiResponse<IpdAdmissionCardView>.Fail(409, "住院卡已存在或患者已有待登记住院卡，请刷新列表");
        }
    }

    [HttpPost("admission/register-with-patient")]
    public async Task<ActionResult<ApiResponse<IpdAdmissionCardView>>> RegisterWithPatient([FromBody] CreateAdmissionWithPatientRequest req)
    {
        if (!HasAnyRole("CASHIER", RoleCode.Cashier, "ADMIN", "OPD_DOC"))
        {
            return StatusCode(403, ApiResponse<IpdAdmissionCardView>.Fail(403, "仅收费员或系统管理员可执行一站式入院建档并开卡"));
        }
        if (string.IsNullOrWhiteSpace(req.FullName) || string.IsNullOrWhiteSpace(req.IdCardNo) ||
            string.IsNullOrWhiteSpace(req.Phone) || string.IsNullOrWhiteSpace(req.SuggestedIpdDeptId))
        {
            return BadRequest(ApiResponse<IpdAdmissionCardView>.Fail(400, "姓名、证件号、联系电话和建议住院科室不能为空"));
        }
        if (req.BirthDateInt <= 19000101 || req.BirthDateInt > 21000101)
        {
            return BadRequest(ApiResponse<IpdAdmissionCardView>.Fail(400, "出生日期格式不合法，请输入有效日期（如 19900101）"));
        }

        var birthYear = req.BirthDateInt / 10000;
        var currentYear = SystemTime.ShanghaiNow.Year;
        var age = currentYear - birthYear;
        if (age < 14 && string.IsNullOrWhiteSpace(req.GuardianName))
        {
            return BadRequest(ApiResponse<IpdAdmissionCardView>.Fail(400, "年龄小于 14 周岁患者强制要求录入法定监护人信息"));
        }

        string requestKey = string.IsNullOrWhiteSpace(req.RequestKey)
            ? "IPD_CASHIER_ADC_" + Guid.NewGuid().ToString("N")
            : req.RequestKey.Trim();
        if (requestKey.Length > 100)
        {
            return BadRequest(ApiResponse<IpdAdmissionCardView>.Fail(400, "请求编号不能超过 100 个字符"));
        }

        try
        {
            using var conn = _connFactory.CreateConnection();

            var oldCard = await conn.QuerySingleOrDefaultAsync<IpdAdmissionCard>(
                "SELECT * FROM ipd_admission_cards WHERE request_key = @RequestKey", new { RequestKey = requestKey });
            if (oldCard != null)
            {
                var existingCard = await LoadCardAsync(conn, oldCard.Id);
                return Ok(ApiResponse<IpdAdmissionCardView>.Success(existingCard!));
            }

            var ipdDept = await conn.QuerySingleOrDefaultAsync<IdCodeRow>(
                "SELECT id, dept_code FROM sys_departments WHERE id = @Id AND dept_category = 'IPD' AND is_active = 1",
                new { Id = req.SuggestedIpdDeptId });
            if (ipdDept == null)
            {
                return BadRequest(ApiResponse<IpdAdmissionCardView>.Fail(400, "建议住院科室不存在或已停用"));
            }
            if (!string.IsNullOrWhiteSpace(req.SuggestedWardId) && _deptContract != null
                && !await _deptContract.IsDeptWardRelationValidAsync(req.SuggestedIpdDeptId, req.SuggestedWardId!))
            {
                return BadRequest(ApiResponse<IpdAdmissionCardView>.Fail(400, "建议病区不属于该住院科室"));
            }

            var idCardHash = CryptoHelper.ComputeBlindIndex(req.IdCardNo.Trim());
            var existingPat = await conn.QuerySingleOrDefaultAsync<PatPatientRow>(
                "SELECT id, medical_record_no, full_name FROM pat_patients WHERE id_card_hash = @Hash AND is_active = 1",
                new { Hash = idCardHash });

            string targetPatientId;
            string targetPatientName;
            string targetMrn;

            if (existingPat != null)
            {
                if (!req.ReuseExistingPatient)
                {
                    return StatusCode(409, ApiResponse<IpdAdmissionCardView>.Fail(409,
                        $"该证件号码已建档，病历号为：{existingPat.medical_record_no}。可直接使用已有档案开立住院卡。"));
                }
                targetPatientId = existingPat.id;
                targetPatientName = existingPat.full_name;
                targetMrn = existingPat.medical_record_no;
            }
            else
            {
                targetPatientId = "PAT" + Guid.NewGuid().ToString("N")[..12].ToUpper();
                targetMrn = "MRN" + SystemTime.ShanghaiNow.ToString("yyMMddHHmmssfff");
                targetPatientName = req.FullName.Trim();
                long nowMs = SystemTime.UtcNowMs;
                string creatorId = CurrentUserId();
                string gender = req.GenderCode.Trim() switch
                {
                    "1" or "M" or "MALE" or "男" => "1",
                    "2" or "F" or "FEMALE" or "女" => "2",
                    _ => "0"
                };
                string idCardCipher = CryptoHelper.EncryptSm4Gcm(req.IdCardNo.Trim());
                string phoneCipher = CryptoHelper.EncryptSm4Gcm(req.Phone.Trim());
                string phoneMasked = CryptoHelper.MaskPhone(req.Phone.Trim());
                string? guardianCipher = string.IsNullOrWhiteSpace(req.GuardianIdCardNo) ? null : CryptoHelper.EncryptSm4Gcm(req.GuardianIdCardNo.Trim());
                string? guardianHash = string.IsNullOrWhiteSpace(req.GuardianIdCardNo) ? null : CryptoHelper.ComputeBlindIndex(req.GuardianIdCardNo.Trim());
                string? allergyJson = req.AllergyTags != null && req.AllergyTags.Count > 0
                    ? JsonSerializer.Serialize(req.AllergyTags)
                    : null;

                const string insertPatientSql = @"
INSERT INTO pat_patients (
    id, medical_record_no, full_name, gender_code, birth_date_int,
    id_card_type, id_card_cipher, id_card_hash, phone_cipher, phone_masked,
    guardian_name, guardian_id_cipher, guardian_id_hash, allergy_tags_json,
    is_temp_emergency, is_active, schema_version, created_at_ms, created_by,
    updated_at_ms, updated_by, version_no
) VALUES (
    @Id, @Mrn, @FullName, @GenderCode, @BirthDateInt,
    '01', @IdCardCipher, @IdCardHash, @PhoneCipher, @PhoneMasked,
    @GuardianName, @GuardianIdCipher, @GuardianIdHash, @AllergyJson,
    0, 1, 1, @NowMs, @CreatedBy,
    @NowMs, @CreatedBy, 1
)";
                await conn.ExecuteAsync(insertPatientSql, new
                {
                    Id = targetPatientId,
                    Mrn = targetMrn,
                    FullName = targetPatientName,
                    GenderCode = gender,
                    BirthDateInt = req.BirthDateInt,
                    IdCardCipher = idCardCipher,
                    IdCardHash = idCardHash,
                    PhoneCipher = phoneCipher,
                    PhoneMasked = phoneMasked,
                    GuardianName = string.IsNullOrWhiteSpace(req.GuardianName) ? null : req.GuardianName.Trim(),
                    GuardianIdCipher = guardianCipher,
                    GuardianIdHash = guardianHash,
                    AllergyJson = allergyJson,
                    NowMs = nowMs,
                    CreatedBy = creatorId
                });

                if (_auditTrail != null)
                {
                    await _auditTrail.RecordAuditAsync("PATIENT_REGISTER", "PatPatient", targetPatientId,
                        $"建立实名患者档案 [{targetMrn} {targetPatientName}]", _userSession?.UserId, _userSession?.DisplayName);
                }
            }

            var pending = await conn.QuerySingleOrDefaultAsync<IpdAdmissionCard>(
                "SELECT * FROM ipd_admission_cards WHERE patient_id = @PatientId AND status = 'PENDING'",
                new { PatientId = targetPatientId });
            if (pending != null)
            {
                return StatusCode(409, ApiResponse<IpdAdmissionCardView>.Fail(409,
                    $"该患者已有待登记住院卡 {pending.CardNo}，请勿重复开立"));
            }
            var activeEncounter = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(1) FROM ipd_encounters WHERE patient_id = @PatientId AND status IN (1, 4)",
                new { PatientId = targetPatientId });
            if (activeEncounter > 0)
            {
                return BadRequest(ApiResponse<IpdAdmissionCardView>.Fail(400, "该患者已有在院记录，禁止重复办理入院登记"));
            }

            string userId = CurrentUserId();
            string deptId = string.IsNullOrWhiteSpace(_userSession?.ActiveDeptId) ? "DEPT-CASHIER" : _userSession!.ActiveDeptId;
            var card = new IpdAdmissionCard
            {
                CardNo = "ADC" + DateTime.UtcNow.ToString("yyMMddHHmmssfff"),
                RequestKey = requestKey,
                PatientId = targetPatientId,
                VisitId = null,
                SourceDoctorId = userId,
                SourceDeptId = deptId,
                SuggestedIpdDeptId = req.SuggestedIpdDeptId.Trim(),
                SuggestedWardId = NullIfBlank(req.SuggestedWardId),
                AdmissionDiagnosis = NullIfBlank(req.AdmissionDiagnosis),
                ContactPhone = NullIfBlank(req.Phone),
                Remark = NullIfBlank(req.Remark),
                Status = "PENDING",
                CreatedBy = userId,
                UpdatedBy = userId
            };
            await _cardRepo.InsertAsync(card);

            var creatorRole = IsCurrentUserAdmin() ? "管理员" :
                _userSession?.Roles?.Any(r => string.Equals(r, "CASHIER", StringComparison.OrdinalIgnoreCase)) == true ? "收费员" : "门诊医生";
            await Audit("IPD_ADMISSION_CARD_CREATE", card.Id,
                $"{creatorRole}为患者 {targetPatientName} 开立住院卡 {card.CardNo}，建议科室 {ipdDept.DeptCode}");

            var createdCard = await LoadCardAsync(conn, card.Id);
            return Ok(ApiResponse<IpdAdmissionCardView>.Success(createdCard!, "建档并开立住院卡成功"));
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return StatusCode(409, ApiResponse<IpdAdmissionCardView>.Fail(409, "数据冲突或已存在待登记记录，请重试"));
        }
    }

    [HttpPost("cards/{cardId}/cancel")]
    public async Task<ApiResponse<IpdAdmissionCardView>> CancelCard(string cardId, [FromBody] CancelRequest? req = null)
    {
        if (!HasAnyRole("OPD_DOC", "ADMIN"))
        {
            return ApiResponse<IpdAdmissionCardView>.Fail(403, "仅门诊医生或系统管理员可以取消住院卡");
        }
        IpdAdmissionCard? card = await _cardRepo.GetByIdAsync(cardId);
        if (card == null)
        {
            return ApiResponse<IpdAdmissionCardView>.Fail(404, "住院卡不存在");
        }
        if (card.Status != "PENDING")
        {
            return ApiResponse<IpdAdmissionCardView>.Fail(409, "仅待登记的住院卡可以取消");
        }
        card.Status = "CANCELLED";
        card.UpdatedBy = CurrentUserId();
        await _cardRepo.UpdateAsync(card);
        await Audit("IPD_ADMISSION_CARD_CANCEL", card.Id,
            $"取消住院卡 {card.CardNo}，原因 {req?.Reason ?? "未填写"}");
        return await GetCard(card.Id);
    }

    [HttpGet("registration/options")]
    public async Task<ApiResponse<IpdRegistrationOptionsView>> GetRegistrationOptions()
    {
        using var conn = _connFactory.CreateConnection();
        const string sql = @"
SELECT d.id, d.dept_code, d.dept_name, w.id AS ward_id, w.dept_code AS ward_code, w.dept_name AS ward_name, r.relation_type
FROM sys_departments d
LEFT JOIN sys_dept_ward_relations r ON r.ipd_dept_id = d.id AND r.is_active = 1
LEFT JOIN sys_departments w ON w.id = r.ward_dept_id AND w.is_active = 1
WHERE d.dept_category = 'IPD' AND d.is_active = 1
ORDER BY d.dept_name, r.priority_order, w.dept_name";
        var rows = (await conn.QueryAsync<RegistrationOptionRow>(sql)).ToList();
        var depts = rows
            .GroupBy(x => new { x.Id, x.DeptCode, x.DeptName })
            .Select(g => new IpdDeptView
            {
                Id = g.Key.Id,
                DeptCode = g.Key.DeptCode,
                DeptName = g.Key.DeptName,
                Wards = g.Where(x => !string.IsNullOrWhiteSpace(x.WardId))
                    .GroupBy(x => x.WardId)
                    .Select(w => new IpdWardView
                    {
                        Id = w.Key!,
                        DeptCode = w.First().WardCode ?? string.Empty,
                        DeptName = w.First().WardName ?? string.Empty,
                        RelationType = w.First().RelationType ?? "PRIMARY"
                    }).ToList()
            }).ToList();
        return ApiResponse<IpdRegistrationOptionsView>.Success(new IpdRegistrationOptionsView { Depts = depts });
    }

    [HttpGet("wards/{wardId}/beds")]
    public async Task<ApiResponse<IReadOnlyList<IpdBed>>> GetWardBeds(string wardId)
    {
        var beds = await _bedRepo.QueryAsync(
            "SELECT * FROM ipd_beds WHERE ward_id = @WardId ORDER BY room_code, bed_code", new { WardId = wardId });
        return ApiResponse<IReadOnlyList<IpdBed>>.Success(beds);
    }

    [HttpGet("wards/{wardId}/staff")]
    public async Task<ApiResponse<IReadOnlyList<IpdStaffView>>> GetWardStaff(
        string wardId,
        [FromQuery] string role = "NURSE",
        [FromQuery] string? deptId = null)
    {
        string category = role.Equals("DOCTOR", StringComparison.OrdinalIgnoreCase) ? "IPD" : "WARD";
        string scopeId = category == "IPD" ? (string.IsNullOrWhiteSpace(deptId) ? wardId : deptId) : wardId;
        const string sql = @"
SELECT u.id, u.user_name, u.display_name, u.employee_no,
       r.role_code, r.role_name, p.dept_id, p.dept_category
FROM sys_users u
JOIN sys_user_dept_posts p ON p.user_id = u.id
JOIN sys_roles r ON r.id = p.role_id
WHERE p.dept_id = @ScopeId AND p.dept_category = @Category
  AND u.is_active = 1 AND r.is_active = 1 AND r.role_code <> 'ADMIN'
ORDER BY u.display_name";
        using var conn = _connFactory.CreateConnection();
        var staff = await conn.QueryAsync<IpdStaffView>(sql, new { ScopeId = scopeId, Category = category });
        return ApiResponse<IReadOnlyList<IpdStaffView>>.Success(staff.ToList());
    }

    public sealed record IpdCashierSummaryDto(int TodayRegisteredCount, decimal TodayPrepaymentAmount);

    [HttpGet("cashier/summary")]
    public async Task<ApiResponse<IpdCashierSummaryDto>> GetCashierSummary([FromQuery] string? cashierId = null)
    {
        using var conn = _connFactory.CreateConnection();
        long dayStart = SystemTime.TodayStartUtcMs;
        long dayEnd = SystemTime.TomorrowStartUtcMs;

        int registeredCount = await conn.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM ipd_encounters
            WHERE created_at_ms >= @DayStart AND created_at_ms < @DayEnd",
            new { DayStart = dayStart, DayEnd = dayEnd });

        decimal prepaymentTotal = await conn.ExecuteScalarAsync<decimal>(@"
            SELECT COALESCE(SUM(amount), 0) FROM ipd_prepayments
            WHERE paid_at_ms >= @DayStart AND paid_at_ms < @DayEnd
              AND (@Cashier IS NULL OR cashier_id = @Cashier)",
            new { DayStart = dayStart, DayEnd = dayEnd, Cashier = NullIfBlank(cashierId) });

        return ApiResponse<IpdCashierSummaryDto>.Success(new IpdCashierSummaryDto(registeredCount, prepaymentTotal));
    }

    [HttpGet("encounters/active")]
    public async Task<ApiResponse<IReadOnlyList<IpdEncounterView>>> GetActiveEncounters(
        [FromQuery] string? admitDeptId = null,
        [FromQuery] string? admitWardId = null,
        [FromQuery] string? keyword = null)
    {
        string? kw = string.IsNullOrWhiteSpace(keyword) ? null : "%" + keyword.Trim() + "%";
        const string sql = @"
SELECT e.*, COALESCE(p.full_name, e.patient_id) AS patient_name,
       COALESCE(p.medical_record_no, '') AS medical_record_no,
       COALESCE(d.dept_name, e.admit_dept_id) AS dept_name,
       COALESCE(w.dept_name, e.admit_ward_id) AS ward_name,
       COALESCE(doc.display_name, e.attending_doctor_id) AS attending_doctor_name,
       COALESCE(nur.display_name, e.primary_nurse_id) AS primary_nurse_name
FROM ipd_encounters e
LEFT JOIN pat_patients p ON p.id = e.patient_id
LEFT JOIN sys_departments d ON d.id = e.admit_dept_id
LEFT JOIN sys_departments w ON w.id = e.admit_ward_id
LEFT JOIN sys_users doc ON doc.id = e.attending_doctor_id
LEFT JOIN sys_users nur ON nur.id = e.primary_nurse_id
WHERE e.status = 1
  AND (@DeptId IS NULL OR e.admit_dept_id = @DeptId)
  AND (@WardId IS NULL OR e.admit_ward_id = @WardId)
  AND (@Kw IS NULL OR e.admission_no LIKE @Kw OR p.full_name LIKE @Kw OR p.medical_record_no LIKE @Kw OR e.bed_code LIKE @Kw)
ORDER BY e.triage_status, e.created_at_ms DESC";
        using var conn = _connFactory.CreateConnection();
        var encounters = await conn.QueryAsync<IpdEncounterView>(sql, new
        {
            DeptId = NullIfBlank(admitDeptId),
            WardId = NullIfBlank(admitWardId),
            Kw = kw
        });
        return ApiResponse<IReadOnlyList<IpdEncounterView>>.Success(encounters.ToList());
    }

    [HttpPost("cards/{cardId}/register")]
    public async Task<ApiResponse<IpdAdmissionCardView>> RegisterCard(
        string cardId, [FromBody] RegisterAdmissionCardRequest req)
    {
        if (!HasAnyRole("CASHIER", "ADMIN"))
        {
            return ApiResponse<IpdAdmissionCardView>.Fail(403, "仅收费员或系统管理员可以办理住院登记");
        }
        if (req.InitialPrepayment < 0 || req.InitialPrepayment > 1000000m)
        {
            return ApiResponse<IpdAdmissionCardView>.Fail(400, "初始押金应在 0 到 1000000 元之间");
        }
        if (string.IsNullOrWhiteSpace(req.IpdDeptId) || string.IsNullOrWhiteSpace(req.WardId)
            || string.IsNullOrWhiteSpace(req.BedCode))
        {
            return ApiResponse<IpdAdmissionCardView>.Fail(400, "请选择住院科室、病区和床位");
        }
        if (!ValidPaymentChannel(req.PaymentChannel))
        {
            return ApiResponse<IpdAdmissionCardView>.Fail(400, "不支持的押金支付方式");
        }
        IpdAdmissionCard? card = await _cardRepo.GetByIdAsync(cardId);
        if (card == null)
        {
            return ApiResponse<IpdAdmissionCardView>.Fail(404, "住院卡不存在");
        }
        if (card.Status == "REGISTERED")
        {
            return await GetCard(card.Id);
        }
        if (card.Status != "PENDING")
        {
            return ApiResponse<IpdAdmissionCardView>.Fail(409, "住院卡已取消，不能办理登记");
        }
        if (_deptContract != null && !await _deptContract.IsDeptWardRelationValidAsync(req.IpdDeptId, req.WardId))
        {
            return ApiResponse<IpdAdmissionCardView>.Fail(400, "住院科室与病区之间没有有效映射关系");
        }
        string? requestedRelationType = await LoadRelationTypeAsync(req.IpdDeptId, req.WardId);
        if (requestedRelationType == null)
        {
            return ApiResponse<IpdAdmissionCardView>.Fail(400, "住院科室与病区之间没有有效映射关系");
        }
        string userId = CurrentUserId();
        try
        {
            IpdEncounter encounter = await _connFactory.ExecuteInTransactionAsync(async ct =>
            {
                using var conn = _connFactory.CreateConnection();
                string? relationType = await conn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                    "SELECT relation_type FROM sys_dept_ward_relations WHERE ipd_dept_id = @DeptId AND ward_dept_id = @WardId AND is_active = 1 LIMIT 1",
                    new { DeptId = req.IpdDeptId, WardId = req.WardId }, cancellationToken: ct));
                if (relationType == null)
                {
                    throw new InvalidOperationException("住院科室与病区的映射关系已变更，请刷新后重试");
                }
                string normalizedRelation = relationType.Trim().ToUpperInvariant();
                if (normalizedRelation is not ("PRIMARY" or "BORROW_ALLOWED"))
                {
                    throw new InvalidOperationException("住院科室与病区没有有效的收治或借床关系");
                }
                bool isBorrowedBed = normalizedRelation == "BORROW_ALLOWED";
                var freshCard = await _cardRepo.GetByIdAsync(card.Id, ct);
                if (freshCard == null || freshCard.Status != "PENDING")
                {
                    throw new InvalidOperationException("住院卡状态已发生变化，请刷新重试");
                }
                var activeEncounterCount = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                    "SELECT COUNT(1) FROM ipd_encounters WHERE patient_id = @PatientId AND status IN (1, 4)",
                    new { PatientId = freshCard.PatientId }, cancellationToken: ct));
                if (activeEncounterCount > 0)
                {
                    throw new InvalidOperationException("该患者已有在院记录，禁止重复办理入院登记");
                }
                var beds = await _bedRepo.QueryAsync(
                    "SELECT * FROM ipd_beds WHERE ward_id = @WardId AND bed_code = @BedCode",
                    new { WardId = req.WardId, BedCode = req.BedCode.Trim() }, ct);
                if (beds.Count == 0)
                {
                    throw new InvalidOperationException("所选床位不存在");
                }
                IpdBed bed = beds[0];
                if (bed.BedStatus != 0 || !string.IsNullOrWhiteSpace(bed.CurrentEncounterId))
                {
                    throw new InvalidOperationException($"床位 {bed.BedCode} 已被占用，请重新选择");
                }
                if (isBorrowedBed)
                {
                    long? limit = await conn.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
                        "SELECT max_borrow_beds FROM sys_dept_ward_relations WHERE ipd_dept_id = @DeptId AND ward_dept_id = @WardId AND relation_type = 'BORROW_ALLOWED' AND is_active = 1 LIMIT 1",
                        new { DeptId = req.IpdDeptId, WardId = req.WardId }, cancellationToken: ct));
                    if (limit == null)
                    {
                        throw new InvalidOperationException("病区未配置借床上限");
                    }
                    long used = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                        "SELECT count(*) FROM ipd_encounters WHERE admit_dept_id = @DeptId AND admit_ward_id = @WardId AND is_borrowed_bed = 1 AND status IN (1, 4)",
                        new { DeptId = req.IpdDeptId, WardId = req.WardId }, cancellationToken: ct));
                    if (used >= limit.Value)
                    {
                        throw new InvalidOperationException(
                            $"跨科室借床已达上限（{used}/{limit.Value} 床），请选择本科室主病区或其他病区");
                    }
                }
                var enc = new IpdEncounter
                {
                    AdmissionNo = "IPD" + DateTime.UtcNow.ToString("yyMMddHHmmssfff"),
                    PatientId = freshCard.PatientId,
                    CampusId = bed.CampusId,
                    AdmitDeptId = req.IpdDeptId,
                    AdmitWardId = req.WardId,
                    IsBorrowedBed = (short)(isBorrowedBed ? 1 : 0),
                    BedCode = bed.BedCode,
                    ActiveBedToken = Guid.NewGuid().ToString("N"),
                    PrepaymentBalance = 0m,
                    Status = 1,
                    TriageStatus = 0,
                    ExtDataJson = "{\"admissionCardId\":\"" + freshCard.Id + "\"}",
                    CreatedBy = userId,
                    UpdatedBy = userId
                };
                await _encounterRepo.InsertAsync(enc, ct);
                bed.BedStatus = 1;
                bed.CurrentEncounterId = enc.Id;
                bed.UpdatedBy = userId;
                await _bedRepo.UpdateAsync(bed, ct);

                if (req.InitialPrepayment > 0)
                {
                    string prepayKey = string.IsNullOrWhiteSpace(req.RequestKey)
                        ? "IPD_REGISTER_PREPAY_" + enc.Id
                        : req.RequestKey.Trim() + "_PREPAY";
                    var existing = await _prepaymentRepo.QuerySingleOrDefaultAsync(
                        "SELECT * FROM ipd_prepayments WHERE request_key = @Key", new { Key = prepayKey }, ct);
                    if (existing == null)
                    {
                        var prepayment = new IpdPrepayment
                        {
                            PrepaymentNo = "IPY" + DateTime.UtcNow.ToString("yyMMddHHmmssfff"),
                            RequestKey = prepayKey.Length > 100 ? prepayKey[..100] : prepayKey,
                            EncounterId = enc.Id,
                            PatientId = freshCard.PatientId,
                            Amount = req.InitialPrepayment,
                            BalanceAfter = req.InitialPrepayment,
                            PaymentChannel = req.PaymentChannel.Trim().ToUpperInvariant(),
                            CashierId = userId,
                            PaidAtMs = SystemTime.UtcNowMs,
                            Remark = NullIfBlank(req.Remark) ?? "住院登记初始押金",
                            CreatedBy = userId,
                            UpdatedBy = userId
                        };
                        await _prepaymentRepo.InsertAsync(prepayment, ct);
                    }
                    enc.PrepaymentBalance = req.InitialPrepayment;
                    await _encounterRepo.UpdateAsync(enc, ct);
                }
                freshCard.Status = "REGISTERED";
                freshCard.EncounterId = enc.Id;
                freshCard.RegisteredBy = userId;
                freshCard.RegisteredAtMs = SystemTime.UtcNowMs;
                freshCard.UpdatedBy = userId;
                await _cardRepo.UpdateAsync(freshCard, ct);
                return enc;
            });
            await Audit("IPD_CARD_REGISTER", encounter.Id,
                $"住院卡 {card.CardNo} 办理登记，住院号 {encounter.AdmissionNo}，科室 {req.IpdDeptId}，病区 {req.WardId}，床位 {req.BedCode}，押金 {req.InitialPrepayment:0.00}");
            return await GetCard(card.Id);
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<IpdAdmissionCardView>.Fail(409, ex.Message);
        }
        catch (DbConcurrencyException ex)
        {
            return ApiResponse<IpdAdmissionCardView>.Fail(409, ex.Message);
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return ApiResponse<IpdAdmissionCardView>.Fail(409, "住院登记或押金已提交，请刷新列表确认结果");
        }
    }

    [HttpGet("prepayments")]
    public async Task<ApiResponse<IReadOnlyList<IpdPrepaymentView>>> GetPrepayments(
        [FromQuery] string? encounterId = null,
        [FromQuery] string? patientId = null)
    {
        const string sql = @"
SELECT pp.*, COALESCE(u.display_name, pp.cashier_id) AS cashier_name
FROM ipd_prepayments pp
LEFT JOIN sys_users u ON u.id = pp.cashier_id
WHERE (@EncounterId IS NULL OR pp.encounter_id = @EncounterId)
  AND (@PatientId IS NULL OR pp.patient_id = @PatientId)
ORDER BY pp.paid_at_ms DESC";
        using var conn = _connFactory.CreateConnection();
        var rows = await conn.QueryAsync<IpdPrepaymentView>(sql,
            new { EncounterId = NullIfBlank(encounterId), PatientId = NullIfBlank(patientId) });
        return ApiResponse<IReadOnlyList<IpdPrepaymentView>>.Success(rows.ToList());
    }

    [HttpPost("encounters/{encounterId}/prepayments")]
    public async Task<ApiResponse<IpdPrepaymentView>> AddPrepayment(
        string encounterId, [FromBody] AddIpdPrepaymentRequest req)
    {
        if (!HasAnyRole("CASHIER", "ADMIN"))
        {
            return ApiResponse<IpdPrepaymentView>.Fail(403, "仅收费员或系统管理员可以收取住院押金");
        }
        if (req.Amount <= 0 || req.Amount > 1000000m)
        {
            return ApiResponse<IpdPrepaymentView>.Fail(400, "押金金额应在 0.01 到 1000000 元之间");
        }
        if (!ValidPaymentChannel(req.PaymentChannel))
        {
            return ApiResponse<IpdPrepaymentView>.Fail(400, "不支持的押金支付方式");
        }
        IpdEncounter? enc = await _encounterRepo.GetByIdAsync(encounterId);
        if (enc == null)
        {
            return ApiResponse<IpdPrepaymentView>.Fail(404, "住院记录不存在");
        }
        if (enc.Status != 1)
        {
            return ApiResponse<IpdPrepaymentView>.Fail(409, "仅当前住院患者可以缴纳押金");
        }
        string requestKey = string.IsNullOrWhiteSpace(req.RequestKey)
            ? "IPD_PREPAY_" + Guid.NewGuid().ToString("N")
            : req.RequestKey.Trim();
        if (requestKey.Length > 100)
        {
            return ApiResponse<IpdPrepaymentView>.Fail(400, "请求编号不能超过 100 个字符");
        }
        string userId = CurrentUserId();
        try
        {
            IpdPrepayment prepayment = await _connFactory.ExecuteInTransactionAsync(async ct =>
            {
                var old = await _prepaymentRepo.QuerySingleOrDefaultAsync(
                    "SELECT * FROM ipd_prepayments WHERE request_key = @Key", new { Key = requestKey }, ct);
                if (old != null)
                {
                    if (old.EncounterId != encounterId || Math.Abs(old.Amount - req.Amount) > 0.001m ||
                        !string.Equals(old.PaymentChannel, req.PaymentChannel.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("请求幂等键冲突：已存在相同键但参数不同的押金记录");
                    }
                    return old;
                }
                var currentEnc = await _encounterRepo.GetByIdAsync(encounterId, ct);
                if (currentEnc == null || currentEnc.Status != 1)
                {
                    throw new InvalidOperationException("住院状态已变化，不能继续缴纳押金");
                }
                decimal newBalance = decimal.Round(currentEnc.PrepaymentBalance + req.Amount, 2);
                var payment = new IpdPrepayment
                {
                    PrepaymentNo = "IPY" + DateTime.UtcNow.ToString("yyMMddHHmmssfff"),
                    RequestKey = requestKey,
                    EncounterId = encounterId,
                    PatientId = currentEnc.PatientId,
                    Amount = req.Amount,
                    BalanceAfter = newBalance,
                    PaymentChannel = req.PaymentChannel.Trim().ToUpperInvariant(),
                    CashierId = userId,
                    PaidAtMs = SystemTime.UtcNowMs,
                    Remark = NullIfBlank(req.Remark) ?? "住院押金",
                    CreatedBy = userId,
                    UpdatedBy = userId
                };
                await _prepaymentRepo.InsertAsync(payment, ct);
                currentEnc.PrepaymentBalance = newBalance;
                currentEnc.UpdatedBy = userId;
                await _encounterRepo.UpdateAsync(currentEnc, ct);
                return payment;
            });
            await Audit("IPD_PREPAYMENT", prepayment.Id,
                $"住院押金入账 {prepayment.PrepaymentNo}，住院号 {enc.AdmissionNo}，金额 {prepayment.Amount:0.00}，余额 {prepayment.BalanceAfter:0.00}");
            using var conn = _connFactory.CreateConnection();
            var view = await conn.QuerySingleAsync<IpdPrepaymentView>(
                "SELECT pp.*, COALESCE(u.display_name, pp.cashier_id) AS cashier_name FROM ipd_prepayments pp LEFT JOIN sys_users u ON u.id = pp.cashier_id WHERE pp.id = @Id",
                new { Id = prepayment.Id });
            return ApiResponse<IpdPrepaymentView>.Success(view, "住院押金已入账");
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<IpdPrepaymentView>.Fail(409, ex.Message);
        }
        catch (DbConcurrencyException ex)
        {
            return ApiResponse<IpdPrepaymentView>.Fail(409, ex.Message);
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return ApiResponse<IpdPrepaymentView>.Fail(409, "该押金请求已提交，请刷新流水确认");
        }
    }

    [HttpPost("encounters/{encounterId}/accept")]
    public Task<ApiResponse<IpdEncounter>> AcceptPatient(
        string encounterId, [FromBody] AcceptIpdPatientRequest req)
    {
        return UpdateAssignmentCore(encounterId, req.AttendingDoctorId, req.PrimaryNurseId, req.NursingLevelCode, true);
    }

    [HttpPut("encounters/{encounterId}/assignment")]
    public Task<ApiResponse<IpdEncounter>> UpdateAssignment(
        string encounterId, [FromBody] UpdateIpdAssignmentRequest req)
    {
        return UpdateAssignmentCore(encounterId, req.AttendingDoctorId, req.PrimaryNurseId, req.NursingLevelCode, false);
    }

    private async Task<ApiResponse<IpdEncounter>> UpdateAssignmentCore(
        string encounterId,
        string doctorId,
        string nurseId,
        string nursingLevel,
        bool isAccept)
    {
        if (!HasAnyRole("WARD_NURSE", "ADMIN"))
        {
            return ApiResponse<IpdEncounter>.Fail(403, "仅病房护士或系统管理员可以接诊和分配人员");
        }
        if (string.IsNullOrWhiteSpace(doctorId) || string.IsNullOrWhiteSpace(nurseId))
        {
            return ApiResponse<IpdEncounter>.Fail(400, "请选择住院医师和责任护士");
        }
        IpdEncounter? enc = await _encounterRepo.GetByIdAsync(encounterId);
        if (enc == null)
        {
            return ApiResponse<IpdEncounter>.Fail(404, "住院记录不存在");
        }
        if (enc.Status != 1)
        {
            return ApiResponse<IpdEncounter>.Fail(409, "仅当前住院患者可接诊或分配人员");
        }
        if (isAccept && enc.TriageStatus == 1)
        {
            return ApiResponse<IpdEncounter>.Fail(409, "该患者已接诊，请使用人员调整功能");
        }
        if (!isAccept && enc.TriageStatus != 1)
        {
            return ApiResponse<IpdEncounter>.Fail(409, "患者尚未接诊，不能调整人员");
        }
        if (enc.PrepaymentBalance <= 0)
        {
            return ApiResponse<IpdEncounter>.Fail(409, "尚未缴纳住院押金，不可接诊；请收费员先完成押金入账");
        }
        if (!await IsAssignedUserAsync(doctorId, enc.AdmitDeptId, "IPD"))
        {
            return ApiResponse<IpdEncounter>.Fail(400, "所选住院医师不属于该住院科室");
        }
        if (!await IsAssignedUserAsync(nurseId, enc.AdmitWardId, "WARD"))
        {
            return ApiResponse<IpdEncounter>.Fail(400, "所选责任护士不属于该病区");
        }
        string level = NormalizeNursingLevel(nursingLevel);
        try
        {
            await _connFactory.ExecuteInTransactionAsync(async ct =>
            {
                var fresh = await _encounterRepo.GetByIdAsync(enc.Id, ct);
                if (fresh == null || fresh.Status != 1)
                {
                    throw new InvalidOperationException("住院状态已变化，请刷新");
                }
                if (isAccept && fresh.TriageStatus == 1)
                {
                    throw new InvalidOperationException("该患者已被其他护士接诊，请刷新");
                }
                fresh.AttendingDoctorId = doctorId;
                fresh.PrimaryNurseId = nurseId;
                fresh.NursingLevelCode = level;
                if (isAccept)
                {
                    fresh.TriageStatus = 1;
                    fresh.AcceptedAtMs = SystemTime.UtcNowMs;
                    fresh.AcceptedBy = CurrentUserId();
                }
                fresh.UpdatedBy = CurrentUserId();
                await _encounterRepo.UpdateAsync(fresh, ct);
                enc = fresh;
            });
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<IpdEncounter>.Fail(409, ex.Message);
        }
        catch (DbConcurrencyException ex)
        {
            return ApiResponse<IpdEncounter>.Fail(409, ex.Message);
        }
        await Audit(isAccept ? "IPD_ACCEPT" : "IPD_ASSIGNMENT", enc.Id,
            $"{(isAccept ? "病房接诊" : "调整责任人员")}：住院医师 {doctorId}，责任护士 {nurseId}，护理级别 {level}");
        return ApiResponse<IpdEncounter>.Success(enc, isAccept ? "接诊完成，住院医师和责任护士已绑定" : "人员分配已更新");
    }

    private async Task<bool> IsAssignedUserAsync(string userId, string scopeId, string category)
    {
        using var conn = _connFactory.CreateConnection();
        int count = await conn.ExecuteScalarAsync<int>(@"
SELECT COUNT(1) FROM sys_users u
JOIN sys_user_dept_posts p ON p.user_id = u.id
JOIN sys_roles r ON r.id = p.role_id
WHERE u.id = @UserId AND p.dept_id = @ScopeId AND p.dept_category = @Category
  AND u.is_active = 1 AND r.is_active = 1 AND r.role_code <> 'ADMIN'",
            new { UserId = userId, ScopeId = scopeId, Category = category });
        return count > 0;
    }

    private async Task Audit(string eventType, string targetId, string detail)
    {
        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync(eventType, "IpdInpatientFlow", targetId, detail, CurrentUserId());
        }
    }

    private string CurrentUserId() => _userSession?.UserId ?? string.Empty;

    private bool HasAnyRole(params string[] roles)
    {
        if (_userSession == null || string.IsNullOrWhiteSpace(_userSession.UserId))
        {
            return false;
        }
        return IsCurrentUserAdmin() || RoleCode.HasAnyRole(_userSession.Roles, roles);
    }

    private bool IsCurrentUserAdmin()
    {
        return _userSession != null
            && (RoleCode.IsAdmin(_userSession.RoleCode)
                || RoleCode.HasRole(_userSession.Roles, RoleCode.Admin));
    }

    private async Task<string?> LoadRelationTypeAsync(string deptId, string wardId)
    {
        using var conn = _connFactory.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<string>(@"
SELECT relation_type FROM sys_dept_ward_relations
WHERE ipd_dept_id = @DeptId AND ward_dept_id = @WardId AND is_active = 1
LIMIT 1", new { DeptId = deptId, WardId = wardId });
    }

    private static bool ValidPaymentChannel(string? channel)
    {
        string normalized = (channel ?? string.Empty).Trim().ToUpperInvariant();
        return normalized is "CASH" or "WECHAT" or "ALIPAY" or "BANK_CARD" or "INSURANCE";
    }

    private static string NormalizeNursingLevel(string? level)
    {
        return (level ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "LEVEL_0" or "SPECIAL" => "LEVEL_0",
            "LEVEL_1" or "FIRST" or "1" => "LEVEL_1",
            "LEVEL_2" or "SECOND" or "2" => "LEVEL_2",
            "LEVEL_3" or "THIRD" or "3" => "LEVEL_3",
            _ => "LEVEL_2"
        };
    }

    private static string? NullIfBlank(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static async Task<IpdAdmissionCardView?> LoadCardAsync(DbConnection conn, string cardId)
    {
        const string sql = @"
SELECT c.*, p.full_name AS patient_name, p.gender_code AS gender_name,
       p.medical_record_no AS medical_record_no,
       COALESCE(d1.display_name, c.source_doctor_id) AS source_doctor_name,
       COALESCE(d2.dept_name, c.suggested_ipd_dept_id) AS suggested_ipd_dept_name,
       COALESCE(d3.dept_name, '') AS suggested_ward_name,
       COALESCE(e.admission_no, '') AS encounter_no,
       COALESCE(e.prepayment_balance, 0) AS prepayment_balance,
       COALESCE(e.bed_code, '') AS bed_code,
       COALESCE(e.triage_status, 0) AS triage_status
FROM ipd_admission_cards c
LEFT JOIN pat_patients p ON p.id = c.patient_id
LEFT JOIN sys_users d1 ON d1.id = c.source_doctor_id
LEFT JOIN sys_departments d2 ON d2.id = c.suggested_ipd_dept_id
LEFT JOIN sys_departments d3 ON d3.id = c.suggested_ward_id
LEFT JOIN ipd_encounters e ON e.id = c.encounter_id
WHERE c.id = @CardId";
        return await conn.QuerySingleOrDefaultAsync<IpdAdmissionCardView>(sql, new { CardId = cardId });
    }

    public sealed record CancelRequest(string? Reason);

    public sealed record CreateAdmissionWithPatientRequest(
        // 患者信息
        string FullName,
        string GenderCode,
        int BirthDateInt,
        string IdCardNo,
        string Phone,
        string? GuardianName = null,
        string? GuardianIdCardNo = null,
        List<string>? AllergyTags = null,
        // 住院卡信息
        string SuggestedIpdDeptId = "",
        string? SuggestedWardId = null,
        string? AdmissionDiagnosis = null,
        string? Remark = null,
        string? RequestKey = null,
        bool ReuseExistingPatient = false
    );

    private sealed class PatPatientRow
    {
        public string id { get; set; } = string.Empty;
        public string medical_record_no { get; set; } = string.Empty;
        public string full_name { get; set; } = string.Empty;
    }

    private sealed class IdNameRow
    {
        public string Id { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
    }

    private sealed class IdCodeRow
    {
        public string Id { get; set; } = string.Empty;
        public string DeptCode { get; set; } = string.Empty;
    }

    private sealed class RegistrationOptionRow
    {
        public string Id { get; set; } = string.Empty;
        public string DeptCode { get; set; } = string.Empty;
        public string DeptName { get; set; } = string.Empty;
        public string? WardId { get; set; }
        public string? WardCode { get; set; }
        public string? WardName { get; set; }
        public string? RelationType { get; set; }
    }
}
