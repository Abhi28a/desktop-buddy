using DesktopBuddy.App.Ui;
using DesktopBuddy.Core;
using DesktopBuddy.Presentation.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DesktopBuddy.App.Views.Settings;

/// Reading and writing the team's setup file (team.json).
internal static class TeamFiles
{
    /// Asks for a setup file and reads it. Null when cancelled or when it isn't a setup file.
    public static async Task<TeamConfig?> ImportAsync(SettingsContext ctx)
    {
        var file = await ctx.PickFileAsync(".json");
        return file is null ? null : JsonFile.Load<TeamConfig>(file.Path);
    }

    public static async Task ExportAsync(SettingsContext ctx, TeamConfig team)
    {
        var file = await ctx.SaveFileAsync("team", "Setup file", ".json");
        if (file is not null) JsonFile.Save(team, file.Path);
    }
}

/// Team setup (admins only, reached from About): which setup file is in use, the Google and Microsoft
/// sign-in clients, and where bug reports go (port of TeamSetupPane in SettingsWindow.swift).
internal sealed class TeamPage : SettingsPage
{
    private TeamConfig _team;
    private bool _saved;

    public TeamPage(SettingsContext ctx) : base(ctx) => _team = ctx.Team();

    public override SettingsRoute Route => SettingsRoute.Team;

    private static FrameworkElement Ready(bool ok) =>
        Sk.StatusText(ok ? StatusLevel.Positive : StatusLevel.Neutral, ok ? "Ready" : "Not set");

    protected override void Build(StackPanel body)
    {
        var google = _team.HasClient(Provider.google);
        var microsoft = _team.HasClient(Provider.microsoft);
        body.Children.Add(Sk.Heading("Team"));
        body.Children.Add(Sk.Group(
            Sk.Row("Setup file", TeamConfig.Source, null, Glyphs.Folder, "TeamSource"),
            Sk.LeadRow(LogoTile.ProviderBadge(Provider.google, 24), "Google Workspace", null, Ready(google), "TeamGoogle", "Google Workspace sign-in"),
            Sk.LeadRow(LogoTile.ProviderBadge(Provider.microsoft, 24), "Microsoft 365", null, Ready(microsoft), "TeamMicrosoft", "Microsoft 365 sign-in")));
        body.Children.Add(Sk.Note("With a team setup file, teammates just choose Add account and sign in. No client IDs needed."));

        body.Children.Add(Sk.Heading("Setup file"));
        body.Children.Add(Sk.Row("Share your setup", "Import a file from your admin, or export this one for a teammate.",
            Sk.Buttons(
                Sk.Btn("Import setup file", () => _ = ImportAsync(), "TeamImport"),
                Sk.Btn("Export setup file", () => _ = TeamFiles.ExportAsync(Ctx, _team), "TeamExport")), Glyphs.Download, "TeamShare"));

        body.Children.Add(Sk.Heading("For admins"));
        body.Children.Add(Sk.Group(
            Field("Google client ID", "TeamGoogleId", _team.GoogleClientID, v => _team.GoogleClientID = v, "client-id.apps.googleusercontent.com"),
            Secret("Google client secret", "TeamGoogleSecret", _team.GoogleClientSecret, v => _team.GoogleClientSecret = v),
            Field("Microsoft client ID", "TeamMicrosoftId", _team.MicrosoftClientID, v => _team.MicrosoftClientID = v),
            Field("Microsoft tenant", "TeamMicrosoftTenant", _team.MicrosoftTenant, v => _team.MicrosoftTenant = v, "organizations"),
            Field("Bug report form ID (Formspree)", "TeamFormId", _team.FeedbackFormID, v => _team.FeedbackFormID = v),
            Field("Bug report email (optional)", "TeamFeedbackEmail", _team.FeedbackEmail, v => _team.FeedbackEmail = v, "name@company.com")));

        var save = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        if (_saved) save.Children.Add(Sk.StatusText(StatusLevel.Positive, "Saved").With(p => p.VerticalAlignment = VerticalAlignment.Center));
        save.Children.Add(Sk.Btn("Save on this PC", Save, "TeamSave", primary: true));
        body.Children.Add(save);

        body.Children.Add(Sk.Note("Google: in Google Cloud Console create an OAuth client of type Desktop app, enable Gmail, Calendar, Chat, Tasks and People APIs, and configure the Chat app. Microsoft: in Entra admin center register an app, add the Mobile and desktop platform with redirect http://localhost, allow public client flows, and add delegated Graph permissions User.Read, Mail.Read, Calendars.Read, Chat.Read and Tasks.Read."));
        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 4, 0, 0) };
        links.Children.Add(Hyperlink("Google Cloud Console", "https://console.cloud.google.com/apis/credentials", "TeamLinkGoogle"));
        links.Children.Add(Hyperlink("Microsoft Entra admin center", "https://entra.microsoft.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade", "TeamLinkMicrosoft"));
        body.Children.Add(links);
    }

    private static HyperlinkButton Hyperlink(string text, string url, string id)
    {
        var b = new HyperlinkButton { Content = text, NavigateUri = new Uri(url), Padding = new Thickness(0, 4, 0, 4) };
        b.Id(id, text);
        return b;
    }

    private Microsoft.UI.Xaml.FrameworkElement Field(string title, string id, string? value, Action<string?> set, string? placeholder = null) =>
        Sk.Row(title, null, Sk.Field(value ?? "", v => { Quiet(() => set(v.Length == 0 ? null : v)); _saved = false; }, title, id, placeholder, 320), null, id + "Row");

    private Microsoft.UI.Xaml.FrameworkElement Secret(string title, string id, string? value, Action<string?> set)
    {
        var box = Sk.Secret(value ?? "", v => { set(v.Length == 0 ? null : v); _saved = false; }, title, id);
        box.Width = 320;
        box.MinWidth = 0;
        return Sk.Row(title, null, box, null, id + "Row");
    }

    private void Save()
    {
        if (!Ctx.Preview) TeamConfig.InstallLocal(_team);
        Ctx.AdminRequested = true;      // you're setting this up: keep this page reachable
        _saved = true;
        Ctx.Shell.RefreshAfterAdminChange();
        QueueRebuild();
        _ = Task.Delay(TimeSpan.FromSeconds(2)).ContinueWith(_ => SettingsContext.OnUi(() =>
        {
            if (!_saved) return;
            _saved = false;
            QueueRebuild();
        }), TaskScheduler.Default);
    }

    private async Task ImportAsync()
    {
        var cfg = await TeamFiles.ImportAsync(Ctx);
        if (cfg is null) return;
        _team = cfg;
        Save();
    }
}
