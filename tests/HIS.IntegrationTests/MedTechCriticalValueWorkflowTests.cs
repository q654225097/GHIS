using System;
using System.Collections.Generic;
using System.Linq;
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
using HIS.Modules.MedTech.Controllers;
using HIS.Modules.MedTech.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static HIS.Modules.MedTech.Controllers.MedTechController;

namespace HIS.IntegrationTests;

public class MedTechCriticalValueWorkflowTests
{
    private const string Queue = "/api/v4/medtech/critical-values";
    private const string EncounterId = "ADM-PHM-DEMO-01";
    private const string PatientId = "PAT-PHM-DEMO-01";
    private const string DoctorId = "USER-DOC-ATTENDING";
    private const string NurseId = "USER-NURSE-W2";
    private const string WardId = "DEPT-WARD-02";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlyAssignedClinicalRolesCanSignAndTwoIndependentSignaturesAreDurable(bool diagnosticReport)
    {
        using var host = new WebApplicationFactory<Program>();
        using var server = host.WithWebHostBuilder(builder => builder.UseEnvironment("Development")
            .UseSetting("HIS_DB_PROVIDER", "Sqlite")
            .UseSetting("HIS_DB_CONNECTION_STRING", "Data Source=:memory:;Mode=Memory;Cache=Shared"));
        using var scope = server.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<IDbConnectionFactory>();
        var jwt = services.GetRequiredService<JwtTokenService>();
        using var conn = db.CreateConnection();
        Assert.NotEqual("his_v4.db", conn.DataSource);
        Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ipd_encounters WHERE id = @Id", new { Id = EncounterId }));

        var request = new MtcServiceRequest
        {
            RequestNo = "CV-REQ-" + Guid.NewGuid().ToString("N")[..8], SourceType = "IPD", SourceVisitId = EncounterId,
            PatientId = PatientId, OrderDeptId = "DEPT-IPD-01", OrderWardId = WardId,
            ExecMedTechDeptId = "DEPT-MEDTECH-01", ServiceItemCode = "CV-TEST", Status = diagnosticReport ? 2 : 3,
            IsFeeConfirmed = 1, IsCriticalValue = (short)(diagnosticReport ? 0 : 1)
        };
        await services.GetRequiredService<IRepository<MtcServiceRequest>>().InsertAsync(request);
        var alertRepo = services.GetRequiredService<IRepository<MedtechCriticalValue>>();
        MedtechCriticalValue alert;
        string reportNo = request.RequestNo;
        if (diagnosticReport)
        {
            var report = new MtcDiagnosticReport
            {
                ReportNo = "CV-RPT-" + Guid.NewGuid().ToString("N")[..8], ServiceRequestId = request.Id, PatientId = PatientId,
                PatientName = "Synthetic test patient", ExamItemName = "Critical item", Status = "PENDING_REVIEW", IsCriticalValue = 1,
                FindingsSummary = "Synthetic critical result"
            };
            await services.GetRequiredService<IRepository<MtcDiagnosticReport>>().InsertAsync(report);
            reportNo = report.ReportNo;
            using var reviewer = await Client("USER-MEDTECH-01", RoleCode.MedTech, request.ExecMedTechDeptId, null);
            using var notReviewer = await Client(DoctorId, RoleCode.IpdAttending, request.ExecMedTechDeptId, null);
            var review = new MedTechReportController.SaveReportContentDto(null, null, "{}", report.FindingsSummary, null, true);
            string approve = $"/api/v4/medtech-reports/{report.Id}/approve";
            using var denied = await notReviewer.PostAsJsonAsync(approve, review);
            Assert.Equal(403, (await denied.Content.ReadFromJsonAsync<ApiResponse<MtcDiagnosticReport>>())!.Code);
            using var approved = await reviewer.PostAsJsonAsync(approve, review);
            var signedReport = await approved.Content.ReadFromJsonAsync<ApiResponse<MtcDiagnosticReport>>();
            Assert.Equal(200, signedReport!.Code);
            Assert.Equal("SIGNED", signedReport.Data!.Status);
            alert = Assert.Single(await alertRepo.QueryAsync("SELECT * FROM medtech_critical_values WHERE report_id = @Id", new { report.Id }));
            Assert.Equal(EncounterId, alert.VisitOrEncounterId);
            Assert.Equal(PatientId, alert.PatientId);
        }
        else
        {
            alert = new MedtechCriticalValue
            {
                ReportId = request.Id, SourceType = "IPD", VisitOrEncounterId = EncounterId, PatientId = PatientId,
                ItemCode = "CV-TEST", ItemName = "Critical item", CriticalValueText = "Synthetic critical result",
                PublishedBy = "USER-MEDTECH-01", PublishedAtMs = SystemTime.UtcNowMs, Status = "PENDING_ACK"
            };
            await alertRepo.InsertAsync(alert);
        }
        var mismatched = new MedtechCriticalValue
        {
            ReportId = alert.ReportId, SourceType = "IPD", VisitOrEncounterId = EncounterId, PatientId = "OTHER-PATIENT",
            ItemCode = "CV-TEST", ItemName = "Wrong patient", CriticalValueText = "Not signable",
            PublishedBy = "USER-MEDTECH-01", PublishedAtMs = SystemTime.UtcNowMs
        };
        await alertRepo.InsertAsync(mismatched);

        using var anonymous = server.CreateClient();
        using var deniedHub = await anonymous.PostAsync("/hubs/clinical-alerts/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, deniedHub.StatusCode);
        using var doctor = await Client(DoctorId, RoleCode.IpdAttending, "DEPT-IPD-01", null);
        using var nurse = await Client(NurseId, RoleCode.WardNurse, "DEPT-WARD-01", WardId);
        using var wrongDepartment = await Client(DoctorId, RoleCode.IpdAttending, "DEPT-IPD-OTHER", null);
        using var wrongRole = await Client(DoctorId, RoleCode.Cashier, "DEPT-IPD-01", null);
        using var samePersonNurse = await Client(DoctorId, RoleCode.WardNurse, "DEPT-WARD-01", WardId);
        using var hub = await doctor.PostAsync("/hubs/clinical-alerts/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.OK, hub.StatusCode);

        var queue = await List(doctor, "?status=PENDING_ACK");
        var first = Assert.Single(queue, x => x.Id == alert.Id);
        Assert.DoesNotContain(queue, x => x.Id == mismatched.Id);
        Assert.Equal(reportNo, first.ReportNo);
        Assert.Equal(request.Id, first.ServiceRequestId);
        Assert.Equal(EncounterId, first.VisitOrEncounterId);
        Assert.Equal(404, (await Sign(wrongDepartment, alert.Id)).Code);
        Assert.Equal(403, (await Sign(wrongRole, alert.Id)).Code);
        Assert.Equal(404, (await Sign(doctor, mismatched.Id)).Code);
        Assert.Empty(await List(wrongDepartment, "?alertId=" + alert.Id));

        var docResult = await Sign(doctor, alert.Id);
        Assert.Equal(200, docResult.Code);
        Assert.Equal(DoctorId, docResult.Data!.DocAckedBy);
        Assert.Equal("PENDING_ACK", docResult.Data.Status);
        Assert.NotNull(docResult.Data.DocAckedAtMs);
        Assert.Equal(docResult.Data.DocAckedAtMs, (await Sign(doctor, alert.Id)).Data!.DocAckedAtMs);
        Assert.Contains(await List(nurse, "?status=PENDING_ACK"), x => x.Id == alert.Id);
        Assert.Equal(404, (await Sign(samePersonNurse, alert.Id)).Code);
        await conn.ExecuteAsync("UPDATE ipd_encounters SET primary_nurse_id = @Id WHERE id = @EncounterId", new { Id = DoctorId, EncounterId });
        Assert.Equal(409, (await Sign(samePersonNurse, alert.Id)).Code);
        await conn.ExecuteAsync("UPDATE ipd_encounters SET primary_nurse_id = @Id WHERE id = @EncounterId", new { Id = NurseId, EncounterId });

        var nurseResult = await Sign(nurse, alert.Id);
        Assert.Equal(200, nurseResult.Code);
        Assert.Equal(DoctorId, nurseResult.Data!.DocAckedBy);
        Assert.Equal(NurseId, nurseResult.Data.NurseAckedBy);
        Assert.Equal("ACKNOWLEDGED", nurseResult.Data.Status);
        using var reopened = await Client(DoctorId, RoleCode.IpdAttending, "DEPT-IPD-01", null);
        Assert.DoesNotContain(await List(reopened, "?status=PENDING_ACK"), x => x.Id == alert.Id);
        var signed = Assert.Single(await List(reopened, "?alertId=" + alert.Id));
        Assert.Equal(nurseResult.Data.NurseAckedAtMs, signed.NurseAckedAtMs);
        Assert.False(signed.CanSign);
        var auditUsers = (await conn.QueryAsync<string>("""
            SELECT user_id FROM ops_audit_events WHERE target_id = @Id AND event_type = 'MTC_CRITICAL_VALUE_ACK'
            """, new { alert.Id })).Order().ToArray();
        Assert.Equal(new[] { DoctorId, NurseId }.Order(), auditUsers);

        async Task<HttpClient> Client(string user, string role, string dept, string? ward)
        {
            var version = await conn.ExecuteScalarAsync<int>("SELECT session_version FROM sys_users WHERE id = @User", new { User = user });
            var client = server.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.GenerateAccessToken(
                user, user, user, "CAMPUS-01", DepartmentCategory.IPD, dept, ward, null,
                new[] { role, RoleCode.IpdAttending }, DataScope.ALL_CAMPUS, version, "ALL", roleCode: role));
            return client;
        }
    }

    private static async Task<ApiResponse<CriticalValueWorkItem>> Sign(HttpClient client, string alertId)
    {
        using var response = await client.PostAsJsonAsync($"{Queue}/{alertId}/acknowledge", new SignCriticalValueDto("Checked original result"));
        return (await response.Content.ReadFromJsonAsync<ApiResponse<CriticalValueWorkItem>>())!;
    }

    private static async Task<List<CriticalValueWorkItem>> List(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<ApiResponse<List<CriticalValueWorkItem>>>(Queue + query))!.Data!;
}
