namespace EmailAI.Core.Entities;

public class EmailEmbedding
{
    public int Id { get; set; }
    public int EmailRecordId { get; set; }
    public string EmailId { get; set; } = string.Empty;
    public float[] Vector { get; set; } = Array.Empty<float>();
    public int Dimensions { get; set; }
    public string ModelUsed { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Email? Email { get; set; }
}
