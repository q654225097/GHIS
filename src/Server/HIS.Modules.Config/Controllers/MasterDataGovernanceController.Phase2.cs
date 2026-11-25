using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Config.Controllers;

public sealed partial class MasterDataGovernanceController
{
    // ===================== Phase 2 DTOs =====================

    public sealed record SaveYbDiseaseDto(string? Id, string DiseaseCode, string DiseaseName, string DiseaseCategory = "OPD_CHRONIC", decimal QuotaLimitAmount = 0m, int EffDateInt = 20240101, int ExpDateInt = 20991231, bool IsActive = true);
    public sealed record SaveDrgRuleStdDto(string? Id, string GroupCode, string GroupName, string PayMode = "DRG", string? MdcCode = null, string? AdrgCode = null, decimal RwWeight = 1.0m, decimal BenchmarkFee = 5000m, string? DiagAuxRule = null, string StdVer = "CHS-DRG-3.0", bool IsActive = true);
    public sealed record SaveIvdReagentDto(string? Id, string ReagentCode, string GenericName, string? SpecModel, string? Methodology, string? ApplicableDevice, string? RegCertNo, string? YbIvdCode, string? UdiDi, bool IsActive = true);
    public sealed record SavePayerPolicyDto(string? Id, string FeeTypeCode, string FeeTypeName, string PoolAreaCode, string? ContractUnitName, string? SettlementRulesJson, bool IsActive = true);
    public sealed record SaveEquipmentStdDto(string? Id, string Code, string Name, string? ModelSpec, string DeptId, string? SerialNo, int DepreciationYears = 5, decimal BootHoursMetric = 0m, string? YbImagingDeviceCode = null, string Status = "NORMAL");
    public sealed record SaveWarehouseStdDto(string? Id, string WarehouseCode, string WarehouseName, string WarehouseType = "OPD_PHM", string? TempHumidityReq = null, string? BinLocationsJson = null, string? ServiceDeptScope = null, bool IsActive = true);
    public sealed record SaveCssdPackDto(string? Id, string PackCode, string PackName, string? InstrumentsJson, string SterilizationMethod = "STEAM", int ValidityDays = 180, string? ChargeItemCode = null, bool IsActive = true);
    public sealed record SaveLogisticsItemDto(string? Id, string ItemCode, string ItemName, string Category = "OFFICE", string? Spec = null, string UnitName = "件", decimal RefPrice = 0m, bool IsActive = true);
    public sealed record SaveMedicationRuleDto(string? Id, string RuleCode, string RuleName, string RouteCode, string FrequencyCode, string? DefaultExecTimes = null, string? TcmDecoctionMethod = null, bool SkinTestRequired = false, string? DefaultSolventCode = null, bool IsActive = true);
    public sealed record SaveMicrobiologyDto(string? Id, string MicroCode, string MicroName, string MicroType = "BACTERIA", string? SpecimenScope = null, string? SusceptibilityPanelJson = null, bool IsMdrMonitored = false, bool IsActive = true);
    public sealed record SaveImagingMappingDto(string? Id, string MappingCode, string Modality, string BodyPartName, string? ScanProtocol = null, string? ExamRoomCode = null, string? DicomAeTitle = null, bool IsActive = true);
    public sealed record SaveOrRoomDto(string? Id, string RoomCode, string RoomName, string CleanLevel = "CLASS_1000", string? SupportedAnesthesia = null, string DefaultAsaScope = "I-V", string DefaultIncisionGrade = "I", bool IsActive = true);
    public sealed record SaveNutritionItemDto(string? Id, string ItemCode, string ItemName, string NutritionType = "DIET", string? FormulaDesc = null, decimal KcalPerUnit = 0m, string UnitName = "份", bool IsActive = true);
    public sealed record SaveLocationDto(string? Id, string LocationCode, string LocationName, string LocationLevel = "ROOM", string? ParentLocationId = null, string? RoomType = null, string? QueueScreenId = null, bool IsActive = true);
    public sealed record SaveMedicalGroupDto(string? Id, string GroupCode, string GroupName, string DeptId, string LeaderStaffId, string? MembersJson = null, string? BedScope = null, int EffDateInt = 20240101, bool IsActive = true);
    public sealed record SaveRegLevelDto(string? Id, string RegLevelCode, string RegLevelName, string? ChargeItemCode = null, decimal RegFee = 15m, string PeriodScope = "MORNING,AFTERNOON", string? PoolRatioJson = null, bool IsActive = true);
    public sealed record SaveAccountingSubjectDto(string? Id, string SubjectCode, string SubjectName, string IncomeExpenseType = "REVENUE", string CostCenterType = "DIRECT_MED", decimal AllocationWeight = 1.0m, bool IsActive = true);
    public sealed record SaveSystemTerminalDto(string? Id, string SysCode, string SysName, string EntryType = "BIZ_SYS", string? EndpointOrIp = null, string? AuthScope = null, string? LocationCode = null, bool IsActive = true);
    public sealed record SaveCaCertDto(string? Id, string CertSerial, string StaffId, string CaOrgName = "北京数字认证(BJCA)", string? SealImageUri = null, int ValidDays = 365, string RevokeStatus = "VALID");
    public sealed record SaveYbExtEntityDto(string? Id, string YbCode, string EntityType, string EntityName, string? OrgName = null, bool IsActive = true);

    [HttpGet("summary-phase2")]
    public async Task<ApiResponse<object>> GetPhase2Summary()
    {
        using var conn = _connFactory.CreateConnection();
        var data = new
        {
            YbDiseases = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM mdm_yb_diseases WHERE is_active = 1"),
            DrgDipRules = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM drg_benchmark_rules WHERE is_active = 1"),
            IvdReagents = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM mdm_ivd_reagents WHERE is_active = 1"),
            PayerPolicies = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM mdm_payer_policies WHERE is_active = 1"),
            Equipments = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM eqp_equipments"),
            Warehouses = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM mdm_warehouses WHERE is_active = 1"),
            CssdPacks = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM mdm_cssd_packs WHERE is_active = 1"),
            LogisticsItems = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM mdm_logistics_items WHERE is_active = 1"),
            MedicationRules = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM mdm_medication_rules WHERE is_active = 1"),
            Microbiology = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM mdm_microbiology_dict WHERE is_active = 1"),
            BloodProducts = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM bld_blood_dictionary"),
            ImagingMappings = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM mdm_imaging_mappings WHERE is_active = 1"),
            OrRooms = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM mdm_or_rooms WHERE is_active = 1"),
            NutritionItems = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM mdm_nutrition_items WHERE is_active = 1"),
            Locations = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM mdm_locations WHERE is_active = 1"),
            MedicalGroups = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM mdm_medical_groups WHERE is_active = 1"),
            RegLevels = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM mdm_reg_levels WHERE is_active = 1"),
            AccountingSubjects = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM mdm_accounting_subjects WHERE is_active = 1"),
            SystemTerminals = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM mdm_system_terminals WHERE is_active = 1"),
            CaCertificates = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM mdm_ca_certificates WHERE revoke_status = 'VALID'"),
            YbExtEntities = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM mdm_yb_ext_entities WHERE is_active = 1")
        };
        return ApiResponse<object>.Success(data);
    }

    // 1. 医保病种目录
    [HttpGet("yb-diseases")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetYbDiseases()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"SELECT id AS Id, disease_code AS DiseaseCode, disease_name AS DiseaseName, disease_category AS DiseaseCategory, quota_limit_amount AS QuotaLimitAmount, eff_date_int AS EffDateInt, exp_date_int AS ExpDateInt, is_active AS IsActive FROM mdm_yb_diseases ORDER BY disease_code")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("yb-diseases")]
    public async Task<ApiResponse<object>> SaveYbDisease([FromBody] SaveYbDiseaseDto req)
    {
        if (string.IsNullOrWhiteSpace(req.DiseaseCode) || string.IsNullOrWhiteSpace(req.DiseaseName))
            return ApiResponse<object>.Fail(400, "病种编码与名称不能为空");
        string code = req.DiseaseCode.Trim().ToUpperInvariant();
        if (!(code.StartsWith('M') || code.StartsWith('B') || code.StartsWith('R')))
            return ApiResponse<object>.Fail(400, "医保病种编码须符合国家规范（M开头门慢特 / B开头按病种 / R开头日间手术）");
        long now = SystemTime.UtcNowMs;
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO mdm_yb_diseases (id, disease_code, disease_name, disease_category, quota_limit_amount, eff_date_int, exp_date_int, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @Code, @Name, @Cat, @Quota, @Eff, @Exp, @Act, 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, Code = code, Name = req.DiseaseName.Trim(), Cat = req.DiseaseCategory, Quota = req.QuotaLimitAmount, Eff = req.EffDateInt, Exp = req.ExpDateInt, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        return ApiResponse<object>.Success(new { Id = id, DiseaseCode = code }, "医保病种目录已保存");
    }

    // 2. DRG/DIP 3.0 分组与权重目录
    [HttpGet("drg-rules")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetDrgRules()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"SELECT id AS Id, group_code AS GroupCode, group_name AS GroupName, pay_mode AS PayMode, mdc_code AS MdcCode, adrg_code AS AdrgCode, rw_weight AS RwWeight, benchmark_fee AS BenchmarkFee, diag_aux_rule AS DiagAuxRule, std_ver AS StdVer, is_active AS IsActive FROM drg_benchmark_rules ORDER BY group_code")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("drg-rules")]
    public async Task<ApiResponse<object>> SaveDrgRule([FromBody] SaveDrgRuleStdDto req)
    {
        if (string.IsNullOrWhiteSpace(req.GroupCode) || string.IsNullOrWhiteSpace(req.GroupName))
            return ApiResponse<object>.Fail(400, "DRG/DIP 分组编码与名称不能为空");
        long now = SystemTime.UtcNowMs;
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO drg_benchmark_rules (id, group_code, group_name, pay_mode, mdc_code, adrg_code, rw_weight, benchmark_fee, diag_aux_rule, std_ver, is_active, created_at_ms, updated_at_ms)
            VALUES (@Id, @Code, @Name, @Mode, @Mdc, @Adrg, @Rw, @Fee, @Aux, @Ver, @Act, @Now, @Now)",
            new { Id = id, Code = req.GroupCode.Trim().ToUpperInvariant(), Name = req.GroupName.Trim(), Mode = req.PayMode, Mdc = req.MdcCode, Adrg = req.AdrgCode, Rw = req.RwWeight, Fee = req.BenchmarkFee, Aux = req.DiagAuxRule, Ver = req.StdVer, Act = req.IsActive ? 1 : 0, Now = now });
        return ApiResponse<object>.Success(new { Id = id }, "DRG/DIP 分组目录已保存");
    }

    // 3. 体外诊断试剂 (IVD)
    [HttpGet("ivd-reagents")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetIvdReagents()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"SELECT id AS Id, reagent_code AS ReagentCode, generic_name AS GenericName, spec_model AS SpecModel, methodology AS Methodology, applicable_device AS ApplicableDevice, reg_cert_no AS RegCertNo, yb_ivd_code AS YbIvdCode, udi_di AS UdiDi, is_active AS IsActive FROM mdm_ivd_reagents ORDER BY reagent_code")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("ivd-reagents")]
    public async Task<ApiResponse<object>> SaveIvdReagent([FromBody] SaveIvdReagentDto req)
    {
        if (string.IsNullOrWhiteSpace(req.ReagentCode) || string.IsNullOrWhiteSpace(req.GenericName))
            return ApiResponse<object>.Fail(400, "IVD 试剂编码与通用名不能为空");
        long now = SystemTime.UtcNowMs;
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO mdm_ivd_reagents (id, reagent_code, generic_name, spec_model, methodology, applicable_device, reg_cert_no, yb_ivd_code, udi_di, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @Code, @Name, @Spec, @Method, @Dev, @Cert, @Yb, @Udi, @Act, 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, Code = req.ReagentCode.Trim(), Name = req.GenericName.Trim(), Spec = req.SpecModel, Method = req.Methodology, Dev = req.ApplicableDevice, Cert = req.RegCertNo, Yb = req.YbIvdCode, Udi = req.UdiDi, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        return ApiResponse<object>.Success(new { Id = id }, "体外诊断试剂 IVD 目录已保存");
    }

    // 4. 费别、统筹区与商保协议单位
    [HttpGet("payer-policies")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetPayerPolicies()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"SELECT id AS Id, fee_type_code AS FeeTypeCode, fee_type_name AS FeeTypeName, pool_area_code AS PoolAreaCode, contract_unit_name AS ContractUnitName, settlement_rules_json AS SettlementRulesJson, is_active AS IsActive FROM mdm_payer_policies ORDER BY fee_type_code")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("payer-policies")]
    public async Task<ApiResponse<object>> SavePayerPolicy([FromBody] SavePayerPolicyDto req)
    {
        if (string.IsNullOrWhiteSpace(req.FeeTypeCode) || string.IsNullOrWhiteSpace(req.PoolAreaCode))
            return ApiResponse<object>.Fail(400, "费别代码与统筹区码不能为空");
        long now = SystemTime.UtcNowMs;
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO mdm_payer_policies (id, fee_type_code, fee_type_name, pool_area_code, contract_unit_name, settlement_rules_json, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @Code, @Name, @Pool, @Unit, @Rules, @Act, 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, Code = req.FeeTypeCode.Trim(), Name = req.FeeTypeName.Trim(), Pool = req.PoolAreaCode.Trim(), Unit = req.ContractUnitName, Rules = req.SettlementRulesJson, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        return ApiResponse<object>.Success(new { Id = id }, "费别统筹区与商保政策已保存");
    }

    // 5. 医疗设备与固定资产
    [HttpGet("equipments")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetEquipmentsStd()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"SELECT id AS Id, code AS Code, name AS Name, model_spec AS ModelSpec, dept_id AS DeptId, serial_no AS SerialNo, depreciation_years AS DepreciationYears, boot_hours_metric AS BootHoursMetric, yb_imaging_device_code AS YbImagingDeviceCode, status AS Status FROM eqp_equipments ORDER BY code")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("equipments")]
    public async Task<ApiResponse<object>> SaveEquipmentStd([FromBody] SaveEquipmentStdDto req)
    {
        if (string.IsNullOrWhiteSpace(req.Code) || string.IsNullOrWhiteSpace(req.Name))
            return ApiResponse<object>.Fail(400, "设备资产编号与名称不能为空");
        long now = SystemTime.UtcNowMs;
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO eqp_equipments (id, code, name, model_spec, dept_id, serial_no, depreciation_years, boot_hours_metric, yb_imaging_device_code, status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @Code, @Name, @Model, @Dept, @Sn, @Dep, @Boot, @Yb, @Status, 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, Code = req.Code.Trim(), Name = req.Name.Trim(), Model = req.ModelSpec, Dept = req.DeptId, Sn = req.SerialNo, Dep = req.DepreciationYears, Boot = req.BootHoursMetric, Yb = req.YbImagingDeviceCode, req.Status, Now = now, Actor = ActorId });
        return ApiResponse<object>.Success(new { Id = id }, "医疗设备固定资产主数据已保存");
    }

    // 6. 库房、药房与货位
    [HttpGet("warehouses")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetWarehouses()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"SELECT id AS Id, warehouse_code AS WarehouseCode, warehouse_name AS WarehouseName, warehouse_type AS WarehouseType, temp_humidity_req AS TempHumidityReq, bin_locations_json AS BinLocationsJson, service_dept_scope AS ServiceDeptScope, is_active AS IsActive FROM mdm_warehouses ORDER BY warehouse_code")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("warehouses")]
    public async Task<ApiResponse<object>> SaveWarehouse([FromBody] SaveWarehouseStdDto req)
    {
        long now = SystemTime.UtcNowMs;
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO mdm_warehouses (id, warehouse_code, warehouse_name, warehouse_type, temp_humidity_req, bin_locations_json, service_dept_scope, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @Code, @Name, @Type, @Temp, @Bins, @Scope, @Act, 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, Code = req.WarehouseCode.Trim(), Name = req.WarehouseName.Trim(), Type = req.WarehouseType, Temp = req.TempHumidityReq, Bins = req.BinLocationsJson, Scope = req.ServiceDeptScope, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        return ApiResponse<object>.Success(new { Id = id }, "库房与货位主数据已保存");
    }

    // 7. CSSD 器械包
    [HttpGet("cssd-packs")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetCssdPacks()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"SELECT id AS Id, pack_code AS PackCode, pack_name AS PackName, instruments_json AS InstrumentsJson, sterilization_method AS SterilizationMethod, validity_days AS ValidityDays, charge_item_code AS ChargeItemCode, is_active AS IsActive FROM mdm_cssd_packs ORDER BY pack_code")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("cssd-packs")]
    public async Task<ApiResponse<object>> SaveCssdPack([FromBody] SaveCssdPackDto req)
    {
        long now = SystemTime.UtcNowMs;
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO mdm_cssd_packs (id, pack_code, pack_name, instruments_json, sterilization_method, validity_days, charge_item_code, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @Code, @Name, @Inst, @Method, @Days, @Chg, @Act, 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, Code = req.PackCode.Trim(), Name = req.PackName.Trim(), Inst = req.InstrumentsJson, Method = req.SterilizationMethod, Days = req.ValidityDays, Chg = req.ChargeItemCode, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        return ApiResponse<object>.Success(new { Id = id }, "CSSD 器械包主数据已保存");
    }

    // 8. 总务后勤物资
    [HttpGet("logistics-items")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetLogisticsItems()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"SELECT id AS Id, item_code AS ItemCode, item_name AS ItemName, category AS Category, spec AS Spec, unit_name AS UnitName, ref_price AS RefPrice, is_active AS IsActive FROM mdm_logistics_items ORDER BY item_code")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("logistics-items")]
    public async Task<ApiResponse<object>> SaveLogisticsItem([FromBody] SaveLogisticsItemDto req)
    {
        long now = SystemTime.UtcNowMs;
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO mdm_logistics_items (id, item_code, item_name, category, spec, unit_name, ref_price, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @Code, @Name, @Cat, @Spec, @Unit, @Price, @Act, 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, Code = req.ItemCode.Trim(), Name = req.ItemName.Trim(), Cat = req.Category, req.Spec, Unit = req.UnitName, Price = req.RefPrice, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        return ApiResponse<object>.Success(new { Id = id }, "总务后勤物资已保存");
    }

    // 9. 用药规则与医嘱辅助字典
    [HttpGet("medication-rules")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetMedicationRules()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"SELECT id AS Id, rule_code AS RuleCode, rule_name AS RuleName, route_code AS RouteCode, frequency_code AS FrequencyCode, default_exec_times AS DefaultExecTimes, tcm_decoction_method AS TcmDecoctionMethod, skin_test_required AS SkinTestRequired, default_solvent_code AS DefaultSolventCode, is_active AS IsActive FROM mdm_medication_rules ORDER BY rule_code")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("medication-rules")]
    public async Task<ApiResponse<object>> SaveMedicationRule([FromBody] SaveMedicationRuleDto req)
    {
        long now = SystemTime.UtcNowMs;
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO mdm_medication_rules (id, rule_code, rule_name, route_code, frequency_code, default_exec_times, tcm_decoction_method, skin_test_required, default_solvent_code, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @Code, @Name, @Route, @Freq, @Times, @Decoct, @Skin, @Solvent, @Act, 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, Code = req.RuleCode.Trim(), Name = req.RuleName.Trim(), Route = req.RouteCode, Freq = req.FrequencyCode, Times = req.DefaultExecTimes, Decoct = req.TcmDecoctionMethod, Skin = req.SkinTestRequired ? 1 : 0, Solvent = req.DefaultSolventCode, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        return ApiResponse<object>.Success(new { Id = id }, "用药规则已保存");
    }

    // 10. 检验标本、微生物与药敏字典
    [HttpGet("microbiology")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetMicrobiology()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"SELECT id AS Id, micro_code AS MicroCode, micro_name AS MicroName, micro_type AS MicroType, specimen_scope AS SpecimenScope, susceptibility_panel_json AS SusceptibilityPanelJson, is_mdr_monitored AS IsMdrMonitored, is_active AS IsActive FROM mdm_microbiology_dict ORDER BY micro_code")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("microbiology")]
    public async Task<ApiResponse<object>> SaveMicrobiology([FromBody] SaveMicrobiologyDto req)
    {
        long now = SystemTime.UtcNowMs;
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO mdm_microbiology_dict (id, micro_code, micro_name, micro_type, specimen_scope, susceptibility_panel_json, is_mdr_monitored, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @Code, @Name, @Type, @Scope, @Panel, @Mdr, @Act, 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, Code = req.MicroCode.Trim(), Name = req.MicroName.Trim(), Type = req.MicroType, Scope = req.SpecimenScope, Panel = req.SusceptibilityPanelJson, Mdr = req.IsMdrMonitored ? 1 : 0, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        return ApiResponse<object>.Success(new { Id = id }, "微生物与药敏字典已保存");
    }

    // 11. 影像检查部位与设备映射
    [HttpGet("imaging-mappings")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetImagingMappings()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"SELECT id AS Id, mapping_code AS MappingCode, modality AS Modality, body_part_name AS BodyPartName, scan_protocol AS ScanProtocol, exam_room_code AS ExamRoomCode, dicom_ae_title AS DicomAeTitle, is_active AS IsActive FROM mdm_imaging_mappings ORDER BY modality, mapping_code")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("imaging-mappings")]
    public async Task<ApiResponse<object>> SaveImagingMapping([FromBody] SaveImagingMappingDto req)
    {
        long now = SystemTime.UtcNowMs;
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO mdm_imaging_mappings (id, mapping_code, modality, body_part_name, scan_protocol, exam_room_code, dicom_ae_title, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @Code, @Mod, @Part, @Proto, @Room, @Ae, @Act, 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, Code = req.MappingCode.Trim(), Mod = req.Modality.Trim().ToUpperInvariant(), Part = req.BodyPartName.Trim(), Proto = req.ScanProtocol, Room = req.ExamRoomCode, Ae = req.DicomAeTitle, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        return ApiResponse<object>.Success(new { Id = id }, "影像部位与设备映射已保存");
    }

    // 12. 手术间与手麻字典
    [HttpGet("or-rooms")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetOrRooms()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"SELECT id AS Id, room_code AS RoomCode, room_name AS RoomName, clean_level AS CleanLevel, supported_anesthesia AS SupportedAnesthesia, default_asa_scope AS DefaultAsaScope, default_incision_grade AS DefaultIncisionGrade, is_active AS IsActive FROM mdm_or_rooms ORDER BY room_code")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("or-rooms")]
    public async Task<ApiResponse<object>> SaveOrRoom([FromBody] SaveOrRoomDto req)
    {
        long now = SystemTime.UtcNowMs;
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO mdm_or_rooms (id, room_code, room_name, clean_level, supported_anesthesia, default_asa_scope, default_incision_grade, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @Code, @Name, @Clean, @Anes, @Asa, @Inc, @Act, 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, Code = req.RoomCode.Trim(), Name = req.RoomName.Trim(), Clean = req.CleanLevel, Anes = req.SupportedAnesthesia, Asa = req.DefaultAsaScope, Inc = req.DefaultIncisionGrade, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        return ApiResponse<object>.Success(new { Id = id }, "手术间与手麻字典已保存");
    }

    // 13. 膳食与临床营养制剂
    [HttpGet("nutrition-items")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetNutritionItems()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"SELECT id AS Id, item_code AS ItemCode, item_name AS ItemName, nutrition_type AS NutritionType, formula_desc AS FormulaDesc, kcal_per_unit AS KcalPerUnit, unit_name AS UnitName, is_active AS IsActive FROM mdm_nutrition_items ORDER BY item_code")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("nutrition-items")]
    public async Task<ApiResponse<object>> SaveNutritionItem([FromBody] SaveNutritionItemDto req)
    {
        long now = SystemTime.UtcNowMs;
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO mdm_nutrition_items (id, item_code, item_name, nutrition_type, formula_desc, kcal_per_unit, unit_name, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @Code, @Name, @Type, @Desc, @Kcal, @Unit, @Act, 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, Code = req.ItemCode.Trim(), Name = req.ItemName.Trim(), Type = req.NutritionType, Desc = req.FormulaDesc, Kcal = req.KcalPerUnit, Unit = req.UnitName, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        return ApiResponse<object>.Success(new { Id = id }, "膳食与临床营养制剂已保存");
    }

    // 14. 物理空间位置树
    [HttpGet("locations")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetLocations()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"SELECT id AS Id, location_code AS LocationCode, location_name AS LocationName, location_level AS LocationLevel, parent_location_id AS ParentLocationId, room_type AS RoomType, queue_screen_id AS QueueScreenId, is_active AS IsActive FROM mdm_locations ORDER BY location_code")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("locations")]
    public async Task<ApiResponse<object>> SaveLocation([FromBody] SaveLocationDto req)
    {
        long now = SystemTime.UtcNowMs;
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO mdm_locations (id, location_code, location_name, location_level, parent_location_id, room_type, queue_screen_id, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @Code, @Name, @Lvl, @Parent, @Room, @Screen, @Act, 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, Code = req.LocationCode.Trim(), Name = req.LocationName.Trim(), Lvl = req.LocationLevel, Parent = req.ParentLocationId, Room = req.RoomType, Screen = req.QueueScreenId, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        return ApiResponse<object>.Success(new { Id = id }, "物理空间位置节点已保存");
    }

    // 15. 医疗组 / 主诊医师组
    [HttpGet("medical-groups")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetMedicalGroups()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"SELECT id AS Id, group_code AS GroupCode, group_name AS GroupName, dept_id AS DeptId, leader_staff_id AS LeaderStaffId, members_json AS MembersJson, bed_scope AS BedScope, eff_date_int AS EffDateInt, is_active AS IsActive FROM mdm_medical_groups ORDER BY group_code")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("medical-groups")]
    public async Task<ApiResponse<object>> SaveMedicalGroup([FromBody] SaveMedicalGroupDto req)
    {
        long now = SystemTime.UtcNowMs;
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO mdm_medical_groups (id, group_code, group_name, dept_id, leader_staff_id, members_json, bed_scope, eff_date_int, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @Code, @Name, @Dept, @Leader, @Members, @Beds, @Eff, @Act, 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, Code = req.GroupCode.Trim(), Name = req.GroupName.Trim(), Dept = req.DeptId, Leader = req.LeaderStaffId, Members = req.MembersJson, Beds = req.BedScope, Eff = req.EffDateInt, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        return ApiResponse<object>.Success(new { Id = id }, "医疗组/主诊医师组已保存");
    }

    // 16. 号别与出诊排班模板
    [HttpGet("reg-levels")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetRegLevels()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"SELECT id AS Id, reg_level_code AS RegLevelCode, reg_level_name AS RegLevelName, charge_item_code AS ChargeItemCode, reg_fee AS RegFee, period_scope AS PeriodScope, pool_ratio_json AS PoolRatioJson, is_active AS IsActive FROM mdm_reg_levels ORDER BY reg_fee")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("reg-levels")]
    public async Task<ApiResponse<object>> SaveRegLevel([FromBody] SaveRegLevelDto req)
    {
        long now = SystemTime.UtcNowMs;
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO mdm_reg_levels (id, reg_level_code, reg_level_name, charge_item_code, reg_fee, period_scope, pool_ratio_json, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @Code, @Name, @Chg, @Fee, @Period, @Pool, @Act, 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, Code = req.RegLevelCode.Trim().ToUpperInvariant(), Name = req.RegLevelName.Trim(), Chg = req.ChargeItemCode, Fee = req.RegFee, Period = req.PeriodScope, Pool = req.PoolRatioJson, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        return ApiResponse<object>.Success(new { Id = id }, "号别与号源池模板已保存");
    }

    // 17. 会计科目与成本中心
    [HttpGet("accounting-subjects")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetAccountingSubjects()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"SELECT id AS Id, subject_code AS SubjectCode, subject_name AS SubjectName, income_expense_type AS IncomeExpenseType, cost_center_type AS CostCenterType, allocation_weight AS AllocationWeight, is_active AS IsActive FROM mdm_accounting_subjects ORDER BY subject_code")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("accounting-subjects")]
    public async Task<ApiResponse<object>> SaveAccountingSubject([FromBody] SaveAccountingSubjectDto req)
    {
        long now = SystemTime.UtcNowMs;
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO mdm_accounting_subjects (id, subject_code, subject_name, income_expense_type, cost_center_type, allocation_weight, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @Code, @Name, @IeType, @CcType, @Wt, @Act, 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, Code = req.SubjectCode.Trim(), Name = req.SubjectName.Trim(), IeType = req.IncomeExpenseType, CcType = req.CostCenterType, Wt = req.AllocationWeight, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        return ApiResponse<object>.Success(new { Id = id }, "会计科目与成本中心配置已保存");
    }

    // 18. 业务系统与终端设备注册表
    [HttpGet("system-terminals")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetSystemTerminals()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"SELECT id AS Id, sys_code AS SysCode, sys_name AS SysName, entry_type AS EntryType, endpoint_or_ip AS EndpointOrIp, auth_scope AS AuthScope, location_code AS LocationCode, is_active AS IsActive FROM mdm_system_terminals ORDER BY entry_type, sys_code")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("system-terminals")]
    public async Task<ApiResponse<object>> SaveSystemTerminal([FromBody] SaveSystemTerminalDto req)
    {
        long now = SystemTime.UtcNowMs;
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO mdm_system_terminals (id, sys_code, sys_name, entry_type, endpoint_or_ip, auth_scope, location_code, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @Code, @Name, @Type, @Ep, @Auth, @Loc, @Act, 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, Code = req.SysCode.Trim(), Name = req.SysName.Trim(), Type = req.EntryType, Ep = req.EndpointOrIp, Auth = req.AuthScope, Loc = req.LocationCode, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        return ApiResponse<object>.Success(new { Id = id }, "业务系统与终端注册信息已保存");
    }

    // 19. CA 证书与医保扩展人员/定点药店
    [HttpGet("ca-certificates")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetCaCertificates()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"SELECT id AS Id, cert_serial AS CertSerial, staff_id AS StaffId, ca_org_name AS CaOrgName, seal_image_uri AS SealImageUri, issued_at_ms AS IssuedAtMs, expires_at_ms AS ExpiresAtMs, revoke_status AS RevokeStatus FROM mdm_ca_certificates ORDER BY issued_at_ms DESC")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("ca-certificates")]
    public async Task<ApiResponse<object>> SaveCaCert([FromBody] SaveCaCertDto req)
    {
        long now = SystemTime.UtcNowMs;
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO mdm_ca_certificates (id, cert_serial, staff_id, ca_org_name, seal_image_uri, issued_at_ms, expires_at_ms, revoke_status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @Serial, @Staff, @Org, @Seal, @Now, @Exp, @Status, 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, Serial = req.CertSerial.Trim(), Staff = req.StaffId.Trim(), Org = req.CaOrgName, Seal = req.SealImageUri, Now = now, Exp = now + req.ValidDays * 86_400_000L, Status = req.RevokeStatus, Actor = ActorId });
        return ApiResponse<object>.Success(new { Id = id }, "医护 CA 数字证书已保存");
    }

    [HttpGet("yb-ext-entities")]
    public async Task<ApiResponse<IReadOnlyList<object>>> GetYbExtEntities()
    {
        using var conn = _connFactory.CreateConnection();
        var rows = (await conn.QueryAsync<dynamic>(@"SELECT id AS Id, yb_code AS YbCode, entity_type AS EntityType, entity_name AS EntityName, org_name AS OrgName, is_active AS IsActive FROM mdm_yb_ext_entities ORDER BY entity_type, yb_code")).ToList<object>();
        return ApiResponse<IReadOnlyList<object>>.Success(rows);
    }

    [HttpPost("yb-ext-entities")]
    public async Task<ApiResponse<object>> SaveYbExtEntity([FromBody] SaveYbExtEntityDto req)
    {
        if (string.IsNullOrWhiteSpace(req.YbCode) || string.IsNullOrWhiteSpace(req.EntityName))
            return ApiResponse<object>.Fail(400, "医保编码与名称不能为空");
        string code = req.YbCode.Trim().ToUpperInvariant();
        if (req.EntityType == "PHARMACIST" && (!code.StartsWith('Y') || code.Length != 13))
            return ApiResponse<object>.Fail(400, "医保药师编码须为 Y 开头 13 位标准编码");
        if (req.EntityType == "RETAIL_PHARMACY" && (!code.StartsWith('P') || code.Length != 12))
            return ApiResponse<object>.Fail(400, "医保定点零售药店编码须为 P 开头 12 位标准编码");

        long now = SystemTime.UtcNowMs;
        string id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("D") : req.Id.Trim();
        using var conn = _connFactory.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO mdm_yb_ext_entities (id, yb_code, entity_type, entity_name, org_name, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@Id, @Code, @Type, @Name, @Org, @Act, 1, @Now, @Actor, @Now, @Actor, 1)",
            new { Id = id, Code = code, Type = req.EntityType, Name = req.EntityName.Trim(), Org = req.OrgName, Act = req.IsActive ? 1 : 0, Now = now, Actor = ActorId });
        return ApiResponse<object>.Success(new { Id = id, YbCode = code }, "医保扩展人员/定点药店已保存");
    }

    // ===================== 统一编辑 / 新增 / 启停治理接口（全 34 域通用） =====================

    public sealed record UpsertMdmRecordDto(
        string DomainKey,
        string? Id,
        string Code,
        string Name,
        string? StdCode = null,
        string? CategoryOrSpec = null,
        string? ExtraAttributes = null);

    public sealed record ToggleMdmRecordDto(string DomainKey, string Id);

    [HttpPost("upsert")]
    public async Task<ApiResponse<object>> UpsertUnifiedMdm([FromBody] UpsertMdmRecordDto req)
    {
        if (string.IsNullOrWhiteSpace(req.DomainKey) || string.IsNullOrWhiteSpace(req.Code) || string.IsNullOrWhiteSpace(req.Name))
            return ApiResponse<object>.Fail(400, "主数据域、业务编码与标准名称不能为空");

        string key = req.DomainKey.Trim().ToLowerInvariant();
        string code = req.Code.Trim();
        string name = req.Name.Trim();
        string std = req.StdCode?.Trim() ?? string.Empty;
        string cat = req.CategoryOrSpec?.Trim() ?? string.Empty;
        string ext = req.ExtraAttributes?.Trim() ?? string.Empty;
        bool isUpdate = !string.IsNullOrWhiteSpace(req.Id);
        string id = isUpdate ? req.Id!.Trim() : Guid.NewGuid().ToString("D");
        long now = SystemTime.UtcNowMs;
        string actor = ActorId;

        using var conn = _connFactory.CreateConnection();
        int affected;

        switch (key)
        {
            case "organizations":
            {
                string uscc = string.IsNullOrWhiteSpace(std) ? "12110000400001999X" : std.ToUpperInvariant();
                if (uscc.Length != 18)
                    return ApiResponse<object>.Fail(400, "统一社会信用代码须符合 GB 32100-2015（18位）规范");
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE mdm_organizations SET org_code=@Code, org_name=@Name, uscc=@Uscc, org_level=@Cat, yb_org_code=@Ext, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Uscc = uscc, Cat = string.IsNullOrEmpty(cat) ? "三级甲等" : cat, Ext = ext, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO mdm_organizations (id, org_code, uscc, yb_org_code, org_name, campus_tag, org_level, org_category_code, admin_region_code, practice_license_no, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Code, @Uscc, @Ext, @Name, '主院区', @Cat, 'A100', '110101', '', 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = code, Name = name, Uscc = uscc, Cat = string.IsNullOrEmpty(cat) ? "三级甲等" : cat, Ext = ext, Now = now, Actor = actor });
                break;
            }
            case "departments":
            {
                string deptCat = string.IsNullOrEmpty(cat) ? "CLINICAL" : cat;
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE sys_departments SET dept_code=@Code, dept_name=@Name, std_subject_code=@Std, dept_category=@Cat, cost_center_code=@Ext, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Std = std, Cat = deptCat, Ext = ext, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO sys_departments (id, campus_id, dept_code, dept_name, dept_category, std_subject_code, cost_center_code, is_allow_appointment, opd_flag, ipd_flag, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, 'CAMPUS-01', @Code, @Name, @Cat, @Std, @Ext, 1, 1, 1, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = code, Name = name, Std = std, Cat = deptCat, Ext = ext, Now = now, Actor = actor });
                break;
            }
            case "beds":
            {
                var parts = code.Split('/', 2, StringSplitOptions.TrimEntries);
                string wardId = parts.Length == 2 && !string.IsNullOrEmpty(parts[0]) ? parts[0] : "WARD-01";
                string bedCode = parts.Length == 2 ? parts[1] : code;
                string bedType = string.IsNullOrEmpty(cat) ? "编制床位" : cat;
                string genderLimit = string.IsNullOrEmpty(std) ? "UNLIMITED" : std;
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE ipd_beds SET ward_id=@WardId, bed_code=@BedCode, room_code=@Room, bed_type=@BedType, gender_limit=@Gender, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, WardId = wardId, BedCode = bedCode, Room = name, BedType = bedType, Gender = genderLimit, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO ipd_beds (id, campus_id, ward_id, room_code, bed_code, bed_type, daily_fee, bed_status, charge_level, gender_limit, is_authorized_bed, is_open_bed, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, 'CAMPUS-01', @WardId, @Room, @BedCode, @BedType, 50, 0, '普通', @Gender, 1, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, WardId = wardId, Room = name, BedCode = bedCode, BedType = bedType, Gender = genderLimit, Now = now, Actor = actor });
                break;
            }
            case "staff":
            {
                string staffCat = string.IsNullOrEmpty(cat) ? "DOCTOR" : cat;
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE hr_employees SET employee_no=@Code, full_name=@Name, yb_staff_code=@Std, staff_category=@Cat, practice_cert_no=@Ext, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Std = std, Cat = staffCat, Ext = ext, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO hr_employees (id, employee_no, full_name, hire_date_int, staff_category, yb_staff_code, practice_cert_no, employment_status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Code, @Name, 20260101, @Cat, @Std, @Ext, 'ACTIVE', 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = code, Name = name, Std = std, Cat = staffCat, Ext = ext, Now = now, Actor = actor });
                if (isUpdate && affected > 0)
                {
                    await conn.ExecuteAsync(@"UPDATE sys_users SET display_name=@Name, employee_no=@Code, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=(SELECT user_id FROM hr_employees WHERE id=@Id) OR employee_no=@Code",
                        new { Id = id, Code = code, Name = name, Now = now, Actor = actor });
                }
                break;
            }
            case "empi":
            {
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE pat_patients SET medical_record_no=@Code, full_name=@Name, region_code=@Std, blood_type_code=@Cat, current_address=@Ext, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Std = std, Cat = cat, Ext = ext, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO pat_patients (id, medical_record_no, full_name, gender_code, birth_date_int, id_card_type, id_card_hash, is_temp_emergency, region_code, blood_type_code, current_address, is_active, merge_status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Code, @Name, '1', 19900101, 'ID_CARD', @Code, 0, @Std, @Cat, @Ext, 1, 'ACTIVE', 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = code, Name = name, Std = std, Cat = cat, Ext = ext, Now = now, Actor = actor });
                break;
            }
            case "vendors":
            {
                if (!string.IsNullOrEmpty(std) && std.Length != 18)
                    return ApiResponse<object>.Fail(400, "供应商统一社会信用代码须为18位");
                string entityType = string.IsNullOrEmpty(cat) ? "DRUG_DISTRIBUTOR" : cat;
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE inv_suppliers SET supplier_code=@Code, supplier_name=@Name, uscc=@Std, entity_type=@Cat, gsp_cert_no=@Ext, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Std = std, Cat = entityType, Ext = ext, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO inv_suppliers (id, supplier_code, supplier_name, uscc, entity_type, license_no, license_expiry_int, gsp_cert_no, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Code, @Name, @Std, @Cat, @Ext, 20991231, @Ext, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = code, Name = name, Std = std, Cat = entityType, Ext = ext, Now = now, Actor = actor });
                break;
            }
            case "drugs":
            {
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE inv_drugs SET drug_code=@Code, drug_name=@Name, generic_name=@Name, yb_drug_code=@Std, spec=@Cat, atc_code=@Ext, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Std = std, Cat = cat, Ext = ext, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO inv_drugs (id, drug_code, drug_name, generic_name, spec, unit_name, conversion_ratio, price, cost_price, antibiotic_level, is_narcotic, is_skin_test_required, atc_code, yb_drug_code, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Code, @Name, @Name, @Cat, '盒', 1, 0, 0, 0, 0, 0, @Ext, @Std, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = code, Name = name, Std = std, Cat = cat, Ext = ext, Now = now, Actor = actor });
                break;
            }
            case "consumables":
            {
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE csm_consumables SET code=@Code, name=@Name, udi_di=@Std, spec=@Cat, yb_consumable_code=@Ext, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Std = std, Cat = cat, Ext = ext, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO csm_consumables (id, code, name, spec, unit, category, price, udi_di, yb_consumable_code, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Code, @Name, @Cat, '个', '一般耗材', 0, @Std, @Ext, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = code, Name = name, Std = std, Cat = cat, Ext = ext, Now = now, Actor = actor });
                break;
            }
            case "order_items":
            {
                string orderCat = string.IsNullOrEmpty(cat) ? "EXAM" : cat;
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE cfg_order_items SET order_item_code=@Code, order_item_name=@Name, yb_service_code=@Std, order_category=@Cat, sample_or_body_part=@Ext, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Std = std, Cat = orderCat, Ext = ext, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO cfg_order_items (id, order_item_code, order_item_name, order_category, sample_or_body_part, yb_service_code, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Code, @Name, @Cat, @Ext, @Std, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = code, Name = name, Std = std, Cat = orderCat, Ext = ext, Now = now, Actor = actor });
                break;
            }
            case "charge_mappings":
            {
                string insType = string.IsNullOrEmpty(cat) ? "BASIC_MEDICAL" : cat;
                string ybCode = string.IsNullOrEmpty(std) ? code : std;
                string insGrade = string.IsNullOrEmpty(ext) ? "甲类" : ext;
                if (isUpdate)
                {
                    affected = await conn.ExecuteAsync(@"UPDATE cfg_charge_item_mappings SET yb_catalog_code=@Std, ins_type=@Cat, ins_grade=@Ext, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Std = ybCode, Cat = insType, Ext = insGrade, Now = now, Actor = actor });
                }
                else
                {
                    var priceId = await conn.ExecuteScalarAsync<string?>("SELECT id FROM cfg_price_items WHERE item_code = @Code LIMIT 1", new { Code = code }) ?? code;
                    affected = await conn.ExecuteAsync(@"INSERT INTO cfg_charge_item_mappings (id, charge_item_id, ins_type, eff_date_int, exp_date_int, yb_catalog_code, ins_grade, self_pay_ratio, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @PriceId, @Cat, 20260101, 20991231, @Std, @Ext, 0, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, PriceId = priceId, Cat = insType, Std = ybCode, Ext = insGrade, Now = now, Actor = actor });
                }
                break;
            }
            case "icd10":
            {
                string ver = string.IsNullOrEmpty(std) ? "ICD10-YB20" : std;
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE cfg_icd10_diagnoses SET icd_code=@Code, disease_name=@Name, std_ver=@Std, category_name=@Cat, chapter_code=@Ext, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Std = ver, Cat = cat, Ext = ext, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO cfg_icd10_diagnoses (id, std_ver, icd_code, disease_name, category_name, chapter_code, gender_exclusive, is_chronic_special, is_infectious_report, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Std, @Code, @Name, @Cat, @Ext, 'ALL', 0, 0, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = code, Name = name, Std = ver, Cat = cat, Ext = ext, Now = now, Actor = actor });
                break;
            }
            case "tcm":
            {
                string tcmType = string.IsNullOrEmpty(cat) ? "DISEASE" : cat;
                string ver = string.IsNullOrEmpty(std) ? "TCM-GB95" : std;
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE cfg_tcm_diagnoses SET tcm_code=@Code, tcm_name=@Name, std_ver=@Std, tcm_type=@Cat, pinyin_code=@Ext, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Std = ver, Cat = tcmType, Ext = ext, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO cfg_tcm_diagnoses (id, std_ver, tcm_code, tcm_name, tcm_type, pinyin_code, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Std, @Code, @Name, @Cat, @Ext, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = code, Name = name, Std = ver, Cat = tcmType, Ext = ext, Now = now, Actor = actor });
                break;
            }
            case "icd9":
            {
                string ver = string.IsNullOrEmpty(std) ? "ICD9-CM3-YB" : std;
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE cfg_icd9_operations SET icd9_code=@Code, operation_name=@Name, std_ver=@Std, default_incision_grade=@Ext, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Std = ver, Ext = string.IsNullOrEmpty(ext) ? "I" : ext, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO cfg_icd9_operations (id, std_ver, icd9_code, operation_name, surgery_level, is_minimally_invasive, default_incision_grade, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Std, @Code, @Name, 2, 1, @Ext, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = code, Name = name, Std = ver, Ext = string.IsNullOrEmpty(ext) ? "I" : ext, Now = now, Actor = actor });
                break;
            }
            case "exam_std":
            {
                string kind = string.IsNullOrEmpty(cat) ? "LAB" : cat;
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE cfg_exam_std_items SET std_code=@Code, std_name=@Name, loinc_code=@Std, item_kind=@Cat, ref_range=@Ext, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Std = std, Cat = kind, Ext = ext, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO cfg_exam_std_items (id, std_code, std_name, item_kind, loinc_code, ref_range, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Code, @Name, @Cat, @Std, @Ext, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = code, Name = name, Std = std, Cat = kind, Ext = ext, Now = now, Actor = actor });
                break;
            }
            case "yb_diseases":
            {
                string upper = code.ToUpperInvariant();
                if (!upper.StartsWith('M') && !upper.StartsWith('B') && !upper.StartsWith('R'))
                    return ApiResponse<object>.Fail(400, "国家医保病种编码须以 M(门慢特)/B(按病种)/R(日间手术) 开头");
                string dcat = string.IsNullOrEmpty(std) ? "CHRONIC_SPECIAL" : std;
                decimal quota = decimal.TryParse(cat, out var q) ? q : 0m;
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE mdm_yb_diseases SET disease_code=@Code, disease_name=@Name, disease_category=@Std, quota_limit_amount=@Quota, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = upper, Name = name, Std = dcat, Quota = quota, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO mdm_yb_diseases (id, disease_code, disease_name, disease_category, quota_limit_amount, eff_date_int, exp_date_int, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Code, @Name, @Std, @Quota, 20260101, 20991231, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = upper, Name = name, Std = dcat, Quota = quota, Now = now, Actor = actor });
                break;
            }
            case "drg_rules":
            {
                string ver = string.IsNullOrEmpty(std) ? "CHS-DRG-3.0" : std;
                decimal rw = decimal.TryParse(cat, out var w) ? w : 1.0m;
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE drg_benchmark_rules SET group_code=@Code, group_name=@Name, std_ver=@Std, rw_weight=@Rw, diag_aux_rule=@Ext, updated_at_ms=@Now WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Std = ver, Rw = rw, Ext = ext, Now = now })
                    : await conn.ExecuteAsync(@"INSERT INTO drg_benchmark_rules (id, group_code, group_name, pay_mode, rw_weight, benchmark_fee, diag_aux_rule, std_ver, is_active, created_at_ms, updated_at_ms) VALUES (@Id, @Code, @Name, 'DRG', @Rw, 10000, @Ext, @Std, 1, @Now, @Now)",
                        new { Id = id, Code = code, Name = name, Std = ver, Rw = rw, Ext = ext, Now = now });
                break;
            }
            case "ivd_reagents":
            {
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE mdm_ivd_reagents SET reagent_code=@Code, generic_name=@Name, yb_ivd_code=@Std, spec_model=@Cat, applicable_device=@Ext, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Std = std, Cat = cat, Ext = ext, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO mdm_ivd_reagents (id, reagent_code, generic_name, spec_model, methodology, applicable_device, yb_ivd_code, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Code, @Name, @Cat, '化学发光', @Ext, @Std, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = code, Name = name, Std = std, Cat = cat, Ext = ext, Now = now, Actor = actor });
                break;
            }
            case "payer_policies":
            {
                string pool = string.IsNullOrEmpty(std) ? "110100" : std;
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE mdm_payer_policies SET fee_type_code=@Code, fee_type_name=@Name, pool_area_code=@Std, contract_unit_name=@Cat, settlement_rules_json=@Ext, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Std = pool, Cat = cat, Ext = ext, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO mdm_payer_policies (id, fee_type_code, fee_type_name, pool_area_code, contract_unit_name, settlement_rules_json, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Code, @Name, @Std, @Cat, @Ext, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = code, Name = name, Std = pool, Cat = cat, Ext = ext, Now = now, Actor = actor });
                break;
            }
            case "equipments":
            {
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE eqp_equipments SET code=@Code, name=@Name, yb_imaging_device_code=@Std, model_spec=@Cat, serial_no=@Ext, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Std = std, Cat = cat, Ext = ext, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO eqp_equipments (id, code, name, model_spec, dept_id, serial_no, depreciation_years, yb_imaging_device_code, status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Code, @Name, @Cat, 'DEPT-OPD-01', @Ext, 8, @Std, 'NORMAL', 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = code, Name = name, Std = std, Cat = cat, Ext = ext, Now = now, Actor = actor });
                break;
            }
            case "warehouses":
            {
                string wtype = string.IsNullOrEmpty(std) ? "OUTPATIENT_PHARMACY" : std;
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE mdm_warehouses SET warehouse_code=@Code, warehouse_name=@Name, warehouse_type=@Std, temp_humidity_req=@Cat, service_dept_scope=@Ext, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Std = wtype, Cat = cat, Ext = ext, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO mdm_warehouses (id, warehouse_code, warehouse_name, warehouse_type, temp_humidity_req, service_dept_scope, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Code, @Name, @Std, @Cat, @Ext, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = code, Name = name, Std = wtype, Cat = cat, Ext = ext, Now = now, Actor = actor });
                break;
            }
            case "cssd_packs":
            {
                string method = string.IsNullOrEmpty(std) ? "HIGH_TEMP" : std;
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE mdm_cssd_packs SET pack_code=@Code, pack_name=@Name, sterilization_method=@Std, charge_item_code=@Cat, instruments_json=@Ext, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Std = method, Cat = cat, Ext = ext, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO mdm_cssd_packs (id, pack_code, pack_name, instruments_json, sterilization_method, validity_days, charge_item_code, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Code, @Name, @Ext, @Std, 180, @Cat, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = code, Name = name, Std = method, Cat = cat, Ext = ext, Now = now, Actor = actor });
                break;
            }
            case "logistics":
            {
                string lcat = string.IsNullOrEmpty(std) ? "OFFICE" : std;
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE mdm_logistics_items SET item_code=@Code, item_name=@Name, category=@Std, spec=@Cat, unit_name=@Ext, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Std = lcat, Cat = cat, Ext = string.IsNullOrEmpty(ext) ? "件" : ext, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO mdm_logistics_items (id, item_code, item_name, category, spec, unit_name, ref_price, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Code, @Name, @Std, @Cat, @Ext, 0, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = code, Name = name, Std = lcat, Cat = cat, Ext = string.IsNullOrEmpty(ext) ? "件" : ext, Now = now, Actor = actor });
                break;
            }
            case "med_rules":
            {
                string route = string.IsNullOrEmpty(std) ? "PO" : std;
                string freq = string.IsNullOrEmpty(cat) ? "BID" : cat;
                string times = string.IsNullOrEmpty(ext) ? "08:00,20:00" : ext;
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE mdm_medication_rules SET rule_code=@Code, rule_name=@Name, route_code=@Std, frequency_code=@Cat, default_exec_times=@Ext, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Std = route, Cat = freq, Ext = times, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO mdm_medication_rules (id, rule_code, rule_name, route_code, frequency_code, default_exec_times, skin_test_required, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Code, @Name, @Std, @Cat, @Ext, 0, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = code, Name = name, Std = route, Cat = freq, Ext = times, Now = now, Actor = actor });
                break;
            }
            case "microbiology":
            {
                string mtype = string.IsNullOrEmpty(std) ? "BACTERIA" : std;
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE mdm_microbiology_dict SET micro_code=@Code, micro_name=@Name, micro_type=@Std, specimen_scope=@Cat, susceptibility_panel_json=@Ext, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Std = mtype, Cat = cat, Ext = ext, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO mdm_microbiology_dict (id, micro_code, micro_name, micro_type, specimen_scope, susceptibility_panel_json, is_mdr_monitored, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Code, @Name, @Std, @Cat, @Ext, 1, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = code, Name = name, Std = mtype, Cat = cat, Ext = ext, Now = now, Actor = actor });
                break;
            }
            case "imaging_map":
            {
                string mod = string.IsNullOrEmpty(std) ? "CT" : std;
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE mdm_imaging_mappings SET mapping_code=@Code, body_part_name=@Name, modality=@Std, dicom_ae_title=@Cat, scan_protocol=@Ext, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Std = mod, Cat = cat, Ext = ext, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO mdm_imaging_mappings (id, mapping_code, modality, body_part_name, scan_protocol, dicom_ae_title, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Code, @Std, @Name, @Ext, @Cat, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = code, Name = name, Std = mod, Cat = cat, Ext = ext, Now = now, Actor = actor });
                break;
            }
            case "or_rooms":
            {
                string clean = string.IsNullOrEmpty(std) ? "百级" : std;
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE mdm_or_rooms SET room_code=@Code, room_name=@Name, clean_level=@Std, supported_anesthesia=@Cat, default_incision_grade=@Ext, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Std = clean, Cat = cat, Ext = string.IsNullOrEmpty(ext) ? "I类" : ext, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO mdm_or_rooms (id, room_code, room_name, clean_level, supported_anesthesia, default_asa_scope, default_incision_grade, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Code, @Name, @Std, @Cat, 'I-IV', @Ext, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = code, Name = name, Std = clean, Cat = cat, Ext = string.IsNullOrEmpty(ext) ? "I类" : ext, Now = now, Actor = actor });
                break;
            }
            case "nutrition":
            {
                string ntype = string.IsNullOrEmpty(std) ? "THERAPEUTIC_DIET" : std;
                decimal kcal = decimal.TryParse(ext, out var kc) ? kc : 400m;
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE mdm_nutrition_items SET item_code=@Code, item_name=@Name, nutrition_type=@Std, formula_desc=@Cat, kcal_per_unit=@Kcal, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Std = ntype, Cat = cat, Kcal = kcal, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO mdm_nutrition_items (id, item_code, item_name, nutrition_type, formula_desc, kcal_per_unit, unit_name, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Code, @Name, @Std, @Cat, @Kcal, '份', 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = code, Name = name, Std = ntype, Cat = cat, Kcal = kcal, Now = now, Actor = actor });
                break;
            }
            case "locations":
            {
                string lvl = string.IsNullOrEmpty(std) ? "ROOM" : std;
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE mdm_locations SET location_code=@Code, location_name=@Name, location_level=@Std, queue_screen_id=@Cat, parent_location_id=@Ext, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Std = lvl, Cat = cat, Ext = ext, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO mdm_locations (id, location_code, location_name, location_level, parent_location_id, room_type, queue_screen_id, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Code, @Name, @Std, @Ext, 'CLINIC', @Cat, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = code, Name = name, Std = lvl, Cat = cat, Ext = ext, Now = now, Actor = actor });
                break;
            }
            case "med_groups":
            {
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE mdm_medical_groups SET group_code=@Code, group_name=@Name, dept_id=@Std, bed_scope=@Cat, members_json=@Ext, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Std = std, Cat = cat, Ext = ext, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO mdm_medical_groups (id, group_code, group_name, dept_id, members_json, bed_scope, eff_date_int, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Code, @Name, @Std, @Ext, @Cat, 20260101, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = code, Name = name, Std = std, Cat = cat, Ext = ext, Now = now, Actor = actor });
                break;
            }
            case "reg_levels":
            {
                decimal fee = decimal.TryParse(cat, out var rf) ? rf : 15m;
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE mdm_reg_levels SET reg_level_code=@Code, reg_level_name=@Name, charge_item_code=@Std, reg_fee=@Fee, pool_ratio_json=@Ext, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Std = std, Fee = fee, Ext = ext, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO mdm_reg_levels (id, reg_level_code, reg_level_name, charge_item_code, reg_fee, period_scope, pool_ratio_json, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Code, @Name, @Std, @Fee, 'AM/PM', @Ext, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = code, Name = name, Std = std, Fee = fee, Ext = ext, Now = now, Actor = actor });
                break;
            }
            case "acc_subjects":
            {
                string ieType = string.IsNullOrEmpty(std) ? "INCOME" : std;
                string ccType = string.IsNullOrEmpty(cat) ? "CLINICAL" : cat;
                decimal aw = decimal.TryParse(ext, out var w) ? w : 1.0m;
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE mdm_accounting_subjects SET subject_code=@Code, subject_name=@Name, income_expense_type=@Std, cost_center_type=@Cat, allocation_weight=@Aw, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Std = ieType, Cat = ccType, Aw = aw, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO mdm_accounting_subjects (id, subject_code, subject_name, income_expense_type, cost_center_type, allocation_weight, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Code, @Name, @Std, @Cat, @Aw, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = code, Name = name, Std = ieType, Cat = ccType, Aw = aw, Now = now, Actor = actor });
                break;
            }
            case "sys_terminals":
            {
                string etype = string.IsNullOrEmpty(std) ? "SUBSYSTEM" : std;
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE mdm_system_terminals SET sys_code=@Code, sys_name=@Name, entry_type=@Std, endpoint_or_ip=@Cat, auth_scope=@Ext, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Std = etype, Cat = cat, Ext = ext, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO mdm_system_terminals (id, sys_code, sys_name, entry_type, endpoint_or_ip, auth_scope, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Code, @Name, @Std, @Cat, @Ext, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = code, Name = name, Std = etype, Cat = cat, Ext = ext, Now = now, Actor = actor });
                break;
            }
            case "ca_certs":
            {
                string staffId = string.IsNullOrEmpty(std) ? "DOC-001" : std;
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE mdm_ca_certificates SET cert_serial=@Code, ca_org_name=@Name, staff_id=@Std, seal_image_uri=@Ext, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = code, Name = name, Std = staffId, Ext = ext, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO mdm_ca_certificates (id, cert_serial, staff_id, ca_org_name, seal_image_uri, issued_at_ms, expires_at_ms, revoke_status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Code, @Std, @Name, @Ext, @Now, @Exp, 'VALID', 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = code, Name = name, Std = staffId, Ext = ext, Now = now, Exp = now + 31536000000L, Actor = actor });
                break;
            }
            case "yb_ext":
            {
                string upper = code.ToUpperInvariant();
                if (!(upper.StartsWith('Y') && upper.Length == 13) && !(upper.StartsWith('P') && upper.Length == 12))
                    return ApiResponse<object>.Fail(400, "国家医保药师须以 Y 开头 13 位，定点零售药店须以 P 开头 12 位");
                string etype = string.IsNullOrEmpty(std) ? (upper.StartsWith('Y') ? "PHARMACIST" : "DESIGNATED_PHARMACY") : std;
                affected = isUpdate
                    ? await conn.ExecuteAsync(@"UPDATE mdm_yb_ext_entities SET yb_code=@Code, entity_name=@Name, entity_type=@Std, org_name=@Cat, updated_at_ms=@Now, updated_by=@Actor, version_no=version_no+1 WHERE id=@Id",
                        new { Id = id, Code = upper, Name = name, Std = etype, Cat = cat, Now = now, Actor = actor })
                    : await conn.ExecuteAsync(@"INSERT INTO mdm_yb_ext_entities (id, yb_code, entity_type, entity_name, org_name, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no) VALUES (@Id, @Code, @Std, @Name, @Cat, 1, 1, @Now, @Actor, @Now, @Actor, 1)",
                        new { Id = id, Code = upper, Name = name, Std = etype, Cat = cat, Now = now, Actor = actor });
                break;
            }
            default:
                return ApiResponse<object>.Fail(400, $"不支持的主数据域：{req.DomainKey}");
        }

        if (isUpdate && affected == 0)
            return ApiResponse<object>.Fail(404, "未找到待修改的主数据记录");

        return ApiResponse<object>.Success(new { Id = id, DomainKey = key, Updated = isUpdate },
            isUpdate ? "主数据修改已保存" : "主数据新增已保存");
    }

    [HttpPost("toggle")]
    public async Task<ApiResponse<object>> ToggleUnifiedMdm([FromBody] ToggleMdmRecordDto req)
    {
        if (string.IsNullOrWhiteSpace(req.DomainKey) || string.IsNullOrWhiteSpace(req.Id))
            return ApiResponse<object>.Fail(400, "主数据域与记录 ID 不能为空");

        string key = req.DomainKey.Trim().ToLowerInvariant();
        string id = req.Id.Trim();
        long now = SystemTime.UtcNowMs;
        string actor = ActorId;

        string? sql = key switch
        {
            "organizations" => "UPDATE mdm_organizations SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "departments" => "UPDATE sys_departments SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "beds" => "UPDATE ipd_beds SET is_open_bed = CASE WHEN is_open_bed = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "staff" => "UPDATE hr_employees SET employment_status = CASE WHEN employment_status = 'ACTIVE' THEN 'INACTIVE' ELSE 'ACTIVE' END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "empi" => "UPDATE pat_patients SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "vendors" => "UPDATE inv_suppliers SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "drugs" => "UPDATE inv_drugs SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "consumables" => "UPDATE csm_consumables SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "order_items" => "UPDATE cfg_order_items SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "charge_mappings" => "UPDATE cfg_charge_item_mappings SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "icd10" => "UPDATE cfg_icd10_diagnoses SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "tcm" => "UPDATE cfg_tcm_diagnoses SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "icd9" => "UPDATE cfg_icd9_operations SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "exam_std" => "UPDATE cfg_exam_std_items SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "yb_diseases" => "UPDATE mdm_yb_diseases SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "drg_rules" => "UPDATE drg_benchmark_rules SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now WHERE id = @Id",
            "ivd_reagents" => "UPDATE mdm_ivd_reagents SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "payer_policies" => "UPDATE mdm_payer_policies SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "equipments" => "UPDATE eqp_equipments SET status = CASE WHEN status = 'NORMAL' THEN 'OUT_OF_SERVICE' ELSE 'NORMAL' END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "warehouses" => "UPDATE mdm_warehouses SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "cssd_packs" => "UPDATE mdm_cssd_packs SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "logistics" => "UPDATE mdm_logistics_items SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "med_rules" => "UPDATE mdm_medication_rules SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "microbiology" => "UPDATE mdm_microbiology_dict SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "imaging_map" => "UPDATE mdm_imaging_mappings SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "or_rooms" => "UPDATE mdm_or_rooms SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "nutrition" => "UPDATE mdm_nutrition_items SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "locations" => "UPDATE mdm_locations SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "med_groups" => "UPDATE mdm_medical_groups SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "reg_levels" => "UPDATE mdm_reg_levels SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "acc_subjects" => "UPDATE mdm_accounting_subjects SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "sys_terminals" => "UPDATE mdm_system_terminals SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "ca_certs" => "UPDATE mdm_ca_certificates SET revoke_status = CASE WHEN revoke_status = 'VALID' THEN 'REVOKED' ELSE 'VALID' END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            "yb_ext" => "UPDATE mdm_yb_ext_entities SET is_active = CASE WHEN is_active = 1 THEN 0 ELSE 1 END, updated_at_ms = @Now, updated_by = @Actor, version_no = version_no + 1 WHERE id = @Id",
            _ => null
        };

        if (sql == null)
            return ApiResponse<object>.Fail(400, $"不支持的主数据域：{req.DomainKey}");

        using var conn = _connFactory.CreateConnection();
        int rows = await conn.ExecuteAsync(sql, new { Id = id, Now = now, Actor = actor });
        if (rows == 0)
            return ApiResponse<object>.Fail(404, "未找到待启停的主数据记录");

        if (key == "staff")
        {
            await conn.ExecuteAsync(@"
                UPDATE sys_users
                SET is_active = CASE WHEN EXISTS(SELECT 1 FROM hr_employees e WHERE e.id = @Id AND e.employment_status = 'ACTIVE') THEN 1 ELSE 0 END,
                    session_version = session_version + 1,
                    updated_at_ms = @Now,
                    updated_by = @Actor
                WHERE id = (SELECT user_id FROM hr_employees WHERE id = @Id)
                   OR employee_no = (SELECT employee_no FROM hr_employees WHERE id = @Id)",
                new { Id = id, Now = now, Actor = actor });
        }

        return ApiResponse<object>.Success(new { Id = id, DomainKey = key }, "主数据状态已切换");
    }
}
