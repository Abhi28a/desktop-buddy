namespace DesktopBuddy.Core;

// Port of Poller in Sources/DesktopBuddy/Poller.swift.
// Checks every connected account on a timer, plus local reminders every 20 s.
// The Swift sources/OAuth are replaced by IServiceGateway (see Contracts.cs).
// Like AppState this is meant for the UI thread: Start() it from there and the awaits come back there.

public sealed class Poller : IDisposable
{
    private static readonly Lazy<Poller> SharedInstance = new(() => new Poller(AppState.Shared));
    public static Poller Shared => SharedInstance.Value;

    public static readonly TimeSpan ReminderInterval = TimeSpan.FromSeconds(20);

    private readonly AppState _state;
    private readonly IServiceGateway? _gateway;
    private CancellationTokenSource? _cts;
    private bool _running;

    /// With no gateway given, whatever Gateway.Current is when a check runs (the app installs the live one at startup).
    public Poller(AppState state, IServiceGateway? gateway = null)
    {
        _state = state;
        _gateway = gateway;
        _state.PollRequested += account => _ = PollAsync(account);   // "turn on" fixes ask for an immediate check
    }

    private IServiceGateway Service => _gateway ?? Gateway.Current;

    /// The time between account checks: the setting, but never under 30 seconds.
    public static TimeSpan IntervalFor(int pollSeconds) => TimeSpan.FromSeconds(Math.Max(30, pollSeconds));

    public void Start()
    {
        Stop();
        var cts = new CancellationTokenSource();
        _cts = cts;
        _ = Loop(IntervalFor(_state.Config.PollSeconds), () => PollAllAsync(cts.Token), cts.Token);
        _ = Loop(ReminderInterval, () => { CheckReminders(); return Task.CompletedTask; }, cts.Token);
        _ = PollAllAsync(cts.Token);
        CheckReminders();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose() => Stop();

    private static async Task Loop(TimeSpan every, Func<Task> work, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(every);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                try { await work(); }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { StatusLog.Write($"ERROR in timer: {e.Message}"); }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
    }

    public async Task PollAllAsync(CancellationToken ct = default)
    {
        if (_running || _state.IsPausedNow) return;
        _running = true;
        try
        {
            foreach (var account in _state.Config.Accounts.ToList())
            {
                if (ct.IsCancellationRequested) return;
                if (account.Enabled && Service.IsSignedIn(account) && !_state.NeedsSignIn(account.Id))
                {
                    await PollAsync(account.Clone(), ct);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Stopped mid-check.
        }
        finally { _running = false; }
    }

    /// Checks one account now (also used by "Check Now" in Settings).
    public async Task PollAsync(AccountConfig account, CancellationToken ct = default)
    {
        _state.SetChecking(account.Id, true);
        try
        {
            var lead = _state.Config.MeetingLeadMinutes;
            var jobs = new List<ItemSource>();
            if (account.WatchMail) jobs.Add(ItemSource.mail);
            if (account.WatchChat) jobs.Add(ItemSource.chat);
            if (account.WatchMeetings) jobs.Add(ItemSource.meeting);
            if (account.WatchTasks) jobs.Add(ItemSource.task);

            foreach (var source in jobs)
            {
                try
                {
                    var result = await Service.FetchAsync(account, source, lead, ct);
                    var muted = MailFilter.Apply(result, account);
                    if (result.Source == ItemSource.mail) _state.MutedMail.UnionWith(muted);
                    result.Items = ClientMatcher.Assign(result.Items, account.Id, _state.Config.Clients);
                    var added = 0;
                    _state.Batch(() =>
                    {
                        _state.Reconcile(result.Source, account.Id, result.LiveIDs);
                        _state.RefreshDetails(result.Items);
                        added = _state.IsQuietNow ? 0 : _state.Offer(result.Items);
                        _state.SetStatus(account.Id, source, new ServiceStatus { LastChecked = _state.Now(), Count = result.LiveIDs.Count });
                    });
                    StatusLog.Write($"{account.Label} {source}: fetched {result.Items.Count}, live {result.LiveIDs.Count}, new {added}");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception error)
                {
                    var fix = (error as ApiException)?.FixUrl;
                    _state.SetStatus(account.Id, source, new ServiceStatus { LastChecked = _state.Now(), Error = error.Message, FixUrl = fix });
                    StatusLog.Write($"{account.Label} {source}: ERROR {error.Message}");
                    if (error is AuthException { Kind: AuthErrorKind.NotSignedIn }) _state.RefreshSignedIn();
                    if (error is AuthException { Kind: AuthErrorKind.Expired })
                    {
                        // One expired sign-in breaks every service: flag the account once and stop.
                        _state.Batch(() =>
                        {
                            _state.SetExpired(account.Id, true);
                            foreach (var s in ItemSourceInfo.All) _state.SetStatus(account.Id, s, new ServiceStatus());
                        });
                        StatusLog.Write($"{account.Label}: sign-in expired");
                        return;
                    }
                }
            }
        }
        finally { _state.SetChecking(account.Id, false); }
    }

    public void CheckReminders(DateTimeOffset? at = null)
    {
        var now = at ?? _state.Now();
        _state.ExpireStaleChats(now);
        if (_state.Config.IsPausedAt(now) || _state.IsQuiet(now)) return;
        _state.WakeSnoozed(now);
        var local = now.ToLocalTime();
        var hour = local.Hour;
        var weekday = Reminder.MacWeekday(local.DayOfWeek);

        var reminders = _state.Config.Reminders;
        foreach (var r in reminders.ToList())
        {
            if (!r.Enabled) continue;
            if (!r.Weekdays.Contains(weekday) || hour < r.StartHour || hour >= r.EndHour) continue;
            if (r.LastFired is { } last && (now - last).TotalSeconds < r.EveryMinutes * 60.0) continue;
            r.LastFired = now;
            _state.SaveConfig();
            var id = $"reminder:{r.Id.ToString().ToUpperInvariant()}";
            _state.Batch(() =>
            {
                _state.Dismiss(id);          // replace an older, uncleared copy
                _state.Forget(id);
                _state.Offer(
                [
                    new BuddyItem
                    {
                        Id = id, Source = ItemSource.reminder,
                        Title = $"{r.Emoji} {r.Title}",
                        Detail = r.Note.Length == 0 ? $"Every {r.EveryMinutes} min" : r.Note,
                        Date = now,
                    },
                ]);
            });
        }
    }
}
