using System;
using System.Security.Cryptography;
using System.Text;

namespace HIS.BuildingBlocks.Security;

public static class CryptoHelper
{
	private static byte[]? _configuredHmacKey;
	private static byte[]? _configuredAesKey;

	public static void ConfigureKeys(string? hmacKey, string? aesKey)
	{
		if (!string.IsNullOrWhiteSpace(hmacKey))
		{
			_configuredHmacKey = Encoding.UTF8.GetBytes(hmacKey.Trim());
		}
		if (!string.IsNullOrWhiteSpace(aesKey))
		{
			_configuredAesKey = SHA256.HashData(Encoding.UTF8.GetBytes(aesKey.Trim()));
		}
	}

	public static string ComputeBlindIndex(string input, byte[]? hmacKey = null)
	{
		if (string.IsNullOrWhiteSpace(input))
		{
			return string.Empty;
		}
		using HMACSHA256 hMACSHA = new HMACSHA256(hmacKey ?? _configuredHmacKey ?? throw MissingKey("HIS_HMAC_KEY"));
		byte[] inArray = hMACSHA.ComputeHash(Encoding.UTF8.GetBytes(input.Trim()));
		return Convert.ToHexString(inArray).ToLowerInvariant();
	}

	public static string ComputeSha256(string input)
	{
		byte[] inArray = SHA256.HashData(Encoding.UTF8.GetBytes(input));
		return Convert.ToHexString(inArray).ToLowerInvariant();
	}

	public static string EncryptAesGcm(string plainText, byte[]? key = null)
	{
		if (string.IsNullOrEmpty(plainText))
		{
			return string.Empty;
		}
		byte[] key2 = key ?? _configuredAesKey ?? throw MissingKey("HIS_AES_KEY");
		byte[] bytes = Encoding.UTF8.GetBytes(plainText);
		byte[] array = new byte[12];
		RandomNumberGenerator.Fill(array);
		byte[] array2 = new byte[bytes.Length];
		byte[] array3 = new byte[16];
		using AesGcm aesGcm = new AesGcm(key2, 16);
		aesGcm.Encrypt(array, bytes, array2, array3);
		byte[] array4 = new byte[array.Length + array3.Length + array2.Length];
		Buffer.BlockCopy(array, 0, array4, 0, array.Length);
		Buffer.BlockCopy(array3, 0, array4, array.Length, array3.Length);
		Buffer.BlockCopy(array2, 0, array4, array.Length + array3.Length, array2.Length);
		return Convert.ToBase64String(array4);
	}

	public static string DecryptAesGcm(string cipherBase64, byte[]? key = null)
	{
		if (string.IsNullOrEmpty(cipherBase64))
		{
			return string.Empty;
		}
		byte[] key2 = key ?? _configuredAesKey ?? throw MissingKey("HIS_AES_KEY");
		byte[] array = Convert.FromBase64String(cipherBase64);
		byte[] array2 = new byte[12];
		byte[] array3 = new byte[16];
		byte[] array4 = new byte[array.Length - 28];
		Buffer.BlockCopy(array, 0, array2, 0, 12);
		Buffer.BlockCopy(array, 12, array3, 0, 16);
		Buffer.BlockCopy(array, 28, array4, 0, array4.Length);
		byte[] array5 = new byte[array4.Length];
		using AesGcm aesGcm = new AesGcm(key2, 16);
		aesGcm.Decrypt(array2, array4, array3, array5);
		return Encoding.UTF8.GetString(array5);
	}

	public static string MaskIdCard(string? idCard)
	{
		if (string.IsNullOrWhiteSpace(idCard) || idCard.Length < 8)
		{
			return "******";
		}
		return idCard.Substring(0, 4) + "**********" + idCard.Substring(idCard.Length - 4);
	}

	public static string MaskPhone(string? phone)
	{
		if (string.IsNullOrWhiteSpace(phone) || phone.Length < 7)
		{
			return "***";
		}
		return phone.Substring(0, 3) + "****" + phone.Substring(phone.Length - 4);
	}

	private static InvalidOperationException MissingKey(string keyName)
	{
		return new InvalidOperationException($"{keyName} 未配置，无法执行敏感数据加密或盲索引计算。");
	}
}
