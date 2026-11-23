using System.Globalization;
using DesktopBuddy.Core;
using DesktopBuddy.Presentation.Settings;
using DesktopBuddy.Presentation.Shortcuts;

namespace DesktopBuddy.Tests;

/// Page routing, the shortcut box and fix wording. No state needed, so no collection.
public class SettingsRoutingTests
{
    private static SettingsRouteContext Context(bool hasAccounts = true, bool team = false) => new()
    {
        HasAccounts = hasAccounts,
        ShowsTeam = team,
        AccountExists = id => id == "A1",
        ClientExists = id => id == "C1",
    };

    [Theory]
    [InlineData(null, "home")]
    [InlineData("", "home")]
    [InlineData("home", "home")]
    [InlineData("HOME", "home")]
    [InlineData("something-else", "home")]
    [InlineData("accounts", "accounts")]
    [InlineData("add-account", "add-account")]
    [InlineData("clients", "clients")]
    [InlineData("notifications", "notifications")]
    [InlineData("reminders", "reminders")]
    [InlineData("about", "about")]
    public void RoutesTheAcceptedValues(string? page, string expected) =>
        Assert.Equal(expected, SettingsRoute.Resolve(page, Context()).Key);

    [Theory]
    [InlineData("general")]
    [InlineData("appearance")]
    [InlineData("General")]
    public void GeneralAndAppearanceGoToNotifications(string page) =>
        Assert.Equal(SettingsRoute.Notifications, SettingsRoute.Resolve(page, Context()));

    [Fact]
    public void AccountAndClientPagesCarryTheirId()
    {
        var account = SettingsRoute.Resolve("account:A1", Context());
        Assert.Equal(SettingsPageKind.Account, account.Kind);
        Assert.Equal("A1", account.Id);
        Assert.Equal("account:A1", account.Key);
        var client = SettingsRoute.Resolve("client:C1", Context());
        Assert.Equal(SettingsRoute.ForClient("C1"), client);
    }

    [Fact]
    public void ARemovedAccountOrClientFallsBackToItsList()
    {
        Assert.Equal(SettingsRoute.Accounts, SettingsRoute.Resolve("account:gone", Context()));
        Assert.Equal(SettingsRoute.Accounts, SettingsRoute.Resolve("account:", Context()));
        Assert.Equal(SettingsRoute.Clients, SettingsRoute.Resolve("client:gone", Context()));
    }

    [Fact]
    public void TeamSetupShowsOnlyWhenAllowed()
    {
        Assert.Equal(SettingsRoute.About, SettingsRoute.Resolve("team", Context(team: false)));
        Assert.Equal(SettingsRoute.Team, SettingsRoute.Resolve("team", Context(team: true)));
    }

    [Fact]
    public void HomeIsTheSignInPageOnAFirstRun()
    {
        Assert.True(SettingsRoute.Home.IsSignInHome(Context(hasAccounts: false)));
        Assert.False(SettingsRoute.Home.IsSignInHome(Context(hasAccounts: true)));
        Assert.False(SettingsRoute.Accounts.IsSignInHome(Context(hasAccounts: false)));
    }

    [Fact]
    public void DrillInPagesKeepTheirSectionAndBreadcrumb()
    {
        var account = SettingsRoute.ForAccount("A1");
        Assert.Equal(SettingsSection.Accounts, account.Section);
        Assert.True(account.IsDrillIn);
        Assert.Equal(["Accounts", "IE Hub"], account.Breadcrumb(_ => "IE Hub"));
        Assert.Equal(["Accounts", "Account"], account.Breadcrumb(_ => ""));
        Assert.Equal(SettingsRoute.Accounts, account.Parent);
        Assert.Equal(["Accounts", "Add account"], SettingsRoute.AddAccount.Breadcrumb(_ => null));
        Assert.Equal(["Clients", "Gatehouse"], SettingsRoute.ForClient("C1").Breadcrumb(_ => "Gatehouse"));
        Assert.Equal(SettingsSection.About, SettingsRoute.Team.Section);
        Assert.Equal(["About", "Team setup"], SettingsRoute.Team.Breadcrumb(_ => null));
        Assert.Equal(["Notifications"], SettingsRoute.Notifications.Breadcrumb(_ => null));
        Assert.False(SettingsRoute.Home.IsDrillIn);
    }

    [Fact]
    public void EveryKeyRoutesBackToItself()
    {
        var all = new[]
        {
            SettingsRoute.Home, SettingsRoute.Accounts, SettingsRoute.AddAccount, SettingsRoute.Clients,
            SettingsRoute.Notifications, SettingsRoute.Reminders, SettingsRoute.About, SettingsRoute.Team,
            SettingsRoute.ForAccount("A1"), SettingsRoute.ForClient("C1"),
        };
        foreach (var route in all) Assert.Equal(route, SettingsRoute.Resolve(route.Key, Context(team: true)));
        foreach (var section in Enum.GetValues<SettingsSection>()) Assert.Equal(section, SettingsRoute.ForSection(section).Section);
    }
}

public class SettingsWordingTests
{
    [Theory]
    [InlineData("Turn On IE Hub", "Turn on IE Hub")]
    [InlineData("Turn On Google Chat", "Turn on Google Chat")]
    [InlineData("Sign In Again…", "Sign in again")]
    [InlineData("Sign In…", "Sign in")]
    [InlineData("Turn On", "Turn on")]
    [InlineData("Remove", "Remove")]
    [InlineData("Add Acme Ltd", "Add Acme Ltd")]
    [InlineData("  Sign In Again…  ", "Sign in again")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void TurnsTitleCaseFixLabelsIntoSentenceCase(string? label, string expected) =>
        Assert.Equal(expected, FixText.Sentence(label));

    [Fact]
    public void FixLabelsFromCoreComeOutInSentenceCase()
    {
        var s = Fx.FreshState();
        var account = s.Config.Accounts[0];
        account.Enabled = false;
        var blocker = s.BlockerFor(account, ItemSource.chat)!.Value;
        Assert.Equal($"Turn On {account.Label}", blocker.FixLabel);                       // Core's own spelling
        Assert.Equal($"Turn on {account.Label}", FixText.Sentence(blocker.FixLabel));
        account.Enabled = true;
        s.Expired.Add(account.Id);
        Assert.Equal("Sign in again", FixText.Sentence(s.BlockerFor(account, ItemSource.chat)!.Value.FixLabel));
    }

    [Fact]
    public void TimeWordsReadLikeAPerson()
    {
        var now = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("just now", TimeText.Ago(now.AddSeconds(-10), now));
        Assert.Equal("a minute ago", TimeText.Ago(now.AddSeconds(-60), now));
        Assert.Equal("5 minutes ago", TimeText.Ago(now.AddMinutes(-5), now));
        Assert.Equal("an hour ago", TimeText.Ago(now.AddMinutes(-60), now));
        Assert.Equal("yesterday", TimeText.Ago(now.AddDays(-1), now));
        var us = CultureInfo.GetCultureInfo("en-US");
        Assert.Equal("10:00 PM", TimeText.Hour(22, us));
        Assert.Equal("8:00 AM", TimeText.Hour(8, us));
        Assert.Equal("22:00", TimeText.Hour(22, CultureInfo.GetCultureInfo("en-GB")));
        Assert.Equal("A, B and C", TimeText.Join(["A", "B", "C"]));
        Assert.Equal("1 service needs", TimeText.Plural(1, "service needs", "services need"));
    }

    [Fact]
    public void ReminderScheduleReadsInWords()
    {
        var us = CultureInfo.GetCultureInfo("en-US");
        var r = new Reminder { EveryMinutes = 30, StartHour = 9, EndHour = 18 };
        Assert.Equal("Every 30 minutes, 9:00 AM to 6:00 PM, weekdays", ReminderText.Summary(r, us));
        r.Weekdays = [2, 4];
        r.EveryMinutes = 120;
        Assert.Equal("Every 2 hours, 9:00 AM to 6:00 PM, Mon, Wed", ReminderText.Summary(r, us));
        Assert.Equal("Every hour", ReminderText.Every(60));
        Assert.Equal("Weekends", ReminderText.Days([1, 7]));
        Assert.Equal("Every day", ReminderText.Days([1, 2, 3, 4, 5, 6, 7]));
    }

    [Fact]
    public void ErrorsKeepTheSentenceAndLoseTheLink()
    {
        var text = AccountStatusText.ShortError("Google Chat API has not been used in project 0 before or it is disabled. Enable it by visiting https://console.example.com/apis/library/chat then retry.");
        Assert.DoesNotContain("https://", text);
        Assert.StartsWith("Google Chat API has not been used", text);
        Assert.EndsWith(".", text);
        Assert.True(AccountStatusText.ShortError(new string('x', 400)).Length <= 152);
    }
}

public class ShortcutBoxTests
{
    private static (ShortcutBoxViewModel Box, List<string> Applied, List<string> Saved) Make(
        string current = "Ctrl+Alt+N", Func<string, ShortcutApplyResult>? apply = null)
    {
        var applied = new List<string>();
        var saved = new List<string>();
        var box = new ShortcutBoxViewModel(current,
            apply ?? (s => { applied.Add(s); return new ShortcutApplyResult(true, $"{s} is ready."); }),
            saved.Add);
        return (box, applied, saved);
    }

    [Fact]
    public void ParsesNormalisesAndSaves()
    {
        var (box, applied, saved) = Make();
        Assert.True(box.Propose("shift+ctrl+k"));
        Assert.Equal("Ctrl+Shift+K", box.Current);
        Assert.Equal(["Ctrl+Shift+K"], applied);
        Assert.Equal(["Ctrl+Shift+K"], saved);
        Assert.Equal("Ctrl+Shift+K is ready.", box.Message);
        Assert.False(box.HasProblem);
        Assert.Equal(["Ctrl", "Shift", "K"], box.Keys);
    }

    [Theory]
    [InlineData("N", "Add Ctrl, Alt or Win")]
    [InlineData("Ctrl+Alt", "Add a key")]
    [InlineData("Shift+K", "Shift alone")]
    [InlineData("Win+N", "Windows keeps Win shortcuts")]
    [InlineData("Ctrl+Alt+Home", "isn't a key you can use")]
    public void RefusesAnUnusableCombinationAndKeepsTheOldOne(string text, string reason)
    {
        var (box, applied, saved) = Make();
        Assert.False(box.Propose(text));
        Assert.Equal("Ctrl+Alt+N", box.Current);
        Assert.Empty(applied);                                     // never reached Windows
        Assert.Empty(saved);
        Assert.True(box.HasProblem);
        Assert.Contains(reason, box.Message);
    }

    [Fact]
    public void KeepsTheOldShortcutWhenWindowsRefuses()
    {
        var (box, _, saved) = Make(apply: _ => new ShortcutApplyResult(false, "Another app is using Ctrl+Alt+K."));
        Assert.False(box.Propose("Ctrl+Alt+K"));
        Assert.Equal("Ctrl+Alt+N", box.Current);
        Assert.Empty(saved);
        Assert.Equal("Another app is using Ctrl+Alt+K.", box.Message);
        Assert.True(box.HasProblem);
    }

    [Fact]
    public void AGoodShortcutClearsTheEarlierProblem()
    {
        var (box, _, _) = Make();
        box.Propose("N");
        Assert.True(box.HasProblem);
        box.Propose("Ctrl+Alt+J");
        Assert.False(box.HasProblem);
        Assert.Equal("Ctrl+Alt+J", box.Current);
    }

    [Fact]
    public void PressingTheKeysBuildsTheShortcut()
    {
        var (box, applied, _) = Make();
        Assert.False(box.Press(ShortcutModifiers.Ctrl | ShortcutModifiers.Alt, 0x11));      // the Ctrl key itself: not a shortcut yet
        Assert.Empty(applied);
        Assert.True(box.Press(ShortcutModifiers.Ctrl | ShortcutModifiers.Alt, 'B'));
        Assert.Equal("Ctrl+Alt+B", box.Current);
        Assert.True(box.Press(ShortcutModifiers.Alt, 0x75));                                  // F6
        Assert.Equal("Alt+F6", box.Current);
        Assert.True(box.Press(ShortcutModifiers.Ctrl, 0x20));
        Assert.Equal("Ctrl+Space", box.Current);
    }

    [Fact]
    public void AKeyWithNoModifierIsRefusedWithAReason()
    {
        var (box, applied, _) = Make();
        Assert.True(box.Press(ShortcutModifiers.None, 'Q'));
        Assert.Empty(applied);
        Assert.Equal("Ctrl+Alt+N", box.Current);
        Assert.Contains("Add Ctrl, Alt or Win", box.Message);
    }

    [Fact]
    public void ResetReturnsToTheDefault()
    {
        var (box, _, saved) = Make("Ctrl+Alt+K");
        Assert.True(box.Reset());
        Assert.Equal("Ctrl+Alt+N", box.Current);
        Assert.Equal(["Ctrl+Alt+N"], saved);
    }

    [Fact]
    public void ReportsChangesToTheView()
    {
        var (box, _, _) = Make();
        var changed = new List<string?>();
        box.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        box.Propose("Ctrl+Alt+M");
        Assert.Contains(nameof(box.Current), changed);
        Assert.Contains(nameof(box.Message), changed);
        Assert.Contains(nameof(box.Keys), changed);
    }

    [Fact]
    public void ShowsWhatYouAreHolding()
    {
        Assert.Equal("Press a shortcut…", ShortcutBoxViewModel.Preview(ShortcutModifiers.None));
        Assert.Equal("Ctrl+Alt+…", ShortcutBoxViewModel.Preview(ShortcutModifiers.Ctrl | ShortcutModifiers.Alt));
        Assert.Null(ShortcutCapture.KeyName(0x11));       // Ctrl
        Assert.Null(ShortcutCapture.KeyName(0x0D));       // Enter
        Assert.Equal("F12", ShortcutCapture.KeyName(0x7B));
        Assert.Equal("7", ShortcutCapture.KeyName('7'));
    }
}

/// Home, account status and the needs-attention list, from real app state.
[Collection("DesktopBuddy.State")]
public class HomeSummaryTests : StorageTest
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    /// Three signed-in accounts: Northwind and Contoso from Fx, plus Fabrikam.
    private static AppState State(DateTimeOffset? now = null)
    {
        var s = Fx.FreshState();
        s.Config.Accounts.Add(new AccountConfig { Id = "acc-f", ProviderChoice = Provider.google, Label = "Fabrikam", Email = "me@fabrikam.example.com" });
        s.SignedIn.Add("acc-f");
        var at = now ?? Fx.At(3, 14, 0);
        s.Now = () => at;
        return s;
    }

    [Fact]
    public void CountsWhatIsWaitingAndWhere()
    {
        var s = State();
        s.Offer([Fx.Mail("1", "acc-g", "a@example.com", "One"), Fx.Mail("2", "acc-g", "a@example.com", "Two"),
                 Fx.Mail("3", "acc-m", "a@example.com", "Three")]);
        var home = HomeSummary.Build(s, culture: Us);
        Assert.Equal(3, home.WaitingCount);
        Assert.Equal(2, home.PlaceCount);
        Assert.Equal("waiting across 2 places", home.WaitingText);
        Assert.Equal(2, home.Owners.Count);
        Assert.Equal(3, home.Owners.Sum(o => o.Count));
        Assert.Contains(home.Owners, o => o.Id == "acc-g" && o.Count == 2 && o.Name == "Northwind");
    }

    [Fact]
    public void WaitingWordsCoverNothingOneAndMany()
    {
        Assert.Equal("nothing waiting", HomeSummary.WaitingWords(0, 0));
        Assert.Equal("waiting in 1 place", HomeSummary.WaitingWords(4, 1));
        Assert.Equal("waiting across 5 places", HomeSummary.WaitingWords(6, 5));
        var home = HomeSummary.Build(State(), culture: Us);
        Assert.Equal(0, home.WaitingCount);
        Assert.Equal("nothing waiting", home.WaitingText);
        Assert.Empty(home.Owners);
    }

    [Fact]
    public void AllConnectedWhenNothingNeedsAttention()
    {
        var s = State();
        var home = HomeSummary.Build(s, culture: Us);
        Assert.Empty(home.Attention);
        Assert.True(home.AllConnected);
        Assert.Equal("Waiting for the first check.", home.AllConnectedDetail);
        s.SetStatus("acc-g", ItemSource.mail, new ServiceStatus { LastChecked = s.Now().AddMinutes(-2) });
        Assert.Equal("Last checked 2 minutes ago.", HomeSummary.Build(s, culture: Us).AllConnectedDetail);
    }

    [Fact]
    public void NoAccountsIsNotAllConnected()
    {
        var s = Fx.FreshState();
        s.Config.Accounts = [];
        var home = HomeSummary.Build(s, culture: Us);
        Assert.False(home.AllConnected);
        Assert.Empty(home.Attention);
    }

    [Fact]
    public void NeedsAttentionListsSignInProblemsBeforeServiceErrors()
    {
        var s = State();
        // Northwind (first account) has a broken service; Contoso's sign-in expired; Fabrikam is the wrong account.
        s.SetStatus("acc-g", ItemSource.chat, new ServiceStatus { Error = "Google Chat API has not been used in project 0 before or it is disabled.", FixUrl = "https://console.example.com/chat" });
        s.Expired.Add("acc-m");
        s.WrongAccount["acc-f"] = "other@fabrikam.example.com";
        var attention = HomeSummary.AttentionItems(s, s.Now());

        Assert.Equal([AttentionKind.SignInExpired, AttentionKind.WrongAccount, AttentionKind.ServiceError], attention.Select(a => a.Kind));
        Assert.Equal(["acc-m", "acc-f", "acc-g"], attention.Select(a => a.AccountId));
        Assert.All(attention.Take(2), a => Assert.Equal(StatusLevel.Critical, a.Level));
        Assert.Equal(StatusLevel.Caution, attention[2].Level);

        Assert.Equal("Contoso: sign-in expired", attention[0].Title);
        Assert.Equal("Sign in again", attention[0].FixLabel);
        Assert.Contains("Microsoft", attention[0].Detail);
        Assert.Equal("Fabrikam: signed in as a different account", attention[1].Title);
        Assert.Contains("other@fabrikam.example.com", attention[1].Detail);
        Assert.Equal("Northwind: Google Chat isn't working", attention[2].Title);
        Assert.Equal("Fix", attention[2].FixLabel);
        Assert.Equal("https://console.example.com/chat", attention[2].FixUrl);
        Assert.DoesNotContain("https://", attention[2].Detail);
        Assert.False(HomeSummary.Build(s, culture: Us).AllConnected);
    }

    [Fact]
    public void ClientProblemsComeAfterAccountProblemsWithSentenceCaseFixes()
    {
        var s = State();
        s.SetStatus("acc-g", ItemSource.chat, new ServiceStatus { Error = "Chat is off." });
        s.Expired.Add("acc-m");
        // Studio looks only in Contoso (expired): its email rules can't be checked, and "team" is too common a word.
        s.Config.Clients =
        [
            new ClientConfig { Id = "c1", Name = "Studio", Keywords = ["team"], MatchChatNames = false, AccountIDs = ["acc-m"] },
            new ClientConfig { Id = "c2", Name = "Quiet client", Senders = ["quiet.example.com"], AccountIDs = ["acc-g"] },
            new ClientConfig { Id = "c3", Name = "Switched off", Enabled = false, Keywords = ["team"] },
        ];
        var items = HomeSummary.AttentionItems(s, s.Now());
        Assert.Equal([AttentionKind.SignInExpired, AttentionKind.ServiceError, AttentionKind.ClientIssue], items.Select(i => i.Kind));
        var client = items.Where(i => i.Kind == AttentionKind.ClientIssue).ToList();
        Assert.All(client, i => Assert.Equal("c1", i.ClientId));              // the other clients are fine or switched off
        // Studio's "email rules can't be checked, sign in again" repeats Contoso's own sign-in line, so it isn't listed.
        Assert.DoesNotContain(items, i => i.IssueId == "mail");
        Assert.Equal("Sign in again", items[0].FixLabel);
        Assert.Equal("Client: Studio", client[0].Detail);
        var common = client.Single(i => i.IssueId == "kw-team");
        Assert.Equal("Open client", common.FixLabel);                          // no one-click fix: open the client
        Assert.Null(common.Fix);
        Assert.Equal("c1.kw-team", common.Key);
        Assert.Equal((2, 1), HomeSummary.NeedCounts(items));                   // Contoso and Northwind; one client (its other problem is Contoso's)
        var home = HomeSummary.Build(s, culture: Us);
        Assert.Equal(2, home.AccountsNeedAttention);
        Assert.Equal(1, home.ClientsNeedAttention);
        Assert.False(home.AllConnected);
    }

    [Fact]
    public void ASignInProblemIsListedOnceNotAgainForEveryClientThatUsesTheAccount()
    {
        var s = State();
        s.Expired.Add("acc-m");
        s.Config.Clients =
        [
            new ClientConfig { Id = "c1", Name = "One", Senders = ["one.example.com"], AccountIDs = ["acc-m"] },
            new ClientConfig { Id = "c2", Name = "Two", Senders = ["two.example.com"], AccountIDs = ["acc-m"] },
        ];
        Assert.Contains(s.ClientIssues(s.Client("c1")!), i => i.Fix?.Kind == ClientFixKind.SignIn);   // Core does report it per client
        var items = HomeSummary.AttentionItems(s, s.Now());
        var only = Assert.Single(items);
        Assert.Equal(AttentionKind.SignInExpired, only.Kind);
        Assert.Equal("acc-m", only.AccountId);
        Assert.Equal((1, 0), HomeSummary.NeedCounts(items));                   // the sidebar agrees: nothing on Clients
        var home = HomeSummary.Build(s, culture: Us);
        Assert.Equal(0, home.ClientsNeedAttention);
    }

    [Fact]
    public void AClientProblemAloneIsNotAllConnected()
    {
        var s = State();
        s.Config.Clients = [new ClientConfig { Id = "c1", Name = "Studio", Keywords = ["team"], MatchChatNames = false }];
        var home = HomeSummary.Build(s, culture: Us);
        Assert.Single(home.Attention);
        Assert.False(home.AllConnected);
        Assert.Equal(0, home.AccountsNeedAttention);
        Assert.Equal(1, home.ClientsNeedAttention);
        Assert.Equal(4, HomeSummary.MaxVisibleAttention);
    }

    [Fact]
    public void AServiceErrorWithoutALinkOpensTheAccount()
    {
        var s = State();
        s.SetStatus("acc-g", ItemSource.mail, new ServiceStatus { Error = "Something went wrong." });
        var item = Assert.Single(HomeSummary.AttentionItems(s, s.Now()));
        Assert.Equal("Open account", item.FixLabel);
        Assert.Null(item.FixUrl);
        Assert.Equal("acc-g.mail", item.Key);
    }

    [Fact]
    public void ASignedOutAccountHidesItsServiceErrorsAndAnAccountThatIsOffIsIgnored()
    {
        var s = State();
        s.SetStatus("acc-g", ItemSource.mail, new ServiceStatus { Error = "Broken." });
        s.SignedIn.Remove("acc-g");
        var item = Assert.Single(HomeSummary.AttentionItems(s, s.Now()));
        Assert.Equal(AttentionKind.NotSignedIn, item.Kind);
        Assert.Equal("Sign in", item.FixLabel);

        s.Config.Accounts.First(a => a.Id == "acc-g").Enabled = false;
        Assert.Empty(HomeSummary.AttentionItems(s, s.Now()));
    }

    [Fact]
    public void AServiceThatIsTurnedOffNeverNeedsAttention()
    {
        var s = State();
        s.SetStatus("acc-g", ItemSource.chat, new ServiceStatus { Error = "Chat is broken." });
        s.Config.Accounts.First(a => a.Id == "acc-g").WatchChat = false;
        Assert.Empty(HomeSummary.AttentionItems(s, s.Now()));
    }

    [Fact]
    public void PausedTextForTheSwitchAndForTimedPauses()
    {
        var s = State(Fx.At(3, 14, 0));
        Assert.False(HomeSummary.Build(s, culture: Us).IsPaused);
        Assert.Null(HomeSummary.Build(s, culture: Us).PausedText);

        s.PauseFor(TimeSpan.FromHours(1));
        var timed = HomeSummary.Build(s, culture: Us);
        Assert.True(timed.IsPaused);
        Assert.True(timed.IsTimedPause);
        Assert.Equal("Paused until 3:00 PM", timed.PausedText);

        s.Resume();
        s.PauseUntilTomorrow();
        Assert.Equal("Paused until tomorrow, 8:00 AM", HomeSummary.Build(s, culture: Us).PausedText);

        s.Resume();
        s.Config.Paused = true;
        var switched = HomeSummary.Build(s, culture: Us);
        Assert.True(switched.IsPaused);
        Assert.False(switched.IsTimedPause);
        Assert.Equal("Paused", switched.PausedText);
    }

    [Fact]
    public void QuietHoursAreDescribedInWords()
    {
        var s = State();
        Assert.False(HomeSummary.Build(s, culture: Us).QuietHoursOn);
        Assert.Equal("Off", HomeSummary.Build(s, culture: Us).QuietText);
        s.Config.QuietStartHour = 22;
        s.Config.QuietEndHour = 8;
        var home = HomeSummary.Build(s, culture: Us);
        Assert.True(home.QuietHoursOn);
        Assert.Equal("10:00 PM to 8:00 AM, every day", home.QuietText);
        s.Config.QuietEndHour = 22;
        Assert.Contains("the same", HomeSummary.Build(s, culture: Us).QuietText);
    }

    [Fact]
    public void ShortcutKeysComeFromTheConfig()
    {
        var s = State();
        Assert.Equal(["Ctrl", "Alt", "N"], HomeSummary.Build(s, culture: Us).ShortcutKeys);
        s.Config.BoardShortcut = "Ctrl+Shift+K";
        Assert.Equal(["Ctrl", "Shift", "K"], HomeSummary.Build(s, culture: Us).ShortcutKeys);
    }

    [Fact]
    public void AccountStatusIsWrittenInWordsWithTheRightPriority()
    {
        var s = State();
        var g = s.Config.Accounts.First(a => a.Id == "acc-g");
        var info = AccountStatusText.Describe(s, g);
        Assert.Equal(AccountHealth.Connected, info.Health);
        Assert.Equal("Connected", info.Words);
        Assert.Equal(StatusLevel.Positive, info.Level);

        s.SetStatus("acc-g", ItemSource.mail, new ServiceStatus { Error = "Broken." });
        s.SetStatus("acc-g", ItemSource.chat, new ServiceStatus { Error = "Also broken." });
        info = AccountStatusText.Describe(s, g);
        Assert.Equal(AccountHealth.ServiceProblems, info.Health);
        Assert.Equal("Connected, 2 services need attention", info.Words);
        Assert.Equal(StatusLevel.Caution, info.Level);

        s.SetStatus("acc-g", ItemSource.chat, new ServiceStatus());
        Assert.Equal("Connected, 1 service needs attention", AccountStatusText.Describe(s, g).Words);

        s.Expired.Add("acc-g");
        info = AccountStatusText.Describe(s, g);
        Assert.Equal(AccountHealth.SignInExpired, info.Health);
        Assert.Equal("Sign-in expired", info.Words);
        Assert.Equal(StatusLevel.Critical, info.Level);

        s.WrongAccount["acc-g"] = "someone@example.com";            // wrong account outranks expired
        info = AccountStatusText.Describe(s, g);
        Assert.Equal("Signed in as a different account", info.Words);
        Assert.Contains("someone@example.com", info.Detail);

        s.WrongAccount.Clear();
        s.Expired.Clear();
        s.SignedIn.Remove("acc-g");
        Assert.Equal("Not signed in", AccountStatusText.Describe(s, g).Words);

        g.Enabled = false;                                           // off outranks everything
        Assert.Equal("Off", AccountStatusText.Describe(s, g).Words);
    }

    [Fact]
    public void ServiceRowsSayWhatIsHappening()
    {
        var s = State();
        var g = s.Config.Accounts.First(a => a.Id == "acc-g");
        Assert.Equal("Waiting for the first check", AccountStatusText.Service(s, g, ItemSource.mail).Status);

        s.SetStatus("acc-g", ItemSource.mail, new ServiceStatus { LastChecked = s.Now().AddMinutes(-3), Count = 2 });
        Assert.Equal("2 waiting, checked 3 minutes ago", AccountStatusText.Service(s, g, ItemSource.mail).Status);
        s.SetStatus("acc-g", ItemSource.mail, new ServiceStatus { LastChecked = s.Now().AddMinutes(-3) });
        Assert.Equal("Nothing new, checked 3 minutes ago", AccountStatusText.Service(s, g, ItemSource.mail).Status);

        s.SetStatus("acc-g", ItemSource.chat, new ServiceStatus { Error = "Chat is off. Turn it on at https://console.example.com/x", FixUrl = "https://console.example.com/x" });
        var error = AccountStatusText.Service(s, g, ItemSource.chat);
        Assert.Equal(StatusLevel.Caution, error.Level);
        Assert.Equal("https://console.example.com/x", error.FixUrl);
        Assert.StartsWith("Chat is off.", error.Error);

        g.WatchTasks = false;
        var off = AccountStatusText.Service(s, g, ItemSource.task);
        Assert.Equal("Off", off.Status);
        Assert.Equal(StatusLevel.Neutral, off.Level);

        s.Config.Clients = [new ClientConfig { Id = "c1", Name = "Gatehouse", Senders = ["gatehouse.example.com"] }];
        g.WatchMail = false;
        var blocked = AccountStatusText.Service(s, g, ItemSource.mail);
        Assert.Equal(StatusLevel.Caution, blocked.Level);
        Assert.Equal("Off. Gatehouse won't get mail from Northwind.", blocked.Status);

        s.SignedIn.Remove("acc-g");
        g.WatchChat = true;
        Assert.Equal("Paused until you sign in", AccountStatusText.Service(s, g, ItemSource.chat).Status);
    }

    [Fact]
    public void ServiceNamesFollowTheProvider()
    {
        Assert.Equal("Gmail", AccountStatusText.ServiceName(Provider.google, ItemSource.mail));
        Assert.Equal("Teams chats", AccountStatusText.ServiceName(Provider.microsoft, ItemSource.chat));
        Assert.Equal("Meetings", AccountStatusText.ServiceName(Provider.microsoft, ItemSource.meeting));
        Assert.Equal("Microsoft To Do", AccountStatusText.ServiceName(Provider.microsoft, ItemSource.task));
    }
}
