using DesktopBuddy.Core;

namespace DesktopBuddy.Presentation.Rail;

/// The pure rules of the Rail: how close a meeting is, when the Rail hides, how big it is.
/// (No drawing here, so they are unit-tested like Core.)
public static class RailRules
{
    /// A token is 40 DIPs at scale 1 (DESIGN.md section 4).
    public const double BaseTokenSize = 40;
    /// Space between tokens, and between the tokens and the pill's ends.
    public const double Gap = 12;
    /// Width of a pill that shows names under the tokens.
    public const double NamedWidth = 80;

    /// Once a meeting is about to start (or has started) the ring never shrinks below this,
    /// so the sign that something is urgent does not vanish while the item is still there.
    public const double MinVisibleUrgency = 0.04;

    /// Token diameter for the "size" setting, clamped like the rest of the dock.
    public static double TokenSize(double scale) =>
        Math.Round(BaseTokenSize * Math.Clamp(scale, DockLayout.ScaleMin, DockLayout.ScaleMax), MidpointRounding.AwayFromZero);

    /// How much of the lead time is left before a meeting starts: 1 = the whole lead time (just appeared),
    /// 0 = starting now or already started. A meeting item's `Date` is its START time and the poller only
    /// lists meetings from 10 minutes ago up to `AppConfig.MeetingLeadMinutes` ahead, so this is
    /// (start - now) / lead, clamped to 0..1. (The item's `Detail` is only text such as "Starts in 5 min".)
    public static double MeetingFraction(DateTimeOffset start, DateTimeOffset now, int leadMinutes)
    {
        var lead = Math.Max(1, leadMinutes);
        return Math.Clamp((start - now).TotalMinutes / lead, 0, 1);
    }

    /// The ring to draw: the fraction, but never below MinVisibleUrgency.
    public static double MeetingUrgency(DateTimeOffset start, DateTimeOffset now, int leadMinutes) =>
        Math.Max(MinVisibleUrgency, MeetingFraction(start, now, leadMinutes));

    /// The ring for one owner: the meeting that starts first. NaN when the owner has no meeting.
    public static (double Urgency, int? Minutes) NextMeeting(IEnumerable<BuddyItem> items, DateTimeOffset now, int leadMinutes)
    {
        var next = items.Where(i => i.Source == ItemSource.meeting).OrderBy(i => i.Date).FirstOrDefault();
        if (next is null) return (double.NaN, null);
        var minutes = (int)Math.Max(0, Math.Round((next.Date - now).TotalMinutes));
        // Rounded to 1%, so a ring that is only getting a hair shorter doesn't redraw the Rail every few seconds.
        return (Math.Round(MeetingUrgency(next.Date, now, leadMinutes), 2), minutes);
    }

    /// "RiskHawk · 1 waiting": the tooltip, and what names show.
    public static string Label(string name, int count) => $"{name} · {count} waiting";

    /// What Narrator reads for a token.
    public static string SpokenName(string name, int count, int? meetingMinutes) =>
        meetingMinutes is { } m
            ? $"{name}, {count} waiting, meeting starts {(m == 0 ? "now" : $"in {m} min")}"
            : $"{name}, {count} waiting";

    /// Why the Rail is out of sight (None = it shows).
    public static RailHiddenReason HiddenReason(int waiting, bool pausedNow, bool screenTaken) =>
        pausedNow ? RailHiddenReason.Paused
        : screenTaken ? RailHiddenReason.ScreenTaken
        : waiting == 0 ? RailHiddenReason.Empty
        : RailHiddenReason.None;

    /// Clear space the pill keeps between a count notch (or the meeting ring) and its own edge.
    public const double Clearance = 6;
    /// Room above a token for the meeting ring (it hangs out 4 DIPs) plus the clearance.
    public const double RingRoom = 4 + Clearance;

    /// How far the count notch hangs out past a token: 20% of its size to the right, 10% down.
    public static double NotchOverhangX(double tokenSize) => Math.Ceiling(tokenSize * 0.2);
    public static double NotchOverhangY(double tokenSize) => Math.Ceiling(tokenSize * 0.1);

    /// The pill's thickness (width for a side edge, height for the bottom), in DIPs: 60 at normal size.
    /// Every notch sits fully inside it with at least `Clearance` to spare. With names under the tokens the
    /// tokens are centred, so both sides need the room, and the pill is never narrower than NamedWidth.
    public static double PillThickness(double tokenSize, bool compact, bool vertical)
    {
        if (!compact) return Math.Max(NamedWidth, tokenSize + 2 * (NotchOverhangX(tokenSize) + Clearance));
        return vertical
            ? Clearance + tokenSize + NotchOverhangX(tokenSize) + Clearance            // token on the left, notch on the right
            : RingRoom + tokenSize + NotchOverhangY(tokenSize) + Clearance;            // ring above, notch below
    }

    /// Space before the first token and after the last one along the pill. The end is bigger, because the
    /// last token's notch hangs out towards the pill's rounded end.
    public static (double Start, double End) EndPads(double tokenSize, bool compact, bool vertical)
    {
        if (!compact) return vertical ? (14, 24) : (22, 26);
        return (Gap, Gap + Math.Ceiling(tokenSize * (vertical ? 0.25 : 0.3)));
    }

    /// Space between the pill's edge and the token across the pill (a token is not centred: its notch hangs
    /// out on one side). NaN = centre it (names mode).
    public static double CrossPad(double tokenSize, bool compact, bool vertical) =>
        !compact ? double.NaN : vertical ? Clearance : RingRoom;
}

public enum RailHiddenReason { None, Empty, Paused, ScreenTaken }
