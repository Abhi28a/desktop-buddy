using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DesktopBuddy.App.Ui.Components;

public enum StatusSeverity { Neutral, Positive, Caution, Critical }

public sealed partial class StatusLine : UserControl
{
    public StatusLine()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) => Update();
        Update();
    }

    internal StatusLine(StatusSeverity severity, string title, string? detail = null, string? actionText = null, Action? action = null) : this()
    {
        Severity = severity;
        Title = title;
        Detail = detail;
        ActionText = actionText;
        Action = action;
    }

    /// Runs when the action button is pressed.
    public Action? Action { get; set; }

    public static readonly DependencyProperty SeverityProperty = Register(nameof(Severity), typeof(StatusSeverity), StatusSeverity.Neutral);
    public StatusSeverity Severity { get => (StatusSeverity)GetValue(SeverityProperty); set => SetValue(SeverityProperty, value); }

    public static readonly DependencyProperty TitleProperty = Register(nameof(Title), typeof(string), "");
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public static readonly DependencyProperty DetailProperty = Register(nameof(Detail), typeof(string), null);
    public string? Detail { get => (string?)GetValue(DetailProperty); set => SetValue(DetailProperty, value); }

    public static readonly DependencyProperty ActionTextProperty = Register(nameof(ActionText), typeof(string), null);
    /// The fix, as a verb ("Fix", "Sign in again"). Null hides the button.
    public string? ActionText { get => (string?)GetValue(ActionTextProperty); set => SetValue(ActionTextProperty, value); }

    public static readonly DependencyProperty AutomationKeyProperty = Register(nameof(AutomationKey), typeof(string), null);
    /// Stable AutomationId stem for UI tests (the button gets "<key>Action").
    public string? AutomationKey { get => (string?)GetValue(AutomationKeyProperty); set => SetValue(AutomationKeyProperty, value); }

    private static DependencyProperty Register(string name, Type type, object? fallback) =>
        DependencyProperty.Register(name, type, typeof(StatusLine), new PropertyMetadata(fallback, (d, _) => ((StatusLine)d).Update()));

    private void Update()
    {
        if (Root is null) return;
        var (glyph, tone, wash) = Severity switch
        {
            StatusSeverity.Positive => ("", "DbPositiveBrush", null),
            StatusSeverity.Caution => ("", "DbCautionBrush", "DbCautionSubtleBrush"),
            StatusSeverity.Critical => ("", "DbCriticalBrush", "DbCriticalSubtleBrush"),
            _ => ("", "DbTextSecondaryBrush", null),
        };
        Icon.Glyph = glyph;
        Icon.Foreground = ThemeBrush(tone);
        Root.Background = wash is null ? null : ThemeBrush(wash);

        TitleText.Text = Title;
        DetailText.Text = Detail ?? "";
        DetailText.Visibility = string.IsNullOrEmpty(Detail) ? Visibility.Collapsed : Visibility.Visible;
        ActionButton.Content = ActionText;
        ActionButton.Visibility = string.IsNullOrEmpty(ActionText) ? Visibility.Collapsed : Visibility.Visible;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ActionButton, string.IsNullOrEmpty(ActionText) ? "" : $"{ActionText}: {Title}");
        if (AutomationKey is { } key)
        {
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(this, key);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(ActionButton, key + "Action");
        }
    }

    /// A token brush for this control's own theme (Light, Dark or High Contrast).
    private Brush ThemeBrush(string key) => Tokens.Brush(this, key);

    private void OnAction(object sender, RoutedEventArgs e) => Action?.Invoke();
}
