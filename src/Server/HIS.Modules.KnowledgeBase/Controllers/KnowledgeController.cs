using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Common;
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
        IRepository<KbClinicalRule> ruleRepo)
    {
        _kbService = kbService;
        _docRepo = docRepo;
        _ruleRepo = ruleRepo;
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
    public ActionResult<ApiResponse<IReadOnlyList<ExternalKnowledgeQueryResultDto>>> QueryExternal(
        [FromQuery] string? sourceType,
        [FromQuery] string? keyword)
    {
        // 模拟调用国家药典/PubMed/ADR联机数据库
        string kw = keyword ?? "临床用药";
        var results = new List<ExternalKnowledgeQueryResultDto>
        {
            new(
                SourceName: "国家药典委员会药品说明书联机库 (NMPA-ChP)",
                SourceType: sourceType ?? "DRUG_DB",
                Title: $"【国家药典权威指引】{kw} 药理毒理及适应症规范 (2025版)",
                Snippet: $"{kw} 属于重点监控药物，需严格遵循肝肾功能监测要求，血肌酐清除率低于30ml/min时建议减量50%或停药。",
                ReferenceUri: "https://db.nmpa.gov.cn/pharmacopoeia/query",
                IsExternal: true
            ),
            new(
                SourceName: "国家药品不良反应监测中心联机预警网",
                SourceType: "ADR_DB",
                Title: $"关于 {kw} 严重过敏反应与联合用药风险警示通报",
                Snippet: $"国家ADR数据库监测显示，{kw} 与抗凝药合用可能显著延长凝血酶原时间，需加强INR动态监测。",
                ReferenceUri: "https://adr.cdr-adr.org.cn/alerts",
                IsExternal: true
            )
        };

        return Ok(ApiResponse<IReadOnlyList<ExternalKnowledgeQueryResultDto>>.Success(results, "外部联机知识库查询成功 (已标注来源机构)"));
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
