namespace EmailAI.Core.DTOs;

public enum MailAuthMethod
{
    AppPassword,
    OAuth2
}

/// <summary>IMAP/SMTP transport encryption mode.</summary>
public enum MailEncryptionMode
{
    /// <summary>Port 993/465 → SSL/TLS; port 143/587 → STARTTLS.</summary>
    Auto,
    /// <summary>TLS/SSL immediately on connect (typical for IMAP 993).</summary>
    SslTls,
    /// <summary>Plain connect then upgrade with STARTTLS (typical for SMTP 587).</summary>
    StartTls,
    /// <summary>No encryption (not recommended).</summary>
    None
}

public static class MailEncryptionLabels
{
    public static readonly string[] Options =
    [
        "Automatic (recommended)",
        "SSL/TLS (port 993 / 465)",
        "STARTTLS (port 143 / 587)",
        "None (not secure)"
    ];

    public static MailEncryptionMode Parse(string label) => label switch
    {
        var s when s.StartsWith("SSL", StringComparison.OrdinalIgnoreCase) => MailEncryptionMode.SslTls,
        var s when s.StartsWith("START", StringComparison.OrdinalIgnoreCase) => MailEncryptionMode.StartTls,
        var s when s.StartsWith("None", StringComparison.OrdinalIgnoreCase) => MailEncryptionMode.None,
        _ => MailEncryptionMode.Auto
    };

    public static string Label(MailEncryptionMode mode) => mode switch
    {
        MailEncryptionMode.SslTls   => Options[1],
        MailEncryptionMode.StartTls => Options[2],
        MailEncryptionMode.None     => Options[3],
        _                           => Options[0]
    };
}
