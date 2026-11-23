using System.Runtime.InteropServices;

namespace DesktopBuddy.App.Platform;

/// The notification-area (system tray) icon: the Windows counterpart of the Mac menu bar item.
/// Uses Shell_NotifyIcon with a hidden window that receives its clicks. Re-adds itself if
/// Explorer restarts. Must be created on the UI thread (its window shares that message loop).
internal sealed class TrayIcon : IDisposable
{
    public sealed record MenuItem(string Text, Action? OnClick = null, bool Checked = false, bool Enabled = true)
    {
        public static readonly MenuItem Separator = new("-");
    }

    private const uint CallbackMessage = Native.WM_APP + 1;
    private const uint IconID = 1;

    private readonly Native.WndProc _wndProc;       // kept alive so the GC can't collect it
    private readonly IntPtr _hwnd;
    private readonly uint _taskbarCreated;
    private IntPtr _icon;
    private string _tooltip;
    private bool _added;

    /// Left click or Enter / Space on the icon. The point is the icon's anchor in screen pixels.
    public event Action? Invoked;
    /// Right click or the menu key: the caller builds the menu (see ShowMenu).
    public event Action? MenuRequested;

    public TrayIcon(string iconPath, string tooltip)
    {
        _tooltip = tooltip;
        _wndProc = WndProc;
        var className = "DesktopBuddyTray";
        var wc = new Native.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<Native.WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = Native.GetModuleHandle(null),
            lpszClassName = className,
        };
        Native.RegisterClassEx(ref wc);
        // A hidden top-level window (not message-only), so it hears Explorer's "TaskbarCreated" broadcast.
        _hwnd = Native.CreateWindowEx(0, className, "Desktop Buddy tray", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        _taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
        _icon = LoadIcon(iconPath);
        Add();
    }

    public void SetTooltip(string text)
    {
        _tooltip = text;
        var data = Data(Native.NIF_TIP | Native.NIF_SHOWTIP);
        Native.Shell_NotifyIcon(Native.NIM_MODIFY, ref data);
    }

    public void SetIcon(string iconPath)
    {
        var old = _icon;
        _icon = LoadIcon(iconPath);
        var data = Data(Native.NIF_ICON);
        Native.Shell_NotifyIcon(Native.NIM_MODIFY, ref data);
        if (old != IntPtr.Zero) Native.DestroyIcon(old);
    }

    /// Where the icon is on screen, in physical pixels (for placing the flyout above it).
    public Native.RECT? Bounds
    {
        get
        {
            var id = new Native.NOTIFYICONIDENTIFIER
            {
                cbSize = (uint)Marshal.SizeOf<Native.NOTIFYICONIDENTIFIER>(),
                hWnd = _hwnd,
                uID = IconID,
            };
            return Native.Shell_NotifyIconGetRect(ref id, out var r) == 0 ? r : null;
        }
    }

    /// Shows a native context menu at the pointer and runs the chosen item.
    public void ShowMenu(IReadOnlyList<MenuItem> items)
    {
        var menu = Native.CreatePopupMenu();
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Text == "-")
                {
                    Native.AppendMenu(menu, Native.MF_SEPARATOR, UIntPtr.Zero, null);
                    continue;
                }
                var flags = Native.MF_STRING | (item.Checked ? Native.MF_CHECKED : 0) | (item.Enabled ? 0 : Native.MF_GRAYED);
                Native.AppendMenu(menu, flags, (UIntPtr)(i + 1), item.Text);
            }
            Native.GetCursorPos(out var pt);
            Native.SetForegroundWindow(_hwnd);     // so the menu closes when you click elsewhere
            var chosen = Native.TrackPopupMenuEx(menu, Native.TPM_RETURNCMD | Native.TPM_RIGHTBUTTON | Native.TPM_BOTTOMALIGN,
                pt.X, pt.Y, _hwnd, IntPtr.Zero);
            Native.PostMessage(_hwnd, Native.WM_NULL, IntPtr.Zero, IntPtr.Zero);
            if (chosen > 0) items[(int)chosen - 1].OnClick?.Invoke();
        }
        finally
        {
            Native.DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = Data(0);
            Native.Shell_NotifyIcon(Native.NIM_DELETE, ref data);
            _added = false;
        }
        if (_icon != IntPtr.Zero) Native.DestroyIcon(_icon);
        Native.DestroyWindow(_hwnd);
    }

    private void Add()
    {
        var data = Data(Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP | Native.NIF_SHOWTIP);
        _added = Native.Shell_NotifyIcon(Native.NIM_ADD, ref data);
        data.uVersion = Native.NOTIFYICON_VERSION_4;
        Native.Shell_NotifyIcon(Native.NIM_SETVERSION, ref data);
    }

    private Native.NOTIFYICONDATA Data(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<Native.NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = IconID,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = _tooltip,
        szInfo = "",
        szInfoTitle = "",
    };

    private static IntPtr LoadIcon(string path) => Native.LoadImage(IntPtr.Zero, path, Native.IMAGE_ICON,
        Native.GetSystemMetrics(Native.SM_CXSMICON), Native.GetSystemMetrics(Native.SM_CYSMICON), Native.LR_LOADFROMFILE);

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == CallbackMessage)
        {
            // NOTIFYICON_VERSION_4: the event is in the low word of lParam.
            var evt = (uint)(lParam.ToInt64() & 0xFFFF);
            switch (evt)
            {
                case Native.NIN_SELECT:
                case Native.NIN_KEYSELECT:
                case Native.WM_LBUTTONUP:
                    Invoked?.Invoke();
                    break;
                case Native.WM_CONTEXTMENU:
                case Native.WM_RBUTTONUP:
                    MenuRequested?.Invoke();
                    break;
            }
            return IntPtr.Zero;
        }
        if (msg == _taskbarCreated)
        {
            Add();      // Explorer restarted: the icon has to be added again
            return IntPtr.Zero;
        }
        return Native.DefWindowProc(hWnd, msg, wParam, lParam);
    }
}
