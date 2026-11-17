using System.Data.Common;
using System.Text.Json;
using Dapper;
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
    private static readonly JsonSerializerOptions ExtJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IRepository<OpdScheduleSlot> _slotRepo;
    private readonly IRepository<OpdAppointment> _appointRepo;
    private readonly IRepository<OpdVisit> _visitRepo;
    private readonly IRepository<OpdTriageQueue> _queueRepo;
    private readonly IRepository<OpdPrescription> _rxRepo;
    private readonly IRepository<OpdPrescriptionItem> _rxItemRepo;
    private readonly IRepository<OpdScheduleTemplate> _templateRepo;
    private readonly IRepository<OpdInfectiousReportCard> _infectiousRepo;
    private readonly IDbConnectionFactory _connFactory;
    private readonly IPatientReadOnlyContract? _patientContract;
    private readonly IDepartmentReadOnlyContract? _departmentContract;
    private readonly IMasterDataReadOnlyContract? _masterData;
    private readonly IAuditTrailContract? _auditTrail;
    private readonly IUserSession? _userSession;
    private readonly IPharmacyInventoryContract? _inventoryContract;

    public OutpatientController(
        IRepository<OpdScheduleSlot> slotRepo,
        IRepository<OpdAppointment> appointRepo,
        IRepository<OpdVisit> visitRepo,
        IRepository<OpdTriageQueue> queueRepo,
        IRepository<OpdPrescription> rxRepo,
        IRepository<OpdPrescriptionItem> rxItemRepo,
        IDbConnectionFactory? connFactory = null,
        IPatientReadOnlyContract? patientContract = null,
        IDepartmentReadOnlyContract? departmentContract = null,
        IMasterDataReadOnlyContract? masterData = null,
        IAuditTrailContract? auditTrail = null,
        IUserSession? userSession = null,
        IRepository<OpdScheduleTemplate>? templateRepo = null,
        IPharmacyInventoryContract? inventoryContract = null,
        IRepository<OpdInfectiousReportCard>? infectiousRepo = null)
    {
        _slotRepo = slotRepo;
        _appointRepo = appointRepo;
        _visitRepo = visitRepo;
        _queueRepo = queueRepo;
        _rxRepo = rxRepo;
        _rxItemRepo = rxItemRepo;
        _connFactory = connFactory ?? new SqliteConnectionFactory();
        _patientContract = patientContract;
        _departmentContract = departmentContract;
        _masterData = masterData;
        _auditTrail = auditTrail;
        _userSession = userSession;
        _inventoryContract = inventoryContract;
        _templateRepo = templateRepo ?? new DapperRepository<OpdScheduleTemplate>(_connFactory, "opd_schedule_templates");
        _infectiousRepo = infectiousRepo ?? new DapperRepository<OpdInfectiousReportCard>(_connFactory, "opd_infectious_report_cards");
    }

    public sealed record SaveScheduleTemplateRequestDto(
        string TemplateName,
        string OpdDeptId,
        string? ClinicRoomId,
        string DoctorId,
        string WeekdayCodes,
        string SlotPeriodCode,
        int StartMinute,
        int EndMinute,
        string RegLevelCode,
        decimal RegistrationFee,
        int TotalCapacity,
        int IsActive = 1,
        string? CampusId = null);

    public sealed record GenerateScheduleRequestDto(
        string TemplateId,
        DateOnly StartDate,
        DateOnly EndDate);

    public sealed record UpdateScheduleSlotRequestDto(
        bool IsStopped,
        int? TotalCapacity = null,
        string? SubstituteDoctorId = null,
        int? AddCapacity = null);

    public sealed record ScheduleGenerationResultDto(
        int CreatedCount,
        int SkippedCount,
        int DaysMatched,
        string Message);

    [HttpGet("schedule-staff")]
    public async Task<ApiResponse<IReadOnlyList<OpdScheduleStaffOption>>> GetScheduleStaff()
    {
        if (!CanAdministerSchedule())
            return ApiResponse<IReadOnlyList<OpdScheduleStaffOption>>.Fail(403, "仅系统管理员可以维护门诊排班");
        using var conn = _connFactory.CreateConnection();
        var staff = await conn.QueryAsync<OpdScheduleStaffOption>(
            """
            SELECT u.id AS user_id,
                   u.display_name,
                   u.employee_no,
                   d.id AS dept_id,
                   d.dept_name,
                   r.id AS role_id,
                   r.role_code,
                   r.role_name
            FROM sys_user_dept_posts udp
            INNER JOIN sys_users u ON u.id = udp.user_id AND u.is_active = 1
            INNER JOIN sys_departments d ON d.id = udp.dept_id AND d.is_active = 1 AND d.dept_category = 'OPD'
            INNER JOIN sys_roles r ON r.id = udp.role_id AND r.is_active = 1
            WHERE udp.dept_category = 'OPD'
              AND (u.prescribe_level >= 1 OR u.emr_sign_level >= 1)
              AND UPPER(r.role_code) NOT LIKE '%NURSE%'
              AND UPPER(r.role_code) NOT LIKE '%NURSING%'
              AND UPPER(r.role_code) NOT LIKE '%PHARMACIST%'
              AND UPPER(r.role_code) NOT LIKE '%CASHIER%'
            ORDER BY d.dept_name, u.display_name, r.role_name
            """,
            new { });
        return ApiResponse<IReadOnlyList<OpdScheduleStaffOption>>.Success(staff.ToList());
    }

    [HttpGet("schedule-templates")]
    public async Task<ApiResponse<IReadOnlyList<OpdScheduleTemplate>>> GetScheduleTemplates(
        [FromQuery] string? opdDeptId = null,
        [FromQuery] bool includeInactive = true)
    {
        if (!CanViewSchedule())
            return ApiResponse<IReadOnlyList<OpdScheduleTemplate>>.Fail(403, "当前角色无权查看门诊排班模板");

        var templates = await _templateRepo.QueryAsync(
            """
            SELECT * FROM opd_schedule_templates
            WHERE (@DeptId IS NULL OR opd_dept_id = @DeptId)
              AND (@IncludeInactive = 1 OR is_active = 1)
            ORDER BY is_active DESC, opd_dept_id ASC, weekday_codes ASC, start_minute ASC
            """,
            new { DeptId = opdDeptId, IncludeInactive = includeInactive ? 1 : 0 });
        return ApiResponse<IReadOnlyList<OpdScheduleTemplate>>.Success(templates);
    }

    [HttpPost("schedule-templates")]
    public async Task<ApiResponse<OpdScheduleTemplate>> CreateScheduleTemplate(
        [FromBody] SaveScheduleTemplateRequestDto req)
    {
        if (!CanAdministerSchedule())
            return ApiResponse<OpdScheduleTemplate>.Fail(403, "仅系统管理员可以维护门诊排班模板");
        if (req == null)
            return ApiResponse<OpdScheduleTemplate>.Fail(400, "排班模板内容不能为空");

        var template = new OpdScheduleTemplate
        {
            Id = Guid.NewGuid().ToString("D"),
            CreatedBy = CurrentOperatorId,
            UpdatedBy = CurrentOperatorId
        };
        var validation = ApplyTemplateRequest(template, req);
        if (validation != null)
            return ApiResponse<OpdScheduleTemplate>.Fail(400, validation);
        validation = await ValidateTemplateReferencesAsync(template);
        if (validation != null)
            return ApiResponse<OpdScheduleTemplate>.Fail(400, validation);

        await _templateRepo.InsertAsync(template);
        await RecordScheduleAuditAsync("OPD_SCHEDULE_TEMPLATE_CREATE", template.Id,
            $"新建排班模板：{template.TemplateName}，门诊科室：{template.OpdDeptId}，时间：{template.StartMinute:0000}-{template.EndMinute:0000}");
        return ApiResponse<OpdScheduleTemplate>.Success(template, "排班模板已创建");
    }

    [HttpPut("schedule-templates/{id}")]
    public async Task<ApiResponse<OpdScheduleTemplate>> UpdateScheduleTemplate(
        string id,
        [FromBody] SaveScheduleTemplateRequestDto req)
    {
        if (!CanAdministerSchedule())
            return ApiResponse<OpdScheduleTemplate>.Fail(403, "仅系统管理员可以维护门诊排班模板");
        if (req == null)
            return ApiResponse<OpdScheduleTemplate>.Fail(400, "排班模板内容不能为空");

        var template = await _templateRepo.GetByIdAsync(id);
        if (template == null)
            return ApiResponse<OpdScheduleTemplate>.Fail(404, "排班模板不存在");

        var validation = ApplyTemplateRequest(template, req);
        if (validation != null)
            return ApiResponse<OpdScheduleTemplate>.Fail(400, validation);
        validation = await ValidateTemplateReferencesAsync(template);
        if (validation != null)
            return ApiResponse<OpdScheduleTemplate>.Fail(400, validation);
        template.UpdatedBy = CurrentOperatorId;

        await _templateRepo.UpdateAsync(template);
        await RecordScheduleAuditAsync("OPD_SCHEDULE_TEMPLATE_UPDATE", template.Id,
            $"更新排班模板：{template.TemplateName}，启用状态：{template.IsActive}");
        return ApiResponse<OpdScheduleTemplate>.Success(template, "排班模板已更新。历史号源保持不变");
    }

    [HttpPost("schedule-templates/{id}/generate")]
    public async Task<ApiResponse<ScheduleGenerationResultDto>> GenerateScheduleSlots(
        string id,
        [FromBody] GenerateScheduleRequestDto req)
    {
        if (!CanAdministerSchedule())
            return ApiResponse<ScheduleGenerationResultDto>.Fail(403, "仅系统管理员可以发布门诊号源");
        if (req == null || !string.Equals(req.TemplateId, id, StringComparison.OrdinalIgnoreCase))
            return ApiResponse<ScheduleGenerationResultDto>.Fail(400, "排班模板标识与发布请求不一致");

        var template = await _templateRepo.GetByIdAsync(id);
        if (template == null)
            return ApiResponse<ScheduleGenerationResultDto>.Fail(404, "排班模板不存在");
        if (template.IsActive != 1)
            return ApiResponse<ScheduleGenerationResultDto>.Fail(400, "该模板已停用，不能发布号源");

        var validation = ValidateTemplate(template);
        if (validation != null)
            return ApiResponse<ScheduleGenerationResultDto>.Fail(400, validation);
        validation = await ValidateTemplateReferencesAsync(template);
        if (validation != null)
            return ApiResponse<ScheduleGenerationResultDto>.Fail(400, validation);

        var start = req.StartDate;
        var end = req.EndDate;
        if (start == default || end == default)
            return ApiResponse<ScheduleGenerationResultDto>.Fail(400, "请选择完整的起始和结束日期");
        if (end < start)
            return ApiResponse<ScheduleGenerationResultDto>.Fail(400, "结束日期不能早于起始日期");
        if (end.DayNumber - start.DayNumber > 365)
            return ApiResponse<ScheduleGenerationResultDto>.Fail(400, "单次发布范围不能超过366天");

        var weekdays = ParseWeekdays(template.WeekdayCodes);
        var created = 0;
        var skipped = 0;
        var daysMatched = 0;
        await _connFactory.ExecuteInTransactionAsync(async ct =>
        {
            for (var date = start; date <= end; date = date.AddDays(1))
            {
                if (!weekdays.Contains((int)date.DayOfWeek == 0 ? 7 : (int)date.DayOfWeek))
                    continue;
                daysMatched++;
                var dateInt = date.Year * 10000 + date.Month * 100 + date.Day;
                var existing = await _slotRepo.QueryAsync(
                    """
                    SELECT * FROM opd_schedule_slots
                    WHERE opd_dept_id = @DeptId
                      AND doctor_id = @DoctorId
                      AND schedule_date_int = @DateInt
                      AND slot_period_code = @Period
                      AND reg_level_code = @Level
                    """,
                    new
                    {
                        DeptId = template.OpdDeptId,
                        DoctorId = template.DoctorId,
                        DateInt = dateInt,
                        Period = template.SlotPeriodCode,
                        Level = template.RegLevelCode
                    }, ct);
                if (existing.Count > 0)
                {
                    skipped++;
                    continue;
                }

                var dateStart = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8));
                var slot = new OpdScheduleSlot
                {
                    CampusId = template.CampusId,
                    OpdDeptId = template.OpdDeptId,
                    ClinicRoomId = template.ClinicRoomId,
                    DoctorId = template.DoctorId,
                    ScheduleDateInt = dateInt,
                    SlotPeriodCode = template.SlotPeriodCode,
                    StartsAtMs = dateStart.AddMinutes(template.StartMinute).ToUnixTimeMilliseconds(),
                    EndsAtMs = dateStart.AddMinutes(template.EndMinute).ToUnixTimeMilliseconds(),
                    RegLevelCode = template.RegLevelCode,
                    RegistrationFee = template.RegistrationFee,
                    TotalCapacity = template.TotalCapacity,
                    ReservedCount = 0,
                    SlotStatus = 1,
                    CreatedBy = CurrentOperatorId,
                    UpdatedBy = CurrentOperatorId
                };
                await _slotRepo.InsertAsync(slot, ct);
                created++;
            }
            return 0;
        });

        var message = created > 0
            ? $"已发布 {created} 个号源，跳过 {skipped} 个已有班次"
            : $"没有新增号源，跳过 {skipped} 个已有班次";
        await RecordScheduleAuditAsync("OPD_SCHEDULE_GENERATE", template.Id,
            $"模板 {template.TemplateName}，{start:yyyy-MM-dd} 至 {end:yyyy-MM-dd}，新增 {created}，跳过 {skipped}");
        return ApiResponse<ScheduleGenerationResultDto>.Success(
            new ScheduleGenerationResultDto(created, skipped, daysMatched, message), message);
    }

    [HttpPatch("schedules/{id}")]
    public async Task<ApiResponse<OpdScheduleSlot>> UpdateScheduleSlot(
        string id,
        [FromBody] UpdateScheduleSlotRequestDto req)
    {
        var slot = await _slotRepo.GetByIdAsync(id);
        if (slot == null)
            return ApiResponse<OpdScheduleSlot>.Fail(404, "号源不存在");

        bool isDoctorOfSlot = _userSession != null && _userSession.UserId == slot.DoctorId;
        if (!CanAdministerSchedule() && !isDoctorOfSlot)
            return ApiResponse<OpdScheduleSlot>.Fail(403, "仅系统管理员或出诊医生可以调整或停诊号源");

        if (req.IsStopped)
        {
            if (slot.ReservedCount > 0)
            {
                if (string.IsNullOrWhiteSpace(req.SubstituteDoctorId))
                {
                    return ApiResponse<OpdScheduleSlot>.Fail(409, $"该号源已有 {slot.ReservedCount} 个预约，停诊必须指定替诊医生 (substitute_doctor_id)");
                }
                slot.SubstituteDoctorId = req.SubstituteDoctorId;
                using var conn = _connFactory.CreateConnection();
                await conn.ExecuteAsync(
                    "UPDATE opd_appointments SET ext_data_json = json_set(COALESCE(ext_data_json, '{}'), '$.substitute_doctor_id', @SubDoctorId) WHERE schedule_id = @SlotId",
                    new { SubDoctorId = req.SubstituteDoctorId, SlotId = slot.Id });
                await conn.ExecuteAsync(
                    "UPDATE opd_visits SET doctor_id = @SubDoctorId WHERE schedule_id = @SlotId",
                    new { SubDoctorId = req.SubstituteDoctorId, SlotId = slot.Id });
            }
            slot.SlotStatus = 3;
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(req.SubstituteDoctorId))
            {
                slot.SubstituteDoctorId = req.SubstituteDoctorId;
            }
            var capacity = req.AddCapacity.HasValue && req.AddCapacity.Value > 0
                ? slot.TotalCapacity + req.AddCapacity.Value
                : (req.TotalCapacity ?? slot.TotalCapacity);
            if (capacity < 1)
                return ApiResponse<OpdScheduleSlot>.Fail(400, "号源容量必须大于0");
            if (capacity < slot.ReservedCount)
                return ApiResponse<OpdScheduleSlot>.Fail(409, $"号源容量不能低于已预约数量 {slot.ReservedCount}");

            slot.TotalCapacity = capacity;
            slot.SlotStatus = slot.ReservedCount >= capacity ? 2 : 1;
        }

        slot.UpdatedBy = CurrentOperatorId;
        await _slotRepo.UpdateAsync(slot);
        await RecordScheduleAuditAsync("OPD_SCHEDULE_SLOT_UPDATE", slot.Id,
            $"{(req.IsStopped ? "停诊" : "恢复")}号源：{slot.ScheduleDateInt} {slot.SlotPeriodCode}，容量 {slot.TotalCapacity}，替诊：{slot.SubstituteDoctorId ?? "无"}");
        return ApiResponse<OpdScheduleSlot>.Success(slot, req.IsStopped ? "号源已停诊并转移预约至替诊医生" : "号源状态已更新");
    }

    [HttpPost("schedules/{id}/add-capacity")]
    public async Task<ApiResponse<OpdScheduleSlot>> AddScheduleSlotCapacity(
        string id,
        [FromQuery] int addCount = 1)
    {
        return await UpdateScheduleSlot(id, new UpdateScheduleSlotRequestDto(false, null, null, addCount));
    }

    [HttpPost("schedules/{id}/substitute")]
    public async Task<ApiResponse<OpdScheduleSlot>> SubstituteScheduleDoctor(
        string id,
        [FromQuery] string substituteDoctorId)
    {
        return await UpdateScheduleSlot(id, new UpdateScheduleSlotRequestDto(true, null, substituteDoctorId, null));
    }

    private bool CanAdministerSchedule()
    {
        return _userSession != null
            && !string.IsNullOrWhiteSpace(_userSession.UserId)
            && (RoleCode.IsAdmin(_userSession.RoleCode)
                || RoleCode.HasRole(_userSession.Roles, RoleCode.Admin));
    }

    private bool CanViewSchedule()
    {
        if (_userSession == null || string.IsNullOrWhiteSpace(_userSession.UserId))
            return false;
        return CanAdministerSchedule()
            || RoleCode.HasRole(_userSession.Roles, RoleCode.OpdDoctor)
            || string.Equals(_userSession.ActiveDeptCategory.ToString(), "OPD", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string?> ValidateTemplateReferencesAsync(OpdScheduleTemplate template)
    {
        using var conn = _connFactory.CreateConnection();
        var departmentExists = await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(1) FROM sys_departments
            WHERE id = @DeptId AND dept_category = 'OPD' AND is_active = 1
            """,
            new { DeptId = template.OpdDeptId });
        if (departmentExists == 0)
            return "所选门诊科室不存在或已停用";

        var roomExists = await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(1) FROM sys_departments
            WHERE id = @RoomId AND is_active = 1
            """,
            new { RoomId = template.ClinicRoomId });
        if (!string.IsNullOrWhiteSpace(template.ClinicRoomId)
            && roomExists == 0
            && template.ClinicRoomId is not ("ROOM-OPD-101" or "ROOM-ER-01"))
            return "所选诊室不存在或已停用";

        var doctorGranted = await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(1)
            FROM sys_user_dept_posts udp
            INNER JOIN sys_users u ON u.id = udp.user_id AND u.is_active = 1
            INNER JOIN sys_departments d ON d.id = udp.dept_id AND d.id = @DeptId
                AND d.dept_category = 'OPD' AND d.is_active = 1
            INNER JOIN sys_roles r ON r.id = udp.role_id AND r.is_active = 1
            WHERE udp.user_id = @DoctorId
              AND udp.dept_category = 'OPD'
              AND (u.prescribe_level >= 1 OR u.emr_sign_level >= 1)
              AND UPPER(r.role_code) NOT LIKE '%NURSE%'
              AND UPPER(r.role_code) NOT LIKE '%NURSING%'
              AND UPPER(r.role_code) NOT LIKE '%PHARMACIST%'
              AND UPPER(r.role_code) NOT LIKE '%CASHIER%'
            """,
            new { DoctorId = template.DoctorId, DeptId = template.OpdDeptId });
        if (doctorGranted == 0)
            return "所选医生未获得该门诊科室的有效出诊授权";
        return null;
    }

    private string CurrentOperatorId => string.IsNullOrEmpty(_userSession?.UserId) ? "SYSTEM" : _userSession.UserId;

    private bool CanPrescribe()
    {
        return IsAdmin() || (IsOpdClinicalDoctor() && _userSession!.PrescribeLevel >= 1);
    }

    private bool IsOpdClinicalDoctor()
    {
        if (_userSession == null || string.IsNullOrWhiteSpace(_userSession.UserId))
            return false;
        if (!string.Equals(_userSession.ActiveDeptCategory.ToString(), "OPD", StringComparison.OrdinalIgnoreCase))
            return false;
        if (_userSession.PrescribeLevel < 1 && _userSession.EmrSignLevel < 1)
            return false;
        return !HasAnyRole("WARD_NURSE", "OPD_NURSE", "NURSE", "NURSING", "PHARMACIST", "CASHIER");
    }

    private bool CanAuditPrescription()
    {
        return IsAdmin() || HasAnyRole("PHARMACIST");
    }

    private bool CanSignSkinTest()
    {
        return IsAdmin() || HasAnyRole("OPD_NURSE", "WARD_NURSE", "NURSE", "NURSING");
    }

    private bool IsAdmin()
    {
        if (_userSession == null || string.IsNullOrWhiteSpace(_userSession.UserId)) return false;
        return RoleCode.IsAdmin(_userSession.RoleCode)
            || RoleCode.HasRole(_userSession.Roles, RoleCode.Admin);
    }

    private bool HasAnyRole(params string[] roleCodes)
    {
        if (_userSession == null || string.IsNullOrWhiteSpace(_userSession.UserId)) return false;
        return RoleCode.HasAnyRole(_userSession.Roles, roleCodes);
    }

    private static bool CanEditPrescription(int status) => status is 0 or 2;

    private static string NormalizeAllergyToken(string value)
    {
        return new string(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }

    private static string? FindAllergyConflict(
        IReadOnlyList<string> allergyTags,
        DrugCatalogSnapshotDto drug)
    {
        var drugToken = NormalizeAllergyToken(drug.DrugName);
        if (drugToken.Length == 0) return null;

        foreach (var tag in allergyTags)
        {
            if (string.IsNullOrWhiteSpace(tag)) continue;
            var tagToken = NormalizeAllergyToken(tag
                .Replace("过敏史", "", StringComparison.OrdinalIgnoreCase)
                .Replace("过敏", "", StringComparison.OrdinalIgnoreCase)
                .Replace("皮试阳性", "", StringComparison.OrdinalIgnoreCase));
            if (tagToken.Length < 2) continue;

            if (drugToken.Contains(tagToken, StringComparison.Ordinal) ||
                tagToken.Contains(drugToken, StringComparison.Ordinal) ||
                (tagToken.Contains("penicillin", StringComparison.Ordinal) &&
                 (drugToken.Contains("penicillin", StringComparison.Ordinal) ||
                  drugToken.Contains("amoxicillin", StringComparison.Ordinal))) ||
                (tagToken.Contains("青霉素", StringComparison.Ordinal) &&
                 (drugToken.Contains("青霉素", StringComparison.Ordinal) ||
                  drugToken.Contains("阿莫西林", StringComparison.Ordinal))))
            {
                return tag;
            }
        }
        return null;
    }

    private static string? ValidateMedicationInput(OpdPrescriptionItem item)
    {
        if (string.IsNullOrWhiteSpace(item.DrugId))
            return "处方明细必须指定院内药品目录 ID 或编码";
        if (item.TotalQuantity <= 0 || item.TotalQuantity > 1000)
            return "药品数量必须为 1 至 1000 之间";
        if (item.Days <= 0 || item.Days > 365)
            return "用药天数必须为 1 至 365 天";
        if (item.DoseOnce < 0 || item.DoseOnce > 100000)
            return "单次剂量无效";
        return null;
    }

    private async Task<string?> ValidatePrescriptionSafetyAsync(
        string patientId,
        IEnumerable<DrugCatalogSnapshotDto> drugs,
        IEnumerable<OpdPrescriptionItem>? items = null,
        string? agentIdCard = null,
        CancellationToken ct = default)
    {
        if (_patientContract == null)
            return "患者过敏档案服务不可用，禁止开具处方";

        var patient = await _patientContract.GetPatientSummaryAsync(patientId, ct);
        if (patient == null || !patient.IsActive)
            return "患者不存在或已停用，禁止开具处方";

        int doctorPrescribeLevel = _userSession?.PrescribeLevel ?? 0;
        var itemList = items?.ToList() ?? new List<OpdPrescriptionItem>();

        foreach (var drug in drugs)
        {
            var conflict = FindAllergyConflict(patient.AllergyTags, drug);
            if (conflict != null)
                return $"患者存在 [{conflict}]，禁止开立 [{drug.DrugName}]";

            var matchingItem = itemList.FirstOrDefault(x => x.DrugId == drug.DrugId);
            if (matchingItem != null && matchingItem.SkinTestFlag == 4)
            {
                return $"药品 [{drug.DrugName}] 皮试结果为阳性，强阻断禁止开立";
            }

            if (drug.AntibioticLevel == 1 && doctorPrescribeLevel < 2 && !IsAdmin())
            {
                return $"当前医师处方权限等级为 {doctorPrescribeLevel}，不足以开具限制使用级抗菌药物 [{drug.DrugName}]（需主治医师及以上）";
            }
            if (drug.AntibioticLevel == 2)
            {
                if (doctorPrescribeLevel < 3 && !IsAdmin())
                {
                    return $"当前医师处方权限等级为 {doctorPrescribeLevel}，不足以开具特殊使用级抗菌药物 [{drug.DrugName}]（需副主任/主任医师）";
                }
                using var conn = _connFactory.CreateConnection();
                int microCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COUNT(1) FROM ord_medical_orders WHERE patient_id = @PatientId AND (order_category = 'MICROBIOLOGY' OR order_category = 'LAB' OR item_name LIKE '%微生物%' OR item_name LIKE '%细菌培养%')",
                    new { PatientId = patientId }, cancellationToken: ct));
                if (microCount == 0)
                {
                    return $"开具特殊使用级抗菌药物 [{drug.DrugName}] 必须预先或同时开具微生物培养与药敏送检医嘱";
                }
            }

            if (drug.IsNarcotic)
            {
                if (string.IsNullOrWhiteSpace(agentIdCard) || !IdCardValidator.IsValid(agentIdCard))
                {
                    return $"开具麻精类药品 [{drug.DrugName}] 必须录入合法的代办人 18 位身份证号码";
                }
                if (matchingItem != null && matchingItem.Days > 7)
                {
                    return $"麻精类药品 [{drug.DrugName}] 单张处方最大用药天数不得超过 7 天（当前：{matchingItem.Days} 天）";
                }
            }
        }
        return null;
    }

    private async Task<(DrugCatalogSnapshotDto? Drug, string? Error)> ResolveDrugAsync(
        string drugIdOrCode,
        CancellationToken ct = default)
    {
        if (_inventoryContract == null)
            return (null, "药品目录服务不可用，禁止开立处方");
        var drug = await _inventoryContract.GetDrugSnapshotAsync(drugIdOrCode, ct);
        if (drug == null) return (null, $"药品 [{drugIdOrCode}] 不存在");
        if (!drug.IsActive) return (null, $"药品 [{drug.DrugName}] 已停用，禁止开立");
        return (drug, null);
    }

    private static void ApplyAuthoritativeDrug(OpdPrescriptionItem item, DrugCatalogSnapshotDto drug)
    {
        item.DrugId = drug.DrugId;
        item.DrugNameSnapshot = drug.DrugName;
        item.UnitPrice = drug.UnitPrice;
        if (drug.IsSkinTestRequired && item.SkinTestFlag == 0)
            item.SkinTestFlag = 1;
        else if (!drug.IsSkinTestRequired)
            item.SkinTestFlag = 0;
    }

    private async Task<string?> ValidateVisitForPrescriptionAsync(
        string visitId,
        string patientId,
        CancellationToken ct = default)
    {
        var visit = await _visitRepo.GetByIdAsync(visitId, ct);
        if (visit == null) return "门诊就诊记录不存在";
        if (visit.Status == 1) return "请先完成医师接诊，再开立处方";
        if (visit.Status != 2) return "当前就诊已完成或已取消，不能继续开立处方";
        if (!string.Equals(visit.PatientId, patientId, StringComparison.OrdinalIgnoreCase))
            return "处方患者与就诊记录不一致";
        return null;
    }

    private async Task<(string? DiagnosisName, string? Error)> ResolveDiagnosisAsync(
        OpdVisit visit,
        string? diagnosisCode,
        string? diagnosisName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(diagnosisCode) && string.IsNullOrWhiteSpace(diagnosisName))
            return (null, "必须填写主诊断编码或诊断名称");

        var resolvedName = diagnosisName?.Trim();
        if (_masterData == null || string.IsNullOrWhiteSpace(diagnosisCode))
            return (resolvedName, null);

        var icd = await _masterData.GetDiagnosisByCodeAsync(diagnosisCode.Trim(), ct);
        if (icd == null)
            return (resolvedName, null);

        if (string.IsNullOrWhiteSpace(resolvedName))
            resolvedName = icd.DiseaseName;

        if (_patientContract == null ||
            icd.GenderExclusive.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            return (resolvedName, null);

        var patient = await _patientContract.GetPatientSummaryAsync(visit.PatientId, ct);
        var gender = (patient?.GenderCode ?? string.Empty).Trim().ToUpperInvariant();
        var isMale = gender is "1" or "男" or "MALE";
        var isFemale = gender is "2" or "女" or "FEMALE";
        if (!isMale && !isFemale)
            return (resolvedName, $"GATE-03 性别未确认：无法开具性别专有 ICD-10 诊断 [{icd.IcdCode} {icd.DiseaseName}]，请先核实患者性别");
        if (isMale && icd.GenderExclusive.Equals("FEMALE_ONLY", StringComparison.OrdinalIgnoreCase))
            return (resolvedName, $"GATE-03 诊断性别冲突：男性患者严禁下达女性专有 ICD-10 诊断 [{icd.IcdCode} {icd.DiseaseName}]");
        if (isFemale && icd.GenderExclusive.Equals("MALE_ONLY", StringComparison.OrdinalIgnoreCase))
            return (resolvedName, $"GATE-03 诊断性别冲突：女性患者严禁下达男性专有 ICD-10 诊断 [{icd.IcdCode} {icd.DiseaseName}]");
        return (resolvedName, null);
    }

    private bool CanAccessVisit(OpdVisit visit) =>
        IsAdmin()
        || string.Equals(visit.DoctorId, _userSession?.UserId, StringComparison.OrdinalIgnoreCase)
        || string.Equals(visit.OpdDeptId, _userSession?.ActiveDeptId, StringComparison.OrdinalIgnoreCase);

    private string? ApplyTemplateRequest(OpdScheduleTemplate template, SaveScheduleTemplateRequestDto req)
    {
        template.CampusId = string.IsNullOrWhiteSpace(req.CampusId) ? "CAMPUS-01" : req.CampusId.Trim();
        template.OpdDeptId = req.OpdDeptId?.Trim() ?? string.Empty;
        template.ClinicRoomId = string.IsNullOrWhiteSpace(req.ClinicRoomId) ? null : req.ClinicRoomId.Trim();
        template.DoctorId = req.DoctorId?.Trim() ?? string.Empty;
        template.TemplateName = req.TemplateName?.Trim() ?? string.Empty;
        template.WeekdayCodes = string.Join(",", ParseWeekdays(req.WeekdayCodes).OrderBy(x => x));
        template.SlotPeriodCode = req.SlotPeriodCode?.Trim().ToUpperInvariant() ?? string.Empty;
        template.StartMinute = req.StartMinute;
        template.EndMinute = req.EndMinute;
        template.RegLevelCode = req.RegLevelCode?.Trim().ToUpperInvariant() ?? string.Empty;
        template.RegistrationFee = req.RegistrationFee;
        template.TotalCapacity = req.TotalCapacity;
        template.IsActive = req.IsActive == 0 ? 0 : 1;
        return ValidateTemplate(template);
    }

    private static string? ValidateTemplate(OpdScheduleTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.TemplateName))
            return "请填写模板名称";
        if (template.TemplateName.Length > 128)
            return "模板名称不能超过128个字符";
        if (string.IsNullOrWhiteSpace(template.OpdDeptId))
            return "请选择门诊科室";
        if (string.IsNullOrWhiteSpace(template.DoctorId))
            return "请选择出诊医生";
        if (string.IsNullOrWhiteSpace(template.WeekdayCodes) || ParseWeekdays(template.WeekdayCodes).Count == 0)
            return "请至少选择一个出诊日";
        if (!IsWeekdayList(template.WeekdayCodes))
            return "出诊日只能包含周一至周日 (1-7)";
        if (!IsValidPeriod(template.SlotPeriodCode))
            return "坐诊时段必须为上午、下午、夜间或全天";
        if (!IsValidRegLevel(template.RegLevelCode))
            return "请选择有效的挂号级别";
        if (template.StartMinute < 0 || template.EndMinute > 1439 || template.StartMinute >= template.EndMinute)
            return "出诊时间须满足 00:00 <= 开始 < 结束 <= 23:59";
        if (template.RegistrationFee < 0 || template.RegistrationFee > 99999.99m)
            return "挂号费范围无效";
        if (template.TotalCapacity < 1 || template.TotalCapacity > 9999)
            return "号源容量必须在1至9999之间";
        return null;
    }

    private static HashSet<int> ParseWeekdays(string? codes)
    {
        var result = new HashSet<int>();
        foreach (var part in (codes ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out var day) && day >= 1 && day <= 7)
                result.Add(day);
        }
        return result;
    }

    private static bool IsWeekdayList(string codes) =>
        codes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(part => int.TryParse(part, out var day) && day >= 1 && day <= 7);

    private static bool IsValidPeriod(string code) => code is "MORNING" or "AFTERNOON" or "EVENING" or "ALL_DAY";

    private static bool IsValidRegLevel(string code) => code is "NORMAL" or "EXPERT" or "SPECIALIST" or "EMERGENCY";

    private async Task RecordScheduleAuditAsync(string eventType, string targetId, string detail)
    {
        if (_auditTrail == null)
            return;
        await _auditTrail.RecordAuditAsync(
            eventType,
            "OPD_SCHEDULE",
            targetId,
            detail,
            _userSession?.UserId,
            _userSession?.DisplayName,
            _userSession?.ActiveDeptId);
    }

    [HttpGet("schedules")]
    public async Task<ApiResponse<IReadOnlyList<OpdScheduleSlot>>> GetSchedules(
        [FromQuery] string? opdDeptId = null,
        [FromQuery] int? scheduleDateInt = null)
    {
        await ReleaseExpiredAppointmentLocksAsync();
        var slots = await _slotRepo.QueryAsync(
            """
            SELECT * FROM opd_schedule_slots
            WHERE (@DeptId IS NULL OR opd_dept_id = @DeptId)
              AND (@DateInt IS NULL OR schedule_date_int = @DateInt)
            ORDER BY schedule_date_int ASC, starts_at_ms ASC
            """,
            new { DeptId = opdDeptId, DateInt = scheduleDateInt });
        return ApiResponse<IReadOnlyList<OpdScheduleSlot>>.Success(slots);
    }

    public sealed record LockSlotRequestDto(string PatientId, string ScheduleId, string? RequestKey = null);

    [HttpPost("appointments/lock")]
    public async Task<ApiResponse<OpdAppointment>> LockAppointmentSlot([FromBody] LockSlotRequestDto req)
    {
        await ReleaseExpiredAppointmentLocksAsync();

        var existingForPatient = await _appointRepo.QuerySingleOrDefaultAsync(
            "SELECT * FROM opd_appointments WHERE patient_id = @PatientId AND schedule_id = @ScheduleId AND status IN (0, 1, 2)",
            new { req.PatientId, req.ScheduleId });
        if (existingForPatient != null)
        {
            return ApiResponse<OpdAppointment>.Fail(409, $"该患者已预约本班次号源（排队号：{existingForPatient.QueueNo}），请勿重复挂号", existingForPatient);
        }

        var slot = await _slotRepo.GetByIdAsync(req.ScheduleId);
        if (slot == null || slot.SlotStatus == 3)
            return ApiResponse<OpdAppointment>.Fail(400, "号源不存在或已停诊");

        if (slot.ReservedCount >= slot.TotalCapacity || slot.SlotStatus == 2)
            return ApiResponse<OpdAppointment>.Fail(400, "该时段号源已满");

        var appoint = await _connFactory.ExecuteInTransactionAsync(async ct =>
        {
            var token = Guid.NewGuid().ToString("N");
            slot.ReservedCount++;
            if (slot.ReservedCount >= slot.TotalCapacity) slot.SlotStatus = 2; // Full
            await _slotRepo.UpdateAsync(slot, ct);

            var apt = new OpdAppointment
            {
                AppointmentNo = "APT" + DateTime.UtcNow.ToString("yyMMddHHmmssfff"),
                RequestKey = req.RequestKey,
                PatientId = req.PatientId,
                ScheduleId = req.ScheduleId,
                ActiveUniqueToken = token,
                QueueNo = slot.ReservedCount,
                Status = 0, // Reserved (locked)
                LockExpiresAtMs = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds()
            };
            await _appointRepo.InsertAsync(apt, ct);
            return apt;
        });

        return ApiResponse<OpdAppointment>.Success(appoint, "锁号成功，请在10分钟内完成缴费确认");
    }

    [HttpPost("appointments/{appointId}/cancel")]
    public async Task<ApiResponse<OpdAppointment>> CancelAppointment(string appointId, [FromQuery] string? reason = null)
    {
        var apt = await _appointRepo.GetByIdAsync(appointId);
        if (apt == null) return ApiResponse<OpdAppointment>.Fail(404, "预约记录不存在");
        if (apt.Status == 3 || apt.Status == 4)
            return ApiResponse<OpdAppointment>.Success(apt, "该预约已取消或已释放");

        if (!string.IsNullOrWhiteSpace(apt.VisitId))
        {
            var visit = await _visitRepo.GetByIdAsync(apt.VisitId);
            if (visit is { Status: >= 2 and not 4 })
            {
                return ApiResponse<OpdAppointment>.Fail(400, "患者已进入医生接诊或已完成诊疗，禁止直接退号！");
            }
        }

        await _connFactory.ExecuteInTransactionAsync(async ct =>
        {
            apt.Status = 3; // Cancelled
            apt.ActiveUniqueToken = null;
            await _appointRepo.UpdateAsync(apt, ct);

            var slot = await _slotRepo.GetByIdAsync(apt.ScheduleId, ct);
            if (slot != null && slot.ReservedCount > 0)
            {
                slot.ReservedCount = Math.Max(0, slot.ReservedCount - 1);
                if (slot.SlotStatus == 2 && slot.ReservedCount < slot.TotalCapacity)
                    slot.SlotStatus = 1; // Available again
                await _slotRepo.UpdateAsync(slot, ct);
            }

            if (!string.IsNullOrWhiteSpace(apt.VisitId))
            {
                var visit = await _visitRepo.GetByIdAsync(apt.VisitId, ct);
                if (visit != null)
                {
                    visit.Status = 4; // Cancelled
                    await _visitRepo.UpdateAsync(visit, ct);
                }

                await _queueRepo.ExecuteAsync(
                    "UPDATE opd_triage_queues SET queue_status = 4, updated_at_ms = @Now WHERE visit_id = @VisitId",
                    new { Now = SystemTime.UtcNowMs, VisitId = apt.VisitId }, ct);
            }
        });

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync(
                "OPD_CANCEL_APPOINTMENT",
                nameof(OpdAppointment),
                apt.Id,
                $"预约单 [{apt.AppointmentNo}] 退号释放号源，原因：{reason ?? "患者主动退号"}");
        }

        return ApiResponse<OpdAppointment>.Success(apt, "退号成功，号源容量已自动归还号池");
    }

    [HttpPost("appointments/{appointId}/confirm-and-triage")]
    public async Task<ApiResponse<OpdVisit>> ConfirmAndTriage(string appointId)
    {
        var apt = await _appointRepo.GetByIdAsync(appointId);
        if (apt == null) return ApiResponse<OpdVisit>.Fail(404, "预约不存在");
        if (apt.Status == 3 || apt.Status == 4)
            return ApiResponse<OpdVisit>.Fail(400, "该预约已取消或已超时过期，请重新挂号");

        if (!string.IsNullOrWhiteSpace(apt.VisitId))
        {
            var existingVisit = await _visitRepo.GetByIdAsync(apt.VisitId);
            if (existingVisit != null)
                return ApiResponse<OpdVisit>.Success(existingVisit, "该预约已签到入候诊池");
        }

        var slot = await _slotRepo.GetByIdAsync(apt.ScheduleId);

        var visit = await _connFactory.ExecuteInTransactionAsync(async ct =>
        {
            var v = new OpdVisit
            {
                VisitNo = "VIS" + DateTime.UtcNow.ToString("yyMMddHHmmssfff"),
                CampusId = slot?.CampusId ?? "CAMPUS-01",
                OpdDeptId = slot?.OpdDeptId ?? "DEPT-OPD-01",
                ClinicRoomId = slot?.ClinicRoomId,
                PatientId = apt.PatientId,
                ScheduleId = apt.ScheduleId,
                DoctorId = slot?.DoctorId ?? "DOC-01",
                Status = 1 // Waiting
            };
            await _visitRepo.InsertAsync(v, ct);

            apt.Status = 2; // CheckedIn
            apt.VisitId = v.Id;
            await _appointRepo.UpdateAsync(apt, ct);

            // Enter triage queue
            var queue = new OpdTriageQueue
            {
                VisitId = v.Id,
                OpdDeptId = v.OpdDeptId,
                ClinicRoomId = v.ClinicRoomId,
                PriorityScore = Math.Max(10, 200 - apt.QueueNo),
                QueueStatus = 1 // Waiting
            };
            await _queueRepo.InsertAsync(queue, ct);

            return v;
        });

        return ApiResponse<OpdVisit>.Success(visit, "报到签到入候诊池成功");
    }

    public sealed record OpdQueuePatientDto(
        string QueueId,
        string VisitId,
        string VisitNo,
        string PatientId,
        string MedicalRecordNo,
        string PatientName,
        string GenderCode,
        int AgeYears,
        string OpdDeptId,
        string OpdDeptName,
        string DoctorId,
        string DoctorName,
        string? ClinicRoomId,
        string QueueNo,
        int QueueStatus,
        int VisitStatus,
        long CheckinAtMs,
        long? CalledAtMs,
        IReadOnlyList<string> AllergyTags);

    private sealed class OpdQueuePatientRow
    {
        public string QueueId { get; set; } = string.Empty;
        public string VisitId { get; set; } = string.Empty;
        public string VisitNo { get; set; } = string.Empty;
        public string PatientId { get; set; } = string.Empty;
        public string? MedicalRecordNo { get; set; }
        public string? PatientName { get; set; }
        public string? GenderCode { get; set; }
        public int? BirthDateInt { get; set; }
        public string OpdDeptId { get; set; } = string.Empty;
        public string? OpdDeptName { get; set; }
        public string DoctorId { get; set; } = string.Empty;
        public string? DoctorName { get; set; }
        public string? ClinicRoomId { get; set; }
        public int QueueStatus { get; set; }
        public int VisitStatus { get; set; }
        public long CheckinAtMs { get; set; }
        public long? CalledAtMs { get; set; }
    }

    [HttpGet("queue")]
    public async Task<ApiResponse<IReadOnlyList<OpdQueuePatientDto>>> GetTriageQueue(
        [FromQuery] string? opdDeptId = null,
        [FromQuery] int? queueDateInt = null)
    {
        if (_userSession == null || string.IsNullOrWhiteSpace(_userSession.UserId))
            return ApiResponse<IReadOnlyList<OpdQueuePatientDto>>.Fail(401, "登录会话已失效");
        if (!IsOpdClinicalDoctor()
            && !HasAnyRole("OPD_NURSE", "NURSE", "NURSING")
            && !IsAdmin())
            return ApiResponse<IReadOnlyList<OpdQueuePatientDto>>.Fail(403, "当前角色无权查看门诊候诊队列");

        // 普通医生只能看到自己的队列；护士和系统管理员可查看当前科室全部患者。
        var requestedDeptId = string.IsNullOrWhiteSpace(opdDeptId)
            ? (HasAnyRole("OPD_NURSE", "NURSE") || IsAdmin() ? _userSession.ActiveDeptId : null)
            : opdDeptId.Trim();
        var dateInt = queueDateInt ?? DateTime.Today.Year * 10000 + DateTime.Today.Month * 100 + DateTime.Today.Day;
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<OpdQueuePatientRow>(
            """
            SELECT q.id AS QueueId, q.visit_id AS VisitId, v.visit_no AS VisitNo,
                   v.patient_id AS PatientId, p.medical_record_no AS MedicalRecordNo,
                   p.full_name AS PatientName, p.gender_code AS GenderCode,
                   p.birth_date_int AS BirthDateInt, q.opd_dept_id AS OpdDeptId,
                   d.dept_name AS OpdDeptName, v.doctor_id AS DoctorId,
                   du.display_name AS DoctorName, q.clinic_room_id AS ClinicRoomId,
                   q.queue_status AS QueueStatus, v.status AS VisitStatus,
                   q.checkin_at_ms AS CheckinAtMs, q.called_at_ms AS CalledAtMs
            FROM opd_triage_queues q
            INNER JOIN opd_visits v ON v.id = q.visit_id
            INNER JOIN pat_patients p ON p.id = v.patient_id
            LEFT JOIN sys_departments d ON d.id = q.opd_dept_id
            LEFT JOIN sys_users du ON du.id = v.doctor_id
            WHERE q.queue_status IN (1, 2, 3, 4)
              AND v.status IN (1, 2, 3)
              AND (@DeptId IS NULL OR q.opd_dept_id = @DeptId)
              AND (@OnlyDoctor = 0 OR v.doctor_id = @UserId)
              AND (@DateInt IS NULL OR q.checkin_at_ms >= @StartMs AND q.checkin_at_ms < @EndMs)
            ORDER BY CASE q.queue_status WHEN 2 THEN 0 WHEN 1 THEN 1 WHEN 3 THEN 2 ELSE 3 END,
                     q.priority_score DESC, q.checkin_at_ms ASC
            """,
            new
            {
                DeptId = requestedDeptId,
                OnlyDoctor = IsAdmin() || HasAnyRole("OPD_NURSE", "NURSE") ? 0 : 1,
                UserId = _userSession.UserId,
                DateInt = dateInt,
                StartMs = new DateTimeOffset(dateInt / 10000, dateInt / 100 % 100, dateInt % 100, 0, 0, 0, TimeSpan.FromHours(8)).ToUnixTimeMilliseconds(),
                EndMs = new DateTimeOffset(dateInt / 10000, dateInt / 100 % 100, dateInt % 100, 0, 0, 0, TimeSpan.FromHours(8)).AddDays(1).ToUnixTimeMilliseconds()
            })).ToList();

        var today = DateTime.Today;
        var result = new List<OpdQueuePatientDto>();
        foreach (var row in rows)
        {
            var allergies = await _patientContract?.GetPatientSummaryAsync(row.PatientId) is { } patient
                ? new List<string>(patient.AllergyTags)
                : new List<string>();
            var age = row.BirthDateInt is int birth && birth > 0
                ? Math.Max(0, today.Year - (birth / 10000) - (today.Month * 100 + today.Day < birth % 10000 ? 1 : 0))
                : 0;
            result.Add(new OpdQueuePatientDto(
                row.QueueId,
                row.VisitId,
                row.VisitNo,
                row.PatientId,
                row.MedicalRecordNo ?? string.Empty,
                row.PatientName ?? string.Empty,
                row.GenderCode ?? string.Empty,
                age,
                row.OpdDeptId,
                row.OpdDeptName ?? row.OpdDeptId,
                row.DoctorId,
                row.DoctorName ?? row.DoctorId,
                row.ClinicRoomId,
                $"{(row.CheckinAtMs == 0 ? 0 : (row.CheckinAtMs / 1000 % 86400) / 60):000}",
                row.QueueStatus,
                row.VisitStatus,
                row.CheckinAtMs,
                row.CalledAtMs,
                allergies));
        }
        return ApiResponse<IReadOnlyList<OpdQueuePatientDto>>.Success(result);
    }

    public sealed record StartConsultationRequestDto(string? QueueId = null, string? VisitId = null, string? ClinicRoomId = null);

    [HttpPost("queue/call")]
    public async Task<ApiResponse<OpdVisit>> StartConsultation([FromBody] StartConsultationRequestDto req)
    {
        if (!IsOpdClinicalDoctor() && !IsAdmin())
            return ApiResponse<OpdVisit>.Fail(403, "仅门诊医师可以呼叫并接诊患者");
        if (req == null || (string.IsNullOrWhiteSpace(req.QueueId) && string.IsNullOrWhiteSpace(req.VisitId)))
            return ApiResponse<OpdVisit>.Fail(400, "必须指定排队记录或门诊就诊记录");

        OpdTriageQueue? queue;
        if (!string.IsNullOrWhiteSpace(req.QueueId))
            queue = await _queueRepo.GetByIdAsync(req.QueueId);
        else
            queue = await _queueRepo.QuerySingleOrDefaultAsync(
                "SELECT * FROM opd_triage_queues WHERE visit_id = @VisitId AND queue_status IN (1, 2) ORDER BY checkin_at_ms DESC LIMIT 1",
                new { VisitId = req.VisitId });
        if (queue == null)
            return ApiResponse<OpdVisit>.Fail(404, "排队记录不存在或已结束");
        if (queue.QueueStatus is not (1 or 2))
            return ApiResponse<OpdVisit>.Fail(409, "该患者已完成或已退出候诊队列");

        var visit = await _visitRepo.GetByIdAsync(queue.VisitId);
        if (visit == null)
            return ApiResponse<OpdVisit>.Fail(404, "关联的门诊就诊记录不存在");
        if (visit.Status is not (1 or 2))
            return ApiResponse<OpdVisit>.Fail(409, "该门诊就诊已完成或已取消");
        if (!IsAdmin()
            && !string.Equals(visit.DoctorId, _userSession!.UserId, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(visit.OpdDeptId, _userSession.ActiveDeptId, StringComparison.OrdinalIgnoreCase))
            return ApiResponse<OpdVisit>.Fail(403, "只能接诊本人或本科室的门诊患者");

        await _connFactory.ExecuteInTransactionAsync(async ct =>
        {
            queue.QueueStatus = 2;
            queue.CalledAtMs = SystemTime.UtcNowMs;
            if (!string.IsNullOrWhiteSpace(req.ClinicRoomId))
                queue.ClinicRoomId = req.ClinicRoomId.Trim();
            queue.UpdatedBy = _userSession.UserId;
            await _queueRepo.UpdateAsync(queue, ct);

            visit.Status = 2;
            if (!string.IsNullOrWhiteSpace(req.ClinicRoomId))
                visit.ClinicRoomId = req.ClinicRoomId.Trim();
            if (string.IsNullOrWhiteSpace(visit.DoctorId))
                visit.DoctorId = _userSession.UserId;
            visit.UpdatedBy = _userSession.UserId;
            await _visitRepo.UpdateAsync(visit, ct);
            return 0;
        });
        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("OPD_START_CONSULTATION", nameof(OpdVisit), visit.Id,
                $"门诊医师 {_userSession.DisplayName} 呼叫接诊，排队号 {queue.Id}", _userSession.UserId, _userSession.DisplayName, _userSession.ActiveDeptId);
        return ApiResponse<OpdVisit>.Success(visit, $"已呼叫并接诊患者，就诊号 [{visit.VisitNo}]");
    }

    [HttpPost("queue/call-next")]
    public async Task<ApiResponse<OpdVisit>> CallNextPatient([FromQuery] string opdDeptId, [FromQuery] string? clinicRoomId = null)
    {
        if (!IsOpdClinicalDoctor() && !IsAdmin())
            return ApiResponse<OpdVisit>.Fail(403, "仅门诊医师可以呼叫患者");
        if (_userSession == null || string.IsNullOrWhiteSpace(_userSession.UserId))
            return ApiResponse<OpdVisit>.Fail(401, "登录会话已失效");
        if (string.IsNullOrWhiteSpace(opdDeptId))
            return ApiResponse<OpdVisit>.Fail(400, "必须指定门诊科室");
        if (!IsAdmin() && !string.Equals(opdDeptId, _userSession?.ActiveDeptId, StringComparison.OrdinalIgnoreCase))
            return ApiResponse<OpdVisit>.Fail(403, "只能在当前登录科室呼叫患者");
        var nextQueue = await _queueRepo.QuerySingleOrDefaultAsync(
            """
            SELECT q.* FROM opd_triage_queues q
            INNER JOIN opd_visits v ON v.id = q.visit_id
            WHERE q.opd_dept_id = @DeptId
              AND q.queue_status = 1
              AND v.status IN (1, 2)
              AND (@OnlyDoctor = 0 OR v.doctor_id = @DoctorId)
            ORDER BY q.priority_score DESC, q.checkin_at_ms ASC
            LIMIT 1
            """,
            new { DeptId = opdDeptId, OnlyDoctor = IsAdmin() ? 0 : 1, DoctorId = _userSession?.UserId });

        if (nextQueue == null)
            return ApiResponse<OpdVisit>.Fail(404, "当前科室候诊池暂无待呼叫患者");

        var visit = await _visitRepo.GetByIdAsync(nextQueue.VisitId);
        if (visit == null)
            return ApiResponse<OpdVisit>.Fail(404, "关联就诊记录不存在");
        if (visit.Status is not (1 or 2))
            return ApiResponse<OpdVisit>.Fail(409, "该门诊就诊已完成或已取消，不能呼叫");
        if (!CanAccessVisit(visit))
            return ApiResponse<OpdVisit>.Fail(403, "只能呼叫本人或本科室的门诊患者");

        await _connFactory.ExecuteInTransactionAsync(async ct =>
        {
            nextQueue.QueueStatus = 2; // Called
            nextQueue.CalledAtMs = SystemTime.UtcNowMs;
            if (!string.IsNullOrWhiteSpace(clinicRoomId))
                nextQueue.ClinicRoomId = clinicRoomId.Trim();
            nextQueue.UpdatedBy = _userSession.UserId;
            await _queueRepo.UpdateAsync(nextQueue, ct);

            visit.Status = 2; // InConsultation
            if (!string.IsNullOrWhiteSpace(clinicRoomId))
                visit.ClinicRoomId = clinicRoomId.Trim();
            if (string.IsNullOrWhiteSpace(visit.DoctorId))
                visit.DoctorId = _userSession.UserId;
            visit.UpdatedBy = _userSession.UserId;
            await _visitRepo.UpdateAsync(visit, ct);
        });

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync(
                "OPD_CALL_NEXT",
                nameof(OpdVisit),
                visit.Id,
                $"按序呼叫患者，排队记录 {nextQueue.Id}，诊室 {nextQueue.ClinicRoomId ?? "未指定"}",
                _userSession.UserId,
                _userSession.DisplayName,
                _userSession.ActiveDeptId);
        return ApiResponse<OpdVisit>.Success(visit, $"已顺呼就诊号 [{visit.VisitNo}] 进入诊室接诊");
    }

    public sealed record CompleteVisitRequestDto(
        string PrimaryDiagCode,
        string PrimaryDiagName,
        string? EmrDocumentId = null,
        string? OfdArchiveId = null
    );

    public sealed record SaveVisitDiagnosisRequestDto(
        string PrimaryDiagCode,
        string PrimaryDiagName
    );

    [HttpPut("visits/{visitId}/diagnosis")]
    public async Task<ApiResponse<OpdVisit>> SaveVisitDiagnosis(
        string visitId,
        [FromBody] SaveVisitDiagnosisRequestDto req)
    {
        if (!IsOpdClinicalDoctor() && !IsAdmin())
            return ApiResponse<OpdVisit>.Fail(403, "仅门诊医师可以录入诊断");
        var visit = await FindVisitAsync(visitId);
        if (visit == null)
            return ApiResponse<OpdVisit>.Fail(404, "门诊就诊记录不存在");
        if (visit.Status != 2)
            return ApiResponse<OpdVisit>.Fail(409, visit.Status == 3
                ? "该门诊就诊已完成，诊断已锁定"
                : "请先接诊后再录入诊断");
        if (!CanAccessVisit(visit))
            return ApiResponse<OpdVisit>.Fail(403, "只能录入本人或本科室接诊患者的诊断");

        var (diagnosisName, diagnosisError) = await ResolveDiagnosisAsync(
            visit, req?.PrimaryDiagCode, req?.PrimaryDiagName);
        if (diagnosisError != null)
            return ApiResponse<OpdVisit>.Fail(400, diagnosisError);

        visit.PrimaryDiagCode = req!.PrimaryDiagCode?.Trim();
        visit.PrimaryDiagName = diagnosisName;
        visit.UpdatedBy = _userSession!.UserId;
        await _visitRepo.UpdateAsync(visit);

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync(
                "OPD_DIAGNOSIS_SAVE",
                nameof(OpdVisit),
                visit.Id,
                $"保存主诊断 [{visit.PrimaryDiagCode ?? "-"}] {visit.PrimaryDiagName}",
                _userSession.UserId,
                _userSession.DisplayName,
                _userSession.ActiveDeptId);
        return ApiResponse<OpdVisit>.Success(visit, "主诊断已保存");
    }

    [HttpPost("visits/{visitId}/complete")]
    public async Task<ApiResponse<OpdVisit>> CompleteVisit(string visitId, [FromBody] CompleteVisitRequestDto req)
    {
        if (!IsOpdClinicalDoctor() && !IsAdmin())
            return ApiResponse<OpdVisit>.Fail(403, "仅门诊医师可以完成门诊诊疗");
        var visit = await FindVisitAsync(visitId);
        if (visit == null)
            return ApiResponse<OpdVisit>.Fail(404, "门诊就诊记录不存在");
        if (visit.Status != 2)
            return ApiResponse<OpdVisit>.Fail(409, visit.Status == 3 ? "该门诊就诊已完成" : "请先开始接诊后再完成门诊诊疗");
        if (!CanAccessVisit(visit))
            return ApiResponse<OpdVisit>.Fail(403, "只能完成本人或本科室的接诊");
        if (string.IsNullOrWhiteSpace(req?.PrimaryDiagName) && string.IsNullOrWhiteSpace(req?.PrimaryDiagCode))
            return ApiResponse<OpdVisit>.Fail(400, "完成门诊接诊必须填写主诊断");
        var incompletePrescription = await _rxRepo.QuerySingleOrDefaultAsync(
            "SELECT * FROM opd_prescriptions WHERE visit_id = @VisitId AND status IN (0, 1, 2) LIMIT 1",
            new { VisitId = visit.Id });
        if (incompletePrescription != null)
            return ApiResponse<OpdVisit>.Fail(409, "该就诊仍有草稿或已驳回处方，不能完成诊疗");

        var (diagName, diagnosisError) = await ResolveDiagnosisAsync(
            visit, req.PrimaryDiagCode, req.PrimaryDiagName);
        if (diagnosisError != null)
            return ApiResponse<OpdVisit>.Fail(400, diagnosisError);
        if (string.IsNullOrWhiteSpace(diagName))
            return ApiResponse<OpdVisit>.Fail(400, "完成门诊接诊必须填写主诊断");

        await _connFactory.ExecuteInTransactionAsync(async ct =>
        {
            visit.PrimaryDiagCode = req.PrimaryDiagCode?.Trim();
            visit.PrimaryDiagName = diagName;
            visit.EmrDocumentId = req.EmrDocumentId ?? visit.EmrDocumentId;
            visit.OfdArchiveId = req.OfdArchiveId ?? visit.OfdArchiveId;
            visit.Status = 3; // Completed
            visit.SignedAtMs = SystemTime.UtcNowMs;
            visit.UpdatedBy = _userSession!.UserId;
            await _visitRepo.UpdateAsync(visit, ct);

            await _queueRepo.ExecuteAsync(
                "UPDATE opd_triage_queues SET queue_status = 4, updated_by = @UpdatedBy, updated_at_ms = @Now, version_no = version_no + 1 WHERE visit_id = @VisitId",
                new { Now = SystemTime.UtcNowMs, VisitId = visit.Id, UpdatedBy = _userSession.UserId }, ct);
        });

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync(
                "OPD_VISIT_COMPLETE",
                nameof(OpdVisit),
                visit.Id,
                $"完成门诊诊疗，主诊断 [{visit.PrimaryDiagCode ?? "-"}] {visit.PrimaryDiagName}",
                _userSession.UserId,
                _userSession.DisplayName,
                _userSession.ActiveDeptId);
        return ApiResponse<OpdVisit>.Success(visit, "门诊诊疗已完成闭环");
    }

    private async Task<OpdVisit?> FindVisitAsync(string visitId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(visitId))
            return null;
        return await _visitRepo.GetByIdAsync(visitId, ct)
            ?? await _visitRepo.QuerySingleOrDefaultAsync(
                "SELECT * FROM opd_visits WHERE visit_no = @VisitNo",
                new { VisitNo = visitId }, ct);
    }

    private async Task ReleaseExpiredAppointmentLocksAsync()
    {
        var nowMs = SystemTime.UtcNowMs;
        var expiredList = await _appointRepo.QueryAsync(
            "SELECT * FROM opd_appointments WHERE status = 0 AND lock_expires_at_ms < @Now",
            new { Now = nowMs });

        if (expiredList.Count == 0) return;

        await _connFactory.ExecuteInTransactionAsync(async ct =>
        {
            foreach (var apt in expiredList)
            {
                apt.Status = 4; // Expired
                apt.ActiveUniqueToken = null;
                await _appointRepo.UpdateAsync(apt, ct);

                var slot = await _slotRepo.GetByIdAsync(apt.ScheduleId, ct);
                if (slot != null && slot.ReservedCount > 0)
                {
                    slot.ReservedCount = Math.Max(0, slot.ReservedCount - 1);
                    if (slot.SlotStatus == 2 && slot.ReservedCount < slot.TotalCapacity)
                        slot.SlotStatus = 1;
                    await _slotRepo.UpdateAsync(slot, ct);
                }
            }
        });
    }

    public sealed record CreatePrescriptionRequestDto(
        string VisitId,
        string PatientId,
        string OpdDeptId,
        string DoctorId,
        string TargetPharmacyId,
        List<OpdPrescriptionItem> Items,
        int HerbDoses = 1,
        string? AgentIdCard = null,
        string RxType = "WESTERN"
    );

    [HttpPost("prescriptions")]
    public async Task<ApiResponse<OpdPrescription>> CreatePrescription([FromBody] CreatePrescriptionRequestDto req)
    {
        if (!CanPrescribe())
            return ApiResponse<OpdPrescription>.Fail(403, "仅门诊医师可以开立处方");
        if (req == null || string.IsNullOrWhiteSpace(req.VisitId) ||
            string.IsNullOrWhiteSpace(req.PatientId) || req.Items == null || req.Items.Count == 0)
            return ApiResponse<OpdPrescription>.Fail(400, "就诊、患者及处方明细不能为空");

        var visit = await _visitRepo.GetByIdAsync(req.VisitId);
        if (visit == null) return ApiResponse<OpdPrescription>.Fail(404, "门诊就诊记录不存在");
        var visitError = await ValidateVisitForPrescriptionAsync(req.VisitId, req.PatientId);
        if (visitError != null) return ApiResponse<OpdPrescription>.Fail(409, visitError);

        var doctorId = !string.IsNullOrWhiteSpace(req.DoctorId) && IsAdmin() ? req.DoctorId.Trim() : _userSession!.UserId;
        if (!IsAdmin() && !string.Equals(visit.DoctorId, doctorId, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(visit.OpdDeptId, _userSession.ActiveDeptId, StringComparison.OrdinalIgnoreCase))
            return ApiResponse<OpdPrescription>.Fail(403, "只能为本人接诊或本科室患者的就诊开立处方");

        var targetPharmacyId = !string.IsNullOrWhiteSpace(req.TargetPharmacyId) ? req.TargetPharmacyId.Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(targetPharmacyId) && _departmentContract != null)
        {
            var department = await _departmentContract.GetDeptSummaryAsync(visit.OpdDeptId);
            if (!string.IsNullOrWhiteSpace(department?.DefaultPharmacyId))
                targetPharmacyId = department.DefaultPharmacyId;
        }
        if (string.IsNullOrWhiteSpace(targetPharmacyId))
        {
            targetPharmacyId = "DEPT-PHM-01";
        }

        var activeInput = req.Items.Where(x => x.IsDeleted == 0).ToList();
        if (activeInput.Count == 0)
            return ApiResponse<OpdPrescription>.Fail(400, "处方至少需要一条有效药品明细");

        var resolved = new List<(OpdPrescriptionItem Item, DrugCatalogSnapshotDto Drug)>();
        foreach (var item in activeInput)
        {
            var inputError = ValidateMedicationInput(item);
            if (inputError != null) return ApiResponse<OpdPrescription>.Fail(400, inputError);
            var (drug, error) = await ResolveDrugAsync(item.DrugId);
            if (error != null || drug == null)
                return ApiResponse<OpdPrescription>.Fail(400, error ?? "药品不存在");
            resolved.Add((item, drug));
        }
        var safetyError = await ValidatePrescriptionSafetyAsync(
            req.PatientId, resolved.Select(x => x.Drug), resolved.Select(x => x.Item), req.AgentIdCard);
        if (safetyError != null)
            return ApiResponse<OpdPrescription>.Fail(400, safetyError);

        var now = SystemTime.UtcNowMs;
        var existingNo = await _rxRepo.QuerySingleOrDefaultAsync(
            "SELECT * FROM opd_prescriptions WHERE visit_id = @VisitId AND status IN (0, 2) ORDER BY created_at_ms DESC LIMIT 1",
            new { VisitId = req.VisitId });
        if (existingNo != null)
            return ApiResponse<OpdPrescription>.Fail(409, "该就诊已有草稿或已驳回处方，请修订原处方后重新提交", existingNo);

        int herbDoses = Math.Max(1, req.HerbDoses);
        var rx = await _connFactory.ExecuteInTransactionAsync(async ct =>
        {
            var prescription = new OpdPrescription
            {
                PrescriptionNo = "RX" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + Random.Shared.Next(1000, 9999),
                VisitId = req.VisitId,
                PatientId = req.PatientId,
                OpdDeptId = visit.OpdDeptId,
                DoctorId = doctorId,
                TargetPharmacyId = targetPharmacyId,
                RxType = req.RxType ?? "WESTERN",
                HerbDoses = herbDoses,
                TotalAmount = resolved.Sum(x => x.Drug.UnitPrice * x.Item.TotalQuantity) * herbDoses,
                Status = 0,
                CreatedBy = doctorId,
                UpdatedBy = doctorId
            };
            await _rxRepo.InsertAsync(prescription, ct);

            foreach (var (item, drug) in resolved)
            {
                if (string.IsNullOrWhiteSpace(item.Id) || await _rxItemRepo.GetByIdAsync(item.Id, ct) != null)
                    item.Id = Guid.NewGuid().ToString("D");
                item.PrescriptionId = prescription.Id;
                item.IsDeleted = 0;
                item.DeletedAtMs = null;
                item.DeletedBy = null;
                ApplyAuthoritativeDrug(item, drug);
                item.CreatedBy = doctorId;
                item.UpdatedBy = doctorId;
                await _rxItemRepo.InsertAsync(item, ct);

                if (drug.IsSkinTestRequired)
                {
                    using DbConnection conn = _connFactory.CreateConnection();
                    string orderId = Guid.NewGuid().ToString("D");
                    string orderNo = $"ORD-SKIN-{Guid.NewGuid():N}"[..24].ToUpperInvariant();
                    await conn.ExecuteAsync(new CommandDefinition(@"
INSERT INTO ord_medical_orders
    (id, order_no, encounter_id, patient_id, order_ipd_dept_id, patient_ward_id, exec_target_dept_id,
     order_term_type, group_no, order_category, item_code, item_name,
     dosage, dose_unit, frequency_code, route_code,
     status, order_doctor_id, order_at_ms, total_quantity, unit_price,
     skin_test_flag, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    (@Id, @OrderNo, @VisitId, @PatientId, 'DEPT-OPD-01', 'WARD-OPD', 'DEPT-NURSE-OPD',
     2, 1, 'TREATMENT', @DrugId, @Title,
     1, '次', 'ST', 'ID',
     0, @DoctorId, @Now, 1, 5.0,
     1, @Now, @DoctorId, @Now, @DoctorId, 1)",
                        new
                        {
                            Id = orderId,
                            OrderNo = orderNo,
                            PatientId = req.PatientId,
                            VisitId = req.VisitId,
                            Title = $"[皮试医嘱] {drug.DrugName} 皮试试验",
                            DrugId = drug.DrugId,
                            Now = now,
                            DoctorId = doctorId
                        }, _connFactory.CurrentTransaction, cancellationToken: ct));
                }
            }

            foreach (var deletedItem in req.Items.Where(x => x.IsDeleted != 0))
            {
                if (string.IsNullOrWhiteSpace(deletedItem.Id))
                    deletedItem.Id = Guid.NewGuid().ToString("D");
                deletedItem.PrescriptionId = prescription.Id;
                deletedItem.IsDeleted = 1;
                deletedItem.DeletedAtMs ??= now;
                deletedItem.DeletedBy = string.IsNullOrWhiteSpace(deletedItem.DeletedBy) ? doctorId : deletedItem.DeletedBy;
                deletedItem.CreatedBy = doctorId;
                deletedItem.UpdatedBy = doctorId;
                await _rxItemRepo.InsertAsync(deletedItem, ct);
            }

            return prescription;
        });

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("OPD_PRESCRIPTION_CREATE", nameof(OpdPrescription), rx.Id,
                $"处方 {rx.PrescriptionNo} 已开立，药房 {rx.TargetPharmacyId}，金额 ¥{rx.TotalAmount:0.00}",
                doctorId, null, visit.OpdDeptId);
        return ApiResponse<OpdPrescription>.Success(rx, "处方已保存为待审草稿，需经药师审方后方可收费");
    }

    [HttpPost("prescriptions/{rxId}/submit")]
    public async Task<ApiResponse<OpdPrescription>> SubmitPrescription(string rxId)
    {
        if (!CanPrescribe())
            return ApiResponse<OpdPrescription>.Fail(403, "仅门诊医师可以提交处方");
        var rx = await _rxRepo.GetByIdAsync(rxId);
        if (rx == null)
            return ApiResponse<OpdPrescription>.Fail(404, "处方不存在");
        if (!IsAdmin() && !string.Equals(rx.DoctorId, _userSession!.UserId, StringComparison.OrdinalIgnoreCase))
            return ApiResponse<OpdPrescription>.Fail(403, "只能提交本人开具的处方");
        if (rx.Status is not (0 or 2))
            return ApiResponse<OpdPrescription>.Fail(409, "仅草稿或已驳回处方可以提交审方");

        var visit = await FindVisitAsync(rx.VisitId);
        if (visit == null)
            return ApiResponse<OpdPrescription>.Fail(404, "处方关联的门诊就诊记录不存在");
        if (visit.Status != 2)
            return ApiResponse<OpdPrescription>.Fail(409, "仅就诊中的患者可以提交处方审方");
        if (!CanAccessVisit(visit))
            return ApiResponse<OpdPrescription>.Fail(403, "只能提交本人或本科室患者的处方");
        if (!string.Equals(visit.PatientId, rx.PatientId, StringComparison.OrdinalIgnoreCase))
            return ApiResponse<OpdPrescription>.Fail(409, "处方患者与就诊记录不一致");

        var activeItems = (await _rxItemRepo.QueryAsync(
            "SELECT * FROM opd_prescription_items WHERE prescription_id = @RxId AND is_deleted = 0 ORDER BY group_no, created_at_ms",
            new { RxId = rxId })).ToList();
        if (activeItems.Count == 0)
            return ApiResponse<OpdPrescription>.Fail(409, "处方没有有效药品明细，不能提交审方");

        var drugs = new List<DrugCatalogSnapshotDto>();
        foreach (var item in activeItems)
        {
            var inputError = ValidateMedicationInput(item);
            if (inputError != null)
                return ApiResponse<OpdPrescription>.Fail(409, $"{item.DrugNameSnapshot}：{inputError}");
            var (drug, drugError) = await ResolveDrugAsync(item.DrugId);
            if (drug == null || drugError != null)
                return ApiResponse<OpdPrescription>.Fail(409, drugError ?? $"药品 [{item.DrugId}] 已失效");
            if (Math.Abs(item.UnitPrice - drug.UnitPrice) > 0.01m)
                return ApiResponse<OpdPrescription>.Fail(409,
                    $"药品 [{drug.DrugName}] 价格已调整，请刷新处方明细后重新提交");
            if (!string.Equals(item.DrugNameSnapshot, drug.DrugName, StringComparison.Ordinal))
                item.DrugNameSnapshot = drug.DrugName;
            if (item.SkinTestFlag == 4)
                return ApiResponse<OpdPrescription>.Fail(409,
                    $"药品 [{drug.DrugName}] 皮试阳性，禁止再次提交审方");
            drugs.Add(drug);
        }

        var safetyError = await ValidatePrescriptionSafetyAsync(rx.PatientId, drugs);
        if (safetyError != null)
            return ApiResponse<OpdPrescription>.Fail(409, safetyError);

        rx.Status = 6;
        rx.ReviewRemark = null;
        rx.UpdatedBy = _userSession!.UserId;
        await _rxRepo.UpdateAsync(rx);

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync(
                "OPD_PRESCRIPTION_SUBMIT",
                nameof(OpdPrescription),
                rx.Id,
                $"医生提交处方 {rx.PrescriptionNo} 待药师审方，金额 ¥{rx.TotalAmount:0.00}",
                _userSession.UserId,
                _userSession.DisplayName,
                rx.OpdDeptId);
        return ApiResponse<OpdPrescription>.Success(rx, "处方已提交，等待药师审方");
    }

    [HttpGet("prescriptions/{rxId}/items")]
    public async Task<ApiResponse<List<OpdPrescriptionItem>>> GetPrescriptionItems(string rxId, [FromQuery] bool includeDeleted = false)
    {
        var sql = includeDeleted
            ? "SELECT * FROM opd_prescription_items WHERE prescription_id = @RxId ORDER BY group_no, created_at_ms"
            : "SELECT * FROM opd_prescription_items WHERE prescription_id = @RxId AND is_deleted = 0 ORDER BY group_no, created_at_ms";
        var items = await _rxItemRepo.QueryAsync(sql, new { RxId = rxId });
        return ApiResponse<List<OpdPrescriptionItem>>.Success(items.ToList());
    }

    [HttpPost("prescriptions/{rxId}/items")]
    public async Task<ApiResponse<OpdPrescriptionItem>> AddPrescriptionItem(string rxId, [FromBody] OpdPrescriptionItem item)
    {
        if (!CanPrescribe()) return ApiResponse<OpdPrescriptionItem>.Fail(403, "仅门诊医师可以修改处方");
        if (item == null) return ApiResponse<OpdPrescriptionItem>.Fail(400, "处方明细不能为空");
        var rx = await _rxRepo.GetByIdAsync(rxId);
        if (rx == null) return ApiResponse<OpdPrescriptionItem>.Fail(404, "处方不存在");
        if (!CanEditPrescription(rx.Status))
            return ApiResponse<OpdPrescriptionItem>.Fail(409, "处方已提交审方、已收费或已发药，禁止新增明细");
        var inputError = ValidateMedicationInput(item);
        if (inputError != null) return ApiResponse<OpdPrescriptionItem>.Fail(400, inputError);
        var (drug, drugError) = await ResolveDrugAsync(item.DrugId);
        if (drug == null || drugError != null)
            return ApiResponse<OpdPrescriptionItem>.Fail(400, drugError ?? "药品不存在");
        var safetyError = await ValidatePrescriptionSafetyAsync(rx.PatientId, new[] { drug });
        if (safetyError != null) return ApiResponse<OpdPrescriptionItem>.Fail(400, safetyError);

        await _connFactory.ExecuteInTransactionAsync(async ct =>
        {
            if (string.IsNullOrWhiteSpace(item.Id) || await _rxItemRepo.GetByIdAsync(item.Id, ct) != null)
                item.Id = Guid.NewGuid().ToString("D");
            item.PrescriptionId = rxId;
            item.IsDeleted = 0;
            item.DeletedAtMs = null;
            item.DeletedBy = null;
            ApplyAuthoritativeDrug(item, drug);
            item.CreatedBy = _userSession!.UserId;
            item.UpdatedBy = _userSession.UserId;
            await _rxItemRepo.InsertAsync(item, ct);
            await RecalculatePrescriptionTotalAsync(rxId, ct);
        });

        return ApiResponse<OpdPrescriptionItem>.Success(item, "处方明细已新增");
    }

    public sealed record PrescriptionAggregateDto(
        OpdPrescription Prescription,
        List<OpdPrescriptionItem> Items,
        List<PrescriptionCustomFieldDto> CustomFields
    );

    [HttpGet("prescriptions/by-visit/{visitId}")]
    public async Task<ApiResponse<PrescriptionAggregateDto>> GetPrescriptionByVisit(
        string visitId,
        [FromQuery] bool includeDeleted = true)
    {
        var rx = await _rxRepo.QuerySingleOrDefaultAsync(
            "SELECT * FROM opd_prescriptions WHERE visit_id = @VisitId ORDER BY created_at_ms DESC LIMIT 1",
            new { VisitId = visitId });
        if (rx == null)
            return ApiResponse<PrescriptionAggregateDto>.Fail(404, "该就诊暂无已保存处方");

        var sql = includeDeleted
            ? "SELECT * FROM opd_prescription_items WHERE prescription_id = @RxId ORDER BY group_no, created_at_ms"
            : "SELECT * FROM opd_prescription_items WHERE prescription_id = @RxId AND is_deleted = 0 ORDER BY group_no, created_at_ms";
        var items = await _rxItemRepo.QueryAsync(sql, new { RxId = rx.Id });

        var aggregate = new PrescriptionAggregateDto(rx, items.ToList(), ReadCustomFields(rx.ExtDataJson));
        return ApiResponse<PrescriptionAggregateDto>.Success(aggregate, "处方已加载");
    }

    public sealed record UpdatePrescriptionItemRequestDto(
        string? DrugNameSnapshot,
        decimal? DoseOnce,
        string? DoseUnit,
        string? FrequencyCode,
        string? RouteCode,
        int? Days,
        int? TotalQuantity,
        decimal? UnitPrice,
        string? OperatorId,
        string? CustomFieldsJson = null
    );

    [HttpPut("prescriptions/items/{itemId}")]
    public async Task<ApiResponse<OpdPrescriptionItem>> UpdatePrescriptionItem(string itemId, [FromBody] UpdatePrescriptionItemRequestDto req)
    {
        if (!CanPrescribe()) return ApiResponse<OpdPrescriptionItem>.Fail(403, "仅门诊医师可以修改处方");
        if (req == null) return ApiResponse<OpdPrescriptionItem>.Fail(400, "修改内容不能为空");
        var item = await _rxItemRepo.GetByIdAsync(itemId);
        if (item == null) return ApiResponse<OpdPrescriptionItem>.Fail(404, "处方药品明细不存在");
        if (item.IsDeleted != 0) return ApiResponse<OpdPrescriptionItem>.Fail(409, "该明细已软删除，不能继续修改");
        var rx = await _rxRepo.GetByIdAsync(item.PrescriptionId);
        if (rx == null) return ApiResponse<OpdPrescriptionItem>.Fail(404, "处方不存在");
        if (!CanEditPrescription(rx.Status))
            return ApiResponse<OpdPrescriptionItem>.Fail(409, "处方已提交审方、已收费或已发药，禁止修改明细");
        var (drug, drugError) = await ResolveDrugAsync(item.DrugId);
        if (drug == null || drugError != null)
            return ApiResponse<OpdPrescriptionItem>.Fail(400, drugError ?? "药品不存在");
        if ((req.TotalQuantity.HasValue && (req.TotalQuantity.Value <= 0 || req.TotalQuantity.Value > 1000)) ||
            (req.Days.HasValue && (req.Days.Value <= 0 || req.Days.Value > 365)) ||
            (req.DoseOnce.HasValue && (req.DoseOnce.Value < 0 || req.DoseOnce.Value > 100000)))
            return ApiResponse<OpdPrescriptionItem>.Fail(400, "数量、天数或单次剂量超出允许范围");

        await _connFactory.ExecuteInTransactionAsync(async ct =>
        {
            if (req.DoseOnce.HasValue) item.DoseOnce = req.DoseOnce.Value;
            if (!string.IsNullOrWhiteSpace(req.DoseUnit)) item.DoseUnit = req.DoseUnit.Trim();
            if (!string.IsNullOrWhiteSpace(req.FrequencyCode)) item.FrequencyCode = req.FrequencyCode.Trim();
            if (!string.IsNullOrWhiteSpace(req.RouteCode)) item.RouteCode = req.RouteCode.Trim();
            if (req.Days.HasValue) item.Days = req.Days.Value;
            if (req.TotalQuantity.HasValue) item.TotalQuantity = req.TotalQuantity.Value;
            if (req.CustomFieldsJson != null) item.ExtDataJson = req.CustomFieldsJson;
            ApplyAuthoritativeDrug(item, drug);
            item.UpdatedBy = _userSession!.UserId;
            await _rxItemRepo.UpdateAsync(item, ct);
            await RecalculatePrescriptionTotalAsync(item.PrescriptionId, ct);
        });

        return ApiResponse<OpdPrescriptionItem>.Success(item, "处方明细已保存");
    }

    public sealed record SoftDeleteRequestDto(string? OperatorId, string? Reason = null);

    [HttpPost("prescriptions/items/{itemId}/soft-delete")]
    public async Task<ApiResponse<OpdPrescriptionItem>> SoftDeletePrescriptionItem(string itemId, [FromBody] SoftDeleteRequestDto? req)
    {
        if (!CanPrescribe()) return ApiResponse<OpdPrescriptionItem>.Fail(403, "仅门诊医师可以删除处方明细");
        var item = await _rxItemRepo.GetByIdAsync(itemId);
        if (item == null) return ApiResponse<OpdPrescriptionItem>.Fail(404, "处方药品明细不存在");
        var rx = await _rxRepo.GetByIdAsync(item.PrescriptionId);
        if (rx == null) return ApiResponse<OpdPrescriptionItem>.Fail(404, "处方不存在");
        if (!CanEditPrescription(rx.Status))
            return ApiResponse<OpdPrescriptionItem>.Fail(409, "处方已提交审方、已收费或已发药，禁止删除明细");
        if (item.IsDeleted != 0)
            return ApiResponse<OpdPrescriptionItem>.Success(item, "明细已软删除，原始记录仍保留");
        using var conn = _connFactory.CreateConnection();
        var activeCount = await conn.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM opd_prescription_items WHERE prescription_id = @RxId AND is_deleted = 0",
            new { RxId = item.PrescriptionId });
        if (activeCount <= 1)
            return ApiResponse<OpdPrescriptionItem>.Fail(409, "处方至少保留一条有效药品明细，请直接作废处方");

        await _connFactory.ExecuteInTransactionAsync(async ct =>
        {
            item.IsDeleted = 1;
            item.DeletedAtMs = SystemTime.UtcNowMs;
            item.DeletedBy = !string.IsNullOrWhiteSpace(req?.OperatorId) ? req.OperatorId.Trim() : _userSession!.UserId;
            item.UpdatedBy = _userSession.UserId;
            if (!string.IsNullOrWhiteSpace(req?.Reason))
            {
                item.ExtDataJson = AppendDeleteReason(item.ExtDataJson, req.Reason);
            }
            await _rxItemRepo.UpdateAsync(item, ct);
            await RecalculatePrescriptionTotalAsync(item.PrescriptionId, ct);
        });

        return ApiResponse<OpdPrescriptionItem>.Success(item, "明细已软删除：界面不再展示，原始记录与审计信息仍保留");
    }

    private async Task RecalculatePrescriptionTotalAsync(string rxId, CancellationToken ct = default)
    {
        var rx = await _rxRepo.GetByIdAsync(rxId, ct);
        if (rx == null) return;
        var activeItems = await _rxItemRepo.QueryAsync(
            "SELECT * FROM opd_prescription_items WHERE prescription_id = @RxId AND is_deleted = 0",
            new { RxId = rxId }, ct);
        rx.TotalAmount = activeItems.Sum(x => x.UnitPrice * x.TotalQuantity);
        await _rxRepo.UpdateAsync(rx, ct);
    }

    public sealed record PrescriptionCustomFieldDto(
        string Id,
        string Name,
        string Value,
        string DataType = "STRING",
        string? Placeholder = null,
        bool IsDeleted = false,
        int SortOrder = 0,
        long? DeletedAtMs = null,
        string? DeletedBy = null
    );

    public sealed record SavePrescriptionExtFieldsRequestDto(
        List<PrescriptionCustomFieldDto> CustomFields,
        string? OperatorId = null
    );

    [HttpPut("prescriptions/{rxId}/ext-fields")]
    public async Task<ApiResponse<List<PrescriptionCustomFieldDto>>> SavePrescriptionExtFields(
        string rxId,
        [FromBody] SavePrescriptionExtFieldsRequestDto req)
    {
        if (!CanPrescribe())
            return ApiResponse<List<PrescriptionCustomFieldDto>>.Fail(403, "仅门诊医师可以修改处方");
        if (req?.CustomFields == null)
            return ApiResponse<List<PrescriptionCustomFieldDto>>.Fail(400, "扩展字段内容不能为空");
        var rx = await _rxRepo.GetByIdAsync(rxId);
        if (rx == null) return ApiResponse<List<PrescriptionCustomFieldDto>>.Fail(404, "处方不存在");
        if (!CanEditPrescription(rx.Status))
            return ApiResponse<List<PrescriptionCustomFieldDto>>.Fail(409, "处方已提交审方、已收费或已发药，禁止修改扩展字段");
        var operatorId = _userSession!.UserId;

        var existing = ReadCustomFields(rx.ExtDataJson);
        var byId = existing.ToDictionary(x => x.Id, x => x, StringComparer.OrdinalIgnoreCase);

        foreach (var incoming in req.CustomFields)
        {
            if (string.IsNullOrWhiteSpace(incoming.Id)) continue;
            var id = incoming.Id.Trim();
            if (byId.TryGetValue(id, out var current))
            {
                byId[id] = incoming with
                {
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(incoming.Name) ? current.Name : incoming.Name.Trim(),
                    Value = incoming.Value ?? current.Value,
                    DataType = string.IsNullOrWhiteSpace(incoming.DataType) ? current.DataType : incoming.DataType,
                    Placeholder = incoming.Placeholder ?? current.Placeholder,
                    IsDeleted = incoming.IsDeleted,
                    DeletedAtMs = incoming.IsDeleted ? incoming.DeletedAtMs ?? current.DeletedAtMs : null,
                    DeletedBy = incoming.IsDeleted ? incoming.DeletedBy ?? current.DeletedBy : null
                };
            }
            else
            {
                byId[id] = incoming with
                {
                    Id = id,
                    Name = incoming.Name?.Trim() ?? string.Empty,
                    IsDeleted = incoming.IsDeleted,
                    DeletedAtMs = incoming.IsDeleted ? incoming.DeletedAtMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : null,
                    DeletedBy = incoming.IsDeleted ? operatorId : null
                };
            }
        }

        // 服务端只合并，不删除历史节点；缺失的字段按软删除处理，数据仍留在 JSON 中。
        var incomingIds = req.CustomFields
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .Select(x => x.Id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in byId)
        {
            if (!incomingIds.Contains(pair.Key) && !pair.Value.IsDeleted)
                byId[pair.Key] = pair.Value with
                {
                    IsDeleted = true,
                    DeletedAtMs = pair.Value.DeletedAtMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    DeletedBy = pair.Value.DeletedBy ?? operatorId
                };
        }

        var merged = byId.Values
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        rx.ExtDataJson = JsonSerializer.Serialize(new { customFields = merged }, ExtJsonOptions);
        rx.UpdatedBy = operatorId;
        await _rxRepo.UpdateAsync(rx);

        return ApiResponse<List<PrescriptionCustomFieldDto>>.Success(merged, "处方扩展字段已保存");
    }

    private static List<PrescriptionCustomFieldDto> ReadCustomFields(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<PrescriptionCustomFieldDto>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("customFields", out var fields) ||
                fields.ValueKind != JsonValueKind.Array)
            {
                return new List<PrescriptionCustomFieldDto>();
            }

            return fields.Deserialize<List<PrescriptionCustomFieldDto>>(ExtJsonOptions)
                ?? new List<PrescriptionCustomFieldDto>();
        }
        catch (JsonException)
        {
            return new List<PrescriptionCustomFieldDto>();
        }
    }

    private static string AppendDeleteReason(string? json, string reason)
    {
        Dictionary<string, object?> payload;
        try
        {
            payload = string.IsNullOrWhiteSpace(json)
                ? new Dictionary<string, object?>()
                : JsonSerializer.Deserialize<Dictionary<string, object?>>(json, ExtJsonOptions) ?? new();
        }
        catch (JsonException)
        {
            payload = new Dictionary<string, object?>();
        }
        payload["deleteReason"] = reason;
        return JsonSerializer.Serialize(payload, ExtJsonOptions);
    }

    public sealed record PreAuditRequestDto(
        string? PharmacistId = null,
        bool? IsApproved = null,
        string? Remark = null,
        int? Status = null,
        string? ReviewRemark = null)
    {
        public bool Approved => IsApproved ?? (Status == 1);
        public string EffectiveRemark => Remark ?? ReviewRemark ?? (Approved ? "前置审方通过" : "前置审方驳回");
    }

    [HttpPost("prescriptions/{rxId}/pre-audit")]
    [HttpPost("prescriptions/{rxId}/review")]
    public async Task<ApiResponse<OpdPrescription>> PreAudit(string rxId, [FromBody] PreAuditRequestDto req)
    {
        if (!CanAuditPrescription())
            return ApiResponse<OpdPrescription>.Fail(403, "仅药师可以执行处方前置审方");
        if (req == null) return ApiResponse<OpdPrescription>.Fail(400, "审方内容不能为空");
        var rx = await _rxRepo.GetByIdAsync(rxId);
        if (rx == null) return ApiResponse<OpdPrescription>.Fail(404, "处方不存在");
        if (rx.Status != 6 && rx.Status != 0)
            return ApiResponse<OpdPrescription>.Fail(409, "仅待审方处方可以执行前置审方");
        if (!IsAdmin() && !string.Equals(rx.TargetPharmacyId, _userSession!.ActiveDeptId, StringComparison.OrdinalIgnoreCase))
            return ApiResponse<OpdPrescription>.Fail(403, "只能在处方目标药房执行审方");
        var pharmacistId = (!string.IsNullOrWhiteSpace(req.PharmacistId) && IsAdmin()) ? req.PharmacistId : _userSession!.UserId;
        var activeItems = (await _rxItemRepo.QueryAsync(
            "SELECT * FROM opd_prescription_items WHERE prescription_id = @RxId AND is_deleted = 0",
            new { RxId = rxId })).ToList();
        if (activeItems.Count == 0)
            return ApiResponse<OpdPrescription>.Fail(409, "处方没有有效药品明细，不能通过审方");

        if (req.Approved)
        {
            var patient = _patientContract == null
                ? null
                : await _patientContract.GetPatientSummaryAsync(rx.PatientId);
            if (patient == null || !patient.IsActive)
            {
                return await RejectPrescriptionAsync(rx, pharmacistId,
                    "患者档案不存在或已停用，审方自动驳回");
            }
            foreach (var item in activeItems)
            {
                var (drug, drugError) = await ResolveDrugAsync(item.DrugId);
                if (drug == null || drugError != null)
                {
                    return await RejectPrescriptionAsync(rx, pharmacistId,
                        drugError ?? $"药品 [{item.DrugId}] 已失效");
                }
                if (Math.Abs(item.UnitPrice - drug.UnitPrice) > 0.01m)
                {
                    return await RejectPrescriptionAsync(rx, pharmacistId,
                        $"药品 [{drug.DrugName}] 价格已调整，请刷新处方明细后重新提交");
                }
                var conflict = FindAllergyConflict(patient.AllergyTags, drug);
                if (conflict != null)
                {
                    return await RejectPrescriptionAsync(rx, pharmacistId,
                        $"前置审方自动拦截：患者存在 [{conflict}]，禁止使用 [{drug.DrugName}]");
                }
                if (item.SkinTestFlag is 1 or 2 or 4)
                {
                    return await RejectPrescriptionAsync(rx, pharmacistId,
                        $"药品 [{drug.DrugName}] 的皮试尚未取得阴性双签结果，禁止通过审方");
                }
            }

            rx.Status = 1; // AuditedPassed
            rx.ReviewerPharmacistId = pharmacistId;
            rx.ReviewRemark = req.EffectiveRemark;
            rx.UpdatedBy = pharmacistId;
            await _rxRepo.UpdateAsync(rx);
        }
        else
        {
            return await RejectPrescriptionAsync(rx, pharmacistId,
                req.EffectiveRemark);
        }

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("OPD_PRESCRIPTION_AUDIT", nameof(OpdPrescription), rx.Id,
                $"药师审核处方 {rx.PrescriptionNo}，结果：通过，金额 ¥{rx.TotalAmount:0.00}",
                pharmacistId, null, rx.TargetPharmacyId);
        return ApiResponse<OpdPrescription>.Success(rx, rx.Status == 1 ? "前置审方通过" : "前置审方驳回");
    }

    private async Task<ApiResponse<OpdPrescription>> RejectPrescriptionAsync(
        OpdPrescription rx,
        string pharmacistId,
        string reason)
    {
        rx.Status = 2;
        rx.ReviewerPharmacistId = pharmacistId;
        rx.ReviewRemark = reason;
        rx.UpdatedBy = pharmacistId;
        await _rxRepo.UpdateAsync(rx);
        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("OPD_PRESCRIPTION_AUDIT", nameof(OpdPrescription), rx.Id,
                $"药师驳回处方 {rx.PrescriptionNo}：{reason}",
                pharmacistId, null, rx.TargetPharmacyId);
        return ApiResponse<OpdPrescription>.Fail(400, reason, rx);
    }

    public sealed record SkinTestDualSignRequestDto(
        string ItemId,
        int Result, // 3=Negative(-), 4=Positive(+)
        string BatchNo,
        string CheckerId,
        string? ExecNurseId = null,
        string? DualCheckNurseId = null
    );

    [HttpPost("prescriptions/items/skin-test-sign")]
    public async Task<ApiResponse<OpdPrescriptionItem>> RecordSkinTestDualSign([FromBody] SkinTestDualSignRequestDto req)
    {
        if (!CanSignSkinTest())
            return ApiResponse<OpdPrescriptionItem>.Fail(403, "仅护理人员可以录入皮试双签结果");
        if (req == null || req.Result is not (3 or 4) || string.IsNullOrWhiteSpace(req.BatchNo))
            return ApiResponse<OpdPrescriptionItem>.Fail(400, "皮试结果必须为阴性(3)或阳性(4)，并填写批号");
        var execNurseId = _userSession!.UserId;
        var dualCheckNurseId = string.IsNullOrWhiteSpace(req.DualCheckNurseId)
            ? req.CheckerId?.Trim()
            : req.DualCheckNurseId.Trim();
        if (string.IsNullOrWhiteSpace(dualCheckNurseId) ||
            string.Equals(execNurseId, dualCheckNurseId, StringComparison.OrdinalIgnoreCase))
            return ApiResponse<OpdPrescriptionItem>.Fail(400, "皮试需两名不同护士双人核对签名");
        if (!string.IsNullOrWhiteSpace(req.ExecNurseId) &&
            !string.Equals(req.ExecNurseId, execNurseId, StringComparison.OrdinalIgnoreCase))
            return ApiResponse<OpdPrescriptionItem>.Fail(403, "执行护士必须为当前登录用户");
        var item = await _rxItemRepo.GetByIdAsync(req.ItemId);
        if (item == null) return ApiResponse<OpdPrescriptionItem>.Fail(404, "处方药品明细不存在");
        if (item.IsDeleted != 0) return ApiResponse<OpdPrescriptionItem>.Fail(409, "已删除明细不能录入皮试");

        var rx = await _rxRepo.GetByIdAsync(item.PrescriptionId);
        if (rx == null) return ApiResponse<OpdPrescriptionItem>.Fail(404, "处方不存在");
        if (rx.Status is not (0 or 2 or 6) || item.SkinTestFlag is 3 or 4)
            return ApiResponse<OpdPrescriptionItem>.Fail(409, "处方已收费发药或皮试结果已录入，不能重复修改");

        var savedItem = await _connFactory.ExecuteInTransactionAsync(async ct =>
        {
            item.SkinTestFlag = req.Result;
            item.SkinTestBatchNo = req.BatchNo.Trim();
            item.SkinTestCheckerId = $"{execNurseId},{dualCheckNurseId}";
            item.UpdatedBy = execNurseId;
            await _rxItemRepo.UpdateAsync(item, ct);

            if (req.Result == 4) // Positive (+)
            {
                rx.Status = 2; // Locked/Rejected
                rx.ReviewRemark = $"皮试阳性(+)强阻断，严禁执行！[药品：{item.DrugNameSnapshot}]";
                rx.UpdatedBy = execNurseId;
                await _rxRepo.UpdateAsync(rx, ct);
                if (_patientContract == null)
                    throw new InvalidOperationException("患者过敏档案服务不可用，无法完成阳性结果闭环");
                var appended = await _patientContract.AppendPatientAllergyAsync(
                    rx.PatientId, $"{item.DrugNameSnapshot}皮试阳性", ct);
                if (!appended)
                    throw new InvalidOperationException("患者终身过敏档案写入失败，皮试结果已回滚");
            }
            return item;
        });

        if (req.Result == 4)
        {
            if (_auditTrail != null)
            {
                await _auditTrail.RecordAuditAsync(
                    "OPD_SKIN_TEST_POSITIVE",
                    nameof(OpdPrescriptionItem),
                    item.Id,
                    $"皮试阳性(+)强阻断：药品 [{item.DrugNameSnapshot}] 批号 [{req.BatchNo}]，双签 [{execNurseId}/{dualCheckNurseId}]，已锁定处方并标引患者终身过敏",
                    execNurseId);
            }

            return ApiResponse<OpdPrescriptionItem>.Fail(400, "皮试阳性(+)，处方已强锁定，阻断发药并已自动标引患者终身过敏档案！", savedItem);
        }

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("OPD_SKIN_TEST_NEGATIVE", nameof(OpdPrescriptionItem), item.Id,
                $"皮试阴性(-)双签：药品 [{item.DrugNameSnapshot}] 批号 [{req.BatchNo}]，核对护士 [{execNurseId}/{dualCheckNurseId}]",
                execNurseId);
        return ApiResponse<OpdPrescriptionItem>.Success(savedItem, "皮试阴性(-)双签核对完成，允许执行输液与发药");
    }

    public sealed record CreateInfectiousReportCardRequestDto(
        string VisitId,
        string PatientId,
        string IcdCode,
        string DiseaseName,
        string DiseaseCategory,
        string DiagnoseType,
        int OnsetDateInt,
        int DiagnosedDateInt);

    [HttpPost("infectious-report-cards")]
    public async Task<ApiResponse<OpdInfectiousReportCard>> CreateInfectiousReportCard([FromBody] CreateInfectiousReportCardRequestDto req)
    {
        if (string.IsNullOrWhiteSpace(req.VisitId) || string.IsNullOrWhiteSpace(req.IcdCode))
            return ApiResponse<OpdInfectiousReportCard>.Fail(400, "就诊 ID 与 ICD 疾病编码不能为空");

        string docId = _userSession?.UserId ?? "USER-DOC-OPD";
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var card = new OpdInfectiousReportCard
        {
            ReportNo = "INF-" + DateTime.Now.ToString("yyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            VisitId = req.VisitId.Trim(),
            PatientId = req.PatientId.Trim(),
            IcdCode = req.IcdCode.Trim(),
            DiseaseName = req.DiseaseName,
            DiseaseCategory = string.IsNullOrWhiteSpace(req.DiseaseCategory) ? "CLASS_B" : req.DiseaseCategory.Trim(),
            DiagnoseType = string.IsNullOrWhiteSpace(req.DiagnoseType) ? "CONFIRMED" : req.DiagnoseType.Trim(),
            OnsetDateInt = req.OnsetDateInt > 0 ? req.OnsetDateInt : SystemTime.TodayInt,
            DiagnosedDateInt = req.DiagnosedDateInt > 0 ? req.DiagnosedDateInt : SystemTime.TodayInt,
            ReportDoctorId = docId,
            ReportedAtMs = nowMs,
            CdcSyncStatus = "PENDING",
            Status = "REPORTED",
            CreatedBy = docId,
            UpdatedBy = docId
        };
        await _infectiousRepo.InsertAsync(card);

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync("OPD_INFECTIOUS_REPORT", "opd_infectious_report_cards", card.Id, $"录入传染病上报卡 [{card.ReportNo} · {card.DiseaseName}]", docId, _userSession?.DisplayName);
        }

        return ApiResponse<OpdInfectiousReportCard>.Success(card, "传染病上报卡已生成并等待疾控 CDC 交换网同步");
    }

    [HttpGet("infectious-report-cards")]
    public async Task<ApiResponse<IReadOnlyList<OpdInfectiousReportCard>>> GetInfectiousReportCards([FromQuery] string? visitId = null, [FromQuery] string? cdcStatus = null)
    {
        string sql = @"
SELECT * FROM opd_infectious_report_cards
WHERE (@VisitId IS NULL OR visit_id = @VisitId)
  AND (@CdcStatus IS NULL OR cdc_sync_status = @CdcStatus)
ORDER BY reported_at_ms DESC LIMIT 100";
        var list = await _infectiousRepo.QueryAsync(sql, new { VisitId = visitId, CdcStatus = cdcStatus });
        return ApiResponse<IReadOnlyList<OpdInfectiousReportCard>>.Success(list);
    }

    [HttpPost("infectious-report-cards/{id}/sync-cdc")]
    public async Task<ApiResponse<OpdInfectiousReportCard>> SyncCdc(string id)
    {
        var card = await _infectiousRepo.GetByIdAsync(id)
            ?? await _infectiousRepo.QuerySingleOrDefaultAsync("SELECT * FROM opd_infectious_report_cards WHERE report_no = @No", new { No = id });
        if (card == null)
            return ApiResponse<OpdInfectiousReportCard>.Fail(404, "未找到传染病上报卡");

        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        card.CdcSyncStatus = "SYNCED";
        card.CdcSyncAtMs = nowMs;
        card.UpdatedAtMs = nowMs;
        card.UpdatedBy = _userSession?.UserId ?? "USER-DOC-OPD";
        await _infectiousRepo.UpdateAsync(card);

        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync("OPD_INFECTIOUS_CDC_SYNC", "opd_infectious_report_cards", card.Id, $"传染病上报卡 [{card.ReportNo}] 已直报国家疾控 CDC 信息系统", _userSession?.UserId, _userSession?.DisplayName);
        }

        return ApiResponse<OpdInfectiousReportCard>.Success(card, "传染病信息已成功直报国家疾控 CDC 网络");
    }
}
