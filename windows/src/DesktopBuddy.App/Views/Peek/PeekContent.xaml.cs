using DesktopBuddy.App.Ui;
using DesktopBuddy.Presentation.Peek;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DesktopBuddy.App.Views.Peek;

/// The Peek card. It draws a PeekCard; the window around it places it and tracks the cursor.
public sealed partial class PeekContent : UserControl
{
    /// Room the window leaves around the card. None: the window is cut to the card's own shape.
    public const double Room = 0;

    private PeekCard? _card;

    public PeekContent()
    {
        InitializeComponent();
        Loaded += (_, _) => Restyle();
        ActualThemeChanged += (_, _) => Restyle();
    }

    public event Action? OpenClicked;
    public event Action? DoneClicked;
    public event Action? LaterClicked;
    public event Action? MoreClicked;

    /// Shows a card: the words, the token, the "N more", and where the time line is.
    public void Apply(PeekCard card)
    {
        _card = card;
        Token.Logo = card.Logo;
        Token.OwnerName = card.OwnerName;
        Token.Tint = card.Tint;
        Token.Count = card.OwnerCount;
        Token.IsReminders = card.IsReminders;

        MetaText.Text = card.Meta;
        TimeText.Text = card.TimeText;
        SenderText.Text = card.Sender;
        SubjectText.Text = card.Subject ?? "";
        SubjectText.Visibility = string.IsNullOrEmpty(card.Subject) ? Visibility.Collapsed : Visibility.Visible;
        DetailText.Text = card.Detail ?? "";
        DetailText.Visibility = string.IsNullOrEmpty(card.Detail) ? Visibility.Collapsed : Visibility.Visible;

        OpenButton.Visibility = card.CanOpen ? Visibility.Visible : Visibility.Collapsed;
        MoreHost.Visibility = card.MoreCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        MoreText.Text = card.MoreText;
        AutomationProperties_SetName(MoreButton, card.MoreText);

        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(TimeText, FontNumeralAlignment.Tabular);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Card, card.SpokenName);
        ApplyThemes();
        SetProgress(card.Progress, card.Hovering);
    }

    /// Where the time line is (1 = full) and whether it is paused because the pointer is on the card.
    public void SetProgress(double progress, bool paused)
    {
        TimeScale.ScaleX = Math.Clamp(progress, 0, 1);
        TimeLine.Background = paused ? Tokens.Brush(this, "DbTextOnInverseSecondaryBrush") : Tokens.Brush(this, "DbAccentBrush");
        Card.BorderBrush = paused ? Tokens.Brush(this, "DbTextOnInverseSecondaryBrush") : Tokens.Brush(this, "DbStrokeOnInverseBrush");
    }

    private void Restyle()
    {
        ApplyThemes();
        if (_card is not null) SetProgress(_card.Progress, _card.Hovering);
    }

    /// The card is on the inverse colour, so the pills that sit on it use the opposite theme: their text,
    /// outline and hover wash then come out right on ink (in light) and on paper (in dark).
    private void ApplyThemes()
    {
        var opposite = ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        var canOpen = _card?.CanOpen ?? true;
        LaterButton.RequestedTheme = opposite;
        MoreHost.RequestedTheme = opposite;
        // With nothing to open, Done is the one primary action (and uses the card's own theme).
        DoneButton.RequestedTheme = canOpen ? opposite : ElementTheme.Default;
        DoneButton.Style = (Style)Application.Current.Resources[canOpen ? "DbSecondaryButtonStyle" : "DbPrimaryButtonStyle"];
    }

    private static void AutomationProperties_SetName(DependencyObject o, string name) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(o, name);

    /// The card's size in DIPs, once measured (without the shadow room).
    public Windows.Foundation.Size MeasureCard()
    {
        Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        return new Windows.Foundation.Size(Math.Ceiling(DesiredSize.Width - 2 * Room), Math.Ceiling(DesiredSize.Height - 2 * Room));
    }

    /// Hides the card until SettleIn runs, so it doesn't flash at full strength first.
    public void PrepareSettle()
    {
        if (Motion.Enabled) Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(Shell).Opacity = 0;
    }

    /// The signature moment: it settles in (fade plus a few percent of scale). No slide.
    public void SettleIn() => Motion.Settle(Shell);

    /// Fades the card out, then runs `done`.
    public void FadeAway(Action? done) => Motion.FadeOut(Shell, done);

    /// The card's rectangle in DIPs relative to this control (for the cursor check).
    public Windows.Foundation.Rect CardBounds =>
        Card.TransformToVisual(this).TransformBounds(new Windows.Foundation.Rect(0, 0, Card.ActualWidth, Card.ActualHeight));

    private void OnOpen(object sender, RoutedEventArgs e) => OpenClicked?.Invoke();
    private void OnDone(object sender, RoutedEventArgs e) => DoneClicked?.Invoke();
    private void OnLater(object sender, RoutedEventArgs e) => LaterClicked?.Invoke();
    private void OnMore(object sender, RoutedEventArgs e) => MoreClicked?.Invoke();
}
