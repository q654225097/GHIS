using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Finance.Models;

public class FinPaymentOrder : BaseEntity
{
	public string PaymentNo { get; set; } = string.Empty;

	public string? RequestKey { get; set; }

	public string Scene { get; set; } = "OPD";

	public string PatientId { get; set; } = string.Empty;

	public string VisitOrEncounterId { get; set; } = string.Empty;

	public decimal TotalAmount { get; set; }

	public decimal InsuranceAmount { get; set; }

	public decimal PrepaymentDeduct { get; set; }

	public decimal SelfPayAmount { get; set; }

	public string PaymentChannel { get; set; } = "WECHAT";

	public string? PrepaymentId { get; set; }

	public int Status { get; set; } = 0;

	public long? PaidAtMs { get; set; }

	public string? InvoiceOfdId { get; set; }
	public string CampusId { get; set; } = "CAMPUS-01";
}
