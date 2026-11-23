using DesktopBuddy.App.Platform;
using DesktopBuddy.Core;
using DesktopBuddy.Presentation.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage;
using Windows.Storage.Pickers;
using Auto = Microsoft.UI.Xaml.Automation.AutomationProperties;

namespace DesktopBuddy.App.Views.Settings;

/// What every Settings page needs from the window around it: navigation, dialogs, file pickers.
/// In `--snapshot` mode there is no window (Preview is true), so nothing here opens a real picker.
internal sealed class SettingsContext
{
    private bool _dialogOpen;

    /// The dialog that is open over Settings, if any.
    public ContentDialog? CurrentDialog { get; private set; }

    /// The window that owns the pickers (null when rendering a snapshot).
    public Window? Host { get; init; }
    /// Rendering a snapshot: no network, no dialogs, no pickers.
    public bool Preview { get; init; }
    /// Where the team's sign-in clients come from (a snapshot can pretend there are none).
    public Func<TeamConfig> Team { get; init; } = () => TeamConfig.Current;
    /// Shift was held while Settings opened, so the admin pages show.
    public bool AdminRequested { get; set; }
    public AppState State => AppState.Shared;
    public SettingsShell Shell { get; set; } = null!;
    public AddAccountFlow AddFlow { get; } = new();

    /// Everyday users never see client IDs. Admins hold Shift while opening Settings; a copy with no
    /// sign-in set up shows them anyway, since that's the only way to fix it.
    public bool ShowsAdmin
    {
        get
        {
            var team = Team();
            return AdminRequested || !(team.HasClient(Provider.google) || team.HasClient(Provider.microsoft));
        }
    }

    public IntPtr Hwnd => Host is null ? IntPtr.Zero : WindowChrome.Handle(Host);

    public void Navigate(string route) => Shell.Navigate(route);

    public static void Open(string url) => App.OpenUrl(url);

    /// Shows a dialog over Settings. Returns None if one is already open.
    public async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        if (Preview || _dialogOpen || Shell.XamlRoot is null) return ContentDialogResult.None;
        _dialogOpen = true;
        CurrentDialog = dialog;
        dialog.XamlRoot = Shell.XamlRoot;
        dialog.RequestedTheme = Shell.ActualTheme;
        Shell.Page?.Hold();
        try { return await dialog.ShowAsync(); }
        finally
        {
            _dialogOpen = false;
            CurrentDialog = null;
            Shell.Page?.Release();
        }
    }

    // MARK: - Files

    public async Task<StorageFile?> PickFileAsync(params string[] extensions)
    {
        if (Preview || Host is null) return null;
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);
        foreach (var e in extensions) picker.FileTypeFilter.Add(e);
        return await picker.PickSingleFileAsync();
    }

    public async Task<StorageFile?> SaveFileAsync(string suggestedName, string typeName, string extension)
    {
        if (Preview || Host is null) return null;
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = suggestedName };
        picker.FileTypeChoices.Add(typeName, [extension]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);
        return await picker.PickSaveFileAsync();
    }

    /// Asks for an image and copies it into the app's own folder, so it survives the original moving.
    public async Task<string?> ChooseLogoAsync(string prefix)
    {
        var file = await PickFileAsync(".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".ico");
        return file is null ? null : ImportLogo(file.Path, prefix);
    }

    public static string? ImportLogo(string source, string prefix)
    {
        try
        {
            var dir = Path.Combine(Paths.SupportDir, "Logos");
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, $"{prefix}-{Guid.NewGuid().ToString("N")[..6]}{Path.GetExtension(source).ToLowerInvariant()}");
            File.Copy(source, target);
            return target;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void CopyToClipboard(string text)
    {
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
    }

    /// Runs on the UI thread, whichever thread asked.
    public static void OnUi(Action action)
    {
        if (App.Instance is { } app) app.UI.TryEnqueue(() => action());
        else action();
    }
}

/// One page of Settings. Pages are built in code and rebuilt whenever something they show changes.
/// A rebuild keeps the scroll position and puts the keyboard focus back (found again by AutomationId),
/// so nothing jumps. The title or breadcrumb above the page belongs to the shell.
internal abstract class SettingsPage : Grid
{
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _body;
    private int _quiet;
    private int _hold;
    private bool _pending;
    private bool _queued;

    protected SettingsContext Ctx { get; }
    protected AppState State => AppState.Shared;
    /// Messages for sender and keyword fields, kept across rebuilds.
    protected Dictionary<string, string?> Problems { get; } = [];

    protected SettingsPage(SettingsContext ctx)
    {
        Ctx = ctx;
        Sk.EnsureResources();
        _body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
        // The page fills the window up to a comfortable width and stays against the left edge.
        // Rows keep at least 680 px (DESIGN.md §4), so the cards never wrap their controls under the title.
        var holder = new Grid { Margin = new Thickness(40, 0, 40, 40) };
        holder.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 880 });
        holder.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        holder.Children.Add(_body);
        _scroll = new ScrollViewer
        {
            Content = holder,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Auto,
        };
        Children.Add(_scroll);
    }

    /// Which page this is (so the window can tell whether you're already here).
    public abstract SettingsRoute Route { get; }

    /// False when the account or client this page shows has been removed.
    public virtual bool Exists => true;

    protected abstract void Build(StackPanel body);

    /// Builds the page for the first time.
    public void Start() => BuildNow();

    private void BuildNow()
    {
        _body.Children.Clear();
        Build(_body);
    }

    /// Scrolls the page (used by snapshots to show the lower part of a long page).
    public void ScrollTo(double offset)
    {
        _scroll.UpdateLayout();
        _scroll.ChangeView(null, offset, null, true);
    }

    /// The window noticed the settings changed.
    public void OnStateChanged()
    {
        if (_quiet > 0) return;       // the change came from a control on this page that already shows it
        QueueRebuild();
    }

    /// Runs a change that came from a control on this page (a slider, a text box) without rebuilding it.
    protected void Quiet(Action change)
    {
        _quiet++;
        try { change(); }
        finally { _quiet--; }
    }

    /// Don't rebuild while a dialog or flyout is open; do it when it closes.
    public void Hold() => _hold++;

    public void Release()
    {
        if (_hold > 0) _hold--;
        if (_hold == 0 && _pending) QueueRebuild();
    }

    public void QueueRebuild()
    {
        if (_hold > 0) { _pending = true; return; }
        if (_queued) return;
        _queued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _queued = false;
            _pending = false;
            Rebuild();
        });
    }

    private void Rebuild()
    {
        if (!Ctx.Shell.IsCurrent(this)) return;
        string? key = null;
        string? text = null;
        var selStart = 0;
        var keyboard = false;
        double offset = _scroll.VerticalOffset;
        if (XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is FrameworkElement focused && IsInside(focused)
            && Auto.GetAutomationId(focused) is { Length: > 0 } id)
        {
            key = id;
            keyboard = focused.FocusState == FocusState.Keyboard;
            if (focused is TextBox tb) { text = tb.Text; selStart = tb.SelectionStart; }
        }

        BuildNow();
        _scroll.UpdateLayout();
        _scroll.ChangeView(null, offset, null, true);

        if (key is not null && Find(_body, key) is { } target)
        {
            if (target is TextBox box && text is not null)
            {
                if (box.Text != text) box.Text = text;
                box.SelectionStart = Math.Min(selStart, box.Text.Length);
            }
            target.Focus(keyboard ? FocusState.Keyboard : FocusState.Programmatic);
        }
    }

    private bool IsInside(DependencyObject d)
    {
        while (d is not null)
        {
            if (ReferenceEquals(d, _body)) return true;
            d = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    private static FrameworkElement? Find(DependencyObject root, string key)
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is Control fe && Auto.GetAutomationId(fe) == key) return fe;
            if (Find(child, key) is { } found) return found;
        }
        return null;
    }
}
