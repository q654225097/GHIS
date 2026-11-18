using System;
using System.Collections.Generic;
using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Finance.Models;

public class FinCostCenter : BaseEntity
{
    public string DeptId { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty; // yyyy-MM
    public decimal DirectCost { get; set; } = 0m;
    public decimal AllocatedCost { get; set; } = 0m;
    public decimal TotalCost { get; set; } = 0m;
    public decimal Revenue { get; set; } = 0m;
    public decimal ProfitMargin { get; set; } = 0m;
    public string AllocationMethod { get; set; } = "STEP_DOWN";
}

public sealed record AllocateCostRequest(
    string DeptId,
    string Period,
    decimal DirectCost,
    decimal AllocatedCost,
    decimal Revenue,
    string? AllocationMethod = "STEP_DOWN"
);

public sealed record CostCenterReportDto(
    string DeptId,
    string Period,
    decimal DirectCost,
    decimal AllocatedCost,
    decimal TotalCost,
    decimal Revenue,
    decimal ProfitMargin,
    string AllocationMethod
);
