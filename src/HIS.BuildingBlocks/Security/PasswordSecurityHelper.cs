using System;
using System.Security.Cryptography;
using System.Text;

namespace HIS.BuildingBlocks.Security;

/// <summary>
/// 密码安全辅助类：采用 PBKDF2-HMAC-SHA256 加盐慢哈希算法，提供抗离线彩虹表与爆破防护，
/// 并支持从旧版无盐 SHA-256 平滑无感升级。
/// </summary>
public static class PasswordSecurityHelper
{
    private const int SaltByteSize = 16;
    private const int HashByteSize = 32;
    private const int DefaultIterations = 10000;
    private const string Prefix = "PBKDF2";

    /// <summary>
    /// 对明文密码进行安全加盐 PBKDF2 哈希
    /// 格式：PBKDF2$10000${SaltBase64}${HashBase64}
    /// </summary>
    public static string HashPassword(string password, int iterations = DefaultIterations)
    {
        if (password == null) throw new ArgumentNullException(nameof(password));

        byte[] salt = RandomNumberGenerator.GetBytes(SaltByteSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            HashByteSize);

        return $"{Prefix}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// 验证密码是否匹配，并返回是否需要升级到新版加盐哈希
    /// </summary>
    public static (bool IsValid, bool NeedsUpgrade) VerifyPassword(string inputPassword, string storedHash)
    {
        if (string.IsNullOrEmpty(inputPassword) || string.IsNullOrEmpty(storedHash))
        {
            return (false, false);
        }

        // 1. 如果已是 PBKDF2 格式
        if (storedHash.StartsWith(Prefix + "$", StringComparison.Ordinal))
        {
            var parts = storedHash.Split('$');
            if (parts.Length == 4 && int.TryParse(parts[1], out int iterations))
            {
                try
                {
                    byte[] salt = Convert.FromBase64String(parts[2]);
                    byte[] expectedHash = Convert.FromBase64String(parts[3]);

                    byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
                        Encoding.UTF8.GetBytes(inputPassword),
                        salt,
                        iterations,
                        HashAlgorithmName.SHA256,
                        expectedHash.Length);

                    bool matches = CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
                    return (matches, false);
                }
                catch
                {
                    return (false, false);
                }
            }
        }

        // 2. 兼容旧版无盐 SHA-256 或历史明文格式
        string legacySha256 = CryptoHelper.ComputeSha256(inputPassword);
        if (string.Equals(storedHash, legacySha256, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(storedHash, inputPassword, StringComparison.Ordinal))
        {
            return (true, true); // 验证通过，但需要平滑升级为加盐哈希
        }

        return (false, false);
    }
}

