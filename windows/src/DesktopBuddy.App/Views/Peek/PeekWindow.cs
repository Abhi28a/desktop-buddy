using DesktopBuddy.App.Platform;
using DesktopBuddy.App.Ui;
using DesktopBuddy.Core;
using DesktopBuddy.Presentation.Peek;
using DesktopBuddy.App.Views.Rail;
using Microsoft.UI.Dispatching;

namespace DesktopBuddy.App.Views.Peek;

/// The Peek's window: a 400 px card at the top centre of the screen. It is made when something arrives and
/// closed when the Peek goes away. It never takes the focus, so it tracks the cursor itself: pointing at the
/// card pauses the time line.
internal sealed class PeekWindow : FloatingWindow
{
    private readonly PeekViewModel _vm;
    private readonly PeekContent _content = new();
    private readonly DispatcherQueueTimer _clock;
    private bool _closing;

    public PeekWindow(PeekViewModel vm)
    {
        _vm = vm;
        SetRoot(_content, "DbSurfaceInverseBrush");
        Shape(14);      // DbRadiusPanel
        _content.PrepareSettle();
        if (Ui.Motion.Enabled) SetWindowAlpha(0);
        _content.OpenClicked += _vm.Open;
        _content.DoneClicked += _vm.Done;
        _content.LaterClicked += _vm.Later;
        _content.MoreClicked += _vm.ShowBoard;
        _content.Loaded += (_, _) =>
        {
            _content.SettleIn();
            FadeWindow(1, Motion.SlowMs);
        };

        _clock = DispatcherQueue.CreateTimer();
        // The time line moves smoothly; with Animation effects off it only needs to keep up.
        _clock.Interval = TimeSpan.FromMilliseconds(Motion.Enabled ? 50 : 250);
        _clock.Tick += (_, _) => Tick();
        _clock.Start();
        Closed += (_, _) => _clock.Stop();
    }

    /// Shows what the view model says and puts the card at the top centre.
    public void Update()
    {
        if (_closing || _vm.Card is not { } card) return;
        _content.Apply(card);
        var size = _content.MeasureCard();
        var (work, screen, _) = Primary();
        var frame = PopupLayout.Frame(screen, work);
        var top = PopupLayout.CardIn(frame).Y + 8;      // a little air under the top of the work area
        PlaceDips(new DockRect(screen.MidX - size.Width / 2, top, size.Width, size.Height));
    }

    /// Fades the card out, then closes the window.
    public void Dismiss()
    {
        if (_closing) return;
        _closing = true;
        _clock.Stop();
        _content.FadeAway(null);
        FadeWindow(0, Motion.FastMs * 2, Close);
    }

    private void Tick()
    {
        if (_closing) return;
        // This window never takes the focus, and a window that isn't active doesn't reliably get
        // pointer-over events, so watch the cursor directly.
        Native.GetCursorPos(out var cursor);
        var inside = Contains(FrameBounds, cursor);
        if (inside != _vm.IsHovering) _vm.SetHovering(inside);   // raises ContentChanged: the controller calls Update()
        _vm.Tick();                                              // may hide the Peek
        if (_closing || !_vm.IsShown) return;
        _content.SetProgress(_vm.Progress, _vm.IsHovering);
    }
}
