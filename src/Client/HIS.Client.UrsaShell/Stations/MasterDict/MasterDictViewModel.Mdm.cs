using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.MasterDict;

public sealed record MdmDomainDescriptor(string Key, string Title, string PrimaryKeySpec, string StandardSpec, string ApiPath);

public sealed class MdmGridRow
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string StandardCode { get; set; } = string.Empty;
    public string CategoryOrSpec { get; set; } = string.Empty;
    public string ExtraAttributes { get; set; } = string.Empty;
    public string StatusText { get; set; } = "有效";
    public string RawStdCode { get; set; } = string.Empty;
    public string RawCategory { get; set; } = string.Empty;
    public string RawExtra { get; set; } = string.Empty;
}

public sealed partial class MasterDictViewModel
{
    private MdmDomainDescriptor? _selectedMdmDomain;
    private MdmGridRow? _selectedMdmRow;
    private string _mdmSearchKeyword = string.Empty;
    private string _mdmSummaryBanner = "正在加载 34 大主数据域统计...";
    private string _mdmEditId = string.Empty;
    private string _mdmQuickCode = string.Empty;
    private string _mdmQuickName = string.Empty;
    private string _mdmQuickStdCode = string.Empty;
    private string _mdmQuickCategory = string.Empty;
    private string _mdmQuickExtra = string.Empty;

    public ObservableCollection<MdmDomainDescriptor> MdmDomains { get; } = new()
    {
        new("organizations", "1. 医疗机构/院区", "org_id / uscc, yb_org_code", "GB 32100-2015 / WS/T 364.14", "api/v4/mdm/organizations"),
        new("departments", "2. 科室与病区", "dept_id / org_id + dept_code", "WS/T 363.14 / 医保科室分类", "api/v4/mdm/departments"),
        new("beds", "3. 床位资源", "bed_id / ward_id + bed_no", "编制床位 vs 开放床位使用率", "api/v4/mdm/beds"),
        new("staff", "4. 医护与员工", "staff_id / emp_no", "WS/T 364.15 / 医保医师护师码", "api/v4/mdm/staff"),
        new("empi", "5. 患者主索引 EMPI", "empi_id / source_sys + local_pid", "WS/T 364-2023 / GB/T 2260", "api/v4/mdm/empi"),
        new("vendors", "6. 供应商/生产厂商", "vendor_id / uscc", "GB 32100-2015 / GSP 资质", "api/v4/mdm/vendors"),
        new("drugs", "7. 药品主数据", "drug_id / drug_code", "医保药品码 / ATC / DDD / 本位码", "api/v4/mdm/drugs"),
        new("consumables", "8. 医用耗材主数据", "material_id / udi_di", "医保耗材码 / NMPA UDI-DI", "api/v4/mdm/consumables"),
        new("order_items", "9. 诊疗与服务项目", "order_item_id / order_item_code", "国家医疗服务价格项目编码", "api/v4/mdm/order-items"),
        new("charge_mappings", "10. 收费目录对照", "charge_item_id + ins_type + eff_date", "医保目录甲乙丙与生效期", "api/v4/mdm/charge-mappings"),
        new("icd10", "11. 西医疾病诊断", "diag_id / std_ver + icd_code", "GB/T 14396-2016 / ICD-10 医保2.0", "api/v4/mdm/icd10"),
        new("tcm", "12. 中医病证字典", "tcm_diag_id / std_ver + tcm_code", "医保中医诊断 95 国标 (6位)", "api/v4/mdm/tcm-diagnoses"),
        new("icd9", "13. 手术操作字典", "oper_id / std_ver + icd9_code", "ICD-9-CM-3 医保版 (1-4级)", "api/v4/mdm/icd9-operations"),
        new("exam_std", "14. 检验检查标准项目", "exam_std_id / std_code", "LOINC / SNOMED CT / 危急值", "api/v4/mdm/exam-std-items"),
        new("yb_diseases", "15. 医保病种目录", "disease_code (M/B/R)", "门慢特M6位 / 按病种B7位 / 日间R", "api/v4/mdm/yb-diseases"),
        new("drg_rules", "16. DRG/DIP 3.0 分组", "group_code + std_ver", "CHS-DRG 3.0 / DIP 3.0 权重费率", "api/v4/mdm/drg-rules"),
        new("ivd_reagents", "17. 体外诊断试剂 IVD", "reagent_code / udi_di", "医保IVD编码 / 注册证 / 方法学", "api/v4/mdm/ivd-reagents"),
        new("payer_policies", "18. 费别与统筹区协议", "fee_type + pool_area", "GB/T 2260 统筹区 / 商保直付", "api/v4/mdm/payer-policies"),
        new("equipments", "19. 医疗设备固定资产", "asset_code / serial_no", "折旧年限 / 开机时长 / 医保成像码", "api/v4/mdm/equipments"),
        new("warehouses", "20. 库房药房与货位", "warehouse_code", "中心库/门住药房/PIVAS/温湿度", "api/v4/mdm/warehouses"),
        new("cssd_packs", "21. CSSD 消毒器械包", "pack_code", "高温/低温等离子灭菌与效期", "api/v4/mdm/cssd-packs"),
        new("logistics", "22. 总务后勤物资", "item_code", "办公/被服/备件 HRP 对接", "api/v4/mdm/logistics-items"),
        new("med_rules", "23. 用药规则与煎服法", "rule_code", "途径/频次时间点/皮试/溶媒", "api/v4/mdm/medication-rules"),
        new("microbiology", "24. 微生物与药敏字典", "micro_code", "细菌真菌 / 多重耐药菌监测", "api/v4/mdm/microbiology"),
        new("imaging_map", "25. 影像部位设备映射", "mapping_code", "模态/部位/扫描协议/AE Title", "api/v4/mdm/imaging-mappings"),
        new("or_rooms", "26. 手术间与手麻字典", "room_code", "洁净级别/麻醉/ASA/切口等级", "api/v4/mdm/or-rooms"),
        new("nutrition", "27. 膳食与营养制剂", "item_code", "治疗膳食 / 肠内肠外营养热卡", "api/v4/mdm/nutrition-items"),
        new("locations", "28. 物理空间位置树", "location_code", "院区→楼宇→楼层→房间/叫号屏", "api/v4/mdm/locations"),
        new("med_groups", "29. 医疗组/主诊组", "group_code", "组长/成员/管辖床位/绩效单元", "api/v4/mdm/medical-groups"),
        new("reg_levels", "30. 号别与号源池模板", "reg_level_code", "普通/专家/MDT/现场线上池占比", "api/v4/mdm/reg-levels"),
        new("acc_subjects", "31. 会计科目成本中心", "subject_code", "政府会计科目/分摊权重", "api/v4/mdm/accounting-subjects"),
        new("sys_terminals", "32. 业务系统与终端", "sys_code", "EMR/LIS/PACS/PDA/自助机", "api/v4/mdm/system-terminals"),
        new("ca_certs", "33. 医护 CA 电子签章", "cert_serial / staff_id", "CA机构/印章/有效期/吊销状态", "api/v4/mdm/ca-certificates"),
        new("yb_ext", "34. 医保药师与定点药店", "yb_code (Y13位/P12位)", "双通道处方流转与药师贯标", "api/v4/mdm/yb-ext-entities")
    };

    public ObservableCollection<MdmGridRow> MdmRows { get; } = new();

    public MdmDomainDescriptor? SelectedMdmDomain
    {
        get => _selectedMdmDomain;
        set
        {
            if (SetField(ref _selectedMdmDomain, value) && value != null)
            {
                ClearMdmEditor();
                _ = LoadMdmDomainRecordsAsync();
            }
        }
    }

    public MdmGridRow? SelectedMdmRow
    {
        get => _selectedMdmRow;
        set
        {
            if (SetField(ref _selectedMdmRow, value) && value != null)
            {
                MdmEditId = value.Id;
                MdmQuickCode = value.Code;
                MdmQuickName = value.Name;
                MdmQuickStdCode = value.RawStdCode;
                MdmQuickCategory = value.RawCategory;
                MdmQuickExtra = value.RawExtra;
            }
        }
    }

    public string MdmSearchKeyword { get => _mdmSearchKeyword; set => SetField(ref _mdmSearchKeyword, value); }
    public string MdmSummaryBanner { get => _mdmSummaryBanner; set => SetField(ref _mdmSummaryBanner, value); }
    public string MdmEditId { get => _mdmEditId; set => SetField(ref _mdmEditId, value); }
    public string MdmQuickCode { get => _mdmQuickCode; set => SetField(ref _mdmQuickCode, value); }
    public string MdmQuickName { get => _mdmQuickName; set => SetField(ref _mdmQuickName, value); }
    public string MdmQuickStdCode { get => _mdmQuickStdCode; set => SetField(ref _mdmQuickStdCode, value); }
    public string MdmQuickCategory { get => _mdmQuickCategory; set => SetField(ref _mdmQuickCategory, value); }
    public string MdmQuickExtra { get => _mdmQuickExtra; set => SetField(ref _mdmQuickExtra, value); }

    public ICommand? RefreshMdmCommand { get; private set; }
    public ICommand? SaveMdmQuickCommand { get; private set; }
    public ICommand? NewMdmRecordCommand { get; private set; }
    public ICommand? ToggleMdmRecordCommand { get; private set; }

    private void ClearMdmEditor()
    {
        _selectedMdmRow = null;
        OnPropertyChanged(nameof(SelectedMdmRow));
        MdmEditId = string.Empty;
        MdmQuickCode = string.Empty;
        MdmQuickName = string.Empty;
        MdmQuickStdCode = string.Empty;
        MdmQuickCategory = string.Empty;
        MdmQuickExtra = string.Empty;
    }

    private void InitializeMdmCommands()
    {
        RefreshMdmCommand = new RelayCommand(async () =>
        {
            await LoadMdmSummaryAsync();
            await LoadMdmDomainRecordsAsync();
        });
        SaveMdmQuickCommand = new RelayCommand(async () => await SaveMdmQuickAsync());
        NewMdmRecordCommand = new RelayCommand(ClearMdmEditor);
        ToggleMdmRecordCommand = new RelayCommand(async () => await ToggleMdmRecordAsync());
        SelectedMdmDomain = MdmDomains.FirstOrDefault();
        _ = LoadMdmSummaryAsync();
    }

    private async Task LoadMdmSummaryAsync()
    {
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var sumEnv = await http.GetFromJsonAsync<ClientApiEnvelope<JsonElement>>("api/v4/mdm/summary", JsonOpts);
            var bedEnv = await http.GetFromJsonAsync<ClientApiEnvelope<JsonElement>>("api/v4/mdm/beds/utilization", JsonOpts);
            if (sumEnv?.Data.ValueKind == JsonValueKind.Object)
            {
                var d = sumEnv.Data;
                int orgs = d.TryGetProperty("organizations", out var p1) ? p1.GetInt32() : 0;
                int depts = d.TryGetProperty("departments", out var p2) ? p2.GetInt32() : 0;
                int drugs = d.TryGetProperty("drugs", out var p3) ? p3.GetInt32() : 0;
                int icd10 = d.TryGetProperty("icd10Diagnoses", out var p4) ? p4.GetInt32() : 0;
                int tcm = d.TryGetProperty("tcmDiagnoses", out var p5) ? p5.GetInt32() : 0;
                int icd9 = d.TryGetProperty("icd9Operations", out var p6) ? p6.GetInt32() : 0;
                int dicts = d.TryGetProperty("dictionaries", out var p7) ? p7.GetInt32() : 0;
                string utilText = "";
                if (bedEnv?.Data.ValueKind == JsonValueKind.Object && bedEnv.Data.TryGetProperty("openBedUtilizationPct", out var pu))
                    utilText = $" · 开放床位使用率 {pu.GetDecimal():0.##}%";
                MdmSummaryBanner = $"机构:{orgs} · 科室:{depts} · 药品:{drugs} · 西医诊断:{icd10} · 中医病证:{tcm} · 手术操作:{icd9} · 标准字典:{dicts}{utilText}";
            }
        }
        catch { }
    }

    private async Task LoadMdmDomainRecordsAsync()
    {
        if (SelectedMdmDomain == null) return;
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            string url = SelectedMdmDomain.ApiPath;
            if (!string.IsNullOrWhiteSpace(MdmSearchKeyword))
                url += $"?keyword={Uri.EscapeDataString(MdmSearchKeyword.Trim())}";
            var env = await http.GetFromJsonAsync<ClientApiEnvelope<List<JsonElement>>>(url, JsonOpts);
            MdmRows.Clear();
            if (env?.Data == null) return;

            foreach (var el in env.Data)
            {
                MdmRows.Add(MapJsonToMdmRow(SelectedMdmDomain.Key, el));
            }
        }
        catch (Exception ex)
        {
            _shell.ShowToast($"加载主数据域失败：{ex.Message}", "Error");
        }
    }

    private static string GetStr(JsonElement el, params string[] names)
    {
        if (el.ValueKind != JsonValueKind.Object) return string.Empty;
        foreach (var n in names)
        {
            if (el.TryGetProperty(n, out var v) && v.ValueKind != JsonValueKind.Null)
                return v.ToString();
            foreach (var prop in el.EnumerateObject())
            {
                if (string.Equals(prop.Name, n, StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind != JsonValueKind.Null)
                    return prop.Value.ToString();
            }
        }
        return string.Empty;
    }

    private static MdmGridRow MapJsonToMdmRow(string key, JsonElement el)
    {
        var row = key switch
        {
            "organizations" => new MdmGridRow
            {
                Code = GetStr(el, "orgCode"),
                Name = GetStr(el, "orgName"),
                StandardCode = $"USCC:{GetStr(el, "uscc")} | YB:{GetStr(el, "ybOrgCode")}",
                CategoryOrSpec = $"{GetStr(el, "orgLevel")} / {GetStr(el, "orgCategoryCode")}",
                ExtraAttributes = $"区划:{GetStr(el, "adminRegionCode")} · 执业证:{GetStr(el, "practiceLicenseNo")}",
                StatusText = GetStr(el, "isActive") == "1" ? "启用" : "停用"
            },
        "departments" => new MdmGridRow
        {
            Code = GetStr(el, "deptCode"),
            Name = GetStr(el, "deptName"),
            StandardCode = $"科目:{GetStr(el, "stdSubjectCode")} | YB:{GetStr(el, "ybDeptCode")}",
            CategoryOrSpec = GetStr(el, "deptCategory"),
            ExtraAttributes = $"成本中心:{GetStr(el, "costCenterCode")} · 门诊:{GetStr(el, "opdFlag")} 住院:{GetStr(el, "ipdFlag")}",
            StatusText = GetStr(el, "isActive") == "1" ? "启用" : "停用"
        },
        "beds" => new MdmGridRow
        {
            Code = $"{GetStr(el, "wardId")}/{GetStr(el, "bedCode")}",
            Name = $"房号:{GetStr(el, "roomCode")} · {GetStr(el, "bedType")}",
            StandardCode = $"收费等级:{GetStr(el, "chargeLevel")} | 性别:{GetStr(el, "genderLimit")}",
            CategoryOrSpec = $"¥{GetStr(el, "dailyFee")}/日",
            ExtraAttributes = $"编制:{GetStr(el, "isAuthorizedBed")} · 开放:{GetStr(el, "isOpenBed")}",
            StatusText = GetStr(el, "bedStatus") == "1" ? "占用" : "空闲"
        },
        "staff" => new MdmGridRow
        {
            Code = GetStr(el, "employeeNo"),
            Name = GetStr(el, "fullName"),
            StandardCode = $"YB:{GetStr(el, "ybStaffCode")} | 证:{GetStr(el, "practiceCertNo")}",
            CategoryOrSpec = $"{GetStr(el, "staffCategory")} / {GetStr(el, "professionalTitle")}",
            ExtraAttributes = $"处方权:{GetStr(el, "prescribeLevel")} 抗菌:{GetStr(el, "antibioticLevel")} 麻精:{GetStr(el, "narcoticLevel")}",
            StatusText = GetStr(el, "employmentStatus")
        },
        "empi" => new MdmGridRow
        {
            Code = GetStr(el, "medicalRecordNo"),
            Name = GetStr(el, "fullName"),
            StandardCode = $"EMPI:{GetStr(el, "empiId")} | 映射:{GetStr(el, "sourceMappingCount")}",
            CategoryOrSpec = $"民族:{GetStr(el, "ethnicityCode")} · 血型:{GetStr(el, "bloodTypeCode")}",
            ExtraAttributes = $"区划:{GetStr(el, "regionCode")} · 合并状态:{GetStr(el, "mergeStatus")}",
            StatusText = GetStr(el, "mergeStatus")
        },
        "vendors" => new MdmGridRow
        {
            Code = GetStr(el, "supplierCode"),
            Name = GetStr(el, "supplierName"),
            StandardCode = $"USCC:{GetStr(el, "uscc")} | GSP:{GetStr(el, "gspCertNo")}",
            CategoryOrSpec = GetStr(el, "entityType"),
            ExtraAttributes = $"效期:{GetStr(el, "gspExpiryInt")} · 银行:{GetStr(el, "bankName")}",
            StatusText = GetStr(el, "isActive") == "1" ? "启用" : "停用"
        },
        "drugs" => new MdmGridRow
        {
            Code = GetStr(el, "drugCode"),
            Name = $"{GetStr(el, "genericName")} ({GetStr(el, "drugName")})",
            StandardCode = $"YB:{GetStr(el, "ybDrugCode")} | ATC:{GetStr(el, "atcCode")}",
            CategoryOrSpec = $"{GetStr(el, "dosageForm")} {GetStr(el, "spec")}",
            ExtraAttributes = $"基药:{GetStr(el, "isEssentialDrug")} 集采:{GetStr(el, "isCentralProcured")} DDD:{GetStr(el, "dddValue")} 本位码:{GetStr(el, "drugStandardCode")}",
            StatusText = GetStr(el, "isActive") == "1" ? "启用" : "停用"
        },
        "consumables" => new MdmGridRow
        {
            Code = GetStr(el, "code"),
            Name = GetStr(el, "name"),
            StandardCode = $"UDI:{GetStr(el, "udiDi")} | YB:{GetStr(el, "ybConsumableCode")}",
            CategoryOrSpec = $"{GetStr(el, "category")} · {GetStr(el, "spec")}",
            ExtraAttributes = $"植入物:{GetStr(el, "isImplant")} 一次性:{GetStr(el, "isDisposable")} 可收费:{GetStr(el, "isChargeable")}",
            StatusText = GetStr(el, "isActive") == "1" ? "启用" : "停用"
        },
        "order_items" => new MdmGridRow
        {
            Code = GetStr(el, "orderItemCode"),
            Name = GetStr(el, "orderItemName"),
            StandardCode = $"YB:{GetStr(el, "ybServiceCode")} | 物价:{GetStr(el, "priceItemCode")}",
            CategoryOrSpec = GetStr(el, "orderCategory"),
            ExtraAttributes = $"样本/部位:{GetStr(el, "sampleOrBodyPart")} · 执行科:{GetStr(el, "execDeptId")}",
            StatusText = GetStr(el, "isActive") == "1" ? "启用" : "停用"
        },
        "charge_mappings" => new MdmGridRow
        {
            Code = GetStr(el, "chargeItemCode"),
            Name = GetStr(el, "chargeItemName"),
            StandardCode = $"目录码:{GetStr(el, "ybCatalogCode")} ({GetStr(el, "insGrade")})",
            CategoryOrSpec = $"{GetStr(el, "insType")} · ¥{GetStr(el, "unitPrice")}",
            ExtraAttributes = $"自付:{GetStr(el, "selfPayRatio")} · 效期:{GetStr(el, "effDateInt")}-{GetStr(el, "expDateInt")}",
            StatusText = GetStr(el, "isActive") == "1" ? "启用" : "停用"
        },
        "icd10" => new MdmGridRow
        {
            Code = GetStr(el, "icdCode"),
            Name = GetStr(el, "diseaseName"),
            StandardCode = $"{GetStr(el, "stdVer")} | 类目:{GetStr(el, "chapterCode")}",
            CategoryOrSpec = GetStr(el, "categoryName"),
            ExtraAttributes = $"DRG/DIP:{GetStr(el, "drgDipFlag")} 慢病:{GetStr(el, "isChronicSpecial")} 传染报卡:{GetStr(el, "isInfectiousReport")}",
            StatusText = GetStr(el, "isActive") == "1" ? "启用" : "停用"
        },
        "tcm" => new MdmGridRow
        {
            Code = GetStr(el, "tcmCode"),
            Name = GetStr(el, "tcmName"),
            StandardCode = GetStr(el, "stdVer"),
            CategoryOrSpec = GetStr(el, "tcmType") == "SYNDROME" ? "证候" : "病名",
            ExtraAttributes = $"拼音:{GetStr(el, "pinyinCode")}",
            StatusText = GetStr(el, "isActive") == "1" ? "启用" : "停用"
        },
        "icd9" => new MdmGridRow
        {
            Code = GetStr(el, "icd9Code"),
            Name = GetStr(el, "operationName"),
            StandardCode = GetStr(el, "stdVer"),
            CategoryOrSpec = $"{GetStr(el, "surgeryLevel")} 级手术 / 切口{GetStr(el, "defaultIncisionGrade")}",
            ExtraAttributes = $"微创:{GetStr(el, "isMinimallyInvasive")} 介入:{GetStr(el, "isInterventional")} 日间:{GetStr(el, "isDaySurgery")}",
            StatusText = GetStr(el, "isActive") == "1" ? "启用" : "停用"
        },
        "exam_std" => new MdmGridRow
        {
            Code = GetStr(el, "stdCode"),
            Name = GetStr(el, "stdName"),
            StandardCode = $"LOINC:{GetStr(el, "loincCode")} | 部位:{GetStr(el, "bodyPartCode")}",
            CategoryOrSpec = $"{GetStr(el, "itemKind")} · {GetStr(el, "resultUnit")}",
            ExtraAttributes = $"参考:{GetStr(el, "refRange")} · 危急值:{GetStr(el, "criticalLow")}~{GetStr(el, "criticalHigh")}",
            StatusText = GetStr(el, "isActive") == "1" ? "启用" : "停用"
        },
        "yb_diseases" => new MdmGridRow
        {
            Code = GetStr(el, "diseaseCode"),
            Name = GetStr(el, "diseaseName"),
            StandardCode = GetStr(el, "diseaseCategory"),
            CategoryOrSpec = $"限额:¥{GetStr(el, "quotaLimitAmount")}",
            ExtraAttributes = $"生效:{GetStr(el, "effDateInt")}-{GetStr(el, "expDateInt")}",
            StatusText = GetStr(el, "isActive") == "1" ? "启用" : "停用"
        },
        "drg_rules" => new MdmGridRow
        {
            Code = GetStr(el, "groupCode"),
            Name = GetStr(el, "groupName"),
            StandardCode = $"{GetStr(el, "stdVer")} | {GetStr(el, "payMode")}",
            CategoryOrSpec = $"权重:{GetStr(el, "rwWeight")} / 基准¥{GetStr(el, "benchmarkFee")}",
            ExtraAttributes = $"MDC:{GetStr(el, "mdcCode")} ADRG:{GetStr(el, "adrgCode")}",
            StatusText = GetStr(el, "isActive") == "1" ? "启用" : "停用"
        },
        "ivd_reagents" => new MdmGridRow
        {
            Code = GetStr(el, "reagentCode"),
            Name = GetStr(el, "genericName"),
            StandardCode = $"IVD:{GetStr(el, "ybIvdCode")} | UDI:{GetStr(el, "udiDi")}",
            CategoryOrSpec = $"{GetStr(el, "methodology")} · {GetStr(el, "specModel")}",
            ExtraAttributes = $"仪器:{GetStr(el, "applicableDevice")} · 注册:{GetStr(el, "regCertNo")}",
            StatusText = GetStr(el, "isActive") == "1" ? "启用" : "停用"
        },
        _ => new MdmGridRow
        {
            Code = GetStr(el, "code", "warehouseCode", "packCode", "itemCode", "ruleCode", "microCode", "mappingCode", "roomCode", "locationCode", "groupCode", "regLevelCode", "subjectCode", "sysCode", "certSerial", "ybCode", "feeTypeCode"),
            Name = GetStr(el, "name", "warehouseName", "packName", "itemName", "ruleName", "microName", "bodyPartName", "roomName", "locationName", "groupName", "regLevelName", "subjectName", "sysName", "caOrgName", "entityName", "feeTypeName"),
            StandardCode = GetStr(el, "ybImagingDeviceCode", "warehouseType", "sterilizationMethod", "category", "routeCode", "microType", "modality", "cleanLevel", "nutritionType", "locationLevel", "deptId", "chargeItemCode", "incomeExpenseType", "entryType", "staffId", "entityType", "poolAreaCode"),
            CategoryOrSpec = GetStr(el, "modelSpec", "tempHumidityReq", "chargeItemCode", "spec", "frequencyCode", "specimenScope", "dicomAeTitle", "supportedAnesthesia", "formulaDesc", "queueScreenId", "bedScope", "regFee", "costCenterType", "endpointOrIp", "revokeStatus", "orgName", "contractUnitName"),
            ExtraAttributes = GetStr(el, "serialNo", "serviceDeptScope", "defaultExecTimes", "scanProtocol", "defaultIncisionGrade", "kcalPerUnit", "parentLocationId", "membersJson", "poolRatioJson", "allocationWeight", "authScope", "sealImageUri"),
            StatusText = GetStr(el, "status", "revokeStatus", "isActive") is "1" or "VALID" or "NORMAL" ? "有效" : "停用"
        }
        };

        row.Id = GetStr(el, "id", "empiId");
        row.RawStdCode = GetStr(el, "uscc", "stdSubjectCode", "genderLimit", "ybStaffCode", "regionCode", "ybDrugCode", "udiDi", "ybServiceCode", "ybCatalogCode", "stdVer", "loincCode", "diseaseCategory", "ybIvdCode", "poolAreaCode", "ybImagingDeviceCode", "warehouseType", "sterilizationMethod", "category", "routeCode", "microType", "modality", "cleanLevel", "nutritionType", "locationLevel", "deptId", "chargeItemCode", "incomeExpenseType", "entryType", "staffId", "entityType");
        row.RawCategory = GetStr(el, "orgLevel", "deptCategory", "bedType", "staffCategory", "bloodTypeCode", "entityType", "spec", "orderCategory", "insType", "categoryName", "tcmType", "itemKind", "quotaLimitAmount", "rwWeight", "specModel", "contractUnitName", "modelSpec", "tempHumidityReq", "frequencyCode", "specimenScope", "dicomAeTitle", "supportedAnesthesia", "formulaDesc", "queueScreenId", "bedScope", "regFee", "costCenterType", "endpointOrIp", "orgName");
        row.RawExtra = GetStr(el, "ybOrgCode", "costCenterCode", "practiceCertNo", "currentAddress", "gspCertNo", "atcCode", "ybConsumableCode", "sampleOrBodyPart", "insGrade", "chapterCode", "pinyinCode", "defaultIncisionGrade", "refRange", "diagAuxRule", "applicableDevice", "settlementRulesJson", "serialNo", "serviceDeptScope", "instrumentsJson", "unitName", "defaultExecTimes", "susceptibilityPanelJson", "scanProtocol", "kcalPerUnit", "parentLocationId", "membersJson", "poolRatioJson", "allocationWeight", "authScope", "sealImageUri");
        return row;
    }

    private async Task SaveMdmQuickAsync()
    {
        if (SelectedMdmDomain == null || string.IsNullOrWhiteSpace(MdmQuickCode) || string.IsNullOrWhiteSpace(MdmQuickName))
        {
            _shell.ShowToast("请选择主数据域并填写编码与名称", "Warning");
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var payload = new
            {
                DomainKey = SelectedMdmDomain.Key,
                Id = string.IsNullOrWhiteSpace(MdmEditId) ? null : MdmEditId.Trim(),
                Code = MdmQuickCode.Trim(),
                Name = MdmQuickName.Trim(),
                StdCode = string.IsNullOrWhiteSpace(MdmQuickStdCode) ? null : MdmQuickStdCode.Trim(),
                CategoryOrSpec = string.IsNullOrWhiteSpace(MdmQuickCategory) ? null : MdmQuickCategory.Trim(),
                ExtraAttributes = string.IsNullOrWhiteSpace(MdmQuickExtra) ? null : MdmQuickExtra.Trim()
            };
            var resp = await http.PostAsJsonAsync("api/v4/mdm/upsert", payload);
            var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
            _shell.ShowToast(env?.Message ?? "已保存主数据", (env?.Code is 200 or 0) ? "Success" : "Error");
            if (env?.Code is 200 or 0)
            {
                ClearMdmEditor();
                await LoadMdmSummaryAsync();
                await LoadMdmDomainRecordsAsync();
            }
        }
        catch (Exception ex)
        {
            _shell.ShowToast($"保存主数据失败：{ex.Message}", "Error");
        }
    }

    private async Task ToggleMdmRecordAsync()
    {
        if (SelectedMdmDomain == null || SelectedMdmRow == null || string.IsNullOrWhiteSpace(SelectedMdmRow.Id))
        {
            _shell.ShowToast("请先在右侧列表选中一条主数据记录", "Warning");
            return;
        }
        try
        {
            using var http = AuthenticatedHttpClientFactory.Create();
            var resp = await http.PostAsJsonAsync("api/v4/mdm/toggle", new
            {
                DomainKey = SelectedMdmDomain.Key,
                Id = SelectedMdmRow.Id
            });
            var env = await resp.Content.ReadFromJsonAsync<ClientApiEnvelope<JsonElement>>(JsonOpts);
            _shell.ShowToast(env?.Message ?? "主数据状态已切换", (env?.Code is 200 or 0) ? "Success" : "Error");
            if (env?.Code is 200 or 0)
            {
                ClearMdmEditor();
                await LoadMdmSummaryAsync();
                await LoadMdmDomainRecordsAsync();
            }
        }
        catch (Exception ex)
        {
            _shell.ShowToast($"切换状态失败：{ex.Message}", "Error");
        }
    }
}
