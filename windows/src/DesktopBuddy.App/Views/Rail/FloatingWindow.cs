using System.Runtime.InteropServices;
using DesktopBuddy.App.Platform;
using DesktopBuddy.App.Ui;
using DesktopBuddy.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace DesktopBuddy.App.Views.Rail;

/// The shell the Rail, its name tooltip, the drag zones and the Peek share: a borderless, always-on-top
/// window that never takes the keyboard focus, stays out of the taskbar and Alt+Tab, and has no system show
/// or hide animation (house rule: only our own fade and settle).
///
/// SHAPE. A WinUI 3 window can't be see-through in this Windows App SDK (a clear system backdrop still
/// composes over black), so the window is cut to its shape with a window region: a rounded rectangle, or a
/// ring for the drag zones. The content fills the whole window and draws its own 1 px edge. Region edges
/// are not anti-aliased, and a window can't cast a shadow outside its region, so panels have no shadow.
///
/// Lessons carried over from the old overlays: create the window when it is needed and Close() it when it
/// is done (a window that is only hidden can leave a stale frame); a layered window doesn't pass the pointer
/// on, so it is layered only while it fades; and track the cursor yourself, because a window that isn't
/// active does not reliably get pointer-over events.
internal class FloatingWindow : Microsoft.UI.Xaml.Window
{
    private const int WsExLayered = 0x00080000;

    private FrameworkElement? _content;
    private bool _shown;
    private double _radius;
    private double _ring;
    private bool _layered;
    private double _alpha = 1;
    private double _alphaTarget = 1;
    private double _alphaStep;
    private Action? _afterFade;
    private DispatcherQueueTimer? _fadeTimer;

    public FloatingWindow()
    {
        Title = "Desktop Buddy";
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        var hwnd = Hwnd;
        var ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
        var doNotRound = 1;     // DWMWCP_DONOTROUND: the region gives the corners
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref doNotRound, sizeof(int));
        var off = 1;            // DWMWA_TRANSITIONS_FORCEDISABLED
        Native.DwmSetWindowAttribute(hwnd, 3, ref off, sizeof(int));
        Closed += (_, _) => _fadeTimer?.Stop();
    }

    public IntPtr Hwnd => WindowChrome.Handle(this);

    /// DIPs to physical pixels on this window's monitor.
    public double Scale => WindowChrome.Scale(this);

    /// The content element.
    public FrameworkElement? Root => _content;

    protected void SetRoot(FrameworkElement content)
    {
        _content = content;
        Content = content;
    }

    /// Content that fills the whole window with a surface token (the window is cut to shape, so the
    /// content has to paint every pixel that is left).
    protected void SetRoot(Microsoft.UI.Xaml.Controls.Control content, string fillKey)
    {
        SetRoot(content);
        content.Loaded += (_, _) => content.Background = Tokens.Brush(content, fillKey);
        content.ActualThemeChanged += (_, _) => content.Background = Tokens.Brush(content, fillKey);
    }

    /// Cuts the window to a rounded rectangle (radius in DIPs). With `ringThickness` > 0 only a ring of that
    /// thickness is kept, so what is behind shows through the middle (the drag zones).
    protected void Shape(double radius, double ringThickness = 0)
    {
        _radius = radius;
        _ring = ringThickness;
    }

    /// Shows the window without taking the focus, and keeps it above other windows.
    public void ShowPassive()
    {
        if (!_shown)
        {
            _shown = true;
            Activate();       // the first Activate is what starts the content; NOACTIVATE keeps the focus where it was
        }
        KeepOnTop();
    }

    public void KeepOnTop() =>
        SetWindowPos(Hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10);   // TOPMOST, NOSIZE | NOMOVE | NOACTIVATE

    /// Moves and sizes the window so its CLIENT area (what the content gets) is exactly `r` (DIPs, primary monitor),
    /// and re-cuts its shape.
    public void PlaceDips(DockRect r)
    {
        var s = Scale;
        PlacePixels(new RectInt32((int)Math.Round(r.X * s), (int)Math.Round(r.Y * s),
            (int)Math.Round(r.Width * s), (int)Math.Round(r.Height * s)));
    }

    /// The same in physical pixels. Windows keeps a thin frame around even a borderless window, so the
    /// client area is sized first, then the window is moved so the client lands where it was asked to.
    public void PlacePixels(RectInt32 px)
    {
        AppWindow.ResizeClient(new SizeInt32(px.Width, px.Height));
        var (dx, dy, w, h) = ClientInWindow();
        AppWindow.Move(new PointInt32(px.X - dx, px.Y - dy));
        ApplyShape(dx, dy, w, h, Scale);
    }

    /// Where the client area sits inside the window (pixels), and its size.
    private (int X, int Y, int Width, int Height) ClientInWindow()
    {
        var hwnd = Hwnd;
        GetWindowRect(hwnd, out var win);
        GetClientRect(hwnd, out var client);
        var origin = new Native.POINT();
        ClientToScreen(hwnd, ref origin);
        return (origin.X - win.Left, origin.Y - win.Top, client.Right - client.Left, client.Bottom - client.Top);
    }

    private void ApplyShape(int x, int y, int width, int height, double scale)
    {
        if (_radius <= 0) return;
        var d = (int)Math.Round(_radius * 2 * scale);
        var region = CreateRoundRectRgn(x, y, x + width + 1, y + height + 1, d, d);
        if (_ring > 0)
        {
            var t = (int)Math.Round(_ring * scale);
            var inner = CreateRoundRectRgn(x + t, y + t, x + width + 1 - t, y + height + 1 - t, Math.Max(2, d - 2 * t), Math.Max(2, d - 2 * t));
            CombineRgn(region, region, inner, 4 /* RGN_DIFF */);
            DeleteObject(inner);
        }
        SetWindowRgn(Hwnd, region, true);      // the system owns the region from here
    }

    // MARK: - Fading the whole window

    /// Fades the window to `alpha` (0 = gone, 1 = fully shown) over `ms`. A layered window doesn't pass the
    /// pointer on, so the layered style is only on while the window is not fully opaque. With Animation
    /// effects off the change is instant.
    public void FadeWindow(double alpha, double ms, Action? done = null)
    {
        _alphaTarget = alpha;
        _afterFade = done;
        if (!Motion.Enabled || ms <= 0)
        {
            SetAlpha(alpha);
            var then = _afterFade;
            _afterFade = null;
            then?.Invoke();
            return;
        }
        _alphaStep = 16.0 / ms;
        _fadeTimer ??= DispatcherQueue.CreateTimer();
        _fadeTimer.Interval = TimeSpan.FromMilliseconds(16);
        _fadeTimer.Tick -= OnFadeTick;
        _fadeTimer.Tick += OnFadeTick;
        _fadeTimer.Start();
    }

    /// Sets the window to `alpha` at once (used before the first show, so it doesn't flash).
    public void SetWindowAlpha(double alpha) => SetAlpha(alpha);

    private void OnFadeTick(DispatcherQueueTimer sender, object args)
    {
        var next = _alpha < _alphaTarget ? Math.Min(_alphaTarget, _alpha + _alphaStep) : Math.Max(_alphaTarget, _alpha - _alphaStep);
        SetAlpha(next);
        if (Math.Abs(next - _alphaTarget) > 0.001) return;
        sender.Stop();
        var then = _afterFade;
        _afterFade = null;
        then?.Invoke();
    }

    private void SetAlpha(double alpha)
    {
        _alpha = alpha;
        var hwnd = Hwnd;
        var ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
        if (alpha >= 1)
        {
            if (_layered)
            {
                Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex & ~WsExLayered);
                _layered = false;
            }
            return;
        }
        if (!_layered)
        {
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex | WsExLayered);
            _layered = true;
        }
        SetLayeredWindowAttributes(hwnd, 0, (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), 0x2 /* LWA_ALPHA */);
    }

    // MARK: - Geometry

    /// The client area's rectangle on the screen, in physical pixels (what the content is drawn in).
    public RectInt32 FrameBounds
    {
        get
        {
            var (dx, dy, w, h) = ClientInWindow();
            return new RectInt32(AppWindow.Position.X + dx, AppWindow.Position.Y + dy, w, h);
        }
    }

    /// True when the cursor (physical pixels) is inside the rectangle.
    public static bool Contains(RectInt32 r, Native.POINT p) =>
        p.X >= r.X && p.X < r.X + r.Width && p.Y >= r.Y && p.Y < r.Y + r.Height;

    /// Keeps this window out of screen sharing and screenshots (or not).
    public void ApplyCapture(bool hide) => WindowChrome.HideFromCapture(this, hide);

    /// The primary monitor: the usable area (no taskbar) and the whole screen, in DIPs, plus its scale.
    public static (DockRect Work, DockRect Screen, double Scale) Primary()
    {
        var scale = PrimaryScale();
        var area = DisplayArea.Primary;
        var work = area.WorkArea;
        var outer = area.OuterBounds;
        return (new DockRect(work.X / scale, work.Y / scale, work.Width / scale, work.Height / scale),
                new DockRect(outer.X / scale, outer.Y / scale, outer.Width / scale, outer.Height / scale), scale);
    }

    private static double PrimaryScale()
    {
        try
        {
            var monitor = MonitorFromPoint(new Native.POINT(), 1 /* MONITOR_DEFAULTTOPRIMARY */);
            if (GetDpiForMonitor(monitor, 0, out var x, out _) == 0 && x > 0) return x / 96.0;
        }
        catch { /* fall through */ }
        return 1;
    }

    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out Native.RECT rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out Native.RECT rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref Native.POINT point);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hWnd, IntPtr region, bool redraw);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Native.POINT pt, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(IntPtr dest, IntPtr a, IntPtr b, int mode);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
}
