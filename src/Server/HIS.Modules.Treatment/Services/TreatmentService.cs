using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Treatment.Models;

namespace HIS.Modules.Treatment.Services;

public class TreatmentService : ITreatmentContract
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IRepository<TrtTreatmentCatalog> _catalogRepo;
    private readonly IRepository<TrtParameterRule> _ruleRepo;

    public TreatmentService(
        IDbConnectionFactory connFactory,
        IRepository<TrtTreatmentCatalog> catalogRepo,
        IRepository<TrtParameterRule> ruleRepo)
    {
        _connFactory = connFactory;
        _catalogRepo = catalogRepo;
        _ruleRepo = ruleRepo;
    }

    public async Task<IReadOnlyList<TreatmentParamSuggestionDto>> SuggestParametersAsync(
        string catalogId,
        string patientId,
        CancellationToken ct = default)
    {
        var suggestions = new List<TreatmentParamSuggestionDto>();
        var catalog = await _catalogRepo.GetByIdAsync(catalogId);
        if (catalog == null) return suggestions;

        using var conn = _connFactory.CreateConnection();

        // Query active parameter rules for this catalog
        var rules = await _ruleRepo.QueryAsync(
            "SELECT * FROM trt_parameter_rules WHERE catalog_id = @CatalogId AND is_active = 1",
            new { CatalogId = catalogId });

        // Query latest lab results for this patient
        var labResults = (await conn.QueryAsync<(string ItemCode, string ItemName, decimal? ValueNum, string Unit)>(
            """
            SELECT item_code, item_name, value_num, unit
            FROM mtc_lab_result_items
            WHERE patient_id = @PatientId
            ORDER BY reported_at_ms DESC
            LIMIT 50
            """,
            new { PatientId = patientId })).ToList();

        foreach (var rule in rules)
        {
            var matchLab = labResults.FirstOrDefault(l =>
                string.Equals(l.ItemCode, rule.LabItemCode, StringComparison.OrdinalIgnoreCase) ||
                l.ItemName.Contains(rule.LabItemCode, StringComparison.OrdinalIgnoreCase));

            string labBasis = matchLab.ValueNum.HasValue
                ? $"{matchLab.ItemName}: {matchLab.ValueNum} {matchLab.Unit}"
                : "未检出近期生化指标";

            string suggestedVal = "标准推荐方案";
            string warning = rule.WarningNotice ?? "无特殊禁忌";

            if (catalog.Category == "DIALYSIS")
            {
                // 透析：结合血钾、肌酐
                if (matchLab.ValueNum.HasValue && matchLab.ValueNum > 5.5m)
                {
                    suggestedVal = "超滤量 2500ml · 低钾透析液 (K+ 2.0mmol/L) · 碳酸氢盐透析 4.0h";
                    warning = "⚠ 患者存在高钾血症风险，已自动调整为低钾配方！";
                }
                else
                {
                    suggestedVal = "超滤量 2000ml · 标准透析液 (K+ 2.5mmol/L) · 碳酸氢盐透析 4.0h";
                }
            }
            else if (catalog.Category == "CHEMO")
            {
                // 化疗：结合 WBC / PLT
                if (matchLab.ValueNum.HasValue && matchLab.ValueNum < 3.5m)
                {
                    suggestedVal = "减量 20% · 输注速率 50ml/h · 备升白药物";
                    warning = "⚠ 骨髓抑制风险：白细胞偏低，建议减量化疗并预防性应用 G-CSF！";
                }
                else
                {
                    suggestedVal = "全剂量输注 · 推荐速率 100ml/h";
                }
            }
            else if (catalog.Category == "HBO")
            {
                suggestedVal = "治疗压力 0.20MPa · 稳压吸氧 60min · 升降压各 20min";
            }
            else
            {
                suggestedVal = rule.ParamSuggestionTemplate;
            }

            suggestions.Add(new TreatmentParamSuggestionDto(
                CatalogId: catalogId,
                ParameterName: rule.ParameterName,
                SuggestedValue: suggestedVal,
                LabBasis: labBasis,
                WarningNotice: warning
            ));
        }

        if (suggestions.Count == 0)
        {
            suggestions.Add(new TreatmentParamSuggestionDto(
                CatalogId: catalogId,
                ParameterName: "临床治疗常规参数",
                SuggestedValue: catalog.Category switch
                {
                    "DIALYSIS" => "超滤量 2000ml · 血液流速 220ml/min · 透析液流速 500ml/min",
                    "CHEMO" => "按体表面积 1.73m² 计算剂量 · 避光滴注",
                    "HBO" => "压力 0.20MPa · 吸氧 60min",
                    "PHYSIO" => "超短波治疗 · 每次 20min · 每日 1 次",
                    _ => "按专科常规参数实施"
                },
                LabBasis: "临床指南规范推荐",
                WarningNotice: catalog.IsHighRisk == 1 ? "高风险治疗项目：实施前必须双人核对患者身份与参数" : "常规低风险项目"
            ));
        }

        return suggestions;
    }
}
