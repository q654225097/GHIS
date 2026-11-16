using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Inpatient.Models;
using HIS.Modules.Inpatient.Services;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Modules.Inpatient.Controllers;

[ApiController]
[Route("api/v4/ipd")]
public class InpatientDischargeController : ControllerBase
{
    private readonly IRepository<IpdDischargeRequest> _requestRepo;
    private readonly IRepository<IpdEncounter> _encounterRepo;
    private readonly IRepository<IpdBed> _bedRepo;
    private readonly IDbConnectionFactory _connFactory;
    private readonly DischargeChecklistService _checklist;
    private readonly IAuditTrailContract? _auditTrail;
    private readonly IUserSession? _userSession;

    public InpatientDischargeController(
        IRepository<IpdDischargeRequest> requestRepo,
        IRepository<IpdEncounter> encounterRepo,
        IRepository<IpdBed> bedRepo,
        IDbConnectionFactory connFactory,
        DischargeChecklistService checklist,
        IAuditTrailContract? auditTrail = null,
        IUserSession? userSession = null)
    {
        _requestRepo = requestRepo;
        _encounterRepo = encounterRepo;
        _bedRepo = bedRepo;
        _connFactory = connFactory;
        _checklist = checklist;
        _auditTrail = auditTrail;
        _userSession = userSession;
    }

    [HttpPost("encounters/{encounterId}/discharge-request")]
    public async Task<ApiResponse<IpdDischargeRequestView>> CreateRequest(
        string encounterId, [FromBody] CreateIpdDischargeRequest? req = null)
    {
        if (!HasAnyRole("IPD_RESIDENT", "IPD_ATTENDING", "ADMIN"))
        {
            return ApiResponse<IpdDischargeRequestView>.Fail(403, "仅住院医师、主治医师或系统管理员可以申请出院");
        }

        IpdEncounter? encounter = await _encounterRepo.GetByIdAsync(encounterId);
        if (encounter == null)
        {
            return ApiResponse<IpdDischargeRequestView>.Fail(404, "住院记录不存在");
        }

        IpdDischargeRequest? existing = (await _requestRepo.QueryAsync(
            "SELECT * FROM ipd_discharge_requests WHERE encounter_id = @EncounterId ORDER BY requested_at_ms DESC",
            new { EncounterId = encounterId })).FirstOrDefault();
        if (existing?.Status == "PENDING")
        {
            return ApiResponse<IpdDischargeRequestView>.Success(
                await LoadRequestAsync(existing.Id), "该患者已有待结算出院申请，已返回原申请");
        }

        if (encounter.Status != 1)
        {
            return ApiResponse<IpdDischargeRequestView>.Fail(409, "患者不在院，不能重复申请出院");
        }
        if (encounter.TriageStatus != 1)
        {
            return ApiResponse<IpdDischargeRequestView>.Fail(409, "患者尚未完成病区接诊，不能申请出院");
        }
        if (!IsAdmin() && !string.IsNullOrWhiteSpace(_userSession?.ActiveDeptId)
            && !_userSession.ActiveDeptId.Equals(encounter.AdmitDeptId, StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<IpdDischargeRequestView>.Fail(403, "只能为本医疗科室的住院患者申请出院");
        }

        string? diagnosisError = await ValidateIcd10GenderAsync(encounter.PatientId,
            req?.DischargeDiagnosis ?? existing?.DischargeDiagnosis);
        if (diagnosisError != null)
        {
            return ApiResponse<IpdDischargeRequestView>.Fail(400, diagnosisError);
        }

        if (existing != null && (existing.Status == "CANCELLED" || existing.Status == "RECALLED"))
        {
            string existingUserId = CurrentUserId();
            try
            {
                await _connFactory.ExecuteInTransactionAsync(async ct =>
                {
                    IpdDischargeRequest? fresh = await _requestRepo.GetByIdAsync(existing.Id, ct);
                    if (fresh == null || (fresh.Status != "CANCELLED" && fresh.Status != "RECALLED"))
                    {
                        throw new InpatientDischargeConflictException("出院申请状态已变化，请刷新后重试");
                    }
                    IpdEncounter? freshEncounter = await _encounterRepo.GetByIdAsync(encounter.Id, ct);
                    if (freshEncounter == null || freshEncounter.Status != 1)
                    {
                        throw new InpatientDischargeConflictException("患者不在院，不能重新申请出院");
                    }
                    freshEncounter.Status = 2;
                    freshEncounter.UpdatedBy = existingUserId;
                    await _encounterRepo.UpdateAsync(freshEncounter, ct);

                    fresh.Status = "PENDING";
                    fresh.DischargeDiagnosis = NullIfBlank(req?.DischargeDiagnosis) ?? fresh.DischargeDiagnosis;
                    fresh.DischargeInstructions = NullIfBlank(req?.DischargeInstructions) ?? fresh.DischargeInstructions;
                    fresh.RequestedAtMs = SystemTime.UtcNowMs;
                    // Keep the original payment/refund links to prevent a second refund.
                    fresh.SettlementRequestKey = null;
                    fresh.UpdatedBy = existingUserId;
                    await _requestRepo.UpdateAsync(fresh, ct);
                    return true;
                });
            }
            catch (InpatientDischargeConflictException ex)
            {
                return ApiResponse<IpdDischargeRequestView>.Fail(409, ex.Message);
            }
            catch (DbConcurrencyException ex)
            {
                return ApiResponse<IpdDischargeRequestView>.Fail(409, ex.Message);
            }
            await RecordAuditAsync("IPD_DISCHARGE_REQUEST", existing.Id,
                $"重新提交出院申请：住院号 {encounter.AdmissionNo}，患者 {encounter.PatientId}");
            return ApiResponse<IpdDischargeRequestView>.Success(
                await LoadRequestAsync(existing.Id), "出院申请已重新提交，等待收费处完成财务结算");
        }

        string userId = CurrentUserId();
        IpdDischargeRequest request = new()
        {
            RequestNo = "DCR" + DateTime.UtcNow.ToString("yyMMddHHmmssfff"),
            EncounterId = encounter.Id,
            PatientId = encounter.PatientId,
            DischargeDiagnosis = NullIfBlank(req?.DischargeDiagnosis),
            DischargeInstructions = NullIfBlank(req?.DischargeInstructions),
            RequestedAtMs = SystemTime.UtcNowMs,
            Status = "PENDING",
            CreatedBy = userId,
            UpdatedBy = userId
        };

        try
        {
            await _connFactory.ExecuteInTransactionAsync(async ct =>
            {
                IpdEncounter? fresh = await _encounterRepo.GetByIdAsync(encounter.Id, ct);
                if (fresh == null || fresh.Status != 1)
                {
                    throw new InpatientDischargeConflictException("患者不在院，不能重复申请出院");
                }
                fresh.Status = 2;
                fresh.UpdatedBy = userId;
                await _encounterRepo.UpdateAsync(fresh, ct);
                await _requestRepo.InsertAsync(request, ct);
                return request;
            });
        }
        catch (InpatientDischargeConflictException ex)
        {
            return ApiResponse<IpdDischargeRequestView>.Fail(409, ex.Message);
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            IpdDischargeRequest? concurrent = (await _requestRepo.QueryAsync(
                "SELECT * FROM ipd_discharge_requests WHERE encounter_id = @EncounterId AND status = 'PENDING'",
                new { EncounterId = encounterId })).FirstOrDefault();
            if (concurrent == null)
            {
                return ApiResponse<IpdDischargeRequestView>.Fail(409, "出院申请已提交，请刷新列表确认");
            }
            return ApiResponse<IpdDischargeRequestView>.Success(
                await LoadRequestAsync(concurrent.Id), "该患者已有待结算出院申请，已返回原申请");
        }

        await RecordAuditAsync("IPD_DISCHARGE_REQUEST", request.Id,
            $"提交出院申请：住院号 {encounter.AdmissionNo}，患者 {encounter.PatientId}");
        return ApiResponse<IpdDischargeRequestView>.Success(
            await LoadRequestAsync(request.Id), "出院申请已提交，等待收费处完成财务结算");
    }

    [HttpGet("discharge-requests")]
    public async Task<ApiResponse<IReadOnlyList<IpdDischargeRequestView>>> GetRequests(
        [FromQuery] string? status = "PENDING", [FromQuery] string? keyword = null)
    {
        if (!HasAnyRole("IPD_RESIDENT", "IPD_ATTENDING", "WARD_NURSE", "CASHIER", "ADMIN"))
        {
            return ApiResponse<IReadOnlyList<IpdDischargeRequestView>>.Fail(403, "无权查看出院结算队列");
        }
        string? normalizedStatus = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToUpperInvariant();
        string? search = string.IsNullOrWhiteSpace(keyword) ? null : "%" + keyword.Trim() + "%";
        using DbConnection conn = _connFactory.CreateConnection();
        const string sql = @"
SELECT r.*, COALESCE(p.full_name, r.patient_id) AS patient_name,
       COALESCE(p.medical_record_no, '') AS medical_record_no,
       COALESCE(e.admission_no, '') AS admission_no,
       COALESCE(d.dept_name, e.admit_dept_id, '') AS dept_name,
       COALESCE(w.dept_name, e.admit_ward_id, '') AS ward_name,
       COALESCE(e.bed_code, '') AS bed_code,
       COALESCE(doc.display_name, e.attending_doctor_id) AS doctor_name,
       pay.payment_no AS payment_no,
       pay.invoice_ofd_id AS invoice_ofd_id
FROM ipd_discharge_requests r
LEFT JOIN ipd_encounters e ON e.id = r.encounter_id
LEFT JOIN pat_patients p ON p.id = r.patient_id
LEFT JOIN sys_departments d ON d.id = e.admit_dept_id
LEFT JOIN sys_departments w ON w.id = e.admit_ward_id
LEFT JOIN sys_users doc ON doc.id = e.attending_doctor_id
LEFT JOIN fin_payment_orders pay ON pay.id = r.settlement_payment_id
WHERE (@Status IS NULL OR r.status = @Status)
  AND (@Search IS NULL OR p.full_name LIKE @Search OR p.medical_record_no LIKE @Search
       OR e.admission_no LIKE @Search OR r.request_no LIKE @Search)
ORDER BY CASE r.status WHEN 'PENDING' THEN 0 ELSE 1 END, r.requested_at_ms DESC";
        List<IpdDischargeRequestView> rows = (await conn.QueryAsync<IpdDischargeRequestView>(
            sql, new { Status = normalizedStatus, Search = search })).ToList();
        foreach (IpdDischargeRequestView row in rows)
        {
            if (row.Status == "PENDING")
            {
                row.ClinicalBlockers = (await _checklist.PerformSixItemCheckAsync(row.EncounterId)).FailedItems.ToList();
            }
        }
        return ApiResponse<IReadOnlyList<IpdDischargeRequestView>>.Success(rows);
    }

    [HttpGet("discharge-requests/{requestId}")]
    public async Task<ApiResponse<IpdDischargeRequestView>> GetRequest(string requestId)
    {
        IpdDischargeRequestView? row = await LoadRequestAsync(requestId);
        return row == null
            ? ApiResponse<IpdDischargeRequestView>.Fail(404, "出院申请不存在")
            : ApiResponse<IpdDischargeRequestView>.Success(row);
    }

    [HttpGet("encounters/{encounterId}/settlement-preview")]
    public async Task<ApiResponse<IpdSettlementPreviewView>> GetSettlementPreview(string encounterId)
    {
        if (!HasAnyRole("CASHIER", "ADMIN", "IPD_RESIDENT", "IPD_ATTENDING"))
        {
            return ApiResponse<IpdSettlementPreviewView>.Fail(403, "无权查看住院结算预览");
        }
        IpdEncounter? encounter = await _encounterRepo.GetByIdAsync(encounterId);
        if (encounter == null)
        {
            return ApiResponse<IpdSettlementPreviewView>.Fail(404, "住院记录不存在");
        }
        IpdSettlementPreviewView preview = await BuildPreviewAsync(encounter, null);
        return ApiResponse<IpdSettlementPreviewView>.Success(preview);
    }

    [HttpPost("discharge-requests/{requestId}/settle")]
    public async Task<ApiResponse<IpdDischargeRequestView>> Settle(
        string requestId, [FromBody] SettleIpdDischargeRequest req)
    {
        if (!HasAnyRole("CASHIER", "ADMIN"))
        {
            return ApiResponse<IpdDischargeRequestView>.Fail(403, "仅收费员或系统管理员可以办理出院结算");
        }
        if (!ValidPaymentChannel(req.PaymentChannel))
        {
            return ApiResponse<IpdDischargeRequestView>.Fail(400, "不支持的结算支付方式");
        }
        string? requestKey = NullIfBlank(req.RequestKey);
        if (requestKey != null)
        {
            IpdDischargeRequest? byKey = (await _requestRepo.QueryAsync(
                "SELECT * FROM ipd_discharge_requests WHERE settlement_request_key = @Key",
                new { Key = requestKey })).FirstOrDefault();
            if (byKey != null)
            {
                if (byKey.Id != requestId)
                {
                    return ApiResponse<IpdDischargeRequestView>.Fail(409, "该幂等键已被其他出院申请使用");
                }
                return ApiResponse<IpdDischargeRequestView>.Success(
                    await LoadRequestAsync(byKey.Id), "幂等命中：已返回原出院结算结果");
            }
        }

        IpdDischargeRequest? request = await _requestRepo.GetByIdAsync(requestId);
        if (request == null)
        {
            return ApiResponse<IpdDischargeRequestView>.Fail(404, "出院申请不存在");
        }
        if (request.Status != "PENDING")
        {
            return ApiResponse<IpdDischargeRequestView>.Fail(409, "该出院申请已处理，不能重复结算");
        }
        IpdEncounter? encounter = await _encounterRepo.GetByIdAsync(request.EncounterId);
        if (encounter == null)
        {
            return ApiResponse<IpdDischargeRequestView>.Fail(404, "住院记录不存在");
        }
        if (encounter.Status != 2 || encounter.TriageStatus != 1)
        {
            return ApiResponse<IpdDischargeRequestView>.Fail(409, "仅已提交出院申请并完成核查的待结算住院患者可以办理出院结算");
        }

        DischargeCheckResult check = await _checklist.PerformSixItemCheckAsync(encounter.Id);
        if (!check.AllPassed)
        {
            return ApiResponse<IpdDischargeRequestView>.Fail(409,
                "出院六项核查未通过：" + string.Join("；", check.FailedItems),
                await LoadRequestAsync(request.Id));
        }

        IpdSettlementPreviewView preview = await BuildPreviewAsync(encounter, request.Id);
        string? amountError = ValidateAmounts(req, preview);
        if (amountError != null)
        {
            return ApiResponse<IpdDischargeRequestView>.Fail(400, amountError);
        }

        IpdDischargeRequestView? settled;
        try
        {
            settled = await _connFactory.ExecuteInTransactionAsync(async ct =>
            {
                IpdDischargeRequest? fresh = await _requestRepo.GetByIdAsync(request.Id, ct);
                if (fresh == null || fresh.Status != "PENDING")
                {
                    throw new InpatientDischargeConflictException("该出院申请已被其他收费员处理，请刷新列表");
                }
                IpdEncounter? freshEncounter = await _encounterRepo.GetByIdAsync(encounter.Id, ct);
                if (freshEncounter == null || freshEncounter.Status != 2)
                {
                    throw new InpatientDischargeConflictException("住院状态已变化，仅出院待结算状态可以办理结算");
                }
                if (requestKey != null)
                {
                    IpdDischargeRequest? keyOwner = (await _requestRepo.QueryAsync(
                        "SELECT * FROM ipd_discharge_requests WHERE settlement_request_key = @Key",
                        new { Key = requestKey }, ct)).FirstOrDefault();
                    if (keyOwner != null && keyOwner.Id != fresh.Id)
                    {
                        throw new InpatientDischargeConflictException("该幂等键已被其他出院申请使用");
                    }
                }

                long nowMs = SystemTime.UtcNowMs;
                string userId = CurrentUserId();
                DischargeCheckResult freshCheck = await _checklist.PerformSixItemCheckAsync(freshEncounter.Id, ct);
                if (!freshCheck.AllPassed)
                {
                    throw new InpatientDischargeConflictException(
                        "出院六项核查未通过：" + string.Join("；", freshCheck.FailedItems));
                }
                IpdSettlementPreviewView freshPreview = await BuildPreviewAsync(freshEncounter, fresh.Id);
                string? freshAmountError = ValidateAmounts(req, freshPreview);
                if (freshAmountError != null)
                {
                    throw new InpatientDischargeConflictException(freshAmountError);
                }
                if (!string.IsNullOrWhiteSpace(fresh.SettlementPaymentId))
                {
                    throw new InpatientDischargeConflictException("该出院申请已完成过结算，已由收费处记账，不能重复结算");
                }

                using DbConnection conn = _connFactory.CreateConnection();
                string paymentId = Guid.NewGuid().ToString("D");
                string paymentNo = "PAY" + DateTime.UtcNow.ToString("yyMMddHHmmssfff");
                string invoiceOfdId = "INV-OFD-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
                await conn.ExecuteAsync(new CommandDefinition(@"
INSERT INTO fin_payment_orders
    (id, payment_no, request_key, scene, patient_id, visit_or_encounter_id,
     total_amount, insurance_amount, prepayment_deduct, self_pay_amount,
     payment_channel, status, paid_at_ms, invoice_ofd_id, ext_data_json,
     schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    (@Id, @PaymentNo, @RequestKey, 'IPD', @PatientId, @EncounterId,
     @TotalAmount, @InsuranceAmount, @PrepaymentDeduct, @SelfPayAmount,
     @PaymentChannel, 1, @PaidAtMs, @InvoiceOfdId, @ExtDataJson,
     1, @Now, @UserId, @Now, @UserId, 1)",
                    new
                    {
                        Id = paymentId, PaymentNo = paymentNo, RequestKey = requestKey,
                        PatientId = fresh.PatientId, EncounterId = fresh.EncounterId,
                        TotalAmount = freshPreview.ChargeTotal, InsuranceAmount = req.InsuranceAmount,
                        PrepaymentDeduct = req.PrepaymentDeduct, SelfPayAmount = req.SelfPayAmount,
                        PaymentChannel = req.PaymentChannel.Trim().ToUpperInvariant(),
                        PaidAtMs = nowMs, InvoiceOfdId = invoiceOfdId,
                        ExtDataJson = JsonSerializer.Serialize(new { source = "IPD_DISCHARGE", dischargeRequestId = fresh.Id }),
                        Now = nowMs, UserId = userId
                    }, _connFactory.CurrentTransaction, null, null, CommandFlags.Buffered, ct));

                if (req.RefundAmount > 0m)
                {
                    string refundNo = "RFD" + DateTime.UtcNow.ToString("yyMMddHHmmssfff")
                        + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
                    await conn.ExecuteAsync(new CommandDefinition(@"
INSERT INTO fin_refund_orders
    (id, refund_no, original_payment_id, refund_amount, reason, drug_return_confirmed,
     approved_by, refunded_at_ms, status, reissued_invoice_ofd_id, ext_data_json,
     schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    (@Id, @RefundNo, @OriginalPaymentId, @RefundAmount, @Reason, 1,
     @ApprovedBy, @RefundedAtMs, 1, NULL, @ExtDataJson,
     1, @Now, @UserId, @Now, @UserId, 1)",
                        new
                        {
                            Id = Guid.NewGuid().ToString("D"), RefundNo = refundNo,
                            OriginalPaymentId = paymentId, RefundAmount = req.RefundAmount,
                            Reason = "出院结算退回预交金余额，住院号 " + freshEncounter.AdmissionNo,
                            ApprovedBy = userId, RefundedAtMs = nowMs,
                            ExtDataJson = JsonSerializer.Serialize(new
                            {
                                source = "IPD_DISCHARGE_PREPAYMENT_REFUND",
                                dischargeRequestId = fresh.Id,
                                encounterId = fresh.EncounterId
                            }),
                            Now = nowMs, UserId = userId
                        }, _connFactory.CurrentTransaction, null, null, CommandFlags.Buffered, ct));
                }

                await conn.ExecuteAsync(new CommandDefinition(@"
UPDATE fin_charge_ledgers
SET payment_order_id = @PaymentId, updated_at_ms = @Now, updated_by = @UserId, version_no = version_no + 1
WHERE visit_or_encounter_id = @EncounterId AND status = 1",
                    new { PaymentId = paymentId, Now = nowMs, UserId = userId, EncounterId = fresh.EncounterId },
                    _connFactory.CurrentTransaction, null, null, CommandFlags.Buffered, ct));

                List<IpdSettlementChargeRow> unledgered = (await conn.QueryAsync<IpdSettlementChargeRow>(new CommandDefinition(@"
SELECT id, service_item_code AS item_code, service_item_code AS item_name, 1 AS quantity,
       fee_amount AS unit_price, fee_amount AS total_amount
FROM mtc_service_requests
WHERE (source_visit_id = @EncounterId
       OR source_order_id = @EncounterId
       OR source_order_id IN (
           SELECT id FROM ord_medical_orders WHERE encounter_id = @EncounterId))
  AND is_fee_confirmed = 1 AND status <> 4
  AND NOT EXISTS (
      SELECT 1 FROM fin_charge_ledgers l
      WHERE l.source_service_request_id = mtc_service_requests.id AND l.status = 1)",
                    new { EncounterId = fresh.EncounterId }, _connFactory.CurrentTransaction,
                    null, null, CommandFlags.Buffered, ct))).ToList();
                foreach (IpdSettlementChargeRow row in unledgered)
                {
                    await conn.ExecuteAsync(new CommandDefinition(@"
INSERT INTO fin_charge_ledgers
    (id, patient_id, visit_or_encounter_id, order_dept_id, patient_ward_id, exec_dept_id,
     item_code, item_name, quantity, unit_price, total_amount, status,
     payment_order_id, source_service_request_id, ext_data_json,
     schema_version, created_at_ms, created_by, updated_at_ms, updated_by, version_no)
VALUES
    (@Id, @PatientId, @EncounterId, @OrderDeptId, @WardId, @OrderDeptId,
     @ItemCode, @ItemName, @Quantity, @UnitPrice, @TotalAmount, 1,
     @PaymentId, @SourceServiceRequestId, NULL,
     1, @Now, @UserId, @Now, @UserId, 1)",
                        new
                        {
                            Id = Guid.NewGuid().ToString("D"), PatientId = fresh.PatientId,
                            EncounterId = fresh.EncounterId, OrderDeptId = freshEncounter.AdmitDeptId,
                            WardId = freshEncounter.AdmitWardId, row.ItemCode, row.ItemName,
                            row.Quantity, row.UnitPrice, row.TotalAmount, PaymentId = paymentId,
                            SourceServiceRequestId = row.Id, Now = nowMs, UserId = userId
                        }, _connFactory.CurrentTransaction, null, null, CommandFlags.Buffered, ct));
                }

                fresh.Status = "SETTLED";
                fresh.SettledAtMs = nowMs;
                fresh.SettledBy = userId;
                fresh.SettlementRequestKey = requestKey;
                fresh.SettlementPaymentId = paymentId;
                fresh.SettlementTotalAmount = freshPreview.ChargeTotal;
                fresh.InsuranceAmount = req.InsuranceAmount;
                fresh.PrepaymentDeduct = req.PrepaymentDeduct;
                fresh.SelfPayAmount = req.SelfPayAmount;
                fresh.RefundAmount = freshEncounter.PrepaymentBalance - req.PrepaymentDeduct;
                fresh.UpdatedBy = userId;
                await _requestRepo.UpdateAsync(fresh, ct);

                freshEncounter.Status = 3;
                freshEncounter.PrepaymentBalance = 0m;
                freshEncounter.UpdatedBy = userId;
                await _encounterRepo.UpdateAsync(freshEncounter, ct);

                if (!string.IsNullOrWhiteSpace(freshEncounter.BedCode))
                {
                    IReadOnlyList<IpdBed> beds = await _bedRepo.QueryAsync(
                        "SELECT * FROM ipd_beds WHERE ward_id = @WardId AND bed_code = @BedCode",
                        new { WardId = freshEncounter.AdmitWardId, BedCode = freshEncounter.BedCode }, ct);
                    if (beds.Count > 0)
                    {
                        IpdBed bed = beds[0];
                        bed.BedStatus = 0;
                        bed.CurrentEncounterId = null;
                        bed.UpdatedBy = userId;
                        await _bedRepo.UpdateAsync(bed, ct);
                    }
                }
                return await LoadRequestAsync(fresh.Id, ct);
            });
        }
        catch (InpatientDischargeConflictException ex)
        {
            return ApiResponse<IpdDischargeRequestView>.Fail(409, ex.Message);
        }
        catch (DbConcurrencyException ex)
        {
            return ApiResponse<IpdDischargeRequestView>.Fail(409, ex.Message);
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return ApiResponse<IpdDischargeRequestView>.Fail(409, "结算请求已提交，请刷新列表确认结果");
        }

        await RecordAuditAsync("IPD_DISCHARGE_SETTLE", request.Id,
            $"完成出院结算：住院号 {encounter.AdmissionNo}，费用 ¥{settled?.SettlementTotalAmount ?? preview.ChargeTotal:0.00}，医保 ¥{req.InsuranceAmount:0.00}，预交金抵扣 ¥{req.PrepaymentDeduct:0.00}，自付 ¥{req.SelfPayAmount:0.00}，退回预交金 ¥{req.RefundAmount:0.00}");
        return ApiResponse<IpdDischargeRequestView>.Success(
            settled!, "出院结算完成，住院状态已关闭并释放床位");
    }

    [HttpPost("discharge-requests/{requestId}/cancel")]
    public async Task<ApiResponse<IpdDischargeRequestView>> Cancel(string requestId, [FromBody] CancelDischargeRequest? req = null)
    {
        if (!HasAnyRole("IPD_RESIDENT", "IPD_ATTENDING", "ADMIN"))
        {
            return ApiResponse<IpdDischargeRequestView>.Fail(403, "仅住院医师、主治医师或系统管理员可以撤回出院申请");
        }
        IpdDischargeRequest? request = await _requestRepo.GetByIdAsync(requestId);
        if (request == null)
        {
            return ApiResponse<IpdDischargeRequestView>.Fail(404, "出院申请不存在");
        }
        if (request.Status != "PENDING")
        {
            return ApiResponse<IpdDischargeRequestView>.Fail(409, "仅待结算申请可以撤回");
        }
        string userId = CurrentUserId();
        try
        {
            await _connFactory.ExecuteInTransactionAsync(async ct =>
            {
                IpdDischargeRequest? fresh = await _requestRepo.GetByIdAsync(request.Id, ct);
                if (fresh == null || fresh.Status != "PENDING")
                {
                    throw new InpatientDischargeConflictException("仅待结算申请可以撤回");
                }
                fresh.Status = "CANCELLED";
                fresh.UpdatedBy = userId;
                await _requestRepo.UpdateAsync(fresh, ct);
                IpdEncounter? encounter = await _encounterRepo.GetByIdAsync(fresh.EncounterId, ct);
                if (encounter != null && encounter.Status == 2)
                {
                    encounter.Status = 1;
                    encounter.UpdatedBy = userId;
                    await _encounterRepo.UpdateAsync(encounter, ct);
                }
                return true;
            });
        }
        catch (InpatientDischargeConflictException ex)
        {
            return ApiResponse<IpdDischargeRequestView>.Fail(409, ex.Message);
        }
        catch (DbConcurrencyException ex)
        {
            return ApiResponse<IpdDischargeRequestView>.Fail(409, ex.Message);
        }
        await RecordAuditAsync("IPD_DISCHARGE_CANCEL", request.Id,
            "撤回出院申请：" + (NullIfBlank(req?.Reason) ?? "医生撤回"));
        return ApiResponse<IpdDischargeRequestView>.Success(
            await LoadRequestAsync(request.Id), "出院申请已撤回");
    }

    private async Task<IpdSettlementPreviewView> BuildPreviewAsync(IpdEncounter encounter, string? requestId)
    {
        using DbConnection conn = _connFactory.CreateConnection();
        List<IpdSettlementChargeView> charges = (await conn.QueryAsync<IpdSettlementChargeRow>(@"
SELECT item_code, item_name, SUM(quantity) AS quantity, MAX(unit_price) AS unit_price,
       SUM(total_amount) AS total_amount
FROM fin_charge_ledgers
WHERE visit_or_encounter_id = @EncounterId AND status = 1
GROUP BY item_code, item_name
ORDER BY item_name", new { EncounterId = encounter.Id }))
            .Select(x => new IpdSettlementChargeView
            {
                ItemCode = x.ItemCode,
                ItemName = x.ItemName,
                Quantity = x.Quantity,
                UnitPrice = x.UnitPrice,
                TotalAmount = x.TotalAmount
            }).ToList();

        List<IpdSettlementChargeRow> medTechCharges = (await conn.QueryAsync<IpdSettlementChargeRow>(new CommandDefinition(@"
SELECT service_item_code AS item_code, service_item_code AS item_name, 1 AS quantity,
       fee_amount AS unit_price, fee_amount AS total_amount
FROM mtc_service_requests
WHERE (source_visit_id = @EncounterId
       OR source_order_id = @EncounterId
       OR source_order_id IN (
           SELECT id FROM ord_medical_orders WHERE encounter_id = @EncounterId))
  AND is_fee_confirmed = 1 AND status <> 4
  AND NOT EXISTS (
      SELECT 1 FROM fin_charge_ledgers l
      WHERE l.source_service_request_id = mtc_service_requests.id AND l.status = 1)",
            new { EncounterId = encounter.Id }, _connFactory.CurrentTransaction, null, null,
            CommandFlags.Buffered, CancellationToken.None))).ToList();
        foreach (IpdSettlementChargeRow row in medTechCharges)
        {
            charges.Add(new IpdSettlementChargeView
            {
                ItemCode = row.ItemCode,
                ItemName = row.ItemName,
                Quantity = row.Quantity,
                UnitPrice = row.UnitPrice,
                TotalAmount = row.TotalAmount
            });
        }

        DischargeCheckResult check = await _checklist.PerformSixItemCheckAsync(encounter.Id);
        IpdDischargeRequest? request = requestId == null
            ? (await _requestRepo.QueryAsync(
                "SELECT * FROM ipd_discharge_requests WHERE encounter_id = @EncounterId AND status = 'PENDING'",
                new { EncounterId = encounter.Id })).FirstOrDefault()
            : await _requestRepo.GetByIdAsync(requestId);

        IpdSettlementAccountView account = await conn.QuerySingleOrDefaultAsync<IpdSettlementAccountView>(@"
SELECT COALESCE(p.full_name, @PatientId) AS patient_name,
       COALESCE(p.medical_record_no, '') AS medical_record_no,
       COALESCE(e.admission_no, '') AS admission_no,
       COALESCE(d.dept_name, e.admit_dept_id, '') AS dept_name,
       COALESCE(w.dept_name, e.admit_ward_id, '') AS ward_name,
       COALESCE(e.bed_code, '') AS bed_code
FROM ipd_encounters e
LEFT JOIN pat_patients p ON p.id = e.patient_id
LEFT JOIN sys_departments d ON d.id = e.admit_dept_id
LEFT JOIN sys_departments w ON w.id = e.admit_ward_id
WHERE e.id = @EncounterId", new { EncounterId = encounter.Id, PatientId = encounter.PatientId }) ?? new();

        return new IpdSettlementPreviewView
        {
            RequestId = request?.Id ?? string.Empty,
            EncounterId = encounter.Id,
            PatientId = encounter.PatientId,
            PatientName = account.PatientName,
            MedicalRecordNo = account.MedicalRecordNo,
            AdmissionNo = account.AdmissionNo,
            DeptName = account.DeptName,
            WardName = account.WardName,
            BedCode = account.BedCode,
            PrepaymentBalance = encounter.PrepaymentBalance,
            ChargeTotal = charges.Sum(x => x.TotalAmount),
            ChargeItems = charges,
            ClinicalBlockers = check.FailedItems.ToList(),
            Status = request?.Status ?? "PENDING"
        };
    }

    private string? ValidateAmounts(SettleIpdDischargeRequest req, IpdSettlementPreviewView preview)
    {
        if (req.TotalAmount != preview.ChargeTotal)
        {
            return $"结算总金额与服务器费用总额不一致，请刷新后重试（当前费用 ¥{preview.ChargeTotal:0.00}）";
        }
        if (req.InsuranceAmount < 0 || req.PrepaymentDeduct < 0 || req.SelfPayAmount < 0 || req.RefundAmount < 0)
        {
            return "结算金额不能为负数";
        }
        if (Math.Abs(req.InsuranceAmount + req.PrepaymentDeduct + req.SelfPayAmount - req.TotalAmount) > 0.01m)
        {
            return "金额勾稽失败：总金额应等于医保统筹、预交金抵扣与个人自付之和";
        }
        if (req.PrepaymentDeduct > preview.PrepaymentBalance + 0.001m)
        {
            return $"预交金抵扣超过可用余额 ¥{preview.PrepaymentBalance:0.00}";
        }
        if (Math.Abs(req.RefundAmount - (preview.PrepaymentBalance - req.PrepaymentDeduct)) > 0.01m)
        {
            return $"退款金额应为预交金余额减去抵扣额（应退 ¥{preview.PrepaymentBalance - req.PrepaymentDeduct:0.00}）";
        }
        if (req.ChargeItems.Count > 0)
        {
            Dictionary<string, IpdSettlementChargeView> serverItems = preview.ChargeItems
                .GroupBy(x => x.ItemCode.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
            HashSet<string> clientCodes = new(StringComparer.OrdinalIgnoreCase);
            foreach (IpdSettlementChargeView item in req.ChargeItems)
            {
                string code = (item.ItemCode ?? string.Empty).Trim();
                if (code.Length == 0 || !clientCodes.Add(code))
                {
                    return "费用明细中存在空项目编码或重复项目，请刷新后重试";
                }
                if (!serverItems.TryGetValue(code, out IpdSettlementChargeView? serverItem))
                {
                    return $"费用明细 [{code}] 不属于该住院患者，不能纳入结算";
                }
                if (Math.Abs(item.Quantity - serverItem.Quantity) > 0.0001m
                    || Math.Abs(item.UnitPrice - serverItem.UnitPrice) > 0.01m
                    || Math.Abs(item.TotalAmount - serverItem.TotalAmount) > 0.01m)
                {
                    return $"费用明细 [{code}] 的数量、单价或金额与服务器记录不一致，请刷新后重试";
                }
            }
            if (clientCodes.Count != serverItems.Count)
            {
                return "费用明细不完整，不能遗漏服务器计费项目";
            }
        }
        return null;
    }

    private async Task<IpdDischargeRequestView?> LoadRequestAsync(string requestId, System.Threading.CancellationToken ct = default)
    {
        using DbConnection conn = _connFactory.CreateConnection();
        const string sql = @"
SELECT r.*, COALESCE(p.full_name, r.patient_id) AS patient_name,
       COALESCE(p.medical_record_no, '') AS medical_record_no,
       COALESCE(e.admission_no, '') AS admission_no,
       COALESCE(d.dept_name, e.admit_dept_id, '') AS dept_name,
       COALESCE(w.dept_name, e.admit_ward_id, '') AS ward_name,
       COALESCE(e.bed_code, '') AS bed_code,
       COALESCE(doc.display_name, e.attending_doctor_id) AS doctor_name,
       pay.payment_no AS payment_no,
       pay.invoice_ofd_id AS invoice_ofd_id
FROM ipd_discharge_requests r
LEFT JOIN ipd_encounters e ON e.id = r.encounter_id
LEFT JOIN pat_patients p ON p.id = r.patient_id
LEFT JOIN sys_departments d ON d.id = e.admit_dept_id
LEFT JOIN sys_departments w ON w.id = e.admit_ward_id
LEFT JOIN sys_users doc ON doc.id = e.attending_doctor_id
LEFT JOIN fin_payment_orders pay ON pay.id = r.settlement_payment_id
WHERE r.id = @RequestId";
        IpdDischargeRequestView? view = await conn.QuerySingleOrDefaultAsync<IpdDischargeRequestView>(
            new CommandDefinition(sql, new { RequestId = requestId }, _connFactory.CurrentTransaction, null, null, CommandFlags.Buffered, ct));
        if (view != null && view.Status == "PENDING")
        {
            view.ClinicalBlockers = (await _checklist.PerformSixItemCheckAsync(view.EncounterId, ct)).FailedItems.ToList();
        }
        return view;
    }

    private async Task RecordAuditAsync(string eventType, string targetId, string detail)
    {
        if (_auditTrail != null)
        {
            await _auditTrail.RecordAuditAsync(eventType, "IpdDischargeRequest", targetId, detail, CurrentUserId());
        }
    }

    private async Task<string?> ValidateIcd10GenderAsync(string patientId, string? diagnosisCode)
    {
        string? code = NullIfBlank(diagnosisCode);
        if (code == null || _connFactory == null)
        {
            return null;
        }
        using DbConnection conn = _connFactory.CreateConnection();
        string? patientGender = await conn.QuerySingleOrDefaultAsync<string>(
            "SELECT gender_code FROM pat_patients WHERE id = @PatientId AND is_active = 1",
            new { PatientId = patientId });
        string normalizedGender = (patientGender ?? string.Empty).Trim().ToUpperInvariant();
        bool isMale = normalizedGender is "1" or "M" or "MALE" or "男";
        bool isFemale = normalizedGender is "2" or "F" or "FEMALE" or "女";
        bool genderUnknown = !isMale && !isFemale;
        IEnumerable<string> codes = code.Split(new[] { ',', '，', ';', '；', '|', ' ' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string item in codes)
        {
            string? exclusive = await conn.QuerySingleOrDefaultAsync<string>(
                "SELECT gender_exclusive FROM cfg_icd10_diagnoses WHERE icd_code = @Code AND is_active = 1",
                new { Code = item });
            string normalized = (exclusive ?? string.Empty).Trim().ToUpperInvariant();
            bool maleOnly = normalized is "M" or "1" or "MALE_ONLY" or "MALE";
            bool femaleOnly = normalized is "F" or "2" or "FEMALE_ONLY" or "FEMALE";
            if (genderUnknown && (maleOnly || femaleOnly))
            {
                return $"诊断 [{item}] 仅适用于{(maleOnly ? "男性" : "女性")}患者，当前患者性别未确认，请先核实性别";
            }
            bool conflict = (maleOnly && isFemale) || (femaleOnly && isMale);
            if (conflict)
            {
                return $"诊断 [{item}] 仅适用于{(maleOnly ? "男性" : "女性")}患者，与当前患者性别不符";
            }
        }
        return null;
    }

    private string CurrentUserId()
    {
        if (string.IsNullOrWhiteSpace(_userSession?.UserId))
        {
            throw new UnauthorizedAccessException("请先登录后再办理出院业务");
        }
        return _userSession.UserId;
    }

    private bool HasAnyRole(params string[] roles)
    {
        if (_userSession == null || string.IsNullOrWhiteSpace(_userSession.UserId))
        {
            return false;
        }
        return IsAdmin() || RoleCode.HasAnyRole(_userSession.Roles, roles);
    }

    private bool IsAdmin() => _userSession != null
        && (RoleCode.IsAdmin(_userSession.RoleCode)
            || RoleCode.HasRole(_userSession.Roles, RoleCode.Admin));

    private static bool ValidPaymentChannel(string? channel) =>
        (channel ?? string.Empty).Trim().ToUpperInvariant() is "CASH" or "WECHAT" or "ALIPAY" or "BANK_CARD" or "INSURANCE";

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class IpdSettlementChargeRow
    {
        public string Id { get; set; } = string.Empty;
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalAmount { get; set; }
    }

    private sealed class IpdSettlementAccountView
    {
        public string PatientName { get; set; } = string.Empty;
        public string MedicalRecordNo { get; set; } = string.Empty;
        public string AdmissionNo { get; set; } = string.Empty;
        public string DeptName { get; set; } = string.Empty;
        public string WardName { get; set; } = string.Empty;
        public string BedCode { get; set; } = string.Empty;
    }

    private sealed class InpatientDischargeConflictException : Exception
    {
        public InpatientDischargeConflictException(string message) : base(message) { }
    }
}

public sealed record CancelDischargeRequest(string? Reason);
