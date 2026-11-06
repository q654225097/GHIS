using System;
using System.Collections.ObjectModel;
using System.Linq;
using HIS.Client.Core;
using HIS.Contracts;

namespace HIS.Client.UrsaShell.ViewModels;

public class WorkbenchModel : ClientModelBase
{
    private string _campusName = "总院区 (Campus 1)";
    private DepartmentCategory _activeCategory = DepartmentCategory.OPD;
    private string _activeDeptName = "心血管内科门诊";
    private string _activeWardName = "内科一病区";
    private string _userName = "张建国 (主治医师)";
    private int _selectedNavIndex = 0; // 0=门诊, 1=住院, 2=病区床位, 3=医技, 4=药房, 5=病历OFD, 6=动态配置
    private PatientQueueItemModel? _selectedPatient;
    private EmrEditorWorkspaceModel _emrWorkspace = new();
    private string _systemNotice = "系统就绪 | 8大架构门禁检验全部通过 (GATE-01 ~ GATE-08)";

    // Modal / Drawer visibility flags
    private bool _isTimelineDrawerOpen;
    private bool _isWhoSurgeryCheckOpen;
    private bool _isCriticalValueAlertOpen;
    private bool _isAddPrescriptionDialogOpen;
    private bool _isDischargeCheckDrawerOpen;
    private bool _isOfdViewerDrawerOpen;
    private bool _isBorrowBedDialogOpen;
    private bool _isCreateUserModalOpen;
    private bool _isCreateRoleModalOpen;
    private bool _isSwitchUserModalOpen;

    // Rich domain models
    private WhoSurgeryCheckModel _whoSurgeryCheck = new();
    private CriticalValueAlertModel _criticalValueAlert = new();
    private DischargeCheckDetailsModel _dischargeCheckDetails = new();
    private DynamicFormTemplateModel? _selectedFormTemplate;
    private NewUserFormModel _newUserForm = new();
    private NewRoleFormModel _newRoleForm = new();

    // RBAC Administration Collections
    public ObservableCollection<UserAdminItemModel> UsersList { get; } = new();
    public ObservableCollection<RoleAdminItemModel> RolesList { get; } = new();
    public ObservableCollection<PermissionMatrixItemModel> PermissionMatrixList { get; } = new();
    public ObservableCollection<LoginUserOption> SwitchableUsers { get; } = new();

    public WorkbenchModel()
    {
        // 1. Initialize Patient Queue
        PatientQueue = new ObservableCollection<PatientQueueItemModel>
        {
            new PatientQueueItemModel
            {
                QueueNo = "001",
                PatientName = "周建强",
                Gender = "男",
                Age = 41,
                MedicalRecordNo = "PAT85061801",
                VisitNo = "OPD20261105001",
                FeeType = "职工医保 (共济)",
                Status = "就诊中",
                StatusClass = "Primary",
                ChiefComplaint = "阵发性心悸胸闷3天，加重2小时",
                AllergyTags = "青霉素过敏史, 磺胺类过敏",
                HasAllergyAlert = true,
                IsSelected = true
            },
            new PatientQueueItemModel
            {
                QueueNo = "002",
                PatientName = "王大明",
                Gender = "男",
                Age = 58,
                MedicalRecordNo = "PAT85061802",
                VisitNo = "OPD20261105002",
                FeeType = "城乡居民医保",
                Status = "候诊中",
                StatusClass = "Warning",
                ChiefComplaint = "劳累后胸骨后闷痛1周",
                AllergyTags = "无已知药物过敏",
                HasAllergyAlert = false
            },
            new PatientQueueItemModel
            {
                QueueNo = "003",
                PatientName = "赵淑珍",
                Gender = "女",
                Age = 63,
                MedicalRecordNo = "PAT85061803",
                VisitNo = "OPD20261105003",
                FeeType = "职工医保",
                Status = "候诊中",
                StatusClass = "Warning",
                ChiefComplaint = "反复心悸伴头晕半月余",
                AllergyTags = "阿司匹林过敏",
                HasAllergyAlert = true
            },
            new PatientQueueItemModel
            {
                QueueNo = "004",
                PatientName = "孙建国",
                Gender = "男",
                Age = 49,
                MedicalRecordNo = "PAT85061804",
                VisitNo = "OPD20261105004",
                FeeType = "自费",
                Status = "候诊中",
                StatusClass = "Warning",
                ChiefComplaint = "体检发现血压升高3天",
                AllergyTags = "无已知药物过敏",
                HasAllergyAlert = false
            },
            new PatientQueueItemModel
            {
                QueueNo = "005",
                PatientName = "钱振华",
                Gender = "男",
                Age = 52,
                MedicalRecordNo = "PAT85061805",
                VisitNo = "OPD20261105005",
                FeeType = "职工医保",
                Status = "已接诊",
                StatusClass = "Success",
                ChiefComplaint = "冠心病术后复查配药",
                AllergyTags = "无已知药物过敏",
                HasAllergyAlert = false
            }
        };

        Prescriptions = new ObservableCollection<PrescriptionItemModel>();
        MedTechOrders = new ObservableCollection<MedTechOrderItemModel>();
        BedCards = new ObservableCollection<BedCardModel>();
        InpatientRecords = new ObservableCollection<InpatientRecordModel>();
        PharmacyRecords = new ObservableCollection<PharmacyDispenseModel>();
        OfdArchiveRecords = new ObservableCollection<OfdArchiveRecordModel>();
        DynamicFormTemplates = new ObservableCollection<DynamicFormTemplateModel>();
        TimelineEvents = new ObservableCollection<TimelineEventModel>();
        AvailableDrugsCatalog = new ObservableCollection<DrugCatalogItemModel>();

        InitBedCards();
        InitInpatientRecords();
        InitPharmacyRecords();
        InitOfdArchives();
        InitDynamicFormTemplates();
        InitTimelineEvents();
        InitAvailableDrugsCatalog();
        InitAdminData();
        SyncWithSession();

        _selectedFormTemplate = DynamicFormTemplates.Count > 0 ? DynamicFormTemplates[0] : null;

        // Load default first patient
        SelectedPatient = PatientQueue[0];
    }

    private void InitBedCards()
    {
        BedCards.Clear();
        var cards = new[]
        {
            new BedCardModel
            {
                BedCode = "01 床",
                BedType = "标准编制床",
                PatientName = "刘德海",
                Gender = "男",
                Age = 67,
                AdmissionNo = "ADM20261102001",
                AdmittingDeptName = "心血管内科住院一科",
                NursingWardName = "内科一病区",
                NursingLevel = "一级护理",
                Diagnosis = "冠心病 急性前壁心肌梗死",
                DailyFee = "¥45.00/天",
                IsBorrowed = false,
                IsOccupied = true,
                CardTheme = "Primary"
            },
            new BedCardModel
            {
                BedCode = "02 床",
                BedType = "⚡ 跨病区借床 (GATE-02)",
                PatientName = "周建强",
                Gender = "男",
                Age = 41,
                AdmissionNo = "ADM20261105001",
                AdmittingDeptName = "心血管内科住院一科 (开医嘱)",
                NursingWardName = "内科二病区 (执行借床/扫码护理)",
                NursingLevel = "二级护理",
                Diagnosis = "不稳定型心绞痛；高血压 II 期",
                DailyFee = "¥45.00/天",
                IsBorrowed = true,
                IsOccupied = true,
                CardTheme = "Warning"
            },
            new BedCardModel
            {
                BedCode = "03 床",
                BedType = "标准空床",
                PatientName = "【空床待收治】",
                Gender = "-",
                Age = 0,
                AdmissionNo = "-",
                AdmittingDeptName = "心血管内科住院一科",
                NursingWardName = "内科一病区",
                NursingLevel = "未指定",
                Diagnosis = "无",
                DailyFee = "¥45.00/天",
                IsBorrowed = false,
                IsOccupied = false,
                CardTheme = "Success"
            },
            new BedCardModel
            {
                BedCode = "04 床",
                BedType = "加床 (TEMP-ADD)",
                PatientName = "孙建国",
                Gender = "男",
                Age = 49,
                AdmissionNo = "ADM20261104003",
                AdmittingDeptName = "心血管内科住院一科",
                NursingWardName = "内科一病区",
                NursingLevel = "二级护理",
                Diagnosis = "高血压危象恢复期",
                DailyFee = "¥35.00/天",
                IsBorrowed = false,
                IsOccupied = true,
                CardTheme = "Primary"
            },
            new BedCardModel
            {
                BedCode = "05 床",
                BedType = "急危重监护床 (ICU)",
                PatientName = "陈芳华",
                Gender = "女",
                Age = 73,
                AdmissionNo = "ADM20261103002",
                AdmittingDeptName = "心血管内科住院一科",
                NursingWardName = "内科一病区",
                NursingLevel = "特级护理 (病危)",
                Diagnosis = "急性非ST段抬高型心肌梗死",
                DailyFee = "¥120.00/天",
                IsBorrowed = false,
                IsOccupied = true,
                CardTheme = "Danger"
            },
            new BedCardModel
            {
                BedCode = "06 床",
                BedType = "待出院关床 (GATE-07)",
                PatientName = "钱学森",
                Gender = "男",
                Age = 55,
                AdmissionNo = "ADM20261101006",
                AdmittingDeptName = "心血管内科住院一科",
                NursingWardName = "内科一病区",
                NursingLevel = "三级护理 (待出院)",
                Diagnosis = "阵发性心房颤动 (射频消融术后)",
                DailyFee = "¥45.00/天",
                IsBorrowed = false,
                IsOccupied = true,
                CardTheme = "Warning"
            }
        };
        foreach (var c in cards) BedCards.Add(c);
    }

    private void InitInpatientRecords()
    {
        InpatientRecords.Clear();
        var records = new[]
        {
            new InpatientRecordModel
            {
                AdmissionNo = "ADM20261105001",
                PatientName = "周建强",
                BedCode = "02床 (借床)",
                AdmittingDept = "心血管内科住院一科",
                NursingWard = "内科二病区 (执行借床)",
                NursingLevel = "二级护理",
                Diagnosis = "不稳定型心绞痛；高血压2级",
                AttendingDoctor = "张建国 (主治)",
                AdmissionDate = "2026-11-05 10:15",
                DailySummary = "今日患者活动后胸闷已缓解，血压 132/84 mmHg，长期阿司匹林、拜新同规律口服，无明显心悸。",
                IsCritical = false,
                IsDischargeReady = false
            },
            new InpatientRecordModel
            {
                AdmissionNo = "ADM20261102001",
                PatientName = "刘德海",
                BedCode = "01床",
                AdmittingDept = "心血管内科住院一科",
                NursingWard = "内科一病区",
                NursingLevel = "一级护理",
                Diagnosis = "急性前壁心肌梗死 (急诊PCI术后)",
                AttendingDoctor = "王敏 (副主任)",
                AdmissionDate = "2026-11-02 08:30",
                DailySummary = "支架植入术后第4天，穿刺点无渗血，心肌酶谱呈下降趋势，生命体征平稳，继续二级抗血小板治疗。",
                IsCritical = false,
                IsDischargeReady = false
            },
            new InpatientRecordModel
            {
                AdmissionNo = "ADM20261103002",
                PatientName = "陈芳华",
                BedCode = "05床 (ICU)",
                AdmittingDept = "心血管内科住院一科",
                NursingWard = "内科一病区",
                NursingLevel = "特级护理 (病危)",
                Diagnosis = "急性非ST段抬高型心梗；心源性休克",
                AttendingDoctor = "张建国 (主治)",
                AdmissionDate = "2026-11-03 14:20",
                DailySummary = "持续多巴胺泵入维持血压，持续心电监护，尿量每小时约 35ml，病情处于高危状态，需严密防范室速室颤！",
                IsCritical = true,
                IsDischargeReady = false
            },
            new InpatientRecordModel
            {
                AdmissionNo = "ADM20261101006",
                PatientName = "钱学森",
                BedCode = "06床",
                AdmittingDept = "心血管内科住院一科",
                NursingWard = "内科一病区",
                NursingLevel = "三级护理",
                Diagnosis = "阵发性房颤 (射频消融术后已转窦性)",
                AttendingDoctor = "王敏 (副主任)",
                AdmissionDate = "2026-11-01 09:00",
                DailySummary = "术后恢复优良，动态心电图未见房颤复发。六严关核查已全部就绪，主治已签审出院记录，等待办理出院结算。",
                IsCritical = false,
                IsDischargeReady = true
            }
        };
        foreach (var r in records) InpatientRecords.Add(r);
    }

    private void InitPharmacyRecords()
    {
        PharmacyRecords.Clear();
        var items = new[]
        {
            new PharmacyDispenseModel
            {
                PrescriptionNo = "RX-20261105-001",
                PatientName = "周建强",
                DrugSummary = "氯化钠注射液 500ml*2瓶, 阿司匹林肠溶片 100mg*30片/盒, 氯吡格雷片 75mg*7片/盒",
                DispenseStatus = "待扫码核验发药",
                PharmacistName = "赵丽 (主管药师)",
                SafetyAudit = "审方通过 (合理用药检测通过)",
                IsAllergyBlocked = true, // Has blocked penicillin
                CanDispense = true
            },
            new PharmacyDispenseModel
            {
                PrescriptionNo = "RX-20261105-002",
                PatientName = "王大明",
                DrugSummary = "单硝酸异山梨酯缓释片 40mg*7片/盒, 苯磺酸氨氯地平片 5mg*7片/盒",
                DispenseStatus = "已完成配发出库",
                PharmacistName = "赵丽 (主管药师)",
                SafetyAudit = "前置审方通过",
                IsAllergyBlocked = false,
                CanDispense = false
            },
            new PharmacyDispenseModel
            {
                PrescriptionNo = "RX-20261105-003",
                PatientName = "赵淑珍",
                DrugSummary = "硫酸氢氯吡格雷片 75mg*7片/盒, 琥珀酸美托洛尔缓释片 47.5mg*7片/盒",
                DispenseStatus = "待扫码核验发药",
                PharmacistName = "李药师",
                SafetyAudit = "前置审方通过",
                IsAllergyBlocked = false,
                CanDispense = true
            }
        };
        foreach (var item in items) PharmacyRecords.Add(item);
    }

    private void InitOfdArchives()
    {
        OfdArchiveRecords.Clear();
        var archives = new[]
        {
            new OfdArchiveRecordModel
            {
                ArchiveNo = "OFD-20261105-CARDIO-001",
                DocType = "门诊初诊病历 (OPD_EMR)",
                PatientName = "周建强",
                DeptName = "心血管内科门诊",
                OfdStandard = "GB/T 33190-2016",
                ZipEntries = "OFD.xml | Document.xml | CustomTags/EmrStructuredData.xml | Signatures.xml",
                HashSha256 = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
                SignatureStatus = "三级电子审签完成 (防篡改验签合法)",
                ArchiveTime = "2026-11-05 15:48:22",
                IsVerified = true
            },
            new OfdArchiveRecordModel
            {
                ArchiveNo = "OFD-20261103-RESP-008",
                DocType = "住院入院记录 (IPD_ADMISSION)",
                PatientName = "刘德海",
                DeptName = "呼吸内科住院部",
                OfdStandard = "GB/T 33190-2016",
                ZipEntries = "OFD.xml | Document.xml | CustomTags/EmrStructuredData.xml | Signatures.xml",
                HashSha256 = "8F434346648F6B96DF89DDA901C5176B10A6D83961DD3C1AC88B59B2DC327AA4",
                SignatureStatus = "主治医师审签完成 (归档封存)",
                ArchiveTime = "2026-11-03 10:22:05",
                IsVerified = true
            },
            new OfdArchiveRecordModel
            {
                ArchiveNo = "OFD-20261031-CARDIO-012",
                DocType = "冠状动脉造影介入手术记录 (OPERATION)",
                PatientName = "钱学森",
                DeptName = "心血管内科介入手术室",
                OfdStandard = "GB/T 33190-2016",
                ZipEntries = "OFD.xml | Document.xml | CustomTags/EmrStructuredData.xml | Signatures.xml",
                HashSha256 = "CA978112CA1BBDCAFAC231B39A23DC4DA786EFF8147C4E72B9807785AFEE48BB",
                SignatureStatus = "术者+一助+麻醉三签完成",
                ArchiveTime = "2026-10-31 16:30:11",
                IsVerified = true
            }
        };
        foreach (var a in archives) OfdArchiveRecords.Add(a);
    }

    private void InitDynamicFormTemplates()
    {
        DynamicFormTemplates.Clear();
        var templates = new[]
        {
            new DynamicFormTemplateModel
            {
                TemplateCode = "FORM-CARDIO-FOLLOWUP-V2",
                TemplateName = "心血管专病结构化随访表单",
                DeptScope = "心血管内科 (OPD & IPD)",
                Version = "v2.1",
                FieldCount = 24,
                GenderRuleCount = 6,
                GenderRuleDescription = "前列腺触诊仅限男性 (MALE_ONLY)；孕产史仅限女性 (FEMALE_ONLY)",
                Status = "生产已发布"
            },
            new DynamicFormTemplateModel
            {
                TemplateCode = "FORM-RESP-COPD-ASSESS",
                TemplateName = "慢性阻塞性肺疾病 CAT 评分量表",
                DeptScope = "呼吸内科 (OPD & IPD)",
                Version = "v1.4",
                FieldCount = 18,
                GenderRuleCount = 0,
                GenderRuleDescription = "无性别排他限制 (ALL)",
                Status = "生产已发布"
            },
            new DynamicFormTemplateModel
            {
                TemplateCode = "FORM-OBGYN-FIRST-VISIT",
                TemplateName = "妇产科门诊初诊专科数据采集表",
                DeptScope = "妇产科门诊 (OPD)",
                Version = "v3.0",
                FieldCount = 32,
                GenderRuleCount = 32,
                GenderRuleDescription = "全表单字段均为女性专属 (FEMALE_ONLY - GATE-03严格约束)",
                Status = "生产已发布"
            }
        };
        foreach (var t in templates) DynamicFormTemplates.Add(t);
    }

    private void InitTimelineEvents()
    {
        TimelineEvents.Clear();
        var events = new[]
        {
            new TimelineEventModel
            {
                EventTime = "2026-11-06 08:05:12",
                EventType = "CRITICAL_ALERT",
                EventTitle = "检验急诊危急值强广播 [hs-cTnI = 12.85 ng/mL]",
                Department = "医学检验科 急诊急救生化室",
                DoctorOrOperator = "李红技师 / 系统自动广播",
                Summary = "超敏肌钙蛋白 I 定量异常升高（超警戒线 300 倍），已触发双通道声光强推送！需管床主治与病区护士双签收。",
                BadgeClass = "Danger",
                ResultIcon = "🚨"
            },
            new TimelineEventModel
            {
                EventTime = "2026-11-05 10:30:15",
                EventType = "SAFETY_GATE",
                EventTitle = "GATE-05 用药红线强阻断 [注射用青霉素钠]",
                Department = "门诊用药安全屏障中心",
                DoctorOrOperator = "系统规则引擎 (前置审方)",
                Summary = "检测到患者既往明确青霉素过敏史且皮试结果阳性，处方已被安全硬隔离锁定，发药与执行接口 100% 拒绝提交！",
                BadgeClass = "Danger",
                ResultIcon = "🛡️"
            },
            new TimelineEventModel
            {
                EventTime = "2026-11-05 10:15:00",
                EventType = "MEDTECH_LOCK",
                EventTitle = "医技到检核收并确费 [12导联同步心电图]",
                Department = "功能检查科 (DEPT-MTC-ECG)",
                DoctorOrOperator = "王敏 (心电技师)",
                Summary = "患者已在门诊 2 楼心电图室完成扫码到检，系统置 is_fee_confirmed=1 确费锁单，直接撤单与财务退款已锁定。",
                BadgeClass = "Warning",
                ResultIcon = "🔒"
            },
            new TimelineEventModel
            {
                EventTime = "2026-11-05 09:30:22",
                EventType = "OPD_VISIT",
                EventTitle = "心血管内科门诊初诊接诊",
                Department = "心血管内科门诊 (DEPT-OPD-01)",
                DoctorOrOperator = "张建国 (主治医师)",
                Summary = "主诉阵发性心前区憋闷心悸3天；初步诊断为劳力性心绞痛 (CCS II级)；完成 EMR 结构化录入并生成 GB/T 33190-2016 OFD。",
                BadgeClass = "Primary",
                ResultIcon = "🩺"
            },
            new TimelineEventModel
            {
                EventTime = "2026-11-03 14:10:00",
                EventType = "ER_RESCUE",
                EventTitle = "急诊医学科绿色通道留观抢救",
                Department = "急诊医学科诊室 (DEPT-ER-01)",
                DoctorOrOperator = "赵晓东 (副主任医师)",
                Summary = "血压急剧升高至 168/102 mmHg，伴气促出汗。开启绿色通道先诊疗后付费授信 (5000元)，静脉滴注硝酸甘油。",
                BadgeClass = "Warning",
                ResultIcon = "🚑"
            },
            new TimelineEventModel
            {
                EventTime = "2026-10-18 09:00:00",
                EventType = "LAB_ARCHIVE",
                EventTitle = "全血细胞分析与凝血功能四项已归档",
                Department = "医学检验科 (DEPT-MTC-LAB)",
                DoctorOrOperator = "张梅 (主管检验师)",
                Summary = "白细胞 7.8×10^9/L，血小板 215×10^9/L，PT 12.1s，APTT 32.5s，报告 OFD 已同步至全院临床数据中心 (CDR)。",
                BadgeClass = "Success",
                ResultIcon = "🧪"
            }
        };
        foreach (var ev in events) TimelineEvents.Add(ev);
    }

    private void InitAvailableDrugsCatalog()
    {
        AvailableDrugsCatalog.Clear();
        var drugs = new[]
        {
            new DrugCatalogItemModel
            {
                DrugCode = "DRUG-CARD-001", DrugName = "单硝酸异山梨酯缓释片", Spec = "40mg*7片/盒",
                DefaultDose = "40mg", DefaultFreq = "qd (早晨一次)", DefaultRoute = "口服", UnitPrice = "¥38.20", StockQuantity = "142盒",
                IsSkinTestRequired = false, IsHighRisk = false
            },
            new DrugCatalogItemModel
            {
                DrugCode = "DRUG-CARD-002", DrugName = "阿托伐他汀钙片", Spec = "20mg*7片/盒",
                DefaultDose = "20mg", DefaultFreq = "qn (睡前一次)", DefaultRoute = "口服", UnitPrice = "¥39.60", StockQuantity = "88盒",
                IsSkinTestRequired = false, IsHighRisk = false
            },
            new DrugCatalogItemModel
            {
                DrugCode = "DRUG-CARD-003", DrugName = "酒石酸美托洛尔片", Spec = "25mg*20片/盒",
                DefaultDose = "25mg", DefaultFreq = "bid (每12小时一次)", DefaultRoute = "口服", UnitPrice = "¥18.50", StockQuantity = "210盒",
                IsSkinTestRequired = false, IsHighRisk = false
            },
            new DrugCatalogItemModel
            {
                DrugCode = "DRUG-CARD-004", DrugName = "缬沙坦氢氯噻嗪片", Spec = "80mg/12.5mg*7片/盒",
                DefaultDose = "1片", DefaultFreq = "qd (早晨一次)", DefaultRoute = "口服", UnitPrice = "¥42.00", StockQuantity = "65盒",
                IsSkinTestRequired = false, IsHighRisk = false
            },
            new DrugCatalogItemModel
            {
                DrugCode = "DRUG-CARD-005", DrugName = "硫酸氢氯吡格雷片", Spec = "75mg*7片/盒",
                DefaultDose = "75mg", DefaultFreq = "qd (每日一次)", DefaultRoute = "口服", UnitPrice = "¥56.80", StockQuantity = "115盒",
                IsSkinTestRequired = false, IsHighRisk = false
            },
            new DrugCatalogItemModel
            {
                DrugCode = "DRUG-CARD-006", DrugName = "阿司匹林肠溶片", Spec = "100mg*30片/盒",
                DefaultDose = "100mg", DefaultFreq = "qn (每晚一次)", DefaultRoute = "口服", UnitPrice = "¥22.50", StockQuantity = "320盒",
                IsSkinTestRequired = false, IsHighRisk = false
            },
            new DrugCatalogItemModel
            {
                DrugCode = "DRUG-ANTI-001", DrugName = "注射用青霉素钠", Spec = "80万单位/支",
                DefaultDose = "80万U", DefaultFreq = "bid (每12小时一次)", DefaultRoute = "静脉滴注", UnitPrice = "¥12.50", StockQuantity = "500支",
                IsSkinTestRequired = true, IsHighRisk = true
            },
            new DrugCatalogItemModel
            {
                DrugCode = "DRUG-ANTI-002", DrugName = "阿奇霉素分散片 (大环内酯类安全抗感染)", Spec = "0.25g*6片/盒",
                DefaultDose = "0.5g", DefaultFreq = "qd (每日一次)", DefaultRoute = "口服", UnitPrice = "¥32.50", StockQuantity = "175盒",
                IsSkinTestRequired = false, IsHighRisk = false
            }
        };
        foreach (var d in drugs) AvailableDrugsCatalog.Add(d);
    }

    public string CampusName { get => _campusName; set => SetField(ref _campusName, value); }
    public DepartmentCategory ActiveCategory { get => _activeCategory; set => SetField(ref _activeCategory, value); }
    public string ActiveDeptName { get => _activeDeptName; set => SetField(ref _activeDeptName, value); }
    public string ActiveWardName { get => _activeWardName; set => SetField(ref _activeWardName, value); }
    public string UserName { get => _userName; set => SetField(ref _userName, value); }
    public int SelectedNavIndex { get => _selectedNavIndex; set => SetField(ref _selectedNavIndex, value); }
    public string SystemNotice { get => _systemNotice; set => SetField(ref _systemNotice, value); }

    public bool IsTimelineDrawerOpen { get => _isTimelineDrawerOpen; set => SetField(ref _isTimelineDrawerOpen, value); }
    public bool IsWhoSurgeryCheckOpen { get => _isWhoSurgeryCheckOpen; set => SetField(ref _isWhoSurgeryCheckOpen, value); }
    public bool IsCriticalValueAlertOpen { get => _isCriticalValueAlertOpen; set => SetField(ref _isCriticalValueAlertOpen, value); }
    public bool IsAddPrescriptionDialogOpen { get => _isAddPrescriptionDialogOpen; set => SetField(ref _isAddPrescriptionDialogOpen, value); }
    public bool IsDischargeCheckDrawerOpen { get => _isDischargeCheckDrawerOpen; set => SetField(ref _isDischargeCheckDrawerOpen, value); }
    public bool IsOfdViewerDrawerOpen { get => _isOfdViewerDrawerOpen; set => SetField(ref _isOfdViewerDrawerOpen, value); }
    public bool IsBorrowBedDialogOpen { get => _isBorrowBedDialogOpen; set => SetField(ref _isBorrowBedDialogOpen, value); }
    public bool IsCreateUserModalOpen { get => _isCreateUserModalOpen; set => SetField(ref _isCreateUserModalOpen, value); }
    public bool IsCreateRoleModalOpen { get => _isCreateRoleModalOpen; set => SetField(ref _isCreateRoleModalOpen, value); }
    public bool IsSwitchUserModalOpen { get => _isSwitchUserModalOpen; set => SetField(ref _isSwitchUserModalOpen, value); }

    public NewUserFormModel NewUserForm { get => _newUserForm; set => SetField(ref _newUserForm, value); }
    public NewRoleFormModel NewRoleForm { get => _newRoleForm; set => SetField(ref _newRoleForm, value); }

    // RBAC Session Indicators
    public string RoleBadgeText => !string.IsNullOrEmpty(SessionContext.Current.RoleName) ? SessionContext.Current.RoleName : "主治医师";
    public string UserRoleSubtitle => !string.IsNullOrEmpty(SessionContext.Current.EmployeeNo)
        ? $"工号: {SessionContext.Current.EmployeeNo} | 权限: {SessionContext.Current.RoleName} (处方权{SessionContext.Current.PrescribeLevel}级)"
        : "工号: EMP001 | 权限: 门诊主治医师/处方权II级";
    public string CurrentRoleCode => SessionContext.Current.RoleCode;
    public bool CanPrescribe => SessionContext.Current.CanPrescribe || string.IsNullOrEmpty(SessionContext.Current.RoleCode);
    public bool CanSignEmr => SessionContext.Current.CanSignEmr || string.IsNullOrEmpty(SessionContext.Current.RoleCode);
    public bool CanSurgery => SessionContext.Current.CanSurgery || string.IsNullOrEmpty(SessionContext.Current.RoleCode);
    public bool IsAdmin => SessionContext.Current.IsAdmin || string.IsNullOrEmpty(SessionContext.Current.RoleCode);
    public bool IsNurse => SessionContext.Current.IsWardNurse;

    public void SyncWithSession()
    {
        if (SessionContext.Current.IsLoggedIn)
        {
            UserName = $"{SessionContext.Current.DisplayName} ({SessionContext.Current.RoleName})";
            if (!string.IsNullOrEmpty(SessionContext.Current.ActiveDeptName))
            {
                ActiveDeptName = SessionContext.Current.ActiveDeptName;
            }
        }
        OnPropertyChanged(nameof(RoleBadgeText));
        OnPropertyChanged(nameof(UserRoleSubtitle));
        OnPropertyChanged(nameof(CurrentRoleCode));
        OnPropertyChanged(nameof(CanPrescribe));
        OnPropertyChanged(nameof(CanSignEmr));
        OnPropertyChanged(nameof(CanSurgery));
        OnPropertyChanged(nameof(IsAdmin));
        OnPropertyChanged(nameof(IsNurse));
    }

    public void SaveNewUser()
    {
        if (string.IsNullOrWhiteSpace(NewUserForm.UserName) || string.IsNullOrWhiteSpace(NewUserForm.DisplayName))
            return;

        var roleMap = new System.Collections.Generic.Dictionary<string, string>
        {
            ["ROLE-ADMIN"] = "超级管理员",
            ["ROLE-OPD-DOC"] = "门诊医生",
            ["ROLE-IPD-RESIDENT"] = "住院医师",
            ["ROLE-IPD-ATTENDING"] = "主治医师",
            ["ROLE-WARD-NURSE"] = "病区护士",
            ["ROLE-PHARMACIST"] = "药剂师",
            ["ROLE-CASHIER"] = "收费员"
        };

        var roleName = roleMap.TryGetValue(NewUserForm.SelectedRole, out var rn) ? rn : "临床医护";

        UsersList.Add(new UserAdminItemModel
        {
            UserId = "USR-" + Guid.NewGuid().ToString("N").ToUpper()[..8],
            UserName = NewUserForm.UserName.Trim(),
            DisplayName = NewUserForm.DisplayName.Trim(),
            EmployeeNo = string.IsNullOrWhiteSpace(NewUserForm.EmployeeNo) ? "EMP" + (UsersList.Count + 1).ToString("D3") : NewUserForm.EmployeeNo.Trim(),
            RoleCode = NewUserForm.SelectedRole,
            RoleName = roleName,
            DeptName = NewUserForm.SelectedDept,
            PrescribeLevel = NewUserForm.PrescribeLevel,
            SurgeryLevel = NewUserForm.SurgeryLevel,
            EmrSignLevel = NewUserForm.EmrSignLevel,
            IsActive = true
        });

        SwitchableUsers.Add(new LoginUserOption
        {
            UserName = NewUserForm.UserName.Trim(),
            DisplayName = NewUserForm.DisplayName.Trim(),
            RoleName = roleName,
            RoleCode = NewUserForm.SelectedRole,
            Avatar = NewUserForm.SelectedRole.Contains("DOC") ? "👨‍⚕️" : NewUserForm.SelectedRole.Contains("NURSE") ? "👩‍⚕️" : "👤"
        });

        SystemNotice = $"[RBAC] 成功创建用户 '{NewUserForm.DisplayName}' 并分配角色 [{roleName}] 与上下文权限！";
        NewUserForm.Reset();
        IsCreateUserModalOpen = false;
    }

    public void ToggleUserStatus(UserAdminItemModel user)
    {
        user.IsActive = !user.IsActive;
        SystemNotice = $"[RBAC] 已更新用户 '{user.DisplayName}' 账号状态为: {user.StatusText}";
    }

    public void SaveNewRole()
    {
        if (string.IsNullOrWhiteSpace(NewRoleForm.RoleCode) || string.IsNullOrWhiteSpace(NewRoleForm.RoleName))
            return;

        var code = NewRoleForm.RoleCode.Trim().ToUpper();
        if (!code.StartsWith("ROLE-")) code = "ROLE-" + code;

        RolesList.Add(new RoleAdminItemModel
        {
            RoleId = code,
            RoleCode = code,
            RoleName = NewRoleForm.RoleName.Trim(),
            DataScope = NewRoleForm.DataScope,
            Description = string.IsNullOrWhiteSpace(NewRoleForm.Description) ? "全院定制业务角色" : NewRoleForm.Description.Trim(),
            UserCount = 0,
            IsActive = true
        });

        SystemNotice = $"[RBAC] 成功创建新业务角色 [{code}] 并注入系统安全访问控制树！";
        NewRoleForm.Reset();
        IsCreateRoleModalOpen = false;
    }

    public void TogglePermissionPolicy(PermissionMatrixItemModel item, string roleTarget)
    {
        string NextPolicy(string curr) => curr switch
        {
            "FullAccess" => "ReadOnly",
            "ReadOnly" => "None",
            _ => "FullAccess"
        };

        if (roleTarget == "DOC") item.PolicyDoc = NextPolicy(item.PolicyDoc);
        else if (roleTarget == "NURSE") item.PolicyNurse = NextPolicy(item.PolicyNurse);
        else if (roleTarget == "PHARM") item.PolicyPharm = NextPolicy(item.PolicyPharm);

        SystemNotice = $"[RBAC] 动态权限策略已更新: {item.ResourceName} -> {roleTarget} 权限 = { (roleTarget == "DOC" ? item.PolicyDoc : roleTarget == "NURSE" ? item.PolicyNurse : item.PolicyPharm) }";
    }

    public void SwitchToUser(string userName)
    {
        var target = SwitchableUsers.FirstOrDefault(u => u.UserName == userName);
        if (target == null) return;

        var deptMap = new System.Collections.Generic.Dictionary<string, (string name, string cat)>
        {
            ["ROLE-ADMIN"] = ("全院管理部", "ADMIN"),
            ["ROLE-OPD-DOC"] = ("心血管内科门诊", "OPD"),
            ["ROLE-IPD-RESIDENT"] = ("心血管内科住院一科", "IPD"),
            ["ROLE-IPD-ATTENDING"] = ("心血管内科住院一科", "IPD"),
            ["ROLE-WARD-NURSE"] = ("内科一病区", "WARD"),
            ["ROLE-PHARMACIST"] = ("药剂科发药室", "PHARMACY"),
            ["ROLE-CASHIER"] = ("门诊收费处", "FINANCE")
        };

        var (deptName, deptCat) = deptMap.TryGetValue(target.RoleCode, out var d) ? d : ("心血管内科门诊", "OPD");
        var (pl, sl, el) = target.RoleCode switch
        {
            "ROLE-ADMIN" => (3, 4, 3),
            "ROLE-IPD-ATTENDING" => (2, 2, 2),
            "ROLE-OPD-DOC" => (2, 1, 2),
            "ROLE-IPD-RESIDENT" => (1, 1, 1),
            _ => (0, 0, 0)
        };

        SessionContext.Current.SetFromLogin(
            userId: "USR-" + target.UserName.ToUpper(),
            userName: target.UserName,
            displayName: target.DisplayName,
            roleCode: target.RoleCode,
            roleName: target.RoleName,
            accessToken: "session-switch-token",
            activeDeptId: "DEPT-" + target.RoleCode,
            activeDeptName: deptName,
            activeDeptCategory: deptCat,
            activeWardId: null,
            prescribeLevel: pl,
            surgeryLevel: sl,
            emrSignLevel: el
        );

        SyncWithSession();

        // Switch to the most relevant view for this role
        SelectedNavIndex = target.RoleCode switch
        {
            "ROLE-OPD-DOC" => 0,
            "ROLE-IPD-RESIDENT" or "ROLE-IPD-ATTENDING" => 1,
            "ROLE-WARD-NURSE" => 2,
            "ROLE-PHARMACIST" => 4,
            "ROLE-ADMIN" => 7,
            _ => 0
        };

        IsSwitchUserModalOpen = false;
        SystemNotice = $"[RBAC] 已安全切换至用户: {target.DisplayName} [{target.RoleName}]，岗位执业上下文已自动切换为: {deptName}！";
    }

    private void InitAdminData()
    {
        SwitchableUsers.Clear();
        SwitchableUsers.Add(new LoginUserOption { UserName = "admin", DisplayName = "系统管理员", RoleName = "超级管理员", RoleCode = "ROLE-ADMIN", Avatar = "🛡️" });
        SwitchableUsers.Add(new LoginUserOption { UserName = "doc_opd", DisplayName = "张建国", RoleName = "门诊医生", RoleCode = "ROLE-OPD-DOC", Avatar = "🩺" });
        SwitchableUsers.Add(new LoginUserOption { UserName = "doc_res", DisplayName = "李华", RoleName = "住院医师", RoleCode = "ROLE-IPD-RESIDENT", Avatar = "🏥" });
        SwitchableUsers.Add(new LoginUserOption { UserName = "doc_att", DisplayName = "王磊", RoleName = "主治医师", RoleCode = "ROLE-IPD-ATTENDING", Avatar = "👨‍⚕️" });
        SwitchableUsers.Add(new LoginUserOption { UserName = "nurse_w1", DisplayName = "陈劳", RoleName = "病区护士", RoleCode = "ROLE-WARD-NURSE", Avatar = "👩‍⚕️" });
        SwitchableUsers.Add(new LoginUserOption { UserName = "nurse_w2", DisplayName = "赵敏", RoleName = "病区护士", RoleCode = "ROLE-WARD-NURSE", Avatar = "👩‍⚕️" });
        SwitchableUsers.Add(new LoginUserOption { UserName = "phm_user", DisplayName = "孙杰", RoleName = "药剂师", RoleCode = "ROLE-PHARMACIST", Avatar = "💊" });
        SwitchableUsers.Add(new LoginUserOption { UserName = "cashier_01", DisplayName = "刘洋", RoleName = "收费员", RoleCode = "ROLE-CASHIER", Avatar = "💰" });

        UsersList.Clear();
        UsersList.Add(new UserAdminItemModel { UserId = "USR-000", UserName = "admin", DisplayName = "系统管理员", EmployeeNo = "EMP000", RoleCode = "ROLE-ADMIN", RoleName = "超级管理员", DeptName = "全院管理部", PrescribeLevel = 3, SurgeryLevel = 4, EmrSignLevel = 3, IsActive = true });
        UsersList.Add(new UserAdminItemModel { UserId = "USR-001", UserName = "doc_opd", DisplayName = "张建国", EmployeeNo = "EMP001", RoleCode = "ROLE-OPD-DOC", RoleName = "门诊医生", DeptName = "心血管内科门诊", PrescribeLevel = 2, SurgeryLevel = 1, EmrSignLevel = 2, IsActive = true });
        UsersList.Add(new UserAdminItemModel { UserId = "USR-002", UserName = "doc_res", DisplayName = "李华", EmployeeNo = "EMP002", RoleCode = "ROLE-IPD-RESIDENT", RoleName = "住院医师", DeptName = "心血管内科住院一科", PrescribeLevel = 1, SurgeryLevel = 1, EmrSignLevel = 1, IsActive = true });
        UsersList.Add(new UserAdminItemModel { UserId = "USR-003", UserName = "doc_att", DisplayName = "王磊", EmployeeNo = "EMP003", RoleCode = "ROLE-IPD-ATTENDING", RoleName = "主治医师", DeptName = "心血管内科住院一科", PrescribeLevel = 2, SurgeryLevel = 2, EmrSignLevel = 2, IsActive = true });
        UsersList.Add(new UserAdminItemModel { UserId = "USR-004", UserName = "nurse_w1", DisplayName = "陈劳", EmployeeNo = "EMP004", RoleCode = "ROLE-WARD-NURSE", RoleName = "病区护士", DeptName = "内科一病区", PrescribeLevel = 0, SurgeryLevel = 0, EmrSignLevel = 0, IsActive = true });
        UsersList.Add(new UserAdminItemModel { UserId = "USR-005", UserName = "nurse_w2", DisplayName = "赵敏", EmployeeNo = "EMP005", RoleCode = "ROLE-WARD-NURSE", RoleName = "病区护士", DeptName = "内科二病区", PrescribeLevel = 0, SurgeryLevel = 0, EmrSignLevel = 0, IsActive = true });
        UsersList.Add(new UserAdminItemModel { UserId = "USR-006", UserName = "phm_user", DisplayName = "孙杰", EmployeeNo = "EMP006", RoleCode = "ROLE-PHARMACIST", RoleName = "药剂师", DeptName = "药剂科中心药房", PrescribeLevel = 0, SurgeryLevel = 0, EmrSignLevel = 0, IsActive = true });
        UsersList.Add(new UserAdminItemModel { UserId = "USR-007", UserName = "cashier_01", DisplayName = "刘洋", EmployeeNo = "EMP007", RoleCode = "ROLE-CASHIER", RoleName = "收费员", DeptName = "门急诊财务处", PrescribeLevel = 0, SurgeryLevel = 0, EmrSignLevel = 0, IsActive = true });

        RolesList.Clear();
        RolesList.Add(new RoleAdminItemModel { RoleId = "ROLE-ADMIN", RoleCode = "ROLE-ADMIN", RoleName = "超级管理员", DataScope = "ALL_CAMPUS", UserCount = 1, IsActive = true, Description = "全院最高管理角色，拥有系统配置、用户角色分配、安全审计及全模块穿透访问权限。" });
        RolesList.Add(new RoleAdminItemModel { RoleId = "ROLE-OPD-DOC", RoleCode = "ROLE-OPD-DOC", RoleName = "门诊医生", DataScope = "DEPT_ONLY", UserCount = 1, IsActive = true, Description = "门诊专科接诊、处方及检查开立、门诊病历书写及电子签名、危急值核收响应。" });
        RolesList.Add(new RoleAdminItemModel { RoleId = "ROLE-IPD-RESIDENT", RoleCode = "ROLE-IPD-RESIDENT", RoleName = "住院医师", DataScope = "MED_GROUP", UserCount = 1, IsActive = true, Description = "住院管床日常查房、住院病程录入、下达常规临时医嘱并交主治医师复核审签。" });
        RolesList.Add(new RoleAdminItemModel { RoleId = "ROLE-IPD-ATTENDING", RoleCode = "ROLE-IPD-ATTENDING", RoleName = "主治医师", DataScope = "DEPT_ONLY", UserCount = 1, IsActive = true, Description = "三级查房终审、高风险药物/限制级抗生素授权、手术双签审批、出院六严关终审放行。" });
        RolesList.Add(new RoleAdminItemModel { RoleId = "ROLE-WARD-NURSE", RoleCode = "ROLE-WARD-NURSE", RoleName = "病区护士", DataScope = "WARD_ONLY", UserCount = 2, IsActive = true, Description = "病区床位维护、跨病区借床扫码执行、危急值护士端签收、医嘱扫码执行与核销。" });
        RolesList.Add(new RoleAdminItemModel { RoleId = "ROLE-PHARMACIST", RoleCode = "ROLE-PHARMACIST", RoleName = "药剂师", DataScope = "PHARMACY_ONLY", UserCount = 1, IsActive = true, Description = "处方前置合理用药审查、过敏史/皮试强阻断拦截核验、摆药复核扫码出库。" });
        RolesList.Add(new RoleAdminItemModel { RoleId = "ROLE-CASHIER", RoleCode = "ROLE-CASHIER", RoleName = "收费员", DataScope = "FINANCE_ONLY", UserCount = 1, IsActive = true, Description = "门急诊收挂号费、住院预交金交退、出院医保结算与财务核算。" });

        PermissionMatrixList.Clear();
        PermissionMatrixList.Add(new PermissionMatrixItemModel { ResourceKey = "OPD.Prescription.Sign", ResourceName = "处方签名与下达 (GATE-05)", ResourceType = "按钮控件", Category = "门诊临床", PolicyAdmin = "FullAccess", PolicyDoc = "FullAccess", PolicyNurse = "None", PolicyPharm = "ReadOnly" });
        PermissionMatrixList.Add(new PermissionMatrixItemModel { ResourceKey = "EMR.Export.OfdArchive", ResourceName = "病历GB/T 33190归档导出 (GATE-04)", ResourceType = "功能动作", Category = "电子病历", PolicyAdmin = "FullAccess", PolicyDoc = "FullAccess", PolicyNurse = "None", PolicyPharm = "None" });
        PermissionMatrixList.Add(new PermissionMatrixItemModel { ResourceKey = "WARD.Bed.BorrowManage", ResourceName = "跨病区借床入科办理 (GATE-02)", ResourceType = "工作流程", Category = "病区护理", PolicyAdmin = "FullAccess", PolicyDoc = "ReadOnly", PolicyNurse = "FullAccess", PolicyPharm = "None" });
        PermissionMatrixList.Add(new PermissionMatrixItemModel { ResourceKey = "CRITICAL.DualSign.Acknowledge", ResourceName = "危急值医生护士双签收", ResourceType = "安全红线", Category = "医疗质控", PolicyAdmin = "FullAccess", PolicyDoc = "FullAccess", PolicyNurse = "FullAccess", PolicyPharm = "ReadOnly" });
        PermissionMatrixList.Add(new PermissionMatrixItemModel { ResourceKey = "DISCHARGE.SixGates.Audit", ResourceName = "出院六严关终审放行 (GATE-07)", ResourceType = "终审关卡", Category = "住院结算", PolicyAdmin = "FullAccess", PolicyDoc = "FullAccess", PolicyNurse = "None", PolicyPharm = "None" });
        PermissionMatrixList.Add(new PermissionMatrixItemModel { ResourceKey = "PHARMACY.Dispense.Confirm", ResourceName = "药房配发扫码核销", ResourceType = "业务执行", Category = "药剂物流", PolicyAdmin = "FullAccess", PolicyDoc = "ReadOnly", PolicyNurse = "ReadOnly", PolicyPharm = "FullAccess" });
        PermissionMatrixList.Add(new PermissionMatrixItemModel { ResourceKey = "SYS.Security.RbacMaintain", ResourceName = "系统用户与角色权限维护", ResourceType = "管理后台", Category = "系统安全", PolicyAdmin = "FullAccess", PolicyDoc = "None", PolicyNurse = "None", PolicyPharm = "None" });
    }

    public WhoSurgeryCheckModel WhoSurgeryCheck { get => _whoSurgeryCheck; set => SetField(ref _whoSurgeryCheck, value); }
    public CriticalValueAlertModel CriticalValueAlert { get => _criticalValueAlert; set => SetField(ref _criticalValueAlert, value); }
    public DischargeCheckDetailsModel DischargeCheckDetails { get => _dischargeCheckDetails; set => SetField(ref _dischargeCheckDetails, value); }
    public DynamicFormTemplateModel? SelectedFormTemplate { get => _selectedFormTemplate; set => SetField(ref _selectedFormTemplate, value); }

    public PatientQueueItemModel? SelectedPatient
    {
        get => _selectedPatient;
        set
        {
            if (SetField(ref _selectedPatient, value) && value != null)
            {
                // Update selection state across queue
                foreach (var p in PatientQueue)
                {
                    p.IsSelected = (p.QueueNo == value.QueueNo);
                }

                CriticalValueAlert.PatientName = value.PatientName;
                WhoSurgeryCheck.PatientName = value.PatientName;

                // Dynamically update clinical data for this patient
                LoadPatientClinicalContext(value);
            }
        }
    }

    private void LoadPatientClinicalContext(PatientQueueItemModel p)
    {
        EmrWorkspace.PatientName = p.PatientName;
        EmrWorkspace.Gender = p.Gender;
        EmrWorkspace.Age = p.Age;
        EmrWorkspace.DocNo = $"DOC-OPD-{p.VisitNo}";
        EmrWorkspace.ChiefComplaint = p.ChiefComplaint;

        Prescriptions.Clear();
        MedTechOrders.Clear();

        if (p.QueueNo == "001") // 周建强 (男, 41岁)
        {
            EmrWorkspace.PresentIllness = "患者于3天前过度劳累后出现心前区憋闷不适，伴阵发性心悸，每次持续约3-5分钟。半天前登山后胸痛症状加重，放射至左肩背部，伴大汗、气促，无晕厥。遂急来就诊。";
            EmrWorkspace.Diagnosis = "冠状动脉粥样硬化性心脏病 (I25.101)；劳力性心绞痛 (CCS II级)";
            EmrWorkspace.ProstateExam = "轻度肥大，质地韧，中央沟浅，未触及明显硬结。";
            EmrWorkspace.PregnancyCount = 0;

            Prescriptions.Add(new PrescriptionItemModel
            {
                Seq = 1, DrugName = "注射用青霉素钠", Spec = "80万单位/支", Dose = "80万U", Freq = "bid (每12小时一次)",
                Route = "静脉滴注", Quantity = "6支", Amount = "¥75.00", SkinTestStatus = "❌ 皮试阳性 (强阻断)",
                SkinTestClass = "Danger", AuditStatus = "已拦截 (过敏史冲突)", AuditClass = "Danger", IsBlocked = true
            });
            Prescriptions.Add(new PrescriptionItemModel
            {
                Seq = 2, DrugName = "0.9% 氯化钠注射液", Spec = "500ml/瓶", Dose = "500ml", Freq = "qd (每日一次)",
                Route = "静脉滴注", Quantity = "2瓶", Amount = "¥11.00", SkinTestStatus = "无需皮试",
                SkinTestClass = "Secondary", AuditStatus = "审核通过", AuditClass = "Success", IsBlocked = false
            });
            Prescriptions.Add(new PrescriptionItemModel
            {
                Seq = 3, DrugName = "阿司匹林肠溶片", Spec = "100mg*30片/盒", Dose = "100mg", Freq = "qn (每晚一次)",
                Route = "口服", Quantity = "1盒", Amount = "¥22.50", SkinTestStatus = "无需皮试",
                SkinTestClass = "Secondary", AuditStatus = "审核通过", AuditClass = "Success", IsBlocked = false
            });
            Prescriptions.Add(new PrescriptionItemModel
            {
                Seq = 4, DrugName = "硫酸氢氯吡格雷片", Spec = "75mg*7片/盒", Dose = "75mg", Freq = "qd (每日一次)",
                Route = "口服", Quantity = "2盒", Amount = "¥56.80", SkinTestStatus = "无需皮试",
                SkinTestClass = "Secondary", AuditStatus = "审核通过", AuditClass = "Success", IsBlocked = false
            });

            MedTechOrders.Add(new MedTechOrderItemModel
            {
                RequestNo = "MTC20261105001", ItemName = "全血细胞分析与五分类 (血常规)", DeptName = "医学检验科 (DEPT-MTC-LAB)",
                ExecLocation = "门诊标本核收室 (紫头EDTA管)", Fee = "¥25.00", Status = "已开单待采样", StatusClass = "Warning", IsFeeLocked = false
            });
            MedTechOrders.Add(new MedTechOrderItemModel
            {
                RequestNo = "MTC20261105002", ItemName = "12导联同步心电图 (常规ECG)", DeptName = "功能检查科 (DEPT-MTC-ECG)",
                ExecLocation = "门诊2楼 心电图1号检查室", Fee = "¥35.00", Status = "已核收确费 (已锁单)", StatusClass = "Success", IsFeeLocked = true
            });
            MedTechOrders.Add(new MedTechOrderItemModel
            {
                RequestNo = "MTC20261105003", ItemName = "心肌坏死三项 (hs-cTnI + CK-MB + Myo)", DeptName = "医学检验科 (DEPT-MTC-LAB)",
                ExecLocation = "急诊检验机房 (黄头促凝管)", Fee = "¥115.00", Status = "已出报告", StatusClass = "Success", IsFeeLocked = true
            });
        }
        else if (p.QueueNo == "002") // 王大明 (男, 58岁)
        {
            EmrWorkspace.PresentIllness = "患者近1周来在重体力活动后反复出现胸骨后闷痛，每次持续约2-3分钟，坐下休息后好转，今早再次发作。";
            EmrWorkspace.Diagnosis = "初发劳力型心绞痛 (I20.000)；高脂血症";
            EmrWorkspace.ProstateExam = "轻度肥大，中央沟存在，无结节。";
            EmrWorkspace.PregnancyCount = 0;

            Prescriptions.Add(new PrescriptionItemModel
            {
                Seq = 1, DrugName = "单硝酸异山梨酯缓释片", Spec = "40mg*7片/盒", Dose = "40mg", Freq = "qd (早晨一次)", Route = "口服",
                Quantity = "2盒", Amount = "¥38.20", SkinTestStatus = "无需皮试", SkinTestClass = "Secondary",
                AuditStatus = "审核通过", AuditClass = "Success", IsBlocked = false
            });
            Prescriptions.Add(new PrescriptionItemModel
            {
                Seq = 2, DrugName = "阿托伐他汀钙片", Spec = "20mg*7片/盒", Dose = "20mg", Freq = "qn (每晚一次)", Route = "口服",
                Quantity = "2盒", Amount = "¥39.60", SkinTestStatus = "无需皮试", SkinTestClass = "Secondary",
                AuditStatus = "审核通过", AuditClass = "Success", IsBlocked = false
            });

            MedTechOrders.Add(new MedTechOrderItemModel
            {
                RequestNo = "MTC20261105004", ItemName = "平板运动负荷心电图 (TET)", DeptName = "功能检查科 (DEPT-MTC-ECG)",
                ExecLocation = "门诊2楼 运动平板室", Fee = "¥160.00", Status = "已开单待采样", StatusClass = "Warning", IsFeeLocked = false
            });
            MedTechOrders.Add(new MedTechOrderItemModel
            {
                RequestNo = "MTC20261105005", ItemName = "血脂全套五项 (TC/TG/HDL/LDL/apoA)", DeptName = "医学检验科 (DEPT-MTC-LAB)",
                ExecLocation = "门诊标本核收室", Fee = "¥48.00", Status = "已核收待检", StatusClass = "Primary", IsFeeLocked = false
            });
        }
        else if (p.QueueNo == "003") // 赵淑珍 (女, 63岁 - GATE-03 女性专科联动)
        {
            EmrWorkspace.PresentIllness = "患者半月来反复于夜间及晨起出现心慌、头晕，测自测血压波动于 145-160/85-95 mmHg，平时无胸痛及晕厥。既往阿司匹林胃肠反应。";
            EmrWorkspace.Diagnosis = "原发性高血压2级 (很高危)；心律失常 (室性期前收缩)";
            EmrWorkspace.ProstateExam = "不适用 (女性患者 - GATE-03锁定)";
            EmrWorkspace.PregnancyCount = 2; // G2P2

            Prescriptions.Add(new PrescriptionItemModel
            {
                Seq = 1, DrugName = "缬沙坦氢氯噻嗪片", Spec = "80mg/12.5mg*7片/盒", Dose = "1片", Freq = "qd (早晨一次)", Route = "口服",
                Quantity = "2盒", Amount = "¥42.00", SkinTestStatus = "无需皮试", SkinTestClass = "Secondary",
                AuditStatus = "审核通过", AuditClass = "Success", IsBlocked = false
            });
            Prescriptions.Add(new PrescriptionItemModel
            {
                Seq = 2, DrugName = "酒石酸美托洛尔片", Spec = "25mg*20片/盒", Dose = "25mg", Freq = "bid (每12小时一次)", Route = "口服",
                Quantity = "1盒", Amount = "¥18.50", SkinTestStatus = "无需皮试", SkinTestClass = "Secondary",
                AuditStatus = "审核通过", AuditClass = "Success", IsBlocked = false
            });

            MedTechOrders.Add(new MedTechOrderItemModel
            {
                RequestNo = "MTC20261105006", ItemName = "24小时动态血压监测 (ABPM)", DeptName = "功能检查科 (DEPT-MTC-ECG)",
                ExecLocation = "门诊2楼 动态血压室", Fee = "¥120.00", Status = "已预约", StatusClass = "Primary", IsFeeLocked = false
            });
            MedTechOrders.Add(new MedTechOrderItemModel
            {
                RequestNo = "MTC20261105007", ItemName = "血清电解质分析 (K/Na/Cl/Ca)", DeptName = "医学检验科 (DEPT-MTC-LAB)",
                ExecLocation = "门诊标本核收室", Fee = "¥38.00", Status = "待采样", StatusClass = "Warning", IsFeeLocked = false
            });
        }
        else if (p.QueueNo == "004") // 孙建国 (男, 49岁)
        {
            EmrWorkspace.PresentIllness = "患者3天前单位体检发现血压达 152/96 mmHg，平时无明显头晕心悸，今来门诊进一步评估。";
            EmrWorkspace.Diagnosis = "原发性高血压1级 (低危)";
            EmrWorkspace.ProstateExam = "未见明显硬结与触痛。";
            EmrWorkspace.PregnancyCount = 0;

            Prescriptions.Add(new PrescriptionItemModel
            {
                Seq = 1, DrugName = "苯磺酸氨氯地平片", Spec = "5mg*7片/盒", Dose = "5mg", Freq = "qd (早晨一次)", Route = "口服",
                Quantity = "2盒", Amount = "¥24.80", SkinTestStatus = "无需皮试", SkinTestClass = "Secondary",
                AuditStatus = "审核通过", AuditClass = "Success", IsBlocked = false
            });

            MedTechOrders.Add(new MedTechOrderItemModel
            {
                RequestNo = "MTC20261105008", ItemName = "双肾及肾上腺彩超", DeptName = "超声医学科 (DEPT-MTC-US)",
                ExecLocation = "门诊3楼 彩超2诊室", Fee = "¥140.00", Status = "待就诊", StatusClass = "Warning", IsFeeLocked = false
            });
        }
        else if (p.QueueNo == "005") // 钱振华 (男, 52岁)
        {
            EmrWorkspace.PresentIllness = "患者3个月前因急性心梗行冠脉造影及支架植入术(PCI)，术后规则抗血小板及调脂治疗，今来复查配药。";
            EmrWorkspace.Diagnosis = "冠脉支架植入术后状态 (Z95.500)；高脂血症";
            EmrWorkspace.ProstateExam = "前列腺正常。";
            EmrWorkspace.PregnancyCount = 0;

            Prescriptions.Add(new PrescriptionItemModel
            {
                Seq = 1, DrugName = "阿托伐他汀钙片", Spec = "20mg*7片/盒", Dose = "20mg", Freq = "qn (每晚一次)", Route = "口服",
                Quantity = "2盒", Amount = "¥39.60", SkinTestStatus = "无需皮试", SkinTestClass = "Secondary",
                AuditStatus = "审核通过", AuditClass = "Success", IsBlocked = false
            });
            Prescriptions.Add(new PrescriptionItemModel
            {
                Seq = 2, DrugName = "硫酸氢氯吡格雷片", Spec = "75mg*7片/盒", Dose = "75mg", Freq = "qd (每日一次)", Route = "口服",
                Quantity = "2盒", Amount = "¥56.80", SkinTestStatus = "无需皮试", SkinTestClass = "Secondary",
                AuditStatus = "审核通过", AuditClass = "Success", IsBlocked = false
            });

            MedTechOrders.Add(new MedTechOrderItemModel
            {
                RequestNo = "MTC20261105009", ItemName = "凝血功能四项 (PT/APTT/TT/FIB)", DeptName = "医学检验科 (DEPT-MTC-LAB)",
                ExecLocation = "门诊标本核收室", Fee = "¥45.00", Status = "已出报告", StatusClass = "Success", IsFeeLocked = true
            });
            MedTechOrders.Add(new MedTechOrderItemModel
            {
                RequestNo = "MTC20261105010", ItemName = "肝肾功能与血脂全套", DeptName = "医学检验科 (DEPT-MTC-LAB)",
                ExecLocation = "门诊标本核收室", Fee = "¥88.00", Status = "已出报告", StatusClass = "Success", IsFeeLocked = true
            });
        }

        SystemNotice = $"已就绪: 患者 [{p.PatientName}] ({p.Gender}, {p.Age}岁) - 就诊号: {p.VisitNo} | 门禁排他规则已同步加载";
    }

    public EmrEditorWorkspaceModel EmrWorkspace { get => _emrWorkspace; set => SetField(ref _emrWorkspace, value); }

    public ObservableCollection<PatientQueueItemModel> PatientQueue { get; set; }
    public ObservableCollection<PrescriptionItemModel> Prescriptions { get; set; }
    public ObservableCollection<MedTechOrderItemModel> MedTechOrders { get; set; }
    public ObservableCollection<BedCardModel> BedCards { get; set; }
    public ObservableCollection<InpatientRecordModel> InpatientRecords { get; set; }
    public ObservableCollection<PharmacyDispenseModel> PharmacyRecords { get; set; }
    public ObservableCollection<OfdArchiveRecordModel> OfdArchiveRecords { get; set; }
    public ObservableCollection<DynamicFormTemplateModel> DynamicFormTemplates { get; set; }
    public ObservableCollection<TimelineEventModel> TimelineEvents { get; set; }
    public ObservableCollection<DrugCatalogItemModel> AvailableDrugsCatalog { get; set; }

    public void SelectNextPatient()
    {
        if (PatientQueue.Count == 0) return;
        var currentIndex = SelectedPatient != null ? PatientQueue.IndexOf(SelectedPatient) : -1;
        var nextIndex = (currentIndex + 1) % PatientQueue.Count;
        SelectedPatient = PatientQueue[nextIndex];
        SystemNotice = $"已顺呼下一位就诊患者: {SelectedPatient.PatientName} ({SelectedPatient.QueueNo})";
    }

    public void SimulateSignOfd()
    {
        EmrWorkspace.OfdStatus = $"GB/T 33190-2016 OFD 审签已完成 (Hash: {DateTime.Now:yyyyMMddHHmmss})";
        SystemNotice = $"国标 OFD 审签成功，已生成防篡改电子双签封包，归档单号: {EmrWorkspace.DocNo}";
    }

    public void AddPrescriptionFromCatalog(DrugCatalogItemModel drug)
    {
        var seq = Prescriptions.Count + 1;
        var isPenicillin = drug.DrugName.Contains("青霉素");
        var hasAllergy = SelectedPatient?.HasAllergyAlert == true;

        if (isPenicillin && hasAllergy)
        {
            Prescriptions.Add(new PrescriptionItemModel
            {
                Seq = seq, DrugName = drug.DrugName, Spec = drug.Spec, Dose = drug.DefaultDose, Freq = drug.DefaultFreq,
                Route = drug.DefaultRoute, Quantity = "1", Amount = drug.UnitPrice,
                SkinTestStatus = "❌ 皮试阳性 (强阻断)", SkinTestClass = "Danger",
                AuditStatus = "已拦截 (过敏史冲突)", AuditClass = "Danger", IsBlocked = true
            });
            SystemNotice = $"🛡️ GATE-05 门禁触发：药品 [{drug.DrugName}] 与患者过敏史冲突且皮试阳性，发药与执行接口 100% 拒绝提交！";
        }
        else
        {
            Prescriptions.Add(new PrescriptionItemModel
            {
                Seq = seq, DrugName = drug.DrugName, Spec = drug.Spec, Dose = drug.DefaultDose, Freq = drug.DefaultFreq,
                Route = drug.DefaultRoute, Quantity = "2", Amount = drug.UnitPrice,
                SkinTestStatus = drug.IsSkinTestRequired ? "待皮试" : "无需皮试",
                SkinTestClass = drug.IsSkinTestRequired ? "Warning" : "Secondary",
                AuditStatus = "审核通过", AuditClass = "Success", IsBlocked = false
            });
            SystemNotice = $"处方开立成功：已新增 [{drug.DrugName}] ({drug.UnitPrice})，前置合理用药审方通过！";
        }

        IsAddPrescriptionDialogOpen = false;
    }
}

public class PatientQueueItemModel : ClientModelBase
{
    private bool _isSelected;

    public string QueueNo { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public int Age { get; set; }
    public string MedicalRecordNo { get; set; } = string.Empty;
    public string VisitNo { get; set; } = string.Empty;
    public string FeeType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusClass { get; set; } = "Primary";
    public string ChiefComplaint { get; set; } = string.Empty;
    public string AllergyTags { get; set; } = string.Empty;
    public bool HasAllergyAlert { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetField(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(CardBorderBrush));
                OnPropertyChanged(nameof(CardBackground));
            }
        }
    }

    public string CardBorderBrush => IsSelected ? "#165DFF" : "Transparent";
    public string CardBackground => IsSelected ? "#1A253A" : "Transparent";
}

public class PrescriptionItemModel : ClientModelBase
{
    public int Seq { get; set; }
    public string DrugName { get; set; } = string.Empty;
    public string Spec { get; set; } = string.Empty;
    public string Dose { get; set; } = string.Empty;
    public string Freq { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string Quantity { get; set; } = string.Empty;
    public string Amount { get; set; } = string.Empty;
    public string SkinTestStatus { get; set; } = "无需皮试";
    public string SkinTestClass { get; set; } = "Secondary";
    public string AuditStatus { get; set; } = "审核通过";
    public string AuditClass { get; set; } = "Success";
    public bool IsBlocked { get; set; }
}

public class MedTechOrderItemModel : ClientModelBase
{
    public string RequestNo { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string DeptName { get; set; } = string.Empty;
    public string ExecLocation { get; set; } = string.Empty;
    public string Fee { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusClass { get; set; } = "Primary";
    public bool IsFeeLocked { get; set; }
}

public class BedCardModel : ClientModelBase
{
    public string BedCode { get; set; } = string.Empty;
    public string BedType { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public int Age { get; set; }
    public string AdmissionNo { get; set; } = string.Empty;
    public string AdmittingDeptName { get; set; } = string.Empty;
    public string NursingWardName { get; set; } = string.Empty;
    public string NursingLevel { get; set; } = string.Empty;
    public string Diagnosis { get; set; } = string.Empty;
    public string DailyFee { get; set; } = string.Empty;
    public bool IsBorrowed { get; set; }
    public bool IsOccupied { get; set; }
    public string CardTheme { get; set; } = "Standard";
}

public class InpatientRecordModel : ClientModelBase
{
    public string AdmissionNo { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string BedCode { get; set; } = string.Empty;
    public string AdmittingDept { get; set; } = string.Empty;
    public string NursingWard { get; set; } = string.Empty;
    public string NursingLevel { get; set; } = string.Empty;
    public string Diagnosis { get; set; } = string.Empty;
    public string AttendingDoctor { get; set; } = string.Empty;
    public string AdmissionDate { get; set; } = string.Empty;
    public string DailySummary { get; set; } = string.Empty;
    public bool IsCritical { get; set; }
    public bool IsDischargeReady { get; set; }
}

public class PharmacyDispenseModel : ClientModelBase
{
    public string PrescriptionNo { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string DrugSummary { get; set; } = string.Empty;
    public string DispenseStatus { get; set; } = string.Empty;
    public string PharmacistName { get; set; } = string.Empty;
    public string SafetyAudit { get; set; } = string.Empty;
    public bool IsAllergyBlocked { get; set; }
    public bool CanDispense { get; set; }
}

public class OfdArchiveRecordModel : ClientModelBase
{
    public string ArchiveNo { get; set; } = string.Empty;
    public string DocType { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string DeptName { get; set; } = string.Empty;
    public string OfdStandard { get; set; } = string.Empty;
    public string ZipEntries { get; set; } = string.Empty;
    public string HashSha256 { get; set; } = string.Empty;
    public string SignatureStatus { get; set; } = string.Empty;
    public string ArchiveTime { get; set; } = string.Empty;
    public bool IsVerified { get; set; }
}

public class DynamicFormTemplateModel : ClientModelBase
{
    public string TemplateCode { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string DeptScope { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public int FieldCount { get; set; }
    public int GenderRuleCount { get; set; }
    public string GenderRuleDescription { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class EmrEditorWorkspaceModel : ClientModelBase
{
    private string _docNo = "DOC-OPD-20261105001";
    private string _docTitle = "心血管内科门诊初诊病历";
    private string _patientName = "周建强";
    private string _gender = "男";
    private int _age = 41;
    private string _chiefComplaint = "阵发性心前区隐痛、心悸3天，活动后加重半天。";
    private string _presentIllness = "患者于3天前因过度劳累后出现心前区憋闷不适，伴阵发性心悸，每次持续约3-5分钟，休息后可逐渐缓解。半天前快步登山后胸痛症状加重，放射至左肩背部，伴大汗、气促，无晕厥及咯血。遂急来我院门诊就诊。";
    private string _diagnosis = "冠状动脉粥样硬化性心脏病 (I25.101)；劳力性心绞痛 (CCS II级)";
    private string _prostateExam = "轻度肥大，质地韧，中央沟浅，未触及明显硬结。";
    private int _pregnancyCount = 0;
    private string _qcStatus = "甲级病历 (评分: 98分)";
    private string _ofdStandard = "GB/T 33190-2016";
    private string _ofdStatus = "已生成电子防篡改签名封包";
    private string? _ofdNo;
    private bool _isBusy;

    public string DocNo { get => _docNo; set => SetField(ref _docNo, value); }
    public string DocTitle { get => _docTitle; set => SetField(ref _docTitle, value); }
    public string PatientName { get => _patientName; set => SetField(ref _patientName, value); }
    public string Gender { get => _gender; set => SetField(ref _gender, value); }
    public int Age { get => _age; set => SetField(ref _age, value); }
    public string ChiefComplaint { get => _chiefComplaint; set => SetField(ref _chiefComplaint, value); }
    public string PresentIllness { get => _presentIllness; set => SetField(ref _presentIllness, value); }
    public string Diagnosis { get => _diagnosis; set => SetField(ref _diagnosis, value); }
    public string ProstateExam { get => _prostateExam; set => SetField(ref _prostateExam, value); }
    public int PregnancyCount { get => _pregnancyCount; set => SetField(ref _pregnancyCount, value); }
    public string QcStatus { get => _qcStatus; set => SetField(ref _qcStatus, value); }
    public string OfdStandard { get => _ofdStandard; set => SetField(ref _ofdStandard, value); }
    public string OfdStatus { get => _ofdStatus; set => SetField(ref _ofdStatus, value); }
    public string? OfdNo { get => _ofdNo; set => SetField(ref _ofdNo, value); }
    public bool IsBusy { get => _isBusy; set => SetField(ref _isBusy, value); }
}

public class TimelineEventModel : ClientModelBase
{
    public string EventTime { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string EventTitle { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string DoctorOrOperator { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string BadgeClass { get; set; } = "Primary";
    public string ResultIcon { get; set; } = "📄";
}

public class WhoSurgeryCheckModel : ClientModelBase
{
    public string PatientName { get; set; } = "周建强";
    public string AdmissionNo { get; set; } = "ADM20261105001";
    public string SurgeryName { get; set; } = "经皮冠状动脉造影及支架植入术 (PCI)";
    public string AnesthesiaType { get; set; } = "局部浸润麻醉 (1% 利多卡因)";
    public string Surgeon { get; set; } = "张建国 (主刀医师)";
    public string Anesthesiologist { get; set; } = "陈建安 (副主任医师)";
    public string ScrubNurse { get; set; } = "王玉玲 (器械护士)";
    public string CirculatingNurse { get; set; } = "赵彩霞 (巡回护士)";
    public bool SignInIdentityOk { get; set; } = true;
    public bool SignInSiteMarked { get; set; } = true;
    public bool SignInAnesthesiaSafetyCheck { get; set; } = true;
    public bool TimeOutAllTeamAgreed { get; set; } = true;
    public bool TimeOutAntibioticGiven { get; set; } = true;
    public bool TimeOutImagingDisplayed { get; set; } = true;
    public bool SignOutInstrumentCountMatched { get; set; } = true;
    public bool SignOutSpecimenLabeled { get; set; } = true;
    public string UdiImplantBarcode { get; set; } = "(01)06941234567890(17)281231(10)LOT20261105";
    public string CheckStatus { get; set; } = "三方核对全通过 · 满足离室条件";
}

public class CriticalValueAlertModel : ClientModelBase
{
    private string _doctorAck = "未签收";
    private string _nurseAck = "未签收";

    public string ReportId { get; set; } = "CRIT-LAB-2026110509";
    public string PatientName { get; set; } = "周建强";
    public string BedCode { get; set; } = "02床 (借床)";
    public string TestItem { get; set; } = "超敏肌钙蛋白 I (hs-cTnI)";
    public string MeasuredValue { get; set; } = "12.85 ng/mL";
    public string ReferenceRange { get; set; } = "0.00 ~ 0.04 ng/mL";
    public string AlertLevel { get; set; } = "超危急值 (超高警戒线 300 倍)";
    public string ReportingTime { get; set; } = "2026-11-06 08:05:12";
    public string ReportingDept { get; set; } = "医学检验科 急诊急救生化室";
    public string DoctorAck { get => _doctorAck; set => SetField(ref _doctorAck, value); }
    public string NurseAck { get => _nurseAck; set => SetField(ref _nurseAck, value); }
    public bool IsAcknowledged => DoctorAck.Contains("已") && NurseAck.Contains("已");
    public string ClinicalAction { get; set; } = "立即通知主治医生查房、心电监护、紧急开辟导管室 PCI 绿色通道";
}

public class DischargeCheckDetailsModel : ClientModelBase
{
    public string PatientName { get; set; } = "钱学森";
    public string AdmissionNo { get; set; } = "ADM20261101006";
    public string BedCode { get; set; } = "06床";
    public bool OrdersAllStopped { get; set; } = true;
    public bool MedTechReportsAllSigned { get; set; } = true;
    public bool DischargeMedsDispensed { get; set; } = true;
    public bool EmrAllThreeLevelsSigned { get; set; } = true;
    public bool MedicalInsuranceSettled { get; set; } = true;
    public bool PrepaymentCleared { get; set; } = true;
    public decimal PrepaymentAmount { get; set; } = 15000.00m;
    public decimal ActualSelfPay { get; set; } = 10520.50m;
    public decimal RefundAmount { get; set; } = 4479.50m;
    public bool AllPassed => OrdersAllStopped && MedTechReportsAllSigned && DischargeMedsDispensed && EmrAllThreeLevelsSigned && MedicalInsuranceSettled && PrepaymentCleared;
}

public class DrugCatalogItemModel : ClientModelBase
{
    public string DrugCode { get; set; } = string.Empty;
    public string DrugName { get; set; } = string.Empty;
    public string Spec { get; set; } = string.Empty;
    public string DefaultDose { get; set; } = string.Empty;
    public string DefaultFreq { get; set; } = string.Empty;
    public string DefaultRoute { get; set; } = string.Empty;
    public string UnitPrice { get; set; } = string.Empty;
    public string StockQuantity { get; set; } = string.Empty;
    public bool IsSkinTestRequired { get; set; }
    public bool IsHighRisk { get; set; }
}

public class UserAdminItemModel : ClientModelBase
{
    private bool _isActive = true;
    private string _displayName = string.Empty;
    private string _roleName = string.Empty;
    private string _deptName = string.Empty;
    private int _prescribeLevel = 1;
    private int _surgeryLevel = 1;
    private int _emrSignLevel = 1;

    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get => _displayName; set => SetField(ref _displayName, value); }
    public string EmployeeNo { get; set; } = string.Empty;
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get => _roleName; set => SetField(ref _roleName, value); }
    public string DeptName { get => _deptName; set => SetField(ref _deptName, value); }
    public int PrescribeLevel { get => _prescribeLevel; set => SetField(ref _prescribeLevel, value); }
    public int SurgeryLevel { get => _surgeryLevel; set => SetField(ref _surgeryLevel, value); }
    public int EmrSignLevel { get => _emrSignLevel; set => SetField(ref _emrSignLevel, value); }

    public string PrescribeLevelText => PrescribeLevel switch { 3 => "III级 (特殊抗生素)", 2 => "II级 (限制级)", 1 => "I级 (普通)", _ => "无处方权" };
    public string SurgeryLevelText => SurgeryLevel switch { 4 => "IV级 (重大手术)", 3 => "III级 (大型手术)", 2 => "II级 (中型手术)", 1 => "I级 (微创/门诊)", _ => "无手术权" };
    public string EmrSignLevelText => EmrSignLevel switch { 3 => "III级 (终审归档)", 2 => "II级 (主治审签)", 1 => "I级 (住院书写)", _ => "无审签权" };

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetField(ref _isActive, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusClass));
            }
        }
    }

    public string StatusText => IsActive ? "正常" : "已停用";
    public string StatusClass => IsActive ? "Success" : "Danger";
}

public class RoleAdminItemModel : ClientModelBase
{
    private string _roleName = string.Empty;
    private string _dataScope = string.Empty;
    private bool _isActive = true;
    private int _userCount;

    public string RoleId { get; set; } = string.Empty;
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get => _roleName; set => SetField(ref _roleName, value); }
    public string DataScope { get => _dataScope; set => SetField(ref _dataScope, value); }
    public string Description { get; set; } = string.Empty;
    public int UserCount { get => _userCount; set => SetField(ref _userCount, value); }
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetField(ref _isActive, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusClass));
            }
        }
    }

    public string StatusText => IsActive ? "正常启用" : "已禁用";
    public string StatusClass => IsActive ? "Success" : "Danger";
    public string ScopeDescription => DataScope switch
    {
        "ALL_CAMPUS" => "全院区级 (跨院区穿透)",
        "CAMPUS_ONLY" => "所属院区级",
        "DEPT_ONLY" => "所属科室级",
        "WARD_ONLY" => "所属护理病区",
        "MED_GROUP" => "所属医疗组",
        "PHARMACY_ONLY" => "药房库区",
        "FINANCE_ONLY" => "财务结算中心",
        _ => DataScope
    };
}

public class PermissionMatrixItemModel : ClientModelBase
{
    private string _policyAdmin = "FullAccess";
    private string _policyDoc = "FullAccess";
    private string _policyNurse = "None";
    private string _policyPharm = "None";

    public string ResourceKey { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
    public string ResourceType { get; set; } = "UI组件";
    public string Category { get; set; } = "核心门禁";

    public string PolicyAdmin { get => _policyAdmin; set => SetField(ref _policyAdmin, value); }
    public string PolicyDoc { get => _policyDoc; set => SetField(ref _policyDoc, value); }
    public string PolicyNurse { get => _policyNurse; set => SetField(ref _policyNurse, value); }
    public string PolicyPharm { get => _policyPharm; set => SetField(ref _policyPharm, value); }
}

public class NewUserFormModel : ClientModelBase
{
    private string _userName = "";
    private string _displayName = "";
    private string _employeeNo = "";
    private string _password = "123456";
    private string _selectedRole = "ROLE-OPD-DOC";
    private string _selectedDept = "心血管内科门诊";
    private int _prescribeLevel = 2;
    private int _surgeryLevel = 1;
    private int _emrSignLevel = 2;

    public string UserName { get => _userName; set => SetField(ref _userName, value); }
    public string DisplayName { get => _displayName; set => SetField(ref _displayName, value); }
    public string EmployeeNo { get => _employeeNo; set => SetField(ref _employeeNo, value); }
    public string Password { get => _password; set => SetField(ref _password, value); }
    public string SelectedRole { get => _selectedRole; set => SetField(ref _selectedRole, value); }
    public string SelectedDept { get => _selectedDept; set => SetField(ref _selectedDept, value); }
    public int PrescribeLevel { get => _prescribeLevel; set => SetField(ref _prescribeLevel, value); }
    public int SurgeryLevel { get => _surgeryLevel; set => SetField(ref _surgeryLevel, value); }
    public int EmrSignLevel { get => _emrSignLevel; set => SetField(ref _emrSignLevel, value); }

    public void Reset()
    {
        UserName = "";
        DisplayName = "";
        EmployeeNo = "";
        Password = "123456";
        SelectedRole = "ROLE-OPD-DOC";
        SelectedDept = "心血管内科门诊";
        PrescribeLevel = 2;
        SurgeryLevel = 1;
        EmrSignLevel = 2;
    }
}

public class NewRoleFormModel : ClientModelBase
{
    private string _roleCode = "";
    private string _roleName = "";
    private string _dataScope = "DEPT_ONLY";
    private string _description = "";

    public string RoleCode { get => _roleCode; set => SetField(ref _roleCode, value); }
    public string RoleName { get => _roleName; set => SetField(ref _roleName, value); }
    public string DataScope { get => _dataScope; set => SetField(ref _dataScope, value); }
    public string Description { get => _description; set => SetField(ref _description, value); }

    public void Reset()
    {
        RoleCode = "";
        RoleName = "";
        DataScope = "DEPT_ONLY";
        Description = "";
    }
}
