using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.BuildingBlocks.Security;
using HIS.Contracts;
using HIS.Modules.Emr.Controllers;
using HIS.Modules.Emr.Models;
using HIS.Modules.Config.Controllers;
using HIS.Modules.Config.Models;
using HIS.Modules.Finance.Controllers;
using HIS.Modules.Finance.Models;
using HIS.Modules.Inpatient.Controllers;
using HIS.Modules.Inpatient.Models;
using HIS.Modules.Inpatient.Services;
using HIS.Modules.Inventory.Controllers;
using HIS.Modules.Inventory.Models;
using HIS.Modules.MedTech.Controllers;
using HIS.Modules.MedTech.Models;
using HIS.Modules.Operations.Services;
using HIS.Modules.Ordering.Controllers;
using HIS.Modules.Ordering.Models;
using HIS.Modules.Outpatient.Controllers;
using HIS.Modules.Outpatient.Models;
using HIS.Modules.Patient.Controllers;
using HIS.Modules.Patient.Models;
using HIS.Modules.Pharmacy.Controllers;
using HIS.Modules.Pharmacy.Models;
using HIS.Modules.Platform.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace HIS.IntegrationTests;

public class EndToEndWorkflowTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly WebApplicationFactory<Program> _factory;

    public EndToEndWorkflowTests(WebApplicationFactory<Program> factory)
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
            sessionVersion: sessionVersion,
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

    private async Task EnsureTestVisitAsync(string visitId, string patientId, string doctorId = "USER-DOC-OPD", string deptId = "DEPT-OPD-01")
    {
        using var scope = _factory.Services.CreateScope();
        var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        using var conn = connFactory.CreateConnection();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO pat_patients (id, medical_record_no, full_name, pinyin_code, gender_code, birth_date_int, id_card_type, id_card_cipher, id_card_hash, phone_masked, allergy_tags_json, is_temp_emergency, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@PatientId, 'MRN-' || substr(@PatientId, 5), '测试患者', 'CSHZ', '1', 19900101, 'ID_CARD', NULL, 'HASH-' || @PatientId, '138****0000', '[]', 0, 1, 1, @Now, 'TEST', @Now, 'TEST', 1)",
            new { PatientId = patientId, Now = now });

        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO opd_visits (id, visit_no, campus_id, opd_dept_id, clinic_room_id, patient_id, schedule_id, doctor_id, is_emergency, is_green_channel, is_return_visit, status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@VisitId, 'VNO-' || substr(@VisitId, 5), 'CAMPUS-01', @DeptId, 'ROOM-OPD-101', @PatientId, NULL, @DoctorId, 0, 0, 0, 2, 1, @Now, @DoctorId, @Now, @DoctorId, 1)",
            new { VisitId = visitId, PatientId = patientId, DeptId = deptId, DoctorId = doctorId, Now = now });
    }

    private async Task EnsureTestEncounterAsync(string encounterId, string patientId, string deptId = "DEPT-IPD-01", string wardId = "DEPT-WARD-01")
    {
        using var scope = _factory.Services.CreateScope();
        var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        using var conn = connFactory.CreateConnection();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO pat_patients (id, medical_record_no, full_name, pinyin_code, gender_code, birth_date_int, id_card_type, id_card_cipher, id_card_hash, phone_masked, allergy_tags_json, is_temp_emergency, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@PatientId, 'MRN-' || substr(@PatientId, 5), '测试患者', 'CSHZ', '1', 19900101, 'ID_CARD', NULL, 'HASH-' || @PatientId, '138****0000', '[]', 0, 1, 1, @Now, 'TEST', @Now, 'TEST', 1)",
            new { PatientId = patientId, Now = now });

        await conn.ExecuteAsync(@"
            INSERT OR REPLACE INTO ipd_encounters (id, admission_no, patient_id, campus_id, admit_dept_id, admit_ward_id, is_borrowed_bed, bed_code, active_bed_token, attending_doctor_id, primary_nurse_id, nursing_level_code, prepayment_balance, status, triage_status, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
            VALUES (@EncounterId, 'ADM-' || substr(@EncounterId, 5), @PatientId, 'CAMPUS-01', @DeptId, @WardId, 0, '01', 'TOKEN-' || @EncounterId, 'USER-DOC-ATTENDING', 'USER-NURSE-01', 'LEVEL_1', 1000.00, 1, 1, 1, @Now, 'TEST', @Now, 'TEST', 1)",
            new { EncounterId = encounterId, PatientId = patientId, DeptId = deptId, WardId = wardId, Now = now });
    }

    [Fact]
    public async Task Prescription_CreateWithSoftDeletedItemsKeepsRowsAndExcludesTheirTotal()
    {
        var visitId = "VIS-RX-CREATE-" + Guid.NewGuid().ToString("N")[..8];
        var deletedItemId = Guid.NewGuid().ToString("D");
        await EnsureTestVisitAsync(visitId, "PAT-RX-TEST");

        var createRes = await _client.PostAsJsonAsync("/api/v4/opd/prescriptions",
            new OutpatientController.CreatePrescriptionRequestDto(
                VisitId: visitId,
                PatientId: "PAT-RX-TEST",
                OpdDeptId: "DEPT-OPD-01",
                DoctorId: "USER-DOC-OPD",
                TargetPharmacyId: "DEPT-PHM-01",
                Items: new List<OpdPrescriptionItem>
                {
                    new()
                    {
                        Id = deletedItemId, GroupNo = 1, DrugId = "DRUG-LOCAL",
                        DrugNameSnapshot = "已撤回药品", DoseOnce = 1m, DoseUnit = "片",
                        FrequencyCode = "QD", RouteCode = "PO", Days = 3,
                        TotalQuantity = 2, UnitPrice = 99.99m, IsDeleted = 1
                    },
                    new()
                    {
                        GroupNo = 2, DrugId = "DRUG-CARD-006",
                        DrugNameSnapshot = "阿司匹林肠溶片", DoseOnce = 100m, DoseUnit = "mg",
                        FrequencyCode = "QN", RouteCode = "PO", Days = 7,
                        TotalQuantity = 1, UnitPrice = 22.50m
                    }
                }));
        Assert.True(createRes.IsSuccessStatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<ApiResponse<OpdPrescription>>();
        Assert.Equal(200, created?.Code);
        Assert.Equal(22.50m, created?.Data?.TotalAmount);

        using var scope = _factory.Services.CreateScope();
        var itemRepo = scope.ServiceProvider.GetRequiredService<IRepository<OpdPrescriptionItem>>();
        var persisted = await itemRepo.GetByIdAsync(deletedItemId);
        Assert.NotNull(persisted);
        Assert.Equal((short)1, persisted!.IsDeleted);
        Assert.NotNull(persisted.DeletedAtMs);
        Assert.Equal("USER-DOC-OPD", persisted.DeletedBy);
    }

    [Fact]
    public async Task Prescription_SoftDeleteKeepsOriginalRowAndMergesExtFields()
    {
        var visitId = "VIS-RX-" + Guid.NewGuid().ToString("N")[..8];
        var firstItemId = Guid.NewGuid().ToString("D");
        var secondItemId = Guid.NewGuid().ToString("D");
        await EnsureTestVisitAsync(visitId, "PAT-RX-TEST");

        var createRes = await _client.PostAsJsonAsync("/api/v4/opd/prescriptions",
            new OutpatientController.CreatePrescriptionRequestDto(
                VisitId: visitId,
                PatientId: "PAT-RX-TEST",
                OpdDeptId: "DEPT-OPD-01",
                DoctorId: "USER-DOC-OPD",
                TargetPharmacyId: "DEPT-PHM-01",
                Items: new List<OpdPrescriptionItem>
                {
                    new()
                    {
                        Id = firstItemId, GroupNo = 1, DrugId = "DRUG-CARD-006",
                        DrugNameSnapshot = "阿司匹林肠溶片", DoseOnce = 100m, DoseUnit = "mg",
                        FrequencyCode = "QN", RouteCode = "PO", Days = 7, TotalQuantity = 1, UnitPrice = 22.50m,
                        ExtDataJson = "{\"spec\":\"100mg*30片/盒\",\"amountText\":\"¥22.50\"}"
                    },
                    new()
                    {
                        Id = secondItemId, GroupNo = 2, DrugId = "DRUG-CARD-005",
                        DrugNameSnapshot = "硫酸氢氯吡格雷片", DoseOnce = 75m, DoseUnit = "mg",
                        FrequencyCode = "QD", RouteCode = "PO", Days = 7, TotalQuantity = 2, UnitPrice = 56.80m
                    }
                }));
        Assert.True(createRes.IsSuccessStatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<ApiResponse<OpdPrescription>>();
        Assert.Equal(200, created?.Code);
        var rxId = created!.Data!.Id;
        Assert.Equal(136.10m, created.Data.TotalAmount);

        var activeRes = await _client.GetAsync($"/api/v4/opd/prescriptions/{rxId}/items");
        var active = await activeRes.Content.ReadFromJsonAsync<ApiResponse<List<OpdPrescriptionItem>>>();
        Assert.Equal(2, active?.Data?.Count);

        var deleteRes = await _client.PostAsJsonAsync(
            $"/api/v4/opd/prescriptions/items/{firstItemId}/soft-delete",
            new OutpatientController.SoftDeleteRequestDto("USER-DOC-OPD", "开立错误撤回"));
        Assert.True(deleteRes.IsSuccessStatusCode);
        var deleted = await deleteRes.Content.ReadFromJsonAsync<ApiResponse<OpdPrescriptionItem>>();
        Assert.Equal((short)1, deleted?.Data?.IsDeleted);
        Assert.NotNull(deleted?.Data?.DeletedAtMs);
        Assert.Equal("USER-DOC-OPD", deleted?.Data?.DeletedBy);
        using (var extDoc = System.Text.Json.JsonDocument.Parse(deleted!.Data!.ExtDataJson!))
        {
            Assert.Equal("开立错误撤回", extDoc.RootElement.GetProperty("deleteReason").GetString());
        }

        var visibleAfterDelete = await _client.GetAsync($"/api/v4/opd/prescriptions/{rxId}/items");
        var visibleItems = await visibleAfterDelete.Content.ReadFromJsonAsync<ApiResponse<List<OpdPrescriptionItem>>>();
        Assert.Single(visibleItems!.Data!);

        var allAfterDelete = await _client.GetAsync($"/api/v4/opd/prescriptions/{rxId}/items?includeDeleted=true");
        var allItems = await allAfterDelete.Content.ReadFromJsonAsync<ApiResponse<List<OpdPrescriptionItem>>>();
        Assert.Equal(2, allItems?.Data?.Count);

        // 直接查库确认物理行仍在，且软删除标记与审计字段齐全。
        using var scope = _factory.Services.CreateScope();
        var itemRepo = scope.ServiceProvider.GetRequiredService<IRepository<OpdPrescriptionItem>>();
        var persisted = await itemRepo.GetByIdAsync(firstItemId);
        Assert.NotNull(persisted);
        Assert.Equal((short)1, persisted!.IsDeleted);
        Assert.NotNull(persisted.DeletedAtMs);
        Assert.Equal("USER-DOC-OPD", persisted.DeletedBy);

        var updateRes = await _client.PutAsJsonAsync($"/api/v4/opd/prescriptions/items/{secondItemId}",
            new OutpatientController.UpdatePrescriptionItemRequestDto(
                DrugNameSnapshot: "硫酸氢氯吡格雷片", DoseOnce: 75m, DoseUnit: "mg",
                FrequencyCode: "BID", RouteCode: "PO", Days: 14, TotalQuantity: 4, UnitPrice: 56.80m,
                OperatorId: "USER-DOC-OPD", CustomFieldsJson: "{\"spec\":\"75mg*7片/盒\",\"amountText\":\"¥227.20\"}"));
        Assert.True(updateRes.IsSuccessStatusCode);
        var updated = await updateRes.Content.ReadFromJsonAsync<ApiResponse<OpdPrescriptionItem>>();
        Assert.Equal("BID", updated?.Data?.FrequencyCode);
        Assert.Equal(14, updated?.Data?.Days);
        Assert.Equal(4, updated?.Data?.TotalQuantity);

        var firstSave = await _client.PutAsJsonAsync($"/api/v4/opd/prescriptions/{rxId}/ext-fields",
            new OutpatientController.SavePrescriptionExtFieldsRequestDto(
                new List<OutpatientController.PrescriptionCustomFieldDto>
                {
                    new("FIELD-NOTE", "用药备注", "餐后服用", "STRING", "补充用药说明", false, 0),
                    new("FIELD-DRIP", "滴速说明", "40滴/分", "STRING", "静脉滴注速度", false, 1)
                },
                "USER-DOC-OPD"));
        Assert.True(firstSave.IsSuccessStatusCode);

        // 第二次保存只带其中一个字段：缺失节点按软删除保留，不能从 JSON 中消失。
        var secondSave = await _client.PutAsJsonAsync($"/api/v4/opd/prescriptions/{rxId}/ext-fields",
            new OutpatientController.SavePrescriptionExtFieldsRequestDto(
                new List<OutpatientController.PrescriptionCustomFieldDto>
                {
                    new("FIELD-NOTE", "用药备注", "睡前服用", "STRING", "补充用药说明", false, 0)
                },
                "USER-DOC-OPD"));
        Assert.True(secondSave.IsSuccessStatusCode);
        var savedFields = await secondSave.Content.ReadFromJsonAsync<ApiResponse<List<OutpatientController.PrescriptionCustomFieldDto>>>();
        Assert.Equal(2, savedFields?.Data?.Count);
        Assert.Contains(savedFields!.Data!, x => x.Id == "FIELD-NOTE" && x.Value == "睡前服用" && !x.IsDeleted);
        Assert.Contains(savedFields.Data!, x => x.Id == "FIELD-DRIP" && x.IsDeleted);

        var byVisitRes = await _client.GetAsync($"/api/v4/opd/prescriptions/by-visit/{visitId}");
        Assert.True(byVisitRes.IsSuccessStatusCode);
        var byVisit = await byVisitRes.Content.ReadFromJsonAsync<ApiResponse<OutpatientController.PrescriptionAggregateDto>>();
        Assert.Equal(rxId, byVisit?.Data?.Prescription.Id);
        Assert.Equal(2, byVisit?.Data?.Items?.Count);
        Assert.Equal(2, byVisit?.Data?.CustomFields?.Count);
        var persistedUpdatedItem = Assert.Single(byVisit!.Data!.Items!, x => x.Id == secondItemId);
        Assert.Equal("BID", persistedUpdatedItem.FrequencyCode);
        Assert.Equal(4, persistedUpdatedItem.TotalQuantity);
        Assert.Equal(56.80m * 4, byVisit.Data.Prescription.TotalAmount);
    }

    [Fact]
    public async Task Auth_LoginRequiresAndReturnsSelectedProfessionalContext()
    {
        var contextsRes = await _client.GetAsync("/api/v4/platform/auth/contexts?userName=doc_opd");
        Assert.Equal(HttpStatusCode.OK, contextsRes.StatusCode);
        var contexts = await contextsRes.Content.ReadFromJsonAsync<ApiResponse<List<PlatformController.LoginContextDto>>>();

        Assert.True(contexts?.Data?.Count >= 2);
        Assert.Contains(contexts!.Data!, x =>
            x.PostId == "UDP-DOC-OPD-OPD" && x.RoleCode == "OPD_DOC");
        Assert.Contains(contexts.Data!, x =>
            x.PostId == "UDP-DOC-OPD-IPD" && x.RoleCode == "IPD_ATTENDING");
        Assert.Equal("UDP-DOC-OPD-OPD", contexts?.Data?.Single(x => x.IsDefault).PostId);
        Assert.Contains(contexts!.Data!, x => x.PostId == "UDP-DOC-OPD-IPD" && x.DeptCategory == "IPD");

        var loginRes = await _client.PostAsJsonAsync("/api/v4/platform/auth/login", new PlatformController.LoginRequestDto(
            UserName: "doc_opd", Password: "123", ContextId: "UDP-DOC-OPD-IPD"));
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);
        var login = await loginRes.Content.ReadFromJsonAsync<ApiResponse<PlatformController.LoginResponseDto>>();

        Assert.Equal("UDP-DOC-OPD-IPD", login?.Data?.PostId);
        Assert.Equal("ROLE-IPD-ATTENDING", login?.Data?.RoleId);
        Assert.Equal("IPD", login?.Data?.ActiveDeptCategory);
        Assert.Equal("心血管内科住院一科", login?.Data?.ActiveDeptName);
        Assert.True(login?.Data?.ComponentPermissions.ContainsKey("EMR.Toolbar.BtnExportOfd"));
        Assert.False(login?.Data?.ComponentPermissions.ContainsKey("OPD.ConsultView.BtnSignPrescription"));

        var claims = new JwtSecurityTokenHandler().ReadJwtToken(login!.Data!.AccessToken).Claims.ToDictionary(x => x.Type, x => x.Value);
        Assert.Equal("UDP-DOC-OPD-IPD", claims["active_post_id"]);
        Assert.Equal("ROLE-IPD-ATTENDING", claims["role_id"]);
        Assert.Equal("IPD_ATTENDING", claims["role_code"]);
        Assert.Equal("心血管内科住院一科", claims["dept_name"]);
    }

    [Fact]
    public async Task Auth_RejectsUnownedOrMissingProfessionalContext()
    {
        var wrongContextLogin = await _client.PostAsJsonAsync("/api/v4/platform/auth/login", new PlatformController.LoginRequestDto(
            UserName: "doc_opd", Password: "123", ContextId: "UDP-DOC-ATT-IPD1"));
        Assert.Equal(HttpStatusCode.Forbidden, wrongContextLogin.StatusCode);

        var noContextLogin = await _client.PostAsJsonAsync("/api/v4/platform/auth/login", new PlatformController.LoginRequestDto(
            UserName: "no_context", Password: "123", ContextId: null));
        Assert.Equal(HttpStatusCode.Forbidden, noContextLogin.StatusCode);
    }

    [Fact]
    public async Task Auth_SwitchContextRequiresBearerAndOnlyAllowsOwnContext()
    {
        var anonClient = _factory.CreateClient();
        var missingBearer = await anonClient.PostAsJsonAsync("/api/v4/platform/auth/switch-context", new { ContextId = "UDP-DOC-OPD-IPD" });
        Assert.Equal(HttpStatusCode.Unauthorized, missingBearer.StatusCode);

        var userClient = _factory.CreateClient();
        var loginRes = await userClient.PostAsJsonAsync("/api/v4/platform/auth/login", new PlatformController.LoginRequestDto(
            UserName: "doc_opd", Password: "123", ContextId: "UDP-DOC-OPD-OPD"));
        var login = await loginRes.Content.ReadFromJsonAsync<ApiResponse<PlatformController.LoginResponseDto>>();
        userClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Data!.AccessToken);

        var wrongContext = await userClient.PostAsJsonAsync("/api/v4/platform/auth/switch-context", new { ContextId = "UDP-DOC-ATT-IPD1" });
        Assert.Equal(HttpStatusCode.Forbidden, wrongContext.StatusCode);

        var switched = await userClient.PostAsJsonAsync("/api/v4/platform/auth/switch-context", new { ContextId = "UDP-DOC-OPD-IPD" });
        Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
        var switchedData = await switched.Content.ReadFromJsonAsync<ApiResponse<PlatformController.LoginResponseDto>>();
        Assert.Equal("UDP-DOC-OPD-IPD", switchedData?.Data?.PostId);
        Assert.Equal("IPD_ATTENDING", switchedData?.Data?.RoleCode);
        Assert.Equal("心血管内科住院一科", switchedData?.Data?.ActiveDeptName);

        SetAdminAuth(_factory, _client);
    }

    [Fact]
    public async Task PrescriptionFieldSchema_ConfigurableFieldPersistsAndDeleteIsSoft()
    {
        const string formCode = "FORM-PRESCRIPTION-ITEM-FIELDS";
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var fieldCode = $"RX_CUSTOM_{suffix}";
        var fieldId = Guid.NewGuid().ToString("D");

        var initialRes = await _client.GetAsync($"/api/v4/config/schemas/{formCode}?includeDraft=true");
        Assert.Equal(HttpStatusCode.OK, initialRes.StatusCode);
        var initial = await initialRes.Content.ReadFromJsonAsync<ApiResponse<ConfigController.FormSchemaDetailDto>>();
        Assert.Equal(200, initial?.Code);
        var schema = initial!.Data!.Schema;
        var originalFields = initial.Data.Fields.ToList();
        Assert.Contains(originalFields, x => x.FieldCode == "SPEC");
        Assert.Contains(originalFields, x => x.FieldCode == "DOSE");

        static object ToSaveField(CfgFieldDefinition field) => new
        {
            field.Id,
            field.FormSchemaId,
            field.FieldCode,
            field.FieldLabel,
            field.DataType,
            field.UrsaControlType,
            field.ControlKey,
            field.DictCode,
            field.UnitText,
            field.ValidationRuleJson,
            field.VisibilityExpr,
            field.IsIndexed,
            field.SortOrder
        };

        var request = new
        {
            Schema = new
            {
                schema.Id,
                schema.FormCode,
                schema.FormName,
                schema.EntityType,
                schema.DeptCategory,
                schema.TargetDeptId,
                schema.GridColumns,
                schema.IsPublished,
                schema.ExtDataJson
            },
            Fields = originalFields
                .Select(ToSaveField)
                .Append(new
                {
                    Id = fieldId,
                    FormSchemaId = schema.Id,
                    FieldCode = fieldCode,
                    FieldLabel = "自定义用药条件",
                    DataType = "INTEGER",
                    UrsaControlType = "u:NumericIntUpDown",
                    ControlKey = "PRESCRIPTION.Dynamic.CustomMedicationCondition",
                    DictCode = (string?)null,
                    UnitText = "次",
                    ValidationRuleJson = "{\"required\":true,\"min\":1,\"max\":20,\"step\":1,\"width\":137,\"system\":false,\"editable\":true,\"placeholder\":\"请输入次数\"}",
                    VisibilityExpr = "ALL",
                    IsIndexed = (short)0,
                    SortOrder = 99
                })
                .ToArray(),
            OperatorId = "USER-DOC-OPD"
        };

        var saveRes = await _client.PostAsJsonAsync("/api/v4/config/schemas", request);
        Assert.Equal(HttpStatusCode.OK, saveRes.StatusCode);
        var saved = await saveRes.Content.ReadFromJsonAsync<ApiResponse<ConfigController.FormSchemaDetailDto>>();
        Assert.Equal(200, saved?.Code);
        var savedField = Assert.Single(saved!.Data!.Fields, x => x.FieldCode == fieldCode);
        Assert.Equal(fieldId, savedField.Id);
        Assert.Equal("自定义用药条件", savedField.FieldLabel);
        Assert.Equal("INTEGER", savedField.DataType);
        Assert.Equal("u:NumericIntUpDown", savedField.UrsaControlType);
        Assert.Equal("次", savedField.UnitText);
        Assert.Contains("\"width\":137", savedField.ValidationRuleJson);

        var reloadedRes = await _client.GetAsync($"/api/v4/config/schemas/{formCode}?includeDraft=true");
        var reloaded = await reloadedRes.Content.ReadFromJsonAsync<ApiResponse<ConfigController.FormSchemaDetailDto>>();
        var reloadedField = Assert.Single(reloaded!.Data!.Fields, x => x.FieldCode == fieldCode);
        Assert.Equal("自定义用药条件", reloadedField.FieldLabel);
        Assert.Equal("u:NumericIntUpDown", reloadedField.UrsaControlType);

        var removeRequest = new
        {
            Schema = new
            {
                schema.Id,
                schema.FormCode,
                schema.FormName,
                schema.EntityType,
                schema.DeptCategory,
                schema.TargetDeptId,
                schema.GridColumns,
                schema.IsPublished,
                schema.ExtDataJson
            },
            Fields = originalFields.Select(ToSaveField).ToArray(),
            OperatorId = "USER-DOC-OPD"
        };
        var removeRes = await _client.PostAsJsonAsync("/api/v4/config/schemas", removeRequest);
        Assert.Equal(HttpStatusCode.OK, removeRes.StatusCode);
        var removed = await removeRes.Content.ReadFromJsonAsync<ApiResponse<ConfigController.FormSchemaDetailDto>>();
        Assert.DoesNotContain(removed!.Data!.Fields, x => x.FieldCode == fieldCode);

        using var scope = _factory.Services.CreateScope();
        var fieldRepo = scope.ServiceProvider.GetRequiredService<IRepository<CfgFieldDefinition>>();
        var persisted = await fieldRepo.GetByIdAsync(fieldId);
        Assert.NotNull(persisted);
        Assert.Equal(fieldCode, persisted!.FieldCode);
        Assert.Equal((short)1, persisted.IsDeleted);
        Assert.NotNull(persisted.DeletedAtMs);
        Assert.Equal("USER-DOC-OPD", persisted.DeletedBy);
    }

    [Fact]
    public async Task PrescriptionFieldSchema_HiddenFieldKeepsHistoricalItemValues()
    {
        const string formCode = "FORM-PRESCRIPTION-ITEM-FIELDS";
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var fieldCode = $"RX_HISTORY_{suffix}";
        var fieldId = Guid.NewGuid().ToString("D");
        var visitId = "VIS-RX-HISTORY-" + suffix;
        var itemId = Guid.NewGuid().ToString("D");
        await EnsureTestVisitAsync(visitId, "PAT-RX-TEST");

        var initialRes = await _client.GetAsync($"/api/v4/config/schemas/{formCode}?includeDraft=true");
        Assert.Equal(HttpStatusCode.OK, initialRes.StatusCode);
        var initial = await initialRes.Content.ReadFromJsonAsync<ApiResponse<ConfigController.FormSchemaDetailDto>>();
        Assert.Equal(200, initial?.Code);
        var schema = initial!.Data!.Schema;
        var originalFields = initial.Data.Fields.ToList();

        static object ToSaveField(CfgFieldDefinition field) => new
        {
            field.Id,
            field.FormSchemaId,
            field.FieldCode,
            field.FieldLabel,
            field.DataType,
            field.UrsaControlType,
            field.ControlKey,
            field.DictCode,
            field.UnitText,
            field.ValidationRuleJson,
            field.VisibilityExpr,
            field.IsIndexed,
            field.SortOrder
        };

        var schemaData = new
        {
            schema.Id,
            schema.FormCode,
            schema.FormName,
            schema.EntityType,
            schema.DeptCategory,
            schema.TargetDeptId,
            schema.GridColumns,
            schema.IsPublished,
            schema.ExtDataJson
        };

        var customField = new
        {
            Id = fieldId,
            FormSchemaId = schema.Id,
            FieldCode = fieldCode,
            FieldLabel = "用药后观察",
            DataType = "INTEGER",
            UrsaControlType = "u:NumericIntUpDown",
            ControlKey = "PRESCRIPTION.Dynamic.Observation",
            DictCode = (string?)null,
            UnitText = "小时",
            ValidationRuleJson = "{\"required\":false,\"min\":0,\"max\":24,\"step\":1,\"width\":96,\"system\":false,\"editable\":true}",
            VisibilityExpr = "ALL",
            IsIndexed = (short)0,
            SortOrder = 99
        };

        try
        {
            var addFieldRes = await _client.PostAsJsonAsync("/api/v4/config/schemas", new
            {
                Schema = schemaData,
                Fields = originalFields
                    .Select(ToSaveField)
                    .Cast<object>()
                    .Append(customField)
                    .ToArray(),
                OperatorId = "USER-DOC-OPD"
            });
            Assert.Equal(HttpStatusCode.OK, addFieldRes.StatusCode);

            var createRes = await _client.PostAsJsonAsync("/api/v4/opd/prescriptions",
                new OutpatientController.CreatePrescriptionRequestDto(
                    VisitId: visitId,
                    PatientId: "PAT-RX-TEST",
                    OpdDeptId: "DEPT-OPD-01",
                    DoctorId: "USER-DOC-OPD",
                    TargetPharmacyId: "DEPT-PHM-01",
                    Items: new List<OpdPrescriptionItem>
                    {
                        new()
                        {
                            Id = itemId,
                            GroupNo = 1,
                            DrugId = "DRUG-CARD-006",
                            DrugNameSnapshot = "阿司匹林肠溶片",
                            DoseOnce = 100m,
                            DoseUnit = "mg",
                            FrequencyCode = "QN",
                            RouteCode = "PO",
                            Days = 7,
                            TotalQuantity = 1,
                            UnitPrice = 22.50m,
                            ExtDataJson = $"{{\"fields\":{{\"{fieldCode}\":\"7\"}}}}"
                        }
                    }));
            Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);

            var beforeHideRes = await _client.GetAsync(
                $"/api/v4/opd/prescriptions/by-visit/{visitId}?includeDeleted=true");
            var beforeHide = await beforeHideRes.Content.ReadFromJsonAsync<ApiResponse<OutpatientController.PrescriptionAggregateDto>>();
            var beforeHideItem = Assert.Single(beforeHide!.Data!.Items!, x => x.Id == itemId);
            using (var beforeDoc = System.Text.Json.JsonDocument.Parse(beforeHideItem.ExtDataJson!))
            {
                Assert.Equal("7", beforeDoc.RootElement
                    .GetProperty("fields")
                    .GetProperty(fieldCode)
                    .GetString());
            }

            var hideRes = await _client.PostAsJsonAsync("/api/v4/config/schemas", new
            {
                Schema = schemaData,
                Fields = originalFields.Select(ToSaveField).ToArray(),
                OperatorId = "USER-DOC-OPD"
            });
            Assert.Equal(HttpStatusCode.OK, hideRes.StatusCode);
            var hidden = await hideRes.Content.ReadFromJsonAsync<ApiResponse<ConfigController.FormSchemaDetailDto>>();
            Assert.DoesNotContain(hidden!.Data!.Fields, x => x.FieldCode == fieldCode);

            using (var scope = _factory.Services.CreateScope())
            {
                var fieldRepo = scope.ServiceProvider.GetRequiredService<IRepository<CfgFieldDefinition>>();
                var persistedField = await fieldRepo.GetByIdAsync(fieldId);
                Assert.NotNull(persistedField);
                Assert.Equal((short)1, persistedField!.IsDeleted);
                Assert.NotNull(persistedField.DeletedAtMs);
                Assert.Equal("USER-DOC-OPD", persistedField.DeletedBy);
            }

            var afterHideRes = await _client.GetAsync(
                $"/api/v4/opd/prescriptions/by-visit/{visitId}?includeDeleted=true");
            var afterHide = await afterHideRes.Content.ReadFromJsonAsync<ApiResponse<OutpatientController.PrescriptionAggregateDto>>();
            var afterHideItem = Assert.Single(afterHide!.Data!.Items!, x => x.Id == itemId);
            using (var afterDoc = System.Text.Json.JsonDocument.Parse(afterHideItem.ExtDataJson!))
            {
                Assert.Equal("7", afterDoc.RootElement
                    .GetProperty("fields")
                    .GetProperty(fieldCode)
                    .GetString());
            }
        }
        finally
        {
            var restoreRes = await _client.PostAsJsonAsync("/api/v4/config/schemas", new
            {
                Schema = schemaData,
                Fields = originalFields.Select(ToSaveField).ToArray(),
                OperatorId = "USER-DOC-OPD"
            });
            Assert.Equal(HttpStatusCode.OK, restoreRes.StatusCode);
        }
    }

    [Fact]
    public async Task PrescriptionFieldSchema_RenameAndReuseCodeKeepsDistinctRows()
    {
        const string formCode = "FORM-PRESCRIPTION-ITEM-FIELDS";
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var renamedCode = $"SPEC_ARCHIVED_{suffix}";
        var newSpecId = Guid.NewGuid().ToString("D");

        var initialRes = await _client.GetAsync($"/api/v4/config/schemas/{formCode}?includeDraft=true");
        Assert.Equal(HttpStatusCode.OK, initialRes.StatusCode);
        var initial = await initialRes.Content.ReadFromJsonAsync<ApiResponse<ConfigController.FormSchemaDetailDto>>();
        var schema = initial!.Data!.Schema;
        var originalFields = initial.Data.Fields.ToList();
        var originalSpec = Assert.Single(originalFields, x => x.FieldCode == "SPEC");

        static object ToSaveField(CfgFieldDefinition field) => new
        {
            field.Id,
            field.FormSchemaId,
            field.FieldCode,
            field.FieldLabel,
            field.DataType,
            field.UrsaControlType,
            field.ControlKey,
            field.DictCode,
            field.UnitText,
            field.ValidationRuleJson,
            field.VisibilityExpr,
            field.IsIndexed,
            field.SortOrder
        };

        var schemaData = new
        {
            schema.Id,
            schema.FormCode,
            schema.FormName,
            schema.EntityType,
            schema.DeptCategory,
            schema.TargetDeptId,
            schema.GridColumns,
            schema.IsPublished,
            schema.ExtDataJson
        };

        var renamedSpec = new
        {
            Id = originalSpec.Id,
            FormSchemaId = schema.Id,
            FieldCode = renamedCode,
            FieldLabel = "规格（历史）",
            DataType = "STRING",
            UrsaControlType = "u:TextBox",
            ControlKey = "PRESCRIPTION.Dynamic.LegacySpec",
            DictCode = (string?)null,
            UnitText = (string?)null,
            ValidationRuleJson = "{\"required\":false,\"width\":118,\"system\":false,\"editable\":true}",
            VisibilityExpr = "ALL",
            IsIndexed = (short)0,
            SortOrder = 30
        };
        var newSpec = new
        {
            Id = newSpecId,
            FormSchemaId = schema.Id,
            FieldCode = "SPEC",
            FieldLabel = "包装规格",
            DataType = "STRING",
            UrsaControlType = "u:TextBox",
            ControlKey = "PRESCRIPTION.Dynamic.PackageSpec",
            DictCode = (string?)null,
            UnitText = (string?)null,
            ValidationRuleJson = "{\"required\":false,\"width\":108,\"system\":false,\"editable\":true}",
            VisibilityExpr = "ALL",
            IsIndexed = (short)0,
            SortOrder = 31
        };
        var requestFields = originalFields
            .Where(x => x.FieldCode != "SPEC")
            .Select(ToSaveField)
            .Cast<object>()
            .Concat(new object[] { renamedSpec, newSpec })
            .ToArray();

        try
        {
            var saveRes = await _client.PostAsJsonAsync("/api/v4/config/schemas", new
            {
                Schema = schemaData,
                Fields = requestFields,
                OperatorId = "USER-DOC-OPD"
            });
            Assert.Equal(HttpStatusCode.OK, saveRes.StatusCode);
            var saved = await saveRes.Content.ReadFromJsonAsync<ApiResponse<ConfigController.FormSchemaDetailDto>>();
            Assert.Equal(200, saved?.Code);

            var savedRenamed = Assert.Single(saved!.Data!.Fields, x => x.FieldCode == renamedCode);
            var savedSpec = Assert.Single(saved.Data.Fields, x => x.FieldCode == "SPEC");
            Assert.Equal(originalSpec.Id, savedRenamed.Id);
            Assert.Equal("规格（历史）", savedRenamed.FieldLabel);
            Assert.NotEqual(originalSpec.Id, savedSpec.Id);
            Assert.Equal("包装规格", savedSpec.FieldLabel);

            var reloadedRes = await _client.GetAsync($"/api/v4/config/schemas/{formCode}?includeDraft=true");
            var reloaded = await reloadedRes.Content.ReadFromJsonAsync<ApiResponse<ConfigController.FormSchemaDetailDto>>();
            Assert.Equal(originalSpec.Id,
                Assert.Single(reloaded!.Data!.Fields, x => x.FieldCode == renamedCode).Id);
            Assert.Equal(savedSpec.Id,
                Assert.Single(reloaded.Data.Fields, x => x.FieldCode == "SPEC").Id);

            using var scope = _factory.Services.CreateScope();
            var fieldRepo = scope.ServiceProvider.GetRequiredService<IRepository<CfgFieldDefinition>>();
            var persistedRenamed = await fieldRepo.GetByIdAsync(originalSpec.Id);
            var persistedSpec = await fieldRepo.GetByIdAsync(savedSpec.Id);
            Assert.NotNull(persistedRenamed);
            Assert.NotNull(persistedSpec);
            Assert.Equal(renamedCode, persistedRenamed!.FieldCode);
            Assert.Equal((short)0, persistedRenamed.IsDeleted);
            Assert.Equal("SPEC", persistedSpec!.FieldCode);
            Assert.Equal((short)0, persistedSpec.IsDeleted);

            var activeFields = await fieldRepo.QueryAsync(
                "SELECT * FROM cfg_field_definitions WHERE form_schema_id = @SchemaId AND is_deleted = 0",
                new { SchemaId = schema.Id });
            Assert.Single(activeFields, x => x.FieldCode == "SPEC");
            Assert.Single(activeFields, x => x.FieldCode == renamedCode);
        }
        finally
        {
            var restoreRes = await _client.PostAsJsonAsync("/api/v4/config/schemas", new
            {
                Schema = schemaData,
                Fields = originalFields.Select(ToSaveField).ToArray(),
                OperatorId = "USER-DOC-OPD"
            });
            Assert.Equal(HttpStatusCode.OK, restoreRes.StatusCode);
        }
    }

    [Fact]
    public async Task PrescriptionFieldSchema_RejectsHidingDrugName()
    {
        const string formCode = "FORM-PRESCRIPTION-ITEM-FIELDS";

        var initialRes = await _client.GetAsync($"/api/v4/config/schemas/{formCode}?includeDraft=true");
        Assert.Equal(HttpStatusCode.OK, initialRes.StatusCode);
        var initial = await initialRes.Content.ReadFromJsonAsync<ApiResponse<ConfigController.FormSchemaDetailDto>>();
        Assert.Equal(200, initial?.Code);
        var schema = initial!.Data!.Schema;
        var originalFields = initial.Data.Fields.ToList();
        Assert.Contains(originalFields, x => x.FieldCode == "DRUG_NAME");

        static object ToSaveField(CfgFieldDefinition field) => new
        {
            field.Id,
            field.FormSchemaId,
            field.FieldCode,
            field.FieldLabel,
            field.DataType,
            field.UrsaControlType,
            field.ControlKey,
            field.DictCode,
            field.UnitText,
            field.ValidationRuleJson,
            field.VisibilityExpr,
            field.IsIndexed,
            field.SortOrder
        };

        var saveRes = await _client.PostAsJsonAsync("/api/v4/config/schemas", new
        {
            Schema = new
            {
                schema.Id,
                schema.FormCode,
                schema.FormName,
                schema.EntityType,
                schema.DeptCategory,
                schema.TargetDeptId,
                schema.GridColumns,
                schema.IsPublished,
                schema.ExtDataJson
            },
            Fields = originalFields
                .Where(x => x.FieldCode != "DRUG_NAME")
                .Select(ToSaveField)
                .ToArray(),
            OperatorId = "USER-DOC-OPD"
        });

        Assert.Equal(HttpStatusCode.OK, saveRes.StatusCode);
        var rejected = await saveRes.Content.ReadFromJsonAsync<ApiResponse<ConfigController.FormSchemaDetailDto>>();
        Assert.Equal(400, rejected?.Code);
        Assert.Contains("药品名称", rejected?.Message);

        var reloadedRes = await _client.GetAsync($"/api/v4/config/schemas/{formCode}?includeDraft=true");
        var reloaded = await reloadedRes.Content.ReadFromJsonAsync<ApiResponse<ConfigController.FormSchemaDetailDto>>();
        Assert.Equal(200, reloaded?.Code);
        Assert.Contains(reloaded!.Data!.Fields, x => x.FieldCode == "DRUG_NAME");

        using var scope = _factory.Services.CreateScope();
        var fieldRepo = scope.ServiceProvider.GetRequiredService<IRepository<CfgFieldDefinition>>();
        var activeFields = (await fieldRepo.GetAllAsync())
            .Where(x => x.FormSchemaId == schema.Id && x.IsDeleted == 0)
            .ToList();
        Assert.Contains(activeFields, x => x.FieldCode == "DRUG_NAME");
    }

    [Fact]
    public async Task EndToEnd_FullHospitalBusinessWorkflow_Test()
    {
        // 1. Auth Login as Doctor
        var loginRes = await _client.PostAsJsonAsync("/api/v4/platform/auth/login", new PlatformController.LoginRequestDto(
            UserName: "doc_opd",
            Password: "123",
            PreferredDeptId: "DEPT-OPD-01"
        ));
        Assert.True(loginRes.IsSuccessStatusCode);
        var loginData = await loginRes.Content.ReadFromJsonAsync<ApiResponse<PlatformController.LoginResponseDto>>();
        Assert.NotNull(loginData?.Data?.AccessToken);
        Assert.Equal("OPD", loginData.Data.ActiveDeptCategory);

        // 2. Real-name Patient Registration (WF-PAT-01)
        var regRes = await _client.PostAsJsonAsync("/api/v4/patients/register", new PatientController.RegisterPatientRequestDto(
            FullName: "周建强",
            GenderCode: "1", // Male
            BirthDateInt: 19850618,
            IdCardNo: "110101198506182357",
            Phone: "13912345678",
            AllergyTags: new List<string> { "青霉素过敏史" }
        ));
        Assert.True(regRes.IsSuccessStatusCode);
        var regData = await regRes.Content.ReadFromJsonAsync<ApiResponse<PatPatient>>();
        Assert.NotNull(regData?.Data?.MedicalRecordNo);
        var patientId = regData.Data.Id;

        // 3. EMR Structured Record & Quality Control (WF-OPD-04, WF-EMR-02)
        // Trying to save female-only element for male patient -> should be blocked!
        var maleFailRes = await _client.PostAsJsonAsync("/api/v4/emr/documents", new EmrController.SaveEmrRequestDto(
            DocNo: "EMR-TEST-FAIL-" + Guid.NewGuid().ToString("N")[..6],
            EmrScene: "OPD",
            VisitOrEncounterId: "VIS-001",
            PatientId: patientId,
            DeptId: "DEPT-OPD-01",
            WardId: null,
            TemplateId: "TMPL-01",
            DocTypeCode: "OPD_FIRST_VISIT",
            DocTitle: "门诊初诊病历",
            StructuredElements: new Dictionary<string, string>
            {
                { "CHIEF_COMPLAINT", "反复胸痛1周" },
                { "DE_PREGNANCY_COUNT", "1" } // Female only!
            },
            PlainTextSummary: "反复胸痛1周",
            AuthorId: "USER-DOC-OPD"
        ));
        var failData = await maleFailRes.Content.ReadFromJsonAsync<ApiResponse<EmrDocument>>();
        Assert.Equal(400, failData?.Code);
        Assert.Contains("禁止录入女性专有病历数据元", failData?.Message);

        // Correct EMR without conflict
        var emrOkRes = await _client.PostAsJsonAsync("/api/v4/emr/documents", new EmrController.SaveEmrRequestDto(
            DocNo: "EMR-TEST-OK-" + Guid.NewGuid().ToString("N")[..6],
            EmrScene: "OPD",
            VisitOrEncounterId: "VIS-001",
            PatientId: patientId,
            DeptId: "DEPT-OPD-01",
            WardId: null,
            TemplateId: "TMPL-01",
            DocTypeCode: "OPD_FIRST_VISIT",
            DocTitle: "门诊初诊病历",
            StructuredElements: new Dictionary<string, string>
            {
                { "CHIEF_COMPLAINT", "反复胸痛1周，加重半天" },
                { "PRESENT_ILLNESS", "患者劳累后胸痛，休息后缓解" }
            },
            PlainTextSummary: "反复胸痛1周，加重半天。劳累后胸痛，休息后缓解。",
            AuthorId: "USER-DOC-OPD"
        ));
        var emrOkData = await emrOkRes.Content.ReadFromJsonAsync<ApiResponse<EmrDocument>>();
        Assert.Equal(200, emrOkData?.Code);
        Assert.NotNull(emrOkData?.Data);
        var docId = emrOkData.Data.Id;

        // Sign EMR & Generate GB/T 33190-2016 OFD Archive
        var signRes = await _client.PostAsJsonAsync($"/api/v4/emr/documents/{docId}/sign-and-generate-ofd", new EmrController.SignEmrRequestDto(
            SignerId: "USER-DOC-OPD",
            SignerName: "张建国 (主治医师)",
            SignLevel: 1
        ));
        Assert.True(signRes.IsSuccessStatusCode);
        var signData = await signRes.Content.ReadFromJsonAsync<ApiResponse<EmrOfdArchive>>();
        Assert.NotNull(signData?.Data?.OfdNo);
        Assert.NotNull(signData?.Data);
        Assert.Equal("GB/T 33190-2016", signData.Data.OfdVersionStandard);

        // 4. Inpatient Admission with Borrowed Bed (WF-IPD-01, WF-IPD-02)
        // Admitting to Cardiology IPD 1 (DEPT-IPD-01) but borrowing bed in Ward 2 (DEPT-WARD-02)
        var dynamicBed = $"B{Guid.NewGuid():N}"[..6];
        var admitRes = await _client.PostAsJsonAsync("/api/v4/ipd/admit", new InpatientController.AdmitPatientRequestDto(
            PatientId: patientId,
            CampusId: "CAMPUS-01",
            AdmitDeptId: "DEPT-IPD-01",
            AdmitWardId: "DEPT-WARD-02",
            BedCode: dynamicBed,
            IsBorrowedBed: true,
            PrepaymentAmount: 3000.00m
        ));
        Assert.True(admitRes.IsSuccessStatusCode);
        var admitData = await admitRes.Content.ReadFromJsonAsync<ApiResponse<IpdEncounter>>();
        Assert.Equal((short)1, admitData!.Data!.IsBorrowedBed);
        var encounterId = admitData.Data.Id;

        // Verify 4-Dimensional Department Access Rule (GATE-02)
        // Admitting IPD Doctor (DEPT-IPD-01) -> PASS
        var docAccessRes = await _client.GetAsync($"/api/v4/ipd/encounters/{encounterId}/access-check?deptCategory=IPD&deptOrWardId=DEPT-IPD-01");
        Assert.True(docAccessRes.IsSuccessStatusCode);

        // Other IPD Doctor (DEPT-IPD-02) -> 403 Forbidden!
        var otherDocAccessRes = await _client.GetAsync($"/api/v4/ipd/encounters/{encounterId}/access-check?deptCategory=IPD&deptOrWardId=DEPT-IPD-02");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, otherDocAccessRes.StatusCode);

        // Borrowed Ward Nurse (DEPT-WARD-02) -> PASS
        var nurseAccessRes = await _client.GetAsync($"/api/v4/ipd/encounters/{encounterId}/access-check?deptCategory=WARD&deptOrWardId=DEPT-WARD-02");
        Assert.True(nurseAccessRes.IsSuccessStatusCode);

        // 5. MedTech Service Request & Fee Lock (WF-MTC-01, GATE-06)
        var reqRes = await _client.PostAsJsonAsync("/api/v4/medtech/requests", new MedTechController.CreateRequestDto(
            SourceType: "IPD",
            SourceVisitOrOrderId: encounterId,
            PatientId: patientId,
            OrderDeptId: "DEPT-IPD-01",
            OrderWardId: "DEPT-WARD-02",
            ServiceItemCode: "LAB_BLOOD_ROUTINE",
            FeeAmount: 25.00m
        ));
        var reqContent = await reqRes.Content.ReadAsStringAsync();
        Assert.True(reqRes.IsSuccessStatusCode, $"Status: {reqRes.StatusCode}, Content: {reqContent}");
        var reqData = await reqRes.Content.ReadFromJsonAsync<ApiResponse<MtcServiceRequest>>();
        Assert.NotNull(reqData?.Data);
        var mtcRequestId = reqData.Data.Id;

        // Confirm fee and receive sample
        var feeRes = await _client.PostAsync($"/api/v4/medtech/requests/{mtcRequestId}/confirm-fee-and-receive", null);
        Assert.True(feeRes.IsSuccessStatusCode);

        // Now cancel request -> MUST BE BLOCKED due to is_fee_confirmed=1 (GATE-06)
        var cancelRes = await _client.PostAsync($"/api/v4/medtech/requests/{mtcRequestId}/cancel", null);
        var cancelData = await cancelRes.Content.ReadFromJsonAsync<ApiResponse<bool>>();
        Assert.Equal(400, cancelData?.Code);
        Assert.Contains("已扫码核收确费", cancelData?.Message);

        // 6. Inpatient Discharge Six-Item Checklist (GATE-07)
        var checkRes = await _client.PostAsync($"/api/v4/ipd/encounters/{encounterId}/discharge-check", null);
        var checkData = await checkRes.Content.ReadFromJsonAsync<ApiResponse<DischargeCheckResult>>();
        // Because discharge summary OFD not yet written, it must fail six-item check
        Assert.Equal(400, checkData?.Code);
        Assert.False(checkData?.Data?.AllPassed);
    }

    [Fact]
    public async Task EndToEnd_PharmacyFefoDispense_ThreeStepDrugReturnRefund_And_AuditChainVerification()
    {
        var visitId = "VIS-FIN-PHM-" + Guid.NewGuid().ToString("N")[..8];
        await EnsureTestVisitAsync(visitId, "PAT-PHM-01");

        // 1. Create prescription with NORMAL_SALINE_500 (seeded in inv_drug_batches)
        var rxRes = await _client.PostAsJsonAsync("/api/v4/opd/prescriptions",
            new OutpatientController.CreatePrescriptionRequestDto(
                VisitId: visitId,
                PatientId: "PAT-PHM-01",
                OpdDeptId: "DEPT-OPD-01",
                DoctorId: "USER-DOC-OPD",
                TargetPharmacyId: "DEPT-PHM-01",
                Items: new List<OpdPrescriptionItem>
                {
                    new()
                    {
                        GroupNo = 1,
                        DrugId = "DRUG-03",
                        DrugNameSnapshot = "0.9%氯化钠注射液",
                        DoseOnce = 500m,
                        DoseUnit = "ml",
                        FrequencyCode = "QD",
                        RouteCode = "IV_DRIP",
                        Days = 1,
                        TotalQuantity = 4,
                        UnitPrice = 5.50m
                    }
                }));
        var rxData = await rxRes.Content.ReadFromJsonAsync<ApiResponse<OpdPrescription>>();
        Assert.Equal(200, rxData?.Code);
        var rxId = rxData!.Data!.Id;

        // 1.5. Pharmacist reviews prescription
        var reviewRes = await _client.PostAsJsonAsync($"/api/v4/opd/prescriptions/{rxId}/review", new
        {
            Status = 1,
            ReviewRemark = "药师前置审方通过"
        });
        Assert.True(reviewRes.IsSuccessStatusCode);

        // 2. Settle payment at Finance window
        var settleRes = await _client.PostAsJsonAsync("/api/v4/finance/settle",
            new FinanceController.SettlePaymentRequestDto(
                Scene: "OPD",
                PatientId: "PAT-PHM-01",
                VisitOrEncounterId: visitId,
                TotalAmount: 22.00m,
                InsuranceAmount: 12.00m,
                PrepaymentDeduct: 0m,
                SelfPayAmount: 10.00m,
                PaymentChannel: "WECHAT",
                ChargeItems: new List<FinChargeLedger>
                {
                    new()
                    {
                        OrderDeptId = "DEPT-OPD-01",
                        ExecDeptId = "DEPT-PHM-01",
                        ItemCode = "NORMAL_SALINE_500",
                        ItemName = "0.9%氯化钠注射液",
                        Quantity = 4m,
                        UnitPrice = 5.50m,
                        TotalAmount = 22.00m
                    }
                },
                RequestKey: "PAY-KEY-" + visitId,
                CashierUserId: "USER-CASHIER-01"));
        var settleData = await settleRes.Content.ReadFromJsonAsync<ApiResponse<FinPaymentOrder>>();
        Assert.Equal(200, settleData?.Code);
        var paymentId = settleData!.Data!.Id;

        // 3. Pharmacy dispenses prescription -> deducts FEFO batch stock
        var dispenseRes = await _client.PostAsJsonAsync("/api/v4/pharmacy/dispense",
            new PharmacyController.DispensePrescriptionRequestDto(
                PharmacyId: "DEPT-PHM-01",
                PrescriptionId: rxId,
                PatientId: "PAT-PHM-01",
                PharmacistId: "USER-PHM-01"));
        var dispenseData = await dispenseRes.Content.ReadFromJsonAsync<ApiResponse<PhmDispenseOrder>>();
        Assert.Equal(200, dispenseData?.Code);
        var dispenseOrderId = dispenseData!.Data!.Id;

        // 4. Attempting direct refund BEFORE pharmacy confirms return -> MUST BE BLOCKED (GATE-06)
        var blockedRefundRes = await _client.PostAsJsonAsync("/api/v4/finance/refund",
            new FinanceController.ExecuteRefundRequestDto(paymentId, 22.00m, "患者申请退药退费"));
        var blockedRefund = await blockedRefundRes.Content.ReadFromJsonAsync<ApiResponse<FinRefundOrder>>();
        Assert.Equal(400, blockedRefund?.Code);
        Assert.Contains("尚未在药房完成实物扫码验收入库", blockedRefund?.Message);

        // 5. Pharmacy confirms physical drug return -> restores FEFO batch stock
        var confirmReturnRes = await _client.PostAsJsonAsync("/api/v4/pharmacy/confirm-return",
            new PharmacyController.ConfirmDrugReturnRequestDto(dispenseOrderId, "USER-PHM-01", "包装完好退药入库"));
        var confirmReturn = await confirmReturnRes.Content.ReadFromJsonAsync<ApiResponse<PhmDispenseOrder>>();
        Assert.Equal(200, confirmReturn?.Code);
        Assert.Equal((short)1, confirmReturn?.Data?.DrugReturnConfirmed);

        // 6. Now Finance refund succeeds
        var okRefundRes = await _client.PostAsJsonAsync("/api/v4/finance/refund",
            new FinanceController.ExecuteRefundRequestDto(paymentId, 22.00m, "药房已确认退药"));
        var okRefund = await okRefundRes.Content.ReadFromJsonAsync<ApiResponse<FinRefundOrder>>();
        Assert.Equal(200, okRefund?.Code);

        // 7. Verify tamper-evident audit hash chain
        var verifyChainRes = await _client.GetAsync("/api/v4/operations/audit-logs/verify-chain");
        var verifyChain = await verifyChainRes.Content.ReadFromJsonAsync<ApiResponse<AuditChainVerificationResult>>();
        Assert.Equal(200, verifyChain?.Code);
        Assert.True(verifyChain?.Data?.IsIntact);
    }

    [Fact]
    public async Task MasterData_Icd10_PriceItems_OrderFrequencies_And_ClinicalDictionaries_ClosedLoopTest()
    {
        // 1. Check Master Data Summary
        var summaryRes = await _client.GetAsync("/api/v4/config/master/summary");
        Assert.Equal(HttpStatusCode.OK, summaryRes.StatusCode);
        var summary = await summaryRes.Content.ReadFromJsonAsync<ApiResponse<ConfigController.MasterDataSummaryDto>>();
        Assert.Equal(200, summary?.Code);
        Assert.True(summary!.Data!.Icd10DiagnosisCount >= 10);
        Assert.True(summary.Data.PriceItemCount >= 10);
        Assert.True(summary.Data.OrderFrequencyCount >= 10);
        Assert.True(summary.Data.ClinicalDictionaryCount >= 20);

        // 2. Query ICD-10 with Male gender filter -> should include N40.x00 (良性前列腺增生) and exclude O80.900 (单胎顺产)
        var maleIcdRes = await _client.GetAsync("/api/v4/config/master/icd10?genderCode=1");
        var maleIcds = await maleIcdRes.Content.ReadFromJsonAsync<ApiResponse<List<CfgIcd10Diagnosis>>>();
        Assert.Contains(maleIcds!.Data!, x => x.IcdCode == "N40.x00");
        Assert.DoesNotContain(maleIcds.Data!, x => x.IcdCode == "O80.900");

        // 3. MedTech Request with FeeAmount=0 -> auto-populates UnitPrice (180.00m) from cfg_price_items for RAD_CHEST_CT
        await EnsureTestVisitAsync("VIS-MASTER-01", "PAT-MASTER-01");
        var mtcRes = await _client.PostAsJsonAsync("/api/v4/medtech/requests",
            new MedTechController.CreateRequestDto(
                SourceType: "OPD",
                SourceVisitOrOrderId: "VIS-MASTER-01",
                PatientId: "PAT-MASTER-01",
                OrderDeptId: "DEPT-OPD-01",
                OrderWardId: null,
                ServiceItemCode: "RAD_CHEST_CT",
                FeeAmount: 0m));
        var mtcData = await mtcRes.Content.ReadFromJsonAsync<ApiResponse<MtcServiceRequest>>();
        Assert.Equal(200, mtcData?.Code);
        Assert.Equal(180.00m, mtcData!.Data!.FeeAmount);
        Assert.Equal("RADIOLOGY", mtcData.Data.MedTechSubType);

        // 4. Customize Order Frequency in cfg_order_frequencies (q6h -> 4 times/day) and verify OrderingController uses it
        var saveFreqRes = await _client.PostAsJsonAsync("/api/v4/config/master/frequencies", new CfgOrderFrequency
        {
            FreqCode = "q6h_custom",
            FreqName = "每6小时一次(专科)",
            TimePointsText = "04:00, 10:00, 16:00, 22:00",
            ApplicableTerm = "STANDING",
            Description = "ICU 专科自定义每6小时频次"
        });
        var savedFreq = await saveFreqRes.Content.ReadFromJsonAsync<ApiResponse<CfgOrderFrequency>>();
        Assert.Equal(200, savedFreq?.Code);
        Assert.Equal(4, savedFreq!.Data!.DailyTimes);

        var encId = "ENC-FREQ-" + Guid.NewGuid().ToString("N")[..6];
        await EnsureTestEncounterAsync(encId, "PAT-MASTER-01", "DEPT-IPD-01", "DEPT-WARD-01");
        var createOrdRes = await _client.PostAsJsonAsync("/api/v4/orders",
            new OrderingController.CreateOrderRequestDto(
                EncounterId: encId,
                PatientId: "PAT-MASTER-01",
                OrderIpdDeptId: "DEPT-IPD-01",
                PatientWardId: "DEPT-WARD-01",
                ExecTargetDeptId: "DEPT-WARD-01",
                OrderTermType: 1,
                GroupNo: 1,
                OrderCategory: "DRUG",
                ItemCode: "PENICILLIN_G",
                ItemName: "美罗培南注射液",
                Dosage: 1.0m,
                FrequencyCode: "q6h_custom",
                RouteCode: "IV_DRIP",
                OrderDoctorId: "USER-DOC-ATTENDING"));
        var ordData = await createOrdRes.Content.ReadFromJsonAsync<ApiResponse<OrdMedicalOrder>>();
        var verifyOrdRes = await _client.PostAsJsonAsync($"/api/v4/orders/{ordData!.Data!.Id}/verify",
            new OrderingController.VerifyOrderRequestDto("USER-NURSE-W1", "DEPT-WARD-01"));
        Assert.True(verifyOrdRes.IsSuccessStatusCode);

        var plansRes = await _client.GetAsync($"/api/v4/orders/plans/by-encounter/{encId}");
        var plans = await plansRes.Content.ReadFromJsonAsync<ApiResponse<List<OrdExecutionPlan>>>();
        Assert.Equal(4, plans?.Data?.Count);
    }

    [Fact]
    public async Task EmrVisualConfig_OutpatientInpatientNursing_AdminLayoutAndTemplateDesign_And_PrintPreview_ClosedLoopTest()
    {
        // 1. Verify seeded templates across Outpatient (OPD), Inpatient (IPD), and Nursing (WARD)
        var allTplRes = await _client.GetAsync("/api/v4/emr/templates");
        Assert.Equal(HttpStatusCode.OK, allTplRes.StatusCode);
        var allTpls = await allTplRes.Content.ReadFromJsonAsync<ApiResponse<List<EmrController.EmrTemplateSummaryDto>>>();
        Assert.Equal(200, allTpls?.Code);
        Assert.Contains(allTpls!.Data!, t => t.EmrScene == "OPD" && t.TemplateCode == "OPD_FIRST_VISIT");
        Assert.Contains(allTpls.Data!, t => t.EmrScene == "IPD" && t.TemplateCode == "IPD_ADMISSION_24H");
        Assert.Contains(allTpls.Data!, t => t.EmrScene == "WARD" && t.TemplateCode == "WARD_ADMISSION_ASSESS");

        // 2. Admin customizes a Nursing Print Layout (A4 with custom margins & nursing scale banner enabled)
        var layoutRes = await _client.PostAsJsonAsync("/api/v4/emr/layouts", new EmrController.UpsertEmrLayoutRequestDto(
            LayoutCode: "WARD_CUSTOM_A4",
            LayoutName: "护理病历管理员自定义A4版式",
            PageSizeCode: "A4",
            PageWidthMm: 210.0m,
            PageHeightMm: 297.0m,
            MarginTopMm: 22.0m,
            MarginBottomMm: 18.0m,
            MarginLeftMm: 22.0m,
            MarginRightMm: 18.0m,
            IsContinuousFlow: 1,
            HospitalName: "国家区域医疗中心 (三级甲等综合医院)",
            SubTitle: "住院患者专科护理评估与三评记录单",
            ShowPatientBanner: true,
            ShowBedAndAdmissionNo: true,
            ShowVitalSignsBanner: true,
            ShowNursingScales: true,
            ShowTrackChanges: false,
            AccentColor: "#059669",
            FooterText: "护理部质控标准版式 · 责任护士与护士长双签生效",
            WatermarkPolicy: "三甲医院护理病历质控专章"
        ));
        var savedLayout = await layoutRes.Content.ReadFromJsonAsync<ApiResponse<EmrOfdLayoutSchema>>();
        Assert.Equal(200, savedLayout?.Code);
        Assert.Equal(22.0m, savedLayout!.Data!.MarginTopMm);

        // 3. Admin visually designs and saves the Nursing EMR Template with custom sections & fields
        var designRes = await _client.PostAsJsonAsync("/api/v4/emr/templates/design", new EmrController.SaveEmrTemplateDesignRequestDto(
            TemplateCode: "WARD_ADMISSION_ASSESS",
            TemplateName: "住院患者入院护理评估单 (管理员修订版)",
            EmrScene: "WARD",
            DocTypeCode: "NURSING_ADMISSION",
            DeptCategory: "WARD",
            TargetDeptId: "DEPT-WARD-01",
            DiseaseIcdPrefix: null,
            TimeLimitHours: 4,
            RequiredSignLevels: 2,
            OfdLayoutId: savedLayout.Data.Id,
            IsPublished: 1,
            Sections: new List<EmrController.EmrSectionDesignItemDto>
            {
                new("SEC_NUR_GENERAL", "一、入科一般资料与生命体征", "GRID_FORM", new List<string> { "NURSING_CONSCIOUSNESS", "ALLERGY_HISTORY", "PHYSICAL_EXAM" }, 1, "ALL", 1),
                new("SEC_NUR_SCALES", "二、护理风险三评量表 (Braden/Barthel/Morse)", "TABLE_GRID", new List<string> { "NURSING_BRADEN_SCORE", "NURSING_BARTHEL_SCORE", "NURSING_FALL_SCORE" }, 1, "ALL", 2),
                new("SEC_NUR_PLAN", "三、专科护理措施与宣教", "INLINE_PARAGRAPH", new List<string> { "NURSING_INTERVENTION" }, 1, "ALL", 3)
            }
        ));
        var designedTpl = await designRes.Content.ReadFromJsonAsync<ApiResponse<EmrController.EmrTemplateDetailDto>>();
        Assert.Equal(200, designedTpl?.Code);
        Assert.Equal(3, designedTpl!.Data!.Sections.Count);

        // 4. Generate WYSIWYG Print Preview for Outpatient (OPD B5), Inpatient (IPD A4), and Nursing (WARD A4)
        var wardPreviewRes = await _client.PostAsJsonAsync("/api/v4/emr/print-preview", new EmrController.EmrPrintPreviewRequestDto(
            DocumentIdOrNo: "DOC-WARD-20261105001",
            EmrScene: "WARD",
            TemplateIdOrCode: "WARD_ADMISSION_ASSESS"
        ));
        var wardPreview = await wardPreviewRes.Content.ReadFromJsonAsync<ApiResponse<EmrController.EmrPrintPreviewResponseDto>>();
        Assert.Equal(200, wardPreview?.Code);
        Assert.True(wardPreview!.Data!.OfdVerifyPassed);
        Assert.Equal("A4", wardPreview.Data.PageSizeCode);
        Assert.Equal(22.0m, wardPreview.Data.MarginTopMm);
        Assert.True(wardPreview.Data.ShowNursingScales);
        Assert.Equal(3, wardPreview.Data.Sections.Count);
    }

    [Fact]
    public async Task PharmacyWarehouseFinance_TenCoreWorkflows_ClosedLoopTest()
    {
        var runId = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var drugId = $"DRUG-R-{runId}";
        var drugCode = $"DRUG-C-{runId}";
        var batchNo = $"LOT-{runId}";
        var invoiceNo = $"INV-{runId}";

        // 1. 药品基础数据维护 (Drug Basic Master Data Maintenance)
        var upsertDrugRes = await _client.PostAsJsonAsync("/api/v4/inventory/drugs", new InventoryController.UpsertDrugRequestDto(
            Id: drugId,
            DrugCode: drugCode,
            DrugName: "瑞舒伐他汀钙片",
            Spec: "10mg*7片/盒",
            UnitName: "盒",
            Price: 36.00m,
            CostPrice: 25.00m,
            AntibioticLevel: 0,
            IsNarcotic: 0,
            IsSkinTestRequired: 0,
            IsActive: 1,
            OperatorId: "USER-ADMIN"
        ));
        var savedDrug = await upsertDrugRes.Content.ReadFromJsonAsync<ApiResponse<InvDrug>>();
        Assert.Equal(200, savedDrug?.Code);
        Assert.Equal(36.00m, savedDrug!.Data!.Price);

        // 3. 药品库存管理 (Stock-In to Central Warehouse DEPT-WH-01)
        var stockInRes = await _client.PostAsJsonAsync("/api/v4/inventory/batches/stock-in", new InventoryController.StockInRequestDto(
            WarehouseOrPharmacyId: "DEPT-WH-01",
            DrugId: drugId,
            BatchNo: batchNo,
            ExpiryDateInt: 20281130,
            Quantity: 500,
            InvoiceNo: invoiceNo,
            ColdChainTempCelsius: null,
            OperatorId: "USER-ADMIN"
        ));
        var stockedBatch = await stockInRes.Content.ReadFromJsonAsync<ApiResponse<InvDrugBatch>>();
        Assert.Equal(200, stockedBatch?.Code);
        Assert.Equal(500, stockedBatch!.Data!.Quantity);

        // 2. 药品定价调价管理 (Adjust Drug Price & Calculate Inventory Revaluation Diff)
        var adjustPriceRes = await _client.PostAsJsonAsync($"/api/v4/inventory/drugs/{drugId}/adjust-price", new InventoryController.AdjustDrugPriceRequestDto(
            NewRetailPrice: 32.00m,
            NewCostPrice: 22.00m,
            AdjustReason: "国家集采中选零差率调价",
            OperatorId: "USER-ADMIN"
        ));
        var priceLog = await adjustPriceRes.Content.ReadFromJsonAsync<ApiResponse<InvDrugPriceLog>>();
        Assert.Equal(200, priceLog?.Code);
        Assert.Equal(500, priceLog!.Data!.AffectedStockQuantity);
        Assert.Equal(-2000.00m, priceLog.Data.PriceDiffAmount); // (32 - 36) * 500 = -2000

        // 6. 药库发药管理 (Central Warehouse DEPT-WH-01 issues 200 boxes to Outpatient Pharmacy DEPT-PHM-01)
        var whIssueRes = await _client.PostAsJsonAsync("/api/v4/inventory/warehouse-issue", new InventoryController.CreateTransferOrIssueRequestDto(
            SourceWarehouseId: "DEPT-WH-01",
            TargetWarehouseId: "DEPT-PHM-01",
            DrugId: drugId,
            Quantity: 200,
            TransferType: "WAREHOUSE_ISSUE",
            OperatorId: "USER-PHM-01",
            Remark: "门诊西药房请领补货"
        ));
        var issueOrders = await whIssueRes.Content.ReadFromJsonAsync<ApiResponse<List<InvTransferOrder>>>();
        Assert.Equal(200, issueOrders?.Code);
        Assert.Single(issueOrders!.Data!);
        Assert.StartsWith("ISS-", issueOrders.Data![0].TransferNo);

        // 5. 多个库房调拨管理 (Inter-Pharmacy Transfer: DEPT-PHM-01 transfers 50 boxes to Inpatient Pharmacy DEPT-PHM-02)
        var transferRes = await _client.PostAsJsonAsync("/api/v4/inventory/transfers", new InventoryController.CreateTransferOrIssueRequestDto(
            SourceWarehouseId: "DEPT-PHM-01",
            TargetWarehouseId: "DEPT-PHM-02",
            DrugId: drugId,
            Quantity: 50,
            TransferType: "INTER_WAREHOUSE",
            OperatorId: "USER-PHM-01",
            Remark: "跨药房应急调拨"
        ));
        var trfOrders = await transferRes.Content.ReadFromJsonAsync<ApiResponse<List<InvTransferOrder>>>();
        Assert.Equal(200, trfOrders?.Code);
        Assert.StartsWith("TRF-", trfOrders!.Data![0].TransferNo);

        // 7, 8, 9 & 10. 收费通知药房 -> 药房给患者发药 -> 发完药关闭订单防二次发药 -> 退药相关功能
        var visitId = $"VIS-LOOP10-{runId}";
        await EnsureTestVisitAsync(visitId, "PAT-LOOP-10");
        var rxRes = await _client.PostAsJsonAsync("/api/v4/opd/prescriptions", new OutpatientController.CreatePrescriptionRequestDto(
            VisitId: visitId,
            PatientId: "PAT-LOOP-10",
            OpdDeptId: "DEPT-OPD-01",
            DoctorId: "USER-DOC-OPD",
            TargetPharmacyId: "DEPT-PHM-01",
            Items: new List<OpdPrescriptionItem>
            {
                new()
                {
                    GroupNo = 1,
                    DrugId = drugId,
                    DrugNameSnapshot = "瑞舒伐他汀钙片",
                    DoseOnce = 10m,
                    DoseUnit = "mg",
                    FrequencyCode = "QN",
                    RouteCode = "PO",
                    Days = 14,
                    TotalQuantity = 2,
                    UnitPrice = 32.00m
                }
            }
        ));
        var rxData = await rxRes.Content.ReadFromJsonAsync<ApiResponse<OpdPrescription>>();
        Assert.Equal(200, rxData?.Code);
        var rxId = rxData!.Data!.Id;

        // (8a) Attempting to dispense BEFORE Cashier payment settlement -> MUST BE BLOCKED (400)!
        var prematureDispenseRes = await _client.PostAsJsonAsync("/api/v4/pharmacy/dispense",
            new PharmacyController.DispensePrescriptionRequestDto("DEPT-PHM-01", rxId, "PAT-LOOP-10", "USER-PHM-01"));
        var prematureDispense = await prematureDispenseRes.Content.ReadFromJsonAsync<ApiResponse<PhmDispenseOrder>>();
        Assert.Equal(400, prematureDispense?.Code);
        Assert.Contains("尚未完成收费员收费结算", prematureDispense?.Message);

        // (8a.2) Pharmacist reviews and approves prescription
        var reviewRes = await _client.PostAsJsonAsync($"/api/v4/opd/prescriptions/{rxId}/review", new
        {
            Status = 1,
            ReviewRemark = "前置审方通过"
        });
        Assert.True(reviewRes.IsSuccessStatusCode);

        // (8b) Cashier settles payment -> updates prescription status to 3 (Paid) and notifies Pharmacy!
        var settleRes = await _client.PostAsJsonAsync("/api/v4/finance/settle", new FinanceController.SettlePaymentRequestDto(
            Scene: "OPD",
            PatientId: "PAT-LOOP-10",
            VisitOrEncounterId: visitId,
            TotalAmount: 64.00m,
            InsuranceAmount: 44.00m,
            PrepaymentDeduct: 0m,
            SelfPayAmount: 20.00m,
            PaymentChannel: "WECHAT",
            ChargeItems: new List<FinChargeLedger>
            {
                new()
                {
                    OrderDeptId = "DEPT-OPD-01",
                    ExecDeptId = "DEPT-PHM-01",
                    ItemCode = drugCode,
                    ItemName = "瑞舒伐他汀钙片",
                    Quantity = 2m,
                    UnitPrice = 32.00m,
                    TotalAmount = 64.00m
                }
            },
            CashierUserId: "USER-CASHIER-01"
        ));
        var settleData = await settleRes.Content.ReadFromJsonAsync<ApiResponse<FinPaymentOrder>>();
        Assert.Equal(200, settleData?.Code);

        // Verify Pharmacy Ready Queue now has the paid notification
        var readyQueueRes = await _client.GetAsync("/api/v4/pharmacy/ready-queue?pharmacyId=DEPT-PHM-01");
        var readyQueue = await readyQueueRes.Content.ReadFromJsonAsync<ApiResponse<List<PharmacyController.PharmacyReadyNotificationDto>>>();
        Assert.Contains(readyQueue!.Data!, q => q.PrescriptionId == rxId && q.CanDispense && !q.IsOrderClosed);

        // (7) Pharmacy dispenses to patient -> succeeds and closes the order
        var okDispenseRes = await _client.PostAsJsonAsync("/api/v4/pharmacy/dispense",
            new PharmacyController.DispensePrescriptionRequestDto("DEPT-PHM-01", rxId, "PAT-LOOP-10", "USER-PHM-01"));
        var okDispense = await okDispenseRes.Content.ReadFromJsonAsync<ApiResponse<PhmDispenseOrder>>();
        Assert.Equal(200, okDispense?.Code);
        var dispenseOrderId = okDispense!.Data!.Id;

        // (9) Attempting secondary dispense on the closed order -> MUST BE BLOCKED with 409 Conflict!
        var duplicateDispenseRes = await _client.PostAsJsonAsync("/api/v4/pharmacy/dispense",
            new PharmacyController.DispensePrescriptionRequestDto("DEPT-PHM-01", rxId, "PAT-LOOP-10", "USER-PHM-01"));
        var duplicateDispense = await duplicateDispenseRes.Content.ReadFromJsonAsync<ApiResponse<PhmDispenseOrder>>();
        Assert.Equal(409, duplicateDispense?.Code);
        Assert.Contains("严禁二次重复发药", duplicateDispense?.Message);

        // (10a) Attempting drug return with damaged packaging -> blocked by GSP quality check (400)
        var damagedReturnRes = await _client.PostAsJsonAsync("/api/v4/pharmacy/confirm-return",
            new PharmacyController.ConfirmDrugReturnRequestDto(dispenseOrderId, "USER-PHM-01", "包装破损测试", IsPackagingIntact: false, IsColdChainQualified: true));
        var damagedReturn = await damagedReturnRes.Content.ReadFromJsonAsync<ApiResponse<PhmDispenseOrder>>();
        Assert.Equal(400, damagedReturn?.Code);

        // (10b) Valid drug return -> restores stock and sets drug_return_confirmed = 1
        var validReturnRes = await _client.PostAsJsonAsync("/api/v4/pharmacy/confirm-return",
            new PharmacyController.ConfirmDrugReturnRequestDto(dispenseOrderId, "USER-PHM-01", "患者药物不良反应退药", IsPackagingIntact: true, IsColdChainQualified: true));
        var validReturn = await validReturnRes.Content.ReadFromJsonAsync<ApiResponse<PhmDispenseOrder>>();
        Assert.Equal(200, validReturn?.Code);
        Assert.Equal((short)1, validReturn!.Data!.DrugReturnConfirmed);

        // 4. 财务药品入库销售管理 (Financial Drug Stock-In & Retail Sales Reconciliation Report)
        var finReportRes = await _client.GetAsync("/api/v4/inventory/finance-report");
        var raw = await finReportRes.Content.ReadAsStringAsync();
        Assert.True(finReportRes.IsSuccessStatusCode, $"Status: {finReportRes.StatusCode}, body: {raw}");
        var finReport = await finReportRes.Content.ReadFromJsonAsync<ApiResponse<InventoryController.DrugFinanceReconciliationReportDto>>();
        Assert.Equal(200, finReport?.Code);
        Assert.True(finReport!.Data!.TotalProcurementCostAmount > 0);
        Assert.Contains(finReport.Data.ItemBreakdowns, b => b.DrugCode == drugCode);
    }

    [Fact]
    public async Task WardPharmacy_DispenseFromReadyQueue_DeductsStockAndBlocksRepeat()
    {
        var runId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var patientId = "PAT-WARD-RX-" + runId;
        var encounterId = Guid.NewGuid().ToString("D");
        var drugId = "DRUG-WARD-RX-" + runId;
        var drugCode = "WARD-RX-" + runId;
        var medicalOrderId = Guid.NewGuid().ToString("D");
        var batchNo = "WARDLOT-" + runId;
        const int stockInQuantity = 30;
        const int dispenseQuantity = 5;
        const string pharmacyId = "DEPT-PHM-02";
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await EnsureTestEncounterAsync(encounterId, patientId, "DEPT-IPD-01", "DEPT-WARD-01");

        var drugRes = await _client.PostAsJsonAsync("/api/v4/inventory/drugs", new InventoryController.UpsertDrugRequestDto(
            Id: drugId,
            DrugCode: drugCode,
            DrugName: "住院发药闭环测试药品",
            Spec: "10mg*12片/盒",
            UnitName: "盒",
            Price: 18.50m,
            CostPrice: 12.00m,
            AntibioticLevel: 0,
            IsNarcotic: 0,
            IsSkinTestRequired: 0,
            IsActive: 1,
            OperatorId: "USER-ADMIN"));
        var savedDrug = await drugRes.Content.ReadFromJsonAsync<ApiResponse<InvDrug>>();
        Assert.Equal(200, savedDrug?.Code);

        // Warehouse receiving is the normal procurement path; the central pharmacy then receives the stock.
        var stockInRes = await _client.PostAsJsonAsync("/api/v4/inventory/batches/stock-in", new InventoryController.StockInRequestDto(
            WarehouseOrPharmacyId: "DEPT-WH-01",
            DrugId: drugId,
            BatchNo: batchNo,
            ExpiryDateInt: 20281231,
            Quantity: stockInQuantity,
            InvoiceNo: "WARD-INV-" + runId,
            OperatorId: "USER-ADMIN"));
        var stockIn = await stockInRes.Content.ReadFromJsonAsync<ApiResponse<InvDrugBatch>>();
        Assert.Equal(200, stockIn?.Code);

        var issueRes = await _client.PostAsJsonAsync("/api/v4/inventory/warehouse-issue", new InventoryController.CreateTransferOrIssueRequestDto(
            SourceWarehouseId: "DEPT-WH-01",
            TargetWarehouseId: pharmacyId,
            DrugId: drugId,
            Quantity: stockInQuantity,
            TransferType: "WAREHOUSE_ISSUE",
            OperatorId: "USER-ADMIN",
            Remark: "住院中心药房闭环测试补货"));
        var issue = await issueRes.Content.ReadFromJsonAsync<ApiResponse<List<InvTransferOrder>>>();
        Assert.Equal(200, issue?.Code);
        Assert.Single(issue!.Data!);

        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            await conn.ExecuteAsync("""
                INSERT INTO ord_medical_orders
                    (id, order_no, encounter_id, patient_id, order_ipd_dept_id, patient_ward_id,
                     exec_target_dept_id, order_term_type, group_no, order_category, item_code,
                     item_name, dosage, frequency_code, route_code, status, order_doctor_id,
                     verify_nurse_id, plan_start_at_ms, plan_stop_at_ms, ext_data_json,
                     schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES
                    (@Id, @OrderNo, @EncounterId, @PatientId, 'DEPT-IPD-01', 'DEPT-WARD-01',
                     @PharmacyId, 2, 1, 'DRUG', @DrugId,
                     '住院发药闭环测试药品', 10, 'qd', 'PO', 1, 'USER-DOC-ATTENDING',
                     'USER-NURSE-01', @Now, NULL, '{"test":"ward-pharmacy"}',
                     1, @Now, 'USER-DOC-ATTENDING', @Now, 'USER-DOC-ATTENDING', 1)
                """, new
            {
                Id = medicalOrderId,
                OrderNo = "WARD-ORD-" + runId,
                EncounterId = encounterId,
                PatientId = patientId,
                PharmacyId = pharmacyId,
                DrugId = drugId,
                Now = nowMs
            });

            var initialAvailable = await conn.ExecuteScalarAsync<int>(
                "SELECT COALESCE(SUM(quantity), 0) FROM inv_drug_batches " +
                "WHERE warehouse_or_pharmacy_id = @PharmacyId AND drug_id = @DrugId " +
                "AND is_frozen = 0 AND quantity > 0",
                new { PharmacyId = pharmacyId, DrugId = drugId });
            Assert.Equal(stockInQuantity, initialAvailable);
        }

        using HttpClient pharmacist = await CreateAuthenticatedClientAsync(
            "phm_user", "PHARMACIST", pharmacyId);

        var queueRes = await pharmacist.GetAsync(
            "/api/v4/pharmacy/ward-ready-queue?pharmacyId=" + pharmacyId +
            "&encounterId=" + Uri.EscapeDataString(encounterId));
        var queue = await queueRes.Content.ReadFromJsonAsync<ApiResponse<List<PharmacyController.WardReadyOrderDto>>>();
        Assert.Equal(200, queue?.Code);
        var queued = Assert.Single(queue!.Data!, x => x.MedicalOrderId == medicalOrderId);
        Assert.Equal(drugId, queued.DrugId);
        Assert.Equal(pharmacyId, queued.TargetPharmacyId);

        var requestKey = "WARD-DISPENSE-" + runId;
        var dispenseRequest = new PharmacyController.WardDispenseRequestDto(
            pharmacyId,
            encounterId,
            new[] { new PharmacyController.WardDispenseLineRequestDto(medicalOrderId, dispenseQuantity) },
            requestKey);
        var dispenseRes = await pharmacist.PostAsJsonAsync("/api/v4/pharmacy/ward-dispense", dispenseRequest);
        var dispenseBody = await dispenseRes.Content.ReadAsStringAsync();
        var dispense = System.Text.Json.JsonSerializer.Deserialize<ApiResponse<PhmDispenseOrder>>(
            dispenseBody, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.True(dispense?.Code == 200,
            $"Ward dispense failed: status={dispenseRes.StatusCode}, body={dispenseBody}");
        Assert.StartsWith("DSPW", dispense!.Data!.DispenseNo);
        Assert.Equal(pharmacyId, dispense.Data.PharmacyId);
        Assert.Equal(encounterId, dispense.Data.WardSummaryId);

        var linesRes = await pharmacist.GetAsync(
            "/api/v4/pharmacy/dispense/" + Uri.EscapeDataString(dispense.Data.Id) + "/lines");
        var lines = await linesRes.Content.ReadFromJsonAsync<ApiResponse<List<PhmDispenseOrderLine>>>();
        Assert.Equal(200, lines?.Code);
        var dispensedLine = Assert.Single(lines!.Data!);
        Assert.Equal(medicalOrderId, dispensedLine.MedicalOrderId);
        Assert.Equal(dispenseQuantity, dispensedLine.Quantity);
        Assert.Equal(drugId, dispensedLine.DrugId);
        Assert.Equal(18.50m, dispensedLine.UnitPrice);

        var remainingRes = await pharmacist.GetAsync(
            "/api/v4/pharmacy/ward-ready-queue?pharmacyId=" + pharmacyId +
            "&encounterId=" + Uri.EscapeDataString(encounterId));
        var remaining = await remainingRes.Content.ReadFromJsonAsync<ApiResponse<List<PharmacyController.WardReadyOrderDto>>>();
        Assert.Equal(200, remaining?.Code);
        Assert.DoesNotContain(remaining!.Data!, x => x.MedicalOrderId == medicalOrderId);

        var repeatRequest = new PharmacyController.WardDispenseRequestDto(
            pharmacyId,
            encounterId,
            new[] { new PharmacyController.WardDispenseLineRequestDto(medicalOrderId, dispenseQuantity) },
            "WARD-REPEAT-" + runId);
        var repeatRes = await pharmacist.PostAsJsonAsync("/api/v4/pharmacy/ward-dispense", repeatRequest);
        var repeatBody = await repeatRes.Content.ReadAsStringAsync();
        var repeat = System.Text.Json.JsonSerializer.Deserialize<ApiResponse<PhmDispenseOrder>>(
            repeatBody, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.True(repeat?.Code == 409, $"Repeat ward dispense should be rejected, body={repeatBody}");
        Assert.Contains("严禁二次发药", repeat?.Message);

        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            var remainingStock = await conn.ExecuteScalarAsync<int>(
                "SELECT COALESCE(SUM(quantity), 0) FROM inv_drug_batches " +
                "WHERE warehouse_or_pharmacy_id = @PharmacyId AND drug_id = @DrugId",
                new { PharmacyId = pharmacyId, DrugId = drugId });
            Assert.Equal(stockInQuantity - dispenseQuantity, remainingStock);

            var ledger = await conn.QueryAsync<InvStockLedger>(
                "SELECT * FROM inv_stock_ledgers " +
                "WHERE warehouse_or_pharmacy_id = @PharmacyId AND drug_id = @DrugId " +
                "AND change_type = 'OUT_DISPENSE'",
                new { PharmacyId = pharmacyId, DrugId = drugId });
            var outbound = Assert.Single(ledger);
            Assert.Equal(-dispenseQuantity, outbound.ChangeQuantity);
            Assert.Equal(dispense.Data.DispenseNo, outbound.ReferenceDocNo);
            Assert.Equal(stockInQuantity - dispenseQuantity, outbound.BalanceAfter);

            var claimCount = await conn.ExecuteScalarAsync<int>(
                "SELECT count(*) FROM phm_dispense_order_lines WHERE medical_order_id = @MedicalOrderId",
                new { MedicalOrderId = medicalOrderId });
            Assert.Equal(1, claimCount);
        }
    }

    [Fact]
    public async Task InpatientDischarge_SettleCreatesRefundAndReleasesBed()
    {
        string suffix = Guid.NewGuid().ToString("N")[..10];
        string patientId = "PAT-IPD-DISCHARGE-" + suffix;
        string encounterId = Guid.NewGuid().ToString("D");
        string bedId = Guid.NewGuid().ToString("D");
        string bedCode = "D" + suffix[..6];
        string admissionNo = "ADMD" + suffix;

        using (var scope = _factory.Services.CreateScope())
        {
            var patientRepo = scope.ServiceProvider.GetRequiredService<IRepository<PatPatient>>();
            var encounterRepo = scope.ServiceProvider.GetRequiredService<IRepository<IpdEncounter>>();
            var bedRepo = scope.ServiceProvider.GetRequiredService<IRepository<IpdBed>>();
            var chargeRepo = scope.ServiceProvider.GetRequiredService<IRepository<FinChargeLedger>>();
            var emrRepo = scope.ServiceProvider.GetRequiredService<IRepository<EmrDocument>>();

            await patientRepo.InsertAsync(new PatPatient
            {
                Id = patientId,
                MedicalRecordNo = "MRD" + suffix,
                FullName = "出院结算测试患者",
                GenderCode = "1",
                BirthDateInt = 19800101,
                IdCardHash = "HASH-" + suffix,
                IsActive = 1,
                CreatedBy = "USER-DOC-ATTENDING"
            });
            await bedRepo.InsertAsync(new IpdBed
            {
                Id = bedId,
                CampusId = "CAMPUS-01",
                WardId = "DEPT-WARD-01",
                RoomCode = "TEST-" + suffix[..4],
                BedCode = bedCode,
                BedStatus = 1,
                CurrentEncounterId = encounterId,
                CreatedBy = "USER-NURSE-W1"
            });
            await encounterRepo.InsertAsync(new IpdEncounter
            {
                Id = encounterId,
                AdmissionNo = admissionNo,
                PatientId = patientId,
                CampusId = "CAMPUS-01",
                AdmitDeptId = "DEPT-IPD-01",
                AdmitWardId = "DEPT-WARD-01",
                BedCode = bedCode,
                ActiveBedToken = Guid.NewGuid().ToString("N"),
                AttendingDoctorId = "USER-DOC-ATTENDING",
                PrimaryNurseId = "USER-NURSE-W1",
                NursingLevelCode = "LEVEL_1",
                PrepaymentBalance = 500.00m,
                Status = 1,
                TriageStatus = 1,
                AcceptedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                AcceptedBy = "USER-NURSE-W1",
                CreatedBy = "USER-CASHIER-01"
            });
            await chargeRepo.InsertAsync(new FinChargeLedger
            {
                PatientId = patientId,
                VisitOrEncounterId = encounterId,
                OrderDeptId = "DEPT-IPD-01",
                PatientWardId = "DEPT-WARD-01",
                ExecDeptId = "DEPT-IPD-01",
                ItemCode = "BED_STD_DAILY",
                ItemName = "普通病房床位费",
                Quantity = 1,
                UnitPrice = 200.00m,
                TotalAmount = 200.00m,
                Status = 1,
                CreatedBy = "USER-DOC-ATTENDING"
            });
            await emrRepo.InsertAsync(new EmrDocument
            {
                DocNo = "DOC-IPD-ADM-" + suffix,
                EmrScene = "IPD",
                VisitOrEncounterId = encounterId,
                PatientId = patientId,
                DeptId = "DEPT-IPD-01",
                WardId = "DEPT-WARD-01",
                TemplateId = "TMPL-IPD-ADMISSION",
                DocTypeCode = "IPD_ADMISSION",
                DocTitle = "住院入院记录",
                Status = 2,
                AuthorId = "USER-DOC-RESIDENT",
                LatestOfdArchiveId = "OFD-ADM-" + suffix,
                CreatedBy = "USER-DOC-RESIDENT"
            });
        }

        using HttpClient doctor = await CreateAuthenticatedClientAsync(
            "doc_res", "IPD_RESIDENT", "DEPT-IPD-01");
        using HttpClient cashier = await CreateAuthenticatedClientAsync(
            "cashier_01", "CASHIER", "DEPT-FIN-01");

        var closeBed = await cashier.PostAsync(
            "/api/v4/ipd/encounters/" + Uri.EscapeDataString(encounterId) + "/close-ward-bed", null);
        var closeBedBody = await closeBed.Content.ReadFromJsonAsync<ApiResponse<IpdEncounter>>();
        Assert.Equal(409, closeBedBody?.Code);

        var createRequest = await doctor.PostAsJsonAsync(
            "/api/v4/ipd/encounters/" + Uri.EscapeDataString(encounterId) + "/discharge-request",
            new CreateIpdDischargeRequest
            {
                DischargeDiagnosis = "冠状动脉粥样硬化性心脏病",
                DischargeInstructions = "按时服药，一周后门诊复诊"
            });
        var created = await createRequest.Content.ReadFromJsonAsync<ApiResponse<IpdDischargeRequestView>>();
        Assert.True(created?.Code == 200,
            $"Code={created?.Code}, Message={created?.Message}, EncounterDept=DEPT-IPD-01");
        Assert.Equal("PENDING", created?.Data?.Status);
        Assert.Contains(created!.Data!.ClinicalBlockers, x => x.Contains("出院记录"));
        string requestId = created.Data.Id;

        var duplicateRequest = await doctor.PostAsJsonAsync(
            "/api/v4/ipd/encounters/" + Uri.EscapeDataString(encounterId) + "/discharge-request",
            new CreateIpdDischargeRequest());
        var duplicate = await duplicateRequest.Content.ReadFromJsonAsync<ApiResponse<IpdDischargeRequestView>>();
        Assert.Equal(200, duplicate?.Code);
        Assert.Equal(requestId, duplicate?.Data?.Id);

        using (var scope = _factory.Services.CreateScope())
        {
            var emrRepo = scope.ServiceProvider.GetRequiredService<IRepository<EmrDocument>>();
            await emrRepo.InsertAsync(new EmrDocument
            {
                DocNo = "DOC-IPD-DIS-" + suffix,
                EmrScene = "IPD",
                VisitOrEncounterId = encounterId,
                PatientId = patientId,
                DeptId = "DEPT-IPD-01",
                WardId = "DEPT-WARD-01",
                TemplateId = "TMPL-IPD-DISCHARGE",
                DocTypeCode = "IPD_DISCHARGE_SUMMARY",
                DocTitle = "住院出院记录",
                Status = 2,
                AuthorId = "USER-DOC-RESIDENT",
                LatestOfdArchiveId = "OFD-DIS-" + suffix,
                CreatedBy = "USER-DOC-RESIDENT"
            });
        }

        var previewResponse = await cashier.GetAsync(
            "/api/v4/ipd/discharge-requests/" + Uri.EscapeDataString(requestId) + "/settlement-preview");
        var preview = await previewResponse.Content.ReadFromJsonAsync<ApiResponse<IpdSettlementPreviewView>>();
        Assert.Equal(200, preview?.Code);
        Assert.Empty(preview!.Data!.ClinicalBlockers);
        Assert.Equal(200.00m, preview.Data.ChargeTotal);
        Assert.Equal(requestId, preview.Data.RequestId);
        Assert.Equal(encounterId, preview.Data.EncounterId);
        Assert.Equal(patientId, preview.Data.PatientId);
        Assert.False(string.IsNullOrWhiteSpace(preview.Data.PreviewVersion));

        var stale = await cashier.PostAsJsonAsync($"/api/v4/ipd/discharge-requests/{requestId}/settle",
            new SettleIpdDischargeRequest
            {
                TotalAmount = 200m, InsuranceAmount = 100m, PrepaymentDeduct = 50m,
                SelfPayAmount = 50m, RefundAmount = 450m, PreviewVersion = "stale-preview"
            });
        Assert.Equal(400, (await stale.Content.ReadFromJsonAsync<ApiResponse<IpdDischargeRequestView>>())?.Code);

        string requestKey = "IPD-TEST-" + suffix;
        var settleResponse = await cashier.PostAsJsonAsync(
            "/api/v4/ipd/discharge-requests/" + Uri.EscapeDataString(requestId) + "/settle",
            new SettleIpdDischargeRequest
            {
                TotalAmount = 200.00m,
                InsuranceAmount = 100.00m,
                PrepaymentDeduct = 50.00m,
                SelfPayAmount = 50.00m,
                RefundAmount = 450.00m,
                PaymentChannel = "CASH",
                RequestKey = requestKey,
                PreviewVersion = preview.Data.PreviewVersion,
                ChargeItems = new List<IpdSettlementChargeView>
                {
                    new()
                    {
                        ItemCode = "BED_STD_DAILY",
                        ItemName = "普通病房床位费",
                        Quantity = 1,
                        UnitPrice = 200.00m,
                        TotalAmount = 200.00m
                    }
                }
            });
        var settled = await settleResponse.Content.ReadFromJsonAsync<ApiResponse<IpdDischargeRequestView>>();
        Assert.Equal(200, settled?.Code);
        Assert.Equal("SETTLED", settled?.Data?.Status);
        Assert.Equal(450.00m, settled?.Data?.RefundAmount);
        Assert.False(string.IsNullOrWhiteSpace(settled?.Data?.SettlementPaymentId));
        var settledPreview = await cashier.GetFromJsonAsync<ApiResponse<IpdSettlementPreviewView>>(
            $"/api/v4/ipd/discharge-requests/{requestId}/settlement-preview");
        Assert.Equal("SETTLED", settledPreview?.Data?.Status);
        Assert.Equal(requestId, settledPreview?.Data?.RequestId);
        Assert.Equal(200.00m, settledPreview?.Data?.ChargeTotal);

        using (var scope = _factory.Services.CreateScope())
        {
            var refundRepo = scope.ServiceProvider.GetRequiredService<IRepository<FinRefundOrder>>();
            var encounterRepo = scope.ServiceProvider.GetRequiredService<IRepository<IpdEncounter>>();
            var bedRepo = scope.ServiceProvider.GetRequiredService<IRepository<IpdBed>>();

            var refund = Assert.Single(await refundRepo.QueryAsync(
                "SELECT * FROM fin_refund_orders WHERE original_payment_id = @PaymentId",
                new { PaymentId = settled!.Data!.SettlementPaymentId }));
            Assert.Equal(450.00m, refund.RefundAmount);
            Assert.Equal((short)1, refund.DrugReturnConfirmed);

            IpdEncounter? closedEncounter = await encounterRepo.GetByIdAsync(encounterId);
            Assert.NotNull(closedEncounter);
            Assert.Equal(3, closedEncounter.Status);
            Assert.Equal(0m, closedEncounter.PrepaymentBalance);

            IpdBed? releasedBed = await bedRepo.GetByIdAsync(bedId);
            Assert.NotNull(releasedBed);
            Assert.Equal(0, releasedBed.BedStatus);
            Assert.Null(releasedBed.CurrentEncounterId);
        }

        var repeated = await cashier.PostAsJsonAsync(
            "/api/v4/ipd/discharge-requests/" + Uri.EscapeDataString(requestId) + "/settle",
            new SettleIpdDischargeRequest
            {
                TotalAmount = 200.00m,
                InsuranceAmount = 100.00m,
                PrepaymentDeduct = 50.00m,
                SelfPayAmount = 50.00m,
                RefundAmount = 450.00m,
                PaymentChannel = "CASH",
                RequestKey = requestKey
            });
        var repeatedBody = await repeated.Content.ReadFromJsonAsync<ApiResponse<IpdDischargeRequestView>>();
        Assert.Equal(200, repeatedBody?.Code);
        Assert.Contains("幂等命中", repeatedBody?.Message);

        var conflicting = await cashier.PostAsJsonAsync(
            "/api/v4/ipd/discharge-requests/" + Uri.EscapeDataString(requestId) + "/settle",
            new SettleIpdDischargeRequest
            {
                TotalAmount = 200.00m,
                InsuranceAmount = 100.00m,
                PrepaymentDeduct = 50.00m,
                SelfPayAmount = 50.00m,
                RefundAmount = 450.00m,
                PaymentChannel = "CASH",
                RequestKey = "IPD-OTHER-" + suffix
            });
        var conflictingBody = await conflicting.Content.ReadFromJsonAsync<ApiResponse<IpdDischargeRequestView>>();
        Assert.Equal(409, conflictingBody?.Code);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(
        string userName, string expectedRoleCode, string expectedDeptId)
    {
        HttpClient client = _factory.CreateClient();

        var contextsResponse = await client.GetAsync(
            "/api/v4/platform/auth/contexts?userName=" + Uri.EscapeDataString(userName));
        Assert.Equal(HttpStatusCode.OK, contextsResponse.StatusCode);
        var contexts = await contextsResponse.Content.ReadFromJsonAsync<
            ApiResponse<List<PlatformController.LoginContextDto>>>();
        Assert.Equal(200, contexts?.Code);

        PlatformController.LoginContextDto context = Assert.Single(
            contexts!.Data!,
            x =>
                (x.RoleCode.Equals(expectedRoleCode, StringComparison.OrdinalIgnoreCase)
                 || RoleCode.Normalize(x.RoleCode).Equals(RoleCode.Normalize(expectedRoleCode), StringComparison.OrdinalIgnoreCase))
                && x.DeptId.Equals(expectedDeptId, StringComparison.OrdinalIgnoreCase));

        var loginResponse = await client.PostAsJsonAsync(
            "/api/v4/platform/auth/login",
            new PlatformController.LoginRequestDto(userName, "123", ContextId: context.PostId));
        var login = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<PlatformController.LoginResponseDto>>();
        Assert.True(login?.Code == 200,
            $"Login failed for {userName}/{expectedRoleCode}: Code={login?.Code}, Message={login?.Message}");
        Assert.NotNull(login);
        Assert.NotNull(login.Data);
        Assert.Equal(context.PostId, login.Data.PostId);
        string actualRole = login.Data.RoleCode ?? string.Empty;
        Assert.True(actualRole.Equals(expectedRoleCode, StringComparison.OrdinalIgnoreCase) ||
                    RoleCode.Normalize(actualRole).Equals(RoleCode.Normalize(expectedRoleCode), StringComparison.OrdinalIgnoreCase));
        Assert.Equal(expectedDeptId, login.Data.ActiveDeptId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", login.Data.AccessToken);
        return client;
    }

    [Fact]
    public async Task Permission_DeniesUnauthorizedRole_ReturnsForbidden()
    {
        // 1. Create a client with CASHIER role
        var cashierClient = await CreateAuthenticatedClientAsync("cashier_01", "CASHIER", "DEPT-FIN-01");

        // 2. Try to access Admin-restricted endpoints:
        // A: GET /api/v4/operations/audit-logs
        var auditRes = await cashierClient.GetAsync("/api/v4/operations/audit-logs");
        Assert.Equal(HttpStatusCode.Forbidden, auditRes.StatusCode);

        // B: GET /api/v4/operations/audit-logs/verify-chain
        var verifyChainRes = await cashierClient.GetAsync("/api/v4/operations/audit-logs/verify-chain");
        Assert.Equal(HttpStatusCode.Forbidden, verifyChainRes.StatusCode);

        // C: POST /api/v4/platform/roles
        var createRoleRes = await cashierClient.PostAsJsonAsync("/api/v4/platform/roles", new
        {
            RoleCode = "HACKER_ROLE",
            RoleName = "黑客角色",
            DataScope = "ALL_CAMPUS",
            Description = "test"
        });
        Assert.Equal(HttpStatusCode.Forbidden, createRoleRes.StatusCode);

        // D: POST /api/v4/config/schemas
        var schemaRes = await cashierClient.PostAsJsonAsync("/api/v4/config/schemas", new
        {
            FormCode = "TEST-FORM",
            Version = 1,
            Fields = new List<object>()
        });
        Assert.Equal(HttpStatusCode.Forbidden, schemaRes.StatusCode);
    }

    [Fact]
    public async Task Idempotency_PreventsDuplicateFinanceSettle_And_PharmacyDispense()
    {
        var cashierClient = await CreateAuthenticatedClientAsync("cashier_01", "CASHIER", "DEPT-FIN-01");
        var pharmacistClient = await CreateAuthenticatedClientAsync("phm_user", "PHARMACIST", "DEPT-PHM-01");
        var doctorClient = await CreateAuthenticatedClientAsync("doc_opd", "OPD_DOC", "DEPT-OPD-01");

        string visitId = "VIS-IDEM-" + Guid.NewGuid().ToString("N")[..8];
        string requestKeySettle = "IDEM-SETTLE-" + Guid.NewGuid().ToString("N");
        string requestKeyDispense = "IDEM-DISP-" + Guid.NewGuid().ToString("N");
        await EnsureTestVisitAsync(visitId, "PAT85061801");

        // 1. Create prescription
        var rxRes = await doctorClient.PostAsJsonAsync("/api/v4/opd/prescriptions", new OutpatientController.CreatePrescriptionRequestDto(
            VisitId: visitId,
            PatientId: "PAT85061801",
            OpdDeptId: "DEPT-OPD-01",
            DoctorId: "USER-DOC-OPD",
            TargetPharmacyId: "DEPT-PHM-01",
            Items: new List<OpdPrescriptionItem>
            {
                new()
                {
                    GroupNo = 1, DrugId = "DRUG-CARD-006",
                    DrugNameSnapshot = "阿司匹林肠溶片", DoseOnce = 100m, DoseUnit = "mg",
                    FrequencyCode = "QD", RouteCode = "PO", Days = 3,
                    TotalQuantity = 1, UnitPrice = 22.50m
                }
            }));
        Assert.True(rxRes.IsSuccessStatusCode);
        var rx = (await rxRes.Content.ReadFromJsonAsync<ApiResponse<OpdPrescription>>())!.Data!;

        // 2. Pharmacist reviews prescription
        var reviewRes = await pharmacistClient.PostAsJsonAsync($"/api/v4/opd/prescriptions/{rx.Id}/review", new
        {
            Status = 1,
            ReviewRemark = "审方合格"
        });
        Assert.True(reviewRes.IsSuccessStatusCode);

        // 3. Settle payment with RequestKey
        var settleReq = new FinanceController.SettlePaymentRequestDto(
            Scene: "OPD",
            PatientId: "PAT85061801",
            VisitOrEncounterId: visitId,
            TotalAmount: 22.50m,
            InsuranceAmount: 0m,
            PrepaymentDeduct: 0m,
            SelfPayAmount: 22.50m,
            PaymentChannel: "CASH",
            ChargeItems: null,
            RequestKey: requestKeySettle);

        var firstSettleRes = await cashierClient.PostAsJsonAsync("/api/v4/finance/settle", settleReq);
        Assert.True(firstSettleRes.IsSuccessStatusCode);
        var firstPayment = (await firstSettleRes.Content.ReadFromJsonAsync<ApiResponse<FinPaymentOrder>>())!.Data!;

        // Replay identical settle request -> should hit idempotency
        var replaySettleRes = await cashierClient.PostAsJsonAsync("/api/v4/finance/settle", settleReq);
        Assert.True(replaySettleRes.IsSuccessStatusCode);
        var replayedPayment = (await replaySettleRes.Content.ReadFromJsonAsync<ApiResponse<FinPaymentOrder>>())!.Data!;
        Assert.Equal(firstPayment.Id, replayedPayment.Id);
        Assert.Equal(firstPayment.PaymentNo, replayedPayment.PaymentNo);

        // 4. Pharmacist dispenses with RequestKey
        var dispenseReq = new PharmacyController.DispensePrescriptionRequestDto(
            PharmacyId: "DEPT-PHM-01",
            PrescriptionId: rx.Id,
            PatientId: "PAT85061801",
            PharmacistId: "USER-PHM-01",
            RequestKey: requestKeyDispense);

        var firstDispenseRes = await pharmacistClient.PostAsJsonAsync("/api/v4/pharmacy/dispense", dispenseReq);
        Assert.True(firstDispenseRes.IsSuccessStatusCode);
        var firstDispense = (await firstDispenseRes.Content.ReadFromJsonAsync<ApiResponse<PhmDispenseOrder>>())!.Data!;

        // Replay identical dispense request -> should return cached dispense order, NOT double deduct or fail 409
        var replayDispenseRes = await pharmacistClient.PostAsJsonAsync("/api/v4/pharmacy/dispense", dispenseReq);
        Assert.True(replayDispenseRes.IsSuccessStatusCode);
        var replayedDispense = (await replayDispenseRes.Content.ReadFromJsonAsync<ApiResponse<PhmDispenseOrder>>())!.Data!;
        Assert.Equal(firstDispense.Id, replayedDispense.Id);
        Assert.Equal(firstDispense.DispenseNo, replayedDispense.DispenseNo);
    }

    [Fact]
    public async Task DemoAccounts_AllAuthenticateWithDevelopmentPassword()
    {
        string[] userNames =
        [
            "admin",
            "doc_opd",
            "doc_res",
            "doc_att",
            "nurse_w1",
            "nurse_w2",
            "phm_user",
            "cashier_01"
        ];

        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            var rows = (await conn.QueryAsync<(string UserName, string PasswordHash)>(
                "SELECT user_name, password_hash FROM sys_users WHERE user_name IN @UserNames",
                new { UserNames = userNames })).ToList();

            Assert.Equal(userNames.Length, rows.Count);
            Assert.All(rows, row =>
            {
                Assert.StartsWith("PBKDF2$", row.PasswordHash);
                Assert.True(PasswordSecurityHelper.VerifyPassword("123", row.PasswordHash).IsValid);
            });
        }

        foreach (string userName in userNames)
        {
            using HttpClient client = _factory.CreateClient();
            var contextsResponse = await client.GetAsync(
                "/api/v4/platform/auth/contexts?userName=" + Uri.EscapeDataString(userName));
            Assert.Equal(HttpStatusCode.OK, contextsResponse.StatusCode);
            var contexts = await contextsResponse.Content.ReadFromJsonAsync<
                ApiResponse<List<PlatformController.LoginContextDto>>>();
            Assert.True(contexts?.Code == 200, $"Context lookup failed for {userName}: {contexts?.Message}");
            Assert.NotEmpty(contexts!.Data!);

            foreach (PlatformController.LoginContextDto context in contexts.Data!)
            {
                var loginResponse = await client.PostAsJsonAsync(
                    "/api/v4/platform/auth/login",
                    new PlatformController.LoginRequestDto(userName, "123", ContextId: context.PostId));
                var login = await loginResponse.Content.ReadFromJsonAsync<
                    ApiResponse<PlatformController.LoginResponseDto>>();
                Assert.True(login?.Code == 200,
                    $"Login failed for {userName} / {context.RoleCode} / {context.DeptId}: {login?.Message}");
                Assert.Equal(context.PostId, login!.Data!.PostId);
            }
        }
    }

    [Fact]
    public async Task Password_ChangePassword_And_InvalidateExistingTokens()
    {
        // 1. Authenticate as doctor
        var doctorClient = await CreateAuthenticatedClientAsync("doc_opd", "OPD_DOC", "DEPT-OPD-01");

        // 2. Change password
        var changeRes = await doctorClient.PostAsJsonAsync("/api/v4/platform/users/USER-DOC-OPD/change-password", new
        {
            OldPassword = "123",
            NewPassword = "NewDocPassword@2026!"
        });
        Assert.True(changeRes.IsSuccessStatusCode);

        // 3. Old token should now be rejected (version mismatch -> 401)
        var oldTokenCall = await doctorClient.GetAsync("/api/v4/opd/queue");
        Assert.Equal(HttpStatusCode.Unauthorized, oldTokenCall.StatusCode);

        // 4. Old password login should fail
        var oldLoginRes = await _client.PostAsJsonAsync("/api/v4/platform/auth/login", new PlatformController.LoginRequestDto(
            "doc_opd", "123", ContextId: "UDP-DOC-OPD-OPD"));
        Assert.Equal(HttpStatusCode.Unauthorized, oldLoginRes.StatusCode);

        // 5. New password login succeeds
        var newLoginRes = await _client.PostAsJsonAsync("/api/v4/platform/auth/login", new PlatformController.LoginRequestDto(
            "doc_opd", "NewDocPassword@2026!", ContextId: "UDP-DOC-OPD-OPD"));
        Assert.True(newLoginRes.IsSuccessStatusCode);

        // Reset password back to the development default for other tests
        var resetLogin = (await newLoginRes.Content.ReadFromJsonAsync<ApiResponse<PlatformController.LoginResponseDto>>())!.Data!;
        var resetClient = _factory.CreateClient();
        resetClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", resetLogin.AccessToken);
        await resetClient.PostAsJsonAsync("/api/v4/platform/users/USER-DOC-OPD/change-password", new
        {
            OldPassword = "NewDocPassword@2026!",
            NewPassword = "123"
        });
    }

    [Fact]
    public async Task OutpatientPrescription_PreCheck_Api_EvaluatesSafetyRulesCorrectly()
    {
        var doctorClient = await CreateAuthenticatedClientAsync("doc_opd", "OPD_DOC", "DEPT-OPD-01");

        // 1. Valid safe item: Aspirin 100mg for clean patient PAT-PHM-DEMO-01
        var passPayload = new
        {
            PatientId = "PAT-PHM-DEMO-01",
            PrescribeLevel = 2,
            Items = new[]
            {
                new
                {
                    DrugId = "DRUG-04", // 阿司匹林肠溶片
                    DoseOnce = 100m,
                    DoseUnit = "mg",
                    FrequencyCode = "QD",
                    RouteCode = "PO",
                    Days = 7,
                    TotalQuantity = 1,
                    SkinTestFlag = 0
                }
            }
        };

        var passRes = await doctorClient.PostAsJsonAsync("/api/v4/opd/prescriptions/pre-check", passPayload);
        Assert.True(passRes.IsSuccessStatusCode);
        var passData = await passRes.Content.ReadFromJsonAsync<ApiResponse<HIS.Modules.Outpatient.Models.PrescriptionReviewResultDto>>();
        Assert.NotNull(passData);
        Assert.Equal(200, passData.Code);
        Assert.Equal("PASS", passData.Data?.OverallStatus);
        Assert.True(passData.Data?.CanProceed);

        // 2. Overdose block: Aspirin 500mg (> 300mg limit)
        var overdosePayload = new
        {
            PatientId = "PAT-PHM-DEMO-01",
            PrescribeLevel = 2,
            Items = new[]
            {
                new
                {
                    DrugId = "DRUG-04",
                    DoseOnce = 500m,
                    DoseUnit = "mg",
                    FrequencyCode = "QD",
                    RouteCode = "PO",
                    Days = 3,
                    TotalQuantity = 1,
                    SkinTestFlag = 0
                }
            }
        };

        var overdoseRes = await doctorClient.PostAsJsonAsync("/api/v4/opd/prescriptions/pre-check", overdosePayload);
        Assert.True(overdoseRes.IsSuccessStatusCode);
        var overdoseData = await overdoseRes.Content.ReadFromJsonAsync<ApiResponse<HIS.Modules.Outpatient.Models.PrescriptionReviewResultDto>>();
        Assert.NotNull(overdoseData);
        Assert.Equal(200, overdoseData.Code);
        Assert.Equal("BLOCK", overdoseData.Data?.OverallStatus);
        Assert.False(overdoseData.Data?.CanProceed);
        Assert.Contains(overdoseData.Data?.Issues ?? new(), x => x.RuleCode == "RULE_DOSE_LIMIT_ASPIRIN");

        // 3. Positive skin test block: Penicillin (SkinTestFlag = 4)
        var skinTestPayload = new
        {
            PatientId = "PAT-PHM-DEMO-01",
            PrescribeLevel = 2,
            Items = new[]
            {
                new
                {
                    DrugId = "DRUG-01", // 注射用青霉素钠
                    DoseOnce = 800000m,
                    DoseUnit = "单位",
                    FrequencyCode = "QD",
                    RouteCode = "IV",
                    Days = 1,
                    TotalQuantity = 1,
                    SkinTestFlag = 4 // Positive
                }
            }
        };

        var skinTestRes = await doctorClient.PostAsJsonAsync("/api/v4/opd/prescriptions/pre-check", skinTestPayload);
        Assert.True(skinTestRes.IsSuccessStatusCode);
        var skinTestData = await skinTestRes.Content.ReadFromJsonAsync<ApiResponse<HIS.Modules.Outpatient.Models.PrescriptionReviewResultDto>>();
        Assert.NotNull(skinTestData);
        Assert.Equal("BLOCK", skinTestData.Data?.OverallStatus);
        Assert.Contains(skinTestData.Data?.Issues ?? new(), x => x.RuleCode == "RULE_SKIN_TEST_POSITIVE");

        // 4. Allergy conflict block: patient with Penicillin allergy tries to prescribe Amoxicillin DRUG-02
        var allergyPatientId = "PAT-ALLERGY-" + Guid.NewGuid().ToString("N")[..8];
        using (var scope = _factory.Services.CreateScope())
        {
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await conn.ExecuteAsync(@"
                INSERT INTO pat_patients (id, medical_record_no, full_name, pinyin_code, gender_code, birth_date_int, id_card_type, id_card_cipher, id_card_hash, phone_masked, allergy_tags_json, is_temp_emergency, is_active, schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
                VALUES (@PatientId, 'MRN-' || @PatientId, '过敏测试者', 'GMCS', '1', 19900101, 'ID_CARD', NULL, 'HASH-' || @PatientId, '138****0000', '[""青霉素过敏""]', 0, 1, 1, @Now, 'TEST', @Now, 'TEST', 1)",
                new { PatientId = allergyPatientId, Now = now });
        }

        var allergyPayload = new
        {
            PatientId = allergyPatientId,
            PrescribeLevel = 2,
            Items = new[]
            {
                new
                {
                    DrugId = "DRUG-02", // 阿莫西林胶囊
                    DoseOnce = 250m,
                    DoseUnit = "mg",
                    FrequencyCode = "TID",
                    RouteCode = "PO",
                    Days = 3,
                    TotalQuantity = 1,
                    SkinTestFlag = 3 // Negative skin test, isolates allergy rule
                }
            }
        };

        var allergyRes = await doctorClient.PostAsJsonAsync("/api/v4/opd/prescriptions/pre-check", allergyPayload);
        Assert.True(allergyRes.IsSuccessStatusCode);
        var allergyData = await allergyRes.Content.ReadFromJsonAsync<ApiResponse<HIS.Modules.Outpatient.Models.PrescriptionReviewResultDto>>();
        Assert.NotNull(allergyData);
        Assert.Equal("BLOCK", allergyData.Data?.OverallStatus);
        Assert.Contains(allergyData.Data?.Issues ?? new(), x => x.RuleCode == "RULE_ALLERGY");
    }
}
