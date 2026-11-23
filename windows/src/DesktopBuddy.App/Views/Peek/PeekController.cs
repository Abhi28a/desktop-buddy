using DesktopBuddy.App.Platform;
using DesktopBuddy.Core;
using DesktopBuddy.Presentation.Peek;

namespace DesktopBuddy.App.Views.Peek;

/// Opens the Peek window when the PeekViewModel shows it and closes it when it goes away.
/// The rules (paused, quiet hours, Windows busy, Peek turned off) live in the view model.
internal sealed class PeekController
{
    private readonly AppState _state;
    private readonly PeekViewModel _vm;
    private PeekWindow? _window;

    public PeekController(AppState state)
    {
        _state = state;
        _vm = new PeekViewModel(state, () => Attention.ShouldHoldPeek(state.Config));
        _vm.Shown += OnShown;
        _vm.Hidden += OnHidden;
        _vm.ContentChanged += () => _window?.Update();
        _vm.BoardRequested += () => App.Instance?.OpenBoard(BoardAnchor.Tray);
        state.Changed += ApplyCapture;
    }

    private void OnShown()
    {
        var window = new PeekWindow(_vm);
        _window = window;
        window.Closed += (_, _) => { if (ReferenceEquals(_window, window)) _window = null; };
        window.Update();
        window.ApplyCapture(Hidden());
        window.ShowPassive();
    }

    private void OnHidden()
    {
        var window = _window;
        _window = null;
        window?.Dismiss();
    }

    private bool Hidden() =>
        DockLayout.SharingHidden(_state.Config.HidesFromSharing, _state.ScreenshotsAllowedUntil, _state.Now());

    private void ApplyCapture() => _window?.ApplyCapture(Hidden());
}
