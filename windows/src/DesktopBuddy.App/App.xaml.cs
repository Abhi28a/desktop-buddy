using DesktopBuddy.App.Platform;
using DesktopBuddy.App.Ui;
using DesktopBuddy.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DesktopBuddy.App;

/// Puts the app together: state, poller, tray icon, and the windows.
///
/// The windows live in their own partial files so they can be built separately:
///   App.Overlays.cs  – tray flyout, notification list, dock of logos, top pop-up, sounds
///   App.Settings.cs  – the Settings window (accounts, sign-in, clients, general, appearance, reminders, team setup)
///   App.Network.cs   – installs the live Google / Microsoft gateway and wires sign-in events to AppState
/// Each implements the partial methods below; a missing one simply does nothing.
/// Where the Board opens from: above the tray icon, or beside the Rail.
public enum BoardAnchor { Tray, Rail }

public partial class App : Application
{
    public static App? Instance { get; private set; }

    private readonly string[] _args;
    private DispatcherQueue? _ui;
    private TrayIcon? _tray;

    internal AppState State => AppState.Shared;
    internal DispatcherQueue UI => _ui!;

    public App(string[] args)
    {
        _args = args;
        Instance = this;
        InitializeComponent();
        // A tray app: closing a window must not quit it. Quit is explicit (tray menu).
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        // A tray app should keep running after an unexpected UI error: log it (type and stack, never
        // content) and carry on, rather than vanish from the tray.
        UnhandledException += (_, e) =>
        {
            StatusLog.Write("UNHANDLED " + e.Exception);
            e.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            StatusLog.Write("UNOBSERVED " + e.Exception);
            e.SetObserved();
        };
    }

    // MARK: - Hooks implemented by the window files

    /// App.Network.cs: install the live gateway and connect sign-in events to AppState.
    partial void InstallNetwork();
    /// App.Overlays.cs: create the Rail, the Peek and sounds; subscribe to AppState.
    partial void StartOverlays();
    /// OLD tray flyout, replaced by the Board. Kept only until T4 removes its implementation
    /// from App.Overlays.cs; nothing calls it any more. (windows/docs/UI-UX-PLAN.md §7)
    partial void ToggleTrayFlyout();
    /// App.Board.cs (T3): open the Board — or close it if it's already open at that anchor.
    /// `owner` filters it to one client or account (opened from a Rail token); null = everything.
    partial void ShowBoard(BoardAnchor anchor, string? owner);
    /// App.Settings.cs: open (or bring forward) Settings, optionally on a page ("account:<id>",
    /// "client:<id>", "general", "appearance", "reminders", "team", "add-account").
    partial void OpenSettings(string? page);
    /// Screens for `--snapshot`, one list per window file.
    partial void AddOverlaySnapshots(List<SnapshotScreen> screens);
    partial void AddSettingsSnapshots(List<SnapshotScreen> screens);
    partial void AddBoardSnapshots(List<SnapshotScreen> screens);

    /// The UI thread, for view models (DesktopBuddy.Presentation.IDispatcher).
    internal DesktopBuddy.Presentation.IDispatcher Dispatcher => _dispatcher ??= new Ui.UiDispatcher(_ui!);
    private DesktopBuddy.Presentation.IDispatcher? _dispatcher;

    // MARK: - Launch

    protected override async void OnLaunched(LaunchActivatedEventArgs e)
    {
        _ui = DispatcherQueue.GetForCurrentThread();

        var snap = Array.IndexOf(_args, "--snapshot");
        if (snap >= 0)
        {
            await RunSnapshotsAsync(snap);
            Exit();
            return;
        }

        Secrets.Store = new CredentialStore();
        InstallNetwork();

        var state = AppState.Shared;
        state.OpenUrlRequested += OpenUrl;
        state.SignInRequested += id => ShowSettings("account:" + id);
        state.Changed += UpdateTray;
        state.Changed += KeepShortcutInStep;

        // Tray: left click opens the Board; right click opens the short menu.
        _tray = new TrayIcon(IconPath(waiting: false), "Desktop Buddy");
        _tray.Invoked += () => OpenBoard(BoardAnchor.Tray);
        _tray.MenuRequested += ShowTrayMenu;
        UpdateTray();
        // A timed pause ends without any other change, so the tray re-checks itself now and then.
        _trayTimer = _ui.CreateTimer();
        _trayTimer.Interval = TimeSpan.FromSeconds(30);
        _trayTimer.Tick += (_, _) => UpdateTray();
        _trayTimer.Start();

        // The global shortcut opens the Board from anywhere.
        _hotkey = new Hotkey();
        ApplyShortcut(state.Config.BoardShortcut);

        StartOverlays();
        Poller.Shared.Start();

        // Sign in first: with no accounts yet, Settings opens on the sign-in page.
        if (!_args.Contains("--background") && state.Config.Accounts.Count == 0) ShowSettings("add-account");
    }

    // MARK: - Public actions (used by every window)

    public void ShowSettings(string? page = null) => OpenSettings(page);

    /// Opens (or closes) the Board. From the tray and the shortcut it shows everything; from a
    /// Rail token it's filtered to that owner.
    public void OpenBoard(BoardAnchor anchor, string? owner = null) => ShowBoard(anchor, owner);

    /// The global shortcut's state, for Settings ("Ctrl+Alt+N is ready." or why it isn't).
    internal HotkeyResult? ShortcutStatus { get; private set; }

    /// Registers a new shortcut. If Windows refuses it, the old one stays and the result says why.
    internal HotkeyResult ApplyShortcut(string shortcut)
    {
        if (_hotkey is null) return new HotkeyResult(false, "The shortcut isn't available yet.");
        ShortcutStatus = _hotkey.Register(shortcut, () => OpenBoard(BoardAnchor.Tray));
        if (!ShortcutStatus.Registered) StatusLog.Write("Shortcut: " + ShortcutStatus.Message);
        return ShortcutStatus;
    }

    private void KeepShortcutInStep()
    {
        var wanted = AppState.Shared.Config.BoardShortcut;
        if (_hotkey is not null && !string.Equals(_hotkey.Current, wanted, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(_lastShortcutTried, wanted, StringComparison.OrdinalIgnoreCase))
        {
            _lastShortcutTried = wanted;
            ApplyShortcut(wanted);
        }
    }

    /// Where the tray icon is, in physical pixels (for placing the tray flyout).
    internal Native.RECT? TrayBounds => _tray?.Bounds;

    internal static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { StatusLog.Write("Couldn't open a link: " + ex.Message); }
    }

    /// Desktop Buddy was opened again while running: show Settings.
    internal void OnOpenedAgain() => _ui?.TryEnqueue(() => ShowSettings());

    public void Quit()
    {
        Poller.Shared.Stop();
        _trayTimer?.Stop();
        _hotkey?.Dispose();
        _tray?.Dispose();
        _tray = null;
        Exit();
    }

    // MARK: - Tray

    private Hotkey? _hotkey;
    private string? _lastShortcutTried;
    private DispatcherQueueTimer? _trayTimer;
    private bool _trayShowsWaiting;

    private static string IconPath(bool waiting) =>
        Path.Combine(AppContext.BaseDirectory, "Assets", waiting ? "AppIcon-waiting.ico" : "AppIcon.ico");

    /// The short right-click menu. Everything else lives in the Board and Settings.
    private void ShowTrayMenu()
    {
        var state = AppState.Shared;
        var shortcut = _hotkey?.Current;
        var items = new List<TrayIcon.MenuItem>
        {
            new(shortcut is null ? "Open Board" : $"Open Board\t{shortcut}", () => OpenBoard(BoardAnchor.Tray)),
            TrayIcon.MenuItem.Separator,
        };
        if (state.IsPausedNow)
        {
            items.Add(new(PausedText(state), Enabled: false));
            items.Add(new("Resume notifications", state.Resume));
        }
        else
        {
            items.Add(new("Pause for 1 hour", () => state.PauseFor(TimeSpan.FromHours(1))));
            items.Add(new("Pause until tomorrow", state.PauseUntilTomorrow));
        }
        items.Add(new("Check now", () => _ = Poller.Shared.PollAllAsync(), Enabled: !state.IsPausedNow && state.Config.Accounts.Count > 0));
        items.Add(TrayIcon.MenuItem.Separator);
        items.Add(new("Settings…", () => ShowSettings()));
        items.Add(new("Quit Desktop Buddy", Quit));
        _tray?.ShowMenu(items);
    }

    private static string PausedText(AppState state) =>
        state.Config.Paused || state.Config.PausedUntilChoice is null
            ? "Paused"
            : state.Config.PausedUntilChoice.Value.ToLocalTime().Date == state.Now().ToLocalTime().Date
                ? $"Paused until {state.Config.PausedUntilChoice.Value.ToLocalTime():t}"
                : $"Paused until tomorrow, {state.Config.PausedUntilChoice.Value.ToLocalTime():t}";

    /// The tray icon carries a coral dot while something is waiting (and not while paused);
    /// the tooltip says the same in words.
    private void UpdateTray()
    {
        if (_tray is null) return;
        var state = AppState.Shared;
        var count = state.Items.Count;
        var paused = state.IsPausedNow;
        var waiting = count > 0 && !paused;
        if (waiting != _trayShowsWaiting)
        {
            _trayShowsWaiting = waiting;
            _tray.SetIcon(IconPath(waiting));
        }
        _tray.SetTooltip(paused
            ? $"Desktop Buddy – {PausedText(state).ToLowerInvariant()}"
            : count switch
            {
                0 => "Desktop Buddy – nothing waiting",
                1 => "Desktop Buddy – 1 waiting",
                _ => $"Desktop Buddy – {count} waiting",
            });
    }

    // MARK: - Snapshots

    private async Task RunSnapshotsAsync(int argIndex)
    {
        var folder = argIndex + 1 < _args.Length && !_args[argIndex + 1].StartsWith("--", StringComparison.Ordinal)
            ? Path.GetFullPath(_args[argIndex + 1])
            : Path.Combine(AppContext.BaseDirectory, "snapshots");
        var onlyIndex = Array.IndexOf(_args, "--only");
        var only = onlyIndex >= 0 && onlyIndex + 1 < _args.Length ? _args[onlyIndex + 1] : null;

        Paths.UseFolder(Path.Combine(Path.GetTempPath(), "DesktopBuddySnapshot-" + Guid.NewGuid().ToString("N")[..8]));
        Secrets.Store = new MemorySecretStore();
        var state = SampleData.Load();

        var screens = new List<SnapshotScreen>
        {
            new("logos", () =>
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Margin = new Thickness(24),
                    VerticalAlignment = VerticalAlignment.Center };
                foreach (var owner in state.ActiveOwners)
                {
                    var account = state.Account(owner);
                    row.Children.Add(new LogoTile(state.OwnerLogo(owner), state.OwnerName(owner), state.OwnerTint(owner), 56,
                        badge: account?.Provider, isReminders: owner == BuddyItem.ReminderOwner));
                }
                row.Children.Add(new LogoTile(null, "Acme Ltd", AccountTint.purple, 56, badge: Provider.microsoft));
                return row;
            }, 520, 110),
            new("components", () => new Ui.Components.ComponentGallery(), 1040, 1400),
            new("components-settings", () => new Ui.Components.ComponentGallery(settingsRows: true), 760, 420),
        };
        if (_args.Contains("--designlab"))
        {
            // P0 direction boards (windows/docs/UI-UX-PLAN.md); throwaway.
            screens.Clear();
            Views.DesignLab.DesignLab.Add(screens);
        }
        else
        {
            AddOverlaySnapshots(screens);
            AddBoardSnapshots(screens);
            AddSettingsSnapshots(screens);
        }
        await SnapshotRunner.RunAsync(folder, screens, only);
    }
}
