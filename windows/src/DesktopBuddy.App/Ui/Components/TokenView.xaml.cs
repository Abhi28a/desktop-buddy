using DesktopBuddy.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DesktopBuddy.App.Ui.Components;

/// The client token: logo pad + client-colour ring + notch count + optional meeting ring.
/// Set the properties from code or x:Bind; everything else follows the theme.
public sealed partial class TokenView : UserControl
{
    public TokenView()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) => Update();
        Update();
    }

    /// Convenience constructor for code-built views.
    internal TokenView(LogoRef? logo, string ownerName, AccountTint tint, double size = 40, int count = 0,
        bool isReminders = false, double urgency = double.NaN) : this()
    {
        Logo = logo;
        OwnerName = ownerName;
        Tint = tint;
        Size = size;
        Count = count;
        IsReminders = isReminders;
        Urgency = urgency;
    }

    // MARK: - Properties

    public static readonly DependencyProperty LogoProperty = Register(nameof(Logo), typeof(object), null);
    /// A LogoRef (picked file or bundled key), or null for a monogram.
    public object? Logo { get => GetValue(LogoProperty); set => SetValue(LogoProperty, value); }

    public static readonly DependencyProperty OwnerNameProperty = Register(nameof(OwnerName), typeof(string), "");
    public string OwnerName { get => (string)GetValue(OwnerNameProperty); set => SetValue(OwnerNameProperty, value); }

    public static readonly DependencyProperty TintProperty = Register(nameof(Tint), typeof(AccountTint), AccountTint.green);
    public AccountTint Tint { get => (AccountTint)GetValue(TintProperty); set => SetValue(TintProperty, value); }

    public static readonly DependencyProperty SizeProperty = Register(nameof(Size), typeof(double), 40.0);
    /// Pad diameter in DIPs (24 in lists, 40 on the Rail, 44 on the Peek).
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    public static readonly DependencyProperty CountProperty = Register(nameof(Count), typeof(int), 0);
    /// Waiting items; 0 hides the notch.
    public int Count { get => (int)GetValue(CountProperty); set => SetValue(CountProperty, value); }

    public static readonly DependencyProperty IsRemindersProperty = Register(nameof(IsReminders), typeof(bool), false);
    /// Your own reminders: a bell on a lily-teal pad (unless a reminder logo was picked).
    public bool IsReminders { get => (bool)GetValue(IsRemindersProperty); set => SetValue(IsRemindersProperty, value); }

    public static readonly DependencyProperty UrgencyProperty = Register(nameof(Urgency), typeof(double), double.NaN);
    /// 0…1: how much time is left before a meeting (drawn as a ring). NaN = no ring.
    public double Urgency { get => (double)GetValue(UrgencyProperty); set => SetValue(UrgencyProperty, value); }

    public static readonly DependencyProperty CutBrushProperty = Register(nameof(CutBrush), typeof(Brush), null);
    /// The colour the notch is cut with — the surface the token sits on. Default: DbSurfaceBaseBrush.
    public Brush? CutBrush { get => (Brush?)GetValue(CutBrushProperty); set => SetValue(CutBrushProperty, value); }

    private static DependencyProperty Register(string name, Type type, object? fallback) =>
        DependencyProperty.Register(name, type, typeof(TokenView),
            new PropertyMetadata(fallback, (d, _) => ((TokenView)d).Update()));

    // MARK: - Layout

    private void Update()
    {
        if (Root is null) return;
        var size = Math.Max(16, Size);
        Root.Width = size;
        Root.Height = size;
        var round = new CornerRadius(size / 2);

        var highContrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        var tint = Palette.Tint(IsReminders ? AccountTint.teal : Tint);
        var logo = Logo as LogoRef?;
        var showBell = IsReminders && logo is null;

        Pad.CornerRadius = round;
        Pad.BorderThickness = new Thickness(Math.Max(1.5, size * 0.05));
        Pad.BorderBrush = highContrast
            ? (Brush)Application.Current.Resources["SystemColorWindowTextColorBrush"]
            : new SolidColorBrush(tint) { Opacity = ActualTheme == ElementTheme.Dark ? 0.6 : 0.45 };
        ReminderPad.CornerRadius = round;
        ReminderPad.Visibility = showBell ? Visibility.Visible : Visibility.Collapsed;

        BellIcon.FontSize = size * 0.42;
        BellIcon.Visibility = showBell ? Visibility.Visible : Visibility.Collapsed;

        var image = showBell ? null : LogoTile.Load(logo);
        LogoImage.Source = image;
        LogoImage.Margin = new Thickness(size * 0.2);
        LogoImage.Visibility = image is null ? Visibility.Collapsed : Visibility.Visible;

        MonogramText.Text = Logos.Monogram(OwnerName);
        MonogramText.FontSize = size * 0.36;
        // Leave room for the notch so it never covers a letter.
        MonogramText.Margin = Count > 0 ? new Thickness(0, 0, size * 0.1, size * 0.1) : new Thickness(0);
        MonogramText.Foreground = new SolidColorBrush(tint);
        MonogramText.Visibility = !showBell && image is null ? Visibility.Visible : Visibility.Collapsed;

        // The notch: a border in the surrounding colour cuts it out of the ring. Below 30 px a number
        // can't be read, so the notch becomes a plain dot (lists show the count as text beside it).
        Notch.Visibility = Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (CutBrush is { } cut) Notch.BorderBrush = cut;
        var small = size < 30;
        Notch.BorderThickness = new Thickness(small ? 2 : Math.Max(3, size * 0.09));
        Notch.CornerRadius = new CornerRadius(small ? 6 : size * 0.26);
        Notch.MinWidth = small ? 12 : size * 0.62;
        Notch.Width = small ? 12 : double.NaN;
        Notch.Height = small ? 12 : size * 0.52;
        Notch.Padding = small ? new Thickness(0) : new Thickness(size * 0.05, 0, size * 0.05, 0);
        Notch.Margin = small ? new Thickness(0, 0, -3, -2) : new Thickness(0, 0, -size * 0.2, -size * 0.1);
        CountText.Visibility = small ? Visibility.Collapsed : Visibility.Visible;
        CountText.Text = Count > 99 ? "99+" : Count.ToString();
        CountText.FontSize = Math.Max(10, size * 0.27);
        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(CountText, FontNumeralAlignment.Tabular);

        // Meeting countdown: an arc around the pad, from 12 o'clock, for the time left.
        if (double.IsNaN(Urgency))
        {
            UrgencyRing.Visibility = Visibility.Collapsed;
        }
        else
        {
            const double thickness = 2;
            UrgencyRing.Visibility = Visibility.Visible;
            UrgencyRing.Width = size + 8;
            UrgencyRing.Height = size + 8;
            UrgencyRing.Margin = new Thickness(-4);
            var circumference = Math.PI * (size + 8 - thickness);
            var left = Math.Clamp(Urgency, 0, 1);
            // StrokeDashArray is in multiples of the stroke thickness.
            UrgencyRing.StrokeDashArray = [Math.Max(0.01, circumference * left / thickness), circumference];
        }

        var name = string.IsNullOrEmpty(OwnerName) ? "Account" : OwnerName;
        AutomationProperties.SetName(this, Count > 0 ? $"{name}, {Count} waiting" : name);
    }
}
