using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HIS.Contracts;

namespace HIS.Modules.Outpatient.Services;

public class UnifiedClinicalOrderReviewEngine : IClinicalOrderReviewEngine
{
    private readonly IClinicalKnowledgeContract? _knowledgeContract;

    public UnifiedClinicalOrderReviewEngine(IClinicalKnowledgeContract? knowledgeContract = null)
    {
        _knowledgeContract = knowledgeContract;
    }

    public async Task<OrderReviewResult> ReviewOrdersAsync(
        OrderReviewContext context,
        CancellationToken ct = default)
    {
        var hits = new List<OrderReviewHitDto>();
        var items = context.ItemsToReview ?? Array.Empty<DrugItemToReviewDto>();
        var allergies = context.AllergyTags?.Select(a => a.ToUpperInvariant()).ToList() ?? new List<string>();

        // 提取最新生化检验指标
        decimal? egfr = context.RecentLabResults?.FirstOrDefault(r => r.ItemCode.Contains("EGFR", StringComparison.OrdinalIgnoreCase))?.ValueNum;
        decimal? potassium = context.RecentLabResults?.FirstOrDefault(r => r.ItemCode.Contains("POTASSIUM", StringComparison.OrdinalIgnoreCase) || r.ItemName.Contains("钾"))?.ValueNum;
        decimal? alt = context.RecentLabResults?.FirstOrDefault(r => r.ItemCode.Contains("ALT", StringComparison.OrdinalIgnoreCase) || r.ItemName.Contains("谷丙"))?.ValueNum;
        decimal? glucose = context.RecentLabResults?.FirstOrDefault(r => r.ItemCode.Contains("GLU", StringComparison.OrdinalIgnoreCase) || r.ItemName.Contains("糖"))?.ValueNum;

        // 1. 性别禁忌审查 (01.01.6 / 03.01.6)
        bool isFemale = context.Gender.Equals("F", StringComparison.OrdinalIgnoreCase) || context.Gender.Equals("FEMALE", StringComparison.OrdinalIgnoreCase) || context.Gender == "女";
        bool isMale = context.Gender.Equals("M", StringComparison.OrdinalIgnoreCase) || context.Gender.Equals("MALE", StringComparison.OrdinalIgnoreCase) || context.Gender == "男";

        if (isFemale)
        {
            foreach (var item in items)
            {
                if (item.DrugName.Contains("非那雄胺") || item.DrugName.Contains("前列地尔") || item.DrugName.Contains("十一酸睾酮"))
                {
                    hits.Add(new OrderReviewHitDto(
                        RuleCode: "RULE-GENDER-CONTRAINDICATION",
                        RuleType: "GENDER_RESTRICTION",
                        Severity: "BLOCK",
                        Message: $"【性别禁忌强拦截】女性患者严禁使用男性专科药物【{item.DrugName}】",
                        SuggestedAction: "请立即撤除该药品",
                        IsBlock: true
                    ));
                }
            }
        }
        else if (isMale)
        {
            foreach (var item in items)
            {
                if (item.DrugName.Contains("黄体酮") || item.DrugName.Contains("结合雌激素") || item.DrugName.Contains("戊酸雌二醇"))
                {
                    hits.Add(new OrderReviewHitDto(
                        RuleCode: "RULE-GENDER-CONTRAINDICATION",
                        RuleType: "GENDER_RESTRICTION",
                        Severity: "BLOCK",
                        Message: $"【性别禁忌强拦截】男性患者严禁使用妇科/孕产专科药物【{item.DrugName}】",
                        SuggestedAction: "请立即撤除该药品",
                        IsBlock: true
                    ));
                }
            }
        }

        // 2. 检验指标联动审查 (肾毒性、高血钾、高血糖、严重肝损)
        foreach (var item in items)
        {
            // 肾功能不全
            if (egfr.HasValue && egfr.Value < 30)
            {
                if (item.DrugName.Contains("造影") || item.DrugName.Contains("二甲双胍") || item.DrugName.Contains("庆大霉素") || item.DrugName.Contains("万古霉素"))
                {
                    hits.Add(new OrderReviewHitDto(
                        RuleCode: "RULE-RENAL-FUNCTION-BLOCK",
                        RuleType: "LAB_CORRELATION",
                        Severity: "BLOCK",
                        Message: $"【肾功能重度不全硬拦截】患者最新 eGFR={egfr.Value:F1} ml/min (<30)，严禁使用肾毒性/乳酸堆积风险药物【{item.DrugName}】",
                        SuggestedAction: "停用或改用非肾排泄药物并密切监测肾功",
                        IsBlock: true
                    ));
                }
            }

            // 高血钾禁忌
            if (potassium.HasValue && potassium.Value >= 5.5m)
            {
                if (item.DrugName.Contains("螺内酯") || item.DrugName.Contains("氯化钾") || item.DrugName.Contains("门冬氨酸钾镁"))
                {
                    hits.Add(new OrderReviewHitDto(
                        RuleCode: "RULE-POTASSIUM-CONTRAINDICATION",
                        RuleType: "LAB_CORRELATION",
                        Severity: "BLOCK",
                        Message: $"【严重高血钾硬拦截】患者最新血钾={potassium.Value:F2} mmol/L (>=5.5)，严禁使用含钾制剂或保钾利尿剂【{item.DrugName}】",
                        SuggestedAction: "请立即停用并给予降钾对症治疗 (如葡萄糖酸钙/胰岛素)",
                        IsBlock: true
                    ));
                }
            }

            // 低血糖禁忌
            if (glucose.HasValue && glucose.Value < 3.9m)
            {
                if (item.DrugName.Contains("胰岛素") || item.DrugName.Contains("格列") || item.DrugName.Contains("瑞格列奈"))
                {
                    hits.Add(new OrderReviewHitDto(
                        RuleCode: "RULE-HYPOGLYCEMIA-WARNING",
                        RuleType: "LAB_CORRELATION",
                        Severity: "WARNING",
                        Message: $"【低血糖强警示】患者最新血糖={glucose.Value:F1} mmol/L (<3.9)，使用降糖药【{item.DrugName}】极易引发低血糖休克",
                        SuggestedAction: "暂停降糖药物，立即静推或口服高渗葡萄糖",
                        IsBlock: false
                    ));
                }
            }

            // 肝功能异常
            if (alt.HasValue && alt.Value >= 150)
            {
                if (item.DrugName.Contains("对乙酰氨基酚") || item.DrugName.Contains("他汀"))
                {
                    hits.Add(new OrderReviewHitDto(
                        RuleCode: "RULE-HEPATIC-FUNCTION-WARNING",
                        RuleType: "LAB_CORRELATION",
                        Severity: "WARNING",
                        Message: $"【严重肝损慎用警示】患者最新 ALT={alt.Value:F1} U/L (>=150)，使用【{item.DrugName}】有加重肝损伤风险",
                        SuggestedAction: "建议减半剂量或换用低肝毒性替代品，并定期复查肝功能",
                        IsBlock: false
                    ));
                }
            }
        }

        // 3. 过敏史强阻断审查
        foreach (var item in items)
        {
            if (allergies.Any(a => a.Contains("青霉素") || a.Contains("PENICILLIN")))
            {
                if (item.DrugName.Contains("青霉素") || item.DrugName.Contains("阿莫西林") || item.DrugName.Contains("哌拉西林"))
                {
                    hits.Add(new OrderReviewHitDto(
                        RuleCode: "RULE-ALLERGY-HARD-BLOCK",
                        RuleType: "ALLERGY_SAFETY",
                        Severity: "BLOCK",
                        Message: $"【药物过敏强阻断】患者明确记录青霉素类过敏史，严禁开立【{item.DrugName}】",
                        SuggestedAction: "严禁使用青霉素类抗菌药，请更换为大环内酯类等替代药",
                        IsBlock: true
                    ));
                }
            }
        }

        // 4. 高警示药品强警示 (High-Alert Drugs A/B/C 级)
        foreach (var item in items)
        {
            if (item.DrugName.Contains("高浓度氯化钾") || item.DrugName.Contains("胰岛素") || item.DrugName.Contains("化疗") || item.DrugName.Contains("肝素") || item.DrugName.Contains("华法林"))
            {
                hits.Add(new OrderReviewHitDto(
                    RuleCode: "RULE-HIGH-ALERT-DRUG",
                    RuleType: "HIGH_ALERT",
                    Severity: "WARNING",
                    Message: $"【高警示药品强警示】药品【{item.DrugName}】属于 ISMP A/B 级高危高警示药品，用药必须严格遵循双人核对制度",
                    SuggestedAction: "请再次核实剂量与滴速，给药前护士站强制双人扫码核对",
                    IsBlock: false
                ));
            }
        }

        // 5. 跨单/同处方重复用药检查
        var seenDrugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (!seenDrugs.Add(item.DrugName))
            {
                hits.Add(new OrderReviewHitDto(
                    RuleCode: "RULE-DUPLICATE-DRUG",
                    RuleType: "DUPLICATE_THERAPY",
                    Severity: "WARNING",
                    Message: $"【重复用药警示】处方/医嘱中存在重复开立的药品【{item.DrugName}】",
                    SuggestedAction: "避免同药重复开立，请合并剂量或删减重复项",
                    IsBlock: false
                ));
            }
        }

        // 6. 联合知识库与 CDSS 引擎评估
        if (_knowledgeContract != null)
        {
            try
            {
                var cdssReq = new CdssEvaluationRequestDto(
                    PatientId: context.PatientId,
                    Gender: context.Gender,
                    AgeYears: context.AgeYears,
                    DiagnosisIcdCodes: context.DiagnosisIcdCodes,
                    AllergyTags: context.AllergyTags,
                    RecentLabResults: context.RecentLabResults,
                    ActiveOrders: items.Select(i => new ActiveOrderItemDto(
                        OrderId: Guid.NewGuid().ToString(),
                        ItemCode: i.DrugId,
                        ItemName: i.DrugName,
                        Category: "DRUG",
                        Dosage: i.DoseOnce,
                        DosageUnit: i.DoseUnit
                    )).ToList()
                );

                var cdssRes = await _knowledgeContract.EvaluateAsync(cdssReq, ct);
                foreach (var cdssHit in cdssRes.Hits)
                {
                    if (!hits.Any(h => h.RuleCode == cdssHit.RuleCode))
                    {
                        hits.Add(new OrderReviewHitDto(
                            RuleCode: cdssHit.RuleCode,
                            RuleType: cdssHit.RuleDomain,
                            Severity: cdssHit.Severity,
                            Message: $"【临床知识库预警】{cdssHit.Explanation}",
                            SuggestedAction: cdssHit.Suggestion,
                            IsBlock: string.Equals(cdssHit.Severity, "BLOCK", StringComparison.OrdinalIgnoreCase)
                        ));
                    }
                }
            }
            catch
            {
                // CDSS 降级容错
            }
        }

        bool hasBlock = hits.Any(h => h.IsBlock);
        return new OrderReviewResult(
            Passed: !hasBlock,
            HasBlockWarning: hasBlock,
            Hits: hits
        );
    }
}
