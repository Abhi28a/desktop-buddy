using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;

namespace DesktopBuddy.Core;

// Port of the OAuth actor in OAuth.swift.
// OAuth 2.0 for installed apps (RFC 8252): system browser + PKCE (S256) + loopback redirect.
// Works for Google and the Microsoft identity platform. Read-only scopes only.
//
// Windows differences from the Mac:
//   - Google uses a Desktop-app client: redirect http://127.0.0.1:<random free port>/ and, when set,
//     the client secret (team.json or the "secret.<id>" entry) in the token request. There is no
//     iOS-style app-link redirect, so SchemeReceiver is not ported.
//   - Microsoft is unchanged: public client, redirect http://localhost:<account's port>.
// Refresh tokens live only in the secret store ("refresh.<accountID>", "secret.<accountID>").
// Access tokens live only in memory. Nothing here logs tokens.

/// The OAuth client an account signs in with: its own, or the team default.
public sealed record OAuthClient(string Id, string? Secret, string AuthUrl, string TokenUrl);

public sealed class OAuth : ITokenProvider
{
    public static OAuth Shared { get; } = new();

    /// Opens a web address in the person's browser; false when it couldn't. The UI and tests can replace it.
    public static Func<string, bool> OpenBrowser { get; set; } = DefaultOpenBrowser;

    private static bool DefaultOpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private readonly ISecretStore? _store;
    private readonly Func<TeamConfig>? _team;
    private readonly HttpClient? _http;
    private readonly Func<string, bool>? _openBrowser;
    private readonly TimeProvider _time;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, (string Token, DateTimeOffset Expires)> _accessTokens = [];
    /// In-flight browser sign-ins, so a new attempt (or Cancel) can end a stuck one and free its port.
    private readonly Dictionary<string, LoopbackReceiver> _pending = [];
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _refreshGates = new();

    /// Every argument defaults to the app-wide one (Secrets.Store, TeamConfig.Current, ApiClient.Http,
    /// OAuth.OpenBrowser, the system clock); tests pass their own.
    public OAuth(ISecretStore? store = null, Func<TeamConfig>? team = null, HttpClient? http = null,
                 Func<string, bool>? openBrowser = null, TimeProvider? time = null)
    {
        _store = store;
        _team = team;
        _http = http;
        _openBrowser = openBrowser;
        _time = time ?? TimeProvider.System;
    }

    /// Opens a link in the browser (this instance's opener, else OAuth.OpenBrowser).
    public bool Open(string url) => (_openBrowser ?? OpenBrowser)(url);

    private ISecretStore Store => _store ?? Secrets.Store;
    private TeamConfig Team => _team?.Invoke() ?? TeamConfig.Current;
    private HttpClient Http => _http ?? ApiClient.Http;

    // MARK: - Scopes

    public static IReadOnlyList<string> Scopes(Provider p) => p == Provider.google
        ?
        [
            "openid", "email",
            "https://www.googleapis.com/auth/gmail.readonly",
            "https://www.googleapis.com/auth/calendar.readonly",
            "https://www.googleapis.com/auth/chat.spaces.readonly",
            "https://www.googleapis.com/auth/chat.messages.readonly",
            "https://www.googleapis.com/auth/tasks.readonly",
            "https://www.googleapis.com/auth/directory.readonly",
        ]
        :
        [
            "openid", "email", "offline_access", "User.Read",
            "Mail.Read", "Calendars.Read", "Chat.Read", "Tasks.Read",
        ];

    /// Human-readable list shown before sign-in so people know exactly what is shared.
    public static IReadOnlyList<string> PermissionSummary(Provider p) => p == Provider.google
        ?
        [
            "Read Gmail (never sends or deletes)", "Read Google Chat messages",
            "Read your calendar", "Read Google Tasks", "Look up names in your directory",
        ]
        :
        [
            "Read Outlook mail (never sends or deletes)", "Read your Teams chats",
            "Read your calendar", "Read Microsoft To Do", "Sign you in and read your profile",
        ];

    // MARK: - Secret store keys

    public static string SecretKey(string accountID) => $"secret.{accountID}";
    public static string RefreshKey(string accountID) => $"refresh.{accountID}";

    public bool IsSignedIn(AccountConfig account) => Store.Get(RefreshKey(account.Id)) is not null;

    /// Removes the sign-in (refresh token and cached access token); keeps any saved client secret.
    public void SignOut(AccountConfig account)
    {
        Store.Set(RefreshKey(account.Id), null);
        lock (_lock) _accessTokens.Remove(account.Id);
    }

    /// Removes the sign-in and the saved client secret.
    public void Forget(AccountConfig account)
    {
        SignOut(account);
        Store.Set(SecretKey(account.Id), null);
    }

    /// Ends a browser sign-in that is waiting (and frees its port).
    public void CancelSignIn(string accountID) => TakePending(accountID)?.Cancel();

    private LoopbackReceiver? TakePending(string accountID)
    {
        lock (_lock)
        {
            _pending.Remove(accountID, out var r);
            return r;
        }
    }

    // MARK: - Client resolution (account override -> team default)

    public OAuthClient ResolveClient(AccountConfig a)
    {
        var team = Team;
        if (a.Provider == Provider.google)
        {
            var id = string.IsNullOrEmpty(a.ClientID) ? (team.GoogleClientID ?? "") : a.ClientID;
            if (id.Length == 0) throw AuthErrors.MissingClient(Provider.google);
            // Desktop-type clients send their (non-confidential) secret; one saved for this account wins over the team's.
            var secret = Store.Get(SecretKey(a.Id)) ?? team.GoogleClientSecret;
            return new OAuthClient(id, string.IsNullOrEmpty(secret) ? null : secret,
                "https://accounts.google.com/o/oauth2/v2/auth", "https://oauth2.googleapis.com/token");
        }
        else
        {
            var id = string.IsNullOrEmpty(a.ClientID) ? (team.MicrosoftClientID ?? "") : a.ClientID;
            if (id.Length == 0) throw AuthErrors.MissingClient(Provider.microsoft);
            var tenant = a.Tenant ?? team.MicrosoftTenant ?? "organizations";
            return new OAuthClient(id, null,
                $"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/authorize",
                $"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token");
        }
    }

    /// The redirect address for an account. Google: any free loopback port (pass the one bound);
    /// Microsoft: http://localhost on the account's sign-in port.
    public static string RedirectUri(AccountConfig a, int? port = null) => a.Provider == Provider.google
        ? $"http://127.0.0.1:{port ?? a.RedirectPort}/"
        : $"http://localhost:{a.RedirectPort}";

    // MARK: - Request building (public so the pieces can be tested on their own)

    /// The browser sign-in link. New accounts have no email yet, so the person picks which account to add.
    public static Uri BuildAuthorizationUrl(AccountConfig account, OAuthClient client, string redirectUri, string codeChallenge, string state)
    {
        var q = new List<KeyValuePair<string, string>>
        {
            new("client_id", client.Id),
            new("redirect_uri", redirectUri),
            new("response_type", "code"),
            new("scope", string.Join(" ", Scopes(account.Provider))),
            new("code_challenge", codeChallenge),
            new("code_challenge_method", "S256"),
            new("state", state),
        };
        if (account.Email.Length > 0) q.Add(new("login_hint", account.Email));
        if (account.Provider == Provider.google)
        {
            q.Add(new("access_type", "offline"));
            q.Add(new("prompt", account.Email.Length == 0 ? "select_account consent" : "consent"));
        }
        else
        {
            q.Add(new("prompt", "select_account"));
        }
        return new Uri(ApiClient.BuildUrl(client.AuthUrl, q));
    }

    /// Token request body for exchanging the authorisation code.
    public static List<KeyValuePair<string, string>> CodeExchangeForm(AccountConfig account, OAuthClient client, string code, string redirectUri, string verifier)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("code", code), new("client_id", client.Id), new("redirect_uri", redirectUri),
            new("grant_type", "authorization_code"), new("code_verifier", verifier),
        };
        if (client.Secret is not null) form.Add(new("client_secret", client.Secret));
        if (account.Provider == Provider.microsoft) form.Add(new("scope", string.Join(" ", Scopes(Provider.microsoft))));
        return form;
    }

    /// Token request body for refreshing an access token.
    public static List<KeyValuePair<string, string>> RefreshForm(AccountConfig account, OAuthClient client, string refreshToken)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("refresh_token", refreshToken), new("client_id", client.Id), new("grant_type", "refresh_token"),
        };
        if (client.Secret is not null) form.Add(new("client_secret", client.Secret));
        if (account.Provider == Provider.microsoft) form.Add(new("scope", string.Join(" ", Scopes(Provider.microsoft))));
        return form;
    }

    /// application/x-www-form-urlencoded, percent-encoding everything except - . _ ~ and letters/digits.
    public static string FormEncode(IEnumerable<KeyValuePair<string, string>> form) =>
        string.Join("&", form.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));

    // MARK: - Interactive sign-in

    /// Signs in through the browser. `onUrl` receives the browser link (for "Copy Link" / "Open Again" while
    /// waiting); it may be called on a background thread. Throws AuthException (Cancelled, PortInUse,
    /// MissingClient, BadResponse) on failure; on success the refresh token is saved.
    public async Task SignInAsync(AccountConfig account, Action<Uri>? onUrl = null, CancellationToken ct = default)
    {
        // A previous attempt that was abandoned (tab closed, wrong browser profile) still holds the port.
        if (TakePending(account.Id) is { } old)
        {
            old.Cancel();
            await Task.Delay(300, ct).ConfigureAwait(false);
        }

        var c = ResolveClient(account);
        var verifier = Pkce.NewVerifier();
        var challenge = Pkce.Challenge(verifier);
        var state = Pkce.NewState();

        var google = account.Provider == Provider.google;
        // Google: any free loopback port. Microsoft: the account's port, reachable as "localhost" on IPv4 and IPv6.
        using var receiver = new LoopbackReceiver(google ? 0 : account.RedirectPort, alsoIPv6: !google);
        lock (_lock) _pending[account.Id] = receiver;
        try
        {
            receiver.Start();   // PortInUse
            var redirect = RedirectUri(account, receiver.Port);
            var authUrl = BuildAuthorizationUrl(account, c, redirect, challenge, state);

            var codeTask = receiver.WaitForCallbackAsync(TimeSpan.FromSeconds(300), ct);
            onUrl?.Invoke(authUrl);
            Open(authUrl.AbsoluteUri);

            var query = await codeTask.ConfigureAwait(false);
            if (!query.TryGetValue("state", out var returned) || !string.Equals(returned, state, StringComparison.Ordinal))
                throw AuthErrors.BadResponse("Sign-in response didn't match. Please try again.");
            if (!query.TryGetValue("code", out var code))
            {
                query.TryGetValue("error_description", out var desc);
                query.TryGetValue("error", out var err);
                throw AuthErrors.BadResponse(desc ?? err ?? "No code returned");
            }

            var json = await TokenRequestAsync(c.TokenUrl, CodeExchangeForm(account, c, code, redirect, verifier), ct).ConfigureAwait(false);
            var refresh = json.Str("refresh_token");
            if (refresh is null)
                throw AuthErrors.BadResponse("The provider did not return a long-lived sign-in. Try again.");
            Store.Set(RefreshKey(account.Id), refresh);
            CacheAccessToken(json, account.Id);
        }
        finally
        {
            lock (_lock)
            {
                if (_pending.TryGetValue(account.Id, out var current) && ReferenceEquals(current, receiver))
                    _pending.Remove(account.Id);
            }
        }
    }

    // MARK: - Access tokens

    public async Task<string> AccessTokenAsync(AccountConfig account, CancellationToken ct = default)
    {
        if (TryCached(account.Id, out var cached)) return cached;

        // One refresh at a time per account: Microsoft rotates refresh tokens, so two at once would race.
        var gate = _refreshGates.GetOrAdd(account.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (TryCached(account.Id, out cached)) return cached;
            var refresh = Store.Get(RefreshKey(account.Id)) ?? throw AuthErrors.NotSignedIn();
            var c = ResolveClient(account);
            var json = await TokenRequestAsync(c.TokenUrl, RefreshForm(account, c, refresh), ct).ConfigureAwait(false);
            // Microsoft rotates refresh tokens; keep the newest.
            if (json.Str("refresh_token") is { } newRefresh) Store.Set(RefreshKey(account.Id), newRefresh);
            return CacheAccessToken(json, account.Id);
        }
        finally
        {
            gate.Release();
        }
    }

    private bool TryCached(string id, out string token)
    {
        lock (_lock)
        {
            if (_accessTokens.TryGetValue(id, out var c) && c.Expires > _time.GetUtcNow().AddSeconds(60))
            {
                token = c.Token;
                return true;
            }
        }
        token = "";
        return false;
    }

    private string CacheAccessToken(System.Text.Json.Nodes.JsonObject json, string id)
    {
        var token = json.Str("access_token") ?? throw AuthErrors.BadResponse("No access token");
        var ttl = json.Num("expires_in") ?? 3600;
        lock (_lock) _accessTokens[id] = (token, _time.GetUtcNow().AddSeconds(ttl));
        return token;
    }

    /// POSTs a token request. `invalid_grant` means the sign-in no longer works (Expired).
    private async Task<System.Text.Json.Nodes.JsonObject> TokenRequestAsync(string url, List<KeyValuePair<string, string>> form, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(FormEncode(form), Encoding.UTF8),
        };
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        var (_, json) = await ApiClient.SendAsync(Http, req, TimeSpan.FromSeconds(60), ct).ConfigureAwait(false);
        if (json.Str("error") is { } err)
        {
            if (err == "invalid_grant") throw AuthErrors.Expired();
            throw AuthErrors.BadResponse($"{err}: {json.Str("error_description") ?? ""}");
        }
        return json;
    }

    // MARK: - Who signed in

    /// Which address(es) actually signed in (to catch "approved in the wrong browser profile").
    public async Task<List<string>> SignedInEmailsAsync(AccountConfig account, CancellationToken ct = default)
    {
        var api = new ApiClient(account, this, Http);
        if (account.Provider == Provider.google)
        {
            var info = await api.GetAsync("https://openidconnect.googleapis.com/v1/userinfo", ct: ct).ConfigureAwait(false);
            return info.Str("email") is { } e ? [e.ToLowerInvariant()] : [];
        }
        var me = await api.GetAsync("https://graph.microsoft.com/v1.0/me",
            new Dictionary<string, string> { ["$select"] = "mail,userPrincipalName,otherMails" }, ct: ct).ConfigureAwait(false);
        var all = new List<string?> { me.Str("mail"), me.Str("userPrincipalName") };
        all.AddRange(me.Strs("otherMails"));
        return [.. all.Where(s => s is not null).Select(s => s!.ToLowerInvariant())];
    }
}
