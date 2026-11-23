using Microsoft.Win32;

namespace DesktopBuddy.App.Platform;

/// "Open at sign-in": the HKCU Run key, the Windows counterpart of the Mac's login item.
internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "Desktop Buddy";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) is string;
        }
    }

    public static void SetEnabled(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue(Name, $"\"{Environment.ProcessPath}\" --background");
        else key.DeleteValue(Name, throwOnMissingValue: false);
    }
}
