using Dapper;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Finance.Models;

namespace HIS.Modules.Finance.Services;

public sealed class FinanceSettlementContract : IFinanceSettlementContract, IOpdServicePaymentProofContract
{
    private readonly IRepository<FinPaymentOrder> _paymentRepo;
    private readonly IRepository<FinRefundOrder> _refundRepo;
    private readonly IRepository<FinChargeLedger> _chargeRepo;
    private readonly IDbConnectionFactory _connectionFactory;

    public FinanceSettlementContract(
        IRepository<FinPaymentOrder> paymentRepo,
        IRepository<FinRefundOrder> refundRepo,
        IRepository<FinChargeLedger> chargeRepo,
        IDbConnectionFactory connectionFactory)
    {
        _paymentRepo = paymentRepo;
        _refundRepo = refundRepo;
        _chargeRepo = chargeRepo;
        _connectionFactory = connectionFactory;
    }

    public async Task<OpdServicePaymentProof?> FindValidPaymentAsync(OpdServicePaymentProofRequest request, CancellationToken ct = default)
    {
        using var conn = _connectionFactory.CreateConnection();
        var rows = (await conn.QueryAsync<OpdServicePaymentProof>(new CommandDefinition("""
            SELECT l.source_service_request_id AS RequestId, p.id AS PaymentOrderId,
                   p.payment_no AS PaymentNo, l.total_amount AS Amount
            FROM fin_charge_ledgers l
            JOIN fin_payment_orders p ON p.id = l.payment_order_id
            WHERE l.source_service_request_id = @RequestId
              AND l.patient_id = @PatientId AND l.visit_or_encounter_id = @VisitId
              AND l.item_code = @ItemCode AND l.total_amount = @Amount
              AND l.status = 1 AND l.refund_amount = 0
              AND p.scene = 'OPD' AND p.status = 1
              AND p.patient_id = @PatientId AND p.visit_or_encounter_id = @VisitId
            LIMIT 2
            """, request, cancellationToken: ct))).ToArray();
        return rows.Length == 1 ? rows[0] : null;
    }

    public static string GenerateFiscalInvoiceNo(string prefix = "BILL")
    {
        string year = DateTime.UtcNow.ToString("yy");
        int randomCode = Random.Shared.Next(100000, 999999);
        long seq = (DateTime.UtcNow.Ticks / 10000) % 100000000;
        return $"{prefix}-210601{year}-{seq:D8}-CHK{randomCode}";
    }

    public async Task<RegistrationPaymentResultDto> CreateRegistrationPaymentAsync(
        RegistrationPaymentRequestDto request,
        CancellationToken ct = default)
    {
        FinPaymentOrder payment = new()
        {
            PaymentNo = "PAY" + DateTime.UtcNow.ToString("yyMMddHHmmssfff")
                + Guid.NewGuid().ToString("N")[..3].ToUpperInvariant(),
            RequestKey = request.RequestKey,
            Scene = "OPD_REG",
            PatientId = request.PatientId,
            VisitOrEncounterId = request.AppointmentId,
            TotalAmount = request.TotalAmount,
            InsuranceAmount = 0m,
            PrepaymentDeduct = 0m,
            SelfPayAmount = request.TotalAmount,
            PaymentChannel = request.PaymentChannel,
            Status = 1,
            PaidAtMs = request.PaidAtMs,
            InvoiceOfdId = GenerateFiscalInvoiceNo("BILL"),
            CreatedBy = request.OperatorId,
            UpdatedBy = request.OperatorId
        };
        await _paymentRepo.InsertAsync(payment, ct);
        await _chargeRepo.InsertAsync(new FinChargeLedger
        {
            PatientId = request.PatientId,
            VisitOrEncounterId = request.AppointmentId,
            OrderDeptId = request.OrderDeptId,
            ExecDeptId = request.OrderDeptId,
            ItemCode = "OPD_REGISTRATION_FEE",
            ItemName = "门诊挂号费",
            Quantity = 1,
            UnitPrice = request.TotalAmount,
            TotalAmount = request.TotalAmount,
            Status = 1,
            CreatedBy = request.OperatorId,
            UpdatedBy = request.OperatorId,
            ExtDataJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                paymentId = payment.Id,
                request.AppointmentNo
            })
        }, ct);
        return new RegistrationPaymentResultDto(
            payment.Id, payment.PaymentNo, payment.InvoiceOfdId ?? string.Empty, payment.Status);
    }

    public async Task<RegistrationPaymentResultDto?> GetRegistrationPaymentAsync(
        string paymentId,
        CancellationToken ct = default)
    {
        FinPaymentOrder? payment = await _paymentRepo.GetByIdAsync(paymentId, ct);
        return payment == null
            ? null
            : new RegistrationPaymentResultDto(
                payment.Id, payment.PaymentNo, payment.InvoiceOfdId ?? string.Empty, payment.Status);
    }

    public async Task<RegistrationRefundResultDto> CreateRegistrationRefundAsync(
        RegistrationRefundRequestDto request,
        CancellationToken ct = default)
    {
        FinRefundOrder refund = new()
        {
            RefundNo = "REF" + DateTime.UtcNow.ToString("yyMMddHHmmssfff")
                + Guid.NewGuid().ToString("N")[..3].ToUpperInvariant(),
            OriginalPaymentId = request.PaymentId,
            RefundAmount = request.RefundAmount,
            Reason = request.Reason,
            DrugReturnConfirmed = 0,
            ApprovedBy = request.OperatorId,
            RefundedAtMs = request.RefundedAtMs,
            Status = 3,
            CreatedBy = request.OperatorId,
            UpdatedBy = request.OperatorId
        };
        await _refundRepo.InsertAsync(refund, ct);
        // The caller owns the surrounding transaction, so report the intended
        // post-refund payment state without mutating the payment here.
        return new RegistrationRefundResultDto(refund.Id, refund.RefundNo, 2);
    }
}
