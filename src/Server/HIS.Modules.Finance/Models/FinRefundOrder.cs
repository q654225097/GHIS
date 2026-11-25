using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Finance.Models;

public class FinRefundOrder : BaseEntity
{
	public string RefundNo { get; set; } = string.Empty;

	public string OriginalPaymentId { get; set; } = string.Empty;

	public decimal RefundAmount { get; set; }

	public string Reason { get; set; } = string.Empty;

	public short DrugReturnConfirmed { get; set; } = 0;

	public string? ApprovedBy { get; set; }

	public long? RefundedAtMs { get; set; }

	public int Status { get; set; } = 1;

	public string? ReissuedInvoiceOfdId { get; set; }
	public string CampusId { get; set; } = "CAMPUS-01";
}
