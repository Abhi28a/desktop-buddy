using DesktopBuddy.App.Ui;
using DesktopBuddy.Core;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using Windows.UI.Text;
using Colors = Microsoft.UI.Colors;

namespace DesktopBuddy.App.Views.DesignLab;

// P0 of windows/docs/UI-UX-PLAN.md: direction boards for the owner to choose from.
// Three visual directions (A Lily pad, B Ledger, C Beacon), each shown as a desktop scene
// (Peek, Rail, Board, tray) and a Settings Home page, in light and dark.
// THROWAWAY: deleted once a direction is chosen and T1 builds the real design system.
// Run: DesktopBuddy.exe --snapshot <folder> --designlab
internal static class DesignLab
{
    public static void Add(List<SnapshotScreen> screens)
    {
        foreach (var d in new[] { Direction.LilyPad, Direction.Ledger, Direction.Beacon })
        {
            var key = d switch { Direction.LilyPad => "a-lilypad", Direction.Ledger => "b-ledger", _ => "c-beacon" };
            screens.Add(new SnapshotScreen($"lab-{key}-desktop", () => new Scene(d).Desktop(), 1440, 900));
            screens.Add(new SnapshotScreen($"lab-{key}-settings", () => new Scene(d).SettingsHome(), 1120, 720));
        }
    }
}

internal enum Direction { LilyPad, Ledger, Beacon }

/// Colours for one direction in one theme. Named by purpose, as the real tokens will be.
internal sealed record Look(
    Color WallTop, Color WallBottom, Color Taskbar,
    Color Surface, Color SurfaceRaised, Color Sunken, Color Stroke,
    Color Text, Color Text2, Color Text3,
    Color Accent, Color OnAccent, Color Waiting, Color Urgent, Color Positive, Color Caution,
    Color Inverse, Color OnInverse, Color OnInverse2, Color Shadow);

internal sealed class Scene
{
    private readonly Direction _d;
    private readonly AppState _state = AppState.Shared;
    private Look _k = null!;
    private bool _dark;

    public Scene(Direction d) => _d = d;

    private static Color C(uint rgb, byte a = 255) => Palette.FromRgb(rgb, a);
    private static SolidColorBrush B(Color c) => new(c);

    private Look LookFor(bool dark) => (_d, dark) switch
    {
        // A. Lily pad: warm paper and ink, lily teal accent, coral for "waiting".
        (Direction.LilyPad, false) => new Look(
            C(0xD9E4E3), C(0xEDE7DD), C(0xF3F1EC, 235),
            C(0xFAFAF8), C(0xFFFFFF), C(0xF2F0EB), C(0x1A1714, 22),
            C(0x1A1714), C(0x5E5852), C(0x8D867E),
            C(0x137A82), C(0xFFFFFF), C(0xE0684A), C(0x137A82), C(0x2E8B6A), C(0xB8741A),
            C(0x1A1714), C(0xFAF7F2), C(0xB9B0A6), C(0x2A1A10, 40)),
        (Direction.LilyPad, true) => new Look(
            C(0x1A2426), C(0x221C18), C(0x1D1B19, 240),
            C(0x1E1C1A), C(0x262421), C(0x171614), C(0xFFF6EA, 22),
            C(0xF4EFE8), C(0xB5ADA3), C(0x857D74),
            C(0x4FC0C4), C(0x0E1A1B), C(0xF08A6C), C(0x4FC0C4), C(0x5CC59B), C(0xE5A84B),
            C(0xF4EFE8), C(0x1A1714), C(0x6E655C), C(0x000000, 90)),

        // B. Ledger: near-monochrome, hairlines, one coral signal.
        (Direction.Ledger, false) => new Look(
            C(0xE6E6E3), C(0xF0EFEB), C(0xF5F5F3, 240),
            C(0xFFFFFF), C(0xFFFFFF), C(0xF6F6F4), C(0x000000, 26),
            C(0x111111), C(0x55554F), C(0x8E8E87),
            C(0x111111), C(0xFFFFFF), C(0xD9542E), C(0xD9542E), C(0x2F7D4F), C(0xA86A12),
            C(0x111111), C(0xFFFFFF), C(0xA9A9A3), C(0x000000, 30)),
        (Direction.Ledger, true) => new Look(
            C(0x1C1C1B), C(0x232321), C(0x191918, 245),
            C(0x161615), C(0x1C1C1B), C(0x121211), C(0xFFFFFF, 26),
            C(0xEDEDEA), C(0xA8A8A2), C(0x75756F),
            C(0xEDEDEA), C(0x111111), C(0xF0754F), C(0xF0754F), C(0x6CC08F), C(0xE0A54B),
            C(0xEDEDEA), C(0x111111), C(0x6F6F69), C(0x000000, 90)),

        // C. Beacon: dark-first glass, each client's colour as a light bar.
        (Direction.Beacon, false) => new Look(
            C(0xC9D3E6), C(0xE4E0EE), C(0xEEEEF4, 230),
            C(0xF7F7FB, 240), C(0xFFFFFF), C(0xEDEDF4), C(0x14142A, 24),
            C(0x14142A), C(0x55556E), C(0x8A8AA0),
            C(0x5B4BDB), C(0xFFFFFF), C(0xFF5C7A), C(0x5B4BDB), C(0x18A874), C(0xC98A0C),
            C(0x101020), C(0xF2F2FA), C(0x9A9AB5), C(0x1B1450, 50)),
        (_, _) => new Look(
            C(0x0D0F1F), C(0x1A1030), C(0x101124, 235),
            C(0x16182B, 235), C(0x1E2036), C(0x101222), C(0xFFFFFF, 26),
            C(0xF0F0FA), C(0xA8A8C4), C(0x6E6E8C),
            C(0x8C7DFF), C(0x0D0D1F), C(0xFF6B88), C(0x8C7DFF), C(0x3DD9A0), C(0xF2B84B),
            C(0x07081A), C(0xF0F0FA), C(0x7C7C9C), C(0x000000, 120)),
    };

    // MARK: - Building blocks

    private TextBlock Text(string text, double size, Color color, FontWeight? weight = null, bool display = false, bool tabular = false, int lines = 1)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = size,
            Foreground = B(color),
            FontWeight = weight ?? FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = lines > 1 ? TextWrapping.WrapWholeWords : TextWrapping.NoWrap,
            MaxLines = lines,
        };
        if (display) t.FontFamily = new FontFamily("Segoe UI Variable Display");
        if (tabular) Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }

    private static FontIcon Glyph(string g, double size, Color color) =>
        new() { Glyph = g, FontSize = size, Foreground = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center };

    /// A soft, two-layer shadow behind a panel (RenderTargetBitmap can't draw ThemeShadow).
    private Grid Elevated(FrameworkElement panel, double radius, double depth = 10)
    {
        var g = new Grid();
        g.Children.Add(new Border { CornerRadius = new CornerRadius(radius + 2), Background = B(Color.FromArgb((byte)(_k.Shadow.A / 2), _k.Shadow.R, _k.Shadow.G, _k.Shadow.B)), Margin = new Thickness(-1, depth * 0.4, -1, -depth * 0.9) });
        g.Children.Add(new Border { CornerRadius = new CornerRadius(radius + 1), Background = B(Color.FromArgb((byte)(_k.Shadow.A / 3), _k.Shadow.R, _k.Shadow.G, _k.Shadow.B)), Margin = new Thickness(-0.5, depth * 0.15, -0.5, -depth * 0.35) });
        g.Children.Add(panel);
        return g;
    }

    private record Owner(string Id, string Name, LogoRef? Logo, AccountTint Tint, bool Reminders, List<BuddyItem> Items);

    private List<Owner> Owners() =>
        _state.ActiveOwners.Select(o => new Owner(o, _state.OwnerName(o), _state.OwnerLogo(o), _state.OwnerTint(o),
            o == BuddyItem.ReminderOwner, _state.Items.Where(i => i.Owner == o).ToList())).ToList();

    private static string ServiceName(BuddyItem i) => i.Source switch
    {
        ItemSource.mail => "Gmail",
        ItemSource.chat => "Google Chat",
        ItemSource.meeting => "Calendar",
        ItemSource.task => "Tasks",
        _ => "Reminder",
    };

    private string Ago(BuddyItem i)
    {
        var m = (int)Math.Max(1, (_state.Now() - i.Date).TotalMinutes);
        return m < 60 ? $"{m}m" : $"{m / 60}h";
    }

    /// The client's logo in the direction's token shape.
    private FrameworkElement Token(Owner o, double size, int count = 0, bool urgent = false, bool selected = false)
    {
        var host = new Grid { Width = size, Height = size };
        switch (_d)
        {
            case Direction.LilyPad:
            {
                // A soft round pad; the count sits in a notch cut into the pad's edge.
                // The pad carries a soft ring in the client's colour, so the notch visibly cuts into it.
                var ringTint = o.Reminders ? _k.Accent : Palette.Tint(o.Tint);
                var pad = new Border
                {
                    CornerRadius = new CornerRadius(size / 2),
                    Background = B(o.Reminders ? _k.Accent : Colors.White),
                    BorderBrush = B(Color.FromArgb(_dark ? (byte)150 : (byte)110, ringTint.R, ringTint.G, ringTint.B)),
                    BorderThickness = new Thickness(Math.Max(1.5, size * 0.05)),
                };
                host.Children.Add(pad);
                if (o.Reminders) host.Children.Add(Glyph("", size * 0.42, _k.OnAccent).With(h => h.HorizontalAlignment = HorizontalAlignment.Center));
                else if (LogoTile.Load(o.Logo) is { } img) host.Children.Add(new Image { Source = img, Margin = new Thickness(size * 0.2), Stretch = Stretch.Uniform });
                else host.Children.Add(Text(Logos.Monogram(o.Name), size * 0.36, Palette.Tint(o.Tint), FontWeights.SemiBold).With(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; }));
                if (urgent)
                {
                    var ring = new Ellipse { Width = size + 8, Height = size + 8, Stroke = B(_k.Urgent), StrokeThickness = 2, Margin = new Thickness(-4), StrokeDashArray = [Math.PI * (size + 6) * 0.68 / 2, 1000] };
                    ring.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
                    ring.RenderTransform = new RotateTransform { Angle = -90 };
                    host.Children.Add(ring);
                }
                if (count > 0)
                {
                    var notch = new Border
                    {
                        Background = B(_k.Waiting),
                        BorderBrush = B(selected ? _k.SurfaceRaised : _k.Surface),
                        // A thick border in the surrounding colour cuts the notch out of the pad's rim.
                        BorderThickness = new Thickness(Math.Max(3, size * 0.09)),
                        CornerRadius = new CornerRadius(size * 0.26),
                        MinWidth = size * 0.62,
                        Height = size * 0.52,
                        Padding = new Thickness(size * 0.05, 0, size * 0.05, 0),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(0, 0, -size * 0.2, -size * 0.1),
                        Child = Text(count.ToString(), Math.Max(10, size * 0.27), Colors.White, FontWeights.SemiBold, tabular: true).With(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 0, size * 0.02); }),
                    };
                    host.Children.Add(notch);
                }
                break;
            }
            case Direction.Ledger:
            {
                // A plain square logo; counts are set as numbers beside it, not as badges.
                var tile = new Border { CornerRadius = new CornerRadius(size * 0.18), Background = B(o.Reminders ? _k.Text : Colors.White), BorderBrush = B(_k.Stroke), BorderThickness = new Thickness(1) };
                host.Children.Add(tile);
                if (o.Reminders) host.Children.Add(Glyph("", size * 0.46, _k.Inverse == _k.Text ? _k.OnInverse : Colors.White).With(h => h.HorizontalAlignment = HorizontalAlignment.Center));
                else if (LogoTile.Load(o.Logo) is { } img) host.Children.Add(new Image { Source = img, Margin = new Thickness(size * 0.14) });
                break;
            }
            default:
            {
                // Rounded square on glass, with the client's colour as a light bar on its outer edge.
                var tint = Palette.Tint(o.Reminders ? AccountTint.teal : o.Tint);
                host.Children.Add(new Border { CornerRadius = new CornerRadius(size * 0.26), Background = B(o.Reminders ? tint : Colors.White) });
                if (o.Reminders) host.Children.Add(Glyph("", size * 0.44, Colors.White).With(h => h.HorizontalAlignment = HorizontalAlignment.Center));
                else if (LogoTile.Load(o.Logo) is { } img) host.Children.Add(new Image { Source = img, Margin = new Thickness(size * 0.16) });
                if (count > 0)
                {
                    host.Children.Add(new Border
                    {
                        Width = size * 0.3, Height = size * 0.3, CornerRadius = new CornerRadius(size * 0.15),
                        Background = B(_k.Waiting), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, -size * 0.08, -size * 0.08, 0),
                        BorderBrush = B(_k.Inverse), BorderThickness = new Thickness(2),
                    });
                }
                break;
            }
        }
        AutomationProperties.SetName(host, o.Name);
        return host;
    }

    private Button Action(string label, bool primary, Color? fg = null, Color? bg = null)
    {
        var b = new Button
        {
            Content = label,
            Padding = new Thickness(12, 5, 12, 6),
            FontSize = 13,
            CornerRadius = new CornerRadius(_d == Direction.LilyPad ? 14 : _d == Direction.Ledger ? 3 : 8),
            Foreground = B(fg ?? (primary ? _k.OnAccent : _k.Text)),
            Background = B(bg ?? (primary ? _k.Accent : Colors.Transparent)),
            BorderThickness = new Thickness(primary ? 0 : 1),
            BorderBrush = B(_k.Stroke),
        };
        return b;
    }

    private FrameworkElement Keycap(string key)
    {
        return new Border
        {
            BorderBrush = B(_k.Stroke), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 0, 5, 1), MinWidth = 20, Background = B(_k.Sunken),
            Child = Text(key, 11, _k.Text2, FontWeights.SemiBold).With(t => t.HorizontalAlignment = HorizontalAlignment.Center),
        };
    }

    // MARK: - Desktop scene

    public FrameworkElement Desktop()
    {
        var root = new Grid();
        root.Loaded += (_, _) => { }; // theme known only after attach; we build for both by reading RequestedTheme below
        root.Children.Add(new ThemeProbe(dark =>
        {
            _dark = dark;
            _k = LookFor(dark);
            return BuildDesktop();
        }));
        return root;
    }

    public FrameworkElement SettingsHome()
    {
        var root = new Grid();
        root.Children.Add(new ThemeProbe(dark =>
        {
            _dark = dark;
            _k = LookFor(dark);
            return BuildSettings();
        }));
        return root;
    }

    private FrameworkElement BuildDesktop()
    {
        var canvas = new Canvas { Width = 1440, Height = 900 };
        canvas.Background = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 1),
            GradientStops = { new GradientStop { Color = _k.WallTop, Offset = 0 }, new GradientStop { Color = _k.WallBottom, Offset = 1 } },
        };

        // A few faint "windows" so the scene reads as a real desktop.
        Place(canvas, new Border { Width = 760, Height = 520, CornerRadius = new CornerRadius(8), Background = B(Color.FromArgb(_dark ? (byte)70 : (byte)120, _k.Surface.R, _k.Surface.G, _k.Surface.B)), BorderBrush = B(_k.Stroke), BorderThickness = new Thickness(1) }, 120, 150);
        Place(canvas, Text(_d switch { Direction.LilyPad => "A · Lily pad", Direction.Ledger => "B · Ledger", _ => "C · Beacon" }, 40, Color.FromArgb(70, _k.Text.R, _k.Text.G, _k.Text.B), FontWeights.SemiBold, display: true), 160, 190);

        var owners = Owners();
        var lead = _state.Items.OrderByDescending(i => i.Date).First(i => i.Source == ItemSource.mail);

        // Peek: top centre.
        Place(canvas, Peek(owners.First(o => o.Id == lead.Owner), lead, owners.Sum(o => o.Items.Count) - 1), (1440 - 400) / 2.0, 14);

        // Rail: right edge.
        var rail = Rail(owners);
        Place(canvas, rail, 1440 - 68, 150);

        // Board: above the tray, left of the Rail.
        Place(canvas, Board(owners), 1440 - 80 - 404, 900 - 48 - 12 - 560);

        // Taskbar with the tray.
        var bar = new Grid { Width = 1440, Height = 48, Background = B(_k.Taskbar) };
        bar.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Background = B(_k.Stroke) });
        var centre = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        foreach (var g in new[] { "", "", "", "" })
            centre.Children.Add(new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(4), Child = Glyph(g, 18, _k.Text2).With(i => i.HorizontalAlignment = HorizontalAlignment.Center) });
        bar.Children.Add(centre);
        var tray = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        var mine = new Grid { Width = 32, Height = 32, CornerRadius = new CornerRadius(4), Background = B(Color.FromArgb(_dark ? (byte)40 : (byte)18, _k.Text.R, _k.Text.G, _k.Text.B)) };
        mine.Children.Add(new LogoTile(new LogoRef(null, "dynamatix"), "Desktop Buddy", AccountTint.graphite, 18).With(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; }));
        mine.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = B(_k.Waiting), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 4, 0) });
        tray.Children.Add(mine);
        tray.Children.Add(Glyph("", 15, _k.Text2));
        tray.Children.Add(Glyph("", 15, _k.Text2));
        var clock = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        clock.Children.Add(Text("2:14 PM", 12, _k.Text, tabular: true).With(t => t.HorizontalAlignment = HorizontalAlignment.Right));
        clock.Children.Add(Text("14/11/2026", 12, _k.Text, tabular: true).With(t => t.HorizontalAlignment = HorizontalAlignment.Right));
        tray.Children.Add(clock);
        bar.Children.Add(tray);
        Place(canvas, bar, 0, 900 - 48);
        return canvas;
    }

    private static void Place(Canvas c, UIElement e, double x, double y)
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        c.Children.Add(e);
    }

    // MARK: Peek

    private FrameworkElement Peek(Owner owner, BuddyItem item, int more)
    {
        var inverse = _d != Direction.Ledger;   // A and C use the inverse (dark) card; B stays on paper
        var bg = _d switch { Direction.Ledger => _k.SurfaceRaised, Direction.Beacon => _k.Inverse, _ => _k.Inverse };
        var fg = inverse ? _k.OnInverse : _k.Text;
        var fg2 = inverse ? _k.OnInverse2 : _k.Text2;

        var card = new Grid { Width = 400, Padding = new Thickness(14, 14, 14, 12), ColumnSpacing = 12, RowSpacing = 10 };
        card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        card.ColumnDefinitions.Add(new ColumnDefinition());
        card.RowDefinitions.Add(new RowDefinition());
        card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var saved = _k;
        if (inverse) _k = _k with { Surface = bg, SurfaceRaised = bg };
        var token = Token(owner, 44, owner.Items.Count);
        _k = saved;
        card.Children.Add(token);

        var text = new StackPanel { Spacing = 1 };
        var meta = new Grid();
        meta.Children.Add(Text($"{owner.Name}  ·  {ServiceName(item)}", 12, fg2));
        meta.Children.Add(Text(Ago(item), 12, fg2, tabular: true).With(t => t.HorizontalAlignment = HorizontalAlignment.Right));
        text.Children.Add(meta);
        text.Children.Add(Text(item.From ?? "", 16, fg, FontWeights.SemiBold, display: true).With(t => t.Margin = new Thickness(0, 2, 0, 0)));
        text.Children.Add(Text(item.Title, 14, fg));
        text.Children.Add(Text(item.Detail ?? "", 13, fg2));
        Grid.SetColumn(text, 1);
        card.Children.Add(text);

        var actions = new Grid();
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        left.Children.Add(Action("Open", true, bg: _d == Direction.Ledger ? _k.Text : _k.Accent, fg: _d == Direction.Ledger ? _k.SurfaceRaised : _k.OnAccent));
        left.Children.Add(Action("Done", false, fg: fg).With(b => b.BorderBrush = B(Color.FromArgb(50, fg.R, fg.G, fg.B))));
        left.Children.Add(Action("Later", false, fg: fg).With(b => b.BorderBrush = B(Color.FromArgb(50, fg.R, fg.G, fg.B))));
        actions.Children.Add(left);
        if (more > 0)
            actions.Children.Add(Text($"{more} more waiting", 12, fg2).With(t => { t.HorizontalAlignment = HorizontalAlignment.Right; t.VerticalAlignment = VerticalAlignment.Center; }));
        Grid.SetRow(actions, 1);
        Grid.SetColumnSpan(actions, 2);
        card.Children.Add(actions);

        var shell = new Grid();
        var radius = _d == Direction.Ledger ? 6 : _d == Direction.LilyPad ? 14 : 12;
        shell.Children.Add(new Border { CornerRadius = new CornerRadius(radius), Background = B(bg), BorderBrush = B(_d == Direction.Ledger ? _k.Stroke : Color.FromArgb(30, 255, 255, 255)), BorderThickness = new Thickness(1) });
        if (_d == Direction.Beacon)
            shell.Children.Add(new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 14), CornerRadius = new CornerRadius(0, 2, 2, 0), Background = B(Palette.Tint(owner.Tint)) });
        shell.Children.Add(card);
        // The time left: a hairline along the bottom edge (not a spinner).
        var timeBar = new Grid { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(radius, 0, radius, 0) };
        timeBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62, GridUnitType.Star) });
        timeBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38, GridUnitType.Star) });
        timeBar.Children.Add(new Border { CornerRadius = new CornerRadius(1), Background = B(_d == Direction.Ledger ? _k.Waiting : _k.Accent) });
        shell.Children.Add(timeBar);
        return Elevated(shell, radius, 14);
    }

    // MARK: Rail

    private FrameworkElement Rail(List<Owner> owners)
    {
        var stack = new StackPanel { Spacing = _d == Direction.Ledger ? 14 : 12, Padding = new Thickness(0, 12, 0, 12) };
        foreach (var o in owners)
        {
            var urgent = o.Items.Any(i => i.Source == ItemSource.meeting);
            if (_d == Direction.Ledger)
            {
                var cell = new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center };
                cell.Children.Add(Token(o, 30));
                cell.Children.Add(Text(o.Items.Count.ToString(), 12, urgent ? _k.Urgent : _k.Text, FontWeights.SemiBold, tabular: true).With(t => t.HorizontalAlignment = HorizontalAlignment.Center));
                stack.Children.Add(cell);
            }
            else if (_d == Direction.Beacon)
            {
                var row = new Grid { Width = 56, Height = 40 };
                row.Children.Add(Token(o, 36, o.Items.Count).With(t => t.HorizontalAlignment = HorizontalAlignment.Center));
                row.Children.Add(new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 6), CornerRadius = new CornerRadius(2, 0, 0, 2), Background = B(Palette.Tint(o.Reminders ? AccountTint.teal : o.Tint)) });
                stack.Children.Add(row);
            }
            else
            {
                stack.Children.Add(Token(o, 40, o.Items.Count, urgent: urgent).With(t => t.HorizontalAlignment = HorizontalAlignment.Center));
            }
        }

        if (_d == Direction.LilyPad)
        {
            // A floating pill; the hovered token shows its name beside it.
            var shell = new Grid { Width = 60 };
            shell.Children.Add(new Border { CornerRadius = new CornerRadius(30), Background = B(_k.Surface), BorderBrush = B(_k.Stroke), BorderThickness = new Thickness(1) });
            shell.Children.Add(stack);
            var outer = new Grid();
            outer.Children.Add(Elevated(shell, 30, 8));
            var tip = new Border
            {
                CornerRadius = new CornerRadius(8), Background = B(_k.Inverse), Padding = new Thickness(10, 5, 10, 6),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(-118, 12 + 52 + 10, 0, 0),
                Child = Text("RiskHawk · 1 waiting", 12, _k.OnInverse),
            };
            outer.Children.Add(tip);
            return outer;
        }
        var flush = new Grid { Width = _d == Direction.Ledger ? 56 : 60 };
        flush.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(_d == Direction.Ledger ? 4 : 14, 0, 0, _d == Direction.Ledger ? 4 : 14),
            Background = B(_d == Direction.Beacon ? _k.Inverse : _k.Surface),
            BorderBrush = B(_k.Stroke), BorderThickness = new Thickness(1, 1, 0, 1),
        });
        flush.Children.Add(stack);
        return Elevated(flush, _d == Direction.Ledger ? 4 : 14, 6).With(g => g.Margin = new Thickness(0, 0, -8, 0));
    }

    // MARK: Board

    private FrameworkElement Board(List<Owner> owners)
    {
        const double width = 404, height = 560;
        var panel = new Grid { Width = width, Height = height };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition());
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var dark = _d == Direction.Beacon;
        var surface = dark ? _k.Inverse : _k.Surface;
        var fg = dark ? _k.OnInverse : _k.Text;
        var fg2 = dark ? _k.OnInverse2 : _k.Text2;

        // Header
        var head = new Grid { Padding = new Thickness(18, 16, 14, 10) };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        title.Children.Add(Text("Waiting", 20, fg, FontWeights.SemiBold, display: true));
        var total = owners.Sum(o => o.Items.Count);
        title.Children.Add(_d == Direction.LilyPad
            ? new Border { Background = B(_k.Waiting), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 1, 8, 2), VerticalAlignment = VerticalAlignment.Center, Child = Text(total.ToString(), 12, Colors.White, FontWeights.SemiBold, tabular: true) }
            : Text(total.ToString(), 20, _d == Direction.Ledger ? _k.Waiting : _k.Accent, FontWeights.SemiBold, display: true, tabular: true));
        head.Children.Add(title);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
        tools.Children.Add(new Button { Content = "Clear all", FontSize = 13, Background = B(Colors.Transparent), BorderThickness = new Thickness(0), Foreground = B(fg2), Padding = new Thickness(8, 4, 8, 5) });
        tools.Children.Add(new Button { Content = Glyph("", 14, fg2), Background = B(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(8, 6, 8, 6) }.With(b => AutomationProperties.SetName(b, "Settings")));
        head.Children.Add(tools);
        panel.Children.Add(head);

        // Problem line (account health leaves the glance surfaces; one line here).
        var issue = new Grid { Margin = new Thickness(14, 0, 14, 8), Padding = new Thickness(12, 8, 8, 8), ColumnSpacing = 10 };
        issue.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        issue.ColumnDefinitions.Add(new ColumnDefinition());
        issue.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        issue.Children.Insert(0, new Border { CornerRadius = new CornerRadius(_d == Direction.Ledger ? 3 : 8), Background = B(Color.FromArgb(dark ? (byte)40 : (byte)26, _k.Caution.R, _k.Caution.G, _k.Caution.B)) }.With(b => { Grid.SetColumnSpan(b, 3); b.Margin = new Thickness(-12, -8, -8, -8); }));
        issue.Children.Add(Glyph("", 14, _k.Caution));
        issue.Children.Add(Text("IE Hub: Google Chat isn't working", 13, fg).With(t => { Grid.SetColumn(t, 1); t.VerticalAlignment = VerticalAlignment.Center; }));
        issue.Children.Add(new HyperlinkButton { Content = "Fix", FontSize = 13, Padding = new Thickness(6, 2, 6, 3), Foreground = B(_d == Direction.Ledger ? _k.Text : _k.Accent) }.With(h => Grid.SetColumn(h, 2)));
        Grid.SetRow(issue, 1);
        panel.Children.Add(issue);

        // The list: grouped by client, rows not cards.
        var list = new StackPanel { Padding = new Thickness(0, 0, 0, 8) };
        var first = true;
        foreach (var o in owners)
        {
            var group = new Grid { Padding = new Thickness(18, first ? 4 : 14, 14, 6), ColumnSpacing = 10 };
            first = false;
            group.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            group.ColumnDefinitions.Add(new ColumnDefinition());
            group.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var savedK = _k;
            if (dark) _k = _k with { Surface = surface };
            group.Children.Add(Token(o, _d == Direction.Ledger ? 20 : 24));
            _k = savedK;
            group.Children.Add(Text(_d == Direction.Ledger ? o.Name.ToUpperInvariant() == o.Name ? o.Name : o.Name : o.Name, 13, fg, FontWeights.SemiBold).With(t => { Grid.SetColumn(t, 1); t.VerticalAlignment = VerticalAlignment.Center; }));
            group.Children.Add(Text(o.Items.Count == 1 ? "1" : o.Items.Count.ToString(), 12, fg2, tabular: true).With(t => { Grid.SetColumn(t, 2); t.VerticalAlignment = VerticalAlignment.Center; }));
            list.Children.Add(group);
            if (_d == Direction.Ledger) list.Children.Add(new Border { Height = 1, Margin = new Thickness(18, 0, 14, 0), Background = B(_k.Stroke) });

            foreach (var it in o.Items)
            {
                var selected = it.Id == "s1";
                var row = new Grid { Padding = new Thickness(18, 7, 14, 8), ColumnSpacing = 10 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_d == Direction.Ledger ? 34 : 24) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                if (selected)
                {
                    // The selection sits in the row's own padding, so it never covers the icon or time.
                    var sel = new Border { CornerRadius = new CornerRadius(_d == Direction.Ledger ? 2 : 8), Margin = new Thickness(-12, -6, -8, -7), Background = B(Color.FromArgb(dark ? (byte)46 : (byte)22, _k.Accent.R, _k.Accent.G, _k.Accent.B)) };
                    Grid.SetColumnSpan(sel, 3);
                    row.Children.Add(sel);
                    row.Children.Add(new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-12, 3, 0, 3), CornerRadius = new CornerRadius(2), Background = B(_k.Accent) }.With(b => Grid.SetColumnSpan(b, 3)));
                }
                if (_d == Direction.Ledger)
                    row.Children.Add(Text(Ago(it), 12, fg2, tabular: true).With(t => t.Margin = new Thickness(0, 1, 0, 0)));
                else
                    row.Children.Add(Glyph(it.Source.Glyph(), 14, Palette.Tint(it.Source)).With(g => { g.VerticalAlignment = VerticalAlignment.Top; g.Margin = new Thickness(0, 2, 0, 0); g.HorizontalAlignment = HorizontalAlignment.Left; }));
                var body = new StackPanel { Spacing = 1 };
                body.Children.Add(Text(it.From ?? it.Title, 14, fg, FontWeights.SemiBold));
                body.Children.Add(Text(it.From is null ? (it.Detail ?? "") : it.Title, 13, fg2));
                Grid.SetColumn(body, 1);
                row.Children.Add(body);
                if (selected)
                {
                    var acts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
                    acts.Children.Add(new Button { Content = Glyph("", 14, fg2), Background = B(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(7) });
                    acts.Children.Add(new Button { Content = Glyph("", 14, fg2), Background = B(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(7) });
                    Grid.SetColumn(acts, 2);
                    row.Children.Add(acts);
                }
                else if (_d != Direction.Ledger)
                {
                    row.Children.Add(Text(Ago(it), 12, it.Source == ItemSource.meeting ? _k.Urgent : fg2, it.Source == ItemSource.meeting ? FontWeights.SemiBold : FontWeights.Normal, tabular: true).With(t => { Grid.SetColumn(t, 2); t.VerticalAlignment = VerticalAlignment.Top; t.Margin = new Thickness(0, 2, 0, 0); }));
                }
                list.Children.Add(row);
            }
        }
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        Grid.SetRow(scroll, 2);
        panel.Children.Add(scroll);

        // Keyboard hints.
        var foot = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Padding = new Thickness(18, 10, 14, 12) };
        foreach (var (k, label) in new[] { ("↑↓", "Move"), ("Enter", "Open"), ("E", "Done"), ("S", "Later") })
        {
            var pair = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var savedK = _k;
            if (dark) _k = _k with { Sunken = Color.FromArgb(40, 255, 255, 255), Text2 = _k.OnInverse2, Stroke = Color.FromArgb(40, 255, 255, 255) };
            pair.Children.Add(Keycap(k));
            _k = savedK;
            pair.Children.Add(Text(label, 12, fg2).With(t => t.VerticalAlignment = VerticalAlignment.Center));
            foot.Children.Add(pair);
        }
        Grid.SetRow(foot, 3);
        panel.Children.Add(foot);
        if (_d != Direction.Beacon)
            panel.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Background = B(_k.Stroke) }.With(b => Grid.SetRow(b, 3)));

        var radius = _d == Direction.Ledger ? 6 : _d == Direction.LilyPad ? 14 : 12;
        var shell = new Grid();
        shell.Children.Add(new Border { CornerRadius = new CornerRadius(radius), Background = B(surface), BorderBrush = B(dark ? Color.FromArgb(30, 255, 255, 255) : _k.Stroke), BorderThickness = new Thickness(1) });
        shell.Children.Add(panel);
        return Elevated(shell, radius, 18);
    }

    // MARK: - Settings Home

    private FrameworkElement BuildSettings()
    {
        var root = new Grid { Width = 1120, Height = 720, Background = B(_k.Sunken) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(248) });
        root.ColumnDefinitions.Add(new ColumnDefinition());

        // Sidebar: six sections only.
        var nav = new StackPanel { Padding = new Thickness(12, 14, 12, 12), Spacing = 2 };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(6, 0, 0, 22) };
        brand.Children.Add(new LogoTile(new LogoRef(null, "dynamatix"), "Desktop Buddy", AccountTint.graphite, 22));
        brand.Children.Add(Text("Desktop Buddy", 13, _k.Text2).With(t => t.VerticalAlignment = VerticalAlignment.Center));
        nav.Children.Add(brand);
        var items = new[] { ("", "Home"), ("", "Accounts"), ("", "Clients"), ("", "Notifications"), ("", "Reminders"), ("", "About") };
        foreach (var (g, label) in items)
        {
            var on = label == "Home";
            var row = new Grid { Height = 38, Padding = new Thickness(12, 0, 10, 0), ColumnSpacing = 14, CornerRadius = new CornerRadius(_d == Direction.Ledger ? 3 : _d == Direction.LilyPad ? 10 : 6) };
            if (on) row.Background = B(_d == Direction.Ledger ? _k.Surface : Color.FromArgb(_dark ? (byte)36 : (byte)20, _k.Accent.R, _k.Accent.G, _k.Accent.B));
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(Glyph(g, 16, on ? (_d == Direction.Ledger ? _k.Text : _k.Accent) : _k.Text2));
            row.Children.Add(Text(label, 14, _k.Text, on ? FontWeights.SemiBold : FontWeights.Normal).With(t => { Grid.SetColumn(t, 1); t.VerticalAlignment = VerticalAlignment.Center; }));
            if (label == "Accounts")
                row.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = B(_k.Caution), HorizontalAlignment = HorizontalAlignment.Right }.With(e => Grid.SetColumn(e, 1)));
            if (on && _d != Direction.Ledger)
                row.Children.Add(new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(2), Background = B(_k.Accent), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-12, 0, 0, 0) });
            nav.Children.Add(row);
        }
        root.Children.Add(nav);

        // Content
        var page = new Border
        {
            Background = B(_k.Surface),
            CornerRadius = new CornerRadius(_d == Direction.Ledger ? 0 : 12, 0, 0, 0),
            BorderBrush = B(_k.Stroke), BorderThickness = new Thickness(1, 1, 0, 0),
        };
        Grid.SetColumn(page, 1);
        var content = new StackPanel { Padding = new Thickness(40, 34, 40, 32), Spacing = 0, MaxWidth = 780, HorizontalAlignment = HorizontalAlignment.Left };
        content.Children.Add(Text("Home", 28, _k.Text, FontWeights.SemiBold, display: true));

        var owners = Owners();
        var total = owners.Sum(o => o.Items.Count);

        // Status hero: the real numbers, said plainly.
        var hero = new Grid { Margin = new Thickness(0, 22, 0, 0), Padding = new Thickness(22, 20, 22, 20), ColumnSpacing = 24 };
        hero.ColumnDefinitions.Add(new ColumnDefinition());
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hero.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(_d == Direction.Ledger ? 4 : 12),
            Background = B(_d == Direction.Beacon ? _k.Inverse : _k.SurfaceRaised),
            BorderBrush = B(_k.Stroke), BorderThickness = new Thickness(1),
            Margin = new Thickness(-22, -20, -22, -20),
        }.With(b => Grid.SetColumnSpan(b, 2)));
        var heroFg = _d == Direction.Beacon ? _k.OnInverse : _k.Text;
        var heroFg2 = _d == Direction.Beacon ? _k.OnInverse2 : _k.Text2;
        var left = new StackPanel { Spacing = 6 };
        var big = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        big.Children.Add(Text(total.ToString(), 40, _d == Direction.Ledger ? _k.Waiting : _d == Direction.LilyPad ? _k.Waiting : _k.Accent, FontWeights.SemiBold, display: true, tabular: true));
        big.Children.Add(Text("waiting across 5 places", 16, heroFg2).With(t => { t.VerticalAlignment = VerticalAlignment.Bottom; t.Margin = new Thickness(0, 0, 0, 8); }));
        left.Children.Add(big);
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var o in owners)
        {
            var saved = _k;
            if (_d == Direction.Beacon) _k = _k with { Surface = _k.Inverse };
            else _k = _k with { Surface = _k.SurfaceRaised };
            strip.Children.Add(Token(o, 36, o.Items.Count, urgent: o.Items.Any(i => i.Source == ItemSource.meeting)));
            _k = saved;
        }
        left.Children.Add(strip);
        hero.Children.Add(left);
        var right = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(Action("Open Board", true, bg: _d == Direction.Ledger ? _k.Text : _k.Accent, fg: _d == Direction.Ledger ? _k.Surface : _k.OnAccent).With(b => b.HorizontalAlignment = HorizontalAlignment.Stretch));
        var shortcut = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
        var savedKc = _k;
        if (_d == Direction.Beacon) _k = _k with { Sunken = Color.FromArgb(40, 255, 255, 255), Text2 = _k.OnInverse2, Stroke = Color.FromArgb(40, 255, 255, 255) };
        foreach (var k in new[] { "Ctrl", "Alt", "N" }) shortcut.Children.Add(Keycap(k));
        _k = savedKc;
        right.Children.Add(shortcut);
        Grid.SetColumn(right, 1);
        hero.Children.Add(right);
        content.Children.Add(hero);

        // Needs attention
        content.Children.Add(Text("Needs attention", 14, _k.Text, FontWeights.SemiBold).With(t => t.Margin = new Thickness(2, 30, 0, 10)));
        content.Children.Add(Row("", _k.Caution, "IE Hub: Google Chat isn't working", "Google says the Chat API is turned off for this project.", "Fix"));
        content.Children.Add(Row("", _k.Waiting, "Acme Ltd: sign-in expired", "Microsoft asked you to sign in again. Nothing is checked until you do.", "Sign in again", gap: true));

        // Quick controls
        content.Children.Add(Text("Right now", 14, _k.Text, FontWeights.SemiBold).With(t => t.Margin = new Thickness(2, 30, 0, 10)));
        content.Children.Add(Toggle("", "Pause notifications", "Everything waits in the Board until you turn this off.", false));
        content.Children.Add(Toggle("", "Quiet hours", "10:00 PM to 8:00 AM, every day", true, gap: true));

        page.Child = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        root.Children.Add(page);
        return root;
    }

    private FrameworkElement Row(string glyph, Color tone, string title, string detail, string action, bool gap = false)
    {
        var g = new Grid { Padding = new Thickness(16, 12, 14, 12), ColumnSpacing = 14, Margin = new Thickness(0, gap ? (_d == Direction.Ledger ? 0 : 4) : 0, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(Card(g, gap));
        g.Children.Add(new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(_d == Direction.Ledger ? 3 : 16), Background = B(Color.FromArgb(_dark ? (byte)44 : (byte)26, tone.R, tone.G, tone.B)), Child = Glyph(glyph, 15, tone).With(i => i.HorizontalAlignment = HorizontalAlignment.Center) });
        var t = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        t.Children.Add(Text(title, 14, _k.Text));
        t.Children.Add(Text(detail, 12, _k.Text2));
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        g.Children.Add(Action(action, false).With(b => { Grid.SetColumn(b, 2); b.VerticalAlignment = VerticalAlignment.Center; }));
        return g;
    }

    private FrameworkElement Toggle(string glyph, string title, string detail, bool on, bool gap = false)
    {
        var g = new Grid { Padding = new Thickness(16, 12, 14, 12), ColumnSpacing = 14, Margin = new Thickness(0, gap ? (_d == Direction.Ledger ? 0 : 4) : 0, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(Card(g, gap));
        g.Children.Add(Glyph(glyph, 16, _k.Text2).With(i => i.Width = 32));
        var t = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        t.Children.Add(Text(title, 14, _k.Text));
        t.Children.Add(Text(detail, 12, _k.Text2));
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        var sw = new ToggleSwitch { IsOn = on, OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(sw, 2);
        g.Children.Add(sw);
        return g;
    }

    /// Ledger joins rows with hairlines; Lily pad and Beacon use separate soft cards.
    private Border Card(Grid row, bool notFirst)
    {
        var b = new Border
        {
            Margin = new Thickness(-16, -12, -14, -12),
            Background = B(_k.SurfaceRaised),
            BorderBrush = B(_k.Stroke),
            CornerRadius = new CornerRadius(_d == Direction.Ledger ? 0 : 10),
            BorderThickness = _d == Direction.Ledger ? new Thickness(0, notFirst ? 0 : 1, 0, 1) : new Thickness(1),
        };
        if (_d == Direction.Ledger) b.Background = B(Colors.Transparent);
        Grid.SetColumnSpan(b, 3);
        return b;
    }
}

/// Builds its content once it knows the theme it's rendered in (light or dark).
internal sealed class ThemeProbe : ContentControl
{
    public ThemeProbe(Func<bool, FrameworkElement> build)
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Loaded += (_, _) =>
        {
            if (Content is not null) return;
            Content = build(ActualTheme == ElementTheme.Dark);
        };
    }
}
