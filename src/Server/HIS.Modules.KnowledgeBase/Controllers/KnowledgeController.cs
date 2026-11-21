using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Common;
using Dapper;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.KnowledgeBase.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.KnowledgeBase.Controllers;

[ApiController]
[Route("api/v4/kb")]
public class KnowledgeController : ControllerBase
{
    private readonly IClinicalKnowledgeContract _kbService;
    private readonly IRepository<KbDocument> _docRepo;
    private readonly IRepository<KbClinicalRule> _ruleRepo;
    private readonly IDbConnectionFactory? _connFactory;

    public record ExternalKnowledgeQueryResultDto(
        string SourceName,
        string SourceType,
        string Title,
        string Snippet,
        string ReferenceUri,
        bool IsExternal
    );

    public record CreateKbRuleDto(
        string RuleCode,
        string RuleDomain,
        string Severity,
        string ConditionDslJson,
        string ExplanationText,
        string ActionSuggestionText,
        string? EvidenceDocId
    );

    public record CreateKbDocDto(
        string DocCode,
        string Category,
        string? SpecialtyDept,
        string Title,
        string? Icd10CodesJson,
        string? ItemCodesJson,
        string ContentMarkdown,
        string? SourceOrg,
        string VersionTag
    );

    public KnowledgeController(
        IClinicalKnowledgeContract kbService,
        IRepository<KbDocument> docRepo,
        IRepository<KbClinicalRule> ruleRepo,
        IDbConnectionFactory? connFactory = null)
    {
        _kbService = kbService;
        _docRepo = docRepo;
        _ruleRepo = ruleRepo;
        _connFactory = connFactory;
    }

    [HttpGet("search")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<KnowledgeDocSummaryDto>>>> Search(
        [FromQuery] string? keyword,
        [FromQuery] string? category,
        [FromQuery] string? icdCode,
        [FromQuery] string? itemCode,
        CancellationToken ct)
    {
        var result = await _kbService.SearchKnowledgeAsync(keyword, category, icdCode, itemCode, ct);
        return Ok(ApiResponse<IReadOnlyList<KnowledgeDocSummaryDto>>.Success(result, "检索临床知识库成功"));
    }

    [HttpPost("evaluate")]
    public async Task<ActionResult<ApiResponse<CdssEvaluationResultDto>>> Evaluate(
        [FromBody] CdssEvaluationRequestDto request,
        CancellationToken ct)
    {
        var result = await _kbService.EvaluateAsync(request, ct);
        return Ok(ApiResponse<CdssEvaluationResultDto>.Success(result, "CDSS 临床规则评估完成"));
    }

    [HttpGet("external/query")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ExternalKnowledgeQueryResultDto>>>> QueryExternal(
        [FromQuery] string? sourceType,
        [FromQuery] string? keyword,
        CancellationToken ct = default)
    {
        string kw = (keyword ?? string.Empty).Trim();
        string likeKw = $"%{kw}%";
        var results = new List<ExternalKnowledgeQueryResultDto>();

        // 1. 检索院内权威知识文档库 kb_documents
        var docs = await _docRepo.QueryAsync(
            "SELECT * FROM kb_documents WHERE title LIKE @Kw OR content_markdown LIKE @Kw ORDER BY updated_at_ms DESC LIMIT 15",
            new { Kw = likeKw }, ct);
        foreach (var d in docs)
        {
            string snippet = string.IsNullOrWhiteSpace(d.ContentMarkdown)
                ? d.Title
                : (d.ContentMarkdown.Length > 180 ? d.ContentMarkdown[..180] + "..." : d.ContentMarkdown);
            results.Add(new ExternalKnowledgeQueryResultDto(
                SourceName: string.IsNullOrWhiteSpace(d.SourceOrg) ? "院内临床知识库 (KB)" : d.SourceOrg,
                SourceType: d.Category,
                Title: d.Title,
                Snippet: snippet,
                ReferenceUri: $"kb://documents/{d.DocCode}",
                IsExternal: false));
        }

        // 2. 检索临床决策规则库 kb_clinical_rules
        var rules = await _ruleRepo.QueryAsync(
            "SELECT * FROM kb_clinical_rules WHERE is_active = 1 AND (explanation_text LIKE @Kw OR action_suggestion_text LIKE @Kw OR rule_code LIKE @Kw) LIMIT 10",
            new { Kw = likeKw }, ct);
        foreach (var r in rules)
        {
            results.Add(new ExternalKnowledgeQueryResultDto(
                SourceName: "临床合理用药与CDSS规则库",
                SourceType: r.RuleDomain,
                Title: $"[{r.Severity}] {r.RuleCode}",
                Snippet: $"{r.ExplanationText} 建议：{r.ActionSuggestionText}",
                ReferenceUri: $"kb://rules/{r.RuleCode}",
                IsExternal: false));
        }

        // 3. 关联检索医院真实药品目录 (inv_drugs) 与 ICD-10 诊断库 (cfg_icd10_diagnoses)
        if (_connFactory != null && !string.IsNullOrWhiteSpace(kw))
        {
            using var conn = _connFactory.CreateConnection();
            var drugs = await conn.QueryAsync<dynamic>(
                "SELECT drug_code AS Code, drug_name AS Name, spec AS Spec, price AS Price, antibiotic_level AS AbLevel, is_skin_test_required AS SkinTest FROM inv_drugs WHERE is_active = 1 AND (drug_name LIKE @Kw OR drug_code LIKE @Kw) LIMIT 10",
                new { Kw = likeKw });
            foreach (var dr in drugs)
            {
                results.Add(new ExternalKnowledgeQueryResultDto(
                    SourceName: "院内药品目录主数据",
                    SourceType: "DRUG_CATALOG",
                    Title: $"{(string)dr.Name} ({(string)dr.Spec})",
                    Snippet: $"编码 {(string)dr.Code} · 零售价 ¥{Convert.ToDecimal(dr.Price):F2} · 抗菌分级 {Convert.ToInt32(dr.AbLevel)} · 皮试要求 {(Convert.ToInt32(dr.SkinTest) == 1 ? "必做" : "免试")}",
                    ReferenceUri: $"his://drugs/{(string)dr.Code}",
                    IsExternal: false));
            }
        }

        return Ok(ApiResponse<IReadOnlyList<ExternalKnowledgeQueryResultDto>>.Success(
            results,
            results.Count > 0 ? $"检索到 {results.Count} 条真实知识库与药典主数据记录" : "未检索到匹配的知识文档或规则"));
    }

    [HttpPost("rules")]
    public async Task<ActionResult<ApiResponse<string>>> CreateRule(
        [FromBody] CreateKbRuleDto req,
        CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var entity = new KbClinicalRule
        {
            Id = Guid.NewGuid().ToString(),
            RuleCode = req.RuleCode,
            RuleDomain = req.RuleDomain,
            Severity = req.Severity,
            ConditionDslJson = req.ConditionDslJson,
            ExplanationText = req.ExplanationText,
            ActionSuggestionText = req.ActionSuggestionText,
            EvidenceDocId = req.EvidenceDocId,
            IsActive = 1,
            CreatedAtMs = now,
            CreatedBy = "SYSTEM",
            UpdatedAtMs = now,
            UpdatedBy = "SYSTEM",
            VersionNo = 1
        };

        await _ruleRepo.InsertAsync(entity, ct);
        return Ok(ApiResponse<string>.Success(entity.Id, "知识库规则添加成功"));
    }

    [HttpPost("documents")]
    public async Task<ActionResult<ApiResponse<string>>> CreateDocument(
        [FromBody] CreateKbDocDto req,
        CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var entity = new KbDocument
        {
            Id = Guid.NewGuid().ToString(),
            DocCode = req.DocCode,
            Category = req.Category,
            SpecialtyDept = req.SpecialtyDept,
            Title = req.Title,
            Icd10CodesJson = req.Icd10CodesJson,
            ItemCodesJson = req.ItemCodesJson,
            ContentMarkdown = req.ContentMarkdown,
            SourceOrg = req.SourceOrg,
            VersionTag = req.VersionTag,
            IsActive = 1,
            CreatedAtMs = now,
            CreatedBy = "SYSTEM",
            UpdatedAtMs = now,
            UpdatedBy = "SYSTEM",
            VersionNo = 1
        };

        await _docRepo.InsertAsync(entity, ct);
        return Ok(ApiResponse<string>.Success(entity.Id, "知识库文档创建成功"));
    }
}
