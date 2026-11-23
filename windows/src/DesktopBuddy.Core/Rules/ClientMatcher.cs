using System.Text.RegularExpressions;

namespace DesktopBuddy.Core;

// Port of ClientMatcher in Sources/DesktopBuddy/Clients.swift ("Client rules").
// Pure functions: the Swift version read AppState.shared, this one takes the clients list.

public static class ClientMatcher
{
    /// Tags items with the first enabled client whose rules match. Returns copies of the items, tagged.
    /// Always recomputed (no early return), so turning a client off clears its old tags.
    public static List<BuddyItem> Assign(IEnumerable<BuddyItem> items, string accountID, IEnumerable<ClientConfig> allClients)
    {
        var clients = allClients
            .Where(c => c.Enabled && (c.AccountIDs.Count == 0 || c.AccountIDs.Contains(accountID)))
            .ToList();
        return items
            .Select(item => item with { ClientID = clients.FirstOrDefault(c => Matches(c, item))?.Id })
            .ToList();
    }

    public static bool Matches(ClientConfig c, BuddyItem item)
    {
        switch (item.Source)
        {
            case ItemSource.mail:
                if (c.Senders.Any(s => SenderMatches(s, item))) return true;
                return c.Keywords.Any(k => ContainsWord(k, MailText(item)));
            case ItemSource.chat:
                if (item.ChatRef is { } reference
                    && c.Chats.Any(ch => ch.ChatID == reference && ch.AccountID == item.AccountID)) return true;
                if (!c.MatchChatNames) return false;
                if (ContainsWord(c.Name, item.Title)) return true;
                // Direct messages have no name; match the person you're talking to instead.
                return item.Title == "Direct message" && ContainsWord(c.Name, item.From ?? "");
            default:
                return false;
        }
    }

    /// A domain rule ("example.co.uk") matches it and its subdomains; an address rule matches exactly.
    public static bool SenderMatches(string rule, BuddyItem item)
    {
        var addr = (item.FromAddress ?? "").ToLowerInvariant();
        var r = rule.ToLowerInvariant().Trim(' ', '@');
        if (r.Length == 0) return false;
        return r.Contains('@')
            ? addr == r
            : (addr.EndsWith("@" + r, StringComparison.Ordinal) || addr.EndsWith("." + r, StringComparison.Ordinal));
    }

    /// What keywords are looked for in: subject or chat name, preview or message, and sender name.
    public static string MailText(BuddyItem item) => item.Title + " " + (item.Detail ?? "") + " " + (item.From ?? "");

    /// Case-insensitive match on word boundaries: "Hub" matches "IE Hub team" but not "GitHub".
    public static bool ContainsWord(string needle, string haystack)
    {
        var n = needle.Trim();
        if (n.Length == 0) return false;
        var pattern = @"(?<![\p{L}\p{N}])" + Regex.Escape(n) + @"(?![\p{L}\p{N}])";
        return Regex.IsMatch(haystack, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
