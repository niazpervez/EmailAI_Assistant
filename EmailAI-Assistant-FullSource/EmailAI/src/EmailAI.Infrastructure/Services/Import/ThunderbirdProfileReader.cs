using EmailAI.Core;
using EmailAI.Core.DTOs;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace EmailAI.Infrastructure.Services.Import;

/// <summary>Reads IMAP/SMTP account settings from Thunderbird profiles.ini and prefs.js.</summary>
internal static class ThunderbirdProfileReader
{
    private static readonly Regex UserPrefRegex = new(
        @"user_pref\(""([^""]+)"",\s*(.+?)\);",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<DiscoveredMailAccount> ReadAccounts(ILogger logger)
    {
        var results = new List<DiscoveredMailAccount>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var profilesIni = Path.Combine(appData, "Thunderbird", "profiles.ini");
        if (!File.Exists(profilesIni)) return results;

        foreach (var profilePath in ResolveProfilePaths(profilesIni))
        {
            var prefsPath = Path.Combine(profilePath, "prefs.js");
            if (!File.Exists(prefsPath)) continue;

            try
            {
                var prefs = ParsePrefs(File.ReadAllText(prefsPath));
                foreach (var account in BuildAccountsFromPrefs(prefs, profilePath))
                {
                    if (!seen.Add(account.Email)) continue;
                    results.Add(account);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not read Thunderbird profile at {Path}", profilePath);
            }
        }

        return results;
    }

    private static IEnumerable<string> ResolveProfilePaths(string profilesIniPath)
    {
        var iniDir = Path.GetDirectoryName(profilesIniPath)!;
        var lines = File.ReadAllLines(profilesIniPath);
        string? path = null;
        var isRelative = true;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("[Profile", StringComparison.OrdinalIgnoreCase))
            {
                if (path is not null)
                    yield return isRelative ? Path.Combine(iniDir, path) : path;
                path = null;
                isRelative = true;
                continue;
            }

            if (line.StartsWith("Path=", StringComparison.OrdinalIgnoreCase))
            {
                path = line["Path=".Length..].Trim();
                continue;
            }

            if (line.StartsWith("IsRelative=", StringComparison.OrdinalIgnoreCase))
                isRelative = line["IsRelative=".Length..].Trim() != "0";
        }

        if (path is not null)
            yield return isRelative ? Path.Combine(iniDir, path) : path;
    }

    private static Dictionary<string, string> ParsePrefs(string content)
    {
        var prefs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in UserPrefRegex.Matches(content))
        {
            var key = match.Groups[1].Value;
            var value = match.Groups[2].Value.Trim().Trim('"');
            prefs[key] = value;
        }
        return prefs;
    }

    private static IEnumerable<DiscoveredMailAccount> BuildAccountsFromPrefs(
        Dictionary<string, string> prefs, string profilePath)
    {
        var servers = prefs
            .Where(p => p.Key.StartsWith("mail.server.", StringComparison.OrdinalIgnoreCase)
                        && p.Key.EndsWith(".type", StringComparison.OrdinalIgnoreCase))
            .Select(p =>
            {
                var serverId = p.Key["mail.server.".Length..][..^".type".Length];
                return (ServerId: serverId, Type: p.Value.Trim('"'));
            })
            .Where(x => x.Type.Equals("imap", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var smtpServers = prefs
            .Where(p => p.Key.StartsWith("mail.smtpserver.", StringComparison.OrdinalIgnoreCase)
                        && p.Key.EndsWith(".hostname", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                p => p.Key["mail.smtpserver.".Length..][..^".hostname".Length],
                p => p.Value.Trim('"'),
                StringComparer.OrdinalIgnoreCase);

        var accounts = prefs
            .Where(p => p.Key.StartsWith("mail.account.account", StringComparison.OrdinalIgnoreCase)
                        && p.Key.EndsWith(".server", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var accountEntry in accounts)
        {
            var accountKey = accountEntry.Key["mail.account.".Length..][..^".server".Length];
            var serverId = accountEntry.Value.Trim('"');
            var server = servers.FirstOrDefault(s => s.ServerId.Equals(serverId, StringComparison.OrdinalIgnoreCase));
            if (server.ServerId is null) continue;

            var prefix = $"mail.server.{serverId}.";
            var hostname = GetPref(prefs, prefix + "hostname");
            if (string.IsNullOrWhiteSpace(hostname)) continue;

            var port = ParseInt(GetPref(prefs, prefix + "port"), 993);
            var username = GetPref(prefs, prefix + "username") ?? "";

            var identityId = GetPref(prefs, $"mail.account.{accountKey}.identity");
            var email = username;
            var displayName = GetPref(prefs, $"mail.account.{accountKey}.name") ?? "";

            if (!string.IsNullOrWhiteSpace(identityId))
            {
                identityId = identityId.Trim('"');
                var idPrefix = $"mail.identity.{identityId}.";
                email = GetPref(prefs, idPrefix + "useremail") ?? email;
                displayName = GetPref(prefs, idPrefix + "fullname") ?? displayName;
            }

            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) continue;

            var smtpServerId = identityId is not null
                ? GetPref(prefs, $"mail.identity.{identityId.Trim('"')}.smtpServer")?.Trim('"')
                : null;
            var smtpHost = smtpServerId is not null && smtpServers.TryGetValue(smtpServerId, out var h)
                ? h
                : null;
            var smtpPort = smtpServerId is not null
                ? ParseInt(GetPref(prefs, $"mail.smtpserver.{smtpServerId}.port"), 587)
                : 587;

            var socketType = ParseInt(GetPref(prefs, prefix + "socketType"), 3);
            var imapEncryption = MapSocketType(socketType);

            var provider = MailProviderDetector.InferProvider(email, hostname, smtpHost);
            var preset = MailProviderPresets.ApplyPreset(provider, new MailAccountConfig { Email = email });
            if (string.IsNullOrWhiteSpace(smtpHost))
                smtpHost = preset.SmtpHost;

            var oauth = provider is MailProvider.Outlook or MailProvider.Gmail;
            var profileName = Path.GetFileName(profilePath);
            var sourceId = $"thunderbird:{profileName}:{accountKey}:{serverId}";
            var summary = OutlookProfileReader.BuildSummary("Thunderbird", email, hostname, port, oauth);

            yield return new DiscoveredMailAccount(
                ExternalMailClient.Thunderbird,
                sourceId,
                email.Trim(),
                string.IsNullOrWhiteSpace(displayName) ? email.Split('@')[0] : displayName.Trim(),
                hostname.Trim(),
                port,
                smtpHost,
                smtpPort,
                imapEncryption,
                MailEncryptionMode.Auto,
                provider,
                oauth,
                summary);
        }
    }

    private static string? GetPref(Dictionary<string, string> prefs, string key)
        => prefs.TryGetValue(key, out var val) ? val.Trim('"') : null;

    private static int ParseInt(string? text, int fallback)
        => int.TryParse(text, out var n) && n > 0 ? n : fallback;

    private static MailEncryptionMode MapSocketType(int socketType) => socketType switch
    {
        1 or 3 => MailEncryptionMode.SslTls,
        2      => MailEncryptionMode.StartTls,
        0      => MailEncryptionMode.None,
        _      => MailEncryptionMode.Auto
    };
}
