using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Inventory.Models;

public class InvDrug : BaseEntity
{
    public string DrugCode { get; set; } = string.Empty;
    public string DrugName { get; set; } = string.Empty;
    public string Spec { get; set; } = string.Empty;
    public string UnitName { get; set; } = "支";
    public decimal Price { get; set; }
    public decimal CostPrice { get; set; }
    public int AntibioticLevel { get; set; } = 0; // 0=Non, 1=Restricted, 2=Special
    public short IsNarcotic { get; set; } = 0;
    public short IsSkinTestRequired { get; set; } = 0;
    public short IsActive { get; set; } = 1;
}

public class InvDrugBatch : BaseEntity
{
    public string WarehouseOrPharmacyId { get; set; } = string.Empty;
    public string DrugId { get; set; } = string.Empty;
    public string BatchNo { get; set; } = string.Empty;
    public int ExpiryDateInt { get; set; }
    public string? InvoiceNo { get; set; }
    public decimal? ColdChainTempCelsius { get; set; }
    public int Quantity { get; set; }
    public short IsFrozen { get; set; } = 0;
}

public class InvStockLedger : BaseEntity
{
    public string WarehouseOrPharmacyId { get; set; } = string.Empty;
    public string DrugId { get; set; } = string.Empty;
    public string BatchId { get; set; } = string.Empty;
    public string ChangeType { get; set; } = "IN_PURCHASE"; // IN_PURCHASE / OUT_DISPENSE / IN_RETURN / ADJUST
    public int ChangeQuantity { get; set; }
    public int BalanceAfter { get; set; }
    public string? ReferenceDocNo { get; set; }
}

