using System.Runtime.InteropServices;
using DesktopBuddy.App.Platform;
using DesktopBuddy.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace DesktopBuddy.App.Views.Settings;

/// The one Settings window: Mica backdrop, our own title bar, six sections in a sidebar on the left and the page on the right.
internal sealed class SettingsWindow : Window
{
    private const double MinimumWidth = 820;
    private const double MinimumHeight = 600;

    private static SettingsWindow? _current;

    private readonly SettingsContext _ctx;
    private readonly SettingsShell _shell;
    private readonly MinSizeHook? _minSize;

    /// Opens Settings, or brings the open one forward (and goes to the page, if one is asked for).
    public static void Show(string? page)
    {
        var shift = ShiftHeld();
        if (_current is { } open)
        {
            if (shift && !open._ctx.AdminRequested)
            {
                open._ctx.AdminRequested = true;
                open._shell.RefreshAfterAdminChange();
            }
            open.BringForward(page);
            return;
        }
        _current = new SettingsWindow(page, shift);
    }

    /// For the live check: the open window, if any.
    internal static SettingsWindow? OpenWindow => _current;
    internal SettingsShell ShellForTest => _shell;

    private static bool ShiftHeld()
    {
        try
        {
            return Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private SettingsWindow(string? page, bool admin)
    {
        _ctx = new SettingsContext { Host = this, AdminRequested = admin };
        var state = AppState.Shared;
        _shell = new SettingsShell(_ctx, page);
        Content = _shell;
        Title = "Desktop Buddy Settings";

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(_shell.TitleBar);
        SystemBackdrop = new MicaBackdrop();
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);
        var bar = AppWindow.TitleBar;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;

        _minSize = MinSizeHook.Install(WindowChrome.Handle(this), MinimumWidth, MinimumHeight);

        var area = WindowChrome.WorkArea(this);
        var width = Math.Min(1100, area.Width - 48);
        var height = Math.Min(780, area.Height - 48);
        WindowChrome.Place(this, area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);

        Closed += (_, _) =>
        {
            _shell.Detach();
            _minSize?.Dispose();
            if (ReferenceEquals(_current, this)) _current = null;
        };
        Activate();

        // First time: a short tour, over the sign-in page.
        _shell.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
        {
            await Task.Delay(400);
            if (state.Config.TourSeen != true && ReferenceEquals(_current, this)) await TourDialog.ShowAsync(_ctx);
        });
    }

    private void BringForward(string? page)
    {
        if (page is not null) _shell.Navigate(page);
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter) presenter.Restore();
        AppWindow.Show();
        Activate();
        Native.SetForegroundWindow(WindowChrome.Handle(this));
    }
}

/// Keeps the window from being dragged smaller than the layout can handle (WM_GETMINMAXINFO).
internal sealed class MinSizeHook : IDisposable
{
    private delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }

    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc proc, UIntPtr id, UIntPtr data);

    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc proc, UIntPtr id);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_GETMINMAXINFO = 0x0024;

    private readonly IntPtr _hwnd;
    private readonly double _minWidth;
    private readonly double _minHeight;
    private readonly SubclassProc _proc;   // kept so it isn't collected while the window lives

    private MinSizeHook(IntPtr hwnd, double minWidth, double minHeight)
    {
        _hwnd = hwnd;
        _minWidth = minWidth;
        _minHeight = minHeight;
        _proc = Handle;
    }

    public static MinSizeHook? Install(IntPtr hwnd, double minWidth, double minHeight)
    {
        var hook = new MinSizeHook(hwnd, minWidth, minHeight);
        return SetWindowSubclass(hwnd, hook._proc, 1, UIntPtr.Zero) ? hook : null;
    }

    private IntPtr Handle(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            var scale = Native.GetDpiForWindow(hWnd) / 96.0;
            info.MinTrackSize.X = (int)Math.Ceiling(_minWidth * scale);
            info.MinTrackSize.Y = (int)Math.Ceiling(_minHeight * scale);
            Marshal.StructureToPtr(info, lParam, false);
            return IntPtr.Zero;
        }
        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose() => RemoveWindowSubclass(_hwnd, _proc, 1);
}
