using EmailAI.Core;
using EmailAI.Core.Interfaces;
using System.Security.Cryptography;
using System.Text;

namespace EmailAI.Infrastructure.Security;

/// <summary>
/// Encrypts sensitive values using Windows DPAPI when available,
/// otherwise AES-256-GCM with a per-machine key stored in app data.
/// </summary>
public sealed class PlatformEncryptionService : IEncryptionService
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("EmailAI-Assistant-v1-salt");
    private static readonly object KeyLock = new();
    private static byte[]? _aesKey;

    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return string.Empty;

        if (OperatingSystem.IsWindows())
            return EncryptWindows(plaintext);

        return EncryptAes(plaintext);
    }

    public string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return string.Empty;

        try
        {
            if (OperatingSystem.IsWindows())
                return DecryptWindows(ciphertext);

            return DecryptAes(ciphertext);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string EncryptWindows(string plaintext)
    {
        var data = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    private static string DecryptWindows(string ciphertext)
    {
        var data = Convert.FromBase64String(ciphertext);
        var decrypted = ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }

    private static string EncryptAes(string plaintext)
    {
        var key = GetOrCreateAesKey();
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        var payload = new byte[nonce.Length + tag.Length + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipherBytes, 0, payload, nonce.Length + tag.Length, cipherBytes.Length);
        return Convert.ToBase64String(payload);
    }

    private static string DecryptAes(string ciphertext)
    {
        var payload = Convert.FromBase64String(ciphertext);
        if (payload.Length < 28) return string.Empty;

        var nonce = payload.AsSpan(0, 12);
        var tag = payload.AsSpan(12, 16);
        var cipherBytes = payload.AsSpan(28);
        var plainBytes = new byte[cipherBytes.Length];

        using var aes = new AesGcm(GetOrCreateAesKey(), 16);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private static byte[] GetOrCreateAesKey()
    {
        if (_aesKey is not null) return _aesKey;

        lock (KeyLock)
        {
            if (_aesKey is not null) return _aesKey;

            var keyPath = Path.Combine(AppPaths.GetAppDataDirectory(), ".encryption-key");
            Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);

            if (File.Exists(keyPath))
            {
                _aesKey = Convert.FromBase64String(File.ReadAllText(keyPath).Trim());
                return _aesKey;
            }

            _aesKey = RandomNumberGenerator.GetBytes(32);
            File.WriteAllText(keyPath, Convert.ToBase64String(_aesKey));
            return _aesKey;
        }
    }
}
