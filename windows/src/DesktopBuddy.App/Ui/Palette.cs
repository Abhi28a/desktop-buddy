using DesktopBuddy.Core;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DesktopBuddy.App.Ui;

/// Colours shared by every screen. Tints come from Core (same values as the Mac app).
internal static class Palette
{
    public static Color FromRgb(uint rgb, byte alpha = 255) =>
        Color.FromArgb(alpha, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public static Color Tint(AccountTint t) => FromRgb(t.Rgb());
    public static Color Tint(ItemSource s) => FromRgb(s.Tint());
    public static SolidColorBrush Brush(Color c) => new(c);
    public static SolidColorBrush Brush(AccountTint t) => new(Tint(t));
    public static SolidColorBrush Brush(ItemSource s) => new(Tint(s));

    /// The red count badge (Windows' critical red).
    public static readonly Color BadgeRed = FromRgb(0xC42B1C);
    /// The pop-up "island": near-black in both themes, like the Mac.
    public static readonly Color Island = FromRgb(0x141414);

    public static readonly Color GoogleBlue = FromRgb(0x4285F4);
    public static readonly Color GoogleRed = FromRgb(0xEA4335);
    public static readonly Color GoogleYellow = FromRgb(0xFBBC05);
    public static readonly Color GoogleGreen = FromRgb(0x34A853);
    public static readonly Color MicrosoftRed = FromRgb(0xF25022);
    public static readonly Color MicrosoftGreen = FromRgb(0x7FBA00);
    public static readonly Color MicrosoftBlue = FromRgb(0x00A4EF);
    public static readonly Color MicrosoftYellow = FromRgb(0xFFB900);
}
