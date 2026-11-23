using DesktopBuddy.Presentation;
using DesktopBuddy.Presentation.Shortcuts;

namespace DesktopBuddy.Tests;

/// Shortcut parsing, and the small pieces every view model stands on.
public class PresentationTests
{
    [Fact]
    public void ParsesTheDefaultShortcut()
    {
        Assert.True(ShortcutParser.TryParse("Ctrl+Alt+N", out var s, out var error), error);
        Assert.Equal(ShortcutModifiers.Ctrl | ShortcutModifiers.Alt, s.Modifiers);
        Assert.Equal((uint)'N', s.VirtualKey);
        Assert.Equal("N", s.KeyName);
        Assert.Equal("Ctrl+Alt+N", s.ToString());
        Assert.Equal(ShortcutParser.Default, s);
    }

    [Fact]
    public void ModifierValuesMatchWin32()
    {
        Assert.Equal(1, (int)ShortcutModifiers.Alt);
        Assert.Equal(2, (int)ShortcutModifiers.Ctrl);
        Assert.Equal(4, (int)ShortcutModifiers.Shift);
        Assert.Equal(8, (int)ShortcutModifiers.Win);
    }

    [Theory]
    [InlineData("alt+ctrl+n", "Ctrl+Alt+N")]
    [InlineData("ALT + CTRL + N", "Ctrl+Alt+N")]
    [InlineData("shift+alt+control+k", "Ctrl+Alt+Shift+K")]
    [InlineData("Windows+Shift+ctrl+7", "Ctrl+Shift+Win+7")]
    [InlineData("win+ctrl+space", "Ctrl+Win+Space")]
    [InlineData("  Ctrl+Alt+n ", "Ctrl+Alt+N")]
    [InlineData("Ctrl+Alt+Shift+Win+Z", "Ctrl+Alt+Shift+Win+Z")]
    public void NormalisesToOneSpelling(string input, string expected)
    {
        Assert.Equal(expected, ShortcutParser.Normalise(input));
        Assert.Equal(expected, ShortcutParser.Normalise(expected));     // stable
    }

    [Theory]
    [InlineData("N")]                      // no modifier
    [InlineData("Ctrl")]                   // no key
    [InlineData("Ctrl+Alt")]               // no key
    [InlineData("Ctrl+Alt+")]              // empty part
    [InlineData("+N")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Ctrl+Alt+Home")]          // unknown key
    [InlineData("Ctrl+Alt+Enter")]
    [InlineData("Ctrl+Alt+Tab")]
    [InlineData("Ctrl+Alt+;")]
    [InlineData("Ctrl+Alt+F25")]
    [InlineData("Ctrl+Alt+F0")]
    [InlineData("Ctrl+Alt+F01")]
    [InlineData("Ctrl+Alt+N+M")]           // two keys
    [InlineData("Ctrl+Ctrl+N")]            // repeated modifier
    [InlineData("Shift+N")]                // Shift alone
    [InlineData("Win+N")]                  // Windows keeps Win-only shortcuts
    [InlineData("Win+Shift+N")]
    [InlineData("Alt+F4")]                 // closes windows
    [InlineData("Alt+Space")]              // window menu
    public void RejectsUnusableShortcuts(string? input)
    {
        Assert.False(ShortcutParser.TryParse(input, out var s, out var error));
        Assert.Equal(default, s);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Null(ShortcutParser.Normalise(input));
    }

    [Theory]
    [InlineData("Ctrl+Alt+A", 0x41u, "A")]
    [InlineData("Ctrl+Alt+z", 0x5Au, "Z")]
    [InlineData("Ctrl+Alt+0", 0x30u, "0")]
    [InlineData("Ctrl+Alt+9", 0x39u, "9")]
    [InlineData("Ctrl+Alt+F1", 0x70u, "F1")]
    [InlineData("Ctrl+Alt+f12", 0x7Bu, "F12")]
    [InlineData("Ctrl+Alt+F24", 0x87u, "F24")]
    [InlineData("Ctrl+Alt+Space", 0x20u, "Space")]
    [InlineData("Ctrl+SPACE", 0x20u, "Space")]
    [InlineData("Alt+F5", 0x74u, "F5")]
    public void KnowsTheKeys(string input, uint vk, string name)
    {
        Assert.True(ShortcutParser.TryParse(input, out var s, out var error), error);
        Assert.Equal(vk, s.VirtualKey);
        Assert.Equal(name, s.KeyName);
    }

    [Fact]
    public void EveryFunctionKeyParses()
    {
        for (var n = 1; n <= 24; n++)
        {
            Assert.True(ShortcutParser.TryParse($"Ctrl+Alt+F{n}", out var s, out var error), error);
            Assert.Equal((uint)(0x70 + n - 1), s.VirtualKey);
            Assert.Equal($"Ctrl+Alt+F{n}", s.ToString());
        }
    }

    [Fact]
    public void ErrorsAreInPlainWords()
    {
        ShortcutParser.TryParse("Win+N", out _, out var win);
        Assert.Contains("Windows", win);
        ShortcutParser.TryParse("Ctrl+Alt+Home", out _, out var key);
        Assert.Contains("Home", key);
    }

    [Fact]
    public void ImmediateDispatcherRunsRightAway()
    {
        var ran = false;
        ImmediateDispatcher.Instance.Post(() => ran = true);
        Assert.True(ran);
    }

    private sealed class Sample(IDispatcher? d = null) : ViewModelBase(d)
    {
        private string _name = "";
        public string Name { get => _name; set => SetProperty(ref _name, value); }
        public void Later(Action a) => Post(a);
    }

    private sealed class QueueDispatcher(Queue<Action> queue) : IDispatcher
    {
        public void Post(Action action) => queue.Enqueue(action);
    }

    [Fact]
    public void ViewModelBaseRaisesChangeAndUsesTheDispatcher()
    {
        var vm = new Sample();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        vm.Name = "x";
        vm.Name = "x";                       // no change, no event
        Assert.Equal(["Name"], changed);

        var queue = new Queue<Action>();
        var queued = new Sample(new QueueDispatcher(queue));
        var ran = false;
        queued.Later(() => ran = true);
        Assert.False(ran);
        queue.Dequeue()();
        Assert.True(ran);
    }
}
