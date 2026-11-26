using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.BuildingBlocks.Security;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Operations.Controllers;

[ApiController]
[RequireHisRole("ADMIN", "CASHIER", "IPD_ATTENDING")]
[Route("api/v4/analytics")]
[Route("api/v4/ops/analytics")]
[RequireRole(RoleCode.Admin, "DIRECTOR", "LEADER", RoleCode.Cashier, RoleCode.DoctorOpd, RoleCode.DoctorIpd)]
public class AnalyticsController : ControllerBase
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IUserSession? _userSession;

    public AnalyticsController(
        IDbConnectionFactory connFactory,
        IUserSession? userSession = null)
    {
        _connFactory = connFactory;
        _userSession = userSession;
    }

    public sealed record HospitalOverviewDto(
        int TodayOutpatientVisits,
        int ActiveInpatients,
        decimal TodayRevenue,
        decimal DrugRevenueRatio,
        decimal BedUtilizationRate,
        int TotalRegisteredPatients,
        int PendingDispenseCount,
        int AvailableBedsCount
    );

    public record class DeptStatDto
    {
        public string DeptId { get; set; } = string.Empty;
        public string DeptName { get; set; } = string.Empty;
        public int VisitCount { get; set; }
        public decimal TotalAmount { get; set; }
    }

    public record class InpatientWardStatDto
    {
        public string WardId { get; set; } = string.Empty;
        public string WardName { get; set; } = string.Empty;
        public int TotalBeds { get; set; }
        public int OccupiedBeds { get; set; }
        public decimal OccupancyRate { get; set; }
        public int InpatientCount { get; set; }
    }

    public class KeyAmountDto
    {
        public string Key { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    public sealed record PharmacyAnalyticsDto(
        int TotalPrescriptions,
        int DispensedPrescriptions,
        decimal TotalDrugSales,
        decimal AntibioticPrescriptionRatio,
        decimal AveragePrescriptionAmount
    );

    public sealed record FinanceAnalyticsDto(
        decimal TotalRevenue,
        decimal OutpatientRevenue,
        decimal InpatientPrepayment,
        decimal TotalRefundAmount,
        Dictionary<string, decimal> RevenueByChannel,
        Dictionary<string, decimal> RevenueByCategory
    );

    public sealed record MedicalQualityAnalyticsDto(
        int TotalFrontSheets,
        int SubmittedFrontSheets,
        decimal FrontSheetGradeARate,
        decimal AverageInpatientDays,
        int TotalEmrDocuments,
        decimal EmrSignCompletionRate
    );

    public sealed record InfectionControlSummaryDto(
        int TotalInfectionReports,
        int ActiveIsolationOrders,
        int HandHygieneAuditsCount,
        decimal HandHygieneComplianceRate,
        int OccupationalExposuresCount
    );

    [HttpGet("overview")]
    public async Task<ApiResponse<HospitalOverviewDto>> GetOverview(CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();
        long todayStartMs = SystemTime.TodayStartUtcMs;

        int opdVisits = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM opd_visits WHERE created_at_ms >= @StartMs", new { StartMs = todayStartMs });

        int activeInpatients = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM ipd_encounters WHERE status IN (1, 4)");

        decimal todayRevenue = await conn.ExecuteScalarAsync<decimal>(
            "SELECT COALESCE(SUM(total_amount), 0) FROM fin_payment_orders WHERE status = 1 AND paid_at_ms >= @StartMs",
            new { StartMs = todayStartMs });

        decimal totalDrugSales = await conn.ExecuteScalarAsync<decimal>(
            "SELECT COALESCE(SUM(total_amount), 0) FROM opd_prescriptions WHERE created_at_ms >= @StartMs",
            new { StartMs = todayStartMs });

        decimal drugRatio = todayRevenue > 0
            ? Math.Round(totalDrugSales / todayRevenue * 100m, 2)
            : 0m;

        int totalBeds = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ipd_beds");
        int occupiedBeds = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ipd_beds WHERE bed_status = 1");
        decimal bedRate = totalBeds > 0
            ? Math.Round((decimal)occupiedBeds / totalBeds * 100m, 2)
            : 0m;

        int totalPatients = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM pat_patients");
        int pendingDispense = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM opd_prescriptions WHERE status = 2");

        return ApiResponse<HospitalOverviewDto>.Success(new HospitalOverviewDto(
            TodayOutpatientVisits: opdVisits,
            ActiveInpatients: activeInpatients,
            TodayRevenue: todayRevenue,
            DrugRevenueRatio: drugRatio,
            BedUtilizationRate: bedRate,
            TotalRegisteredPatients: totalPatients,
            PendingDispenseCount: pendingDispense,
            AvailableBedsCount: Math.Max(0, totalBeds - occupiedBeds)
        ));
    }

    [HttpGet("outpatient")]
    public async Task<ApiResponse<IReadOnlyList<DeptStatDto>>> GetOutpatientAnalysis(
        [FromQuery] long? startMs = null,
        [FromQuery] long? endMs = null)
    {
        using var conn = _connFactory.CreateConnection();
        long start = startMs ?? (SystemTime.TodayStartUtcMs - 7 * 86400000L);
        long end = endMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        const string sql = @"
SELECT d.id AS DeptId, d.dept_name AS DeptName,
       COUNT(v.id) AS VisitCount,
       COALESCE(SUM(p.total_amount), 0) AS TotalAmount
FROM sys_departments d
LEFT JOIN opd_visits v ON v.opd_dept_id = d.id AND v.created_at_ms BETWEEN @Start AND @End
LEFT JOIN fin_payment_orders p ON p.visit_or_encounter_id = v.id AND p.status = 1
WHERE d.dept_category = 'OPD' AND d.is_active = 1
GROUP BY d.id, d.dept_name
ORDER BY VisitCount DESC, TotalAmount DESC";

        var list = (await conn.QueryAsync<DeptStatDto>(sql, new { Start = start, End = end })).ToList();
        return ApiResponse<IReadOnlyList<DeptStatDto>>.Success(list);
    }

    [HttpGet("inpatient")]
    public async Task<ApiResponse<IReadOnlyList<InpatientWardStatDto>>> GetInpatientAnalysis()
    {
        using var conn = _connFactory.CreateConnection();
        const string sql = @"
SELECT w.id AS WardId, w.dept_name AS WardName,
       COUNT(b.id) AS TotalBeds,
       COALESCE(SUM(CASE WHEN b.bed_status = 1 THEN 1 ELSE 0 END), 0) AS OccupiedBeds,
       0 AS OccupancyRate,
       COUNT(DISTINCT e.id) AS InpatientCount
FROM sys_departments w
LEFT JOIN ipd_beds b ON b.ward_id = w.id
LEFT JOIN ipd_encounters e ON e.admit_ward_id = w.id AND e.status IN (1, 4)
WHERE w.dept_category = 'WARD' AND w.is_active = 1
GROUP BY w.id, w.dept_name
ORDER BY TotalBeds DESC";

        var rows = (await conn.QueryAsync<InpatientWardStatDto>(sql)).ToList();
        var result = rows.Select(r =>
        {
            decimal rate = r.TotalBeds > 0
                ? Math.Round((decimal)r.OccupiedBeds / r.TotalBeds * 100m, 2)
                : 0m;
            return r with { OccupancyRate = rate };
        }).ToList();

        return ApiResponse<IReadOnlyList<InpatientWardStatDto>>.Success(result);
    }

    [HttpGet("pharmacy")]
    public async Task<ApiResponse<PharmacyAnalyticsDto>> GetPharmacyAnalysis(
        [FromQuery] long? startMs = null,
        [FromQuery] long? endMs = null)
    {
        using var conn = _connFactory.CreateConnection();
        long start = startMs ?? (SystemTime.TodayStartUtcMs - 30 * 86400000L);
        long end = endMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        int totalRx = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM opd_prescriptions WHERE created_at_ms BETWEEN @Start AND @End",
            new { Start = start, End = end });

        int dispensedRx = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM phm_dispense_orders WHERE status = 2 AND created_at_ms BETWEEN @Start AND @End",
            new { Start = start, End = end });

        decimal totalSales = await conn.ExecuteScalarAsync<decimal>(
            "SELECT COALESCE(SUM(total_amount), 0) FROM opd_prescriptions WHERE created_at_ms BETWEEN @Start AND @End",
            new { Start = start, End = end });

        int antibioticRx = await conn.ExecuteScalarAsync<int>(@"
SELECT COUNT(DISTINCT p.id)
FROM opd_prescriptions p
JOIN opd_prescription_items i ON i.prescription_id = p.id
JOIN inv_drugs d ON d.id = i.drug_id
WHERE d.antibiotic_level > 0 AND p.created_at_ms BETWEEN @Start AND @End",
            new { Start = start, End = end });

        decimal antibioticRatio = totalRx > 0 ? Math.Round((decimal)antibioticRx / totalRx * 100m, 2) : 0m;
        decimal avgAmount = totalRx > 0 ? Math.Round(totalSales / totalRx, 2) : 0m;

        return ApiResponse<PharmacyAnalyticsDto>.Success(new PharmacyAnalyticsDto(
            TotalPrescriptions: totalRx,
            DispensedPrescriptions: dispensedRx,
            TotalDrugSales: totalSales,
            AntibioticPrescriptionRatio: antibioticRatio,
            AveragePrescriptionAmount: avgAmount
        ));
    }

    [HttpGet("finance")]
    public async Task<ApiResponse<FinanceAnalyticsDto>> GetFinanceAnalysis(
        [FromQuery] long? startMs = null,
        [FromQuery] long? endMs = null)
    {
        using var conn = _connFactory.CreateConnection();
        long start = startMs ?? (SystemTime.TodayStartUtcMs - 30 * 86400000L);
        long end = endMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        decimal totalRevenue = await conn.ExecuteScalarAsync<decimal>(
            "SELECT COALESCE(SUM(total_amount), 0) FROM fin_payment_orders WHERE status = 1 AND paid_at_ms BETWEEN @Start AND @End",
            new { Start = start, End = end });

        decimal opdRevenue = await conn.ExecuteScalarAsync<decimal>(
            "SELECT COALESCE(SUM(total_amount), 0) FROM fin_payment_orders WHERE status = 1 AND scene = 'OPD' AND paid_at_ms BETWEEN @Start AND @End",
            new { Start = start, End = end });

        decimal ipdPrepayment = await conn.ExecuteScalarAsync<decimal>(
            "SELECT COALESCE(SUM(amount), 0) FROM ipd_prepayments WHERE paid_at_ms BETWEEN @Start AND @End",
            new { Start = start, End = end });

        decimal totalRefund = await conn.ExecuteScalarAsync<decimal>(
            "SELECT COALESCE(SUM(refund_amount), 0) FROM fin_refund_orders WHERE status = 1 AND refunded_at_ms BETWEEN @Start AND @End",
            new { Start = start, End = end });

        var channelRows = await conn.QueryAsync<KeyAmountDto>(@"
SELECT payment_channel AS Key, COALESCE(SUM(total_amount), 0) AS Amount
FROM fin_payment_orders
WHERE status = 1 AND paid_at_ms BETWEEN @Start AND @End
GROUP BY payment_channel", new { Start = start, End = end });
        var byChannel = channelRows.ToDictionary(x => x.Key, x => x.Amount);

        var catRows = await conn.QueryAsync<KeyAmountDto>(@"
SELECT COALESCE(c.fee_category, 'OTHER') AS Key, COALESCE(SUM(l.total_amount), 0) AS Amount
FROM fin_charge_ledgers l
LEFT JOIN cfg_price_items c ON c.item_code = l.item_code
WHERE l.created_at_ms BETWEEN @Start AND @End
GROUP BY COALESCE(c.fee_category, 'OTHER')", new { Start = start, End = end });
        var byCategory = catRows.ToDictionary(x => x.Key, x => x.Amount);

        return ApiResponse<FinanceAnalyticsDto>.Success(new FinanceAnalyticsDto(
            TotalRevenue: totalRevenue,
            OutpatientRevenue: opdRevenue,
            InpatientPrepayment: ipdPrepayment,
            TotalRefundAmount: totalRefund,
            RevenueByChannel: byChannel,
            RevenueByCategory: byCategory
        ));
    }

    [HttpGet("quality")]
    public async Task<ApiResponse<MedicalQualityAnalyticsDto>> GetQualityAnalysis()
    {
        using var conn = _connFactory.CreateConnection();
        int totalSheets = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM emr_inpatient_front_sheets");
        int submittedSheets = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM emr_inpatient_front_sheets WHERE status = 'SUBMITTED'");
        int gradeASheets = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM emr_inpatient_front_sheets WHERE quality_level = 'GRADE_A'");
        decimal avgDays = await conn.ExecuteScalarAsync<decimal>("SELECT COALESCE(AVG(inpatient_days), 0) FROM emr_inpatient_front_sheets");
        int totalEmr = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM emr_documents");
        int chiefSignedEmr = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM emr_documents WHERE chief_signed_at_ms IS NOT NULL");

        decimal gradeARate = totalSheets > 0 ? Math.Round((decimal)gradeASheets / totalSheets * 100m, 2) : 100m;
        decimal emrSignRate = totalEmr > 0 ? Math.Round((decimal)chiefSignedEmr / totalEmr * 100m, 2) : 0m;

        return ApiResponse<MedicalQualityAnalyticsDto>.Success(new MedicalQualityAnalyticsDto(
            TotalFrontSheets: totalSheets,
            SubmittedFrontSheets: submittedSheets,
            FrontSheetGradeARate: gradeARate,
            AverageInpatientDays: Math.Round(avgDays, 1),
            TotalEmrDocuments: totalEmr,
            EmrSignCompletionRate: emrSignRate
        ));
    }

    [HttpGet("infection")]
    public async Task<ApiResponse<InfectionControlSummaryDto>> GetInfectionSummary()
    {
        using var conn = _connFactory.CreateConnection();
        int reports = 0;
        int isolations = 0;
        int audits = 0;
        decimal compliance = 100m;
        int exposures = 0;

        try
        {
            reports = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ipc_infection_reports");
            isolations = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ipc_isolation_orders WHERE end_ms IS NULL");
            audits = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ipc_hand_hygiene_audits");
            var auditRow = await conn.QuerySingleOrDefaultAsync<(int obs, int comp)>(
                "SELECT COALESCE(SUM(observed_count), 0) AS obs, COALESCE(SUM(compliant_count), 0) AS comp FROM ipc_hand_hygiene_audits");
            if (auditRow.obs > 0)
            {
                compliance = Math.Round((decimal)auditRow.comp / auditRow.obs * 100m, 2);
            }
            exposures = await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ipc_occupational_exposures");
        }
        catch
        {
            // If tables created later in P1-E, graceful default
        }

        return ApiResponse<InfectionControlSummaryDto>.Success(new InfectionControlSummaryDto(
            TotalInfectionReports: reports,
            ActiveIsolationOrders: isolations,
            HandHygieneAuditsCount: audits,
            HandHygieneComplianceRate: compliance,
            OccupationalExposuresCount: exposures
        ));
    }

    [HttpGet("export")]
    [HttpGet("export.csv")]
    public async Task<IActionResult> ExportReport([FromQuery] string reportType = "OVERVIEW", [FromQuery] string? dataset = null)
    {
        string actualReportType = string.IsNullOrWhiteSpace(dataset) ? reportType : dataset;
        using var conn = _connFactory.CreateConnection();
        var sb = new StringBuilder();

        switch (actualReportType.ToUpperInvariant())
        {
            case "OUTPATIENT":
                sb.AppendLine("科室ID,科室名称,就诊人次,收费金额");
                var opd = (await conn.QueryAsync<DeptStatDto>(@"
SELECT d.id AS DeptId, d.dept_name AS DeptName, COUNT(v.id) AS VisitCount, COALESCE(SUM(p.total_amount), 0) AS TotalAmount
FROM sys_departments d
LEFT JOIN opd_visits v ON v.opd_dept_id = d.id
LEFT JOIN fin_payment_orders p ON p.visit_or_encounter_id = v.id AND p.status = 1
WHERE d.dept_category = 'OPD' GROUP BY d.id, d.dept_name")).ToList();
                foreach (var item in opd)
                {
                    sb.AppendLine($"{item.DeptId},{item.DeptName},{item.VisitCount},{item.TotalAmount:F2}");
                }
                break;

            case "INPATIENT":
                sb.AppendLine("病区ID,病区名称,总床位数,占用床位,在院人数");
                var ipd = (await conn.QueryAsync<InpatientWardStatDto>(@"
SELECT w.id AS WardId, w.dept_name AS WardName, COUNT(b.id) AS TotalBeds,
       COALESCE(SUM(CASE WHEN b.bed_status = 1 THEN 1 ELSE 0 END), 0) AS OccupiedBeds,
       0 AS OccupancyRate, COUNT(DISTINCT e.id) AS InpatientCount
FROM sys_departments w
LEFT JOIN ipd_beds b ON b.ward_id = w.id
LEFT JOIN ipd_encounters e ON e.admit_ward_id = w.id AND e.status IN (1, 4)
WHERE w.dept_category = 'WARD' GROUP BY w.id, w.dept_name")).ToList();
                foreach (var item in ipd)
                {
                    sb.AppendLine($"{item.WardId},{item.WardName},{item.TotalBeds},{item.OccupiedBeds},{item.InpatientCount}");
                }
                break;

            default:
                sb.AppendLine("指标名称,数值,单位");
                var ovRes = await GetOverview();
                var d = ovRes.Data;
                sb.AppendLine($"今日门诊就诊,{d?.TodayOutpatientVisits ?? 0},人次");
                sb.AppendLine($"在院患者人数,{d?.ActiveInpatients ?? 0},人");
                sb.AppendLine($"今日总收入,{d?.TodayRevenue ?? 0:F2},元");
                sb.AppendLine($"药占比,{d?.DrugRevenueRatio ?? 0:F2},%");
                sb.AppendLine($"全院床位使用率,{d?.BedUtilizationRate ?? 0:F2},%");
                sb.AppendLine($"累计实名档案,{d?.TotalRegisteredPatients ?? 0},人");
                break;
        }

        byte[] bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"hospital_analytics_{reportType.ToLowerInvariant()}_{DateTime.Now:yyyyMMdd}.csv");
    }
}
