using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmailAI.Core;
using EmailAI.Core.DTOs;
using EmailAI.Core.Interfaces;
using System.Collections.ObjectModel;
using System.Text;
using EmailAI.UI.Shared.Abstractions;

namespace EmailAI.UI.Shared.ViewModels;

public partial class SyncViewModel : ObservableObject
{
    private readonly IMailService _mail;
    private readonly ISyncService _sync;
    private readonly ISyncStateRepository _syncStates;
    private readonly ISettingsRepository _settings;
    private readonly IExternalMailImportService _import;
    private readonly MainViewModel _main;
    private readonly IUiDispatcher _ui;
    private readonly IMessageService _messages;

    [ObservableProperty] private bool _isSignedIn;
    [ObservableProperty] private string _userName = "";
    [ObservableProperty] private string _userEmail = "";
    [ObservableProperty] private string _userInitial = "?";
    [ObservableProperty] private bool _isSyncing;
    [ObservableProperty] private bool _canSync;
    [ObservableProperty] private double _syncProgress;
    [ObservableProperty] private string _syncStatusText = "Ready to sync";
    [ObservableProperty] private string _syncLog = "";

    [ObservableProperty] private string _accountEmail = "";
    [ObservableProperty] private string _selectedProvider = "Gmail";
    [ObservableProperty] private bool _showCustomServers;
    [ObservableProperty] private string _imapHost = "";
    [ObservableProperty] private string _imapPort = "993";
    [ObservableProperty] private string _smtpHost = "";
    [ObservableProperty] private string _smtpPort = "587";
    [ObservableProperty] private string _connectionHint = "";
    [ObservableProperty] private bool _showOAuthButtons;
    [ObservableProperty] private bool _showGoogleOAuth;
    [ObservableProperty] private bool _showMicrosoftOAuth;
    [ObservableProperty] private string _selectedImapEncryption = MailEncryptionLabels.Options[0];
    [ObservableProperty] private string _selectedSmtpEncryption = MailEncryptionLabels.Options[0];

    public List<string> EncryptionOptions { get; } = MailEncryptionLabels.Options.ToList();

    public List<string> ProviderOptions { get; } =
    [
        "Gmail",
        "Yahoo Mail",
        "Outlook / Hotmail / Live",
        "Custom (IMAP/SMTP)"
    ];

    public List<string> AvailableSyncPeriods { get; } = Core.SyncPeriodOptions.Labels.ToList();

    [ObservableProperty] private string _selectedSyncPeriod = Core.SyncPeriodOptions.LabelForKey(Core.SyncPeriodOptions.DefaultKey);

    public ObservableCollection<FolderStateItem> FolderStates { get; } = new();
    public ObservableCollection<DiscoveredAccountItem> DiscoveredAccounts { get; } = new();

    [ObservableProperty] private bool _showImportSection;
    [ObservableProperty] private bool _outlookDetected;
    [ObservableProperty] private bool _thunderbirdDetected;
    [ObservableProperty] private bool _isScanningAccounts;
    [ObservableProperty] private DiscoveredAccountItem? _selectedDiscoveredAccount;
    [ObservableProperty] private string _importStatusText = "";
    [ObservableProperty] private string _folderPreviewText = "";
    [ObservableProperty] private bool _startSyncAfterImport = true;

    private DiscoveredMailAccount? _appliedImportAccount;
    private readonly StringBuilder _logBuffer = new();
    private CancellationTokenSource? _syncCts;

    public SyncViewModel(
        IMailService mail,
        ISyncService sync,
        ISyncStateRepository syncStates,
        ISettingsRepository settings,
        IExternalMailImportService import,
        MainViewModel main, IUiDispatcher ui, IMessageService messages)
    {
        _mail = mail;
        _sync = sync;
        _syncStates = syncStates;
        _settings = settings;
        _import = import;
        _main = main;
        _ui = ui;
        _messages = messages;

        _sync.SyncProgressChanged += OnSyncProgress;
        _ = InitAsync();
    }

    partial void OnAccountEmailChanged(string value) => RefreshOAuthVisibility();
    partial void OnImapHostChanged(string value) => RefreshOAuthVisibility();

    private void RefreshOAuthVisibility()
    {
        var isGmail = SelectedProvider == "Gmail" || MailProviderDetector.LooksLikeGoogle(AccountEmail, ImapHost);
        var isMicrosoft = SelectedProvider == "Outlook / Hotmail / Live"
            || MailProviderDetector.LooksLikeMicrosoft(AccountEmail, ImapHost, SmtpHost);

        ShowGoogleOAuth = isGmail;
        ShowMicrosoftOAuth = isMicrosoft;
        ShowOAuthButtons = isGmail || isMicrosoft;

        if (ShowMicrosoftOAuth && ShowCustomServers)
        {
            ConnectionHint = "⚠️ Hotmail/Outlook detected — password login is usually BLOCKED by Microsoft even with correct servers. Use 'Sign in with Microsoft' above (browser opens). Settings → OAuth Setup → add Microsoft Client ID first.";
        }
        else if (ShowGoogleOAuth && ShowCustomServers)
        {
            ConnectionHint = "⚠️ Gmail detected — use 'Sign in with Google' (browser) or a 16-char App Password. Normal Gmail password will not work.";
        }
    }

    partial void OnSelectedProviderChanged(string value)
    {
        ShowCustomServers = value.StartsWith("Custom", StringComparison.OrdinalIgnoreCase);
        ShowGoogleOAuth = value == "Gmail";
        ShowMicrosoftOAuth = value == "Outlook / Hotmail / Live";
        ShowOAuthButtons = value is "Gmail" or "Outlook / Hotmail / Live";

        ConnectionHint = value switch
        {
            "Gmail" => "⚠️ Gmail will NOT send a notification inside this app. Use 'Sign in with Google' (browser opens) OR paste a 16-char App Password below.",
            "Yahoo Mail" => "Yahoo requires an app password from login.yahoo.com/account/security — not your normal password.",
            "Outlook / Hotmail / Live" => "⚠️ Microsoft blocks normal passwords for most accounts. Use 'Sign in with Microsoft' (browser opens). App Password only works if still enabled on your account.",
            _ => "Set IMAP/SMTP servers and choose encryption: SSL/TLS for port 993, STARTTLS for port 587."
        };

        if (!ShowCustomServers)
        {
            var provider = ParseProvider(value);
            var preset = MailProviderPresets.ApplyPreset(provider, new MailAccountConfig { Email = AccountEmail });
            ImapHost = preset.ImapHost;
            ImapPort = preset.ImapPort.ToString();
            SmtpHost = preset.SmtpHost;
            SmtpPort = preset.SmtpPort.ToString();
        }

        RefreshOAuthVisibility();
    }

    private async Task InitAsync()
    {
        var periodKey = await _settings.GetAsync(SettingsKeys.SyncPeriodDays);
        var saved = await _mail.GetSavedAccountAsync();
        var signedIn = await _mail.IsAuthenticatedAsync();

        _ui.Invoke(() =>
        {
            SelectedSyncPeriod = Core.SyncPeriodOptions.LabelForKey(
                string.IsNullOrWhiteSpace(periodKey) ? Core.SyncPeriodOptions.DefaultKey : periodKey);

            if (saved is not null)
            {
                AccountEmail = saved.Email;
                SelectedProvider = MailProviderPresets.ProviderLabel(saved.Provider);
                ImapHost = saved.ImapHost;
                ImapPort = saved.ImapPort.ToString();
                SmtpHost = saved.SmtpHost;
                SmtpPort = saved.SmtpPort.ToString();
                SelectedImapEncryption = MailEncryptionLabels.Label(saved.ImapEncryption);
                SelectedSmtpEncryption = MailEncryptionLabels.Label(saved.SmtpEncryption);
            }

            IsSignedIn = signedIn;
        });

        if (signedIn)
        {
            var name = await _mail.GetUserDisplayNameAsync();
            var email = await _mail.GetUserEmailAsync();
            _ui.Invoke(() =>
            {
                UserName = name;
                UserEmail = email;
                UserInitial = name.Length > 0 ? name[0].ToString().ToUpper() : "?";
                CanSync = true;
            });
            await _main.RefreshUserInfoAsync();
            await LoadFolderStatesAsync();
        }

        _ = ScanAccountsInternalAsync();
    }

    [RelayCommand]
    private async Task ScanAccounts()
    {
        await ScanAccountsInternalAsync();
    }

    [RelayCommand]
    private void UseDiscoveredAccount()
    {
        if (SelectedDiscoveredAccount?.Account is not { } account) return;
        ApplyDiscoveredAccount(account);
    }

    private void ApplyDiscoveredAccount(DiscoveredMailAccount account)
    {
        _appliedImportAccount = account;
        var config = _import.ToMailAccountConfig(account);

        AccountEmail = config.Email;
        SelectedProvider = MailProviderPresets.ProviderLabel(config.Provider);
        ImapHost = config.ImapHost;
        ImapPort = config.ImapPort.ToString();
        SmtpHost = config.SmtpHost;
        SmtpPort = config.SmtpPort.ToString();
        SelectedImapEncryption = MailEncryptionLabels.Label(config.ImapEncryption);
        SelectedSmtpEncryption = MailEncryptionLabels.Label(config.SmtpEncryption);
        ShowCustomServers = config.Provider == MailProvider.Custom;

        RefreshOAuthVisibility();

        ImportStatusText = account.Source switch
        {
            ExternalMailClient.Outlook => $"Imported Outlook settings for {account.Email}. Connect below — all mailbox folders will sync (including rule folders).",
            _ => $"Imported Thunderbird settings for {account.Email}. Connect below — all mailbox folders will sync."
        };

        Log($"📥 Applied {account.Source} account: {account.Email}");
        Log("All server folders will sync after connect (Inbox, Sent, and custom/rule folders).");
    }

    private async Task ScanAccountsInternalAsync()
    {
        IsScanningAccounts = true;
        try
        {
            var clients = await _import.DetectInstalledClientsAsync();
            OutlookDetected = clients.OutlookInstalled;
            ThunderbirdDetected = clients.ThunderbirdInstalled;
            ShowImportSection = clients.OutlookInstalled || clients.ThunderbirdInstalled;

            if (!ShowImportSection)
            {
                ImportStatusText = "Outlook and Thunderbird were not detected on this PC.";
                return;
            }

            var accounts = await _import.DiscoverAccountsAsync();
            _ui.Invoke(() =>
            {
                DiscoveredAccounts.Clear();
                foreach (var a in accounts)
                    DiscoveredAccounts.Add(new DiscoveredAccountItem(a));
            });

            var clientParts = new List<string>();
            if (clients.OutlookInstalled) clientParts.Add("Outlook");
            if (clients.ThunderbirdInstalled) clientParts.Add("Thunderbird");

            ImportStatusText = accounts.Count > 0
                ? $"Found {accounts.Count} account(s) from {string.Join(" and ", clientParts)}. Select one and click Use Account."
                : $"{string.Join(" and ", clientParts)} detected but no IMAP accounts were found. Add an IMAP account in your mail client first, or connect manually below.";
        }
        catch (Exception ex)
        {
            ImportStatusText = "Could not scan mail clients: " + ex.Message;
            Log($"❌ Import scan failed: {ex.Message}");
        }
        finally
        {
            IsScanningAccounts = false;
        }
    }

    private async Task CompleteImportSetupAsync()
    {
        if (_appliedImportAccount is null) return;

        await _settings.SetAsync(SettingsKeys.SyncAllFolders, "1");
        await _settings.SetAsync(SettingsKeys.SyncFolders, "[]");

        try
        {
            var folders = (await _mail.GetMailFoldersAsync()).ToList();
            FolderPreviewText = folders.Count > 0
                ? $"{folders.Count} folder(s) on your mailbox will sync — including custom and rule-target folders."
                : "Connected — folder list will appear after first sync.";
            Log($"📂 {folders.Count} mailbox folder(s) ready to sync (all folders enabled).");

            if (folders.Count > 0)
            {
                var preview = string.Join(", ", folders.Take(8).Select(f => f.DisplayName));
                if (folders.Count > 8)
                    preview += $", … +{folders.Count - 8} more";
                Log($"   Folders: {preview}");
            }
        }
        catch (Exception ex)
        {
            FolderPreviewText = "Connected — could not list folders yet.";
            Log($"⚠️ Folder preview failed: {ex.Message}");
        }

        if (StartSyncAfterImport)
        {
            Log("▶️ Starting initial sync for all folders…");
            await SyncNowCommand.ExecuteAsync(null);
        }

        _appliedImportAccount = null;
    }

    public async Task ConnectWithPasswordAsync(string password)
    {
        if (string.IsNullOrWhiteSpace(AccountEmail) || string.IsNullOrWhiteSpace(password))
        {
            Log("❌ Enter your email address and password.");
            return;
        }

        try
        {
            var provider = ParseProvider(SelectedProvider);
            var config = new MailAccountConfig
            {
                Email = AccountEmail.Trim(),
                Password = password,
                Provider = provider,
                ImapHost = ImapHost.Trim(),
                ImapPort = int.TryParse(ImapPort, out var ip) ? ip : 993,
                SmtpHost = SmtpHost.Trim(),
                SmtpPort = int.TryParse(SmtpPort, out var sp) ? sp : 587,
                UseSsl = true,
                ImapEncryption = MailEncryptionLabels.Parse(SelectedImapEncryption),
                SmtpEncryption = MailEncryptionLabels.Parse(SelectedSmtpEncryption)
            };

            Log($"Connecting to {config.Email} via {config.ImapHost}…");
            await _mail.ConnectAccountAsync(config);

            _ui.Invoke(() =>
            {
                IsSignedIn = true;
                UserEmail = config.Email;
                UserName = config.DisplayName;
                UserInitial = UserName.Length > 0 ? UserName[0].ToString().ToUpper() : "?";
                CanSync = true;
            });

            Log("✅ Connected to " + config.Email);
            await _main.RefreshUserInfoAsync();
            await LoadFolderStatesAsync();
            await CompleteImportSetupAsync();
        }
        catch (Exception ex)
        {
            if (MailProviderDetector.LooksLikeMicrosoft(AccountEmail, ImapHost, SmtpHost))
            {
                Log($"""
                    ❌ Password login rejected by Microsoft.

                    Your servers (outlook.office365.com / smtp-mail.outlook.com) are CORRECT.
                    Microsoft blocks normal passwords for most Hotmail accounts — this is not a settings error.

                    FIX — do this instead:
                    1. Settings → OAuth Setup → paste Microsoft Client ID → Save
                    2. Click "Sign in with Microsoft" above (browser opens — approve there)

                    Technical detail: {ex.Message}
                    """);
            }
            else
            {
                Log($"❌ Connection failed:\n{ex.Message}");
            }
        }
    }

    [RelayCommand]
    private async Task SignInGoogle()
    {
        await ConnectOAuthInternalAsync(MailProvider.Gmail);
    }

    [RelayCommand]
    private async Task SignInMicrosoft()
    {
        await ConnectOAuthInternalAsync(MailProvider.Outlook);
    }

    private async Task ConnectOAuthInternalAsync(MailProvider provider)
    {
        var isGoogle = provider == MailProvider.Gmail;
        var clientKey = isGoogle ? SettingsKeys.OAuthGoogleClientId : SettingsKeys.OAuthMicrosoftClientId;
        var providerName = isGoogle ? "Google" : "Microsoft";

        try
        {
            var clientId = await _settings.GetAsync(clientKey);
            if (string.IsNullOrWhiteSpace(clientId))
            {
                const string msg = """
                    OAuth Client ID is not configured.

                    1. Open Settings → OAuth Setup
                    2. Paste your Microsoft (or Google) Client ID
                    3. Click "Save Settings"
                    4. Return here and try again

                    Azure Portal: portal.azure.com → App registrations
                    Redirect URI: http://localhost
                    """;
                _ = _messages.ShowAlertAsync(msg, "OAuth Setup Required", MessageIcon.Warning);
                Log($"❌ {providerName} OAuth Client ID missing — configure in Settings → OAuth Setup → Save Settings.");
                return;
            }

            Log(isGoogle
                ? "Opening browser for Google sign-in…"
                : "Opening browser for Microsoft sign-in…");

            // MSAL interactive login must run on the WPF UI thread
            await _ui.InvokeAsync(async () => await _mail.ConnectOAuthAsync(provider));

            var email = await _mail.GetUserEmailAsync();
            var name = await _mail.GetUserDisplayNameAsync();

            _ui.Invoke(() =>
            {
                IsSignedIn = true;
                AccountEmail = email;
                UserEmail = email;
                UserName = name;
                UserInitial = name.Length > 0 ? name[0].ToString().ToUpper() : "?";
                CanSync = true;
                SelectedProvider = MailProviderPresets.ProviderLabel(provider);
            });

            Log("✅ Connected via OAuth: " + email);
            await _main.RefreshUserInfoAsync();
            await LoadFolderStatesAsync();
            await CompleteImportSetupAsync();
        }
        catch (Exception ex)
        {
            Log($"❌ OAuth sign-in failed:\n{ex.Message}");
            _ = _messages.ShowAlertAsync(ex.Message, $"{providerName} Sign-in Failed", MessageIcon.Error);
        }
    }

    [RelayCommand]
    private async Task SignOut()
    {
        await _mail.SignOutAsync();
        IsSignedIn = false;
        CanSync = false;
        FolderStates.Clear();
        await _main.RefreshUserInfoAsync();
        Log("Disconnected.");
    }

    [RelayCommand]
    private async Task SyncNow()
    {
        if (IsSyncing) return;

        if (string.IsNullOrWhiteSpace(SelectedSyncPeriod))
        {
            Log("❌ Choose how far back to sync before starting.");
            return;
        }

        if (SelectedSyncPeriod == "All emails")
        {
            var confirm = await _messages.ShowConfirmAsync(
                "All emails will download every message in the selected folders. This can take a long time and use a lot of disk space.\n\nContinue?",
                "Sync all emails?",
                MessageIcon.Question);
            if (!confirm)
                return;
        }

        var periodKey = Core.SyncPeriodOptions.KeyForLabel(SelectedSyncPeriod);
        await _settings.SetAsync(SettingsKeys.SyncPeriodDays, periodKey);
        var syncOptions = Core.SyncPeriodOptions.ToOptions(periodKey);

        _syncCts = new CancellationTokenSource();
        IsSyncing = true;
        CanSync = false;
        SyncProgress = 0;
        Log($"▶️ Starting sync — {syncOptions.PeriodLabel} only…");

        try
        {
            var progress = new Progress<SyncProgress>(p =>
            {
                _ui.Invoke(() =>
                {
                    SyncStatusText = $"{p.FolderName}: {p.Status}";
                    Log($"[{p.FolderName}] {p.Status}" + (p.Error is not null ? $" — {p.Error}" : ""));
                });
            });

            await _sync.SyncAllFoldersAsync(progress, syncOptions, _syncCts.Token);
            await LoadFolderStatesAsync();
            await _main.RefreshUserInfoAsync();

            var states = (await _syncStates.GetAllAsync()).ToList();
            var errors = states.Where(s => s.Status == "error").ToList();

            if (errors.Count > 0)
            {
                var detail = string.Join("; ", errors.Select(e =>
                    $"{e.FolderName}: {(e.LastError?.Length > 80 ? e.LastError[..80] + "…" : e.LastError)}"));
                Log($"⚠️ Sync finished with {errors.Count} error(s): {detail}");
            }
            else if (FolderStates.Count == 0)
                Log("⚠️ Sync finished but no folders were synced. Check Settings → folders or try Sync again.");
            else
                Log($"✅ Sync complete! {FolderStates.Sum(f => f.TotalSynced)} emails across {FolderStates.Count} folder(s).");
        }
        catch (OperationCanceledException)
        {
            Log("⏹ Sync cancelled.");
        }
        catch (Exception ex)
        {
            Log($"❌ Sync error: {ex.Message}");
        }
        finally
        {
            IsSyncing = false;
            CanSync = true;
            SyncProgress = 100;
        }
    }

    [RelayCommand]
    private void ClearLog()
    {
        _logBuffer.Clear();
        SyncLog = "";
    }

    private void OnSyncProgress(object? sender, Core.Interfaces.SyncProgress e)
    {
        _ui.Invoke(() =>
        {
            SyncStatusText = $"{e.FolderName}: {e.Status}";
        });
    }

    private async Task LoadFolderStatesAsync()
    {
        var states = await _syncStates.GetAllAsync();
        _ui.Invoke(() =>
        {
            FolderStates.Clear();
            foreach (var s in states)
            {
                FolderStates.Add(new FolderStateItem
                {
                    FolderName = s.FolderName,
                    TotalSynced = s.TotalSynced,
                    LastSyncText = s.LastSyncedAt.HasValue
                        ? $"Last synced {s.LastSyncedAt.Value:g}"
                        : "Never synced",
                    StatusColor = s.Status switch
                    {
                        "error"   => UiColors.Danger,
                        "syncing" => UiColors.Warning,
                        _         => UiColors.Success
                    }
                });
            }
        });
    }

    private static MailProvider ParseProvider(string label) => label switch
    {
        "Yahoo Mail" => MailProvider.Yahoo,
        "Outlook / Hotmail / Live" => MailProvider.Outlook,
        var s when s.StartsWith("Custom", StringComparison.OrdinalIgnoreCase) => MailProvider.Custom,
        _ => MailProvider.Gmail
    };

    private void Log(string message)
    {
        _ui.Invoke(() =>
        {
            _logBuffer.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}\n\n");
            if (_logBuffer.Length > 8000) _logBuffer.Remove(6000, _logBuffer.Length - 6000);
            SyncLog = _logBuffer.ToString();
        });
    }
}

public class FolderStateItem
{
    public string FolderName { get; set; } = "";
    public int TotalSynced { get; set; }
    public string LastSyncText { get; set; } = "";
    public string StatusColor { get; set; } = UiColors.Muted;
}

public class DiscoveredAccountItem
{
    public DiscoveredAccountItem(DiscoveredMailAccount account) => Account = account;

    public DiscoveredMailAccount Account { get; }

    public string SourceLabel => Account.Source switch
    {
        ExternalMailClient.Outlook => "Outlook",
        _ => "Thunderbird"
    };

    public string Email => Account.Email;
    public string Summary => Account.Summary;
}
