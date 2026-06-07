using EmailAI.Core.Interfaces;
using EmailAI.Infrastructure.Data;
using EmailAI.Infrastructure.Data.Repositories;
using EmailAI.Infrastructure.Security;
using EmailAI.Infrastructure.Services.Mail;
using Microsoft.Extensions.DependencyInjection;

var appData = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "EmailAI Assistant");
var dbPath = Path.Combine(appData, "emailai.db");

var services = new ServiceCollection();
services.AddSingleton(_ => new DatabaseConnectionFactory(dbPath));
services.AddSingleton<IEncryptionService, PlatformEncryptionService>();
services.AddSingleton<ISettingsRepository, SettingsRepository>();
services.AddSingleton<MailOAuthService>();
services.AddHttpClient();
services.AddSingleton<IMailService, ImapMailService>();

await using var provider = services.BuildServiceProvider();
var mail = provider.GetRequiredService<IMailService>();

if (!await mail.IsAuthenticatedAsync())
{
    Console.WriteLine("FAIL: Email account not connected.");
    return 1;
}

var folders = (await mail.GetMailFoldersAsync()).ToList();
Console.WriteLine($"Found {folders.Count} folder(s):");
foreach (var f in folders)
    Console.WriteLine($"  - {f.DisplayName} (id: {f.Id}, unread: {f.UnreadCount}, total: {f.TotalCount})");

return folders.Count > 1 ? 0 : 1;
