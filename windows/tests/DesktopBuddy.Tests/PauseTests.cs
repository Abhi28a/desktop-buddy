using DesktopBuddy.Core;

namespace DesktopBuddy.Tests;

/// Timed pause from the tray menu ("Pause for 1 hour", "Pause until tomorrow", "Resume").
[Collection("DesktopBuddy.State")]
public class PauseTests : StorageTest
{
    [Fact]
    public void PauseForAnHourEndsByItself()
    {
        var s = Fx.FreshState();
        var now = Fx.At(3, 14, 0);
        s.Now = () => now;
        s.PauseFor(TimeSpan.FromHours(1));
        Assert.True(s.IsPausedNow);
        now = now.AddMinutes(59);
        Assert.True(s.IsPausedNow);
        now = now.AddMinutes(2);
        Assert.False(s.IsPausedNow);
        Assert.False(s.Config.Paused);                       // the switch was never touched
    }

    [Fact]
    public void PauseUntilTomorrowMeansEightInTheMorning()
    {
        var s = Fx.FreshState();
        var now = Fx.At(3, 22, 30);                          // Tuesday 10:30 PM
        s.Now = () => now;
        s.PauseUntilTomorrow();
        var until = s.Config.PausedUntilChoice!.Value.ToLocalTime();
        Assert.Equal(now.ToLocalTime().Date.AddDays(1).AddHours(8), until.DateTime);
        now = Fx.At(4, 7, 59);
        Assert.True(s.IsPausedNow);
        now = Fx.At(4, 8, 0);
        Assert.False(s.IsPausedNow);
    }

    [Fact]
    public void ResumeClearsBothKindsOfPause()
    {
        var s = Fx.FreshState();
        s.Config.Paused = true;
        s.PauseFor(TimeSpan.FromHours(3));
        s.Resume();
        Assert.False(s.IsPausedNow);
        Assert.Null(s.Config.PausedUntilChoice);
        Assert.Null(new AppState().Config.PausedUntilChoice);   // saved
    }

    [Fact]
    public void TimedPauseStopsRemindersAndChecks()
    {
        var s = Fx.FreshState();
        var now = Fx.At(3, 10, 0);
        s.Now = () => now;
        s.Config.Reminders = [new Reminder { Title = "Stretch", EveryMinutes = 30, StartHour = 9, EndHour = 18, Weekdays = [1, 2, 3, 4, 5, 6, 7] }];
        s.PauseFor(TimeSpan.FromHours(1));
        var poller = new Poller(s, new OfflineGateway());
        poller.CheckReminders(now);
        Assert.Empty(s.Items);                               // paused: no reminder
        now = now.AddHours(2);
        poller.CheckReminders(now);
        Assert.Single(s.Items);                              // pause over: the reminder comes
    }
}
