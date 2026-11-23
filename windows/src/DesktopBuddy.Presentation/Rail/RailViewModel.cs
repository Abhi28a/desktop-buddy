using DesktopBuddy.Core;

namespace DesktopBuddy.Presentation.Rail;

/// The Rail: a slim pill of client tokens on a screen edge that answers "who's waiting for me?".
/// Wraps AppState; the app's window just draws what this says. Single-threaded like AppState (UI thread).
public sealed class RailViewModel : ViewModelBase, IDisposable
{
    private readonly AppState _state;
    private readonly Func<bool> _screenTaken;
    private IReadOnlyList<RailTokenViewModel> _tokens = [];
    private RailHiddenReason _hidden = RailHiddenReason.Empty;
    private bool _compact = true, _hiddenFromCapture = true;
    private double _tokenSize = RailRules.BaseTokenSize;
    private DockSpot _spot = DockSpotInfo.Standard;

    /// `screenTaken` says whether a full-screen app or a presentation has the screen (the app passes
    /// `Attention.Current() is FullScreen or Presenting`; tests pass a flag).
    public RailViewModel(AppState state, Func<bool>? screenTaken = null, IDispatcher? dispatcher = null) : base(dispatcher)
    {
        _state = state;
        _screenTaken = screenTaken ?? (() => false);
        _state.Changed += Refresh;
        Refresh();
    }

    /// Raised after Refresh changed something the view shows.
    public event Action? Updated;

    /// In `ActiveOwners` order. New owners join first (left or top).
    public IReadOnlyList<RailTokenViewModel> Tokens => _tokens;
    public bool IsVisible => _hidden == RailHiddenReason.None;
    public RailHiddenReason HiddenReason => _hidden;
    /// Names show only in the tooltip (true), or sit under the tokens (false).
    public bool Compact => _compact;
    public double TokenSize => _tokenSize;
    public DockSpot Spot => _spot;
    public bool Vertical => DockLayout.IsVertical(_spot);
    /// Kept out of screen sharing and screenshots (the Rail follows the same privacy as every window).
    public bool HiddenFromCapture => _hiddenFromCapture;
    public int TotalWaiting => _tokens.Sum(t => t.Count);

    /// Re-reads AppState (and the screen). The app also calls this every few seconds, because
    /// a full-screen app starting and a meeting getting closer don't change AppState.
    public void Refresh()
    {
        var cfg = _state.Config;
        var now = _state.Now();
        var items = _state.Items;
        var tokens = new List<RailTokenViewModel>();
        foreach (var owner in _state.ActiveOwners)
        {
            var mine = items.Where(i => i.Owner == owner).ToList();
            if (mine.Count == 0) continue;
            var (urgency, minutes) = RailRules.NextMeeting(mine, now, cfg.MeetingLeadMinutes);
            tokens.Add(new RailTokenViewModel(owner, _state.OwnerName(owner), mine.Count, _state.OwnerLogo(owner),
                _state.OwnerTint(owner), owner == BuddyItem.ReminderOwner, urgency, minutes));
        }

        var hidden = RailRules.HiddenReason(tokens.Sum(t => t.Count), _state.IsPausedNow, _screenTaken());
        var compact = cfg.RailCompact;
        var size = RailRules.TokenSize(cfg.DockScale);
        var spot = cfg.DockSpot;
        var capture = DockLayout.SharingHidden(cfg.HidesFromSharing, _state.ScreenshotsAllowedUntil, now);

        var changed = hidden != _hidden || compact != _compact || size != _tokenSize || spot != _spot
            || capture != _hiddenFromCapture || !tokens.SequenceEqual(_tokens);
        _hidden = hidden; _compact = compact; _tokenSize = size; _spot = spot; _hiddenFromCapture = capture;
        _tokens = tokens;
        if (changed)
        {
            OnPropertyChanged(string.Empty);
            Updated?.Invoke();
        }
    }

    /// The pill was dropped near `spot`: remember it (Settings shows the same choice).
    public void MoveTo(DockSpot spot)
    {
        if (_state.Config.DockSpot == spot) return;
        _state.Config.DockSpot = spot;
        _state.SaveConfig();      // raises Changed, which refreshes us
    }

    public void Dispose() => _state.Changed -= Refresh;
}
