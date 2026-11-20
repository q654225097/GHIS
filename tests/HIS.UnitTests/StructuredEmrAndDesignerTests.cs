using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.Emr.Controllers;
using HIS.Modules.Emr.Models;
using HIS.Modules.Emr.Services;
using Xunit;

namespace HIS.UnitTests;

public class StructuredEmrAndDesignerTests
{
    private sealed class MockPatientContract : IPatientReadOnlyContract
    {
        public Task<PatientSummaryDto?> GetPatientSummaryAsync(string patientId, CancellationToken ct = default)
        {
            return Task.FromResult<PatientSummaryDto?>(new PatientSummaryDto(
                Id: patientId,
                MedicalRecordNo: "MRN-001",
                FullName: "测试患者",
                GenderCode: "1",
                BirthDateInt: 19800101,
                AgeYears: 45,
                PhoneMasked: "138****0000",
                AllergyTags: Array.Empty<string>(),
                IsTempEmergency: false,
                IsActive: true
            ));
        }

        public Task<IReadOnlyList<PatientSummaryDto>> SearchPatientsAsync(string keyword, int limit = 20, CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<PatientSummaryDto>>(Array.Empty<PatientSummaryDto>());
        }

        public Task<bool> AppendPatientAllergyAsync(string patientId, string allergyTag, CancellationToken ct = default)
        {
            return Task.FromResult(true);
        }
    }

    private async Task<(IDbConnectionFactory connFactory, EmrController controller, UserSession session)> CreateTestControllerAsync(
        string userId = "USER-DOC-01",
        string role = RoleCode.DoctorIpd,
        string deptId = "DEPT-IPD-01")
    {
        string connStr = $"Data Source=memdb_emr_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var connFactory = new SqliteConnectionFactory(connStr);
        await DbSchemaInitializer.InitializeAsync(connFactory);

        var docRepo = new DapperRepository<EmrDocument>(connFactory, "emr_documents");
        var docElementRepo = new DapperRepository<EmrDocumentElement>(connFactory, "emr_document_elements");
        var archiveRepo = new DapperRepository<EmrOfdArchive>(connFactory, "emr_ofd_archives");
        var dataElementRepo = new DapperRepository<EmrDataElement>(connFactory, "emr_data_elements");
        var layoutRepo = new DapperRepository<EmrOfdLayoutSchema>(connFactory, "emr_ofd_layout_schemas");
        var templateRepo = new DapperRepository<EmrDocTemplate>(connFactory, "emr_doc_templates");
        var sectionRepo = new DapperRepository<EmrSectionSchema>(connFactory, "emr_section_schemas");
        var frontSheetRepo = new DapperRepository<EmrInpatientFrontSheet>(connFactory, "emr_inpatient_front_sheets");
        var userTemplateRepo = new DapperRepository<EmrUserTemplate>(connFactory, "emr_user_templates");

        var patientContract = new MockPatientContract();
        var qcService = new EmrQualityControlService(dataElementRepo, patientContract);
        var ofdRenderService = new EmrOfdRenderService(archiveRepo, templateRepo, layoutRepo);

        var session = new UserSession
        {
            UserId = userId,
            UserName = "测试医师",
            DisplayName = "测试医师",
            RoleCode = role,
            Roles = new[] { role },
            ActiveDeptId = deptId
        };

        var controller = new EmrController(
            docRepo,
            docElementRepo,
            archiveRepo,
            qcService,
            ofdRenderService,
            dataElementRepo,
            layoutRepo,
            templateRepo,
            sectionRepo,
            patientContract: patientContract,
            connFactory: connFactory,
            auditTrail: null,
            userSession: session,
            frontSheetRepo: frontSheetRepo,
            userTemplateRepo: userTemplateRepo
        );

        return (connFactory, controller, session);
    }

    [Fact]
    public async Task Designer_FillMode_ShouldSaveStructuredControlsAndGridProperties()
    {
        var (_, controller, _) = await CreateTestControllerAsync(role: RoleCode.Admin);

        var request = new EmrController.SaveEmrTemplateDesignRequestDto(
            TemplateCode: "TMPL_IPD_CUSTOM_ROUND",
            TemplateName: "住院高级查房记录结构化模板",
            EmrScene: "IPD",
            DocTypeCode: "IPD_PROGRESS",
            DeptCategory: "IPD",
            TargetDeptId: "DEPT-IPD-01",
            DiseaseIcdPrefix: "I25",
            TimeLimitHours: 8,
            RequiredSignLevels: 2,
            OfdLayoutId: "LAYOUT-IPD-A4",
            IsPublished: 1,
            DesignMode: "FILL",
            Sections: new List<EmrController.EmrSectionDesignItemDto>
            {
                new EmrController.EmrSectionDesignItemDto(
                    SectionCode: "SEC_DAILY_ROUND",
                    SectionTitle: "日常查房重点信息",
                    RenderMode: "GRID_FORM",
                    ElementCodes: new List<string> { "PROGRESS_CONTENT", "PHYSICAL_EXAM" },
                    LayoutControls: new List<EmrController.EmrLayoutControlDto>
                    {
                        new EmrController.EmrLayoutControlDto(
                            ElementCode: "PROGRESS_CONTENT",
                            ElementName: "病程查房小结",
                            ControlType: "MULTI_TEXT",
                            Row: 0,
                            Col: 0,
                            ColSpan: 2,
                            WidthPx: 600,
                            HeightPx: 90,
                            PrintHeightMm: 30.0m,
                            Placeholder: "请详细记录主诉、生命体征与今日查房分析..."
                        ),
                        new EmrController.EmrLayoutControlDto(
                            ElementCode: "PHYSICAL_EXAM",
                            ElementName: "心肺腹部查体",
                            ControlType: "SINGLE_TEXT",
                            Row: 1,
                            Col: 0,
                            ColSpan: 1,
                            WidthPx: 300,
                            HeightPx: 32,
                            PrintHeightMm: 8.0m,
                            Placeholder: "双下肺未闻及啰音，律齐"
                        )
                    }
                )
            }
        );

        var res = await controller.SaveTemplateDesign(request);

        Assert.Equal(200, res.Code);
        Assert.NotNull(res.Data);
        Assert.Equal("TMPL_IPD_CUSTOM_ROUND", res.Data.Template.TemplateCode);
        Assert.Single(res.Data.Sections);

        var sec = res.Data.Sections[0];
        Assert.Equal("SEC_DAILY_ROUND", sec.SectionCode);
        Assert.NotNull(sec.LayoutControls);
        Assert.Equal(2, sec.LayoutControls.Count);
        Assert.Equal("MULTI_TEXT", sec.LayoutControls[0].ControlType);
        Assert.Equal(2, sec.LayoutControls[0].ColSpan);
    }

    [Fact]
    public async Task Designer_PrintMode_WithinPaperBoundary_ShouldSucceed()
    {
        var (_, controller, _) = await CreateTestControllerAsync(role: RoleCode.Admin);

        // A4 Paper: 297mm height, 20mm margins => 257mm available.
        // Fixed overhead = 66mm. Remaining for sections = 191mm.
        // Section: 8mm header + 30mm body = 38mm. Total ~104mm < 257mm. Should succeed!
        var request = new EmrController.SaveEmrTemplateDesignRequestDto(
            TemplateCode: "TMPL_WARD_CARE_A4",
            TemplateName: "病区护理观察记录 A4标准版式",
            EmrScene: "WARD",
            DocTypeCode: "NURSING_CRITICAL",
            DeptCategory: "WARD",
            TargetDeptId: "DEPT-WARD-01",
            DiseaseIcdPrefix: null,
            TimeLimitHours: 4,
            RequiredSignLevels: 2,
            OfdLayoutId: "LAYOUT-WARD-A4",
            IsPublished: 1,
            DesignMode: "PRINT",
            PageSizeCode: "A4",
            PageWidthMm: 210.0m,
            PageHeightMm: 297.0m,
            MarginTopMm: 20.0m,
            MarginBottomMm: 20.0m,
            MarginLeftMm: 20.0m,
            MarginRightMm: 20.0m,
            Sections: new List<EmrController.EmrSectionDesignItemDto>
            {
                new EmrController.EmrSectionDesignItemDto(
                    SectionCode: "SEC_VITALS",
                    SectionTitle: "生命体征与意识状态",
                    RenderMode: "GRID_FORM",
                    ElementCodes: new List<string> { "NURSING_CONSCIOUSNESS", "PHYSICAL_EXAM" },
                    EstimatedHeightMm: 35.0m
                ),
                new EmrController.EmrSectionDesignItemDto(
                    SectionCode: "SEC_CARE_PLAN",
                    SectionTitle: "护理干预与执行措施",
                    RenderMode: "INLINE_PARAGRAPH",
                    ElementCodes: new List<string> { "NURSING_INTERVENTION" },
                    EstimatedHeightMm: 45.0m
                )
            }
        );

        var res = await controller.SaveTemplateDesign(request);

        Assert.Equal(200, res.Code);
        Assert.NotNull(res.Data);
        Assert.Equal("TMPL_WARD_CARE_A4", res.Data.Template.TemplateCode);
    }

    [Fact]
    public async Task Designer_PrintMode_ExceedingPaperBoundary_ShouldStrictlyReject()
    {
        var (_, controller, _) = await CreateTestControllerAsync(role: RoleCode.Admin);

        // A5 Paper: 210mm height, 20mm margins => 170mm available.
        // Fixed overhead = 66mm. Available for sections = 104mm.
        // We configure sections totaling 170mm + 16mm headers = 186mm.
        // Total = 66 + 186 = 252mm > 170mm. Should be REJECTED!
        var request = new EmrController.SaveEmrTemplateDesignRequestDto(
            TemplateCode: "TMPL_OVERFLOW_DOC",
            TemplateName: "超限病历模板",
            EmrScene: "IPD",
            DocTypeCode: "IPD_ADMISSION",
            DeptCategory: "IPD",
            TargetDeptId: "DEPT-IPD-01",
            DiseaseIcdPrefix: null,
            TimeLimitHours: 24,
            RequiredSignLevels: 2,
            OfdLayoutId: "LAYOUT-OPD-B5",
            IsPublished: 1,
            DesignMode: "PRINT",
            PageSizeCode: "A5",
            PageWidthMm: 148.0m,
            PageHeightMm: 210.0m,
            MarginTopMm: 20.0m,
            MarginBottomMm: 20.0m,
            MarginLeftMm: 15.0m,
            MarginRightMm: 15.0m,
            Sections: new List<EmrController.EmrSectionDesignItemDto>
            {
                new EmrController.EmrSectionDesignItemDto(
                    SectionCode: "SEC_BIG_1",
                    SectionTitle: "章节1 - 巨额体格检查",
                    RenderMode: "GRID_FORM",
                    ElementCodes: new List<string> { "CHIEF_COMPLAINT" },
                    EstimatedHeightMm: 80.0m
                ),
                new EmrController.EmrSectionDesignItemDto(
                    SectionCode: "SEC_BIG_2",
                    SectionTitle: "章节2 - 巨额诊疗计划",
                    RenderMode: "GRID_FORM",
                    ElementCodes: new List<string> { "TREATMENT_PLAN" },
                    EstimatedHeightMm: 90.0m
                )
            }
        );

        var res = await controller.SaveTemplateDesign(request);

        Assert.Equal(400, res.Code);
        Assert.Contains("[打印模式尺寸超限]", res.Message);
        Assert.Contains("超过纸张 A5 可打印有效高度", res.Message);
    }

    [Fact]
    public async Task UserTemplates_DoctorCanCreate_Query_And_IsolateOthers()
    {
        var (connFactory, docController, _) = await CreateTestControllerAsync(
            userId: "USER-DOC-LI",
            role: RoleCode.DoctorIpd,
            deptId: "DEPT-IPD-01");

        // 1. Create personal template derived from TMPL-IPD-PROGRESS
        var createReq = new EmrController.SaveEmrUserTemplateRequestDto(
            Id: null,
            BaseTemplateId: "TMPL-IPD-PROGRESS",
            TemplateCode: "UTPL_DOC_MY_CORONARY",
            TemplateName: "李医师-冠心病初次查房常用模板",
            EmrScene: "IPD",
            DocTypeCode: "IPD_PROGRESS",
            DefaultContentJson: "{\"PROGRESS_CONTENT\":\"今日查房：患者胸闷改善，双肺呼吸音清，心率72次/分。继续抗凝抗板。\"}",
            IsSharedDept: true,
            SortOrder: 1
        );

        var createRes = await docController.SaveUserTemplate(createReq);
        Assert.Equal(200, createRes.Code);
        Assert.NotNull(createRes.Data);
        Assert.Equal("李医师-冠心病初次查房常用模板", createRes.Data.TemplateName);
        Assert.Equal("USER-DOC-LI", createRes.Data.UserId);
        Assert.Equal(1, createRes.Data.IsSharedDept);

        // 2. Query user templates for Dr. Li
        var queryRes = await docController.GetUserTemplates(emrScene: "IPD");
        Assert.Equal(200, queryRes.Code);
        Assert.NotNull(queryRes.Data);
        Assert.Contains(queryRes.Data, t => t.TemplateCode == "UTPL_DOC_MY_CORONARY");

        // 3. Nurse in another dept queries user templates (should not see Dr. Li's template)
        var (_, nurseController, _) = await CreateTestControllerAsync(
            userId: "USER-NURSE-ZHANG",
            role: RoleCode.NurseIpd,
            deptId: "DEPT-WARD-99");

        var nurseQueryRes = await nurseController.GetUserTemplates(emrScene: "IPD");
        Assert.Equal(200, nurseQueryRes.Code);
        Assert.NotNull(nurseQueryRes.Data);
        Assert.DoesNotContain(nurseQueryRes.Data, t => t.TemplateCode == "UTPL_DOC_MY_CORONARY");

        // 4. Nurse attempts to delete Dr. Li's template -> Should be 403 Forbidden!
        // We reuse the docController's DB by passing createRes.Data.Id to a controller bound to nurse
        var nurseSession = new UserSession
        {
            UserId = "USER-NURSE-ZHANG",
            UserName = "张护士",
            DisplayName = "张护士",
            RoleCode = RoleCode.NurseIpd,
            Roles = new[] { RoleCode.NurseIpd },
            ActiveDeptId = "DEPT-WARD-99"
        };
        var nurseWithSameDb = new EmrController(
            new DapperRepository<EmrDocument>(connFactory, "emr_documents"),
            new DapperRepository<EmrDocumentElement>(connFactory, "emr_document_elements"),
            new DapperRepository<EmrOfdArchive>(connFactory, "emr_ofd_archives"),
            new EmrQualityControlService(new DapperRepository<EmrDataElement>(connFactory, "emr_data_elements"), new MockPatientContract()),
            new EmrOfdRenderService(new DapperRepository<EmrOfdArchive>(connFactory, "emr_ofd_archives")),
            connFactory: connFactory,
            userSession: nurseSession,
            userTemplateRepo: new DapperRepository<EmrUserTemplate>(connFactory, "emr_user_templates")
        );

        var deleteRes = await nurseWithSameDb.DeleteUserTemplate(createRes.Data.Id);
        Assert.Equal(403, deleteRes.Code);

        // 5. Dr. Li can delete own template
        var docDeleteRes = await docController.DeleteUserTemplate(createRes.Data.Id);
        Assert.Equal(200, docDeleteRes.Code);
        Assert.True(docDeleteRes.Data);
    }

    [Fact]
    public async Task Opd_Doctor_Can_Fetch_Templates_From_Designer_And_Verify_Sections()
    {
        var (connFactory, controller, _) = await CreateTestControllerAsync(
            userId: "USER-DOC-OPD",
            role: RoleCode.DoctorOpd,
            deptId: "DEPT-OPD-01");

        // 1. Save standard elements into emr_data_elements
        await controller.UpsertDataElement(new EmrController.UpsertEmrDataElementRequestDto(
            ElementCode: "CHIEF_COMPLAINT",
            NhcStdCode: "DE04.01.119.00",
            ElementName: "主诉",
            DataType: "STRING",
            UrsaControlType: "u:TextBox"
        ));
        await controller.UpsertDataElement(new EmrController.UpsertEmrDataElementRequestDto(
            ElementCode: "PRESENT_ILLNESS",
            NhcStdCode: "DE02.10.071.00",
            ElementName: "现病史",
            DataType: "TEXT",
            UrsaControlType: "u:TextArea"
        ));
        await controller.UpsertDataElement(new EmrController.UpsertEmrDataElementRequestDto(
            ElementCode: "PRIMARY_DIAGNOSIS",
            NhcStdCode: "DE05.01.024.00",
            ElementName: "初步诊断",
            DataType: "STRING",
            UrsaControlType: "u:TextBox"
        ));

        // 2. Designer creates and publishes a custom OPD template
        var designReq = new EmrController.SaveEmrTemplateDesignRequestDto(
            TemplateCode: "OPD_PED_FEVER",
            TemplateName: "儿科发热专病门诊病历",
            EmrScene: "OPD",
            DocTypeCode: "OPD_PEDIATRIC",
            DeptCategory: "儿科",
            TargetDeptId: "DEPT-OPD-02",
            DiseaseIcdPrefix: "R50",
            TimeLimitHours: 2,
            RequiredSignLevels: 1,
            OfdLayoutId: null,
            IsPublished: 1,
            Sections: new List<EmrController.EmrSectionDesignItemDto>
            {
                new(
                    SectionCode: "SEC_PED_CHIEF",
                    SectionTitle: "患儿发热主诉与现病史",
                    RenderMode: "GRID_FORM",
                    ElementCodes: new List<string> { "CHIEF_COMPLAINT", "PRESENT_ILLNESS" },
                    IsRequired: 1,
                    VisibilityExpr: "ALL",
                    SortOrder: 1
                ),
                new(
                    SectionCode: "SEC_PED_DIAG",
                    SectionTitle: "初步诊断与退热处置",
                    RenderMode: "INLINE_PARAGRAPH",
                    ElementCodes: new List<string> { "PRIMARY_DIAGNOSIS" },
                    IsRequired: 1,
                    VisibilityExpr: "ALL",
                    SortOrder: 2
                )
            }
        );

        var designRes = await controller.SaveTemplateDesign(designReq);
        Assert.Equal(200, designRes.Code);
        Assert.NotNull(designRes.Data);
        Assert.Equal("儿科发热专病门诊病历", designRes.Data.Template.TemplateName);
        Assert.Equal(2, designRes.Data.Sections.Count);

        // 3. Outpatient doctor queries templates from designer catalog
        var templatesRes = await controller.GetTemplates(emrScene: "OPD");
        Assert.Equal(200, templatesRes.Code);
        Assert.NotNull(templatesRes.Data);
        var customTpl = templatesRes.Data.FirstOrDefault(t => t.TemplateCode == "OPD_PED_FEVER");
        Assert.NotNull(customTpl);
        Assert.Equal("儿科发热专病门诊病历", customTpl.TemplateName);
        Assert.Equal("儿科", customTpl.DeptCategory);
        Assert.Equal(2, customTpl.SectionCount);

        // 4. Doctor loads the template detail to dynamically render form
        var detailRes = await controller.GetTemplateDetail(customTpl.Id);
        Assert.Equal(200, detailRes.Code);
        Assert.NotNull(detailRes.Data);
        Assert.Equal(2, detailRes.Data.Sections.Count);
        Assert.Contains(detailRes.Data.Sections, s => s.SectionCode == "SEC_PED_CHIEF" && s.Elements.Count == 2);
        Assert.Contains(detailRes.Data.Sections, s => s.SectionCode == "SEC_PED_DIAG" && s.Elements.Count == 1);
    }

    [Fact]
    public async Task Opd_Doctor_Can_Create_Dept_And_Personal_UserTemplates_With_Predefined_Phrases_And_Isolate_Permissions()
    {
        var (connFactory, docZhangController, _) = await CreateTestControllerAsync(
            userId: "USER-DOC-OPD",
            role: RoleCode.DoctorOpd,
            deptId: "DEPT-OPD-01");

        // 1. Dr. Zhang creates a Department Shared Template for Coronary Artery Disease
        var deptTplReq = new EmrController.SaveEmrUserTemplateRequestDto(
            Id: null,
            BaseTemplateId: "TMPL-OPD-FIRST",
            TemplateCode: "UTPL_OPD_CAD_DEPT",
            TemplateName: "心血管内科-冠心病劳力性心绞痛门诊初诊模板",
            EmrScene: "OPD",
            DocTypeCode: "OPD_FIRST",
            DefaultContentJson: "{\"CHIEF_COMPLAINT\":\"反复发作性心前区闷痛2周，加重2天\",\"PRESENT_ILLNESS\":\"患者2周前于快步行走或劳累时出现心前区闷痛...\",\"PRIMARY_DIAGNOSIS\":\"冠心病\",\"TREATMENT_PLAN\":\"1. 阿司匹林肠溶片 100mg qd\"}",
            IsSharedDept: true,
            SortOrder: 1
        );
        var deptRes = await docZhangController.SaveUserTemplate(deptTplReq);
        Assert.Equal(200, deptRes.Code);
        Assert.NotNull(deptRes.Data);
        Assert.Equal("心血管内科-冠心病劳力性心绞痛门诊初诊模板", deptRes.Data.TemplateName);
        Assert.Equal(1, deptRes.Data.IsSharedDept);

        // 2. Dr. Zhang creates a Personal Template for Acute Gastritis
        var personalTplReq = new EmrController.SaveEmrUserTemplateRequestDto(
            Id: null,
            BaseTemplateId: "TMPL-OPD-FIRST",
            TemplateCode: "UTPL_OPD_GASTRO_PERSONAL",
            TemplateName: "张医师-急性胃肠炎个人初诊模板",
            EmrScene: "OPD",
            DocTypeCode: "OPD_FIRST",
            DefaultContentJson: "{\"CHIEF_COMPLAINT\":\"腹痛伴腹泻1天\",\"PRIMARY_DIAGNOSIS\":\"急性胃肠炎\",\"TREATMENT_PLAN\":\"口服补液盐与蒙脱石散\"}",
            IsSharedDept: false,
            SortOrder: 2
        );
        var personalRes = await docZhangController.SaveUserTemplate(personalTplReq);
        Assert.Equal(200, personalRes.Code);
        Assert.NotNull(personalRes.Data);
        Assert.Equal(0, personalRes.Data.IsSharedDept);

        // 3. Dr. Zhang queries OPD templates -> should see BOTH
        var zhangTemplates = await docZhangController.GetUserTemplates(emrScene: "OPD");
        Assert.Equal(200, zhangTemplates.Code);
        Assert.NotNull(zhangTemplates.Data);
        Assert.Contains(zhangTemplates.Data, t => t.TemplateCode == "UTPL_OPD_CAD_DEPT");
        Assert.Contains(zhangTemplates.Data, t => t.TemplateCode == "UTPL_OPD_GASTRO_PERSONAL");

        // 4. Colleague Dr. Wang in the same OPD department queries -> should see Dept template, but NOT Dr. Zhang's personal template
        var wangSession = new UserSession
        {
            UserId = "USER-DOC-WANG",
            UserName = "王医生",
            DisplayName = "王医生",
            RoleCode = RoleCode.DoctorOpd,
            Roles = new[] { RoleCode.DoctorOpd },
            ActiveDeptId = "DEPT-OPD-01"
        };
        var wangController = new EmrController(
            new DapperRepository<EmrDocument>(connFactory, "emr_documents"),
            new DapperRepository<EmrDocumentElement>(connFactory, "emr_document_elements"),
            new DapperRepository<EmrOfdArchive>(connFactory, "emr_ofd_archives"),
            new EmrQualityControlService(new DapperRepository<EmrDataElement>(connFactory, "emr_data_elements"), new MockPatientContract()),
            new EmrOfdRenderService(new DapperRepository<EmrOfdArchive>(connFactory, "emr_ofd_archives")),
            connFactory: connFactory,
            userSession: wangSession,
            userTemplateRepo: new DapperRepository<EmrUserTemplate>(connFactory, "emr_user_templates")
        );
        var wangTemplates = await wangController.GetUserTemplates(emrScene: "OPD");
        Assert.Equal(200, wangTemplates.Code);
        Assert.NotNull(wangTemplates.Data);
        Assert.Contains(wangTemplates.Data, t => t.TemplateCode == "UTPL_OPD_CAD_DEPT");
        Assert.DoesNotContain(wangTemplates.Data, t => t.TemplateCode == "UTPL_OPD_GASTRO_PERSONAL");

        // 5. Dr. Wang attempts to delete Dr. Zhang's dept template -> Should be 403 Forbidden
        var unauthorizedDelete = await wangController.DeleteUserTemplate(deptRes.Data.Id);
        Assert.Equal(403, unauthorizedDelete.Code);

        // 6. Dr. Zhang can delete own personal template
        var authorizedDelete = await docZhangController.DeleteUserTemplate(personalRes.Data.Id);
        Assert.Equal(200, authorizedDelete.Code);
        Assert.True(authorizedDelete.Data);
    }
}
