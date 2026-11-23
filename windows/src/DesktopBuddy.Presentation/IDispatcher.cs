namespace DesktopBuddy.Presentation;

/// Puts work on the UI thread. The app implements this with its DispatcherQueue;
/// tests use ImmediateDispatcher, so view models run without WinUI.
public interface IDispatcher
{
    void Post(Action action);
}

/// Runs the action right away on the calling thread. The default for tests.
public sealed class ImmediateDispatcher : IDispatcher
{
    public static readonly ImmediateDispatcher Instance = new();

    public void Post(Action action) => action();
}
