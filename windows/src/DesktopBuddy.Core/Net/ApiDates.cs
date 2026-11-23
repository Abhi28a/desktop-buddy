using System.Globalization;
using System.Text.RegularExpressions;

namespace DesktopBuddy.Core;

// Port of parseDate / isoPlain (GoogleSources.swift) and graphDate (MicrosoftSources.swift).

public static class ApiDates
{
    // ISO 8601 with a time zone designator ("Z" or "+01:00"), with or without fractional seconds.
    private static readonly Regex HasZone = new(@"(Z|[+-]\d{2}:\d{2})$", RegexOptions.Compiled);

    private static readonly string[] IsoFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
    ];

    /// "2026-10-21T09:15:23Z" in UTC, whole seconds: how the APIs want times in queries and filters.
    public static string IsoPlain(DateTimeOffset d) =>
        d.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// Parses an ISO 8601 timestamp (fractional seconds optional, offset required). Null when it isn't one.
    public static DateTimeOffset? ParseDate(string? s)
    {
        // A timestamp without a zone isn't one (Swift's ISO8601DateFormatter rejects it).
        if (s is null || !HasZone.IsMatch(s)) return null;
        return DateTimeOffset.TryParseExact(s, IsoFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
    }

    /// Graph returns UTC times without a "Z" when asked for UTC; normalise before parsing.
    public static DateTimeOffset? GraphDate(string? s)
    {
        if (s is null) return null;
        if (!s.EndsWith('Z') && !s.Contains('+')) s += "Z";
        var dot = s.IndexOf('.');
        var z = s.LastIndexOf('Z');
        if (dot >= 0 && z >= 0 && z - dot > 4)
            s = s[..(dot + 4)] + "Z";      // trim 7-digit fractions to milliseconds
        return ParseDate(s);
    }

    /// "yyyy-MM-dd" in the PC's own time zone (the Mac's DateFormatter default).
    public static string Day(DateTimeOffset d) =>
        d.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// Midnight at the start of the local day containing `d`.
    public static DateTime StartOfLocalDay(DateTimeOffset d) => d.ToLocalTime().Date;

    /// Midnight at the end of today (start of tomorrow), local time.
    public static DateTimeOffset EndOfToday(DateTimeOffset now) =>
        new(StartOfLocalDay(now).AddDays(1));   // an unspecified DateTime is read as local time

    /// "Starts in 5 min" / "Starting now" / "Started 3 min ago" (whole minutes, truncated toward zero like Swift's Int()).
    public static string WhenText(DateTimeOffset start, DateTimeOffset now)
    {
        var mins = (int)((start - now).TotalSeconds / 60);
        return mins > 0 ? $"Starts in {mins} min" : (mins == 0 ? "Starting now" : $"Started {-mins} min ago");
    }
}
