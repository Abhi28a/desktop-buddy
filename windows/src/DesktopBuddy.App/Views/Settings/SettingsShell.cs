using DesktopBuddy.App.Ui;
using DesktopBuddy.Core;
using DesktopBuddy.Presentation.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Auto = Microsoft.UI.Xaml.Automation.AutomationProperties;

namespace DesktopBuddy.App.Views.Settings;

/// Everything inside the Settings window: the title strip, the sidebar with its six sections, the page
/// title or breadcrumb, and the current page. It can be built without a real window, which is how
/// `--snapshot` renders it.
internal sealed class SettingsShell : Grid
{
    private readonly NavigationView _nav;
    private readonly Dictionary<SettingsSection, NavigationViewItem> _items = [];
    private readonly Grid _header;
    private readonly TextBlock _title;
    private readonly BreadcrumbBar _crumbs;
    private readonly ContentControl _host;
    private readonly Grid _titleBar;
    private bool _attached;
    private bool _navQueued;
    private SettingsRoute _route = SettingsRoute.Home;

    public SettingsContext Ctx { get; }
    public SettingsPage? Page { get; private set; }
    public SettingsRoute Route => _route;
    public NavigationView Navigation => _nav;

    /// The strip at the top that the window drags by.
    public UIElement TitleBar => _titleBar;

    public SettingsShell(SettingsContext ctx, string? route)
    {
        Sk.EnsureResources();
        Ctx = ctx;
        ctx.Shell = this;
        Style = Sk.Sty("SkWindowRoot");

        RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _titleBar = BuildTitleBar();
        Children.Add(_titleBar);

        _title = Sk.PageTitle("");
        _crumbs = new BreadcrumbBar { Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left };
        _crumbs.Id("Breadcrumb", "Where you are");
        _crumbs.Resources["BreadcrumbBarItemThemeFontSize"] = 28.0;
        _crumbs.Resources["BreadcrumbBarChevronFontSize"] = 14.0;
        _crumbs.ItemClicked += OnCrumb;
        _header = new Grid { Margin = new Thickness(40, 20, 40, 12) };
        _header.Children.Add(_title);
        _header.Children.Add(_crumbs);

        _host = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(_header);
        Grid.SetRow(_host, 1);
        content.Children.Add(_host);
        var surface = new Border { Style = Sk.Sty("SkContentSurface"), Child = content };

        _nav = new NavigationView
        {
            // Wide: labels. Narrow: icons only, so the page keeps its width (Microsoft's "compact when narrow").
            PaneDisplayMode = NavigationViewPaneDisplayMode.Auto,
            ExpandedModeThresholdWidth = 1000,
            CompactModeThresholdWidth = 560,
            IsPaneToggleButtonVisible = false,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            IsSettingsVisible = false,
            IsPaneOpen = true,
            OpenPaneLength = 248,
            CompactPaneLength = 52,
            IsTitleBarAutoPaddingEnabled = false,
            Background = null,
            Content = surface,
        };
        _nav.Id("SettingsNavigation", "Settings sections");
        _nav.ItemInvoked += OnItemInvoked;
        BuildNavigation();
        Grid.SetRow(_nav, 1);
        Children.Add(_nav);

        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();

        Navigate(route);
    }

    // MARK: - Title strip

    private static Grid BuildTitleBar()
    {
        var bar = new Grid { Height = 40 };
        bar.Children.Add(new Border { Style = Sk.Sty("SkTitleStrip") });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        // On a white tile, so the brown tree reads in dark mode too.
        row.Children.Add(new LogoTile(new LogoRef(null, "dynamatix"), "Desktop Buddy", AccountTint.graphite, 20)
        {
            VerticalAlignment = VerticalAlignment.Center,
        });
        var title = Sk.Text("Desktop Buddy", "DbCaptionStyle");
        title.TextWrapping = TextWrapping.NoWrap;
        title.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(title);
        bar.Children.Add(row);
        return bar;
    }

    // MARK: - Events

    private void Attach()
    {
        if (_attached) return;
        _attached = true;
        Ctx.State.Changed += OnStateChanged;
        SignInCenter.Shared.Changed += OnSignInChanged;
        Ctx.AddFlow.Changed += OnFlowChanged;
    }

    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        Ctx.State.Changed -= OnStateChanged;
        SignInCenter.Shared.Changed -= OnSignInChanged;
        Ctx.AddFlow.Changed -= OnFlowChanged;
        Ctx.AddFlow.CancelIfWaiting();
    }

    private void OnStateChanged()
    {
        // Pages decide synchronously whether the change came from one of their own controls.
        Page?.OnStateChanged();
        QueueNavigation();
        if (Page is { Exists: false })
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (Page is { Exists: false }) Navigate(_route.Parent.Key);
            });
        }
    }

    private void OnSignInChanged() => SettingsContext.OnUi(() =>
    {
        Page?.QueueRebuild();
        QueueNavigation();
    });

    private void OnFlowChanged() => SettingsContext.OnUi(() => Page?.QueueRebuild());

    private void QueueNavigation()
    {
        if (_navQueued) return;
        _navQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _navQueued = false;
            RefreshChrome();
        });
    }

    // MARK: - Routing

    public bool IsCurrent(SettingsPage page) => ReferenceEquals(page, Page);

    public SettingsRouteContext RouteContext => new()
    {
        HasAccounts = Ctx.State.Config.Accounts.Count > 0,
        ShowsTeam = Ctx.ShowsAdmin,
        AccountExists = id => Ctx.State.Account(id) is not null,
        ClientExists = id => Ctx.State.Client(id) is not null,
    };

    public void Navigate(string? key) => Navigate(SettingsRoute.Resolve(key, RouteContext));

    public void Navigate(SettingsRoute route)
    {
        var context = RouteContext;
        // First run: Home is the sign-in page.
        var firstRun = !context.HasAccounts && route.Kind is SettingsPageKind.Home or SettingsPageKind.AddAccount;
        if (Page is { } current && current.Route == route && current.Exists && _route == route) { RefreshChrome(); return; }
        if (Page is AddAccountPage && route.Kind != SettingsPageKind.AddAccount && !(firstRun && route.Kind == SettingsPageKind.Home)) Ctx.AddFlow.CancelIfWaiting();
        if (route.Kind == SettingsPageKind.AddAccount) Ctx.AddFlow.ResetIfDone();

        _route = route;
        var page = Create(route, firstRun);
        Page = page;
        _host.Content = page;
        page.Start();
        RefreshChrome();
    }

    private SettingsPage Create(SettingsRoute route, bool firstRun)
    {
        if (firstRun) return new AddAccountPage(Ctx, firstRun: true);
        return route.Kind switch
        {
            SettingsPageKind.Account => new AccountPage(Ctx, route.Id!),
            SettingsPageKind.Accounts => new AccountsPage(Ctx),
            SettingsPageKind.AddAccount => new AddAccountPage(Ctx, firstRun: false),
            SettingsPageKind.Client => new ClientPage(Ctx, route.Id!),
            SettingsPageKind.Clients => new ClientsPage(Ctx),
            SettingsPageKind.Notifications => new NotificationsPage(Ctx),
            SettingsPageKind.Reminders => new RemindersPage(Ctx),
            SettingsPageKind.About => new AboutPage(Ctx),
            SettingsPageKind.Team => new TeamPage(Ctx),
            _ => new HomePage(Ctx),
        };
    }

    // MARK: - Header and sidebar

    private void OnCrumb(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        var count = (_crumbs.ItemsSource as ICollection<string>)?.Count ?? 0;
        if (args.Index < count - 1) Navigate(_route.Parent);
    }

    private string? NameOf(SettingsRoute route) => route.Kind switch
    {
        SettingsPageKind.Account => Ctx.State.Account(route.Id!)?.Label,
        SettingsPageKind.Client => Ctx.State.Client(route.Id!)?.Name,
        _ => null,
    };

    private void BuildNavigation()
    {
        void Add(SettingsSection section, string glyph, string id)
        {
            var title = SettingsRoute.SectionTitle(section);
            var item = new NavigationViewItem { Content = title, Icon = new FontIcon { Glyph = glyph }, Tag = section };
            item.Id(id, title);
            ToolTipService.SetToolTip(item, title);
            _items[section] = item;
            _nav.MenuItems.Add(item);
        }
        Add(SettingsSection.Home, Glyphs.Home, "NavHome");
        Add(SettingsSection.Accounts, Glyphs.Person, "NavAccounts");
        Add(SettingsSection.Clients, Glyphs.Briefcase, "NavClients");
        Add(SettingsSection.Notifications, Glyphs.Bell, "NavNotifications");
        Add(SettingsSection.Reminders, Glyphs.Clock, "NavReminders");
        Add(SettingsSection.About, Glyphs.Info, "NavAbout");
    }

    /// The title or breadcrumb, the selected section and the little counts on Accounts and Clients.
    public void RefreshChrome()
    {
        var state = Ctx.State;
        var owns = Page is AddAccountPage { OwnsHeader: true };
        _header.Visibility = owns ? Visibility.Collapsed : Visibility.Visible;
        if (_route.IsDrillIn)
        {
            _title.Visibility = Visibility.Collapsed;
            _crumbs.Visibility = Visibility.Visible;
            var crumbs = _route.Breadcrumb(NameOf).ToList();
            if (_crumbs.ItemsSource is not List<string> old || !old.SequenceEqual(crumbs)) _crumbs.ItemsSource = crumbs;
        }
        else
        {
            _crumbs.Visibility = Visibility.Collapsed;
            _title.Visibility = Visibility.Visible;
            _title.Text = SettingsRoute.SectionTitle(_route.Section);
        }

        var section = !Ctx.State.Config.Accounts.Any() && _route.Kind is SettingsPageKind.Home or SettingsPageKind.AddAccount
            ? SettingsSection.Home
            : _route.Section;
        if (_items.TryGetValue(section, out var item) && !ReferenceEquals(_nav.SelectedItem, item)) _nav.SelectedItem = item;

        // A count beside Accounts and Clients while something needs a fix.
        // Home lists the same problems, so the counts always agree. With no accounts yet (first run) there are none.
        var (accountsNeed, clientsNeed) = state.Config.Accounts.Count == 0
            ? (0, 0)
            : HomeSummary.NeedCounts(HomeSummary.AttentionItems(state, state.Now()));
        Badge(SettingsSection.Accounts, accountsNeed);
        Badge(SettingsSection.Clients, clientsNeed);
    }

    private void Badge(SettingsSection section, int count)
    {
        var item = _items[section];
        var title = SettingsRoute.SectionTitle(section);
        if (count > 0)
        {
            if (item.InfoBadge is not InfoBadge badge) item.InfoBadge = badge = new InfoBadge { Style = Sk.Sty("SkCautionBadge") };
            badge.Value = count;
            Auto.SetName(item, $"{title}, {count} need attention");
        }
        else
        {
            item.InfoBadge = null;
            Auto.SetName(item, title);
        }
    }

    private void OnItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is not NavigationViewItem { Tag: SettingsSection section }) return;
        Navigate(SettingsRoute.ForSection(section));
    }

    /// Tells the sidebar and the About page that the admin pages may have appeared.
    public void RefreshAfterAdminChange()
    {
        Page?.QueueRebuild();
        RefreshChrome();
    }
}
