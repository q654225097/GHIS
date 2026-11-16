using HIS.BuildingBlocks.Database;

namespace HIS.Modules.Outpatient.Models;

/// <summary>Active department/role grant used to select eligible outpatient doctors.</summary>
public sealed class OpdScheduleStaffOption : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string EmployeeNo { get; set; } = string.Empty;
    public string DeptId { get; set; } = string.Empty;
    public string DeptName { get; set; } = string.Empty;
    public string RoleId { get; set; } = string.Empty;
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
}
