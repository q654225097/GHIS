using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Finance.Models;

public class FinPaymentOrder : BaseEntity
{
    public string PaymentNo { get; set; } = string.Empty;
    public string? RequestKey { get; set; }
    public string Scene { get; set; } = "OPD"; // OPD / IPD
    public string PatientId { get; set; } = string.Empty;
    public string VisitOrEncounterId { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal InsuranceAmount { get; set; }
    public decimal PrepaymentDeduct { get; set; }
    public decimal SelfPayAmount { get; set; }
    public string PaymentChannel { get; set; } = "WECHAT"; // WECHAT / ALIPAY / CASH / PREPAYMENT / UNIONPAY
    public int Status { get; set; } = 0; // 0=Pending, 1=Paid, 2=Refunded, 3=Cancelled
    public long? PaidAtMs { get; set; }
    public string? InvoiceOfdId { get; set; }
}

public class FinRefundOrder : BaseEntity
{
    public string RefundNo { get; set; } = string.Empty;
    public string OriginalPaymentId { get; set; } = string.Empty;
    public decimal RefundAmount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public short DrugReturnConfirmed { get; set; } = 0; // 1=Confirmed by pharmacy
    public string? ApprovedBy { get; set; }
    public long? RefundedAtMs { get; set; }
    public int Status { get; set; } = 1; // 1=Refunded
    public string? ReissuedInvoiceOfdId { get; set; }
}

public class FinChargeLedger : BaseEntity
{
    public string PatientId { get; set; } = string.Empty;
    public string VisitOrEncounterId { get; set; } = string.Empty;
    public string OrderDeptId { get; set; } = string.Empty;  // 开单临床科
    public string? PatientWardId { get; set; }               // 患者所在护理病区
    public string ExecDeptId { get; set; } = string.Empty;   // 执行医技/药房科室
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalAmount { get; set; }
    public int Status { get; set; } = 1; // 1=Valid, 2=Reversed
}

public class FinDailyClosing : BaseEntity
{
    public string ClosingNo { get; set; } = string.Empty;
    public string CashierUserId { get; set; } = string.Empty;
    public int ClosingDateInt { get; set; }
    public decimal TotalIncome { get; set; }
    public decimal TotalRefund { get; set; }
    public decimal NetAmount { get; set; }
    public int ReconciledStatus { get; set; } = 1; // 1=Reconciled
}

