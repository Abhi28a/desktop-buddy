using Microsoft.UI.Dispatching;

namespace DesktopBuddy.App.Ui;

/// The app's IDispatcher: runs view-model work on the UI thread. Already on it? Runs straight away.
internal sealed class UiDispatcher(DispatcherQueue queue) : DesktopBuddy.Presentation.IDispatcher
{
    public void Post(Action action)
    {
        if (queue.HasThreadAccess) action();
        else queue.TryEnqueue(() => action());
    }
}
