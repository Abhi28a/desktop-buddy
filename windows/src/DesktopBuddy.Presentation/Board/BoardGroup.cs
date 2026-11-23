using DesktopBuddy.Core;

namespace DesktopBuddy.Presentation.Board;

/// One owner's rows (a client, an account, or your reminders). The list is the group: the view
/// gives it to a grouped ListView as it is.
public sealed class BoardGroup : List<BoardRow>
{
    public BoardGroup(string ownerId, string name, LogoRef? logo, AccountTint tint, bool isReminders, bool isFirst, bool showHeader, int index)
    {
        OwnerId = ownerId;
        Name = name;
        Logo = logo;
        Tint = tint;
        IsReminders = isReminders;
        IsFirst = isFirst;
        ShowHeader = showHeader;
        Index = index;
    }

    public string OwnerId { get; }
    public string Name { get; }
    public LogoRef? Logo { get; }
    public AccountTint Tint { get; }
    public bool IsReminders { get; }
    /// The first group sits closer to the header above it.
    public bool IsFirst { get; }
    /// Hidden when the Board is already filtered to this owner (the header says who it is).
    public bool ShowHeader { get; }
    public int Index { get; }

    public string ClearAutomationId => $"BoardClearGroup_{Index}";
    public string ClearName => $"Clear {Name}";
}
