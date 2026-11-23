using Microsoft.UI.Xaml.Controls;

namespace DesktopBuddy.App.Views.Rail;

/// The name card beside a Rail token.
public sealed partial class RailTip : UserControl
{
    public RailTip() => InitializeComponent();

    internal RailTip(string text) : this() => Label.Text = text;

    public string Text
    {
        get => Label.Text;
        set => Label.Text = value;
    }
}
