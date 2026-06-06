namespace EmailAI.Core;

public static class SettingsKeys
{
    public const string DeepSeekApiKey = "DeepSeek:ApiKey";
    public const string DeepSeekModel = "DeepSeek:Model";
    public const string DeepSeekEmbeddingModel = "DeepSeek:EmbeddingModel";
    public const string SyncIntervalMinutes = "Sync:IntervalMinutes";
    public const string SyncFolders = "Sync:Folders";
    public const string SyncAllFolders = "Sync:AllFolders";
    public const string SyncPeriodDays = "Sync:PeriodDays";
    public const string DatabasePath = "Database:Path";
    public const string MailEmail = "Mail:Email";
    public const string MailPassword = "Mail:Password";
    public const string MailProvider = "Mail:Provider";
    public const string MailImapHost = "Mail:ImapHost";
    public const string MailImapPort = "Mail:ImapPort";
    public const string MailSmtpHost = "Mail:SmtpHost";
    public const string MailSmtpPort = "Mail:SmtpPort";
    public const string MailUseSsl = "Mail:UseSsl";
    public const string MailAuthMethod = "Mail:AuthMethod";
    public const string MailImapEncryption = "Mail:ImapEncryption";
    public const string MailSmtpEncryption = "Mail:SmtpEncryption";
    public const string MailOAuthRefreshToken = "Mail:OAuthRefreshToken";
    public const string OAuthGoogleClientId = "OAuth:Google:ClientId";
    public const string OAuthGoogleClientSecret = "OAuth:Google:ClientSecret";
    public const string OAuthMicrosoftClientId = "OAuth:Microsoft:ClientId";
    public const string UserEmail = "User:Email";
    public const string UserDisplayName = "User:DisplayName";
    public const string MaxEmailsPerSync = "Sync:MaxEmailsPerSync";
    public const string EmbeddingDimensions = "Embedding:Dimensions";
}

public static class AppConstants
{
    public static readonly string[] DefaultFolders = ["Inbox", "Sent", "Archive", "Trash"];
    public const int DefaultSyncIntervalMinutes = 15;
    public const int DefaultTopK = 10;
    public const int MaxContextEmails = 15;
    public const int MaxBodyLength = 4000;
    public const string DeepSeekChatModel = "deepseek-chat";
    public const string DeepSeekEmbeddingModel = "deepseek-reasoner"; // fallback: use OpenAI-compatible embedding
    public const int EmbeddingDimensions = 1536;
    public const string AppName = "EmailAI Assistant";
    public const string AppVersion = "1.0.0";
}
