using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace DesktopBuddy.App;

/// Entry point. Keeps one copy of Desktop Buddy running: opening it again just brings up
/// Settings in the copy that's already running. `--snapshot <folder>` renders screens to PNGs
/// and exits (it never touches your real sign-ins or settings).
public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (args.Contains("--selftest"))
        {
            Environment.SetEnvironmentVariable("DESKTOPBUDDY_HOME", Path.Combine(Path.GetTempPath(), "DesktopBuddySelfTest"));
            return Platform.PlatformSelfTest.Run();
        }

        if (!args.Contains("--snapshot"))
        {
            var key = AppInstance.FindOrRegisterForKey("DesktopBuddy.main");
            if (!key.IsCurrent)
            {
                var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
                // Redirect off the UI thread: waiting on it here would deadlock the STA.
                Task.Run(() => key.RedirectActivationToAsync(activation).AsTask()).Wait();
                return 0;
            }
            key.Activated += (_, _) => App.Instance?.OnOpenedAgain();
        }

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App(args);
        });
        return 0;
    }
}
