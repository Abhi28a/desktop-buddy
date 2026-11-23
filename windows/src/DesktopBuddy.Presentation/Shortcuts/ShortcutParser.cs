using System.Globalization;

namespace DesktopBuddy.Presentation.Shortcuts;

/// Modifier keys of a global shortcut. The values match the Win32 MOD_* flags, so they can go
/// straight into RegisterHotKey.
[Flags]
public enum ShortcutModifiers
{
    None = 0,
    Alt = 0x1,      // MOD_ALT
    Ctrl = 0x2,     // MOD_CONTROL
    Shift = 0x4,    // MOD_SHIFT
    Win = 0x8,      // MOD_WIN
}

/// A parsed shortcut: modifiers, the Windows virtual-key code, and the key's display name.
public readonly record struct Shortcut(ShortcutModifiers Modifiers, uint VirtualKey, string KeyName)
{
    /// The canonical text, for example "Ctrl+Alt+N".
    public override string ToString() => ShortcutParser.Format(this);
}

/// Turns the text of a shortcut ("Ctrl+Alt+N", "alt+ctrl+n") into modifiers plus a virtual-key
/// code, and back into one canonical spelling. Pure logic with no Windows calls, so it's tested
/// directly; Platform/Hotkey.cs registers the result with Windows.
///
/// Accepted keys: letters, digits, F1 to F24 and Space. Modifiers: Ctrl, Alt, Shift, Win
/// (also "Control" and "Windows"), in any order and any case. A shortcut needs one key and at
/// least one of Ctrl, Alt or Win (Shift alone would clash with typing). A Win-only shortcut such
/// as Win+N is refused because Windows keeps those for itself.
public static class ShortcutParser
{
    public static readonly Shortcut Default = new(ShortcutModifiers.Ctrl | ShortcutModifiers.Alt, 'N', "N");

    /// Parses the text. On failure `shortcut` is default and `error` says why in plain words.
    public static bool TryParse(string? text, out Shortcut shortcut, out string error)
    {
        shortcut = default;
        error = "";
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Press a shortcut, for example Ctrl+Alt+N.";
            return false;
        }

        var modifiers = ShortcutModifiers.None;
        string? keyText = null;
        foreach (var raw in text.Split('+'))
        {
            var part = raw.Trim();
            if (part.Length == 0)
            {
                error = $"\"{text.Trim()}\" isn't a valid shortcut.";
                return false;
            }
            var mod = Modifier(part);
            if (mod != ShortcutModifiers.None)
            {
                if (modifiers.HasFlag(mod))
                {
                    error = $"{Name(mod)} is listed twice.";
                    return false;
                }
                modifiers |= mod;
                continue;
            }
            if (keyText is not null)
            {
                error = "Use only one key besides Ctrl, Alt, Shift and Win.";
                return false;
            }
            keyText = part;
        }

        if (keyText is null)
        {
            error = "Add a key, for example Ctrl+Alt+N.";
            return false;
        }
        if (!TryKey(keyText, out var vk, out var keyName))
        {
            error = $"\"{keyText}\" isn't a key you can use. Try a letter, a digit, F1 to F24 or Space.";
            return false;
        }
        if (modifiers == ShortcutModifiers.None)
        {
            error = "Add Ctrl, Alt or Win, so it doesn't clash with typing.";
            return false;
        }
        if (modifiers == ShortcutModifiers.Shift)
        {
            error = "Shift alone would clash with typing. Add Ctrl or Alt.";
            return false;
        }
        if (modifiers == ShortcutModifiers.Win || modifiers == (ShortcutModifiers.Win | ShortcutModifiers.Shift))
        {
            error = "Windows keeps Win shortcuts for itself. Add Ctrl or Alt.";
            return false;
        }
        if (modifiers == ShortcutModifiers.Alt && (vk == 0x20 || vk == 0x73))
        {
            error = "Windows uses that one (Alt+Space and Alt+F4). Pick another.";
            return false;
        }

        shortcut = new Shortcut(modifiers, vk, keyName);
        return true;
    }

    /// The canonical spelling ("Ctrl+Alt+N"), or null when the text isn't a usable shortcut.
    public static string? Normalise(string? text) => TryParse(text, out var s, out _) ? Format(s) : null;

    /// Modifiers in a fixed order (Ctrl, Alt, Shift, Win), then the key.
    public static string Format(Shortcut shortcut) => Format(shortcut.Modifiers, shortcut.KeyName);

    public static string Format(ShortcutModifiers modifiers, string keyName)
    {
        var parts = new List<string>(5);
        if (modifiers.HasFlag(ShortcutModifiers.Ctrl)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ShortcutModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ShortcutModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ShortcutModifiers.Win)) parts.Add("Win");
        parts.Add(keyName);
        return string.Join('+', parts);
    }

    private static ShortcutModifiers Modifier(string part) => part.ToLowerInvariant() switch
    {
        "ctrl" or "control" => ShortcutModifiers.Ctrl,
        "alt" => ShortcutModifiers.Alt,
        "shift" => ShortcutModifiers.Shift,
        "win" or "windows" => ShortcutModifiers.Win,
        _ => ShortcutModifiers.None,
    };

    private static string Name(ShortcutModifiers m) => m switch
    {
        ShortcutModifiers.Ctrl => "Ctrl",
        ShortcutModifiers.Alt => "Alt",
        ShortcutModifiers.Shift => "Shift",
        _ => "Win",
    };

    private static bool TryKey(string text, out uint vk, out string name)
    {
        vk = 0;
        name = "";
        if (text.Length == 1)
        {
            var c = char.ToUpperInvariant(text[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')   // virtual-key codes equal the ASCII code
            {
                vk = c;
                name = c.ToString();
                return true;
            }
            return false;
        }
        if (text.Equals("Space", StringComparison.OrdinalIgnoreCase))
        {
            vk = 0x20;
            name = "Space";
            return true;
        }
        if ((text[0] is 'F' or 'f') && int.TryParse(text.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            && n is >= 1 and <= 24
            && text.Length == (n < 10 ? 2 : 3))                // "F01" is not F1
        {
            vk = (uint)(0x70 + n - 1);                         // VK_F1 = 0x70 ... VK_F24 = 0x87
            name = "F" + n;
            return true;
        }
        return false;
    }
}
