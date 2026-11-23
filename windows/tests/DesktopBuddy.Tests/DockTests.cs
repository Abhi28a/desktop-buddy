using DesktopBuddy.Core;

namespace DesktopBuddy.Tests;

// Port of "On-screen bar: places, list, screen sharing" and "Menu bar pop-up" in SelfTest.swift.
// The Mac numbers its y axis upwards; these rectangles are top-left based (y grows downwards),
// so each check is mirrored from the Mac one.

public class DockTests
{
    // A work area with a taskbar below it: 1440 x 790 starting 80 down from the top of the screen.
    private static readonly DockRect Main = new(0, 0, 1440, 790);

    [Fact]
    public void SixPlacesTwoPerEdgeNoneInTheMiddle()
    {
        Assert.Equal(6, DockSpotInfo.All.Length);
        Assert.All(new[] { DockEdge.bottom, DockEdge.left, DockEdge.right },
            e => Assert.Equal(2, DockSpotInfo.All.Count(s => s.Edge() == e)));
    }

    [Fact]
    public void LogosStartAtTheirUsualSizeAndTheSizeSettingScalesThemWithinLimits()
    {
        Assert.Equal(1, new AppConfig().DockScale);
        Assert.Equal(30, DockLayout.Item(1));
        Assert.Equal(48, DockLayout.Item(1.6));                                    // the size setting makes logos bigger
        Assert.Equal(48, DockLayout.Item(5));                                      // within limits
        Assert.Equal(24, DockLayout.Item(0.1));
        // The biggest logos and their label still fit the canvas.
        Assert.True(DockLayout.CanvasSize(vertical: false).Height >= 6 + 4 + 48 + 4 + 10 + 30 + 6);
    }

    [Fact]
    public void OldPlacesMoveToTheNearestCornerAndMissingMeansStandard()
    {
        Assert.Equal(DockSpot.rightBottom, DockSpotInfo.Saved("rightMiddle"));
        Assert.Equal(DockSpot.leftBottom, DockSpotInfo.Saved("leftMiddle"));
        Assert.Equal(DockSpotInfo.Standard, DockSpotInfo.Saved("bottomCenter"));
        Assert.Equal(DockSpotInfo.Standard, DockSpotInfo.Saved(null));
        Assert.Equal(DockSpotInfo.Standard, new AppConfig().DockSpot);
    }

    [Fact]
    public void PlacesDontOverlapAndAreFullyOnScreen()
    {
        var zones = DockSpotInfo.All.Select(s => DockLayout.Zone(s, Main)).ToList();
        for (var a = 0; a < zones.Count; a++)
            for (var b = 0; b < zones.Count; b++)
                Assert.True(a == b || !zones[a].Intersects(zones[b]), $"{DockSpotInfo.All[a]} overlaps {DockSpotInfo.All[b]}");
        Assert.All(zones, z => Assert.True(Main.Contains(z)));
    }

    [Fact]
    public void NoPlaceCoversTheMiddleOfAnEdge()
    {
        // Bars like a dictation tool's sit in the middle of the bottom, left or right edge.
        DockRect[] middleBars =
        [
            new(Main.MidX - 80, Main.Bottom - 56, 160, 56),
            new(Main.Left, Main.MidY - 80, 56, 160),
            new(Main.Right - 56, Main.MidY - 80, 56, 160),
        ];
        foreach (var s in DockSpotInfo.All)
        {
            var z = DockLayout.Zone(s, Main);
            Assert.All(middleBars, bar => Assert.False(z.Intersects(bar), $"{s} covers a middle bar"));
        }
    }

    [Fact]
    public void CanvasesSitWhereEachPlaceSays()
    {
        var br = DockLayout.Canvas(DockSpot.bottomRight, Main);
        Assert.Equal(Main.Bottom - DockLayout.EdgeInset, br.Bottom);               // sits on the bottom...
        Assert.Equal(Main.Right - DockLayout.CornerInset, br.Right);               // ...near the right
        var bl = DockLayout.Canvas(DockSpot.bottomLeft, Main);
        Assert.Equal(Main.Left + DockLayout.CornerInset, bl.Left);
        Assert.Equal(Main.Bottom - DockLayout.EdgeInset, bl.Bottom);
        var lt = DockLayout.Canvas(DockSpot.leftTop, Main);
        Assert.Equal(Main.Left + DockLayout.EdgeInset, lt.Left);                   // left top hugs the left edge...
        Assert.Equal(Main.Top + DockLayout.SideTopInset, lt.Top);                  // ...near the top
        var rb = DockLayout.Canvas(DockSpot.rightBottom, Main);
        Assert.Equal(Main.Right - DockLayout.EdgeInset, rb.Right);
        Assert.Equal(Main.Bottom - DockLayout.CornerInset, rb.Bottom);
        Assert.Equal(DockLayout.CanvasSize(vertical: true), rb.Size);
        Assert.Equal(DockLayout.CanvasSize(vertical: false), br.Size);
    }

    [Fact]
    public void DraggingPicksThePlaceNearestThePointer()
    {
        Assert.Equal(DockSpot.bottomRight, DockLayout.Nearest(new DockPoint(Main.Right - 200, Main.Bottom - 30), Main));
        (DockPoint, DockSpot)[] aims =
        [
            (new(Main.Right - 30, Main.Top + 60), DockSpot.rightTop), (new(Main.Left + 30, Main.Top + 60), DockSpot.leftTop),
            (new(Main.Right - 30, Main.Bottom - 170), DockSpot.rightBottom), (new(Main.Left + 30, Main.Bottom - 170), DockSpot.leftBottom),
            (new(Main.Right - 170, Main.Bottom - 20), DockSpot.bottomRight), (new(Main.Left + 170, Main.Bottom - 20), DockSpot.bottomLeft),
        ];
        Assert.All(aims, a => Assert.Equal(a.Item2, DockLayout.Nearest(a.Item1, Main)));       // pointing at any corner place picks it, from anywhere
        Assert.Equal(DockSpot.leftTop, DockLayout.Nearest(new DockPoint(Main.Left + 40, Main.Top + 120), Main));   // dragging up the left side
    }

    [Fact]
    public void TheListOpensBesideTheBarIntoTheScreen()
    {
        var bar = new DockRect(Main.Right - 66, Main.Bottom - 96 - 200, 50, 200);
        var list = DockLayout.List(bar, DockSpot.rightBottom, Main);
        Assert.True(list.Right < bar.Left && list.Size == DockLayout.ListSize);    // opens to the left of a right-side bar, with a fixed size
        Assert.True(Main.Contains(list));

        var low = DockLayout.List(new DockRect(1100, Main.Bottom - 10 - 62, 230, 62), DockSpot.bottomRight, Main);
        Assert.True(low.Bottom < Main.Bottom - 72);                                // from the bottom it opens above the bar
        Assert.Equal(1330, low.Right);                                             // ...lined up with its right edge
        Assert.True(Main.Contains(low));

        var left = DockLayout.List(new DockRect(10, 24, 50, 200), DockSpot.leftTop, Main);
        Assert.True(left.Left > 60 && left.Top == 24 && Main.Contains(left));      // from the left edge it opens to the right, top-aligned

        var bl = DockLayout.List(new DockRect(96, Main.Bottom - 72, 230, 62), DockSpot.bottomLeft, Main);
        Assert.Equal(96, bl.Left);                                                 // bottom-left lines up with the bar's left
        Assert.True(DockLayout.ListSize.Height >= 4 * 90);                         // the list fits about four
    }

    [Fact]
    public void TheListIsKeptOnScreenEvenWhenTheBarIsNearAnEdge()
    {
        var tiny = new DockRect(0, 0, 300, 300);
        var list = DockLayout.List(new DockRect(250, 250, 40, 40), DockSpot.bottomRight, tiny);
        Assert.True(list.Left >= tiny.Left + 8 || list.Right <= tiny.Right - 8);   // clamped to the work area as far as it fits
        var normal = DockLayout.List(new DockRect(0, Main.Bottom - 60, 100, 50), DockSpot.bottomLeft, Main);
        Assert.True(normal.Left >= Main.Left + 8 && normal.Top >= Main.Top + 8);
    }

    [Fact]
    public void ChevronAndPinFollowThePlace()
    {
        Assert.Equal(OpenDirection.Up, DockLayout.OpenDirectionFor(DockSpot.bottomRight));
        Assert.Equal(OpenDirection.Right, DockLayout.OpenDirectionFor(DockSpot.leftTop));
        Assert.Equal(OpenDirection.Left, DockLayout.OpenDirectionFor(DockSpot.rightBottom));
        Assert.Equal(DockPin.BottomRight, DockLayout.Pin(DockSpot.rightBottom));
        Assert.Equal(DockPin.TopLeft, DockLayout.Pin(DockSpot.leftTop));
        Assert.True(DockLayout.IsVertical(DockSpot.leftBottom));
        Assert.False(DockLayout.IsVertical(DockSpot.bottomLeft));
    }

    [Fact]
    public void RectangleHelpers()
    {
        var r = new DockRect(10, 20, 100, 50);
        Assert.True(r.Contains(new DockRect(10, 20, 100, 50)));
        Assert.False(r.Contains(new DockRect(5, 20, 100, 50)));
        Assert.False(r.Intersects(new DockRect(110, 20, 10, 10)));                // touching edges don't intersect
        Assert.True(r.Intersects(new DockRect(109, 20, 10, 10)));
        Assert.Equal(new DockRect(12, 22, 96, 46), r.Inset(2, 2));
    }

    // MARK: Screen sharing

    [Fact]
    public void HiddenFromSharingByDefaultAndScreenshotsCanBeAllowedForAMinute()
    {
        var now = Fx.At(3, 12);
        Assert.True(new AppConfig().HidesFromSharing && DockLayout.SharingHidden(true, null, now));   // hidden from sharing by default
        Assert.False(DockLayout.SharingHidden(true, now.AddSeconds(60), now));                         // screenshots allowed for the minute
        Assert.True(DockLayout.SharingHidden(true, now.AddSeconds(-1), now));                          // ...then hidden again
        Assert.False(DockLayout.SharingHidden(false, null, now));                                      // can be turned off for good
    }

    // MARK: Pop-up from the top centre

    [Fact]
    public void ThePopupIsCentredJustBelowTheTopOfTheWorkArea()
    {
        var screen = new DockRect(0, 0, 1440, 900);
        var visible = new DockRect(0, 40, 1440, 820);        // e.g. a taskbar at the top
        var frame = PopupLayout.Frame(screen, visible);
        var card = PopupLayout.CardIn(frame);
        Assert.True(Math.Abs(card.MidX - screen.MidX) < 0.5);                      // centred at the top of the screen
        Assert.True(Math.Abs(card.Top - (visible.Top + PopupLayout.Gap)) < 0.5);   // just below the edge
        Assert.True(screen.Contains(card));                                        // fully on screen
        Assert.True(PopupLayout.Canvas.Height > PopupLayout.Card.Height + PopupLayout.ViewMore.Height);   // View more sits under the card
    }

    [Fact]
    public void PopupTimingAndWhenItShows()
    {
        var old = Fx.Mail("g1", "a", "a@b.example.com", "Old");
        var fresh = Fx.Mail("g2", "a", "a@b.example.com", "New");
        Assert.Equal([fresh.Id], PopupRules.Arrivals(new HashSet<string> { old.Id }, [fresh, old]).Select(i => i.Id));   // only new arrivals pop up
        Assert.Empty(PopupRules.Arrivals(new HashSet<string> { old.Id, fresh.Id }, [fresh, old]));                      // nothing new, no pop-up
        Assert.False(PopupRules.ShouldShow(paused: true, quiet: false));           // no pop-up while paused
        Assert.False(PopupRules.ShouldShow(paused: false, quiet: true));           // no pop-up in quiet hours
        Assert.True(PopupRules.ShouldShow(paused: false, quiet: false));
        Assert.Equal(10, PopupRules.ShowSeconds);                                  // shows for 10 seconds
        var t0 = Fx.At(3, 12);
        Assert.Equal(6.5, PopupRules.TimeLeft(t0.AddSeconds(6.5), t0));            // hovering freezes the time left
        Assert.Equal(0, PopupRules.TimeLeft(t0, t0.AddSeconds(3)));                // never goes below zero
        Assert.True(PopupRules.MinimumAfterHover >= 2);                            // a moment to move away after reading
    }
}
