using CommunityToolkit.Mvvm.ComponentModel;
using DesktopBuddy.Core;

namespace DesktopBuddy.Presentation.Board;

/// One waiting item as the Board shows it. Plain data plus the two bits of UI state a row has
/// (selected, hovered). Colours are 0xRRGGBB numbers; the view turns them into brushes.
public sealed partial class BoardRow : ObservableObject
{
    public BoardRow(BuddyItem item, int index, string timeText, bool isUrgent)
    {
        Item = item;
        Index = index;
        TimeText = timeText;
        IsUrgent = isUrgent;
        Sender = string.IsNullOrWhiteSpace(item.From) ? item.Title : item.From!;
        Secondary = string.IsNullOrWhiteSpace(item.From) ? item.Detail ?? "" : item.Title;
    }

    public BuddyItem Item { get; }
    public string Id => Item.Id;
    public string OwnerId => Item.Owner;

    /// Position in the whole list, counting from 0 (the AutomationIds use it: BoardRow_0, BoardDone_0 …).
    public int Index { get; }
    public string RowAutomationId => $"BoardRow_{Index}";
    public string DoneAutomationId => $"BoardDone_{Index}";
    public string LaterAutomationId => $"BoardLater_{Index}";

    /// Segoe Fluent Icons glyph for the source icon (the colour is a view token, per source).
    public string Glyph => Item.Source.Glyph();
    public string SourceLabel => Item.Source.Label();
    // Which kind it is, for the view to pick the theme-aware glyph colour (DbSource…Brush).
    public bool IsMail => Item.Source == ItemSource.mail;
    public bool IsChat => Item.Source == ItemSource.chat;
    public bool IsMeeting => Item.Source == ItemSource.meeting;
    public bool IsTask => Item.Source == ItemSource.task;
    public bool IsReminder => Item.Source == ItemSource.reminder;

    /// First line: who it's from, or the title when there is no sender.
    public string Sender { get; }
    /// Second line: the subject, or the detail when the first line already is the title.
    public string Secondary { get; }

    /// "4m", "2h", or for a meeting "in 5 min".
    [ObservableProperty] public partial string TimeText { get; set; }
    /// A meeting that is about to start (or has): the time is drawn in the urgent colour.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNormalTime), nameof(ShowUrgentTime))]
    public partial bool IsUrgent { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowActions), nameof(ShowNormalTime), nameof(ShowUrgentTime))]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowActions), nameof(ShowNormalTime), nameof(ShowUrgentTime))]
    public partial bool IsHovered { get; set; }

    /// Later and Done show on the selected or hovered row (in place of the time).
    public bool ShowActions => IsSelected || IsHovered;
    public bool ShowNormalTime => !IsUrgent && !ShowActions;
    public bool ShowUrgentTime => IsUrgent && !ShowActions;

    /// What a screen reader says for the row (type, sender, subject, when).
    public string AccessibleName
    {
        get
        {
            var parts = new List<string> { SourceLabel, Sender };
            if (!string.IsNullOrWhiteSpace(Secondary)) parts.Add(Secondary);
            parts.Add(TimeText);
            return string.Join(", ", parts);
        }
    }

    /// Only the time text and urgency change while the list stays the same.
    internal void UpdateTime(string timeText, bool isUrgent)
    {
        TimeText = timeText;
        IsUrgent = isUrgent;
        OnPropertyChanged(nameof(AccessibleName));
    }
}
