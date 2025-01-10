using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Linq;

namespace Kalkatos.Network;

public static class EncryptionHelper
{
	public static string Encrypt (string plainText, string keyString)
	{
		byte[] key = GetValidKey(keyString);
		byte[] iv = RandomNumberGenerator.GetBytes(16); // Secure Initialization Vector

		using (Aes aesAlg = Aes.Create())
		{
			aesAlg.Key = key;
			aesAlg.IV = iv;

			ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

			using (MemoryStream msEncrypt = new MemoryStream())
			{
				using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
				{
					using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
					{
						swEncrypt.Write(plainText);
					}
				}

				byte[] encrypted = msEncrypt.ToArray();
				byte[] combinedIvAndCipherText = new byte[iv.Length + encrypted.Length];
				Array.Copy(iv, 0, combinedIvAndCipherText, 0, iv.Length);
				Array.Copy(encrypted, 0, combinedIvAndCipherText, iv.Length, encrypted.Length);

				return Convert.ToBase64String(combinedIvAndCipherText);
			}
		}
	}

	public static string Decrypt (string cipherText, string keyString)
	{
		byte[] fullCipherText = Convert.FromBase64String(cipherText);

		byte[] iv = new byte[16];
		Array.Copy(fullCipherText, 0, iv, 0, iv.Length);
		byte[] cipherTextBytes = new byte[fullCipherText.Length - iv.Length];
		Array.Copy(fullCipherText, iv.Length, cipherTextBytes, 0, cipherTextBytes.Length);

		byte[] key = GetValidKey(keyString);

		using (Aes aesAlg = Aes.Create())
		{
			aesAlg.Key = key;
			aesAlg.IV = iv;

			ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);

			using (MemoryStream msDecrypt = new MemoryStream(cipherTextBytes))
			{
				using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
				{
					using (StreamReader srDecrypt = new StreamReader(csDecrypt))
					{
						return srDecrypt.ReadToEnd();
					}
				}
			}
		}
	}

	private static byte[] GetValidKey (string keyString)
	{
		byte[] key = Encoding.UTF8.GetBytes(keyString);
		if (key.Length < 16) Array.Resize(ref key, 16); // Pad to 16 bytes
		else if (key.Length > 32) Array.Resize(ref key, 32); // Trim to 32 bytes
		return key.Take(32).ToArray(); // Ensure max length is 32
	}
}
