using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Finance.Models;

public class FinDailyClosing : BaseEntity
{
	public string ClosingNo { get; set; } = string.Empty;

	public string CashierUserId { get; set; } = string.Empty;

	public int ClosingDateInt { get; set; }

	public decimal TotalIncome { get; set; }

	public decimal TotalRefund { get; set; }

	public decimal NetAmount { get; set; }

	public int ReconciledStatus { get; set; } = 1;
}
