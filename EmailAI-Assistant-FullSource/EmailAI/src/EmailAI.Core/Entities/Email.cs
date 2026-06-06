namespace EmailAI.Core.Entities;

public class Email
{
    public int Id { get; set; }
    public string EmailId { get; set; } = string.Empty;        // Graph API message ID
    public string ConversationId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string Recipients { get; set; } = string.Empty;     // JSON array
    public DateTime ReceivedDate { get; set; }
    public string BodyText { get; set; } = string.Empty;       // Plain text body
    public string BodyHtml { get; set; } = string.Empty;       // HTML body
    public string FolderName { get; set; } = string.Empty;
    public string FolderId { get; set; } = string.Empty;
    public bool HasAttachments { get; set; }
    public bool IsRead { get; set; }
    public bool IsImportant { get; set; }
    public string Importance { get; set; } = "normal";
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
    public string? ChangeKey { get; set; }                     // For incremental sync

    // Navigation
    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
    public EmailEmbedding? Embedding { get; set; }
}
