using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace XboxMetroLauncher.Utilities;

/// <summary>
/// Linux replacement for DPAPI: AES-GCM with a per-user random key stored in the user data folder (mode 0600).
/// Values protected on Windows (DPAPI) cannot be read here and simply come back empty (the user re-connects).
/// </summary>
internal static class SecureStringStorage
{
	private const string Prefix = "lx1:";

	private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DashX360.DiscordSession");

	private static byte[] GetKey()
	{
		string path = Path.Combine(AppPaths.UserDataFolder, ".secret.key");
		if (File.Exists(path))
		{
			byte[] existing = File.ReadAllBytes(path);
			if (existing.Length == 32)
			{
				return existing;
			}
		}
		byte[] key = RandomNumberGenerator.GetBytes(32);
		File.WriteAllBytes(path, key);
		try
		{
			File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
		}
		catch
		{
		}
		return key;
	}

	public static string Protect(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}
		byte[] plain = Encoding.UTF8.GetBytes(value);
		byte[] nonce = RandomNumberGenerator.GetBytes(12);
		byte[] cipher = new byte[plain.Length];
		byte[] tag = new byte[16];
		using (AesGcm aes = new AesGcm(GetKey(), 16))
		{
			aes.Encrypt(nonce, plain, cipher, tag, Entropy);
		}
		byte[] blob = new byte[nonce.Length + tag.Length + cipher.Length];
		nonce.CopyTo(blob, 0);
		tag.CopyTo(blob, nonce.Length);
		cipher.CopyTo(blob, nonce.Length + tag.Length);
		return Prefix + Convert.ToBase64String(blob);
	}

	public static string Unprotect(string? protectedValue)
	{
		if (string.IsNullOrWhiteSpace(protectedValue) || !protectedValue.StartsWith(Prefix, StringComparison.Ordinal))
		{
			return string.Empty;
		}
		try
		{
			byte[] blob = Convert.FromBase64String(protectedValue[Prefix.Length..]);
			byte[] nonce = blob[..12];
			byte[] tag = blob[12..28];
			byte[] cipher = blob[28..];
			byte[] plain = new byte[cipher.Length];
			using AesGcm aes = new AesGcm(GetKey(), 16);
			aes.Decrypt(nonce, cipher, tag, plain, Entropy);
			return Encoding.UTF8.GetString(plain);
		}
		catch
		{
			return string.Empty;
		}
	}
}
