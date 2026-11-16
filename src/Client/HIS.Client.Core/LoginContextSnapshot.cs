namespace HIS.Client.Core;

public sealed record LoginContextSnapshot(
    string PostId,
    string UserId,
    string UserName,
    string DisplayName,
    string EmployeeNo,
    int PrescribeLevel,
    int SurgeryLevel,
    int EmrSignLevel,
    string RoleId,
    string RoleCode,
    string RoleName,
    string DeptId,
    string DeptName,
    string DeptCategory,
    string? WardId,
    string DataScope,
    bool IsDefault);
