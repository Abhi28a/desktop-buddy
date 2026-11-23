using DesktopBuddy.App.Platform;
using DesktopBuddy.Core;
using DesktopBuddy.Presentation.Rail;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.Graphics;

namespace DesktopBuddy.App.Views.Rail;

/// The Rail's window: a floating pill on a screen edge. It is made when something is waiting and closed
/// when nothing is. It never takes the focus, so it tracks the cursor itself (hover wash, name tooltip) and
/// handles dragging: let go and the pill goes to the nearest of the six places.
internal sealed class RailWindow : FloatingWindow
{
    private readonly RailViewModel _vm;
    private readonly RailContent _content = new();
    private readonly DispatcherQueueTimer _poll;
    private bool _placed;
    private bool _closing;

    // Dragging (physical pixels).
    private bool _pressed;
    private bool _dragging;
    private Native.POINT _pressCursor;
    private PointInt32 _pressPosition;
    private DockSpot? _target;
    private RailZones? _zones;

    // The name tooltip.
    private string? _hotOwner;
    private string? _focusOwner;
    private long _hotSince;
    private RailTipWindow? _tip;
    private string? _tipOwner;

    private const int TipDelayMs = 350;

    public RailWindow(RailViewModel vm, Action<string> openBoard)
    {
        _vm = vm;
        SetRoot(_content, "DbSurfaceBaseBrush");
        _content.PrepareSettle();
        if (Ui.Motion.Enabled) SetWindowAlpha(0);
        _content.TokenInvoked += openBoard;
        _content.TokenFocusChanged += (owner, has) =>
        {
            _focusOwner = has ? owner : null;
            if (has) ShowTip(owner); else if (_hotOwner != owner) HideTip();
        };
        _content.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPressed), true);
        _content.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnMoved), true);
        _content.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnReleased), true);
        _content.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnReleased), true);
        _content.Loaded += (_, _) =>
        {
            _content.SettleIn();
            FadeWindow(1, Ui.Motion.SlowMs);
        };

        _poll = DispatcherQueue.CreateTimer();
        _poll.Interval = TimeSpan.FromMilliseconds(80);
        _poll.Tick += (_, _) => Poll();
        _poll.Start();
        Closed += (_, _) =>
        {
            _poll.Stop();
            HideTip();
            _zones?.Close();
        };
    }

    public bool IsDragging => _dragging;

    /// Shows what the view model says, and puts the pill in its place.
    public void Apply()
    {
        if (_dragging || _closing) return;
        _content.Apply(_vm.Tokens, _vm.Vertical, _vm.Compact, _vm.TokenSize, animateNew: _placed);
        var pill = _content.MeasurePill();
        var (work, _, _) = Primary();
        var box = DockLayout.Pinned(new DockSize(pill.Width, pill.Height), _vm.Spot, work);
        Shape(RailRules.PillThickness(_vm.TokenSize, _vm.Compact, _vm.Vertical) / 2);
        PlaceDips(box);
        var s = Scale;
        RailPlacement.Bounds = new RectInt32((int)Math.Round(box.X * s), (int)Math.Round(box.Y * s),
            (int)Math.Round(box.Width * s), (int)Math.Round(box.Height * s));
        RailPlacement.Spot = _vm.Spot;
        ApplyCapture(_vm.HiddenFromCapture);
        _placed = true;
        HideTipIfStale();
    }

    /// Fades the pill out, then closes the window.
    public void Dismiss()
    {
        if (_closing) return;
        _closing = true;
        HideTip();
        _content.FadeAway(null);
        FadeWindow(0, Ui.Motion.FastMs * 2, Close);
    }

    // MARK: - Cursor tracking (hover wash and tooltip)

    private void Poll()
    {
        if (_dragging || _closing) return;
        Native.GetCursorPos(out var cursor);
        string? owner = null;
        if (Contains(FrameBounds, cursor))
        {
            var s = Scale;
            var frame = FrameBounds;
            var p = new Point((cursor.X - frame.X) / s, (cursor.Y - frame.Y) / s);
            owner = _content.OwnerAt(p);
        }
        _content.SetHot(owner ?? _focusOwner);
        if (owner != _hotOwner)
        {
            _hotOwner = owner;
            _hotSince = Environment.TickCount64;
            if (owner is null && _focusOwner is null) HideTip();
            else if (owner is not null && _tipOwner != owner) HideTip();
        }
        else if (owner is not null && _tip is null && Environment.TickCount64 - _hotSince >= TipDelayMs)
        {
            ShowTip(owner);
        }
    }

    private void ShowTip(string owner)
    {
        if (_closing || _dragging) return;
        if (_tipOwner == owner && _tip is not null) return;
        HideTip();
        var token = _vm.Tokens.FirstOrDefault(t => t.OwnerId == owner);
        if (token is null) return;
        var rect = _content.TokenRects().Where(r => r.Owner == owner).Select(r => (Rect?)r.Bounds).FirstOrDefault();
        if (rect is not { } box) return;

        var tip = new RailTipWindow(token.Tooltip);
        var s = Scale;
        var size = tip.MeasureDips();
        var w = (int)Math.Ceiling(size.Width * s);
        var h = (int)Math.Ceiling(size.Height * s);
        var gap = (int)Math.Round(8 * s);
        var win = new PointInt32(FrameBounds.X, FrameBounds.Y);
        var cx = win.X + (int)Math.Round((box.X + box.Width / 2) * s);
        var cy = win.Y + (int)Math.Round((box.Y + box.Height / 2) * s);
        int x, y;
        switch (_vm.Spot.Edge())
        {
            case DockEdge.left:
                x = win.X + (int)Math.Round((box.X + box.Width) * s) + gap; y = cy - h / 2; break;
            case DockEdge.bottom:
                x = cx - w / 2; y = win.Y + (int)Math.Round(box.Y * s) - gap - h; break;
            default:
                x = win.X + (int)Math.Round(box.X * s) - gap - w; y = cy - h / 2; break;
        }
        var area = Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea;
        x = Math.Clamp(x, area.X + 4, Math.Max(area.X + 4, area.X + area.Width - w - 4));
        y = Math.Clamp(y, area.Y + 4, Math.Max(area.Y + 4, area.Y + area.Height - h - 4));
        tip.PlacePixels(new RectInt32(x, y, w, h));
        tip.ApplyCapture(_vm.HiddenFromCapture);
        tip.ShowPassive();
        tip.FadeIn();
        _tip = tip;
        _tipOwner = owner;
    }

    private void HideTip()
    {
        var tip = _tip;
        _tip = null;
        _tipOwner = null;
        tip?.Close();
    }

    /// The token the tooltip belongs to is gone (or its text changed): drop it.
    private void HideTipIfStale()
    {
        if (_tipOwner is null) return;
        var token = _vm.Tokens.FirstOrDefault(t => t.OwnerId == _tipOwner);
        if (token is null || _tip is null || _tip.Text != token.Tooltip) HideTip();
    }

    // MARK: - Dragging

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(_content).Properties.IsLeftButtonPressed) return;
        _pressed = true;
        Native.GetCursorPos(out _pressCursor);
        _pressPosition = AppWindow.Position;
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_pressed) return;
        Native.GetCursorPos(out var now);
        var dx = now.X - _pressCursor.X;
        var dy = now.Y - _pressCursor.Y;
        if (!_dragging)
        {
            if (Math.Abs(dx) < 6 && Math.Abs(dy) < 6) return;
            _dragging = true;
            _content.SuppressClick = true;
            HideTip();
            _content.SetHot(null);
            _zones = new RailZones();
            KeepOnTop();
        }
        AppWindow.Move(new PointInt32(_pressPosition.X + dx, _pressPosition.Y + dy));
        var (work, _, scale) = Primary();
        var target = DockLayout.Nearest(new DockPoint(now.X / scale, now.Y / scale), work);
        if (target != _target)
        {
            _target = target;
            _zones?.Light(target);
        }
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        var wasDragging = _dragging;
        _pressed = false;
        // The click that ends a drag must not press a token: clear the flag once the release is handled.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _content.SuppressClick = false);
        if (!wasDragging) return;
        _dragging = false;
        Native.GetCursorPos(out var now);
        var (work, _, scale) = Primary();
        var spot = DockLayout.Nearest(new DockPoint(now.X / scale, now.Y / scale), work);
        _target = null;
        _zones?.Close();
        _zones = null;
        _vm.MoveTo(spot);     // saves the place; Changed makes the controller re-apply
        Apply();              // and if it didn't move, put the pill back
    }
}

/// The small window for a token's name.
internal sealed class RailTipWindow : FloatingWindow
{
    private readonly RailTip _tip;

    public RailTipWindow(string text)
    {
        _tip = new RailTip(text);
        SetRoot(_tip, "DbSurfaceInverseBrush");
        Shape(8);
        if (Ui.Motion.Enabled) SetWindowAlpha(0);
    }

    public string Text => _tip.Text;

    public Size MeasureDips()
    {
        _tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return _tip.DesiredSize;
    }

    public void FadeIn() => FadeWindow(1, Ui.Motion.NormalMs);
}

/// The six places the Rail can go, shown while it is dragged. The nearest one lights up.
internal sealed class RailZones
{
    private sealed class ZoneWindow : FloatingWindow
    {
        public DockSpot Spot { get; }
        public RailZone Zone { get; }

        public ZoneWindow(DockSpot spot, double thickness)
        {
            Spot = spot;
            Zone = new RailZone();
            SetRoot(Zone);
            Shape(thickness / 2, ringThickness: 2.5);
        }
    }

    private readonly List<ZoneWindow> _windows = [];

    public RailZones()
    {
        var (work, _, _) = FloatingWindow.Primary();
        foreach (var spot in DockSpotInfo.All)
        {
            var box = DockLayout.Zone(spot, work);
            var window = new ZoneWindow(spot, Math.Min(box.Width, box.Height));
            window.PlaceDips(box);
            window.ShowPassive();
            _windows.Add(window);
        }
    }

    public void Light(DockSpot spot)
    {
        foreach (var w in _windows) w.Zone.SetLit(w.Spot == spot);
    }

    public void Close()
    {
        foreach (var w in _windows) w.Close();
        _windows.Clear();
    }
}
