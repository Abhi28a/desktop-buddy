namespace DesktopBuddy.Core;

// Port of MailFilter in Sources/DesktopBuddy/Clients.swift ("Muted mail and chats").

/// An account's mute rules: mail from these senders or with these words, and chats picked or
/// with these words, never pop up.
public static class MailFilter
{
    public static bool IsMuted(BuddyItem item, AccountConfig account)
    {
        switch (item.Source)
        {
            case ItemSource.mail:
                return account.MutedSenders.Any(s => ClientMatcher.SenderMatches(s, item))
                    || account.MutedKeywords.Any(k => ClientMatcher.ContainsWord(k, ClientMatcher.MailText(item)));
            case ItemSource.chat:
                if (item.ChatRef is { } reference && account.MutedChats.Any(c => c.ChatID == reference)) return true;
                return account.MutedKeywords.Any(k => ClientMatcher.ContainsWord(k, ClientMatcher.MailText(item)));
            default:
                return false;
        }
    }

    /// Drops muted mail or chats from a fetch, including from the live IDs, so muted ones already on
    /// screen clear too. Returns the IDs dropped.
    public static HashSet<string> Apply(FetchResult result, AccountConfig account)
    {
        if (result.Source != ItemSource.mail && result.Source != ItemSource.chat) return [];
        var muted = result.Items.Where(i => IsMuted(i, account)).Select(i => i.Id).ToHashSet();
        if (muted.Count == 0) return [];
        result.Items.RemoveAll(i => muted.Contains(i.Id));
        result.LiveIDs.ExceptWith(muted);
        return muted;
    }
}
