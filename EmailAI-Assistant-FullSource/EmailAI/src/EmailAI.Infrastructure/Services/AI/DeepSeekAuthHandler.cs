using EmailAI.Core;
using EmailAI.Core.Interfaces;
using System.Net.Http.Headers;

namespace EmailAI.Infrastructure.Services.AI;

/// <summary>
/// Injects the DeepSeek API key from settings into every outgoing HTTP request.
/// Do not set InnerHandler here — HttpClientFactory builds the handler pipeline.
/// </summary>
public sealed class DeepSeekAuthHandler : DelegatingHandler
{
    private readonly ISettingsRepository _settings;

    public DeepSeekAuthHandler(ISettingsRepository settings)
    {
        _settings = settings;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var apiKey = await _settings.GetAsync(SettingsKeys.DeepSeekApiKey, ct);
        apiKey = apiKey?.Trim();
        if (apiKey?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
            apiKey = apiKey["Bearer ".Length..].Trim();

        if (!string.IsNullOrEmpty(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        return await base.SendAsync(request, ct);
    }
}
