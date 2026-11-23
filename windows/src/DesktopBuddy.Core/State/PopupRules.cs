namespace DesktopBuddy.Core;

// Port of the pure rules of GlimpsePlacement / GlimpseController in Sources/DesktopBuddy/MenuBarGlimpse.swift:
// when the top pop-up appears, how long it stays. (Its screen position is in Dock/DockLayout.cs.)

public static class PopupRules
{
    /// The pop-up shows for 10 seconds.
    public const double ShowSeconds = 10;

    /// After reading, a moment to move away before it tucks itself in.
    public const double MinimumAfterHover = 3;

    /// Items that weren't there before, in the order given (newest first).
    public static List<BuddyItem> Arrivals(ISet<string> previous, IEnumerable<BuddyItem> now) =>
        now.Where(i => !previous.Contains(i.Id)).ToList();

    public static bool ShouldShow(bool paused, bool quiet) => !paused && !quiet;

    public static double TimeLeft(DateTimeOffset deadline, DateTimeOffset now) => Math.Max(0, (deadline - now).TotalSeconds);
}
