using DesktopBuddy.Core;

namespace DesktopBuddy.Presentation.Board;

/// A rectangle in physical pixels (left/top inclusive, right/bottom exclusive).
public readonly record struct PxRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public int CenterX => (Left + Right) / 2;
    public int CenterY => (Top + Bottom) / 2;
}

/// Where the Board goes on screen. Pure maths in physical pixels, so every taskbar edge and the
/// Rail's six places can be tested without a screen.
public static class BoardPlacement
{
    public const double BoardWidth = 404;
    public const double MaxBoardHeight = 560;
    /// Space between the Board and what it opens from, and between the Board and the screen edge (DIPs).
    public const double Gap = 12;

    /// The taskbar's edge, from the monitor's full bounds and its work area (the part the taskbar
    /// leaves). With an auto-hidden taskbar they're equal, so the edge nearest the icon is used.
    public static ScreenEdge EdgeOf(PxRect icon, PxRect work, PxRect outer)
    {
        if (work.Bottom < outer.Bottom) return ScreenEdge.Bottom;
        if (work.Top > outer.Top) return ScreenEdge.Top;
        if (work.Left > outer.Left) return ScreenEdge.Left;
        if (work.Right < outer.Right) return ScreenEdge.Right;
        var distances = new (ScreenEdge Edge, int D)[]
        {
            (ScreenEdge.Bottom, outer.Bottom - icon.CenterY),
            (ScreenEdge.Top, icon.CenterY - outer.Top),
            (ScreenEdge.Left, icon.CenterX - outer.Left),
            (ScreenEdge.Right, outer.Right - icon.CenterX),
        };
        return distances.MinBy(d => d.D).Edge;
    }

    /// Top-left of the Board when it opens from the tray icon: above the icon when the taskbar is at
    /// the bottom, below it at the top, beside it at the sides. `w`/`h` and `gap` are in pixels.
    public static (int X, int Y) FromTray(PxRect icon, PxRect work, PxRect outer, int w, int h, int gap)
    {
        int x, y;
        switch (EdgeOf(icon, work, outer))
        {
            case ScreenEdge.Top:
                x = icon.CenterX - w / 2;
                y = work.Top + gap;
                break;
            case ScreenEdge.Left:
                x = work.Left + gap;
                y = icon.CenterY - h / 2;
                break;
            case ScreenEdge.Right:
                x = work.Right - w - gap;
                y = icon.CenterY - h / 2;
                break;
            default:
                x = icon.CenterX - w / 2;
                y = work.Bottom - h - gap;
                break;
        }
        return Clamp(x, y, work, w, h, gap);
    }

    /// Top-left of the Board when it opens beside the Rail: above a bottom Rail, to the right of a
    /// left Rail, to the left of a right Rail, lined up with the Rail's near end.
    public static (int X, int Y) FromRail(PxRect rail, DockSpot spot, PxRect work, int w, int h, int gap)
    {
        int x, y;
        switch (spot.Edge())
        {
            case DockEdge.left:
                x = rail.Right + gap;
                y = spot == DockSpot.leftTop ? rail.Top : rail.Bottom - h;
                break;
            case DockEdge.right:
                x = rail.Left - gap - w;
                y = spot == DockSpot.rightTop ? rail.Top : rail.Bottom - h;
                break;
            default:
                x = spot == DockSpot.bottomLeft ? rail.Left : rail.Right - w;
                y = rail.Top - gap - h;
                break;
        }
        return Clamp(x, y, work, w, h, gap);
    }

    /// Keeps the Board inside the work area, `gap` from its edges.
    public static (int X, int Y) Clamp(int x, int y, PxRect work, int w, int h, int gap)
    {
        var cx = Math.Clamp(x, work.Left + gap, Math.Max(work.Left + gap, work.Right - w - gap));
        var cy = Math.Clamp(y, work.Top + gap, Math.Max(work.Top + gap, work.Bottom - h - gap));
        return (cx, cy);
    }

    /// The tallest the Board may be: 560 DIPs, or less when the work area is small.
    public static double MaxHeightDips(double workHeightDips) =>
        Math.Max(200, Math.Min(MaxBoardHeight, workHeightDips - 2 * Gap));
}

/// The four sides of a screen (Core DockEdge has no top).
public enum ScreenEdge { Bottom, Top, Left, Right }
