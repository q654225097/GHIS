using HIS.BuildingBlocks.Security;
using Dapper;
using System.Text.Json;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Outpatient.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Outpatient.Controllers;

[ApiController]
[RequireHisRole("ADMIN", "CASHIER", "OPD_DOC", "WARD_NURSE")]
[Route("api/v4/opd/registration")]
public class OpdRegistrationController : ControllerBase
{
    private const string ActiveRegistrationToken = "ACTIVE";

    private static readonly HashSet<string> PaymentChannels = new(StringComparer.OrdinalIgnoreCase)
    {
        "CASH", "WECHAT", "ALIPAY", "MEDICAL_INSURANCE"
    };

    private static readonly HashSet<string> CashierRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "ROLE-CASHIER", "CASHIER", "FINANCE", "ADMIN", "ROLE-ADMIN", "SUPER_ADMIN"
    };

    private readonly IRepository<OpdAppointment> _appointmentRepo;
    private readonly IRepository<OpdScheduleSlot> _slotRepo;
    private readonly IRepository<OpdVisit> _visitRepo;
    private readonly IRepository<OpdTriageQueue> _queueRepo;
    private readonly IFinanceSettlementContract _settlement;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IAuditTrailContract? _auditTrail;
    private readonly IUserSession? _userSession;

    public OpdRegistrationController(
        IRepository<OpdAppointment> appointmentRepo,
        IRepository<OpdScheduleSlot> slotRepo,
        IRepository<OpdVisit> visitRepo,
        IRepository<OpdTriageQueue> queueRepo,
        IFinanceSettlementContract settlement,
        IDbConnectionFactory connectionFactory,
        IAuditTrailContract? auditTrail = null,
        IUserSession? userSession = null)
    {
        _appointmentRepo = appointmentRepo;
        _slotRepo = slotRepo;
        _visitRepo = visitRepo;
        _queueRepo = queueRepo;
        _settlement = settlement;
        _connectionFactory = connectionFactory;
        _auditTrail = auditTrail;
        _userSession = userSession;
    }

    public sealed class PatientSearchItem
    {
        public string Id { get; set; } = string.Empty;
        public string MedicalRecordNo { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string GenderCode { get; set; } = string.Empty;
        public int Age { get; set; }
        public string? PhoneMasked { get; set; }
        public int IsTempEmergency { get; set; }
        public string? AllergyTagsJson { get; set; }
    }

    public sealed class RegistrationSlotItem
    {
        public string Id { get; set; } = string.Empty;
        public int ScheduleDateInt { get; set; }
        public string OpdDeptId { get; set; } = string.Empty;
        public string DeptName { get; set; } = string.Empty;
        public string DoctorId { get; set; } = string.Empty;
        public string DoctorName { get; set; } = string.Empty;
        public string? ClinicRoomId { get; set; }
        public string? RoomName { get; set; }
        public string SlotPeriodCode { get; set; } = string.Empty;
        public long StartsAtMs { get; set; }
        public long EndsAtMs { get; set; }
        public string RegLevelCode { get; set; } = string.Empty;
        public decimal RegistrationFee { get; set; }
        public int TotalCapacity { get; set; }
        public int ReservedCount { get; set; }
        public int SlotStatus { get; set; }
    }

    public sealed class RegistrationListItem
    {
        public string Id { get; set; } = string.Empty;
        public string AppointmentNo { get; set; } = string.Empty;
        public string PatientId { get; set; } = string.Empty;
        public string PatientName { get; set; } = string.Empty;
        public string MedicalRecordNo { get; set; } = string.Empty;
        public string GenderCode { get; set; } = string.Empty;
        public string? PhoneMasked { get; set; }
        public string ScheduleId { get; set; } = string.Empty;
        public int ScheduleDateInt { get; set; }
        public string DeptName { get; set; } = string.Empty;
        public string DoctorName { get; set; } = string.Empty;
        public string RegLevelCode { get; set; } = string.Empty;
        public string SlotPeriodCode { get; set; } = string.Empty;
        public long StartsAtMs { get; set; }
        public int QueueNo { get; set; }
        public decimal PaidAmount { get; set; }
        public string? PaymentChannel { get; set; }
        public long? PaidAtMs { get; set; }
        public int Status { get; set; }
        public int CheckinStatus { get; set; } = 1;
        public int? VisitStatus { get; set; }
        public string? VisitId { get; set; }
        public string? PaymentId { get; set; }
        public string? RefundId { get; set; }
        public string? RefundReason { get; set; }
    }

    public sealed record SettleRegistrationRequestDto(
        string PatientId,
        string ScheduleId,
        string PaymentChannel,
        string? RequestKey = null);

    public sealed record ReserveRegistrationRequestDto(
        string PatientId,
        string ScheduleId,
        string? RequestKey = null);

    public sealed record CheckinRegistrationRequestDto(
        string PaymentChannel,
        string? RequestKey = null);

    public sealed record RefundRegistrationRequestDto(string? Reason = null);

    private sealed class SlotRow
    {
        public string DoctorName { get; set; } = string.Empty;
        public string? RoomName { get; set; }
        public string Id { get; set; } = string.Empty;
        public string CampusId { get; set; } = string.Empty;
        public string OpdDeptId { get; set; } = string.Empty;
        public string? ClinicRoomId { get; set; }
        public string DoctorId { get; set; } = string.Empty;
        public int ScheduleDateInt { get; set; }
        public string SlotPeriodCode { get; set; } = string.Empty;
        public long StartsAtMs { get; set; }
        public long EndsAtMs { get; set; }
        public string RegLevelCode { get; set; } = string.Empty;
        public decimal RegistrationFee { get; set; }
        public int TotalCapacity { get; set; }
        public int ReservedCount { get; set; }
        public int SlotStatus { get; set; }
        public string DeptName { get; set; } = string.Empty;
    }

    private static readonly (string Code, string Name)[] PaymentChannelOptions =
    {
        ("CASH", "现金"),
        ("WECHAT", "微信"),
        ("ALIPAY", "支付宝"),
        ("MEDICAL_INSURANCE", "医保")
    };

    [HttpGet("payment-channels")]
    public ApiResponse<IReadOnlyList<PaymentChannelItem>> GetPaymentChannels()
    {
        if (!CanOperateRegistration())
            return ApiResponse<IReadOnlyList<PaymentChannelItem>>.Fail(403, "仅收费员或系统管理员可以查询收款渠道");

        return ApiResponse<IReadOnlyList<PaymentChannelItem>>.Success(
            PaymentChannelOptions
                .Select(option => new PaymentChannelItem
                {
                    Code = option.Code,
                    Name = option.Name
                })
                .ToList());
    }

    public sealed class PaymentChannelItem
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    [HttpGet("patients")]
    public async Task<ApiResponse<IReadOnlyList<PatientSearchItem>>> SearchPatients(
        [FromQuery] string? keyword = null)
    {
        if (!CanOperateRegistration())
            return ApiResponse<IReadOnlyList<PatientSearchItem>>.Fail(403, "仅收费员或系统管理员可以查询门诊挂号收费患者");

        var search = keyword?.Trim() ?? string.Empty;
        if (search.Length > 64)
            search = search[..64];

        using var connection = _connectionFactory.CreateConnection();
        var patients = await connection.QueryAsync<PatientSearchItem>(
            """
            SELECT id AS Id,
                   medical_record_no AS MedicalRecordNo,
                   full_name AS FullName,
                   gender_code AS GenderCode,
                   CASE
                       WHEN birth_date_int <= 0 THEN 0
                       ELSE MAX(0, (CAST(strftime('%Y%m%d', 'now', '+8 hours') AS INTEGER) - birth_date_int) / 10000)
                   END AS Age,
                   phone_masked AS PhoneMasked,
                   is_temp_emergency AS IsTempEmergency,
                   allergy_tags_json AS AllergyTagsJson
            FROM pat_patients
            WHERE is_active = 1
              AND merged_to_patient_id IS NULL
              AND (@Search = '' OR medical_record_no LIKE @Like
                   OR full_name LIKE @Like OR pinyin_code LIKE @Like)
            ORDER BY full_name, medical_record_no
            LIMIT 60
            """,
            new { Search = search, Like = "%" + search + "%" });
        return ApiResponse<IReadOnlyList<PatientSearchItem>>.Success(patients.ToList());
    }

    [HttpGet("slots")]
    public async Task<ApiResponse<IReadOnlyList<RegistrationSlotItem>>> GetSlots(
        [FromQuery] string? date = null,
        [FromQuery] string? opdDeptId = null,
        [FromQuery] string? doctorId = null)
    {
        if (!CanOperateRegistration())
            return ApiResponse<IReadOnlyList<RegistrationSlotItem>>.Fail(403, "仅收费员或系统管理员可以查看门诊可挂号号源");
        if (!TryParseDateInt(date, out var dateInt))
            return ApiResponse<IReadOnlyList<RegistrationSlotItem>>.Fail(400, "号源日期格式无效，请使用 yyyy-MM-dd");

        using var connection = _connectionFactory.CreateConnection();
        var slots = await connection.QueryAsync<RegistrationSlotItem>(
            """
            SELECT s.id AS Id,
                   s.schedule_date_int AS ScheduleDateInt,
                   s.opd_dept_id AS OpdDeptId,
                   COALESCE(d.dept_name, s.opd_dept_id) AS DeptName,
                   s.doctor_id AS DoctorId,
                   COALESCE(u.display_name, s.doctor_id) AS DoctorName,
                   s.clinic_room_id AS ClinicRoomId,
                   CASE
                       WHEN s.clinic_room_id LIKE 'ROOM-OPD-101' THEN '门诊101诊室'
                       WHEN s.clinic_room_id LIKE 'ROOM-ER-01' THEN '急诊01诊室'
                       ELSE s.clinic_room_id
                   END AS RoomName,
                   s.slot_period_code AS SlotPeriodCode,
                   s.starts_at_ms AS StartsAtMs,
                   s.ends_at_ms AS EndsAtMs,
                   s.reg_level_code AS RegLevelCode,
                   s.registration_fee AS RegistrationFee,
                   s.total_capacity AS TotalCapacity,
                   s.reserved_count AS ReservedCount,
                   s.slot_status AS SlotStatus
            FROM opd_schedule_slots s
            LEFT JOIN sys_departments d ON d.id = s.opd_dept_id
            LEFT JOIN sys_users u ON u.id = s.doctor_id
            WHERE s.schedule_date_int = @DateInt
              AND s.slot_status = 1
              AND s.reserved_count < s.total_capacity
              AND (@DeptId IS NULL OR s.opd_dept_id = @DeptId)
              AND (@DoctorId IS NULL OR s.doctor_id = @DoctorId)
            ORDER BY s.starts_at_ms, d.dept_name, u.display_name
            """,
            new
            {
                DateInt = dateInt,
                DeptId = string.IsNullOrWhiteSpace(opdDeptId) ? null : opdDeptId.Trim(),
                DoctorId = string.IsNullOrWhiteSpace(doctorId) ? null : doctorId.Trim()
            });
        return ApiResponse<IReadOnlyList<RegistrationSlotItem>>.Success(slots.ToList());
    }

    [HttpGet("registrations")]
    public async Task<ApiResponse<IReadOnlyList<RegistrationListItem>>> GetRegistrations(
        [FromQuery] string? date = null,
        [FromQuery] string? keyword = null,
        [FromQuery] bool includeReserved = false)
    {
        if (!CanOperateRegistration())
            return ApiResponse<IReadOnlyList<RegistrationListItem>>.Fail(403, "仅收费员或系统管理员可以查询门诊挂号记录");
        if (!TryParseDateInt(date, out var dateInt))
            return ApiResponse<IReadOnlyList<RegistrationListItem>>.Fail(400, "挂号日期格式无效，请使用 yyyy-MM-dd");

        var search = keyword?.Trim() ?? string.Empty;
        if (search.Length > 64)
            search = search[..64];

        using var connection = _connectionFactory.CreateConnection();
        var registrations = await connection.QueryAsync<RegistrationListItem>(
            """
            SELECT a.id AS Id,
                   a.appointment_no AS AppointmentNo,
                   a.patient_id AS PatientId,
                   COALESCE(p.full_name, a.patient_id) AS PatientName,
                   COALESCE(p.medical_record_no, '') AS MedicalRecordNo,
                   COALESCE(p.gender_code, '') AS GenderCode,
                   p.phone_masked AS PhoneMasked,
                   a.schedule_id AS ScheduleId,
                   COALESCE(s.schedule_date_int, 0) AS ScheduleDateInt,
                   COALESCE(d.dept_name, s.opd_dept_id, '') AS DeptName,
                   COALESCE(u.display_name, s.doctor_id, '') AS DoctorName,
                   COALESCE(s.reg_level_code, '') AS RegLevelCode,
                   COALESCE(s.slot_period_code, '') AS SlotPeriodCode,
                   COALESCE(s.starts_at_ms, 0) AS StartsAtMs,
                   a.queue_no AS QueueNo,
                   a.paid_amount AS PaidAmount,
                   a.payment_channel AS PaymentChannel,
                   a.paid_at_ms AS PaidAtMs,
                   a.status AS Status,
                   a.checkin_status AS CheckinStatus,
                   v.status AS VisitStatus,
                   a.visit_id AS VisitId,
                   a.payment_id AS PaymentId,
                   a.refund_id AS RefundId,
                   a.refund_reason AS RefundReason
            FROM opd_appointments a
            LEFT JOIN pat_patients p ON p.id = a.patient_id
            LEFT JOIN opd_schedule_slots s ON s.id = a.schedule_id
            LEFT JOIN sys_departments d ON d.id = s.opd_dept_id
            LEFT JOIN sys_users u ON u.id = s.doctor_id
            LEFT JOIN opd_visits v ON v.id = a.visit_id
            WHERE s.schedule_date_int = @DateInt
              AND (@IncludeReserved = 1 OR a.status <> 0)
              AND (@Search = '' OR p.full_name LIKE @Like
                   OR p.medical_record_no LIKE @Like OR a.appointment_no LIKE @Like)
            ORDER BY COALESCE(a.paid_at_ms, a.created_at_ms) DESC, a.created_at_ms DESC
            LIMIT 300
            """,
            new { DateInt = dateInt, IncludeReserved = includeReserved ? 1 : 0, Search = search, Like = "%" + search + "%" });
        return ApiResponse<IReadOnlyList<RegistrationListItem>>.Success(registrations.ToList());
    }

    [HttpGet("registrations/reserved")]
    public async Task<ApiResponse<IReadOnlyList<RegistrationListItem>>> GetReservedAppointments(
        [FromQuery] string? date = null,
        [FromQuery] string? keyword = null)
    {
        if (!CanOperateRegistration())
            return ApiResponse<IReadOnlyList<RegistrationListItem>>.Fail(403, "仅收费员或系统管理员可以查询预约待取号记录");
        var search = keyword?.Trim() ?? string.Empty;
        int? dateInt = null;
        if (!string.IsNullOrWhiteSpace(date) && TryParseDateInt(date, out var parsed))
            dateInt = parsed;

        using var connection = _connectionFactory.CreateConnection();
        var list = await connection.QueryAsync<RegistrationListItem>(
            """
            SELECT a.id AS Id,
                   a.appointment_no AS AppointmentNo,
                   a.patient_id AS PatientId,
                   COALESCE(p.full_name, a.patient_id) AS PatientName,
                   COALESCE(p.medical_record_no, '') AS MedicalRecordNo,
                   COALESCE(p.gender_code, '') AS GenderCode,
                   p.phone_masked AS PhoneMasked,
                   a.schedule_id AS ScheduleId,
                   COALESCE(s.schedule_date_int, 0) AS ScheduleDateInt,
                   COALESCE(d.dept_name, s.opd_dept_id, '') AS DeptName,
                   COALESCE(u.display_name, s.doctor_id, '') AS DoctorName,
                   COALESCE(s.reg_level_code, '') AS RegLevelCode,
                   COALESCE(s.slot_period_code, '') AS SlotPeriodCode,
                   COALESCE(s.starts_at_ms, 0) AS StartsAtMs,
                   a.queue_no AS QueueNo,
                   a.paid_amount AS PaidAmount,
                   a.payment_channel AS PaymentChannel,
                   a.paid_at_ms AS PaidAtMs,
                   a.status AS Status,
                   a.checkin_status AS CheckinStatus,
                   v.status AS VisitStatus,
                   a.visit_id AS VisitId,
                   a.payment_id AS PaymentId,
                   a.refund_id AS RefundId,
                   a.refund_reason AS RefundReason
            FROM opd_appointments a
            LEFT JOIN pat_patients p ON p.id = a.patient_id
            LEFT JOIN opd_schedule_slots s ON s.id = a.schedule_id
            LEFT JOIN sys_departments d ON d.id = s.opd_dept_id
            LEFT JOIN sys_users u ON u.id = s.doctor_id
            LEFT JOIN opd_visits v ON v.id = a.visit_id
            WHERE a.status = 0 AND a.checkin_status = 0
              AND (@DateInt IS NULL OR s.schedule_date_int = @DateInt)
              AND (@Search = '' OR p.full_name LIKE @Like
                   OR p.medical_record_no LIKE @Like OR a.appointment_no LIKE @Like)
            ORDER BY COALESCE(s.starts_at_ms, a.created_at_ms) ASC
            LIMIT 200
            """,
            new { DateInt = dateInt, Search = search, Like = "%" + search + "%" });
        return ApiResponse<IReadOnlyList<RegistrationListItem>>.Success(list.ToList());
    }

    [HttpPost("registrations/reserve")]
    public async Task<ApiResponse<RegistrationListItem>> ReserveRegistration(
        [FromBody] ReserveRegistrationRequestDto request)
    {
        if (!CanOperateRegistration())
            return ApiResponse<RegistrationListItem>.Fail(403, "仅收费员或系统管理员可以办理预约锁号");
        if (request == null || string.IsNullOrWhiteSpace(request.PatientId) || string.IsNullOrWhiteSpace(request.ScheduleId))
            return ApiResponse<RegistrationListItem>.Fail(400, "请选择患者和预约号源");

        var requestKey = string.IsNullOrWhiteSpace(request.RequestKey) ? null : request.RequestKey.Trim();
        try
        {
            var result = await _connectionFactory.ExecuteInTransactionAsync(async ct =>
            {
                using var connection = _connectionFactory.CreateConnection();
                var transaction = _connectionFactory.CurrentTransaction;

                if (requestKey != null)
                {
                    var original = await FindRegistrationAsync(connection, transaction, null, requestKey, ct);
                    if (original != null)
                        return RegistrationSettlementResult.Success(original, true);
                }

                var patient = await connection.QuerySingleOrDefaultAsync<PatientSearchItem>(
                    new CommandDefinition(
                        """
                        SELECT id AS Id, medical_record_no AS MedicalRecordNo, full_name AS FullName,
                               gender_code AS GenderCode, phone_masked AS PhoneMasked, is_temp_emergency AS IsTempEmergency
                        FROM pat_patients
                        WHERE id = @PatientId AND is_active = 1 AND merged_to_patient_id IS NULL
                        """,
                        new { PatientId = request.PatientId.Trim() }, transaction, cancellationToken: ct));
                if (patient == null)
                    return RegistrationSettlementResult.Fail(404, "患者不存在、已停用或已合并");

                var slot = await GetSlotAsync(connection, transaction, request.ScheduleId.Trim(), ct);
                if (slot == null)
                    return RegistrationSettlementResult.Fail(404, "号源不存在");
                if (slot.SlotStatus != 1 || slot.ReservedCount >= slot.TotalCapacity)
                    return RegistrationSettlementResult.Fail(409, "该时段号源已约满或已停诊");

                var activeReg = await connection.QuerySingleOrDefaultAsync<string>(
                    new CommandDefinition(
                        "SELECT id FROM opd_appointments WHERE patient_id = @PatientId AND schedule_id = @ScheduleId AND active_unique_token = @ActiveToken LIMIT 1",
                        new { PatientId = patient.Id, ScheduleId = slot.Id, ActiveToken = ActiveRegistrationToken },
                        transaction, cancellationToken: ct));
                if (activeReg != null)
                    return RegistrationSettlementResult.Fail(409, "该患者已预约或已挂该时段号源");

                var nowMs = SystemTime.UtcNowMs;
                var operatorId = CurrentOperatorId;
                int updated = await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE opd_schedule_slots
                    SET reserved_count = reserved_count + 1,
                        slot_status = CASE WHEN reserved_count + 1 >= total_capacity THEN 2 ELSE 1 END,
                        updated_at_ms = @Now, updated_by = @Op, version_no = version_no + 1
                    WHERE id = @ScheduleId AND slot_status = 1 AND reserved_count < total_capacity
                    """,
                    new { ScheduleId = slot.Id, Now = nowMs, Op = operatorId }, transaction, cancellationToken: ct));
                if (updated != 1)
                    return RegistrationSettlementResult.Fail(409, "号源已被占用，请刷新后重试");

                int queueNo = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COALESCE(MAX(queue_no), 0) + 1 FROM opd_appointments WHERE schedule_id = @ScheduleId AND status <> 4",
                    new { ScheduleId = slot.Id }, transaction, cancellationToken: ct));

                var appointment = new OpdAppointment
                {
                    AppointmentNo = "APT" + DateTime.UtcNow.ToString("yyMMddHHmmssfff") + Guid.NewGuid().ToString("N")[..3].ToUpperInvariant(),
                    RequestKey = requestKey,
                    PatientId = patient.Id,
                    ScheduleId = slot.Id,
                    ActiveUniqueToken = ActiveRegistrationToken,
                    QueueNo = queueNo,
                    Status = 0,
                    CheckinStatus = 0,
                    LockExpiresAtMs = Math.Max(nowMs + 3_600_000L, slot.EndsAtMs),
                    PaidAmount = 0m,
                    CreatedBy = operatorId,
                    UpdatedBy = operatorId
                };
                await _appointmentRepo.InsertAsync(appointment, ct);

                var item = await FindRegistrationAsync(connection, transaction, appointment.Id, null, ct);
                return RegistrationSettlementResult.Success(item ?? throw new InvalidOperationException("预约写入失败"), false);
            });

            if (!result.IsSuccess || result.Item == null)
                return ApiResponse<RegistrationListItem>.Fail(result.Code, result.Message);

            return ApiResponse<RegistrationListItem>.Success(result.Item,
                $"预约锁号成功：{result.Item.AppointmentNo}（排队序号 {result.Item.QueueNo}），请提示患者到院后在窗口或自助机签到取号缴费");
        }
        catch (Exception ex)
        {
            return ApiResponse<RegistrationListItem>.Fail(500, "预约锁号失败：" + ex.Message);
        }
    }

    [HttpPost("registrations/{appointmentId}/checkin")]
    public async Task<ApiResponse<RegistrationListItem>> CheckinReservedAppointment(
        string appointmentId,
        [FromBody] CheckinRegistrationRequestDto request)
    {
        if (!CanOperateRegistration())
            return ApiResponse<RegistrationListItem>.Fail(403, "仅收费员或系统管理员可以办理到院签到取号");
        if (request == null)
            return ApiResponse<RegistrationListItem>.Fail(400, "请选择支付渠道");

        var channel = request.PaymentChannel?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!PaymentChannels.Contains(channel))
            return ApiResponse<RegistrationListItem>.Fail(400, "支付渠道无效，请选择现金、微信、支付宝或医保");

        var requestKey = string.IsNullOrWhiteSpace(request.RequestKey) ? null : request.RequestKey.Trim();
        try
        {
            var result = await _connectionFactory.ExecuteInTransactionAsync(async ct =>
            {
                using var connection = _connectionFactory.CreateConnection();
                var transaction = _connectionFactory.CurrentTransaction;

                var appointment = await connection.QuerySingleOrDefaultAsync<OpdAppointment>(
                    new CommandDefinition("SELECT * FROM opd_appointments WHERE id = @Id", new { Id = appointmentId },
                    transaction, cancellationToken: ct));
                if (appointment == null)
                    return RegistrationSettlementResult.Fail(404, "预约记录不存在");
                if (appointment.Status == 1 && appointment.CheckinStatus == 1)
                {
                    var already = await FindRegistrationAsync(connection, transaction, appointment.Id, null, ct);
                    return RegistrationSettlementResult.Success(already!, true);
                }
                if (appointment.Status != 0 || appointment.CheckinStatus != 0)
                    return RegistrationSettlementResult.Fail(409, "仅待签到锁号记录允许办理到院取号");

                var slot = await GetSlotAsync(connection, transaction, appointment.ScheduleId, ct);
                if (slot == null)
                    return RegistrationSettlementResult.Fail(404, "对应排班号源不存在");

                long nowMs = SystemTime.UtcNowMs;
                string operatorId = CurrentOperatorId;
                var payment = await _settlement.CreateRegistrationPaymentAsync(
                    new RegistrationPaymentRequestDto(
                        RequestKey: requestKey,
                        PatientId: appointment.PatientId,
                        AppointmentId: appointment.Id,
                        TotalAmount: slot.RegistrationFee,
                        PaymentChannel: channel,
                        OrderDeptId: slot.OpdDeptId,
                        AppointmentNo: appointment.AppointmentNo,
                        OperatorId: operatorId,
                        PaidAtMs: nowMs),
                    ct);

                var visit = new OpdVisit
                {
                    VisitNo = "VIS" + DateTime.UtcNow.ToString("yyMMddHHmmssfff") + Guid.NewGuid().ToString("N")[..3].ToUpperInvariant(),
                    CampusId = slot.CampusId,
                    OpdDeptId = slot.OpdDeptId,
                    ClinicRoomId = slot.ClinicRoomId,
                    PatientId = appointment.PatientId,
                    ScheduleId = slot.Id,
                    DoctorId = slot.DoctorId,
                    Status = 1,
                    CreatedBy = operatorId,
                    UpdatedBy = operatorId
                };
                await _visitRepo.InsertAsync(visit, ct);

                await _queueRepo.InsertAsync(new OpdTriageQueue
                {
                    VisitId = visit.Id,
                    OpdDeptId = visit.OpdDeptId,
                    ClinicRoomId = visit.ClinicRoomId,
                    PriorityScore = Math.Max(10, 200 - appointment.QueueNo),
                    QueueStatus = 1,
                    CheckinAtMs = nowMs,
                    CreatedBy = operatorId,
                    UpdatedBy = operatorId
                }, ct);

                appointment.Status = 1;
                appointment.CheckinStatus = 1;
                appointment.PaymentId = payment.PaymentId;
                appointment.PaidAmount = slot.RegistrationFee;
                appointment.PaymentChannel = channel;
                appointment.PaidAtMs = nowMs;
                appointment.VisitId = visit.Id;
                appointment.UpdatedAtMs = nowMs;
                appointment.UpdatedBy = operatorId;
                await _appointmentRepo.UpdateAsync(appointment, ct);

                var item = await FindRegistrationAsync(connection, transaction, appointment.Id, null, ct);
                return RegistrationSettlementResult.Success(item ?? throw new InvalidOperationException("签到取号读取失败"), false);
            });

            if (!result.IsSuccess || result.Item == null)
                return ApiResponse<RegistrationListItem>.Fail(result.Code, result.Message);

            if (!result.IsIdempotentHit && _auditTrail != null)
            {
                await _auditTrail.RecordAuditAsync("OPD_APPOINTMENT_CHECKIN", nameof(OpdAppointment), result.Item.Id,
                    $"预约到院取号缴费 {result.Item.AppointmentNo}，患者 {result.Item.PatientName}，金额 ¥{result.Item.PaidAmount:0.00}",
                    CurrentOperatorId, _userSession?.DisplayName, _userSession?.ActiveDeptId);
            }

            return ApiResponse<RegistrationListItem>.Success(result.Item, "预约到院取号缴费成功，已进入诊室候诊队列");
        }
        catch (Exception ex)
        {
            return ApiResponse<RegistrationListItem>.Fail(500, "到院取号签到失败：" + ex.Message);
        }
    }

    [HttpPost("registrations/settle")]
    public async Task<ApiResponse<RegistrationListItem>> SettleRegistration(
        [FromBody] SettleRegistrationRequestDto request)
    {
        if (!CanOperateRegistration())
            return ApiResponse<RegistrationListItem>.Fail(403, "仅收费员或系统管理员可以办理门诊挂号收费");
        if (request == null || string.IsNullOrWhiteSpace(request.PatientId)
            || string.IsNullOrWhiteSpace(request.ScheduleId))
            return ApiResponse<RegistrationListItem>.Fail(400, "请选择患者和可挂号号源");

        var channel = request.PaymentChannel?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!PaymentChannels.Contains(channel))
            return ApiResponse<RegistrationListItem>.Fail(400, "支付渠道无效，请选择现金、微信、支付宝或医保");

        var requestKey = string.IsNullOrWhiteSpace(request.RequestKey)
            ? null
            : request.RequestKey.Trim();
        if (requestKey != null && requestKey.Length > 100)
            return ApiResponse<RegistrationListItem>.Fail(400, "收费请求标识不能超过 100 个字符");

        try
        {
            var result = await _connectionFactory.ExecuteInTransactionAsync(async ct =>
            {
                using var connection = _connectionFactory.CreateConnection();
                var transaction = _connectionFactory.CurrentTransaction;

                if (requestKey != null)
                {
                    var original = await FindRegistrationAsync(
                        connection, transaction, null, requestKey, ct);
                    if (original != null)
                        return RegistrationSettlementResult.Success(original, true);

                    var usedByOtherOperation = await connection.ExecuteScalarAsync<int>(
                        new CommandDefinition(
                            """
                            SELECT COUNT(*) FROM fin_payment_orders
                            WHERE request_key = @RequestKey
                            """,
                            new { RequestKey = requestKey },
                            transaction, cancellationToken: ct));
                    if (usedByOtherOperation != 0)
                        return RegistrationSettlementResult.Fail(409, "该收费请求标识已被其他业务使用");
                }

                var patient = await connection.QuerySingleOrDefaultAsync<PatientSearchItem>(
                    new CommandDefinition(
                        """
                        SELECT id AS Id,
                               medical_record_no AS MedicalRecordNo,
                               full_name AS FullName,
                               gender_code AS GenderCode,
                               phone_masked AS PhoneMasked,
                               is_temp_emergency AS IsTempEmergency
                        FROM pat_patients
                        WHERE id = @PatientId AND is_active = 1
                          AND merged_to_patient_id IS NULL
                        """,
                        new { PatientId = request.PatientId.Trim() },
                        transaction, cancellationToken: ct));
                if (patient == null)
                    return RegistrationSettlementResult.Fail(404, "患者不存在、已停用或已合并");

                var slot = await GetSlotAsync(connection, transaction, request.ScheduleId.Trim(), ct);
                if (slot == null)
                    return RegistrationSettlementResult.Fail(404, "号源不存在");
                if (slot.SlotStatus == 3)
                    return RegistrationSettlementResult.Fail(409, "该班次已停诊，不能挂号");
                if (slot.SlotStatus != 1 || slot.ReservedCount >= slot.TotalCapacity)
                    return RegistrationSettlementResult.Fail(409, "该班次号源已约满");

                var activeRegistration = await connection.QuerySingleOrDefaultAsync<string>(
                    new CommandDefinition(
                        """
                        SELECT id FROM opd_appointments
                        WHERE patient_id = @PatientId AND schedule_id = @ScheduleId
                          AND active_unique_token = @ActiveToken
                        LIMIT 1
                        """,
                        new
                        {
                            PatientId = patient.Id,
                            ScheduleId = slot.Id,
                            ActiveToken = ActiveRegistrationToken
                        },
                        transaction, cancellationToken: ct));
                if (activeRegistration != null)
                    return RegistrationSettlementResult.Fail(409, "该患者已在该班次挂号，不能重复收费");

                var nowMs = SystemTime.UtcNowMs;
                var operatorId = CurrentOperatorId;
                var updatedRows = await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE opd_schedule_slots
                    SET reserved_count = reserved_count + 1,
                        slot_status = CASE
                            WHEN reserved_count + 1 >= total_capacity THEN 2
                            ELSE 1
                        END,
                        updated_at_ms = @Now,
                        updated_by = @OperatorId,
                        version_no = version_no + 1
                    WHERE id = @ScheduleId
                      AND slot_status = 1
                      AND reserved_count < total_capacity
                    """,
                    new { ScheduleId = slot.Id, Now = nowMs, OperatorId = operatorId },
                    transaction, cancellationToken: ct));
                if (updatedRows != 1)
                    return RegistrationSettlementResult.Fail(409, "号源已被其他收费窗口占用，请刷新号源后重试");

                var appointment = new OpdAppointment
                {
                    AppointmentNo = "REG" + DateTime.UtcNow.ToString("yyMMddHHmmssfff")
                        + Guid.NewGuid().ToString("N")[..3].ToUpperInvariant(),
                    RequestKey = requestKey,
                    PatientId = patient.Id,
                    ScheduleId = slot.Id,
                    ActiveUniqueToken = ActiveRegistrationToken,
                    QueueNo = 0,
                    Status = 1,
                    LockExpiresAtMs = nowMs,
                    PaidAmount = slot.RegistrationFee,
                    PaymentChannel = channel,
                    PaidAtMs = nowMs,
                    CreatedBy = operatorId,
                    UpdatedBy = operatorId
                };
                var queueNo = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    """
                    SELECT COALESCE(MAX(queue_no), 0) + 1
                    FROM opd_appointments
                    WHERE schedule_id = @ScheduleId AND status <> 4
                    """,
                    new { ScheduleId = slot.Id },
                    transaction, cancellationToken: ct));
                appointment.QueueNo = queueNo;

                RegistrationPaymentResultDto payment = await _settlement.CreateRegistrationPaymentAsync(
                    new RegistrationPaymentRequestDto(
                        RequestKey: requestKey,
                        PatientId: patient.Id,
                        AppointmentId: appointment.Id,
                        TotalAmount: slot.RegistrationFee,
                        PaymentChannel: channel,
                        OrderDeptId: slot.OpdDeptId,
                        AppointmentNo: appointment.AppointmentNo,
                        OperatorId: operatorId,
                        PaidAtMs: nowMs),
                    ct);
                appointment.PaymentId = payment.PaymentId;
                await _appointmentRepo.InsertAsync(appointment, ct);

                var visit = new OpdVisit
                {
                    VisitNo = "VIS" + DateTime.UtcNow.ToString("yyMMddHHmmssfff")
                        + Guid.NewGuid().ToString("N")[..3].ToUpperInvariant(),
                    CampusId = slot.CampusId,
                    OpdDeptId = slot.OpdDeptId,
                    ClinicRoomId = slot.ClinicRoomId,
                    PatientId = patient.Id,
                    ScheduleId = slot.Id,
                    DoctorId = slot.DoctorId,
                    Status = 1,
                    CreatedBy = operatorId,
                    UpdatedBy = operatorId
                };
                await _visitRepo.InsertAsync(visit, ct);

                await _queueRepo.InsertAsync(new OpdTriageQueue
                {
                    VisitId = visit.Id,
                    OpdDeptId = visit.OpdDeptId,
                    ClinicRoomId = visit.ClinicRoomId,
                    PriorityScore = Math.Max(10, 200 - appointment.QueueNo),
                    QueueStatus = 1,
                    CheckinAtMs = nowMs,
                    CreatedBy = operatorId,
                    UpdatedBy = operatorId
                }, ct);

                appointment.VisitId = visit.Id;
                await _appointmentRepo.UpdateAsync(appointment, ct);

                var item = await FindRegistrationAsync(
                    connection, transaction, appointment.Id, null, ct);
                return RegistrationSettlementResult.Success(
                    item ?? throw new InvalidOperationException("挂号已写入但无法读取"), false);
            });

            if (!result.IsSuccess || result.Item == null)
                return ApiResponse<RegistrationListItem>.Fail(result.Code, result.Message);

            if (!result.IsIdempotentHit && _auditTrail != null)
            {
                await _auditTrail.RecordAuditAsync(
                    "OPD_REGISTRATION_SETTLED",
                    nameof(OpdAppointment),
                    result.Item.Id,
                    $"门诊挂号收费 {result.Item.AppointmentNo}，患者 {result.Item.PatientName}，"
                    + $"{result.Item.DeptName}，金额 ¥{result.Item.PaidAmount:0.00}，"
                    + $"支付渠道 {result.Item.PaymentChannel}，排队号 {result.Item.QueueNo}",
                    CurrentOperatorId,
                    _userSession?.DisplayName,
                    _userSession?.ActiveDeptId);
            }

            return result.IsIdempotentHit
                ? ApiResponse<RegistrationListItem>.Success(result.Item, "幂等命中：已返回原挂号收费记录")
                : ApiResponse<RegistrationListItem>.Success(result.Item, "挂号收费成功，已生成缴费流水");
        }
        catch (Exception ex) when (IsIdempotencyCollision(ex, requestKey))
        {
            using var connection = _connectionFactory.CreateConnection();
            var original = await FindRegistrationAsync(connection, null, null, requestKey, default);
            return original == null
                ? ApiResponse<RegistrationListItem>.Fail(409, "该收费请求已被使用，未重复扣费")
                : ApiResponse<RegistrationListItem>.Success(original, "幂等命中：已返回原挂号收费记录");
        }
        catch (Exception ex)
        {
            return ApiResponse<RegistrationListItem>.Fail(500, "挂号收费失败，号源与收费均未变更：" + ex.Message);
        }
    }

    [HttpPost("registrations/{appointmentId}/refund")]
    public async Task<ApiResponse<RegistrationListItem>> RefundRegistration(
        string appointmentId,
        [FromBody] RefundRegistrationRequestDto? request)
    {
        if (!CanOperateRegistration())
            return ApiResponse<RegistrationListItem>.Fail(403, "仅收费员或系统管理员可以办理门诊挂号退费");

        var reason = request?.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
            reason = "患者申请退号";
        if (reason.Length > 500)
            return ApiResponse<RegistrationListItem>.Fail(400, "退费原因不能超过 500 个字符");

        try
        {
            var result = await _connectionFactory.ExecuteInTransactionAsync(async ct =>
            {
                using var connection = _connectionFactory.CreateConnection();
                var transaction = _connectionFactory.CurrentTransaction;
                var appointment = await connection.QuerySingleOrDefaultAsync<OpdAppointment>(
                    new CommandDefinition(
                        "SELECT * FROM opd_appointments WHERE id = @Id",
                        new { Id = appointmentId },
                        transaction, cancellationToken: ct));
                if (appointment == null)
                    return RegistrationSettlementResult.Fail(404, "挂号记录不存在");
                if (appointment.Status == 3)
                {
                    var alreadyRefunded = await FindRegistrationAsync(
                        connection, transaction, appointment.Id, null, ct);
                    return RegistrationSettlementResult.Success(
                        alreadyRefunded ?? throw new InvalidOperationException("退费记录无法读取"), true);
                }
                if (appointment.Status != 1 && appointment.Status != 2)
                    return RegistrationSettlementResult.Fail(409, "只有已缴费有效挂号可以退费");
                if (string.IsNullOrWhiteSpace(appointment.PaymentId))
                    return RegistrationSettlementResult.Fail(409, "挂号记录缺少缴费单据，不能自动退费");

                var slotExists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COUNT(*) FROM opd_schedule_slots WHERE id = @ScheduleId",
                    new { ScheduleId = appointment.ScheduleId },
                    transaction, cancellationToken: ct)) == 1;
                if (!slotExists)
                    return RegistrationSettlementResult.Fail(409, "原挂号号源不存在，不能退费");

                if (!string.IsNullOrWhiteSpace(appointment.VisitId))
                {
                    var visitStatus = await connection.QuerySingleOrDefaultAsync<int?>(
                        new CommandDefinition(
                            "SELECT status FROM opd_visits WHERE id = @VisitId",
                            new { VisitId = appointment.VisitId },
                            transaction, cancellationToken: ct));
                    if (visitStatus is >= 2 and not 4)
                        return RegistrationSettlementResult.Fail(
                            409, "患者已进入接诊或诊疗完成，不能退挂号费");
                }

                RegistrationPaymentResultDto? payment = await _settlement.GetRegistrationPaymentAsync(
                    appointment.PaymentId, ct);
                if (payment == null)
                    return RegistrationSettlementResult.Fail(409, "原缴费单不存在，不能自动退费");

                var existingRefundId = await connection.QuerySingleOrDefaultAsync<string>(
                    new CommandDefinition(
                        """
                        SELECT id FROM fin_refund_orders
                        WHERE original_payment_id = @PaymentId
                          AND status IN (1, 3)
                        ORDER BY created_at_ms
                        LIMIT 1
                        """,
                        new { PaymentId = payment.PaymentId },
                        transaction, cancellationToken: ct));
                if (existingRefundId != null)
                    return RegistrationSettlementResult.Fail(409, "原缴费单已有退费单，不能重复退费");

                var nowMs = SystemTime.UtcNowMs;
                RegistrationRefundResultDto refund = await _settlement.CreateRegistrationRefundAsync(
                    new RegistrationRefundRequestDto(
                        PaymentId: payment.PaymentId,
                        RefundAmount: appointment.PaidAmount,
                        Reason: reason,
                        OperatorId: CurrentOperatorId,
                        RefundedAtMs: nowMs),
                    ct);

                var paymentRows = await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE fin_payment_orders
                    SET status = 2, updated_at_ms = @Now, updated_by = @OperatorId,
                        version_no = version_no + 1
                    WHERE id = @PaymentId AND status = 1
                    """,
                    new { PaymentId = payment.PaymentId, Now = nowMs, OperatorId = CurrentOperatorId },
                    transaction, cancellationToken: ct));
                if (paymentRows != 1)
                    return RegistrationSettlementResult.Fail(409, "原缴费单已退款，不能重复操作");

                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE fin_charge_ledgers
                    SET status = 3, updated_at_ms = @Now, updated_by = @OperatorId,
                        version_no = version_no + 1
                    WHERE visit_or_encounter_id = @AppointmentId
                      AND item_code = 'OPD_REGISTRATION_FEE'
                      AND status = 1
                    """,
                    new { AppointmentId = appointment.Id, Now = nowMs, OperatorId = CurrentOperatorId },
                    transaction, cancellationToken: ct));

                appointment.RefundReason = reason;
                appointment.UpdatedBy = CurrentOperatorId;
                await CloseRefundedRegistrationAsync(
                    connection, transaction, appointment, refund.RefundId, ct);

                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE opd_schedule_slots
                    SET reserved_count = MAX(0, reserved_count - 1),
                        slot_status = CASE
                            WHEN slot_status = 3 THEN 3
                            WHEN slot_status = 2 THEN 1
                            ELSE slot_status
                        END,
                        updated_at_ms = @Now,
                        updated_by = @OperatorId,
                        version_no = version_no + 1
                    WHERE id = @ScheduleId
                    """,
                    new { ScheduleId = appointment.ScheduleId, Now = nowMs, OperatorId = CurrentOperatorId },
                    transaction, cancellationToken: ct));

                if (!string.IsNullOrWhiteSpace(appointment.VisitId))
                {
                    var visit = await _visitRepo.GetByIdAsync(appointment.VisitId, ct);
                    if (visit != null && visit.Status == 1)
                    {
                        visit.Status = 4;
                        visit.UpdatedBy = CurrentOperatorId;
                        await _visitRepo.UpdateAsync(visit, ct);
                    }
                    await connection.ExecuteAsync(new CommandDefinition(
                        "UPDATE opd_triage_queues SET queue_status = 4, updated_at_ms = @Now WHERE visit_id = @VisitId AND queue_status IN (1, 2, 3)",
                        new { Now = nowMs, VisitId = appointment.VisitId },
                        transaction, cancellationToken: ct));
                }

                var item = await FindRegistrationAsync(
                    connection, transaction, appointment.Id, null, ct);
                return RegistrationSettlementResult.Success(
                    item ?? throw new InvalidOperationException("退费已完成但无法读取挂号记录"), false);
            });

            if (!result.IsSuccess || result.Item == null)
                return ApiResponse<RegistrationListItem>.Fail(result.Code, result.Message);
            if (result.IsIdempotentHit)
                return ApiResponse<RegistrationListItem>.Success(result.Item, "该挂号已退费，未重复退款");

            if (_auditTrail != null)
            {
                await _auditTrail.RecordAuditAsync(
                    "OPD_REGISTRATION_REFUNDED",
                    nameof(OpdAppointment),
                    result.Item.Id,
                    $"门诊退号退费 {result.Item.AppointmentNo}，金额 ¥{result.Item.PaidAmount:0.00}，原因：{reason}",
                    CurrentOperatorId,
                    _userSession?.DisplayName,
                    _userSession?.ActiveDeptId);
            }
            return ApiResponse<RegistrationListItem>.Success(
                result.Item, $"退费成功 ¥{result.Item.PaidAmount:0.00}，已释放 1 个号源");
        }
        catch (Exception ex)
        {
            return ApiResponse<RegistrationListItem>.Fail(500, "挂号退费失败，财务和号源均未变更：" + ex.Message);
        }
    }

    private bool CanOperateRegistration()
    {
        return _userSession != null
            && (_userSession.Roles.Any(role => CashierRoles.Contains(role))
                || string.Equals(_userSession.ActiveDeptCategory.ToString(), "FINANCE",
                    StringComparison.OrdinalIgnoreCase));
    }

    private string CurrentOperatorId =>
        string.IsNullOrWhiteSpace(_userSession?.UserId) ? "SYSTEM" : _userSession.UserId;

    private static Task CloseRefundedRegistrationAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction? transaction,
        OpdAppointment appointment,
        string refundId,
        CancellationToken ct)
    {
        return connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE opd_appointments
            SET status = 3,
                active_unique_token = @ReleasedToken,
                refund_id = @RefundId,
                refund_reason = @Reason,
                updated_at_ms = @Now,
                updated_by = @OperatorId,
                version_no = version_no + 1
            WHERE id = @AppointmentId
            """,
            new
            {
                AppointmentId = appointment.Id,
                RefundId = refundId,
                ReleasedToken = "REFUNDED-" + appointment.Id,
                Reason = appointment.RefundReason,
                Now = SystemTime.UtcNowMs,
                OperatorId = appointment.UpdatedBy
            },
            transaction, cancellationToken: ct));
    }

    private static async Task<SlotRow?> GetSlotAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction? transaction,
        string scheduleId,
        CancellationToken ct)
    {
        return await connection.QuerySingleOrDefaultAsync<SlotRow>(new CommandDefinition(
            """
            SELECT s.id AS Id, s.campus_id AS CampusId, s.opd_dept_id AS OpdDeptId,
                   s.clinic_room_id AS ClinicRoomId, s.doctor_id AS DoctorId,
                   COALESCE(u.display_name, s.doctor_id) AS DoctorName,
                   s.schedule_date_int AS ScheduleDateInt,
                   s.slot_period_code AS SlotPeriodCode,
                   s.starts_at_ms AS StartsAtMs, s.ends_at_ms AS EndsAtMs,
                   s.reg_level_code AS RegLevelCode,
                   s.registration_fee AS RegistrationFee,
                   s.total_capacity AS TotalCapacity,
                   s.reserved_count AS ReservedCount,
                   s.slot_status AS SlotStatus,
                   COALESCE(d.dept_name, s.opd_dept_id) AS DeptName,
                   CASE
                       WHEN s.clinic_room_id LIKE 'ROOM-OPD-101' THEN '门诊101诊室'
                       WHEN s.clinic_room_id LIKE 'ROOM-ER-01' THEN '急诊01诊室'
                       ELSE s.clinic_room_id
                   END AS RoomName
            FROM opd_schedule_slots s
            LEFT JOIN sys_departments d ON d.id = s.opd_dept_id
            LEFT JOIN sys_users u ON u.id = s.doctor_id
            WHERE s.id = @ScheduleId
            """,
            new { ScheduleId = scheduleId },
            transaction, cancellationToken: ct));
    }

    private static async Task<RegistrationListItem?> FindRegistrationAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction? transaction,
        string? appointmentId,
        string? requestKey,
        CancellationToken ct)
    {
        return await connection.QuerySingleOrDefaultAsync<RegistrationListItem>(new CommandDefinition(
            """
            SELECT a.id AS Id, a.appointment_no AS AppointmentNo,
                   a.patient_id AS PatientId,
                   COALESCE(p.full_name, a.patient_id) AS PatientName,
                   COALESCE(p.medical_record_no, '') AS MedicalRecordNo,
                   COALESCE(p.gender_code, '') AS GenderCode,
                   p.phone_masked AS PhoneMasked,
                   a.schedule_id AS ScheduleId,
                   COALESCE(s.schedule_date_int, 0) AS ScheduleDateInt,
                   COALESCE(d.dept_name, s.opd_dept_id, '') AS DeptName,
                   COALESCE(u.display_name, s.doctor_id, '') AS DoctorName,
                   COALESCE(s.reg_level_code, '') AS RegLevelCode,
                   COALESCE(s.slot_period_code, '') AS SlotPeriodCode,
                   COALESCE(s.starts_at_ms, 0) AS StartsAtMs,
                   a.queue_no AS QueueNo, a.paid_amount AS PaidAmount,
                   a.payment_channel AS PaymentChannel, a.paid_at_ms AS PaidAtMs,
                   a.status AS Status, a.checkin_status AS CheckinStatus,
                   v.status AS VisitStatus, a.visit_id AS VisitId,
                   a.payment_id AS PaymentId, a.refund_id AS RefundId,
                   a.refund_reason AS RefundReason
            FROM opd_appointments a
            LEFT JOIN pat_patients p ON p.id = a.patient_id
            LEFT JOIN opd_schedule_slots s ON s.id = a.schedule_id
            LEFT JOIN sys_departments d ON d.id = s.opd_dept_id
            LEFT JOIN sys_users u ON u.id = s.doctor_id
            LEFT JOIN opd_visits v ON v.id = a.visit_id
            WHERE (@AppointmentId IS NULL OR a.id = @AppointmentId)
              AND (@RequestKey IS NULL OR a.request_key = @RequestKey)
            ORDER BY a.created_at_ms DESC
            LIMIT 1
            """,
            new { AppointmentId = appointmentId, RequestKey = requestKey },
            transaction, cancellationToken: ct));
    }

    private static bool TryParseDateInt(string? value, out int dateInt)
    {
        if (!DateOnly.TryParseExact(value?.Trim(), "yyyy-MM-dd", out var date))
        {
            dateInt = 0;
            return false;
        }
        dateInt = date.Year * 10000 + date.Month * 100 + date.Day;
        return true;
    }

    private static bool IsIdempotencyCollision(Exception exception, string? requestKey)
    {
        if (requestKey == null)
            return false;
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
                && (current.Message.Contains("request_key", StringComparison.OrdinalIgnoreCase)
                    || current.Message.Contains("opd_appointments", StringComparison.OrdinalIgnoreCase)
                    || current.Message.Contains("fin_payment_orders", StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }

    private sealed record RegistrationSettlementResult(
        bool IsSuccess,
        int Code,
        string Message,
        RegistrationListItem? Item,
        bool IsIdempotentHit)
    {
        public static RegistrationSettlementResult Success(
            RegistrationListItem item, bool idempotentHit) =>
            new(true, 200, idempotentHit ? "幂等命中" : "成功", item, idempotentHit);

        public static RegistrationSettlementResult Fail(int code, string message) =>
            new(false, code, message, null, false);
    }
}
