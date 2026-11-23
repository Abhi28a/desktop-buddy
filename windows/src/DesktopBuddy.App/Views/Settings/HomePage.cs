using DesktopBuddy.App.Ui;
using DesktopBuddy.App.Ui.Components;
using DesktopBuddy.Core;
using DesktopBuddy.Presentation.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Auto = Microsoft.UI.Xaml.Automation.AutomationProperties;

namespace DesktopBuddy.App.Views.Settings;

/// Home: what's waiting, anything that needs a fix, and the two things you reach for most (Pause, Quiet hours).
internal sealed class HomePage : SettingsPage
{
    private bool _showAll;

    public HomePage(SettingsContext ctx) : base(ctx) { }

    public override SettingsRoute Route => SettingsRoute.Home;

    private void Set(Action<AppConfig> change)
    {
        change(State.Config);
        State.SaveConfig();
    }

    protected override void Build(StackPanel body)
    {
        var home = HomeSummary.Build(State);
        body.Children.Add(Hero(home));

        body.Children.Add(Sk.Heading("Needs attention"));
        var lines = new StackPanel { Spacing = 8 };
        if (home.Attention.Count == 0)
        {
            lines.Children.Add(Boxed(new StatusLine(StatusSeverity.Positive, "All accounts connected", home.AllConnectedDetail) { AutomationKey = "AllConnected" }));
        }
        var shown = _showAll ? home.Attention : home.Attention.Take(HomeSummary.MaxVisibleAttention).ToList();
        foreach (var item in shown) lines.Children.Add(Boxed(AttentionLine(item)));
        var more = home.Attention.Count - HomeSummary.MaxVisibleAttention;
        if (more > 0)
        {
            var toggle = Sk.QuietBtn(_showAll ? "Show fewer" : $"Show {more} more", () => { _showAll = !_showAll; QueueRebuild(); }, "AttentionMore",
                _showAll ? "Show fewer problems" : $"Show {more} more problems");
            toggle.HorizontalAlignment = HorizontalAlignment.Left;
            lines.Children.Add(toggle);
        }
        body.Children.Add(lines);

        body.Children.Add(Sk.Heading("Right now"));
        body.Children.Add(Sk.Group(Parts.Pause(State, Ctx, home), Parts.QuietHours(State, Set)));
    }

    // MARK: - Hero

    private FrameworkElement Hero(HomeSummary home)
    {
        var card = new Border { Style = Sk.Sty("DbCardStyle"), Padding = new Thickness(24, 20, 24, 22) };
        card.Id("HomeHero", "Waiting summary");
        var grid = new Grid { ColumnSpacing = 24 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Spacing = 16 };
        if (home.WaitingCount > 0)
        {
            var figure = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var number = new TextBlock { Text = home.WaitingCount.ToString(), Style = Sk.Sty("DbFigureStyle") };
            Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(number, Microsoft.UI.Xaml.FontNumeralAlignment.Tabular);
            number.Id("WaitingCount", $"{home.WaitingCount} {home.WaitingText}");
            figure.Children.Add(number);
            var words = Sk.Text(home.WaitingText, "DbSecondaryStyle");
            words.VerticalAlignment = VerticalAlignment.Bottom;
            words.Margin = new Thickness(0, 0, 0, 8);
            figure.Children.Add(words);
            left.Children.Add(figure);

            var tokens = new WrapPanel { HorizontalSpacing = 14, VerticalSpacing = 12 };
            foreach (var owner in home.Owners)
            {
                var token = Sk.OwnerToken(State, owner.Id, 36, owner.Count);
                ToolTipService.SetToolTip(token, $"{owner.Name}: {owner.Count} waiting");
                tokens.Children.Add(token);
            }
            left.Children.Add(tokens);
        }
        else
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var check = Sk.Icon(Glyphs.Positive, 24, "SkIconPositive");
            check.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(check);
            row.Children.Add(Sk.Text("Nothing waiting", "DbPanelTitleStyle").With(t => t.VerticalAlignment = VerticalAlignment.Center));
            left.Children.Add(row);
            left.Children.Add(Sk.Caption("You're all caught up. New mail, chats and meetings show up here and in the Board."));
        }
        grid.Children.Add(left);

        var right = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        var open = Sk.Btn("Open Board", () =>
        {
            if (!Ctx.Preview) App.Instance?.OpenBoard(BoardAnchor.Tray);
        }, "OpenBoard", primary: true);
        open.HorizontalAlignment = HorizontalAlignment.Right;
        right.Children.Add(open);
        right.Children.Add(Shortcut(home));
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        card.Child = grid;
        return card;
    }

    private static FrameworkElement Shortcut(HomeSummary home)
    {
        var keys = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var key in home.ShortcutKeys) keys.Children.Add(new Keycap(key));
        keys.Id("BoardShortcutKeys", "Shortcut: " + string.Join(" plus ", home.ShortcutKeys));
        return keys;
    }

    // MARK: - Needs attention

    /// Each problem sits in its own raised card, like a settings row.
    private static FrameworkElement Boxed(StatusLine line) =>
        new Border { Style = Sk.Sty("DbCardStyle"), Padding = new Thickness(4), Child = line };

    private StatusLine AttentionLine(AttentionItem item)
    {
        var line = new StatusLine(item.Level == StatusLevel.Critical ? StatusSeverity.Critical : StatusSeverity.Caution,
            item.Title, item.Detail, item.FixLabel, () => Fix(item))
        {
            AutomationKey = "Attention_" + item.Key,
        };
        return line;
    }

    private void Fix(AttentionItem item)
    {
        if (Ctx.Preview) return;
        if (item.Kind == AttentionKind.ClientIssue)
        {
            if (item.Fix is { } fix) State.Apply(fix, item.ClientId!);       // sign-in fixes raise SignInRequested, which opens the account
            else Ctx.Navigate("client:" + item.ClientId);
            return;
        }
        if (item.FixUrl is { } url) { SettingsContext.Open(url); return; }
        var account = State.Account(item.AccountId);
        Ctx.Navigate("account:" + item.AccountId);
        if (account is not null && item.Kind is AttentionKind.SignInExpired or AttentionKind.WrongAccount or AttentionKind.NotSignedIn)
        {
            _ = SignInCenter.Shared.StartAsync(account);     // the app checks the account itself once the sign-in finishes
        }
    }
}
