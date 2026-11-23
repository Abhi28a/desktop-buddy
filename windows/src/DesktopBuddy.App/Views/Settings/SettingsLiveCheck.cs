using DesktopBuddy.App.Platform;
using DesktopBuddy.App.Ui;
using DesktopBuddy.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DesktopBuddy.App.Views.Settings;

/// A smoke test for what a still picture can't show: the real Settings window opening, every page reachable
/// from it, pages rebuilding after a change, and the dialogs opening. Runs inside `--snapshot` only when
/// DESKTOPBUDDY_SETTINGS_LIVECHECK=1, always against the throwaway data folder. Results go to
/// %TEMP%\DesktopBuddySettingsLive.log.
internal static class SettingsLiveCheck
{
    private static string LogPath => Path.Combine(Path.GetTempPath(), "DesktopBuddySettingsLive.log");

    private static void Log(string line)
    {
        try { File.AppendAllText(LogPath, line + Environment.NewLine); } catch (Exception) { }
    }

    public static void Add(List<SnapshotScreen> screens)
    {
        if (Environment.GetEnvironmentVariable("DESKTOPBUDDY_SETTINGS_LIVECHECK") != "1") return;
        try { File.Delete(LogPath); } catch (Exception) { }
        var state = AppState.Shared;
        var started = false;

        screens.Add(new SnapshotScreen("live-window", () =>
        {
            var grid = new Grid();
            grid.Loaded += async (_, _) =>
            {
                if (started) return;
                started = true;
                try { await Run(state); }
                catch (Exception e) { Log($"FAILED {e.GetType().Name}: {e.Message}\n{e.StackTrace}"); }
            };
            return grid;
        }, 300, 200));
        for (var i = 1; i <= 30; i++) screens.Add(new SnapshotScreen($"live-wait-{i:00}", () => new Grid(), 160, 90));
    }

    private static async Task Run(AppState state)
    {
        await Task.Delay(300);
        SettingsWindow.Show("home");
        await Task.Delay(900);
        var window = SettingsWindow.OpenWindow;
        if (window is null) { Log("window: NOT OPEN"); return; }
        var shell = window.ShellForTest;
        var scale = Native.GetDpiForWindow(WindowChrome.Handle(window)) / 96.0;
        Log($"window: open, size {window.AppWindow.Size.Width / scale:0}x{window.AppWindow.Size.Height / scale:0} DIPs, route {shell.Route.Key}");

        var account = state.Config.Accounts.First();
        var client = state.Config.Clients.First();
        var routes = new[] { "accounts", "account:" + account.Id, "add-account", "clients", "client:" + client.Id, "notifications", "reminders", "about", "team", "general", "client:gone", "home" };
        foreach (var route in routes)
        {
            shell.Navigate(route);
            await Task.Delay(350);
            Log($"navigate {route} -> {shell.Route.Key}, page {shell.Page?.GetType().Name}, children {Count(shell)}");
        }

        // A change in the settings rebuilds the page.
        shell.Navigate("account:" + account.Id);
        await Task.Delay(250);
        var a = state.Account(account.Id)!;
        a.WatchChat = false;
        state.Update(a);
        await Task.Delay(400);
        Log($"rebuild after change: page {shell.Page?.GetType().Name}");
        a.WatchChat = true;
        state.Update(a);

        // Dialogs.
        var ctx = shell.Ctx;
        async Task Dialog(string name, Func<Task> open)
        {
            _ = open();
            await Task.Delay(600);
            Log($"dialog {name}: {(ctx.CurrentDialog is null ? "DID NOT OPEN" : "open")}");
            ctx.CurrentDialog?.Hide();
            await Task.Delay(250);
        }
        await Dialog("feedback", () => FeedbackDialog.ShowAsync(ctx));
        await Dialog("tour", () => TourDialog.ShowAsync(ctx));
        await Dialog("add-client", () => AddClientFlow.RunAsync(ctx));
        shell.Navigate("reminders");
        await Task.Delay(300);
        SettingsWindow.Show("about");
        await Task.Delay(300);
        Log($"second Show -> same window {ReferenceEquals(window, SettingsWindow.OpenWindow)}, route {shell.Route.Key}");
        window.Close();
        await Task.Delay(300);
        Log($"closed -> {(SettingsWindow.OpenWindow is null ? "none" : "still open")}");
        Log("done");
    }

    private static int Count(DependencyObject root)
    {
        var n = VisualTreeHelper.GetChildrenCount(root);
        var total = n;
        for (var i = 0; i < n; i++) total += Count(VisualTreeHelper.GetChild(root, i));
        return total;
    }
}
