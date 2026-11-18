using System;
using System.Collections.Generic;
using HIS.BuildingBlocks.Common;
using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Consumables.Models;

public class CsmConsumable : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Spec { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public short IsSterile { get; set; }
    public short IsHighValue { get; set; }
    public decimal Price { get; set; }
    public short IsActive { get; set; } = 1;
}

public class CsmStock : BaseEntity
{
    public string ConsumableId { get; set; } = string.Empty;
    public string WarehouseId { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int ReservedQty { get; set; }
}

public class CsmBatch : BaseEntity
{
    public string ConsumableId { get; set; } = string.Empty;
    public string BatchNo { get; set; } = string.Empty;
    public string ExpiryDate { get; set; } = string.Empty;
    public string? ManufactureDate { get; set; }
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public short IsFrozen { get; set; }
}

public class CsmStockMovement : BaseEntity
{
    public string ConsumableId { get; set; } = string.Empty;
    public string? BatchId { get; set; }
    public string? FromWarehouseId { get; set; }
    public string? ToWarehouseId { get; set; }
    public string MovementType { get; set; } = string.Empty; // PURCHASE_IN, DEPT_ISSUE, WAREHOUSE_TRANSFER, DISCARD
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string? ReferenceId { get; set; }
    public string? Remark { get; set; }
}

public class CsmRequisition : BaseEntity
{
    public string RequisitionNo { get; set; } = string.Empty;
    public string WardId { get; set; } = string.Empty;
    public string DeptId { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING"; // PENDING, APPROVED, ISSUED, REJECTED
    public string RequestedBy { get; set; } = string.Empty;
    public string? ApprovedBy { get; set; }
    public string? IssuedBy { get; set; }
    public string? Remark { get; set; }
}

public class CsmRequisitionLine : BaseEntity
{
    public string RequisitionId { get; set; } = string.Empty;
    public string ConsumableId { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int IssuedQuantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class CsmSterilePackage : BaseEntity
{
    public string PackageCode { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string? ContentsJson { get; set; }
    public string? SterilizationBatchNo { get; set; }
    public string Status { get; set; } = "CREATED"; // CREATED, STERILIZED, ISSUED, USED, RETURNED, EXPIRED
    public long? ValidUntilMs { get; set; }
    public string CurrentLocation { get; set; } = "CSSD";
}

public class CsmSterilizationBatch : BaseEntity
{
    public string BatchNo { get; set; } = string.Empty;
    public string Method { get; set; } = "HIGH_PRESSURE_STEAM"; // HIGH_PRESSURE_STEAM, ETHYLENE_OXIDE, PLASMA
    public string OperatorId { get; set; } = string.Empty;
    public long StartedAtMs { get; set; }
    public long? CompletedAtMs { get; set; }
    public string Result { get; set; } = "QUALIFIED"; // QUALIFIED, UNQUALIFIED
    public string? Remark { get; set; }
}

public class CsmPackageMovement : BaseEntity
{
    public string PackageId { get; set; } = string.Empty;
    public string FromLocation { get; set; } = string.Empty;
    public string ToLocation { get; set; } = string.Empty;
    public string MovementType { get; set; } = string.Empty; // ISSUE, RETURN, RECALL
    public string OperatorId { get; set; } = string.Empty;
    public string? Remark { get; set; }
}

public class CsmTraceCode : BaseEntity
{
    public string ConsumableId { get; set; } = string.Empty;
    public string TraceCode { get; set; } = string.Empty;
    public string BatchNo { get; set; } = string.Empty;
    public string? PatientId { get; set; }
    public string? EncounterId { get; set; }
    public string Status { get; set; } = "IN_STOCK"; // IN_STOCK, USED, RECALLED
    public long? UsedAtMs { get; set; }
    public string? UsedBy { get; set; }
    public long? RecalledAtMs { get; set; }
    public string? RecallReason { get; set; }
}

// DTOs
public sealed record CreateConsumableRequestDto(
    string Code,
    string Name,
    string Spec,
    string Unit,
    string Category,
    bool IsSterile,
    bool IsHighValue,
    decimal Price
);

public sealed record AdjustConsumablePriceRequestDto(decimal NewPrice, string Reason);

public sealed record StockInRequestDto(
    string ConsumableId,
    string WarehouseId,
    string BatchNo,
    string ExpiryDate,
    string? ManufactureDate,
    int Quantity,
    decimal UnitCost
);

public sealed record CreateRequisitionRequestDto(
    string WardId,
    string DeptId,
    string? Remark,
    List<RequisitionLineItemDto> Items
);

public sealed record RequisitionLineItemDto(
    string ConsumableId,
    int Quantity
);

public sealed record TransferConsumableRequestDto(
    string ConsumableId,
    string FromWarehouseId,
    string ToWarehouseId,
    int Quantity,
    string? Remark
);

public sealed record CreateSterilePackageRequestDto(
    string PackageName,
    List<string> Contents,
    string CurrentLocation = "CSSD"
);

public sealed record SterilizePackageRequestDto(
    string SterilizationBatchNo,
    string Method = "HIGH_PRESSURE_STEAM",
    long ValidDays = 180
);

public sealed record IssueSterilePackageRequestDto(
    string TargetLocation,
    string? Remark = null
);

public sealed record ReturnSterilePackageRequestDto(
    string Reason,
    string TargetLocation = "CSSD"
);

public sealed record ScanUseTraceCodeRequestDto(
    string TraceCode,
    string PatientId,
    string? EncounterId,
    string? ClinicalNote = null
);

public sealed record ScanRecallTraceCodeRequestDto(
    string TraceCode,
    string RecallReason
);

public sealed record ConsumableStockSummaryView(
    string ConsumableId,
    string Code,
    string Name,
    string Spec,
    string Unit,
    string Category,
    bool IsHighValue,
    bool IsSterile,
    decimal Price,
    string WarehouseId,
    int Quantity,
    int ReservedQty
);

public sealed record RequisitionDetailView(
    CsmRequisition Requisition,
    List<RequisitionLineDetailView> Lines
);

public sealed record RequisitionLineDetailView(
    CsmRequisitionLine Line,
    string ConsumableCode,
    string ConsumableName,
    string Spec,
    string Unit
);

public sealed record ConsumableFinanceReportDto(
    int TotalConsumablesCount,
    int TotalStockQuantity,
    decimal TotalStockValuation,
    int RequisitionsCount,
    int TotalSterilePackages,
    int TotalUsedHighValueItems
);
