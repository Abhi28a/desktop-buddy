namespace DesktopBuddy.Core;

// Port of the pure part of Sounds in Sources/DesktopBuddy/Sounds.swift (which sound a batch plays).
// Playing the sound is the app's job.

public static class SoundRules
{
    /// Most urgent first: when several things arrive together, the most urgent one's sound plays.
    public static readonly ItemSource[] Priority =
        [ItemSource.meeting, ItemSource.chat, ItemSource.mail, ItemSource.task, ItemSource.reminder];

    /// The type whose sound a batch of new items plays.
    public static ItemSource? LeadSource(IEnumerable<BuddyItem> items)
    {
        var present = items.Select(i => i.Source).ToHashSet();
        foreach (var s in Priority) if (present.Contains(s)) return s;
        return null;
    }
}
