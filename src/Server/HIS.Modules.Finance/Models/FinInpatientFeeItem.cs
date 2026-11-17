using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Finance.Models;

public class FinInpatientFeeItem : BaseEntity
{
	public string EncounterId { get; set; } = string.Empty;
	public string PatientId { get; set; } = string.Empty;
	public int FeeDateInt { get; set; }
	public string ItemCategory { get; set; } = "MEDICINE";
	public string ItemCode { get; set; } = string.Empty;
	public string ItemName { get; set; } = string.Empty;
	public string? Spec { get; set; }
	public string UnitName { get; set; } = "项";
	public decimal UnitPrice { get; set; }
	public decimal Quantity { get; set; }
	public decimal TotalAmount { get; set; }
	public decimal SelfPayRatio { get; set; } = 0;
	public string ReimburseType { get; set; } = "CLASS_A";
	public string ChargeDeptId { get; set; } = string.Empty;
	public string ExecDeptId { get; set; } = string.Empty;
	public string? OrderId { get; set; }
	public short IsSettled { get; set; } = 0;
	public string? SettlePaymentId { get; set; }
	public short IsRefunded { get; set; } = 0;
	public string? OriginalFeeItemId { get; set; }
}
