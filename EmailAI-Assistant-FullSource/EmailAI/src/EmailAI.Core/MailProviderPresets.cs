using EmailAI.Core.DTOs;

namespace EmailAI.Core;

public static class MailProviderPresets
{
    public static MailAccountConfig ApplyPreset(MailProvider provider, MailAccountConfig config)
    {
        return provider switch
        {
            MailProvider.Gmail => config with
            {
                Provider = MailProvider.Gmail,
                ImapHost = "imap.gmail.com",
                ImapPort = 993,
                SmtpHost = "smtp.gmail.com",
                SmtpPort = 587,
                UseSsl = true
            },
            MailProvider.Yahoo => config with
            {
                Provider = MailProvider.Yahoo,
                ImapHost = "imap.mail.yahoo.com",
                ImapPort = 993,
                SmtpHost = "smtp.mail.yahoo.com",
                SmtpPort = 587,
                UseSsl = true
            },
            MailProvider.Outlook => config with
            {
                Provider = MailProvider.Outlook,
                ImapHost = "outlook.office365.com",
                ImapPort = 993,
                SmtpHost = "smtp-mail.outlook.com",
                SmtpPort = 587,
                UseSsl = true
            },
            _ => config
        };
    }

    public static string ProviderLabel(MailProvider provider) => provider switch
    {
        MailProvider.Gmail   => "Gmail",
        MailProvider.Yahoo   => "Yahoo Mail",
        MailProvider.Outlook => "Outlook / Hotmail / Live",
        MailProvider.Custom  => "Custom (IMAP/SMTP)",
        _ => provider.ToString()
    };

    /// <summary>Normalizes configured folder names to match IMAP display names.</summary>
    public static HashSet<string> NormalizeFolderSelection(IEnumerable<string> folderNames)
        => folderNames
            .Select(NormalizeFolderDisplayName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Maps IMAP folder full names to user-friendly labels used in settings.</summary>
    public static string NormalizeFolderDisplayName(string fullName)
    {
        var name = fullName;
        var slash = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
        if (slash >= 0 && slash < name.Length - 1)
            name = name[(slash + 1)..];

        var upper = name.ToUpperInvariant();
        if (upper == "INBOX") return "Inbox";
        if (upper is "SENT" or "SENT ITEMS" or "SENT MESSAGES") return "Sent";
        if (upper is "TRASH" or "DELETED" or "DELETED ITEMS") return "Trash";
        if (upper is "ARCHIVE" or "ARCHIVE ITEMS") return "Archive";
        if (upper is "DRAFTS" or "DRAFT") return "Drafts";
        if (upper is "JUNK" or "JUNK EMAIL" or "SPAM") return "Junk";
        return name;
    }
}
