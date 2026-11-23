using DesktopBuddy.App.Platform;
using DesktopBuddy.App.Ui;
using DesktopBuddy.Core;
using DesktopBuddy.Presentation.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DesktopBuddy.App.Views.Settings;

/// What the Updates section remembers between rebuilds.
internal static class UpdateStatus
{
    public static bool Checking { get; private set; }
    public static UpdateCheckResult? Last { get; private set; }

    public static async Task CheckAsync()
    {
        if (Checking) return;
        Checking = true;
        try { Last = await UpdateCheck.CheckAsync(ApiClient.Http); }
        finally { Checking = false; }
    }
}

/// About: version and updates, help (report a bug, the welcome tour), open at sign-in, your data, and
/// Team setup (only when Shift was held while Settings opened, or when sign-in isn't set up in this copy).
internal sealed class AboutPage : SettingsPage
{
    private string? _loginError;

    public AboutPage(SettingsContext ctx) : base(ctx) { }

    public override SettingsRoute Route => SettingsRoute.About;

    private AppConfig Config => State.Config;

    private void Set(Action<AppConfig> change)
    {
        change(Config);
        State.SaveConfig();
    }

    protected override void Build(StackPanel body)
    {
        Updates(body);

        body.Children.Add(Sk.Heading("Help"));
        body.Children.Add(Sk.Group(
            Sk.Row("Report a bug or an idea", "Goes straight to the Desktop Buddy team. No notifications, mail or chats are included.",
                Sk.Btn("Report", () => _ = FeedbackDialog.ShowAsync(Ctx), "ReportBug", name: "Report a bug or an idea"), Glyphs.Bug, "ReportBugRow"),
            Sk.Row("Welcome tour", "A quick walk through the tray icon, the Peek, the Rail and the settings that cut the noise.",
                Sk.Btn("Show the tour", () => _ = TourDialog.ShowAsync(Ctx), "ShowTour"), Glyphs.Hand, "ShowTourRow")));

        body.Children.Add(Sk.Heading("Startup"));
        UIElement? error = _loginError is null ? null : Sk.StatusText(StatusLevel.Critical, _loginError);
        body.Children.Add(Sk.RowOf("Open at sign-in", error ?? Sk.Caption("Starts Desktop Buddy quietly in the notification area when you sign in to Windows."),
            Sk.Switch(Ctx.Preview ? false : Startup.IsEnabled, SetStartup, "Open at sign-in", "OpenAtSignIn"), Glyphs.Power, "OpenAtSignInRow"));

        body.Children.Add(Sk.Heading("Privacy"));
        var dataFolder = Ctx.Preview
            ? @"%LOCALAPPDATA%\DesktopBuddy"
            : Paths.SupportDir.Replace(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%", StringComparison.OrdinalIgnoreCase);
        body.Children.Add(Sk.Row("Your data", dataFolder,
            Sk.Btn("Show in File Explorer", () => { if (!Ctx.Preview) App.OpenUrl(Paths.SupportDir); }, "ShowDataFolder"), Glyphs.Folder, "DataFolderRow"));
        body.Children.Add(Sk.Note("Everything stays on this PC. Sign-ins are stored in Windows Credential Manager, and Desktop Buddy talks only to Google and Microsoft (plus GitHub, to check for updates). There are no servers of ours and no AI involved."));

        if (Ctx.ShowsAdmin)
        {
            body.Children.Add(Sk.Heading("Team setup"));
            body.Children.Add(Sk.Row("Team setup", "The sign-in clients your team shares, and where bug reports go.", null, Glyphs.People, "TeamSetupRow")
                .Clickable(() => Ctx.Navigate("team")));
        }
    }

    private void SetStartup(bool on)
    {
        if (Ctx.Preview) return;
        try
        {
            Startup.SetEnabled(on);
            _loginError = null;
        }
        catch (Exception e)
        {
            _loginError = e.Message;
        }
        QueueRebuild();
    }

    // MARK: - Updates

    private void Updates(StackPanel body)
    {
        body.Children.Add(Sk.Heading("Version and updates"));
        var rows = Sk.Group();
        rows.Children.Add(Sk.Row("Desktop Buddy", $"Version {UpdateCheck.CurrentVersion()}", null, Glyphs.Info, "VersionRow"));

        var last = UpdateStatus.Last;
        if (last?.Available is { } release)
        {
            rows.Children.Add(Sk.Row($"Desktop Buddy {release.Version} is available", "Download it and run the installer. Your accounts and settings stay.",
                Sk.Buttons(
                    Sk.Btn("What's new", () => App.OpenUrl(release.Page), "WhatsNew"),
                    Sk.Btn("Download", () => App.OpenUrl(release.Page), "DownloadUpdate", primary: true)), Glyphs.Download, "UpdateRow"));
        }
        else
        {
            UIElement trailing = UpdateStatus.Checking
                ? new ProgressRing { IsActive = true, Width = 20, Height = 20 }.Id("UpdateProgress", "Checking for updates")
                : Sk.Btn("Check now", () => _ = CheckAsync(), "CheckUpdates");
            var title = last?.Error is not null ? "Couldn't check for updates" : last is null ? "Not checked yet" : "Up to date";
            UIElement sub = last?.Error is not null
                ? Sk.StatusText(StatusLevel.Caution, last.Error)
                : Sk.Caption(last is null ? "Checks automatically once a day" : $"Checked {TimeText.Ago(last.CheckedAt, State.Now())}");
            rows.Children.Add(Sk.RowOf(title, sub, trailing, Glyphs.Refresh, "UpdateRow"));
        }
        rows.Children.Add(Sk.Row("Check for updates automatically", null,
            Sk.Switch(Config.CheckForUpdates, on => Set(c => c.CheckForUpdates = on), "Check for updates automatically", "AutoUpdate"), null, "AutoUpdateRow"));
        body.Children.Add(rows);
        body.Children.Add(Sk.Note("Desktop Buddy asks GitHub once a day whether a newer version exists. Nothing about you or your accounts is sent."));
    }

    private async Task CheckAsync()
    {
        if (Ctx.Preview) return;
        var task = UpdateStatus.CheckAsync();
        QueueRebuild();
        await task;
        QueueRebuild();
    }
}
