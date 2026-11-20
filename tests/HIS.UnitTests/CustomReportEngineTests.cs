using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.Operations.Controllers;
using HIS.Modules.Operations.Models;
using Xunit;

namespace HIS.UnitTests;

public class CustomReportEngineTests
{
    private async Task<(IDbConnectionFactory connFactory, CustomReportController controller, UserSession adminSession)> CreateTestControllerAsync()
    {
        string connStr = $"Data Source=memdb_reports_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var connFactory = new SqliteConnectionFactory(connStr);
        await DbSchemaInitializer.InitializeAsync(connFactory);

        var reportRepo = new DapperRepository<OpsReportDefinition>(connFactory, "ops_report_definitions");
        var execRepo = new DapperRepository<OpsReportExecution>(connFactory, "ops_report_executions");
        var adminSession = new UserSession
        {
            UserId = "USER-ADMIN-01",
            UserName = "系统管理员",
            DisplayName = "系统管理员",
            RoleCode = RoleCode.Admin,
            Roles = new[] { RoleCode.Admin }
        };

        var controller = new CustomReportController(connFactory, reportRepo, execRepo, adminSession);
        return (connFactory, controller, adminSession);
    }

    [Fact]
    public async Task SqlValidation_ShouldAllow_SafeSelectQueries_And_ExtractParameters()
    {
        var (_, controller, _) = await CreateTestControllerAsync();

        string sql = @"
SELECT 
    v.dept_name AS 科室名称,
    COUNT(1) AS 就诊人次
FROM opd_visits v
WHERE (@StartDate IS NULL OR v.visit_date_int >= @StartDate)
  AND (@EndDate IS NULL OR v.visit_date_int <= @EndDate)
  AND (@DeptName IS NULL OR v.dept_name LIKE '%' || @DeptName || '%')
GROUP BY v.dept_name";

        var res = controller.ValidateSqlEndpoint(new CustomReportController.ValidateSqlRequest(sql));

        Assert.Equal(200, res.Code);
        Assert.NotNull(res.Data);
        Assert.True(res.Data.IsValid);
        Assert.Null(res.Data.ErrorMessage);

        // Extracted parameters
        Assert.Equal(3, res.Data.ExtractedParameters.Count);
        Assert.Contains(res.Data.ExtractedParameters, p => p.Name == "StartDate" && p.Type == "INT");
        Assert.Contains(res.Data.ExtractedParameters, p => p.Name == "EndDate" && p.Type == "INT");
        Assert.Contains(res.Data.ExtractedParameters, p => p.Name == "DeptName" && p.Type == "STRING");
    }

    [Theory]
    [InlineData("DROP TABLE pat_patients;")]
    [InlineData("DELETE FROM opd_visits WHERE id = '1'")]
    [InlineData("UPDATE sys_users SET is_active = 0")]
    [InlineData("INSERT INTO sys_roles (id) VALUES ('HACK')")]
    [InlineData("ALTER TABLE sys_departments ADD COLUMN test INT")]
    [InlineData("TRUNCATE TABLE fin_payment_orders")]
    [InlineData("SELECT * FROM sys_users; DROP TABLE sys_users;")]
    public async Task SqlValidation_ShouldReject_DangerousOrDmlStatements(string dangerousSql)
    {
        var (_, controller, _) = await CreateTestControllerAsync();

        var res = controller.ValidateSqlEndpoint(new CustomReportController.ValidateSqlRequest(dangerousSql));

        Assert.Equal(200, res.Code);
        Assert.NotNull(res.Data);
        Assert.False(res.Data.IsValid);
        Assert.NotNull(res.Data.ErrorMessage);
    }

    [Fact]
    public async Task ReportExecution_WithDynamicParameters_ShouldFilterResultsCorrectly()
    {
        var (connFactory, controller, _) = await CreateTestControllerAsync();

        // Seed test data in sys_departments and opd_visits
        using (var conn = connFactory.CreateConnection())
        {
            await conn.ExecuteAsync(@"
INSERT INTO sys_departments (id, campus_id, dept_code, dept_name, pinyin_code, dept_category, sub_specialty_type, rated_bed_count, is_allow_appointment, is_active, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES 
('D1', 'CAMPUS-01', 'OPD_CARDIO', '心血管内科门诊', 'XG', 'OPD', 'CARDIOLOGY', 0, 1, 1, 1, 1000, 1000, 1),
('D2', 'CAMPUS-01', 'OPD_NEURO', '神经内科门诊', 'SJ', 'OPD', 'NEUROLOGY', 0, 1, 1, 1, 1000, 1000, 1);

INSERT INTO opd_visits (id, visit_no, campus_id, opd_dept_id, clinic_room_id, patient_id, doctor_id, is_emergency, is_green_channel, is_return_visit, status, schema_version, created_at_ms, updated_at_ms, version_no)
VALUES 
('V1', 'VN001', 'CAMPUS-01', 'D1', 'ROOM-1', 'P1', 'DOC1', 0, 0, 0, 2, 1, 1000, 1000, 1),
('V2', 'VN002', 'CAMPUS-01', 'D1', 'ROOM-1', 'P2', 'DOC1', 0, 0, 0, 2, 1, 1000, 1000, 1),
('V3', 'VN003', 'CAMPUS-01', 'D2', 'ROOM-2', 'P3', 'DOC2', 0, 0, 0, 2, 1, 1000, 1000, 1),
('V4', 'VN004', 'CAMPUS-01', 'D2', 'ROOM-2', 'P4', 'DOC2', 0, 0, 0, 1, 1, 1000, 1000, 1);
");
        }

        // Create report definition
        var createRes = await controller.CreateDefinition(new CustomReportController.UpsertReportDefinitionRequest(
            ReportCode: "RPT_OPD_TEST_SUMMARY",
            ReportName: "门诊就诊测试统计",
            Category: "门急诊",
            Description: "测试动态参数筛选",
            SqlTemplate: @"
SELECT 
    d.dept_name AS 科室名称,
    COUNT(v.id) AS 就诊人次
FROM opd_visits v
JOIN sys_departments d ON d.id = v.opd_dept_id
WHERE (@Status IS NULL OR v.status = @Status)
  AND (@DeptName IS NULL OR @DeptName = '' OR d.dept_name LIKE '%' || @DeptName || '%')
GROUP BY d.dept_name
ORDER BY 就诊人次 DESC",
            ParametersSchemaJson: @"[
  { ""Name"": ""Status"", ""Label"": ""就诊状态"", ""Type"": ""INT"", ""DefaultValue"": ""2"" },
  { ""Name"": ""DeptName"", ""Label"": ""科室名称"", ""Type"": ""STRING"", ""DefaultValue"": """" }
]",
            DisplayColumnsJson: null,
            IsPublished: 1
        ));

        Assert.Equal(200, createRes.Code);
        string reportId = createRes.Data!.Id!;

        // 1. Query all without filtering dept (status = 2)
        var execResAll = await controller.ExecuteReport(reportId, new Dictionary<string, object?>
        {
            ["Status"] = 2,
            ["DeptName"] = ""
        });

        Assert.Equal(200, execResAll.Code);
        Assert.NotNull(execResAll.Data);
        Assert.Equal(2, execResAll.Data.Rows.Count); // Two departments with status 2
        Assert.Contains(execResAll.Data.Rows, r => r.Contains("心血管内科门诊") && r.Contains("2"));
        Assert.Contains(execResAll.Data.Rows, r => r.Contains("神经内科门诊") && r.Contains("1"));

        // 2. Query filtered by dept dynamically
        var execResFiltered = await controller.ExecuteReport(reportId, new Dictionary<string, object?>
        {
            ["Status"] = 2,
            ["DeptName"] = "心血管"
        });

        Assert.Equal(200, execResFiltered.Code);
        Assert.NotNull(execResFiltered.Data);
        Assert.Single(execResFiltered.Data.Rows);
        Assert.Equal("心血管内科门诊", execResFiltered.Data.Rows[0][0]);
        Assert.Equal("2", execResFiltered.Data.Rows[0][1]); // 2 visits

        // 3. Verify execution history audit log was recorded
        var historyRes = await controller.GetExecutionHistory(reportId);
        Assert.Equal(200, historyRes.Code);
        Assert.NotNull(historyRes.Data);
        Assert.True(historyRes.Data.Count >= 2);
    }

    [Fact]
    public async Task ReportDefinition_CrudOperations_ShouldWorkCorrectly()
    {
        var (_, controller, _) = await CreateTestControllerAsync();

        // 1. Create
        var createReq = new CustomReportController.UpsertReportDefinitionRequest(
            ReportCode: "RPT_DRUG_STAT_01",
            ReportName: "药品销售统计",
            Category: "药事进销存",
            Description: "全院药品销量统计",
            SqlTemplate: "SELECT 'AMOX' AS 药品编码, 100 AS 数量",
            ParametersSchemaJson: null,
            DisplayColumnsJson: null,
            IsPublished: 1
        );
        var createRes = await controller.CreateDefinition(createReq);
        Assert.Equal(200, createRes.Code);
        Assert.NotNull(createRes.Data?.Id);
        string id = createRes.Data.Id;

        // 2. Read
        var getRes = await controller.GetDefinitionById(id);
        Assert.Equal(200, getRes.Code);
        Assert.Equal("药品销售统计", getRes.Data!.ReportName);

        // 3. Update
        var updateReq = new CustomReportController.UpsertReportDefinitionRequest(
            ReportCode: "RPT_DRUG_STAT_01",
            ReportName: "药品销售统计(已更新)",
            Category: "药事进销存",
            Description: "全院药品销量更新版",
            SqlTemplate: "SELECT 'AMOX' AS 药品编码, 200 AS 数量",
            ParametersSchemaJson: null,
            DisplayColumnsJson: null,
            IsPublished: 1
        );
        var updateRes = await controller.UpdateDefinition(id, updateReq);
        Assert.Equal(200, updateRes.Code);
        Assert.Equal("药品销售统计(已更新)", updateRes.Data!.ReportName);

        // 4. Delete
        var delRes = await controller.DeleteDefinition(id);
        Assert.Equal(200, delRes.Code);

        // 5. Verify 404 after delete
        var getAfterDel = await controller.GetDefinitionById(id);
        Assert.Equal(404, getAfterDel.Code);
    }
}
