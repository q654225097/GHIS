using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.Modules.Outpatient.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Outpatient.Controllers;

public partial class OutpatientController
{
    // ==========================================
    // Outpatient Level 7 Closed-Loop APIs
    // ==========================================

    public sealed record PrescriptionExecutionTrackDto(
        string PrescriptionId,
        string PrescriptionNo,
        string PatientId,
        string DoctorName,
        decimal TotalAmount,
        string CurrentStatus,
        long PrescribedAtMs,
        long? AuditedAtMs,
        long? PaidAtMs,
        long? DispensedAtMs,
        long? AdministeredAtMs,
        IReadOnlyList<string> DrugItems
    );

    [HttpGet("prescriptions/execution-track/{patientId}")]
    public async Task<ApiResponse<IReadOnlyList<PrescriptionExecutionTrackDto>>> GetPrescriptionExecutionTrack(string patientId)
    {
        if (string.IsNullOrWhiteSpace(patientId))
            return ApiResponse<IReadOnlyList<PrescriptionExecutionTrackDto>>.Fail(400, "必须提供患者ID");

        await using var conn = _connFactory.CreateConnection();
        var rxList = (await conn.QueryAsync<OpdPrescription>(
            "SELECT * FROM opd_prescriptions WHERE patient_id = @PatId ORDER BY created_at_ms DESC LIMIT 30",
            new { PatId = patientId.Trim() })).ToList();

        var result = new List<PrescriptionExecutionTrackDto>();
        foreach (var rx in rxList)
        {
            var items = (await conn.QueryAsync<string>(
                "SELECT drug_name_snapshot || ' ' || single_dose || dose_unit || ' ' || frequency_code || ' ' || route_name FROM opd_prescription_items WHERE prescription_id = @RxId AND is_deleted = 0",
                new { RxId = rx.Id })).ToList();

            string statusName = rx.Status switch
            {
                0 => "待提交",
                1 => "前置审方通过/待缴费",
                2 => "审方驳回",
                3 => "已缴费/待配药",
                4 => "已发药",
                5 => "已执行/注射完成",
                6 => "待人工审方",
                _ => "未知状态"
            };

            long? auditedAt = rx.Status >= 1 && rx.Status != 6 ? rx.UpdatedAtMs : null;
            long? paidAt = rx.Status is 3 or 4 or 5 ? rx.UpdatedAtMs : null;
            long? dispensedAt = rx.Status is 4 or 5 ? rx.UpdatedAtMs : null;
            long? adminAt = rx.Status == 5 ? rx.UpdatedAtMs : null;

            result.Add(new PrescriptionExecutionTrackDto(
                PrescriptionId: rx.Id,
                PrescriptionNo: rx.PrescriptionNo,
                PatientId: rx.PatientId,
                DoctorName: rx.DoctorId,
                TotalAmount: rx.TotalAmount,
                CurrentStatus: statusName,
                PrescribedAtMs: rx.CreatedAtMs,
                AuditedAtMs: auditedAt,
                PaidAtMs: paidAt,
                DispensedAtMs: dispensedAt,
                AdministeredAtMs: adminAt,
                DrugItems: items
            ));
        }

        return ApiResponse<IReadOnlyList<PrescriptionExecutionTrackDto>>.Success(result);
    }

    [HttpGet("prescriptions/comment-feedbacks")]
    public async Task<ApiResponse<IReadOnlyList<dynamic>>> GetPrescriptionCommentFeedbacks(
        [FromQuery] string? doctorId = null)
    {
        await using var conn = _connFactory.CreateConnection();
        string targetDoc = doctorId ?? _userSession?.UserId ?? "";

        var feedbacks = await conn.QueryAsync(@"
            SELECT c.id, c.prescription_id, p.prescription_no, c.doctor_id, c.pharmacist_id, c.comment_category, c.comment_detail, c.doctor_appeal_note, c.appeal_status, c.created_at_ms, c.updated_at_ms
            FROM pha_prescription_comments c
            INNER JOIN opd_prescriptions p ON c.prescription_id = p.id
            WHERE (@DocId = '' OR c.doctor_id = @DocId)
            ORDER BY c.created_at_ms DESC LIMIT 50",
            new { DocId = targetDoc });

        return ApiResponse<IReadOnlyList<dynamic>>.Success(feedbacks.ToList());
    }

    [HttpPost("visits/{visitId}/sync-to-emr")]
    public async Task<ApiResponse<dynamic>> SyncVisitToEmr(string visitId)
    {
        if (string.IsNullOrWhiteSpace(visitId))
            return ApiResponse<dynamic>.Fail(400, "必须指定就诊记录ID");

        await using var conn = _connFactory.CreateConnection();
        var visit = await conn.QuerySingleOrDefaultAsync<OpdVisit>(
            "SELECT * FROM opd_visits WHERE id = @Id OR visit_no = @Id", new { Id = visitId });
        if (visit == null)
            return ApiResponse<dynamic>.Fail(404, "门诊就诊记录不存在");

        var rxItems = (await conn.QueryAsync(@"
            SELECT i.drug_name_snapshot, i.dose_once, i.dose_unit, i.frequency_code, i.route_code
            FROM opd_prescription_items i
            INNER JOIN opd_prescriptions p ON i.prescription_id = p.id
            WHERE p.visit_id = @Vid AND i.is_deleted = 0",
            new { Vid = visit.Id })).ToList();

        var rxSummary = string.Join("; ", rxItems.Select(i => $"{i.drug_name_snapshot} {i.dose_once}{i.dose_unit} {i.frequency_code} {i.route_code}"));
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string fullSummary = $"【就诊主诉】常规门诊就诊\n【初步诊断】{visit.PrimaryDiagName ?? "临床待查"} ({visit.PrimaryDiagCode ?? "R69"})\n【处方记录】{(string.IsNullOrWhiteSpace(rxSummary) ? "未开立处方" : rxSummary)}";

        var existingDoc = await conn.QuerySingleOrDefaultAsync<dynamic>(
            "SELECT id FROM emr_documents WHERE visit_or_encounter_id = @Vid AND emr_scene = 'OPD' LIMIT 1",
            new { Vid = visit.Id });

        if (existingDoc != null)
        {
            await conn.ExecuteAsync(@"
                UPDATE emr_documents
                SET plain_text_summary = @Summary, updated_at_ms = @Now, updated_by = @User
                WHERE id = @Id",
                new { Summary = fullSummary, Now = nowMs, User = _userSession?.UserId ?? visit.DoctorId, Id = (string)existingDoc.id });
        }
        else
        {
            string docId = Guid.NewGuid().ToString();
            string docNo = "EMR-OPD-" + (visit.VisitNo ?? visit.Id);
            await conn.ExecuteAsync(@"
                INSERT INTO emr_documents (id, doc_no, campus_id, emr_scene, visit_or_encounter_id, patient_id, dept_id, template_id, template_version, doc_type_code, doc_title, clinical_time_ms, plain_text_summary, status, author_id, schema_version, created_at_ms, updated_at_ms, version_no)
                VALUES (@Id, @No, 'CAMPUS-01', 'OPD', @Vid, @PatId, @DeptId, 'TPL-OPD-REC', 1, 'OPD_RECORD', '门诊初诊病历', @Now, @Summary, 0, @Author, 1, @Now, @Now, 1)",
                new
                {
                    Id = docId,
                    No = docNo,
                    Vid = visit.Id,
                    PatId = visit.PatientId,
                    DeptId = visit.OpdDeptId,
                    Now = nowMs,
                    Summary = fullSummary,
                    Author = visit.DoctorId
                });
        }

        return ApiResponse<dynamic>.Success(new { VisitId = visit.Id, PatientId = visit.PatientId, Status = "SYNCED" }, "门诊就诊记录及处方信息已实时同步至电子病历");
    }

    public sealed record PreTriageVitalsDto(
        decimal? TemperatureC,
        int? PulseBpm,
        int? SysBp,
        int? DiaBp,
        decimal? Spo2,
        decimal? WeightKg,
        string? TriageNurseNotes = null
    );

    [HttpPost("triage/{queueId}/pre-vitals")]
    public async Task<ApiResponse<dynamic>> RecordPreTriageVitals(string queueId, [FromBody] PreTriageVitalsDto req)
    {
        if (string.IsNullOrWhiteSpace(queueId))
            return ApiResponse<dynamic>.Fail(400, "分诊队列ID不能为空");

        await using var conn = _connFactory.CreateConnection();
        var queueItem = await conn.QuerySingleOrDefaultAsync<OpdTriageQueue>(
            "SELECT * FROM opd_triage_queues WHERE id = @Id", new { Id = queueId });

        if (queueItem == null)
            return ApiResponse<dynamic>.Fail(404, "分诊队列项不存在");

        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string vitalsJson = JsonSerializer.Serialize(new
        {
            req.TemperatureC,
            req.PulseBpm,
            req.SysBp,
            req.DiaBp,
            req.Spo2,
            req.WeightKg,
            req.TriageNurseNotes
        });

        // 1. Update queue
        await conn.ExecuteAsync(@"
            UPDATE opd_triage_queues
            SET ext_data_json = @Vitals, updated_at_ms = @Now, updated_by = @Nurse
            WHERE id = @Id",
            new { Vitals = vitalsJson, Now = nowMs, Nurse = _userSession?.UserId ?? "TRIAGE_NURSE", Id = queueId });

        string patientId = await conn.QuerySingleOrDefaultAsync<string>(
            "SELECT patient_id FROM opd_visits WHERE id = @Vid LIMIT 1",
            new { Vid = queueItem.VisitId }) ?? "PATIENT-OPD";

        // 2. Insert into nur_vitals_signs
        string vitalId = Guid.NewGuid().ToString();
        await conn.ExecuteAsync(@"
            INSERT INTO nur_vitals_signs (id, encounter_id, patient_id, temperature_c, pulse_bpm, sys_bp, dia_bp, spo2, weight_kg, recorded_at_ms, recorded_by, schema_version, created_at_ms, updated_at_ms, version_no)
            VALUES (@Id, @EncId, @PatId, @Temp, @Pulse, @Sys, @Dia, @Spo2, @Weight, @Now, @Nurse, 1, @Now, @Now, 1)",
            new
            {
                Id = vitalId,
                EncId = queueItem.VisitId ?? queueId,
                PatId = patientId,
                Temp = req.TemperatureC,
                Pulse = req.PulseBpm,
                Sys = req.SysBp,
                Dia = req.DiaBp,
                Spo2 = req.Spo2,
                Weight = req.WeightKg,
                Now = nowMs,
                Nurse = _userSession?.UserId ?? "TRIAGE_NURSE"
            });

        return ApiResponse<dynamic>.Success(new
        {
            QueueId = queueId,
            PatientId = patientId,
            RecordedAtMs = nowMs
        }, "诊前预检生命体征录入成功，已同步至医师工作站与护理体征库");
    }
}
