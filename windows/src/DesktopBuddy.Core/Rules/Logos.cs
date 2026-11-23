using System.Globalization;

namespace DesktopBuddy.Core;

// Port of the logic in `enum Logos` and the owner-logo lookup in Sources/DesktopBuddy/Clients.swift.
// Images are the app's job: this only decides which logo an owner uses.

/// Which logo to show: a picked file, or one of the bundled logos by key (Resources/Logos/&lt;key&gt;.png).
public readonly record struct LogoRef(string? Path, string? BundledKey);

public static class Logos
{
    /// Bundled logos, matched by account/client name or email domain.
    public static readonly string[] BundledKeys = ["dynamatix", "iehub", "riskhawk", "gatehouse"];

    public static string? GuessKey(string name, string email = "")
    {
        var hay = (name + " " + email).ToLowerInvariant().Replace(" ", "");
        return BundledKeys.FirstOrDefault(k => hay.Contains(k, StringComparison.Ordinal));
    }

    /// A picked logo file if it still exists, otherwise a bundled one matched by name; null = no logo (use a monogram).
    public static LogoRef? Resolve(string? path, string name, string email = "")
    {
        if (path is not null && File.Exists(path)) return new LogoRef(path, null);
        var key = GuessKey(name, email);
        return key is null ? null : new LogoRef(null, key);
    }

    /// First letters of the first two words ("Acme Ltd" gives "AL"), shown when there is no logo.
    public static string Monogram(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2);
        return string.Concat(words.Select(w => StringInfo.GetNextTextElement(w))).ToUpperInvariant();
    }
}
