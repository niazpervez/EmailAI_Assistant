using EmailAI.Core;
using EmailAI.Core.DTOs;
using MailKit;
using MailKit.Security;
using System.Net;
using System.Text;

namespace EmailAI.Infrastructure.Services.Mail;

internal static class MailAuthHelper
{
    public static Task AuthenticateOAuthAsync(
        IMailService client, string email, string accessToken, CancellationToken ct)
    {
        var oauth2 = new SaslMechanismOAuth2(email.Trim(), accessToken);
        return client.AuthenticateAsync(oauth2, ct);
    }

    public static async Task AuthenticateAsync(
        IMailService client, MailAccountConfig account, CancellationToken ct)
    {
        if (account.AuthMethod == MailAuthMethod.OAuth2)
            throw new InvalidOperationException("OAuth account requires an access token. Use AuthenticateOAuthAsync.");

        client.AuthenticationMechanisms.Remove("XOAUTH2");
        client.AuthenticationMechanisms.Remove("GSSAPI");
        client.AuthenticationMechanisms.Remove("NTLM");

        var user = account.Email.Trim();
        var password = account.Password;

        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrEmpty(password))
            throw new AuthenticationException("Email address and password are required.");

        try
        {
            await client.AuthenticateAsync(new NetworkCredential(user, password), ct);
        }
        catch (AuthenticationException ex)
        {
            throw new AuthenticationException(BuildHelpMessage(account, ex), ex);
        }
    }

    public static string BuildHelpMessage(MailAccountConfig account, Exception? inner = null)
    {
        var sb = new StringBuilder();
        sb.Append("Authentication failed.");

        if (inner is not null && !string.IsNullOrWhiteSpace(inner.Message))
            sb.Append(' ').Append(inner.Message.TrimEnd('.'));

        sb.AppendLine().AppendLine();
        sb.AppendLine(GetProviderSteps(ResolveHelpProvider(account)));

        if (ResolveHelpProvider(account) == MailProvider.Outlook)
        {
            sb.AppendLine();
            sb.AppendLine("Your IMAP/SMTP server settings can be correct and login still fails — Microsoft often blocks password login entirely.");
            sb.Append("Server: IMAP ").Append(account.ImapHost).Append(':').Append(account.ImapPort);
            sb.Append(", SMTP ").Append(account.SmtpHost).Append(':').Append(account.SmtpPort).Append('.');
        }

        return sb.ToString().TrimEnd();
    }

    private static MailProvider ResolveHelpProvider(MailAccountConfig account)
    {
        if (account.Provider != MailProvider.Custom)
            return account.Provider;
        if (MailProviderDetector.LooksLikeMicrosoft(account.Email, account.ImapHost, account.SmtpHost))
            return MailProvider.Outlook;
        if (MailProviderDetector.LooksLikeGoogle(account.Email, account.ImapHost))
            return MailProvider.Gmail;
        return account.Provider;
    }

    private static string GetProviderSteps(MailProvider provider) => provider switch
    {
        MailProvider.Gmail =>
            """
            Gmail — choose ONE method:
            • RECOMMENDED: Click "Sign in with Google" (browser opens; approve there — no message appears inside this app)
            • OR use an App Password: https://myaccount.google.com/apppasswords (16 chars, not your normal password)
            • Enable IMAP in Gmail settings → Forwarding and POP/IMAP
            """,

        MailProvider.Outlook =>
            """
            Microsoft (Outlook/Hotmail) — choose ONE method:
            • RECOMMENDED: Click "Sign in with Microsoft" (browser opens; approve there — no message appears inside this app)
            • OR use an App Password: https://account.microsoft.com/security
            • Enable IMAP: outlook.live.com → Settings → Mail → Sync email
            """,

        MailProvider.Yahoo =>
            """
            Yahoo checklist:
            • Generate an App Password at https://login.yahoo.com/account/security
            • Use that app password here — not your normal Yahoo password
            """,

        _ =>
            """
            Checklist:
            • Confirm IMAP is enabled with your email provider
            • Use an app-specific password if two-factor authentication is enabled
            • Verify the IMAP host, port (993), SMTP host, and port (587)
            """
    };
}
