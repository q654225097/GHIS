using Dapper;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;
using HIS.Modules.Finance.Models;

namespace HIS.Modules.Finance.Services;

public sealed class FinanceSettlementContract : IFinanceSettlementContract
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
            InvoiceOfdId = "INV-OFD-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
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
