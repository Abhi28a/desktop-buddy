using System.Globalization;

namespace DesktopBuddy.Presentation.Settings;

/// Small wording helpers shared by the Settings pages.
public static class TimeText
{
    /// "just now", "5 minutes ago", "2 hours ago".
    public static string Ago(DateTimeOffset date, DateTimeOffset now)
    {
        var seconds = (long)Math.Max(0, (now - date).TotalSeconds);
        if (seconds < 45) return "just now";
        var minutes = Math.Max(1, (seconds + 30) / 60);
        if (minutes < 60) return minutes == 1 ? "a minute ago" : $"{minutes} minutes ago";
        var hours = minutes / 60;
        if (hours < 24) return hours == 1 ? "an hour ago" : $"{hours} hours ago";
        var days = hours / 24;
        return days == 1 ? "yesterday" : $"{days} days ago";
    }

    /// An hour as the PC shows it ("9:00 AM" or "09:00").
    public static string Hour(int hour, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var time = new DateTime(2000, 1, 1, ((hour % 24) + 24) % 24, 0, 0);
        return culture.DateTimeFormat.ShortTimePattern.Contains("tt", StringComparison.Ordinal)
            ? time.ToString("h:mm tt", culture)
            : time.ToString("HH:mm", culture);
    }

    /// A time of day the way the PC writes it ("3:00 PM").
    public static string Clock(DateTimeOffset time, CultureInfo? culture = null) =>
        time.ToLocalTime().ToString("t", culture ?? CultureInfo.CurrentCulture);

    public static string Plural(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";

    /// "A, B and C".
    public static string Join(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
    };
}
