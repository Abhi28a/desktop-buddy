using DesktopBuddy.Presentation.Rail;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DesktopBuddy.App.Views.Rail;

/// A client token on the Rail that can be pressed (click, Enter or Space).
public sealed partial class RailTokenButton : UserControl
{
    public RailTokenButton()
    {
        InitializeComponent();
    }

    public string OwnerId { get; private set; } = "";

    /// The token was pressed.
    public event Action<string>? Invoked;
    /// The button got or lost the keyboard focus (the Rail shows the name while it has it).
    public event Action<string, bool>? FocusChanged;

    /// Shows the token. `compact` = names only in the tooltip; otherwise the name sits under the token.
    public void Apply(RailTokenViewModel token, double size, bool compact, double nameWidth)
    {
        OwnerId = token.OwnerId;
        Token.Logo = token.Logo;
        Token.OwnerName = token.Name;
        Token.Tint = token.Tint;
        Token.Size = size;
        Token.Count = token.Count;
        Token.IsReminders = token.IsReminders;
        Token.Urgency = token.Urgency;

        NameText.Text = token.Name;
        NameText.MaxWidth = nameWidth;
        NameText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        HotBack.CornerRadius = compact ? new CornerRadius(size / 2 + 4) : (CornerRadius)Application.Current.Resources["DbRadiusRow"];
        // In names mode the wash sits a little wider so it holds the name.
        HotBack.Margin = compact ? new Thickness(-4) : new Thickness(-6, -4, -6, -4);

        AutomationProperties.SetAutomationId(Hit, token.AutomationId);
        AutomationProperties.SetName(Hit, token.AutomationName);
    }

    /// The pointer is on this token (RailWindow tracks the cursor itself).
    public bool IsHot
    {
        set => HotBack.Opacity = value ? 1 : 0;
    }

    private void OnClick(object sender, RoutedEventArgs e) => Invoked?.Invoke(OwnerId);
    private void OnGotFocus(object sender, RoutedEventArgs e) => FocusChanged?.Invoke(OwnerId, true);
    private void OnLostFocus(object sender, RoutedEventArgs e) => FocusChanged?.Invoke(OwnerId, false);
}
