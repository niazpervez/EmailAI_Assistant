namespace EmailAI.Core.DTOs;

using EmailAI.Core;

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
    DateTime? LastSyncedAt,
    IEnumerable<SentFollowUpItemDto> SentFollowUps,
    int AwaitingReplyCount,
    int RepliedFollowUpCount
);

public record SentFollowUpItemDto(
    string EmailId,
    string Subject,
    string Recipient,
    DateTime SentDate,
    bool HasReply,
    DateTime? ReplyDate,
    string? ReplySender,
    string Category,
    string StatusLabel
);

public record ChatRequest(string Message, string SessionId, int TopK = AppConstants.DefaultTopK);

public record ChatResponse(string Message, string SessionId, IEnumerable<string> SourceEmailIds, int TokensUsed = 0);

public record SyncStatusDto(
    string FolderName,
    string Status,
    DateTime? LastSyncedAt,
    int TotalSynced,
    string? LastError
);
