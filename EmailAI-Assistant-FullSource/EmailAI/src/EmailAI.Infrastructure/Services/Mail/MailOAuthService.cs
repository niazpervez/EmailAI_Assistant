using EmailAI.Core;
using EmailAI.Core.DTOs;
using EmailAI.Core.Interfaces;
using Microsoft.Identity.Client;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EmailAI.Infrastructure.Services.Mail;

public sealed record OAuthSignInResult(
    string Email,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    MailProvider Provider);

/// <summary>
/// Interactive OAuth sign-in for Gmail and Microsoft (Outlook/Hotmail).
/// Opens the system browser — approval appears in the browser, not inside EmailAI.
/// </summary>
public sealed class MailOAuthService
{
    private const int GoogleLoopbackPort = 7890;
    private static readonly string GoogleRedirectUri = $"http://127.0.0.1:{GoogleLoopbackPort}/";

    private readonly ISettingsRepository _settings;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IOAuthParentWindowProvider? _windowProvider;

    public MailOAuthService(
        ISettingsRepository settings,
        IHttpClientFactory httpFactory,
        IOAuthParentWindowProvider? windowProvider = null)
    {
        _settings = settings;
        _httpFactory = httpFactory;
        _windowProvider = windowProvider;
    }

    public async Task<OAuthSignInResult> SignInGoogleAsync(CancellationToken ct = default)
    {
        var clientId = await RequireSettingAsync(SettingsKeys.OAuthGoogleClientId, "Google OAuth Client ID", ct);
        var clientSecret = await _settings.GetAsync(SettingsKeys.OAuthGoogleClientSecret, ct) ?? "";

        var code = await WaitForAuthorizationCodeAsync(
            BuildGoogleAuthUrl(clientId),
            GoogleRedirectUri,
            GoogleLoopbackPort,
            ct);

        var token = await ExchangeGoogleCodeAsync(clientId, clientSecret, code, ct);
        var email = await GetGoogleEmailAsync(token.AccessToken, ct);

        return new OAuthSignInResult(
            email, token.AccessToken, token.RefreshToken ?? "",
            DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn), MailProvider.Gmail);
    }

    public async Task<OAuthSignInResult> SignInMicrosoftAsync(CancellationToken ct = default)
    {
        var clientId = await RequireSettingAsync(SettingsKeys.OAuthMicrosoftClientId, "Microsoft OAuth Client ID", ct);

        var app = BuildMicrosoftApp(clientId);

        var scopes = new[]
        {
            "https://outlook.office.com/IMAP.AccessAsUser.All",
            "https://outlook.office.com/SMTP.Send",
            "offline_access",
            "openid",
            "profile",
            "email"
        };

        var result = await BuildMicrosoftInteractiveRequest(app, scopes)
            .ExecuteAsync(ct);

        var email = result.Account?.Username
            ?? throw new InvalidOperationException("Microsoft sign-in did not return an email address.");

        return new OAuthSignInResult(
            email,
            result.AccessToken,
            "", // MSAL manages refresh via its cache; access token is refreshed on demand
            result.ExpiresOn,
            MailProvider.Outlook);
    }

    public async Task<string> GetMicrosoftAccessTokenAsync(CancellationToken ct = default)
    {
        var clientId = await RequireSettingAsync(SettingsKeys.OAuthMicrosoftClientId, "Microsoft OAuth Client ID", ct);
        var email = await _settings.GetAsync(SettingsKeys.MailEmail, ct)
            ?? throw new InvalidOperationException("No Microsoft account saved.");

        var app = BuildMicrosoftApp(clientId);

        var account = (await app.GetAccountsAsync()).FirstOrDefault(a =>
            a.Username.Equals(email, StringComparison.OrdinalIgnoreCase));

        var scopes = new[]
        {
            "https://outlook.office.com/IMAP.AccessAsUser.All",
            "https://outlook.office.com/SMTP.Send"
        };

        if (account is not null)
        {
            try
            {
                var silent = await app.AcquireTokenSilent(scopes, account).ExecuteAsync(ct);
                return silent.AccessToken;
            }
            catch (MsalUiRequiredException)
            {
                var interactive = await BuildMicrosoftInteractiveRequest(app, scopes)
                    .WithAccount(account)
                    .ExecuteAsync(ct);
                return interactive.AccessToken;
            }
        }

        var result = await BuildMicrosoftInteractiveRequest(app, scopes)
            .WithLoginHint(email)
            .ExecuteAsync(ct);
        return result.AccessToken;
    }

    public async Task<string> RefreshGoogleAccessTokenAsync(CancellationToken ct = default)
    {
        var clientId = await RequireSettingAsync(SettingsKeys.OAuthGoogleClientId, "Google OAuth Client ID", ct);
        var clientSecret = await _settings.GetAsync(SettingsKeys.OAuthGoogleClientSecret, ct) ?? "";
        var refreshToken = await _settings.GetAsync(SettingsKeys.MailOAuthRefreshToken, ct)
            ?? throw new InvalidOperationException("Google refresh token missing. Sign in again.");

        using var http = _httpFactory.CreateClient();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        });

        var response = await http.PostAsync("https://oauth2.googleapis.com/token", content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Google token refresh failed: {body}");

        var token = JsonSerializer.Deserialize<GoogleTokenResponse>(body)
            ?? throw new InvalidOperationException("Invalid Google token response.");

        if (string.IsNullOrEmpty(token.AccessToken))
            throw new InvalidOperationException("Google did not return an access token.");

        return token.AccessToken;
    }

    private static string BuildGoogleAuthUrl(string clientId)
    {
        var scope = Uri.EscapeDataString("https://mail.google.com/ openid email profile");
        var redirect = Uri.EscapeDataString(GoogleRedirectUri);
        return "https://accounts.google.com/o/oauth2/v2/auth"
            + $"?client_id={Uri.EscapeDataString(clientId)}"
            + $"&redirect_uri={redirect}"
            + "&response_type=code"
            + $"&scope={scope}"
            + "&access_type=offline"
            + "&prompt=consent";
    }

    private static async Task<string> WaitForAuthorizationCodeAsync(
        string authUrl, string redirectPrefix, int port, CancellationToken ct)
    {
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();

        try
        {
            BrowserLauncher.Open(authUrl);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));

            while (!timeout.Token.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(timeout.Token);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync(timeout.Token);
                if (string.IsNullOrEmpty(requestLine)) continue;

                // Read headers
                while (true)
                {
                    var line = await reader.ReadLineAsync(timeout.Token);
                    if (string.IsNullOrEmpty(line)) break;
                }

                var path = requestLine.Split(' ')[1];
                var code = GetQueryParam(path, "error") is { Length: > 0 } err
                    ? throw new InvalidOperationException(
                        $"Google sign-in error: {err} — {GetQueryParam(path, "error_description")}")
                    : GetQueryParam(path, "code");

                if (string.IsNullOrEmpty(code))
                    throw new InvalidOperationException("Google sign-in did not return an authorization code.");

                var responseHtml = """
                    HTTP/1.1 200 OK
                    Content-Type: text/html; charset=utf-8
                    Connection: close

                    <!DOCTYPE html><html><body style="font-family:sans-serif;text-align:center;padding:40px">
                    <h2>Signed in successfully</h2>
                    <p>You can close this tab and return to EmailAI Assistant.</p>
                    </body></html>
                    """;

                var responseBytes = Encoding.UTF8.GetBytes(responseHtml.Replace("\n", "\r\n"));
                await stream.WriteAsync(responseBytes, timeout.Token);
                return code;
            }

            throw new OperationCanceledException("Google sign-in timed out.");
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task<GoogleTokenResponse> ExchangeGoogleCodeAsync(
        string clientId, string clientSecret, string code, CancellationToken ct)
    {
        using var http = _httpFactory.CreateClient();
        var fields = new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = clientId,
            ["redirect_uri"] = GoogleRedirectUri,
            ["grant_type"] = "authorization_code"
        };
        if (!string.IsNullOrEmpty(clientSecret))
            fields["client_secret"] = clientSecret;

        using var content = new FormUrlEncodedContent(fields);
        var response = await http.PostAsync("https://oauth2.googleapis.com/token", content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Google token exchange failed: {body}");

        return JsonSerializer.Deserialize<GoogleTokenResponse>(body)
            ?? throw new InvalidOperationException("Invalid Google token response.");
    }

    private async Task<string> GetGoogleEmailAsync(string accessToken, CancellationToken ct)
    {
        using var http = _httpFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v2/userinfo");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var info = await response.Content.ReadFromJsonAsync<GoogleUserInfo>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Could not read Google profile.");
        return info.Email ?? throw new InvalidOperationException("Google profile has no email.");
    }

    private async Task<string> RequireSettingAsync(string key, string label, CancellationToken ct)
    {
        var value = await _settings.GetAsync(key, ct);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{label} is not configured.\n\n" +
                "Go to Settings → OAuth Setup and paste your Client ID.\n" +
                "See README for free setup steps (Google Cloud Console / Azure Portal).");
        }
        return value.Trim();
    }

    private AcquireTokenInteractiveParameterBuilder BuildMicrosoftInteractiveRequest(
        IPublicClientApplication app, string[] scopes)
    {
        var request = app.AcquireTokenInteractive(scopes)
            .WithUseEmbeddedWebView(false)
            .WithPrompt(Prompt.SelectAccount)
            .WithSystemWebViewOptions(new SystemWebViewOptions
            {
                HtmlMessageSuccess = "<h2>Signed in — return to EmailAI Assistant</h2>"
            });

        var hwnd = _windowProvider?.GetOwnerWindowHandle() ?? 0;
        if (hwnd != 0)
            request = request.WithParentActivityOrWindow(hwnd);

        return request;
    }

    private static IPublicClientApplication BuildMicrosoftApp(string clientId)
    {
        var app = PublicClientApplicationBuilder
            .Create(clientId)
            .WithAuthority("https://login.microsoftonline.com/common")
            .WithRedirectUri("http://localhost")
            .Build();
        MsalTokenCacheHelper.Bind(app);
        return app;
    }

    private static string? GetQueryParam(string pathAndQuery, string key)
    {
        var q = pathAndQuery.IndexOf('?');
        if (q < 0) return null;
        foreach (var part in pathAndQuery[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && string.Equals(kv[0], key, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(kv[1]);
        }
        return null;
    }

    private sealed class GoogleTokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; } = 3600;
    }

    private sealed class GoogleUserInfo
    {
        [JsonPropertyName("email")] public string? Email { get; set; }
    }
}
