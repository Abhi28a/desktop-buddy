using CommunityToolkit.Mvvm.ComponentModel;

namespace DesktopBuddy.Presentation;

/// Base class for every view model: change notification from CommunityToolkit.Mvvm, plus a way
/// to hand work to the UI thread through an IDispatcher (so tests can run it straight away).
public abstract class ViewModelBase : ObservableObject
{
    protected ViewModelBase(IDispatcher? dispatcher = null) => Dispatcher = dispatcher ?? ImmediateDispatcher.Instance;

    protected IDispatcher Dispatcher { get; }

    /// Runs the action on the UI thread (or straight away, with the default test dispatcher).
    protected void Post(Action action) => Dispatcher.Post(action);
}
