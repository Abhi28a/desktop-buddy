using System.Globalization;
using DesktopBuddy.Core;

namespace DesktopBuddy.Presentation.Settings;

public enum AttentionKind { WrongAccount, SignInExpired, NotSignedIn, ServiceError, ClientIssue }

/// One line under "Needs attention" on Home: what's wrong, in words, and the one thing that fixes it.
/// `FixUrl` is a page that fixes it; without one the fix is to open the account (or, for a client problem, the client).
/// A client problem carries the client and, when Core has a one-click fix, that fix (`Fix`).
public sealed record AttentionItem(
    AttentionKind Kind, string AccountId, ItemSource? Source, StatusLevel Level,
    string Title, string? Detail, string FixLabel, string? FixUrl,
    string? ClientId = null, ClientFix? Fix = null, string? IssueId = null)
{
    /// A stable key for tests and AutomationIds.
    public string Key => ClientId is not null ? $"{ClientId}.{IssueId}" : Source is { } s ? $"{AccountId}.{s}" : AccountId + "." + Kind;
}

/// A token on the Home hero card.
public sealed record HomeOwner(string Id, string Name, AccountTint Tint, int Count, LogoRef? Logo, bool IsReminders);

/// Everything the Home page shows, worked out from AppState. Pure, so it is tested without WinUI.
public sealed class HomeSummary
{
    public int WaitingCount { get; init; }
    public int PlaceCount { get; init; }
    public IReadOnlyList<HomeOwner> Owners { get; init; } = [];
    public IReadOnlyList<AttentionItem> Attention { get; init; } = [];
    /// "waiting across 5 places", "waiting in 1 place" or "nothing waiting".
    public string WaitingText { get; init; } = "";
    /// At least one account is connected and nothing needs attention (shows "All accounts connected").
    public bool AllConnected { get; init; }
    public string AllConnectedDetail { get; init; } = "";
    /// How many accounts and clients have something in the list (for the counts in the sidebar).
    public int AccountsNeedAttention { get; init; }
    public int ClientsNeedAttention { get; init; }
    public bool IsPaused { get; init; }
    /// "Paused until 3:00 PM", "Paused until tomorrow, 8:00 AM" or "Paused".
    public string? PausedText { get; init; }
    public bool IsTimedPause { get; init; }
    public bool QuietHoursOn { get; init; }
    public string QuietText { get; init; } = "";
    public IReadOnlyList<string> ShortcutKeys { get; init; } = [];

    public static HomeSummary Build(AppState state, DateTimeOffset? now = null, CultureInfo? culture = null)
    {
        now ??= state.Now();
        var config = state.Config;
        var owners = state.ActiveOwners
            .Select(id => new HomeOwner(id, state.OwnerName(id), state.OwnerTint(id), state.Items.Count(i => i.Owner == id),
                state.OwnerLogo(id), id == BuddyItem.ReminderOwner))
            .ToList();
        var waiting = state.Items.Count;
        var attention = AttentionItems(state, now.Value);
        var accounts = config.Accounts.Where(a => a.Enabled).ToList();

        var quiet = config.QuietStartHour is not null && config.QuietEndHour is not null;
        return new HomeSummary
        {
            WaitingCount = waiting,
            PlaceCount = owners.Count,
            Owners = owners,
            Attention = attention,
            AccountsNeedAttention = NeedCounts(attention).Accounts,
            ClientsNeedAttention = NeedCounts(attention).Clients,
            WaitingText = WaitingWords(waiting, owners.Count),
            AllConnected = attention.Count == 0 && accounts.Count > 0,
            AllConnectedDetail = LastChecked(state, accounts, now.Value),
            IsPaused = state.IsPausedNow,
            PausedText = state.IsPausedNow ? PausedWords(config, now.Value, culture) : null,
            IsTimedPause = !config.Paused && config.PausedUntilChoice is { } until && now.Value < until,
            QuietHoursOn = quiet,
            QuietText = QuietWords(config, culture),
            ShortcutKeys = config.BoardShortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        };
    }

    public static string WaitingWords(int waiting, int places) =>
        waiting == 0 ? "nothing waiting" : places == 1 ? "waiting in 1 place" : $"waiting across {places} places";

    /// Home shows this many problems, then "N more".
    public const int MaxVisibleAttention = 4;

    /// How many different accounts and clients the problems are about.
    public static (int Accounts, int Clients) NeedCounts(IEnumerable<AttentionItem> items)
    {
        var list = items.ToList();
        return (list.Where(i => i.ClientId is null).Select(i => i.AccountId).Distinct().Count(),
                list.Where(i => i.ClientId is not null).Select(i => i.ClientId).Distinct().Count());
    }

    /// The problems, most serious first: sign-in problems (nothing is checked until they're fixed), then broken
    /// services, then client problems. Accounts and clients keep the order they have in Settings. Accounts and
    /// clients that are switched off don't count.
    public static IReadOnlyList<AttentionItem> AttentionItems(AppState state, DateTimeOffset now)
    {
        var signIn = new List<AttentionItem>();
        var services = new List<AttentionItem>();
        foreach (var a in state.Config.Accounts.Where(a => a.Enabled))
        {
            if (state.WrongAccount.TryGetValue(a.Id, out var other))
            {
                signIn.Add(new(AttentionKind.WrongAccount, a.Id, null, StatusLevel.Critical, $"{a.Label}: signed in as a different account",
                    $"You signed in as {other}, but this account is {a.Email}. Nothing is checked until this is fixed.", "Sign in again", null));
            }
            else if (state.Expired.Contains(a.Id))
            {
                var who = a.Provider == Provider.google ? "Google" : "Microsoft";
                signIn.Add(new(AttentionKind.SignInExpired, a.Id, null, StatusLevel.Critical, $"{a.Label}: sign-in expired",
                    $"{who} asked you to sign in again. Nothing is checked until you do.", "Sign in again", null));
            }
            else if (!state.SignedIn.Contains(a.Id))
            {
                signIn.Add(new(AttentionKind.NotSignedIn, a.Id, null, StatusLevel.Critical, $"{a.Label}: not signed in",
                    $"Sign in to start checking {a.Email}.", "Sign in", null));
            }
            else
            {
                foreach (var source in AccountStatusText.Services.Where(s => AccountStatusText.Watches(a, s)))
                {
                    var status = state.Status(a.Id, source);
                    if (status.Error is not { } error) continue;
                    var service = AccountStatusText.ServiceName(a.Provider, source);
                    services.Add(new(AttentionKind.ServiceError, a.Id, source, StatusLevel.Caution, $"{a.Label}: {service} isn't working",
                        AccountStatusText.ShortError(error), status.FixUrl is null ? "Open account" : "Fix", status.FixUrl));
                }
            }
        }
        // A client problem whose fix is "sign in to account X" repeats the line the account already has: drop it.
        var listed = signIn.Concat(services).Select(i => i.AccountId).ToHashSet();
        var clients = new List<AttentionItem>();
        foreach (var c in state.Config.Clients.Where(c => c.Enabled))
        {
            foreach (var issue in state.ClientIssues(c))
            {
                if (issue.Fix is { Kind: ClientFixKind.SignIn, AccountID: { } owner } && listed.Contains(owner)) continue;
                var fix = FixText.Sentence(issue.FixLabel);
                clients.Add(new(AttentionKind.ClientIssue, "", null, StatusLevel.Caution, issue.Text, $"Client: {c.Name}",
                    issue.Fix is not null && fix.Length > 0 ? fix : "Open client", null, c.Id, issue.Fix, issue.Id));
            }
        }
        return [.. signIn, .. services, .. clients];
    }

    private static string LastChecked(AppState state, List<AccountConfig> accounts, DateTimeOffset now)
    {
        DateTimeOffset? last = accounts
            .SelectMany(a => AccountStatusText.Services.Select(s => state.Status(a.Id, s).LastChecked))
            .Where(t => t is not null).Max();
        return last is { } t ? $"Last checked {TimeText.Ago(t, now)}." : "Waiting for the first check.";
    }

    public static string PausedWords(AppConfig config, DateTimeOffset now, CultureInfo? culture = null)
    {
        if (config.Paused || config.PausedUntilChoice is not { } until) return "Paused";
        var clock = TimeText.Clock(until, culture);
        return until.ToLocalTime().Date == now.ToLocalTime().Date ? $"Paused until {clock}" : $"Paused until tomorrow, {clock}";
    }

    /// "10:00 PM to 8:00 AM, every day", "Off", or a hint when start and end are the same.
    public static string QuietWords(AppConfig config, CultureInfo? culture = null)
    {
        if (config.QuietStartHour is not { } s || config.QuietEndHour is not { } e) return "Off";
        if (s == e) return "Start and end are the same, so quiet hours are off.";
        return $"{TimeText.Hour(s, culture)} to {TimeText.Hour(e, culture)}, every day";
    }
}
