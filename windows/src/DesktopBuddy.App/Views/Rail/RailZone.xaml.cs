using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DesktopBuddy.App.Views.Rail;

/// A drop target shown while the Rail is dragged.
public sealed partial class RailZone : UserControl
{
    public RailZone() => InitializeComponent();

    public void SetLit(bool lit)
    {
        Lit.Visibility = lit ? Visibility.Visible : Visibility.Collapsed;
        Idle.Visibility = lit ? Visibility.Collapsed : Visibility.Visible;
    }
}
