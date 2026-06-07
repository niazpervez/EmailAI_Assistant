namespace EmailAI.Core;

using EmailAI.Core.DTOs;

public static class MailProviderDetector
{
    private static readonly string[] MicrosoftDomains =
    [
        "@hotmail.com", "@outlook.com", "@live.com", "@msn.com",
        "@hotmail.co.uk", "@outlook.co.uk", "@live.co.uk"
    ];

    public static bool IsMicrosoftEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        var lower = email.Trim().ToLowerInvariant();
        return MicrosoftDomains.Any(d => lower.EndsWith(d));
    }

    public static bool IsGoogleEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        var lower = email.Trim().ToLowerInvariant();
        return lower.EndsWith("@gmail.com") || lower.EndsWith("@googlemail.com");
    }

    public static bool IsMicrosoftServer(string? imapHost, string? smtpHost = null)
    {
        static bool Match(string? host) =>
            !string.IsNullOrWhiteSpace(host) &&
            (host.Contains("outlook", StringComparison.OrdinalIgnoreCase) ||
             host.Contains("office365", StringComparison.OrdinalIgnoreCase) ||
             host.Contains("live.com", StringComparison.OrdinalIgnoreCase));

        return Match(imapHost) || Match(smtpHost);
    }

    public static bool LooksLikeMicrosoft(string? email, string? imapHost, string? smtpHost = null)
        => IsMicrosoftEmail(email) || IsMicrosoftServer(imapHost, smtpHost);

    public static bool LooksLikeGoogle(string? email, string? imapHost = null)
        => IsGoogleEmail(email) ||
           (!string.IsNullOrWhiteSpace(imapHost) &&
            imapHost.Contains("gmail", StringComparison.OrdinalIgnoreCase));

    public static MailProvider InferProvider(string? email, string? imapHost, string? smtpHost = null)
    {
        if (LooksLikeGoogle(email, imapHost)) return MailProvider.Gmail;
        if (LooksLikeMicrosoft(email, imapHost, smtpHost)) return MailProvider.Outlook;
        if (!string.IsNullOrWhiteSpace(imapHost) &&
            imapHost.Contains("yahoo", StringComparison.OrdinalIgnoreCase))
            return MailProvider.Yahoo;
        return MailProvider.Custom;
    }
}
