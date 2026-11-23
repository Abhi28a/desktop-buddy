namespace DesktopBuddy.App.Ui;

internal static class FluentExtensions
{
    /// Sets a few properties inline while building UI in code: `new TextBlock().With(t => t.Margin = …)`.
    public static T With<T>(this T element, Action<T> change)
    {
        change(element);
        return element;
    }
}
