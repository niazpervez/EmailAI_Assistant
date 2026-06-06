using EmailAI.Core.Interfaces;
using System.Security.Cryptography;
using System.Text;

namespace EmailAI.Infrastructure.Security;

/// <summary>
/// Encrypts sensitive values using Windows DPAPI (Data Protection API).
/// Keys are scoped to the current Windows user — no plaintext credentials on disk.
/// </summary>
public sealed class DpapiEncryptionService : IEncryptionService
{
    // Optional entropy adds application-specific context
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("EmailAI-Assistant-v1-salt");

    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return string.Empty;
        var data = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return string.Empty;
        try
        {
            var data = Convert.FromBase64String(ciphertext);
            var decrypted = ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            // Return empty on failure rather than throwing — settings page handles missing key
            return string.Empty;
        }
    }
}
