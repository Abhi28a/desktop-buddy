using DesktopBuddy.Core;

namespace DesktopBuddy.Presentation.Peek;

/// The Peek: one new item, shown for a few seconds. Deal with it (Open, Done, Later) or ignore it.
/// The view model decides WHEN it shows, WHAT it says and how much time is left; the window draws it.
/// Single-threaded like AppState (UI thread).
public sealed class PeekViewModel : ViewModelBase, IDisposable
{
    /// "Later" snoozes for an hour.
    public const int LaterMinutes = 60;

    private readonly AppState _state;
    private readonly Func<bool> _windowsHold;
    private readonly Func<DateTimeOffset> _clock;
    private BuddyItem? _item;
    private int _more;
    private double _remaining;
    private DateTimeOffset _lastTick;
    private bool _hovering;
    private bool _shown;

    /// `windowsHold` says whether Windows is busy (Do not disturb, presenting, full screen) and the
    /// person asked us to respect that: the app passes `() => Attention.ShouldHoldPeek(config)`.
    /// `clock` is the time source for the time line (tests pass their own).
    public PeekViewModel(AppState state, Func<bool>? windowsHold = null, Func<DateTimeOffset>? clock = null,
        IDispatcher? dispatcher = null) : base(dispatcher)
    {
        _state = state;
        _windowsHold = windowsHold ?? (() => false);
        _clock = clock ?? (() => DateTimeOffset.Now);
        _state.ItemArrived += OnArrived;
        _state.Changed += OnChanged;
    }

    /// The Peek appeared (the window should open).
    public event Action? Shown;
    /// The Peek went away (the window should fade out and close).
    public event Action? Hidden;
    /// The item, the "N more", the time or the hover look changed while it is on screen.
    public event Action? ContentChanged;
    /// "N more waiting" was pressed: open the Board.
    public event Action? BoardRequested;

    // MARK: - What it says

    public bool IsShown => _shown;
    public BuddyItem? Item => _item;
    /// Other items still waiting on the Board.
    public int MoreCount => _more;
    public bool IsHovering => _hovering;
    /// 1 = the whole time is left, 0 = it is about to go.
    public double Progress => _shown ? Math.Clamp(_remaining / TotalSeconds, 0, 1) : 0;
    public double TotalSeconds => _state.Config.PeekSeconds;

    /// What the card shows right now (null while the Peek is away).
    public PeekCard? Card => _item is null ? null : PeekCard.From(_state, _item, _more, Progress, _hovering);

    // MARK: - When it shows

    /// The rule: the Peek shows only when it is on, not paused, not in quiet hours and Windows isn't busy.
    /// Otherwise the item simply waits in the Board.
    public static bool ShouldShow(AppConfig config, bool pausedNow, bool quietNow, bool windowsHold) =>
        config.PeekEnabled && !pausedNow && !quietNow && !windowsHold;

    private bool CanShowNow() =>
        ShouldShow(_state.Config, _state.IsPausedNow, _state.IsQuietNow, _windowsHold());

    private void OnArrived(BuddyItem item)
    {
        if (!CanShowNow()) return;
        // The newest wins: a second arrival replaces the first and the time starts again.
        _item = item;
        _remaining = TotalSeconds;
        _lastTick = _clock();
        _more = OthersWaiting();
        if (_shown)
        {
            ContentChanged?.Invoke();
            return;
        }
        _shown = true;
        _hovering = false;
        OnPropertyChanged(string.Empty);
        Shown?.Invoke();
    }

    private int OthersWaiting() => _item is null ? 0 : _state.Items.Count(i => i.Id != _item.Id);

    /// AppState changed: the item may have been dealt with elsewhere, the person may have paused, or the count moved.
    private void OnChanged()
    {
        if (!_shown) return;
        if (_item is null || _state.Items.All(i => i.Id != _item.Id)
            || _state.IsPausedNow || !_state.Config.PeekEnabled)
        {
            Hide();
            return;
        }
        var more = OthersWaiting();
        if (more != _more)
        {
            _more = more;
            ContentChanged?.Invoke();
        }
    }

    // MARK: - Time

    /// Call every 50 to 100 ms while the Peek is on screen. Counts the time down unless the pointer is on it,
    /// and hides the Peek when the time is up. (The view reads `Progress` after each call.)
    public void Tick()
    {
        if (!_shown) return;
        var now = _clock();
        var elapsed = Math.Max(0, (now - _lastTick).TotalSeconds);
        _lastTick = now;
        if (!_hovering) _remaining -= elapsed;
        if (_remaining <= 0)
        {
            Hide();
        }
    }

    /// The pointer is on the card (pauses the time line) or left it again. After pointing at it the
    /// Peek stays at least PopupRules.MinimumAfterHover seconds, so there is time to read.
    public void SetHovering(bool hovering)
    {
        if (!_shown || _hovering == hovering) return;
        _lastTick = _clock();
        _hovering = hovering;
        if (!hovering) _remaining = Math.Max(_remaining, PopupRules.MinimumAfterHover);
        ContentChanged?.Invoke();
    }

    // MARK: - Actions

    /// Open (the primary action): open the item's link and clear it.
    public void Open()
    {
        var item = _item;
        Hide();
        if (item is not null) _state.Open(item);
    }

    /// Done: clear it (with Undo in the Board).
    public void Done()
    {
        var item = _item;
        Hide();
        if (item is not null) _state.Done(item.Id);
    }

    /// Later: it comes back in an hour.
    public void Later()
    {
        var item = _item;
        Hide();
        if (item is not null) _state.Snooze(item, LaterMinutes);
    }

    /// "N more waiting": the Board shows everything.
    public void ShowBoard()
    {
        Hide();
        BoardRequested?.Invoke();
    }

    /// Goes away without doing anything to the item (it stays on the Board).
    public void Hide()
    {
        if (!_shown) return;
        _shown = false;
        _hovering = false;
        _item = null;
        _more = 0;
        Hidden?.Invoke();
    }

    public void Dispose()
    {
        _state.ItemArrived -= OnArrived;
        _state.Changed -= OnChanged;
    }
}
