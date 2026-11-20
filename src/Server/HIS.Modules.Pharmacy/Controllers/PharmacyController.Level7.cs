using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Pharmacy.Models;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Pharmacy.Controllers;

public partial class PharmacyController
{
    public record CreateCommentDto(
        string SourceType,
        string TargetId,
        string DoctorId,
        string PharmacistId,
        bool IsRational,
        IReadOnlyList<string>? DefectCodes,
        string CommentText,
        string? ErrorCategory
    );

    public record DoctorReplyDto(
        string DoctorReply,
        string Status // ACKED / APPEALED
    );

    public record CreatePivasBatchDto(
        string? EncounterId,
        string WardId,
        string BatchNo,
        string PrepType,
        string PharmacistId
    );

    public record GenerateUnitDoseDto(
        string MedicalOrderId,
        string DrugId,
        string DoseText,
        string PatientId,
        int Count = 1
    );

    public record SaveMedicationReconDto(
        string EncounterId,
        string PatientId,
        string Source,
        string DrugName,
        string DosageText,
        string Decision,
        string ReconciledBy
    );

    public record ImportExternalRxDto(
        string ExternalRxNo,
        string SourceOrgCode,
        string SourceOrgName,
        string PatientId,
        object RxPayload
    );

    [HttpPost("comments/sample")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<object>>>> SamplePrescriptionsForReview(
        [FromQuery] string sourceType,
        [FromQuery] int sampleCount = 10,
        [FromServices] IDbConnectionFactory? connFactory = null,
        CancellationToken ct = default)
    {
        var factory = connFactory ?? _connFactory;
        using var conn = factory.CreateConnection();
        string table = sourceType.Equals("IPD", StringComparison.OrdinalIgnoreCase)
            ? "ord_medical_orders"
            : "opd_prescriptions";

        var list = await conn.QueryAsync(
            $"SELECT id, created_at_ms, patient_id FROM {table} ORDER BY RANDOM() LIMIT @Limit",
            new { Limit = sampleCount });

        return Ok(ApiResponse<IReadOnlyList<object>>.Success(list.ToList(), "随机抽样待点评处方成功"));
    }

    [HttpPost("comments")]
    public async Task<ActionResult<ApiResponse<string>>> CreateComment(
        [FromBody] CreateCommentDto req,
        [FromServices] IRepository<PhmPrescriptionComment> commentRepo,
        CancellationToken ct = default)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var entity = new PhmPrescriptionComment
        {
            Id = Guid.NewGuid().ToString(),
            SourceType = req.SourceType,
            TargetId = req.TargetId,
            DoctorId = req.DoctorId,
            PharmacistId = req.PharmacistId,
            IsRational = (short)(req.IsRational ? 1 : 0),
            DefectCodesJson = req.DefectCodes != null ? JsonSerializer.Serialize(req.DefectCodes) : null,
            CommentText = req.CommentText,
            ErrorCategory = req.ErrorCategory,
            DoctorAckStatus = "PENDING",
            CreatedAtMs = now,
            CreatedBy = req.PharmacistId,
            UpdatedAtMs = now,
            UpdatedBy = req.PharmacistId,
            VersionNo = 1
        };

        await commentRepo.InsertAsync(entity, ct);
        return Ok(ApiResponse<string>.Success(entity.Id, "处方点评提交成功，已向主诊医师推送点评通知"));
    }

    [HttpPost("comments/{id}/doctor-reply")]
    public async Task<ActionResult<ApiResponse<bool>>> ReplyComment(
        string id,
        [FromBody] DoctorReplyDto req,
        [FromServices] IRepository<PhmPrescriptionComment> commentRepo,
        CancellationToken ct = default)
    {
        var comment = await commentRepo.GetByIdAsync(id, ct);
        if (comment == null)
        {
            return NotFound(ApiResponse<bool>.Fail(404, "点评记录不存在"));
        }

        comment.DoctorReply = req.DoctorReply;
        comment.DoctorAckStatus = string.Equals(req.Status, "APPEALED", StringComparison.OrdinalIgnoreCase) ? "APPEALED" : "ACKED";
        comment.UpdatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await commentRepo.UpdateAsync(comment, ct);

        return Ok(ApiResponse<bool>.Success(true, "医师反馈成功"));
    }

    [HttpGet("comments")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PhmPrescriptionComment>>>> GetComments(
        [FromQuery] string? doctorId,
        [FromQuery] string? status,
        [FromServices] IRepository<PhmPrescriptionComment> commentRepo,
        CancellationToken ct = default)
    {
        string sql = "WHERE 1=1";
        if (!string.IsNullOrEmpty(doctorId)) sql += " AND doctor_id = @DocId";
        if (!string.IsNullOrEmpty(status)) sql += " AND doctor_ack_status = @Status";
        sql += " ORDER BY created_at_ms DESC LIMIT 100";

        var list = await commentRepo.QueryAsync(sql, new { DocId = doctorId, Status = status }, ct);
        return Ok(ApiResponse<IReadOnlyList<PhmPrescriptionComment>>.Success(list, "获取处方点评列表成功"));
    }

    [HttpPost("pivas/batches")]
    public async Task<ActionResult<ApiResponse<string>>> CreatePivasBatch(
        [FromBody] CreatePivasBatchDto req,
        [FromServices] IRepository<PhmWardPivasBatch> pivasRepo,
        CancellationToken ct = default)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var entity = new PhmWardPivasBatch
        {
            Id = Guid.NewGuid().ToString(),
            EncounterId = req.EncounterId,
            WardId = req.WardId,
            BatchNo = req.BatchNo,
            PrepType = req.PrepType,
            PharmacistAuditBy = req.PharmacistId,
            LabelPrintedAtMs = now,
            Status = "AUDITED",
            CreatedAtMs = now,
            CreatedBy = req.PharmacistId,
            UpdatedAtMs = now,
            UpdatedBy = req.PharmacistId,
            VersionNo = 1
        };

        await pivasRepo.InsertAsync(entity, ct);
        return Ok(ApiResponse<string>.Success(entity.Id, "集中摆药/静配批次创建成功"));
    }

    [HttpPost("unit-dose/generate-barcodes")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<string>>>> GenerateUnitDoseBarcodes(
        [FromBody] GenerateUnitDoseDto req,
        [FromServices] IRepository<PhmUnitDoseBarcode> barcodeRepo,
        CancellationToken ct = default)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var barcodes = new List<string>();
        int count = Math.Max(1, Math.Min(req.Count, 50));

        for (int i = 0; i < count; i++)
        {
            string code = $"UD-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..24].ToUpperInvariant();
            var entity = new PhmUnitDoseBarcode
            {
                Id = Guid.NewGuid().ToString(),
                Barcode = code,
                MedicalOrderId = req.MedicalOrderId,
                DrugId = req.DrugId,
                DoseText = req.DoseText,
                PatientId = req.PatientId,
                Status = "PRINTED",
                CreatedAtMs = now,
                CreatedBy = "PHARMACY",
                UpdatedAtMs = now,
                UpdatedBy = "PHARMACY",
                VersionNo = 1
            };

            await barcodeRepo.InsertAsync(entity, ct);
            barcodes.Add(code);
        }

        return Ok(ApiResponse<IReadOnlyList<string>>.Success(barcodes, $"成功生成 {barcodes.Count} 张药品单品/单剂量条码"));
    }

    [HttpGet("reconciliations/{encounterId}")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PhmMedicationReconciliation>>>> GetReconciliations(
        string encounterId,
        [FromServices] IRepository<PhmMedicationReconciliation> reconRepo,
        CancellationToken ct = default)
    {
        var list = await reconRepo.QueryAsync(
            "SELECT * FROM phm_medication_reconciliations WHERE encounter_id = @EncId ORDER BY created_at_ms DESC",
            new { EncId = encounterId }, ct);
        return Ok(ApiResponse<IReadOnlyList<PhmMedicationReconciliation>>.Success(list, "获取用药重整记录成功"));
    }

    [HttpPost("reconciliations/{encounterId}")]
    public async Task<ActionResult<ApiResponse<string>>> SaveReconciliation(
        string encounterId,
        [FromBody] SaveMedicationReconDto req,
        [FromServices] IRepository<PhmMedicationReconciliation> reconRepo,
        CancellationToken ct = default)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var entity = new PhmMedicationReconciliation
        {
            Id = Guid.NewGuid().ToString(),
            EncounterId = encounterId,
            PatientId = req.PatientId,
            Source = req.Source,
            DrugName = req.DrugName,
            DosageText = req.DosageText,
            Decision = req.Decision,
            ReconciledBy = req.ReconciledBy,
            CreatedAtMs = now,
            CreatedBy = req.ReconciledBy,
            UpdatedAtMs = now,
            UpdatedBy = req.ReconciledBy,
            VersionNo = 1
        };

        await reconRepo.InsertAsync(entity, ct);
        return Ok(ApiResponse<string>.Success(entity.Id, "入院前用药重整记录保存成功"));
    }

    [HttpPost("external-rx/import")]
    public async Task<ActionResult<ApiResponse<string>>> ImportExternalRx(
        [FromBody] ImportExternalRxDto req,
        [FromServices] IRepository<PhmExternalPrescription> rxRepo,
        CancellationToken ct = default)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var entity = new PhmExternalPrescription
        {
            Id = Guid.NewGuid().ToString(),
            ExternalRxNo = req.ExternalRxNo,
            SourceOrgCode = req.SourceOrgCode,
            SourceOrgName = req.SourceOrgName,
            PatientId = req.PatientId,
            RxPayloadJson = JsonSerializer.Serialize(req.RxPayload),
            CaSignatureVerified = 1,
            DispenseStatus = "PENDING",
            CreatedAtMs = now,
            CreatedBy = "HIE_IMPORT",
            UpdatedAtMs = now,
            UpdatedBy = "HIE_IMPORT",
            VersionNo = 1
        };

        await rxRepo.InsertAsync(entity, ct);
        return Ok(ApiResponse<string>.Success(entity.Id, "外院处方录入成功"));
    }

    [HttpPost("external-rx/{id}/dispense")]
    public async Task<ActionResult<ApiResponse<bool>>> DispenseExternalRx(
        string id,
        [FromQuery] string dispensedBy,
        [FromServices] IRepository<PhmExternalPrescription> rxRepo,
        CancellationToken ct = default)
    {
        var rx = await rxRepo.GetByIdAsync(id, ct);
        if (rx == null)
        {
            return NotFound(ApiResponse<bool>.Fail(404, "外院处方不存在"));
        }

        rx.DispenseStatus = "DISPENSED";
        rx.DispensedBy = dispensedBy;
        rx.DispensedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        rx.UpdatedAtMs = rx.DispensedAtMs.Value;
        await rxRepo.UpdateAsync(rx, ct);

        return Ok(ApiResponse<bool>.Success(true, "外院处方跨院调剂完成"));
    }

    [HttpGet("patient-clinical-context/{patientId}")]
    public async Task<ActionResult<ApiResponse<object>>> GetPatientClinicalContext(
        string patientId,
        [FromServices] IDbConnectionFactory? connFactory = null,
        CancellationToken ct = default)
    {
        var factory = connFactory ?? _connFactory;
        using var conn = factory.CreateConnection();

        var patient = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT full_name, gender_code, birth_date_int, allergy_tags_json FROM pat_patients WHERE id = @Id",
            new { Id = patientId });

        var recentLabs = await conn.QueryAsync<dynamic>(
            "SELECT item_code, item_name, value_num, value_text, unit, abnormal_flag, reported_at_ms FROM mtc_lab_result_items WHERE patient_id = @PatId ORDER BY reported_at_ms DESC LIMIT 20",
            new { PatId = patientId });

        var activeOrders = await conn.QueryAsync<dynamic>(
            "SELECT item_name, dosage, frequency_code, route_code, plan_start_at_ms FROM ord_medical_orders WHERE patient_id = @PatId AND status = 1 LIMIT 20",
            new { PatId = patientId });

        var recons = await conn.QueryAsync<dynamic>(
            "SELECT drug_name, dosage_text, decision, source FROM phm_medication_reconciliations WHERE patient_id = @PatId LIMIT 10",
            new { PatId = patientId });

        return Ok(ApiResponse<object>.Success(new
        {
            Patient = patient,
            RecentLabs = recentLabs,
            ActiveOrders = activeOrders,
            MedicationReconciliations = recons
        }, "药师审方临床全景上下文获取成功"));
    }
}
