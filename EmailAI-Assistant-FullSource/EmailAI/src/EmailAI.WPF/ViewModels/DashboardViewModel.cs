using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmailAI.Application.Services;
using System.Collections.ObjectModel;

namespace EmailAI.WPF.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly DashboardService _dashboard;
    private readonly EmailAIService _emailAI;

    [ObservableProperty] private int _totalEmails;
    [ObservableProperty] private int _unreadEmails;
    [ObservableProperty] private int _todayEmails;
    [ObservableProperty] private int _indexedEmbeddings;
    [ObservableProperty] private string _actionItems = "Loading…";
    [ObservableProperty] private bool _isLoadingActions;
    [ObservableProperty] private bool _isLoadingSummary;
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private string _summaryTitle = "";
    [ObservableProperty] private bool _hasSummary;
    [ObservableProperty] private string _subtitleText = $"Good {Greeting()}, here's your overview";

    public ObservableCollection<SenderItem> TopSenders { get; } = new();

    public DashboardViewModel(DashboardService dashboard, EmailAIService emailAI)
    {
        _dashboard = dashboard;
        _emailAI = emailAI;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            IsLoadingActions = true;
            var dto = await _dashboard.GetDashboardAsync();
            TotalEmails = dto.TotalEmails;
            UnreadEmails = dto.UnreadEmails;
            TodayEmails = dto.TodayEmails;
            IndexedEmbeddings = dto.IndexedEmbeddings;
            ActionItems = dto.ActionItems;

            TopSenders.Clear();
            foreach (var (sender, count) in dto.TopSenders)
            {
                var parts = sender.Split('@');
                TopSenders.Add(new SenderItem
                {
                    Email = sender,
                    DisplayName = parts[0],
                    Initial = parts[0].Length > 0 ? parts[0][0].ToString().ToUpper() : "?",
                    Count = count
                });
            }
        }
        catch (Exception ex)
        {
            ActionItems = $"Could not load dashboard: {ex.Message}";
        }
        finally
        {
            IsLoadingActions = false;
        }
    }

    [RelayCommand]
    private async Task RefreshActionItems()
    {
        IsLoadingActions = true;
        try { var dto = await _dashboard.GetDashboardAsync(); ActionItems = dto.ActionItems; }
        catch (Exception ex) { ActionItems = ex.Message; }
        finally { IsLoadingActions = false; }
    }

    [RelayCommand]
    private async Task DailySummary()
    {
        IsLoadingSummary = true;
        HasSummary = false;
        try
        {
            SummaryTitle = "📅 Daily Email Summary";
            SummaryText = await _emailAI.GetDailySummaryAsync();
            HasSummary = true;
        }
        catch (Exception ex) { SummaryText = ex.Message; HasSummary = true; }
        finally { IsLoadingSummary = false; }
    }

    [RelayCommand]
    private async Task WeeklySummary()
    {
        IsLoadingSummary = true;
        HasSummary = false;
        try
        {
            SummaryTitle = "📆 Weekly Email Summary";
            SummaryText = await _emailAI.GetWeeklySummaryAsync();
            HasSummary = true;
        }
        catch (Exception ex) { SummaryText = ex.Message; HasSummary = true; }
        finally { IsLoadingSummary = false; }
    }

    [RelayCommand]
    private void OpenChat() { /* Raise event handled by MainViewModel */ }

    private static string Greeting()
    {
        var h = DateTime.Now.Hour;
        return h < 12 ? "morning" : h < 17 ? "afternoon" : "evening";
    }
}

public class SenderItem
{
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Initial { get; set; } = "";
    public int Count { get; set; }
}
