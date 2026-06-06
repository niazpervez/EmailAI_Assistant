namespace EmailAI.Core.Entities;

public class ChatMessage
{
    public int Id { get; set; }
    public string SessionId { get; set; } = Guid.NewGuid().ToString();
    public string Role { get; set; } = string.Empty;           // user | assistant | system
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? RelevantEmailIds { get; set; }              // JSON array of email IDs used as context
    public int? TokensUsed { get; set; }
}
