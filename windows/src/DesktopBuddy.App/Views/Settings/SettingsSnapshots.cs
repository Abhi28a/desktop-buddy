using DesktopBuddy.App.Ui;
using DesktopBuddy.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DesktopBuddy.App.Views.Settings;

/// The Settings screens for `--snapshot`. Each one is the real window content (sidebar and page), built
/// without a window, from the sample data. Nothing here signs in or touches the network. Throwaway changes
/// to the sample state are made inside a screen's own build and put back straight after it.
internal static class SettingsSnapshots
{
    private const double W = 1100;
    private const double H = 760;

    public static void Add(List<SnapshotScreen> screens)
    {
        var state = AppState.Shared;

        // The sample team has both sign-in clients and a bug-report form (in the throwaway data folder).
        TeamConfig.InstallLocal(new TeamConfig
        {
            GoogleClientID = "sample-client.apps.googleusercontent.com",
            GoogleClientSecret = "sample-secret",
            MicrosoftClientID = "00000000-0000-0000-0000-000000000000",
            MicrosoftTenant = "organizations",
            FeedbackFormID = "sample",
        });

        var iehub = state.Config.Accounts.First(a => a.Label == "IE Hub");
        var acme = state.Config.Accounts.First(a => a.Label == "Acme Ltd");
        var dynamatix = state.Config.Accounts.First(a => a.Label == "Dynamatix");
        var gatehouse = state.Config.Clients.First(c => c.Name == "Gatehouse");

        // The IE Hub chat error comes with a link to the page that fixes it.
        var chat = state.Status(iehub.Id, ItemSource.chat);
        state.SetStatus(iehub.Id, ItemSource.chat, chat with { FixUrl = "https://console.cloud.google.com/apis/library/chat.googleapis.com" });

        // One more client with problems, to show "Needs attention" and the count in the sidebar.
        var studio = new ClientConfig { Name = "Acme Studio", TintChoice = AccountTint.purple, Keywords = ["team", "Studio"], AccountIDs = [acme.Id] };
        state.AddClient(studio);

        // Rules and chats the sample accounts and clients don't have, set straight on the config so that
        // nothing is re-matched or removed (the other windows' sample screens stay as they are).
        dynamatix.OnlyLabelsList = [new MailLabel { Id = "L1", Name = "Clients" }, new MailLabel { Id = "L2", Name = "Finance" }];
        dynamatix.MutedSendersList = ["news.example.com"];
        dynamatix.MutedKeywordsList = ["newsletter"];
        dynamatix.MutedChatsList = [new ChatRef { AccountID = dynamatix.Id, ChatID = "spaces/muted", Name = "Office banter", Kind = ChatKind.space }];
        var riskhawk = state.Config.Clients.First(c => c.Name == "RiskHawk");
        riskhawk.AccountIDs = [dynamatix.Id, iehub.Id];
        riskhawk.Chats =
        [
            new ChatRef { AccountID = dynamatix.Id, ChatID = "spaces/rh", Name = "RiskHawk delivery", Kind = ChatKind.space },
            new ChatRef { AccountID = acme.Id, ChatID = "chats/rh2", Name = "Sample Colleague", Kind = ChatKind.direct },
        ];

        SettingsShell Shell(string route, Action<SettingsContext>? setup = null, Func<TeamConfig>? team = null, double scroll = 0)
        {
            var ctx = team is null ? new SettingsContext { Preview = true } : new SettingsContext { Preview = true, Team = team };
            setup?.Invoke(ctx);
            var shell = new SettingsShell(ctx, route);
            if (scroll > 0) shell.Loaded += (_, _) => shell.DispatcherQueue.TryEnqueue(() => shell.Page?.ScrollTo(scroll));
            return shell;
        }

        /// Builds a screen with a temporary change to the sample state, then puts the state back.
        SettingsShell WithChange(Action change, Action undo, Func<SettingsShell> build)
        {
            change();
            try { return build(); }
            finally { undo(); }
        }

        void Add(string name, Func<FrameworkElement> build, double width = W, double height = H) =>
            screens.Add(new SnapshotScreen(name, build, width, height));

        // Everything connected: no problems.
        SettingsShell Healthy(string route, double scroll = 0)
        {
            var chatStatus = state.Status(iehub.Id, ItemSource.chat);
            var allClients = state.Config.Clients;
            return WithChange(() =>
            {
                state.Config.Clients = [gatehouse];              // the sample clients have problems of their own
                state.SetExpired(acme.Id, false);
                state.SetStatus(iehub.Id, ItemSource.chat, new ServiceStatus { LastChecked = state.Now().AddMinutes(-2) });
            }, () =>
            {
                state.Config.Clients = allClients;
                state.SetExpired(acme.Id, true);
                state.SetStatus(iehub.Id, ItemSource.chat, chatStatus);
            }, () => Shell(route, scroll: scroll));
        }

        Add("settings-home", () => Healthy("home"));
        Add("settings-home-problems", () => Shell("home"));
        Add("settings-home-paused", () => WithChange(
            () => state.Config.PausedUntilChoice = state.Now().AddHours(1),
            () => state.Config.PausedUntilChoice = null,
            () => Shell("home")));
        Add("settings-first-run", () =>
        {
            var saved = state.Config.Accounts;
            state.Config.Accounts = [];
            try { return Shell("home"); }
            finally { state.Config.Accounts = saved; }
        });
        Add("settings-accounts", () => Shell("accounts"));
        Add("settings-account", () => Shell("account:" + iehub.Id));
        Add("settings-account-2", () => Shell("account:" + dynamatix.Id, scroll: 640));
        Add("settings-account-3", () => Shell("account:" + dynamatix.Id, scroll: 1400));
        Add("settings-account-admin", () => Shell("account:" + dynamatix.Id, ctx => ctx.AdminRequested = true, scroll: 2400));
        Add("settings-account-expired", () => Shell("account:" + acme.Id));
        Add("settings-account-narrow", () => Shell("account:" + iehub.Id), 860, H);
        Add("settings-add-account", () => Shell("add-account"));
        Add("settings-add-account-no-setup", () => Shell("add-account", team: () => new TeamConfig()));
        Add("settings-signing-in", () => Shell("add-account", ctx => ctx.AddFlow.ShowWaitingForPreview(Provider.google)));
        Add("settings-add-account-done", () => Shell("add-account", ctx => ctx.AddFlow.ShowDoneForPreview(
            new AccountConfig { Label = "Dynamatix", Email = "you@dynamatix.co.uk", ProviderChoice = Provider.google })));
        Add("settings-clients", () => Shell("clients"));
        Add("settings-client", () => Shell("client:" + gatehouse.Id));
        Add("settings-client-issues", () => Shell("client:" + studio.Id));
        Add("settings-client-chats", () => Shell("client:" + riskhawk.Id, scroll: 700));
        Add("settings-notifications", () => Shell("notifications"));
        Add("settings-notifications-2", () => Shell("notifications", scroll: 700));
        Add("settings-notifications-3", () => Shell("notifications", scroll: 1500));
        Add("settings-reminders", () => Shell("reminders"));
        Add("settings-about", () => Shell("about"));
        Add("settings-team", () => Shell("team", ctx => ctx.AdminRequested = true));

        SettingsLiveCheck.Add(screens);

        FrameworkElement Framed(FrameworkElement content)
        {
            var card = new Border { Style = Sk.Sty("DbCardStyle"), Child = content, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(0) };
            return new Grid { Children = { card } };
        }
        Sk.EnsureResources();
        var tourContext = new SettingsContext { Preview = true };
        Add("settings-tour-welcome", () => Framed(TourDialog.Preview(tourContext, 0)), 640, 560);
        Add("settings-tour-peek", () => Framed(TourDialog.Preview(tourContext, 2)), 640, 560);
        Add("settings-tour-sounds", () => Framed(TourDialog.Preview(tourContext, 4)), 640, 560);
        Add("settings-feedback", () => Framed(FeedbackDialog.Preview()), 560, 620);
    }
}
