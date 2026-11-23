using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;

namespace DesktopBuddy.App.Platform;

/// Saves a rendered piece of UI as a PNG. Used by `DesktopBuddy.exe --snapshot <folder>`
/// to check screens without clicking through the app (the Mac app's --snapshot).
internal static class Snapshot
{
    public static async Task SaveAsync(UIElement element, string path)
    {
        var rtb = new RenderTargetBitmap();
        await rtb.RenderAsync(element);
        var pixels = (await rtb.GetPixelsAsync()).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        var stream = file.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        var dpi = 96 * (rtb.PixelWidth / Math.Max(1.0, element.ActualSize.X));
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, dpi, dpi, pixels);
        await encoder.FlushAsync();
    }
}
