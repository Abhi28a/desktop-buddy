using DesktopBuddy.Core;

namespace DesktopBuddy.Presentation.Peek;

/// Everything the Peek card shows, as plain data (no WinUI), so the window, the snapshots and the tests
/// all read the same thing.
public sealed record PeekCard(
    string OwnerId,
    string OwnerName,
    string Meta,
    string TimeText,
    string Sender,
    string? Subject,
    string? Detail,
    int OwnerCount,
    LogoRef? Logo,
    AccountTint Tint,
    bool IsReminders,
    int MoreCount,
    bool CanOpen,
    double Progress,
    bool Hovering)
{
    public string MoreText => MoreCount == 1 ? "1 more waiting" : $"{MoreCount} more waiting";

    /// What Narrator reads when the Peek appears.
    public string SpokenName =>
        $"New from {OwnerName}: {Sender}" + (string.IsNullOrEmpty(Subject) ? "" : $", {Subject}");

    public static PeekCard From(AppState state, BuddyItem item, int more, double progress = 1, bool hovering = false)
    {
        var owner = item.Owner;
        var ownerName = state.OwnerName(owner);
        var subject = ItemText.Subhead(item);
        return new PeekCard(
            owner, ownerName,
            ItemText.Meta(ownerName, ItemText.ServiceName(state, item), item.Source),
            ItemText.When(item, state.Now()),
            ItemText.Headline(item), subject, ItemText.Detail(item, subject),
            Math.Max(1, state.Items.Count(i => i.Owner == owner)),
            state.OwnerLogo(owner), state.OwnerTint(owner), owner == BuddyItem.ReminderOwner,
            more, !string.IsNullOrWhiteSpace(item.Url), progress, hovering);
    }
}
