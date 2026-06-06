namespace EmailAI.Core;

public static class SyncPeriodOptions
{
    public const string KeyAll = "0";
    public const string Key7Days = "7";
    public const string Key30Days = "30";
    public const string Key90Days = "90";
    public const string Key6Months = "180";
    public const string Key1Year = "365";

    public static readonly string[] Labels =
    [
        "Last 7 days",
        "Last 30 days",
        "Last 90 days",
        "Last 6 months",
        "Last year",
        "All emails"
    ];

    public static string DefaultKey => Key30Days;

    public static string LabelForKey(string? key) => key switch
    {
        Key7Days     => Labels[0],
        Key30Days    => Labels[1],
        Key90Days    => Labels[2],
        Key6Months   => Labels[3],
        Key1Year     => Labels[4],
        KeyAll       => Labels[5],
        _            => Labels[1]
    };

    public static string KeyForLabel(string? label) => label switch
    {
        "Last 7 days"    => Key7Days,
        "Last 30 days"   => Key30Days,
        "Last 90 days"   => Key90Days,
        "Last 6 months"  => Key6Months,
        "Last year"      => Key1Year,
        "All emails"     => KeyAll,
        _                => DefaultKey
    };

    public static DateTime? GetSinceUtc(string? key)
    {
        if (!int.TryParse(key, out var days) || days <= 0)
            return null;

        return DateTime.UtcNow.AddDays(-days);
    }

    public static SyncOptions ToOptions(string? key)
    {
        var since = GetSinceUtc(key);
        return new SyncOptions(since, LabelForKey(key));
    }
}

public record SyncOptions(DateTime? SinceUtc, string PeriodLabel);
