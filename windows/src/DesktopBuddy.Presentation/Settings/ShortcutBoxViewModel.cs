using CommunityToolkit.Mvvm.ComponentModel;
using DesktopBuddy.Presentation.Shortcuts;

namespace DesktopBuddy.Presentation.Settings;

/// The result of asking Windows to use a shortcut.
public readonly record struct ShortcutApplyResult(bool Registered, string Message);

/// The "Board shortcut" box: you press a combination, it is checked, handed to Windows, and saved only
/// if Windows accepts it. A shortcut that is refused (it isn't a usable combination, or another app owns
/// it) leaves the old one in place and says why in plain words.
public sealed partial class ShortcutBoxViewModel : ViewModelBase
{
    private readonly Func<string, ShortcutApplyResult> _apply;
    private readonly Action<string> _save;

    [ObservableProperty] private string _current;
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _hasProblem;

    /// `apply` hands the normalised text to Windows (App.ApplyShortcut). `save` stores it (the config).
    public ShortcutBoxViewModel(string current, Func<string, ShortcutApplyResult> apply, Action<string> save, IDispatcher? dispatcher = null)
        : base(dispatcher)
    {
        _apply = apply;
        _save = save;
        _current = ShortcutParser.Normalise(current) ?? current;
    }

    public IReadOnlyList<string> Keys => Current.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// Tries a shortcut written as text. True when it is now the one in use.
    public bool Propose(string? text)
    {
        if (!ShortcutParser.TryParse(text, out var shortcut, out var error))
        {
            Refuse(error);
            return false;
        }
        var normal = ShortcutParser.Format(shortcut);
        if (string.Equals(normal, Current, StringComparison.Ordinal))
        {
            // Pressing the shortcut you already have: make sure Windows still has it and say so.
            var again = _apply(normal);
            if (again.Registered) Accept(normal, again.Message); else Refuse(again.Message);
            return again.Registered;
        }
        var result = _apply(normal);
        if (!result.Registered)
        {
            Refuse(result.Message);
            return false;
        }
        _save(normal);
        Accept(normal, result.Message);
        return true;
    }

    /// A key press in the box. A modifier alone (Ctrl) isn't a shortcut yet, so nothing happens until a key arrives.
    /// Returns true when the press was a complete combination (applied or refused).
    public bool Press(ShortcutModifiers modifiers, uint virtualKey)
    {
        var name = ShortcutCapture.KeyName(virtualKey);
        if (name is null) return false;
        Propose(ShortcutParser.Format(modifiers, name));
        return true;
    }

    /// Back to Ctrl+Alt+N.
    public bool Reset() => Propose(ShortcutParser.Format(ShortcutParser.Default));

    /// Shows how a held combination reads so far ("Ctrl+Alt+…").
    public static string Preview(ShortcutModifiers modifiers)
    {
        var text = ShortcutParser.Format(modifiers, "…");
        return modifiers == ShortcutModifiers.None ? "Press a shortcut…" : text;
    }

    private void Refuse(string message)
    {
        Message = message;
        HasProblem = true;
    }

    private void Accept(string normal, string message)
    {
        Current = normal;
        Message = message;
        HasProblem = false;
        OnPropertyChanged(nameof(Keys));
    }
}

/// Turns a pressed key into the name a shortcut uses.
public static class ShortcutCapture
{
    /// "A", "7", "F5" or "Space" for the key codes a shortcut can use. Modifier keys and anything else give null.
    public static string? KeyName(uint virtualKey)
    {
        if (virtualKey is >= 'A' and <= 'Z') return ((char)virtualKey).ToString();
        if (virtualKey is >= '0' and <= '9') return ((char)virtualKey).ToString();
        if (virtualKey is >= 0x70 and <= 0x87) return "F" + (virtualKey - 0x70 + 1);
        if (virtualKey == 0x20) return "Space";
        return null;
    }
}
