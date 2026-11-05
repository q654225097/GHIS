using System.Net.Http.Json;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Emr.Controllers;
using HIS.Modules.Emr.Models;
using HIS.Modules.Inpatient.Controllers;
using HIS.Modules.Inpatient.Models;
using HIS.Modules.Inpatient.Services;
using HIS.Modules.MedTech.Controllers;
using HIS.Modules.MedTech.Models;
using HIS.Modules.Ordering.Controllers;
using HIS.Modules.Ordering.Models;
using HIS.Modules.Outpatient.Controllers;
using HIS.Modules.Outpatient.Models;
using HIS.Modules.Patient.Controllers;
using HIS.Modules.Patient.Models;
using HIS.Modules.Platform.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace HIS.IntegrationTests;

public class EndToEndWorkflowTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public EndToEndWorkflowTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task EndToEnd_FullHospitalBusinessWorkflow_Test()
    {
        // 1. Auth Login as Doctor
        var loginRes = await _client.PostAsJsonAsync("/api/v4/platform/auth/login", new PlatformController.LoginRequestDto(
            UserName: "doc_opd",
            Password: "123456",
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
        Assert.Equal("GB/T 33190-2016", signData.Data.OfdVersionStandard);

        // 4. Inpatient Admission with Borrowed Bed (WF-IPD-01, WF-IPD-02)
        // Admitting to Cardiology IPD 1 (DEPT-IPD-01) but borrowing bed in Ward 2 (DEPT-WARD-02)
        var admitRes = await _client.PostAsJsonAsync("/api/v4/ipd/admit", new InpatientController.AdmitPatientRequestDto(
            PatientId: patientId,
            CampusId: "CAMPUS-01",
            AdmitDeptId: "DEPT-IPD-01",
            AdmitWardId: "DEPT-WARD-02",
            BedCode: "01",
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
}
