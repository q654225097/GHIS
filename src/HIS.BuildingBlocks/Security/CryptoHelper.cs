using System;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Asn1.GM;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Paddings;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities.Encoders;

namespace HIS.BuildingBlocks.Security;

/// <summary>
/// 医院信息系统密码运算助手：
/// 支持国密三级 (GB/T 39786-2021) 算法套件 (SM2 / SM3 / SM4)，并向前兼容经典 AES-GCM / SHA-256。
/// </summary>
public static class CryptoHelper
{
	private static byte[]? _configuredHmacKey;
	private static byte[]? _configuredAesKey;

	private static readonly byte[] DefaultHmacKey = Encoding.UTF8.GetBytes("HIS_SM3_HMAC_DEFAULT_KEY_202611!");
	private static readonly byte[] DefaultSm4Key = Encoding.UTF8.GetBytes("GHIS_SM4_KEY2026");

	// 演示根证书 SM2 公私钥对
	public const string DefaultCaPrivateKeyHex = "6fbc73082a937a0f7596ff521b444fb91e0a969f691885b5db7908b763ec2084";
	public static readonly string DefaultCaPublicKeyHex;

	private static readonly ECDomainParameters Sm2DomainParams;

	static CryptoHelper()
	{
		var x9 = GMNamedCurves.GetByName("sm2p256v1");
		Sm2DomainParams = new ECDomainParameters(x9.Curve, x9.G, x9.N, x9.H);
		var d = new BigInteger(1, Hex.Decode(DefaultCaPrivateKeyHex));
		var q = Sm2DomainParams.G.Multiply(d).Normalize();
		DefaultCaPublicKeyHex = Hex.ToHexString(q.GetEncoded(false));
	}

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

	#region SM3 & HMAC-SM3

	public static string ComputeSm3(string input)
	{
		if (string.IsNullOrEmpty(input))
		{
			return string.Empty;
		}
		return ComputeSm3(Encoding.UTF8.GetBytes(input));
	}

	public static string ComputeSm3(byte[] input)
	{
		byte[] result = ComputeSm3Bytes(input);
		return result.Length == 0 ? string.Empty : Convert.ToHexString(result).ToLowerInvariant();
	}

	public static byte[] ComputeSm3Bytes(byte[] input)
	{
		if (input == null || input.Length == 0)
		{
			return Array.Empty<byte>();
		}
		SM3Digest digest = new SM3Digest();
		digest.BlockUpdate(input, 0, input.Length);
		byte[] result = new byte[digest.GetDigestSize()];
		digest.DoFinal(result, 0);
		return result;
	}

	public static string ComputeHmacSm3(string input, byte[]? key = null)
	{
		if (string.IsNullOrEmpty(input))
		{
			return string.Empty;
		}
		byte[] k = key ?? _configuredHmacKey ?? DefaultHmacKey;
		byte[] inBytes = Encoding.UTF8.GetBytes(input);
		HMac hmac = new HMac(new SM3Digest());
		hmac.Init(new KeyParameter(k));
		hmac.BlockUpdate(inBytes, 0, inBytes.Length);
		byte[] result = new byte[hmac.GetMacSize()];
		hmac.DoFinal(result, 0);
		return Convert.ToHexString(result).ToLowerInvariant();
	}

	/// <summary>
	/// 基于国密 HMAC-SM3 计算患者隐私数据盲索引 (Blind Index)
	/// </summary>
	public static string ComputeBlindIndex(string input, byte[]? hmacKey = null)
	{
		if (string.IsNullOrWhiteSpace(input))
		{
			return string.Empty;
		}
		return ComputeHmacSm3(input.Trim(), hmacKey);
	}

	public static string ComputeSha256(string input)
	{
		byte[] inArray = SHA256.HashData(Encoding.UTF8.GetBytes(input));
		return Convert.ToHexString(inArray).ToLowerInvariant();
	}

	#endregion

	#region SM4 (GCM & CBC)

	private static byte[] GetSm4Key(byte[]? customKey)
	{
		byte[] source = customKey ?? _configuredAesKey ?? DefaultSm4Key;
		if (source.Length == 16)
		{
			return source;
		}
		byte[] sm4Key = new byte[16];
		if (source.Length > 16)
		{
			Buffer.BlockCopy(source, 0, sm4Key, 0, 16);
		}
		else
		{
			Buffer.BlockCopy(source, 0, sm4Key, 0, source.Length);
			for (int i = source.Length; i < 16; i++) sm4Key[i] = 0x30;
		}
		return sm4Key;
	}

	/// <summary>
	/// SM4-GCM 认证加密
	/// 格式：[12 bytes IV] + [密文 + 16 bytes Tag]
	/// </summary>
	public static string EncryptSm4Gcm(string plainText, byte[]? key = null)
	{
		if (string.IsNullOrEmpty(plainText))
		{
			return string.Empty;
		}
		byte[] sm4Key = GetSm4Key(key);
		byte[] inBytes = Encoding.UTF8.GetBytes(plainText);
		byte[] iv = new byte[12];
		RandomNumberGenerator.Fill(iv);

		GcmBlockCipher cipher = new GcmBlockCipher(new SM4Engine());
		AeadParameters parameters = new AeadParameters(new KeyParameter(sm4Key), 128, iv);
		cipher.Init(true, parameters);

		int outputSize = cipher.GetOutputSize(inBytes.Length);
		byte[] output = new byte[outputSize];
		int len = cipher.ProcessBytes(inBytes, 0, inBytes.Length, output, 0);
		cipher.DoFinal(output, len);

		byte[] combined = new byte[iv.Length + output.Length];
		Buffer.BlockCopy(iv, 0, combined, 0, iv.Length);
		Buffer.BlockCopy(output, 0, combined, iv.Length, output.Length);

		return Convert.ToBase64String(combined);
	}

	/// <summary>
	/// SM4-GCM 解密
	/// </summary>
	public static string DecryptSm4Gcm(string cipherBase64, byte[]? key = null)
	{
		if (string.IsNullOrEmpty(cipherBase64))
		{
			return string.Empty;
		}
		byte[] sm4Key = GetSm4Key(key);
		byte[] combined = Convert.FromBase64String(cipherBase64);
		if (combined.Length < 28)
		{
			throw new CryptographicException("密文长度不足，无效的 SM4-GCM 数据包");
		}

		byte[] iv = new byte[12];
		Buffer.BlockCopy(combined, 0, iv, 0, 12);
		byte[] cipherWithTag = new byte[combined.Length - 12];
		Buffer.BlockCopy(combined, 12, cipherWithTag, 0, cipherWithTag.Length);

		GcmBlockCipher cipher = new GcmBlockCipher(new SM4Engine());
		AeadParameters parameters = new AeadParameters(new KeyParameter(sm4Key), 128, iv);
		cipher.Init(false, parameters);

		int outSize = cipher.GetOutputSize(cipherWithTag.Length);
		byte[] output = new byte[outSize];
		int len = cipher.ProcessBytes(cipherWithTag, 0, cipherWithTag.Length, output, 0);
		int finalLen = len + cipher.DoFinal(output, len);

		return Encoding.UTF8.GetString(output, 0, finalLen);
	}

	/// <summary>
	/// SM4-CBC 分组加密 (带 PKCS7 填充)
	/// </summary>
	public static string EncryptSm4Cbc(string plainText, byte[]? key = null)
	{
		if (string.IsNullOrEmpty(plainText))
		{
			return string.Empty;
		}
		byte[] sm4Key = GetSm4Key(key);
		byte[] inBytes = Encoding.UTF8.GetBytes(plainText);
		byte[] iv = new byte[16];
		RandomNumberGenerator.Fill(iv);

		var cipher = new PaddedBufferedBlockCipher(new CbcBlockCipher(new SM4Engine()), new Pkcs7Padding());
		cipher.Init(true, new ParametersWithIV(new KeyParameter(sm4Key), iv));

		byte[] output = new byte[cipher.GetOutputSize(inBytes.Length)];
		int len = cipher.ProcessBytes(inBytes, 0, inBytes.Length, output, 0);
		int finalLen = len + cipher.DoFinal(output, len);

		byte[] combined = new byte[iv.Length + finalLen];
		Buffer.BlockCopy(iv, 0, combined, 0, iv.Length);
		Buffer.BlockCopy(output, 0, combined, iv.Length, finalLen);

		return Convert.ToBase64String(combined);
	}

	/// <summary>
	/// SM4-CBC 解密
	/// </summary>
	public static string DecryptSm4Cbc(string cipherBase64, byte[]? key = null)
	{
		if (string.IsNullOrEmpty(cipherBase64))
		{
			return string.Empty;
		}
		byte[] sm4Key = GetSm4Key(key);
		byte[] combined = Convert.FromBase64String(cipherBase64);
		if (combined.Length < 16)
		{
			throw new CryptographicException("密文长度不足，无效的 SM4-CBC 数据包");
		}

		byte[] iv = new byte[16];
		Buffer.BlockCopy(combined, 0, iv, 0, 16);
		byte[] cipherBytes = new byte[combined.Length - 16];
		Buffer.BlockCopy(combined, 16, cipherBytes, 0, cipherBytes.Length);

		var cipher = new PaddedBufferedBlockCipher(new CbcBlockCipher(new SM4Engine()), new Pkcs7Padding());
		cipher.Init(false, new ParametersWithIV(new KeyParameter(sm4Key), iv));

		byte[] output = new byte[cipher.GetOutputSize(cipherBytes.Length)];
		int len = cipher.ProcessBytes(cipherBytes, 0, cipherBytes.Length, output, 0);
		int finalLen = len + cipher.DoFinal(output, len);

		return Encoding.UTF8.GetString(output, 0, finalLen);
	}

	#endregion

	#region SM2 非对称签名与验签

	public static (string PublicKeyHex, string PrivateKeyHex) GenerateSm2KeyPair()
	{
		var keyGenParams = new ECKeyGenerationParameters(Sm2DomainParams, new SecureRandom());
		var generator = new ECKeyPairGenerator();
		generator.Init(keyGenParams);
		var pair = generator.GenerateKeyPair();
		var pub = (ECPublicKeyParameters)pair.Public;
		var priv = (ECPrivateKeyParameters)pair.Private;

		return (Hex.ToHexString(pub.Q.GetEncoded(false)), Hex.ToHexString(priv.D.ToByteArrayUnsigned()));
	}

	public static string SignSm2(byte[] data, string? privateKeyHex = null, string? userId = null)
	{
		if (data == null || data.Length == 0)
		{
			return string.Empty;
		}
		string privHex = string.IsNullOrWhiteSpace(privateKeyHex) ? DefaultCaPrivateKeyHex : privateKeyHex;
		byte[] privBytes = Hex.Decode(privHex);
		var privKey = new ECPrivateKeyParameters(new BigInteger(1, privBytes), Sm2DomainParams);

		var signer = new SM2Signer(new SM3Digest());
		if (string.IsNullOrWhiteSpace(userId))
		{
			signer.Init(true, new ParametersWithRandom(privKey, new SecureRandom()));
		}
		else
		{
			byte[] uid = Encoding.UTF8.GetBytes(userId);
			signer.Init(true, new ParametersWithID(new ParametersWithRandom(privKey, new SecureRandom()), uid));
		}
		signer.BlockUpdate(data, 0, data.Length);
		byte[] sig = signer.GenerateSignature();
		return Convert.ToBase64String(sig);
	}

	public static bool VerifySm2(byte[] data, string signatureBase64, string? publicKeyHex = null, string? userId = null)
	{
		if (data == null || data.Length == 0 || string.IsNullOrWhiteSpace(signatureBase64))
		{
			return false;
		}
		try
		{
			string pubHex = string.IsNullOrWhiteSpace(publicKeyHex) ? DefaultCaPublicKeyHex : publicKeyHex;
			byte[] pubBytes = Hex.Decode(pubHex);
			var q = Sm2DomainParams.Curve.DecodePoint(pubBytes);
			var pubKey = new ECPublicKeyParameters(q, Sm2DomainParams);

			var signer = new SM2Signer(new SM3Digest());
			if (string.IsNullOrWhiteSpace(userId))
			{
				signer.Init(false, pubKey);
			}
			else
			{
				byte[] uid = Encoding.UTF8.GetBytes(userId);
				signer.Init(false, new ParametersWithID(pubKey, uid));
			}
			signer.BlockUpdate(data, 0, data.Length);

			byte[] sig = Convert.FromBase64String(signatureBase64);
			return signer.VerifySignature(sig);
		}
		catch
		{
			return false;
		}
	}

	#endregion

	#region Legacy AES-GCM (Backward Compatible)

	public static string EncryptAesGcm(string plainText, byte[]? key = null)
	{
		if (string.IsNullOrEmpty(plainText))
		{
			return string.Empty;
		}
		byte[] key2 = key ?? _configuredAesKey ?? DefaultSm4Key;
		if (key2.Length != 16 && key2.Length != 24 && key2.Length != 32)
		{
			key2 = SHA256.HashData(key2);
		}
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
		byte[] key2 = key ?? _configuredAesKey ?? DefaultSm4Key;
		if (key2.Length != 16 && key2.Length != 24 && key2.Length != 32)
		{
			key2 = SHA256.HashData(key2);
		}
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

	#endregion
}
