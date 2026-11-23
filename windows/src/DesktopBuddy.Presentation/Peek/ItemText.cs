using DesktopBuddy.Core;

namespace DesktopBuddy.Presentation.Peek;

/// The words for one item on the Peek (and anywhere else that shows a single item). Port of the old
/// OverlayUi wording helpers (Overlay.swift: serviceName, headline, subhead).
public static class ItemText
{
    public static string ServiceName(AppState state, BuddyItem item)
    {
        var provider = item.AccountID is { } id ? state.Account(id)?.Provider : null;
        return item.Source switch
        {
            ItemSource.mail => provider == Provider.microsoft ? "Outlook" : "Gmail",
            ItemSource.chat => provider == Provider.microsoft ? "Teams" : "Google Chat",
            ItemSource.meeting => "Calendar",
            ItemSource.task => provider == Provider.microsoft ? "To Do" : "Tasks",
            _ => "Reminder",
        };
    }

    /// The bold first line: who it is from (mail and chat), or what it is.
    public static string Headline(BuddyItem item) =>
        item.Source is ItemSource.mail or ItemSource.chat ? (item.From ?? item.Title) : item.Title;

    /// The second line: subject, chat space, organiser or task list. Null when there is nothing to add.
    public static string? Subhead(BuddyItem item) => item.Source switch
    {
        ItemSource.mail => item.Title,
        ItemSource.chat => item.Title == "Direct message" ? null : item.Title,
        ItemSource.meeting or ItemSource.task => item.From,
        _ => null,
    };

    /// "Gatehouse · Gmail". Your own reminders are just "Reminders".
    public static string Meta(string ownerName, string service, ItemSource source) =>
        source == ItemSource.reminder ? ownerName : $"{ownerName} · {service}";

    /// When: "2m", "3h" for things that arrived; "in 5 min", "now" or "2 min ago" for meetings,
    /// whose date is when they start.
    public static string When(BuddyItem item, DateTimeOffset now)
    {
        if (item.Source != ItemSource.meeting) return TokenRules.ShortAgo(item.Date, now);
        var minutes = (int)Math.Round((item.Date - now).TotalMinutes);
        return minutes switch
        {
            0 => "now",
            > 0 => $"in {minutes} min",
            _ => $"{-minutes} min ago",
        };
    }

    /// A line only when it adds something the lines above don't already say.
    public static string? Detail(BuddyItem item, string? subhead)
    {
        var detail = item.Detail?.Trim();
        if (string.IsNullOrEmpty(detail)) return null;
        return string.Equals(detail, subhead, StringComparison.Ordinal) ? null : detail;
    }
}
