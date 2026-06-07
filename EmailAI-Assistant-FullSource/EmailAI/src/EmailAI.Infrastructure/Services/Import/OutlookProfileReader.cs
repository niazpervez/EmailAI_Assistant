using EmailAI.Core;
using EmailAI.Core.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Text;
using System.Text.RegularExpressions;

namespace EmailAI.Infrastructure.Services.Import;

/// <summary>Reads IMAP/SMTP account settings from Outlook registry profiles.</summary>
internal static class OutlookProfileReader
{
    private static readonly string[] OfficeVersions = ["16.0", "15.0", "14.0"];
    private const string AccountsGuidPrefix = "9375CFF041311";
    private static readonly Regex EmailRegex = new(
        @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // MAPI property tags stored as registry value names (PR_* unicode).
    private const string PrEmailAddress = "0103001f";
    private const string PrDisplayName = "011f0100";
    private const string PrImapServer = "011f6647";
    private const string PrSmtpServer = "011f6648";

    public static IReadOnlyList<DiscoveredMailAccount> ReadAccounts(ILogger logger)
    {
        var results = new List<DiscoveredMailAccount>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var profileRoot in GetProfileRoots())
        {
            try
            {
                using var profilesKey = Registry.CurrentUser.OpenSubKey(profileRoot);
                if (profilesKey is null) continue;

                foreach (var profileName in profilesKey.GetSubKeyNames())
                {
                    try
                    {
                        using var profileKey = profilesKey.OpenSubKey(profileName);
                        if (profileKey is null) continue;

                        foreach (var subKeyName in profileKey.GetSubKeyNames())
                        {
                            if (!subKeyName.StartsWith(AccountsGuidPrefix, StringComparison.OrdinalIgnoreCase))
                                continue;

                            using var accountsKey = profileKey.OpenSubKey(subKeyName);
                            if (accountsKey is null) continue;

                            foreach (var accountId in accountsKey.GetSubKeyNames())
                            {
                                try
                                {
                                    using var accountKey = accountsKey.OpenSubKey(accountId);
                                    if (accountKey is null) continue;

                                    var account = ParseAccount(accountKey, accountId, profileName);
                                    if (account is null) continue;
                                    if (!seen.Add(account.Email)) continue;
                                    results.Add(account);
                                }
                                catch (Exception ex)
                                {
                                    logger.LogDebug(ex, "Skipping Outlook account {Id}", accountId);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Could not read Outlook profile {Profile}", profileName);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not open Outlook profiles at {Path}", profileRoot);
            }
        }

        return results;
    }

    private static IEnumerable<string> GetProfileRoots()
    {
        yield return @"Software\Microsoft\Windows NT\CurrentVersion\Windows Messaging Subsystem\Profiles";
        foreach (var ver in OfficeVersions)
            yield return $@"Software\Microsoft\Office\{ver}\Outlook\Profiles";
    }

    private static DiscoveredMailAccount? ParseAccount(RegistryKey accountKey, string accountId, string profileName)
    {
        var serviceName = ReadDirectString(accountKey, "Service Name");
        if (serviceName?.Equals("CONTAB", StringComparison.OrdinalIgnoreCase) == true)
            return null;

        var email = ReadDirectString(accountKey, "Account Name");
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            email = ReadDirectString(accountKey, "Email");

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            email = ReadStringValue(accountKey, PrEmailAddress) ?? FindEmailInValues(accountKey);

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) return null;

        email = email.Trim();
        var displayName = ReadDirectString(accountKey, "Display Name")?.Trim()
                          ?? ReadStringValue(accountKey, PrDisplayName)?.Trim()
                          ?? email.Split('@')[0];

        var isExchange = serviceName?.Equals("MSEMS", StringComparison.OrdinalIgnoreCase) == true;

        var imapHost = ReadStringValue(accountKey, PrImapServer)
                       ?? FindServerInValues(accountKey, "imap");
        var smtpHost = ReadStringValue(accountKey, PrSmtpServer)
                       ?? FindServerInValues(accountKey, "smtp");

        var imapPort = FindPortInValues(accountKey, "imap") ?? 993;
        var smtpPort = FindPortInValues(accountKey, "smtp") ?? 587;

        var provider = MailProviderDetector.InferProvider(email, imapHost, smtpHost);
        var preset = MailProviderPresets.ApplyPreset(provider, new MailAccountConfig { Email = email });

        if (string.IsNullOrWhiteSpace(imapHost))
            imapHost = preset.ImapHost;
        if (string.IsNullOrWhiteSpace(smtpHost))
            smtpHost = preset.SmtpHost;

        if (string.IsNullOrWhiteSpace(imapHost) && provider == MailProvider.Custom)
            return null;

        var oauth = isExchange
                    || provider is MailProvider.Outlook or MailProvider.Gmail
                    || MailProviderDetector.LooksLikeMicrosoft(email, imapHost, smtpHost);

        var sourceId = $"outlook:{profileName}:{accountId}";
        var summary = BuildSummary("Outlook", email, imapHost, imapPort, oauth);

        return new DiscoveredMailAccount(
            ExternalMailClient.Outlook,
            sourceId,
            email,
            displayName,
            imapHost,
            imapPort > 0 ? imapPort : preset.ImapPort,
            smtpHost,
            smtpPort > 0 ? smtpPort : preset.SmtpPort,
            MailEncryptionMode.Auto,
            MailEncryptionMode.Auto,
            provider,
            oauth,
            summary);
    }

    private static string? ReadDirectString(RegistryKey key, string valueName)
    {
        var val = key.GetValue(valueName);
        if (val is string s) return s.Trim('\0', ' ');
        return DecodeRegistryString(val);
    }

    private static string? ReadStringValue(RegistryKey key, string valueName)
    {
        foreach (var name in key.GetValueNames())
        {
            if (!name.Equals(valueName, StringComparison.OrdinalIgnoreCase)) continue;
            return DecodeRegistryString(key.GetValue(name));
        }
        return null;
    }

    private static string? FindEmailInValues(RegistryKey key)
    {
        foreach (var name in key.GetValueNames())
        {
            var text = DecodeRegistryString(key.GetValue(name));
            if (string.IsNullOrWhiteSpace(text)) continue;
            var match = EmailRegex.Match(text);
            if (match.Success) return match.Value;
        }
        return null;
    }

    private static string? FindServerInValues(RegistryKey key, string kind)
    {
        foreach (var name in key.GetValueNames())
        {
            var text = DecodeRegistryString(key.GetValue(name));
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (text.Contains('@')) continue;
            if (text.Contains('.', StringComparison.Ordinal) &&
                text.Contains(kind, StringComparison.OrdinalIgnoreCase))
                return text.Trim();
        }

        foreach (var name in key.GetValueNames())
        {
            var text = DecodeRegistryString(key.GetValue(name));
            if (string.IsNullOrWhiteSpace(text) || text.Contains('@')) continue;
            if (LooksLikeHost(text)) return text.Trim();
        }

        return null;
    }

    private static int? FindPortInValues(RegistryKey key, string kind)
    {
        foreach (var name in key.GetValueNames())
        {
            if (!name.Contains(kind, StringComparison.OrdinalIgnoreCase)) continue;
            var val = key.GetValue(name);
            if (val is int i && i > 0 && i < 65536) return i;
        }
        return null;
    }

    private static string? DecodeRegistryString(object? value)
    {
        if (value is null) return null;
        if (value is string s) return s.Trim('\0', ' ');
        if (value is byte[] bytes && bytes.Length > 0)
        {
            if (bytes.Length >= 2 && bytes[0] == 0 && bytes[1] == 0)
                return null;
            try
            {
                var text = Encoding.Unicode.GetString(bytes).Trim('\0', ' ');
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
            catch { /* fall through */ }

            try
            {
                var text = Encoding.UTF8.GetString(bytes).Trim('\0', ' ');
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
            catch { /* ignore */ }
        }
        return value.ToString();
    }

    private static bool LooksLikeHost(string text)
    {
        if (text.Length < 4 || text.Contains(' ') || text.Contains('@')) return false;
        return text.Contains('.', StringComparison.Ordinal) &&
               !text.StartsWith('{') &&
               text.All(c => char.IsLetterOrDigit(c) || c is '.' or '-');
    }

    internal static string BuildSummary(
        string client, string email, string? imapHost, int imapPort, bool oauth)
    {
        var auth = oauth ? "OAuth recommended" : "App password";
        var server = string.IsNullOrWhiteSpace(imapHost) ? "preset servers" : $"{imapHost}:{imapPort}";
        return $"{client} · {email} · {server} · {auth}";
    }
}
