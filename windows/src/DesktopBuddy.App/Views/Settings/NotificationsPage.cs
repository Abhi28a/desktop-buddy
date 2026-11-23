using CommunityToolkit.WinUI.Controls;
using DesktopBuddy.App.Platform;
using DesktopBuddy.App.Ui;
using DesktopBuddy.Core;
using DesktopBuddy.Presentation.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;

namespace DesktopBuddy.App.Views.Settings;

/// Shows the slider's value as a percentage ("120%") in its tooltip.
internal sealed class PercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => $"{Math.Round((double)value * 100)}%";
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// Notifications: the Peek, the Rail, the Board shortcut, sounds, quiet hours, how Windows' own focus is
/// respected, screen-share privacy, and how often accounts are checked.
internal sealed class NotificationsPage : SettingsPage
{
    public NotificationsPage(SettingsContext ctx) : base(ctx) { }

    public override SettingsRoute Route => SettingsRoute.Notifications;

    private AppConfig Config => State.Config;

    private void Set(Action<AppConfig> change)
    {
        change(Config);
        State.SaveConfig();
    }

    protected override void Build(StackPanel body)
    {
        PeekGroup(body);
        RailGroup(body);
        BoardGroup(body);
        SoundsGroup(body);
        QuietGroup(body);
        PrivacyGroup(body);
        CheckingGroup(body);
    }

    // MARK: - Peek

    private void PeekGroup(StackPanel body)
    {
        body.Children.Add(Sk.Heading("Peek"));
        var on = Config.PeekEnabled;
        var sw = Sk.Switch(on, v => Set(c => c.PeekEnabled = v), "Show a Peek when something arrives", "PeekSwitch");
        if (!on)
        {
            body.Children.Add(Sk.Row("Show a Peek", "A small card at the top of the screen when something arrives, with Open, Done and Later.", sw, Glyphs.Bell, "PeekRow"));
            return;
        }
        var value = Sk.Text($"{Config.PeekSeconds} seconds", "DbBodyStyle");
        value.Width = 78;
        value.TextAlignment = TextAlignment.Right;
        value.VerticalAlignment = VerticalAlignment.Center;
        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
        var slider = new Slider
        {
            Minimum = AppConfig.MinPeekSeconds,
            Maximum = AppConfig.MaxPeekSeconds,
            StepFrequency = 1,
            Value = Config.PeekSeconds,
            Width = 200,
            Tag = "PeekSeconds",
        };
        slider.Id("PeekSeconds", "How long the Peek stays, in seconds");
        slider.ValueChanged += (_, e) =>
        {
            var v = (int)Math.Round(e.NewValue);
            value.Text = $"{v} seconds";
            Quiet(() => Set(c => c.PeekSeconds = v));      // saved without rebuilding, so the drag isn't interrupted
        };
        var control = Sk.Buttons(slider, value);
        control.Spacing = 10;
        var how = Sk.Row("How long it stays", null, control, id: "PeekSecondsRow");
        body.Children.Add(Sk.Expander("Show a Peek", "A small card at the top of the screen when something arrives, with Open, Done and Later.", sw, Glyphs.Bell, "PeekRow", expanded: true, how));
    }

    // MARK: - Rail

    private void RailGroup(StackPanel body)
    {
        body.Children.Add(Sk.Heading("Rail"));
        var percent = Sk.Text($"{Math.Round(Config.DockScale * 100)}%", "DbBodyStyle");
        percent.Width = 52;
        percent.TextAlignment = TextAlignment.Right;
        percent.VerticalAlignment = VerticalAlignment.Center;
        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(percent, FontNumeralAlignment.Tabular);
        var slider = new Slider
        {
            Minimum = DockLayout.ScaleMin,
            Maximum = DockLayout.ScaleMax,
            StepFrequency = 0.1,
            TickFrequency = 0.1,
            Value = Math.Min(DockLayout.ScaleMax, Math.Max(DockLayout.ScaleMin, Config.DockScale)),
            Width = 200,
            ThumbToolTipValueConverter = new PercentConverter(),
            Tag = "RailSize",
        };
        slider.Id("RailSize", "Rail size");
        slider.ValueChanged += (_, e) =>
        {
            var v = Math.Round(e.NewValue, 1);
            percent.Text = $"{Math.Round(v * 100)}%";
            Quiet(() => Set(c => c.DockScale = v));
        };
        var size = Sk.Buttons(slider, percent);
        size.Spacing = 10;

        body.Children.Add(Sk.Group(
            Sk.Row("Edge", "Which edge of the screen the strip of tokens sits on.",
                Sk.Combo(DockSpotInfo.All.Select(s => (s.Title(), s)).ToList(), Config.DockSpot, v => Set(c => c.DockSpot = v), "Rail edge", "RailEdge", 200), Glyphs.Rail, "RailEdgeRow"),
            Sk.Row("Compact", "Show names only when you hover or focus a token.",
                Sk.Switch(Config.RailCompact, v => Set(c => c.RailCompact = v), "Compact Rail", "RailCompact"), Glyphs.Switch, "RailCompactRow"),
            Sk.Row("Size", null, size, Glyphs.Size, "RailSizeRow")));
    }

    // MARK: - Board shortcut

    private void BoardGroup(StackPanel body)
    {
        body.Children.Add(Sk.Heading("Board"));
        var app = App.Instance;
        var vm = new DesktopBuddy.Presentation.Settings.ShortcutBoxViewModel(Config.BoardShortcut,
            text =>
            {
                if (Ctx.Preview || app is null) return new ShortcutApplyResult(true, $"{text} is ready.");
                var r = app.ApplyShortcut(text);
                return new ShortcutApplyResult(r.Registered, r.Message);
            },
            text => Quiet(() => Set(c => c.BoardShortcut = text)),
            app?.Dispatcher);
        var initial = app?.ShortcutStatus?.Message ?? $"{Config.BoardShortcut} is ready.";
        var box = new ShortcutBox(vm, initial);
        body.Children.Add(Sk.Group(Sk.Row("Board shortcut", "Opens the Board from anywhere in Windows.", box, Glyphs.Keyboard, "BoardShortcutRow")));
    }

    // MARK: - Sounds

    private static string PluralName(ItemSource s) => s switch
    {
        ItemSource.mail => "Mail",
        ItemSource.chat => "Chats",
        ItemSource.meeting => "Meetings",
        ItemSource.task => "Tasks",
        _ => "Reminders",
    };

    private void SoundsGroup(StackPanel body)
    {
        body.Children.Add(Sk.Heading("Sounds"));
        var items = ItemSourceInfo.All.Select(s => SoundRow(Config, s, Config.PlaySound, Ctx, Set)).ToArray();
        var sw = Sk.Switch(Config.PlaySound, v => Set(c => c.PlaySound = v), "Play sounds", "PlaySound");
        body.Children.Add(Sk.Expander("Play sounds", "One sound for each kind of notification, so you know what arrived without looking.", sw, Glyphs.Volume, "SoundsGroup", expanded: true, items));
    }

    /// One row per kind of notification: name, sound menu and a play button.
    internal static SettingsCard SoundRow(AppConfig config, ItemSource source, bool enabled, SettingsContext ctx, Action<Action<AppConfig>> set)
    {
        var current = config.Sound(source);
        var options = new List<(string Text, string Value)> { (AppConfig.NoSound, AppConfig.NoSound) };
        foreach (var n in Sounds.Names) if (n != AppConfig.NoSound) options.Add((n, n));
        if (!options.Any(o => o.Value == current)) options.Add((current, current));

        var name = PluralName(source);
        var combo = Sk.Combo(options, current, v =>
        {
            set(c => c.SetSound(v, source));
            if (!ctx.Preview) Sounds.Play(v);
        }, $"{name} sound", $"Sound_{source}", 160);
        var play = Sk.IconBtn(Glyphs.Play, $"Play the {name.ToLowerInvariant()} sound", $"SoundPlay_{source}", () => { if (!ctx.Preview) Sounds.Play(current); }, 12);
        play.IsEnabled = enabled && current != AppConfig.NoSound;
        ToolTipService.SetToolTip(play, $"Play {current}");
        combo.IsEnabled = enabled;
        var row = Sk.Row(name, null, Sk.Buttons(combo, play), null, $"SoundRow_{source}");
        var icon = enabled ? Sk.SourceIcon(source) : Sk.Icon(source.Glyph(), 16, "SkIconSecondary");
        row.HeaderIcon = icon;
        return row;
    }

    // MARK: - Quiet hours and Windows focus

    private void QuietGroup(StackPanel body)
    {
        body.Children.Add(Sk.Heading("Quiet hours and focus"));
        body.Children.Add(Sk.Group(
            Parts.QuietHours(State, Set, "Notif"),
            Sk.Row("Respect Windows Do not disturb, presentations and full screen",
                "A Peek waits until you're back. Everything stays in the Board.",
                Sk.Switch(Config.RespectWindowsFocus, v => Set(c => c.RespectWindowsFocus = v), "Respect Windows focus", "RespectFocus"), Glyphs.Shield, "RespectFocusRow")));
    }

    // MARK: - Screen sharing

    private void PrivacyGroup(StackPanel body)
    {
        body.Children.Add(Sk.Heading("Screen sharing"));
        var hides = Config.HidesFromSharing;
        var items = new List<SettingsCard>();
        var sw = Sk.Switch(hides, v => Set(c => c.HidesFromSharing = v), "Hide from screen sharing", "HideFromSharing");
        if (hides)
        {
            var allowed = State.ScreenshotsAllowedUntil is { } until && until > State.Now();
            UIElement trailing = allowed
                ? Sk.StatusText(StatusLevel.Neutral, "Allowed for a minute")
                : Sk.Btn("Allow screenshots for 1 minute", AllowScreenshots, "AllowScreenshots");
            items.Add(Sk.Row("Screenshots", "Windows uses the same switch for screenshots and recordings.", trailing, Glyphs.Camera, "ScreenshotsRow"));
        }
        body.Children.Add(hides
            ? Sk.Expander("Hide from screen sharing", "People you share your screen with don't see Desktop Buddy's windows.", sw, Glyphs.Lock, "SharingGroup", expanded: true, [.. items])
            : Sk.Row("Hide from screen sharing", "People you share your screen with don't see Desktop Buddy's windows.", sw, Glyphs.Lock, "SharingGroup"));
    }

    private void AllowScreenshots()
    {
        State.ScreenshotsAllowedUntil = State.Now().AddMinutes(1);
        _ = Task.Delay(TimeSpan.FromSeconds(61)).ContinueWith(_ => SettingsContext.OnUi(() =>
        {
            if (State.ScreenshotsAllowedUntil is { } until && until <= State.Now()) State.ScreenshotsAllowedUntil = null;
        }), TaskScheduler.Default);
    }

    // MARK: - Checking

    private void CheckingGroup(StackPanel body)
    {
        body.Children.Add(Sk.Heading("Checking"));
        body.Children.Add(Sk.Group(
            Sk.Row("Check accounts every", "How often mail, chats and tasks are looked at.",
                Sk.Combo(WithCurrent([("30 seconds", 30), ("1 minute", 60), ("2 minutes", 120), ("5 minutes", 300), ("15 minutes", 900)],
                    Config.PollSeconds, s => $"{s} seconds"), Config.PollSeconds, v =>
                {
                    Set(c => c.PollSeconds = v);
                    if (!Ctx.Preview) Poller.Shared.Start();
                }, "Check accounts every", "PollInterval"), Glyphs.Timer, "PollIntervalRow"),
            Sk.Row("Warn about meetings", "How long before one starts.",
                Sk.Combo(WithCurrent([("2 minutes before", 2), ("5 minutes before", 5), ("10 minutes before", 10), ("15 minutes before", 15)],
                    Config.MeetingLeadMinutes, m => $"{m} minutes before"), Config.MeetingLeadMinutes, v => Set(c => c.MeetingLeadMinutes = v), "Warn about meetings", "MeetingLead"),
                ItemSource.meeting.Glyph(), "MeetingLeadRow")));
    }

    /// The options, plus the saved value when it isn't one of them.
    private static List<(string Text, int Value)> WithCurrent(List<(string Text, int Value)> options, int current, Func<int, string> label)
    {
        if (!options.Any(o => o.Value == current)) options.Add((label(current), current));
        return options.OrderBy(o => o.Value).ToList();
    }
}
