using System.ComponentModel;
using DesktopBuddy.App.Ui;
using DesktopBuddy.App.Ui.Components;
using DesktopBuddy.Presentation.Board;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace DesktopBuddy.App.Views.Board;

/// The Board's content (header, problem line, grouped list, Undo bar, hints) with its keyboard
/// handling. The window shell (BoardWindow) and `--snapshot` both host this one control.
public sealed partial class BoardView : UserControl
{
    private bool _syncing;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _clock;

    /// `hosted`: it fills a real window (the window supplies the rounded corners and edge) instead
    /// of drawing its own panel (snapshots).
    public BoardView(BoardViewModel vm, bool hosted = false)
    {
        Vm = vm;
        InitializeComponent();
        if (hosted)
        {
            Shell.Width = double.NaN;
            Shell.MaxHeight = double.PositiveInfinity;
            Shell.HorizontalAlignment = HorizontalAlignment.Stretch;
            Shell.VerticalAlignment = VerticalAlignment.Stretch;
            Shell.CornerRadius = new CornerRadius(0);
            Shell.BorderThickness = new Thickness(0);
        }

        Vm.PropertyChanged += OnVmChanged;
        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += (_, _) =>
        {
            _clock = DispatcherQueue.CreateTimer();
            _clock.Interval = TimeSpan.FromSeconds(30);
            _clock.Tick += (_, _) => Vm.Refresh();        // "2m" becomes "3m"; a meeting counts down
            _clock.Start();
        };
        Unloaded += (_, _) =>
        {
            _clock?.Stop();
            Vm.PropertyChanged -= OnVmChanged;
        };

        RebuildList();
        SyncSelection();
        UpdateProblem();
    }

    public BoardViewModel Vm { get; }

    /// Asks the host to close the Board (Esc).
    public event Action? CloseRequested;

    /// Menus open from this Board (Later). The window must not close just because one took the focus.
    public int OpenMenus { get; private set; }

    // MARK: - Sizing and focus (used by the window shell)

    /// The height the content wants at this width, up to `maxHeight` (DIPs).
    public double MeasureHeight(double width, double maxHeight)
    {
        Measure(new Windows.Foundation.Size(width, maxHeight));
        return Math.Ceiling(DesiredSize.Height);
    }

    /// Puts the keyboard focus on the selected row.
    public void FocusSelected()
    {
        if (Vm.SelectedRow is not { } row) return;
        List.ScrollIntoView(row);
        List.UpdateLayout();
        if (List.ContainerFromItem(row) is Control container) container.Focus(FocusState.Keyboard);
    }

    private void EnqueueFocus() => DispatcherQueue.TryEnqueue(FocusSelected);

    private bool ListHasFocus()
    {
        if (XamlRoot is null || FocusManager.GetFocusedElement(XamlRoot) is not DependencyObject focused) return false;
        for (DependencyObject? d = focused; d is not null; d = VisualTreeHelper.GetParent(d))
            if (ReferenceEquals(d, List)) return true;
        return false;
    }

    // MARK: - x:Bind helpers

    /// The first group sits close to the header; later ones get room above.
    public static Thickness HeaderPadding(bool isFirst) => isFirst ? new Thickness(18, 4, 12, 0) : new Thickness(18, 14, 12, 0);

    // MARK: - Keeping the view in step with the view model

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(BoardViewModel.Groups): RebuildList(); break;
            case nameof(BoardViewModel.SelectedRow): SyncSelection(); break;
            case nameof(BoardViewModel.Problem): UpdateProblem(); break;
        }
    }

    /// Swaps in the new groups, keeping the scroll position (and the keyboard focus, if the list had it).
    private void RebuildList()
    {
        var hadFocus = ListHasFocus();
        var scroller = FindScroller(List);
        var offset = scroller?.VerticalOffset ?? 0;
        _syncing = true;
        try
        {
            var source = new CollectionViewSource { IsSourceGrouped = true, Source = Vm.Groups.ToList() };
            List.ItemsSource = source.View;
        }
        finally { _syncing = false; }
        if (offset > 0)
        {
            List.UpdateLayout();
            scroller?.ChangeView(null, offset, null, disableAnimation: true);
        }
        if (hadFocus) EnqueueFocus();
    }

    private void SyncSelection()
    {
        var row = Vm.SelectedRow;
        _syncing = true;
        try { List.SelectedItem = row; }
        finally { _syncing = false; }
        if (row is not null) List.ScrollIntoView(row);
    }

    private void UpdateProblem()
    {
        if (Vm.Problem is not { } p)
        {
            ProblemLine.Visibility = Visibility.Collapsed;
            return;
        }
        ProblemLine.Severity = p.Severity == BoardSeverity.Neutral ? StatusSeverity.Neutral : StatusSeverity.Caution;
        ProblemLine.Title = p.Title;
        ProblemLine.ActionText = p.ActionText;
        ProblemLine.AutomationKey = "BoardProblem";
        ProblemLine.Action = Vm.RunProblemAction;
        ProblemLine.Visibility = Visibility.Visible;
    }

    private static ScrollViewer? FindScroller(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv) return sv;
            if (FindScroller(child) is { } found) return found;
        }
        return null;
    }

    // MARK: - List events

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;
        Vm.SelectedRow = List.SelectedItem as BoardRow;
    }

    private void OnItemClick(object sender, ItemClickEventArgs e) => Vm.Open(e.ClickedItem as BoardRow);

    /// Gives each row's container its AutomationId and a spoken name (type, sender, subject, when).
    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is not BoardRow row) return;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(args.ItemContainer, row.RowAutomationId);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(args.ItemContainer, row.AccessibleName);
    }

    private void OnRowPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is BoardRow row) row.IsHovered = true;
    }

    private void OnRowPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is BoardRow row) row.IsHovered = false;
    }

    private void OnDoneClick(object sender, RoutedEventArgs e) => Vm.Done(((FrameworkElement)sender).Tag as BoardRow);

    private void OnLaterClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is BoardRow row) ShowLaterMenu(row, (FrameworkElement)sender);
    }

    // A group's "Clear" shows while its header is hovered or the button has the keyboard focus. It is
    // always there for Tab and screen readers (opacity only, so nothing shifts).
    private static Button? ClearButton(object sender) =>
        sender is Grid { Children.Count: > 3 } grid ? grid.Children[3] as Button : null;

    private void OnHeaderPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (ClearButton(sender) is { } button) { button.Opacity = 1; ((Grid)sender).Tag = true; }
    }

    private void OnHeaderPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (ClearButton(sender) is not { } button) return;
        ((Grid)sender).Tag = null;
        button.Opacity = button.FocusState == FocusState.Unfocused ? 0 : 1;
    }

    private void OnClearGotFocus(object sender, RoutedEventArgs e) => ((Button)sender).Opacity = 1;

    private void OnClearLostFocus(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        button.Opacity = VisualTreeHelper.GetParent(button) is Grid { Tag: not null } ? 1 : 0;
    }

    private void OnClearGroup(object sender, RoutedEventArgs e) => Vm.ClearGroup(((FrameworkElement)sender).Tag as BoardGroup);
    private void OnClearAll(object sender, RoutedEventArgs e) => Vm.ClearAll();
    private void OnUndo(object sender, RoutedEventArgs e) => Vm.Undo();
    private void OnShowAll(object sender, RoutedEventArgs e) => Vm.ShowEverything();
    private void OnSettings(object sender, RoutedEventArgs e) => Vm.OpenSettings();

    // MARK: - Later menu

    /// 10 minutes, 1 hour, tomorrow 9 am: the same three choices as the Mac.
    private void ShowLaterMenu(BoardRow row, FrameworkElement anchor)
    {
        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
        foreach (var option in Vm.LaterOptions())
        {
            var item = new MenuFlyoutItem { Text = option.Label };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, $"BoardLaterOption_{option.Minutes}");
            item.Click += (_, _) => Vm.Later(row, option.Minutes);
            menu.Items.Add(item);
        }
        menu.Opened += (_, _) => OpenMenus++;
        menu.Closed += (_, _) =>
        {
            OpenMenus = Math.Max(0, OpenMenus - 1);
            EnqueueFocus();                              // back to the list, on the next row if this one left
        };
        menu.ShowAt(anchor);
    }

    // MARK: - Keyboard

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    /// Up and Down are the list's own. The rest: Enter opens, E or Delete is Done, S is Later, Ctrl+Z
    /// undoes, Esc closes. The row keys only count while the focus is in the list, so Enter still
    /// presses a button that has the focus.
    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = IsDown(VirtualKey.Control);
        var alt = IsDown(VirtualKey.Menu);
        if (e.Key == VirtualKey.Escape)
        {
            if (OpenMenus > 0) return;                   // Esc closes the menu first
            e.Handled = true;
            CloseRequested?.Invoke();
            return;
        }
        if (ctrl && !alt && e.Key == VirtualKey.Z)
        {
            e.Handled = true;
            Vm.Undo();
            return;
        }
        if (ctrl || alt || !ListHasFocus()) return;

        var onButton = FocusManager.GetFocusedElement(XamlRoot) is Button;
        switch (e.Key)
        {
            case VirtualKey.Enter when !onButton:
                e.Handled = true;                        // not the list's own click: one open only
                Vm.OpenSelected();
                break;
            case VirtualKey.E:
            case VirtualKey.Delete:
                e.Handled = true;
                Vm.DoneSelected();
                break;
            case VirtualKey.S:
                e.Handled = true;
                if (Vm.SelectedRow is { } row)
                    ShowLaterMenu(row, List.ContainerFromItem(row) as FrameworkElement ?? List);
                break;
        }
    }
}
