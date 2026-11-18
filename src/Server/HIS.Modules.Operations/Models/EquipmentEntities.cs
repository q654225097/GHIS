using System;
using System.Collections.Generic;
using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Operations.Models;

public class EqpEquipment : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ModelSpec { get; set; }
    public string DeptId { get; set; } = string.Empty;
    public string? PurchaseDate { get; set; }
    public decimal Price { get; set; } = 0m;
    public string? WarrantyEnd { get; set; }
    public short IsMandatoryInspection { get; set; } = 0;
    public string? NextInspectionDate { get; set; }
    public string Status { get; set; } = "NORMAL"; // NORMAL, MAINTENANCE, OUT_OF_SERVICE
}

public class EqpMaintenanceRecord : BaseEntity
{
    public string EquipmentId { get; set; } = string.Empty;
    public string Type { get; set; } = "ROUTINE"; // ROUTINE, PREVENTIVE, REPAIR, CALIBRATION
    public string MaintenanceDate { get; set; } = string.Empty;
    public decimal Cost { get; set; } = 0m;
    public string? Vendor { get; set; }
    public string? EngineerName { get; set; }
    public string Result { get; set; } = "QUALIFIED"; // QUALIFIED, REPAIRED, FAILED
    public string? Remark { get; set; }
}

public sealed record CreateEquipmentRequest(
    string Code,
    string Name,
    string? ModelSpec,
    string DeptId,
    string? PurchaseDate,
    decimal Price,
    string? WarrantyEnd,
    bool IsMandatoryInspection = false,
    string? NextInspectionDate = null
);

public sealed record RecordMaintenanceRequest(
    string Type,
    string MaintenanceDate,
    decimal Cost,
    string? Vendor,
    string? EngineerName,
    string Result,
    string? Remark
);

public sealed record EquipmentReportDto(
    int TotalEquipmentCount,
    int NormalCount,
    int MaintenanceCount,
    int OutOfServiceCount,
    decimal TotalAssetValue,
    int PendingCalibrationCount
);
