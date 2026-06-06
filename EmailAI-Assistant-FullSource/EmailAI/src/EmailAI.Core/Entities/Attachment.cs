namespace EmailAI.Core.Entities;

public class Attachment
{
    public int Id { get; set; }
    public string AttachmentId { get; set; } = string.Empty;   // Graph API attachment ID
    public int EmailRecordId { get; set; }                     // FK to Email.Id
    public string EmailId { get; set; } = string.Empty;        // Graph API message ID
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string ExtractedText { get; set; } = string.Empty;
    public bool IsTextExtracted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Email? Email { get; set; }
}
