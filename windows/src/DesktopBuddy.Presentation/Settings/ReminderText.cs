using System.Globalization;
using DesktopBuddy.Core;

namespace DesktopBuddy.Presentation.Settings;

/// Words for a reminder's schedule. Weekdays are stored as Mac numbers (1 = Sunday ... 7 = Saturday)
/// so a config file loads on both platforms; the week is shown Monday first.
public static class ReminderText
{
    public static readonly (int Day, string Short, string Long)[] Week =
    [
        (2, "Mon", "Monday"), (3, "Tue", "Tuesday"), (4, "Wed", "Wednesday"), (5, "Thu", "Thursday"),
        (6, "Fri", "Friday"), (7, "Sat", "Saturday"), (1, "Sun", "Sunday"),
    ];

    public static string Days(HashSet<int> days)
    {
        if (days.SetEquals([2, 3, 4, 5, 6])) return "Weekdays";
        if (days.SetEquals([1, 7])) return "Weekends";
        if (days.Count == 7) return "Every day";
        return string.Join(", ", Week.Where(w => days.Contains(w.Day)).Select(w => w.Short));
    }

    /// "Every 30 minutes", "Every hour", "Every 2 hours".
    public static string Every(int minutes) => minutes switch
    {
        60 => "Every hour",
        > 60 when minutes % 60 == 0 => $"Every {minutes / 60} hours",
        1 => "Every minute",
        _ => $"Every {minutes} minutes",
    };

    /// "Every 30 minutes, 9:00 AM to 6:00 PM, weekdays".
    public static string Summary(Reminder r, CultureInfo? culture = null)
    {
        var days = Days(r.Weekdays);
        if (days is "Weekdays" or "Weekends" or "Every day") days = char.ToLowerInvariant(days[0]) + days[1..];
        return $"{Every(r.EveryMinutes)}, {TimeText.Hour(r.StartHour, culture)} to {TimeText.Hour(r.EndHour, culture)}, {days}";
    }
}
