namespace EmailAI.Core.DTOs;

public enum MailProvider
{
    Gmail,
    Yahoo,
    Outlook,
    Custom
}

public record MailAccountConfig
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public MailProvider Provider { get; init; } = MailProvider.Gmail;
    public MailAuthMethod AuthMethod { get; init; } = MailAuthMethod.AppPassword;
    public string ImapHost { get; init; } = string.Empty;
    public int ImapPort { get; init; } = 993;
    public string SmtpHost { get; init; } = string.Empty;
    public int SmtpPort { get; init; } = 587;
    public bool UseSsl { get; init; } = true;
    public MailEncryptionMode ImapEncryption { get; init; } = MailEncryptionMode.Auto;
    public MailEncryptionMode SmtpEncryption { get; init; } = MailEncryptionMode.Auto;
    public string? OAuthRefreshToken { get; init; }

    public bool HasCredentials =>
        AuthMethod == MailAuthMethod.OAuth2
            ? Provider == MailProvider.Gmail
                ? !string.IsNullOrEmpty(OAuthRefreshToken)
                : !string.IsNullOrWhiteSpace(Email)
            : !string.IsNullOrWhiteSpace(Password);

    public string DisplayName =>
        string.IsNullOrWhiteSpace(Email) ? "Unknown"
        : Email.Split('@')[0].Replace('.', ' ');
}
