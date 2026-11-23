using DesktopBuddy.App.Ui;
using DesktopBuddy.Presentation.Rail;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace DesktopBuddy.App.Views.Rail;

/// The Rail's pill and its tokens. It draws what the RailViewModel says; the window around it
/// (RailWindow) places it, moves it and tracks the cursor.
public sealed partial class RailContent : UserControl
{
    /// Room the window leaves around the pill. None: the window is cut to the pill's own shape.
    public const double Room = 0;

    private readonly Dictionary<string, RailTokenButton> _buttons = [];
    private bool _vertical = true;
    private bool _compact = true;
    private double _size = RailRules.BaseTokenSize;

    public RailContent()
    {
        InitializeComponent();
    }

    /// A token was pressed (click, Enter or Space). Not raised for the click that ends a drag.
    public event Action<string>? TokenInvoked;
    /// A token got or lost the keyboard focus.
    public event Action<string, bool>? TokenFocusChanged;
    /// True while the pill is being dragged: the release must not press a token.
    public bool SuppressClick { get; set; }

    /// Shows these tokens. New ones settle in when `animateNew`.
    public void Apply(IReadOnlyList<RailTokenViewModel> tokens, bool vertical, bool compact, double tokenSize, bool animateNew)
    {
        _vertical = vertical;
        _compact = compact;
        _size = tokenSize;
        var thickness = RailRules.PillThickness(tokenSize, compact, vertical);
        var round = new CornerRadius(thickness / 2);
        Pill.CornerRadius = round;

        Tokens.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        Tokens.Spacing = RailRules.Gap;
        var (start, end) = RailRules.EndPads(tokenSize, compact, vertical);
        var cross = RailRules.CrossPad(tokenSize, compact, vertical);
        var centred = double.IsNaN(cross);
        if (vertical)
        {
            Pill.Width = thickness; Pill.Height = double.NaN;
            Tokens.Margin = new Thickness(centred ? 0 : cross, start, 0, end);
            Tokens.HorizontalAlignment = centred ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            Tokens.VerticalAlignment = VerticalAlignment.Top;
        }
        else
        {
            Pill.Height = thickness; Pill.Width = double.NaN;
            Tokens.Margin = new Thickness(start, centred ? 0 : cross, end, 0);
            Tokens.VerticalAlignment = centred ? VerticalAlignment.Center : VerticalAlignment.Top;
            Tokens.HorizontalAlignment = HorizontalAlignment.Left;
        }
        // A name never needs to be wider than the pill (vertical) or about a token and a half (horizontal).
        var nameWidth = vertical ? thickness - 14 : Math.Max(tokenSize + 36, 76);

        var wanted = tokens.Select(t => t.OwnerId).ToHashSet();
        foreach (var gone in _buttons.Keys.Where(k => !wanted.Contains(k)).ToList())
        {
            Tokens.Children.Remove(_buttons[gone]);
            _buttons.Remove(gone);
        }

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var isNew = !_buttons.TryGetValue(token.OwnerId, out var button);
            if (isNew)
            {
                button = new RailTokenButton();
                button.Invoked += owner => { if (!SuppressClick) TokenInvoked?.Invoke(owner); };
                button.FocusChanged += (owner, has) => TokenFocusChanged?.Invoke(owner, has);
                _buttons[token.OwnerId] = button;
            }
            button!.Apply(token, tokenSize, compact, nameWidth);
            var at = Tokens.Children.IndexOf(button);
            if (at != i)
            {
                if (at >= 0) Tokens.Children.RemoveAt(at);
                Tokens.Children.Insert(i, button);
            }
            if (isNew && animateNew) Arrive(button);
        }
    }

    /// The signature moment: a new token fades in while growing a few percent (no sliding).
    private static void Arrive(RailTokenButton button)
    {
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(button);
        if (Motion.Enabled) visual.Opacity = 0;
        button.Loaded += OnLoaded;

        static void OnLoaded(object sender, RoutedEventArgs e)
        {
            var b = (RailTokenButton)sender;
            b.Loaded -= OnLoaded;
            Motion.Settle(b);
        }
    }

    /// Hides the pill until SettleIn runs, so it doesn't flash at full strength first.
    public void PrepareSettle()
    {
        if (Motion.Enabled) Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(Shell).Opacity = 0;
    }

    /// The whole pill settles in (when the Rail first appears).
    public void SettleIn() => Motion.Settle(Shell);

    /// Fades the whole pill out, then runs `done`.
    public void FadeAway(Action? done) => Motion.FadeOut(Shell, done);

    /// The pill's size in DIPs (without the shadow room), once the content is measured.
    public Size MeasurePill()
    {
        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return new Size(Math.Ceiling(DesiredSize.Width - 2 * Room), Math.Ceiling(DesiredSize.Height - 2 * Room));
    }

    /// The token under a point (DIPs, relative to this control), or null.
    public string? OwnerAt(Point point)
    {
        foreach (var (owner, rect) in TokenRects())
            if (rect.Contains(point)) return owner;
        return null;
    }

    /// Each token's rectangle in DIPs relative to this control (with the hover wash around it).
    public IEnumerable<(string Owner, Rect Bounds)> TokenRects()
    {
        foreach (var (owner, button) in _buttons)
        {
            if (button.ActualWidth <= 0) continue;
            var box = button.TransformToVisual(this).TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight));
            var pad = _compact ? 6 : 4;
            yield return (owner, new Rect(box.X - pad, box.Y - pad, box.Width + 2 * pad, box.Height + 2 * pad));
        }
    }

    /// Lights one token as pointed at (or none).
    public void SetHot(string? owner)
    {
        foreach (var (id, button) in _buttons) button.IsHot = id == owner;
    }

    /// Shows a token as pointed at, for screenshots.
    public void PreviewHot(string owner) => SetHot(owner);
}
