using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Inventory.Models;

public class InvTransferOrder : BaseEntity
{
	public string TransferNo { get; set; } = string.Empty;

	public string TransferType { get; set; } = "WAREHOUSE_ISSUE";

	public string SourceWarehouseId { get; set; } = string.Empty;

	public string TargetWarehouseId { get; set; } = string.Empty;

	public string DrugId { get; set; } = string.Empty;

	public string DrugNameSnapshot { get; set; } = string.Empty;

	public string BatchId { get; set; } = string.Empty;

	public string BatchNo { get; set; } = string.Empty;

	public int ExpiryDateInt { get; set; }

	public int Quantity { get; set; }

	public decimal UnitCostPrice { get; set; }

	public decimal UnitRetailPrice { get; set; }

	public decimal TotalRetailAmount { get; set; }

	public int Status { get; set; } = 0;

	public string? ApplicantId { get; set; }

	public string? DispatchedById { get; set; }

	public long? DispatchedAtMs { get; set; }

	public string? ReceivedById { get; set; }

	public long? ReceivedAtMs { get; set; }

	public string? Remark { get; set; }
	public string ApprovalStatus { get; set; } = "DRAFT";
	public string? WorkflowInstanceId { get; set; }
	public long? SubmittedAtMs { get; set; }
	public long? ApprovedAtMs { get; set; }
	public string? ApprovedBy { get; set; }
}
