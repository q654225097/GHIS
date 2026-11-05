using HIS.BuildingBlocks.Database;
using HIS.Contracts;

namespace HIS.Modules.Platform.Models;

public class SysDepartment : BaseEntity
{
    public string CampusId { get; set; } = "CAMPUS-01";
    public string DeptCode { get; set; } = string.Empty;
    public string DeptName { get; set; } = string.Empty;
    public string? PinyinCode { get; set; }
    public string DeptCategory { get; set; } = nameof(Contracts.DepartmentCategory.OPD);
    public string? SubSpecialtyType { get; set; }
    public string? ParentAdminId { get; set; }
    public string? GenderLimit { get; set; }
    public int? MinAgeDays { get; set; }
    public int? MaxAgeYears { get; set; }
    public string? DefaultPharmacyId { get; set; }
    public int RatedBedCount { get; set; }
    public string? LocationAddress { get; set; }
    public short IsAllowAppointment { get; set; } = 1;
    public short IsActive { get; set; } = 1;
}

public class SysDeptWardRelation : BaseEntity
{
    public string IpdDeptId { get; set; } = string.Empty;
    public string WardDeptId { get; set; } = string.Empty;
    public string RelationType { get; set; } = nameof(DeptWardRelationType.PRIMARY);
    public int MaxBorrowBeds { get; set; } = 10;
    public int PriorityOrder { get; set; } = 1;
    public short IsActive { get; set; } = 1;
}

public class SysUser : BaseEntity
{
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string EmployeeNo { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? BoundPatientId { get; set; }
    public int PrescribeLevel { get; set; } = 1;
    public int SurgeryLevel { get; set; } = 1;
    public int EmrSignLevel { get; set; } = 1;
    public string? CaCertId { get; set; }
    public int SessionVersion { get; set; } = 1;
    public short IsActive { get; set; } = 1;
}

public class SysUserDeptPost : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public string DeptId { get; set; } = string.Empty;
    public string DeptCategory { get; set; } = nameof(Contracts.DepartmentCategory.OPD);
    public string? MedGroupId { get; set; }
    public string RoleId { get; set; } = string.Empty;
    public short IsDefaultContext { get; set; } = 1;
}

public class SysRole : BaseEntity
{
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string DataScope { get; set; } = nameof(Contracts.DataScope.ALL_CAMPUS);
    public short IsActive { get; set; } = 1;
}

public class SysPermissionResource : BaseEntity
{
    public string ResourceKey { get; set; } = string.Empty;
    public string? ParentKey { get; set; }
    public string ResourceType { get; set; } = nameof(Contracts.ResourceType.COMPONENT);
    public string ResourceName { get; set; } = string.Empty;
    public string? UrsaControlType { get; set; }
    public string? ApiMethodPath { get; set; }
}

public class SysRolePermission : BaseEntity
{
    public string RoleId { get; set; } = string.Empty;
    public string ResourceKey { get; set; } = string.Empty;
    public int AccessPolicy { get; set; } = (int)ControlAccessPolicy.FullAccess;
}

