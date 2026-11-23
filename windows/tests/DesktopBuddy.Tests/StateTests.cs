using DesktopBuddy.Core;

namespace DesktopBuddy.Tests;

// Port of the AppState-related scenarios in Sources/DesktopBuddy/SelfTest.swift:
// inbox flows, undo, service switches, client reassignment, client health, reminders' state
// side (see PollerTests for the timers and fetching), quiet hours, mute rules, owner slots.
// Every test gets its own throwaway folder through Paths.UseFolder.

/// Tests that touch Paths or Gateway.Current share these statics, so they must not run in parallel.
[CollectionDefinition("DesktopBuddy.State", DisableParallelization = true)]
public sealed class StateCollection;

/// A throwaway storage folder for one test, and a clean Gateway afterwards.
public abstract class StorageTest : IDisposable
{
    protected string Dir { get; } = Path.Combine(Path.GetTempPath(), "desktopbuddy-tests-" + Guid.NewGuid().ToString("N"));

    protected StorageTest()
    {
        // xUnit installs its own SynchronizationContext, which would delay the continuation after an
        // awaited timer until the test ends. These tests drive the timers by hand and expect them inline.
        SynchronizationContext.SetSynchronizationContext(null);
        Directory.CreateDirectory(Dir);
        Paths.UseFolder(Dir);
        Gateway.Current = new OfflineGateway();
    }

    public void Dispose()
    {
        Paths.UseFolder(null);
        Gateway.Current = new OfflineGateway();
        try { Directory.Delete(Dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }
}

/// Made-up items and a known week, with example.com addresses only.
public static class Fx
{
    public static BuddyItem Mail(string id, string account, string address, string subject, string preview = "",
                                 DateTimeOffset? date = null, string from = "Jane Example") => new()
    {
        Id = $"mail:{account}:{id}", Source = ItemSource.mail, AccountID = account, AccountLabel = account,
        Title = subject, From = from, Detail = preview, Date = date ?? DateTimeOffset.Now, FromAddress = address,
    };

    public static BuddyItem Chat(string id, string account, string space, string reference, string text = "hi") => new()
    {
        Id = $"chat:{account}:{id}", Source = ItemSource.chat, AccountID = account, AccountLabel = account,
        Title = space, From = "Sam Example", Detail = text, Date = DateTimeOffset.Now, ChatRef = reference,
    };

    /// A known week: 2026-10-18 is a Sunday (Mac weekday 1). Local time, like the app uses.
    public static DateTimeOffset At(int weekday, int hour, int minute = 0) =>
        new(new DateTime(2026, 10, 17 + weekday, hour, minute, 0, DateTimeKind.Local));

    /// Two signed-in accounts, no clients, no reminders, no quiet hours, no sound.
    public static AppState FreshState()
    {
        var s = new AppState();
        s.ResetForTesting();
        s.Config.Accounts =
        [
            new AccountConfig { Id = "acc-g", ProviderChoice = Provider.google, Label = "Northwind", Email = "me@northwind.example.com" },
            new AccountConfig { Id = "acc-m", ProviderChoice = Provider.microsoft, Label = "Contoso", Email = "me@contoso.example.com" },
        ];
        s.Config.Clients = [];
        s.Config.Reminders = [];
        s.Config.QuietStartHour = null;
        s.Config.QuietEndHour = null;
        s.Config.Paused = false;
        s.Config.PlaySound = false;
        s.SignedIn = ["acc-g", "acc-m"];   // as if both accounts were signed in
        return s;
    }
}

[Collection("DesktopBuddy.State")]
public class StateTests : StorageTest
{
    // MARK: Notifications: arrive, dedupe, dismiss, read elsewhere

    [Fact]
    public void InboxFlows()
    {
        var s = Fx.FreshState();
        var a = Fx.Mail("n1", "acc-g", "x@example.com", "One");
        var b = Fx.Mail("n2", "acc-g", "x@example.com", "Two");
        Assert.True(s.Offer([a, b]) == 2 && s.Items.Count == 2);                 // new items are shown
        Assert.True(s.Offer([a, b]) == 0 && s.Items.Count == 2);                 // same items again are not repeated
        s.Dismiss(a.Id);
        Assert.DoesNotContain(s.Items, i => i.Id == a.Id);                       // Done removes the item
        Assert.Equal(0, s.Offer([a]));                                            // a dismissed item never pops up again
        var other = Fx.Mail("n3", "acc-m", "x@example.com", "Other account");
        s.Offer([other]);
        s.Reconcile(ItemSource.mail, "acc-g", new HashSet<string>());
        Assert.DoesNotContain(s.Items, i => i.Id == b.Id);                       // read in Gmail: disappears
        Assert.Contains(s.Items, i => i.Id == other.Id);                         // ...without touching other accounts
        s.Expanded = true;
        s.ClearAll("acc-m");
        Assert.Empty(s.Items);                                                    // Clear on one account only clears that account
        Assert.True(s.Undo is not null && s.Expanded);                            // an empty list stays open while Undo is offered
    }

    [Fact]
    public void OfferKeepsNewestFirstAndReportsTheNewestArrival()
    {
        var s = Fx.FreshState();
        var now = DateTimeOffset.Now;
        var old = Fx.Mail("o", "acc-g", "x@example.com", "Old", date: now.AddMinutes(-5));
        var recent = Fx.Mail("r", "acc-g", "x@example.com", "Recent", date: now);
        BuddyItem? arrived = null;
        s.ItemArrived += i => arrived = i;
        s.Offer([old, recent]);
        Assert.Equal([recent.Id, old.Id], s.Items.Select(i => i.Id));
        Assert.Equal(recent.Id, arrived?.Id);
        Assert.Equal(recent.Id, s.LastArrival?.Id);
    }

    [Fact]
    public void RefreshDetailsUpdatesTextOnItemsAlreadyShown()
    {
        var s = Fx.FreshState();
        var m = new BuddyItem { Id = "meet:acc-g:1", Source = ItemSource.meeting, AccountID = "acc-g", Title = "Sync", Detail = "Starts in 5 min", Date = DateTimeOffset.Now };
        s.Offer([m]);
        s.RefreshDetails([m with { Detail = "Starts in 3 min" }]);
        Assert.Equal("Starts in 3 min", s.Items.Single().Detail);
    }

    [Fact]
    public void ForgetLetsARepeatingItemShowAgain()
    {
        var s = Fx.FreshState();
        var t = new BuddyItem { Id = "task:acc-g:1", Source = ItemSource.task, AccountID = "acc-g", Title = "File report", Date = DateTimeOffset.Now };
        s.Offer([t]);
        s.Dismiss(t.Id);
        Assert.Equal(0, s.Offer([t]));
        s.Forget(t.Id);
        Assert.Equal(1, s.Offer([t]));
    }

    [Fact]
    public void SeenListIsCappedAt3000()
    {
        var s = Fx.FreshState();
        var items = Enumerable.Range(0, 3001).Select(i => new BuddyItem
        {
            Id = $"task:acc-g:{i}", Source = ItemSource.task, AccountID = "acc-g", Title = "t", Date = DateTimeOffset.Now,
        }).ToList();
        s.Offer(items);
        s.Reconcile(ItemSource.task, "acc-g", new HashSet<string>());   // empty the screen, keep the seen list
        Assert.Empty(s.Items);
        // Past 3,000 the oldest are forgotten (down to 2,500), so the first one can show again; the newest can't.
        Assert.Equal(1, s.Offer([items[0]]));
        Assert.Equal(0, s.Offer([items[3000]]));
        Assert.Equal(0, s.Offer([items[600]]));      // 0..500 were dropped; 501 onwards are still remembered
        Assert.Equal(1, s.Offer([items[500]]));
    }

    // MARK: Undo after Done and Clear

    [Fact]
    public void UndoAfterDoneAndClear()
    {
        var s = Fx.FreshState();
        var a = Fx.Mail("u1", "acc-g", "a@example.com", "One", date: DateTimeOffset.Now);
        var b = Fx.Mail("u2", "acc-g", "a@example.com", "Two", date: DateTimeOffset.Now.AddSeconds(-60));
        s.Offer([a, b]);
        s.ClearAll();
        Assert.Empty(s.Items);                                                    // Clear All empties the list
        Assert.True(s.Undo?.Items.Count == 2 && s.Undo.Label == "2 removed");     // ...and offers Undo for both
        s.UndoLast();
        Assert.Equal([a.Id, b.Id], s.Items.Select(i => i.Id));                    // Undo puts them back, newest first
        Assert.Null(s.Undo);
        s.Done(a.Id);
        Assert.True(s.Undo?.Label == "1 removed" && s.Items.Select(i => i.Id).SequenceEqual([b.Id]));   // Done offers Undo for one
        s.UndoLast();
        Assert.Contains(s.Items, i => i.Id == a.Id);                              // Undo after Done brings it back
        s.Dismiss(b.Id);
        Assert.Null(s.Undo);                                                      // removals by syncing don't offer Undo
        s.Expanded = true;
        s.ClearAll();
        Assert.True(s.Expanded);                                                  // the list stays open while Undo is offered
        s.UndoLast();
    }

    [Fact]
    public void UndoSkipsAnythingThatCameBackOnItsOwn()
    {
        var s = Fx.FreshState();
        var a = Fx.Mail("u1", "acc-g", "a@example.com", "One");
        s.Offer([a]);
        s.Done(a.Id);
        s.Forget(a.Id);
        s.Offer([a]);                  // it came back by itself
        s.UndoLast();
        Assert.Single(s.Items);
    }

    [Fact]
    public void UndoIsKeptForSixSecondsThenGone()
    {
        Assert.Equal(6, AppState.UndoSeconds);
        var s = Fx.FreshState();
        var wait = new TaskCompletionSource();
        TimeSpan asked = default;
        s.Delay = t => { asked = t; return wait.Task; };
        s.Offer([Fx.Mail("u1", "acc-g", "a@example.com", "One")]);
        s.Expanded = true;
        s.ClearAll();
        Assert.Equal(TimeSpan.FromSeconds(6), asked);
        Assert.NotNull(s.Undo);
        wait.SetResult();
        Assert.Null(s.Undo);
        Assert.False(s.Expanded);       // nothing left to show, so the list closes with Undo
    }

    [Fact]
    public void ANewerClearReplacesTheUndoAndTheOldTimerDoesNotRemoveIt()
    {
        var s = Fx.FreshState();
        var timers = new List<TaskCompletionSource>();
        s.Delay = _ => { var t = new TaskCompletionSource(); timers.Add(t); return t.Task; };
        s.Offer([Fx.Mail("a", "acc-g", "a@example.com", "A")]);
        s.ClearAll();
        s.UndoLast();
        s.Done(s.Items[0].Id);
        var second = s.Undo;
        timers[0].SetResult();          // the first timer fires late
        Assert.Same(second, s.Undo);
        timers[1].SetResult();
        Assert.Null(s.Undo);
    }

    // MARK: Service and account switches

    [Fact]
    public void ServiceAndAccountSwitches()
    {
        var s = Fx.FreshState();
        s.Offer([Fx.Mail("s1", "acc-g", "x@example.com", "Mail"),
                 Fx.Chat("s2", "acc-g", "Team", "spaces/T"),
                 Fx.Mail("s3", "acc-m", "x@example.com", "MS mail")]);
        var g = s.Account("acc-g")!;
        g.WatchMail = false;
        s.Update(g);
        Assert.DoesNotContain(s.Items, i => i.Id == "mail:acc-g:s1");             // turning Mail off clears that account's mail
        Assert.Contains(s.Items, i => i.Id == "chat:acc-g:s2");                   // ...but keeps its chats
        Assert.Contains(s.Items, i => i.Id == "mail:acc-m:s3");                   // ...and other accounts' mail
        g.Enabled = false;
        s.Update(g);
        Assert.DoesNotContain(s.Items, i => i.AccountID == "acc-g");              // turning the account off clears everything from it
        s.RemoveAccount("acc-m");
        Assert.True(s.Items.Count == 0 && s.Account("acc-m") is null);            // removing an account removes its items
    }

    [Fact]
    public void RemovingAnAccountForgetsItsStatusAndTellsTheNetworkLayer()
    {
        var s = Fx.FreshState();
        s.SetStatus("acc-g", ItemSource.mail, new ServiceStatus { Count = 3 });
        s.SetStatus("acc-m", ItemSource.mail, new ServiceStatus { Count = 1 });
        AccountConfig? gone = null;
        s.AccountRemoved += a => gone = a;
        s.RemoveAccount("acc-g");
        Assert.Equal("acc-g", gone?.Id);
        Assert.False(s.ServiceStatuses.ContainsKey("acc-g.mail"));
        Assert.True(s.ServiceStatuses.ContainsKey("acc-m.mail"));
        Assert.DoesNotContain("acc-g", s.SignedIn);
    }

    [Fact]
    public void AccountsAreCopiesSoEditsNeedUpdate()
    {
        var s = Fx.FreshState();
        var draft = s.Account("acc-g")!;
        draft.Label = "Renamed";
        draft.MutedSenders = ["x.example.com"];
        Assert.Equal("Northwind", s.Account("acc-g")!.Label);
        Assert.Empty(s.Account("acc-g")!.MutedSenders);
        s.Update(draft);
        Assert.Equal("Renamed", s.Account("acc-g")!.Label);
        Assert.Equal(["x.example.com"], s.Account("acc-g")!.MutedSenders);
    }

    [Fact]
    public void SignInStateDecidesNeedsSignIn()
    {
        var s = Fx.FreshState();
        Assert.False(s.NeedsSignIn("acc-g"));
        s.Expired.Add("acc-g");
        Assert.True(s.NeedsSignIn("acc-g"));
        s.Expired.Remove("acc-g");
        s.WrongAccount["acc-g"] = "someone@example.com";
        Assert.True(s.NeedsSignIn("acc-g"));
        s.WrongAccount.Remove("acc-g");
        s.SignedIn.Remove("acc-g");
        Assert.True(s.NeedsSignIn("acc-g"));
    }

    [Fact]
    public void ServiceStatusIsKeyedByAccountAndSourceAndCountsErrors()
    {
        var s = Fx.FreshState();
        Assert.Equal(0, s.ErrorCount("acc-g"));
        Assert.Equal(0, s.Status("acc-g", ItemSource.mail).Count);
        s.SetStatus("acc-g", ItemSource.chat, new ServiceStatus { Error = "boom" });
        s.SetStatus("acc-g", ItemSource.task, new ServiceStatus { Error = "boom" });
        s.SetStatus("acc-g", ItemSource.mail, new ServiceStatus { Count = 4 });
        Assert.True(s.ServiceStatuses.ContainsKey("acc-g.chat"));
        Assert.Equal(2, s.ErrorCount("acc-g"));
        Assert.Equal(0, s.ErrorCount("acc-m"));
    }

    // MARK: Clients added or changed later

    [Fact]
    public void ClientsAddedOrChangedLater()
    {
        var s = Fx.FreshState();
        var m1 = Fx.Mail("c1", "acc-g", "pm@acme.example.com", "Release");
        var c1 = Fx.Chat("c2", "acc-g", "Acme Team", "spaces/G");
        s.Offer([m1, c1]);
        Assert.All(s.Items, i => Assert.Equal("acc-g", i.Owner));                 // before the client exists, items belong to the account
        var acme = new ClientConfig { Name = "Acme", Senders = ["acme.example.com"] };
        s.AddClient(acme);
        Assert.All(s.Items, i => Assert.Equal(acme.Id, i.Owner));                 // adding a client moves matching items under it
        Assert.Equal(acme.Id, s.ActiveOwners.First());                            // client shows first, before accounts
        acme.Enabled = false;
        s.UpdateClient(acme);
        Assert.All(s.Items, i => Assert.Equal("acc-g", i.Owner));                 // turning the client off moves them back
        acme.Enabled = true;
        acme.AccountIDs = ["acc-m"];
        s.UpdateClient(acme);
        Assert.All(s.Items, i => Assert.Equal("acc-g", i.Owner));                 // a client limited to another account doesn't take these
        s.RemoveClient(acme.Id);
        Assert.Equal(2, s.Items.Count);                                           // removing the client leaves items with the account
        var tagged = ClientMatcher.Assign([Fx.Mail("c3", "acc-g", "a@example.com", "x")], "acc-g", s.Config.Clients);
        Assert.Null(tagged[0].ClientID);                                          // no clients: nothing tagged
    }

    [Fact]
    public void ClientsAreCopiesSoEditsNeedUpdateClient()
    {
        var s = Fx.FreshState();
        var c = new ClientConfig { Name = "Acme", Keywords = ["Acme"] };
        s.AddClient(c);
        c.Keywords.Add("Other");                              // editing the caller's copy...
        Assert.Equal(["Acme"], s.Client(c.Id)!.Keywords);     // ...doesn't change the stored client
        s.UpdateClient(c);
        Assert.Equal(["Acme", "Other"], s.Client(c.Id)!.Keywords);
    }

    // MARK: Client health

    [Fact]
    public void ClientHealth()
    {
        var s = Fx.FreshState();
        var dm = new ChatRef { AccountID = "acc-g", ChatID = "spaces/DM", Name = "Sam Taylor", Kind = ChatKind.direct };
        var c = new ClientConfig { Name = "acme", Chats = [dm] };
        s.AddClient(c);
        Assert.Empty(s.ClientIssues(c));                                          // a healthy client has no issues

        var g = s.Account("acc-g")!;
        g.WatchChat = false; s.Update(g);
        var issues = s.ClientIssues(c);
        Assert.Contains(issues, i => i.Chat == dm && i.Text.Contains("turned off"));          // chat off on the chat's account is flagged
        Assert.Contains(issues, i => i.Fix == ClientFix.TurnOnService("acc-g", ItemSource.chat));   // ...with a Turn On fix
        Assert.Equal(["acme"], s.ClientsUsing(g, ItemSource.chat).Select(x => x.Name));        // account page says who depends on it
        s.Apply(ClientFix.TurnOnService("acc-g", ItemSource.chat), c.Id);
        Assert.True(s.Account("acc-g")!.WatchChat && s.ClientIssues(c).Count == 0);           // the fix turns chat back on

        g = s.Account("acc-g")!; g.Enabled = false; s.Update(g);
        Assert.Contains(s.ClientIssues(c), i => i.Fix == ClientFix.TurnOnAccount("acc-g"));   // account off is flagged
        g.Enabled = true; s.Update(g);

        c.AccountIDs = ["acc-m"]; s.UpdateClient(c);
        Assert.Contains(s.ClientIssues(c), i => i.Fix == ClientFix.AddToLookIn("acc-g"));     // chat outside Look in is flagged
        c.AccountIDs = []; s.UpdateClient(c);

        s.Expired.Add("acc-g");
        Assert.Contains(s.ClientIssues(c), i => i.Fix == ClientFix.SignIn("acc-g"));          // expired sign-in is flagged
        s.Expired.Remove("acc-g");

        c.Keywords = ["team"]; s.UpdateClient(c);
        Assert.Contains(s.ClientIssues(c), i => i.Id == "kw-team");                           // generic keyword is flagged
        c.Keywords = []; c.Chats = []; c.MatchChatNames = false; s.UpdateClient(c);
        Assert.Contains(s.ClientIssues(c), i => i.Id == "norules");                           // client with no rules is flagged
        c.Enabled = false; s.UpdateClient(c);
        Assert.Equal(["off"], s.ClientIssues(c).Select(i => i.Id));                           // client turned off says so (and only that)

        c.Enabled = true; c.Chats = [dm]; s.UpdateClient(c);
        s.RemoveAccount("acc-g");
        Assert.Empty(s.Client(c.Id)!.Chats);                                      // removing an account removes its chats from clients
    }

    [Fact]
    public void ClientHealthWordingAndFixLabels()
    {
        var s = Fx.FreshState();
        var g = s.Account("acc-g")!;
        g.WatchChat = false; s.Update(g);
        var b = s.BlockerFor(s.Account("acc-g")!, ItemSource.chat)!.Value;
        Assert.Equal("Google Chat is turned off for Northwind", b.Text);
        Assert.Equal("Turn On Google Chat", b.FixLabel);
        s.SignedIn.Remove("acc-m");
        Assert.Equal("Contoso isn't signed in", s.BlockerFor(s.Account("acc-m")!, ItemSource.mail)!.Value.Text);
        Assert.Equal("Sign In…", s.BlockerFor(s.Account("acc-m")!, ItemSource.mail)!.Value.FixLabel);
        s.Expired.Add("acc-m");
        Assert.Equal("Contoso's sign-in has expired", s.BlockerFor(s.Account("acc-m")!, ItemSource.mail)!.Value.Text);
        Assert.Equal("Sign In Again…", s.BlockerFor(s.Account("acc-m")!, ItemSource.mail)!.Value.FixLabel);
        Assert.Null(s.BlockerFor(s.Account("acc-g")!, ItemSource.mail));
    }

    [Fact]
    public void EmailRulesAreFlaggedOnlyWhenEveryAccountIsBlocked()
    {
        var s = Fx.FreshState();
        var c = new ClientConfig { Name = "Acme", Senders = ["acme.example.com"], MatchChatNames = false };
        s.AddClient(c);
        Assert.Empty(s.ClientIssues(c));
        s.SignedIn.Remove("acc-g");
        Assert.Empty(s.ClientIssues(c));                      // acc-m still checks mail
        s.SignedIn.Remove("acc-m");
        var issue = Assert.Single(s.ClientIssues(c));
        Assert.Equal("mail", issue.Id);
        Assert.StartsWith("Email rules aren't being checked: ", issue.Text);
    }

    [Fact]
    public void FixesCanAskForAnImmediateCheckAndTheSignInPage()
    {
        var s = Fx.FreshState();
        var c = new ClientConfig { Name = "Acme", Senders = ["acme.example.com"] };
        s.AddClient(c);
        var g = s.Account("acc-g")!; g.Enabled = false; s.Update(g);
        var polled = new List<string>();
        string? signIn = null;
        s.PollRequested += a => polled.Add(a.Id);
        s.SignInRequested += id => signIn = id;
        s.Apply(ClientFix.TurnOnAccount("acc-g"), c.Id);
        Assert.True(s.Account("acc-g")!.Enabled);
        Assert.Equal(["acc-g"], polled);
        s.Apply(ClientFix.SignIn("acc-m"), c.Id);
        Assert.Equal("acc-m", signIn);
        s.Apply(ClientFix.AddToLookIn("acc-g"), c.Id);
        Assert.Equal(["acc-g"], s.Client(c.Id)!.AccountIDs);
        s.Apply(ClientFix.TurnOnClient, c.Id);
    }

    [Fact]
    public void ClientIssueIdsForPickedChats()
    {
        var s = Fx.FreshState();
        var gone = new ChatRef { AccountID = "acc-x", ChatID = "spaces/Z", Name = "Old chat" };
        var c = new ClientConfig { Name = "Acme", Chats = [gone] };
        s.AddClient(c);
        var issue = Assert.Single(s.ClientIssues(c));
        Assert.Equal("“Old chat” belongs to an account that was removed.", issue.Text);
        Assert.Equal(ClientFix.RemoveChat(gone), issue.Fix);
        s.Apply(issue.Fix!, c.Id);
        Assert.Empty(s.Client(c.Id)!.Chats);
    }

    // MARK: Mute rules saved on an account

    [Fact]
    public void SavingMuteRulesClearsMatchingMailAtOnce()
    {
        var s = Fx.FreshState();
        var g = s.Account("acc-g")!;
        g.MutedSenders = ["news.example.com", "noreply@shop.example.com"];
        g.MutedKeywords = ["newsletter"];
        var keep = Fx.Mail("f1", "acc-g", "jo@client.example.com", "Contract");
        var drop = Fx.Mail("f2", "acc-g", "a@news.example.com", "Weekly");
        s.Config.Clients = [new ClientConfig { Name = "News", Senders = ["news.example.com"] }];
        s.Offer([drop, keep]);
        s.Update(g);
        Assert.DoesNotContain(s.Items, i => i.Id == drop.Id);                     // saving a mute rule clears matching mail at once
        Assert.Contains(s.Items, i => i.Id == keep.Id);                           // ...and keeps other mail
        var other = Fx.Mail("f3", "acc-m", "a@news.example.com", "Weekly");
        Assert.False(MailFilter.IsMuted(other, s.Account("acc-m")!));             // rules don't leak to another account
        s.MutedMail = ["mail:acc-g:old"];
        g.MutedKeywords = [];
        s.Update(g);
        Assert.Empty(s.MutedMail);                                                // changing rules forgets cached muted mail
    }

    [Fact]
    public void MutingAChatClearsItAtOnce()
    {
        var s = Fx.FreshState();
        var h = s.Account("acc-g")!;
        h.MutedChats = [new ChatRef { AccountID = h.Id, ChatID = "spaces/LOUD", Name = "All hands" }];
        h.MutedKeywords = ["standup"];
        var loud = Fx.Chat("c8", h.Id, "All hands", "spaces/LOUD");
        var calm = Fx.Chat("c9", h.Id, "Design", "spaces/QUIET");
        var plain = h.Clone(); plain.MutedChats = []; plain.MutedKeywords = [];
        s.Update(plain);
        s.Offer([loud, calm]);
        s.Update(h);
        Assert.DoesNotContain(s.Items, i => i.Id == loud.Id);                     // muting a chat clears it at once
        Assert.Contains(s.Items, i => i.Id == calm.Id);
    }

    // MARK: Snooze, wake, stale chats

    [Fact]
    public void SnoozeComesBackAndSurvivesQuitting()
    {
        var s = Fx.FreshState();
        var item = Fx.Mail("z1", "acc-g", "a@example.com", "Later");
        s.Offer([item]);
        var t0 = Fx.At(3, 10);
        s.Snooze(item, 10, t0);
        Assert.Empty(s.Items);
        Assert.Single(s.SnoozedItems);
        s.WakeSnoozed(t0.AddMinutes(9));
        Assert.Empty(s.Items);                                                    // not yet
        var back = new AppState();                                                // quit and reopen
        Assert.Single(back.SnoozedItems);
        BuddyItem? arrived = null;
        back.ItemArrived += i => arrived = i;
        back.WakeSnoozed(t0.AddMinutes(11));
        Assert.Equal(item.Id, back.Items.Single().Id);
        Assert.Equal(t0.AddMinutes(11), back.Items[0].Date);                      // shows as new
        Assert.Equal(item.Id, arrived?.Id);
        Assert.Empty(back.SnoozedItems);
    }

    [Fact]
    public void SnoozingTwiceKeepsOneSnooze()
    {
        var s = Fx.FreshState();
        var item = Fx.Mail("z1", "acc-g", "a@example.com", "Later");
        s.Snooze(item, 10, Fx.At(3, 10));
        s.Snooze(item, 60, Fx.At(3, 10));
        Assert.Equal(Fx.At(3, 11), s.SnoozedItems.Single().Until);
    }

    [Fact]
    public void ChatsExpireAfterADayButMailDoesNot()
    {
        var s = Fx.FreshState();
        var now = Fx.At(3, 12);
        var oldChat = Fx.Chat("old", "acc-g", "Team", "spaces/A") with { Date = now.AddHours(-25) };
        var freshChat = Fx.Chat("new", "acc-g", "Team", "spaces/B") with { Date = now.AddHours(-23) };
        var oldMail = Fx.Mail("m", "acc-g", "a@example.com", "Old", date: now.AddDays(-3));
        s.Offer([oldChat, freshChat, oldMail]);
        s.ExpireStaleChats(now);
        Assert.Equal(new[] { freshChat.Id, oldMail.Id }.Order(), s.Items.Select(i => i.Id).Order());
    }

    // MARK: Owner slots (logo order)

    [Fact]
    public void LogosKeepTheirPlace()
    {
        string[] order = ["acme", "northwind", "contoso", "reminders"];
        Assert.Equal(["acme", "northwind"], AppState.Slots([], new HashSet<string> { "northwind", "acme" }, order));   // first logos follow the usual order
        Assert.Equal(["reminders", "acme", "northwind"],
            AppState.Slots(["acme", "northwind"], new HashSet<string> { "acme", "northwind", "reminders" }, order));   // a new logo joins on the left
        Assert.Equal(["reminders", "northwind"],
            AppState.Slots(["reminders", "acme", "northwind"], new HashSet<string> { "reminders", "northwind" }, order));   // a leaving logo doesn't reorder the rest
    }

    [Fact]
    public void ActiveOwnersFollowSlotsAsItemsComeAndGo()
    {
        var s = Fx.FreshState();
        s.Offer([Fx.Mail("1", "acc-m", "a@example.com", "x")]);
        s.Offer([Fx.Mail("2", "acc-g", "a@example.com", "y")]);
        Assert.Equal(["acc-g", "acc-m"], s.ActiveOwners);                         // the newcomer joins on the left
        s.Dismiss("mail:acc-g:2");
        Assert.Equal(["acc-m"], s.ActiveOwners);
    }

    [Fact]
    public void OwnerNamesTintsAndLogos()
    {
        var s = Fx.FreshState();
        var c = new ClientConfig { Name = "Acme", TintChoice = AccountTint.orange };
        s.AddClient(c);
        Assert.Equal("Reminders", s.OwnerName(BuddyItem.ReminderOwner));
        Assert.Equal("Northwind", s.OwnerName("acc-g"));
        Assert.Equal("Acme", s.OwnerName(c.Id));
        Assert.Equal("Account", s.OwnerName("nobody"));
        Assert.Equal(AccountTint.teal, s.OwnerTint(BuddyItem.ReminderOwner));
        Assert.Equal(AccountTint.purple, s.OwnerTint("acc-m"));                   // Microsoft default tint
        Assert.Equal(AccountTint.orange, s.OwnerTint(c.Id));
        Assert.Equal(AccountTint.blue, s.OwnerTint("nobody"));
        Assert.Null(s.OwnerLogo("acc-g"));                                        // no bundled logo matches: monogram
        var named = s.Client(c.Id)!; named.Name = "Riskhawk Group"; s.UpdateClient(named);
        Assert.Equal(new LogoRef(null, "riskhawk"), s.OwnerLogo(c.Id));
        Assert.Null(s.OwnerLogo(BuddyItem.ReminderOwner));
        s.Config.ReminderLogoPath = "bell.png";
        Assert.Equal(new LogoRef("bell.png", null), s.OwnerLogo(BuddyItem.ReminderOwner));
    }

    [Fact]
    public void ToggleListOpensFiltersAndCloses()
    {
        var s = Fx.FreshState();
        s.Offer([Fx.Mail("1", "acc-g", "a@example.com", "x"), Fx.Mail("2", "acc-m", "a@example.com", "y")]);
        s.ToggleList("acc-g");
        Assert.True(s.Expanded);
        Assert.Equal("acc-g", s.FilterOwner);
        s.ToggleList("acc-m");
        Assert.True(s.Expanded);
        Assert.Equal("acc-m", s.FilterOwner);
        s.ToggleList("acc-m");
        Assert.False(s.Expanded);
    }

    [Fact]
    public void RemovingItemsTidiesUpTheListAndFilter()
    {
        var s = Fx.FreshState();
        var a = Fx.Mail("1", "acc-g", "a@example.com", "x");
        s.Offer([a]);
        s.ToggleList("acc-g");
        Assert.Equal(a.Id, s.LastArrival?.Id);
        s.Dismiss(a.Id);
        Assert.Null(s.LastArrival);
        Assert.Null(s.FilterOwner);
        Assert.False(s.Expanded);
    }

    // MARK: Quiet hours

    [Fact]
    public void QuietHours()
    {
        var s = Fx.FreshState();
        s.Config.QuietStartHour = 22; s.Config.QuietEndHour = 8;
        Assert.True(s.IsQuiet(Fx.At(3, 23)));                  // overnight window: 23:00 is quiet
        Assert.True(s.IsQuiet(Fx.At(3, 7, 59)));               // 07:59 is quiet
        Assert.False(s.IsQuiet(Fx.At(3, 8)));                  // 08:00 is not
        Assert.False(s.IsQuiet(Fx.At(3, 12)));                 // noon is not
        s.Config.QuietStartHour = 13; s.Config.QuietEndHour = 14;
        Assert.True(s.IsQuiet(Fx.At(3, 13, 30)));              // daytime window: 13:30 is quiet
        s.Config.QuietStartHour = 9; s.Config.QuietEndHour = 9;
        Assert.False(s.IsQuiet(Fx.At(3, 9)));                  // same start and end means no quiet hours
        s.Config.QuietStartHour = null; s.Config.QuietEndHour = null;
        Assert.False(s.IsQuiet(Fx.At(3, 23)));
    }

    [Fact]
    public void IsQuietNowUsesTheInjectedClock()
    {
        var s = Fx.FreshState();
        s.Config.QuietStartHour = 22; s.Config.QuietEndHour = 8;
        s.Now = () => Fx.At(3, 23);
        Assert.True(s.IsQuietNow);
        s.Now = () => Fx.At(3, 12);
        Assert.False(s.IsQuietNow);
    }

    // MARK: Saving and loading

    [Fact]
    public void EverythingSurvivesQuitting()
    {
        var s = Fx.FreshState();
        var a = Fx.Mail("1", "acc-g", "a@example.com", "Kept");
        s.Offer([a]);
        s.Config.PollSeconds = 120;
        s.SaveConfig();
        var back = new AppState();
        Assert.Equal(120, back.Config.PollSeconds);
        Assert.Equal(["acc-g", "acc-m"], back.Config.Accounts.Select(x => x.Id));
        Assert.Equal(a.Id, back.Items.Single().Id);
        Assert.Equal(0, back.Offer([a]));                                         // it was already seen
    }

    [Fact]
    public void AssigningConfigSavesIt()
    {
        var s = Fx.FreshState();
        s.Config = new AppConfig { PollSeconds = 99 };
        Assert.Equal(99, new AppState().Config.PollSeconds);
    }

    [Fact]
    public void FirstRunSeedsTheTeamClientsOnce()
    {
        var s = new AppState();
        Assert.Equal(["Gatehouse", "RiskHawk"], s.Config.Clients.Select(c => c.Name));
        Assert.Equal(true, s.Config.SeededClients);
        s.RemoveClient(s.Config.Clients[0].Id);
        Assert.Single(new AppState().Config.Clients);                             // not seeded again
    }

    [Fact]
    public void ItemsFromVersionOneAttachToTheirAccountByLabel()
    {
        var cfg = new AppConfig { SeededClients = true };
        cfg.Accounts.Add(new AccountConfig { Id = "A1", Label = "Work", Email = "me@work.example.com" });
        JsonFile.Save(cfg, Paths.Config);
        JsonFile.Save(new List<BuddyItem>
        {
            new() { Id = "old:1", Source = ItemSource.mail, AccountLabel = "Work", Title = "Hi", Date = DateTimeOffset.Now },
        }, Paths.Inbox);
        var s = new AppState();
        Assert.Equal("A1", s.Items.Single().AccountID);
    }

    [Fact]
    public void ExistingItemsAreSortedUnderClientsOnStartup()
    {
        var cfg = new AppConfig { SeededClients = true };
        cfg.Accounts.Add(new AccountConfig { Id = "A1", Label = "Work", Email = "me@work.example.com" });
        cfg.Clients.Add(new ClientConfig { Id = "C1", Name = "Acme", Senders = ["acme.example.com"] });
        JsonFile.Save(cfg, Paths.Config);
        JsonFile.Save(new List<BuddyItem> { Fx.Mail("1", "A1", "pm@acme.example.com", "Hello") }, Paths.Inbox);
        var s = new AppState();
        Assert.Equal("C1", s.Items.Single().ClientID);
        Assert.Equal(["C1"], s.ActiveOwners);
    }

    // MARK: Events and demo helpers

    [Fact]
    public void ChangedIsRaisedOnceForOneUserAction()
    {
        var s = Fx.FreshState();
        s.Offer([Fx.Mail("1", "acc-g", "a@example.com", "x")]);
        var count = 0;
        s.Changed += () => count++;
        s.Done("mail:acc-g:1");
        Assert.Equal(1, count);
        s.UndoLast();
        Assert.Equal(2, count);
        s.Offer([]);                                                              // nothing new: nothing to announce
        Assert.Equal(2, count);
    }

    [Fact]
    public void UiStateRaisesChangedOnlyWhenItChanges()
    {
        var s = Fx.FreshState();
        var count = 0;
        s.Changed += () => count++;
        s.Expanded = true; s.Expanded = true;
        s.UsingKeyboard = true;
        s.ScreenshotsAllowedUntil = Fx.At(3, 10);
        s.FilterOwner = "acc-g";
        Assert.Equal(4, count);
        Assert.True(s.Expanded && s.UsingKeyboard);
    }

    [Fact]
    public void BatchRaisesChangedOnceAtTheEnd()
    {
        var s = Fx.FreshState();
        var count = 0;
        s.Changed += () => count++;
        s.Batch(() => { s.Expanded = true; s.UsingKeyboard = true; s.FilterOwner = "x"; });
        Assert.Equal(1, count);
    }

    [Fact]
    public void ArrivalsAskForTheLeadSourcesSound()
    {
        var s = Fx.FreshState();
        s.Config.PlaySound = true;
        var played = new List<string>();
        s.SoundRequested += played.Add;
        s.Config.SetSound("Chime", ItemSource.meeting);
        s.Offer([Fx.Mail("1", "acc-g", "a@example.com", "x"),
                 new BuddyItem { Id = "meet:acc-g:1", Source = ItemSource.meeting, AccountID = "acc-g", Title = "Sync", Date = DateTimeOffset.Now }]);
        Assert.Equal(["Chime"], played);                                          // the meeting's sound wins over mail's
        s.Config.SetSound(AppConfig.NoSound, ItemSource.mail);
        s.Offer([Fx.Mail("2", "acc-g", "a@example.com", "y")]);
        Assert.Single(played);                                                    // a silent type plays nothing
        s.Config.PlaySound = false;
        s.Offer([Fx.Chat("3", "acc-g", "Team", "spaces/T")]);
        Assert.Single(played);
    }

    [Fact]
    public void DemoItemsAreSamplesThatCanBePurged()
    {
        var s = Fx.FreshState();
        s.Now = () => Fx.At(3, 10);
        s.Offer([Fx.Mail("real", "acc-g", "a@example.com", "Real")]);
        s.OfferDemoItems();
        Assert.Equal(4, s.Items.Count);
        s.PurgeDemoItems();
        Assert.Equal(["mail:acc-g:real"], s.Items.Select(i => i.Id));
    }

    [Fact]
    public void OpeningAnItemOpensItsLinkAndRemovesIt()
    {
        var s = Fx.FreshState();
        var item = Fx.Mail("1", "acc-g", "a@example.com", "x") with { Url = "https://mail.example.com/1" };
        s.Offer([item]);
        string? opened = null;
        s.OpenUrlRequested += u => opened = u;
        s.Open(item);
        Assert.Equal("https://mail.example.com/1", opened);
        Assert.Empty(s.Items);
    }

    // MARK: Status log

    [Fact]
    public void StatusLogKeepsCountsAndRotatesPastOneMegabyte()
    {
        StatusLog.Write("Northwind mail: fetched 2, live 2, new 1");
        var text = File.ReadAllText(Paths.StatusLog);
        Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ Northwind mail: fetched 2, live 2, new 1\n$", text);
        File.WriteAllText(Paths.StatusLog, new string('x', 1_000_001));
        StatusLog.Write("after rotation");
        Assert.True(File.Exists(StatusLog.OldPath));
        Assert.True(new FileInfo(StatusLog.OldPath).Length > 1_000_000);
        Assert.Contains("after rotation", File.ReadAllText(Paths.StatusLog));
        Assert.True(new FileInfo(Paths.StatusLog).Length < 200);
    }
}
