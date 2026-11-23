using CommunityToolkit.WinUI.Controls;
using DesktopBuddy.App.Ui;
using DesktopBuddy.App.Ui.Components;
using DesktopBuddy.Core;
using DesktopBuddy.Presentation.Settings;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Auto = Microsoft.UI.Xaml.Automation.AutomationProperties;

namespace DesktopBuddy.App.Views.Settings;

/// Segoe Fluent Icons used in Settings. Written as escapes so file encodings can't mangle them.
internal static class Glyphs
{
    public const string Home = "";
    public const string Person = "";
    public const string People = "";
    public const string Briefcase = "";
    public const string Bell = "";
    public const string Clock = "";
    public const string Info = "";
    public const string Gear = "";
    public const string Pause = "";
    public const string Moon = "";
    public const string Add = "";
    public const string Check = "";
    public const string Warning = "";
    public const string Close = "";
    public const string Copy = "";
    public const string Chevron = "";
    public const string Play = "";
    public const string Tag = "";
    public const string Mute = "";
    public const string Delete = "";
    public const string Edit = "";
    public const string Refresh = "";
    public const string Download = "";
    public const string Lock = "";
    public const string Camera = "";
    public const string Search = "";
    public const string Bug = "";
    public const string Link = "";
    public const string Folder = "";
    public const string Volume = "";
    public const string Chat = "";
    public const string Mail = "";
    public const string Keyboard = "";
    public const string Hand = "";
    public const string Screen = "";
    public const string Rail = "";
    public const string Size = "";
    public const string Timer = "";
    public const string Shield = "";
    public const string Power = "";
    public const string SignOut = "";
    public const string Logo = "";
    public const string Palette = "";
    public const string Switch = "";
    public const string Wrench = "";
    public const string Positive = "";
    public const string Critical = "";
}

/// A panel that lays children out left to right and wraps to the next line (for chips).
internal sealed class WrapPanel : Panel
{
    public double HorizontalSpacing { get; set; } = 6;
    public double VerticalSpacing { get; set; } = 6;

    protected override Size MeasureOverride(Size available)
    {
        double x = 0, y = 0, rowHeight = 0, widest = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(available.Width, available.Height));
            var s = child.DesiredSize;
            if (x > 0 && x + s.Width > available.Width)
            {
                x = 0;
                y += rowHeight + VerticalSpacing;
                rowHeight = 0;
            }
            x += s.Width + HorizontalSpacing;
            rowHeight = Math.Max(rowHeight, s.Height);
            widest = Math.Max(widest, x - HorizontalSpacing);
        }
        var width = double.IsInfinity(available.Width) ? widest : available.Width;
        return new Size(width, y + rowHeight);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double x = 0, y = 0, rowHeight = 0;
        foreach (var child in Children)
        {
            var s = child.DesiredSize;
            if (x > 0 && x + s.Width > final.Width)
            {
                x = 0;
                y += rowHeight + VerticalSpacing;
                rowHeight = 0;
            }
            child.Arrange(new Rect(x, y, Math.Min(s.Width, final.Width), s.Height));
            x += s.Width + HorizontalSpacing;
            rowHeight = Math.Max(rowHeight, s.Height);
        }
        return final;
    }
}

/// Small builders so every Settings page looks the same. Pages are built in code (they are rebuilt when
/// the settings change); every colour comes from a Db token through a style, every size from a type
/// style, and every control gets an AutomationId.
internal static class Sk
{
    private static bool _loaded;

    /// Loads SettingsResources.xaml into the app's resources (once).
    public static void EnsureResources()
    {
        if (_loaded) return;
        _loaded = true;
        Application.Current.Resources.MergedDictionaries.Add(
            new ResourceDictionary { Source = new Uri("ms-appx:///Views/Settings/SettingsResources.xaml") });
    }

    public static Style Sty(string key) => (Style)Application.Current.Resources[key];

    /// Sets the AutomationId (and, when given, the name a screen reader says).
    public static T Id<T>(this T element, string id, string? name = null) where T : DependencyObject
    {
        Auto.SetAutomationId(element, id);
        if (name is not null) Auto.SetName(element, name);
        return element;
    }

    // MARK: - Text

    public static TextBlock Text(string text, string style = "DbBodyStyle") =>
        new() { Text = text, Style = Sty(style), TextWrapping = TextWrapping.Wrap };

    /// Meta text: wraps (DbCaptionStyle alone would trim).
    public static TextBlock Caption(string text) =>
        new() { Text = text, Style = Sty("DbCaptionStyle"), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None };

    public static TextBlock PageTitle(string text)
    {
        var t = new TextBlock { Text = text, Style = Sty("DbPageTitleStyle"), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
        Auto.SetHeadingLevel(t, AutomationHeadingLevel.Level1);
        return t;
    }

    /// The heading above a group of rows.
    public static TextBlock Heading(string text)
    {
        var t = new TextBlock { Text = text, Style = Sty("DbSectionStyle") };
        Auto.SetHeadingLevel(t, AutomationHeadingLevel.Level2);
        return t;
    }

    /// Explanatory text under a group.
    public static TextBlock Note(string text)
    {
        var t = Caption(text);
        t.Margin = new Thickness(2, 8, 0, 0);
        t.MaxWidth = 680;
        t.HorizontalAlignment = HorizontalAlignment.Left;
        return t;
    }

    public static FontIcon Icon(string glyph, double size = 16, string style = "SkIconSecondary")
    {
        var icon = LogoTile.Glyph(glyph, size);
        icon.Style = Sty(style);
        icon.FontSize = size;
        return icon;
    }

    /// A service glyph (mail, chat, meeting, task, reminder) in its theme token.
    public static FontIcon SourceIcon(ItemSource source, double size = 16) =>
        Icon(source.Glyph(), size, "SkIconSource" + source switch
        {
            ItemSource.mail => "Mail",
            ItemSource.chat => "Chat",
            ItemSource.meeting => "Meeting",
            ItemSource.task => "Task",
            _ => "Reminder",
        });

    public static (string Glyph, string IconStyle, string TextStyle) Level(StatusLevel level) => level switch
    {
        StatusLevel.Positive => (Glyphs.Positive, "SkIconPositive", "SkTextPositive"),
        StatusLevel.Caution => (Glyphs.Warning, "SkIconCaution", "SkTextCaution"),
        StatusLevel.Critical => (Glyphs.Critical, "SkIconCritical", "SkTextCritical"),
        _ => (Glyphs.Info, "SkIconSecondary", "DbCaptionStyle"),
    };

    /// A line of status: an icon for the level, then words. Never colour-only.
    public static StackPanel StatusText(StatusLevel level, string text, double maxWidth = 560)
    {
        var (glyph, iconStyle, textStyle) = Level(level);
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var icon = Icon(glyph, 12, iconStyle);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 2, 0, 0);
        panel.Children.Add(icon);
        var t = new TextBlock { Text = text, Style = Sty(textStyle), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None, MaxWidth = maxWidth };
        panel.Children.Add(t);
        return panel;
    }

    // MARK: - Controls

    /// A button. `primary` is the one filled action on a surface; the rest are outlined pills.
    public static Button Btn(string text, Action click, string id, bool primary = false, bool enabled = true, string? name = null)
    {
        var b = new Button { Content = text, IsEnabled = enabled, Style = Sty(primary ? "DbPrimaryButtonStyle" : "DbSecondaryButtonStyle"), Tag = id };
        b.Id(id, name ?? text);
        b.Click += (_, _) => click();
        return b;
    }

    /// A quiet text action.
    public static Button QuietBtn(string text, Action click, string id, string? name = null)
    {
        var b = new Button { Content = text, Style = Sty("DbQuietButtonStyle"), Tag = id };
        b.Id(id, name ?? text);
        b.Click += (_, _) => click();
        return b;
    }

    /// A quiet icon-only button (a name and tooltip are required).
    public static Button IconBtn(string glyph, string name, string id, Action click, double iconSize = 12)
    {
        var b = new Button { Content = Icon(glyph, iconSize, "SkIconPrimary"), Style = Sty("DbIconButtonStyle"), Tag = id };
        b.Id(id, name);
        ToolTipService.SetToolTip(b, name);
        b.Click += (_, _) => click();
        return b;
    }

    public static HyperlinkButton Link(string text, Action click, string id, string? name = null, string? tooltip = null)
    {
        var b = new HyperlinkButton { Content = text, Padding = new Thickness(4, 2, 4, 2), Tag = id };
        b.Id(id, name ?? text);
        if (tooltip is not null) ToolTipService.SetToolTip(b, tooltip);
        b.Click += (_, _) => click();
        return b;
    }

    public static ToggleSwitch Switch(bool on, Action<bool> set, string name, string id)
    {
        var t = new ToggleSwitch { IsOn = on, MinWidth = 0, OnContent = "", OffContent = "", Tag = id };
        t.Id(id, name);
        t.Toggled += (_, _) => set(t.IsOn);
        return t;
    }

    public static ComboBox Combo<T>(IReadOnlyList<(string Text, T Value)> options, T selected, Action<T> set, string name, string id, double minWidth = 180)
    {
        var box = new ComboBox { MinWidth = minWidth, Tag = id };
        box.Id(id, name);
        var selectedIndex = -1;
        for (var i = 0; i < options.Count; i++)
        {
            box.Items.Add(options[i].Text);
            if (EqualityComparer<T>.Default.Equals(options[i].Value, selected)) selectedIndex = i;
        }
        box.SelectedIndex = selectedIndex;
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex >= 0 && box.SelectedIndex < options.Count) set(options[box.SelectedIndex].Value);
        };
        return box;
    }

    public static TextBox Field(string value, Action<string> changed, string name, string id, string? placeholder = null, double? width = null)
    {
        var box = new TextBox { Text = value, PlaceholderText = placeholder ?? "", Tag = id, MinWidth = 220 };
        if (width is { } w) { box.Width = w; box.MinWidth = 0; }
        box.Id(id, name);
        box.TextChanged += (_, _) => changed(box.Text);
        return box;
    }

    public static PasswordBox Secret(string value, Action<string> changed, string name, string id, string? placeholder = null)
    {
        var box = new PasswordBox { Password = value, PlaceholderText = placeholder ?? "", Tag = id, MinWidth = 220 };
        box.Id(id, name);
        box.PasswordChanged += (_, _) => changed(box.Password);
        return box;
    }

    /// Buttons side by side.
    public static StackPanel Buttons(params UIElement?[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        foreach (var b in buttons) if (b is not null) panel.Children.Add(b);
        return panel;
    }

    // MARK: - Rows (Community Toolkit SettingsCard / SettingsExpander, themed in SettingsResources.xaml)

    /// Rows stacked with a small gap, like the rows of one group in Windows Settings.
    public static StackPanel Group(params UIElement?[] rows)
    {
        var panel = new StackPanel { Spacing = 3 };
        foreach (var row in rows) if (row is not null) panel.Children.Add(row);
        return panel;
    }

    private static CornerRadius CardRadius => (CornerRadius)Application.Current.Resources["DbRadiusCard"];

    /// A raised card around any content (chips, a form) that isn't one setting.
    public static Border Block(UIElement child, Thickness? padding = null) =>
        new() { Style = Sty("DbCardStyle"), Child = child, Padding = padding ?? new Thickness(16, 14, 16, 14) };

    private static TextBlock RowTitle(string text) =>
        new() { Text = text, Style = Sty("DbBodyStyle"), TextWrapping = TextWrapping.Wrap };

    /// One settings row: optional icon, title, a line under it, and a control on the right.
    public static SettingsCard Row(string title, string? description = null, UIElement? control = null, string? glyph = null, string? id = null)
    {
        var card = new SettingsCard { Header = RowTitle(title), CornerRadius = CardRadius };
        if (description is not null) card.Description = Caption(description);
        if (glyph is not null) card.HeaderIcon = Icon(glyph, 16);
        if (control is not null) card.Content = control;
        if (id is not null) card.Id(id, title);
        return card;
    }

    /// A row whose description is any element (a status line, a fix button).
    public static SettingsCard RowOf(string title, UIElement? description, UIElement? control = null, string? glyph = null, string? id = null)
    {
        var card = new SettingsCard { Header = RowTitle(title), Description = description, CornerRadius = CardRadius };
        if (glyph is not null) card.HeaderIcon = Icon(glyph, 16);
        if (control is not null) card.Content = control;
        if (id is not null) card.Id(id, title);
        return card;
    }

    /// A row that leads with a token (or any element): the token, then title and a line under it.
    public static SettingsCard LeadRow(UIElement lead, string title, UIElement? sub, UIElement? control = null, string? id = null, string? name = null)
    {
        var grid = new Grid { ColumnSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (lead is FrameworkElement l) l.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(lead);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        text.Children.Add(RowTitle(title));
        if (sub is not null) text.Children.Add(sub);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var card = new SettingsCard { Header = grid, Padding = new Thickness(16, 10, 16, 10), CornerRadius = CardRadius };
        if (control is not null) card.Content = control;
        if (id is not null) card.Id(id, name ?? title);
        return card;
    }

    /// Makes a row a whole-row button with a chevron (a list row that opens a page).
    public static SettingsCard Clickable(this SettingsCard card, Action click)
    {
        card.IsClickEnabled = true;
        card.Click += (_, _) => click();
        return card;
    }

    /// One grouped card: a header row (title, line, optional switch) with the rows inside it.
    public static SettingsExpander Expander(string title, string? description, UIElement? control, string? glyph, string id,
                                            bool expanded, params SettingsCard[] items)
    {
        var e = new SettingsExpander { Header = RowTitle(title), IsExpanded = expanded, CornerRadius = CardRadius };
        if (description is not null) e.Description = Caption(description);
        if (glyph is not null) e.HeaderIcon = Icon(glyph, 16);
        if (control is not null) e.Content = control;
        foreach (var item in items) e.Items.Add(item);
        e.Id(id, title);
        return e;
    }

    // MARK: - Tokens

    /// The client token (the product's signature), 36 px on pages and 24 in lists, with an optional provider badge.
    /// The notch is cut with the colour of the surface the token sits on: cards are raised.
    public static FrameworkElement OwnerToken(AppState state, string owner, double size, int count = 0, Provider? badge = null,
                                              string surface = "DbSurfaceRaisedBrush")
    {
        var token = new TokenView(state.OwnerLogo(owner), state.OwnerName(owner), state.OwnerTint(owner), size, count,
            owner == BuddyItem.ReminderOwner);
        return Wrap(token, surface, size, badge);
    }

    /// A token for an account or client that has no items (not in ActiveOwners).
    public static FrameworkElement Token(LogoRef? logo, string name, AccountTint tint, double size, int count = 0, Provider? badge = null,
                                         bool isReminders = false, string surface = "DbSurfaceRaisedBrush")
    {
        var token = new TokenView(logo, name, tint, size, count, isReminders);
        return Wrap(token, surface, size, badge);
    }

    private static FrameworkElement Wrap(TokenView token, string surface, double size, Provider? badge)
    {
        void Cut() => token.CutBrush = Tokens.Brush(token, surface);
        token.Loaded += (_, _) => Cut();
        token.ActualThemeChanged += (_, _) => Cut();
        var grid = new Grid { Width = size + (badge is null ? 0 : 4), Height = size + (badge is null ? 0 : 4) };
        token.HorizontalAlignment = HorizontalAlignment.Left;
        token.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(token);
        if (badge is { } p)
        {
            var b = LogoTile.ProviderBadge(p, Math.Max(14, size * 0.38));
            b.HorizontalAlignment = HorizontalAlignment.Right;
            b.VerticalAlignment = VerticalAlignment.Bottom;
            grid.Children.Add(b);
        }
        return grid;
    }

    /// The account's token with its provider badge.
    public static FrameworkElement AccountToken(AppState state, AccountConfig a, double size) =>
        Token(state.OwnerLogo(a.Id), a.Label, a.Tint, size, 0, a.Provider);

    // MARK: - Colour picker and chips

    public static FrameworkElement TintPicker(AccountTint selected, Action<AccountTint> set, string name, string id)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var tint in AccountTintInfo.All)
        {
            var title = char.ToUpperInvariant(tint.ToString()[0]) + tint.ToString()[1..];
            var on = tint == selected;
            var grid = new Grid { Width = 28, Height = 28 };
            if (on) grid.Children.Add(new Ellipse { Width = 28, Height = 28, Style = Sty("SkRing") });
            grid.Children.Add(new Ellipse { Width = on ? 18 : 22, Height = on ? 18 : 22, Fill = Palette.Brush(tint) });
            if (on)
            {
                var ink = tint == AccountTint.yellow ? Palette.FromRgb(0x202020) : Colors.White;
                grid.Children.Add(LogoTile.Glyph(Glyphs.Check, 9, new SolidColorBrush(ink)));
            }
            var button = new Button
            {
                Content = grid,
                Padding = new Thickness(0),
                MinWidth = 0,
                MinHeight = 0,
                Width = 32,
                Height = 32,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(16),
                Tag = $"{id}-{tint}",
            };
            button.Id($"{id}_{tint}", $"{title}{(on ? ", selected" : "")}");
            ToolTipService.SetToolTip(button, title);
            var t = tint;
            button.Click += (_, _) => set(t);
            panel.Children.Add(button);
        }
        panel.Id(id, name);
        return panel;
    }

    /// A removable chip (a sender, keyword, label or chat).
    public static Border Chip(string text, string? glyph, Action remove, string removeName, string id)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        if (glyph is not null) row.Children.Add(Icon(glyph, 11));
        var label = new TextBlock { Text = text, Style = Sty("DbBodyStyle"), FontSize = 13, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(label);
        row.Children.Add(IconBtn(Glyphs.Close, removeName, id, remove, 9).With(b => { b.Width = 22; b.Height = 22; b.MinWidth = 0; b.MinHeight = 0; b.Padding = new Thickness(0); }));
        return new Border { Style = Sty("SkChip"), Height = 28, Padding = new Thickness(glyph is null ? 10 : 9, 0, 2, 0), Child = row };
    }

    /// A small quiet label ("Suggested").
    public static Border Pill(string text) =>
        new() { Style = Sty("SkChip"), Height = 22, Padding = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = text, Style = Sty("DbCaptionStyle"), TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center } };

    /// "9:00 AM" or "09:00".
    public static string HourText(int hour) => TimeText.Hour(hour);
}
