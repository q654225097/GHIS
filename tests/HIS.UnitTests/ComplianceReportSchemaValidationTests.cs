using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace HIS.UnitTests;

public class ComplianceReportSchemaValidationTests
{
    private const string ReportPath = @"C:\Users\admin\Desktop\GHIS-compliance-report.json";

    [Fact]
    public void ComplianceReportJson_ConformsStrictlyToSchema_AndHasAllFindingsVerified()
    {
        Assert.True(File.Exists(ReportPath), $"Report file not found at {ReportPath}");

        var jsonText = File.ReadAllText(ReportPath);
        using var doc = JsonDocument.Parse(jsonText);
        var root = doc.RootElement;

        // 1. report_meta
        Assert.True(root.TryGetProperty("report_meta", out var meta));
        Assert.Matches(@"^GHIS-CR-[0-9]{4}-[0-9]{2}$", meta.GetProperty("report_id").GetString()!);
        Assert.Equal("GHIS", meta.GetProperty("target_system").GetString());
        Assert.False(string.IsNullOrWhiteSpace(meta.GetProperty("title").GetString()));
        Assert.True(meta.GetProperty("review_method").GetArrayLength() > 0);
        Assert.True(meta.GetProperty("evidence_batches").GetArrayLength() > 0);

        // 2. standards
        Assert.True(root.TryGetProperty("standards", out var standards));
        Assert.True(standards.GetArrayLength() > 0);
        foreach (var std in standards.EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(std.GetProperty("standard_id").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(std.GetProperty("family").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(std.GetProperty("title").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(std.GetProperty("version").GetString()));
        }

        // 3. executive_summary
        Assert.True(root.TryGetProperty("executive_summary", out var execSummary));
        Assert.False(string.IsNullOrWhiteSpace(execSummary.GetProperty("overall_conclusion").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(execSummary.GetProperty("roadmap_summary").GetString()));
        Assert.True(execSummary.GetProperty("top_risks").GetArrayLength() > 0);
        var severityCounts = execSummary.GetProperty("counts_by_severity");
        Assert.Equal(0, severityCounts.GetProperty("critical").GetInt32());
        Assert.Equal(0, severityCounts.GetProperty("high").GetInt32());
        Assert.Equal(0, severityCounts.GetProperty("medium").GetInt32());
        Assert.Equal(0, severityCounts.GetProperty("low").GetInt32());

        // 4. findings
        Assert.True(root.TryGetProperty("findings", out var findings));
        Assert.Equal(20, findings.GetArrayLength());

        var validSeverities = new[] { "critical", "high", "medium", "low" };
        var validPriorities = new[] { "P0", "P1", "P2", "P3" };
        var validEfforts = new[] { "S", "M", "L" };

        foreach (var finding in findings.EnumerateArray())
        {
            var id = finding.GetProperty("id").GetString()!;
            Assert.Matches(@"^F-[0-9]{3}$", id);
            Assert.False(string.IsNullOrWhiteSpace(finding.GetProperty("title").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(finding.GetProperty("category").GetString()));
            Assert.Contains(finding.GetProperty("severity").GetString(), validSeverities);
            Assert.Equal("verified", finding.GetProperty("status").GetString());
            Assert.True(finding.GetProperty("standard_refs").GetArrayLength() >= 1);
            Assert.True(finding.GetProperty("evidence").GetArrayLength() >= 1);
            Assert.False(string.IsNullOrWhiteSpace(finding.GetProperty("impact").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(finding.GetProperty("remediation_summary").GetString()));
            Assert.Contains(finding.GetProperty("priority").GetString(), validPriorities);
            if (finding.TryGetProperty("effort", out var effortProp) && effortProp.GetString() != null)
            {
                Assert.Contains(effortProp.GetString(), validEfforts);
            }
        }

        // 5. missing_features
        Assert.True(root.TryGetProperty("missing_features", out var missingFeatures));
        Assert.True(missingFeatures.GetArrayLength() > 0);
        foreach (var mf in missingFeatures.EnumerateArray())
        {
            Assert.Matches(@"^M-[0-9]{3}$", mf.GetProperty("id").GetString()!);
            Assert.False(string.IsNullOrWhiteSpace(mf.GetProperty("title").GetString()));
            Assert.Contains(mf.GetProperty("priority").GetString(), validPriorities);
        }

        // 6. remediation_plan
        Assert.True(root.TryGetProperty("remediation_plan", out var plan));
        Assert.True(plan.GetProperty("phases").GetArrayLength() > 0);

        // 7. appendix
        Assert.True(root.TryGetProperty("appendix", out var appendix));
        Assert.True(appendix.TryGetProperty("table_inventory", out var tableInventory));
        Assert.True(tableInventory.GetArrayLength() > 0);

        var activatedDormantTables = new[]
        {
            "emr_inpatient_front_sheets",
            "opd_infectious_report_cards",
            "medtech_critical_values",
            "inv_drug_trace_codes",
            "inv_stock_takes",
            "inv_stock_take_lines",
            "fin_inpatient_fee_items"
        };

        foreach (var table in tableInventory.EnumerateArray())
        {
            var name = table.GetProperty("table").GetString();
            var status = table.GetProperty("status").GetString();
            if (Array.IndexOf(activatedDormantTables, name) >= 0)
            {
                Assert.Equal("used", status);
            }
        }

        Assert.True(appendix.TryGetProperty("api_inventory", out var apiInventory));
        Assert.True(apiInventory.GetArrayLength() > 140);
    }
}
