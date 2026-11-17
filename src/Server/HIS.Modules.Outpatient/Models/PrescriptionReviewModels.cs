using System.Collections.Generic;
using System.Linq;

namespace HIS.Modules.Outpatient.Models;

public sealed class PrescriptionReviewResultDto
{
    public string OverallStatus { get; set; } = "PASS"; // "PASS" | "WARNING" | "BLOCK"
    public bool CanProceed => OverallStatus != "BLOCK";
    public int TotalIssues => Issues.Count;
    public int BlockCount => Issues.Count(x => x.Severity == "BLOCK");
    public int WarningCount => Issues.Count(x => x.Severity == "WARNING");
    public int InfoCount => Issues.Count(x => x.Severity == "INFO");
    public List<PrescriptionReviewIssueDto> Issues { get; set; } = new();
    public string SummaryMessage { get; set; } = string.Empty;

    public static PrescriptionReviewResultDto Pass(string summary = "处方自动审方通过，未发现用药安全阻断与警示项")
    {
        return new PrescriptionReviewResultDto
        {
            OverallStatus = "PASS",
            SummaryMessage = summary
        };
    }
}

public sealed class PrescriptionReviewIssueDto
{
    public string RuleCode { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string Severity { get; set; } = "BLOCK"; // "BLOCK" | "WARNING" | "INFO"
    public string? DrugId { get; set; }
    public string? DrugName { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string ClinicalSuggestion { get; set; } = string.Empty;
    public string ReferenceStandard { get; set; } = string.Empty;
}

public sealed class PrescriptionPreCheckRequestDto
{
    public string PatientId { get; set; } = string.Empty;
    public string? DoctorId { get; set; }
    public int? PrescribeLevel { get; set; }
    public string? AgentIdCard { get; set; }
    public List<PrescriptionPreCheckItemDto> Items { get; set; } = new();
}

public sealed class PrescriptionPreCheckItemDto
{
    public string DrugId { get; set; } = string.Empty;
    public string? DrugName { get; set; }
    public decimal DoseOnce { get; set; }
    public string DoseUnit { get; set; } = "mg";
    public string FrequencyCode { get; set; } = "QD";
    public string RouteCode { get; set; } = "PO";
    public int Days { get; set; } = 3;
    public int TotalQuantity { get; set; } = 1;
    public int SkinTestFlag { get; set; } = 0;
}
