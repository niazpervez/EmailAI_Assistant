using EmailAI.Core.Entities;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EmailAI.Core;

public static class SentFollowUpHelper
{
    private static readonly string[] FollowUpKeywords =
    [
        "follow up", "follow-up", "following up", "reminder", "gentle reminder",
        "checking in", "check in", "any update", "status update", "awaiting your",
        "waiting for your", "please respond", "please reply", "kindly respond",
        "let me know", "circling back", "bump", "ping", "just a reminder"
    ];

    public static bool LooksLikeFollowUp(Email email)
    {
        if (!email.FolderName.Equals("Sent", StringComparison.OrdinalIgnoreCase))
            return false;

        var subject = email.Subject.Trim();
        if (subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
            || subject.StartsWith("Fwd:", StringComparison.OrdinalIgnoreCase))
            return true;

        var text = $"{email.Subject} {email.BodyText}".ToLowerInvariant();
        return FollowUpKeywords.Any(text.Contains);
    }

    public static string DetectCategory(string subject, string body)
    {
        var text = $"{subject} {body}".ToLowerInvariant();
        if (text.Contains("reminder")) return "Reminder";
        if (text.Contains("follow")) return "Follow-up";
        if (text.Contains("ping") || text.Contains("checking in") || text.Contains("check in"))
            return "Check-in";
        return "Sent mail";
    }

    public static string NormalizeSubject(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return "";
        var s = subject.Trim();
        while (true)
        {
            var next = Regex.Replace(s, @"^(re|fwd|fw):\s*", "", RegexOptions.IgnoreCase).Trim();
            if (next.Equals(s, StringComparison.OrdinalIgnoreCase)) break;
            s = next;
        }
        return s;
    }

    public static IReadOnlyList<string> ParseRecipientEmails(string? recipientsJson)
    {
        if (string.IsNullOrWhiteSpace(recipientsJson)) return [];

        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(recipientsJson);
            return list?.Where(e => e.Contains('@')).ToList() ?? [];
        }
        catch
        {
            return recipientsJson.Contains('@')
                ? [recipientsJson.Trim()]
                : [];
        }
    }

    public static string FormatRecipientDisplay(string? recipientsJson)
    {
        var emails = ParseRecipientEmails(recipientsJson);
        if (emails.Count == 0) return "Unknown recipient";
        if (emails.Count == 1) return emails[0];
        return $"{emails[0]} +{emails.Count - 1}";
    }

    public static string BuildAiContext(IEnumerable<DTOs.SentFollowUpItemDto> followUps)
    {
        var items = followUps.ToList();
        if (items.Count == 0) return "";

        var lines = items.Select(f =>
            f.HasReply
                ? $"- SENT (replied): \"{f.Subject}\" to {f.Recipient} on {f.SentDate:g} — reply from {f.ReplySender} on {f.ReplyDate:g}"
                : $"- SENT (awaiting reply): \"{f.Subject}\" to {f.Recipient} on {f.SentDate:g} — no response yet");

        return """
            Sent follow-ups / reminders (track whether recipients replied):
            """ + "\n" + string.Join("\n", lines);
    }
}
