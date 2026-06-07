namespace EmailAI.Core.DTOs;

public enum ExternalMailClient
{
    Outlook,
    Thunderbird
}

public record InstalledMailClientsInfo(
    bool OutlookInstalled,
    bool ThunderbirdInstalled,
    string? OutlookVersion,
    string? ThunderbirdPath);

/// <summary>Account settings discovered from Outlook or Thunderbird (no password).</summary>
public record DiscoveredMailAccount(
    ExternalMailClient Source,
    string SourceAccountId,
    string Email,
    string DisplayName,
    string? ImapHost,
    int ImapPort,
    string? SmtpHost,
    int SmtpPort,
    MailEncryptionMode ImapEncryption,
    MailEncryptionMode SmtpEncryption,
    MailProvider Provider,
    bool IsOAuthCapable,
    string Summary);
