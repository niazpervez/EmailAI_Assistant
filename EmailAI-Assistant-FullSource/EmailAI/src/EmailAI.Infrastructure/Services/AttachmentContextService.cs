using EmailAI.Core;
using EmailAI.Core.Entities;
using EmailAI.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace EmailAI.Infrastructure.Services;

public sealed class AttachmentContextService : IAttachmentContextService
{
    private readonly IMailService _mail;
    private readonly IEmailRepository _emails;
    private readonly IAttachmentRepository _attachments;
    private readonly IAttachmentExtractor _extractor;
    private readonly ILogger<AttachmentContextService> _logger;

    public AttachmentContextService(
        IMailService mail,
        IEmailRepository emails,
        IAttachmentRepository attachments,
        IAttachmentExtractor extractor,
        ILogger<AttachmentContextService> logger)
    {
        _mail = mail;
        _emails = emails;
        _attachments = attachments;
        _extractor = extractor;
        _logger = logger;
    }

    public async Task ProcessBatchAsync(IEnumerable<Email> emails, CancellationToken ct = default)
    {
        foreach (var email in emails.Where(e => e.HasAttachments))
        {
            if (ct.IsCancellationRequested) break;
            await ProcessEmailAsync(email.EmailId, ct);
        }
    }

    public async Task EnsureProcessedAsync(string emailId, CancellationToken ct = default)
        => await ProcessEmailAsync(emailId, ct);

    public async Task<string> GetContextForEmailAsync(string emailId, CancellationToken ct = default)
    {
        await ProcessEmailAsync(emailId, ct);
        return await _attachments.GetCombinedExtractedTextAsync(
            emailId, AppConstants.MaxAttachmentContextLength, ct);
    }

    public async Task<IEnumerable<Email>> EnrichForChatAsync(IEnumerable<Email> emails, CancellationToken ct = default)
    {
        var enriched = new List<Email>();
        foreach (var email in emails)
        {
            if (!email.HasAttachments)
            {
                enriched.Add(email);
                continue;
            }

            // Chat uses local DB only — never block on live IMAP during a question.
            var attachmentText = await _attachments.GetCombinedExtractedTextAsync(
                email.EmailId, AppConstants.MaxAttachmentContextLength, ct);
            if (string.IsNullOrWhiteSpace(attachmentText))
            {
                enriched.Add(email);
                continue;
            }

            enriched.Add(CloneWithAttachmentContext(email, attachmentText));
        }

        return enriched;
    }

    private async Task ProcessEmailAsync(string emailId, CancellationToken ct)
    {
        try
        {
            var dbEmail = await _emails.GetByEmailIdAsync(emailId, ct);
            if (dbEmail is null || !dbEmail.HasAttachments) return;

            var attachments = await _mail.GetAttachmentsAsync(emailId, ct);
            foreach (var info in attachments)
            {
                if (ct.IsCancellationRequested) break;

                var attachmentKey = $"{emailId}|{info.Id}";
                if (await _attachments.ExistsAsync(attachmentKey, ct)) continue;

                var record = new Attachment
                {
                    AttachmentId = attachmentKey,
                    EmailRecordId = dbEmail.Id,
                    EmailId = emailId,
                    FileName = info.FileName,
                    ContentType = info.ContentType,
                    SizeBytes = info.SizeBytes,
                    CreatedAt = DateTime.UtcNow
                };

                if (_extractor.CanExtract(info.ContentType, info.FileName))
                {
                    var bytes = await _mail.GetAttachmentContentAsync(emailId, info.Id, ct);
                    if (bytes is not null)
                    {
                        record.ExtractedText = await _extractor.ExtractTextAsync(
                            bytes, info.ContentType, info.FileName, ct);
                        record.IsTextExtracted = !string.IsNullOrWhiteSpace(record.ExtractedText);
                    }
                }

                await _attachments.InsertAsync(record, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Attachment processing failed for {EmailId}", emailId);
        }
    }

    private static Email CloneWithAttachmentContext(Email email, string attachmentText)
    {
        return new Email
        {
            Id = email.Id,
            EmailId = email.EmailId,
            ConversationId = email.ConversationId,
            MessageId = email.MessageId,
            Subject = email.Subject,
            Sender = email.Sender,
            SenderName = email.SenderName,
            Recipients = email.Recipients,
            ReceivedDate = email.ReceivedDate,
            BodyText = $"{email.BodyText}\n\n--- Attachment content ---\n{attachmentText}",
            BodyHtml = email.BodyHtml,
            FolderName = email.FolderName,
            FolderId = email.FolderId,
            HasAttachments = email.HasAttachments,
            IsRead = email.IsRead,
            IsImportant = email.IsImportant,
            Importance = email.Importance,
            SyncedAt = email.SyncedAt,
            ChangeKey = email.ChangeKey
        };
    }
}
