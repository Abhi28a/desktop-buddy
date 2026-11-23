using System.Runtime.InteropServices;
using DesktopBuddy.Core;

namespace DesktopBuddy.App.Platform;

/// The notification sounds: Windows' own sounds from C:\Windows\Media, by a friendly name
/// (the Mac app uses the alert sounds that come with macOS). The names are what Settings
/// lists and what AppConfig stores; "None" is silent.
public static class Sounds
{
    private static readonly (string Name, string File)[] Known =
    [
        ("Default", "Windows Notify System Generic.wav"),
        ("Messaging", "Windows Notify Messaging.wav"),
        ("Calendar", "Windows Notify Calendar.wav"),
        ("Email", "Windows Notify Email.wav"),
        ("Chimes", "chimes.wav"),
        ("Ding", "ding.wav"),
        ("Chord", "chord.wav"),
        ("Proximity", "Windows Proximity Notification.wav"),
    ];

    private static string MediaFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");

    private static string? PathFor(string name)
    {
        foreach (var (n, file) in Known)
        {
            if (!string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) continue;
            var path = Path.Combine(MediaFolder, file);
            return File.Exists(path) ? path : null;
        }
        return null;
    }

    /// Sounds that exist on this PC ("Default" is always listed), then "None".
    public static IReadOnlyList<string> Names { get; } = BuildNames();

    private static List<string> BuildNames()
    {
        var names = Known
            .Where(k => k.Name == AppConfig.DefaultSound || File.Exists(Path.Combine(MediaFolder, k.File)))
            .Select(k => k.Name)
            .ToList();
        names.Add(AppConfig.NoSound);
        return names;
    }

    /// Plays a sound without waiting for it. "None" and missing files are skipped; an unknown
    /// name plays the default one.
    public static void Play(string name)
    {
        if (name == AppConfig.NoSound) return;
        var path = PathFor(name) ?? PathFor(AppConfig.DefaultSound);
        if (path is null) return;
        try { PlaySound(path, IntPtr.Zero, SND_FILENAME | SND_ASYNC | SND_NODEFAULT); }
        catch (Exception ex) { StatusLog.Write("Couldn't play a sound: " + ex.Message); }
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "PlaySoundW")]
    private static extern bool PlaySound(string sound, IntPtr module, uint flags);

    private const uint SND_ASYNC = 0x0001;
    private const uint SND_NODEFAULT = 0x0002;
    private const uint SND_FILENAME = 0x00020000;
}
