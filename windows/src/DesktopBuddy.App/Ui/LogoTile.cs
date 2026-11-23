using DesktopBuddy.Core;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DesktopBuddy.App.Ui;

/// An owner's logo: a white rounded tile with the logo fitted inside, or a monogram in the
/// owner's colour when there's no logo. Reminders without a picked logo show a white bell on
/// teal. Optionally carries a small Google or Microsoft badge in the corner (accounts).
/// Port of LogoTile / OwnerBuddy / AccountMark in Clients.swift.
internal sealed class LogoTile : Grid
{
    private static readonly Dictionary<string, BitmapImage> Cache = [];
    private static readonly HashSet<string> MissingLogged = [];

    public LogoTile(LogoRef? logo, string name, AccountTint tint, double size = 40, Provider? badge = null, bool isReminders = false)
    {
        Width = size;
        Height = size;
        var radius = new CornerRadius(size * 0.225);
        AutomationProperties.SetName(this, $"{name} logo");

        if (isReminders && logo is null)
        {
            Children.Add(new Border { CornerRadius = radius, Background = Palette.Brush(AccountTint.teal) });
            Children.Add(Glyph("", size * 0.46, new SolidColorBrush(Microsoft.UI.Colors.White)));
        }
        else
        {
            Children.Add(new Border
            {
                CornerRadius = radius,
                Background = new SolidColorBrush(Microsoft.UI.Colors.White),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(28, 0, 0, 0)),
                BorderThickness = new Thickness(Math.Max(0.5, size / 80)),
            });
            if (Load(logo) is { } image)
            {
                Children.Add(new Image { Source = image, Stretch = Stretch.Uniform, Margin = new Thickness(size * 0.14) });
            }
            else
            {
                Children.Add(new TextBlock
                {
                    Text = Logos.Monogram(name),
                    FontSize = size * 0.4,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Palette.Brush(tint),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
        }

        if (badge is { } provider) Children.Add(ProviderBadge(provider, size * 0.48, size));
    }

    /// The full path of a bundled logo (Assets\<key>.png next to the app).
    public static string BundledPath(string key) => Path.Combine(AppContext.BaseDirectory, "Assets", key + ".png");

    public static BitmapImage? Load(LogoRef? logo)
    {
        var path = logo switch
        {
            { Path: { } p } when File.Exists(p) => p,
            { BundledKey: { } k } => BundledPath(k),
            _ => null,
        };
        if (path is null) return null;
        if (!File.Exists(path))
        {
            // A bundled logo should always ship with the app. If it's missing, the build is broken:
            // say so once in status.log instead of quietly showing initials.
            if (MissingLogged.Add(path)) DesktopBuddy.Core.StatusLog.Write($"Logo file missing: {path}. Showing initials instead.");
            return null;
        }
        if (!Cache.TryGetValue(path, out var image))
        {
            image = new BitmapImage(new Uri(path));
            Cache[path] = image;
        }
        return image;
    }

    /// Google "G" or the Microsoft squares, in a small white circle at the bottom-right corner.
    public static FrameworkElement ProviderBadge(Provider provider, double badgeSize, double? tileSize = null)
    {
        var holder = new Grid
        {
            Width = badgeSize,
            Height = badgeSize,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        if (tileSize is { } t) holder.Margin = new Thickness(0, 0, -t * 0.12, -t * 0.12);
        holder.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(badgeSize / 2),
            Background = new SolidColorBrush(Microsoft.UI.Colors.White),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(40, 0, 0, 0)),
            BorderThickness = new Thickness(0.5),
        });
        if (provider == Provider.google)
        {
            holder.Children.Add(new TextBlock
            {
                Text = "G",
                FontSize = badgeSize * 0.62,
                FontWeight = FontWeights.Bold,
                Foreground = Palette.Brush(Palette.GoogleBlue),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, badgeSize * 0.06),
            });
        }
        else
        {
            var squares = new Grid
            {
                Width = badgeSize * 0.5,
                Height = badgeSize * 0.5,
                ColumnSpacing = badgeSize * 0.04,
                RowSpacing = badgeSize * 0.04,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            squares.ColumnDefinitions.Add(new ColumnDefinition());
            squares.ColumnDefinitions.Add(new ColumnDefinition());
            squares.RowDefinitions.Add(new RowDefinition());
            squares.RowDefinitions.Add(new RowDefinition());
            var colours = new[] { Palette.MicrosoftRed, Palette.MicrosoftGreen, Palette.MicrosoftBlue, Palette.MicrosoftYellow };
            for (var i = 0; i < 4; i++)
            {
                var r = new Microsoft.UI.Xaml.Shapes.Rectangle { Fill = Palette.Brush(colours[i]) };
                SetRow(r, i / 2);
                SetColumn(r, i % 2);
                squares.Children.Add(r);
            }
            holder.Children.Add(squares);
        }
        AutomationProperties.SetName(holder, provider.Name());
        return holder;
    }

    /// A Segoe Fluent Icons glyph.
    public static FontIcon Glyph(string glyph, double size, Brush? foreground = null)
    {
        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (foreground is not null) icon.Foreground = foreground;
        return icon;
    }
}

internal static class AutomationProperties
{
    public static void SetName(DependencyObject o, string name) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(o, name);
}
