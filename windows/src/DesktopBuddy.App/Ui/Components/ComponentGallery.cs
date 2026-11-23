using CommunityToolkit.WinUI.Controls;
using DesktopBuddy.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DesktopBuddy.App.Ui.Components;

/// Every Desktop Buddy component in one place, for `--snapshot <folder> --only components`.
/// T1 of windows/docs/UI-UX-PLAN.md: the reference other tasks build from and reviewers compare to.
internal sealed class ComponentGallery : Grid
{
    public ComponentGallery(bool settingsRows = false)
    {
        Loaded += (_, _) =>
        {
            if (Children.Count > 0) return;
            Background = Tokens.Brush(this, "DbSurfaceBaseBrush");
            var content = settingsRows ? new StackPanel { Padding = new Thickness(32, 8, 32, 32) }.With(p => AddSettingsRows(p)) : Build();
            Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden });
        };
    }

    private static TextBlock Styled(string text, string style) =>
        new() { Text = text, Style = (Style)Application.Current.Resources[style] };

    private StackPanel Build()
    {
        var state = AppState.Shared;
        var page = new StackPanel { Padding = new Thickness(32, 28, 32, 32), Spacing = 0, MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Left };
        page.Children.Add(Styled("Lily pad components", "DbPageTitleStyle"));
        page.Children.Add(Styled("Tokens, type and controls from Styles/Tokens.xaml and Styles/Type.xaml.", "DbSecondaryStyle"));

        // Tokens
        page.Children.Add(Styled("Client tokens", "DbSectionStyle"));
        var tokens = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 26, Padding = new Thickness(8, 6, 8, 6) };
        foreach (var owner in state.ActiveOwners)
        {
            var count = state.Items.Count(i => i.Owner == owner);
            var urgent = state.Items.Any(i => i.Owner == owner && i.Source == ItemSource.meeting);
            tokens.Children.Add(Labelled(new TokenView(state.OwnerLogo(owner), state.OwnerName(owner), state.OwnerTint(owner), 40, count,
                owner == BuddyItem.ReminderOwner, urgent ? 0.7 : double.NaN), state.OwnerName(owner)));
        }
        tokens.Children.Add(Labelled(new TokenView(null, "Acme Ltd", AccountTint.purple, 40, 12), "Monogram, 12"));
        tokens.Children.Add(Labelled(new TokenView(null, "Northwind", AccountTint.orange, 40, 120), "99+"));
        tokens.Children.Add(Labelled(new TokenView(new LogoRef(null, "iehub"), "IE Hub", AccountTint.green, 40), "Nothing waiting"));
        page.Children.Add(tokens);

        var sizes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 26, Padding = new Thickness(8, 14, 8, 6) };
        foreach (var s in new[] { 24.0, 32, 44, 56 })
            sizes.Children.Add(Labelled(new TokenView(new LogoRef(null, "gatehouse"), "Gatehouse", AccountTint.green, s, 2), $"{s:0} px"));
        page.Children.Add(sizes);

        // Status lines
        page.Children.Add(Styled("Status lines", "DbSectionStyle"));
        var status = new StackPanel { Spacing = 6, MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        status.Children.Add(new StatusLine(StatusSeverity.Caution, "IE Hub: Google Chat isn't working", "Google says the Chat API is turned off for this project.", "Fix"));
        status.Children.Add(new StatusLine(StatusSeverity.Critical, "Acme Ltd: sign-in expired", "Nothing is checked until you sign in again.", "Sign in again"));
        status.Children.Add(new StatusLine(StatusSeverity.Positive, "All accounts connected", "Last checked a minute ago."));
        status.Children.Add(new StatusLine(StatusSeverity.Neutral, "Paused until 3:00 PM", null, "Resume"));
        page.Children.Add(status);

        // Buttons and keycaps
        page.Children.Add(Styled("Actions", "DbSectionStyle"));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(new Button { Content = "Open", Style = (Style)Application.Current.Resources["DbPrimaryButtonStyle"] });
        actions.Children.Add(new Button { Content = "Done", Style = (Style)Application.Current.Resources["DbSecondaryButtonStyle"] });
        actions.Children.Add(new Button { Content = "Later", Style = (Style)Application.Current.Resources["DbSecondaryButtonStyle"] });
        actions.Children.Add(new Button { Content = "Clear all", Style = (Style)Application.Current.Resources["DbQuietButtonStyle"] });
        var settingsButton = new Button { Style = (Style)Application.Current.Resources["DbIconButtonStyle"], Content = new FontIcon { Glyph = "", FontSize = 14 } };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(settingsButton, "Settings");
        actions.Children.Add(settingsButton);
        page.Children.Add(actions);
        var keys = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(0, 14, 0, 0) };
        foreach (var (k, label) in new[] { ("↑↓", "Move"), ("Enter", "Open"), ("E", "Done"), ("S", "Later"), ("Ctrl+Z", "Undo") })
        {
            var pair = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            pair.Children.Add(new Keycap(k));
            pair.Children.Add(Styled(label, "DbCaptionStyle"));
            keys.Children.Add(pair);
        }
        page.Children.Add(keys);

        // Type ramp
        page.Children.Add(Styled("Type", "DbSectionStyle"));
        var type = new Border { Style = (Style)Application.Current.Resources["DbCardStyle"], MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        var ramp = new StackPanel { Spacing = 6 };
        var figure = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        figure.Children.Add(Styled("6", "DbFigureStyle"));
        figure.Children.Add(Styled("waiting across 5 places", "DbSecondaryStyle").With(t => t.VerticalAlignment = VerticalAlignment.Bottom));
        ramp.Children.Add(figure);
        ramp.Children.Add(Styled("Waiting", "DbPanelTitleStyle"));
        ramp.Children.Add(Styled("Sample Sender", "DbRowTitleStyle"));
        ramp.Children.Add(Styled("Mortgage offer – revised schedule", "DbSecondaryStyle"));
        ramp.Children.Add(Styled("Gatehouse · Gmail · 2m", "DbCaptionStyle"));
        type.Child = ramp;
        page.Children.Add(type);
        return page;
    }

    /// Settings rows (Community Toolkit SettingsControls, themed by our accent).
    private static void AddSettingsRows(StackPanel page)
    {
        page.Children.Add(Styled("Settings rows", "DbSectionStyle"));
        var rows = new StackPanel { Spacing = 4, MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        rows.Children.Add(new SettingsCard
        {
            Header = "Pause notifications",
            Description = "Everything waits in the Board until you turn this off.",
            HeaderIcon = new FontIcon { Glyph = "" },
            Content = new ToggleSwitch { IsOn = false },
        });
        var expander = new SettingsExpander
        {
            Header = "Quiet hours",
            Description = "10:00 PM to 8:00 AM, every day",
            HeaderIcon = new FontIcon { Glyph = "" },
            Content = new ToggleSwitch { IsOn = true },
            IsExpanded = true,
        };
        expander.Items.Add(new SettingsCard { Header = "From", Content = new ComboBox { Items = { "10:00 PM" }, SelectedIndex = 0 } });
        expander.Items.Add(new SettingsCard { Header = "Until", Content = new ComboBox { Items = { "8:00 AM" }, SelectedIndex = 0 } });
        rows.Children.Add(expander);
        page.Children.Add(rows);
    }

    private static StackPanel Labelled(FrameworkElement token, string label)
    {
        var s = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        token.HorizontalAlignment = HorizontalAlignment.Center;
        s.Children.Add(token);
        s.Children.Add(Styled(label, "DbCaptionStyle").With(t => t.HorizontalAlignment = HorizontalAlignment.Center));
        return s;
    }
}
