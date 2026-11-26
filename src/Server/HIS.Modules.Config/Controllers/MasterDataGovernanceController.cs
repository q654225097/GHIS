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

namespace HIS.Modules.Config.Controllers;

/// <summary>
/// 全院 15 大主数据治理中心 (MDM)：覆盖核心实体与资源、药耗诊疗收费、临床标准术语与参考字典
/// </summary>
[ApiController]
[RequireHisRole("ADMIN", "WAREHOUSE_KEEPER", "PHARMACIST")]
[Route("api/v4/mdm")]
public sealed partial class MasterDataGovernanceController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _session;
    private readonly IAuditTrailContract? _auditTrail;

    public MasterDataGovernanceController(
        IDbConnectionFactory connFactory,
        IUserSession? session = null,
        IAuditTrailContract? auditTrail = null)
    {
        _connFactory = connFactory;
        _session = session;
        _auditTrail = auditTrail;
    }

    private string ActorId => _session?.UserId?.Trim() ?? "SYSTEM";
    private string ActorName => _session?.DisplayName ?? _session?.UserName ?? ActorId;

    // ===================== DTOs =====================

    public sealed record SaveOrganizationDto(
        string? Id, string OrgCode, string Uscc, string? YbOrgCode, string OrgName,
        string CampusTag = "MAIN", string OrgLevel = "三级甲等", string OrgCategoryCode = "A100",
        string AdminRegionCode = "110101", string? PracticeLicenseNo = null, bool IsActive = true);

    public sealed record UpdateDepartmentStdDto(
        string Id, string? OrgId, bool OpdFlag, bool IpdFlag,
        string? StdSubjectCode, string? CostCenterCode, string? YbDeptCode, bool IsActive = true);

    public sealed record SaveBedStdDto(
        string? Id, string CampusId, string WardId, string RoomCode, string BedCode,
        string BedType = "AUTHORIZED", decimal DailyFee = 45m, string ChargeLevel = "NORMAL",
        string GenderLimit = "ALL", bool IsAuthorizedBed = true, bool IsOpenBed = true);

    public sealed record UpdateStaffStdDto(
        string Id, string IdType, string? PracticeCertNo, int PrescribeLevel,
        int AntibioticLevel, int NarcoticLevel, string? YbStaffCode);

    public sealed record UpdateEmpiStdDto(
        string Id, string EthnicityCode, string MaritalStatusCode, string? CurrentAddress,
        string RegionCode, string BloodTypeCode, bool HasAllergyFlag);

    public sealed record SaveVendorStdDto(
        string? Id, string SupplierCode, string SupplierName, string? Uscc, string EntityType,
        string LicenseNo, int LicenseExpiryInt, string? GspCertNo, int GspExpiryInt,
        string? BankAccount, string? BankName, bool IsColdChainQualified = true, bool IsActive = true);

    public sealed record SaveDrugStdDto(
        string? Id, string DrugCode, string DrugName, string? GenericName, string? TradeName,
        string? DosageForm, string Spec, string UnitName, string? PackUnit, string? BaseDoseUnit,
        decimal ConversionRatio, decimal Price, decimal CostPrice, string? ApprovalNo, string? Manufacturer,
        string? AtcCode, bool IsEssentialDrug, bool IsCentralProcured, int AntibioticLevel,
        bool IsNarcotic, bool IsSkinTestRequired, decimal? DddValue, string? YbDrugCode, string? DrugStandardCode,
        bool IsActive = true);

    public sealed record SaveConsumableStdDto(
        string? Id, string Code, string Name, string Spec, string Unit, string Category,
        decimal Price, string? UdiDi, bool IsImplant, bool IsDisposable, string? RegCertNo,
        string? Manufacturer, bool IsChargeable, string? YbConsumableCode, bool IsHighValue,
        bool IsSterile, bool IsActive = true);

    public sealed record SaveOrderItemDto(
        string? Id, string OrderItemCode, string OrderItemName, string? PinyinCode,
        string OrderCategory, string? ExecDeptId, string? SampleOrBodyPart,
        string? ItemConnotation, string? PriceItemCode, string? YbServiceCode, bool IsActive = true);

    public sealed record SaveChargeMappingDto(
        string? Id, string ChargeItemId, string InsType, int EffDateInt, int ExpDateInt,
        string YbCatalogCode, string InsGrade = "CLASS_A", decimal SelfPayRatio = 0m, bool IsActive = true);

    public sealed record SaveIcd10Dto(
        string? Id, string StdVer, string IcdCode, string DiseaseName, string? PinyinCode,
        string? WubiCode, string CategoryName, string? ChapterCode, string GenderExclusive = "ALL",
        bool IsChronicSpecial = false, bool IsInfectiousReport = false, bool DrgDipFlag = true, bool IsActive = true);

    public sealed record SaveTcmDiagnosisDto(
        string? Id, string StdVer, string TcmCode, string TcmName, string? PinyinCode,
        string TcmType = "DISEASE", bool IsActive = true);

    public sealed record SaveIcd9OperationDto(
        string? Id, string StdVer, string Icd9Code, string OperationName, string? PinyinCode,
        int SurgeryLevel = 1, bool IsMinimallyInvasive = false, bool IsInterventional = false,
        bool IsDaySurgery = false, string DefaultIncisionGrade = "I", bool IsActive = true);

    public sealed record SaveExamStdItemDto(
        string? Id, string StdCode, string StdName, string ItemKind = "LAB",
        string? SpecimenType = null, string? ResultUnit = null, string? RefRange = null,
        string? CriticalLow = null, string? CriticalHigh = null, string? LoincCode = null,
        string? BodyPartCode = null, bool IsActive = true);

    // ===================== 0. 主数据域总览 =====================

    [HttpGet("summary")]
    public async Task<ApiResponse<object>> GetMasterDataSummary()
    {
        using var conn = _connFactory.CreateConnection();
        var summary = new
        {
            Organizations = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM mdm_organizations WHERE is_active = 1"),
            Departments = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM sys_departments WHERE is_active = 1"),
            Beds = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ipd_beds"),
            AuthorizedBeds = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ipd_beds WHERE is_authorized_bed = 1"),
            OpenBeds = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ipd_beds WHERE is_open_bed = 1"),
            Staff = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM hr_employees WHERE employment_status = 'ACTIVE'"),
            EmpiPatients = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM pat_patients WHERE is_active = 1"),
            Vendors = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM inv_suppliers WHERE is_active = 1"),
            Drugs = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM inv_drugs WHERE is_active = 1"),
            Consumables = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM csm_consumables WHERE is_active = 1"),
            OrderItems = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM cfg_order_items WHERE is_active = 1"),
            ChargeItems = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM cfg_price_items WHERE is_active = 1"),
            ChargeMappings = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM cfg_charge_item_mappings WHERE is_active = 1"),
            Icd10Diagnoses = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM cfg_icd10_diagnoses WHERE is_active = 1"),
            TcmDiagnoses = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM cfg_tcm_diagnoses WHERE is_active = 1"),
            Icd9Operations = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM cfg_icd9_operations WHERE is_active = 1"),
            ExamStdItems = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM cfg_exam_std_items WHERE is_active = 1"),
            Dictionaries = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM cfg_clinical_dictionaries WHERE is_active = 1")
        };
        return ApiResponse<object>.Success(summary);
    }

    // ===================== 1. 医疗机构/院区 =====================

    [HttpGet("organizations")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetOrganizations()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT id AS Id, org_code AS OrgCode, uscc AS Uscc, yb_org_code AS YbOrgCode,
                   org_name AS OrgName, campus_tag AS CampusTag, org_level AS OrgLevel,
                   org_category_code AS OrgCategoryCode, admin_region_code AS AdminRegionCode,
                   practice_license_no AS PracticeLicenseNo, is_active AS IsActive
            FROM mdm_organizations ORDER BY org_code ASC")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("organizations")]
    public async Task<ApiResponse<object>> SaveOrganization([FromBody] SaveOrganizationDto req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.OrgCode) || string.IsNullOrWhiteSpace(req.OrgName) || string.IsNullOrWhiteSpace(req.Uscc))
            return ApiResponse<object>.Fail(400, "机构编码、名称与统一社会信用代码不能为空");
        string uscc = req.Uscc.Trim().ToUpperInvariant();
        if (uscc.Length != 18)
            return ApiResponse<object>.Fail(400, "统一社会信用代码须符合 GB 32100-2015（18位）规范");

        long now = SystemTime.UtcNowMs;
        using var conn = _connFactory.CreateConnection();
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        int exists = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM mdm_organizations WHERE id = @Id", new { Id = id });
        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE mdm_organizations
                SET org_code = @OrgCode, uscc = @Uscc, yb_org_code = @YbOrgCode, org_name = @OrgName,
                    campus_tag = @CampusTag, org_level = @OrgLevel, org_category_code = @OrgCategoryCode,
                    admin_region_code = @AdminRegionCode, practice_license_no = @PracticeLicenseNo,
                    is_active = @Act, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1
                WHERE id = @Id",
                new { Id = id, OrgCode = req.OrgCode.Trim(), Uscc = uscc, req.YbOrgCode, OrgName = req.OrgName.Trim(), req.CampusTag, req.OrgLevel, req.OrgCategoryCode, req.AdminRegionCode, req.PracticeLicenseNo, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        }
        else
        {
            await conn.ExecuteAsync(@"
                INSERT INTO mdm_organizations
                (id, org_code, uscc, yb_org_code, org_name, campus_tag, org_level, org_category_code, admin_region_code, practice_license_no, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES
                (@Id, @OrgCode, @Uscc, @YbOrgCode, @OrgName, @CampusTag, @OrgLevel, @OrgCategoryCode, @AdminRegionCode, @PracticeLicenseNo, @Act, 1, @Now, @Actor, @Now, @Actor, 1)",
                new { Id = id, OrgCode = req.OrgCode.Trim(), Uscc = uscc, req.YbOrgCode, OrgName = req.OrgName.Trim(), req.CampusTag, req.OrgLevel, req.OrgCategoryCode, req.AdminRegionCode, req.PracticeLicenseNo, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        }

        if (_auditTrail != null)
            await _auditTrail.RecordAuditAsync("MDM_ORG_SAVE", "mdm_organizations", id, $"维护医疗机构主数据：{req.OrgName} ({uscc})", ActorId, ActorName, null, ct);

        return ApiResponse<object>.Success(new { Id = id, Uscc = uscc }, "医疗机构主数据已保存");
    }

    // ===================== 2. 科室与病区 =====================

    [HttpGet("departments")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetDepartments()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT id AS Id, COALESCE(org_id, campus_id) AS OrgId, campus_id AS CampusId,
                   dept_code AS DeptCode, dept_name AS DeptName, dept_category AS DeptCategory,
                   parent_admin_id AS ParentAdminId, opd_flag AS OpdFlag, ipd_flag AS IpdFlag,
                   std_subject_code AS StdSubjectCode, cost_center_code AS CostCenterCode,
                   yb_dept_code AS YbDeptCode, rated_bed_count AS RatedBedCount, is_active AS IsActive
            FROM sys_departments ORDER BY dept_code ASC")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("departments/standard")]
    public async Task<ApiResponse<object>> UpdateDepartmentStandard([FromBody] UpdateDepartmentStdDto req)
    {
        if (string.IsNullOrWhiteSpace(req.Id))
            return ApiResponse<object>.Fail(400, "科室 ID 不能为空");
        using var conn = _connFactory.CreateConnection();
        int rows = await conn.ExecuteAsync(@"
            UPDATE sys_departments
            SET org_id = @OrgId, opd_flag = @Opd, ipd_flag = @Ipd, std_subject_code = @Subj,
                cost_center_code = @Cc, yb_dept_code = @Yb, is_active = @Act,
                updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1
            WHERE id = @Id",
            new { req.Id, req.OrgId, Opd = req.OpdFlag ? 1 : 0, Ipd = req.IpdFlag ? 1 : 0, Subj = req.StdSubjectCode, Cc = req.CostCenterCode, Yb = req.YbDeptCode, Act = req.IsActive ? 1 : 0, Now = SystemTime.UtcNowMs, Actor = ActorId });
        if (rows == 0) return ApiResponse<object>.Fail(404, "未找到科室记录");
        return ApiResponse<object>.Success(new { req.Id }, "科室标准编码属性已更新");
    }

    // ===================== 3. 床位资源 =====================

    [HttpGet("beds")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetBeds([FromQuery] string? wardId = null)
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT id AS Id, campus_id AS CampusId, ward_id AS WardId, room_code AS RoomCode,
                   bed_code AS BedCode, bed_type AS BedType, daily_fee AS DailyFee,
                   charge_level AS ChargeLevel, gender_limit AS GenderLimit,
                   is_authorized_bed AS IsAuthorizedBed, is_open_bed AS IsOpenBed,
                   bed_status AS BedStatus, current_encounter_id AS CurrentEncounterId
            FROM ipd_beds
            WHERE (@WardId IS NULL OR ward_id = @WardId)
            ORDER BY ward_id ASC, bed_code ASC",
            new { WardId = string.IsNullOrWhiteSpace(wardId) ? null : wardId.Trim() })).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpGet("beds/utilization")]
    public async Task<ApiResponse<object>> GetBedUtilization()
    {
        using var conn = _connFactory.CreateConnection();
        var total = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ipd_beds");
        var authorized = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ipd_beds WHERE is_authorized_bed = 1");
        var open = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ipd_beds WHERE is_open_bed = 1");
        var occupied = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ipd_beds WHERE bed_status = 1");
        decimal openRate = open > 0 ? Math.Round((decimal)occupied * 100m / open, 2) : 0m;
        decimal authRate = authorized > 0 ? Math.Round((decimal)occupied * 100m / authorized, 2) : 0m;
        return ApiResponse<object>.Success(new { Total = total, AuthorizedBeds = authorized, OpenBeds = open, OccupiedBeds = occupied, OpenBedUtilizationPct = openRate, AuthorizedBedUtilizationPct = authRate });
    }

    [HttpPost("beds")]
    public async Task<ApiResponse<object>> SaveBed([FromBody] SaveBedStdDto req)
    {
        if (string.IsNullOrWhiteSpace(req.WardId) || string.IsNullOrWhiteSpace(req.BedCode))
            return ApiResponse<object>.Fail(400, "所属病区与床号不能为空");
        long now = SystemTime.UtcNowMs;
        using var conn = _connFactory.CreateConnection();
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        int exists = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ipd_beds WHERE id = @Id", new { Id = id });
        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE ipd_beds
                SET room_code = @RoomCode, bed_code = @BedCode, bed_type = @BedType, daily_fee = @DailyFee,
                    charge_level = @ChargeLevel, gender_limit = @GenderLimit,
                    is_authorized_bed = @AuthBed, is_open_bed = @OpenBed,
                    updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1
                WHERE id = @Id",
                new { Id = id, req.RoomCode, req.BedCode, req.BedType, req.DailyFee, req.ChargeLevel, req.GenderLimit, AuthBed = req.IsAuthorizedBed ? 1 : 0, OpenBed = req.IsOpenBed ? 1 : 0, Now = now, Actor = ActorId });
        }
        else
        {
            await conn.ExecuteAsync(@"
                INSERT INTO ipd_beds
                (id, campus_id, ward_id, room_code, bed_code, bed_type, daily_fee, bed_status, charge_level, gender_limit, is_authorized_bed, is_open_bed, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES
                (@Id, @CampusId, @WardId, @RoomCode, @BedCode, @BedType, @DailyFee, 0, @ChargeLevel, @GenderLimit, @AuthBed, @OpenBed, 1, @Now, @Actor, @Now, @Actor, 1)",
                new { Id = id, req.CampusId, req.WardId, req.RoomCode, req.BedCode, req.BedType, req.DailyFee, req.ChargeLevel, req.GenderLimit, AuthBed = req.IsAuthorizedBed ? 1 : 0, OpenBed = req.IsOpenBed ? 1 : 0, Now = now, Actor = ActorId });
        }
        return ApiResponse<object>.Success(new { Id = id }, "床位主数据已保存");
    }

    // ===================== 4. 医护与员工 =====================

    [HttpGet("staff")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetStaff([FromQuery] string? keyword = null)
    {
        using var conn = _connFactory.CreateConnection();
        string sql = @"
            SELECT id AS Id, employee_no AS EmployeeNo, full_name AS FullName, gender AS Gender,
                   id_type AS IdType, id_card_masked AS IdCardMasked, primary_dept_id AS PrimaryDeptId,
                   staff_category AS StaffCategory, professional_title AS ProfessionalTitle,
                   practice_cert_no AS PracticeCertNo, prescribe_level AS PrescribeLevel,
                   antibiotic_level AS AntibioticLevel, narcotic_level AS NarcoticLevel,
                   yb_staff_code AS YbStaffCode, employment_status AS EmploymentStatus
            FROM hr_employees WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(keyword))
            sql += " AND (employee_no LIKE @Kw OR full_name LIKE @Kw OR yb_staff_code LIKE @Kw)";
        sql += " ORDER BY employee_no ASC LIMIT 200";
        var rows = (await conn.QueryAsync<dynamic>(sql, new { Kw = $"%{keyword?.Trim()}%" })).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("staff/standard")]
    public async Task<ApiResponse<object>> UpdateStaffStandard([FromBody] UpdateStaffStdDto req)
    {
        using var conn = _connFactory.CreateConnection();
        long now = SystemTime.UtcNowMs;
        int rows = await conn.ExecuteAsync(@"
            UPDATE hr_employees
            SET id_type = @IdType, practice_cert_no = @PracticeCertNo, prescribe_level = @PrescribeLevel,
                antibiotic_level = @AntibioticLevel, narcotic_level = @NarcoticLevel, yb_staff_code = @YbStaffCode,
                updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1
            WHERE id = @Id",
            new { req.Id, req.IdType, req.PracticeCertNo, req.PrescribeLevel, req.AntibioticLevel, req.NarcoticLevel, req.YbStaffCode, Now = now, Actor = ActorId });
        if (rows == 0) return ApiResponse<object>.Fail(404, "未找到职工记录");
        await conn.ExecuteAsync(@"
            UPDATE sys_users
            SET prescribe_level = @PrescribeLevel, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1
            WHERE id = (SELECT user_id FROM hr_employees WHERE id = @Id)
               OR employee_no = (SELECT employee_no FROM hr_employees WHERE id = @Id)",
            new { req.Id, req.PrescribeLevel, Now = now, Actor = ActorId });
        return ApiResponse<object>.Success(new { req.Id }, "医护人员资质与医保标准编码已更新并同步系统账号权限");
    }

    // ===================== 5. 患者主索引 (EMPI) =====================

    [HttpGet("empi")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetEmpiPatients([FromQuery] string? keyword = null)
    {
        using var conn = _connFactory.CreateConnection();
        string sql = @"
            SELECT p.id AS EmpiId, p.medical_record_no AS MedicalRecordNo, p.full_name AS FullName,
                   p.gender_code AS GenderCode, p.birth_date_int AS BirthDateInt, p.id_card_type AS IdCardType,
                   p.phone_masked AS PhoneMasked, p.ethnicity_code AS EthnicityCode, p.marital_status_code AS MaritalStatusCode,
                   p.current_address AS CurrentAddress, p.region_code AS RegionCode, p.blood_type_code AS BloodTypeCode,
                   p.has_allergy_flag AS HasAllergyFlag, p.merge_status AS MergeStatus,
                   p.merged_to_patient_id AS MergedToPatientId,
                   (SELECT COUNT(1) FROM hie_empi_mappings m WHERE m.empi_id = p.id) AS SourceMappingCount
            FROM pat_patients p WHERE p.is_active = 1";
        if (!string.IsNullOrWhiteSpace(keyword))
            sql += " AND (p.medical_record_no LIKE @Kw OR p.full_name LIKE @Kw)";
        sql += " ORDER BY p.created_at_ms DESC LIMIT 200";
        var rows = (await conn.QueryAsync<dynamic>(sql, new { Kw = $"%{keyword?.Trim()}%" })).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("empi/standard")]
    public async Task<ApiResponse<object>> UpdateEmpiStandard([FromBody] UpdateEmpiStdDto req)
    {
        using var conn = _connFactory.CreateConnection();
        int rows = await conn.ExecuteAsync(@"
            UPDATE pat_patients
            SET ethnicity_code = @EthnicityCode, marital_status_code = @MaritalStatusCode,
                current_address = @CurrentAddress, region_code = @RegionCode,
                blood_type_code = @BloodTypeCode, has_allergy_flag = @Allergy,
                updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1
            WHERE id = @Id",
            new { req.Id, req.EthnicityCode, req.MaritalStatusCode, req.CurrentAddress, req.RegionCode, req.BloodTypeCode, Allergy = req.HasAllergyFlag ? 1 : 0, Now = SystemTime.UtcNowMs, Actor = ActorId });
        if (rows == 0) return ApiResponse<object>.Fail(404, "未找到患者 EMPI 主索引");
        return ApiResponse<object>.Success(new { req.Id }, "患者 EMPI 人口学与区域标准属性已更新");
    }

    // ===================== 6. 供应商/生产厂商 =====================

    [HttpGet("vendors")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetVendors()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT id AS Id, supplier_code AS SupplierCode, supplier_name AS SupplierName,
                   uscc AS Uscc, entity_type AS EntityType, license_no AS LicenseNo,
                   license_expiry_int AS LicenseExpiryInt, gsp_cert_no AS GspCertNo,
                   gsp_expiry_int AS GspExpiryInt, bank_account AS BankAccount,
                   bank_name AS BankName, is_cold_chain_qualified AS IsColdChainQualified, is_active AS IsActive
            FROM inv_suppliers ORDER BY supplier_code ASC")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("vendors")]
    public async Task<ApiResponse<object>> SaveVendor([FromBody] SaveVendorStdDto req)
    {
        if (string.IsNullOrWhiteSpace(req.SupplierCode) || string.IsNullOrWhiteSpace(req.SupplierName))
            return ApiResponse<object>.Fail(400, "供应商编码与企业名称不能为空");
        if (!string.IsNullOrWhiteSpace(req.Uscc) && req.Uscc.Trim().Length != 18)
            return ApiResponse<object>.Fail(400, "统一社会信用代码须为18位");

        long now = SystemTime.UtcNowMs;
        using var conn = _connFactory.CreateConnection();
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        int exists = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM inv_suppliers WHERE id = @Id", new { Id = id });
        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE inv_suppliers
                SET supplier_code = @SupplierCode, supplier_name = @SupplierName, uscc = @Uscc, entity_type = @EntityType,
                    license_no = @LicenseNo, license_expiry_int = @LicenseExpiryInt, gsp_cert_no = @GspCertNo,
                    gsp_expiry_int = @GspExpiryInt, bank_account = @BankAccount, bank_name = @BankName,
                    is_cold_chain_qualified = @Cold, is_active = @Act, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1
                WHERE id = @Id",
                new { Id = id, req.SupplierCode, req.SupplierName, req.Uscc, req.EntityType, req.LicenseNo, req.LicenseExpiryInt, req.GspCertNo, req.GspExpiryInt, req.BankAccount, req.BankName, Cold = req.IsColdChainQualified ? 1 : 0, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        }
        else
        {
            await conn.ExecuteAsync(@"
                INSERT INTO inv_suppliers
                (id, supplier_code, supplier_name, uscc, entity_type, license_no, license_expiry_int, gsp_cert_no, gsp_expiry_int, bank_account, bank_name, is_cold_chain_qualified, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES
                (@Id, @SupplierCode, @SupplierName, @Uscc, @EntityType, @LicenseNo, @LicenseExpiryInt, @GspCertNo, @GspExpiryInt, @BankAccount, @BankName, @Cold, @Act, 1, @Now, @Actor, @Now, @Actor, 1)",
                new { Id = id, req.SupplierCode, req.SupplierName, req.Uscc, req.EntityType, req.LicenseNo, req.LicenseExpiryInt, req.GspCertNo, req.GspExpiryInt, req.BankAccount, req.BankName, Cold = req.IsColdChainQualified ? 1 : 0, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        }
        return ApiResponse<object>.Success(new { Id = id }, "供应商/生产厂商主数据已保存");
    }

    // ===================== 7. 药品主数据 (全字段) =====================

    [HttpGet("drugs")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetDrugs([FromQuery] string? keyword = null)
    {
        using var conn = _connFactory.CreateConnection();
        string sql = @"
            SELECT id AS Id, drug_code AS DrugCode, drug_name AS DrugName,
                   COALESCE(generic_name, drug_name) AS GenericName, trade_name AS TradeName,
                   dosage_form AS DosageForm, spec AS Spec, unit_name AS UnitName,
                   pack_unit AS PackUnit, base_dose_unit AS BaseDoseUnit, conversion_ratio AS ConversionRatio,
                   price AS Price, cost_price AS CostPrice, approval_no AS ApprovalNo, manufacturer AS Manufacturer,
                   atc_code AS AtcCode, is_essential_drug AS IsEssentialDrug, is_central_procured AS IsCentralProcured,
                   antibiotic_level AS AntibioticLevel, is_narcotic AS IsNarcotic, is_skin_test_required AS IsSkinTestRequired,
                   ddd_value AS DddValue, yb_drug_code AS YbDrugCode, drug_standard_code AS DrugStandardCode, is_active AS IsActive
            FROM inv_drugs WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(keyword))
            sql += " AND (drug_code LIKE @Kw OR drug_name LIKE @Kw OR atc_code LIKE @Kw OR yb_drug_code LIKE @Kw)";
        sql += " ORDER BY drug_code ASC LIMIT 200";
        var rows = (await conn.QueryAsync<dynamic>(sql, new { Kw = $"%{keyword?.Trim()}%" })).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("drugs")]
    public async Task<ApiResponse<object>> SaveDrug([FromBody] SaveDrugStdDto req)
    {
        if (string.IsNullOrWhiteSpace(req.DrugCode) || string.IsNullOrWhiteSpace(req.DrugName))
            return ApiResponse<object>.Fail(400, "药品编码与名称不能为空");
        long now = SystemTime.UtcNowMs;
        using var conn = _connFactory.CreateConnection();
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        int exists = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM inv_drugs WHERE id = @Id", new { Id = id });
        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE inv_drugs
                SET drug_code = @DrugCode, drug_name = @DrugName, generic_name = @GenericName, trade_name = @TradeName,
                    dosage_form = @DosageForm, spec = @Spec, unit_name = @UnitName, pack_unit = @PackUnit,
                    base_dose_unit = @BaseDoseUnit, conversion_ratio = @ConversionRatio, price = @Price, cost_price = @CostPrice,
                    approval_no = @ApprovalNo, manufacturer = @Manufacturer, atc_code = @AtcCode,
                    is_essential_drug = @Ess, is_central_procured = @Cent, antibiotic_level = @AntibioticLevel,
                    is_narcotic = @Narc, is_skin_test_required = @Skin, ddd_value = @DddValue,
                    yb_drug_code = @YbDrugCode, drug_standard_code = @DrugStandardCode, is_active = @Act,
                    updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1
                WHERE id = @Id",
                new { Id = id, req.DrugCode, req.DrugName, req.GenericName, req.TradeName, req.DosageForm, req.Spec, req.UnitName, req.PackUnit, req.BaseDoseUnit, ConversionRatio = req.ConversionRatio > 0 ? req.ConversionRatio : 1m, req.Price, req.CostPrice, req.ApprovalNo, req.Manufacturer, req.AtcCode, Ess = req.IsEssentialDrug ? 1 : 0, Cent = req.IsCentralProcured ? 1 : 0, req.AntibioticLevel, Narc = req.IsNarcotic ? 1 : 0, Skin = req.IsSkinTestRequired ? 1 : 0, req.DddValue, req.YbDrugCode, req.DrugStandardCode, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        }
        else
        {
            await conn.ExecuteAsync(@"
                INSERT INTO inv_drugs
                (id, drug_code, drug_name, generic_name, trade_name, dosage_form, spec, unit_name, pack_unit, base_dose_unit, conversion_ratio, price, cost_price, approval_no, manufacturer, atc_code, is_essential_drug, is_central_procured, antibiotic_level, is_narcotic, is_skin_test_required, ddd_value, yb_drug_code, drug_standard_code, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES
                (@Id, @DrugCode, @DrugName, @GenericName, @TradeName, @DosageForm, @Spec, @UnitName, @PackUnit, @BaseDoseUnit, @ConversionRatio, @Price, @CostPrice, @ApprovalNo, @Manufacturer, @AtcCode, @Ess, @Cent, @AntibioticLevel, @Narc, @Skin, @DddValue, @YbDrugCode, @DrugStandardCode, @Act, 1, @Now, @Actor, @Now, @Actor, 1)",
                new { Id = id, req.DrugCode, req.DrugName, req.GenericName, req.TradeName, req.DosageForm, req.Spec, req.UnitName, req.PackUnit, req.BaseDoseUnit, ConversionRatio = req.ConversionRatio > 0 ? req.ConversionRatio : 1m, req.Price, req.CostPrice, req.ApprovalNo, req.Manufacturer, req.AtcCode, Ess = req.IsEssentialDrug ? 1 : 0, Cent = req.IsCentralProcured ? 1 : 0, req.AntibioticLevel, Narc = req.IsNarcotic ? 1 : 0, Skin = req.IsSkinTestRequired ? 1 : 0, req.DddValue, req.YbDrugCode, req.DrugStandardCode, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        }
        return ApiResponse<object>.Success(new { Id = id }, "药品全字段主数据已保存");
    }

    // ===================== 8. 医用耗材主数据 (UDI-DI) =====================

    [HttpGet("consumables")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetConsumables([FromQuery] string? keyword = null)
    {
        using var conn = _connFactory.CreateConnection();
        string sql = @"
            SELECT id AS Id, code AS Code, name AS Name, spec AS Spec, unit AS Unit, category AS Category,
                   price AS Price, udi_di AS UdiDi, is_implant AS IsImplant, is_disposable AS IsDisposable,
                   reg_cert_no AS RegCertNo, manufacturer AS Manufacturer, is_chargeable AS IsChargeable,
                   yb_consumable_code AS YbConsumableCode, is_high_value AS IsHighValue, is_sterile AS IsSterile, is_active AS IsActive
            FROM csm_consumables WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(keyword))
            sql += " AND (code LIKE @Kw OR name LIKE @Kw OR udi_di LIKE @Kw)";
        sql += " ORDER BY code ASC LIMIT 200";
        var rows = (await conn.QueryAsync<dynamic>(sql, new { Kw = $"%{keyword?.Trim()}%" })).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("consumables")]
    public async Task<ApiResponse<object>> SaveConsumable([FromBody] SaveConsumableStdDto req)
    {
        if (string.IsNullOrWhiteSpace(req.Code) || string.IsNullOrWhiteSpace(req.Name))
            return ApiResponse<object>.Fail(400, "耗材编码与名称不能为空");
        long now = SystemTime.UtcNowMs;
        using var conn = _connFactory.CreateConnection();
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        int exists = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM csm_consumables WHERE id = @Id", new { Id = id });
        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE csm_consumables
                SET code = @Code, name = @Name, spec = @Spec, unit = @Unit, category = @Category, price = @Price,
                    udi_di = @UdiDi, is_implant = @Imp, is_disposable = @Disp, reg_cert_no = @RegCertNo,
                    manufacturer = @Manufacturer, is_chargeable = @Chg, yb_consumable_code = @YbConsumableCode,
                    is_high_value = @Hv, is_sterile = @Ste, is_active = @Act, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1
                WHERE id = @Id",
                new { Id = id, req.Code, req.Name, req.Spec, req.Unit, req.Category, req.Price, req.UdiDi, Imp = req.IsImplant ? 1 : 0, Disp = req.IsDisposable ? 1 : 0, req.RegCertNo, req.Manufacturer, Chg = req.IsChargeable ? 1 : 0, req.YbConsumableCode, Hv = req.IsHighValue ? 1 : 0, Ste = req.IsSterile ? 1 : 0, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        }
        else
        {
            await conn.ExecuteAsync(@"
                INSERT INTO csm_consumables
                (id, code, name, spec, unit, category, price, udi_di, is_implant, is_disposable, reg_cert_no, manufacturer, is_chargeable, yb_consumable_code, is_high_value, is_sterile, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES
                (@Id, @Code, @Name, @Spec, @Unit, @Category, @Price, @UdiDi, @Imp, @Disp, @RegCertNo, @Manufacturer, @Chg, @YbConsumableCode, @Hv, @Ste, @Act, 1, @Now, @Actor, @Now, @Actor, 1)",
                new { Id = id, req.Code, req.Name, req.Spec, req.Unit, req.Category, req.Price, req.UdiDi, Imp = req.IsImplant ? 1 : 0, Disp = req.IsDisposable ? 1 : 0, req.RegCertNo, req.Manufacturer, Chg = req.IsChargeable ? 1 : 0, req.YbConsumableCode, Hv = req.IsHighValue ? 1 : 0, Ste = req.IsSterile ? 1 : 0, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        }
        return ApiResponse<object>.Success(new { Id = id }, "医用耗材 UDI 主数据已保存");
    }

    // ===================== 9. 诊疗与医疗服务项目 =====================

    [HttpGet("order-items")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetOrderItems([FromQuery] string? category = null, [FromQuery] string? keyword = null)
    {
        using var conn = _connFactory.CreateConnection();
        string sql = @"
            SELECT id AS Id, order_item_code AS OrderItemCode, order_item_name AS OrderItemName,
                   pinyin_code AS PinyinCode, order_category AS OrderCategory, exec_dept_id AS ExecDeptId,
                   sample_or_body_part AS SampleOrBodyPart, item_connotation AS ItemConnotation,
                   price_item_code AS PriceItemCode, yb_service_code AS YbServiceCode, is_active AS IsActive
            FROM cfg_order_items WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(category)) sql += " AND order_category = @Cat";
        if (!string.IsNullOrWhiteSpace(keyword)) sql += " AND (order_item_code LIKE @Kw OR order_item_name LIKE @Kw)";
        sql += " ORDER BY order_category ASC, order_item_code ASC LIMIT 200";
        var rows = (await conn.QueryAsync<dynamic>(sql, new { Cat = category?.Trim(), Kw = $"%{keyword?.Trim()}%" })).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("order-items")]
    public async Task<ApiResponse<object>> SaveOrderItem([FromBody] SaveOrderItemDto req)
    {
        if (string.IsNullOrWhiteSpace(req.OrderItemCode) || string.IsNullOrWhiteSpace(req.OrderItemName))
            return ApiResponse<object>.Fail(400, "诊疗项目编码与名称不能为空");
        long now = SystemTime.UtcNowMs;
        using var conn = _connFactory.CreateConnection();
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        int exists = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM cfg_order_items WHERE id = @Id", new { Id = id });
        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE cfg_order_items
                SET order_item_code = @OrderItemCode, order_item_name = @OrderItemName, pinyin_code = @PinyinCode,
                    order_category = @OrderCategory, exec_dept_id = @ExecDeptId, sample_or_body_part = @SampleOrBodyPart,
                    item_connotation = @ItemConnotation, price_item_code = @PriceItemCode, yb_service_code = @YbServiceCode,
                    is_active = @Act, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1
                WHERE id = @Id",
                new { Id = id, req.OrderItemCode, req.OrderItemName, req.PinyinCode, req.OrderCategory, req.ExecDeptId, req.SampleOrBodyPart, req.ItemConnotation, req.PriceItemCode, req.YbServiceCode, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        }
        else
        {
            await conn.ExecuteAsync(@"
                INSERT INTO cfg_order_items
                (id, order_item_code, order_item_name, pinyin_code, order_category, exec_dept_id, sample_or_body_part, item_connotation, price_item_code, yb_service_code, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES
                (@Id, @OrderItemCode, @OrderItemName, @PinyinCode, @OrderCategory, @ExecDeptId, @SampleOrBodyPart, @ItemConnotation, @PriceItemCode, @YbServiceCode, @Act, 1, @Now, @Actor, @Now, @Actor, 1)",
                new { Id = id, req.OrderItemCode, req.OrderItemName, req.PinyinCode, req.OrderCategory, req.ExecDeptId, req.SampleOrBodyPart, req.ItemConnotation, req.PriceItemCode, req.YbServiceCode, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        }
        return ApiResponse<object>.Success(new { Id = id }, "诊疗服务项目已保存");
    }

    // ===================== 10. 收费项目与目录对照 =====================

    [HttpGet("charge-mappings")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetChargeMappings()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"
            SELECT m.id AS Id, m.charge_item_id AS ChargeItemId, p.item_code AS ChargeItemCode,
                   p.item_name AS ChargeItemName, p.unit_price AS UnitPrice, p.fee_category AS FeeCategory,
                   m.ins_type AS InsType, m.eff_date_int AS EffDateInt, m.exp_date_int AS ExpDateInt,
                   m.yb_catalog_code AS YbCatalogCode, m.ins_grade AS InsGrade,
                   m.self_pay_ratio AS SelfPayRatio, m.is_active AS IsActive
            FROM cfg_charge_item_mappings m
            LEFT JOIN cfg_price_items p ON p.id = m.charge_item_id
            ORDER BY m.eff_date_int DESC, m.yb_catalog_code ASC LIMIT 200")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("charge-mappings")]
    public async Task<ApiResponse<object>> SaveChargeMapping([FromBody] SaveChargeMappingDto req)
    {
        if (string.IsNullOrWhiteSpace(req.ChargeItemId) || string.IsNullOrWhiteSpace(req.YbCatalogCode))
            return ApiResponse<object>.Fail(400, "收费项目与医保目录编码不能为空");
        long now = SystemTime.UtcNowMs;
        using var conn = _connFactory.CreateConnection();
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        int exists = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM cfg_charge_item_mappings WHERE id = @Id", new { Id = id });
        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE cfg_charge_item_mappings
                SET charge_item_id = @ChargeItemId, ins_type = @InsType, eff_date_int = @EffDateInt,
                    exp_date_int = @ExpDateInt, yb_catalog_code = @YbCatalogCode, ins_grade = @InsGrade,
                    self_pay_ratio = @SelfPayRatio, is_active = @Act, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1
                WHERE id = @Id",
                new { Id = id, req.ChargeItemId, req.InsType, req.EffDateInt, req.ExpDateInt, req.YbCatalogCode, req.InsGrade, req.SelfPayRatio, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        }
        else
        {
            await conn.ExecuteAsync(@"
                INSERT INTO cfg_charge_item_mappings
                (id, charge_item_id, ins_type, eff_date_int, exp_date_int, yb_catalog_code, ins_grade, self_pay_ratio, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES
                (@Id, @ChargeItemId, @InsType, @EffDateInt, @ExpDateInt, @YbCatalogCode, @InsGrade, @SelfPayRatio, @Act, 1, @Now, @Actor, @Now, @Actor, 1)",
                new { Id = id, req.ChargeItemId, req.InsType, req.EffDateInt, req.ExpDateInt, req.YbCatalogCode, req.InsGrade, req.SelfPayRatio, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        }
        return ApiResponse<object>.Success(new { Id = id }, "收费目录医保对照已保存");
    }

    // ===================== 11. 西医疾病诊断字典 (ICD-10) =====================

    [HttpGet("icd10")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetIcd10([FromQuery] string? keyword = null)
    {
        using var conn = _connFactory.CreateConnection();
        string sql = @"
            SELECT id AS Id, std_ver AS StdVer, icd_code AS IcdCode, disease_name AS DiseaseName,
                   pinyin_code AS PinyinCode, wubi_code AS WubiCode, category_name AS CategoryName,
                   chapter_code AS ChapterCode, gender_exclusive AS GenderExclusive,
                   is_chronic_special AS IsChronicSpecial, is_infectious_report AS IsInfectiousReport,
                   drg_dip_flag AS DrgDipFlag, is_active AS IsActive
            FROM cfg_icd10_diagnoses WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(keyword))
            sql += " AND (icd_code LIKE @Kw OR disease_name LIKE @Kw OR pinyin_code LIKE @Kw)";
        sql += " ORDER BY icd_code ASC LIMIT 200";
        var rows = (await conn.QueryAsync<dynamic>(sql, new { Kw = $"%{keyword?.Trim()}%" })).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("icd10")]
    public async Task<ApiResponse<object>> SaveIcd10([FromBody] SaveIcd10Dto req)
    {
        if (string.IsNullOrWhiteSpace(req.IcdCode) || string.IsNullOrWhiteSpace(req.DiseaseName))
            return ApiResponse<object>.Fail(400, "ICD-10 编码与诊断名称不能为空");
        long now = SystemTime.UtcNowMs;
        using var conn = _connFactory.CreateConnection();
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        int exists = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM cfg_icd10_diagnoses WHERE id = @Id", new { Id = id });
        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE cfg_icd10_diagnoses
                SET std_ver = @StdVer, icd_code = @IcdCode, disease_name = @DiseaseName, pinyin_code = @PinyinCode,
                    wubi_code = @WubiCode, category_name = @CategoryName, chapter_code = @ChapterCode,
                    gender_exclusive = @GenderExclusive, is_chronic_special = @Chr, is_infectious_report = @Inf,
                    drg_dip_flag = @Drg, is_active = @Act, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1
                WHERE id = @Id",
                new { Id = id, req.StdVer, req.IcdCode, req.DiseaseName, req.PinyinCode, req.WubiCode, req.CategoryName, req.ChapterCode, req.GenderExclusive, Chr = req.IsChronicSpecial ? 1 : 0, Inf = req.IsInfectiousReport ? 1 : 0, Drg = req.DrgDipFlag ? 1 : 0, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        }
        else
        {
            await conn.ExecuteAsync(@"
                INSERT INTO cfg_icd10_diagnoses
                (id, std_ver, icd_code, disease_name, pinyin_code, wubi_code, category_name, chapter_code, gender_exclusive, is_chronic_special, is_infectious_report, drg_dip_flag, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES
                (@Id, @StdVer, @IcdCode, @DiseaseName, @PinyinCode, @WubiCode, @CategoryName, @ChapterCode, @GenderExclusive, @Chr, @Inf, @Drg, @Act, 1, @Now, @Actor, @Now, @Actor, 1)",
                new { Id = id, req.StdVer, req.IcdCode, req.DiseaseName, req.PinyinCode, req.WubiCode, req.CategoryName, req.ChapterCode, req.GenderExclusive, Chr = req.IsChronicSpecial ? 1 : 0, Inf = req.IsInfectiousReport ? 1 : 0, Drg = req.DrgDipFlag ? 1 : 0, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        }
        return ApiResponse<object>.Success(new { Id = id }, "ICD-10 西医诊断主数据已保存");
    }

    // ===================== 12. 中医病证字典 =====================

    [HttpGet("tcm-diagnoses")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetTcmDiagnoses([FromQuery] string? keyword = null)
    {
        using var conn = _connFactory.CreateConnection();
        string sql = @"
            SELECT id AS Id, std_ver AS StdVer, tcm_code AS TcmCode, tcm_name AS TcmName,
                   pinyin_code AS PinyinCode, tcm_type AS TcmType, is_active AS IsActive
            FROM cfg_tcm_diagnoses WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(keyword))
            sql += " AND (tcm_code LIKE @Kw OR tcm_name LIKE @Kw OR pinyin_code LIKE @Kw)";
        sql += " ORDER BY tcm_code ASC LIMIT 200";
        var rows = (await conn.QueryAsync<dynamic>(sql, new { Kw = $"%{keyword?.Trim()}%" })).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("tcm-diagnoses")]
    public async Task<ApiResponse<object>> SaveTcmDiagnosis([FromBody] SaveTcmDiagnosisDto req)
    {
        if (string.IsNullOrWhiteSpace(req.TcmCode) || string.IsNullOrWhiteSpace(req.TcmName))
            return ApiResponse<object>.Fail(400, "中医病证编码与名称不能为空");
        long now = SystemTime.UtcNowMs;
        using var conn = _connFactory.CreateConnection();
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        int exists = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM cfg_tcm_diagnoses WHERE id = @Id", new { Id = id });
        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE cfg_tcm_diagnoses
                SET std_ver = @StdVer, tcm_code = @TcmCode, tcm_name = @TcmName, pinyin_code = @PinyinCode,
                    tcm_type = @TcmType, is_active = @Act, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1
                WHERE id = @Id",
                new { Id = id, req.StdVer, req.TcmCode, req.TcmName, req.PinyinCode, req.TcmType, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        }
        else
        {
            await conn.ExecuteAsync(@"
                INSERT INTO cfg_tcm_diagnoses
                (id, std_ver, tcm_code, tcm_name, pinyin_code, tcm_type, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES
                (@Id, @StdVer, @TcmCode, @TcmName, @PinyinCode, @TcmType, @Act, 1, @Now, @Actor, @Now, @Actor, 1)",
                new { Id = id, req.StdVer, req.TcmCode, req.TcmName, req.PinyinCode, req.TcmType, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        }
        return ApiResponse<object>.Success(new { Id = id }, "中医病证字典已保存");
    }

    // ===================== 13. 手术操作字典 (ICD-9-CM-3) =====================

    [HttpGet("icd9-operations")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetIcd9Operations([FromQuery] string? keyword = null)
    {
        using var conn = _connFactory.CreateConnection();
        string sql = @"
            SELECT id AS Id, std_ver AS StdVer, icd9_code AS Icd9Code, operation_name AS OperationName,
                   pinyin_code AS PinyinCode, surgery_level AS SurgeryLevel,
                   is_minimally_invasive AS IsMinimallyInvasive, is_interventional AS IsInterventional,
                   is_day_surgery AS IsDaySurgery, default_incision_grade AS DefaultIncisionGrade, is_active AS IsActive
            FROM cfg_icd9_operations WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(keyword))
            sql += " AND (icd9_code LIKE @Kw OR operation_name LIKE @Kw OR pinyin_code LIKE @Kw)";
        sql += " ORDER BY icd9_code ASC LIMIT 200";
        var rows = (await conn.QueryAsync<dynamic>(sql, new { Kw = $"%{keyword?.Trim()}%" })).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("icd9-operations")]
    public async Task<ApiResponse<object>> SaveIcd9Operation([FromBody] SaveIcd9OperationDto req)
    {
        if (string.IsNullOrWhiteSpace(req.Icd9Code) || string.IsNullOrWhiteSpace(req.OperationName))
            return ApiResponse<object>.Fail(400, "手术操作编码与名称不能为空");
        long now = SystemTime.UtcNowMs;
        using var conn = _connFactory.CreateConnection();
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        int level = Math.Clamp(req.SurgeryLevel, 1, 4);
        int exists = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM cfg_icd9_operations WHERE id = @Id", new { Id = id });
        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE cfg_icd9_operations
                SET std_ver = @StdVer, icd9_code = @Icd9Code, operation_name = @OperationName, pinyin_code = @PinyinCode,
                    surgery_level = @Lvl, is_minimally_invasive = @Mi, is_interventional = @Intv,
                    is_day_surgery = @Day, default_incision_grade = @DefaultIncisionGrade, is_active = @Act,
                    updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1
                WHERE id = @Id",
                new { Id = id, req.StdVer, req.Icd9Code, req.OperationName, req.PinyinCode, Lvl = level, Mi = req.IsMinimallyInvasive ? 1 : 0, Intv = req.IsInterventional ? 1 : 0, Day = req.IsDaySurgery ? 1 : 0, req.DefaultIncisionGrade, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        }
        else
        {
            await conn.ExecuteAsync(@"
                INSERT INTO cfg_icd9_operations
                (id, std_ver, icd9_code, operation_name, pinyin_code, surgery_level, is_minimally_invasive, is_interventional, is_day_surgery, default_incision_grade, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES
                (@Id, @StdVer, @Icd9Code, @OperationName, @PinyinCode, @Lvl, @Mi, @Intv, @Day, @DefaultIncisionGrade, @Act, 1, @Now, @Actor, @Now, @Actor, 1)",
                new { Id = id, req.StdVer, req.Icd9Code, req.OperationName, req.PinyinCode, Lvl = level, Mi = req.IsMinimallyInvasive ? 1 : 0, Intv = req.IsInterventional ? 1 : 0, Day = req.IsDaySurgery ? 1 : 0, req.DefaultIncisionGrade, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        }
        return ApiResponse<object>.Success(new { Id = id }, "手术操作 ICD-9-CM-3 字典已保存");
    }

    // ===================== 14. 检验/检查标准项目 (LOINC) =====================

    [HttpGet("exam-std-items")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetExamStdItems([FromQuery] string? kind = null)
    {
        using var conn = _connFactory.CreateConnection();
        string sql = @"
            SELECT id AS Id, std_code AS StdCode, std_name AS StdName, item_kind AS ItemKind,
                   specimen_type AS SpecimenType, result_unit AS ResultUnit, ref_range AS RefRange,
                   critical_low AS CriticalLow, critical_high AS CriticalHigh,
                   loinc_code AS LoincCode, body_part_code AS BodyPartCode, is_active AS IsActive
            FROM cfg_exam_std_items WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(kind)) sql += " AND item_kind = @Kind";
        sql += " ORDER BY std_code ASC LIMIT 200";
        var rows = (await conn.QueryAsync<dynamic>(sql, new { Kind = kind?.Trim() })).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("exam-std-items")]
    public async Task<ApiResponse<object>> SaveExamStdItem([FromBody] SaveExamStdItemDto req)
    {
        if (string.IsNullOrWhiteSpace(req.StdCode) || string.IsNullOrWhiteSpace(req.StdName))
            return ApiResponse<object>.Fail(400, "检验/检查标准编码与名称不能为空");
        long now = SystemTime.UtcNowMs;
        using var conn = _connFactory.CreateConnection();
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        int exists = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM cfg_exam_std_items WHERE id = @Id", new { Id = id });
        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE cfg_exam_std_items
                SET std_code = @StdCode, std_name = @StdName, item_kind = @ItemKind, specimen_type = @SpecimenType,
                    result_unit = @ResultUnit, ref_range = @RefRange, critical_low = @CriticalLow,
                    critical_high = @CriticalHigh, loinc_code = @LoincCode, body_part_code = @BodyPartCode,
                    is_active = @Act, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1
                WHERE id = @Id",
                new { Id = id, req.StdCode, req.StdName, req.ItemKind, req.SpecimenType, req.ResultUnit, req.RefRange, req.CriticalLow, req.CriticalHigh, req.LoincCode, req.BodyPartCode, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        }
        else
        {
            await conn.ExecuteAsync(@"
                INSERT INTO cfg_exam_std_items
                (id, std_code, std_name, item_kind, specimen_type, result_unit, ref_range, critical_low, critical_high, loinc_code, body_part_code, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES
                (@Id, @StdCode, @StdName, @ItemKind, @SpecimenType, @ResultUnit, @RefRange, @CriticalLow, @CriticalHigh, @LoincCode, @BodyPartCode, @Act, 1, @Now, @Actor, @Now, @Actor, 1)",
                new { Id = id, req.StdCode, req.StdName, req.ItemKind, req.SpecimenType, req.ResultUnit, req.RefRange, req.CriticalLow, req.CriticalHigh, req.LoincCode, req.BodyPartCode, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        }
        return ApiResponse<object>.Success(new { Id = id }, "检验/检查标准项目已保存");
    }
}
