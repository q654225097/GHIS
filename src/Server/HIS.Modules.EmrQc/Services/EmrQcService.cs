using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.EmrQc.Models;

namespace HIS.Modules.EmrQc.Services;

public class EmrQcService : IEmrQcContract
{
    private readonly IDbConnectionFactory _connFactory;
    private readonly IRepository<EmrQcTask> _taskRepo;
    private readonly IRepository<EmrQcDefect> _defectRepo;

    public EmrQcService(
        IDbConnectionFactory connFactory,
        IRepository<EmrQcTask> taskRepo,
        IRepository<EmrQcDefect> defectRepo)
    {
        _connFactory = connFactory;
        _taskRepo = taskRepo;
        _defectRepo = defectRepo;
    }

    public async Task<EmrQcReportDto> RunAutoQcAsync(
        string encounterId,
        CancellationToken ct = default)
    {
        using var conn = _connFactory.CreateConnection();

        // 1. Query encounter info
        var enc = await conn.QuerySingleOrDefaultAsync<(string PatientId, string AttendingDoctorId, long CreatedAtMs)>(
            "SELECT patient_id, attending_doctor_id, created_at_ms FROM ipd_encounters WHERE id = @Id",
            new { Id = encounterId });

        string doctorId = enc.AttendingDoctorId ?? "SYSTEM";
        string patientId = enc.PatientId ?? string.Empty;
        long now = SystemTime.UtcNowMs;

        var defects = new List<EmrQcDefectDto>();
        decimal totalDeduct = 0m;

        // Create Task
        var task = new EmrQcTask
        {
            Id = Guid.NewGuid().ToString("D"),
            EncounterId = encounterId,
            PatientId = patientId,
            TaskType = "AUTO_PROCESS",
            TotalScore = 100m,
            Grade = "A",
            Status = "PENDING",
            ReviewedAtMs = now,
            ReviewerId = "AUTO_QC_ROBOT",
            CreatedAtMs = now,
            CreatedBy = "AUTO_QC_ROBOT",
            UpdatedAtMs = now,
            UpdatedBy = "AUTO_QC_ROBOT"
        };

        // Check 1: 24h 入院记录时限
        long admitTime = enc.CreatedAtMs > 0 ? enc.CreatedAtMs : now - 3600000;
        var admitDoc = await conn.QueryFirstOrDefaultAsync<(string Id, long CreatedAtMs)>(
            "SELECT id, created_at_ms FROM emr_documents WHERE visit_or_encounter_id = @EncounterId AND doc_type_code LIKE '%ADMISSION%'",
            new { EncounterId = encounterId });

        if (admitDoc.Id == null)
        {
            if (now - admitTime > 24 * 3600 * 1000)
            {
                var d = new EmrQcDefect
                {
                    Id = Guid.NewGuid().ToString("D"),
                    TaskId = task.Id,
                    EncounterId = encounterId,
                    RuleCode = "QC-TIME-ADMISSION-24H",
                    QcDimension = "TIMELINESS",
                    DefectDescription = "入院记录未在患者入院后 24 小时内完成书写并审签归档",
                    DeductPoints = 10.0m,
                    ResponsibleDoctorId = doctorId,
                    DeadlineMs = now + 12 * 3600 * 1000,
                    Status = "OPEN",
                    CreatedAtMs = now,
                    CreatedBy = "AUTO_QC_ROBOT",
                    UpdatedAtMs = now,
                    UpdatedBy = "AUTO_QC_ROBOT"
                };
                await _defectRepo.InsertAsync(d);
                totalDeduct += 10.0m;
                defects.Add(new EmrQcDefectDto(d.Id, encounterId, null, d.RuleCode, d.QcDimension, d.DefectDescription, d.DeductPoints, d.Status));
            }
        }

        // Check 2: 首次病程 8h 时限
        var firstCourse = await conn.QueryFirstOrDefaultAsync<(string Id, long CreatedAtMs)>(
            "SELECT id, created_at_ms FROM emr_documents WHERE visit_or_encounter_id = @EncounterId AND doc_type_code LIKE '%FIRST_COURSE%'",
            new { EncounterId = encounterId });

        if (firstCourse.Id == null && (now - admitTime > 8 * 3600 * 1000))
        {
            var d = new EmrQcDefect
            {
                Id = Guid.NewGuid().ToString("D"),
                TaskId = task.Id,
                EncounterId = encounterId,
                RuleCode = "QC-TIME-FIRST-COURSE-8H",
                QcDimension = "TIMELINESS",
                DefectDescription = "首次病程记录未在患者入院后 8 小时内完成",
                DeductPoints = 5.0m,
                ResponsibleDoctorId = doctorId,
                DeadlineMs = now + 6 * 3600 * 1000,
                Status = "OPEN",
                CreatedAtMs = now,
                CreatedBy = "AUTO_QC_ROBOT",
                UpdatedAtMs = now,
                UpdatedBy = "AUTO_QC_ROBOT"
            };
            await _defectRepo.InsertAsync(d);
            totalDeduct += 5.0m;
            defects.Add(new EmrQcDefectDto(d.Id, encounterId, null, d.RuleCode, d.QcDimension, d.DefectDescription, d.DeductPoints, d.Status));
        }

        // Check 3: 合理用药审方缺陷联动 (DRUG_RATIONALITY)
        int irrationalCount = await conn.ExecuteScalarAsync<int>(
            """
            SELECT count(*) FROM ord_order_review_logs
            WHERE encounter_or_visit_id = @EncounterId AND is_blocked = 1 AND override_reason IS NULL
            """,
            new { EncounterId = encounterId });

        if (irrationalCount > 0)
        {
            var d = new EmrQcDefect
            {
                Id = Guid.NewGuid().ToString("D"),
                TaskId = task.Id,
                EncounterId = encounterId,
                RuleCode = "QC-DRUG-UNRATIONAL-ORDERS",
                QcDimension = "DRUG_RATIONALITY",
                DefectDescription = $"存在 {irrationalCount} 条合理用药强阻断但未录入合理覆盖理由的医嘱",
                DeductPoints = 5.0m,
                ResponsibleDoctorId = doctorId,
                DeadlineMs = now + 24 * 3600 * 1000,
                Status = "OPEN",
                CreatedAtMs = now,
                CreatedBy = "AUTO_QC_ROBOT",
                UpdatedAtMs = now,
                UpdatedBy = "AUTO_QC_ROBOT"
            };
            await _defectRepo.InsertAsync(d);
            totalDeduct += 5.0m;
            defects.Add(new EmrQcDefectDto(d.Id, encounterId, null, d.RuleCode, d.QcDimension, d.DefectDescription, d.DeductPoints, d.Status));
        }

        // Check 4: 医院感染上报内涵质控联动 (INFECTION_CONTROL)
        int unclosedInfection = await conn.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM ipc_infection_reports WHERE encounter_id = @EncounterId AND status = 'REPORTED'",
            new { EncounterId = encounterId });

        if (unclosedInfection > 0)
        {
            var d = new EmrQcDefect
            {
                Id = Guid.NewGuid().ToString("D"),
                TaskId = task.Id,
                EncounterId = encounterId,
                RuleCode = "QC-IPC-UNCLOSED-REPORT",
                QcDimension = "INFECTION_CONTROL",
                DefectDescription = "存在院感上报但未完成专科病历隔离或病原学转归记录",
                DeductPoints = 5.0m,
                ResponsibleDoctorId = doctorId,
                DeadlineMs = now + 48 * 3600 * 1000,
                Status = "OPEN",
                CreatedAtMs = now,
                CreatedBy = "AUTO_QC_ROBOT",
                UpdatedAtMs = now,
                UpdatedBy = "AUTO_QC_ROBOT"
            };
            await _defectRepo.InsertAsync(d);
            totalDeduct += 5.0m;
            defects.Add(new EmrQcDefectDto(d.Id, encounterId, null, d.RuleCode, d.QcDimension, d.DefectDescription, d.DeductPoints, d.Status));
        }

        decimal finalScore = Math.Max(0m, 100m - totalDeduct);
        string grade = finalScore >= 90m ? "A" : (finalScore >= 75m ? "B" : "C");

        task.TotalScore = finalScore;
        task.Grade = grade;
        task.Status = defects.Count > 0 ? "DEFECT_ISSUED" : "CLOSED";
        await _taskRepo.InsertAsync(task);

        return new EmrQcReportDto(
            TaskId: task.Id,
            EncounterId: encounterId,
            TotalScore: finalScore,
            Grade: grade,
            Defects: defects
        );
    }
}
