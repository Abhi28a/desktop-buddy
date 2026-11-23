using System.Runtime.InteropServices;
using DesktopBuddy.App.Platform;
using DesktopBuddy.App.Ui;
using DesktopBuddy.App.Views.Rail;
using DesktopBuddy.Core;
using DesktopBuddy.Presentation.Board;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace DesktopBuddy.App.Views.Board;

/// The Board's floating window. Created when it opens and destroyed when it closes (a window that is
/// only hidden can leave a stale frame). Opaque surface, no backdrop: the content paints it.
/// Opens above the tray icon (or beside it on a side taskbar), or beside the Rail.
internal sealed class BoardWindow : Window
{
    private const int DwmTransitionsForceDisabled = 3;

    private readonly AppState _state = AppState.Shared;
    private readonly BoardViewModel _vm;
    private readonly BoardView _view;
    private double _maxHeightDip = BoardPlacement.MaxBoardHeight;
    private bool _active;
    private bool _ready;
    private bool _closing;
    private long _readyAt;

    public BoardWindow(BoardAnchor anchor, string? owner)
    {
        Anchor = anchor;
        Owner = owner;
        Title = "Desktop Buddy Board";
        WindowChrome.MakeFloatingPanel(this);
        var hwnd = WindowChrome.Handle(this);
        var off = 1;      // no system show or hide animation: house rule
        Native.DwmSetWindowAttribute(hwnd, DwmTransitionsForceDisabled, ref off, sizeof(int));
        ApplyCapture();

        _vm = new BoardViewModel(_state, App.Instance!.Dispatcher, owner);
        _vm.SettingsRequested += page =>
        {
            App.Instance!.ShowSettings(page);
            RequestClose(fade: false);
        };
        _vm.PropertyChanged += (_, e) =>
        {
            // What changes the height: the list, the problem line, the Undo bar, the empty state.
            if (e.PropertyName is nameof(BoardViewModel.Groups) or nameof(BoardViewModel.Problem)
                or nameof(BoardViewModel.HasUndo) or nameof(BoardViewModel.IsEmpty) or nameof(BoardViewModel.IsFiltered))
                DispatcherQueue.TryEnqueue(Refit);
        };
        _view = new BoardView(_vm, hosted: true);
        _view.CloseRequested += () => RequestClose();
        Content = _view;

        _state.Changed += ApplyCapture;
        Activated += OnActivated;
        Closed += OnClosed;
        _view.Loaded += (_, _) => DispatcherQueue.TryEnqueue(Reveal);

        // Start off screen at full height so the content lays out; Reveal() fits and moves it into place.
        var target = Resolve(double.NaN);
        _maxHeightDip = target.MaxHeightDip;
        AppWindow.Move(new PointInt32(-32000, -32000));
        AppWindow.ResizeClient(new SizeInt32(target.Width, target.Height));
        Activate();
    }

    public BoardAnchor Anchor { get; }
    public string? Owner { get; }
    /// Closed because it lost the focus (so the click that did it must not reopen it).
    public bool ClosedByDeactivate { get; private set; }

    // MARK: - Opening

    /// Fits the height to the content, moves into place, takes the keyboard and fades in.
    private void Reveal()
    {
        if (_ready || _closing) return;
        _ready = true;
        PlaceFitted();
        _readyAt = Environment.TickCount64;
        Native.SetForegroundWindow(WindowChrome.Handle(this));
        Activate();
        Motion.FadeIn(_view);
        DispatcherQueue.TryEnqueue(_view.FocusSelected);
    }

    /// Keeps the height in step with the content while the Board is open (items arrive, Undo appears).
    private void Refit()
    {
        if (_ready && !_closing) PlaceFitted();
    }

    private void PlaceFitted()
    {
        var target = Resolve(_view.MeasureHeight(BoardPlacement.BoardWidth, _maxHeightDip));
        for (var pass = 0; pass < 2; pass++)       // twice: a second monitor's DPI may rescale the first
        {
            AppWindow.Move(new PointInt32(target.X, target.Y));
            AppWindow.ResizeClient(new SizeInt32(target.Width, target.Height));    // the content gets exactly 404 wide
        }
    }

    // MARK: - Where it goes

    private readonly record struct Target(int X, int Y, int Width, int Height, double MaxHeightDip);

    /// Position and size in physical pixels. `heightDip` NaN = as tall as allowed.
    private Target Resolve(double heightDip)
    {
        var useRail = Anchor == BoardAnchor.Rail && RailPlacement.Bounds is not null;
        PxRect anchor;
        if (useRail)
        {
            var r = RailPlacement.Bounds!.Value;
            anchor = new PxRect(r.X, r.Y, r.X + r.Width, r.Y + r.Height);
        }
        else if (App.Instance?.TrayBounds is { } t)
        {
            anchor = new PxRect(t.Left, t.Top, t.Right, t.Bottom);
        }
        else
        {
            Native.GetCursorPos(out var c);
            anchor = new PxRect(c.X - 16, c.Y - 16, c.X + 16, c.Y + 16);
        }

        var area = DisplayArea.GetFromPoint(new PointInt32(anchor.CenterX, anchor.CenterY), DisplayAreaFallback.Nearest);
        var wa = area.WorkArea;
        var ob = area.OuterBounds;
        var work = new PxRect(wa.X, wa.Y, wa.X + wa.Width, wa.Y + wa.Height);
        var outer = new PxRect(ob.X, ob.Y, ob.X + ob.Width, ob.Y + ob.Height);
        var scale = MonitorScale(anchor.CenterX, anchor.CenterY);

        var maxDip = BoardPlacement.MaxHeightDips(work.Height / scale);
        var hDip = double.IsNaN(heightDip) ? maxDip : Math.Clamp(heightDip, 1, maxDip);
        var w = (int)Math.Round(BoardPlacement.BoardWidth * scale);
        var h = (int)Math.Round(hDip * scale);
        var gap = (int)Math.Round(BoardPlacement.Gap * scale);
        var (x, y) = useRail
            ? BoardPlacement.FromRail(anchor, RailPlacement.Spot, work, w, h, gap)
            : BoardPlacement.FromTray(anchor, work, outer, w, h, gap);
        return new Target(x, y, w, h, maxDip);
    }

    private static double MonitorScale(int x, int y)
    {
        try
        {
            var monitor = MonitorFromPoint(new Native.POINT { X = x, Y = y }, 2 /* nearest */);
            if (GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 && dpi > 0) return dpi / 96.0;
        }
        catch { /* fall back below */ }
        return 1;
    }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Native.POINT pt, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);

    // MARK: - Privacy

    /// Kept out of screen sharing unless that's off or allowed for a minute.
    private void ApplyCapture() =>
        WindowChrome.HideFromCapture(this, DockLayout.SharingHidden(_state.Config.HidesFromSharing, _state.ScreenshotsAllowedUntil, _state.Now()));

    // MARK: - Closing

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        _active = e.WindowActivationState != WindowActivationState.Deactivated;
        if (_active || !_ready || _closing) return;
        // Clicked somewhere else: close, unless a menu from a row is what took the focus.
        DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(150);
            if (_active || _closing || _view.OpenMenus > 0) return;
            if (Environment.TickCount64 - _readyAt < 400) return;
            ClosedByDeactivate = true;
            RequestClose();
        });
    }

    /// Fades out and closes. `fade: false` closes at once (another Board is replacing it).
    public void RequestClose(bool fade = true)
    {
        if (_closing) return;
        _closing = true;
        if (fade && _ready) Motion.FadeOut(_view, () => DispatcherQueue.TryEnqueue(Close));
        else Close();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _state.Changed -= ApplyCapture;
        _vm.Dispose();
        BoardHost.Closed(this);
    }
}

/// Opens and closes the one Board. Called from App.Board.cs.
internal static class BoardHost
{
    private static BoardWindow? _open;
    private static (BoardAnchor Anchor, string? Owner, long At)? _lostFocus;

    /// Opens the Board, or closes it when it's already open at that anchor and owner. Opening at a
    /// different anchor or owner replaces it.
    public static void Toggle(BoardAnchor anchor, string? owner)
    {
        if (_open is { } current)
        {
            var same = current.Anchor == anchor && current.Owner == owner;
            if (same) { current.RequestClose(); return; }
            current.RequestClose(fade: false);
        }
        else if (_lostFocus is { } lost && lost.Anchor == anchor && lost.Owner == owner
                 && Environment.TickCount64 - lost.At < 500)
        {
            // The click on the tray icon took the focus first and closed the Board; it means "close".
            _lostFocus = null;
            return;
        }
        _lostFocus = null;
        _open = new BoardWindow(anchor, owner);
    }

    internal static void Closed(BoardWindow window)
    {
        if (ReferenceEquals(_open, window)) _open = null;
        if (window.ClosedByDeactivate) _lostFocus = (window.Anchor, window.Owner, Environment.TickCount64);
    }
}
