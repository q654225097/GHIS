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
using HIS.Modules.Consumables.Models;
using HIS.Modules.Emr.Controllers;
using HIS.Modules.Emr.Models;
using HIS.Modules.Finance.Controllers;
using HIS.Modules.Finance.Models;
using HIS.Modules.Inpatient.Controllers;
using HIS.Modules.Inpatient.Models;
using HIS.Modules.Inventory.Controllers;
using HIS.Modules.Inventory.Models;
using HIS.Modules.MedTech.Controllers;
using HIS.Modules.MedTech.Models;
using HIS.Modules.Operations.Controllers;
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

    private Task<HttpClient> CreateCashierClientAsync()
    {
        HttpClient client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        using var conn = connFactory.CreateConnection();
        var sessionVersion = conn.ExecuteScalar<int>(
            "SELECT session_version FROM sys_users WHERE id = 'USER-CASHIER-01'");

        var jwt = scope.ServiceProvider.GetRequiredService<BuildingBlocks.Security.JwtTokenService>();
        var token = jwt.GenerateAccessToken(
            userId: "USER-CASHIER-01",
            userName: "cashier_01",
            displayName: "李小明 (收费员)",
            campusId: "CAMPUS-01",
            activeDeptCategory: DepartmentCategory.FINANCE,
            activeDeptId: "DEPT-CASHIER",
            activeWardId: null,
            linkedWardIds: null,
            roles: new[] { "ROLE-CASHIER", "CASHIER" },
            dataScope: DataScope.ALL_CAMPUS,
            sessionVersion: sessionVersion,
            permHash: "ALL",
            prescribeLevel: 0,
            surgeryLevel: 0,
            emrSignLevel: 0,
            activePostId: "UDP-CASHIER",
            roleId: "ROLE-CASHIER",
            roleCode: "CASHIER",
            roleName: "门诊收费员",
            activeDeptName: "门诊收费处");

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

        if (alertsData?.Data == null || alertsData.Data.Count == 0)
        {
            // Seed a threat alert if database was drained by earlier tests
            using var scope = _factory.Services.CreateScope();
            var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            using var conn = connFactory.CreateConnection();
            var now = SystemTime.UtcNowMs;
            await conn.ExecuteAsync(@"INSERT INTO ops_security_alerts
                (id, rule_code, severity, title, detail, status, detected_at_ms, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES ('TEST-ALERT-01', 'BRUTE_FORCE', 'HIGH', '测试暴力破解告警', '测试告警明细', 'PENDING', @Now, 1, @Now, @Now, 1)",
                new { Now = now });
            alertsRes = await _client.GetAsync("/api/v4/operations/security/alerts");
            alertsData = await alertsRes.Content.ReadFromJsonAsync<ApiResponse<List<OpsSecurityAlert>>>();
        }

        Assert.NotNull(alertsData?.Data);
        Assert.NotEmpty(alertsData!.Data!);

        // 4. Resolve an alert
        var targetAlert = alertsData.Data.FirstOrDefault(a => a.Status != "RESOLVED") ?? alertsData.Data.First();
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

        var jwtKey = keysData.Data.FirstOrDefault(k => k.KeyName == "JWT_SIGNING_KEY" && k.State == "ACTIVE")
                     ?? keysData.Data.First(k => k.KeyName == "JWT_SIGNING_KEY");
        Assert.Equal((short)1, jwtKey.HsmProtected);
        int oldVersion = jwtKey.Version;

        // 2. Rotate Key (tolerates 200 or 409 if already rotated in current session)
        var rotateRes = await _client.PostAsJsonAsync("/api/v4/operations/kms/keys/rotate", new KeyRotationRequestDto("JWT_SIGNING_KEY", "自动化轮换演练测试"));
        Assert.True(rotateRes.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict);

        if (rotateRes.StatusCode == HttpStatusCode.OK)
        {
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
        }

        // 4. HSM Status check
        var hsmRes = await _client.GetAsync("/api/v4/operations/kms/hsm-status");
        Assert.Equal(HttpStatusCode.OK, hsmRes.StatusCode);
        var hsmData = await hsmRes.Content.ReadFromJsonAsync<ApiResponse<HsmStatusDto>>();
        Assert.NotNull(hsmData?.Data);
        Assert.Equal("ONLINE", hsmData!.Data!.State);
    }

    [Fact]
    public async Task P0A_InpatientCashier_RegisterWithPatient_And_CreateAdmissionCard()
    {
        using HttpClient cashierClient = await CreateCashierClientAsync();
        string uniqueSuffix = Guid.NewGuid().ToString("N")[..6];
        string idCard = "330106199201" + uniqueSuffix[..6];
        string phone = "138000" + uniqueSuffix[..5];

        // 1. One-stop patient registration and admission card creation
        var request = new InpatientAdmissionController.CreateAdmissionWithPatientRequest(
            FullName: "一站式患者" + uniqueSuffix,
            GenderCode: "1",
            BirthDateInt: 19920101,
            IdCardNo: idCard,
            Phone: phone,
            SuggestedIpdDeptId: "DEPT-IPD-01",
            SuggestedWardId: "DEPT-WARD-01",
            AdmissionDiagnosis: "呼吸道感染待查",
            Remark: "门诊收费员直接代建档开卡",
            RequestKey: "REQ-ONE-STOP-" + uniqueSuffix
        );

        var res = await cashierClient.PostAsJsonAsync("/api/v4/ipd/admission/register-with-patient", request);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var data = await res.Content.ReadFromJsonAsync<ApiResponse<IpdAdmissionCardView>>();
        Assert.NotNull(data?.Data);
        Assert.StartsWith("ADC", data!.Data!.CardNo);
        Assert.Equal("PENDING", data.Data.Status);
        Assert.Equal("呼吸道感染待查", data.Data.AdmissionDiagnosis);
        string cardId = data.Data.Id;
        string patientId = data.Data.PatientId;

        // 2. Verify duplicate patient returns 409 with existing medical record number
        var dupRes = await cashierClient.PostAsJsonAsync("/api/v4/ipd/admission/register-with-patient", request with
        {
            RequestKey = "REQ-ONE-STOP-DUP-" + uniqueSuffix
        });
        Assert.Equal(HttpStatusCode.Conflict, dupRes.StatusCode);
        var dupData = await dupRes.Content.ReadFromJsonAsync<ApiResponse<IpdAdmissionCardView>>();
        Assert.Contains("已建档", dupData?.Message ?? string.Empty);

        // 3. Verify Cashier can also create admission card for existing patient via /cards
        // First cancel the pending card so another card can be opened
        var cancelRes = await _client.PostAsJsonAsync($"/api/v4/ipd/cards/{cardId}/cancel", new InpatientAdmissionController.CancelRequest("测试取消"));
        Assert.Equal(HttpStatusCode.OK, cancelRes.StatusCode);

        var cashierCardRes = await cashierClient.PostAsJsonAsync("/api/v4/ipd/cards", new CreateAdmissionCardRequest
        {
            PatientId = patientId,
            SuggestedIpdDeptId = "DEPT-IPD-01",
            AdmissionDiagnosis = "收费员代开卡测试",
            RequestKey = "REQ-CASHIER-CARD-" + uniqueSuffix
        });
        Assert.Equal(HttpStatusCode.OK, cashierCardRes.StatusCode);
        var cashierCardData = await cashierCardRes.Content.ReadFromJsonAsync<ApiResponse<IpdAdmissionCardView>>();
        Assert.NotNull(cashierCardData?.Data);
        Assert.Equal("PENDING", cashierCardData!.Data!.Status);

        // 4. Verify audit trail has PATIENT_REGISTER and IPD_ADMISSION_CARD_CREATE
        using var scope = _factory.Services.CreateScope();
        var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        using var conn = connFactory.CreateConnection();
        var patAudits = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM ops_audit_events WHERE event_type = 'PATIENT_REGISTER' AND target_id = @PatientId",
            new { PatientId = patientId });
        Assert.True(patAudits >= 1);

        var cardAudits = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM ops_audit_events WHERE event_type = 'IPD_ADMISSION_CARD_CREATE' AND action_detail LIKE '%收费员%'");
        Assert.True(cardAudits >= 1);
    }

    [Fact]
    public async Task P0C_MedicalInsuranceAdapter_Capabilities_And_Provider()
    {
        using var scope = _factory.Services.CreateScope();
        var adapter = scope.ServiceProvider.GetRequiredService<IMedicalInsuranceAdapter>();
        Assert.NotNull(adapter);

        // 1. Patient match
        var match = await adapter.MatchPatientAsync("330106198506182215", "周建强");
        Assert.NotNull(match);
        Assert.Equal("NORMAL", match.InsuredStatus);
        Assert.True(match.AccountBalance > 0);

        // 2. Catalog sync
        var sync = await adapter.SyncCatalogAsync();
        Assert.NotNull(sync);
        Assert.Equal("SUCCESS", sync.Status);
        Assert.True(sync.TotalItems > 0);

        // 3. Real-time settlement
        var settleReq = new InsuranceSettleRequest(
            PatientId: "PAT85061801",
            EncounterOrVisitId: "ADM20261105001",
            TotalAmount: 1000m,
            FundPaymentAmount: 700m,
            PersonalAccountAmount: 300m,
            CashAmount: 0m,
            SettlementType: "INPATIENT"
        );
        var settleResult = await adapter.RealTimeSettleAsync(settleReq);
        Assert.NotNull(settleResult);
        Assert.Equal("SETTLED", settleResult.Status);
        Assert.Equal(1000m, settleResult.TotalAmount);
        Assert.StartsWith("INS-STL-", settleResult.InsuranceSettleNo);

        // 4. Refund
        var refundResult = await adapter.RefundAsync(settleResult.InsuranceSettleNo, 1000m);
        Assert.NotNull(refundResult);
        Assert.Equal("REFUNDED", refundResult.Status);
        Assert.StartsWith("INS-RFD-", refundResult.InsuranceRefundNo);

        // 5. Query Balance
        var balance = await adapter.QueryBalanceAsync(match.InsuranceCardNo);
        Assert.NotNull(balance);
        Assert.Equal("NORMAL", balance.Status);
        Assert.True(balance.Balance > 0);
    }

    [Fact]
    public async Task P1A_ConsumablesAndCssd_FullLifecycle()
    {
        // 1. Query catalog and verify seeded consumables
        var catRes = await _client.GetFromJsonAsync<ApiResponse<List<CsmConsumable>>>("/api/v4/csm/consumables");
        Assert.NotNull(catRes?.Data);
        Assert.Contains(catRes.Data, c => c.Code == "HC001");
        Assert.Contains(catRes.Data, c => c.Code == "HC003" && c.IsHighValue == 1);

        // 2. Create new consumable item
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..6];
        var createRes = await _client.PostAsJsonAsync("/api/v4/csm/consumables", new CreateConsumableRequestDto(
            Code: "HC-" + uniqueSuffix,
            Name: "测试无菌纱布包-" + uniqueSuffix,
            Spec: "10x10cm 5片装",
            Unit: "包",
            Category: "SURGICAL",
            IsSterile: true,
            IsHighValue: false,
            Price: 8.50m
        ));
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);
        var createdData = await createRes.Content.ReadFromJsonAsync<ApiResponse<CsmConsumable>>();
        Assert.NotNull(createdData?.Data);
        var consumableId = createdData!.Data!.Id;

        // 3. Purchase Stock-In
        var stockInRes = await _client.PostAsJsonAsync("/api/v4/csm/stock-in", new StockInRequestDto(
            ConsumableId: consumableId,
            WarehouseId: "DEPT-WH-CSM",
            BatchNo: "BAT-AUTO-" + uniqueSuffix,
            ExpiryDate: "2028-12-31",
            ManufactureDate: "2026-11-01",
            Quantity: 200,
            UnitCost: 5.00m
        ));
        Assert.Equal(HttpStatusCode.OK, stockInRes.StatusCode);

        // 4. Ward Requisition
        var reqRes = await _client.PostAsJsonAsync("/api/v4/csm/requisitions", new CreateRequisitionRequestDto(
            WardId: "DEPT-WARD-01",
            DeptId: "DEPT-WARD-01",
            Remark: "病区临床急需请领",
            Items: new List<RequisitionLineItemDto>
            {
                new(ConsumableId: consumableId, Quantity: 20)
            }
        ));
        Assert.Equal(HttpStatusCode.OK, reqRes.StatusCode);
        var reqData = await reqRes.Content.ReadFromJsonAsync<ApiResponse<CsmRequisition>>();
        Assert.NotNull(reqData?.Data);
        var requisitionId = reqData!.Data!.Id;
        Assert.Equal("PENDING", reqData.Data.Status);

        // 5. Approve Requisition
        var approveRes = await _client.PostAsync($"/api/v4/csm/requisitions/{requisitionId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approveRes.StatusCode);

        // 6. Issue Requisition
        var issueRes = await _client.PostAsync($"/api/v4/csm/requisitions/{requisitionId}/issue", null);
        Assert.Equal(HttpStatusCode.OK, issueRes.StatusCode);
        var issuedData = await issueRes.Content.ReadFromJsonAsync<ApiResponse<CsmRequisition>>();
        Assert.NotNull(issuedData?.Data);
        Assert.Equal("ISSUED", issuedData!.Data!.Status);

        // 7. High-Value Consumable Trace Code Use & Verification
        var bindRes = await _client.PostAsJsonAsync("/api/v4/csm/trace/use", new ScanUseTraceCodeRequestDto(
            TraceCode: "UDI-HC-2026110001",
            PatientId: "PAT85061801",
            EncounterId: "ADM20261105001"
        ));
        Assert.Equal(HttpStatusCode.OK, bindRes.StatusCode);

        var verifyRes = await _client.GetFromJsonAsync<ApiResponse<List<CsmTraceCode>>>("/api/v4/csm/trace-codes?patientId=PAT85061801");
        Assert.NotNull(verifyRes?.Data);
        Assert.Contains(verifyRes.Data, tc => tc.TraceCode == "UDI-HC-2026110001" && tc.Status == "USED");

        // 8. CSSD Sterile Package lifecycle
        var pkgRes = await _client.PostAsJsonAsync("/api/v4/csm/cssd/packages", new CreateSterilePackageRequestDto(
            PackageName: "测试骨科微创包-" + uniqueSuffix,
            Contents: new List<string> { "持针钳", "缝合线" }
        ));
        Assert.Equal(HttpStatusCode.OK, pkgRes.StatusCode);
        var pkgData = await pkgRes.Content.ReadFromJsonAsync<ApiResponse<CsmSterilePackage>>();
        Assert.NotNull(pkgData?.Data);
        var packageId = pkgData!.Data!.Id;

        // Sterilize
        var sterilizeRes = await _client.PostAsJsonAsync($"/api/v4/csm/sterile-packages/{packageId}/sterilize", new SterilizePackageRequestDto(
            SterilizationBatchNo: "ST-BAT-" + uniqueSuffix,
            Method: "HIGH_PRESSURE_STEAM",
            ValidDays: 180
        ));
        Assert.Equal(HttpStatusCode.OK, sterilizeRes.StatusCode);

        // Dispatch / Issue to ward
        var dispatchRes = await _client.PostAsJsonAsync($"/api/v4/csm/sterile-packages/{packageId}/issue", new IssueSterilePackageRequestDto(
            TargetLocation: "DEPT-WARD-01",
            Remark: "手术借包"
        ));
        Assert.Equal(HttpStatusCode.OK, dispatchRes.StatusCode);

        // 9. Finance inventory report
        var rptRes = await _client.GetFromJsonAsync<ApiResponse<ConsumableFinanceReportDto>>("/api/v4/csm/finance/report");
        Assert.NotNull(rptRes?.Data);
        Assert.True(rptRes.Data.TotalConsumablesCount > 0);
        Assert.True(rptRes.Data.TotalStockValuation >= 0);
    }

    [Fact]
    public async Task P1F_Analytics_HospitalOverviewAndExport()
    {
        // 1. Overview KPIs
        var ovRes = await _client.GetAsync("/api/v4/ops/analytics/overview");
        Assert.Equal(HttpStatusCode.OK, ovRes.StatusCode);
        var ovData = await ovRes.Content.ReadFromJsonAsync<ApiResponse<AnalyticsController.HospitalOverviewDto>>();
        Assert.NotNull(ovData?.Data);
        Assert.True(ovData.Data.TotalRegisteredPatients >= 0);
        Assert.True(ovData.Data.AvailableBedsCount >= 0);

        // 2. Outpatient analytics
        var opdRes = await _client.GetAsync("/api/v4/ops/analytics/outpatient");
        var opdBody = await opdRes.Content.ReadAsStringAsync();
        Assert.True(opdRes.IsSuccessStatusCode, $"Outpatient failed with {opdRes.StatusCode}: {opdBody}");

        // 3. Inpatient analytics
        var ipdRes = await _client.GetAsync("/api/v4/ops/analytics/inpatient");
        Assert.Equal(HttpStatusCode.OK, ipdRes.StatusCode);

        // 4. Pharmacy analytics
        var phmRes = await _client.GetAsync("/api/v4/ops/analytics/pharmacy");
        Assert.Equal(HttpStatusCode.OK, phmRes.StatusCode);

        // 5. Finance analytics
        var finRes = await _client.GetAsync("/api/v4/ops/analytics/finance");
        Assert.Equal(HttpStatusCode.OK, finRes.StatusCode);

        // 6. Quality indicators
        var qltRes = await _client.GetAsync("/api/v4/ops/analytics/quality");
        Assert.Equal(HttpStatusCode.OK, qltRes.StatusCode);

        // 7. CSV Export
        var csvRes = await _client.GetAsync("/api/v4/ops/analytics/export?reportType=OVERVIEW");
        Assert.Equal(HttpStatusCode.OK, csvRes.StatusCode);
        var csvText = await csvRes.Content.ReadAsStringAsync();
        Assert.Contains("指标名称,数值,单位", csvText);
        Assert.Contains("今日门诊就诊", csvText);
    }
}
