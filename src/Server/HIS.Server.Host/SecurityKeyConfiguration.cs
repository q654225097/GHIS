using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HIS.Server.Host;

public sealed record SecurityKeys(string JwtSigningKey, string HmacKey, string AesMasterKey);

public static class SecurityKeyConfiguration
{
    public const int MinimumKeyLength = 32;

    public static SecurityKeys Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        string? jwtKey = configuration["HIS_JWT_KEY"];
        string? hmacKey = configuration["HIS_HMAC_KEY"];
        string? aesKey = configuration["HIS_AES_KEY"];

        string scope = environment.IsProduction()
            ? "生产环境"
            : $"环境 {environment.EnvironmentName}";

        Validate(jwtKey, "HIS_JWT_KEY", scope);
        Validate(hmacKey, "HIS_HMAC_KEY", scope);
        Validate(aesKey, "HIS_AES_KEY", scope);

        return new SecurityKeys(jwtKey!.Trim(), hmacKey!.Trim(), aesKey!.Trim());
    }

    private static void Validate(string? value, string name, string scope)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{scope}缺少 {name}。请通过环境变量或 appsettings 配置至少 {MinimumKeyLength} 字符的独立密钥。");
        }

        if (value.Trim().Length < MinimumKeyLength)
        {
            throw new InvalidOperationException(
                $"{scope}配置的 {name} 强度不足，至少需要 {MinimumKeyLength} 字符。");
        }
    }
}
