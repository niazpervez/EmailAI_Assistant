namespace EmailAI.Core.Models;

public sealed class FolderSummary
{
    public string FolderName { get; set; } = "";
    public int TotalCount { get; set; }
    public int UnreadCount { get; set; }
}
