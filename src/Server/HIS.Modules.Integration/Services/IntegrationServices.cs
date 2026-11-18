using System;
using System.Threading;
using System.Threading.Tasks;
using HIS.BuildingBlocks.Database;
using HIS.Contracts;

namespace HIS.Modules.Integration.Services;

public abstract class MedicalInsuranceProviderBase : IMedicalInsuranceAdapter
{
    public abstract Task<string> PreSettleAsync(string patientId, decimal totalAmount, CancellationToken ct = default);
    public abstract Task<string> SettleAsync(string patientId, decimal totalAmount, CancellationToken ct = default);
    public abstract Task<InsurancePatientMatch> MatchPatientAsync(string idCardNo, string name, CancellationToken ct = default);
    public abstract Task<InsuranceCatalogSync> SyncCatalogAsync(CancellationToken ct = default);
    public abstract Task<InsuranceSettleResult> RealTimeSettleAsync(InsuranceSettleRequest req, CancellationToken ct = default);
    public abstract Task<InsuranceRefundResult> RefundAsync(string insuranceSettleNo, decimal amount, CancellationToken ct = default);
    public abstract Task<InsuranceBalance> QueryBalanceAsync(string insuranceCardNo, CancellationToken ct = default);
}

public class MockMedicalInsuranceAdapter : MedicalInsuranceProviderBase
{
    public override Task<string> PreSettleAsync(string patientId, decimal totalAmount, CancellationToken ct = default)
    {
        return Task.FromResult($"INS-PRE-{Guid.NewGuid():N}");
    }

    public override Task<string> SettleAsync(string patientId, decimal totalAmount, CancellationToken ct = default)
    {
        return Task.FromResult($"INS-SETTLE-{Guid.NewGuid():N}");
    }

    public override Task<InsurancePatientMatch> MatchPatientAsync(string idCardNo, string name, CancellationToken ct = default)
    {
        return Task.FromResult(new InsurancePatientMatch(
            InsuranceCardNo: "INS" + (idCardNo.Length >= 8 ? idCardNo[..8] : "00000000"),
            IdCardNo: idCardNo,
            Name: name,
            InsuredStatus: "NORMAL",
            PersonnelType: "URBAN_EMPLOYEE",
            AccountBalance: 8888.88m
        ));
    }

    public override Task<InsuranceCatalogSync> SyncCatalogAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new InsuranceCatalogSync(
            TotalItems: 2450,
            MatchedItems: 2432,
            SyncedAtMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Status: "SUCCESS"
        ));
    }

    public override Task<InsuranceSettleResult> RealTimeSettleAsync(InsuranceSettleRequest req, CancellationToken ct = default)
    {
        decimal fund = Math.Round(req.TotalAmount * 0.7m, 2);
        decimal self = req.TotalAmount - fund;
        return Task.FromResult(new InsuranceSettleResult(
            InsuranceSettleNo: $"INS-STL-{Guid.NewGuid():N}",
            PatientId: req.PatientId,
            TotalAmount: req.TotalAmount,
            FundPay: fund,
            PersonalAccountPay: self,
            CashPay: 0m,
            Status: "SETTLED",
            SettledAtMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    public override Task<InsuranceRefundResult> RefundAsync(string insuranceSettleNo, decimal amount, CancellationToken ct = default)
    {
        return Task.FromResult(new InsuranceRefundResult(
            InsuranceRefundNo: $"INS-RFD-{Guid.NewGuid():N}",
            OriginalSettleNo: insuranceSettleNo,
            RefundAmount: amount,
            Status: "REFUNDED",
            RefundedAtMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    public override Task<InsuranceBalance> QueryBalanceAsync(string insuranceCardNo, CancellationToken ct = default)
    {
        return Task.FromResult(new InsuranceBalance(
            InsuranceCardNo: insuranceCardNo,
            Balance: 8888.88m,
            Status: "NORMAL",
            QueriedAtMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }
}
