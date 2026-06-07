using EmailAI.Core;
using EmailAI.Core.DTOs;
using EmailAI.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace EmailAI.Infrastructure.Services.Import;

public sealed class ExternalMailImportService : IExternalMailImportService
{
    private readonly ILogger<ExternalMailImportService> _logger;

    public ExternalMailImportService(ILogger<ExternalMailImportService> logger)
    {
        _logger = logger;
    }

    public Task<InstalledMailClientsInfo> DetectInstalledClientsAsync(CancellationToken ct = default)
    {
        var outlookVersion = DetectOutlookVersion();
        var thunderbirdPath = FindThunderbirdExecutable();

        var info = new InstalledMailClientsInfo(
            OutlookInstalled: outlookVersion is not null,
            ThunderbirdInstalled: thunderbirdPath is not null,
            OutlookVersion: outlookVersion,
            ThunderbirdPath: thunderbirdPath);

        return Task.FromResult(info);
    }

    public Task<IReadOnlyList<DiscoveredMailAccount>> DiscoverAccountsAsync(
        ExternalMailClient? client = null, CancellationToken ct = default)
    {
        var accounts = new List<DiscoveredMailAccount>();

        if (client is null or ExternalMailClient.Outlook)
        {
            try
            {
                accounts.AddRange(OutlookProfileReader.ReadAccounts(_logger));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Outlook account discovery failed");
            }
        }

        if (client is null or ExternalMailClient.Thunderbird)
        {
            try
            {
                accounts.AddRange(ThunderbirdProfileReader.ReadAccounts(_logger));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Thunderbird account discovery failed");
            }
        }

        var distinct = accounts
            .GroupBy(a => a.Email, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(a => a.Source)
            .ThenBy(a => a.Email, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger.LogInformation("Discovered {Count} external mail account(s)", distinct.Count);
        return Task.FromResult<IReadOnlyList<DiscoveredMailAccount>>(distinct);
    }

    public MailAccountConfig ToMailAccountConfig(DiscoveredMailAccount account)
    {
        var config = new MailAccountConfig
        {
            Email = account.Email,
            Provider = account.Provider,
            ImapHost = account.ImapHost ?? "",
            ImapPort = account.ImapPort,
            SmtpHost = account.SmtpHost ?? "",
            SmtpPort = account.SmtpPort,
            UseSsl = true,
            ImapEncryption = account.ImapEncryption,
            SmtpEncryption = account.SmtpEncryption,
            AuthMethod = account.IsOAuthCapable ? MailAuthMethod.OAuth2 : MailAuthMethod.AppPassword
        };

        return MailProviderPresets.ApplyPreset(account.Provider, config);
    }

    private static string? DetectOutlookVersion()
    {
        foreach (var ver in new[] { "16.0", "15.0", "14.0" })
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Office\{ver}\Outlook");
            if (key is not null) return ver;
        }

        if (File.Exists(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Microsoft Office", "root", "Office16", "OUTLOOK.EXE")))
            return "16.0";

        if (File.Exists(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft Office", "root", "Office16", "OUTLOOK.EXE")))
            return "16.0";

        return null;
    }

    private static string? FindThunderbirdExecutable()
    {
        var appDataIni = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Thunderbird", "profiles.ini");
        if (File.Exists(appDataIni)) return appDataIni;

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Mozilla Thunderbird", "thunderbird.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Mozilla Thunderbird", "thunderbird.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
