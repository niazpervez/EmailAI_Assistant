using CoreMailService = EmailAI.Core.Interfaces.IMailService;
using EmailAI.Core;
using EmailAI.Core.DTOs;
using EmailAI.Core.Entities;
using EmailAI.Core.Interfaces;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using Microsoft.Extensions.Logging;
using MimeKit;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EmailAI.Infrastructure.Services.Mail;

/// <summary>
/// Provider-agnostic email access via IMAP (read/sync) and SMTP (send/reply).
/// Works with Gmail, Yahoo, Outlook/Hotmail, and any standard IMAP/SMTP server.
/// </summary>
public sealed class ImapMailService : CoreMailService
{
    private readonly ISettingsRepository _settings;
    private readonly MailOAuthService _oauth;
    private readonly ILogger<ImapMailService> _logger;

    private MailAccountConfig? _cachedAccount;

    public ImapMailService(
        ISettingsRepository settings,
        MailOAuthService oauth,
        ILogger<ImapMailService> logger)
    {
        _settings = settings;
        _oauth = oauth;
        _logger = logger;
    }

    public async Task SaveAccountAsync(MailAccountConfig config, CancellationToken ct = default)
    {
        var resolved = ResolveConfig(config);
        await PersistAccountAsync(resolved, ct);
        _cachedAccount = resolved;
    }

    public async Task ConnectAccountAsync(MailAccountConfig config, CancellationToken ct = default)
    {
        var resolved = ResolveConfig(config);

        using var client = await ConnectImapAsync(resolved, ct);
        await client.DisconnectAsync(true, ct);

        await PersistAccountAsync(resolved, ct);
        _cachedAccount = resolved;
        _logger.LogInformation("Connected to {Email} via IMAP", resolved.Email);
    }

    public async Task ConnectOAuthAsync(MailProvider provider, CancellationToken ct = default)
    {
        var oauthResult = provider switch
        {
            MailProvider.Gmail   => await _oauth.SignInGoogleAsync(ct),
            MailProvider.Outlook => await _oauth.SignInMicrosoftAsync(ct),
            _ => throw new NotSupportedException("OAuth is only available for Gmail and Outlook/Hotmail.")
        };

        var config = MailProviderPresets.ApplyPreset(provider, new MailAccountConfig
        {
            Email = oauthResult.Email,
            Provider = provider,
            AuthMethod = MailAuthMethod.OAuth2,
            OAuthRefreshToken = oauthResult.RefreshToken,
            Password = ""
        });

        using var client = await ConnectImapAsync(config with { }, oauthResult.AccessToken, ct);
        await client.DisconnectAsync(true, ct);

        await PersistAccountAsync(config, ct);
        if (!string.IsNullOrEmpty(oauthResult.RefreshToken))
            await _settings.SetAsync(SettingsKeys.MailOAuthRefreshToken, oauthResult.RefreshToken, encrypt: true, ct: ct);

        _cachedAccount = config;
        _logger.LogInformation("Connected to {Email} via OAuth", oauthResult.Email);
    }

    private async Task PersistAccountAsync(MailAccountConfig resolved, CancellationToken ct)
    {
        await _settings.SetAsync(SettingsKeys.MailEmail, resolved.Email, ct: ct);
        await _settings.SetAsync(SettingsKeys.MailPassword, resolved.Password, encrypt: true, ct: ct);
        await _settings.SetAsync(SettingsKeys.MailProvider, resolved.Provider.ToString(), ct: ct);
        await _settings.SetAsync(SettingsKeys.MailImapHost, resolved.ImapHost, ct: ct);
        await _settings.SetAsync(SettingsKeys.MailImapPort, resolved.ImapPort.ToString(), ct: ct);
        await _settings.SetAsync(SettingsKeys.MailSmtpHost, resolved.SmtpHost, ct: ct);
        await _settings.SetAsync(SettingsKeys.MailSmtpPort, resolved.SmtpPort.ToString(), ct: ct);
        await _settings.SetAsync(SettingsKeys.MailUseSsl, resolved.UseSsl ? "1" : "0", ct: ct);
        await _settings.SetAsync(SettingsKeys.MailAuthMethod, resolved.AuthMethod.ToString(), ct: ct);
        await _settings.SetAsync(SettingsKeys.MailImapEncryption, resolved.ImapEncryption.ToString(), ct: ct);
        await _settings.SetAsync(SettingsKeys.MailSmtpEncryption, resolved.SmtpEncryption.ToString(), ct: ct);
        if (resolved.AuthMethod == MailAuthMethod.OAuth2 && !string.IsNullOrEmpty(resolved.OAuthRefreshToken))
            await _settings.SetAsync(SettingsKeys.MailOAuthRefreshToken, resolved.OAuthRefreshToken, encrypt: true, ct: ct);
        await _settings.SetAsync(SettingsKeys.UserEmail, resolved.Email, ct: ct);
        await _settings.SetAsync(SettingsKeys.UserDisplayName, resolved.DisplayName, ct: ct);
    }

    private static MailAccountConfig ResolveConfig(MailAccountConfig config)
    {
        var trimmed = config with
        {
            Email = config.Email.Trim(),
            Password = config.Password,
            ImapHost = config.ImapHost.Trim(),
            SmtpHost = config.SmtpHost.Trim(),
            ImapEncryption = config.ImapEncryption,
            SmtpEncryption = config.SmtpEncryption
        };

        if (trimmed.Provider == MailProvider.Custom)
            return trimmed;

        var preset = MailProviderPresets.ApplyPreset(trimmed.Provider, trimmed);
        return preset with
        {
            AuthMethod = trimmed.AuthMethod,
            Password = trimmed.Password,
            OAuthRefreshToken = trimmed.OAuthRefreshToken,
            ImapEncryption = trimmed.ImapEncryption,
            SmtpEncryption = trimmed.SmtpEncryption
        };
    }

    public async Task<MailAccountConfig?> GetSavedAccountAsync(CancellationToken ct = default)
    {
        var email = await _settings.GetAsync(SettingsKeys.MailEmail, ct);
        if (string.IsNullOrWhiteSpace(email)) return null;

        var password = await _settings.GetAsync(SettingsKeys.MailPassword, ct) ?? "";
        var providerStr = await _settings.GetAsync(SettingsKeys.MailProvider, ct) ?? "Gmail";
        Enum.TryParse<MailProvider>(providerStr, out var provider);
        var authStr = await _settings.GetAsync(SettingsKeys.MailAuthMethod, ct) ?? "AppPassword";
        Enum.TryParse<MailAuthMethod>(authStr, out var authMethod);
        Enum.TryParse<MailEncryptionMode>(await _settings.GetAsync(SettingsKeys.MailImapEncryption, ct), out var imapEnc);
        Enum.TryParse<MailEncryptionMode>(await _settings.GetAsync(SettingsKeys.MailSmtpEncryption, ct), out var smtpEnc);

        return new MailAccountConfig
        {
            Email = email,
            Password = password,
            Provider = provider,
            AuthMethod = authMethod,
            OAuthRefreshToken = await _settings.GetAsync(SettingsKeys.MailOAuthRefreshToken, ct),
            ImapHost = await _settings.GetAsync(SettingsKeys.MailImapHost, ct) ?? "",
            ImapPort = int.TryParse(await _settings.GetAsync(SettingsKeys.MailImapPort, ct), out var imapPort) ? imapPort : 993,
            SmtpHost = await _settings.GetAsync(SettingsKeys.MailSmtpHost, ct) ?? "",
            SmtpPort = int.TryParse(await _settings.GetAsync(SettingsKeys.MailSmtpPort, ct), out var smtpPort) ? smtpPort : 587,
            UseSsl = (await _settings.GetAsync(SettingsKeys.MailUseSsl, ct)) != "0",
            ImapEncryption = imapEnc,
            SmtpEncryption = smtpEnc
        };
    }

    public async Task<bool> IsAuthenticatedAsync(CancellationToken ct = default)
    {
        var account = await GetAccountAsync(ct);
        if (account is null || !account.HasCredentials) return false;

        try
        {
            using var client = await ConnectImapAsync(account, ct);
            await client.DisconnectAsync(true, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "IMAP connection check failed");
            return false;
        }
    }

    public async Task SignInAsync(CancellationToken ct = default)
    {
        var account = await GetAccountAsync(ct)
            ?? throw new InvalidOperationException("No email account configured. Enter your email and password first.");

        using var client = await ConnectImapAsync(account, ct);
        await client.DisconnectAsync(true, ct);
        _logger.LogInformation("Connected to {Email} via IMAP", account.Email);
    }

    public async Task SignOutAsync(CancellationToken ct = default)
    {
        _cachedAccount = null;
        await _settings.DeleteAsync(SettingsKeys.MailPassword, ct);
        await _settings.DeleteAsync(SettingsKeys.MailOAuthRefreshToken, ct);
        await _settings.DeleteAsync(SettingsKeys.MailAuthMethod, ct);
        MsalTokenCacheHelper.Clear();
    }

    public async Task<string> GetUserDisplayNameAsync(CancellationToken ct = default)
    {
        var name = await _settings.GetAsync(SettingsKeys.UserDisplayName, ct);
        if (!string.IsNullOrWhiteSpace(name)) return name;
        var account = await GetAccountAsync(ct);
        return account?.DisplayName ?? "Unknown";
    }

    public async Task<string> GetUserEmailAsync(CancellationToken ct = default)
    {
        var account = await GetAccountAsync(ct);
        return account?.Email ?? "Unknown";
    }

    public async Task<IEnumerable<Core.Interfaces.MailFolder>> GetMailFoldersAsync(CancellationToken ct = default)
    {
        var account = await RequireAccountAsync(ct);
        using var client = await ConnectImapAsync(account, ct);

        var results = new List<Core.Interfaces.MailFolder>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var walked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Inbox first — Outlook may omit it from LIST or report an empty FullName.
        TryAddMailFolder(client.Inbox, results, seen);

        foreach (var ns in client.PersonalNamespaces)
        {
            try
            {
                var root = client.GetFolder(ns);
                await WalkFolderTreeAsync(root, results, seen, walked, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not walk personal namespace {Path}", ns.Path);
            }
        }

        // Some providers expose user folders under OtherNamespaces.
        foreach (var ns in client.OtherNamespaces)
        {
            try
            {
                var root = client.GetFolder(ns);
                await WalkFolderTreeAsync(root, results, seen, walked, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not walk other namespace {Path}", ns.Path);
            }
        }

        // Outlook/Hotmail: custom folders are siblings of Inbox under the personal namespace.
        // HasChildren is often false on the namespace root, so always list its children explicitly.
        if (client.PersonalNamespaces.Count > 0)
        {
            try
            {
                var personal = client.GetFolder(client.PersonalNamespaces[0]);
                foreach (var sub in await personal.GetSubfoldersAsync(StatusItems.None, false, ct))
                    await WalkFolderTreeAsync(sub, results, seen, walked, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Top-level personal namespace folder listing failed");
            }
        }

        if (results.Count <= 1)
        {
            try
            {
                var parent = client.Inbox.ParentFolder;
                if (parent is not null)
                    await WalkFolderTreeAsync(parent, results, seen, walked, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Inbox parent folder walk failed");
            }
        }

        await client.DisconnectAsync(true, ct);
        _logger.LogInformation(
            "Listed {Count} IMAP folders for {Email}: {Names}",
            results.Count, account.Email,
            string.Join(", ", results.Select(f => f.DisplayName)));
        return results.OrderBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<SyncResult> SyncFolderAsync(
        string folderId, string folderName, string? deltaLink, DateTime? sinceUtc = null, CancellationToken ct = default)
    {
        var account = await RequireAccountAsync(ct);
        var maxEmails = await GetMaxEmailsPerSyncAsync(ct);

        using var client = await ConnectImapAsync(account, ct);
        var folder = await ResolveFolderAsync(client, folderId, folderName, ct);
        await OpenFolderAsync(folder, ct);

        uint lastUid = uint.TryParse(deltaLink, out var parsed) ? parsed : 0;
        IList<UniqueId> uids;

        if (lastUid == 0)
        {
            var query = sinceUtc.HasValue
                ? SearchQuery.DeliveredAfter(sinceUtc.Value)
                : SearchQuery.All;

            var all = folder.Search(query);
            uids = all.Count <= maxEmails
                ? all
                : all.Skip(all.Count - maxEmails).ToList();

            if (sinceUtc.HasValue)
                _logger.LogInformation(
                    "Initial sync for {Folder}: {Count} messages since {Since:u}",
                    folderName, uids.Count, sinceUtc.Value);
        }
        else
        {
            uids = folder.Search(SearchQuery.Uids(
                new UniqueIdRange(new UniqueId(lastUid + 1), UniqueId.MaxValue)));
        }

        var emails = new List<Email>();
        uint highestUid = lastUid;

        if (uids.Count > 0)
        {
            var summaries = folder.Fetch(uids, MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope
                | MessageSummaryItems.Flags | MessageSummaryItems.BodyStructure);

            foreach (var summary in summaries)
            {
                if (ct.IsCancellationRequested) break;

                var msg = folder.GetMessage(summary.UniqueId);
                var resolvedId = ResolveFolderId(folder);
                emails.Add(MapMessage(msg, summary, folder.FullName, resolvedId));
                if (summary.UniqueId.Id > highestUid)
                    highestUid = summary.UniqueId.Id;
            }
        }

        await client.DisconnectAsync(true, ct);
        return new SyncResult(emails, highestUid.ToString(), null, false);
    }

    public async Task<IEnumerable<MailAttachmentInfo>> GetAttachmentsAsync(
        string messageId, CancellationToken ct = default)
    {
        var account = await RequireAccountAsync(ct);
        var (folderId, uid) = ParseEmailId(messageId);

        using var client = await ConnectImapAsync(account, ct);
        var folder = await ResolveFolderAsync(client, folderId, null, ct);
        await OpenFolderAsync(folder, ct);

        var msg = folder.GetMessage(uid);
        var parts = MimeAttachmentHelper.ListParts(msg.Body);
        var results = new List<MailAttachmentInfo>();

        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            var size = part.ContentDisposition?.Size ?? 0;
            results.Add(new MailAttachmentInfo(
                MimeAttachmentHelper.ResolveAttachmentId(part, i),
                part.FileName ?? $"attachment_{i + 1}",
                part.ContentType?.MimeType ?? "application/octet-stream",
                size));
        }

        await client.DisconnectAsync(true, ct);
        return results;
    }

    public async Task<byte[]?> GetAttachmentContentAsync(
        string messageId, string attachmentId, CancellationToken ct = default)
    {
        var account = await RequireAccountAsync(ct);
        var (folderId, uid) = ParseEmailId(messageId);

        using var client = await ConnectImapAsync(account, ct);
        var folder = await ResolveFolderAsync(client, folderId, null, ct);
        await OpenFolderAsync(folder, ct);

        var msg = folder.GetMessage(uid);
        var parts = MimeAttachmentHelper.ListParts(msg.Body);

        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            if (!MimeAttachmentHelper.MatchesAttachmentId(part, i, attachmentId)) continue;

            using var ms = new MemoryStream();
            await part.Content.DecodeToAsync(ms, ct);
            await client.DisconnectAsync(true, ct);
            return ms.ToArray();
        }

        await client.DisconnectAsync(true, ct);
        return null;
    }

    public async Task SendEmailAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        var account = await RequireAccountAsync(ct);
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(account.Email));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = body };

        await SendViaSmtpAsync(account, message, ct);
    }

    public async Task<bool> ReplyToEmailAsync(string messageId, string body, CancellationToken ct = default)
    {
        try
        {
            var account = await RequireAccountAsync(ct);
            var (folderId, uid) = ParseEmailId(messageId);

            using var client = await ConnectImapAsync(account, ct);
            var folder = await ResolveFolderAsync(client, folderId, null, ct);
            await OpenFolderAsync(folder, ct);
            var original = folder.GetMessage(uid);
            await client.DisconnectAsync(true, ct);

            var reply = new MimeMessage();
            reply.From.Add(MailboxAddress.Parse(account.Email));
            reply.To.Add(original.From.Mailboxes.First());
            reply.Subject = original.Subject?.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) == true
                ? original.Subject
                : $"Re: {original.Subject}";
            reply.InReplyTo = original.MessageId;
            if (!string.IsNullOrEmpty(original.MessageId))
                reply.References.Add(original.MessageId);
            reply.Body = new TextPart("plain") { Text = body };

            await SendViaSmtpAsync(account, reply, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reply failed for {MessageId}", messageId);
            return false;
        }
    }

    private async Task<MailAccountConfig?> GetAccountAsync(CancellationToken ct)
    {
        if (_cachedAccount is not null && _cachedAccount.HasCredentials)
            return _cachedAccount;
        return await GetSavedAccountAsync(ct);
    }

    private async Task<MailAccountConfig> RequireAccountAsync(CancellationToken ct)
    {
        var account = await GetAccountAsync(ct);
        if (account is null || string.IsNullOrWhiteSpace(account.Email) || !account.HasCredentials)
            throw new InvalidOperationException("Email account not configured.");
        _cachedAccount = account;
        return account;
    }

    private async Task<string> GetOAuthAccessTokenAsync(MailAccountConfig account, CancellationToken ct)
    {
        return account.Provider switch
        {
            MailProvider.Gmail   => await _oauth.RefreshGoogleAccessTokenAsync(ct),
            MailProvider.Outlook => await _oauth.GetMicrosoftAccessTokenAsync(ct),
            _ => throw new NotSupportedException($"OAuth not supported for {account.Provider}.")
        };
    }

    private async Task<ImapClient> ConnectImapAsync(MailAccountConfig account, CancellationToken ct)
        => await ConnectImapAsync(account, null, ct);

    private async Task<ImapClient> ConnectImapAsync(
        MailAccountConfig account, string? accessToken, CancellationToken ct)
    {
        var client = new ImapClient();
        var socketOptions = MailSocketHelper.GetImapOptions(account);

        await client.ConnectAsync(account.ImapHost, account.ImapPort, socketOptions, ct);

        if (account.AuthMethod == MailAuthMethod.OAuth2)
        {
            var token = accessToken ?? await GetOAuthAccessTokenAsync(account, ct);
            await MailAuthHelper.AuthenticateOAuthAsync(client, account.Email, token, ct);
        }
        else
            await MailAuthHelper.AuthenticateAsync(client, account, ct);

        return client;
    }

    private async Task SendViaSmtpAsync(MailAccountConfig account, MimeMessage message, CancellationToken ct)
    {
        using var client = new SmtpClient();
        var socketOptions = MailSocketHelper.GetSmtpOptions(account);

        await client.ConnectAsync(account.SmtpHost, account.SmtpPort, socketOptions, ct);

        if (account.AuthMethod == MailAuthMethod.OAuth2)
        {
            var token = await GetOAuthAccessTokenAsync(account, ct);
            await MailAuthHelper.AuthenticateOAuthAsync(client, account.Email, token, ct);
        }
        else
            await MailAuthHelper.AuthenticateAsync(client, account, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }

    private static async Task WalkFolderTreeAsync(
        IMailFolder folder,
        List<Core.Interfaces.MailFolder> results,
        HashSet<string> seen,
        HashSet<string> walked,
        CancellationToken ct)
    {
        if (folder.Attributes.HasFlag(FolderAttributes.NonExistent)) return;

        var walkKey = !string.IsNullOrWhiteSpace(folder.FullName)
            ? folder.FullName
            : folder.Name;
        if (!string.IsNullOrWhiteSpace(walkKey) && !walked.Add(walkKey)) return;

        TryAddMailFolder(folder, results, seen);

        IList<IMailFolder> subs;
        try
        {
            // Always call LIST — do not rely on HasChildren (Outlook often leaves it false).
            subs = await folder.GetSubfoldersAsync(StatusItems.None, false, ct);
        }
        catch
        {
            return;
        }

        foreach (var sub in subs)
            await WalkFolderTreeAsync(sub, results, seen, walked, ct);
    }

    private static void TryAddMailFolder(
        IMailFolder folder, List<Core.Interfaces.MailFolder> results, HashSet<string> seen)
    {
        if (folder.Attributes.HasFlag(FolderAttributes.NonExistent)) return;
        if (folder.Attributes.HasFlag(FolderAttributes.NoSelect)) return;

        var folderId = ResolveFolderId(folder);
        if (string.IsNullOrWhiteSpace(folderId)) return;
        if (!seen.Add(folderId)) return;

        var display = folder.Attributes.HasFlag(FolderAttributes.Inbox)
            ? "Inbox"
            : MailProviderPresets.NormalizeFolderDisplayName(
                string.IsNullOrWhiteSpace(folder.Name) ? folder.FullName : folder.Name);

        results.Add(new Core.Interfaces.MailFolder(
            folderId,
            string.IsNullOrWhiteSpace(folder.Name) ? display : folder.Name,
            display,
            (int)folder.Count,
            folder.Unread));
    }

    /// <summary>
    /// Outlook/Exchange sometimes reports Inbox with an empty FullName; use a stable id instead.
    /// </summary>
    private static string ResolveFolderId(IMailFolder folder)
    {
        if (!string.IsNullOrWhiteSpace(folder.FullName))
            return folder.FullName;

        if (folder.Attributes.HasFlag(FolderAttributes.Inbox))
            return "INBOX";

        return folder.Name;
    }

    private static async Task<IMailFolder> ResolveFolderAsync(
        ImapClient client, string folderId, string? folderName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(folderId)
            || folderId.Equals("INBOX", StringComparison.OrdinalIgnoreCase)
            || folderName?.Equals("Inbox", StringComparison.OrdinalIgnoreCase) == true)
            return client.Inbox;

        return await client.GetFolderAsync(folderId, ct);
    }

    private static async Task OpenFolderAsync(IMailFolder folder, CancellationToken ct)
    {
        try
        {
            await folder.OpenAsync(FolderAccess.ReadOnly, ct);
        }
        catch (ImapCommandException ex) when (ex.Message.Contains("BAD", StringComparison.OrdinalIgnoreCase))
        {
            // Some Exchange/Outlook mailboxes reject EXAMINE but accept SELECT.
            await folder.OpenAsync(FolderAccess.ReadWrite, ct);
        }
    }

    private static Email MapMessage(MimeMessage msg, IMessageSummary summary, string folderName, string folderId)
    {
        var bodyText = msg.TextBody ?? StripHtml(msg.HtmlBody ?? "");
        if (bodyText.Length > 8000) bodyText = bodyText[..8000];

        var from = msg.From.Mailboxes.FirstOrDefault();
        var recipients = JsonSerializer.Serialize(msg.To.Mailboxes.Select(m => m.Address).ToList());

        return new Email
        {
            EmailId = $"{folderId}|{summary.UniqueId.Id}",
            ConversationId = msg.InReplyTo ?? msg.MessageId ?? "",
            MessageId = msg.MessageId ?? "",
            Subject = msg.Subject ?? "(No Subject)",
            Sender = from?.Address ?? "",
            SenderName = from?.Name ?? from?.Address ?? "",
            Recipients = recipients,
            ReceivedDate = msg.Date.UtcDateTime,
            BodyText = bodyText,
            BodyHtml = msg.HtmlBody ?? "",
            FolderName = MailProviderPresets.NormalizeFolderDisplayName(folderName),
            FolderId = folderId,
            HasAttachments = msg.Attachments.Any(),
            IsRead = summary.Flags?.HasFlag(MessageFlags.Seen) ?? false,
            IsImportant = msg.Importance == MessageImportance.High,
            Importance = msg.Importance.ToString().ToLowerInvariant(),
            SyncedAt = DateTime.UtcNow,
            ChangeKey = summary.UniqueId.Id.ToString()
        };
    }

    private static (string folderId, UniqueId uid) ParseEmailId(string emailId)
    {
        var sep = emailId.LastIndexOf('|');
        if (sep <= 0) throw new ArgumentException($"Invalid email ID: {emailId}");
        var folderId = emailId[..sep];
        if (!uint.TryParse(emailId[(sep + 1)..], out var uidVal))
            throw new ArgumentException($"Invalid email UID in: {emailId}");
        return (folderId, new UniqueId(uidVal));
    }

    private async Task<int> GetMaxEmailsPerSyncAsync(CancellationToken ct)
    {
        var val = await _settings.GetAsync(SettingsKeys.MaxEmailsPerSync, ct);
        return int.TryParse(val, out var n) && n > 0 ? n : 100;
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var text = Regex.Replace(html, @"<[^>]+>", " ");
        return Regex.Replace(text, @"\s{2,}", " ").Trim();
    }
}
