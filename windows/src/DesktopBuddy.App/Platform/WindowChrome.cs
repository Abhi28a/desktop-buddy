using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace DesktopBuddy.App.Platform;

/// Helpers for the floating windows (the dock of logos, the pop-up, the notification list).
internal static class WindowChrome
{
    public static IntPtr Handle(Window w) => WinRT.Interop.WindowNative.GetWindowHandle(w);

    /// Scale from DIPs (what XAML uses) to physical pixels for this window's monitor.
    public static double Scale(Window w) => Native.GetDpiForWindow(Handle(w)) / 96.0;

    /// A borderless, always-on-top panel with rounded Windows 11 corners that stays out of
    /// the taskbar and Alt+Tab. Used for the dock, the pop-up and the notification list.
    public static void MakeFloatingPanel(Window w, bool round = true)
    {
        var appWindow = w.AppWindow;
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        appWindow.SetPresenter(presenter);
        appWindow.IsShownInSwitchers = false;

        var hwnd = Handle(w);
        var ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_TOOLWINDOW);
        if (round)
        {
            var pref = Native.DWMWCP_ROUND;
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
    }

    /// Keeps the window out of screen sharing and screenshots ("Hide from Screen Sharing").
    public static void HideFromCapture(Window w, bool hide) =>
        Native.SetWindowDisplayAffinity(Handle(w), hide ? Native.WDA_EXCLUDEFROMCAPTURE : Native.WDA_NONE);

    /// Moves and sizes the window. Position and size are in DIPs on the window's current monitor.
    public static void Place(Window w, double x, double y, double width, double height)
    {
        var s = Scale(w);
        w.AppWindow.MoveAndResize(new RectInt32((int)Math.Round(x * s), (int)Math.Round(y * s),
            (int)Math.Round(width * s), (int)Math.Round(height * s)));
    }

    /// The usable area (without the taskbar) of the monitor the window is on, in DIPs.
    public static Rect WorkArea(Window w)
    {
        var area = DisplayArea.GetFromWindowId(w.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var s = Scale(w);
        return new Rect(area.X / s, area.Y / s, area.Width / s, area.Height / s);
    }

    /// The primary monitor's usable area in physical pixels (before a window exists).
    public static RectInt32 PrimaryWorkAreaPixels() => DisplayArea.Primary.WorkArea;

    public readonly record struct Rect(double X, double Y, double Width, double Height)
    {
        public double Right => X + Width;
        public double Bottom => Y + Height;
    }
}
