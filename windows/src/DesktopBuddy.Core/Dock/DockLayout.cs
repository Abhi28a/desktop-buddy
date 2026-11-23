namespace DesktopBuddy.Core;

// Port of the pure maths in Sources/DesktopBuddy/Dock.swift (DockSpot helpers and DockPlacement),
// OverlayPlacement in Overlay.swift, and GlimpsePlacement.frame in MenuBarGlimpse.swift. No drawing.
//
// COORDINATES: the Mac works with y pointing up; Windows screens have y pointing down, so these
// rectangles are top-left based (Y grows downwards) and the maths is mirrored to match. Units are
// whatever the caller uses (device-independent pixels is the natural choice).

public readonly record struct DockPoint(double X, double Y);

public readonly record struct DockSize(double Width, double Height);

public readonly record struct DockRect(double X, double Y, double Width, double Height)
{
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double MidX => X + Width / 2;
    public double MidY => Y + Height / 2;
    public DockSize Size => new(Width, Height);

    /// The other rectangle lies completely inside this one.
    public bool Contains(DockRect other) =>
        other.Left >= Left && other.Right <= Right && other.Top >= Top && other.Bottom <= Bottom;

    /// The two overlap by more than an edge (rectangles that only touch don't intersect).
    public bool Intersects(DockRect other) =>
        Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;

    public DockRect Inset(double dx, double dy) => new(X + dx, Y + dy, Width - 2 * dx, Height - 2 * dy);
}

/// Which way the list opens from the dock, into the screen (the chevron on a pill points the same way).
public enum OpenDirection { Up, Left, Right }

/// Where a dock's pinned corner is: the dock grows away from it.
public enum DockPin { BottomLeft, BottomRight, TopLeft, TopRight }

public static class DockLayout
{
    public const double EdgeInset = 10;        // from the screen edge (the bar has 6 px of room around it)
    public const double CornerInset = 96;      // keeps a corner's two places apart
    public const double SideTopInset = 24;
    public const double ItemSize = 30;
    public static readonly DockSize ListSize = new(380, 500);   // about four notifications, then it scrolls
    public const double ListGap = 10;
    public const double ScaleMin = 0.8;
    public const double ScaleMax = 1.6;

    // MARK: - Places

    public static bool IsVertical(DockSpot spot) => spot.Edge() != DockEdge.bottom;

    /// Which corner of the dock is pinned to the place's anchor point.
    public static DockPin Pin(DockSpot spot) => spot switch
    {
        DockSpot.bottomLeft or DockSpot.leftBottom => DockPin.BottomLeft,
        DockSpot.bottomRight or DockSpot.rightBottom => DockPin.BottomRight,
        DockSpot.leftTop => DockPin.TopLeft,
        _ => DockPin.TopRight,
    };

    /// The list opens into the screen: up from the bottom, right from the left edge, left from the right edge.
    public static OpenDirection OpenDirectionFor(DockSpot spot) => spot.Edge() switch
    {
        DockEdge.bottom => OpenDirection.Up,
        DockEdge.left => OpenDirection.Right,
        _ => OpenDirection.Left,
    };

    // MARK: - Sizes

    /// The size setting (Appearance, On Screen) scales the logos.
    public static double Item(double scale) => Math.Round(ItemSize * Math.Min(Math.Max(scale, ScaleMin), ScaleMax), MidpointRounding.AwayFromZero);

    /// Fixed canvas (never resized while things animate), with room for the hover label.
    public static DockSize CanvasSize(bool vertical) => vertical ? new DockSize(420, 720) : new DockSize(820, 150);

    public static DockSize ZoneSize(bool vertical) => vertical ? new DockSize(64, 160) : new DockSize(160, 64);

    // MARK: - Placement (vf = the work area of the screen, without the taskbar)

    /// The point each place pins its dock to.
    public static DockPoint Anchor(DockSpot spot, DockRect vf) => spot switch
    {
        DockSpot.bottomLeft => new DockPoint(vf.Left + CornerInset, vf.Bottom - EdgeInset),
        DockSpot.bottomRight => new DockPoint(vf.Right - CornerInset, vf.Bottom - EdgeInset),
        DockSpot.leftTop => new DockPoint(vf.Left + EdgeInset, vf.Top + SideTopInset),
        DockSpot.leftBottom => new DockPoint(vf.Left + EdgeInset, vf.Bottom - CornerInset),
        DockSpot.rightTop => new DockPoint(vf.Right - EdgeInset, vf.Top + SideTopInset),
        _ => new DockPoint(vf.Right - EdgeInset, vf.Bottom - CornerInset),
    };

    /// A box of `size` pinned to the place's anchor.
    public static DockRect Pinned(DockSize size, DockSpot spot, DockRect vf)
    {
        var p = Anchor(spot, vf);
        var pin = Pin(spot);
        var right = pin is DockPin.BottomRight or DockPin.TopRight;
        var bottom = pin is DockPin.BottomLeft or DockPin.BottomRight;
        return new DockRect(right ? p.X - size.Width : p.X, bottom ? p.Y - size.Height : p.Y, size.Width, size.Height);
    }

    public static DockRect Canvas(DockSpot spot, DockRect vf) => Pinned(CanvasSize(IsVertical(spot)), spot, vf);

    /// The light-up target drawn while dragging.
    public static DockRect Zone(DockSpot spot, DockRect vf) => Pinned(ZoneSize(IsVertical(spot)), spot, vf);

    /// The place closest to where the dock is being dragged.
    public static DockSpot Nearest(DockPoint point, DockRect vf) =>
        DockSpotInfo.All.MinBy(s =>
        {
            var z = Zone(s, vf);
            return Math.Sqrt(Math.Pow(z.MidX - point.X, 2) + Math.Pow(z.MidY - point.Y, 2));
        });

    /// The list opens next to the dock, into the screen, and stays on it.
    public static DockRect List(DockRect dock, DockSpot spot, DockRect vf)
    {
        var s = ListSize;
        double x, y;
        var pin = Pin(spot);
        switch (spot.Edge())
        {
            case DockEdge.bottom:
                y = dock.Top - ListGap - s.Height;                                       // above the bar
                x = pin == DockPin.BottomLeft ? dock.Left : dock.Right - s.Width;        // lined up with it
                break;
            default:
                x = spot.Edge() == DockEdge.left ? dock.Right + ListGap : dock.Left - ListGap - s.Width;
                y = pin is DockPin.TopLeft or DockPin.TopRight ? dock.Top : dock.Bottom - s.Height;
                break;
        }
        x = Math.Min(Math.Max(x, vf.Left + 8), vf.Right - 8 - s.Width);
        y = Math.Min(Math.Max(y, vf.Top + 8), vf.Bottom - 8 - s.Height);
        return new DockRect(x, y, s.Width, s.Height);
    }

    // MARK: - Screen sharing

    /// Kept out of screen sharing (and screenshots) unless turned off or briefly allowed.
    public static bool SharingHidden(bool protection, DateTimeOffset? allowedUntil, DateTimeOffset now) =>
        protection && !(allowedUntil is { } until && until > now);
}

/// Where the pop-up that drops from the top centre sits (GlimpsePlacement in MenuBarGlimpse.swift).
public static class PopupLayout
{
    /// The panel never resizes (resizing makes content jump); the card animates inside it.
    public static readonly DockSize Card = new(372, 120);        // the tallest it gets
    public static readonly DockSize ViewMore = new(104, 24);
    public const double Spacing = 6;       // between the card and "View more"
    public const double Margin = 2;
    public const double Gap = 6;           // between the top of the work area and the card

    public static DockSize Canvas => new(Card.Width + Margin * 2, Card.Height + Spacing + ViewMore.Height + Margin * 2);

    /// The panel's frame: centred on the screen, with the card just below the top of the work area.
    public static DockRect Frame(DockRect screen, DockRect visible)
    {
        var size = Canvas;
        var top = visible.Top + Gap - Margin;
        return new DockRect(screen.MidX - size.Width / 2, top, size.Width, size.Height);
    }

    /// The card inside the frame.
    public static DockRect CardIn(DockRect frame) => frame.Inset(Margin, Margin);
}
