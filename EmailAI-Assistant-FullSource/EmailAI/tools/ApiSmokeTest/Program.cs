using EmailAI.Core;
using EmailAI.Core.Interfaces;
using EmailAI.Infrastructure.Data;
using EmailAI.Infrastructure.Data.Repositories;
using EmailAI.Infrastructure.Security;
using EmailAI.Infrastructure.Services.AI;
using Microsoft.Extensions.DependencyInjection;

var appData = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "EmailAI Assistant");
var dbPath = Path.Combine(appData, "emailai.db");

if (!File.Exists(dbPath))
{
    Console.WriteLine("FAIL: Database not found at " + dbPath);
    return 1;
}

var services = new ServiceCollection();
services.AddSingleton(_ => new DatabaseConnectionFactory(dbPath));
services.AddSingleton<IEncryptionService, DpapiEncryptionService>();
services.AddSingleton<ISettingsRepository, SettingsRepository>();
services.AddTransient<DeepSeekAuthHandler>();
services.AddHttpClient<DeepSeekAIService>((_, http) =>
{
    http.DefaultRequestHeaders.Add("Accept", "application/json");
}).AddHttpMessageHandler<DeepSeekAuthHandler>();

await using var provider = services.BuildServiceProvider();
var settings = provider.GetRequiredService<ISettingsRepository>();
var apiKey = await settings.GetAsync(SettingsKeys.DeepSeekApiKey);

if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.WriteLine("FAIL: No DeepSeek API key in settings.");
    return 1;
}

Console.WriteLine($"OK: API key loaded (length {apiKey.Length})");

var ai = provider.GetRequiredService<DeepSeekAIService>();
try
{
    var result = await ai.ExtractActionItemsAsync([]);
    Console.WriteLine("OK: DeepSeek API call succeeded.");
    Console.WriteLine("Response preview: " + result.Trim()[..Math.Min(120, result.Trim().Length)]);
    return 0;
}
catch (InvalidOperationException ex)
{
    Console.WriteLine("FAIL: " + ex.Message);
    return 1;
}
