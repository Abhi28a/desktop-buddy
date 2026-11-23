namespace DesktopBuddy.Presentation.Settings;

/// The six places in the sidebar.
public enum SettingsSection { Home, Accounts, Clients, Notifications, Reminders, About }

/// Every page the Settings window can show.
public enum SettingsPageKind { Home, Accounts, Account, AddAccount, Clients, Client, Notifications, Reminders, About, Team }

/// What the router needs to know about the app to turn the text it was given into a real page.
public sealed record SettingsRouteContext
{
    /// There is at least one account. With none, Home is the sign-in page.
    public bool HasAccounts { get; init; } = true;
    /// Team setup is shown only while Shift was held when Settings opened, or when sign-in isn't set up.
    public bool ShowsTeam { get; init; }
    public Func<string, bool> AccountExists { get; init; } = _ => true;
    public Func<string, bool> ClientExists { get; init; } = _ => true;
}

/// One page of Settings, as text ("account:ABC") and as a value. Pure logic, tested without WinUI.
public sealed record SettingsRoute(SettingsPageKind Kind, string? Id = null)
{
    public static readonly SettingsRoute Home = new(SettingsPageKind.Home);
    public static readonly SettingsRoute Accounts = new(SettingsPageKind.Accounts);
    public static readonly SettingsRoute AddAccount = new(SettingsPageKind.AddAccount);
    public static readonly SettingsRoute Clients = new(SettingsPageKind.Clients);
    public static readonly SettingsRoute Notifications = new(SettingsPageKind.Notifications);
    public static readonly SettingsRoute Reminders = new(SettingsPageKind.Reminders);
    public static readonly SettingsRoute About = new(SettingsPageKind.About);
    public static readonly SettingsRoute Team = new(SettingsPageKind.Team);

    public static SettingsRoute ForAccount(string id) => new(SettingsPageKind.Account, id);
    public static SettingsRoute ForClient(string id) => new(SettingsPageKind.Client, id);

    /// The text form, the same one `App.OpenSettings(page)` accepts.
    public string Key => Kind switch
    {
        SettingsPageKind.Home => "home",
        SettingsPageKind.Accounts => "accounts",
        SettingsPageKind.Account => "account:" + Id,
        SettingsPageKind.AddAccount => "add-account",
        SettingsPageKind.Clients => "clients",
        SettingsPageKind.Client => "client:" + Id,
        SettingsPageKind.Notifications => "notifications",
        SettingsPageKind.Reminders => "reminders",
        SettingsPageKind.About => "about",
        _ => "team",
    };

    /// Which sidebar item is selected while this page is open.
    public SettingsSection Section => Kind switch
    {
        SettingsPageKind.Home => SettingsSection.Home,
        SettingsPageKind.Accounts or SettingsPageKind.Account or SettingsPageKind.AddAccount => SettingsSection.Accounts,
        SettingsPageKind.Clients or SettingsPageKind.Client => SettingsSection.Clients,
        SettingsPageKind.Notifications => SettingsSection.Notifications,
        SettingsPageKind.Reminders => SettingsSection.Reminders,
        _ => SettingsSection.About,
    };

    /// A page you drill into from a list (it gets a breadcrumb), as opposed to a top-level page.
    public bool IsDrillIn => Kind is SettingsPageKind.Account or SettingsPageKind.AddAccount or SettingsPageKind.Client or SettingsPageKind.Team;

    /// The page one step up the breadcrumb.
    public SettingsRoute Parent => Kind switch
    {
        SettingsPageKind.Account or SettingsPageKind.AddAccount => Accounts,
        SettingsPageKind.Client => Clients,
        SettingsPageKind.Team => About,
        _ => this,
    };

    /// The top-level page of a sidebar section.
    public static SettingsRoute ForSection(SettingsSection section) => section switch
    {
        SettingsSection.Home => Home,
        SettingsSection.Accounts => Accounts,
        SettingsSection.Clients => Clients,
        SettingsSection.Notifications => Notifications,
        SettingsSection.Reminders => Reminders,
        _ => About,
    };

    public static string SectionTitle(SettingsSection section) => section switch
    {
        SettingsSection.Home => "Home",
        SettingsSection.Accounts => "Accounts",
        SettingsSection.Clients => "Clients",
        SettingsSection.Notifications => "Notifications",
        SettingsSection.Reminders => "Reminders",
        _ => "About",
    };

    /// The crumbs for a drill-in page, for example ["Accounts", "IE Hub"]. One entry for a top-level page.
    /// `nameOf` gives the account or client name; an empty name reads as "Account" or "Client".
    public IReadOnlyList<string> Breadcrumb(Func<SettingsRoute, string?> nameOf) => Kind switch
    {
        SettingsPageKind.Account => ["Accounts", Named(nameOf(this), "Account")],
        SettingsPageKind.AddAccount => ["Accounts", "Add account"],
        SettingsPageKind.Client => ["Clients", Named(nameOf(this), "Client")],
        SettingsPageKind.Team => ["About", "Team setup"],
        _ => [SectionTitle(Section)],
    };

    private static string Named(string? name, string fallback) => string.IsNullOrWhiteSpace(name) ? fallback : name;

    /// Turns the text `OpenSettings` was given into a real page.
    ///   null or "home"            Home (the sign-in page while there are no accounts)
    ///   "accounts", "account:{id}", "add-account"
    ///   "clients", "client:{id}"
    ///   "notifications" (also "general" and "appearance", which used to be separate pages)
    ///   "reminders", "about", "team"
    /// Anything unknown opens Home. An account or client that no longer exists opens its list.
    public static SettingsRoute Resolve(string? page, SettingsRouteContext? context = null)
    {
        context ??= new SettingsRouteContext();
        var text = (page ?? "").Trim();
        var lower = text.ToLowerInvariant();

        if (lower.StartsWith("account:", StringComparison.Ordinal))
        {
            var id = text[8..];
            return id.Length > 0 && context.AccountExists(id) ? ForAccount(id) : Accounts;
        }
        if (lower.StartsWith("client:", StringComparison.Ordinal))
        {
            var id = text[7..];
            return id.Length > 0 && context.ClientExists(id) ? ForClient(id) : Clients;
        }
        return lower switch
        {
            "accounts" => Accounts,
            "add-account" => AddAccount,
            "clients" => Clients,
            "notifications" or "general" or "appearance" => Notifications,
            "reminders" => Reminders,
            "about" => About,
            "team" => context.ShowsTeam ? Team : About,
            _ => Home,
        };
    }

    /// Home is the sign-in page on a first run (no accounts yet).
    public bool IsSignInHome(SettingsRouteContext context) => Kind == SettingsPageKind.Home && !context.HasAccounts;
}
