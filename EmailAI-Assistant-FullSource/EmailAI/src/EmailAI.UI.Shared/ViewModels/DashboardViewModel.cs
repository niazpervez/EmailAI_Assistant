using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmailAI.Application.Services;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace EmailAI.UI.Shared.ViewModels;

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
    [ObservableProperty] private string _lastSyncDisplay = "Not synced yet";
    [ObservableProperty] private string _dateDisplay = DateTime.Now.ToString("dddd, MMMM d");
    [ObservableProperty] private bool _hasActionItems;
    [ObservableProperty] private int _awaitingReplyCount;
    [ObservableProperty] private int _repliedFollowUpCount;
    [ObservableProperty] private bool _hasFollowUpItems;

    public ObservableCollection<DashboardKpiItem> KpiCards { get; } = new();
    public ObservableCollection<ActionItemCard> ActionItemCards { get; } = new();
    public ObservableCollection<FollowUpCard> FollowUpCards { get; } = new();
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
            AwaitingReplyCount = dto.AwaitingReplyCount;
            RepliedFollowUpCount = dto.RepliedFollowUpCount;
            LastSyncDisplay = dto.LastSyncedAt.HasValue
                ? $"Last sync · {dto.LastSyncedAt.Value:MMM d, HH:mm}"
                : "Not synced yet";

            TopSenders.Clear();
            var senderList = dto.TopSenders.ToList();
            var maxCount = senderList.Count > 0 ? senderList.Max(s => s.Count) : 1;

            foreach (var (sender, count) in senderList)
            {
                var parts = sender.Split('@');
                var name = parts[0];
                TopSenders.Add(new SenderItem
                {
                    Email = sender,
                    DisplayName = name,
                    Initial = name.Length > 0 ? char.ToUpperInvariant(name[0]).ToString() : "?",
                    Count = count,
                    BarFraction = (double)count / maxCount
                });
            }

            UpdateKpiCards();
            UpdateActionItemCards(ActionItems);
            UpdateFollowUpCards(dto.SentFollowUps);
        }
        catch (Exception ex)
        {
            ActionItems = $"Could not load dashboard: {ex.Message}";
            UpdateActionItemCards(ActionItems);
        }
        finally
        {
            IsLoadingActions = false;
        }
    }

    private void UpdateKpiCards()
    {
        KpiCards.Clear();

        var unreadPct = TotalEmails > 0 ? (double)UnreadEmails / TotalEmails : 0;
        var indexedPct = TotalEmails > 0 ? (double)IndexedEmbeddings / TotalEmails : 0;

        KpiCards.Add(new DashboardKpiItem
        {
            Label = "Total Emails",
            Value = TotalEmails.ToString("N0"),
            Subtitle = TodayEmails > 0 ? $"{TodayEmails} received today" : "Synced to local database",
            Icon = "📧",
            AccentColor = UiColors.Accent,
            AccentSoftColor = UiColors.AccentSoft,
            Progress = 1.0,
            ShowProgress = false
        });

        KpiCards.Add(new DashboardKpiItem
        {
            Label = "Unread",
            Value = UnreadEmails.ToString("N0"),
            Subtitle = TotalEmails > 0 ? $"{unreadPct:P0} of all mail" : "No mail synced",
            Icon = "✉",
            AccentColor = UiColors.AccentAlt,
            AccentSoftColor = UiColors.AccentAltSoft,
            Progress = unreadPct,
            ShowProgress = true
        });

        KpiCards.Add(new DashboardKpiItem
        {
            Label = "Awaiting Reply",
            Value = AwaitingReplyCount.ToString("N0"),
            Subtitle = RepliedFollowUpCount > 0
                ? $"{RepliedFollowUpCount} sent follow-up(s) got replies"
                : "Sent reminders & follow-ups",
            Icon = "📤",
            AccentColor = UiColors.Orange,
            AccentSoftColor = UiColors.OrangeSoft,
            Progress = (AwaitingReplyCount + RepliedFollowUpCount) > 0
                ? (double)RepliedFollowUpCount / (AwaitingReplyCount + RepliedFollowUpCount)
                : 0,
            ShowProgress = AwaitingReplyCount + RepliedFollowUpCount > 0
        });

        KpiCards.Add(new DashboardKpiItem
        {
            Label = "AI Indexed",
            Value = IndexedEmbeddings.ToString("N0"),
            Subtitle = TotalEmails > 0 ? $"{indexedPct:P0} ready for chat" : "Run sync to index",
            Icon = "🧠",
            AccentColor = UiColors.Green,
            AccentSoftColor = UiColors.GreenSoft,
            Progress = indexedPct,
            ShowProgress = true
        });
    }

    private void UpdateActionItemCards(string text)
    {
        ActionItemCards.Clear();
        foreach (var card in ParseActionItems(text))
            ActionItemCards.Add(card);

        HasActionItems = ActionItemCards.Count > 0;
    }

    private void UpdateFollowUpCards(IEnumerable<Core.DTOs.SentFollowUpItemDto> followUps)
    {
        FollowUpCards.Clear();
        foreach (var f in followUps.Take(12))
        {
            FollowUpCards.Add(new FollowUpCard
            {
                EmailId = f.EmailId,
                Subject = f.Subject,
                Recipient = f.Recipient,
                SentDateText = f.SentDate.ToString("MMM d, HH:mm"),
                Category = f.Category,
                StatusLabel = f.StatusLabel,
                HasReply = f.HasReply,
                ReplyDetail = f.HasReply && f.ReplyDate.HasValue
                    ? $"Reply from {f.ReplySender} · {f.ReplyDate.Value:MMM d, HH:mm}"
                    : "No response yet — consider a follow-up"
            });
        }
        HasFollowUpItems = FollowUpCards.Count > 0;
    }

    [RelayCommand]
    private async Task RefreshDashboard() => await LoadAsync();

    [RelayCommand]
    private async Task RefreshActionItems()
    {
        IsLoadingActions = true;
        try
        {
            var dto = await _dashboard.GetDashboardAsync();
            ActionItems = dto.ActionItems;
            AwaitingReplyCount = dto.AwaitingReplyCount;
            RepliedFollowUpCount = dto.RepliedFollowUpCount;
            UpdateKpiCards();
            UpdateActionItemCards(ActionItems);
            UpdateFollowUpCards(dto.SentFollowUps);
        }
        catch (Exception ex)
        {
            ActionItems = ex.Message;
            UpdateActionItemCards(ActionItems);
        }
        finally
        {
            IsLoadingActions = false;
        }
    }

    [RelayCommand]
    private async Task DailySummary()
    {
        IsLoadingSummary = true;
        HasSummary = false;
        try
        {
            SummaryTitle = "Daily Email Summary";
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
            SummaryTitle = "Weekly Email Summary";
            SummaryText = await _emailAI.GetWeeklySummaryAsync();
            HasSummary = true;
        }
        catch (Exception ex) { SummaryText = ex.Message; HasSummary = true; }
        finally { IsLoadingSummary = false; }
    }

    private static List<ActionItemCard> ParseActionItems(string text)
    {
        var result = new List<ActionItemCard>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        var matches = Regex.Matches(
            text,
            @"(\d+)\.\s*\*\*Action:\*\*\s*(.+?)(?=\d+\.\s*\*\*Action:|$)",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        foreach (Match match in matches)
        {
            var body = match.Groups[2].Value.Trim();
            var actionLine = body.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? body;
            result.Add(new ActionItemCard
            {
                Index = int.Parse(match.Groups[1].Value),
                Action = CleanMarkdown(actionLine),
                Responsible = CleanMarkdown(ExtractField(body, "Responsible")),
                Deadline = CleanMarkdown(ExtractField(body, "Deadline"))
            });
        }

        if (result.Count == 0 && !string.IsNullOrWhiteSpace(text) &&
            !text.StartsWith("No action", StringComparison.OrdinalIgnoreCase) &&
            !text.StartsWith("Could not", StringComparison.OrdinalIgnoreCase) &&
            !text.StartsWith("Loading", StringComparison.OrdinalIgnoreCase))
        {
            result.Add(new ActionItemCard
            {
                Index = 1,
                Action = CleanMarkdown(text),
                Responsible = "",
                Deadline = ""
            });
        }

        return result;
    }

    private static string ExtractField(string body, string field)
    {
        var match = Regex.Match(body, $@"-\s*\*\*{field}:\*\*\s*(.+?)(?:\r?\n|$)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }

    private static string CleanMarkdown(string value) =>
        value.Replace("**", "").Trim();

    private static string Greeting()
    {
        var h = DateTime.Now.Hour;
        return h < 12 ? "morning" : h < 17 ? "afternoon" : "evening";
    }
}

public class DashboardKpiItem
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "0";
    public string Subtitle { get; set; } = "";
    public string Icon { get; set; } = "";
    public string AccentColor { get; set; } = "#FFFFFF";
    public string AccentSoftColor { get; set; } = "Transparent";
    public double Progress { get; set; }
    public double ProgressPercent => Math.Round(Progress * 100, 1);
    public bool ShowProgress { get; set; }
}

public class ActionItemCard
{
    public int Index { get; set; }
    public string Action { get; set; } = "";
    public string Responsible { get; set; } = "";
    public string Deadline { get; set; } = "";
    public bool HasResponsible => !string.IsNullOrWhiteSpace(Responsible);
    public bool HasDeadline => !string.IsNullOrWhiteSpace(Deadline);
}

public class SenderItem
{
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Initial { get; set; } = "";
    public int Count { get; set; }
    public double BarFraction { get; set; }
}

public class FollowUpCard
{
    public string EmailId { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Recipient { get; set; } = "";
    public string SentDateText { get; set; } = "";
    public string Category { get; set; } = "";
    public string StatusLabel { get; set; } = "";
    public bool HasReply { get; set; }
    public string ReplyDetail { get; set; } = "";
}
