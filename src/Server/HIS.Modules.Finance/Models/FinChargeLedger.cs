using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Finance.Models;

public class FinChargeLedger : BaseEntity
{
	public string PatientId { get; set; } = string.Empty;

	public string VisitOrEncounterId { get; set; } = string.Empty;

    public string? PaymentOrderId { get; set; }

    public string? SourceServiceRequestId { get; set; }

	public string OrderDeptId { get; set; } = string.Empty;

	public string? PatientWardId { get; set; }

	public string ExecDeptId { get; set; } = string.Empty;

	public string ItemCode { get; set; } = string.Empty;

	public string ItemName { get; set; } = string.Empty;

	public decimal Quantity { get; set; }

	public decimal UnitPrice { get; set; }

	public decimal TotalAmount { get; set; }

    public decimal RefundAmount { get; set; }

	public int Status { get; set; } = 1;
}
