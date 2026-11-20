using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.Hr.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Hr.Controllers;

[ApiController]
[Route("api/v4/hr")]
public class HrController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IRepository<HrEmployee> _empRepo;
    private readonly IRepository<HrEmployeeCredential> _credRepo;
    private readonly IRepository<HrChangeRecord> _changeRepo;
    private readonly IRepository<HrContract> _contractRepo;
    private readonly IUserSession? _session;
    private readonly IAuditTrailContract? _auditTrail;
    private readonly IWorkflowEngineContract? _workflowEngine;

    public HrController(
        IDbConnectionFactory connFactory,
        IRepository<HrEmployee> empRepo,
        IRepository<HrEmployeeCredential> credRepo,
        IRepository<HrChangeRecord> changeRepo,
        IRepository<HrContract> contractRepo,
        IUserSession? session = null,
        IAuditTrailContract? auditTrail = null,
        IWorkflowEngineContract? workflowEngine = null)
    {
        _connFactory = connFactory;
        _empRepo = empRepo;
        _credRepo = credRepo;
        _changeRepo = changeRepo;
        _contractRepo = contractRepo;
        _session = session;
        _auditTrail = auditTrail;
        _workflowEngine = workflowEngine;
    }

    public sealed record OnboardEmployeeDto(
        string? EmployeeNo,
        string FullName,
        string Gender,
        int? BirthDateInt,
        string? IdCardNo,
        string? Phone,
        string? Email,
        string? Education,
        string? GraduatedSchool,
        string? MajorName,
        string? PoliticalStatus,
        int HireDateInt,
        int? ContractEndDateInt,
        string EmploymentType,
        string StaffCategory,
        string PrimaryDeptId,
        string? DirectSupervisorId,
        string ProfessionalTitle,
        string? JobPosition,
        bool AutoCreateUserAccount = true,
        string? LoginUserName = null,
        string? InitialPassword = null,
        string? InitialRoleId = null,
        int PrescribeLevel = 1,
        int SurgeryLevel = 1,
        int EmrSignLevel = 1,
        int SpecialExamLevel = 1,
        string? Remark = null);

    public sealed record UpdateEmployeeInfoDto(
        string FullName,
        string Gender,
        int? BirthDateInt,
        string? Phone,
        string? Email,
        string? Education,
        string? GraduatedSchool,
        string? MajorName,
        string? PoliticalStatus,
        string EmploymentType,
        string StaffCategory,
        string? DirectSupervisorId,
        string ProfessionalTitle,
        string? JobPosition,
        int? ContractEndDateInt,
        string? Remark,
        string? ChangeReason = null);

    public sealed record TransferDeptDto(
        string TargetDeptId,
        string? TargetRoleId,
        string? NewDirectSupervisorId,
        int EffectiveDateInt,
        string Reason);

    public sealed record PromoteTitleDto(
        string NewProfessionalTitle,
        string? NewJobPosition,
        int? NewPrescribeLevel,
        int? NewSurgeryLevel,
        int? NewEmrSignLevel,
        int? NewSpecialExamLevel,
        int EffectiveDateInt,
        string Reason);

    public sealed record ResignEmployeeDto(
        string EmploymentStatus, // RESIGNED / RETIRED
        int ResignDateInt,
        string Reason,
        bool DisableSystemAccount = true);

    public sealed record CreateCredentialDto(
        string CredentialType,
        string CertNo,
        string? IssuingAuthority,
        int? IssueDateInt,
        int? ExpiryDateInt,
        int GrantedLevel = 1,
        string? AttachmentUri = null,
        string? Remark = null,
        bool SyncMedicalPrivilege = true);

    public sealed record CreateContractDto(
        string? ContractNo,
        string ContractType,
        int StartDateInt,
        int? EndDateInt,
        int ProbationMonths = 3,
        string? Remark = null);

    private string ActorId => _session?.UserId?.Trim() ?? string.Empty;
    private string ActorName => _session?.DisplayName ?? _session?.UserName ?? ActorId;

    [HttpGet("summary")]
    public async Task<ApiResponse<object>> GetSummary()
    {
        using var conn = _connFactory.CreateConnection();
        int totalActive = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM hr_employees WHERE employment_status IN ('ACTIVE','PROBATION','ONBOARDING')");
        int doctors = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM hr_employees WHERE employment_status IN ('ACTIVE','PROBATION') AND staff_category = 'DOCTOR'");
        int nurses = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM hr_employees WHERE employment_status IN ('ACTIVE','PROBATION') AND staff_category = 'NURSE'");
        int resigned = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM hr_employees WHERE employment_status IN ('RESIGNED','RETIRED')");
        int warnDateInt = int.Parse(SystemTime.ShanghaiNow.AddDays(60).ToString("yyyyMMdd"));
        int expiringCreds = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM hr_employee_credentials WHERE expiry_date_int IS NOT NULL AND expiry_date_int <= @WarnDate AND verify_status = 'VALID'",
            new { WarnDate = warnDateInt });
        int expiringContracts = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM hr_contracts WHERE end_date_int IS NOT NULL AND end_date_int <= @WarnDate AND status = 'ACTIVE'",
            new { WarnDate = warnDateInt });

        return ApiResponse<object>.Success(new
        {
            TotalActive = totalActive,
            DoctorCount = doctors,
            NurseCount = nurses,
            ResignedOrRetiredCount = resigned,
            ExpiringCredentials60d = expiringCreds,
            ExpiringContracts60d = expiringContracts
        });
    }

    [HttpGet("employees")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetEmployees(
        [FromQuery] string? keyword = null,
        [FromQuery] string? deptId = null,
        [FromQuery] string? staffCategory = null,
        [FromQuery] string? status = null)
    {
        using var conn = _connFactory.CreateConnection();
        string sql = @"
            SELECT e.id AS Id, e.employee_no AS EmployeeNo, e.user_id AS UserId, e.full_name AS FullName,
                   e.gender AS Gender, e.birth_date_int AS BirthDateInt, e.id_card_masked AS IdCardMasked,
                   e.phone_masked AS PhoneMasked, e.email AS Email, e.education AS Education,
                   e.graduated_school AS GraduatedSchool, e.major_name AS MajorName, e.political_status AS PoliticalStatus,
                   e.hire_date_int AS HireDateInt, e.contract_end_date_int AS ContractEndDateInt,
                   e.employment_type AS EmploymentType, e.staff_category AS StaffCategory,
                   e.primary_dept_id AS PrimaryDeptId, COALESCE(d.dept_name, e.primary_dept_id) AS PrimaryDeptName,
                   e.direct_supervisor_id AS DirectSupervisorId, e.professional_title AS ProfessionalTitle,
                   e.job_position AS JobPosition, e.employment_status AS EmploymentStatus,
                   e.resign_date_int AS ResignDateInt, e.resign_reason AS ResignReason,
                   e.approval_status AS ApprovalStatus, e.remark AS Remark,
                   u.user_name AS LoginUserName, u.is_active AS AccountActive
            FROM hr_employees e
            LEFT JOIN sys_departments d ON d.id = e.primary_dept_id
            LEFT JOIN sys_users u ON u.id = e.user_id
            WHERE 1 = 1";
        var dp = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            sql += " AND (e.full_name LIKE @Kw OR e.employee_no LIKE @Kw OR e.phone_masked LIKE @Kw OR e.professional_title LIKE @Kw)";
            dp.Add("Kw", $"%{keyword.Trim()}%");
        }
        if (!string.IsNullOrWhiteSpace(deptId))
        {
            sql += " AND e.primary_dept_id = @DeptId";
            dp.Add("DeptId", deptId.Trim());
        }
        if (!string.IsNullOrWhiteSpace(staffCategory))
        {
            sql += " AND e.staff_category = @Cat";
            dp.Add("Cat", staffCategory.Trim().ToUpperInvariant());
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            sql += " AND e.employment_status = @Status";
            dp.Add("Status", status.Trim().ToUpperInvariant());
        }
        sql += " ORDER BY e.created_at_ms DESC LIMIT 200";
        var list = (await conn.QueryAsync<dynamic>(sql, dp)).Cast<object>().ToList();
        return ApiResponse<IReadOnlyList<object>>.Success(list);
    }

    /// <summary>
    /// 新员工入职登记（支持自动加密证件、生成唯一工号、一键开通 sys_users 账号与科室岗位权限并记录劳动合同与入职异动）
    /// </summary>
    [RequireRole("ADMIN", "ROLE-ADMIN", "HR_ADMIN")]
    [HttpPost("employees/onboard")]
    public async Task<ApiResponse<object>> OnboardEmployee([FromBody] OnboardEmployeeDto req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.FullName) || string.IsNullOrWhiteSpace(req.PrimaryDeptId))
            return ApiResponse<object>.Fail(400, "员工姓名与归属主科室不能为空");

        string? idCard = req.IdCardNo?.Trim();
        string? idCardBlind = null;
        string? idCardCipher = null;
        string? idCardMasked = null;
        int? parsedBirthDate = req.BirthDateInt;
        string? parsedGender = string.IsNullOrWhiteSpace(req.Gender) ? null : req.Gender.Trim();
        if (!string.IsNullOrWhiteSpace(idCard))
        {
            var idCheck = IdCardValidator.Validate(idCard);
            if (!idCheck.IsValid)
                return ApiResponse<object>.Fail(400, idCheck.ErrorMessage ?? "身份证号格式校验不通过，请核实 18 位身份证号");
            parsedBirthDate ??= idCheck.BirthDateInt;
            parsedGender ??= idCheck.GenderCode;
            idCardBlind = CryptoHelper.ComputeBlindIndex(idCard.ToUpperInvariant());
            idCardCipher = CryptoHelper.EncryptSm4Gcm(idCard.ToUpperInvariant());
            idCardMasked = CryptoHelper.MaskIdCard(idCard);
        }

        string? phone = req.Phone?.Trim();
        string? phoneCipher = string.IsNullOrWhiteSpace(phone) ? null : CryptoHelper.EncryptSm4Gcm(phone);
        string? phoneMasked = string.IsNullOrWhiteSpace(phone) ? null : CryptoHelper.MaskPhone(phone);

        long now = SystemTime.UtcNowMs;
        string empNo = string.IsNullOrWhiteSpace(req.EmployeeNo)
            ? "EMP" + SystemTime.ShanghaiNow.ToString("yyMMdd") + Random.Shared.Next(100, 999)
            : req.EmployeeNo.Trim().ToUpperInvariant();

        using var conn = _connFactory.CreateConnection();
        if (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM hr_employees WHERE employee_no = @No", new { No = empNo }) > 0)
            return ApiResponse<object>.Fail(409, $"工号 [{empNo}] 已存在，请勿重复入职登记");
        if (!string.IsNullOrWhiteSpace(idCardBlind) &&
            await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM hr_employees WHERE id_card_blind_index = @Idx AND employment_status IN ('ACTIVE','PROBATION','ONBOARDING')", new { Idx = idCardBlind }) > 0)
            return ApiResponse<object>.Fail(409, "该身份证号对应的员工当前已在职，禁止重复办理入职");

        string empId = Guid.NewGuid().ToString("D");
        string? createdUserId = null;
        string? loginUserName = null;

        await _connFactory.ExecuteInTransactionAsync(async token =>
        {
            using var txConn = _connFactory.CreateConnection();
            if (req.AutoCreateUserAccount)
            {
                loginUserName = string.IsNullOrWhiteSpace(req.LoginUserName) ? empNo.ToLowerInvariant() : req.LoginUserName.Trim().ToLowerInvariant();
                if (await txConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys_users WHERE LOWER(user_name) = @Un", new { Un = loginUserName }) > 0)
                    throw new InvalidOperationException($"登录账号 [{loginUserName}] 已被占用");

                createdUserId = "USR-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
                string rawPwd = string.IsNullOrWhiteSpace(req.InitialPassword) ? "His@2026!" : req.InitialPassword.Trim();
                string pwdHash = PasswordSecurityHelper.HashPassword(rawPwd);

                await txConn.ExecuteAsync(@"
                    INSERT INTO sys_users (id, user_name, display_name, employee_no, password_hash, prescribe_level, surgery_level, emr_sign_level, special_exam_level, professional_title, direct_supervisor_id, session_version, failed_login_count, must_change_password, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                    VALUES (@Id, @UserName, @DisplayName, @EmpNo, @PwdHash, @PrescribeLevel, @SurgeryLevel, @EmrSignLevel, @SpecialExamLevel, @Title, @SupervisorId, 1, 0, 1, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                    new
                    {
                        Id = createdUserId,
                        UserName = loginUserName,
                        DisplayName = req.FullName.Trim(),
                        EmpNo = empNo,
                        PwdHash = pwdHash,
                        req.PrescribeLevel,
                        req.SurgeryLevel,
                        req.EmrSignLevel,
                        req.SpecialExamLevel,
                        Title = req.ProfessionalTitle.Trim(),
                        SupervisorId = req.DirectSupervisorId,
                        Now = now,
                        Actor = ActorId
                    });

                string roleId = req.InitialRoleId ?? await txConn.ExecuteScalarAsync<string?>("SELECT id FROM sys_roles ORDER BY id LIMIT 1") ?? "ROLE-OPD-DOC";
                string deptCategory = await txConn.ExecuteScalarAsync<string?>(
                    "SELECT dept_category FROM sys_departments WHERE id = @DeptId LIMIT 1",
                    new { DeptId = req.PrimaryDeptId.Trim() }) ?? "OPD";
                await txConn.ExecuteAsync(@"
                    INSERT INTO sys_user_dept_posts (id, user_id, dept_id, dept_category, role_id, is_default_context, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                    VALUES (@Id, @UserId, @DeptId, @DeptCat, @RoleId, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                    new { Id = Guid.NewGuid().ToString("D"), UserId = createdUserId, DeptId = req.PrimaryDeptId.Trim(), DeptCat = deptCategory, RoleId = roleId, Now = now, Actor = ActorId });
            }

            var emp = new HrEmployee
            {
                Id = empId,
                EmployeeNo = empNo,
                UserId = createdUserId,
                FullName = req.FullName.Trim(),
                Gender = string.IsNullOrWhiteSpace(parsedGender) ? "UNKNOWN" : parsedGender,
                BirthDateInt = parsedBirthDate,
                IdCardCipher = idCardCipher,
                IdCardBlindIndex = idCardBlind,
                IdCardMasked = idCardMasked,
                PhoneCipher = phoneCipher,
                PhoneMasked = phoneMasked,
                Email = req.Email?.Trim(),
                Education = req.Education?.Trim(),
                GraduatedSchool = req.GraduatedSchool?.Trim(),
                MajorName = req.MajorName?.Trim(),
                PoliticalStatus = req.PoliticalStatus?.Trim(),
                HireDateInt = req.HireDateInt > 0 ? req.HireDateInt : int.Parse(SystemTime.ShanghaiNow.ToString("yyyyMMdd")),
                ContractEndDateInt = req.ContractEndDateInt,
                EmploymentType = string.IsNullOrWhiteSpace(req.EmploymentType) ? "CONTRACT" : req.EmploymentType.Trim().ToUpperInvariant(),
                StaffCategory = string.IsNullOrWhiteSpace(req.StaffCategory) ? "DOCTOR" : req.StaffCategory.Trim().ToUpperInvariant(),
                PrimaryDeptId = req.PrimaryDeptId.Trim(),
                DirectSupervisorId = req.DirectSupervisorId,
                ProfessionalTitle = string.IsNullOrWhiteSpace(req.ProfessionalTitle) ? "住院医师" : req.ProfessionalTitle.Trim(),
                JobPosition = req.JobPosition?.Trim(),
                EmploymentStatus = "ACTIVE",
                ApprovalStatus = "APPROVED",
                Remark = req.Remark?.Trim(),
                CreatedAtMs = now,
                CreatedBy = ActorId,
                UpdatedAtMs = now,
                UpdatedBy = ActorId
            };
            await _empRepo.InsertAsync(emp, token);

            if (req.ContractEndDateInt.HasValue)
            {
                await _contractRepo.InsertAsync(new HrContract
                {
                    Id = Guid.NewGuid().ToString("D"),
                    ContractNo = "CTR-" + empNo + "-01",
                    EmployeeId = empId,
                    ContractType = emp.EmploymentType == "PERMANENT" ? "OPEN_ENDED" : "FIXED_TERM",
                    StartDateInt = emp.HireDateInt,
                    EndDateInt = req.ContractEndDateInt,
                    ProbationMonths = 3,
                    Status = "ACTIVE",
                    CreatedAtMs = now,
                    CreatedBy = ActorId,
                    UpdatedAtMs = now,
                    UpdatedBy = ActorId
                }, token);
            }

            await _changeRepo.InsertAsync(new HrChangeRecord
            {
                Id = Guid.NewGuid().ToString("D"),
                ChangeNo = "HRC-" + SystemTime.ShanghaiNow.ToString("yyMMddHHmmss") + Random.Shared.Next(10, 99),
                EmployeeId = empId,
                EmployeeNo = empNo,
                EmployeeName = emp.FullName,
                ChangeType = "ONBOARD",
                AfterSnapshotJson = JsonSerializer.Serialize(new { emp.EmployeeNo, emp.PrimaryDeptId, emp.ProfessionalTitle, emp.StaffCategory, createdUserId }),
                EffectiveDateInt = emp.HireDateInt,
                Reason = "新员工入职登记",
                ApprovalStatus = "APPROVED",
                AppliedAtMs = now,
                AppliedBy = ActorId,
                CreatedAtMs = now,
                CreatedBy = ActorId,
                UpdatedAtMs = now,
                UpdatedBy = ActorId
            }, token);
        }, ct);

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("HR_ONBOARD", "hr_employees", empId, $"新员工入职：{req.FullName}（工号 {empNo}，账号 {loginUserName ?? "未开立"}）", ActorId, ActorName, req.PrimaryDeptId, ct);

        return ApiResponse<object>.Success(new { EmployeeId = empId, EmployeeNo = empNo, UserId = createdUserId, LoginUserName = loginUserName }, "新员工入职登记完成，系统工号与岗位权限已同步开通");
    }

    /// <summary>
    /// 修改职工人事基础信息（留存前后快照异动记录，并同步刷新 sys_users 姓名/职称/直属上级）
    /// </summary>
    [RequireRole("ADMIN", "ROLE-ADMIN", "HR_ADMIN")]
    [HttpPut("employees/{id}")]
    public async Task<ApiResponse<HrEmployee>> UpdateEmployee(string id, [FromBody] UpdateEmployeeInfoDto req, CancellationToken ct = default)
    {
        var emp = await _empRepo.GetByIdAsync(id, ct);
        if (emp == null) return ApiResponse<HrEmployee>.Fail(404, "职工档案不存在");

        string beforeJson = JsonSerializer.Serialize(new { emp.FullName, emp.Gender, emp.PhoneMasked, emp.Email, emp.Education, emp.ProfessionalTitle, emp.JobPosition, emp.DirectSupervisorId, emp.EmploymentType, emp.StaffCategory });
        long now = SystemTime.UtcNowMs;

        emp.FullName = req.FullName.Trim();
        emp.Gender = req.Gender.Trim();
        emp.BirthDateInt = req.BirthDateInt;
        if (!string.IsNullOrWhiteSpace(req.Phone) && !req.Phone.Contains('*'))
        {
            emp.PhoneCipher = CryptoHelper.EncryptSm4Gcm(req.Phone.Trim());
            emp.PhoneMasked = CryptoHelper.MaskPhone(req.Phone.Trim());
        }
        emp.Email = req.Email?.Trim();
        emp.Education = req.Education?.Trim();
        emp.GraduatedSchool = req.GraduatedSchool?.Trim();
        emp.MajorName = req.MajorName?.Trim();
        emp.PoliticalStatus = req.PoliticalStatus?.Trim();
        emp.EmploymentType = req.EmploymentType.Trim().ToUpperInvariant();
        emp.StaffCategory = req.StaffCategory.Trim().ToUpperInvariant();
        emp.DirectSupervisorId = req.DirectSupervisorId;
        emp.ProfessionalTitle = req.ProfessionalTitle.Trim();
        emp.JobPosition = req.JobPosition?.Trim();
        emp.ContractEndDateInt = req.ContractEndDateInt;
        emp.Remark = req.Remark?.Trim();
        emp.UpdatedAtMs = now;
        emp.UpdatedBy = ActorId;

        await _empRepo.UpdateAsync(emp, ct);

        if (!string.IsNullOrWhiteSpace(emp.UserId))
        {
            using var conn = _connFactory.CreateConnection();
            await conn.ExecuteAsync(
                "UPDATE sys_users SET display_name = @Name, professional_title = @Title, direct_supervisor_id = @SupId, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @UserId",
                new { Name = emp.FullName, Title = emp.ProfessionalTitle, SupId = emp.DirectSupervisorId, Now = now, Actor = ActorId, UserId = emp.UserId });
        }

        string afterJson = JsonSerializer.Serialize(new { emp.FullName, emp.Gender, emp.PhoneMasked, emp.Email, emp.Education, emp.ProfessionalTitle, emp.JobPosition, emp.DirectSupervisorId, emp.EmploymentType, emp.StaffCategory });
        await _changeRepo.InsertAsync(new HrChangeRecord
        {
            Id = Guid.NewGuid().ToString("D"),
            ChangeNo = "HRC-" + SystemTime.ShanghaiNow.ToString("yyMMddHHmmss") + Random.Shared.Next(10, 99),
            EmployeeId = emp.Id,
            EmployeeNo = emp.EmployeeNo,
            EmployeeName = emp.FullName,
            ChangeType = "INFO_UPDATE",
            BeforeSnapshotJson = beforeJson,
            AfterSnapshotJson = afterJson,
            EffectiveDateInt = int.Parse(SystemTime.ShanghaiNow.ToString("yyyyMMdd")),
            Reason = string.IsNullOrWhiteSpace(req.ChangeReason) ? "人事基础信息维护" : req.ChangeReason.Trim(),
            ApprovalStatus = "APPROVED",
            AppliedAtMs = now,
            AppliedBy = ActorId,
            CreatedAtMs = now,
            CreatedBy = ActorId,
            UpdatedAtMs = now,
            UpdatedBy = ActorId
        }, ct);

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("HR_INFO_UPDATE", "hr_employees", emp.Id, $"修改人事信息：{emp.FullName}（{emp.EmployeeNo}）", ActorId, ActorName, emp.PrimaryDeptId, ct);

        return ApiResponse<HrEmployee>.Success(emp, "人事信息已更新并同步至系统账号");
    }

    /// <summary>
    /// 调科/调岗异动（同步更新 sys_user_dept_posts 主科室与角色）
    /// </summary>
    [RequireRole("ADMIN", "ROLE-ADMIN", "HR_ADMIN")]
    [HttpPost("employees/{id}/transfer")]
    public async Task<ApiResponse<object>> TransferDepartment(string id, [FromBody] TransferDeptDto req, CancellationToken ct = default)
    {
        var emp = await _empRepo.GetByIdAsync(id, ct);
        if (emp == null) return ApiResponse<object>.Fail(404, "职工档案不存在");

        string oldDept = emp.PrimaryDeptId;
        long now = SystemTime.UtcNowMs;
        emp.PrimaryDeptId = req.TargetDeptId.Trim();
        if (!string.IsNullOrWhiteSpace(req.NewDirectSupervisorId)) emp.DirectSupervisorId = req.NewDirectSupervisorId;
        emp.UpdatedAtMs = now;
        emp.UpdatedBy = ActorId;
        await _empRepo.UpdateAsync(emp, ct);

        if (!string.IsNullOrWhiteSpace(emp.UserId))
        {
            using var conn = _connFactory.CreateConnection();
            string newDeptCategory = await conn.ExecuteScalarAsync<string?>(
                "SELECT dept_category FROM sys_departments WHERE id = @DeptId LIMIT 1",
                new { DeptId = emp.PrimaryDeptId }) ?? "OPD";
            await conn.ExecuteAsync(
                "UPDATE sys_user_dept_posts SET dept_id = @NewDept, dept_category = @DeptCat, role_id = COALESCE(@RoleId, role_id), updated_at_ms = @Now, updated_by = @Actor WHERE user_id = @UserId AND is_default_context = 1",
                new { NewDept = emp.PrimaryDeptId, DeptCat = newDeptCategory, RoleId = string.IsNullOrWhiteSpace(req.TargetRoleId) ? null : req.TargetRoleId, Now = now, Actor = ActorId, UserId = emp.UserId });
            await conn.ExecuteAsync(
                "UPDATE sys_users SET direct_supervisor_id = COALESCE(@SupId, direct_supervisor_id), session_version = session_version + 1, updated_at_ms = @Now WHERE id = @UserId",
                new { SupId = req.NewDirectSupervisorId, Now = now, UserId = emp.UserId });
        }

        await _changeRepo.InsertAsync(new HrChangeRecord
        {
            Id = Guid.NewGuid().ToString("D"),
            ChangeNo = "HRC-" + SystemTime.ShanghaiNow.ToString("yyMMddHHmmss") + Random.Shared.Next(10, 99),
            EmployeeId = emp.Id,
            EmployeeNo = emp.EmployeeNo,
            EmployeeName = emp.FullName,
            ChangeType = "TRANSFER_DEPT",
            BeforeSnapshotJson = JsonSerializer.Serialize(new { PrimaryDeptId = oldDept }),
            AfterSnapshotJson = JsonSerializer.Serialize(new { PrimaryDeptId = emp.PrimaryDeptId, req.TargetRoleId }),
            EffectiveDateInt = req.EffectiveDateInt > 0 ? req.EffectiveDateInt : int.Parse(SystemTime.ShanghaiNow.ToString("yyyyMMdd")),
            Reason = req.Reason,
            ApprovalStatus = "APPROVED",
            AppliedAtMs = now,
            AppliedBy = ActorId,
            CreatedAtMs = now,
            CreatedBy = ActorId,
            UpdatedAtMs = now,
            UpdatedBy = ActorId
        }, ct);

        return ApiResponse<object>.Success(new { emp.Id, OldDept = oldDept, NewDept = emp.PrimaryDeptId }, "调科异动已生效，主执业科室已同步更新");
    }

    /// <summary>
    /// 职称晋升与医疗权限等级联动调整
    /// </summary>
    [RequireRole("ADMIN", "ROLE-ADMIN", "HR_ADMIN")]
    [HttpPost("employees/{id}/promote")]
    public async Task<ApiResponse<object>> PromoteTitle(string id, [FromBody] PromoteTitleDto req, CancellationToken ct = default)
    {
        var emp = await _empRepo.GetByIdAsync(id, ct);
        if (emp == null) return ApiResponse<object>.Fail(404, "职工档案不存在");

        string oldTitle = emp.ProfessionalTitle;
        long now = SystemTime.UtcNowMs;
        emp.ProfessionalTitle = req.NewProfessionalTitle.Trim();
        if (!string.IsNullOrWhiteSpace(req.NewJobPosition)) emp.JobPosition = req.NewJobPosition.Trim();
        emp.UpdatedAtMs = now;
        emp.UpdatedBy = ActorId;
        await _empRepo.UpdateAsync(emp, ct);

        if (!string.IsNullOrWhiteSpace(emp.UserId))
        {
            using var conn = _connFactory.CreateConnection();
            await conn.ExecuteAsync(@"
                UPDATE sys_users
                SET professional_title = @Title,
                    prescribe_level = COALESCE(@Prescribe, prescribe_level),
                    surgery_level = COALESCE(@Surgery, surgery_level),
                    emr_sign_level = COALESCE(@EmrSign, emr_sign_level),
                    special_exam_level = COALESCE(@SpecExam, special_exam_level),
                    updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1
                WHERE id = @UserId",
                new
                {
                    Title = emp.ProfessionalTitle,
                    Prescribe = req.NewPrescribeLevel,
                    Surgery = req.NewSurgeryLevel,
                    EmrSign = req.NewEmrSignLevel,
                    SpecExam = req.NewSpecialExamLevel,
                    Now = now,
                    Actor = ActorId,
                    UserId = emp.UserId
                });
        }

        await _changeRepo.InsertAsync(new HrChangeRecord
        {
            Id = Guid.NewGuid().ToString("D"),
            ChangeNo = "HRC-" + SystemTime.ShanghaiNow.ToString("yyMMddHHmmss") + Random.Shared.Next(10, 99),
            EmployeeId = emp.Id,
            EmployeeNo = emp.EmployeeNo,
            EmployeeName = emp.FullName,
            ChangeType = "PROMOTE_TITLE",
            BeforeSnapshotJson = JsonSerializer.Serialize(new { ProfessionalTitle = oldTitle }),
            AfterSnapshotJson = JsonSerializer.Serialize(new { emp.ProfessionalTitle, req.NewPrescribeLevel, req.NewSurgeryLevel, req.NewEmrSignLevel, req.NewSpecialExamLevel }),
            EffectiveDateInt = req.EffectiveDateInt > 0 ? req.EffectiveDateInt : int.Parse(SystemTime.ShanghaiNow.ToString("yyyyMMdd")),
            Reason = req.Reason,
            ApprovalStatus = "APPROVED",
            AppliedAtMs = now,
            AppliedBy = ActorId,
            CreatedAtMs = now,
            CreatedBy = ActorId,
            UpdatedAtMs = now,
            UpdatedBy = ActorId
        }, ct);

        return ApiResponse<object>.Success(new { emp.Id, OldTitle = oldTitle, NewTitle = emp.ProfessionalTitle }, "职称晋升及医疗权限等级已同步生效");
    }

    /// <summary>
    /// 办理离职/退休（自动停用 sys_users 工号并递增 session_version 立即强制下线，保障病历安全）
    /// </summary>
    [RequireRole("ADMIN", "ROLE-ADMIN", "HR_ADMIN")]
    [HttpPost("employees/{id}/resign")]
    public async Task<ApiResponse<object>> ResignEmployee(string id, [FromBody] ResignEmployeeDto req, CancellationToken ct = default)
    {
        var emp = await _empRepo.GetByIdAsync(id, ct);
        if (emp == null) return ApiResponse<object>.Fail(404, "职工档案不存在");

        long now = SystemTime.UtcNowMs;
        string targetStatus = req.EmploymentStatus.Equals("RETIRED", StringComparison.OrdinalIgnoreCase) ? "RETIRED" : "RESIGNED";
        emp.EmploymentStatus = targetStatus;
        emp.ResignDateInt = req.ResignDateInt > 0 ? req.ResignDateInt : int.Parse(SystemTime.ShanghaiNow.ToString("yyyyMMdd"));
        emp.ResignReason = req.Reason;
        emp.UpdatedAtMs = now;
        emp.UpdatedBy = ActorId;
        await _empRepo.UpdateAsync(emp, ct);

        if (req.DisableSystemAccount && !string.IsNullOrWhiteSpace(emp.UserId))
        {
            using var conn = _connFactory.CreateConnection();
            await conn.ExecuteAsync(
                "UPDATE sys_users SET is_active = 0, session_version = session_version + 1, updated_at_ms = @Now, updated_by = @Actor WHERE id = @UserId",
                new { Now = now, Actor = ActorId, UserId = emp.UserId });
            await conn.ExecuteAsync(
                "UPDATE sec_emr_access_grants SET status = 0, updated_at_ms = @Now WHERE grantee_user_id = @UserId AND status = 1",
                new { Now = now, UserId = emp.UserId });
        }

        await _changeRepo.InsertAsync(new HrChangeRecord
        {
            Id = Guid.NewGuid().ToString("D"),
            ChangeNo = "HRC-" + SystemTime.ShanghaiNow.ToString("yyMMddHHmmss") + Random.Shared.Next(10, 99),
            EmployeeId = emp.Id,
            EmployeeNo = emp.EmployeeNo,
            EmployeeName = emp.FullName,
            ChangeType = targetStatus == "RETIRED" ? "RETIRE" : "RESIGN",
            BeforeSnapshotJson = JsonSerializer.Serialize(new { EmploymentStatus = "ACTIVE" }),
            AfterSnapshotJson = JsonSerializer.Serialize(new { emp.EmploymentStatus, emp.ResignDateInt, req.DisableSystemAccount }),
            EffectiveDateInt = emp.ResignDateInt.Value,
            Reason = req.Reason,
            ApprovalStatus = "APPROVED",
            AppliedAtMs = now,
            AppliedBy = ActorId,
            CreatedAtMs = now,
            CreatedBy = ActorId,
            UpdatedAtMs = now,
            UpdatedBy = ActorId
        }, ct);

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("HR_RESIGN", "hr_employees", emp.Id, $"办理离职/退休：{emp.FullName}（{emp.EmployeeNo}），账号已强制停权踢出", ActorId, ActorName, emp.PrimaryDeptId, ct);

        return ApiResponse<object>.Success(new { emp.Id, emp.EmploymentStatus, AccountDisabled = req.DisableSystemAccount }, "离职/退休手续已办结，关联工号已自动停权并强制下线");
    }

    [RequireRole("ADMIN", "ROLE-ADMIN", "HR_ADMIN")]
    [HttpPost("employees/{id}/decrypt-pii")]
    public async Task<ApiResponse<object>> DecryptEmployeePii(string id, [FromQuery] string reason = "人事核查", CancellationToken ct = default)
    {
        var emp = await _empRepo.GetByIdAsync(id, ct);
        if (emp == null) return ApiResponse<object>.Fail(404, "职工档案不存在");

        string? idCard = string.IsNullOrWhiteSpace(emp.IdCardCipher) ? null : CryptoHelper.DecryptSm4Gcm(emp.IdCardCipher);
        string? phone = string.IsNullOrWhiteSpace(emp.PhoneCipher) ? null : CryptoHelper.DecryptSm4Gcm(emp.PhoneCipher);

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("HR_DECRYPT_PII", "hr_employees", emp.Id, $"解密查看职工隐私证件：{emp.FullName}，原因：{reason}", ActorId, ActorName, emp.PrimaryDeptId, ct);

        return ApiResponse<object>.Success(new { emp.Id, emp.FullName, IdCardNo = idCard, Phone = phone });
    }

    [HttpGet("employees/{id}/credentials")]
    public async Task<ApiResponse<IReadOnlyList<HrEmployeeCredential>>> GetCredentials(string id)
    {
        var list = await _credRepo.QueryAsync("SELECT * FROM hr_employee_credentials WHERE employee_id = @Id ORDER BY created_at_ms DESC", new { Id = id });
        return ApiResponse<IReadOnlyList<HrEmployeeCredential>>.Success(list);
    }

    [RequireRole("ADMIN", "ROLE-ADMIN", "HR_ADMIN")]
    [HttpPost("employees/{id}/credentials")]
    public async Task<ApiResponse<HrEmployeeCredential>> AddCredential(string id, [FromBody] CreateCredentialDto req, CancellationToken ct = default)
    {
        var emp = await _empRepo.GetByIdAsync(id, ct);
        if (emp == null) return ApiResponse<HrEmployeeCredential>.Fail(404, "职工档案不存在");

        long now = SystemTime.UtcNowMs;
        var cred = new HrEmployeeCredential
        {
            Id = Guid.NewGuid().ToString("D"),
            EmployeeId = emp.Id,
            CredentialType = req.CredentialType.Trim().ToUpperInvariant(),
            CertNo = req.CertNo.Trim(),
            IssuingAuthority = req.IssuingAuthority?.Trim(),
            IssueDateInt = req.IssueDateInt,
            ExpiryDateInt = req.ExpiryDateInt,
            GrantedLevel = req.GrantedLevel,
            VerifyStatus = "VALID",
            AttachmentUri = req.AttachmentUri?.Trim(),
            Remark = req.Remark?.Trim(),
            CreatedAtMs = now,
            CreatedBy = ActorId,
            UpdatedAtMs = now,
            UpdatedBy = ActorId
        };
        await _credRepo.InsertAsync(cred, ct);

        // 执业/授权证照自动同步 sys_users 医疗权限等级
        if (req.SyncMedicalPrivilege && !string.IsNullOrWhiteSpace(emp.UserId))
        {
            using var conn = _connFactory.CreateConnection();
            string? col = cred.CredentialType switch
            {
                "ANTIBIOTIC_CERT" or "NARCOTIC_CERT" => "prescribe_level",
                "SURGERY_CERT" => "surgery_level",
                "SPECIAL_EXAM_CERT" => "special_exam_level",
                "TITLE_CERT" => "emr_sign_level",
                _ => null
            };
            if (col != null)
            {
                await conn.ExecuteAsync($"UPDATE sys_users SET {col} = CASE WHEN {col} < @Lvl THEN @Lvl ELSE {col} END, updated_at_ms = @Now WHERE id = @Uid",
                    new { Lvl = cred.GrantedLevel, Now = now, Uid = emp.UserId });
            }
        }

        return ApiResponse<HrEmployeeCredential>.Success(cred, "执业资格/授权证书已登记并同步医疗权限");
    }

    [HttpGet("credentials/expiring")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetExpiringCredentials([FromQuery] int daysAhead = 60)
    {
        int cutoff = int.Parse(SystemTime.ShanghaiNow.AddDays(Math.Max(1, daysAhead)).ToString("yyyyMMdd"));
        using var conn = _connFactory.CreateConnection();
        var list = (await conn.QueryAsync<dynamic>(@"
            SELECT c.id AS Id, c.employee_id AS EmployeeId, e.employee_no AS EmployeeNo, e.full_name AS EmployeeName,
                   c.credential_type AS CredentialType, c.cert_no AS CertNo, c.expiry_date_int AS ExpiryDateInt, c.verify_status AS VerifyStatus
            FROM hr_employee_credentials c
            INNER JOIN hr_employees e ON e.id = c.employee_id
            WHERE c.expiry_date_int IS NOT NULL AND c.expiry_date_int <= @Cutoff AND e.employment_status IN ('ACTIVE','PROBATION')
            ORDER BY c.expiry_date_int ASC",
            new { Cutoff = cutoff })).Cast<object>().ToList();
        return ApiResponse<IReadOnlyList<object>>.Success(list);
    }

    [HttpGet("changes")]
    public async Task<ApiResponse<IReadOnlyList<HrChangeRecord>>> GetChangeRecords([FromQuery] string? employeeId = null)
    {
        if (!string.IsNullOrWhiteSpace(employeeId))
        {
            var filtered = await _changeRepo.QueryAsync("SELECT * FROM hr_change_records WHERE employee_id = @EmpId ORDER BY created_at_ms DESC LIMIT 100", new { EmpId = employeeId.Trim() });
            return ApiResponse<IReadOnlyList<HrChangeRecord>>.Success(filtered);
        }
        var all = await _changeRepo.QueryAsync("SELECT * FROM hr_change_records ORDER BY created_at_ms DESC LIMIT 100");
        return ApiResponse<IReadOnlyList<HrChangeRecord>>.Success(all);
    }

    [HttpGet("employees/{id}/contracts")]
    public async Task<ApiResponse<IReadOnlyList<HrContract>>> GetContracts(string id)
    {
        var list = await _contractRepo.QueryAsync("SELECT * FROM hr_contracts WHERE employee_id = @Id ORDER BY start_date_int DESC", new { Id = id });
        return ApiResponse<IReadOnlyList<HrContract>>.Success(list);
    }

    [RequireRole("ADMIN", "ROLE-ADMIN", "HR_ADMIN")]
    [HttpPost("employees/{id}/contracts")]
    public async Task<ApiResponse<HrContract>> CreateContract(string id, [FromBody] CreateContractDto req, CancellationToken ct = default)
    {
        var emp = await _empRepo.GetByIdAsync(id, ct);
        if (emp == null) return ApiResponse<HrContract>.Fail(404, "职工档案不存在");

        long now = SystemTime.UtcNowMs;
        var ctr = new HrContract
        {
            Id = Guid.NewGuid().ToString("D"),
            ContractNo = string.IsNullOrWhiteSpace(req.ContractNo) ? $"CTR-{emp.EmployeeNo}-{SystemTime.ShanghaiNow:yyMMdd}" : req.ContractNo.Trim(),
            EmployeeId = emp.Id,
            ContractType = req.ContractType.Trim().ToUpperInvariant(),
            StartDateInt = req.StartDateInt,
            EndDateInt = req.EndDateInt,
            ProbationMonths = req.ProbationMonths,
            Status = "ACTIVE",
            Remark = req.Remark?.Trim(),
            CreatedAtMs = now,
            CreatedBy = ActorId,
            UpdatedAtMs = now,
            UpdatedBy = ActorId
        };
        await _contractRepo.InsertAsync(ctr, ct);
        if (req.EndDateInt.HasValue)
        {
            emp.ContractEndDateInt = req.EndDateInt;
            await _empRepo.UpdateAsync(emp, ct);
        }
        return ApiResponse<HrContract>.Success(ctr, "劳动合同已登记");
    }
}
