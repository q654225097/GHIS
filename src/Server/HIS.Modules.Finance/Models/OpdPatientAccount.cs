using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Finance.Models;

public class OpdPatientAccount : BaseEntity
{
    public string PatientId { get; set; } = string.Empty;
    public string AccountNo { get; set; } = string.Empty;
    public string? CardNo { get; set; }
    public decimal Balance { get; set; }
    public decimal FrozenAmount { get; set; }
    public int Status { get; set; } = 1; // 1: Active, 2: Frozen, 3: Closed
}

public class OpdAccountLedger : BaseEntity
{
    public string AccountId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string TransType { get; set; } = "RECHARGE"; // RECHARGE, DEDUCT, REFUND
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public string? PaymentChannel { get; set; } = "CASH";
    public string OperatorId { get; set; } = string.Empty;
    public string? PrescriptionId { get; set; }
    public string? PaymentOrderId { get; set; }
    public string? Remark { get; set; }
}
