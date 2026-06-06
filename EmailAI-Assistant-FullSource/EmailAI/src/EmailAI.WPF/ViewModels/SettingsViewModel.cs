using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmailAI.Core;
using EmailAI.Core.Interfaces;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace EmailAI.WPF.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsRepository _settings;
    private readonly IMailService _mail;

    [ObservableProperty] private string _apiKeyMasked = "";
    [ObservableProperty] private string _apiKeyStatus = "";
    [ObservableProperty] private Brush _apiKeyStatusBrush = Brushes.Gray;
    [ObservableProperty] private string _selectedModel = "deepseek-chat";
    [ObservableProperty] private string _selectedInterval = "Every 15 minutes";
    [ObservableProperty] private string _selectedSyncPeriod = Core.SyncPeriodOptions.LabelForKey(Core.SyncPeriodOptions.DefaultKey);
    [ObservableProperty] private string _databasePath = "";
    [ObservableProperty] private string _saveStatus = "";
    [ObservableProperty] private Brush _saveStatusBrush = Brushes.Gray;
    [ObservableProperty] private string _googleOAuthClientId = "";
    [ObservableProperty] private string _googleOAuthClientSecret = "";
    [ObservableProperty] private string _microsoftOAuthClientId = "";
    [ObservableProperty] private bool _syncAllFolders = true;
    [ObservableProperty] private bool _folderListEnabled = false;
    [ObservableProperty] private string _folderListHint = "Connect your email account, then refresh to see folders.";

    public List<string> AvailableModels { get; } = ["deepseek-chat", "deepseek-reasoner"];
    public List<string> SyncIntervals { get; } =
        ["Every 5 minutes", "Every 15 minutes", "Every 30 minutes", "Every hour", "Manual only"];

    public List<string> AvailableSyncPeriods { get; } = Core.SyncPeriodOptions.Labels.ToList();

    public ObservableCollection<FolderOption> FolderOptions { get; } = new();

    public SettingsViewModel(ISettingsRepository settings, IMailService mail)
    {
        _settings = settings;
        _mail = mail;
        _ = LoadAsync();
    }

    partial void OnSyncAllFoldersChanged(bool value)
    {
        FolderListEnabled = !value && FolderOptions.Count > 0;
        FolderListHint = value
            ? "All folders on your mail server will sync (Inbox, Sent, and every custom folder)."
            : "Choose which folders to sync. Click Refresh to load folders from your mailbox.";
    }

    private async Task LoadAsync()
    {
        try
        {
            var apiKey = await _settings.GetAsync(SettingsKeys.DeepSeekApiKey);
            var model = await _settings.GetAsync(SettingsKeys.DeepSeekModel) ?? "deepseek-chat";
            var interval = await _settings.GetAsync(SettingsKeys.SyncIntervalMinutes) ?? "15";
            var syncPeriodKey = await _settings.GetAsync(SettingsKeys.SyncPeriodDays);
            var syncAllRaw = await _settings.GetAsync(SettingsKeys.SyncAllFolders);
            var googleId = await _settings.GetAsync(SettingsKeys.OAuthGoogleClientId) ?? "";
            var googleSecret = await _settings.GetAsync(SettingsKeys.OAuthGoogleClientSecret) ?? "";
            var msId = await _settings.GetAsync(SettingsKeys.OAuthMicrosoftClientId) ?? "";

            var syncAll = string.IsNullOrWhiteSpace(syncAllRaw) || syncAllRaw is "1" or "true";
            var savedFolders = await LoadSavedFolderSelectionAsync();

            RunOnUiThread(() =>
            {
                ApiKeyMasked = string.IsNullOrEmpty(apiKey) ? "" : new string('●', Math.Min(apiKey.Length, 20));
                ApiKeyStatus = string.IsNullOrEmpty(apiKey)
                    ? "⚠️ No API key configured"
                    : "✅ API key configured (encrypted)";
                ApiKeyStatusBrush = string.IsNullOrEmpty(apiKey)
                    ? WarningBrush()
                    : SuccessBrush();

                SelectedModel = model;
                SelectedInterval = interval switch
                {
                    "5"  => "Every 5 minutes",
                    "30" => "Every 30 minutes",
                    "60" => "Every hour",
                    "0"  => "Manual only",
                    _    => "Every 15 minutes"
                };

                SelectedSyncPeriod = Core.SyncPeriodOptions.LabelForKey(
                    string.IsNullOrWhiteSpace(syncPeriodKey) ? Core.SyncPeriodOptions.DefaultKey : syncPeriodKey);

                var appData = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "EmailAI Assistant");
                DatabasePath = System.IO.Path.Combine(appData, "emailai.db");
                GoogleOAuthClientId = googleId;
                GoogleOAuthClientSecret = googleSecret;
                MicrosoftOAuthClientId = msId;
                SyncAllFolders = syncAll;
            });

            if (await _mail.IsAuthenticatedAsync())
                await RefreshFoldersFromServerAsync(savedFolders, syncAll);
            else
                ApplyDefaultFolderOptions(savedFolders, syncAll);
        }
        catch (Exception ex)
        {
            RunOnUiThread(() =>
            {
                ApiKeyStatus = $"⚠️ Could not load settings: {ex.Message}";
                ApiKeyStatusBrush = WarningBrush();
            });
        }
    }

    [RelayCommand]
    private async Task RefreshFolders()
    {
        if (!await _mail.IsAuthenticatedAsync())
        {
            RunOnUiThread(() =>
            {
                FolderListHint = "⚠️ Connect your email on the Sync page first, then refresh folders.";
            });
            return;
        }

        var saved = await LoadSavedFolderSelectionAsync();
        await RefreshFoldersFromServerAsync(saved, SyncAllFolders);
    }

    [RelayCommand]
    private async Task SaveApiKey(object parameter)
    {
        var key = parameter as string ?? "";
        if (string.IsNullOrWhiteSpace(key) || key.All(c => c is '●' or '•' or '*'))
        {
            RunOnUiThread(() =>
            {
                ApiKeyStatus = "⚠️ Please enter your DeepSeek API key (from platform.deepseek.com)";
                ApiKeyStatusBrush = WarningBrush();
            });
            return;
        }

        try
        {
            await _settings.SetAsync(SettingsKeys.DeepSeekApiKey, key.Trim(), encrypt: true);
            RunOnUiThread(() =>
            {
                ApiKeyMasked = new string('●', Math.Min(key.Length, 20));
                ApiKeyStatus = "✅ API key saved (encrypted with Windows DPAPI)";
                ApiKeyStatusBrush = SuccessBrush();
            });
        }
        catch (Exception ex)
        {
            RunOnUiThread(() =>
            {
                ApiKeyStatus = $"⚠️ Could not save API key: {ex.Message}";
                ApiKeyStatusBrush = WarningBrush();
            });
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        try
        {
            await _settings.SetAsync(SettingsKeys.DeepSeekModel, SelectedModel);

            var intervalMinutes = SelectedInterval switch
            {
                "Every 5 minutes"  => "5",
                "Every 30 minutes" => "30",
                "Every hour"       => "60",
                "Manual only"      => "0",
                _                  => "15"
            };
            await _settings.SetAsync(SettingsKeys.SyncIntervalMinutes, intervalMinutes);
            await _settings.SetAsync(SettingsKeys.SyncPeriodDays,
                Core.SyncPeriodOptions.KeyForLabel(SelectedSyncPeriod));
            await _settings.SetAsync(SettingsKeys.SyncAllFolders, SyncAllFolders ? "1" : "0");

            var selectedFolders = FolderOptions.Where(f => f.IsSelected).Select(f => f.Name);
            await _settings.SetAsync(SettingsKeys.SyncFolders,
                JsonSerializer.Serialize(selectedFolders));

            await _settings.SetAsync(SettingsKeys.OAuthGoogleClientId, GoogleOAuthClientId.Trim());
            await _settings.SetAsync(SettingsKeys.OAuthGoogleClientSecret, GoogleOAuthClientSecret.Trim());
            await _settings.SetAsync(SettingsKeys.OAuthMicrosoftClientId, MicrosoftOAuthClientId.Trim());

            RunOnUiThread(() =>
            {
                SaveStatus = SyncAllFolders
                    ? "✅ Settings saved — all mailbox folders will sync"
                    : "✅ Settings saved successfully";
                SaveStatusBrush = SuccessBrush();
            });
        }
        catch (Exception ex)
        {
            RunOnUiThread(() =>
            {
                SaveStatus = $"⚠️ Could not save settings: {ex.Message}";
                SaveStatusBrush = WarningBrush();
            });
        }
    }

    [RelayCommand]
    private void BrowseDb()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "SQLite Database|*.db|All Files|*.*",
            Title = "Select Database Location"
        };
        if (dlg.ShowDialog() == true)
            DatabasePath = dlg.FileName;
    }

    [RelayCommand]
    private void ClearData()
    {
        var result = MessageBox.Show(
            "This will delete ALL synced emails, embeddings, and chat history. This cannot be undone.\n\nContinue?",
            "Clear All Data", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            SaveStatus = "⚠️ Please restart the application to complete data reset.";
            SaveStatusBrush = WarningBrush();
        }
    }

    private async Task<HashSet<string>> LoadSavedFolderSelectionAsync()
    {
        var json = await _settings.GetAsync(SettingsKeys.SyncFolders);
        if (string.IsNullOrWhiteSpace(json)) return MailProviderPresets.NormalizeFolderSelection(AppConstants.DefaultFolders);

        try
        {
            var folders = JsonSerializer.Deserialize<string[]>(json);
            if (folders is { Length: > 0 })
                return MailProviderPresets.NormalizeFolderSelection(folders);
        }
        catch { /* use defaults */ }

        return MailProviderPresets.NormalizeFolderSelection(AppConstants.DefaultFolders);
    }

    private async Task RefreshFoldersFromServerAsync(HashSet<string> savedSelection, bool syncAll)
    {
        try
        {
            var serverFolders = (await _mail.GetMailFoldersAsync()).ToList();
            RunOnUiThread(() =>
            {
                FolderOptions.Clear();
                foreach (var folder in serverFolders)
                {
                    FolderOptions.Add(new FolderOption
                    {
                        Name = folder.DisplayName,
                        IsSelected = syncAll || savedSelection.Contains(folder.DisplayName)
                    });
                }

                FolderListEnabled = !syncAll && FolderOptions.Count > 0;
                FolderListHint = syncAll
                    ? $"All {FolderOptions.Count} folder(s) on your mailbox will sync."
                    : $"{FolderOptions.Count} folder(s) found — check the ones you want to sync.";
            });
        }
        catch (Exception ex)
        {
            RunOnUiThread(() =>
            {
                FolderListHint = $"⚠️ Could not load folders: {ex.Message}";
            });
            ApplyDefaultFolderOptions(savedSelection, syncAll);
        }
    }

    private void ApplyDefaultFolderOptions(HashSet<string> savedSelection, bool syncAll)
    {
        RunOnUiThread(() =>
        {
            if (FolderOptions.Count > 0) return;

            foreach (var name in AppConstants.DefaultFolders.Concat(["Drafts"]))
            {
                FolderOptions.Add(new FolderOption
                {
                    Name = name,
                    IsSelected = syncAll || savedSelection.Contains(name)
                });
            }
            FolderListEnabled = !syncAll;
        });
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    private static SolidColorBrush SuccessBrush() =>
        new(Color.FromRgb(62, 207, 142));

    private static SolidColorBrush WarningBrush() =>
        new(Color.FromRgb(245, 166, 35));
}

public class FolderOption : ObservableObject
{
    private bool _isSelected;
    public string Name { get; set; } = "";
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
