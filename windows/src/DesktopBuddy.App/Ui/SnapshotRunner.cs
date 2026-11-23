using DesktopBuddy.App.Platform;
using DesktopBuddy.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DesktopBuddy.App.Ui;

/// One screen to render: a name and how to build it at a given size.
internal sealed record SnapshotScreen(string Name, Func<FrameworkElement> Build, double Width, double Height, bool Transparent = false);

/// `DesktopBuddy.exe --snapshot <folder>`: renders every registered screen with sample data, in
/// light and dark mode, to <folder>\<name>-light.png and -dark.png, then exits.
/// Uses a throwaway data folder and an in-memory secret store, so it never touches real sign-ins.
internal static class SnapshotRunner
{
    public static async Task RunAsync(string folder, IReadOnlyList<SnapshotScreen> screens, string? only = null)
    {
        Directory.CreateDirectory(folder);
        foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
        {
            foreach (var screen in screens)
            {
                if (only is not null && !screen.Name.Contains(only, StringComparison.OrdinalIgnoreCase)) continue;
                var dark = theme == ElementTheme.Dark;
                var root = new Grid { RequestedTheme = theme };
                root.Background = new SolidColorBrush(screen.Transparent
                    ? (dark ? Palette.FromRgb(0x2B4A6F) : Palette.FromRgb(0x8FB3D9))     // a desktop-ish backdrop
                    : (dark ? Palette.FromRgb(0x202020) : Palette.FromRgb(0xF3F3F3)));   // Mica base colours
                var content = screen.Build();
                root.Children.Add(content);

                var window = new Window { Content = root, Title = "Desktop Buddy snapshot" };
                window.Activate();
                var s = WindowChrome.Scale(window);
                window.AppWindow.Move(new Windows.Graphics.PointInt32(40, 40));
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(
                    (int)Math.Ceiling(screen.Width * s), (int)Math.Ceiling(screen.Height * s)));
                await Task.Delay(700);   // let layout, images and theme resources settle
                var path = Path.Combine(folder, $"{screen.Name}-{(theme == ElementTheme.Dark ? "dark" : "light")}.png");
                try { await Snapshot.SaveAsync(root, path); }
                catch (Exception ex) { await File.WriteAllTextAsync(path + ".error.txt", ex.ToString()); }
                window.Close();
            }
        }
    }
}
