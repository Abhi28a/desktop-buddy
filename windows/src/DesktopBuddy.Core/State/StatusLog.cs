using System.Globalization;

namespace DesktopBuddy.Core;

// Port of StatusLog in Sources/DesktopBuddy/Poller.swift.

/// Counts and errors only (never message content), for troubleshooting.
public static class StatusLog
{
    public const long MaxBytes = 1_000_000;
    private static readonly object Gate = new();

    public static string OldPath => Path.Combine(Paths.SupportDir, "status.old.log");

    public static void Write(string line)
    {
        var stamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var entry = $"{stamp} {line}\n";
        lock (Gate)
        {
            try
            {
                var path = Paths.StatusLog;
                // Keep the log small: past 1 MB, the current file becomes status.old.log (replacing the previous one).
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                {
                    File.Move(path, OldPath, overwrite: true);
                }
                File.AppendAllText(path, entry);
            }
            catch (Exception)
            {
                // Logging is best effort, as on the Mac.
            }
        }
    }
}
