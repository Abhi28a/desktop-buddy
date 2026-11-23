namespace DesktopBuddy.Core;

// Port of the pure checks in TokenField (ClientSettings.swift) and shortAgo (MenuBarPopover.swift):
// what a typed sender or keyword must look like, and the compact "4m" age text.

public static class TokenRules
{
    /// Senders must be a domain (has a dot) or a full address. Returns the message to show, or null if fine.
    public static string? SenderProblem(string s)
    {
        var v = s.Trim(' ', '@');
        if (v.Contains(' ')) return $"“{s}” has a space. Use a domain like gatehouse.co.uk or an address like jane@gatehouse.co.uk.";
        if (v.Contains('@'))
        {
            var parts = v.Split('@', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 2 && parts[1].Contains('.') ? null : $"“{s}” isn't a complete email address.";
        }
        return v.Contains('.') ? null : $"“{s}” isn't a domain. Add the ending, e.g. {v}.co.uk or {v}.com.";
    }

    public static string? KeywordProblem(string s) =>
        s.Length < 3 ? $"“{s}” is too short to be useful. Use at least 3 letters." : null;

    /// Accepts comma-separated input; trims, drops empties and case-insensitive duplicates.
    public static List<string> Merge(string input, IEnumerable<string> tokens)
    {
        var output = tokens.ToList();
        foreach (var t in input.Split(',').Select(x => x.Trim(' ', '\t')).Where(x => x.Length > 0))
        {
            if (!output.Any(o => string.Equals(o, t, StringComparison.OrdinalIgnoreCase))) output.Add(t);
        }
        return output;
    }

    /// "now", "4m", "2h", "3d": compact like Notification Center.
    public static string ShortAgo(DateTimeOffset date, DateTimeOffset now)
    {
        var s = (long)Math.Max(0, (now - date).TotalSeconds);
        if (s < 60) return "now";
        if (s < 3600) return $"{s / 60}m";
        if (s < 86_400) return $"{s / 3600}h";
        return $"{s / 86_400}d";
    }
}
