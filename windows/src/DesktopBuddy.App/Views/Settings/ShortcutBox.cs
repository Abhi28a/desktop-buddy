using DesktopBuddy.App.Ui;
using DesktopBuddy.App.Ui.Components;
using DesktopBuddy.Presentation.Settings;
using DesktopBuddy.Presentation.Shortcuts;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace DesktopBuddy.App.Views.Settings;

/// The Board shortcut box: click it (or tab to it) and press the combination you want. The key presses are
/// handed to ShortcutBoxViewModel, which checks them and asks Windows. A refused shortcut keeps the old one and
/// says why in words, with an icon. Esc or leaving the box stops listening.
internal sealed class ShortcutBox : StackPanel
{
    private readonly ShortcutBoxViewModel _vm;
    private readonly Button _box;
    private readonly StackPanel _keys;
    private readonly ContentControl _status;
    private bool _listening;

    public ShortcutBox(ShortcutBoxViewModel vm, string? initialMessage)
    {
        _vm = vm;
        Spacing = 6;
        Orientation = Orientation.Vertical;

        _keys = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
        _box = new Button
        {
            Content = _keys,
            Style = Sk.Sty("DbSecondaryButtonStyle"),
            MinWidth = 200,
            HorizontalAlignment = HorizontalAlignment.Right,
            IsTabStop = true,
        };
        _box.Id("ShortcutBox", "Board shortcut. Press a key combination to change it.");
        ToolTipService.SetToolTip(_box, "Click, then press the shortcut you want");
        _box.GotFocus += (_, _) => { _listening = true; Show(null); };
        _box.LostFocus += (_, _) => { _listening = false; Show(null); };
        _box.PreviewKeyDown += OnKeyDown;
        _box.PreviewKeyUp += (_, _) => { if (_listening) Show(Held()); };

        var reset = Sk.QuietBtn("Reset", () => _vm.Reset(), "ShortcutReset", "Reset the shortcut to Ctrl+Alt+N");
        ToolTipService.SetToolTip(reset, "Back to Ctrl+Alt+N");
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        line.Children.Add(reset);
        line.Children.Add(_box);

        _status = new ContentControl { HorizontalAlignment = HorizontalAlignment.Right, IsTabStop = false };
        Children.Add(line);
        Children.Add(_status);

        _vm.PropertyChanged += (_, _) => { Show(_listening ? Held() : null); ShowStatus(); };
        Show(null);
        ShowStatus(initialMessage);
    }

    private static ShortcutModifiers Held()
    {
        static bool Down(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
        var m = ShortcutModifiers.None;
        try
        {
            if (Down(VirtualKey.Control)) m |= ShortcutModifiers.Ctrl;
            if (Down(VirtualKey.Menu)) m |= ShortcutModifiers.Alt;
            if (Down(VirtualKey.Shift)) m |= ShortcutModifiers.Shift;
            if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows)) m |= ShortcutModifiers.Win;
        }
        catch (Exception) { }
        return m;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var mods = Held();
        if (e.Key == VirtualKey.Escape && mods == ShortcutModifiers.None)
        {
            _listening = false;
            Show(null);
            return;                       // lets focus move on as normal
        }
        if (e.Key == VirtualKey.Tab && mods == ShortcutModifiers.None) return;     // keyboard users must be able to leave
        if (e.Key is VirtualKey.Control or VirtualKey.Menu or VirtualKey.Shift or VirtualKey.LeftWindows or VirtualKey.RightWindows
            or VirtualKey.LeftControl or VirtualKey.RightControl or VirtualKey.LeftMenu or VirtualKey.RightMenu
            or VirtualKey.LeftShift or VirtualKey.RightShift)
        {
            Show(mods);
            e.Handled = true;
            return;
        }
        if (e.Key == VirtualKey.Enter || e.Key == VirtualKey.Space && mods == ShortcutModifiers.None)
        {
            return;                       // Enter and Space on the button itself just focus it
        }
        _vm.Press(mods, (uint)e.Key);
        e.Handled = true;
    }

    private void Show(ShortcutModifiers? held)
    {
        _keys.Children.Clear();
        if (_listening && held is { } mods && mods != ShortcutModifiers.None)
        {
            _keys.Children.Add(Sk.Caption(ShortcutBoxViewModel.Preview(mods)));
        }
        else if (_listening)
        {
            _keys.Children.Add(Sk.Caption("Press a shortcut…"));
        }
        else
        {
            foreach (var key in _vm.Keys) _keys.Children.Add(new Keycap(key));
        }
    }

    private void ShowStatus(string? message = null)
    {
        var text = _vm.Message.Length > 0 ? _vm.Message : message ?? "";
        _status.Content = text.Length == 0
            ? null
            : Sk.StatusText(_vm.HasProblem ? StatusLevel.Caution : StatusLevel.Positive, text, 360).With(p => { p.HorizontalAlignment = HorizontalAlignment.Right; p.Id("ShortcutStatus", text); });
    }
}
