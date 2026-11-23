using System.Runtime.InteropServices;
using DesktopBuddy.Presentation.Shortcuts;

namespace DesktopBuddy.App.Platform;

/// Whether a shortcut registered, and if not, why in plain words.
public sealed record HotkeyResult(bool Registered, string Message)
{
    public static HotkeyResult Ok(string shortcut) => new(true, $"{shortcut} is ready.");
}

/// The global shortcut that opens the Board from anywhere. RegisterHotKey on a hidden top-level
/// window (the same approach as TrayIcon), with MOD_NOREPEAT so holding the keys fires once.
/// Create it on the UI thread: WM_HOTKEY arrives through that thread's message loop.
internal sealed class Hotkey : IDisposable
{
    private const uint WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly Native.WndProc _wndProc;       // kept alive so the GC can't collect it
    private readonly IntPtr _hwnd;
    private int _nextID = 1;
    private int _activeID;                          // 0 = nothing registered
    private Shortcut _active;
    private Action? _onPressed;

    public Hotkey()
    {
        _wndProc = WndProc;
        var className = "DesktopBuddyHotkey" + Guid.NewGuid().ToString("N");   // one class per instance, so each keeps its own WndProc
        var wc = new Native.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<Native.WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = Native.GetModuleHandle(null),
            lpszClassName = className,
        };
        Native.RegisterClassEx(ref wc);
        _hwnd = Native.CreateWindowEx(0, className, "Desktop Buddy shortcut", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
    }

    /// The shortcut that is registered now, in canonical form, or null.
    public string? Current => _activeID == 0 ? null : ShortcutParser.Format(_active);

    /// Registers the shortcut, replacing the previous one. The old one is kept until the new
    /// one has registered, so a refused change doesn't leave you with no shortcut at all.
    public HotkeyResult Register(string shortcut, Action onPressed)
    {
        if (!ShortcutParser.TryParse(shortcut, out var parsed, out var error))
            return new HotkeyResult(false, error);

        var text = ShortcutParser.Format(parsed);
        if (_activeID != 0 && _active == parsed)
        {
            _onPressed = onPressed;                             // already ours: only the callback changed
            return HotkeyResult.Ok(text);
        }

        var id = _nextID++;
        if (!RegisterHotKey(_hwnd, id, (uint)parsed.Modifiers | MOD_NOREPEAT, parsed.VirtualKey))
        {
            var code = Marshal.GetLastWin32Error();
            return new HotkeyResult(false, code == ERROR_HOTKEY_ALREADY_REGISTERED
                ? $"Another app is already using {text}."
                : $"Windows wouldn't register {text} (error {code}).");
        }

        Unregister();
        _onPressed = onPressed;
        _activeID = id;
        _active = parsed;
        return HotkeyResult.Ok(text);
    }

    /// Releases the current shortcut, if any.
    public void Unregister()
    {
        if (_activeID == 0) return;
        UnregisterHotKey(_hwnd, _activeID);
        _activeID = 0;
    }

    public void Dispose()
    {
        Unregister();
        _onPressed = null;
        if (_hwnd != IntPtr.Zero) Native.DestroyWindow(_hwnd);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_HOTKEY && _activeID != 0 && wParam.ToInt32() == _activeID)
        {
            try { _onPressed?.Invoke(); } catch { /* a bad handler must not break the message loop */ }
            return IntPtr.Zero;
        }
        return Native.DefWindowProc(hWnd, msg, wParam, lParam);
    }
}
