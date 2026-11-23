namespace DesktopBuddy.Presentation.Settings;

/// Core writes some fix labels in Title Case ("Turn On Google Chat", "Sign In Again…"). Settings writes
/// sentence case with no trailing ellipsis: "Turn on Google Chat", "Sign in again".
public static class FixText
{
    // Small words that Title Case capitalises but a sentence doesn't. Names ("Google Chat", account labels) keep their capitals.
    private static readonly HashSet<string> Small = new(StringComparer.Ordinal)
    {
        "On", "Off", "In", "Out", "Up", "Down", "Again", "Back", "To", "Now", "Over", "Using", "Anyway",
    };

    public static string Sentence(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return "";
        var text = label.Trim().TrimEnd('…', '.').TrimEnd();
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 1; i < words.Length; i++)
        {
            if (Small.Contains(words[i])) words[i] = words[i].ToLowerInvariant();
        }
        return string.Join(' ', words);
    }
}
