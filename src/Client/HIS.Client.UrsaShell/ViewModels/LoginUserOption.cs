namespace HIS.Client.UrsaShell.ViewModels;

public sealed class LoginUserOption
{
	public string UserName { get; set; } = string.Empty;

	public string DisplayName { get; set; } = string.Empty;

	public string RoleName { get; set; } = string.Empty;

	public string RoleCode { get; set; } = string.Empty;

	public string EmployeeNo { get; set; } = string.Empty;

	public string Initials => string.IsNullOrWhiteSpace(DisplayName)
		? string.Empty
		: DisplayName.Trim()[^1..];
}
