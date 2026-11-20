using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.EmrQc.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HIS.Modules.EmrQc.Services;

public class EmrQcTimelinessWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmrQcTimelinessWorker> _logger;

    public EmrQcTimelinessWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<EmrQcTimelinessWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EMR Level 7 Timeliness QC Worker started (Interval: 15 minutes).");

        // Short initial delay to allow application startup and database migration to settle
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanTimelinessAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error occurred during EMR timeliness QC scan cycle.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task ScanTimelinessAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var connFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        var alertPublisher = scope.ServiceProvider.GetService<IClinicalAlertPublisher>();
        var defectRepo = scope.ServiceProvider.GetService<IRepository<EmrQcDefect>>();

        using var conn = connFactory.CreateConnection();
        long now = SystemTime.UtcNowMs;

        // Query active inpatient encounters
        var encounters = (await conn.QueryAsync<(string Id, string PatientId, string WardId, string DoctorId, long CreatedAtMs)>(
            """
            SELECT id AS Id, patient_id AS PatientId, admit_ward_id AS WardId, attending_doctor_id AS DoctorId, created_at_ms AS CreatedAtMs
            FROM ipd_encounters
            WHERE status = 1
            LIMIT 200
            """)).AsList();

        foreach (var enc in encounters)
        {
            if (ct.IsCancellationRequested) break;

            long admitTime = enc.CreatedAtMs > 0 ? enc.CreatedAtMs : now;
            string doctorId = enc.DoctorId ?? "SYSTEM";

            // 1. Admission Note (24 hours)
            long admitDeadline = admitTime + 24L * 3600 * 1000;
            bool hasAdmitDoc = await conn.ExecuteScalarAsync<int>(
                "SELECT count(*) FROM emr_documents WHERE visit_or_encounter_id = @EncId AND (doc_type_code LIKE '%ADMISSION%' OR doc_title LIKE '%入院记录%')",
                new { EncId = enc.Id }) > 0;

            if (!hasAdmitDoc)
            {
                if (now > admitDeadline)
                {
                    await RecordDefectAndAlertAsync(conn, defectRepo, alertPublisher, enc.Id, enc.PatientId, doctorId,
                        "QC-TIME-ADMISSION-24H", "TIMELINESS", "入院记录书写超时（规定24小时内完成）", 10.0m, "入院记录", "【超时严重缺陷】患者入院记录已超期未书写，请立即补齐审签！");
                }
                else if (admitDeadline - now <= 2L * 3600 * 1000)
                {
                    if (alertPublisher != null)
                    {
                        await alertPublisher.PublishTimelinessAlertAsync(enc.Id, doctorId, "入院记录", "【时限预警】患者入院记录剩余书写时间不足2小时，请及时完成！");
                    }
                }
            }

            // 2. First Progress Note (8 hours)
            long firstCourseDeadline = admitTime + 8L * 3600 * 1000;
            bool hasFirstCourse = await conn.ExecuteScalarAsync<int>(
                "SELECT count(*) FROM emr_documents WHERE visit_or_encounter_id = @EncId AND (doc_type_code LIKE '%FIRST_COURSE%' OR doc_title LIKE '%首次病程%')",
                new { EncId = enc.Id }) > 0;

            if (!hasFirstCourse)
            {
                if (now > firstCourseDeadline)
                {
                    await RecordDefectAndAlertAsync(conn, defectRepo, alertPublisher, enc.Id, enc.PatientId, doctorId,
                        "QC-TIME-FIRST-COURSE-8H", "TIMELINESS", "首次病程记录书写超时（规定8小时内完成）", 5.0m, "首次病程", "【超时严重缺陷】首次病程记录已超期未完成！");
                }
                else if (firstCourseDeadline - now <= 2L * 3600 * 1000)
                {
                    if (alertPublisher != null)
                    {
                        await alertPublisher.PublishTimelinessAlertAsync(enc.Id, doctorId, "首次病程", "【时限预警】首次病程记录剩余书写时间不足2小时！");
                    }
                }
            }

            // 3. Rescue Note (6 hours) - if rescue order was executed
            var rescueOrder = await conn.QueryFirstOrDefaultAsync<(long CreatedAtMs, string ItemName)>(
                """
                SELECT created_at_ms AS CreatedAtMs, item_name AS ItemName
                FROM ord_medical_orders
                WHERE encounter_id = @EncId AND (item_name LIKE '%抢救%' OR item_code LIKE '%RESCUE%')
                ORDER BY created_at_ms DESC LIMIT 1
                """,
                new { EncId = enc.Id });

            if (rescueOrder.CreatedAtMs > 0)
            {
                long rescueDeadline = rescueOrder.CreatedAtMs + 6L * 3600 * 1000;
                bool hasRescueDoc = await conn.ExecuteScalarAsync<int>(
                    "SELECT count(*) FROM emr_documents WHERE visit_or_encounter_id = @EncId AND doc_title LIKE '%抢救%' AND created_at_ms >= @RescueTime",
                    new { EncId = enc.Id, RescueTime = rescueOrder.CreatedAtMs }) > 0;

                if (!hasRescueDoc)
                {
                    if (now > rescueDeadline)
                    {
                        await RecordDefectAndAlertAsync(conn, defectRepo, alertPublisher, enc.Id, enc.PatientId, doctorId,
                            "QC-TIME-RESCUE-6H", "TIMELINESS", "抢救记录书写超时（抢救结束后6小时内完成）", 10.0m, "抢救记录", "【超时严重缺陷】抢救记录已超6小时未书写完成！");
                    }
                    else if (rescueDeadline - now <= 2L * 3600 * 1000)
                    {
                        if (alertPublisher != null)
                        {
                            await alertPublisher.PublishTimelinessAlertAsync(enc.Id, doctorId, "抢救记录", "【时限预警】抢救记录距6小时截止不足2小时，请及时补齐！");
                        }
                    }
                }
            }

            // 4. Discharge Summary (24 hours) - if discharge request submitted
            var dischargeReq = await conn.QueryFirstOrDefaultAsync<(long CreatedAtMs, string Id)>(
                "SELECT created_at_ms AS CreatedAtMs, id AS Id FROM ipd_discharge_requests WHERE encounter_id = @EncId ORDER BY created_at_ms DESC LIMIT 1",
                new { EncId = enc.Id });

            if (dischargeReq.CreatedAtMs > 0)
            {
                long dischargeDeadline = dischargeReq.CreatedAtMs + 24L * 3600 * 1000;
                bool hasDischargeDoc = await conn.ExecuteScalarAsync<int>(
                    "SELECT count(*) FROM emr_documents WHERE visit_or_encounter_id = @EncId AND (doc_title LIKE '%出院小结%' OR doc_title LIKE '%出院记录%')",
                    new { EncId = enc.Id }) > 0;

                if (!hasDischargeDoc)
                {
                    if (now > dischargeDeadline)
                    {
                        await RecordDefectAndAlertAsync(conn, defectRepo, alertPublisher, enc.Id, enc.PatientId, doctorId,
                            "QC-TIME-DISCHARGE-24H", "TIMELINESS", "出院小结/出院记录书写超时（规定出院申请后24小时内完成）", 8.0m, "出院小结", "【超时严重缺陷】出院小结已超时未书写完成！");
                    }
                    else if (dischargeDeadline - now <= 2L * 3600 * 1000)
                    {
                        if (alertPublisher != null)
                        {
                            await alertPublisher.PublishTimelinessAlertAsync(enc.Id, doctorId, "出院小结", "【时限预警】出院记录剩余不足2小时！");
                        }
                    }
                }
            }
        }
    }

    private static async Task RecordDefectAndAlertAsync(
        System.Data.IDbConnection conn,
        IRepository<EmrQcDefect>? defectRepo,
        IClinicalAlertPublisher? alertPublisher,
        string encounterId,
        string patientId,
        string doctorId,
        string ruleCode,
        string dimension,
        string description,
        decimal deductPoints,
        string docType,
        string alertMsg)
    {
        long now = SystemTime.UtcNowMs;

        // Check if an open defect already exists for this rule and encounter to prevent spamming
        int exists = await conn.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM emr_qc_defects WHERE encounter_id = @EncId AND rule_code = @RuleCode AND status = 'OPEN'",
            new { EncId = encounterId, RuleCode = ruleCode });

        if (exists == 0 && defectRepo != null)
        {
            var defect = new EmrQcDefect
            {
                Id = Guid.NewGuid().ToString("D"),
                TaskId = Guid.NewGuid().ToString("D"),
                EncounterId = encounterId,
                RuleCode = ruleCode,
                QcDimension = dimension,
                DefectDescription = description,
                DeductPoints = deductPoints,
                ResponsibleDoctorId = doctorId,
                DeadlineMs = now + 12L * 3600 * 1000,
                Status = "OPEN",
                CreatedAtMs = now,
                CreatedBy = "EMR_QC_WORKER",
                UpdatedAtMs = now,
                UpdatedBy = "EMR_QC_WORKER"
            };
            await defectRepo.InsertAsync(defect);
        }

        if (alertPublisher != null)
        {
            await alertPublisher.PublishTimelinessAlertAsync(encounterId, doctorId, docType, alertMsg);
        }
    }
}
