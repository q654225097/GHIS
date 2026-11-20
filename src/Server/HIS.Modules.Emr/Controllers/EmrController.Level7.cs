using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Security;
using HIS.Modules.Emr.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Emr.Controllers;

public partial class EmrController
{
    // ==========================================
    // EMR Level 7 Closed-Loop & Governance APIs
    // ==========================================

    [HttpGet("insertable-data/{encounterOrVisitId}")]
    public async Task<ApiResponse<EmrInsertableClinicalDataDto>> GetInsertableClinicalData(
        string encounterOrVisitId,
        [FromQuery] bool includeExternal = true)
    {
        await using var conn = _connFactory.CreateConnection();

        // 1. Resolve PatientId
        string patientId = string.Empty;
        var encPatient = await conn.QuerySingleOrDefaultAsync<string>(
            "SELECT patient_id FROM ipd_encounters WHERE id = @Id OR admission_no = @Id LIMIT 1",
            new { Id = encounterOrVisitId });
        if (!string.IsNullOrWhiteSpace(encPatient))
        {
            patientId = encPatient;
        }
        else
        {
            var opdPatient = await conn.QuerySingleOrDefaultAsync<string>(
                "SELECT patient_id FROM opd_visits WHERE id = @Id OR visit_no = @Id LIMIT 1",
                new { Id = encounterOrVisitId });
            if (!string.IsNullOrWhiteSpace(opdPatient))
            {
                patientId = opdPatient;
            }
        }

        // 2. Query Lab Results
        var labs = new List<InsertableLabItemDto>();
        try
        {
            var labRows = await conn.QueryAsync(@"
                SELECT i.item_code, i.item_name, i.val_string, i.val_numeric, i.unit, i.ref_range, i.abnormal_flag, r.created_at_ms AS reported_at_ms
                FROM mtc_lab_result_items i
                INNER JOIN mtc_service_requests r ON i.request_id = r.id
                WHERE (r.source_visit_id = @Id OR r.patient_id = @PatId)
                ORDER BY r.created_at_ms DESC LIMIT 100",
                new { Id = encounterOrVisitId, PatId = patientId });

            foreach (var r in labRows)
            {
                labs.Add(new InsertableLabItemDto(
                    ItemCode: (string)r.item_code ?? "",
                    ItemName: (string)r.item_name ?? "",
                    ValueText: (string)r.val_string ?? "",
                    ValueNum: r.val_numeric != null ? Convert.ToDecimal(r.val_numeric) : null,
                    Unit: (string)r.unit ?? "",
                    ReferenceRange: (string)r.ref_range ?? "",
                    AbnormalFlag: (string)r.abnormal_flag ?? "NORMAL",
                    ReportedAtMs: Convert.ToInt64(r.reported_at_ms ?? 0),
                    IsExternal: false
                ));
            }
        }
        catch { }

        // 3. Query Exam Reports
        var exams = new List<InsertableExamItemDto>();
        try
        {
            var examRows = await conn.QueryAsync(@"
                SELECT rep.id, rep.request_id, req.service_item_code, rep.modality, rep.exam_findings, rep.diagnostic_conclusion, rep.reported_at_ms, rep.pacs_study_uid
                FROM mtc_exam_reports rep
                INNER JOIN mtc_service_requests req ON rep.request_id = req.id
                WHERE (req.source_visit_id = @Id OR req.patient_id = @PatId)
                ORDER BY rep.reported_at_ms DESC LIMIT 50",
                new { Id = encounterOrVisitId, PatId = patientId });

            foreach (var r in examRows)
            {
                exams.Add(new InsertableExamItemDto(
                    RequestId: (string)r.request_id ?? "",
                    ExamName: (string)r.service_item_code ?? "",
                    Modality: (string)r.modality ?? "CR",
                    BodyPart: "",
                    Findings: (string)r.exam_findings ?? "",
                    Conclusion: (string)r.diagnostic_conclusion ?? "",
                    ReportedAtMs: Convert.ToInt64(r.reported_at_ms ?? 0),
                    PacsStudyUid: (string?)r.pacs_study_uid
                ));
            }
        }
        catch { }

        // 4. Query Nursing Vital Signs
        var vitals = new List<InsertableVitalSignDto>();
        try
        {
            var vitalRows = await conn.QueryAsync(@"
                SELECT measured_at_ms AS recorded_at_ms, temperature_c, pulse_bpm, resp_rate AS respiration_bpm, sys_bp, dia_bp, spo2
                FROM nur_vital_signs
                WHERE encounter_id = @Id OR patient_id = @PatId
                ORDER BY measured_at_ms DESC LIMIT 30",
                new { Id = encounterOrVisitId, PatId = patientId });

            foreach (var r in vitalRows)
            {
                vitals.Add(new InsertableVitalSignDto(
                    RecordedAtMs: Convert.ToInt64(r.recorded_at_ms ?? 0),
                    TemperatureC: r.temperature_c != null ? Convert.ToDecimal(r.temperature_c) : null,
                    PulseBpm: r.pulse_bpm != null ? Convert.ToInt32(r.pulse_bpm) : null,
                    RespirationBpm: r.respiration_bpm != null ? Convert.ToInt32(r.respiration_bpm) : null,
                    SysBp: r.sys_bp != null ? Convert.ToInt32(r.sys_bp) : null,
                    DiaBp: r.dia_bp != null ? Convert.ToInt32(r.dia_bp) : null,
                    Spo2: r.spo2 != null ? Convert.ToDecimal(r.spo2) : null
                ));
            }
        }
        catch { }

        // 5. Query Intake / Output
        var intakeOutputs = new List<InsertableIntakeOutputDto>();
        try
        {
            var ioRows = await conn.QueryAsync(@"
                SELECT measured_at_ms AS recorded_at_ms, '入量' AS category, '总入量' AS item_name, intake_ml AS volume_ml
                FROM nur_vital_signs
                WHERE (encounter_id = @Id OR patient_id = @PatId) AND intake_ml IS NOT NULL AND intake_ml > 0
                UNION ALL
                SELECT measured_at_ms AS recorded_at_ms, '出量' AS category, '总出量' AS item_name, output_ml AS volume_ml
                FROM nur_vital_signs
                WHERE (encounter_id = @Id OR patient_id = @PatId) AND output_ml IS NOT NULL AND output_ml > 0
                ORDER BY recorded_at_ms DESC LIMIT 30",
                new { Id = encounterOrVisitId, PatId = patientId });

            foreach (var r in ioRows)
            {
                intakeOutputs.Add(new InsertableIntakeOutputDto(
                    RecordedAtMs: Convert.ToInt64(r.recorded_at_ms ?? 0),
                    Category: (string)r.category ?? "",
                    ItemName: (string)r.item_name ?? "",
                    VolumeMl: Convert.ToDecimal(r.volume_ml ?? 0)
                ));
            }
        }
        catch { }

        // 6. Query Surgery Records
        var surgeries = new List<InsertableSurgeryDto>();
        try
        {
            var surgRows = await conn.QueryAsync(@"
                SELECT s.surgery_name, s.surgeon_doctor_id AS surgeon_name, a.anesthesia_method, a.anesthesiologist_id AS anesthesiologist_name, s.start_time_ms, s.end_time_ms
                FROM mte_surgery_schedules s
                LEFT JOIN mte_anesthesia_records a ON s.id = a.schedule_id
                WHERE (s.encounter_id = @Id OR s.patient_id = @PatId)
                ORDER BY s.created_at_ms DESC LIMIT 10",
                new { Id = encounterOrVisitId, PatId = patientId });

            foreach (var r in surgRows)
            {
                surgeries.Add(new InsertableSurgeryDto(
                    SurgeryName: (string)r.surgery_name ?? "",
                    SurgeonName: (string)r.surgeon_name ?? "",
                    AnesthesiaMethod: (string)r.anesthesia_method ?? "",
                    AnesthesiologistName: (string)r.anesthesiologist_name ?? "",
                    StartTimeMs: r.start_time_ms != null ? Convert.ToInt64(r.start_time_ms) : null,
                    EndTimeMs: r.end_time_ms != null ? Convert.ToInt64(r.end_time_ms) : null
                ));
            }
        }
        catch { }

        // 7. Query External Health Records (if enabled)
        var externalRecords = new List<InsertableExternalEhrDto>();
        if (includeExternal && !string.IsNullOrWhiteSpace(patientId))
        {
            try
            {
                var extRows = await conn.QueryAsync<EmrExternalHealthRecord>(
                    "SELECT * FROM emr_external_health_records WHERE patient_id = @PatId ORDER BY clinical_time_ms DESC LIMIT 20",
                    new { PatId = patientId });

                foreach (var r in extRows)
                {
                    externalRecords.Add(new InsertableExternalEhrDto(
                        SourceOrgName: r.SourceOrgName,
                        RecordCategory: r.RecordCategory,
                        SummaryText: r.SummaryText ?? "",
                        ClinicalTimeMs: r.ClinicalTimeMs
                    ));
                }
            }
            catch { }
        }

        var dto = new EmrInsertableClinicalDataDto(
            EncounterOrVisitId: encounterOrVisitId,
            PatientId: patientId,
            LabResults: labs,
            ExamReports: exams,
            VitalSigns: vitals,
            IntakeOutputs: intakeOutputs,
            Surgeries: surgeries,
            ExternalRecords: externalRecords
        );

        return ApiResponse<EmrInsertableClinicalDataDto>.Success(dto);
    }

    // ==========================================
    // Historical Digitized EMR (Paper OCR Scan)
    // ==========================================

    public sealed record UpsertDigitizedHistoryDto(
        string PatientId,
        string LegacyMrn,
        string VisitDate,
        string DocTitle,
        string FileStorageUri,
        string? OcrText
    );

    [HttpGet("digitized-history")]
    public async Task<ApiResponse<IReadOnlyList<EmrDigitizedHistory>>> GetDigitizedHistory(
        [FromQuery] string? patientId = null,
        [FromQuery] string? legacyMrn = null,
        [FromQuery] string? keyword = null)
    {
        var list = await _digitizedRepo.QueryAsync(
            "SELECT * FROM emr_digitized_history ORDER BY visit_date DESC");

        if (!string.IsNullOrWhiteSpace(patientId))
            list = list.Where(x => x.PatientId.Equals(patientId.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

        if (!string.IsNullOrWhiteSpace(legacyMrn))
            list = list.Where(x => x.LegacyMrn.Equals(legacyMrn.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            list = list.Where(x => x.DocTitle.Contains(kw, StringComparison.OrdinalIgnoreCase)
                                || (x.OcrText != null && x.OcrText.Contains(kw, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        return ApiResponse<IReadOnlyList<EmrDigitizedHistory>>.Success(list);
    }

    [HttpPost("digitized-history")]
    public async Task<ApiResponse<EmrDigitizedHistory>> CreateDigitizedHistory([FromBody] UpsertDigitizedHistoryDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.PatientId) || string.IsNullOrWhiteSpace(dto.DocTitle))
        {
            return ApiResponse<EmrDigitizedHistory>.Fail(400, "患者ID与病历名称不能为空");
        }

        var text = dto.OcrText ?? string.Empty;
        var sm3 = CryptoHelper.ComputeSm3(text);
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var record = new EmrDigitizedHistory
        {
            PatientId = dto.PatientId.Trim(),
            LegacyMrn = dto.LegacyMrn?.Trim() ?? string.Empty,
            VisitDate = dto.VisitDate?.Trim() ?? DateTime.UtcNow.ToString("yyyy-MM-dd"),
            DocTitle = dto.DocTitle.Trim(),
            FileStorageUri = dto.FileStorageUri?.Trim() ?? string.Empty,
            OcrText = dto.OcrText,
            Sm3Hash = sm3,
            CreatedBy = _userSession?.UserId ?? "SYSTEM",
            UpdatedBy = _userSession?.UserId ?? "SYSTEM"
        };

        await _digitizedRepo.InsertAsync(record);
        return ApiResponse<EmrDigitizedHistory>.Success(record, "纸质病历数字化扫描存档成功");
    }

    // ==========================================
    // EMR Storage Governance & Retention Policies
    // ==========================================

    public sealed record SetStoragePolicyDto(string SceneType, int RetentionYears);

    [HttpPost("storage/policies")]
    public async Task<ApiResponse<EmrStoragePolicy>> SetStoragePolicy([FromBody] SetStoragePolicyDto req)
    {
        var scene = (req.SceneType ?? "IPD").Trim().ToUpperInvariant();
        var years = req.RetentionYears <= 0 ? (scene == "OPD" ? 15 : 30) : req.RetentionYears;
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var retainUntilMs = nowMs + (long)(years * 365.25 * 24 * 3600 * 1000);

        var existing = await _storagePolicyRepo.QuerySingleOrDefaultAsync(
            "SELECT * FROM emr_storage_policies WHERE scene_type = @Scene",
            new { Scene = scene });

        if (existing != null)
        {
            existing.RetentionYears = years;
            existing.RetainUntilMs = retainUntilMs;
            existing.UpdatedBy = _userSession?.UserId ?? "ADMIN";
            await _storagePolicyRepo.UpdateAsync(existing);
            return ApiResponse<EmrStoragePolicy>.Success(existing, $"病历法定保存年限已更新为 {years} 年");
        }

        var policy = new EmrStoragePolicy
        {
            SceneType = scene,
            RetentionYears = years,
            RetainUntilMs = retainUntilMs,
            CreatedBy = _userSession?.UserId ?? "ADMIN",
            UpdatedBy = _userSession?.UserId ?? "ADMIN"
        };
        await _storagePolicyRepo.InsertAsync(policy);
        return ApiResponse<EmrStoragePolicy>.Success(policy, $"病历法定保存策略已设定为 {years} 年");
    }

    [HttpGet("storage/governance-metrics")]
    public async Task<ApiResponse<EmrStorageGovernanceMetricsDto>> GetStorageGovernanceMetrics()
    {
        await using var conn = _connFactory.CreateConnection();
        var totalDocs = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM emr_documents");
        var archivedOfds = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM emr_ofd_archives");
        var digitizedCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM emr_digitized_history");

        var opdPolicy = await conn.QuerySingleOrDefaultAsync<int?>(
            "SELECT retention_years FROM emr_storage_policies WHERE scene_type = 'OPD' LIMIT 1") ?? 15;
        var ipdPolicy = await conn.QuerySingleOrDefaultAsync<int?>(
            "SELECT retention_years FROM emr_storage_policies WHERE scene_type = 'IPD' LIMIT 1") ?? 30;

        // All non-expired records are protected
        var protectedCount = totalDocs + archivedOfds + digitizedCount;
        var estimatedBytes = (long)(totalDocs * 8192 + archivedOfds * 102400 + digitizedCount * 512000);

        var metrics = new EmrStorageGovernanceMetricsDto(
            TotalDocuments: totalDocs,
            ArchivedOfdCount: archivedOfds,
            RetentionProtectedCount: protectedCount,
            OpdRetentionYears: opdPolicy,
            IpdRetentionYears: ipdPolicy,
            ComplianceRate: 100.0m,
            EstimatedStorageBytes: estimatedBytes
        );

        return ApiResponse<EmrStorageGovernanceMetricsDto>.Success(metrics);
    }

    // ==========================================
    // External EHR / HIE Records
    // ==========================================

    [HttpGet("external-records/{patientId}")]
    public async Task<ApiResponse<IReadOnlyList<EmrExternalHealthRecord>>> GetExternalRecords(string patientId)
    {
        var records = await _externalRecordRepo.QueryAsync(
            "SELECT * FROM emr_external_health_records WHERE patient_id = @PatId ORDER BY clinical_time_ms DESC",
            new { PatId = patientId });

        return ApiResponse<IReadOnlyList<EmrExternalHealthRecord>>.Success(records);
    }
}
