using System.Runtime.InteropServices;
using DesktopBuddy.Core;

namespace DesktopBuddy.App.Platform;

/// What Windows says about interrupting the person right now.
public enum AttentionState
{
    /// Free to show things.
    Available,
    /// Screen saver or locked session: nobody is looking.
    Busy,
    /// A presentation is running (Windows "presentation mode").
    Presenting,
    /// A full-screen app, game or video has the screen.
    FullScreen,
    /// Windows "Do not disturb" / Focus is on.
    QuietTime,
    /// Windows didn't say (the call failed or returned something new).
    Unknown,
}

/// Reads Windows' notification state (SHQueryUserNotificationState) so the Peek can wait while
/// the person is presenting, watching full screen or in Do not disturb.
public static class Attention
{
    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    /// The state right now. Never throws; anything unexpected is Unknown.
    public static AttentionState Current()
    {
        try
        {
            return SHQueryUserNotificationState(out var quns) == 0 ? Map(quns) : AttentionState.Unknown;
        }
        catch
        {
            return AttentionState.Unknown;
        }
    }

    /// Maps a QUERY_USER_NOTIFICATION_STATE value:
    ///   1 QUNS_NOT_PRESENT              screen saver or locked session -> Busy
    ///   2 QUNS_BUSY                     a full-screen app (not Direct3D) -> FullScreen
    ///   3 QUNS_RUNNING_D3D_FULL_SCREEN  a full-screen Direct3D game      -> FullScreen
    ///   4 QUNS_PRESENTATION_MODE        presentation mode                -> Presenting
    ///   5 QUNS_ACCEPTS_NOTIFICATIONS    normal: notifications are fine   -> Available
    ///   6 QUNS_QUIET_TIME               Do not disturb / Focus           -> QuietTime
    ///   7 QUNS_APP                      a Windows Store app is in front  -> Available
    ///   anything else                                                    -> Unknown
    public static AttentionState Map(int quns) => quns switch
    {
        1 => AttentionState.Busy,
        2 or 3 => AttentionState.FullScreen,
        4 => AttentionState.Presenting,
        5 or 7 => AttentionState.Available,
        6 => AttentionState.QuietTime,
        _ => AttentionState.Unknown,
    };

    /// True when the Peek should wait: the person asked us to respect Windows, and Windows is
    /// anything but free (or unknown, where we carry on rather than hide things forever).
    public static bool ShouldHoldPeek(AppConfig cfg) => ShouldHoldPeek(cfg, Current());

    /// The same rule for a state you already have (used by tests and by callers that cache it).
    public static bool ShouldHoldPeek(AppConfig cfg, AttentionState state) =>
        cfg.RespectWindowsFocus && state is not (AttentionState.Available or AttentionState.Unknown);
}
