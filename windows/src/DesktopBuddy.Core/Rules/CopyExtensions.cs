namespace DesktopBuddy.Core;

// Swift's structs copy on assignment; these records hold lists, so a plain copy would share them.
// AppState hands out and stores these copies so callers can edit a draft and then call Update.

public static class CopyExtensions
{
    public static ChatRef Clone(this ChatRef c) => new()
    {
        AccountID = c.AccountID, ChatID = c.ChatID, Name = c.Name, Kind = c.Kind, LastActive = c.LastActive,
    };

    public static AccountConfig Clone(this AccountConfig a) => a with
    {
        MutedSendersList = a.MutedSendersList?.ToList(),
        MutedKeywordsList = a.MutedKeywordsList?.ToList(),
        OnlyLabelsList = a.OnlyLabelsList?.Select(l => l with { }).ToList(),
        MutedChatsList = a.MutedChatsList?.Select(c => c.Clone()).ToList(),
    };

    public static ClientConfig Clone(this ClientConfig c) => c with
    {
        Senders = c.Senders.ToList(),
        Keywords = c.Keywords.ToList(),
        Chats = c.Chats.Select(x => x.Clone()).ToList(),
        AccountIDs = c.AccountIDs.ToList(),
    };

    /// Same list contents (both null counts as equal; null and empty do not, like Swift's optional arrays).
    internal static bool SameList<T>(List<T>? a, List<T>? b)
    {
        if (a is null || b is null) return a is null && b is null;
        return a.SequenceEqual(b);
    }
}
