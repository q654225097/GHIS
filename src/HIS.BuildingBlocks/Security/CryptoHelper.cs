using System.Security.Cryptography;
using System.Text;

namespace HIS.BuildingBlocks.Security;

public static class CryptoHelper
{
    private static readonly byte[] DefaultHmacKey = Encoding.UTF8.GetBytes("HIS_V4_HMAC_SECRET_KEY_2026_DEFAULT!");
    private static readonly byte[] DefaultAesKey = SHA256.HashData(Encoding.UTF8.GetBytes("HIS_V4_AES_MASTER_KEY_2026_DEFAULT!"));

    public static string ComputeBlindIndex(string input, byte[]? hmacKey = null)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        using var hmac = new HMACSHA256(hmacKey ?? DefaultHmacKey);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(input.Trim()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string EncryptAesGcm(string plainText, byte[]? key = null)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;
        var masterKey = key ?? DefaultAesKey;
        var plainBytes = Encoding.UTF8.GetBytes(plainText);

        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);

        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[16];

        using var aesGcm = new AesGcm(masterKey, 16);
        aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);

        // Format: Base64(nonce + tag + cipherBytes)
        var combined = new byte[nonce.Length + tag.Length + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipherBytes, 0, combined, nonce.Length + tag.Length, cipherBytes.Length);

        return Convert.ToBase64String(combined);
    }

    public static string DecryptAesGcm(string cipherBase64, byte[]? key = null)
    {
        if (string.IsNullOrEmpty(cipherBase64)) return string.Empty;
        var masterKey = key ?? DefaultAesKey;
        var combined = Convert.FromBase64String(cipherBase64);

        var nonce = new byte[12];
        var tag = new byte[16];
        var cipherBytes = new byte[combined.Length - 28];

        Buffer.BlockCopy(combined, 0, nonce, 0, 12);
        Buffer.BlockCopy(combined, 12, tag, 0, 16);
        Buffer.BlockCopy(combined, 28, cipherBytes, 0, cipherBytes.Length);

        var plainBytes = new byte[cipherBytes.Length];
        using var aesGcm = new AesGcm(masterKey, 16);
        aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }

    public static string MaskIdCard(string? idCard)
    {
        if (string.IsNullOrWhiteSpace(idCard) || idCard.Length < 8) return "******";
        return idCard.Substring(0, 4) + "**********" + idCard.Substring(idCard.Length - 4);
    }

    public static string MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone) || phone.Length < 7) return "***";
        return phone.Substring(0, 3) + "****" + phone.Substring(phone.Length - 4);
    }
}

