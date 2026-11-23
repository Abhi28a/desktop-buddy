using DesktopBuddy.App.Platform;
using DesktopBuddy.Core;
using DesktopBuddy.Presentation.Rail;
using Microsoft.UI.Dispatching;

namespace DesktopBuddy.App.Views.Rail;

/// Keeps the Rail window in step with the RailViewModel: opens it when something is waiting, closes it when
/// nothing is (or when paused, or a full-screen app or a presentation has the screen), and re-checks every
/// few seconds, because Windows telling us "full screen" or a meeting getting closer doesn't change AppState.
internal sealed class RailController
{
    private readonly RailViewModel _vm;
    private readonly DispatcherQueueTimer _clock;
    private RailWindow? _window;

    public RailController(AppState state)
    {
        _vm = new RailViewModel(state, ScreenTaken);
        _vm.Updated += Sync;
        _clock = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _clock.Interval = TimeSpan.FromSeconds(3);
        _clock.Tick += (_, _) => _vm.Refresh();
        _clock.Start();
        Sync();
    }

    /// A full-screen app or a presentation has the screen: stay out of the way.
    private static bool ScreenTaken() => Attention.Current() is AttentionState.FullScreen or AttentionState.Presenting;

    private void Sync()
    {
        RailPlacement.Spot = _vm.Spot;
        if (!_vm.IsVisible)
        {
            Close();
            return;
        }
        if (_window is null)
        {
            var window = new RailWindow(_vm, owner => App.Instance?.OpenBoard(BoardAnchor.Rail, owner));
            _window = window;
            window.Closed += (_, _) => { if (ReferenceEquals(_window, window)) _window = null; };
            window.Apply();
            window.ShowPassive();
        }
        else
        {
            _window.Apply();
        }
    }

    private void Close()
    {
        var window = _window;
        if (window is null)
        {
            RailPlacement.Bounds = null;
            return;
        }
        _window = null;
        RailPlacement.Bounds = null;
        window.Dismiss();
    }
}
