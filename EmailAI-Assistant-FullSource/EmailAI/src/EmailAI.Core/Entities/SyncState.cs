namespace EmailAI.Core.Entities;

public class SyncState
{
    public int Id { get; set; }
    public string FolderId { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public string? DeltaLink { get; set; }                     // Graph API delta link for incremental sync
    public string? NextLink { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public int TotalSynced { get; set; }
    public string Status { get; set; } = "idle";               // idle | syncing | error
    public string? LastError { get; set; }
}
