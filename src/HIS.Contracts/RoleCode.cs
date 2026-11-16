using System;
using System.Collections.Generic;
using System.Linq;

namespace HIS.Contracts;

/// <summary>
/// Canonical role comparison shared by server authorization and client capability checks.
/// Seed data and tokens historically mixed ROLE-ADMIN, ADMIN and ROLE_ADMIN spellings.
/// </summary>
public static class RoleCode
{
	public const string Admin = "ADMIN";
	public const string Cashier = "CASHIER";
	public const string Pharmacist = "PHARMACIST";
	public const string OpdDoctor = "OPD-DOC";
	public const string DoctorOpd = "OPD-DOC";
	public const string IpdResident = "IPD-RESIDENT";
	public const string IpdAttending = "IPD-ATTENDING";
	public const string DoctorIpd = "IPD-ATTENDING";
	public const string WardNurse = "WARD-NURSE";
	public const string NurseIpd = "WARD-NURSE";

	public static string Normalize(string? role)
	{
		if (string.IsNullOrWhiteSpace(role))
		{
			return string.Empty;
		}

		var value = role.Trim().ToUpperInvariant().Replace('_', '-');
		while (value.StartsWith("ROLE-", StringComparison.Ordinal))
		{
			value = value[5..];
		}

		return value switch
		{
			"SUPER-ADMIN" => Admin,
			"SYSTEM-ADMIN" => Admin,
			_ => value
		};
	}

	public static bool IsAdmin(string? role) =>
		string.Equals(Normalize(role), Admin, StringComparison.Ordinal);

	public static bool HasRole(IEnumerable<string>? roles, string expected)
	{
		var normalizedExpected = Normalize(expected);
		if (normalizedExpected.Length == 0)
		{
			return false;
		}

		return roles?.Any(role => string.Equals(Normalize(role), normalizedExpected, StringComparison.Ordinal)) == true;
	}

	public static bool HasAnyRole(IEnumerable<string>? roles, params string[] expected)
	{
		foreach (var role in expected)
		{
			if (HasRole(roles, role))
			{
				return true;
			}
		}

		return false;
	}

	public static bool Matches(string? role, string expected) =>
		string.Equals(Normalize(role), Normalize(expected), StringComparison.Ordinal);
}
