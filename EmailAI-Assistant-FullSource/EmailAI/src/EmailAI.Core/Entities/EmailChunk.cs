namespace EmailAI.Core.Entities;

public class EmailChunk
{
    public int Id { get; set; }
    public string ChunkId { get; set; } = string.Empty;
    public int EmailRecordId { get; set; }
    public string EmailId { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    /// <summary>header, body, or attachment</summary>
    public string Source { get; set; } = "body";
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
