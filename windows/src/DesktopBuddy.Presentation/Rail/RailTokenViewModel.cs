using DesktopBuddy.Core;

namespace DesktopBuddy.Presentation.Rail;

/// One token on the Rail: everything the view needs, no WinUI types.
public sealed record RailTokenViewModel(
    string OwnerId,
    string Name,
    int Count,
    LogoRef? Logo,
    AccountTint Tint,
    bool IsReminders,
    double Urgency,
    int? MeetingMinutes)
{
    /// A meeting is coming: the token shows the countdown ring.
    public bool HasUrgency => !double.IsNaN(Urgency);

    /// The tooltip: "RiskHawk · 1 waiting". A token with a meeting says so in words too, because the ring
    /// alone is not enough.
    public string Tooltip =>
        MeetingMinutes is { } m
            ? $"{RailRules.Label(Name, Count)} · meeting {(m == 0 ? "starting now" : $"in {m} min")}"
            : RailRules.Label(Name, Count);

    public string AutomationId => "RailToken_" + OwnerId;

    public string AutomationName => RailRules.SpokenName(Name, Count, MeetingMinutes);
}
