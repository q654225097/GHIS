using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.Emr.Controllers;
using HIS.Modules.Emr.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HIS.IntegrationTests;

public class StructuredEmrDesignerIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly WebApplicationFactory<Program> _factory;

    public StructuredEmrDesignerIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        SetAdminAuth(_factory, _client);
    }

    private static void SetAdminAuth(WebApplicationFactory<Program> factory, HttpClient client)
    {
        using var scope = factory.Services.CreateScope();
        var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        using var conn = connFactory.CreateConnection();
        var sessionVersion = conn.ExecuteScalar<int>(
            "SELECT session_version FROM sys_users WHERE id = 'USER-ADMIN'");

        var jwt = scope.ServiceProvider.GetRequiredService<JwtTokenService>();
        var token = jwt.GenerateAccessToken(
            userId: "USER-ADMIN",
            userName: "admin",
            displayName: "系统管理员",
            campusId: "CAMPUS-01",
            activeDeptCategory: DepartmentCategory.ADMIN,
            activeDeptId: "DEPT-ADMIN-01",
            activeWardId: null,
            linkedWardIds: null,
            roles: new[] { "ROLE-ADMIN", "ADMIN" },
            dataScope: DataScope.ALL_CAMPUS,
            sessionVersion: sessionVersion > 0 ? sessionVersion : 1,
            permHash: "ALL",
            prescribeLevel: 3,
            surgeryLevel: 4,
            emrSignLevel: 3,
            activePostId: "UDP-ADMIN-ADMIN",
            roleId: "ROLE-ADMIN",
            roleCode: "ADMIN",
            roleName: "超级管理员",
            activeDeptName: "全院管理部");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [Fact]
    public async Task EMR_Designer_FillMode_ShouldSuccessfullySaveGridCoordinates()
    {
        var req = new EmrController.SaveEmrTemplateDesignRequestDto(
            TemplateCode: "TMPL_PROGRESS",
            TemplateName: "日常查房记录",
            EmrScene: "IPD",
            DocTypeCode: "IPD_PROGRESS",
            DeptCategory: "IPD",
            TargetDeptId: null,
            DiseaseIcdPrefix: null,
            TimeLimitHours: 24,
            RequiredSignLevels: 2,
            OfdLayoutId: "LAYOUT-A4-PORTRAIT",
            IsPublished: 1,
            DesignMode: "FILL",
            PageSizeCode: "A4",
            PageWidthMm: 210,
            PageHeightMm: 297,
            MarginTopMm: 20,
            MarginBottomMm: 20,
            MarginLeftMm: 20,
            MarginRightMm: 20,
            Sections:
            [
                new EmrController.EmrSectionDesignItemDto(
                    SectionCode: "SEC_PROGRESS_FILL",
                    SectionTitle: "查房与诊疗记录 (填写模式)",
                    RenderMode: "GRID_FORM",
                    ElementCodes: ["DE_PROGRESS_DATE", "DE_PROGRESS_DOCTOR"],
                    SortOrder: 1,
                    EstimatedHeightMm: 80,
                    LayoutControls:
                    [
                        new EmrController.EmrLayoutControlDto(
                            ElementCode: "DE_PROGRESS_DATE",
                            ElementName: "查房日期时间",
                            ControlType: "DATETIME",
                            Row: 0,
                            Col: 0,
                            ColSpan: 2,
                            RowSpan: 1,
                            WidthPx: 260,
                            HeightPx: 32,
                            PrintHeightMm: 12,
                            DefaultValue: null,
                            Placeholder: "请选择查房时间",
                            UnitText: null,
                            DictCode: null,
                            IsRequired: true
                        ),
                        new EmrController.EmrLayoutControlDto(
                            ElementCode: "DE_PROGRESS_DOCTOR",
                            ElementName: "查房医师级别",
                            ControlType: "SELECT",
                            Row: 0,
                            Col: 2,
                            ColSpan: 2,
                            RowSpan: 1,
                            WidthPx: 260,
                            HeightPx: 32,
                            PrintHeightMm: 12,
                            DefaultValue: "主治医师查房",
                            Placeholder: "请选择医师级别",
                            UnitText: null,
                            DictCode: null,
                            IsRequired: true
                        )
                    ]
                )
            ]
        );

        var response = await _client.PostAsJsonAsync("/api/v4/emr/templates/design", req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<EmrController.EmrTemplateDetailDto>>();
        Assert.NotNull(result);
        Assert.Equal(200, result.Code);

        // Verify that template detail endpoint returns the saved design
        var detailRes = await _client.GetFromJsonAsync<ApiResponse<EmrController.EmrTemplateDetailDto>>("/api/v4/emr/templates/TMPL-IPD-PROGRESS");
        Assert.NotNull(detailRes?.Data);
        Assert.NotEmpty(detailRes.Data.Sections);
        Assert.NotNull(detailRes.Data.Sections[0].LayoutControls);
    }

    [Fact]
    public async Task EMR_Designer_PrintMode_WithinPaperBoundary_ShouldSucceed()
    {
        var req = new EmrController.SaveEmrTemplateDesignRequestDto(
            TemplateCode: "TMPL_PROGRESS",
            TemplateName: "日常查房记录",
            EmrScene: "IPD",
            DocTypeCode: "IPD_PROGRESS",
            DeptCategory: "IPD",
            TargetDeptId: null,
            DiseaseIcdPrefix: null,
            TimeLimitHours: 24,
            RequiredSignLevels: 2,
            OfdLayoutId: "LAYOUT-A4-PORTRAIT",
            IsPublished: 1,
            DesignMode: "PRINT",
            PageSizeCode: "A4",
            PageWidthMm: 210,
            PageHeightMm: 297,
            MarginTopMm: 25,
            MarginBottomMm: 25,
            MarginLeftMm: 20,
            MarginRightMm: 20,
            Sections:
            [
                new EmrController.EmrSectionDesignItemDto(
                    SectionCode: "SEC_PRINT_A4",
                    SectionTitle: "日常查房标准记录单",
                    RenderMode: "GRID_FORM",
                    ElementCodes: ["DE_CHIEF_COMPLAINT"],
                    SortOrder: 1,
                    EstimatedHeightMm: 120, // 66 fixed overhead + 120 = 186mm <= 247mm
                    LayoutControls:
                    [
                        new EmrController.EmrLayoutControlDto(
                            ElementCode: "DE_CHIEF_COMPLAINT",
                            ElementName: "主要主诉",
                            ControlType: "TEXTAREA",
                            Row: 0,
                            Col: 0,
                            ColSpan: 4,
                            RowSpan: 2,
                            WidthPx: 720,
                            HeightPx: 80,
                            PrintHeightMm: 45,
                            DefaultValue: null,
                            Placeholder: "患者当前主观不适",
                            UnitText: null,
                            DictCode: null,
                            IsRequired: true
                        )
                    ]
                )
            ]
        );

        var response = await _client.PostAsJsonAsync("/api/v4/emr/templates/design", req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<EmrController.EmrTemplateDetailDto>>();
        Assert.NotNull(result);
        Assert.Equal(200, result.Code);
    }

    [Fact]
    public async Task EMR_Designer_PrintMode_ExceedingPaperBoundary_MustStrictlyRejectWith400()
    {
        // A5 size: 148 x 210mm. Margins: top 25, bottom 25 -> Available = 160mm.
        // Fixed overhead = 66mm.
        // If section height = 150mm -> Total = 216mm > 160mm -> Must reject!
        var req = new EmrController.SaveEmrTemplateDesignRequestDto(
            TemplateCode: "TMPL_PROGRESS",
            TemplateName: "日常查房记录",
            EmrScene: "IPD",
            DocTypeCode: "IPD_PROGRESS",
            DeptCategory: "IPD",
            TargetDeptId: null,
            DiseaseIcdPrefix: null,
            TimeLimitHours: 24,
            RequiredSignLevels: 2,
            OfdLayoutId: "LAYOUT-A4-PORTRAIT",
            IsPublished: 1,
            DesignMode: "PRINT",
            PageSizeCode: "A5",
            PageWidthMm: 148,
            PageHeightMm: 210,
            MarginTopMm: 25,
            MarginBottomMm: 25,
            MarginLeftMm: 15,
            MarginRightMm: 15,
            Sections:
            [
                new EmrController.EmrSectionDesignItemDto(
                    SectionCode: "SEC_OVERFLOW",
                    SectionTitle: "超长化疗与监护全记录",
                    RenderMode: "GRID_FORM",
                    ElementCodes: [],
                    SortOrder: 1,
                    EstimatedHeightMm: 150,
                    LayoutControls: []
                )
            ]
        );

        var response = await _client.PostAsJsonAsync("/api/v4/emr/templates/design", req);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("打印模式尺寸超限", content);
        Assert.Contains("超限", content);
    }

    [Fact]
    public async Task EMR_UserTemplates_FullLifecycle_Create_Query_And_Delete()
    {
        // 1. Create a personal user template
        var createReq = new EmrController.SaveEmrUserTemplateRequestDto(
            Id: null,
            BaseTemplateId: "TMPL-IPD-PROGRESS",
            TemplateCode: $"TEST_UTPL_{Guid.NewGuid():N}",
            TemplateName: "集成测试高血压标准查房模板",
            EmrScene: "IPD",
            DocTypeCode: "IPD_PROGRESS",
            DefaultContentJson: "{\"PROGRESS_CONTENT\":\"患者血压平稳，心肺听诊未闻及明显干湿啰音。\"}",
            IsSharedDept: true,
            SortOrder: 10
        );

        var createRes = await _client.PostAsJsonAsync("/api/v4/emr/user-templates", createReq);
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);

        var created = await createRes.Content.ReadFromJsonAsync<ApiResponse<EmrUserTemplate>>();
        Assert.NotNull(created?.Data);
        Assert.Equal("集成测试高血压标准查房模板", created.Data.TemplateName);
        var tplId = created.Data.Id;

        // 2. Query user templates
        var queryRes = await _client.GetFromJsonAsync<ApiResponse<List<EmrUserTemplate>>>("/api/v4/emr/user-templates?emrScene=IPD&docTypeCode=IPD_PROGRESS");
        Assert.NotNull(queryRes?.Data);
        Assert.Contains(queryRes.Data, t => t.Id == tplId);

        // 3. Delete user template
        var delRes = await _client.DeleteAsync($"/api/v4/emr/user-templates/{tplId}");
        Assert.Equal(HttpStatusCode.OK, delRes.StatusCode);

        // 4. Verify gone
        var verifyRes = await _client.GetFromJsonAsync<ApiResponse<List<EmrUserTemplate>>>("/api/v4/emr/user-templates?emrScene=IPD&docTypeCode=IPD_PROGRESS");
        Assert.NotNull(verifyRes?.Data);
        Assert.DoesNotContain(verifyRes.Data, t => t.Id == tplId);
    }
}
