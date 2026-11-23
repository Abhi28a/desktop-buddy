using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace DesktopBuddy.App.Ui;

/// Reads design tokens (Styles/Tokens.xaml) from code for an element's OWN theme.
/// Prefer {ThemeResource Db…} in XAML; use this only where a brush has to be chosen in code.
/// Application.Current.Resources[key] alone would return the app theme's brush, not the element's.
internal static class Tokens
{
    public static Brush Brush(FrameworkElement owner, string key)
    {
        var dictionaryKey = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast
            ? "HighContrast"
            : owner.ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
        foreach (var dict in Merged(Application.Current.Resources))
        {
            if (dict.ThemeDictionaries.TryGetValue(dictionaryKey, out var theme)
                && theme is ResourceDictionary themed && themed.TryGetValue(key, out var value))
            {
                if (value is Brush b) return b;
            }
        }
        return (Brush)Application.Current.Resources[key];
    }

    private static IEnumerable<ResourceDictionary> Merged(ResourceDictionary root)
    {
        yield return root;
        foreach (var child in root.MergedDictionaries)
            foreach (var d in Merged(child)) yield return d;
    }
}
