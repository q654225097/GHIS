using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Inpatient.Models;

public class IpdPrepayment : BaseEntity
{
    public string PrepaymentNo { get; set; } = string.Empty;

    public string RequestKey { get; set; } = string.Empty;

    public string EncounterId { get; set; } = string.Empty;

    public string PatientId { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public decimal BalanceAfter { get; set; }

    public string PaymentChannel { get; set; } = string.Empty;

    public string CashierId { get; set; } = string.Empty;

    public long PaidAtMs { get; set; }

    public string? Remark { get; set; }
}
