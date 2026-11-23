using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DesktopBuddy.App.Ui.Components;

public sealed partial class Keycap : UserControl
{
    public Keycap() => InitializeComponent();

    internal Keycap(string key) : this() => Key = key;

    public static readonly DependencyProperty KeyProperty = DependencyProperty.Register(
        nameof(Key), typeof(string), typeof(Keycap),
        new PropertyMetadata("", (d, e) => ((Keycap)d).KeyText.Text = (string)e.NewValue));

    public string Key { get => (string)GetValue(KeyProperty); set => SetValue(KeyProperty, value); }
}
