using DesktopBuddy.Core;

namespace DesktopBuddy.App.Views.Rail;

/// Where the Rail is on screen right now, so the Board can open beside it (UI-UX-PLAN §7 hand-over).
/// The Rail (T4) keeps this up to date; the Board (T3) only reads it.
internal static class RailPlacement
{
    /// The Rail window's bounds in physical pixels, or null while the Rail is hidden.
    public static Windows.Graphics.RectInt32? Bounds { get; set; }

    /// The edge and corner the Rail is pinned to.
    public static DockSpot Spot { get; set; } = DockSpotInfo.Standard;
}
