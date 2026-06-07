using EmailAI.Core.Interfaces;
using EmailAI.Infrastructure.Services.Import;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
services.AddSingleton<IExternalMailImportService, ExternalMailImportService>();

await using var provider = services.BuildServiceProvider();
var import = provider.GetRequiredService<IExternalMailImportService>();

Console.WriteLine("=== External Mail Import Smoke Test ===\n");

var clients = await import.DetectInstalledClientsAsync();
Console.WriteLine($"Outlook installed: {clients.OutlookInstalled}" +
                  (clients.OutlookVersion is not null ? $" (v{clients.OutlookVersion})" : ""));
Console.WriteLine($"Thunderbird installed: {clients.ThunderbirdInstalled}" +
                  (clients.ThunderbirdPath is not null ? $" ({clients.ThunderbirdPath})" : ""));

if (!clients.OutlookInstalled && !clients.ThunderbirdInstalled)
{
    Console.WriteLine("\nNo Outlook or Thunderbird detected — import UI will stay hidden.");
    return 0;
}

var accounts = await import.DiscoverAccountsAsync();
Console.WriteLine($"\nDiscovered {accounts.Count} account(s):\n");

foreach (var a in accounts)
{
    Console.WriteLine($"  [{a.Source}] {a.Email}");
    Console.WriteLine($"    IMAP: {a.ImapHost}:{a.ImapPort}");
    Console.WriteLine($"    SMTP: {a.SmtpHost}:{a.SmtpPort}");
    Console.WriteLine($"    OAuth recommended: {a.IsOAuthCapable}");
    Console.WriteLine($"    {a.Summary}");

    var config = import.ToMailAccountConfig(a);
    Console.WriteLine($"    Mapped provider: {config.Provider}, auth: {config.AuthMethod}");
    Console.WriteLine();
}

return accounts.Count > 0 ? 0 : (clients.OutlookInstalled || clients.ThunderbirdInstalled ? 2 : 0);
