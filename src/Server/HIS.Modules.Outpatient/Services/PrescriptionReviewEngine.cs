using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Outpatient.Models;

namespace HIS.Modules.Outpatient.Services;

public class PrescriptionReviewEngine : IPrescriptionReviewEngine
{
    private readonly IPatientReadOnlyContract? _patientContract;
    private readonly IDbConnectionFactory _connFactory;

    public PrescriptionReviewEngine(
        IPatientReadOnlyContract? patientContract = null,
        IDbConnectionFactory? connFactory = null)
    {
        _patientContract = patientContract;
        _connFactory = connFactory ?? new SqliteConnectionFactory();
    }

    public async Task<PrescriptionReviewResultDto> ReviewAsync(
        string patientId,
        int doctorPrescribeLevel,
        bool isAdmin,
        IReadOnlyList<OpdPrescriptionItem> items,
        IReadOnlyList<DrugCatalogSnapshotDto> drugs,
        string? agentIdCard = null,
        CancellationToken ct = default)
    {
        var result = new PrescriptionReviewResultDto();
        var issues = new List<PrescriptionReviewIssueDto>();

        if (string.IsNullOrWhiteSpace(patientId))
        {
            issues.Add(new PrescriptionReviewIssueDto
            {
                RuleCode = "RULE_PATIENT_VALIDITY",
                RuleName = "患者档案有效性核查",
                Severity = "BLOCK",
                Title = "患者档案无效",
                Message = "处方必须关联有效的患者档案，当前患者 ID 为空",
                ClinicalSuggestion = "请重新选择患者后再行开立处方",
                ReferenceStandard = "《处方管理办法》第六条"
            });
            result.Issues = issues;
            result.OverallStatus = "BLOCK";
            result.SummaryMessage = "前置审方拦截：患者档案无效";
            return result;
        }

        PatientSummaryDto? patient = null;
        if (_patientContract != null)
        {
            patient = await _patientContract.GetPatientSummaryAsync(patientId, ct);
        }

        if (patient == null || !patient.IsActive)
        {
            issues.Add(new PrescriptionReviewIssueDto
            {
                RuleCode = "RULE_PATIENT_VALIDITY",
                RuleName = "患者档案有效性核查",
                Severity = "BLOCK",
                Title = "患者档案不存在或已停用",
                Message = $"患者档案 [{patientId}] 不存在或已处于注销停用状态，严禁开具处方",
                ClinicalSuggestion = "请核对患者就诊卡号或重新建档激活",
                ReferenceStandard = "《处方管理办法》第六条"
            });
            result.Issues = issues;
            result.OverallStatus = "BLOCK";
            result.SummaryMessage = "前置审方拦截：患者档案不存在或已停用";
            return result;
        }

        var drugList = drugs.ToList();
        var itemList = items.ToList();

        // 1. RULE_ALLERGY & RULE_SKIN_TEST: 过敏史及皮试阻断
        CheckAllergyAndSkinTest(patient, drugList, itemList, issues);

        // 2. RULE_DOSE_LIMIT: 单次剂量与日用剂量极限审查
        CheckDoseLimits(drugList, itemList, issues);

        // 3. RULE_INTERACTION: 药物相互作用与配伍禁忌
        CheckDrugInteractions(drugList, issues);

        // 4. RULE_DUPLICATE_THERAPY: 重复用药审核
        CheckDuplicateTherapy(drugList, issues);

        // 5. RULE_ANTIBIOTIC_STW: 抗菌药物分级管理权限与病原学送检
        await CheckAntibioticStewardshipAsync(patientId, doctorPrescribeLevel, isAdmin, drugList, issues, ct);

        // 6. RULE_SPECIAL_POPULATION: 特殊人群用药禁忌 (儿童、老年)
        CheckSpecialPopulationContraindications(patient, drugList, issues);

        // Narcotic regulations
        CheckNarcoticRegulations(drugList, itemList, agentIdCard, issues);

        result.Issues = issues;
        if (issues.Any(x => x.Severity == "BLOCK"))
        {
            result.OverallStatus = "BLOCK";
            var blockCount = issues.Count(x => x.Severity == "BLOCK");
            result.SummaryMessage = $"前置审方强阻断拦截：发现 {blockCount} 项严重用药安全风险，禁止开具或通过审方！";
        }
        else if (issues.Any(x => x.Severity == "WARNING"))
        {
            result.OverallStatus = "WARNING";
            var warnCount = issues.Count(x => x.Severity == "WARNING");
            result.SummaryMessage = $"前置审方警示提示：发现 {warnCount} 项用药安全警示，请临床医生或药师重点复核关注。";
        }
        else
        {
            result.OverallStatus = "PASS";
            result.SummaryMessage = "前置审方审查通过：各项指标均符合临床用药安全规范。";
        }

        return result;
    }

    private void CheckAllergyAndSkinTest(
        PatientSummaryDto patient,
        List<DrugCatalogSnapshotDto> drugs,
        List<OpdPrescriptionItem> items,
        List<PrescriptionReviewIssueDto> issues)
    {
        var allergyTags = patient.AllergyTags ?? Array.Empty<string>();

        foreach (var drug in drugs)
        {
            // Allergy check
            var conflict = FindAllergyConflict(allergyTags, drug);
            if (conflict != null)
            {
                issues.Add(new PrescriptionReviewIssueDto
                {
                    RuleCode = "RULE_ALLERGY",
                    RuleName = "过敏史红线拦截审查",
                    Severity = "BLOCK",
                    DrugId = drug.DrugId,
                    DrugName = drug.DrugName,
                    Title = "用药过敏红线阻断",
                    Message = $"患者已知过敏史中包含 [{conflict}]，与所开立药品 [{drug.DrugName}] 存在直接或交叉过敏风险",
                    ClinicalSuggestion = "强阻断：禁止开立致敏药品，请停用并选用无交叉过敏风险的其他类药物（如大环内酯类替代青霉素等）",
                    ReferenceStandard = "《处方管理办法》第十四条、《医院处方点评管理规范》第十九条"
                });
            }

            // Skin test check
            var matchingItem = items.FirstOrDefault(x => x.DrugId == drug.DrugId);
            if (drug.IsSkinTestRequired)
            {
                if (matchingItem != null && matchingItem.SkinTestFlag == 4)
                {
                    issues.Add(new PrescriptionReviewIssueDto
                    {
                        RuleCode = "RULE_SKIN_TEST_POSITIVE",
                        RuleName = "皮试阳性强阻断审查",
                        Severity = "BLOCK",
                        DrugId = drug.DrugId,
                        DrugName = drug.DrugName,
                        Title = "皮试结果阳性阻断",
                        Message = $"药品 [{drug.DrugName}] 皮试结果明确记录为 [阳性 (+)]，严禁使用！",
                        ClinicalSuggestion = "强阻断：严禁再次开立该批号或同类皮试阳性药物，必须立即更换为其他结构药品",
                        ReferenceStandard = "《临床用药须知》强制皮试操作规程"
                    });
                }
                else if (matchingItem != null && matchingItem.SkinTestFlag is 0 or 1 or 2)
                {
                    issues.Add(new PrescriptionReviewIssueDto
                    {
                        RuleCode = "RULE_SKIN_TEST_PENDING",
                        RuleName = "法定皮试阴性双签审查",
                        Severity = "BLOCK",
                        DrugId = drug.DrugId,
                        DrugName = drug.DrugName,
                        Title = "未取得皮试阴性双签结果",
                        Message = $"药品 [{drug.DrugName}] 属于法定强制皮试药品，当前尚未取得护士/医生双人核对的阴性双签结果（当前状态：{matchingItem.SkinTestFlag}）",
                        ClinicalSuggestion = "强阻断：请先开立皮试医嘱并在护士站执行观察，获得阴性合格报告后方可开方",
                        ReferenceStandard = "卫健委《医疗质量安全核心制度》查对制度与用药安全规范"
                    });
                }
            }
        }
    }

    private void CheckDoseLimits(
        List<DrugCatalogSnapshotDto> drugs,
        List<OpdPrescriptionItem> items,
        List<PrescriptionReviewIssueDto> issues)
    {
        foreach (var item in items)
        {
            var drug = drugs.FirstOrDefault(x => x.DrugId == item.DrugId);
            var drugName = drug?.DrugName ?? item.DrugNameSnapshot;
            decimal dose = item.DoseOnce;
            string unit = (item.DoseUnit ?? "mg").ToLowerInvariant();
            string freq = (item.FrequencyCode ?? "QD").ToUpperInvariant();

            int timesPerDay = freq switch
            {
                "BID" or "Q12H" => 2,
                "TID" or "Q8H" => 3,
                "QID" or "Q6H" => 4,
                "Q4H" => 6,
                _ => 1
            };

            decimal dailyDose = dose * timesPerDay;

            // 1. Aspirin: Single > 300mg
            if (ContainsAny(drugName, "阿司匹林", "aspirin"))
            {
                if (unit.Contains("mg") && dose > 300m)
                {
                    issues.Add(new PrescriptionReviewIssueDto
                    {
                        RuleCode = "RULE_DOSE_LIMIT_ASPIRIN",
                        RuleName = "超极量单次用药拦截",
                        Severity = "BLOCK",
                        DrugId = item.DrugId,
                        DrugName = drugName,
                        Title = "阿司匹林单次剂量超极量",
                        Message = $"阿司匹林单次剂量达 {dose}mg，超过门诊安全常规上限 (300mg)，存在重度消化道黏膜大出血及水杨酸中毒风险",
                        ClinicalSuggestion = "心脑血管抗血小板常规推荐单次剂量 100mg qd；镇痛抗炎常规单次不超过 300mg",
                        ReferenceStandard = "国家药监局《阿司匹林肠溶片说明书》极量警示"
                    });
                }
            }

            // 2. Atorvastatin: Daily / Single > 80mg
            if (ContainsAny(drugName, "阿托伐他汀", "atorvastatin"))
            {
                if (unit.Contains("mg") && (dose > 80m || dailyDose > 80m))
                {
                    issues.Add(new PrescriptionReviewIssueDto
                    {
                        RuleCode = "RULE_DOSE_LIMIT_STATIN",
                        RuleName = "他汀类超极量用药拦截",
                        Severity = "BLOCK",
                        DrugId = item.DrugId,
                        DrugName = drugName,
                        Title = "阿托伐他汀超极限剂量",
                        Message = $"阿托伐他汀剂量达单次 {dose}mg (日剂量 {dailyDose}mg)，超过中国药典每日最大极量 (80mg)，急性横纹肌溶解与肝功能损害风险急剧升高",
                        ClinicalSuggestion = "中国成年人推荐常规起始剂量为 10~20mg qn，每日最高不得超过 80mg",
                        ReferenceStandard = "《中国成人血脂异常防治指南》及药品说明书"
                    });
                }
            }

            // 3. Rosuvastatin: Daily / Single > 20mg
            if (ContainsAny(drugName, "瑞舒伐他汀", "rosuvastatin"))
            {
                if (unit.Contains("mg") && (dose > 20m || dailyDose > 20m))
                {
                    issues.Add(new PrescriptionReviewIssueDto
                    {
                        RuleCode = "RULE_DOSE_LIMIT_STATIN",
                        RuleName = "他汀类超极量用药拦截",
                        Severity = "BLOCK",
                        DrugId = item.DrugId,
                        DrugName = drugName,
                        Title = "瑞舒伐他汀超极限剂量",
                        Message = $"瑞舒伐他汀剂量达 {dose}mg (日剂量 {dailyDose}mg)，超过中国人群推荐最高极量 (20mg/日)",
                        ClinicalSuggestion = "亚裔人群常规推荐剂量为 5~10mg qn，极量不超过 20mg/日",
                        ReferenceStandard = "《瑞舒伐他汀说明书》亚裔人群用药特别警示"
                    });
                }
            }

            // 4. Acetaminophen: Single > 1000mg or Daily > 2000mg
            if (ContainsAny(drugName, "对乙酰氨基酚", "扑热息痛", "acetaminophen", "paracetamol"))
            {
                if (unit.Contains("mg") && (dose > 1000m || dailyDose > 2000m))
                {
                    issues.Add(new PrescriptionReviewIssueDto
                    {
                        RuleCode = "RULE_DOSE_LIMIT_ACETAMINOPHEN",
                        RuleName = "超极量致死性肝毒性拦截",
                        Severity = "BLOCK",
                        DrugId = item.DrugId,
                        DrugName = drugName,
                        Title = "对乙酰氨基酚超极限剂量",
                        Message = $"对乙酰氨基酚单次剂量达 {dose}mg (日剂量 {dailyDose}mg)，超过单次 1000mg 或日极量 2000mg，可诱发致死性急性暴发性肝衰竭",
                        ClinicalSuggestion = "成人推荐单次 300~500mg，24小时总剂量严禁超过 2000mg，间隔时间不少于 4~6 小时",
                        ReferenceStandard = "国家药监局《关于对乙酰氨基酚过量导致急性肝损伤的黑框警示》"
                    });
                }
            }

            // 5. Ibuprofen: Single > 800mg or Daily > 2400mg
            if (ContainsAny(drugName, "布洛芬", "ibuprofen"))
            {
                if (unit.Contains("mg") && (dose > 800m || dailyDose > 2400m))
                {
                    issues.Add(new PrescriptionReviewIssueDto
                    {
                        RuleCode = "RULE_DOSE_LIMIT_IBUPROFEN",
                        RuleName = "NSAID超极量用药拦截",
                        Severity = "BLOCK",
                        DrugId = item.DrugId,
                        DrugName = drugName,
                        Title = "布洛芬超极限剂量",
                        Message = $"布洛芬单次剂量达 {dose}mg (日剂量 {dailyDose}mg)，超过单次 800mg 或日极量 2400mg 上限",
                        ClinicalSuggestion = "常规抗炎镇痛推荐单次 200~400mg tid~qid，日剂量严禁超过 2400mg",
                        ReferenceStandard = "《布洛芬缓释胶囊说明书》"
                    });
                }
            }

            // 6. Metformin: Single > 1000mg or Daily > 2550mg
            if (ContainsAny(drugName, "二甲双胍", "metformin"))
            {
                if (unit.Contains("mg") && (dose > 1000m || dailyDose > 2550m))
                {
                    issues.Add(new PrescriptionReviewIssueDto
                    {
                        RuleCode = "RULE_DOSE_LIMIT_METFORMIN",
                        RuleName = "降糖药超极量用药拦截",
                        Severity = "BLOCK",
                        DrugId = item.DrugId,
                        DrugName = drugName,
                        Title = "二甲双胍超极限用药量",
                        Message = $"二甲双胍单次剂量达 {dose}mg (日剂量 {dailyDose}mg)，超过单次 1000mg 或日极限 2550mg，存在乳酸性酸中毒高危风险",
                        ClinicalSuggestion = "常规推荐单次 500mg bid~tid 随餐服用，每日最大推荐剂量为 2000mg~2550mg",
                        ReferenceStandard = "《中国2型糖尿病防治指南》二甲双胍用药规范"
                    });
                }
            }

            // 7. Amlodipine: Single / Daily > 10mg
            if (ContainsAny(drugName, "氨氯地平", "amlodipine"))
            {
                if (unit.Contains("mg") && (dose > 10m || dailyDose > 10m))
                {
                    issues.Add(new PrescriptionReviewIssueDto
                    {
                        RuleCode = "RULE_DOSE_LIMIT_AMLODIPINE",
                        RuleName = "降压药超极限拦截",
                        Severity = "BLOCK",
                        DrugId = item.DrugId,
                        DrugName = drugName,
                        Title = "氨氯地平超极限剂量",
                        Message = $"氨氯地平单次剂量达 {dose}mg，超过日推荐最大极量 (10mg)，易致重度外周水肿与反射性低血压",
                        ClinicalSuggestion = "常规起始剂量为 2.5~5mg qd，每日最大维持剂量为 10mg qd",
                        ReferenceStandard = "《苯磺酸氨氯地平片说明书》"
                    });
                }
            }

            // 8. Ceftriaxone: Single > 2000mg or Daily > 4000mg
            if (ContainsAny(drugName, "头孢曲松", "ceftriaxone"))
            {
                decimal doseMg = unit.Contains("g") && !unit.Contains("mg") ? dose * 1000m : dose;
                decimal dailyMg = doseMg * timesPerDay;
                if (doseMg > 2000m || dailyMg > 4000m)
                {
                    issues.Add(new PrescriptionReviewIssueDto
                    {
                        RuleCode = "RULE_DOSE_LIMIT_CEFTRIAXONE",
                        RuleName = "抗生素超极限用药拦截",
                        Severity = "BLOCK",
                        DrugId = item.DrugId,
                        DrugName = drugName,
                        Title = "头孢曲松超极限剂量",
                        Message = $"头孢曲松单次达 {doseMg}mg (日剂量 {dailyMg}mg)，超过成人单次 2g 或日极限 4g 上限",
                        ClinicalSuggestion = "成人常规抗感染单次 1~2g qd，重症感染日用量不得超过 4g",
                        ReferenceStandard = "《抗菌药物临床应用指导原则》"
                    });
                }
            }
        }
    }

    private void CheckDrugInteractions(
        List<DrugCatalogSnapshotDto> drugs,
        List<PrescriptionReviewIssueDto> issues)
    {
        for (int i = 0; i < drugs.Count; i++)
        {
            for (int j = i + 1; j < drugs.Count; j++)
            {
                var d1 = drugs[i];
                var d2 = drugs[j];

                // 1. Warfarin + NSAID (Aspirin / Ibuprofen / Diclofenac / Celecoxib) -> BLOCK
                bool isWarfarin1 = ContainsAny(d1.DrugName, "华法林", "warfarin");
                bool isWarfarin2 = ContainsAny(d2.DrugName, "华法林", "warfarin");
                bool isNsaid1 = IsNsaid(d1.DrugName);
                bool isNsaid2 = IsNsaid(d2.DrugName);

                if ((isWarfarin1 && isNsaid2) || (isWarfarin2 && isNsaid1))
                {
                    var warf = isWarfarin1 ? d1 : d2;
                    var nsaid = isWarfarin1 ? d2 : d1;
                    issues.Add(new PrescriptionReviewIssueDto
                    {
                        RuleCode = "RULE_INTERACTION_WARFARIN_NSAID",
                        RuleName = "药物相互作用高危红线",
                        Severity = "BLOCK",
                        DrugId = warf.DrugId,
                        DrugName = $"{warf.DrugName} + {nsaid.DrugName}",
                        Title = "华法林与NSAIDs合用致大出血高危",
                        Message = $"处方同时包含抗凝药物 [{warf.DrugName}] 与非甾体抗炎药 [{nsaid.DrugName}]，NSAIDs抑制血小板并损伤胃黏膜，合用极度增加致死性消化道及颅内大出血风险！",
                        ClinicalSuggestion = "强阻断：禁止两类药物联用！若需抗炎止痛，应改用对胃肠及血小板无影响的其他对症治疗",
                        ReferenceStandard = "FDA及NMPA黑框警告、《临床用药配伍禁忌手册》"
                    });
                }

                // 2. Ceftriaxone + Calcium injection -> BLOCK
                bool isCeft1 = ContainsAny(d1.DrugName, "头孢曲松", "ceftriaxone");
                bool isCeft2 = ContainsAny(d2.DrugName, "头孢曲松", "ceftriaxone");
                bool isCalcium1 = ContainsAny(d1.DrugName, "葡萄糖酸钙", "氯化钙", "含钙");
                bool isCalcium2 = ContainsAny(d2.DrugName, "葡萄糖酸钙", "氯化钙", "含钙");

                if ((isCeft1 && isCalcium2) || (isCeft2 && isCalcium1))
                {
                    var ceft = isCeft1 ? d1 : d2;
                    var calc = isCeft1 ? d2 : d1;
                    issues.Add(new PrescriptionReviewIssueDto
                    {
                        RuleCode = "RULE_INTERACTION_CEFTRIAXONE_CALCIUM",
                        RuleName = "严重配伍禁忌强阻断",
                        Severity = "BLOCK",
                        DrugId = ceft.DrugId,
                        DrugName = $"{ceft.DrugName} + {calc.DrugName}",
                        Title = "头孢曲松与含钙溶液致死性沉淀",
                        Message = $"处方同时包含 [{ceft.DrugName}] 与 [{calc.DrugName}]，头孢曲松与钙离子结合生成难溶性头孢曲松钙白色结晶沉淀，静脉输注可导致微血管栓塞致死！",
                        ClinicalSuggestion = "绝对禁忌：严禁将头孢曲松与任何含钙溶液配伍或同管道连续输注！",
                        ReferenceStandard = "国家药品监督管理局《关于修订注射用头孢曲松钠说明书的警示通告》"
                    });
                }

                // 3. Sildenafil/Tadalafil + Nitrates -> BLOCK
                bool isPde5_1 = ContainsAny(d1.DrugName, "西地那非", "他达拉非", "伐地那非", "sildenafil", "tadalafil");
                bool isPde5_2 = ContainsAny(d2.DrugName, "西地那非", "他达拉非", "伐地那非", "sildenafil", "tadalafil");
                bool isNitrate1 = ContainsAny(d1.DrugName, "硝酸甘油", "单硝酸异山梨酯", "硝酸异山梨酯", "nitroglycerin", "isosorbide");
                bool isNitrate2 = ContainsAny(d2.DrugName, "硝酸甘油", "单硝酸异山梨酯", "硝酸异山梨酯", "nitroglycerin", "isosorbide");

                if ((isPde5_1 && isNitrate2) || (isPde5_2 && isNitrate1))
                {
                    var pde5 = isPde5_1 ? d1 : d2;
                    var nit = isPde5_1 ? d2 : d1;
                    issues.Add(new PrescriptionReviewIssueDto
                    {
                        RuleCode = "RULE_INTERACTION_PDE5_NITRATE",
                        RuleName = "绝对禁忌用药审查",
                        Severity = "BLOCK",
                        DrugId = pde5.DrugId,
                        DrugName = $"{pde5.DrugName} + {nit.DrugName}",
                        Title = "PDE5抑制剂与硝酸酯类恶性低血压休克",
                        Message = $"处方同时包含 PDE5 抑制剂 [{pde5.DrugName}] 与硝酸酯类药物 [{nit.DrugName}]，两药协同扩张血管可导致灾难性难治性恶性低血压休克、心肌梗死甚至死亡！",
                        ClinicalSuggestion = "绝对禁忌：服用任何形式硝酸酯类药物的患者绝对禁用 PDE5 抑制剂！",
                        ReferenceStandard = "《临床药理学与药物治疗学指南》绝对禁忌证"
                    });
                }

                // 4. Spironolactone + ACEI/ARB -> WARNING
                bool isSpiro1 = ContainsAny(d1.DrugName, "螺内酯", "spironolactone");
                bool isSpiro2 = ContainsAny(d2.DrugName, "螺内酯", "spironolactone");
                bool isAceiArb1 = IsAceiOrArb(d1.DrugName);
                bool isAceiArb2 = IsAceiOrArb(d2.DrugName);

                if ((isSpiro1 && isAceiArb2) || (isSpiro2 && isAceiArb1))
                {
                    var spiro = isSpiro1 ? d1 : d2;
                    var acei = isSpiro1 ? d2 : d1;
                    issues.Add(new PrescriptionReviewIssueDto
                    {
                        RuleCode = "RULE_INTERACTION_HYPERKALEMIA",
                        RuleName = "相互作用电解质蓄积警示",
                        Severity = "WARNING",
                        DrugId = spiro.DrugId,
                        DrugName = $"{spiro.DrugName} + {acei.DrugName}",
                        Title = "螺内酯联用ACEI/ARB高钾血症风险",
                        Message = $"保钾利尿剂 [{spiro.DrugName}] 与抑制肾素-血管紧张素系统药物 [{acei.DrugName}] 联用，血钾排泄显著受阻，易诱发严重高钾血症及致死性室性心律失常",
                        ClinicalSuggestion = "警示：合用时必须定期严密监测血清钾 (K+) 及肾功能肌酐水平，告知患者避免摄入高钾食物",
                        ReferenceStandard = "《中国心力衰竭诊断和治疗指南》"
                    });
                }

                // 5. Clopidogrel + Omeprazole -> WARNING
                bool isClop1 = ContainsAny(d1.DrugName, "氯吡格雷", "clopidogrel");
                bool isClop2 = ContainsAny(d2.DrugName, "氯吡格雷", "clopidogrel");
                bool isOme1 = ContainsAny(d1.DrugName, "奥美拉唑", "埃索美拉唑", "omeprazole", "esomeprazole");
                bool isOme2 = ContainsAny(d2.DrugName, "奥美拉唑", "埃索美拉唑", "omeprazole", "esomeprazole");

                if ((isClop1 && isOme2) || (isClop2 && isOme1))
                {
                    var clop = isClop1 ? d1 : d2;
                    var ome = isClop1 ? d2 : d1;
                    issues.Add(new PrescriptionReviewIssueDto
                    {
                        RuleCode = "RULE_INTERACTION_CLOPIDOGREL_PPI",
                        RuleName = "CYP2C19竞争代谢拮抗警示",
                        Severity = "WARNING",
                        DrugId = clop.DrugId,
                        DrugName = $"{clop.DrugName} + {ome.DrugName}",
                        Title = "奥美拉唑减弱氯吡格雷抗血小板效果",
                        Message = $"[{ome.DrugName}] 强效抑制 CYP2C19 代谢酶，阻碍前体药 [{clop.DrugName}] 活化，导致血小板聚集抑制率下降近半，增加支架血栓与心脑血管缺血事件",
                        ClinicalSuggestion = "建议：改用对 CYP2C19 抑制较弱的质子泵抑制剂（如泮托拉唑或雷贝拉唑）",
                        ReferenceStandard = "FDA关于氯吡格雷与质子泵抑制剂相互作用的安全通告"
                    });
                }
            }
        }
    }

    private void CheckDuplicateTherapy(
        List<DrugCatalogSnapshotDto> drugs,
        List<PrescriptionReviewIssueDto> issues)
    {
        // 1. Dual NSAIDs -> BLOCK
        var nsaids = drugs.Where(d => IsNsaid(d.DrugName)).ToList();
        if (nsaids.Count >= 2)
        {
            var names = string.Join("、", nsaids.Select(x => x.DrugName));
            issues.Add(new PrescriptionReviewIssueDto
            {
                RuleCode = "RULE_DUPLICATE_NSAIDS",
                RuleName = "同类药物重复开具审查",
                Severity = "BLOCK",
                DrugName = names,
                Title = "同方重复开具多种非甾体抗炎药",
                Message = $"同一处方开立了 {nsaids.Count} 种非甾体抗炎药 ({names})，联用并不能增加镇痛抗炎效果，但消化道大溃疡穿孔出血及急性肾损伤风险呈数倍增加！",
                ClinicalSuggestion = "强阻断：严禁两种口服非甾体抗炎药同开，请保留一种最适药品并删除其他重复项",
                ReferenceStandard = "《医院处方点评管理规范》第十九条、《处方管理办法》"
            });
        }

        // 2. Dual Statins -> BLOCK
        var statins = drugs.Where(d => IsStatin(d.DrugName)).ToList();
        if (statins.Count >= 2)
        {
            var names = string.Join("、", statins.Select(x => x.DrugName));
            issues.Add(new PrescriptionReviewIssueDto
            {
                RuleCode = "RULE_DUPLICATE_STATINS",
                RuleName = "他汀类降脂药重复审查",
                Severity = "BLOCK",
                DrugName = names,
                Title = "同方重复开具多种他汀类药物",
                Message = $"同一处方开立了 {statins.Count} 种他汀类降脂药 ({names})，重复用药将急剧增加横纹肌溶解综合征及急性药物性肝坏死风险！",
                ClinicalSuggestion = "强阻断：降脂治疗仅需一种他汀药物，若单药不达标应联用胆固醇吸收抑制剂（如依折麦布）",
                ReferenceStandard = "《中国成人血脂异常防治指南》"
            });
        }

        // 3. Dual PPIs -> WARNING
        var ppis = drugs.Where(d => IsPpi(d.DrugName)).ToList();
        if (ppis.Count >= 2)
        {
            var names = string.Join("、", ppis.Select(x => x.DrugName));
            issues.Add(new PrescriptionReviewIssueDto
            {
                RuleCode = "RULE_DUPLICATE_PPIS",
                RuleName = "质子泵抑制剂重复用药提示",
                Severity = "WARNING",
                DrugName = names,
                Title = "同方重复开具多种质子泵抑制剂",
                Message = $"处方包含 {ppis.Count} 种同类质子泵抑制剂 ({names})，属于无协同价值的同机制重复用药",
                ClinicalSuggestion = "建议：精简处方保留一种抑酸药，避免增加医疗支出与潜在药物相互作用",
                ReferenceStandard = "《医院处方点评管理规范》第二十条"
            });
        }

        // 4. Dual Benzodiazepines -> WARNING
        var bzds = drugs.Where(d => IsBenzodiazepine(d.DrugName)).ToList();
        if (bzds.Count >= 2)
        {
            var names = string.Join("、", bzds.Select(x => x.DrugName));
            issues.Add(new PrescriptionReviewIssueDto
            {
                RuleCode = "RULE_DUPLICATE_BZDS",
                RuleName = "镇静安眠类重复用药提示",
                Severity = "WARNING",
                DrugName = names,
                Title = "同方重复开具苯二氮䓬类镇静药物",
                Message = $"处方包含 {bzds.Count} 种苯二氮䓬类药物 ({names})，易产生中枢过度镇静、嗜睡、呼吸抑制及蓄积耐受性",
                ClinicalSuggestion = "建议：评估患者睡眠障碍类型，精简选用一种适宜半衰期的催眠药物",
                ReferenceStandard = "《精神药品临床应用指导原则》"
            });
        }
    }

    private async Task CheckAntibioticStewardshipAsync(
        string patientId,
        int doctorPrescribeLevel,
        bool isAdmin,
        List<DrugCatalogSnapshotDto> drugs,
        List<PrescriptionReviewIssueDto> issues,
        CancellationToken ct)
    {
        foreach (var drug in drugs)
        {
            // Level 1: Restricted
            if (drug.AntibioticLevel == 1 && doctorPrescribeLevel < 2 && !isAdmin)
            {
                issues.Add(new PrescriptionReviewIssueDto
                {
                    RuleCode = "RULE_ANTIBIOTIC_LEVEL_RESTRICTED",
                    RuleName = "抗菌药物分级管理权限审查",
                    Severity = "BLOCK",
                    DrugId = drug.DrugId,
                    DrugName = drug.DrugName,
                    Title = "无权限开具限制使用级抗菌药物",
                    Message = $"药品 [{drug.DrugName}] 为限制使用级抗菌药物（二级），需主治医师及以上资质（等级 >= 2），当前医师处方权限等级仅为 {doctorPrescribeLevel}",
                    ClinicalSuggestion = "强阻断：请由主治医师或具备限制级资质的医师开具，或选用非限制使用级抗菌药物",
                    ReferenceStandard = "《抗菌药物临床应用管理办法》第二十四条"
                });
            }

            // Level 2: Special
            if (drug.AntibioticLevel == 2)
            {
                if (doctorPrescribeLevel < 3 && !isAdmin)
                {
                    issues.Add(new PrescriptionReviewIssueDto
                    {
                        RuleCode = "RULE_ANTIBIOTIC_LEVEL_SPECIAL",
                        RuleName = "抗菌药物分级管理权限审查",
                        Severity = "BLOCK",
                        DrugId = drug.DrugId,
                        DrugName = drug.DrugName,
                        Title = "无权限开具特殊使用级抗菌药物",
                        Message = $"药品 [{drug.DrugName}] 为特殊使用级抗菌药物（三级），需副主任医师/主任医师资质（等级 >= 3），当前医师处方权限等级为 {doctorPrescribeLevel}",
                        ClinicalSuggestion = "强阻断：特殊使用级抗菌药物需由副高及以上医师会诊同意后由授权医师开立",
                        ReferenceStandard = "《抗菌药物临床应用管理办法》第二十五条"
                    });
                }

                // Check microbiological culture order
                using var conn = _connFactory.CreateConnection();
                int microCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COUNT(1) FROM ord_medical_orders WHERE patient_id = @PatientId AND (order_category = 'MICROBIOLOGY' OR order_category = 'LAB' OR item_name LIKE '%微生物%' OR item_name LIKE '%细菌培养%')",
                    new { PatientId = patientId }, cancellationToken: ct));

                if (microCount == 0)
                {
                    issues.Add(new PrescriptionReviewIssueDto
                    {
                        RuleCode = "RULE_ANTIBIOTIC_MICROBIOLOGY_REQUIRED",
                        RuleName = "特殊级抗菌药病原学送检必开核查",
                        Severity = "BLOCK",
                        DrugId = drug.DrugId,
                        DrugName = drug.DrugName,
                        Title = "缺少病原微生物送检医嘱",
                        Message = $"开具特殊使用级抗菌药物 [{drug.DrugName}] 必须预先或同时开立病原微生物培养及药敏试验医嘱，当前未检索到任何病原学送检记录",
                        ClinicalSuggestion = "强阻断：根据卫健委三级公立医院考核要求，必须先开立细菌培养+药敏送检医嘱后再提交特殊级抗生素",
                        ReferenceStandard = "《抗菌药物临床应用管理办法》第二十八条"
                    });
                }
            }
        }
    }

    private void CheckSpecialPopulationContraindications(
        PatientSummaryDto patient,
        List<DrugCatalogSnapshotDto> drugs,
        List<PrescriptionReviewIssueDto> issues)
    {
        int age = patient.AgeYears;

        foreach (var drug in drugs)
        {
            // 1. Pediatric Fluoroquinolones (<18 yo) -> BLOCK
            if (age > 0 && age < 18 && IsFluoroquinolone(drug.DrugName))
            {
                issues.Add(new PrescriptionReviewIssueDto
                {
                    RuleCode = "RULE_PEDIATRIC_FLUOROQUINOLONE",
                    RuleName = "特殊人群儿童绝对禁忌审查",
                    Severity = "BLOCK",
                    DrugId = drug.DrugId,
                    DrugName = drug.DrugName,
                    Title = "18岁以下人群禁用氟喹诺酮类",
                    Message = $"患者年龄为 {age} 岁 (<18岁)，氟喹诺酮类药物 [{drug.DrugName}] 可诱发承重软骨关节软骨损害与关节病变，18岁以下儿童及青少年绝对禁用！",
                    ClinicalSuggestion = "绝对禁忌：强阻断！儿童感染建议改用头孢菌素类、大环内酯类或阿莫西林等适龄安全抗生素",
                    ReferenceStandard = "国家药品监督管理局《氟喹诺酮类药品说明书黑框禁忌》"
                });
            }

            // 2. Pediatric Tetracyclines (<8 yo) -> BLOCK
            if (age > 0 && age < 8 && IsTetracycline(drug.DrugName))
            {
                issues.Add(new PrescriptionReviewIssueDto
                {
                    RuleCode = "RULE_PEDIATRIC_TETRACYCLINE",
                    RuleName = "特殊人群低龄儿童用药禁忌",
                    Severity = "BLOCK",
                    DrugId = drug.DrugId,
                    DrugName = drug.DrugName,
                    Title = "8岁以下儿童禁用四环素类",
                    Message = $"患者年龄为 {age} 岁 (<8岁)，四环素类药物 [{drug.DrugName}] 沉积于牙齿和骨骼，引起恒齿永久性黄染及牙釉质发育不良，8岁以下儿童禁用！",
                    ClinicalSuggestion = "强阻断：请改用头孢类或大环内酯类抗生素替代",
                    ReferenceStandard = "《中国国家处方集（儿童版）》"
                });
            }

            // 3. Pediatric Aspirin (<16 yo) -> WARNING (Reye syndrome)
            if (age > 0 && age < 16 && ContainsAny(drug.DrugName, "阿司匹林", "aspirin"))
            {
                issues.Add(new PrescriptionReviewIssueDto
                {
                    RuleCode = "RULE_PEDIATRIC_REYE_SYNDROME",
                    RuleName = "特殊人群儿童发热用药警示",
                    Severity = "WARNING",
                    DrugId = drug.DrugId,
                    DrugName = drug.DrugName,
                    Title = "儿童使用阿司匹林诱发Reye综合征警示",
                    Message = $"患者年龄为 {age} 岁 (<16岁)，儿童病毒性感染发热使用阿司匹林存在诱发致死性 Reye 综合征（急性脑病合并脂肪肝坏死）风险",
                    ClinicalSuggestion = "警示建议：儿童退热首选对乙酰氨基酚或布洛芬混悬液",
                    ReferenceStandard = "《儿童发热对症治疗临床指南》"
                });
            }

            // 4. Elderly (>65 yo) Sedatives / Hypoglycemic warning -> WARNING
            if (age >= 65 && (IsBenzodiazepine(drug.DrugName) || ContainsAny(drug.DrugName, "格列本脲", "glibenclamide")))
            {
                issues.Add(new PrescriptionReviewIssueDto
                {
                    RuleCode = "RULE_ELDERLY_FALL_RISK",
                    RuleName = "老年人潜在不适当用药提示",
                    Severity = "WARNING",
                    DrugId = drug.DrugId,
                    DrugName = drug.DrugName,
                    Title = "老年高龄患者防跌倒与低血糖风险提示",
                    Message = $"患者年龄为 {age} 岁 (>=65岁)，使用中枢镇静药物 [{drug.DrugName}] 显著增加肌无力、认知减退及跌倒骨折风险",
                    ClinicalSuggestion = "警示建议：从小剂量起始，严格防范夜间跌倒，或选用非药物睡眠认知干预",
                    ReferenceStandard = "美国老年医学会 Beers 标准、中国老年人潜在不适当用药目录"
                });
            }
        }
    }

    private void CheckNarcoticRegulations(
        List<DrugCatalogSnapshotDto> drugs,
        List<OpdPrescriptionItem> items,
        string? agentIdCard,
        List<PrescriptionReviewIssueDto> issues)
    {
        foreach (var drug in drugs)
        {
            if (drug.IsNarcotic)
            {
                var item = items.FirstOrDefault(x => x.DrugId == drug.DrugId);
                if (string.IsNullOrWhiteSpace(agentIdCard) || !IdCardValidator.IsValid(agentIdCard))
                {
                    issues.Add(new PrescriptionReviewIssueDto
                    {
                        RuleCode = "RULE_NARCOTIC_AGENT_IDCARD",
                        RuleName = "麻精药品代办人身份审核",
                        Severity = "BLOCK",
                        DrugId = drug.DrugId,
                        DrugName = drug.DrugName,
                        Title = "麻精药品代办人身份证缺失或无效",
                        Message = $"开具麻精类高危管制药品 [{drug.DrugName}]，必须查验并录入合法的代办人 18 位身份证号码",
                        ClinicalSuggestion = "强阻断：请核验代办人实体二代身份证并在处方界面录入有效身份证号",
                        ReferenceStandard = "《麻醉药品和精神药品管理条例》第三十二条"
                    });
                }

                if (item != null && item.Days > 7)
                {
                    issues.Add(new PrescriptionReviewIssueDto
                    {
                        RuleCode = "RULE_NARCOTIC_DAYS_LIMIT",
                        RuleName = "麻精药品单方用药天数超限审查",
                        Severity = "BLOCK",
                        DrugId = drug.DrugId,
                        DrugName = drug.DrugName,
                        Title = "麻精药品处方用量超 7 天上限",
                        Message = $"麻精类药品 [{drug.DrugName}] 单张处方用药天数达 {item.Days} 天，超过国家法定门诊最高 7 天上限！",
                        ClinicalSuggestion = "强阻断：请将用药天数缩减至 7 天或以内，必要时办理特殊病历复核",
                        ReferenceStandard = "《处方管理办法》第二十三条"
                    });
                }
            }
        }
    }

    private static string? FindAllergyConflict(IReadOnlyList<string>? allergyTags, DrugCatalogSnapshotDto drug)
    {
        if (allergyTags == null || allergyTags.Count == 0) return null;
        var drugToken = (drug.DrugName + " " + drug.DrugCode).ToLowerInvariant();

        foreach (var tag in allergyTags)
        {
            if (string.IsNullOrWhiteSpace(tag)) continue;
            var tagToken = tag.Trim().ToLowerInvariant();

            if (drugToken.Contains(tagToken, StringComparison.Ordinal) ||
                tagToken.Contains(drugToken, StringComparison.Ordinal) ||
                (tagToken.Contains("penicillin", StringComparison.Ordinal) &&
                 (drugToken.Contains("penicillin", StringComparison.Ordinal) ||
                  drugToken.Contains("amoxicillin", StringComparison.Ordinal))) ||
                (tagToken.Contains("青霉素", StringComparison.Ordinal) &&
                 (drugToken.Contains("青霉素", StringComparison.Ordinal) ||
                  drugToken.Contains("阿莫西林", StringComparison.Ordinal) ||
                  drugToken.Contains("氨苄西林", StringComparison.Ordinal))) ||
                (tagToken.Contains("头孢", StringComparison.Ordinal) &&
                 (drugToken.Contains("头孢", StringComparison.Ordinal) ||
                  drugToken.Contains("cef", StringComparison.Ordinal))) ||
                (tagToken.Contains("磺胺", StringComparison.Ordinal) &&
                 (drugToken.Contains("磺胺", StringComparison.Ordinal) ||
                  drugToken.Contains("sulfa", StringComparison.Ordinal))) ||
                (tagToken.Contains("阿司匹林", StringComparison.Ordinal) &&
                 (drugToken.Contains("阿司匹林", StringComparison.Ordinal) ||
                  drugToken.Contains("aspirin", StringComparison.Ordinal))))
            {
                return tag;
            }
        }
        return null;
    }

    private static bool IsNsaid(string drugName)
    {
        return ContainsAny(drugName, "布洛芬", "双氯芬酸", "塞来昔布", "依托考昔", "洛索洛芬", "美洛昔康", "吲哚美辛", "阿司匹林", "双氯灭痛", "尼美舒利");
    }

    private static bool IsStatin(string drugName)
    {
        return ContainsAny(drugName, "阿托伐他汀", "瑞舒伐他汀", "辛伐他汀", "普伐他汀", "氟伐他汀", "匹伐他汀", "statin");
    }

    private static bool IsPpi(string drugName)
    {
        return ContainsAny(drugName, "奥美拉唑", "泮托拉唑", "兰索拉唑", "雷贝拉唑", "埃索美拉唑", "prazole");
    }

    private static bool IsBenzodiazepine(string drugName)
    {
        return ContainsAny(drugName, "地西泮", "阿普唑仑", "艾司唑仑", "劳拉西泮", "氯硝西泮", "奥沙西泮");
    }

    private static bool IsFluoroquinolone(string drugName)
    {
        return ContainsAny(drugName, "左氧氟沙星", "环丙沙星", "莫西沙星", "诺氟沙星", "氧氟沙星", "依诺沙星", "氟罗沙星", "flox");
    }

    private static bool IsTetracycline(string drugName)
    {
        return ContainsAny(drugName, "多西环素", "米诺环素", "四环素", "土霉素", "cycline");
    }

    private static bool IsAceiOrArb(string drugName)
    {
        return ContainsAny(drugName, "卡托普利", "依那普利", "赖诺普利", "培哚普利", "雷米普利", "贝那普利", "氯沙坦", "缬沙坦", "厄贝沙坦", "替米沙坦", "坎地沙坦", "普利", "沙坦");
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (var n in needles)
        {
            if (text.Contains(n, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
