using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using DesktopBuddy.Core;

namespace DesktopBuddy.Presentation.Board;

/// "In 10 minutes", and how many minutes that is from now.
public sealed record LaterOption(string Label, int Minutes);

/// The Board: everything that's waiting, grouped by owner, with triage actions and keyboard
/// commands. Wraps AppState (so it can be unit-tested without WinUI) and keeps itself current by
/// listening to AppState.Changed. Call Dispose when the Board closes.
public sealed partial class BoardViewModel : ViewModelBase, IDisposable
{
    private readonly AppState _state;
    private readonly Action _onChanged;
    private string? _filter;
    private string _signature = "";
    private List<BoardRow> _rows = [];
    private BoardRow? _selected;
    private bool _disposed;

    public BoardViewModel(AppState state, IDispatcher? dispatcher = null, string? owner = null) : base(dispatcher)
    {
        _state = state;
        _filter = owner;
        _onChanged = () => Post(Refresh);
        _state.Changed += _onChanged;
        Refresh();
    }

    public AppState State => _state;

    /// Culture for times ("3:00 PM"). Tests set it to be sure of the text.
    public CultureInfo Culture { get; set; } = CultureInfo.CurrentCulture;

    // MARK: - What the view shows

    /// Owners in AppState.ActiveOwners order, each with its rows.
    [ObservableProperty] public partial IReadOnlyList<BoardGroup> Groups { get; private set; } = [];

    /// Every row in list order (the order the arrow keys walk).
    public IReadOnlyList<BoardRow> Rows => _rows;

    [ObservableProperty] public partial int TotalCount { get; private set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRows))]
    public partial bool IsEmpty { get; private set; } = true;
    public bool HasRows => !IsEmpty;
    [ObservableProperty] public partial string EmptyDetail { get; private set; } = "";

    /// The one line at the top, or null.
    [ObservableProperty] public partial BoardProblem? Problem { get; private set; }

    [ObservableProperty] public partial bool HasUndo { get; private set; }
    [ObservableProperty] public partial string UndoLabel { get; private set; } = "";

    // Filtered to one owner (opened from a Rail token).
    [ObservableProperty] public partial bool IsFiltered { get; private set; }
    [ObservableProperty] public partial string Title { get; private set; } = "Waiting";
    [ObservableProperty] public partial string FilterName { get; private set; } = "";
    [ObservableProperty] public partial LogoRef? FilterLogo { get; private set; }
    [ObservableProperty] public partial AccountTint FilterTint { get; private set; }
    [ObservableProperty] public partial bool FilterIsReminders { get; private set; }

    public string? FilterOwner => _filter;

    public BoardRow? SelectedRow
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value)) return;
            if (value is not null && !_rows.Contains(value)) return;       // a row from a list that was just replaced
            if (_selected is not null) _selected.IsSelected = false;
            _selected = value;
            if (value is not null) value.IsSelected = true;
            OnPropertyChanged();
        }
    }

    /// Raised for "Settings" and for a problem's fix: the page to open ("account:<id>", "home") or null.
    public event Action<string?>? SettingsRequested;

    // MARK: - Refresh

    /// Re-reads AppState. Cheap when nothing changed: it then only updates the times ("2m" to "3m").
    public void Refresh()
    {
        if (_disposed) return;
        var now = _state.Now();

        var owners = _state.ActiveOwners;
        if (_filter is not null) owners = owners.Where(o => o == _filter).ToList();
        var byOwner = _state.Items.GroupBy(i => i.Owner).ToDictionary(g => g.Key, g => g.ToList());

        var groups = new List<BoardGroup>();
        var rows = new List<BoardRow>();
        foreach (var owner in owners)
        {
            if (!byOwner.TryGetValue(owner, out var items)) continue;
            var group = new BoardGroup(owner, _state.OwnerName(owner), _state.OwnerLogo(owner), _state.OwnerTint(owner),
                owner == BuddyItem.ReminderOwner, isFirst: groups.Count == 0, showHeader: _filter is null, index: groups.Count);
            foreach (var item in items)
            {
                var (text, urgent) = FormatTime(item, now);
                var row = new BoardRow(item, rows.Count, text, urgent);
                group.Add(row);
                rows.Add(row);
            }
            groups.Add(group);
        }

        // Header, problem line, undo bar, empty state.
        IsFiltered = _filter is not null;
        if (_filter is { } f)
        {
            FilterName = _state.OwnerName(f);
            FilterLogo = _state.OwnerLogo(f);
            FilterTint = _state.OwnerTint(f);
            FilterIsReminders = f == BuddyItem.ReminderOwner;
        }
        Title = IsFiltered ? FilterName : "Waiting";
        Problem = ComputeProblem(now);
        HasUndo = _state.Undo is not null;
        UndoLabel = _state.Undo?.Label ?? "";
        EmptyDetail = ComputeEmptyDetail(now);

        // Only swap the list when what it shows changed (so scrolling and hover survive a poll).
        var signature = Signature(groups);
        if (signature == _signature)
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                var (text, urgent) = FormatTime(rows[i].Item, now);
                _rows[i].UpdateTime(text, urgent);
            }
            return;
        }
        _signature = signature;

        var previousId = _selected?.Id;
        var previousIndex = _selected?.Index ?? 0;
        if (_selected is not null) _selected.IsSelected = false;
        _selected = null;
        _rows = rows;
        TotalCount = rows.Count;
        IsEmpty = rows.Count == 0;
        Groups = groups;
        var next = previousId is null ? null : rows.FirstOrDefault(r => r.Id == previousId);
        next ??= rows.Count == 0 ? null : rows[Math.Min(previousIndex, rows.Count - 1)];
        if (next is not null) next.IsSelected = true;
        _selected = next;
        OnPropertyChanged(nameof(SelectedRow));
        OnPropertyChanged(nameof(Rows));
    }

    private static string Signature(List<BoardGroup> groups)
    {
        var sb = new StringBuilder();
        foreach (var g in groups)
        {
            sb.Append('#').Append(g.OwnerId).Append('|').Append(g.Name).Append('|').Append(g.ShowHeader).Append('|')
              .Append(g.Logo?.Path).Append('|').Append(g.Logo?.BundledKey).Append('|').Append(g.Tint);
            foreach (var r in g) sb.Append('\n').Append(r.Id).Append('|').Append(r.Sender).Append('|').Append(r.Secondary);
        }
        return sb.ToString();
    }

    // MARK: - Times

    /// "now", "4m", "2h", "3d"; a meeting says "in 5 min" and counts as urgent.
    public static (string Text, bool Urgent) FormatTime(BuddyItem item, DateTimeOffset now)
    {
        if (item.Source != ItemSource.meeting) return (TokenRules.ShortAgo(item.Date, now), false);
        var until = item.Date - now;
        if (until > TimeSpan.Zero) return ($"in {Math.Max(1, (int)Math.Ceiling(until.TotalMinutes))} min", true);
        var ago = (int)Math.Floor(-until.TotalMinutes);
        return (ago < 1 ? "now" : $"started {ago}m ago", true);
    }

    private static string AgoWords(DateTimeOffset date, DateTimeOffset now)
    {
        var s = (long)Math.Max(0, (now - date).TotalSeconds);
        if (s < 60) return "just now";
        if (s < 3600) return $"{s / 60} min ago";
        if (s < 86_400) return $"{s / 3600} h ago";
        return $"{s / 86_400} d ago";
    }

    private string ComputeEmptyDetail(DateTimeOffset now)
    {
        var last = _state.ServiceStatuses.Values.Where(s => s.LastChecked is not null).Select(s => s.LastChecked!.Value).DefaultIfEmpty().Max();
        if (last != default) return "Last checked " + AgoWords(last, now);
        return _state.Config.Accounts.Count == 0 ? "Add an account in Settings to get started." : "Waiting for the first check.";
    }

    // MARK: - The problem line

    /// Sign-in problems and broken services come first (one line, or a count when there are several);
    /// when nothing is wrong and notifications are paused, the line says so and offers Resume.
    internal BoardProblem? ComputeProblem(DateTimeOffset now)
    {
        var found = new List<(string Title, string Action, string Page)>();
        foreach (var a in _state.Config.Accounts.Where(a => a.Enabled))
        {
            var label = string.IsNullOrWhiteSpace(a.Label) ? a.Email : a.Label;
            var page = "account:" + a.Id;
            if (_state.NeedsSignIn(a.Id))
            {
                if (_state.Expired.Contains(a.Id)) found.Add(($"{label}: sign-in expired", "Sign in again", page));
                else if (_state.WrongAccount.ContainsKey(a.Id)) found.Add(($"{label}: signed in as someone else", "Sign in again", page));
                else found.Add(($"{label}: not signed in", "Sign in", page));
                continue;       // a broken sign-in explains every service error below it
            }
            foreach (var source in new[] { ItemSource.mail, ItemSource.chat, ItemSource.meeting, ItemSource.task })
            {
                var watched = source switch
                {
                    ItemSource.mail => a.WatchMail,
                    ItemSource.chat => a.WatchChat,
                    ItemSource.meeting => a.WatchMeetings,
                    _ => a.WatchTasks,
                };
                if (watched && _state.Status(a.Id, source).Error is not null)
                    found.Add(($"{label}: {ServiceName(a.Provider, source)} isn't working", "Fix", page));
            }
        }

        if (found.Count == 1)
        {
            var (title, action, page) = found[0];
            var kind = action == "Fix" ? BoardProblemKind.Service : BoardProblemKind.SignIn;
            return new BoardProblem(kind, BoardSeverity.Caution, title, action, page);
        }
        if (found.Count > 1)
            return new BoardProblem(BoardProblemKind.Several, BoardSeverity.Caution, $"{found.Count} things need attention", "Review", "home", found.Count);

        if (_state.IsPausedNow) return new BoardProblem(BoardProblemKind.Paused, BoardSeverity.Neutral, PausedText(now), "Resume", null);
        return null;
    }

    private static string ServiceName(Provider provider, ItemSource source) => source switch
    {
        ItemSource.mail => provider == Provider.google ? "Gmail" : "Outlook mail",
        ItemSource.chat => provider == Provider.google ? "Google Chat" : "Teams",
        ItemSource.meeting => "Calendar",
        _ => provider == Provider.google ? "Google Tasks" : "Microsoft To Do",
    };

    private string PausedText(DateTimeOffset now)
    {
        var until = _state.Config.PausedUntilChoice;
        if (_state.Config.Paused || until is null) return "Paused";
        var local = until.Value.ToLocalTime();
        var time = Clock(local);
        var days = (local.Date - now.ToLocalTime().Date).Days;
        return days <= 0 ? $"Paused until {time}" : days == 1 ? $"Paused until tomorrow, {time}" : $"Paused until {local.ToString("ddd", Culture)}, {time}";
    }

    /// "3:00 PM": the culture's short time, without a leading zero on 12-hour clocks.
    private string Clock(DateTime time) => time.ToString(Culture.DateTimeFormat.ShortTimePattern.Replace("hh", "h"), Culture);
    private string Clock(DateTimeOffset time) => Clock(time.DateTime);

    // MARK: - Actions

    public void RunProblemAction()
    {
        if (Problem is not { } p) return;
        if (p.Kind == BoardProblemKind.Paused) _state.Resume();
        else SettingsRequested?.Invoke(p.SettingsPage);
    }

    public void OpenSettings() => SettingsRequested?.Invoke(null);

    public void Open(BoardRow? row) { if (row is not null) _state.Open(row.Item); }
    public void Done(BoardRow? row) { if (row is not null) _state.Done(row.Id); }
    public void Later(BoardRow? row, int minutes) { if (row is not null) _state.Snooze(row.Item, minutes); }

    public void OpenSelected() => Open(_selected);
    public void DoneSelected() => Done(_selected);
    public void LaterSelected(int minutes) => Later(_selected, minutes);

    public void Undo() => _state.UndoLast();

    /// "Clear all": everything shown (just that owner when filtered). Undo-able.
    public void ClearAll() => _state.ClearAll(_filter);
    public void ClearGroup(BoardGroup? group) { if (group is not null) _state.ClearAll(group.OwnerId); }

    /// Up (-1) or down (+1) through every row, group to group. Stops at the ends.
    public void MoveSelection(int delta)
    {
        if (_rows.Count == 0) return;
        var i = _selected is null ? (delta > 0 ? -1 : _rows.Count) : _selected.Index;
        SelectedRow = _rows[Math.Clamp(i + delta, 0, _rows.Count - 1)];
    }

    /// Opened from a Rail token: show only that owner. Null shows everything.
    public void SetFilter(string? owner)
    {
        if (_filter == owner) return;
        _filter = owner;
        _signature = "";
        Refresh();
    }

    public void ShowEverything() => SetFilter(null);

    /// 10 minutes, 1 hour, tomorrow 9 am (the same three as the Mac), as minutes from now.
    public IReadOnlyList<LaterOption> LaterOptions()
    {
        var now = _state.Now();
        var tomorrow = now.ToLocalTime().Date.AddDays(1).AddHours(9);
        var at = new DateTimeOffset(tomorrow, TimeZoneInfo.Local.GetUtcOffset(tomorrow));
        return
        [
            new("In 10 minutes", 10),
            new("In 1 hour", 60),
            new($"Tomorrow at {Clock(tomorrow)}", Math.Max(1, (int)Math.Ceiling((at - now).TotalMinutes))),
        ];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _state.Changed -= _onChanged;
    }
}
