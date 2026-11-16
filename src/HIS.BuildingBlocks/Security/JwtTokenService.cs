using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HIS.Contracts;
using Microsoft.IdentityModel.Tokens;

namespace HIS.BuildingBlocks.Security;

public class JwtTokenService
{
	private readonly byte[] _signingKey;

	private readonly string _issuer;

	private readonly string _audience;

	public JwtTokenService(string? secret = null, string issuer = "HIS.V4", string audience = "HIS.Client")
	{
		if (string.IsNullOrWhiteSpace(secret))
		{
			throw new ArgumentException("JWT signing key must be supplied through configuration.", nameof(secret));
		}
		_signingKey = Encoding.UTF8.GetBytes(secret.Trim());
		_issuer = issuer;
		_audience = audience;
	}

	public string GenerateAccessToken(string userId, string userName, string displayName, string campusId, DepartmentCategory activeDeptCategory, string activeDeptId, string? activeWardId, IEnumerable<string>? linkedWardIds, IEnumerable<string> roles, DataScope dataScope, int sessionVersion, string permHash, int prescribeLevel = 1, int surgeryLevel = 1, int emrSignLevel = 1, TimeSpan? expiry = null, string? activePostId = null, string? roleId = null, string? roleCode = null, string? roleName = null, string? activeDeptName = null)
	{
		List<Claim> claims = new List<Claim>
		{
			new Claim("sub", userId),
			new Claim("user_name", userName),
			new Claim("display_name", displayName),
			new Claim("campus_id", campusId),
			new Claim("active_dept_category", activeDeptCategory.ToString()),
			new Claim("active_dept_id", activeDeptId),
			new Claim("active_post_id", activePostId ?? string.Empty),
			new Claim("role_id", roleId ?? string.Empty),
			new Claim("role_code", roleCode ?? string.Join(",", roles)),
			new Claim("role_name", roleName ?? string.Empty),
			new Claim("dept_name", activeDeptName ?? string.Empty),
			new Claim("active_ward_id", activeWardId ?? string.Empty),
			new Claim("linked_ward_ids", string.Join(",", linkedWardIds ?? Array.Empty<string>())),
			new Claim("roles", string.Join(",", roles)),
			new Claim("data_scope", dataScope.ToString()),
			new Claim("ver", sessionVersion.ToString()),
			new Claim("perm_hash", permHash),
			new Claim("prescribe_level", prescribeLevel.ToString()),
			new Claim("surgery_level", surgeryLevel.ToString()),
			new Claim("emr_sign_level", emrSignLevel.ToString())
		};
		SigningCredentials signingCredentials = new SigningCredentials(new SymmetricSecurityKey(_signingKey), "HS256");
		string issuer = _issuer;
		string audience = _audience;
		DateTime? expires = DateTime.UtcNow.Add(expiry ?? TimeSpan.FromHours(8));
		SigningCredentials signingCredentials2 = signingCredentials;
		JwtSecurityToken token = new JwtSecurityToken(issuer, audience, claims, null, expires, signingCredentials2);
		return new JwtSecurityTokenHandler().WriteToken(token);
	}

	public ClaimsPrincipal? ValidateToken(string token)
	{
		// Keep the claim names emitted by GenerateAccessToken intact. The default
		// inbound map rewrites "roles" to ClaimTypes.Role, which made IUserSession
		// lose the active professional role on every authenticated request.
		JwtSecurityTokenHandler jwtSecurityTokenHandler = new JwtSecurityTokenHandler
		{
			MapInboundClaims = false
		};
		TokenValidationParameters validationParameters = new TokenValidationParameters
		{
			ValidateIssuerSigningKey = true,
			IssuerSigningKey = new SymmetricSecurityKey(_signingKey),
			ValidateIssuer = true,
			ValidIssuer = _issuer,
			ValidateAudience = true,
			ValidAudience = _audience,
			ValidateLifetime = true,
			ClockSkew = TimeSpan.FromMinutes(5L)
		};
		try
		{
			SecurityToken validatedToken;
			return jwtSecurityTokenHandler.ValidateToken(token, validationParameters, out validatedToken);
		}
		catch
		{
			return null;
		}
	}
}
