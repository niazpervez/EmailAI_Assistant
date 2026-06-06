# EmailAI Assistant Windows Installer

> Production-ready AI-powered Windows desktop email management application  
> Built with .NET 9 · WPF · IMAP/SMTP (MailKit) · DeepSeek · SQLite + sqlite-vec

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                        EmailAI Assistant                         │
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │                     WPF UI Layer                          │   │
│  │  MainWindow → DashboardView · ChatView · EmailListView   │   │
│  │  SyncView · SettingsView                                  │   │
│  │  MVVM: CommunityToolkit.Mvvm                             │   │
│  └──────────────────────────────────────────────────────────┘   │
│                            ↕ DI                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │                  Application Layer                        │   │
│  │  DashboardService · ChatService · EmailAIService          │   │
│  └──────────────────────────────────────────────────────────┘   │
│                            ↕ Interfaces                          │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │                 Infrastructure Layer                      │   │
│  │                                                           │   │
│  │  ┌─────────────┐  ┌──────────────┐  ┌────────────────┐  │   │
│  │  │ IMAP/SMTP   │  │  DeepSeek AI │  │  SQLite + vec  │  │   │
│  │  │ (MailKit)   │  │  Chat+Embed  │  │  Repositories  │  │   │
│  │  └─────────────┘  └──────────────┘  └────────────────┘  │   │
│  │  ┌─────────────┐  ┌──────────────┐  ┌────────────────┐  │   │
│  │  │ SyncService │  │ SearchService│  │  DPAPI Encrypt │  │   │
│  │  │ (UID sync)  │  │ (Hybrid RAG) │  │  (Credentials) │  │   │
│  │  └─────────────┘  └──────────────┘  └────────────────┘  │   │
│  └──────────────────────────────────────────────────────────┘   │
│                            ↕                                     │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │                    Core Layer                             │   │
│  │  Entities · Interfaces · DTOs · Constants                │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

## RAG Pipeline

```
User Question
     ↓
Generate Embedding (DeepSeek / OpenAI-compatible)
     ↓
Vector Search (sqlite-vec KNN) + FTS5 keyword search
     ↓
Top-K Relevant Emails (hybrid merge)
     ↓
Context Assembly (subject + sender + body truncated to 4000 chars each)
     ↓
DeepSeek Chat API (deepseek-chat model, system prompt + context)
     ↓
AI Response → persisted in ChatMessages table
```

---

## Project Structure

```
EmailAI/
├── EmailAI.sln
├── build.ps1                          # One-command build + MSI packaging
├── installer/
│   └── EmailAI.Installer.wxs          # WiX 4 MSI definition
└── src/
    ├── EmailAI.Core/                   # Domain — no external dependencies
    │   ├── Entities/
    │   │   ├── Email.cs
    │   │   ├── Attachment.cs
    │   │   ├── EmailEmbedding.cs
    │   │   ├── SyncState.cs
    │   │   ├── ChatMessage.cs
    │   │   └── AppSetting.cs
    │   ├── Interfaces/
    │   │   ├── IRepositories.cs        # IEmailRepository, IEmbeddingRepository, …
    │   │   └── IServices.cs            # IGraphService, IAIService, ISyncService, …
    │   └── DTOs/
    │       └── DTOs.cs                 # DashboardDto, ChatRequest/Response, …
    │
    ├── EmailAI.Infrastructure/         # External integrations
    │   ├── Data/
    │   │   ├── DatabaseInitializer.cs  # Schema DDL + sqlite-vec virtual tables
    │   │   ├── DatabaseConnectionFactory.cs
    │   │   └── Repositories/
    │   │       ├── EmailRepository.cs         # FTS5 + LIKE fallback search
    │   │       ├── EmbeddingRepository.cs     # sqlite-vec + brute-force fallback
    │   │       └── OtherRepositories.cs       # Attachment, SyncState, Chat, Settings
    │   ├── Services/
    │   │   ├── AI/
    │   │   │   ├── DeepSeekService.cs         # Chat completions + embeddings
    │   │   │   └── DeepSeekAuthHandler.cs     # Runtime API key injection
    │   │   ├── Graph/
    │   │   │   └── GraphService.cs            # MSAL auth + Graph API delta sync
    │   │   ├── Sync/
    │   │   │   ├── SyncService.cs             # Incremental sync + embedding gen
    │   │   │   └── AutoSyncHostedService.cs   # Background timer
    │   │   ├── AttachmentExtractor.cs         # PDF/DOCX/XLSX/TXT text extraction
    │   │   └── SearchService.cs               # Hybrid vector + keyword search
    │   └── Security/
    │       └── DpapiEncryptionService.cs      # Windows DPAPI for tokens/keys
    │
    ├── EmailAI.Application/            # Use cases
    │   └── Services/
    │       ├── DashboardService.cs
    │       ├── ChatService.cs          # RAG orchestration
    │       └── EmailAIService.cs       # Summaries + reply generation
    │
    └── EmailAI.WPF/                    # Presentation
        ├── App.xaml / App.xaml.cs      # DI host, startup
        ├── Views/
        │   ├── MainWindow.xaml         # Shell + sidebar navigation
        │   ├── DashboardView.xaml      # Stats, action items, summaries
        │   ├── ChatView.xaml           # ChatGPT-style AI chat with RAG
        │   ├── EmailListView.xaml      # Inbox browser + AI reply panel
        │   ├── SyncView.xaml           # OAuth sign-in + sync control
        │   └── SettingsView.xaml       # API keys, intervals, folders
        ├── ViewModels/
        │   ├── MainViewModel.cs
        │   ├── DashboardViewModel.cs
        │   ├── ChatViewModel.cs
        │   ├── EmailListViewModel.cs
        │   ├── SyncViewModel.cs
        │   └── SettingsViewModel.cs
        ├── Styles/
        │   ├── Colors.xaml             # Dark theme color palette
        │   ├── Base.xaml               # Typography, window defaults
        │   └── Controls.xaml           # Button, TextBox, Card, Badge styles
        └── Converters/
            └── Converters.cs           # BoolToVisibility, InverseBool, …
```

---

## Database Schema

```sql
-- Core email storage
CREATE TABLE Emails (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    EmailId         TEXT UNIQUE NOT NULL,   -- Graph API message ID
    ConversationId  TEXT,
    Subject         TEXT,
    Sender          TEXT,   -- email address
    SenderName      TEXT,   -- display name
    Recipients      TEXT,   -- JSON array of addresses
    ReceivedDate    TEXT,   -- ISO 8601 UTC
    BodyText        TEXT,   -- plain text (stripped HTML)
    BodyHtml        TEXT,
    FolderName      TEXT,
    FolderId        TEXT,
    HasAttachments  INTEGER DEFAULT 0,
    IsRead          INTEGER DEFAULT 0,
    IsImportant     INTEGER DEFAULT 0,
    Importance      TEXT DEFAULT 'normal',
    SyncedAt        TEXT,
    ChangeKey       TEXT    -- for incremental sync
);

-- Attachment text extraction
CREATE TABLE Attachments (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    AttachmentId    TEXT UNIQUE,
    EmailRecordId   INTEGER REFERENCES Emails(Id) ON DELETE CASCADE,
    FileName        TEXT,
    ContentType     TEXT,
    SizeBytes       INTEGER,
    ExtractedText   TEXT,
    IsTextExtracted INTEGER DEFAULT 0
);

-- Binary embedding vectors
CREATE TABLE EmailEmbeddings (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    EmailRecordId   INTEGER UNIQUE REFERENCES Emails(Id) ON DELETE CASCADE,
    EmailId         TEXT UNIQUE,
    VectorData      BLOB,   -- raw float32 little-endian
    Dimensions      INTEGER,
    ModelUsed       TEXT
);

-- sqlite-vec ANN virtual table
CREATE VIRTUAL TABLE EmailVectors USING vec0(
    email_id     TEXT PRIMARY KEY,
    embedding    float[1536]     -- KNN searchable
);

-- FTS5 full-text search
CREATE VIRTUAL TABLE EmailsFts USING fts5(
    email_id UNINDEXED,
    subject,
    sender_name,
    body_text,
    content='Emails', content_rowid='Id'
);

-- Graph API incremental sync state
CREATE TABLE SyncStates (
    FolderId        TEXT UNIQUE,
    FolderName      TEXT,
    DeltaLink       TEXT,   -- Graph delta link for next sync
    LastSyncedAt    TEXT,
    TotalSynced     INTEGER,
    Status          TEXT    -- idle | syncing | error
);

-- Chat history
CREATE TABLE ChatMessages (
    SessionId       TEXT,
    Role            TEXT,   -- user | assistant
    Content         TEXT,
    RelevantEmailIds TEXT,  -- JSON array of EmailIds used as RAG context
    TokensUsed      INTEGER
);

-- Encrypted settings store
CREATE TABLE AppSettings (
    Key             TEXT UNIQUE,
    Value           TEXT,
    IsEncrypted     INTEGER  -- 1 = DPAPI-encrypted
);
```

---

## Setup Guide

### 1. Prerequisites

| Tool | Version | Install |
|------|---------|---------|
| .NET SDK | 9.0+ | https://dot.net |
| Visual Studio | 2022 17.8+ | Community edition |
| WiX Toolset | 4.x | `dotnet tool install -g wix` |

### 2. Connect Your Email (IMAP)

No Azure or Microsoft 365 registration required. The app uses standard **IMAP** (read/sync) and **SMTP** (send/reply).

1. Launch the app → **Sync**
2. Choose your provider: **Gmail**, **Yahoo**, **Outlook/Hotmail**, or **Custom**
3. Enter your email address and password (or **app password** if 2FA is enabled)
4. Click **Connect**

| Provider | IMAP | SMTP | Notes |
|----------|------|------|-------|
| Gmail | imap.gmail.com:993 | smtp.gmail.com:587 | [App password](https://myaccount.google.com/apppasswords) required with 2FA |
| Yahoo | imap.mail.yahoo.com:993 | smtp.mail.yahoo.com:587 | App password in Yahoo Account Security |
| Outlook/Hotmail | outlook.office365.com:993 | smtp.office365.com:587 | Enable IMAP in Outlook settings |
| Custom | Your server | Your server | Any IMAP/SMTP provider |

Credentials are encrypted with Windows DPAPI and stored locally.

### 3. DeepSeek API Key

1. Register at https://platform.deepseek.com
2. Create an API key
3. Launch the app → **Settings** → paste key → **Save**
4. Key is encrypted with Windows DPAPI (user-scoped, never in plaintext)

### 4. sqlite-vec Extension

1. Download `vec0.dll` from https://github.com/asg017/sqlite-vec/releases  
   (choose the Windows x64 build)
2. Place at: `src/EmailAI.Infrastructure/native/vec0.dll`
3. The build script copies it to the publish output automatically

> **Without vec0.dll**: The app works but vector/semantic search falls back to brute-force cosine similarity in-process. Slower but functional.

### 5. Build & Install

```powershell
# Build + MSI (requires WiX 4)
.\build.ps1

# Build only (skip MSI, for development)
.\build.ps1 -SkipMsi

# Run from Visual Studio
# Set EmailAI.WPF as startup project → F5
```

---

## Security Design

| Concern | Implementation |
|---------|---------------|
| Email passwords | DPAPI-encrypted in local SQLite settings |
| API keys | DPAPI `ProtectedData.Protect` (CurrentUser scope) |
| Database | Local SQLite file in `%LOCALAPPDATA%\EmailAI Assistant\` |
| Network | Connects to your mail provider (IMAP/SMTP) + DeepSeek API only |
| Telemetry | None — fully local-first |
| Plaintext credentials | Never stored |

---

## Feature Checklist

- [x] Provider-agnostic IMAP/SMTP (Gmail, Yahoo, Outlook, custom)
- [x] Email synchronization with IMAP UID tracking (incremental)
- [x] SQLite storage (no external DB required)
- [x] FTS5 full-text search
- [x] sqlite-vec vector similarity search
- [x] DeepSeek RAG chat (retrieval-first, never full mailbox)
- [x] AI email summaries (daily / weekly / customer)
- [x] AI reply generation (4 tones + send confirmation)
- [x] Attachment text extraction (PDF, DOCX, XLSX, TXT)
- [x] Dashboard with stats and action items
- [x] Dark theme WPF UI with MVVM
- [x] DPAPI-encrypted API key storage
- [x] Background auto-sync hosted service
- [x] WiX 4 MSI one-click installer
- [x] Self-contained single-file publish (no .NET runtime install needed)

---

## Extension Points

| Feature | Where to add |
|---------|-------------|
| Additional AI providers | Implement `IAIService`, register in `App.xaml.cs` |
| New email folders | Settings → folder list; `SyncService` reads from `AppSettings` |
| Webhook / push notifications | Add IMAP IDLE support in `ImapMailService` |
| Multi-account support | Add `AccountId` column to `Emails`, multiple saved accounts |
| Conversation threading | `ConversationId` already stored; add UI grouping |
