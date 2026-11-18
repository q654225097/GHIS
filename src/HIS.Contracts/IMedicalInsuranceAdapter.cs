using System;
using System.Threading;
using System.Threading.Tasks;

namespace HIS.Contracts;

public sealed record InsurancePatientMatch(
    string InsuranceCardNo,
    string IdCardNo,
    string Name,
    string InsuredStatus,
    string PersonnelType,
    decimal AccountBalance
);

public sealed record InsuranceCatalogSync(
    int TotalItems,
    int MatchedItems,
    long SyncedAtMs,
    string Status
);

public sealed record InsuranceSettleRequest(
    string PatientId,
    string EncounterOrVisitId,
    decimal TotalAmount,
    decimal FundPaymentAmount,
    decimal PersonalAccountAmount,
    decimal CashAmount,
    string SettlementType
);

public sealed record InsuranceSettleResult(
    string InsuranceSettleNo,
    string PatientId,
    decimal TotalAmount,
    decimal FundPay,
    decimal PersonalAccountPay,
    decimal CashPay,
    string Status,
    long SettledAtMs
);

public sealed record InsuranceRefundResult(
    string InsuranceRefundNo,
    string OriginalSettleNo,
    decimal RefundAmount,
    string Status,
    long RefundedAtMs
);

public sealed record InsuranceBalance(
    string InsuranceCardNo,
    decimal Balance,
    string Status,
    long QueriedAtMs
);

public interface IMedicalInsuranceAdapter
{
    // 现有
    Task<string> PreSettleAsync(string patientId, decimal totalAmount, CancellationToken ct = default);
    Task<string> SettleAsync(string patientId, decimal totalAmount, CancellationToken ct = default);

    // 新增
    Task<InsurancePatientMatch> MatchPatientAsync(string idCardNo, string name, CancellationToken ct = default);
    Task<InsuranceCatalogSync> SyncCatalogAsync(CancellationToken ct = default);
    Task<InsuranceSettleResult> RealTimeSettleAsync(InsuranceSettleRequest req, CancellationToken ct = default);
    Task<InsuranceRefundResult> RefundAsync(string insuranceSettleNo, decimal amount, CancellationToken ct = default);
    Task<InsuranceBalance> QueryBalanceAsync(string insuranceCardNo, CancellationToken ct = default);
}
