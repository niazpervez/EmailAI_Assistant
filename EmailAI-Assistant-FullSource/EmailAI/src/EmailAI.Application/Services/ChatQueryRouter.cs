namespace EmailAI.Application.Services;

internal enum ChatQueryIntent
{
    General,
    TodaySummary,
    WeekSummary,
    UnreadSummary
}

internal static class ChatQueryRouter
{
    public static ChatQueryIntent DetectIntent(string message)
    {
        var q = message.ToLowerInvariant();

        if (ContainsAny(q, "today", "today's", "todays") &&
            ContainsAny(q, "summar", "overview", "recap", "digest", "emails", "mail", "inbox"))
            return ChatQueryIntent.TodaySummary;

        if (ContainsAny(q, "this week", "past week", "last 7 days", "weekly") &&
            ContainsAny(q, "summar", "overview", "recap", "digest", "emails", "mail"))
            return ChatQueryIntent.WeekSummary;

        if (ContainsAny(q, "unread") &&
            ContainsAny(q, "summar", "overview", "list", "show", "what", "any"))
            return ChatQueryIntent.UnreadSummary;

        return ChatQueryIntent.General;
    }

    private static bool ContainsAny(string text, params string[] terms)
    {
        foreach (var term in terms)
        {
            if (text.Contains(term, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
