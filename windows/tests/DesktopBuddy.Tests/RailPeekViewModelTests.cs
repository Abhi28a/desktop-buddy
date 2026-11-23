using DesktopBuddy.Core;
using DesktopBuddy.Presentation.Peek;
using DesktopBuddy.Presentation.Rail;

namespace DesktopBuddy.Tests;

/// The Rail and the Peek view models: who is waiting, how close a meeting is, when each one shows or hides,
/// and how the Peek's time line counts down. Made-up names and example.com addresses only.
[Collection("DesktopBuddy.State")]
public class RailPeekViewModelTests : StorageTest
{
    private static BuddyItem Meeting(string id, string account, DateTimeOffset start) => new()
    {
        Id = $"meet:{account}:{id}", Source = ItemSource.meeting, AccountID = account, AccountLabel = account,
        Title = "Design review", From = "Sample Calendar", Detail = "Starts in 5 min", Date = start,
    };

    // MARK: - Rail rules (pure)

    [Fact]
    public void MeetingFractionIsTheShareOfLeadTimeLeft()
    {
        var now = Fx.At(3, 10);
        Assert.Equal(1.0, RailRules.MeetingFraction(now.AddMinutes(10), now, 10), 6);     // just appeared
        Assert.Equal(0.5, RailRules.MeetingFraction(now.AddMinutes(5), now, 10), 6);
        Assert.Equal(0.25, RailRules.MeetingFraction(now.AddMinutes(30), now, 120), 6);
        Assert.Equal(1.0, RailRules.MeetingFraction(now.AddMinutes(40), now, 10), 6);     // never above 1
        Assert.Equal(0.0, RailRules.MeetingFraction(now, now, 10), 6);                    // starting now
        Assert.Equal(0.0, RailRules.MeetingFraction(now.AddMinutes(-3), now, 10), 6);     // already started
        Assert.Equal(1.0, RailRules.MeetingFraction(now.AddMinutes(5), now, 0), 6);       // a lead of 0 counts as 1 minute
    }

    [Fact]
    public void TheRingNeverVanishesWhileTheMeetingIsStillListed()
    {
        var now = Fx.At(3, 10);
        Assert.Equal(RailRules.MinVisibleUrgency, RailRules.MeetingUrgency(now, now, 10));
        Assert.Equal(RailRules.MinVisibleUrgency, RailRules.MeetingUrgency(now.AddMinutes(-5), now, 10));
        Assert.Equal(0.5, RailRules.MeetingUrgency(now.AddMinutes(5), now, 10), 6);
    }

    [Fact]
    public void NextMeetingPicksTheOneThatStartsFirst()
    {
        var now = Fx.At(3, 10);
        var items = new[]
        {
            Fx.Mail("m1", "acc-g", "a@example.com", "Hello"),
            Meeting("later", "acc-g", now.AddMinutes(8)),
            Meeting("sooner", "acc-g", now.AddMinutes(3)),
        };
        var (urgency, minutes) = RailRules.NextMeeting(items, now, 10);
        Assert.Equal(0.3, urgency, 6);
        Assert.Equal(3, minutes);

        var (none, noMinutes) = RailRules.NextMeeting([items[0]], now, 10);
        Assert.True(double.IsNaN(none));
        Assert.Null(noMinutes);
    }

    [Theory]
    [InlineData(1.0, 40)]
    [InlineData(1.5, 60)]
    [InlineData(0.5, 32)]      // clamped to the smallest size
    [InlineData(3.0, 64)]      // clamped to the largest size
    public void TokenSizeFollowsTheSizeSetting(double scale, double expected) =>
        Assert.Equal(expected, RailRules.TokenSize(scale));

    [Fact]
    public void ThePillIs60AtNormalSizeAndKeepsEveryNotchInsideWithClearance()
    {
        Assert.Equal(60, RailRules.PillThickness(40, compact: true, vertical: true));
        Assert.Equal(60, RailRules.PillThickness(40, compact: true, vertical: false));
        Assert.Equal(80, RailRules.PillThickness(40, compact: false, vertical: true));      // names under the tokens
        foreach (var size in new[] { 32.0, 40, 48, 60, 64 })
        {
            // Vertical: token on the left, notch hangs out to the right of it.
            var across = RailRules.PillThickness(size, compact: true, vertical: true);
            Assert.True(across - (RailRules.CrossPad(size, true, true) + size + RailRules.NotchOverhangX(size)) >= RailRules.Clearance);
            // Horizontal: ring above (4 out), notch hangs out below.
            var height = RailRules.PillThickness(size, compact: true, vertical: false);
            Assert.True(height - (RailRules.CrossPad(size, true, false) + size + RailRules.NotchOverhangY(size)) >= RailRules.Clearance);
            // Names mode centres the token: the notch has the same room on the right as the token has on the left.
            var named = RailRules.PillThickness(size, compact: false, vertical: true);
            Assert.True((named - size) / 2 - RailRules.NotchOverhangX(size) >= RailRules.Clearance);
        }
        Assert.Equal(84, RailRules.PillThickness(60, compact: true, vertical: true));      // bigger tokens get a bigger pill
        Assert.Equal(82, RailRules.PillThickness(60, compact: true, vertical: false));
    }

    [Fact]
    public void TheLastTokenGetsMoreRoomAtTheEndForItsNotch()
    {
        foreach (var vertical in new[] { true, false })
        {
            var (start, end) = RailRules.EndPads(40, compact: true, vertical);
            Assert.True(end > start);
            Assert.True(end >= 22);
        }
    }

    [Theory]
    [InlineData(3, false, false, RailHiddenReason.None)]
    [InlineData(0, false, false, RailHiddenReason.Empty)]
    [InlineData(3, true, false, RailHiddenReason.Paused)]
    [InlineData(3, false, true, RailHiddenReason.ScreenTaken)]
    [InlineData(0, true, true, RailHiddenReason.Paused)]
    public void TheRailHidesWhenNothingWaitsOrPausedOrTheScreenIsTaken(int waiting, bool paused, bool screen, RailHiddenReason expected) =>
        Assert.Equal(expected, RailRules.HiddenReason(waiting, paused, screen));

    // MARK: - Rail view model

    [Fact]
    public void RailShowsOneTokenPerOwnerWithItsCount()
    {
        var s = Fx.FreshState();
        s.Offer([Fx.Mail("a", "acc-g", "x@example.com", "One"), Fx.Mail("b", "acc-g", "x@example.com", "Two"),
                 Fx.Mail("c", "acc-m", "x@example.com", "Three")]);
        using var rail = new RailViewModel(s);
        Assert.True(rail.IsVisible);
        Assert.Equal(["acc-g", "acc-m"], rail.Tokens.Select(t => t.OwnerId));
        Assert.Equal([2, 1], rail.Tokens.Select(t => t.Count));
        Assert.Equal(["Northwind", "Contoso"], rail.Tokens.Select(t => t.Name));
        Assert.Equal(3, rail.TotalWaiting);
        Assert.Equal("Northwind · 2 waiting", rail.Tokens[0].Tooltip);
        Assert.Equal("RailToken_acc-g", rail.Tokens[0].AutomationId);
        Assert.Equal("Northwind, 2 waiting", rail.Tokens[0].AutomationName);
    }

    [Fact]
    public void ANewTokenJoinsOnTheLeftOrTopAndTheOthersKeepTheirPlace()
    {
        var s = Fx.FreshState();
        s.Config.Clients = [new ClientConfig { Id = "client-1", Name = "RiskHawk" }];
        s.Offer([Fx.Mail("a", "acc-g", "x@example.com", "One"), Fx.Mail("b", "acc-m", "x@example.com", "Two")]);
        using var rail = new RailViewModel(s);
        Assert.Equal(["acc-g", "acc-m"], rail.Tokens.Select(t => t.OwnerId));

        s.Offer([Fx.Mail("c", "acc-g", "x@example.com", "Three") with { ClientID = "client-1" }]);
        Assert.Equal(["client-1", "acc-g", "acc-m"], rail.Tokens.Select(t => t.OwnerId));
        Assert.Equal([1, 1, 1], rail.Tokens.Select(t => t.Count));
    }

    [Fact]
    public void TheRailHidesWhenNothingIsWaitingAndComesBack()
    {
        var s = Fx.FreshState();
        using var rail = new RailViewModel(s);
        Assert.False(rail.IsVisible);
        Assert.Equal(RailHiddenReason.Empty, rail.HiddenReason);

        var item = Fx.Mail("a", "acc-g", "x@example.com", "One");
        s.Offer([item]);
        Assert.True(rail.IsVisible);
        s.Done(item.Id);
        Assert.False(rail.IsVisible);
    }

    [Fact]
    public void TheRailHidesWhilePaused()
    {
        var s = Fx.FreshState();
        var now = Fx.At(3, 14);
        s.Now = () => now;
        s.Offer([Fx.Mail("a", "acc-g", "x@example.com", "One")]);
        using var rail = new RailViewModel(s);
        Assert.True(rail.IsVisible);

        s.PauseFor(TimeSpan.FromHours(1));
        Assert.Equal(RailHiddenReason.Paused, rail.HiddenReason);
        now = now.AddMinutes(61);
        rail.Refresh();                                           // the pause ran out by itself: the app's timer calls Refresh
        Assert.True(rail.IsVisible);
    }

    [Fact]
    public void TheRailHidesWhileAFullScreenAppOrPresentationHasTheScreen()
    {
        var s = Fx.FreshState();
        s.Offer([Fx.Mail("a", "acc-g", "x@example.com", "One")]);
        var screenTaken = false;
        using var rail = new RailViewModel(s, () => screenTaken);
        Assert.True(rail.IsVisible);

        var raised = 0;
        rail.Updated += () => raised++;
        screenTaken = true;
        rail.Refresh();
        Assert.False(rail.IsVisible);
        Assert.Equal(RailHiddenReason.ScreenTaken, rail.HiddenReason);
        Assert.Equal(1, raised);

        rail.Refresh();                                           // nothing changed: nothing raised
        Assert.Equal(1, raised);

        screenTaken = false;
        rail.Refresh();
        Assert.True(rail.IsVisible);
        Assert.Equal(2, raised);
    }

    [Fact]
    public void AMeetingTokenCarriesTheUrgencyRingAndSaysSoInWords()
    {
        var s = Fx.FreshState();
        var now = Fx.At(3, 14);
        s.Now = () => now;
        s.Config.MeetingLeadMinutes = 10;
        s.Offer([Meeting("standup", "acc-g", now.AddMinutes(4)), Fx.Mail("a", "acc-m", "x@example.com", "Hello")]);
        using var rail = new RailViewModel(s);

        var meeting = rail.Tokens.Single(t => t.OwnerId == "acc-g");
        Assert.True(meeting.HasUrgency);
        Assert.Equal(0.4, meeting.Urgency, 6);
        Assert.Equal(4, meeting.MeetingMinutes);
        Assert.Equal("Northwind · 1 waiting · meeting in 4 min", meeting.Tooltip);
        Assert.Contains("meeting starts in 4 min", meeting.AutomationName);

        var mail = rail.Tokens.Single(t => t.OwnerId == "acc-m");
        Assert.False(mail.HasUrgency);
        Assert.Equal("Contoso · 1 waiting", mail.Tooltip);

        now = now.AddMinutes(2);                                  // two minutes later the ring is shorter
        rail.Refresh();
        Assert.Equal(0.2, rail.Tokens.Single(t => t.OwnerId == "acc-g").Urgency, 6);
    }

    [Fact]
    public void TheRailFollowsSizeNamesPlaceAndPrivacySettings()
    {
        var s = Fx.FreshState();
        s.Offer([Fx.Mail("a", "acc-g", "x@example.com", "One")]);
        using var rail = new RailViewModel(s);
        Assert.True(rail.Compact);
        Assert.Equal(40, rail.TokenSize);
        Assert.Equal(DockSpotInfo.Standard, rail.Spot);
        Assert.False(rail.Vertical);                              // the standard place is on the bottom edge
        Assert.True(rail.HiddenFromCapture);

        s.Config.RailCompact = false;
        s.Config.DockScale = 1.5;
        s.Config.DockSpot = DockSpot.rightTop;
        s.SaveConfig();
        Assert.False(rail.Compact);
        Assert.Equal(60, rail.TokenSize);
        Assert.Equal(DockSpot.rightTop, rail.Spot);
        Assert.True(rail.Vertical);

        s.ScreenshotsAllowedUntil = s.Now().AddSeconds(60);
        Assert.False(rail.HiddenFromCapture);                     // "Allow screenshots for a minute"
    }

    [Fact]
    public void DroppingTheRailSavesTheNearestPlace()
    {
        var s = Fx.FreshState();
        s.Offer([Fx.Mail("a", "acc-g", "x@example.com", "One")]);
        using var rail = new RailViewModel(s);
        rail.MoveTo(DockSpot.leftTop);
        Assert.Equal(DockSpot.leftTop, rail.Spot);
        Assert.Equal(DockSpot.leftTop, s.Config.DockSpot);
        Assert.Equal(DockSpot.leftTop, new AppState().Config.DockSpot);   // saved to config.json
    }

    // MARK: - Peek rules (pure)

    [Theory]
    [InlineData(true, false, false, false, true)]
    [InlineData(false, false, false, false, false)]     // the Peek is turned off
    [InlineData(true, true, false, false, false)]       // paused
    [InlineData(true, false, true, false, false)]       // quiet hours
    [InlineData(true, false, false, true, false)]       // Windows is busy
    public void ThePeekShowsOnlyWhenOnNotPausedNotQuietAndWindowsIsFree(bool on, bool paused, bool quiet, bool windows, bool expected) =>
        Assert.Equal(expected, PeekViewModel.ShouldShow(new AppConfig { PeekChoice = on }, paused, quiet, windows));

    // MARK: - Peek view model

    private static (AppState State, Func<DateTimeOffset> Clock, Action<TimeSpan> Advance) Setup()
    {
        var s = Fx.FreshState();
        var now = Fx.At(3, 14);
        s.Now = () => now;
        return (s, () => now, d => now += d);
    }

    [Fact]
    public void ThePeekShowsAnArrivalWithItsWords()
    {
        var (s, clock, _) = Setup();
        using var peek = new PeekViewModel(s, clock: clock);
        var shown = 0;
        peek.Shown += () => shown++;

        var item = Fx.Mail("a", "acc-g", "x@example.com", "Mortgage offer", "Please review the schedule", clock().AddMinutes(-2), "Jane Example") with { Url = "https://example.com/1" };
        s.Offer([item]);

        Assert.True(peek.IsShown);
        Assert.Equal(1, shown);
        var card = peek.Card!;
        Assert.Equal("Northwind · Gmail", card.Meta);
        Assert.Equal("2m", card.TimeText);
        Assert.Equal("Jane Example", card.Sender);
        Assert.Equal("Mortgage offer", card.Subject);
        Assert.Equal("Please review the schedule", card.Detail);
        Assert.Equal(0, card.MoreCount);
        Assert.True(card.CanOpen);
        Assert.Equal(1, peek.Progress);
    }

    [Theory]
    [InlineData("off")]
    [InlineData("paused")]
    [InlineData("quiet")]
    [InlineData("windows")]
    public void ThePeekHoldsBackAndTheItemWaitsInTheBoard(string reason)
    {
        var (s, clock, _) = Setup();
        var windowsBusy = reason == "windows";
        using var peek = new PeekViewModel(s, () => windowsBusy, clock);
        if (reason == "off") s.Config.PeekEnabled = false;
        if (reason == "paused") s.PauseFor(TimeSpan.FromHours(1));
        if (reason == "quiet") { s.Config.QuietStartHour = 12; s.Config.QuietEndHour = 18; }      // the fixed clock says 2 pm

        var shown = 0;
        peek.Shown += () => shown++;
        s.Offer([Fx.Mail("a", "acc-g", "x@example.com", "One")]);

        Assert.False(peek.IsShown);
        Assert.Equal(0, shown);
        Assert.Single(s.Items);                                   // it just waits in the Board
    }

    [Fact]
    public void TheNewestArrivalWinsAndCountsTheOthersWaiting()
    {
        var (s, clock, _) = Setup();
        using var peek = new PeekViewModel(s, clock: clock);
        var shown = 0;
        var changed = 0;
        peek.Shown += () => shown++;
        peek.ContentChanged += () => changed++;

        s.Offer([Fx.Mail("a", "acc-g", "x@example.com", "First", from: "Sender One")]);
        Assert.Equal("Sender One", peek.Card!.Sender);
        Assert.Equal(0, peek.MoreCount);

        s.Offer([Fx.Mail("b", "acc-m", "x@example.com", "Second", from: "Sender Two")]);
        Assert.Equal("Sender Two", peek.Card!.Sender);            // the newest is on show
        Assert.Equal(1, peek.MoreCount);
        Assert.Equal("1 more waiting", peek.Card.MoreText);

        s.Offer([Fx.Mail("c", "acc-g", "x@example.com", "Third", from: "Sender Three"), Fx.Mail("d", "acc-g", "x@example.com", "Fourth", from: "Sender Four", date: clock().AddMinutes(-9))]);
        Assert.Equal(3, peek.MoreCount);
        Assert.Equal("3 more waiting", peek.Card!.MoreText);
        Assert.Equal(1, shown);                                   // one window, content changed in place
        Assert.True(changed >= 2);
    }

    [Fact]
    public void AnotherArrivalStartsTheTimeLineAgain()
    {
        var (s, clock, advance) = Setup();
        using var peek = new PeekViewModel(s, clock: clock);
        s.Offer([Fx.Mail("a", "acc-g", "x@example.com", "First")]);
        advance(TimeSpan.FromSeconds(6));
        peek.Tick();
        Assert.Equal(0.4, peek.Progress, 3);
        s.Offer([Fx.Mail("b", "acc-g", "x@example.com", "Second")]);
        Assert.Equal(1, peek.Progress);
    }

    [Fact]
    public void TheTimeLineRunsDownOverPeekSecondsThenTheyPeekGoes()
    {
        var (s, clock, advance) = Setup();
        s.Config.PeekSeconds = 20;
        using var peek = new PeekViewModel(s, clock: clock);
        var hidden = 0;
        peek.Hidden += () => hidden++;
        s.Offer([Fx.Mail("a", "acc-g", "x@example.com", "One")]);
        Assert.Equal(20, peek.TotalSeconds);

        advance(TimeSpan.FromSeconds(5));
        peek.Tick();
        Assert.Equal(0.75, peek.Progress, 3);
        advance(TimeSpan.FromSeconds(10));
        peek.Tick();
        Assert.Equal(0.25, peek.Progress, 3);
        Assert.True(peek.IsShown);
        advance(TimeSpan.FromSeconds(5.01));
        peek.Tick();
        Assert.False(peek.IsShown);
        Assert.Equal(1, hidden);
        Assert.Single(s.Items);                                   // timing out is "ignore it": the item stays in the Board
    }

    [Fact]
    public void PointingAtTheCardPausesTheTimeLine()
    {
        var (s, clock, advance) = Setup();
        using var peek = new PeekViewModel(s, clock: clock);
        s.Offer([Fx.Mail("a", "acc-g", "x@example.com", "One")]);

        advance(TimeSpan.FromSeconds(4));
        peek.Tick();                                              // 6 of 10 seconds left
        peek.SetHovering(true);
        advance(TimeSpan.FromSeconds(60));
        peek.Tick();
        Assert.True(peek.IsShown);
        Assert.Equal(0.6, peek.Progress, 3);                      // paused
        Assert.True(peek.Card!.Hovering);

        peek.SetHovering(false);                                  // moved away: 6 s left is more than the 3 s minimum
        Assert.Equal(0.6, peek.Progress, 3);
        advance(TimeSpan.FromSeconds(2));
        peek.Tick();
        Assert.Equal(0.4, peek.Progress, 3);
    }

    [Fact]
    public void AfterPointingAtItThePeekStaysAtLeastThreeSeconds()
    {
        var (s, clock, advance) = Setup();
        using var peek = new PeekViewModel(s, clock: clock);
        s.Offer([Fx.Mail("a", "acc-g", "x@example.com", "One")]);

        advance(TimeSpan.FromSeconds(9.5));
        peek.Tick();                                              // half a second left
        peek.SetHovering(true);
        advance(TimeSpan.FromSeconds(30));
        peek.Tick();
        peek.SetHovering(false);
        Assert.Equal(PopupRules.MinimumAfterHover / 10, peek.Progress, 3);
        advance(TimeSpan.FromSeconds(2.9));
        peek.Tick();
        Assert.True(peek.IsShown);
        advance(TimeSpan.FromSeconds(0.2));
        peek.Tick();
        Assert.False(peek.IsShown);
    }

    [Fact]
    public void DoneClearsTheItemAndHidesThePeek()
    {
        var (s, clock, _) = Setup();
        using var peek = new PeekViewModel(s, clock: clock);
        var item = Fx.Mail("a", "acc-g", "x@example.com", "One");
        s.Offer([item]);
        peek.Done();
        Assert.False(peek.IsShown);
        Assert.Empty(s.Items);
        Assert.NotNull(s.Undo);                                   // the Board can undo it
    }

    [Fact]
    public void OpenOpensTheLinkAndClearsTheItem()
    {
        var (s, clock, _) = Setup();
        var opened = new List<string>();
        s.OpenUrlRequested += opened.Add;
        using var peek = new PeekViewModel(s, clock: clock);
        s.Offer([Fx.Mail("a", "acc-g", "x@example.com", "One") with { Url = "https://example.com/m/1" }]);
        peek.Open();
        Assert.Equal(["https://example.com/m/1"], opened);
        Assert.Empty(s.Items);
        Assert.False(peek.IsShown);
    }

    [Fact]
    public void LaterBringsItBackInAnHour()
    {
        var (s, clock, advance) = Setup();
        using var peek = new PeekViewModel(s, clock: clock);
        var item = Fx.Mail("a", "acc-g", "x@example.com", "One");
        s.Offer([item]);
        peek.Later();
        Assert.False(peek.IsShown);
        Assert.Empty(s.Items);
        var snoozed = Assert.Single(s.SnoozedItems);
        Assert.Equal(item.Id, snoozed.Item.Id);
        Assert.Equal(clock().AddMinutes(60), snoozed.Until);
        advance(TimeSpan.FromMinutes(59));
        s.WakeSnoozed();
        Assert.Empty(s.Items);
        advance(TimeSpan.FromMinutes(2));
        s.WakeSnoozed();                                          // an hour later it is back, and the Peek shows it again
        Assert.Single(s.Items);
        Assert.True(peek.IsShown);
    }

    [Fact]
    public void MoreWaitingOpensTheBoardAndHidesThePeek()
    {
        var (s, clock, _) = Setup();
        using var peek = new PeekViewModel(s, clock: clock);
        var board = 0;
        peek.BoardRequested += () => board++;
        s.Offer([Fx.Mail("a", "acc-g", "x@example.com", "One")]);
        s.Offer([Fx.Mail("b", "acc-g", "x@example.com", "Two")]);
        peek.ShowBoard();
        Assert.Equal(1, board);
        Assert.False(peek.IsShown);
        Assert.Equal(2, s.Items.Count);                           // nothing was cleared
    }

    [Fact]
    public void ThePeekGoesAwayWhenTheItemIsDealtWithElsewhereOrTheyPause()
    {
        var (s, clock, _) = Setup();
        using var peek = new PeekViewModel(s, clock: clock);
        var item = Fx.Mail("a", "acc-g", "x@example.com", "One");
        s.Offer([item]);
        s.Dismiss(item.Id);                                       // cleared in the Board
        Assert.False(peek.IsShown);

        s.Offer([Fx.Mail("b", "acc-g", "x@example.com", "Two")]);
        Assert.True(peek.IsShown);
        s.PauseFor(TimeSpan.FromHours(1));
        Assert.False(peek.IsShown);
    }

    [Fact]
    public void TheMoreCountFollowsTheBoardWhileThePeekIsOnScreen()
    {
        var (s, clock, _) = Setup();
        using var peek = new PeekViewModel(s, clock: clock);
        var older = Fx.Mail("a", "acc-g", "x@example.com", "Older", date: clock().AddMinutes(-5));
        s.Offer([older]);
        s.Offer([Fx.Mail("b", "acc-g", "x@example.com", "Newer", date: clock())]);
        Assert.Equal(1, peek.MoreCount);
        s.Dismiss(older.Id);
        Assert.Equal(0, peek.MoreCount);
        Assert.True(peek.IsShown);
    }

    [Fact]
    public void AnItemWithoutALinkHasNothingToOpen()
    {
        var (s, clock, _) = Setup();
        using var peek = new PeekViewModel(s, clock: clock);
        s.Offer([new BuddyItem { Id = "task-1", Source = ItemSource.task, AccountID = "acc-g", Title = "Send report", Detail = "Google Tasks", Date = clock() }]);
        Assert.False(peek.Card!.CanOpen);
    }

    [Fact]
    public void ThePeekCardHasNoSecondLineThatJustRepeatsTheFirst()
    {
        var (s, clock, _) = Setup();
        var task = new BuddyItem { Id = "t1", Source = ItemSource.task, AccountID = "acc-g", Title = "Send report", From = "Weekly", Detail = "Weekly", Date = clock() };
        var card = PeekCard.From(s, task, 0);
        Assert.Equal("Send report", card.Sender);
        Assert.Equal("Weekly", card.Subject);
        Assert.Null(card.Detail);                                 // the detail would only repeat the subject
    }

    // MARK: - Wording

    [Fact]
    public void ItemTextWordsEachKindOfItem()
    {
        var (s, clock, _) = Setup();
        var now = clock();
        var chat = Fx.Chat("c1", "acc-m", "Project room", "spaces/x", "Deploy is done");
        Assert.Equal("Sam Example", ItemText.Headline(chat));
        Assert.Equal("Project room", ItemText.Subhead(chat));
        Assert.Equal("Teams", ItemText.ServiceName(s, chat));
        Assert.Null(ItemText.Subhead(chat with { Title = "Direct message" }));

        var reminder = new BuddyItem { Id = "r", Source = ItemSource.reminder, Title = "Drink water", Date = now };
        Assert.Equal("Reminders", ItemText.Meta("Reminders", "Reminder", ItemSource.reminder));
        Assert.Equal("Northwind · Gmail", ItemText.Meta("Northwind", "Gmail", ItemSource.mail));
        Assert.Equal("Drink water", ItemText.Headline(reminder));
    }

    [Fact]
    public void WhenSaysHowLongAgoOrHowSoonForMeetings()
    {
        var now = Fx.At(3, 14);
        Assert.Equal("2m", ItemText.When(Fx.Mail("a", "acc-g", "x@example.com", "S", date: now.AddMinutes(-2)), now));
        Assert.Equal("now", ItemText.When(Fx.Mail("a", "acc-g", "x@example.com", "S", date: now), now));
        Assert.Equal("in 5 min", ItemText.When(Meeting("m", "acc-g", now.AddMinutes(5)), now));
        Assert.Equal("now", ItemText.When(Meeting("m", "acc-g", now.AddSeconds(20)), now));
        Assert.Equal("3 min ago", ItemText.When(Meeting("m", "acc-g", now.AddMinutes(-3)), now));
    }
}
