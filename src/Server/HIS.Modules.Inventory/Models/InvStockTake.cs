using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Inventory.Models;

public class InvStockTake : BaseEntity
{
	public string StockTakeNo { get; set; } = string.Empty;
	public string WarehouseOrPharmacyId { get; set; } = string.Empty;
	public string DeptName { get; set; } = string.Empty;
	public string StockTakeType { get; set; } = "ROUTINE";
	public string Status { get; set; } = "DRAFT";
	public int TotalSystemQty { get; set; } = 0;
	public int TotalActualQty { get; set; } = 0;
	public int TotalDiffQty { get; set; } = 0;
	public decimal TotalDiffAmount { get; set; } = 0;
	public string CreatedByUserId { get; set; } = string.Empty;
	public string? ApprovedByUserId { get; set; }
	public long? ApprovedAtMs { get; set; }
	public string? ApprovedBy { get; set; }
	public string ApprovalStatus { get; set; } = "DRAFT";
	public string? WorkflowInstanceId { get; set; }
	public long? SubmittedAtMs { get; set; }
	public string? Remark { get; set; }
}
