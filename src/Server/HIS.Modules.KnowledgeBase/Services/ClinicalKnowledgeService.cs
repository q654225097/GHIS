using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.KnowledgeBase.Models;

namespace HIS.Modules.KnowledgeBase.Services;

public sealed class ClinicalKnowledgeService : IClinicalKnowledgeContract
{
    private readonly IRepository<KbDocument> _docRepo;
    private readonly IRepository<KbClinicalRule> _ruleRepo;
    private readonly IRepository<KbOrderBinding> _bindingRepo;
    private readonly IRepository<KbExternalSource> _sourceRepo;

    public ClinicalKnowledgeService(
        IRepository<KbDocument> docRepo,
        IRepository<KbClinicalRule> ruleRepo,
        IRepository<KbOrderBinding> bindingRepo,
        IRepository<KbExternalSource> sourceRepo)
    {
        _docRepo = docRepo;
        _ruleRepo = ruleRepo;
        _bindingRepo = bindingRepo;
        _sourceRepo = sourceRepo;
    }

    public async Task<CdssEvaluationResultDto> EvaluateAsync(
        CdssEvaluationRequestDto request,
        CancellationToken ct = default)
    {
        var rules = await _ruleRepo.QueryAsync("SELECT * FROM kb_clinical_rules WHERE is_active = 1", ct: ct);
        var hits = new List<CdssHitRuleDto>();

        // 1. 检验指标联动规则评估 (eGFR / 血钾 / 肝功能)
        decimal? egfr = request.RecentLabResults?.FirstOrDefault(r => r.ItemCode.Contains("EGFR", StringComparison.OrdinalIgnoreCase))?.ValueNum;
        decimal? potassium = request.RecentLabResults?.FirstOrDefault(r => r.ItemCode.Contains("POTASSIUM", StringComparison.OrdinalIgnoreCase) || r.ItemName.Contains("钾"))?.ValueNum;
        decimal? alt = request.RecentLabResults?.FirstOrDefault(r => r.ItemCode.Contains("ALT", StringComparison.OrdinalIgnoreCase) || r.ItemName.Contains("谷丙"))?.ValueNum;

        // 2. 在院开立医嘱/处方项目
        var orderCodes = request.ActiveOrders?.Select(o => o.ItemCode.ToUpperInvariant()).ToHashSet() ?? new HashSet<string>();
        var orderNames = request.ActiveOrders?.Select(o => o.ItemName).ToList() ?? new List<string>();

        // 3. 过敏史比对
        var allergies = request.AllergyTags?.Select(a => a.ToUpperInvariant()).ToList() ?? new List<string>();

        foreach (var rule in rules)
        {
            bool triggered = false;

            // 规则: 肾功能不全造影剂 / 双胍类阻断 (RULE-CDSS-RENAL-01)
            if (rule.RuleCode == "RULE-CDSS-RENAL-01" && egfr.HasValue && egfr.Value < 30)
            {
                if (orderNames.Any(n => n.Contains("造影") || n.Contains("二甲双胍")))
                {
                    triggered = true;
                }
            }
            // 规则: 高血钾禁用保钾利尿剂/补钾 (RULE-CDSS-K-01)
            else if (rule.RuleCode == "RULE-CDSS-K-01" && potassium.HasValue && potassium.Value >= 5.5m)
            {
                if (orderNames.Any(n => n.Contains("螺内酯") || n.Contains("氯化钾") || n.Contains("门冬氨酸钾")))
                {
                    triggered = true;
                }
            }
            // 规则: 严重肝功能不全慎用肝毒性药物 (RULE-CDSS-HEPATIC-01)
            else if (rule.RuleCode == "RULE-CDSS-HEPATIC-01" && alt.HasValue && alt.Value >= 150)
            {
                if (orderNames.Any(n => n.Contains("阿托伐他汀") || n.Contains("对乙酰氨基酚")))
                {
                    triggered = true;
                }
            }
            // 规则: 青霉素/头孢过敏强阻断 (RULE-CDSS-ALLERGY-01)
            else if (rule.RuleCode == "RULE-CDSS-ALLERGY-01" && allergies.Any(a => a.Contains("青霉素") || a.Contains("PENICILLIN")))
            {
                if (orderNames.Any(n => n.Contains("青霉素") || n.Contains("阿莫西林") || n.Contains("哌拉西林")))
                {
                    triggered = true;
                }
            }
            // 规则: 儿童喹诺酮类禁忌 (RULE-CDSS-PEDIATRIC-01)
            else if (rule.RuleCode == "RULE-CDSS-PEDIATRIC-01" && request.AgeYears < 18)
            {
                if (orderNames.Any(n => n.Contains("左氧氟沙星") || n.Contains("莫西沙星") || n.Contains("诺氟沙星") || n.Contains("环丙沙星")))
                {
                    triggered = true;
                }
            }
            // 规则: 性别禁忌 (RULE-CDSS-GENDER-01)
            else if (rule.RuleCode == "RULE-CDSS-GENDER-01" && request.Gender.Equals("F", StringComparison.OrdinalIgnoreCase))
            {
                if (orderNames.Any(n => n.Contains("非那雄胺")))
                {
                    triggered = true;
                }
            }
            // 通用 DSL 匹配
            else if (!string.IsNullOrWhiteSpace(rule.ConditionDslJson) && rule.ConditionDslJson != "{}")
            {
                try
                {
                    using var doc = JsonDocument.Parse(rule.ConditionDslJson);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("requiredDiagnosisIcd", out var diagProp) && diagProp.ValueKind == JsonValueKind.Array)
                    {
                        var reqDiags = diagProp.EnumerateArray().Select(d => d.GetString() ?? "").ToList();
                        if (reqDiags.Any(d => request.DiagnosisIcdCodes.Any(userDiag => userDiag.StartsWith(d, StringComparison.OrdinalIgnoreCase))))
                        {
                            if (root.TryGetProperty("blockedItemKeywords", out var kwProp) && kwProp.ValueKind == JsonValueKind.Array)
                            {
                                var keywords = kwProp.EnumerateArray().Select(k => k.GetString() ?? "").ToList();
                                if (orderNames.Any(name => keywords.Any(kw => !string.IsNullOrEmpty(kw) && name.Contains(kw))))
                                {
                                    triggered = true;
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // 降级跳过解析错误的规则
                }
            }

            if (triggered)
            {
                hits.Add(new CdssHitRuleDto(
                    RuleCode: rule.RuleCode,
                    RuleDomain: rule.RuleDomain,
                    Severity: rule.Severity,
                    Explanation: rule.ExplanationText,
                    Suggestion: rule.ActionSuggestionText,
                    EvidenceDocId: rule.EvidenceDocId
                ));
            }
        }

        bool hasBlocking = hits.Any(h => string.Equals(h.Severity, "BLOCK", StringComparison.OrdinalIgnoreCase));
        return new CdssEvaluationResultDto(hits, hasBlocking);
    }

    public async Task<IReadOnlyList<KnowledgeDocSummaryDto>> SearchKnowledgeAsync(
        string? keyword,
        string? category,
        string? icdCode,
        string? itemCode = null,
        CancellationToken ct = default)
    {
        string sql = "WHERE is_active = 1";
        if (!string.IsNullOrEmpty(category)) sql += " AND category = @Category";
        if (!string.IsNullOrEmpty(keyword)) sql += " AND (title LIKE @Kw OR content_markdown LIKE @Kw)";
        if (!string.IsNullOrEmpty(icdCode)) sql += " AND icd10_codes_json LIKE @Icd";
        if (!string.IsNullOrEmpty(itemCode)) sql += " AND item_codes_json LIKE @Item";
        sql += " ORDER BY created_at_ms DESC LIMIT 50";

        var list = await _docRepo.QueryAsync(sql, new
        {
            Category = category,
            Kw = $"%{keyword}%",
            Icd = $"%{icdCode}%",
            Item = $"%{itemCode}%"
        }, ct: ct);

        return list.Select(d => new KnowledgeDocSummaryDto(
            Id: d.Id,
            DocCode: d.DocCode,
            Category: d.Category,
            SpecialtyDept: d.SpecialtyDept ?? "全院通用",
            Title: d.Title,
            SourceOrg: d.SourceOrg,
            VersionTag: d.VersionTag
        )).ToList();
    }
}
