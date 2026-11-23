using System.Globalization;
using DesktopBuddy.Core;
using DesktopBuddy.Presentation.Board;

namespace DesktopBuddy.Tests;

/// The Board's view model: grouping, filter, the problem line, keyboard commands, Undo, empty state,
/// times, and where the window goes. Made-up data only.
[Collection("DesktopBuddy.State")]
public class BoardViewModelTests : StorageTest
{
    private static readonly CultureInfo English = new("en-US", useUserOverride: false);

    private static BuddyItem Meeting(string id, string account, DateTimeOffset start) => new()
    {
        Id = $"meet:{account}:{id}", Source = ItemSource.meeting, AccountID = account, AccountLabel = account,
        Title = "Design review", From = "Sample Calendar", Detail = "Starts soon", Date = start, Url = "https://meet.example.com/x",
    };

    private static BuddyItem Task(string id, string account, DateTimeOffset date) => new()
    {
        Id = $"task:{account}:{id}", Source = ItemSource.task, AccountID = account, AccountLabel = account,
        Title = "Send sprint report", Detail = "Google Tasks", Date = date,
    };

    /// Two accounts: Northwind (acc-g) with three items, Contoso (acc-m) with two, at a fixed time.
    private static (AppState S, DateTimeOffset Now) Seeded()
    {
        var s = Fx.FreshState();
        var now = Fx.At(3, 14, 0);
        s.Now = () => now;
        s.Delay = _ => new TaskCompletionSource().Task;        // Undo never expires on its own
        s.Offer(
        [
            Fx.Mail("g1", "acc-g", "a@example.com", "Invoice approval", "Please approve", now.AddMinutes(-2), "Sam Example"),
            Fx.Chat("g2", "acc-g", "Project room", "spaces/x", "Deploy is done"),
            Task("g3", "acc-g", now.AddMinutes(-30)),
            Fx.Mail("m1", "acc-m", "b@example.com", "Contract", "Draft attached", now.AddMinutes(-11), "Jane Example"),
            Meeting("m2", "acc-m", now.AddMinutes(5)),
        ]);
        return (s, now);
    }

    private static BoardViewModel Vm(AppState s, string? owner = null) => new(s, owner: owner) { Culture = English };

    // MARK: - Grouping

    [Fact]
    public void GroupsFollowActiveOwnersOrderWithCounts()
    {
        var (s, _) = Seeded();
        using var vm = Vm(s);
        Assert.Equal(s.ActiveOwners, vm.Groups.Select(g => g.OwnerId).ToList());
        Assert.Equal(["acc-g", "acc-m"], vm.Groups.Select(g => g.OwnerId).ToList());
        Assert.Equal([3, 2], vm.Groups.Select(g => g.Count).ToList());
        Assert.Equal(5, vm.TotalCount);
        Assert.Equal(["Northwind", "Contoso"], vm.Groups.Select(g => g.Name).ToList());
        Assert.True(vm.Groups[0].IsFirst);
        Assert.False(vm.Groups[1].IsFirst);
        Assert.All(vm.Groups, g => Assert.True(g.ShowHeader));
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public void RowsAreNumberedAcrossGroupsInListOrder()
    {
        var (s, _) = Seeded();
        using var vm = Vm(s);
        Assert.Equal([0, 1, 2, 3, 4], vm.Rows.Select(r => r.Index).ToList());
        Assert.Equal("BoardRow_3", vm.Rows[3].RowAutomationId);
        Assert.Equal("BoardDone_3", vm.Rows[3].DoneAutomationId);
        Assert.Equal("BoardLater_3", vm.Rows[3].LaterAutomationId);
        Assert.Equal(vm.Groups.SelectMany(g => g).Select(r => r.Id), vm.Rows.Select(r => r.Id));
    }

    [Fact]
    public void RowShowsSenderThenSubjectOrTitleThenDetail()
    {
        var (s, _) = Seeded();
        using var vm = Vm(s);
        var mail = vm.Rows.First(r => r.Item.Source == ItemSource.mail);
        Assert.Equal("Sam Example", mail.Sender);
        Assert.Equal("Invoice approval", mail.Secondary);
        var task = vm.Rows.First(r => r.Item.Source == ItemSource.task);       // no sender: the title leads
        Assert.Equal("Send sprint report", task.Sender);
        Assert.Equal("Google Tasks", task.Secondary);
        Assert.Equal(ItemSource.mail.Glyph(), mail.Glyph);
        Assert.True(mail.IsMail); Assert.False(mail.IsChat); Assert.True(task.IsTask);
    }

    [Fact]
    public void ReminderItemsGetTheirOwnGroup()
    {
        var (s, now) = Seeded();
        s.Offer([new BuddyItem { Id = "rem1", Source = ItemSource.reminder, Title = "Stretch", Detail = "Stand up", Date = now }]);
        using var vm = Vm(s);
        var g = vm.Groups.Single(g => g.OwnerId == BuddyItem.ReminderOwner);
        Assert.Equal("Reminders", g.Name);
        Assert.True(g.IsReminders);
    }

    // MARK: - Filter

    [Fact]
    public void FilterShowsOneOwnerAndHidesItsGroupHeader()
    {
        var (s, _) = Seeded();
        using var vm = Vm(s, owner: "acc-m");
        Assert.True(vm.IsFiltered);
        Assert.Equal("Contoso", vm.Title);
        Assert.Equal("Contoso", vm.FilterName);
        Assert.Equal(2, vm.TotalCount);
        var g = Assert.Single(vm.Groups);
        Assert.Equal("acc-m", g.OwnerId);
        Assert.False(g.ShowHeader);
        Assert.Equal([0, 1], vm.Rows.Select(r => r.Index).ToList());
    }

    [Fact]
    public void ShowEverythingLeavesTheFilter()
    {
        var (s, _) = Seeded();
        using var vm = Vm(s, owner: "acc-m");
        vm.ShowEverything();
        Assert.False(vm.IsFiltered);
        Assert.Equal("Waiting", vm.Title);
        Assert.Equal(5, vm.TotalCount);
        Assert.Equal(2, vm.Groups.Count);
        Assert.Null(vm.FilterOwner);
        Assert.Null(s.FilterOwner);                       // the shared AppState filter is never touched
    }

    [Fact]
    public void FilteredClearAllClearsOnlyThatOwner()
    {
        var (s, _) = Seeded();
        using var vm = Vm(s, owner: "acc-m");
        vm.ClearAll();
        Assert.All(s.Items, i => Assert.Equal("acc-g", i.Owner));
        Assert.Equal(3, s.Items.Count);
        Assert.True(vm.IsEmpty);                          // nothing left for this owner
        Assert.True(vm.HasUndo);
    }

    // MARK: - The problem line

    [Fact]
    public void NoProblemWhenAllIsWell()
    {
        var (s, _) = Seeded();
        using var vm = Vm(s);
        Assert.Null(vm.Problem);
    }

    [Fact]
    public void ExpiredSignInIsOneLineWithSignInAgain()
    {
        var (s, _) = Seeded();
        s.SetExpired("acc-m", true);
        using var vm = Vm(s);
        var p = vm.Problem!;
        Assert.Equal(BoardProblemKind.SignIn, p.Kind);
        Assert.Equal("Contoso: sign-in expired", p.Title);
        Assert.Equal("Sign in again", p.ActionText);
        Assert.Equal("account:acc-m", p.SettingsPage);
        Assert.Equal(BoardSeverity.Caution, p.Severity);
    }

    [Fact]
    public void WrongAccountAndNotSignedInAreSignInProblemsToo()
    {
        var (s, _) = Seeded();
        s.WrongAccount["acc-g"] = "someone@else.example.com";
        using (var vm = Vm(s)) Assert.Equal("Northwind: signed in as someone else", vm.Problem!.Title);
        s.WrongAccount.Clear();
        s.SignedIn.Remove("acc-g");
        using (var vm = Vm(s))
        {
            Assert.Equal("Northwind: not signed in", vm.Problem!.Title);
            Assert.Equal("Sign in", vm.Problem.ActionText);
        }
    }

    [Fact]
    public void ServiceErrorIsOneLineWithFix()
    {
        var (s, _) = Seeded();
        s.SetStatus("acc-g", ItemSource.chat, new ServiceStatus { Error = "API disabled" });
        using var vm = Vm(s);
        var p = vm.Problem!;
        Assert.Equal(BoardProblemKind.Service, p.Kind);
        Assert.Equal("Northwind: Google Chat isn't working", p.Title);
        Assert.Equal("Fix", p.ActionText);
        Assert.Equal("account:acc-g", p.SettingsPage);
    }

    [Fact]
    public void ServiceNamesFollowTheProvider()
    {
        var (s, _) = Seeded();
        s.SetStatus("acc-m", ItemSource.chat, new ServiceStatus { Error = "x" });
        using var vm = Vm(s);
        Assert.Equal("Contoso: Teams isn't working", vm.Problem!.Title);
    }

    [Fact]
    public void ServiceErrorOnAnAccountThatNeedsSignInIsNotCountedTwice()
    {
        var (s, _) = Seeded();
        s.SetExpired("acc-g", true);
        s.SetStatus("acc-g", ItemSource.mail, new ServiceStatus { Error = "401" });
        using var vm = Vm(s);
        Assert.Equal(BoardProblemKind.SignIn, vm.Problem!.Kind);
        Assert.Equal("Northwind: sign-in expired", vm.Problem.Title);
    }

    [Fact]
    public void ErrorsOnServicesThatAreSwitchedOffOrAccountsThatAreDisabledAreIgnored()
    {
        var (s, _) = Seeded();
        var g = s.Config.Accounts.First(a => a.Id == "acc-g");
        g.WatchChat = false;
        s.SetStatus("acc-g", ItemSource.chat, new ServiceStatus { Error = "x" });
        s.Config.Accounts.First(a => a.Id == "acc-m").Enabled = false;
        s.SetExpired("acc-m", true);
        using var vm = Vm(s);
        Assert.Null(vm.Problem);
    }

    [Fact]
    public void SeveralProblemsBecomeACountWithReview()
    {
        var (s, _) = Seeded();
        s.SetExpired("acc-m", true);
        s.SetStatus("acc-g", ItemSource.chat, new ServiceStatus { Error = "x" });
        using var vm = Vm(s);
        var p = vm.Problem!;
        Assert.Equal(BoardProblemKind.Several, p.Kind);
        Assert.Equal("2 things need attention", p.Title);
        Assert.Equal("Review", p.ActionText);
        Assert.Equal("home", p.SettingsPage);
        Assert.Equal(2, p.Count);
    }

    [Fact]
    public void TwoErrorsOnOneAccountCountAsTwo()
    {
        var (s, _) = Seeded();
        s.SetStatus("acc-g", ItemSource.chat, new ServiceStatus { Error = "x" });
        s.SetStatus("acc-g", ItemSource.task, new ServiceStatus { Error = "y" });
        using var vm = Vm(s);
        Assert.Equal("2 things need attention", vm.Problem!.Title);
    }

    [Fact]
    public void ProblemActionAsksForTheRightSettingsPage()
    {
        var (s, _) = Seeded();
        s.SetExpired("acc-m", true);
        using var vm = Vm(s);
        var pages = new List<string?>();
        vm.SettingsRequested += pages.Add;
        vm.RunProblemAction();
        vm.OpenSettings();
        Assert.Equal(["account:acc-m", null], pages);
    }

    [Fact]
    public void PausedShowsANeutralLineThatResumes()
    {
        var (s, now) = Seeded();
        s.PauseFor(TimeSpan.FromHours(1));                // until 15:00 the same day
        using var vm = Vm(s);
        var p = vm.Problem!;
        Assert.Equal(BoardProblemKind.Paused, p.Kind);
        Assert.Equal(BoardSeverity.Neutral, p.Severity);
        Assert.Equal("Paused until 3:00 PM", p.Title);
        Assert.Equal("Resume", p.ActionText);
        Assert.Null(p.SettingsPage);

        vm.RunProblemAction();
        Assert.False(s.IsPausedNow);
        Assert.Null(vm.Problem);                          // refreshed by AppState.Changed
    }

    [Fact]
    public void PausedUntilTomorrowAndPausedBySwitchSaySo()
    {
        var (s, now) = Seeded();
        s.PauseUntilTomorrow();
        using (var vm = Vm(s)) Assert.Equal("Paused until tomorrow, 8:00 AM", vm.Problem!.Title);
        s.Resume();
        s.Config.Paused = true;
        using (var vm = Vm(s)) Assert.Equal("Paused", vm.Problem!.Title);
    }

    [Fact]
    public void ProblemsComeBeforePaused()
    {
        var (s, _) = Seeded();
        s.PauseFor(TimeSpan.FromHours(1));
        s.SetExpired("acc-m", true);
        using var vm = Vm(s);
        Assert.Equal(BoardProblemKind.SignIn, vm.Problem!.Kind);
    }

    // MARK: - Keyboard commands call AppState

    [Fact]
    public void FirstRowIsSelectedWhenTheBoardOpens()
    {
        var (s, _) = Seeded();
        using var vm = Vm(s);
        Assert.Same(vm.Rows[0], vm.SelectedRow);
        Assert.True(vm.Rows[0].IsSelected);
        Assert.True(vm.Rows[0].ShowActions);
        Assert.False(vm.Rows[1].ShowActions);
    }

    [Fact]
    public void ArrowsMoveThroughEveryRowAndStopAtTheEnds()
    {
        var (s, _) = Seeded();
        using var vm = Vm(s);
        vm.MoveSelection(-1);
        Assert.Equal(0, vm.SelectedRow!.Index);
        for (var i = 0; i < 3; i++) vm.MoveSelection(1);
        Assert.Equal(3, vm.SelectedRow!.Index);            // crossed into the second group
        Assert.Equal("acc-m", vm.SelectedRow.OwnerId);
        vm.MoveSelection(1);
        vm.MoveSelection(1);
        Assert.Equal(4, vm.SelectedRow!.Index);
        Assert.False(vm.Rows[0].IsSelected);
    }

    [Fact]
    public void DoneRemovesTheSelectedItemAndOffersUndo()
    {
        var (s, _) = Seeded();
        using var vm = Vm(s);
        var id = vm.SelectedRow!.Id;
        vm.DoneSelected();
        Assert.DoesNotContain(s.Items, i => i.Id == id);
        Assert.NotNull(s.Undo);
        Assert.True(vm.HasUndo);
        Assert.Equal("1 removed", vm.UndoLabel);
        Assert.Equal(4, vm.TotalCount);
    }

    [Fact]
    public void AfterDoneTheNextRowIsSelected()
    {
        var (s, _) = Seeded();
        using var vm = Vm(s);
        vm.MoveSelection(1);
        var removed = vm.SelectedRow!.Id;
        var following = vm.Rows[2].Id;
        vm.DoneSelected();
        Assert.NotEqual(removed, vm.SelectedRow!.Id);
        Assert.Equal(following, vm.SelectedRow.Id);
        Assert.Equal(1, vm.SelectedRow.Index);
    }

    [Fact]
    public void DoneOnTheLastRowSelectsTheOneBeforeIt()
    {
        var (s, _) = Seeded();
        using var vm = Vm(s);
        vm.MoveSelection(1); vm.MoveSelection(1); vm.MoveSelection(1); vm.MoveSelection(1);
        var before = vm.Rows[3].Id;
        vm.DoneSelected();
        Assert.Equal(before, vm.SelectedRow!.Id);
    }

    [Fact]
    public void EnterOpensTheItemAndRemovesIt()
    {
        var (s, _) = Seeded();
        string? opened = null;
        s.OpenUrlRequested += u => opened = u;
        using var vm = Vm(s);
        vm.SelectedRow = vm.Rows.Single(r => r.Item.Source == ItemSource.meeting);
        var meeting = vm.SelectedRow!;
        vm.OpenSelected();
        Assert.Equal("https://meet.example.com/x", opened);
        Assert.DoesNotContain(s.Items, i => i.Id == meeting.Id);
    }

    [Fact]
    public void LaterSnoozesForTheChosenMinutes()
    {
        var (s, now) = Seeded();
        using var vm = Vm(s);
        var item = vm.SelectedRow!.Item;
        vm.LaterSelected(60);
        Assert.DoesNotContain(s.Items, i => i.Id == item.Id);
        var snoozed = Assert.Single(s.SnoozedItems);
        Assert.Equal(item.Id, snoozed.Item.Id);
        Assert.Equal(now.AddMinutes(60), snoozed.Until);
    }

    [Fact]
    public void LaterOptionsAreTenMinutesOneHourAndTomorrowNine()
    {
        var (s, now) = Seeded();                           // Tuesday 2:00 PM
        using var vm = Vm(s);
        var options = vm.LaterOptions();
        Assert.Equal(["In 10 minutes", "In 1 hour", "Tomorrow at 9:00 AM"], options.Select(o => o.Label).ToList());
        Assert.Equal(10, options[0].Minutes);
        Assert.Equal(60, options[1].Minutes);
        Assert.Equal(19 * 60, options[2].Minutes);         // 14:00 to 09:00 next day
        var tomorrow = now.ToLocalTime().Date.AddDays(1).AddHours(9);
        Assert.Equal(tomorrow, now.AddMinutes(options[2].Minutes).ToLocalTime().DateTime);
    }

    [Fact]
    public void ClearAllAndClearGroupOfferUndo()
    {
        var (s, _) = Seeded();
        using var vm = Vm(s);
        vm.ClearGroup(vm.Groups[1]);
        Assert.Equal(3, s.Items.Count);
        Assert.All(s.Items, i => Assert.Equal("acc-g", i.Owner));
        Assert.Equal("2 removed", vm.UndoLabel);
        vm.ClearAll();
        Assert.Empty(s.Items);
        Assert.True(vm.IsEmpty);
        Assert.Equal("3 removed", vm.UndoLabel);
    }

    [Fact]
    public void UndoPutsTheItemsBack()
    {
        var (s, _) = Seeded();
        using var vm = Vm(s);
        vm.ClearAll();
        Assert.True(vm.IsEmpty);
        vm.Undo();
        Assert.Equal(5, vm.TotalCount);
        Assert.False(vm.HasUndo);
        Assert.Equal(5, s.Items.Count);
        Assert.NotNull(vm.SelectedRow);
    }

    [Fact]
    public void KeysWithNothingSelectedDoNothing()
    {
        var s = Fx.FreshState();
        using var vm = Vm(s);
        vm.DoneSelected(); vm.OpenSelected(); vm.LaterSelected(10); vm.MoveSelection(1); vm.Undo();
        Assert.Null(vm.SelectedRow);
        Assert.Null(s.Undo);
    }

    // MARK: - Undo bar

    [Fact]
    public void UndoBarShowsAfterDoneAndGoesWhenTheTimeRunsOut()
    {
        var (s, _) = Seeded();
        var gate = new TaskCompletionSource();
        s.Delay = _ => gate.Task;
        using var vm = Vm(s);
        Assert.False(vm.HasUndo);
        vm.DoneSelected();
        Assert.True(vm.HasUndo);
        gate.SetResult();                                  // the 6 seconds are up
        Assert.False(vm.HasUndo);
        Assert.Equal("", vm.UndoLabel);
    }

    // MARK: - Empty state

    [Fact]
    public void EmptyStateSaysWhenItWasLastChecked()
    {
        var s = Fx.FreshState();
        var now = Fx.At(3, 14, 0);
        s.Now = () => now;
        s.SetStatus("acc-g", ItemSource.mail, new ServiceStatus { LastChecked = now.AddMinutes(-7) });
        s.SetStatus("acc-m", ItemSource.mail, new ServiceStatus { LastChecked = now.AddMinutes(-2) });
        using var vm = Vm(s);
        Assert.True(vm.IsEmpty);
        Assert.Equal(0, vm.TotalCount);
        Assert.Empty(vm.Groups);
        Assert.Equal("Last checked 2 min ago", vm.EmptyDetail);
    }

    [Theory]
    [InlineData(10, "Last checked just now")]
    [InlineData(150, "Last checked 2 min ago")]
    [InlineData(3 * 3600, "Last checked 3 h ago")]
    [InlineData(2 * 86_400, "Last checked 2 d ago")]
    public void LastCheckedWording(int secondsAgo, string expected)
    {
        var s = Fx.FreshState();
        var now = Fx.At(3, 14, 0);
        s.Now = () => now;
        s.SetStatus("acc-g", ItemSource.mail, new ServiceStatus { LastChecked = now.AddSeconds(-secondsAgo) });
        using var vm = Vm(s);
        Assert.Equal(expected, vm.EmptyDetail);
    }

    [Fact]
    public void EmptyStateBeforeTheFirstCheckAndWithoutAccounts()
    {
        var s = Fx.FreshState();
        using (var vm = Vm(s)) Assert.Equal("Waiting for the first check.", vm.EmptyDetail);
        s.Config.Accounts = [];
        using (var vm = Vm(s)) Assert.Equal("Add an account in Settings to get started.", vm.EmptyDetail);
    }

    // MARK: - Times

    [Theory]
    [InlineData(5, "now")]
    [InlineData(59, "now")]
    [InlineData(60, "1m")]
    [InlineData(6 * 60, "6m")]
    [InlineData(3 * 3600 + 5, "3h")]
    [InlineData(2 * 86_400, "2d")]
    public void MailChatAndTaskTimesAreCompact(int secondsAgo, string expected)
    {
        var now = Fx.At(3, 14, 0);
        var item = Fx.Mail("t", "acc-g", "a@example.com", "Hi", date: now.AddSeconds(-secondsAgo));
        var (text, urgent) = BoardViewModel.FormatTime(item, now);
        Assert.Equal(expected, text);
        Assert.False(urgent);
    }

    [Theory]
    [InlineData(300, "in 5 min")]
    [InlineData(241, "in 5 min")]        // 4 min 1 s left still reads "in 5 min": never promises too much time
    [InlineData(60, "in 1 min")]
    [InlineData(20, "in 1 min")]
    [InlineData(0, "now")]
    [InlineData(-30, "now")]
    [InlineData(-120, "started 2m ago")]
    public void MeetingsSayWhenTheyStartAndAreUrgent(int secondsUntil, string expected)
    {
        var now = Fx.At(3, 14, 0);
        var item = Meeting("t", "acc-g", now.AddSeconds(secondsUntil));
        var (text, urgent) = BoardViewModel.FormatTime(item, now);
        Assert.Equal(expected, text);
        Assert.True(urgent);
    }

    [Fact]
    public void MeetingRowShowsTheUrgentTime()
    {
        var (s, _) = Seeded();
        using var vm = Vm(s);
        var meeting = vm.Rows.Single(r => r.Item.Source == ItemSource.meeting);
        Assert.Equal("in 5 min", meeting.TimeText);
        Assert.True(meeting.IsUrgent);
        Assert.True(meeting.ShowUrgentTime);
        Assert.Equal("2m", vm.Rows.Single(r => r.Id == "mail:acc-g:g1").TimeText);
        Assert.False(vm.Rows.Single(r => r.Id == "mail:acc-g:g1").IsUrgent);
    }

    [Fact]
    public void TimeTextMovesOnWithTheClockWithoutReplacingTheList()
    {
        var s = Fx.FreshState();
        var now = Fx.At(3, 14, 0);
        s.Now = () => now;
        s.Offer([Fx.Mail("a", "acc-g", "a@example.com", "Hello", date: now.AddMinutes(-2))]);
        using var vm = Vm(s);
        var groups = vm.Groups;
        var row = vm.Rows[0];
        Assert.Equal("2m", row.TimeText);
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        now = now.AddMinutes(3);
        vm.Refresh();
        Assert.Equal("5m", row.TimeText);
        Assert.Same(groups, vm.Groups);                    // same list: scroll position and hover survive
        Assert.Same(row, vm.Rows[0]);
        Assert.Contains(nameof(BoardRow.TimeText), changed);
    }

    [Fact]
    public void HoverShowsTheActionsInPlaceOfTheTime()
    {
        var (s, _) = Seeded();
        using var vm = Vm(s);
        var row = vm.Rows[2];
        Assert.True(row.ShowNormalTime);
        Assert.False(row.ShowActions);
        row.IsHovered = true;
        Assert.True(row.ShowActions);
        Assert.False(row.ShowNormalTime);
        row.IsHovered = false;
        Assert.True(row.ShowNormalTime);
    }

    // MARK: - Staying current

    [Fact]
    public void ListFollowsAppStateChanges()
    {
        var (s, now) = Seeded();
        using var vm = Vm(s);
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        s.Offer([Fx.Mail("new", "acc-g", "a@example.com", "Brand new", date: now)]);
        Assert.Equal(6, vm.TotalCount);
        Assert.Contains(nameof(BoardViewModel.Groups), changes);
        Assert.Equal(4, vm.Groups[0].Count);
    }

    [Fact]
    public void SelectionStaysOnTheSameItemWhenAnotherArrives()
    {
        var (s, now) = Seeded();
        using var vm = Vm(s);
        vm.MoveSelection(1);
        var id = vm.SelectedRow!.Id;
        s.Offer([Fx.Mail("new", "acc-g", "a@example.com", "Brand new", date: now.AddMinutes(1))]);
        Assert.Equal(id, vm.SelectedRow!.Id);
        Assert.Equal(2, vm.SelectedRow.Index);              // pushed down by the newcomer
    }

    [Fact]
    public void DisposedViewModelStopsListening()
    {
        var (s, now) = Seeded();
        var vm = Vm(s);
        vm.Dispose();
        s.Offer([Fx.Mail("new", "acc-g", "a@example.com", "Brand new", date: now)]);
        Assert.Equal(5, vm.TotalCount);
    }

    [Fact]
    public void WorstCaseDataKeepsTextAsGivenForTheViewToTrim()
    {
        var s = Fx.FreshState();
        var now = Fx.At(3, 14, 0);
        s.Now = () => now;
        var longName = new string('W', 200);
        s.Offer([Fx.Mail("x", "acc-g", "a@example.com", "Ünïcode 件名 こんにちは 🎉 " + new string('s', 300), from: longName, date: now.AddMinutes(-1))]);
        s.Offer(Enumerable.Range(0, 1500).Select(i => Fx.Mail("b" + i, "acc-m", "a@example.com", "Bulk " + i, date: now.AddSeconds(-i))));
        using var vm = Vm(s);
        Assert.Equal(1501, vm.TotalCount);
        Assert.Equal(longName, vm.Groups.Single(g => g.OwnerId == "acc-g")[0].Sender);
        Assert.Equal(1500, vm.Groups.Single(g => g.OwnerId == "acc-m").Count);
        Assert.Equal(1500, vm.Rows[^1].Index);
    }

    // MARK: - Where the window goes

    private static readonly PxRect Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void TaskbarEdgeComesFromWhatTheTaskbarTakesFromTheWorkArea()
    {
        var icon = new PxRect(1800, 1040, 1830, 1070);
        Assert.Equal(ScreenEdge.Bottom, BoardPlacement.EdgeOf(icon, new PxRect(0, 0, 1920, 1040), Screen));
        Assert.Equal(ScreenEdge.Top, BoardPlacement.EdgeOf(icon, new PxRect(0, 40, 1920, 1080), Screen));
        Assert.Equal(ScreenEdge.Left, BoardPlacement.EdgeOf(icon, new PxRect(60, 0, 1920, 1080), Screen));
        Assert.Equal(ScreenEdge.Right, BoardPlacement.EdgeOf(icon, new PxRect(0, 0, 1860, 1080), Screen));
        // Auto-hidden taskbar: nothing is taken, so the nearest edge wins.
        Assert.Equal(ScreenEdge.Bottom, BoardPlacement.EdgeOf(icon, Screen, Screen));
        Assert.Equal(ScreenEdge.Right, BoardPlacement.EdgeOf(new PxRect(1890, 400, 1910, 420), Screen, Screen));
    }

    [Fact]
    public void BoardOpensAboveATrayIconOnABottomTaskbar()
    {
        var work = new PxRect(0, 0, 1920, 1040);
        var icon = new PxRect(1500, 1044, 1532, 1076);
        var (x, y) = BoardPlacement.FromTray(icon, work, Screen, 404, 560, 12);
        Assert.Equal(1516 - 202, x);                       // centred on the icon
        Assert.Equal(1040 - 560 - 12, y);                  // 12 above the taskbar
    }

    [Fact]
    public void BoardStaysOnScreenWhenTheIconIsNearAnEdge()
    {
        var work = new PxRect(0, 0, 1920, 1040);
        var icon = new PxRect(1890, 1044, 1914, 1068);
        var (x, _) = BoardPlacement.FromTray(icon, work, Screen, 404, 560, 12);
        Assert.Equal(1920 - 404 - 12, x);
    }

    [Fact]
    public void BoardOpensBelowOrBesideATrayIconOnOtherTaskbarEdges()
    {
        var icon = new PxRect(1800, 4, 1824, 28);
        var top = BoardPlacement.FromTray(icon, new PxRect(0, 40, 1920, 1080), Screen, 404, 300, 12);
        Assert.Equal(40 + 12, top.Y);
        var left = BoardPlacement.FromTray(new PxRect(8, 500, 40, 532), new PxRect(60, 0, 1920, 1080), Screen, 404, 300, 12);
        Assert.Equal(60 + 12, left.X);
        Assert.Equal(516 - 150, left.Y);
        var right = BoardPlacement.FromTray(new PxRect(1880, 500, 1912, 532), new PxRect(0, 0, 1860, 1080), Screen, 404, 300, 12);
        Assert.Equal(1860 - 404 - 12, right.X);
    }

    [Fact]
    public void BoardOpensBesideTheRailForEverySpot()
    {
        var work = new PxRect(0, 0, 1920, 1040);
        // bottom rail, 400 x 70 in the corner
        var bottomRight = BoardPlacement.FromRail(new PxRect(1500, 950, 1900, 1020), DockSpot.bottomRight, work, 404, 500, 12);
        Assert.Equal((1900 - 404, 950 - 12 - 500), bottomRight);
        var bottomLeft = BoardPlacement.FromRail(new PxRect(20, 950, 420, 1020), DockSpot.bottomLeft, work, 404, 500, 12);
        Assert.Equal((20, 950 - 12 - 500), bottomLeft);
        // side rails, 70 x 400
        var leftTop = BoardPlacement.FromRail(new PxRect(20, 100, 90, 500), DockSpot.leftTop, work, 404, 500, 12);
        Assert.Equal((90 + 12, 100), leftTop);
        var leftBottom = BoardPlacement.FromRail(new PxRect(20, 600, 90, 1000), DockSpot.leftBottom, work, 404, 500, 12);
        Assert.Equal((90 + 12, 1000 - 500), leftBottom);
        var rightTop = BoardPlacement.FromRail(new PxRect(1830, 100, 1900, 500), DockSpot.rightTop, work, 404, 500, 12);
        Assert.Equal((1830 - 12 - 404, 100), rightTop);
        var rightBottom = BoardPlacement.FromRail(new PxRect(1830, 600, 1900, 1000), DockSpot.rightBottom, work, 404, 500, 12);
        Assert.Equal((1830 - 12 - 404, 1000 - 500), rightBottom);
    }

    [Fact]
    public void ClampKeepsTheBoardInsideAGap()
    {
        var work = new PxRect(0, 0, 800, 600);
        Assert.Equal((12, 12), BoardPlacement.Clamp(-50, -50, work, 404, 300, 12));
        Assert.Equal((800 - 404 - 12, 600 - 300 - 12), BoardPlacement.Clamp(900, 900, work, 404, 300, 12));
        Assert.Equal((12, 12), BoardPlacement.Clamp(5, 5, new PxRect(0, 0, 300, 200), 404, 300, 12));   // bigger than the screen
    }

    [Theory]
    [InlineData(1000, 560)]
    [InlineData(600, 560)]
    [InlineData(500, 476)]
    [InlineData(100, 200)]
    public void BoardHeightIsCappedAt560AndShrinksOnSmallScreens(double workHeight, double expected) =>
        Assert.Equal(expected, BoardPlacement.MaxHeightDips(workHeight));
}
