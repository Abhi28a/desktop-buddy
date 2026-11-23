using DesktopBuddy.Core;

namespace DesktopBuddy.Tests;

// Port of the "Reminders" and "Quiet hours" scenarios in SelfTest.swift, plus tests for the part
// of Poller.swift the self-test never covered: the fetch pipeline, driven by a fake gateway.

/// Stands in for the network layer: nobody signs in or fetches anything unless a test says so.
public sealed class FakeGateway : IServiceGateway
{
    public HashSet<string> SignedInIds { get; } = [];
    public List<(string Account, ItemSource Source, int Lead)> Calls { get; } = [];
    public Func<AccountConfig, ItemSource, Task<FetchResult>>? Handler { get; set; }

    public bool IsSignedIn(AccountConfig account) => SignedInIds.Contains(account.Id);

    public async Task<FetchResult> FetchAsync(AccountConfig account, ItemSource source, int meetingLeadMinutes, CancellationToken ct = default)
    {
        Calls.Add((account.Id, source, meetingLeadMinutes));
        if (Handler is null) throw new AuthException(AuthErrorKind.NotSignedIn, "Not signed in");
        return await Handler(account, source);
    }

    public static Task<FetchResult> Result(ItemSource source, params BuddyItem[] items) =>
        Task.FromResult(new FetchResult { Source = source, Items = items.ToList(), LiveIDs = items.Select(i => i.Id).ToHashSet() });
}

[Collection("DesktopBuddy.State")]
public class PollerTests : StorageTest
{
    private readonly AppState _state;
    private readonly FakeGateway _gateway = new();
    private readonly Poller _poller;

    public PollerTests()
    {
        // Built here, not in a field initializer: those run before the base constructor has
        // pointed storage at the temp folder.
        _state = Fx.FreshState();
        _gateway.SignedInIds.UnionWith(["acc-g", "acc-m"]);
        Gateway.Current = _gateway;
        _poller = new Poller(_state, _gateway);
        // Only Google mail unless a test says otherwise.
        foreach (var a in _state.Config.Accounts) { a.WatchChat = false; a.WatchMeetings = false; a.WatchTasks = false; }
        _state.Config.Accounts[1].Enabled = false;
        _state.Now = () => Fx.At(3, 12);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition(), "timed out waiting");
    }

    // MARK: Fetch pipeline

    [Fact]
    public async Task FetchThenMuteThenClientMatchThenOfferThenStatusThenLog()
    {
        var g = _state.Account("acc-g")!;
        g.MutedSenders = ["news.example.com"];
        _state.Update(g);
        _state.Config.Clients = [new ClientConfig { Id = "C1", Name = "Acme", Senders = ["acme.example.com"] }];
        var fromAcme = Fx.Mail("1", "acc-g", "pm@acme.example.com", "Release plan");
        var plain = Fx.Mail("2", "acc-g", "x@other.example.com", "Lunch");
        var muted = Fx.Mail("3", "acc-g", "a@news.example.com", "Weekly");
        _gateway.Handler = (_, source) => FakeGateway.Result(source, fromAcme, plain, muted);

        await _poller.PollAsync(_state.Account("acc-g")!);

        Assert.Equal(new[] { fromAcme.Id, plain.Id }.Order(), _state.Items.Select(i => i.Id).Order());   // the muted one never shows
        Assert.Equal("C1", _state.Items.Single(i => i.Id == fromAcme.Id).ClientID);                      // sorted under its client
        Assert.Null(_state.Items.Single(i => i.Id == plain.Id).ClientID);
        Assert.Contains(muted.Id, _state.MutedMail);                                                     // remembered so it isn't downloaded again
        var status = _state.Status("acc-g", ItemSource.mail);
        Assert.Equal(2, status.Count);                                                                   // live count, without the muted one
        Assert.Equal(Fx.At(3, 12), status.LastChecked);
        Assert.Null(status.Error);
        Assert.Contains("Northwind mail: fetched 2, live 2, new 2", File.ReadAllText(Paths.StatusLog));
        Assert.DoesNotContain("Release plan", File.ReadAllText(Paths.StatusLog));                        // counts only, never content
    }

    [Fact]
    public async Task ItemsThatDisappearUpstreamDisappearHere()
    {
        var m = Fx.Mail("1", "acc-g", "x@example.com", "Read me");
        _gateway.Handler = (_, source) => FakeGateway.Result(source, m);
        await _poller.PollAsync(_state.Account("acc-g")!);
        Assert.Single(_state.Items);
        _gateway.Handler = (_, source) => FakeGateway.Result(source);     // read elsewhere
        await _poller.PollAsync(_state.Account("acc-g")!);
        Assert.Empty(_state.Items);
        Assert.Equal(0, _state.Status("acc-g", ItemSource.mail).Count);
    }

    [Fact]
    public async Task TextOnItemsAlreadyShownIsRefreshed()
    {
        var meet = new BuddyItem { Id = "meet:acc-g:1", Source = ItemSource.meeting, AccountID = "acc-g", Title = "Sync", Detail = "Starts in 5 min", Date = Fx.At(3, 12) };
        var g = _state.Account("acc-g")!;
        g.WatchMeetings = true;
        _state.Update(g);
        _gateway.Handler = (_, source) => FakeGateway.Result(source, meet);
        await _poller.PollAsync(_state.Account("acc-g")!);
        Assert.Equal("Starts in 5 min", _state.Items.Single().Detail);
        _gateway.Handler = (_, source) => FakeGateway.Result(source, meet with { Detail = "Starts in 3 min" });
        await _poller.PollAsync(_state.Account("acc-g")!);
        Assert.Equal("Starts in 3 min", _state.Items.Single().Detail);
    }

    [Fact]
    public async Task QuietHoursKeepItemsOffTheScreenButStillCheck()
    {
        _state.Config.QuietStartHour = 22; _state.Config.QuietEndHour = 8;
        _state.Now = () => Fx.At(3, 23);
        var m = Fx.Mail("1", "acc-g", "x@example.com", "Overnight");
        _gateway.Handler = (_, source) => FakeGateway.Result(source, m);
        await _poller.PollAsync(_state.Account("acc-g")!);
        Assert.Empty(_state.Items);
        Assert.Equal(1, _state.Status("acc-g", ItemSource.mail).Count);
        Assert.Contains("fetched 1, live 1, new 0", File.ReadAllText(Paths.StatusLog));
    }

    [Fact]
    public async Task OnlyTheWatchedServicesAreFetchedInOrderWithTheMeetingLead()
    {
        var g = _state.Account("acc-g")!;
        g.WatchMail = true; g.WatchChat = true; g.WatchMeetings = true; g.WatchTasks = true;
        _state.Update(g);
        _state.Config.MeetingLeadMinutes = 15;
        _gateway.Handler = (_, source) => FakeGateway.Result(source);
        await _poller.PollAsync(_state.Account("acc-g")!);
        Assert.Equal([ItemSource.mail, ItemSource.chat, ItemSource.meeting, ItemSource.task], _gateway.Calls.Select(c => c.Source));
        Assert.All(_gateway.Calls, c => Assert.Equal(15, c.Lead));

        _gateway.Calls.Clear();
        g.WatchMail = false; g.WatchMeetings = false;
        _state.Update(g);
        await _poller.PollAsync(_state.Account("acc-g")!);
        Assert.Equal([ItemSource.chat, ItemSource.task], _gateway.Calls.Select(c => c.Source));
    }

    [Fact]
    public async Task TheAccountIsMarkedCheckingWhileItIsBeingChecked()
    {
        bool? during = null;
        _gateway.Handler = (_, source) => { during = _state.Checking.Contains("acc-g"); return FakeGateway.Result(source); };
        await _poller.PollAsync(_state.Account("acc-g")!);
        Assert.True(during);
        Assert.DoesNotContain("acc-g", _state.Checking);
    }

    // MARK: Errors

    [Fact]
    public async Task ExpiredSignInFlagsTheAccountResetsStatusesAndStops()
    {
        var g = _state.Account("acc-g")!;
        g.WatchChat = true; g.WatchTasks = true;
        _state.Update(g);
        _state.SetStatus("acc-g", ItemSource.task, new ServiceStatus { Count = 5, LastChecked = Fx.At(3, 9) });
        _gateway.Handler = (_, source) => throw new AuthException(AuthErrorKind.Expired, "Sign-in expired");
        await _poller.PollAsync(_state.Account("acc-g")!);
        Assert.Contains("acc-g", _state.Expired);
        Assert.Equal([ItemSource.mail], _gateway.Calls.Select(c => c.Source));                          // one expired sign-in breaks every service: stop
        Assert.All(ItemSourceInfo.All, s => Assert.Equal(new ServiceStatus(), _state.Status("acc-g", s)));
        Assert.Contains("Northwind: sign-in expired", File.ReadAllText(Paths.StatusLog));
        Assert.True(_state.NeedsSignIn("acc-g"));
    }

    [Fact]
    public async Task NotSignedInRefreshesTheSignedInSetAndKeepsGoing()
    {
        var g = _state.Account("acc-g")!;
        g.WatchChat = true;
        _state.Update(g);
        _gateway.SignedInIds.Remove("acc-g");                  // the token went away behind our back
        await _poller.PollAsync(_state.Account("acc-g")!);
        Assert.DoesNotContain("acc-g", _state.SignedIn);
        Assert.Equal("Not signed in", _state.Status("acc-g", ItemSource.mail).Error);
        Assert.Equal(2, _gateway.Calls.Count);                 // chat was still tried
        Assert.DoesNotContain("acc-g", _state.Expired);
    }

    [Fact]
    public async Task ApiErrorsKeepTheirFixLinkAndOtherServicesStillRun()
    {
        var g = _state.Account("acc-g")!;
        g.WatchChat = true;
        _state.Update(g);
        var chatItem = Fx.Chat("1", "acc-g", "Team", "spaces/T");
        _gateway.Handler = (_, source) => source == ItemSource.mail
            ? throw new ApiException(403, "gmail.googleapis.com", "Gmail API is off. Enable it at https://console.example.com/apis/gmail.")
            : FakeGateway.Result(source, chatItem);
        await _poller.PollAsync(_state.Account("acc-g")!);
        var mail = _state.Status("acc-g", ItemSource.mail);
        Assert.Contains("Gmail API is off", mail.Error);
        Assert.Equal("https://console.example.com/apis/gmail", mail.FixUrl);
        Assert.Equal(1, _state.ErrorCount("acc-g"));
        Assert.Null(_state.Status("acc-g", ItemSource.chat).Error);
        Assert.Single(_state.Items);
        Assert.Contains("Northwind mail: ERROR Gmail API is off", File.ReadAllText(Paths.StatusLog));
    }

    [Fact]
    public async Task AnyOtherErrorIsShownOnThatServiceOnly()
    {
        _gateway.Handler = (_, _) => throw new InvalidOperationException("boom");
        await _poller.PollAsync(_state.Account("acc-g")!);
        Assert.Equal("boom", _state.Status("acc-g", ItemSource.mail).Error);
        Assert.Null(_state.Status("acc-g", ItemSource.mail).FixUrl);
        Assert.DoesNotContain("acc-g", _state.Expired);
    }

    // MARK: Poll all

    [Fact]
    public async Task PollAllChecksOnlyEnabledSignedInAccounts()
    {
        var all = _state.Config.Accounts;
        all[1].Enabled = true;                                               // acc-m is on...
        _state.Config.Accounts.Add(new AccountConfig { Id = "acc-off", Label = "Off", Email = "o@example.com", Enabled = false });
        _state.Config.Accounts.Add(new AccountConfig { Id = "acc-new", Label = "New", Email = "n@example.com" });   // never signed in
        _state.Config.Accounts.Add(new AccountConfig { Id = "acc-exp", Label = "Exp", Email = "e@example.com" });
        _gateway.SignedInIds.UnionWith(["acc-off", "acc-exp"]);
        _state.SignedIn = ["acc-g", "acc-m", "acc-off", "acc-exp"];
        _state.Expired.Add("acc-exp");
        _gateway.Handler = (_, source) => FakeGateway.Result(source);
        await _poller.PollAllAsync();
        Assert.Equal(new[] { "acc-g", "acc-m" }.Order(), _gateway.Calls.Select(c => c.Account).Distinct().Order());
    }

    [Fact]
    public async Task PausedMeansNothingIsChecked()
    {
        _state.Config.Paused = true;
        _gateway.Handler = (_, source) => FakeGateway.Result(source);
        await _poller.PollAllAsync();
        Assert.Empty(_gateway.Calls);
    }

    [Fact]
    public async Task OnlyOneRoundOfCheckingRunsAtATime()
    {
        var release = new TaskCompletionSource();
        _gateway.Handler = async (_, source) => { await release.Task; return new FetchResult { Source = source }; };
        var first = _poller.PollAllAsync();
        await WaitUntil(() => _gateway.Calls.Count == 1);
        await _poller.PollAllAsync();                          // returns at once: a round is already running
        Assert.Single(_gateway.Calls);
        release.SetResult();
        await first;
        await _poller.PollAllAsync();                          // and after it finishes, the next round runs
        Assert.Equal(2, _gateway.Calls.Count);
    }

    // MARK: Timers

    [Theory]
    [InlineData(5, 30)]
    [InlineData(30, 30)]
    [InlineData(60, 60)]
    [InlineData(300, 300)]
    public void PollIntervalIsTheSettingButNeverUnder30Seconds(int setting, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), Poller.IntervalFor(setting));

    [Fact]
    public void RemindersAreCheckedEveryTwentySeconds() => Assert.Equal(TimeSpan.FromSeconds(20), Poller.ReminderInterval);

    [Fact]
    public async Task StartChecksRightAwayAndStopEnds()
    {
        _gateway.Handler = (_, source) => FakeGateway.Result(source);
        _poller.Start();
        await WaitUntil(() => _gateway.Calls.Count > 0);
        _poller.Stop();
        _poller.Stop();                                        // stopping twice is fine
        _poller.Start();
        _poller.Stop();
    }

    [Fact]
    public async Task ATurnOnFixAsksForAnImmediateCheck()
    {
        var c = new ClientConfig { Name = "Acme", Senders = ["acme.example.com"] };
        _state.AddClient(c);
        var g = _state.Account("acc-g")!;
        g.WatchMail = false;
        _state.Update(g);
        _gateway.Handler = (_, source) => FakeGateway.Result(source);
        _state.Apply(ClientFix.TurnOnService("acc-g", ItemSource.mail), c.Id);
        await WaitUntil(() => _gateway.Calls.Any(x => x.Source == ItemSource.mail));
    }

    // MARK: Reminders

    private Reminder WeekdayReminder(string note = "")
    {
        var r = new Reminder { Title = "Drink water", Note = note, EveryMinutes = 30, StartHour = 9, EndHour = 18, Weekdays = [2, 3, 4, 5, 6] };
        _state.Config.Reminders = [r];
        return r;
    }

    private static string IdOf(Reminder r) => $"reminder:{r.Id.ToString().ToUpperInvariant()}";

    [Fact]
    public void Reminders()
    {
        var r = WeekdayReminder();
        var id = IdOf(r);
        _poller.CheckReminders(Fx.At(2, 8, 30));
        Assert.DoesNotContain(_state.Items, i => i.Id == id);                      // not before the start hour
        _poller.CheckReminders(Fx.At(2, 9, 0));
        Assert.Contains(_state.Items, i => i.Id == id);                            // fires inside the window on a weekday
        _state.Dismiss(id);
        _poller.CheckReminders(Fx.At(2, 9, 20));
        Assert.DoesNotContain(_state.Items, i => i.Id == id);                      // does not repeat within the interval
        _poller.CheckReminders(Fx.At(2, 9, 31));
        Assert.Contains(_state.Items, i => i.Id == id);                            // repeats after the interval, even if dismissed
        _poller.CheckReminders(Fx.At(2, 10, 5));
        Assert.Single(_state.Items, i => i.Id == id);                              // never stacks: one copy at a time
        _state.ClearAll();
        _state.Config.Reminders[0].LastFired = null;
        _poller.CheckReminders(Fx.At(1, 11, 0));
        Assert.Empty(_state.Items);                                                // skips days that aren't selected (Sunday)
        _poller.CheckReminders(Fx.At(2, 18, 0));
        Assert.Empty(_state.Items);                                                // stops at the end hour
        _state.Config.Reminders[0].Enabled = false;
        _poller.CheckReminders(Fx.At(3, 10, 0));
        Assert.Empty(_state.Items);                                                // switched-off reminder never fires
        _state.Config.Reminders[0].Enabled = true;
        _state.Config.Paused = true;
        _poller.CheckReminders(Fx.At(3, 10, 0));
        Assert.Empty(_state.Items);                                                // Pause All silences reminders
        _state.Config.Paused = false;
    }

    [Fact]
    public void ReminderTextAndId()
    {
        var plain = WeekdayReminder();
        _poller.CheckReminders(Fx.At(2, 9));
        var item = _state.Items.Single();
        Assert.Equal("💧 Drink water", item.Title);
        Assert.Equal("Every 30 min", item.Detail);                                 // no note: says how often
        Assert.Equal(ItemSource.reminder, item.Source);
        Assert.Equal(BuddyItem.ReminderOwner, item.Owner);
        Assert.Equal(Fx.At(2, 9), item.Date);
        Assert.Matches(@"^reminder:[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}$", item.Id);   // upper-case, as on the Mac
        _state.ClearAll();
        var withNote = WeekdayReminder("Stay hydrated!");
        _poller.CheckReminders(Fx.At(3, 9));
        Assert.Equal("Stay hydrated!", _state.Items.Single(i => i.Id == IdOf(withNote)).Detail);
    }

    [Fact]
    public void ReminderWeekdaysUseMacNumbersSundayIsOne()
    {
        Assert.Equal(1, Reminder.MacWeekday(DayOfWeek.Sunday));
        Assert.Equal(7, Reminder.MacWeekday(DayOfWeek.Saturday));
        var r = WeekdayReminder();
        r.Weekdays = [7];                                                          // Saturday only
        _poller.CheckReminders(Fx.At(6, 10));                                      // Friday
        Assert.Empty(_state.Items);
        _poller.CheckReminders(Fx.At(7, 10));                                      // Saturday
        Assert.Single(_state.Items);
    }

    [Fact]
    public void FiringAReminderSavesWhenItFired()
    {
        var r = WeekdayReminder();
        _poller.CheckReminders(Fx.At(2, 9));
        var back = new AppState();
        Assert.Equal(Fx.At(2, 9), back.Config.Reminders.Single(x => x.Id == r.Id).LastFired);
    }

    [Fact]
    public void RemindersWaitDuringQuietHours()
    {
        _state.Config.QuietStartHour = 13; _state.Config.QuietEndHour = 14;
        _state.Config.Reminders = [new Reminder { Title = "Stretch", EveryMinutes = 15, StartHour = 9, EndHour = 18, Weekdays = [1, 2, 3, 4, 5, 6, 7] }];
        _poller.CheckReminders(Fx.At(3, 13, 30));
        Assert.Empty(_state.Items);                                                // reminders wait during quiet hours
        _poller.CheckReminders(Fx.At(3, 14, 0));
        Assert.Single(_state.Items);
    }

    [Fact]
    public void TheReminderTickAlsoExpiresOldChatsAndWakesSnoozes()
    {
        var now = Fx.At(3, 12);
        var oldChat = Fx.Chat("old", "acc-g", "Team", "spaces/A") with { Date = now.AddHours(-30) };
        _state.Offer([oldChat]);
        var mail = Fx.Mail("m", "acc-g", "a@example.com", "Later");
        _state.Offer([mail]);
        _state.Snooze(mail, 10, now.AddMinutes(-20));                              // due since ten minutes ago
        _poller.CheckReminders(now);
        Assert.Equal([mail.Id], _state.Items.Select(i => i.Id));                   // the old chat is gone, the snooze is back

        // Even when paused or quiet, old chats clear; snoozes wait.
        _state.Config.Paused = true;
        _state.Snooze(mail, 10, now.AddMinutes(-20));
        var chat2 = Fx.Chat("old2", "acc-g", "Team", "spaces/B") with { Date = now.AddHours(-30) };
        _state.Offer([chat2]);
        _poller.CheckReminders(now);
        Assert.DoesNotContain(_state.Items, i => i.Id == chat2.Id);
        Assert.DoesNotContain(_state.Items, i => i.Id == mail.Id);
        Assert.Single(_state.SnoozedItems);
    }
}
