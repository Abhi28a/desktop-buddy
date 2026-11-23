using System.Globalization;

namespace DesktopBuddy.Core;

// Port of AccountConfig.suggestedLabel / uniqueLabel (SettingsWindow.swift)
// and genericKeywords (ClientHealth.swift).

public static class AccountNaming
{
    private static readonly Dictionary<string, string> KnownDomains = new()
    {
        ["dynamatix.co.uk"] = "Dynamatix",
        ["dynamatix.com"] = "Dynamatix",
        ["iehub.org"] = "IE Hub",
    };

    private static readonly HashSet<string> PersonalDomains =
        ["gmail.com", "googlemail.com", "outlook.com", "hotmail.com", "live.com", "icloud.com"];

    private static readonly HashSet<string> GenericDomainParts =
        ["co", "com", "org", "net", "io", "ac", "gov", "edu", "uk", "us", "in", "au", "ltd", "plc", "onmicrosoft"];

    /// Very common words that would sweep in unrelated mail when used as a client keyword.
    public static readonly HashSet<string> GenericKeywords =
    [
        "team", "the", "and", "update", "meeting", "hi", "hello", "re", "fw", "fwd",
        "please", "thanks", "urgent", "info", "request", "report", "project", "test",
    ];

    /// A friendly name from the email's domain: "jo@dynamatix.co.uk" gives "Dynamatix", "jo@acme-ltd.com" gives "Acme Ltd".
    public static string SuggestedLabel(string email)
    {
        var domain = email.Split('@', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.ToLowerInvariant() ?? "";
        if (KnownDomains.TryGetValue(domain, out var known)) return known;
        if (PersonalDomains.Contains(domain)) return "Personal";
        var parts = domain.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var name = parts.FirstOrDefault(p => !GenericDomainParts.Contains(p)) ?? parts.FirstOrDefault();
        if (string.IsNullOrEmpty(name)) return "Account";
        return string.Join(" ", name.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries).Select(Capitalise));
    }

    /// "Dynamatix" becomes "Dynamatix 2" when that name is already used.
    public static string UniqueLabel(string baseName, ISet<string> taken)
    {
        if (!taken.Contains(baseName)) return baseName;
        var n = 2;
        while (taken.Contains($"{baseName} {n}")) n++;
        return $"{baseName} {n}";
    }

    private static string Capitalise(string word)
    {
        var first = StringInfo.GetNextTextElement(word);
        return first.ToUpperInvariant() + word[first.Length..];
    }
}
