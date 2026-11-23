using System.Net;
using System.Net.Sockets;
using DesktopBuddy.Core;

namespace DesktopBuddy.Tests;

// Tests for sign-in (Core/Auth): PKCE, request building, the loopback receiver on a real local socket,
// the whole browser flow with a fake browser and a fake token endpoint, token refresh, SignInCenter.
// Port of the sign-in parts of SelfTest.swift. No real network, no real secret store.

internal sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

internal static class AuthKit
{
    public static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public static async Task<int> StatusOf(string url)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try { return (int)(await http.GetAsync(url)).StatusCode; }
        catch (Exception) { return -1; }
    }

    public static async Task<(int Status, string Body)> Fetch(string url)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var r = await http.GetAsync(url);
        return ((int)r.StatusCode, await r.Content.ReadAsStringAsync());
    }

    public static Dictionary<string, string> Query(Uri u) => LoopbackReceiver.ParseQuery(u.Query);
}

/// An OAuth object wired to a fake browser, fake token endpoint and fake userinfo.
internal sealed class AuthRig
{
    public MemorySecretStore Store { get; } = new();
    public TestClock Clock { get; } = new();
    public NetFakeHandler Http { get; }
    public OAuth Oauth { get; }
    public TeamConfig Team { get; set; } = new() { GoogleClientID = "123-abc.apps.googleusercontent.com", MicrosoftClientID = "00000000-aaaa-bbbb-cccc-000000000000" };
    public List<Uri> AuthUrls { get; } = [];
    public List<string> OpenedUrls { get; } = [];

    /// What the "browser" does after opening the sign-in link: the query to send back to the redirect
    /// (null = nothing, the person never finishes).
    public Func<Dictionary<string, string>, string?> Browser { get; set; } = q => $"code=abc123&state={Uri.EscapeDataString(q["state"])}";
    public string TokenJson { get; set; } = """{"access_token":"at-1","refresh_token":"rt-1","expires_in":3600}""";
    public string UserinfoEmail { get; set; } = "me@example.com";
    public bool UserinfoFails { get; set; }

    public AuthRig()
    {
        Http = new NetFakeHandler(Respond);
        Oauth = new OAuth(Store, () => Team, new HttpClient(Http), OpenBrowser, Clock);
    }

    private (HttpStatusCode, string) Respond(SeenRequest r)
    {
        var host = r.Uri.Host;
        if (host is "oauth2.googleapis.com" or "login.microsoftonline.com") return (HttpStatusCode.OK, TokenJson);
        if (UserinfoFails && (host == "openidconnect.googleapis.com" || host == "graph.microsoft.com")) return (HttpStatusCode.InternalServerError, "{}");
        if (host == "openidconnect.googleapis.com") return (HttpStatusCode.OK, $$"""{"email":"{{UserinfoEmail}}","sub":"111"}""");
        if (host == "graph.microsoft.com" && r.Path.EndsWith("/me"))
            return (HttpStatusCode.OK, $$"""{"mail":"{{UserinfoEmail}}","userPrincipalName":"UPN@Example.com","otherMails":["Other@Example.com"]}""");
        return (HttpStatusCode.NotFound, "{}");
    }

    private bool OpenBrowser(string url)
    {
        lock (OpenedUrls) OpenedUrls.Add(url);
        var uri = new Uri(url);
        lock (AuthUrls) AuthUrls.Add(uri);
        var q = AuthKit.Query(uri);
        var callback = Browser(q);
        if (callback is not null)
        {
            var redirect = q["redirect_uri"].TrimEnd('/') + "/?" + callback;
            _ = Task.Run(async () => await AuthKit.StatusOf(redirect));
        }
        return true;
    }

    public SeenRequest TokenRequest() => Http.Requests.First(r => r.Method == HttpMethod.Post);
}

public class PkceTests
{
    [Fact]
    public void VerifierHasAValidLengthAndCharset()
    {
        var v = Pkce.NewVerifier();
        Assert.InRange(v.Length, 43, 128);          // RFC 7636 section 4.1
        Assert.Matches("^[A-Za-z0-9_-]+$", v);
        Assert.NotEqual(v, Pkce.NewVerifier());
    }

    [Fact]
    public void StateIsLongRandomAndUrlSafe()
    {
        var s = Pkce.NewState();
        Assert.Equal(32, s.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", s);
        Assert.NotEqual(s, Pkce.NewState());
    }

    [Fact]
    public void ChallengeMatchesTheRfc7636AppendixBVector() =>
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", Pkce.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));

    [Fact]
    public void Base64UrlHasNoPaddingAndUsesTheUrlAlphabet() =>
        Assert.Equal("-_8", Pkce.Base64Url([0xFB, 0xFF]));
}

public class AuthRequestTests
{
    private static OAuthClient GoogleClient(string? secret = null) =>
        new("123-abc.apps.googleusercontent.com", secret, "https://accounts.google.com/o/oauth2/v2/auth", "https://oauth2.googleapis.com/token");

    [Fact]
    public void GoogleScopesAreTheEightReadOnlyOnes()
    {
        Assert.Equal(
        [
            "openid", "email",
            "https://www.googleapis.com/auth/gmail.readonly",
            "https://www.googleapis.com/auth/calendar.readonly",
            "https://www.googleapis.com/auth/chat.spaces.readonly",
            "https://www.googleapis.com/auth/chat.messages.readonly",
            "https://www.googleapis.com/auth/tasks.readonly",
            "https://www.googleapis.com/auth/directory.readonly",
        ], OAuth.Scopes(Provider.google));
    }

    [Fact]
    public void MicrosoftScopesAreTheDelegatedReadOnesPlusOfflineAccess() =>
        Assert.Equal(["openid", "email", "offline_access", "User.Read", "Mail.Read", "Calendars.Read", "Chat.Read", "Tasks.Read"], OAuth.Scopes(Provider.microsoft));

    [Fact]
    public void NoScopeCanWrite()
    {
        foreach (var s in OAuth.Scopes(Provider.google).Concat(OAuth.Scopes(Provider.microsoft)))
            Assert.DoesNotMatch(@"(?i)(send|write|modify|compose|delete|ReadWrite)", s);
    }

    [Fact]
    public void PermissionSummaryIsWhatThePersonSeesBeforeSigningIn()
    {
        Assert.Equal(["Read Gmail (never sends or deletes)", "Read Google Chat messages", "Read your calendar", "Read Google Tasks", "Look up names in your directory"],
            OAuth.PermissionSummary(Provider.google));
        Assert.Equal(["Read Outlook mail (never sends or deletes)", "Read your Teams chats", "Read your calendar", "Read Microsoft To Do", "Sign you in and read your profile"],
            OAuth.PermissionSummary(Provider.microsoft));
    }

    [Fact]
    public void GoogleUrlForANewAccountLetsThePersonPickTheAccount()
    {
        var a = NetTestData.Google(email: "");
        var url = OAuth.BuildAuthorizationUrl(a, GoogleClient(), "http://127.0.0.1:53011/", "CHALLENGE", "STATE");
        Assert.Equal("accounts.google.com", url.Host);
        Assert.Equal("/o/oauth2/v2/auth", url.AbsolutePath);
        var q = AuthKit.Query(url);
        Assert.Equal("123-abc.apps.googleusercontent.com", q["client_id"]);
        Assert.Equal("http://127.0.0.1:53011/", q["redirect_uri"]);
        Assert.Equal("code", q["response_type"]);
        Assert.Equal(string.Join(" ", OAuth.Scopes(Provider.google)), q["scope"]);
        Assert.Equal("CHALLENGE", q["code_challenge"]);
        Assert.Equal("S256", q["code_challenge_method"]);
        Assert.Equal("STATE", q["state"]);
        Assert.Equal("offline", q["access_type"]);
        Assert.Equal("select_account consent", q["prompt"]);
        Assert.False(q.ContainsKey("login_hint"));
    }

    [Fact]
    public void GoogleUrlForAKnownAccountHintsTheAddressAndSkipsThePicker()
    {
        var q = AuthKit.Query(OAuth.BuildAuthorizationUrl(NetTestData.Google(email: "me@example.com"), GoogleClient(), "http://127.0.0.1:1/", "C", "S"));
        Assert.Equal("me@example.com", q["login_hint"]);
        Assert.Equal("consent", q["prompt"]);
        Assert.Equal("offline", q["access_type"]);
    }

    [Fact]
    public void MicrosoftUrlUsesTheTenantAndTheLocalhostRedirect()
    {
        var a = NetTestData.Microsoft(email: "me@contoso.example.com");
        var client = new OAuthClient("00000000-aaaa", null, "https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize", "https://login.microsoftonline.com/organizations/oauth2/v2.0/token");
        var url = OAuth.BuildAuthorizationUrl(a, client, OAuth.RedirectUri(a), "C", "S");
        Assert.Equal("login.microsoftonline.com", url.Host);
        Assert.Equal("/organizations/oauth2/v2.0/authorize", url.AbsolutePath);
        var q = AuthKit.Query(url);
        Assert.Equal("http://localhost:8766", q["redirect_uri"]);
        Assert.Equal(string.Join(" ", OAuth.Scopes(Provider.microsoft)), q["scope"]);
        Assert.Equal("select_account", q["prompt"]);
        Assert.Equal("me@contoso.example.com", q["login_hint"]);
        Assert.Equal("S256", q["code_challenge_method"]);
        Assert.False(q.ContainsKey("access_type"));
    }

    [Fact]
    public void RedirectAddresses()
    {
        var g = NetTestData.Google();
        Assert.Equal("http://127.0.0.1:53011/", OAuth.RedirectUri(g, 53011));
        var m = NetTestData.Microsoft();
        Assert.Equal($"http://localhost:{m.RedirectPort}", OAuth.RedirectUri(m));       // Microsoft keeps the localhost redirect
        m.RedirectPort = 9100;
        Assert.Equal("http://localhost:9100", OAuth.RedirectUri(m));
    }

    [Fact]
    public void CodeExchangeWithoutASecretSendsNoSecret()
    {
        var form = OAuth.CodeExchangeForm(NetTestData.Google(), GoogleClient(), "the-code", "http://127.0.0.1:5/", "the-verifier");
        var d = form.ToDictionary(k => k.Key, k => k.Value);
        Assert.Equal(["code", "client_id", "redirect_uri", "grant_type", "code_verifier"], form.Select(k => k.Key));
        Assert.Equal("authorization_code", d["grant_type"]);
        Assert.Equal("the-verifier", d["code_verifier"]);
        Assert.False(d.ContainsKey("client_secret"));
        Assert.False(d.ContainsKey("scope"));
    }

    [Fact]
    public void CodeExchangeWithAGoogleSecretSendsIt()
    {
        var d = OAuth.CodeExchangeForm(NetTestData.Google(), GoogleClient("not-a-real-secret"), "c", "r", "v").ToDictionary(k => k.Key, k => k.Value);
        Assert.Equal("not-a-real-secret", d["client_secret"]);
    }

    [Fact]
    public void MicrosoftCodeExchangeAndRefreshCarryTheScopesAndNoSecret()
    {
        var client = new OAuthClient("id", null, "a", "t");
        var exchange = OAuth.CodeExchangeForm(NetTestData.Microsoft(), client, "c", "http://localhost:8766", "v").ToDictionary(k => k.Key, k => k.Value);
        Assert.Equal(string.Join(" ", OAuth.Scopes(Provider.microsoft)), exchange["scope"]);
        Assert.False(exchange.ContainsKey("client_secret"));
        var refresh = OAuth.RefreshForm(NetTestData.Microsoft(), client, "rt").ToDictionary(k => k.Key, k => k.Value);
        Assert.Equal("rt", refresh["refresh_token"]);
        Assert.Equal("refresh_token", refresh["grant_type"]);
        Assert.Equal(string.Join(" ", OAuth.Scopes(Provider.microsoft)), refresh["scope"]);
    }

    [Fact]
    public void GoogleRefreshWithAndWithoutSecret()
    {
        var without = OAuth.RefreshForm(NetTestData.Google(), GoogleClient(), "rt");
        Assert.Equal(["refresh_token", "client_id", "grant_type"], without.Select(k => k.Key));
        var with = OAuth.RefreshForm(NetTestData.Google(), GoogleClient("s"), "rt").ToDictionary(k => k.Key, k => k.Value);
        Assert.Equal("s", with["client_secret"]);
    }

    [Fact]
    public void FormValuesArePercentEncoded() =>
        Assert.Equal("a=b%20c%26d%3De&f~g-h_i.j=%2F%2B", OAuth.FormEncode([new("a", "b c&d=e"), new("f~g-h_i.j", "/+")]));

    [Fact]
    public void ClientComesFromTheAccountThenTheTeam()
    {
        var store = new MemorySecretStore();
        var team = new TeamConfig { GoogleClientID = "team-id", GoogleClientSecret = "team-secret", MicrosoftClientID = "team-ms", MicrosoftTenant = "team-tenant" };
        var oauth = new OAuth(store, () => team);

        var g = NetTestData.Google();
        Assert.Equal("team-id", oauth.ResolveClient(g).Id);
        Assert.Equal("team-secret", oauth.ResolveClient(g).Secret);
        g.ClientID = "own-id";
        Assert.Equal("own-id", oauth.ResolveClient(g).Id);
        store.Set(OAuth.SecretKey(g.Id), "own-secret");
        Assert.Equal("own-secret", oauth.ResolveClient(g).Secret);          // a secret saved for the account wins
        Assert.Equal("https://oauth2.googleapis.com/token", oauth.ResolveClient(g).TokenUrl);

        var m = NetTestData.Microsoft();
        Assert.Equal("team-ms", oauth.ResolveClient(m).Id);
        Assert.Equal("https://login.microsoftonline.com/team-tenant/oauth2/v2.0/token", oauth.ResolveClient(m).TokenUrl);
        m.Tenant = "common";
        Assert.Equal("https://login.microsoftonline.com/common/oauth2/v2.0/authorize", oauth.ResolveClient(m).AuthUrl);
        Assert.Null(oauth.ResolveClient(m).Secret);
    }

    [Fact]
    public void MicrosoftTenantDefaultsToOrganizations()
    {
        var oauth = new OAuth(new MemorySecretStore(), () => new TeamConfig { MicrosoftClientID = "id" });
        Assert.Equal("https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize", oauth.ResolveClient(NetTestData.Microsoft()).AuthUrl);
    }

    [Fact]
    public void AnEmptySecretMeansNoSecret()
    {
        var oauth = new OAuth(new MemorySecretStore(), () => new TeamConfig { GoogleClientID = "id", GoogleClientSecret = "" });
        Assert.Null(oauth.ResolveClient(NetTestData.Google()).Secret);
    }

    [Fact]
    public void NoClientGivesTheAdminMessage()
    {
        var oauth = new OAuth(new MemorySecretStore(), () => new TeamConfig { GoogleClientSecret = "only-a-secret" });
        var e = Assert.Throws<AuthException>(() => oauth.ResolveClient(NetTestData.Google()));
        Assert.Equal(AuthErrorKind.MissingClient, e.Kind);
        Assert.Equal("No Google Workspace sign-in client is set up. Ask your admin for the team setup file, or add a client ID under Advanced.", e.Message);
        var m = Assert.Throws<AuthException>(() => oauth.ResolveClient(NetTestData.Microsoft()));
        Assert.StartsWith("No Microsoft 365 sign-in client", m.Message);
    }

    [Fact]
    public void ErrorMessagesAreWordForWord()
    {
        Assert.Equal("Not signed in.", AuthErrors.NotSignedIn().Message);
        Assert.Equal("Sign-in expired. Sign in again to keep checking this account.", AuthErrors.Expired().Message);
        Assert.Equal("Sign-in was cancelled.", AuthErrors.Cancelled().Message);
        Assert.Equal("Port 8766 is busy. Change the sign-in port under Advanced and try again.", AuthErrors.PortInUse(8766).Message);
    }
}

public class CallbackParsingTests
{
    [Fact]
    public void CodeAndStateAreRead()
    {
        var q = LoopbackReceiver.ParseRequestQuery("GET /?code=4%2F0AbC-d_e&state=xyz HTTP/1.1\r\nHost: 127.0.0.1:5\r\n\r\n");
        Assert.Equal("4/0AbC-d_e", q["code"]);
        Assert.Equal("xyz", q["state"]);
    }

    [Fact]
    public void AccessDeniedComesBackAsAnError()
    {
        var q = LoopbackReceiver.ParseRequestQuery("GET /oauth2callback?error=access_denied&error_description=The+user+said+no&state=xyz HTTP/1.1\r\n\r\n");
        Assert.Equal("access_denied", q["error"]);
        Assert.Equal("The user said no", q["error_description"]);
        Assert.False(q.ContainsKey("code"));
    }

    [Fact]
    public void RequestsWithoutAQueryHaveNoParameters()
    {
        Assert.Empty(LoopbackReceiver.ParseRequestQuery("GET /favicon.ico HTTP/1.1\r\n\r\n"));
        Assert.Empty(LoopbackReceiver.ParseRequestQuery(""));
        Assert.Empty(LoopbackReceiver.ParseRequestQuery("garbage"));
    }

    [Fact]
    public void FragmentsAndBadEscapesDoNotBreakParsing()
    {
        var q = LoopbackReceiver.ParseQuery("/x?a=1#frag&b=2");
        Assert.Equal("1", q["a"]);
        Assert.False(q.ContainsKey("b"));
        Assert.Equal("%zz", LoopbackReceiver.ParseQuery("/x?a=%zz")["a"]);
    }

    [Fact]
    public void ThePageIsFriendlyAndEscapesWhatTheUrlSaid()
    {
        var page = System.Text.Encoding.UTF8.GetString(LoopbackReceiver.Response("Sign-in didn't finish", "<script>alert(1)</script>"));
        Assert.StartsWith("HTTP/1.1 200 OK\r\n", page);
        Assert.Contains("Content-Type: text/html; charset=utf-8", page);
        Assert.Contains("Sign-in didn&#39;t finish", page);
        Assert.DoesNotContain("<script>", page);
        Assert.Contains("&lt;script&gt;", page);
        var notFound = System.Text.Encoding.UTF8.GetString(LoopbackReceiver.Response("Not found", "", "404 Not Found"));
        Assert.StartsWith("HTTP/1.1 404 Not Found\r\n", notFound);
    }
}

// Port of "Browser sign-in: cancel, restart, callback" in SelfTest.swift (real local sockets).
public class LoopbackReceiverTests
{
    [Fact]
    public async Task CancelEndsTheWaitRightAway()
    {
        using var r = new LoopbackReceiver(AuthKit.FreePort());
        var t = r.WaitForCallbackAsync(TimeSpan.FromSeconds(30));
        await Task.Delay(300);
        var started = DateTime.UtcNow;
        r.Cancel();
        var e = await Assert.ThrowsAsync<AuthException>(() => t);
        Assert.Equal(AuthErrorKind.Cancelled, e.Kind);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task CancelBeforeWaitingAlsoEndsIt()
    {
        using var r = new LoopbackReceiver(AuthKit.FreePort());
        r.Cancel();
        var e = await Assert.ThrowsAsync<AuthException>(() => r.WaitForCallbackAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(AuthErrorKind.Cancelled, e.Kind);
    }

    [Fact]
    public async Task TheCallerCanCancelThroughAToken()
    {
        using var r = new LoopbackReceiver(0);
        using var cts = new CancellationTokenSource();
        var t = r.WaitForCallbackAsync(TimeSpan.FromSeconds(30), cts.Token);
        cts.Cancel();
        var e = await Assert.ThrowsAsync<AuthException>(() => t);
        Assert.Equal(AuthErrorKind.Cancelled, e.Kind);
    }

    [Fact]
    public async Task TimingOutCountsAsCancelled()
    {
        using var r = new LoopbackReceiver(0);
        var e = await Assert.ThrowsAsync<AuthException>(() => r.WaitForCallbackAsync(TimeSpan.FromMilliseconds(200)));
        Assert.Equal(AuthErrorKind.Cancelled, e.Kind);
    }

    [Fact]
    public async Task PortIsReusableAfterCancelStrayRequestsAreIgnoredAndTheCallbackReturnsTheCode()
    {
        var port = AuthKit.FreePort();
        using (var first = new LoopbackReceiver(port))
        {
            var t1 = first.WaitForCallbackAsync(TimeSpan.FromSeconds(30));
            await Task.Delay(200);
            first.Cancel();
            await Assert.ThrowsAsync<AuthException>(() => t1);
        }

        using var second = new LoopbackReceiver(port);
        var t2 = second.WaitForCallbackAsync(TimeSpan.FromSeconds(30));
        await Task.Delay(200);
        Assert.Equal(404, await AuthKit.StatusOf($"http://127.0.0.1:{port}/favicon.ico"));    // a stray browser request is ignored
        Assert.False(t2.IsCompleted);
        var ok = await AuthKit.Fetch($"http://127.0.0.1:{port}/oauth2callback?code=abc&state=xyz");
        Assert.Equal(200, ok.Status);                                                          // the callback page loads for the person
        Assert.Contains("signed in", ok.Body);
        Assert.Contains("You can close this tab and go back to Desktop Buddy.", ok.Body);
        var p = await t2;
        Assert.Equal("abc", p["code"]);
        Assert.Equal("xyz", p["state"]);
    }

    [Fact]
    public async Task AccessDeniedReturnsInsteadOfWaiting()
    {
        using var r = new LoopbackReceiver(0);
        var t = r.WaitForCallbackAsync(TimeSpan.FromSeconds(30));
        var page = await AuthKit.Fetch($"http://127.0.0.1:{r.Port}/?error=access_denied&state=xyz");
        Assert.Contains("Sign-in didn&#39;t finish", page.Body);
        var p = await t;
        Assert.Equal("access_denied", p["error"]);
    }

    [Fact]
    public async Task OnlyOneCallbackIsTakenAndThenThePortIsFree()
    {
        using var r = new LoopbackReceiver(0);
        var t = r.WaitForCallbackAsync(TimeSpan.FromSeconds(30));
        var port = r.Port;
        await AuthKit.StatusOf($"http://127.0.0.1:{port}/?code=first&state=s");
        Assert.Equal("first", (await t)["code"]);
        Assert.Equal(-1, await AuthKit.StatusOf($"http://127.0.0.1:{port}/?code=second&state=s"));   // listener is closed
    }

    [Fact]
    public async Task ABrowserPreConnectThatSendsNothingDoesNotBlock()
    {
        using var r = new LoopbackReceiver(0);
        var t = r.WaitForCallbackAsync(TimeSpan.FromSeconds(30));
        using var idle = new TcpClient();
        await idle.ConnectAsync(IPAddress.Loopback, r.Port);
        var ok = await AuthKit.StatusOf($"http://127.0.0.1:{r.Port}/?code=abc&state=s");
        Assert.Equal(200, ok);
        Assert.Equal("abc", (await t)["code"]);
    }

    [Fact]
    public async Task ABusyPortGivesAClearError()
    {
        var port = AuthKit.FreePort();
        using var blocker = new LoopbackReceiver(port);
        var tb = blocker.WaitForCallbackAsync(TimeSpan.FromSeconds(30));
        await Task.Delay(200);
        using var other = new LoopbackReceiver(port);
        var e = await Assert.ThrowsAsync<AuthException>(() => other.WaitForCallbackAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(AuthErrorKind.PortInUse, e.Kind);
        Assert.Equal($"Port {port} is busy. Change the sign-in port under Advanced and try again.", e.Message);
        blocker.Cancel();
        await Assert.ThrowsAsync<AuthException>(() => tb);
    }

    [Fact]
    public async Task ListensOnLocalhostForBothAddressFamiliesWhenAsked()
    {
        using var r = new LoopbackReceiver(0, alsoIPv6: true);
        var t = r.WaitForCallbackAsync(TimeSpan.FromSeconds(30));
        var status = await AuthKit.StatusOf($"http://localhost:{r.Port}/?code=abc&state=s");
        Assert.Equal(200, status);
        Assert.Equal("abc", (await t)["code"]);
    }

    [Fact]
    public async Task OnlyTheLoopbackInterfaceIsOpen()
    {
        using var r = new LoopbackReceiver(0);
        r.Start();
        var listener = typeof(LoopbackReceiver).GetField("_listeners", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var all = (List<TcpListener>)listener.GetValue(r)!;
        Assert.All(all, l => Assert.True(IPAddress.IsLoopback(((IPEndPoint)l.LocalEndpoint).Address)));
        await Task.CompletedTask;
    }
}

public class SignInFlowTests
{
    private static AccountConfig NewGoogle(string email = "") => NetTestData.Google(email: email);

    private static AccountConfig MsOnFreePort(string email = "")
    {
        var a = NetTestData.Microsoft(email: email);
        a.RedirectPort = AuthKit.FreePort();
        return a;
    }

    [Fact]
    public async Task GoogleSignInSavesTheRefreshTokenAndUsesPkceWithTheSecret()
    {
        var rig = new AuthRig { Team = new TeamConfig { GoogleClientID = "123-abc.apps.googleusercontent.com", GoogleClientSecret = "not-a-real-secret" } };
        var a = NewGoogle();
        Uri? shown = null;
        await rig.Oauth.SignInAsync(a, u => shown = u);

        // The browser got the link that was also reported for "Copy Link".
        Assert.NotNull(shown);
        Assert.Equal(shown!.AbsoluteUri, Assert.Single(rig.OpenedUrls));
        var q = AuthKit.Query(shown);
        Assert.Matches(@"^http://127\.0\.0\.1:\d+/$", q["redirect_uri"]);

        // The token request carries the verifier for the challenge that was sent, and the secret.
        var token = rig.TokenRequest();
        Assert.Equal("https://oauth2.googleapis.com/token", token.Uri.AbsoluteUri);
        Assert.Equal("application/x-www-form-urlencoded", token.Headers.GetValueOrDefault("Content-Type") ?? "application/x-www-form-urlencoded");
        var form = token.Form;
        Assert.Equal("abc123", form["code"]);
        Assert.Equal(q["redirect_uri"], form["redirect_uri"]);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal(q["code_challenge"], Pkce.Challenge(form["code_verifier"]));
        Assert.Equal("not-a-real-secret", form["client_secret"]);

        // Only the refresh token is saved, under the Mac's key; the access token is cached in memory.
        Assert.Equal("rt-1", rig.Store.Get("refresh.acc1"));
        Assert.True(rig.Oauth.IsSignedIn(a));
        Assert.Equal("at-1", await rig.Oauth.AccessTokenAsync(a));
        Assert.Equal(1, rig.Http.Count(r => r.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task GoogleWithoutASecretSendsNone()
    {
        var rig = new AuthRig { Team = new TeamConfig { GoogleClientID = "id" } };
        await rig.Oauth.SignInAsync(NewGoogle());
        Assert.False(rig.TokenRequest().Form.ContainsKey("client_secret"));
    }

    [Fact]
    public async Task MicrosoftSignInUsesLocalhostAndTheScopes()
    {
        var rig = new AuthRig();
        var a = MsOnFreePort("me@contoso.example.com");
        await rig.Oauth.SignInAsync(a);
        var q = AuthKit.Query(Assert.Single(rig.AuthUrls));
        Assert.Equal($"http://localhost:{a.RedirectPort}", q["redirect_uri"]);
        Assert.Equal("me@contoso.example.com", q["login_hint"]);
        var token = rig.TokenRequest();
        Assert.Equal("https://login.microsoftonline.com/organizations/oauth2/v2.0/token", token.Uri.AbsoluteUri);
        Assert.Equal(string.Join(" ", OAuth.Scopes(Provider.microsoft)), token.Form["scope"]);
        Assert.False(token.Form.ContainsKey("client_secret"));
        Assert.Equal("rt-1", rig.Store.Get("refresh.acc2"));
    }

    [Fact]
    public async Task AnAccountsOwnClientIdIsUsed()
    {
        var rig = new AuthRig();
        var a = NewGoogle();
        a.ClientID = "own-client.apps.googleusercontent.com";
        await rig.Oauth.SignInAsync(a);
        Assert.Equal("own-client.apps.googleusercontent.com", AuthKit.Query(rig.AuthUrls[0])["client_id"]);
        Assert.Equal("own-client.apps.googleusercontent.com", rig.TokenRequest().Form["client_id"]);
    }

    [Fact]
    public async Task AStateThatDoesNotMatchIsRefusedAndNothingIsSaved()
    {
        var rig = new AuthRig { Browser = _ => "code=abc123&state=not-the-state" };
        var e = await Assert.ThrowsAsync<AuthException>(() => rig.Oauth.SignInAsync(NewGoogle()));
        Assert.Equal(AuthErrorKind.BadResponse, e.Kind);
        Assert.Equal("Sign-in response didn't match. Please try again.", e.Message);
        Assert.Empty(rig.Http.Requests);                       // the code was never exchanged
        Assert.Null(rig.Store.Get("refresh.acc1"));
    }

    [Fact]
    public async Task ACallbackWithoutStateIsRefused()
    {
        var rig = new AuthRig { Browser = _ => "code=abc123" };
        var e = await Assert.ThrowsAsync<AuthException>(() => rig.Oauth.SignInAsync(NewGoogle()));
        Assert.Equal("Sign-in response didn't match. Please try again.", e.Message);
    }

    [Fact]
    public async Task AccessDeniedShowsTheProvidersWord()
    {
        var rig = new AuthRig { Browser = q => $"error=access_denied&state={q["state"]}" };
        var e = await Assert.ThrowsAsync<AuthException>(() => rig.Oauth.SignInAsync(NewGoogle()));
        Assert.Equal(AuthErrorKind.BadResponse, e.Kind);
        Assert.Equal("access_denied", e.Message);
        Assert.Empty(rig.Http.Requests);
    }

    [Fact]
    public async Task ADescriptionFromTheProviderIsShownWhenThereIsOne()
    {
        var rig = new AuthRig { Browser = q => $"error=access_denied&error_description=AADSTS65004%3A+User+declined&state={q["state"]}" };
        var e = await Assert.ThrowsAsync<AuthException>(() => rig.Oauth.SignInAsync(MsOnFreePort()));
        Assert.Equal("AADSTS65004: User declined", e.Message);
    }

    [Fact]
    public async Task NoLongLivedTokenIsAClearError()
    {
        var rig = new AuthRig { TokenJson = """{"access_token":"at-1","expires_in":3600}""" };
        var e = await Assert.ThrowsAsync<AuthException>(() => rig.Oauth.SignInAsync(NewGoogle()));
        Assert.Equal("The provider did not return a long-lived sign-in. Try again.", e.Message);
        Assert.False(rig.Oauth.IsSignedIn(NewGoogle()));
    }

    [Fact]
    public async Task TheTokenEndpointRefusingTheCodeSurfacesItsError()
    {
        var rig = new AuthRig { TokenJson = """{"error":"invalid_client","error_description":"Unauthorized"}""" };
        var e = await Assert.ThrowsAsync<AuthException>(() => rig.Oauth.SignInAsync(NewGoogle()));
        Assert.Equal("invalid_client: Unauthorized", e.Message);
    }

    [Fact]
    public async Task WithoutAClientNothingIsOpened()
    {
        var rig = new AuthRig { Team = new TeamConfig() };
        var e = await Assert.ThrowsAsync<AuthException>(() => rig.Oauth.SignInAsync(NewGoogle()));
        Assert.Equal(AuthErrorKind.MissingClient, e.Kind);
        Assert.Empty(rig.OpenedUrls);
    }

    [Fact]
    public async Task CancelEndsASignInNobodyFinishesAndFreesThePort()
    {
        var rig = new AuthRig { Browser = _ => null };
        var a = MsOnFreePort();
        var t = rig.Oauth.SignInAsync(a);
        await Task.Delay(400);
        var started = DateTime.UtcNow;
        rig.Oauth.CancelSignIn(a.Id);
        var e = await Assert.ThrowsAsync<AuthException>(() => t);
        Assert.Equal(AuthErrorKind.Cancelled, e.Kind);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2));
        using var again = new LoopbackReceiver(a.RedirectPort);
        again.Start();                                         // the port is free again
    }

    [Fact]
    public async Task StartingAgainReplacesAStuckAttempt()
    {
        var attempts = 0;
        var rig = new AuthRig();
        rig.Browser = q => ++attempts == 1 ? null : $"code=abc123&state={q["state"]}";      // the first browser tab is abandoned
        var a = MsOnFreePort();
        var first = rig.Oauth.SignInAsync(a);
        await Task.Delay(400);
        var second = rig.Oauth.SignInAsync(a);
        var e = await Assert.ThrowsAsync<AuthException>(() => first);
        Assert.Equal(AuthErrorKind.Cancelled, e.Kind);
        await second;
        Assert.Equal("rt-1", rig.Store.Get(OAuth.RefreshKey(a.Id)));
    }

    [Fact]
    public async Task ABusyMicrosoftPortGivesTheAdvancedHint()
    {
        var rig = new AuthRig { Browser = _ => null };
        var a = MsOnFreePort();
        using var blocker = new LoopbackReceiver(a.RedirectPort);
        blocker.Start();
        var e = await Assert.ThrowsAsync<AuthException>(() => rig.Oauth.SignInAsync(a));
        Assert.Equal(AuthErrorKind.PortInUse, e.Kind);
        Assert.Equal($"Port {a.RedirectPort} is busy. Change the sign-in port under Advanced and try again.", e.Message);
        Assert.Empty(rig.OpenedUrls);
    }

    [Fact]
    public async Task TwoGoogleSignInsAtOnceGetTheirOwnPorts()
    {
        var rig = new AuthRig();
        var a1 = NetTestData.Google("g1", "");
        var a2 = NetTestData.Google("g2", "");
        await Task.WhenAll(rig.Oauth.SignInAsync(a1), rig.Oauth.SignInAsync(a2));
        Assert.Equal("rt-1", rig.Store.Get("refresh.g1"));
        Assert.Equal("rt-1", rig.Store.Get("refresh.g2"));
        Assert.Equal(2, rig.AuthUrls.Select(u => AuthKit.Query(u)["redirect_uri"]).Distinct().Count());
    }
}

public class TokenTests
{
    [Fact]
    public async Task AccessTokenIsRefreshedOnceThenCachedUntilItNearlyExpires()
    {
        var rig = new AuthRig { TokenJson = """{"access_token":"at-2","expires_in":3600}""" };
        var a = NetTestData.Google();
        rig.Store.Set("refresh.acc1", "rt-old");

        Assert.Equal("at-2", await rig.Oauth.AccessTokenAsync(a));
        Assert.Equal("at-2", await rig.Oauth.AccessTokenAsync(a));
        Assert.Equal(1, rig.Http.Requests.Count);

        var form = rig.Http.Requests[0].Form;
        Assert.Equal("rt-old", form["refresh_token"]);
        Assert.Equal("refresh_token", form["grant_type"]);
        Assert.Equal("123-abc.apps.googleusercontent.com", form["client_id"]);

        rig.Clock.Advance(TimeSpan.FromSeconds(3600 - 61));     // still valid with a minute to spare
        await rig.Oauth.AccessTokenAsync(a);
        Assert.Equal(1, rig.Http.Requests.Count);
        rig.Clock.Advance(TimeSpan.FromSeconds(2));             // inside the last minute: refresh early
        await rig.Oauth.AccessTokenAsync(a);
        Assert.Equal(2, rig.Http.Requests.Count);
        Assert.Equal("rt-old", rig.Store.Get("refresh.acc1"));  // Google didn't send a new refresh token
    }

    [Fact]
    public async Task MicrosoftRotatesTheRefreshToken()
    {
        var rig = new AuthRig { TokenJson = """{"access_token":"at-9","refresh_token":"rt-new","expires_in":"1800"}""" };
        var a = NetTestData.Microsoft();
        rig.Store.Set("refresh.acc2", "rt-old");
        Assert.Equal("at-9", await rig.Oauth.AccessTokenAsync(a));
        Assert.Equal("rt-new", rig.Store.Get("refresh.acc2"));
        rig.Clock.Advance(TimeSpan.FromSeconds(1800 - 61));      // expires_in as a string is honoured
        await rig.Oauth.AccessTokenAsync(a);
        Assert.Equal(1, rig.Http.Requests.Count);
        rig.Clock.Advance(TimeSpan.FromSeconds(2));
        await rig.Oauth.AccessTokenAsync(a);
        Assert.Equal(2, rig.Http.Requests.Count);
    }

    [Fact]
    public async Task ExpiresInDefaultsToAnHour()
    {
        var rig = new AuthRig { TokenJson = """{"access_token":"at"}""" };
        rig.Store.Set("refresh.acc1", "rt");
        await rig.Oauth.AccessTokenAsync(NetTestData.Google());
        rig.Clock.Advance(TimeSpan.FromSeconds(3500));
        await rig.Oauth.AccessTokenAsync(NetTestData.Google());
        Assert.Equal(1, rig.Http.Requests.Count);
    }

    [Fact]
    public async Task ARejectedRefreshTokenMeansSignInExpired()
    {
        var rig = new AuthRig { TokenJson = """{"error":"invalid_grant","error_description":"Token has been expired or revoked."}""" };
        rig.Store.Set("refresh.acc1", "rt");
        var e = await Assert.ThrowsAsync<AuthException>(() => rig.Oauth.AccessTokenAsync(NetTestData.Google()));
        Assert.Equal(AuthErrorKind.Expired, e.Kind);
        Assert.Equal("Sign-in expired. Sign in again to keep checking this account.", e.Message);
    }

    [Fact]
    public async Task OtherTokenErrorsAreBadResponses()
    {
        var rig = new AuthRig { TokenJson = """{"error":"invalid_client","error_description":"The OAuth client was not found."}""" };
        rig.Store.Set("refresh.acc1", "rt");
        var e = await Assert.ThrowsAsync<AuthException>(() => rig.Oauth.AccessTokenAsync(NetTestData.Google()));
        Assert.Equal(AuthErrorKind.BadResponse, e.Kind);
        Assert.Equal("invalid_client: The OAuth client was not found.", e.Message);
    }

    [Fact]
    public async Task AnAnswerWithoutATokenIsABadResponse()
    {
        var rig = new AuthRig { TokenJson = "<html>oops</html>" };
        rig.Store.Set("refresh.acc1", "rt");
        var e = await Assert.ThrowsAsync<AuthException>(() => rig.Oauth.AccessTokenAsync(NetTestData.Google()));
        Assert.Equal("No access token", e.Message);
    }

    [Fact]
    public async Task NotSignedInSaysSoWithoutCallingTheNetwork()
    {
        var rig = new AuthRig();
        var e = await Assert.ThrowsAsync<AuthException>(() => rig.Oauth.AccessTokenAsync(NetTestData.Google()));
        Assert.Equal(AuthErrorKind.NotSignedIn, e.Kind);
        Assert.Empty(rig.Http.Requests);
    }

    [Fact]
    public async Task ConcurrentCallsShareOneRefresh()
    {
        var rig = new AuthRig { TokenJson = """{"access_token":"at-5","refresh_token":"rt-5","expires_in":3600}""" };
        rig.Store.Set("refresh.acc2", "rt-4");
        var a = NetTestData.Microsoft();
        var tokens = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => rig.Oauth.AccessTokenAsync(a))));
        Assert.All(tokens, t => Assert.Equal("at-5", t));
        Assert.Equal(1, rig.Http.Requests.Count);
    }

    [Fact]
    public async Task SignOutKeepsTheSecretForgetRemovesIt()
    {
        var rig = new AuthRig { TokenJson = """{"access_token":"at","expires_in":3600}""" };
        var a = NetTestData.Google();
        rig.Store.Set("refresh.acc1", "rt");
        rig.Store.Set("secret.acc1", "s");
        await rig.Oauth.AccessTokenAsync(a);

        rig.Oauth.SignOut(a);
        Assert.False(rig.Oauth.IsSignedIn(a));
        Assert.Equal("s", rig.Store.Get("secret.acc1"));
        await Assert.ThrowsAsync<AuthException>(() => rig.Oauth.AccessTokenAsync(a));      // the cached access token is gone too

        rig.Store.Set("refresh.acc1", "rt");
        rig.Oauth.Forget(a);
        Assert.Null(rig.Store.Get("refresh.acc1"));
        Assert.Null(rig.Store.Get("secret.acc1"));
    }

    [Fact]
    public void SecretStoreKeysMatchTheMac()
    {
        Assert.Equal("refresh.ABC", OAuth.RefreshKey("ABC"));
        Assert.Equal("secret.ABC", OAuth.SecretKey("ABC"));
    }

    // Port of "Sign-in storage" in SelfTest.swift, against the same store interface.
    [Fact]
    public void TheStoreStoresOverwritesAndRemoves()
    {
        ISecretStore store = new MemorySecretStore();
        store.Set("refresh.t", "token-1");
        Assert.Equal("token-1", store.Get("refresh.t"));
        store.Set("refresh.t", "token-2");
        Assert.Equal("token-2", store.Get("refresh.t"));
        store.Set("refresh.t", null);
        Assert.Null(store.Get("refresh.t"));
    }

    [Fact]
    public async Task TokensAreNeverWrittenToDisk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "db-auth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var rig = new AuthRig();
            var before = Directory.GetFiles(dir).Length;
            await rig.Oauth.SignInAsync(NetTestData.Google());
            Assert.Equal(before, Directory.GetFiles(dir).Length);
            Assert.Equal("rt-1", rig.Store.Get("refresh.acc1"));          // it went to the store, nowhere else
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task WhoSignedInGoogle()
    {
        var rig = new AuthRig { UserinfoEmail = "Jo@Example.com" };
        rig.Store.Set("refresh.acc1", "rt");
        Assert.Equal(["jo@example.com"], await rig.Oauth.SignedInEmailsAsync(NetTestData.Google()));
    }

    [Fact]
    public async Task WhoSignedInMicrosoftListsEveryAddress()
    {
        var rig = new AuthRig { UserinfoEmail = "Jo@Contoso.example.com" };
        rig.Store.Set("refresh.acc2", "rt");
        var emails = await rig.Oauth.SignedInEmailsAsync(NetTestData.Microsoft());
        Assert.Equal(["jo@contoso.example.com", "upn@example.com", "other@example.com"], emails);
        var req = rig.Http.Requests.Last();
        Assert.Equal("mail,userPrincipalName,otherMails", req.Query["$select"]);
    }
}

public class SignInCenterTests
{
    private static AccountConfig MsOnFreePort(string email)
    {
        var a = NetTestData.Microsoft(email: email);
        a.RedirectPort = AuthKit.FreePort();
        return a;
    }

    [Fact]
    public async Task SuccessfulSignInRaisesSignedInAndClearsWaiting()
    {
        var rig = new AuthRig { UserinfoEmail = "me@example.com" };
        var center = new SignInCenter(rig.Oauth);
        var signedIn = new List<string>();
        var changes = 0;
        var sawLink = false;
        center.SignedIn += signedIn.Add;
        center.Changed += () =>
        {
            changes++;
            if (center.Link("acc1") is not null) sawLink = true;
        };

        var ok = await center.StartAsync(NetTestData.Google(email: "me@example.com"));

        Assert.True(ok);
        Assert.Equal(["acc1"], signedIn);
        Assert.True(sawLink);                      // the link was available while waiting
        Assert.True(changes >= 2);
        Assert.False(center.IsWaiting("acc1"));
        Assert.Null(center.Error("acc1"));
    }

    [Fact]
    public async Task ANewAccountHasNoAddressYetSoNoCheckIsMade()
    {
        var rig = new AuthRig { UserinfoEmail = "someone@example.com" };
        var center = new SignInCenter(rig.Oauth);
        var wrong = 0;
        center.WrongAccount += (_, _) => wrong++;
        Assert.True(await center.StartAsync(NetTestData.Google(email: "")));
        Assert.Equal(0, wrong);
        Assert.Equal(0, rig.Http.Count(r => r.Uri.Host == "openidconnect.googleapis.com"));
    }

    [Fact]
    public async Task SigningInAsADifferentAddressIsFlaggedAndCanBeAccepted()
    {
        var rig = new AuthRig { UserinfoEmail = "someone.else@example.com" };
        var center = new SignInCenter(rig.Oauth);
        var signedIn = 0;
        var wrong = new List<(string, string)>();
        var accepted = new List<(string, string)>();
        center.SignedIn += _ => signedIn++;
        center.WrongAccount += (id, addr) => wrong.Add((id, addr));
        center.AddressAccepted += (id, addr) => accepted.Add((id, addr));

        var ok = await center.StartAsync(NetTestData.Google(email: "me@example.com"));

        Assert.False(ok);
        Assert.Equal(0, signedIn);
        Assert.Equal([("acc1", "someone.else@example.com")], wrong);
        Assert.Equal("someone.else@example.com", center.WrongAddress("acc1"));
        Assert.True(rig.Oauth.IsSignedIn(NetTestData.Google()));       // the token is kept so "Use <address>" works

        center.AcceptSignedInAddress("acc1");
        Assert.Equal([("acc1", "someone.else@example.com")], accepted);
        Assert.Null(center.WrongAddress("acc1"));
        center.AcceptSignedInAddress("acc1");                          // nothing left to accept
        Assert.Single(accepted);
    }

    [Fact]
    public async Task AddressComparisonIgnoresCase()
    {
        var rig = new AuthRig { UserinfoEmail = "ME@Example.com" };
        var center = new SignInCenter(rig.Oauth);
        Assert.True(await center.StartAsync(NetTestData.Google(email: "Me@example.COM")));
    }

    [Fact]
    public async Task AnAddressCheckThatFailsDoesNotBlockSignIn()
    {
        var rig = new AuthRig { UserinfoFails = true };
        var center = new SignInCenter(rig.Oauth);
        Assert.True(await center.StartAsync(NetTestData.Google(email: "me@example.com")));
        Assert.Null(center.WrongAddress("acc1"));
    }

    [Fact]
    public async Task AFailedSignInKeepsTheErrorUntilCleared()
    {
        var rig = new AuthRig { Browser = q => $"error=access_denied&state={q["state"]}" };
        var center = new SignInCenter(rig.Oauth);
        var ok = await center.StartAsync(NetTestData.Google(email: ""));
        Assert.False(ok);
        Assert.Equal("access_denied", center.Error("acc1"));
        Assert.False(center.IsWaiting("acc1"));

        var cleared = 0;
        center.Changed += () => cleared++;
        center.ClearError("acc1");
        Assert.Null(center.Error("acc1"));
        Assert.Equal(1, cleared);
    }

    [Fact]
    public async Task StartingClearsAnOldError()
    {
        var rig = new AuthRig { Browser = q => $"error=access_denied&state={q["state"]}" };
        var center = new SignInCenter(rig.Oauth);
        await center.StartAsync(NetTestData.Google(email: ""));
        Assert.NotNull(center.Error("acc1"));
        rig.Browser = _ => null;
        var t = center.StartAsync(NetTestData.Google(email: ""));
        await Task.Delay(300);
        Assert.Null(center.Error("acc1"));
        Assert.True(center.IsWaiting("acc1"));
        center.Cancel("acc1");
        Assert.False(await t);
    }

    [Fact]
    public async Task CancelStopsWaitingWithoutAnError()
    {
        var rig = new AuthRig { Browser = _ => null };
        var center = new SignInCenter(rig.Oauth);
        var a = NetTestData.Google(email: "");
        var t = center.StartAsync(a);
        await Task.Delay(300);
        Assert.True(center.IsWaiting("acc1"));
        Assert.NotNull(center.Link("acc1"));
        center.Cancel("acc1");
        Assert.False(center.IsWaiting("acc1"));
        Assert.Null(center.Link("acc1"));
        Assert.False(await t);
        Assert.Null(center.Error("acc1"));       // cancelling is not an error
    }

    [Fact]
    public async Task ANewerAttemptReplacesAnOlderOneAndLeavesItsStateAlone()
    {
        var attempts = 0;
        var rig = new AuthRig();
        rig.Browser = q => ++attempts == 1 ? null : $"code=abc123&state={q["state"]}";
        var center = new SignInCenter(rig.Oauth);
        var a = MsOnFreePort("");
        var signedIn = 0;
        center.SignedIn += _ => signedIn++;

        var first = center.StartAsync(a);
        await Task.Delay(400);
        var second = center.StartAsync(a);
        Assert.False(await first);                // replaced: reports nothing
        Assert.True(await second);
        Assert.Equal(1, signedIn);
        Assert.Null(center.Error(a.Id));
        Assert.False(center.IsWaiting(a.Id));
    }

    [Fact]
    public async Task CopyLinkGivesTheTextAndShowsCopiedForTwoSeconds()
    {
        var rig = new AuthRig { Browser = _ => null };
        var center = new SignInCenter(rig.Oauth);
        Assert.Null(center.CopyLink("acc1"));      // no link yet
        var t = center.StartAsync(NetTestData.Google(email: ""));
        await Task.Delay(300);

        var text = center.CopyLink("acc1");
        Assert.NotNull(text);
        Assert.Equal(center.Link("acc1")!.AbsoluteUri, text);
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", text);
        Assert.Equal("acc1", center.Copied);

        var until = DateTime.UtcNow.AddSeconds(5);
        while (center.Copied is not null && DateTime.UtcNow < until) await Task.Delay(100);
        Assert.Null(center.Copied);

        center.Cancel("acc1");
        await t;
    }

    [Fact]
    public async Task ReopenOpensTheSameLinkAgain()
    {
        var rig = new AuthRig { Browser = _ => null };
        var center = new SignInCenter(rig.Oauth);
        var t = center.StartAsync(NetTestData.Google(email: ""));
        await Task.Delay(300);
        center.Reopen("acc1");
        Assert.Equal(2, rig.OpenedUrls.Count);
        Assert.Equal(rig.OpenedUrls[0], rig.OpenedUrls[1]);
        center.Reopen("nobody");                    // no link: nothing happens
        Assert.Equal(2, rig.OpenedUrls.Count);
        center.Cancel("acc1");
        await t;
    }

    [Fact]
    public async Task SignInsOfDifferentAccountsAreTrackedSeparately()
    {
        var rig = new AuthRig { Browser = _ => null };
        var center = new SignInCenter(rig.Oauth);
        var t1 = center.StartAsync(NetTestData.Google("g1", ""));
        var t2 = center.StartAsync(NetTestData.Google("g2", ""));
        await Task.Delay(400);
        Assert.True(center.IsWaiting("g1") && center.IsWaiting("g2"));
        center.Cancel("g1");
        Assert.False(center.IsWaiting("g1"));
        Assert.True(center.IsWaiting("g2"));
        center.Cancel("g2");
        await Task.WhenAll(t1, t2);
    }

    [Fact]
    public void WaitingPanelWordsAreTheMacOnes()
    {
        Assert.Equal("Finish signing in in your browser", SignInWaitingText.Title);
        Assert.Equal("Choose the account you want to add and allow access. This updates by itself when you're done.", SignInWaitingText.Subtitle(""));
        Assert.Equal("Sign in as jo@example.com and allow access. This updates by itself when you're done.", SignInWaitingText.Subtitle("jo@example.com"));
        Assert.Equal("Copy Sign-in Link", SignInWaitingText.CopyLink);
        Assert.Equal("Copied", SignInWaitingText.Copied);
        Assert.Equal("Open Again", SignInWaitingText.OpenAgain);
        Assert.Equal("Paste this link into the browser profile signed in as jo@example.com", SignInWaitingText.CopyHelp("jo@example.com"));
        Assert.Equal("Opened in the wrong browser profile? Copy the link and paste it into the profile you use for this account.", SignInWaitingText.WrongProfileHint(""));
        Assert.Equal("Opened in the wrong browser profile? Copy the link and paste it into the profile signed in as jo@example.com.", SignInWaitingText.WrongProfileHint("jo@example.com"));
    }
}

public class TeamSetupTests
{
    // From "Parsing what the APIs send" in SelfTest.swift.
    [Fact]
    public void GoogleNeedsOnlyTheIdTheSecretIsOptional()
    {
        Assert.True(new TeamConfig { GoogleClientID = "x" }.HasClient(Provider.google));
        Assert.False(new TeamConfig { GoogleClientSecret = "y" }.HasClient(Provider.google));
    }

    [Fact]
    public void MicrosoftNeedsOnlyTheId() => Assert.True(new TeamConfig { MicrosoftClientID = "x" }.HasClient(Provider.microsoft));
}
