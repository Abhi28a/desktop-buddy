using System.Text.Json.Serialization;

namespace DesktopBuddy.Core;

// Port of Sources/DesktopBuddy/AppState.swift (everything that isn't drawing UI), plus the
// AppState extensions in Clients.swift (client lookup, owner logos).
//
// THREADING: like Swift's @MainActor, AppState is single-threaded by design. Create it and call it
// from the UI thread only. Timers inside it use `await`, so they come back on the thread that
// started them (the UI thread, which has a SynchronizationContext).
//
// @Published becomes two things: `Changed` (raised once after any mutation, so the UI re-reads what
// it shows) and `ItemArrived` (a new item was offered, which drives the pop-up / lastArrival).

/// A snoozed item and when it comes back. Saved in snoozed.json.
public sealed record Snoozed
{
    [JsonPropertyName("item")] public BuddyItem Item { get; set; } = new();
    [JsonPropertyName("until")] public DateTimeOffset Until { get; set; }
}

/// What the last Done / Clear removed, kept for a few seconds so it can be put back.
public sealed class UndoClear(IReadOnlyList<BuddyItem> items)
{
    public Guid Id { get; } = Guid.NewGuid();
    public IReadOnlyList<BuddyItem> Items { get; } = items;
    public string Label => Items.Count == 1 ? "1 removed" : $"{Items.Count} removed";
}

public sealed partial class AppState
{
    private static readonly Lazy<AppState> SharedInstance = new(() => new AppState());

    /// The one the running app uses. Loads from Paths on first use.
    public static AppState Shared => SharedInstance.Value;

    /// Seconds Undo stays offered after Done / Clear.
    public const double UndoSeconds = 6;
    /// Seen list cap: past this many IDs, the oldest are forgotten down to SeenKeep.
    public const int SeenLimit = 3000;
    public const int SeenKeep = 2500;

    // MARK: - Events

    /// Raised once after any change to what the UI shows.
    public event Action? Changed;
    /// A new item was offered (or a snooze came back): show the pop-up for it.
    public event Action<BuddyItem>? ItemArrived;
    /// New items arrived and the sound is on: play this sound (a sound name, never "None").
    public event Action<string>? SoundRequested;
    /// A client fix wants this account checked now (the Poller listens to this).
    public event Action<AccountConfig>? PollRequested;
    /// A client fix wants the Sign In page for this account.
    public event Action<string>? SignInRequested;
    /// An account was removed: the network layer should forget its saved sign-in.
    public event Action<AccountConfig>? AccountRemoved;
    /// An item with a link was opened: the app should open the URL in the browser.
    public event Action<string>? OpenUrlRequested;

    /// The clock. Tests replace it to control time.
    public Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.Now;

    /// How long to wait before Undo expires. Tests replace it to control when that happens.
    public Func<TimeSpan, Task> Delay { get; set; } = ts => Task.Delay(ts);

    private int _batch;
    private bool _pending;

    /// Tells the UI something changed. Public so code that edits Config or the sets directly can announce it.
    public void NotifyChanged()
    {
        if (_batch > 0) { _pending = true; return; }
        Changed?.Invoke();
    }

    /// Runs several changes and raises Changed once at the end.
    public void Batch(Action changes)
    {
        _batch++;
        try { changes(); }
        finally
        {
            if (--_batch == 0 && _pending) { _pending = false; Changed?.Invoke(); }
        }
    }

    // MARK: - State

    private AppConfig _config;
    private List<BuddyItem> _items = [];
    private List<Snoozed> _snoozed = [];
    private UndoClear? _undo;
    private bool _expanded;
    private string? _filterOwner;
    private BuddyItem? _lastArrival;
    private bool _usingKeyboard;
    private DateTimeOffset? _screenshotsAllowedUntil;
    private List<string> _ownerSlots = [];

    /// IDs already shown (or dismissed) so they never pop up twice.
    private List<string> _seen = [];
    private HashSet<string> _seenSet = [];

    /// Settings. Assigning saves; if you edit a part of it in place, call SaveConfig().
    public AppConfig Config
    {
        get => _config;
        set { _config = value; SaveConfig(); }
    }

    /// Saves config.json and tells the UI. (Swift saved on every change with didSet.)
    public void SaveConfig()
    {
        JsonFile.Save(_config, Paths.Config);
        NotifyChanged();
    }

    public IReadOnlyList<BuddyItem> Items => _items;
    public IReadOnlyList<Snoozed> SnoozedItems => _snoozed;

    /// What the last Done / Clear removed, for a few seconds.
    public UndoClear? Undo => _undo;

    // The properties below are plain UI state: the WinUI layer binds to them. Setting one raises Changed.

    public bool Expanded { get => _expanded; set { if (_expanded != value) { _expanded = value; NotifyChanged(); } } }
    /// null = show every owner in the list.
    public string? FilterOwner { get => _filterOwner; set { if (_filterOwner != value) { _filterOwner = value; NotifyChanged(); } } }
    /// Drives the pop-up.
    public BuddyItem? LastArrival { get => _lastArrival; set { if (!Equals(_lastArrival, value)) { _lastArrival = value; NotifyChanged(); } } }
    /// True after Tab/arrow keys and false again on mouse use, so rows only look selected while you use the keyboard.
    public bool UsingKeyboard { get => _usingKeyboard; set { if (_usingKeyboard != value) { _usingKeyboard = value; NotifyChanged(); } } }
    /// "Allow Screenshots for 1 Minute".
    public DateTimeOffset? ScreenshotsAllowedUntil { get => _screenshotsAllowedUntil; set { if (_screenshotsAllowedUntil != value) { _screenshotsAllowedUntil = value; NotifyChanged(); } } }

    /// Per-service health, keyed "&lt;accountID&gt;.&lt;source&gt;". Use Status / SetStatus.
    public Dictionary<string, ServiceStatus> ServiceStatuses { get; set; } = [];
    /// Account IDs with a saved sign-in.
    public HashSet<string> SignedIn { get; set; } = [];
    /// Account IDs being checked right now.
    public HashSet<string> Checking { get; set; } = [];
    /// Account IDs whose sign-in the provider rejected.
    public HashSet<string> Expired { get; set; } = [];
    /// accountID to the address that actually signed in, when it isn't the account's email.
    public Dictionary<string, string> WrongAccount { get; set; } = [];
    /// Muted mail IDs, so Gmail doesn't download them again every check. Cleared when mute rules change.
    public HashSet<string> MutedMail { get; set; } = [];

    /// True when the account can't be checked until you sign in (never, expired, or as someone else).
    public bool NeedsSignIn(string id) => !SignedIn.Contains(id) || Expired.Contains(id) || WrongAccount.ContainsKey(id);

    // MARK: - Setup

    /// Loads from Paths. Tests call Paths.UseFolder(tempDir) first; the app uses AppState.Shared.
    public AppState()
    {
        _config = JsonFile.Load<AppConfig>(Paths.Config) ?? new AppConfig();
        var loaded = JsonFile.Load<List<BuddyItem>>(Paths.Inbox) ?? [];
        // v1 items only had a label; attach them to the matching account.
        for (var i = 0; i < loaded.Count; i++)
        {
            if (loaded[i].AccountID is null && loaded[i].AccountLabel is { } label)
            {
                loaded[i] = loaded[i] with { AccountID = _config.Accounts.FirstOrDefault(a => a.Label == label)?.Id };
            }
        }
        _items = loaded;
        _seen = JsonFile.Load<List<string>>(Paths.Seen) ?? [];
        _snoozed = JsonFile.Load<List<Snoozed>>(Paths.Snoozed) ?? [];
        _seenSet = _seen.ToHashSet();
        RefreshSignedIn();
        SeedDefaultClients();
        _ownerSlots = Slots([], _items.Select(i => i.Owner).ToHashSet(), DefaultOwnerOrder);
        ReassignClients();   // sort what's already on screen
    }

    /// First run of v3: add the team's known clients (logos are bundled). Rules can be edited in Settings.
    private void SeedDefaultClients()
    {
        if (_config.SeededClients == true) return;
        var existing = _config.Clients.Select(c => c.Name.ToLowerInvariant()).ToHashSet();
        foreach (var (name, tint) in new[] { ("Gatehouse", AccountTint.green), ("RiskHawk", AccountTint.orange) })
        {
            if (existing.Contains(name.ToLowerInvariant())) continue;
            _config.Clients.Add(new ClientConfig { Name = name, TintChoice = tint, Keywords = [name] });
        }
        _config.SeededClients = true;
        JsonFile.Save(_config, Paths.Config);
    }

    // MARK: - Items (every change goes through SetItems: saves inbox.json and keeps the logo order)

    private void SetItems(List<BuddyItem> items)
    {
        _items = items;
        JsonFile.Save(_items, Paths.Inbox);
        _ownerSlots = Slots(_ownerSlots, _items.Select(i => i.Owner).ToHashSet(), DefaultOwnerOrder);
    }

    private void SetSnoozed(List<Snoozed> snoozed)
    {
        _snoozed = snoozed;
        JsonFile.Save(_snoozed, Paths.Snoozed);
    }

    private void SaveSeen() => JsonFile.Save(_seen, Paths.Seen);

    // MARK: - Clients

    public ClientConfig? Client(string id) => _config.Clients.FirstOrDefault(c => c.Id == id)?.Clone();

    public void AddClient(ClientConfig c)
    {
        Batch(() =>
        {
            _config.Clients.Add(c.Clone());
            SaveConfig();
            ReassignClients();
        });
    }

    public void UpdateClient(ClientConfig c)
    {
        var i = _config.Clients.FindIndex(x => x.Id == c.Id);
        if (i < 0) return;
        Batch(() =>
        {
            _config.Clients[i] = c.Clone();
            SaveConfig();
            ReassignClients();
        });
    }

    public void RemoveClient(string id)
    {
        Batch(() =>
        {
            _config.Clients.RemoveAll(c => c.Id == id);
            SaveConfig();
            ReassignClients();
        });
    }

    /// Re-applies client rules to everything on screen (after rules change).
    public void ReassignClients()
    {
        Batch(() =>
        {
            var changed = false;
            var next = _items.ToList();
            for (var i = 0; i < next.Count; i++)
            {
                if (next[i].AccountID is not { } acc) continue;
                var tagged = ClientMatcher.Assign([next[i]], acc, _config.Clients)[0];
                if (tagged != next[i]) { next[i] = tagged; changed = true; }
            }
            if (changed) { SetItems(next); NotifyChanged(); }
            TidyAfterRemoval();
        });
    }

    // MARK: - Accounts

    /// A copy of the account, so you can edit it and pass it to Update (null if there's no such account).
    public AccountConfig? Account(string id) => _config.Accounts.FirstOrDefault(a => a.Id == id)?.Clone();

    public void RefreshSignedIn()
    {
        var gateway = Core.Gateway.Current;
        SignedIn = _config.Accounts.Where(gateway.IsSignedIn).Select(a => a.Id).ToHashSet();
        NotifyChanged();
    }

    public void AddAccount(AccountConfig a)
    {
        Batch(() =>
        {
            _config.Accounts.Add(a.Clone());
            SaveConfig();
            RefreshSignedIn();
        });
    }

    public void RemoveAccount(string id)
    {
        var a = Account(id);
        if (a is null) return;
        Batch(() =>
        {
            AccountRemoved?.Invoke(a);
            _config.Accounts.RemoveAll(x => x.Id == id);
            foreach (var c in _config.Clients)
            {
                c.Chats.RemoveAll(ch => ch.AccountID == id);
                c.AccountIDs.RemoveAll(x => x == id);
            }
            SaveConfig();
            SetItems(_items.Where(i => i.AccountID != id).ToList());
            ServiceStatuses = ServiceStatuses.Where(kv => !kv.Key.StartsWith(id + ".", StringComparison.Ordinal)).ToDictionary(kv => kv.Key, kv => kv.Value);
            SignedIn.Remove(id);
            TidyAfterRemoval();
            NotifyChanged();
        });
    }

    public void Update(AccountConfig a)
    {
        var i = _config.Accounts.FindIndex(x => x.Id == a.Id);
        if (i < 0) return;
        Batch(() =>
        {
            var old = _config.Accounts[i];
            _config.Accounts[i] = a.Clone();
            SaveConfig();
            if (!old.MutedSenders.SequenceEqual(a.MutedSenders) || !old.MutedKeywords.SequenceEqual(a.MutedKeywords)
                || !CopyExtensions.SameList(old.OnlyLabelsList, a.OnlyLabelsList)
                || !CopyExtensions.SameList(old.MutedChatsList, a.MutedChatsList))
            {
                // Rules changed: muted mail or chats may now be wanted (forget the cache), and newly muted mail leaves at once.
                var prefix = $"mail:{a.Id}:";
                MutedMail = MutedMail.Where(m => !m.StartsWith(prefix, StringComparison.Ordinal)).ToHashSet();
                var kept = _items.Where(it => !(it.AccountID == a.Id && MailFilter.IsMuted(it, a))).ToList();
                if (kept.Count != _items.Count) { SetItems(kept); TidyAfterRemoval(); NotifyChanged(); }
            }
            // Turning a service off clears what it was showing.
            var off = new List<ItemSource>();
            if (!a.WatchMail) off.Add(ItemSource.mail);
            if (!a.WatchChat) off.Add(ItemSource.chat);
            if (!a.WatchMeetings) off.Add(ItemSource.meeting);
            if (!a.WatchTasks) off.Add(ItemSource.task);
            if (!a.Enabled || off.Count > 0)
            {
                SetItems(_items.Where(it => !(it.AccountID == a.Id && (!a.Enabled || off.Contains(it.Source)))).ToList());
                TidyAfterRemoval();
                NotifyChanged();
            }
        });
    }

    public ServiceStatus Status(string accountID, ItemSource source) =>
        ServiceStatuses.TryGetValue($"{accountID}.{source}", out var s) ? s : new ServiceStatus();

    public void SetStatus(string accountID, ItemSource source, ServiceStatus s)
    {
        ServiceStatuses[$"{accountID}.{source}"] = s;
        NotifyChanged();
    }

    public int ErrorCount(string accountID) => ItemSourceInfo.All.Count(s => Status(accountID, s).Error != null);

    /// Marks an account as being checked (or done).
    public void SetChecking(string accountID, bool on)
    {
        if (on ? Checking.Add(accountID) : Checking.Remove(accountID)) NotifyChanged();
    }

    /// Marks an account's sign-in as rejected by the provider (or fine again).
    public void SetExpired(string accountID, bool expired)
    {
        if (expired ? Expired.Add(accountID) : Expired.Remove(accountID)) NotifyChanged();
    }

    // MARK: - Incoming

    /// Adds items that have never been seen before. Returns how many were new.
    public int Offer(IEnumerable<BuddyItem> incoming)
    {
        var fresh = incoming.Where(i => !_seenSet.Contains(i.Id)).ToList();
        if (fresh.Count == 0) return 0;
        BuddyItem lead = null!;
        Batch(() =>
        {
            foreach (var item in fresh) MarkSeen(item.Id);
            SaveSeen();
            var next = fresh.OrderByDescending(i => i.Date).Concat(_items).ToList();
            SetItems(next);
            lead = fresh.MaxBy(i => i.Date)!;
            _lastArrival = lead;
            if (_config.PlaySound && SoundRules.LeadSource(fresh) is { } source)
            {
                var sound = _config.Sound(source);
                if (sound != AppConfig.NoSound) SoundRequested?.Invoke(sound);
            }
            NotifyChanged();
        });
        ItemArrived?.Invoke(lead);
        return fresh.Count;
    }

    /// Removes items of a source/account that are no longer "live" upstream:
    /// for example the mail was read, the task completed, the meeting is over.
    public void Reconcile(ItemSource source, string accountID, ISet<string> liveIDs)
    {
        var kept = _items.Where(i => !(i.Source == source && i.AccountID == accountID && !liveIDs.Contains(i.Id))).ToList();
        if (kept.Count == _items.Count) return;
        Batch(() => { SetItems(kept); TidyAfterRemoval(); NotifyChanged(); });
    }

    /// Updates text on items already on screen (for example "Starts in 3 min").
    public void RefreshDetails(IEnumerable<BuddyItem> fresh)
    {
        List<BuddyItem>? next = null;
        foreach (var f in fresh)
        {
            var i = (next ?? _items).FindIndex(x => x.Id == f.Id);
            if (i < 0 || (next ?? _items)[i].Detail == f.Detail) continue;
            next ??= _items.ToList();
            next[i] = next[i] with { Detail = f.Detail };
        }
        if (next is null) return;
        SetItems(next);
        NotifyChanged();
    }

    /// Lets a repeating item (meeting, task, reminder) show again later.
    public void Forget(string id)
    {
        _seenSet.Remove(id);
        _seen.RemoveAll(x => x == id);
        SaveSeen();
    }

    /// Tests only: forget everything seen/snoozed so each scenario starts clean.
    public void ResetForTesting()
    {
        Batch(() =>
        {
            _seen = []; _seenSet = [];
            SaveSeen();
            SetSnoozed([]);
            SetItems([]);
            Expired = []; WrongAccount = []; ServiceStatuses = []; MutedMail = [];
            NotifyChanged();
        });
    }

    /// Removes sample items left over from a demo run.
    public void PurgeDemoItems()
    {
        Batch(() =>
        {
            SetItems(_items.Where(i => !i.Id.StartsWith("test:", StringComparison.Ordinal)).ToList());
            TidyAfterRemoval();
            NotifyChanged();
        });
    }

    /// Demo: three sample notifications so the design can be previewed (port of sendTestItems in App.swift).
    public void OfferDemoItems()
    {
        var now = Now();
        var stamp = now.ToUnixTimeSeconds();
        var accounts = _config.Accounts;
        var a1 = accounts.FirstOrDefault();
        var a2 = accounts.Skip(1).FirstOrDefault() ?? a1;
        Offer(
        [
            new BuddyItem { Id = $"test:mail:{stamp}", Source = ItemSource.mail, AccountID = a1?.Id, AccountLabel = a1?.Label,
                Title = "Sample: Q4 invoice approval", From = "Sample sender",
                Detail = "This is a sample notification so you can preview the design.", Date = now },
            new BuddyItem { Id = $"test:meet:{stamp}", Source = ItemSource.meeting, AccountID = a2?.Id, AccountLabel = a2?.Label,
                Title = "Sample: Daily stand-up", From = "Sample calendar", Detail = "Starts in 5 min", Date = now.AddSeconds(-1) },
            new BuddyItem { Id = $"test:reminder:{stamp}", Source = ItemSource.reminder,
                Title = "💧 Drink a glass of water", Detail = "Stay hydrated!", Date = now.AddSeconds(-2) },
        ]);
    }

    // MARK: - User actions

    public void Dismiss(string id)
    {
        Batch(() =>
        {
            SetItems(_items.Where(i => i.Id != id).ToList());
            TidyAfterRemoval();
            NotifyChanged();
        });
    }

    /// Clears everything, or one owner's items. Offers Undo.
    public void ClearAll(string? owner = null)
    {
        Batch(() =>
        {
            Remember(_items.Where(i => owner is null || i.Owner == owner).ToList());
            SetItems(owner is null ? [] : _items.Where(i => i.Owner != owner).ToList());
            TidyAfterRemoval();
            NotifyChanged();
        });
    }

    /// Done on one or more notifications (from you, not from syncing): removed, with Undo for a few seconds.
    public void Done(IEnumerable<string> ids)
    {
        var set = ids.ToHashSet();
        Batch(() =>
        {
            Remember(_items.Where(i => set.Contains(i.Id)).ToList());
            SetItems(_items.Where(i => !set.Contains(i.Id)).ToList());
            TidyAfterRemoval();
            NotifyChanged();
        });
    }

    public void Done(string id) => Done([id]);

    private void Remember(List<BuddyItem> removed)
    {
        if (removed.Count == 0) return;
        var u = new UndoClear(removed);
        _undo = u;
        NotifyChanged();
        _ = ExpireUndoAsync(u);
    }

    private async Task ExpireUndoAsync(UndoClear u)
    {
        await Delay(TimeSpan.FromSeconds(UndoSeconds));
        if (_undo?.Id != u.Id) return;
        _undo = null;
        if (_items.Count == 0) _expanded = false;
        NotifyChanged();
    }

    /// Puts back what the last Done / Clear removed (skipping anything that has come back on its own).
    public void UndoLast()
    {
        if (_undo is not { } u) return;
        Batch(() =>
        {
            _undo = null;
            var present = _items.Select(i => i.Id).ToHashSet();
            SetItems(_items.Concat(u.Items.Where(i => !present.Contains(i.Id))).OrderByDescending(i => i.Date).ToList());
            NotifyChanged();
        });
    }

    /// Opens the item's link (through OpenUrlRequested) and removes the item.
    public void Open(BuddyItem item)
    {
        Batch(() =>
        {
            if (item.Url is { } url) OpenUrlRequested?.Invoke(url);
            Dismiss(item.Id);
        });
    }

    /// Snoozes survive quitting the app: they're saved and brought back by WakeSnoozed.
    public void Snooze(BuddyItem item, int minutes = 10, DateTimeOffset? now = null)
    {
        var at = now ?? Now();
        Batch(() =>
        {
            Dismiss(item.Id);
            var next = _snoozed.Where(s => s.Item.Id != item.Id).ToList();
            next.Add(new Snoozed { Item = item, Until = at.AddMinutes(minutes) });
            SetSnoozed(next);
            NotifyChanged();
        });
    }

    /// Called on the reminder tick: returns due snoozes to the screen.
    public void WakeSnoozed(DateTimeOffset? now = null)
    {
        var at = now ?? Now();
        var due = _snoozed.Where(s => s.Until <= at).ToList();
        if (due.Count == 0) return;
        var arrived = new List<BuddyItem>();
        Batch(() =>
        {
            SetSnoozed(_snoozed.Where(s => s.Until > at).ToList());
            foreach (var s in due)
            {
                var again = s.Item with { Date = at };
                var next = _items.Where(i => i.Id != again.Id).ToList();
                next.Insert(0, again);
                SetItems(next);
                _lastArrival = again;
                arrived.Add(again);
            }
            NotifyChanged();
        });
        foreach (var item in arrived) ItemArrived?.Invoke(item);
    }

    /// Google Chat can't tell us what you've read, so chat notifications clear themselves after a day.
    public void ExpireStaleChats(DateTimeOffset? now = null, double afterHours = 24)
    {
        var at = now ?? Now();
        var kept = _items.Where(i => !(i.Source == ItemSource.chat && (at - i.Date).TotalSeconds > afterHours * 3600)).ToList();
        if (kept.Count == _items.Count) return;
        Batch(() => { SetItems(kept); TidyAfterRemoval(); NotifyChanged(); });
    }

    // MARK: - Owners (one logo per account or client, plus your reminders)

    /// Owners that currently have something to show, in a stable left-to-right order.
    public List<string> ActiveOwners => Slots(_ownerSlots, _items.Select(i => i.Owner).ToHashSet(), DefaultOwnerOrder);

    private List<string> DefaultOwnerOrder =>
        _config.Clients.Select(c => c.Id).Concat(_config.Accounts.Select(a => a.Id)).Append(BuddyItem.ReminderOwner).ToList();

    /// Logos keep their place: the logos sit in a corner, so a newcomer joins on the left (where
    /// there's room) instead of pushing the others aside. Owners that leave just drop out.
    public static List<string> Slots(IReadOnlyList<string> previous, ISet<string> present, IReadOnlyList<string> defaultOrder)
    {
        var kept = previous.Where(present.Contains).ToList();
        var newcomers = defaultOrder.Where(o => present.Contains(o) && !kept.Contains(o));
        return newcomers.Concat(kept).ToList();
    }

    public string OwnerName(string owner)
    {
        if (owner == BuddyItem.ReminderOwner) return "Reminders";
        return Account(owner)?.Label ?? Client(owner)?.Name ?? "Account";
    }

    public AccountTint OwnerTint(string owner)
    {
        if (owner == BuddyItem.ReminderOwner) return AccountTint.teal;
        return Account(owner)?.Tint ?? Client(owner)?.Tint ?? AccountTint.blue;
    }

    /// The logo an owner uses, as data (the app loads the image): a file, a bundled key, or null for a
    /// monogram. Reminders use the picked reminder logo, or null (the app draws a bell).
    public LogoRef? OwnerLogo(string owner)
    {
        if (_config.Accounts.FirstOrDefault(a => a.Id == owner) is { } a) return Logos.Resolve(a.LogoPath, a.Label, a.Email);
        if (_config.Clients.FirstOrDefault(c => c.Id == owner) is { } c) return Logos.Resolve(c.LogoPath, c.Name);
        return _config.ReminderLogoPath is { } p ? new LogoRef(p, null) : null;
    }

    public void ToggleList(string owner)
    {
        Batch(() =>
        {
            if (_expanded && _filterOwner == owner) { Expanded = false; return; }
            FilterOwner = owner;
            Expanded = true;
        });
    }

    // MARK: - Pause

    /// Paused by the switch, or by "Pause for 1 hour" / "Pause until tomorrow" that hasn't run out yet.
    public bool IsPausedNow => _config.IsPausedAt(Now());

    /// "Pause for 1 hour" (or any span).
    public void PauseFor(TimeSpan span)
    {
        _config.PausedUntilChoice = Now() + span;
        SaveConfig();
    }

    /// "Pause until tomorrow": until 8:00 tomorrow morning, local time.
    public void PauseUntilTomorrow()
    {
        var local = Now().ToLocalTime();
        var tomorrow = local.Date.AddDays(1).AddHours(8);
        _config.PausedUntilChoice = new DateTimeOffset(tomorrow, local.Offset);
        SaveConfig();
    }

    /// Ends any pause, timed or switched on.
    public void Resume()
    {
        _config.Paused = false;
        _config.PausedUntilChoice = null;
        SaveConfig();
    }

    // MARK: - Quiet hours

    public bool IsQuietNow => IsQuiet(Now());

    public bool IsQuiet(DateTimeOffset date)
    {
        if (_config.QuietStartHour is not { } s || _config.QuietEndHour is not { } e || s == e) return false;
        var h = date.ToLocalTime().Hour;
        return s < e ? (h >= s && h < e) : (h >= s || h < e);
    }

    // MARK: - Private

    private void MarkSeen(string id)
    {
        if (!_seenSet.Add(id)) return;
        _seen.Add(id);
        if (_seen.Count > SeenLimit)
        {
            var dropCount = _seen.Count - SeenKeep;
            foreach (var d in _seen.Take(dropCount)) _seenSet.Remove(d);
            _seen.RemoveRange(0, dropCount);
        }
    }

    private void TidyAfterRemoval()
    {
        if (_lastArrival is { } last && !_items.Any(i => i.Id == last.Id)) _lastArrival = null;
        if (_items.Count == 0 && _undo is null) _expanded = false;      // stays open briefly so Undo is reachable
        if (_filterOwner is { } f && !_items.Any(i => i.Owner == f)) _filterOwner = null;
        NotifyChanged();
    }
}
