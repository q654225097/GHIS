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
using HIS.Contracts;
using HIS.Modules.Emr.Controllers;
using HIS.Modules.Emr.Models;
using HIS.Modules.Finance.Controllers;
using HIS.Modules.Finance.Models;
using HIS.Modules.Inventory.Controllers;
using HIS.Modules.Inventory.Models;
using HIS.Modules.MedTech.Controllers;
using HIS.Modules.MedTech.Models;
using HIS.Modules.Operations.Models;
using HIS.Modules.Operations.Services;
using HIS.Modules.Outpatient.Controllers;
using HIS.Modules.Outpatient.Models;
using HIS.Modules.Patient.Controllers;
using HIS.Modules.Patient.Models;
using HIS.Modules.Platform.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace HIS.IntegrationTests;

public class ComplianceRemediationVerificationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly WebApplicationFactory<Program> _factory;

    public ComplianceRemediationVerificationTests(WebApplicationFactory<Program> factory)
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

        var jwt = scope.ServiceProvider.GetRequiredService<BuildingBlocks.Security.JwtTokenService>();
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

    private Task<HttpClient> CreateDoctorClientAsync()
    {
        HttpClient client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        using var conn = connFactory.CreateConnection();
        var sessionVersion = conn.ExecuteScalar<int>(
            "SELECT session_version FROM sys_users WHERE id = 'USER-DOC-OPD'");

        var jwt = scope.ServiceProvider.GetRequiredService<BuildingBlocks.Security.JwtTokenService>();
        var token = jwt.GenerateAccessToken(
            userId: "USER-DOC-OPD",
            userName: "doc_opd",
            displayName: "张建国 (主治医师)",
            campusId: "CAMPUS-01",
            activeDeptCategory: DepartmentCategory.OPD,
            activeDeptId: "DEPT-OPD-01",
            activeWardId: null,
            linkedWardIds: null,
            roles: new[] { "ROLE-OPD-DOC", "OPD_DOC", "DOCTOR" },
            dataScope: DataScope.OPD_DEPT_SCOPE,
            sessionVersion: sessionVersion,
            permHash: "ALL",
            prescribeLevel: 2,
            surgeryLevel: 2,
            emrSignLevel: 2,
            activePostId: "UDP-DOC-OPD-OPD",
            roleId: "ROLE-OPD-DOC",
            roleCode: "OPD_DOC",
            roleName: "门诊主治医师",
            activeDeptName: "心血管内科门诊");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return Task.FromResult(client);
    }

    [Fact]
    public async Task F011_EmrAdminEndpoints_MustRejectNonAdminCallers()
    {
        using HttpClient doctorClient = await CreateDoctorClientAsync();

        // 1. Data element creation without admin role -> 403
        var deRes = await doctorClient.PostAsJsonAsync("/api/v4/emr/data-elements", new
        {
            ElementCode = "DE_TEST_DENIED",
            ElementName = "越权测试数据元"
        });
        Assert.Equal(HttpStatusCode.Forbidden, deRes.StatusCode);

        // 2. Layout upsert without admin role -> 403
        var layoutRes = await doctorClient.PostAsJsonAsync("/api/v4/emr/layouts", new
        {
            LayoutCode = "LAYOUT_TEST_DENIED",
            LayoutName = "越权测试版式"
        });
        Assert.Equal(HttpStatusCode.Forbidden, layoutRes.StatusCode);

        // 3. Template design without admin role -> 403
        var tplRes = await doctorClient.PostAsJsonAsync("/api/v4/emr/templates/design", new
        {
            TemplateCode = "TPL_TEST_DENIED",
            TemplateName = "越权测试模板",
            EmrScene = "OPD"
        });
        Assert.Equal(HttpStatusCode.Forbidden, tplRes.StatusCode);

        // 4. Admin caller succeeds
        var adminDeRes = await _client.PostAsJsonAsync("/api/v4/emr/data-elements", new
        {
            ElementCode = "DE_TEST_ALLOWED",
            ElementName = "管理员合规数据元"
        });
        Assert.Equal(HttpStatusCode.OK, adminDeRes.StatusCode);
    }

    [Fact]
    public async Task F011_EmrSign_ProxySignatureAttempt_MustBeRejectedAndAudited()
    {
        using HttpClient doctorClient = await CreateDoctorClientAsync();

        // Doctor attempting to sign with another user's ID
        var signRes = await doctorClient.PostAsJsonAsync(
            "/api/v4/emr/documents/DOC-NON-EXISTENT/sign-and-generate-ofd",
            new EmrController.SignEmrRequestDto("USER-FORGED-ID", "伪造医生姓名", 1));

        Assert.Equal(HttpStatusCode.Forbidden, signRes.StatusCode);
        var body = await signRes.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Contains("禁止代签", body?.Message);
    }

    [Fact]
    public async Task F018_PatientPii_MaskedForDoctor_AndAuditedDecryption()
    {
        // 1. Register a test patient via admin
        var regRes = await _client.PostAsJsonAsync("/api/v4/patients/register", new
        {
            FullName = "合规隐私测试患者",
            GenderCode = "1",
            BirthDateInt = 19920512,
            IdCardType = "RESIDENT_ID",
            IdCardNo = "110101199205121234",
            Phone = "13912345678"
        });
        Assert.Equal(HttpStatusCode.OK, regRes.StatusCode);
        var regData = await regRes.Content.ReadFromJsonAsync<ApiResponse<PatPatient>>();
        Assert.NotNull(regData?.Data?.Id);
        string patientId = regData.Data.Id;

        // 2. Doctor queries patient -> raw ciphers and hashes are sanitized
        using HttpClient doctorClient = await CreateDoctorClientAsync();
        var getRes = await doctorClient.GetAsync($"/api/v4/patients/{patientId}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var getData = await getRes.Content.ReadFromJsonAsync<ApiResponse<PatPatient>>();
        Assert.NotNull(getData?.Data);
        Assert.True(string.IsNullOrEmpty(getData.Data.IdCardCipher), "IdCardCipher must be masked for non-admin");
        Assert.True(string.IsNullOrEmpty(getData.Data.PhoneCipher), "PhoneCipher must be masked for non-admin");
        Assert.True(string.IsNullOrEmpty(getData.Data.IdCardHash), "IdCardHash must be hidden for non-admin");

        // 3. Decrypt PII with justification
        var decryptRes = await doctorClient.PostAsJsonAsync($"/api/v4/patients/{patientId}/decrypt-pii", new
        {
            FieldName = "ID_CARD",
            Reason = "医保特病审核核验身份"
        });
        string decryptBody = await decryptRes.Content.ReadAsStringAsync();
        Assert.True(decryptRes.IsSuccessStatusCode, $"Decrypt failed: Status={decryptRes.StatusCode}, Body={decryptBody}");
        var decryptData = await decryptRes.Content.ReadFromJsonAsync<ApiResponse<string>>();
        Assert.Equal("110101199205121234", decryptData?.Data);
    }

    [Fact]
    public async Task F015_DrugTraceCodes_ScanIn_Dispense_And_Recall()
    {
        string traceCode = "TRC-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

        // 1. Scan in
        var scanInRes = await _client.PostAsJsonAsync("/api/v4/inventory/trace-codes/scan-in", new InventoryController.ScanInTraceCodeRequestDto(
            WarehouseOrPharmacyId: "DEPT-WH-01",
            DrugId: "DRUG-01",
            BatchId: "BATCH-01",
            BatchNo: "B20261117-01",
            NationalInsCode: "869000000001",
            TraceCodes: new[] { traceCode }
        ));
        Assert.Equal(HttpStatusCode.OK, scanInRes.StatusCode);

        // 2. Query
        var queryRes = await _client.GetAsync($"/api/v4/inventory/trace-codes?traceCode={traceCode}");
        Assert.Equal(HttpStatusCode.OK, queryRes.StatusCode);
        var queryData = await queryRes.Content.ReadFromJsonAsync<ApiResponse<List<InvDrugTraceCode>>>();
        Assert.NotEmpty(queryData!.Data!);
        Assert.Equal("IN_STOCK", queryData.Data![0].Status);

        // 3. Scan dispense
        var dispRes = await _client.PostAsJsonAsync("/api/v4/inventory/trace-codes/scan-dispense", new InventoryController.ScanDispenseTraceCodeRequestDto(
            DispenseOrderId: "DSP-001",
            DispenseOrderLineId: "LINE-001",
            TraceCodes: new[] { traceCode }
        ));
        Assert.Equal(HttpStatusCode.OK, dispRes.StatusCode);

        // 4. Recall
        var recallRes = await _client.PostAsJsonAsync($"/api/v4/inventory/trace-codes/{traceCode}/recall", new { });
        Assert.Equal(HttpStatusCode.OK, recallRes.StatusCode);

        var verifyRecall = await _client.GetAsync($"/api/v4/inventory/trace-codes?traceCode={traceCode}");
        var verifyData = await verifyRecall.Content.ReadFromJsonAsync<ApiResponse<List<InvDrugTraceCode>>>();
        Assert.Equal("RECALLED", verifyData!.Data![0].Status);
        Assert.Equal((short)1, verifyData.Data[0].IsRecalled);
    }

    [Fact]
    public async Task F015_StockTake_Create_AddLines_And_Approve()
    {
        // 1. Create stock take
        var createRes = await _client.PostAsJsonAsync("/api/v4/inventory/stock-takes", new InventoryController.CreateStockTakeRequestDto(
            WarehouseOrPharmacyId: "DEPT-WH-01",
            DeptName: "全院中心药库",
            StockTakeType: "MONTHLY",
            Remark: "2026年11月例行盘点"
        ));
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);
        var createData = await createRes.Content.ReadFromJsonAsync<ApiResponse<InvStockTake>>();
        Assert.NotNull(createData?.Data?.Id);
        string takeId = createData.Data.Id;

        // 2. Add line
        var lineRes = await _client.PostAsJsonAsync($"/api/v4/inventory/stock-takes/{takeId}/lines", new InventoryController.AddStockTakeLineRequestDto(
            DrugId: "DRUG-01",
            BatchId: "BATCH-01",
            DrugCode: "DRUG_AMOX",
            DrugName: "阿莫西林胶囊",
            Spec: "0.25g*24粒/盒",
            UnitName: "盒",
            BatchNo: "B20261101",
            SystemQty: 100,
            ActualQty: 98,
            UnitCostPrice: 12.50m,
            Reason: "自然挥发损耗"
        ));
        Assert.Equal(HttpStatusCode.OK, lineRes.StatusCode);

        // 3. Detail
        var detailRes = await _client.GetAsync($"/api/v4/inventory/stock-takes/{takeId}");
        Assert.Equal(HttpStatusCode.OK, detailRes.StatusCode);
        var detailData = await detailRes.Content.ReadFromJsonAsync<ApiResponse<InventoryController.StockTakeDetailDto>>();
        Assert.Equal(-2, detailData?.Data?.Header.TotalDiffQty);
        Assert.Single(detailData!.Data!.Lines);

        // 4. Approve
        var appRes = await _client.PostAsJsonAsync($"/api/v4/inventory/stock-takes/{takeId}/approve", new InventoryController.ApproveStockTakeRequestDto("盘点审核通过"));
        Assert.Equal(HttpStatusCode.OK, appRes.StatusCode);
        var appData = await appRes.Content.ReadFromJsonAsync<ApiResponse<InvStockTake>>();
        Assert.Equal("APPROVED", appData?.Data?.Status);
    }

    [Fact]
    public async Task F015_FinInpatientFeeItem_Record_And_DailyStatement()
    {
        string encounterId = "ENC-TEST-" + Guid.NewGuid().ToString("N")[..8];
        string patientId = "PAT-TEST-01";

        // 1. Record fee items
        var itemRes1 = await _client.PostAsJsonAsync("/api/v4/finance/inpatient-fee-items", new FinanceController.RecordInpatientFeeItemRequestDto(
            EncounterId: encounterId,
            PatientId: patientId,
            FeeDateInt: 20261117,
            ItemCategory: "MEDICINE",
            ItemCode: "MED_01",
            ItemName: "氯化钠注射液",
            Spec: "500ml",
            UnitName: "瓶",
            UnitPrice: 6.50m,
            Quantity: 2m,
            SelfPayRatio: 0.1m,
            ReimburseType: "CLASS_A",
            ChargeDeptId: "DEPT-WARD-01",
            ExecDeptId: "DEPT-PHM-02"
        ));
        Assert.Equal(HttpStatusCode.OK, itemRes1.StatusCode);

        var itemRes2 = await _client.PostAsJsonAsync("/api/v4/finance/inpatient-fee-items", new FinanceController.RecordInpatientFeeItemRequestDto(
            EncounterId: encounterId,
            PatientId: patientId,
            FeeDateInt: 20261117,
            ItemCategory: "BED",
            ItemCode: "BED_STD",
            ItemName: "标准床位费",
            Spec: null,
            UnitName: "日",
            UnitPrice: 60.00m,
            Quantity: 1m,
            SelfPayRatio: 0m,
            ReimburseType: "CLASS_A",
            ChargeDeptId: "DEPT-WARD-01",
            ExecDeptId: "DEPT-WARD-01"
        ));
        Assert.Equal(HttpStatusCode.OK, itemRes2.StatusCode);

        // 2. Query fee items
        var queryRes = await _client.GetAsync($"/api/v4/finance/inpatient-fee-items/{encounterId}");
        Assert.Equal(HttpStatusCode.OK, queryRes.StatusCode);
        var queryData = await queryRes.Content.ReadFromJsonAsync<ApiResponse<List<FinInpatientFeeItem>>>();
        Assert.Equal(2, queryData?.Data?.Count);

        // 3. Daily statement
        var stmtRes = await _client.GetAsync($"/api/v4/finance/inpatient-fee-items/{encounterId}/daily-statement");
        Assert.Equal(HttpStatusCode.OK, stmtRes.StatusCode);
        var stmtData = await stmtRes.Content.ReadFromJsonAsync<ApiResponse<List<FinanceController.DailyStatementItemDto>>>();
        Assert.Single(stmtData!.Data!);
        Assert.Equal(73.00m, stmtData.Data![0].TotalAmount);
    }

    [Fact]
    public async Task F015_OpdInfectiousReportCard_Create_And_SyncCdc()
    {
        // 1. Create card
        var createRes = await _client.PostAsJsonAsync("/api/v4/opd/infectious-report-cards", new OutpatientController.CreateInfectiousReportCardRequestDto(
            VisitId: "VISIT-01",
            PatientId: "PAT-01",
            IcdCode: "A00.0",
            DiseaseName: "古典生物型霍乱",
            DiseaseCategory: "CLASS_A",
            DiagnoseType: "CONFIRMED",
            OnsetDateInt: 20261116,
            DiagnosedDateInt: 20261117
        ));
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);
        var createData = await createRes.Content.ReadFromJsonAsync<ApiResponse<OpdInfectiousReportCard>>();
        Assert.NotNull(createData?.Data?.Id);
        string cardId = createData.Data.Id;
        Assert.Equal("PENDING", createData.Data.CdcSyncStatus);

        // 2. Sync CDC
        var syncRes = await _client.PostAsJsonAsync($"/api/v4/opd/infectious-report-cards/{cardId}/sync-cdc", new { });
        Assert.Equal(HttpStatusCode.OK, syncRes.StatusCode);
        var syncData = await syncRes.Content.ReadFromJsonAsync<ApiResponse<OpdInfectiousReportCard>>();
        Assert.Equal("SYNCED", syncData?.Data?.CdcSyncStatus);
    }

    [Fact]
    public async Task F015_EmrFrontSheet_Save_Get_And_Submit()
    {
        string encounterId = "ENC-FS-" + Guid.NewGuid().ToString("N")[..8];

        // 1. Save Front Sheet
        var saveRes = await _client.PostAsJsonAsync("/api/v4/emr/front-sheets", new EmrController.SaveFrontSheetRequestDto(
            EncounterId: encounterId,
            PatientId: "PAT-01",
            MedicalRecordNo: "MRN-2026-001",
            AdmissionTimes: 1,
            PayTypeCode: "SOCIAL_SECURITY",
            HealthCardNo: "HC99887766",
            AdmitDeptId: "DEPT-IPD-01",
            AdmitWardId: "DEPT-WARD-01",
            AdmitTimeMs: DateTimeOffset.UtcNow.AddDays(-7).ToUnixTimeMilliseconds(),
            DischargeDeptId: "DEPT-IPD-01",
            DischargeWardId: "DEPT-WARD-01",
            DischargeTimeMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            InpatientDays: 7,
            OutpatientDiagCode: "I25.101",
            OutpatientDiagName: "冠状动脉粥样硬化性心脏病",
            PrimaryDiagCode: "I25.101",
            PrimaryDiagName: "冠状动脉粥样硬化性心脏病",
            PrimaryAdmissionCondition: "SERIOUS",
            OtherDiagsJson: "[]",
            SurgeriesJson: "[]",
            TotalCost: 6500.00m,
            SelfPayAmount: 1200.00m,
            FeeBreakdownJson: null,
            ChiefPhysicianId: "USER-ADMIN",
            AttendingPhysicianId: "USER-DOC-IPD",
            ResidentPhysicianId: "USER-DOC-RES",
            PrimaryNurseId: "USER-NURSE-01"
        ));
        Assert.Equal(HttpStatusCode.OK, saveRes.StatusCode);

        // 2. Query
        var getRes = await _client.GetAsync($"/api/v4/emr/front-sheets/{encounterId}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var getData = await getRes.Content.ReadFromJsonAsync<ApiResponse<EmrInpatientFrontSheet>>();
        Assert.Equal("DRAFT", getData?.Data?.Status);

        // 3. Submit
        var submitRes = await _client.PostAsJsonAsync($"/api/v4/emr/front-sheets/{encounterId}/submit", new { });
        Assert.Equal(HttpStatusCode.OK, submitRes.StatusCode);
        var submitData = await submitRes.Content.ReadFromJsonAsync<ApiResponse<EmrInpatientFrontSheet>>();
        Assert.Equal("SUBMITTED", submitData?.Data?.Status);
        Assert.Equal("GRADE_A", submitData?.Data?.QualityLevel);
    }

    [Fact]
    public async Task M019_IntrusionDetection_ScanThreats_AlertsList_And_Resolve()
    {
        // 1. Posture check
        var postureRes = await _client.GetAsync("/api/v4/operations/security/posture");
        Assert.Equal(HttpStatusCode.OK, postureRes.StatusCode);
        var postureData = await postureRes.Content.ReadFromJsonAsync<ApiResponse<SecurityPostureDto>>();
        Assert.NotNull(postureData?.Data);
        Assert.True(postureData!.Data!.SecurityScore >= 10);
        Assert.Equal("COMPLIANT_L3", postureData.Data.DengbaoStatus);

        // 2. Scan threats
        var scanRes = await _client.PostAsync("/api/v4/operations/security/alerts/scan", null);
        Assert.Equal(HttpStatusCode.OK, scanRes.StatusCode);

        // 3. Query alerts
        var alertsRes = await _client.GetAsync("/api/v4/operations/security/alerts");
        Assert.Equal(HttpStatusCode.OK, alertsRes.StatusCode);
        var alertsData = await alertsRes.Content.ReadFromJsonAsync<ApiResponse<List<OpsSecurityAlert>>>();
        Assert.NotNull(alertsData?.Data);
        Assert.NotEmpty(alertsData!.Data!);

        // 4. Resolve an alert
        var targetAlert = alertsData.Data.First();
        var resolveRes = await _client.PostAsJsonAsync($"/api/v4/operations/security/alerts/{targetAlert.Id}/resolve", new ResolveAlertRequestDto("安全管理员已确认并处置威胁"));
        Assert.Equal(HttpStatusCode.OK, resolveRes.StatusCode);
        var resolveData = await resolveRes.Content.ReadFromJsonAsync<ApiResponse<OpsSecurityAlert>>();
        Assert.Equal("RESOLVED", resolveData?.Data?.Status);
    }

    [Fact]
    public async Task M021_KmsKeyManagement_QueryKeys_RotateKey_And_HsmStatus()
    {
        // 1. Query Keys
        var keysRes = await _client.GetAsync("/api/v4/operations/kms/keys");
        Assert.Equal(HttpStatusCode.OK, keysRes.StatusCode);
        var keysData = await keysRes.Content.ReadFromJsonAsync<ApiResponse<List<OpsKmsKey>>>();
        Assert.NotNull(keysData?.Data);
        Assert.True(keysData!.Data!.Count >= 4);

        var jwtKey = keysData.Data.First(k => k.KeyName == "JWT_SIGNING_KEY" && k.State == "ACTIVE");
        Assert.Equal((short)1, jwtKey.HsmProtected);
        int oldVersion = jwtKey.Version;

        // 2. Rotate Key
        var rotateRes = await _client.PostAsJsonAsync("/api/v4/operations/kms/keys/rotate", new KeyRotationRequestDto("JWT_SIGNING_KEY", "自动化轮换演练测试"));
        Assert.Equal(HttpStatusCode.OK, rotateRes.StatusCode);
        var rotateData = await rotateRes.Content.ReadFromJsonAsync<ApiResponse<OpsKmsKey>>();
        Assert.NotNull(rotateData?.Data);
        Assert.Equal(oldVersion + 1, rotateData!.Data!.Version);
        Assert.Equal("ACTIVE", rotateData.Data.State);

        // 3. Re-query to confirm previous key was retired
        var postRotateRes = await _client.GetAsync("/api/v4/operations/kms/keys");
        var postRotateData = await postRotateRes.Content.ReadFromJsonAsync<ApiResponse<List<OpsKmsKey>>>();
        var retiredKey = postRotateData?.Data?.FirstOrDefault(k => k.KeyName == "JWT_SIGNING_KEY" && k.Version == oldVersion);
        Assert.NotNull(retiredKey);
        Assert.Equal("RETIRED", retiredKey!.State);

        // 4. HSM Status check
        var hsmRes = await _client.GetAsync("/api/v4/operations/kms/hsm-status");
        Assert.Equal(HttpStatusCode.OK, hsmRes.StatusCode);
        var hsmData = await hsmRes.Content.ReadFromJsonAsync<ApiResponse<HsmStatusDto>>();
        Assert.NotNull(hsmData?.Data);
        Assert.Equal("ONLINE", hsmData!.Data!.State);
    }
}
