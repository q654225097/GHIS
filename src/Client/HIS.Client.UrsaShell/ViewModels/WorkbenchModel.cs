using System.Collections.ObjectModel;
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
    private string _systemNotice = "系统运行正常 | 8大门禁检验全部通过 (GATE-01 ~ GATE-08)";

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

        _selectedPatient = PatientQueue[0];

        // 2. Initialize Prescriptions
        Prescriptions = new ObservableCollection<PrescriptionItemModel>
        {
            new PrescriptionItemModel
            {
                Seq = 1,
                DrugName = "注射用青霉素钠",
                Spec = "80万单位/支",
                Dose = "80万U",
                Freq = "bid (每12小时一次)",
                Route = "静脉滴注",
                Quantity = "6支",
                Amount = "¥75.00",
                SkinTestStatus = "❌ 皮试阳性 (强阻断)",
                SkinTestClass = "Danger",
                AuditStatus = "已拦截 (过敏史冲突)",
                AuditClass = "Danger",
                IsBlocked = true
            },
            new PrescriptionItemModel
            {
                Seq = 2,
                DrugName = "0.9% 氯化钠注射液",
                Spec = "500ml/瓶",
                Dose = "500ml",
                Freq = "qd (每日一次)",
                Route = "静脉滴注",
                Quantity = "2瓶",
                Amount = "¥11.00",
                SkinTestStatus = "无需皮试",
                SkinTestClass = "Secondary",
                AuditStatus = "审核通过",
                AuditClass = "Success",
                IsBlocked = false
            },
            new PrescriptionItemModel
            {
                Seq = 3,
                DrugName = "阿司匹林肠溶片",
                Spec = "100mg*30片/盒",
                Dose = "100mg",
                Freq = "qn (每晚一次)",
                Route = "口服",
                Quantity = "1盒",
                Amount = "¥22.50",
                SkinTestStatus = "无需皮试",
                SkinTestClass = "Secondary",
                AuditStatus = "审核通过",
                AuditClass = "Success",
                IsBlocked = false
            },
            new PrescriptionItemModel
            {
                Seq = 4,
                DrugName = "硫酸氢氯吡格雷片",
                Spec = "75mg*7片/盒",
                Dose = "75mg",
                Freq = "qd (每日一次)",
                Route = "口服",
                Quantity = "2盒",
                Amount = "¥56.80",
                SkinTestStatus = "无需皮试",
                SkinTestClass = "Secondary",
                AuditStatus = "审核通过",
                AuditClass = "Success",
                IsBlocked = false
            }
        };

        // 3. Initialize MedTech Requests
        MedTechOrders = new ObservableCollection<MedTechOrderItemModel>
        {
            new MedTechOrderItemModel
            {
                RequestNo = "MTC20261105001",
                ItemName = "全血细胞分析与五分类 (血常规)",
                DeptName = "医学检验科 (DEPT-MTC-LAB)",
                ExecLocation = "门诊标本核收室 (紫头EDTA管)",
                Fee = "¥25.00",
                Status = "已开单待采样",
                StatusClass = "Warning",
                IsFeeLocked = false
            },
            new MedTechOrderItemModel
            {
                RequestNo = "MTC20261105002",
                ItemName = "12导联同步心电图 (常规ECG)",
                DeptName = "功能检查科 (DEPT-MTC-ECG)",
                ExecLocation = "门诊2楼 心电图1号检查室",
                Fee = "¥35.00",
                Status = "已核收确费 (已锁单)",
                StatusClass = "Success",
                IsFeeLocked = true
            },
            new MedTechOrderItemModel
            {
                RequestNo = "MTC20261105003",
                ItemName = "胸部高分辨率 CT 平扫 (HRCT)",
                DeptName = "放射影像科 (DEPT-MTC-RAD)",
                ExecLocation = "医技楼1楼 CT-01号机房",
                Fee = "¥220.00",
                Status = "智能分时预约已确认",
                StatusClass = "Primary",
                IsFeeLocked = false
            }
        };

        // 4. Initialize Bed Map Cards
        BedCards = new ObservableCollection<BedCardModel>
        {
            new BedCardModel
            {
                BedCode = "01 床",
                BedType = "标准编制床",
                PatientName = "刘德海",
                Gender = "男",
                Age = 62,
                AdmissionNo = "IPD20261101001",
                AdmittingDeptName = "呼吸内科住院科",
                NursingWardName = "内科二病区",
                NursingLevel = "一级护理",
                Diagnosis = "慢阻肺急性加重期 (AECOPD)",
                DailyFee = "¥60.00/天",
                IsBorrowed = false,
                IsOccupied = true,
                CardTheme = "Standard"
            },
            new BedCardModel
            {
                BedCode = "02 床",
                BedType = "跨病区借床 (BORROWED)",
                PatientName = "赵建中",
                Gender = "男",
                Age = 55,
                AdmissionNo = "IPD20261103008",
                AdmittingDeptName = "心血管内科住院一科",
                NursingWardName = "内科二病区 (执行借床)",
                NursingLevel = "二级护理",
                Diagnosis = "不稳定型心绞痛 (CCS III级)",
                DailyFee = "¥60.00/天",
                IsBorrowed = true,
                IsOccupied = true,
                CardTheme = "Borrowed"
            },
            new BedCardModel
            {
                BedCode = "03 床",
                BedType = "标准编制床",
                PatientName = "空床 (可收治)",
                Gender = "-",
                Age = 0,
                AdmissionNo = "-",
                AdmittingDeptName = "-",
                NursingWardName = "内科二病区",
                NursingLevel = "-",
                Diagnosis = "已完成终末消毒，符合收治标准",
                DailyFee = "¥60.00/天",
                IsBorrowed = false,
                IsOccupied = false,
                CardTheme = "Empty"
            },
            new BedCardModel
            {
                BedCode = "04 床",
                BedType = "加床 (EXTRA_BED)",
                PatientName = "孙建国",
                Gender = "男",
                Age = 49,
                AdmissionNo = "IPD20261104012",
                AdmittingDeptName = "消化内科住院科",
                NursingWardName = "内科二病区",
                NursingLevel = "二级护理",
                Diagnosis = "急性胰腺炎 (轻症)",
                DailyFee = "¥45.00/天",
                IsBorrowed = false,
                IsOccupied = true,
                CardTheme = "Extra"
            },
            new BedCardModel
            {
                BedCode = "05 床",
                BedType = "重症监护床 (ICU)",
                PatientName = "陈芳华",
                Gender = "女",
                Age = 71,
                AdmissionNo = "IPD20261102003",
                AdmittingDeptName = "心血管内科住院一科",
                NursingWardName = "内科二病区",
                NursingLevel = "特级护理 (病重)",
                Diagnosis = "急性非ST段抬高型心肌梗死",
                DailyFee = "¥120.00/天",
                IsBorrowed = false,
                IsOccupied = true,
                CardTheme = "Critical"
            },
            new BedCardModel
            {
                BedCode = "06 床",
                BedType = "标准编制床",
                PatientName = "钱学森",
                Gender = "男",
                Age = 66,
                AdmissionNo = "IPD20261028005",
                AdmittingDeptName = "心血管内科住院一科",
                NursingWardName = "内科二病区",
                NursingLevel = "三级护理",
                Diagnosis = "冠脉支架植入术后恢复期 (待出院)",
                DailyFee = "¥60.00/天",
                IsBorrowed = false,
                IsOccupied = true,
                CardTheme = "DischargeReady"
            }
        };

        // 5. Initialize Inpatient Records (Domain 1)
        InpatientRecords = new ObservableCollection<InpatientRecordModel>
        {
            new InpatientRecordModel
            {
                AdmissionNo = "IPD20261101001",
                PatientName = "刘德海",
                BedCode = "01 床",
                AdmittingDept = "呼吸内科住院科",
                NursingWard = "内科二病区",
                NursingLevel = "一级护理",
                Diagnosis = "慢阻肺急性加重期 (AECOPD)",
                AttendingDoctor = "张建国 (主治)",
                AdmissionDate = "2026-11-01 (已住院5天)",
                DailySummary = "SPO2 94%，静脉抗感染治疗中，咳痰较多",
                IsCritical = false,
                IsDischargeReady = false
            },
            new InpatientRecordModel
            {
                AdmissionNo = "IPD20261102003",
                PatientName = "陈芳华",
                BedCode = "05 床",
                AdmittingDept = "心血管内科住院一科",
                NursingWard = "内科二病区",
                NursingLevel = "特级护理",
                Diagnosis = "急性非ST段抬高型心肌梗死",
                AttendingDoctor = "张建国 (主治)",
                AdmissionDate = "2026-11-02 (已住院4天)",
                DailySummary = "持续心电监护，cTnI 2.8ng/mL，需严密防范心衰及恶性心律失常",
                IsCritical = true,
                IsDischargeReady = false
            },
            new InpatientRecordModel
            {
                AdmissionNo = "IPD20261028005",
                PatientName = "钱学森",
                BedCode = "06 床",
                AdmittingDept = "心血管内科住院一科",
                NursingWard = "内科二病区",
                NursingLevel = "三级护理",
                Diagnosis = "冠脉支架术后恢复期",
                AttendingDoctor = "张建国 (主治)",
                AdmissionDate = "2026-10-28 (已住院9天)",
                DailySummary = "造影伤口愈合良好，生命体征平稳，已完成出院六严关校验 (GATE-07)",
                IsCritical = false,
                IsDischargeReady = true
            }
        };

        // 6. Initialize Pharmacy Dispensing Records (Domain 4)
        PharmacyRecords = new ObservableCollection<PharmacyDispenseModel>
        {
            new PharmacyDispenseModel
            {
                PrescriptionNo = "RX-OPD-20261105001",
                PatientName = "周建强",
                DrugSummary = "0.9%氯化钠注射液 × 2瓶, 阿司匹林肠溶片 × 1盒, 硫酸氢氯吡格雷片 × 2盒",
                DispenseStatus = "待扫码核配",
                PharmacistName = "李芳 (主管药师)",
                SafetyAudit = "过敏阻断通过: 青霉素已强拦截 (GATE-05)",
                IsAllergyBlocked = true,
                CanDispense = true
            },
            new PharmacyDispenseModel
            {
                PrescriptionNo = "RX-OPD-20261105002",
                PatientName = "王大明",
                DrugSummary = "单硝酸异山梨酯片 × 2盒, 琥珀酸美托洛尔缓释片 × 1盒",
                DispenseStatus = "已发药出库 ✓",
                PharmacistName = "李芳 (主管药师)",
                SafetyAudit = "处方双签名审核通过",
                IsAllergyBlocked = false,
                CanDispense = false
            },
            new PharmacyDispenseModel
            {
                PrescriptionNo = "RX-IPD-20261104018",
                PatientName = "刘德海 (01床)",
                DrugSummary = "多索茶碱注射液 0.2g × 4支, 盐酸氨溴索注射液 30mg × 6支",
                DispenseStatus = "病区批次领药完成",
                PharmacistName = "张明远 (药师)",
                SafetyAudit = "住院长嘱每日自动摆药",
                IsAllergyBlocked = false,
                CanDispense = false
            }
        };

        // 7. Initialize OFD Archive Records (Domain 5)
        OfdArchiveRecords = new ObservableCollection<OfdArchiveRecordModel>
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
                DeptName = "呼吸内科住院科",
                OfdStandard = "GB/T 33190-2016",
                ZipEntries = "OFD.xml | Document.xml | CustomTags/EmrStructuredData.xml | Signatures.xml",
                HashSha256 = "8F434346648F6B96DF89DDA901C5176B10A6D83961DD3C1AC88B59B2DC327AA4",
                SignatureStatus = "主治医师审签完成",
                ArchiveTime = "2026-11-03 10:22:05",
                IsVerified = true
            },
            new OfdArchiveRecordModel
            {
                ArchiveNo = "OFD-20261031-CARDIO-012",
                DocType = "冠状动脉造影介入手术记录 (OPERATION)",
                PatientName = "钱学森",
                DeptName = "心血管内科住院一科",
                OfdStandard = "GB/T 33190-2016",
                ZipEntries = "OFD.xml | Document.xml | CustomTags/EmrStructuredData.xml | Signatures.xml",
                HashSha256 = "CA978112CA1BBDCAFAC231B39A23DC4DA786EFF8147C4E72B9807785AFEE48BB",
                SignatureStatus = "术者+一助+麻醉三签完成",
                ArchiveTime = "2026-10-31 16:30:11",
                IsVerified = true
            }
        };

        // 8. Initialize Dynamic Form Templates (Domain 6)
        DynamicFormTemplates = new ObservableCollection<DynamicFormTemplateModel>
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
    }

    public string CampusName { get => _campusName; set => SetField(ref _campusName, value); }
    public DepartmentCategory ActiveCategory { get => _activeCategory; set => SetField(ref _activeCategory, value); }
    public string ActiveDeptName { get => _activeDeptName; set => SetField(ref _activeDeptName, value); }
    public string ActiveWardName { get => _activeWardName; set => SetField(ref _activeWardName, value); }
    public string UserName { get => _userName; set => SetField(ref _userName, value); }
    public int SelectedNavIndex { get => _selectedNavIndex; set => SetField(ref _selectedNavIndex, value); }
    public string SystemNotice { get => _systemNotice; set => SetField(ref _systemNotice, value); }

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

                // Sync to EMR Workspace
                EmrWorkspace.PatientName = value.PatientName;
                EmrWorkspace.Gender = value.Gender;
                EmrWorkspace.Age = value.Age;
                EmrWorkspace.ChiefComplaint = value.ChiefComplaint;
                EmrWorkspace.DocNo = $"DOC-{value.VisitNo}";

                if (value.Gender == "女")
                {
                    EmrWorkspace.ProstateExam = "不适用 (女性患者排他)";
                    EmrWorkspace.PregnancyCount = 2;
                }
                else
                {
                    EmrWorkspace.ProstateExam = "轻度肥大，质地韧，中央沟浅，未触及明显硬结。";
                    EmrWorkspace.PregnancyCount = 0;
                }
            }
        }
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

    public void SelectNextPatient()
    {
        if (PatientQueue.Count == 0) return;
        var currentIndex = SelectedPatient != null ? PatientQueue.IndexOf(SelectedPatient) : -1;
        var nextIndex = (currentIndex + 1) % PatientQueue.Count;
        SelectedPatient = PatientQueue[nextIndex];
        SystemNotice = $"已叫号下一位就诊患者: {SelectedPatient.PatientName} ({SelectedPatient.QueueNo})";
    }

    public void SimulateSignOfd()
    {
        EmrWorkspace.OfdStatus = $"GB/T 33190-2016 OFD 审签已完成 (Hash: {DateTime.Now:yyyyMMddHHmmss})";
        SystemNotice = $"国标 OFD 审签成功，已生成防篡改电子双签封包，归档单号: {EmrWorkspace.DocNo}";
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

    public string CardBorderBrush => IsSelected ? "#165DFF" : "#333842";
    public string CardBackground => IsSelected ? "#1F2837" : "#23272E";
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
    public string SkinTestStatus { get; set; } = string.Empty;
    public string SkinTestClass { get; set; } = "Secondary";
    public string AuditStatus { get; set; } = string.Empty;
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
