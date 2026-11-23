namespace DesktopBuddy.Core;

// The seam between the state/rules layer (AppState, Poller, rules) and the
// network layer (sign-in, Google and Microsoft sources). The Poller only talks to
// IServiceGateway, so tests can swap in a fake and the network can't leak into rules.

/// A fetch returns the items to offer plus the full set of IDs that are still
/// "live" upstream, so items read or completed elsewhere disappear.
/// Port of FetchResult in GoogleSources.swift.
public sealed class FetchResult
{
    public ItemSource Source { get; set; }
    public List<BuddyItem> Items { get; set; } = [];
    public HashSet<string> LiveIDs { get; set; } = [];
}

/// Live health of one service (for example "Teams chats") for one account.
/// Port of ServiceStatus in AppState.swift.
public sealed record ServiceStatus
{
    public DateTimeOffset? LastChecked { get; init; }
    public int Count { get; init; }
    public string? Error { get; init; }
    public string? FixUrl { get; init; }
}

/// A Google or Microsoft API answered with an error. Port of APIError in GoogleSources.swift.
public sealed class ApiException(int status, string host, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Host { get; } = host;

    /// A console link if the provider said how to fix it (for example "enable the API").
    public string? FixUrl
    {
        get
        {
            var m = System.Text.RegularExpressions.Regex.Match(Message, @"https://[^\s]+");
            return m.Success ? m.Value.TrimEnd('.', ',', ')') : null;
        }
    }
}

/// Port of AuthError in OAuth.swift.
public enum AuthErrorKind { MissingClient, NotSignedIn, Expired, Cancelled, PortInUse, BadResponse }

public sealed class AuthException(AuthErrorKind kind, string message) : Exception(message)
{
    public AuthErrorKind Kind { get; } = kind;
}

/// Everything the Poller needs from the network layer.
public interface IServiceGateway
{
    /// A refresh token is saved for this account.
    bool IsSignedIn(AccountConfig account);

    /// Fetches one service for one account. Throws ApiException or AuthException.
    Task<FetchResult> FetchAsync(AccountConfig account, ItemSource source, int meetingLeadMinutes, CancellationToken ct = default);
}

/// The gateway the running app uses. The network layer installs the live one at startup.
public static class Gateway
{
    public static IServiceGateway Current { get; set; } = new OfflineGateway();
}

/// Used until the network layer is installed, and in tests: nobody is signed in.
public sealed class OfflineGateway : IServiceGateway
{
    public bool IsSignedIn(AccountConfig account) => false;
    public Task<FetchResult> FetchAsync(AccountConfig account, ItemSource source, int meetingLeadMinutes, CancellationToken ct = default) =>
        throw new AuthException(AuthErrorKind.NotSignedIn, "Not signed in");
}
