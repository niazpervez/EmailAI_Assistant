namespace EmailAI.Core.DTOs;

public record EmailSummaryDto(
    string EmailId,
    string Subject,
    string Sender,
    string SenderName,
    DateTime ReceivedDate,
    string FolderName,
    bool IsRead,
    bool HasAttachments,
    bool IsImportant,
    string PreviewText
);

public record DashboardDto(
    int TotalEmails,
    int UnreadEmails,
    int TodayEmails,
    int IndexedEmbeddings,
    IEnumerable<(string Sender, int Count)> TopSenders,
    string ActionItems,
    DateTime? LastSyncedAt
);

public record ChatRequest(string Message, string SessionId, int TopK = 10);

public record ChatResponse(string Message, string SessionId, IEnumerable<string> SourceEmailIds, int TokensUsed = 0);

public record SyncStatusDto(
    string FolderName,
    string Status,
    DateTime? LastSyncedAt,
    int TotalSynced,
    string? LastError
);
