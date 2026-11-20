using HIS.BuildingBlocks.Database;

namespace HIS.Modules.KnowledgeBase.Models;

public class KbDocument : BaseEntity
{
    public string DocCode { get; set; } = string.Empty;
    public string Category { get; set; } = "GUIDELINE";
    public string? SpecialtyDept { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Icd10CodesJson { get; set; }
    public string? ItemCodesJson { get; set; }
    public string ContentMarkdown { get; set; } = string.Empty;
    public string? SourceOrg { get; set; }
    public string VersionTag { get; set; } = "v1.0";
    public short IsActive { get; set; } = 1;
}

public class KbClinicalRule : BaseEntity
{
    public string RuleCode { get; set; } = string.Empty;
    public string RuleDomain { get; set; } = "ORDER";
    public string Severity { get; set; } = "WARNING";
    public string ConditionDslJson { get; set; } = "{}";
    public string ExplanationText { get; set; } = string.Empty;
    public string ActionSuggestionText { get; set; } = string.Empty;
    public string? EvidenceDocId { get; set; }
    public short IsActive { get; set; } = 1;
}

public class KbOrderBinding : BaseEntity
{
    public string ItemType { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string? KbDocId { get; set; }
    public string? RuleCode { get; set; }
}

public class KbExternalSource : BaseEntity
{
    public string SourceCode { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string SourceType { get; set; } = "DRUG_DB";
    public string EndpointUrl { get; set; } = string.Empty;
    public string? AuthHeaderConfig { get; set; }
    public int TimeoutMs { get; set; } = 3000;
    public short IsEnabled { get; set; } = 1;
}
