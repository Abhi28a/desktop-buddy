using DesktopBuddy.App.Ui;
using DesktopBuddy.App.Ui.Components;
using DesktopBuddy.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace DesktopBuddy.App.Views.Settings;

/// The welcome tour, shown once and from About (port of Tour.swift). Five short pages in a dialog.
internal static class TourDialog
{
    public const int PageCount = 5;

    /// Opens the tour. Afterwards the tour counts as seen; if the person chose "Add your first account", Settings goes there.
    public static async Task ShowAsync(SettingsContext ctx)
    {
        var addAccount = false;
        var page = 0;
        var host = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        var footer = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var dialog = new ContentDialog { CloseButtonText = "", DefaultButton = ContentDialogButton.None };
        dialog.Id("TourDialog", "Welcome tour");
        dialog.Resources["ContentDialogMaxWidth"] = 640.0;
        dialog.Resources["ContentDialogMaxHeight"] = 640.0;
        dialog.Resources["ContentDialogPadding"] = new Thickness(0);

        void Render()
        {
            host.Content = Page(ctx, page);
            footer.Content = Footer(ctx, page, p => { page = p; Render(); }, add => { addAccount = add; dialog.Hide(); });
        }

        var root = new Grid { Width = 560, Height = 480 };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(host);
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);
        dialog.Content = root;
        Render();

        await ctx.ShowAsync(dialog);
        ctx.State.Config.TourSeen = true;
        ctx.State.SaveConfig();
        if (addAccount) ctx.Navigate("add-account");
    }

    /// The whole tour as one element (for snapshots).
    public static FrameworkElement Preview(SettingsContext ctx, int page)
    {
        var root = new Grid { Width = 560, Height = 480 };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(Page(ctx, page));
        var footer = Footer(ctx, page, _ => { }, _ => { });
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);
        return root;
    }

    private static FrameworkElement Footer(SettingsContext ctx, int page, Action<int> go, Action<bool> finish)
    {
        var grid = new Grid { Margin = new Thickness(24, 8, 24, 20) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        FrameworkElement left = page == 0
            ? Sk.Link("Skip tour", () => finish(false), "TourSkip")
            : Sk.Btn("Back", () => go(page - 1), "TourBack");
        left.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Children.Add(left);

        var dots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < PageCount; i++)
        {
            dots.Children.Add(new Ellipse { Width = 7, Height = 7, Style = Sk.Sty(i == page ? "SkDotOn" : "SkDotOff") });
        }
        dots.Id("TourPage", $"Page {page + 1} of {PageCount}");
        Grid.SetColumn(dots, 1);
        grid.Children.Add(dots);

        Button right;
        if (page == PageCount - 1)
        {
            var none = ctx.State.Config.Accounts.Count == 0;
            right = Sk.Btn(none ? "Add your first account" : "Done", () => finish(none), "TourDone", primary: true);
        }
        else
        {
            right = Sk.Btn("Continue", () => go(page + 1), "TourContinue", primary: true);
        }
        right.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        return grid;
    }

    // MARK: - Pages

    private static FrameworkElement Page(SettingsContext ctx, int page)
    {
        var first = ctx.State.Config.Accounts.Count == 0;
        return page switch
        {
            0 => Layout(first ? "Welcome to Desktop Buddy" : "What's new in Desktop Buddy",
                first ? "A quiet heads-up when something needs you in Google Workspace or Microsoft 365: new mail, chats, meetings about to start, and tasks due today."
                      : "A quick look at what's changed. Your accounts and settings are just as you left them.",
                AppMark(), [(Glyphs.Lock, "Read-only. Desktop Buddy never sends, changes or deletes anything.")]),
            1 => Layout("It lives in your notification area",
                "A dot on the icon means something is waiting. Click it to open the Board and clear things one by one.",
                TrayPreview(), [(Glyphs.Hand, "Right-click it to pause for an hour or until tomorrow. Drag it out of the ^ menu to keep it in view.")]),
            2 => Layout("New things appear at the top",
                "When something arrives, a Peek shows who it's from and what it says, with Open, Done and Later. It waits while Windows is in Do not disturb, presenting or full screen.",
                PeekPreview(),
                [(Glyphs.Rail, "A strip of tokens on the screen edge shows who's waiting. Click one to see just theirs."),
                 (Glyphs.Keyboard, $"Press {ctx.State.Config.BoardShortcut} from anywhere to open the Board. You can change it in Settings.")]),
            3 => Layout("Only what matters", "Tell Desktop Buddy what to call out and what to ignore, in Settings.", Features(), []),
            _ => Layout("A sound for each kind",
                "Know what arrived without looking. Pick a sound for mail, chats, meetings, tasks and reminders. Try one:",
                SoundsPreview(ctx), [(Glyphs.Gear, "Change them any time in Settings, under Notifications.")]),
        };
    }

    private static FrameworkElement Layout(string title, string text, FrameworkElement art, (string Glyph, string Text)[] notes)
    {
        var panel = new StackPanel { Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(32, 24, 32, 0) };
        var artBox = new Grid { Height = 150 };
        art.HorizontalAlignment = HorizontalAlignment.Center;
        art.VerticalAlignment = VerticalAlignment.Center;
        artBox.Children.Add(art);
        panel.Children.Add(artBox);

        var heading = Sk.Text(title, "DbPanelTitleStyle");
        heading.TextAlignment = TextAlignment.Center;
        heading.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(heading);

        var body = Sk.Text(text, "DbSecondaryStyle");
        body.TextAlignment = TextAlignment.Center;
        body.TextTrimming = TextTrimming.None;
        body.MaxWidth = 440;
        panel.Children.Add(body);

        var noteList = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(noteList);
        foreach (var (glyph, note) in notes)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var icon = Sk.Icon(glyph, 13);
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 2, 0, 0);
            row.Children.Add(icon);
            var t = Sk.Caption(note);
            t.MaxWidth = 400;
            row.Children.Add(t);
            noteList.Children.Add(row);
        }
        return panel;
    }

    private static FrameworkElement AppMark() =>
        // On a white tile, so the brown tree reads in dark mode too.
        new LogoTile(new LogoRef(null, "dynamatix"), "Desktop Buddy", AccountTint.graphite, 96);

    /// A slice of the taskbar with the Desktop Buddy icon and a count.
    private static FrameworkElement TrayPreview()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Sk.Icon(Glyphs.Chevron, 14).With(i => i.RenderTransform = new Microsoft.UI.Xaml.Media.RotateTransform { Angle = -90, CenterX = 7, CenterY = 7 }));
        row.Children.Add(Sk.Icon(Glyphs.Volume, 16));
        var mine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        mine.Children.Add(new LogoTile(new LogoRef(null, "dynamatix"), "Desktop Buddy", AccountTint.graphite, 20));
        mine.Children.Add(Sk.Pill("3"));
        row.Children.Add(mine);
        row.Children.Add(Sk.Text("9:41 AM").With(t => t.TextWrapping = TextWrapping.NoWrap));
        return Sk.Block(row, new Thickness(22, 12, 22, 12));
    }

    /// The Peek: one item, three actions.
    private static FrameworkElement PeekPreview()
    {
        var grid = new Grid { ColumnSpacing = 12, Width = 380 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(Sk.Token(new LogoRef(null, "gatehouse"), "Gatehouse", AccountTint.green, 44, 1));
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        text.Children.Add(Sk.Text("Sample Sender", "DbRowTitleStyle"));
        text.Children.Add(Sk.Text("Mortgage offer – revised schedule", "DbSecondaryStyle"));
        var actions = Sk.Buttons(Sk.Btn("Open", () => { }, "TourPeekOpen", primary: true), Sk.Btn("Done", () => { }, "TourPeekDone"), Sk.Btn("Later", () => { }, "TourPeekLater"));
        actions.Margin = new Thickness(0, 8, 0, 0);
        actions.IsHitTestVisible = false;
        text.Children.Add(actions);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return Sk.Block(grid, new Thickness(16, 14, 20, 14));
    }

    private static FrameworkElement Features()
    {
        var panel = new StackPanel { Spacing = 16, Width = 380 };
        panel.Children.Add(Feature(Glyphs.Briefcase, "Clients", "Group a client's mail and chats under their token."));
        panel.Children.Add(Feature(Glyphs.Mute, "Mute", "Never hear about newsletters or noisy senders again."));
        panel.Children.Add(Feature(Glyphs.Tag, "Labels", "Only get mail your Gmail labels or Outlook categories pick out."));
        return panel;
    }

    private static FrameworkElement Feature(string glyph, string title, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        row.Children.Add(Sk.Icon(glyph, 22, "SkIconAccent").With(i => i.VerticalAlignment = VerticalAlignment.Center));
        var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        t.Children.Add(Sk.Text(title, "DbRowTitleStyle"));
        t.Children.Add(Sk.Caption(text));
        row.Children.Add(t);
        return row;
    }

    private static FrameworkElement SoundsPreview(SettingsContext ctx)
    {
        var state = ctx.State;
        void Set(Action<AppConfig> change)
        {
            change(state.Config);
            state.SaveConfig();
        }
        var panel = new StackPanel { Spacing = 3, Width = 440 };
        panel.Children.Add(NotificationsPage.SoundRow(state.Config, ItemSource.mail, true, ctx, Set));
        panel.Children.Add(NotificationsPage.SoundRow(state.Config, ItemSource.meeting, true, ctx, Set));
        return panel;
    }
}
