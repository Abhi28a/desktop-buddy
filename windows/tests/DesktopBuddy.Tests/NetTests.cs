using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DesktopBuddy.Core;

namespace DesktopBuddy.Tests;

// Tests for the network layer (Core/Net): ApiClient, date parsing, the Formspree feedback request.
// Everything goes through a fake HttpMessageHandler; nothing touches the real network.

/// One request the fake handler saw.
internal sealed record SeenRequest(HttpMethod Method, Uri Uri, string? Authorization, string Body, IReadOnlyDictionary<string, string> Headers)
{
    public Dictionary<string, string> Query => LoopbackReceiver.ParseQuery(Uri.Query);
    public string Path => Uri.AbsolutePath;
    public Dictionary<string, string> Form => LoopbackReceiver.ParseQuery("?" + Body);
}

/// Answers requests from a function and records them.
internal sealed class NetFakeHandler(Func<SeenRequest, (HttpStatusCode Status, string Body)> respond) : HttpMessageHandler
{
    private readonly object _lock = new();
    public List<SeenRequest> Requests { get; } = [];

    public NetFakeHandler(string json, HttpStatusCode status = HttpStatusCode.OK) : this(_ => (status, json)) { }

    public int Count(Func<SeenRequest, bool> match)
    {
        lock (_lock) return Requests.Count(match);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value));
        var seen = new SeenRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body, headers);
        lock (_lock) Requests.Add(seen);
        var (status, text) = respond(seen);
        return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    }
}

/// A token provider that always hands out the same token.
internal sealed class NetFakeTokens(string token = "tok-123") : ITokenProvider
{
    public Task<string> AccessTokenAsync(AccountConfig account, CancellationToken ct = default) => Task.FromResult(token);
}

internal static class NetTestData
{
    public static AccountConfig Google(string id = "acc1", string email = "me@example.com") =>
        new() { Id = id, Label = "Work", Email = email, Provider = Provider.google };

    public static AccountConfig Microsoft(string id = "acc2", string email = "me@example.com") =>
        new() { Id = id, Label = "Contoso", Email = email, Provider = Provider.microsoft };

    public static ApiClient Api(AccountConfig account, NetFakeHandler handler) =>
        new(account, new NetFakeTokens(), new HttpClient(handler));
}

public class ApiClientTests
{
    [Fact]
    public async Task GetSendsBearerTokenAndQuery()
    {
        var h = new NetFakeHandler("""{"ok":true}""");
        var api = NetTestData.Api(NetTestData.Google(), h);
        var json = await api.GetAsync("https://gmail.googleapis.com/gmail/v1/users/me/messages",
            new Dictionary<string, string> { ["q"] = "in:inbox is:unread newer_than:2d", ["maxResults"] = "25" });
        Assert.True(json.Bool("ok"));
        var req = Assert.Single(h.Requests);
        Assert.Equal("Bearer tok-123", req.Authorization);
        Assert.Equal("in:inbox is:unread newer_than:2d", req.Query["q"]);
        Assert.Equal("25", req.Query["maxResults"]);
        Assert.Equal("gmail.googleapis.com", req.Uri.Host);
    }

    [Fact]
    public async Task ExistingQueryInTheUrlIsKept()
    {
        var h = new NetFakeHandler("{}");
        var api = NetTestData.Api(NetTestData.Google(), h);
        await api.GetAsync("https://x.example.com/m?format=metadata&metadataHeaders=From", new Dictionary<string, string> { ["a"] = "b c" });
        Assert.Equal("https://x.example.com/m?format=metadata&metadataHeaders=From&a=b%20c", h.Requests[0].Uri.AbsoluteUri);
    }

    [Fact]
    public void ODataNamesKeepTheirDollarSign()
    {
        var url = ApiClient.BuildUrl("https://graph.microsoft.com/v1.0/me", [new("$select", "id,mail"), new("$top", "25")]);
        Assert.Equal("https://graph.microsoft.com/v1.0/me?$select=id%2Cmail&$top=25", url);
    }

    [Fact]
    public async Task ExtraHeadersAreSent()
    {
        var h = new NetFakeHandler("{}");
        var api = NetTestData.Api(NetTestData.Microsoft(), h);
        await api.GetAsync("https://graph.microsoft.com/v1.0/me/calendarView", headers: new Dictionary<string, string> { ["Prefer"] = "outlook.timezone=\"UTC\"" });
        Assert.Equal("outlook.timezone=\"UTC\"", h.Requests[0].Headers["Prefer"]);
    }

    [Fact]
    public async Task ApiErrorUsesTheProvidersMessage()
    {
        var h = new NetFakeHandler("""{"error":{"code":403,"message":"Gmail API has not been used in project 123 before. Enable it at https://console.developers.google.com/apis/api/gmail.googleapis.com/overview?project=123."}}""", HttpStatusCode.Forbidden);
        var api = NetTestData.Api(NetTestData.Google(), h);
        var e = await Assert.ThrowsAsync<ApiException>(() => api.GetAsync("https://gmail.googleapis.com/gmail/v1/users/me/labels"));
        Assert.Equal(403, e.Status);
        Assert.Equal("gmail.googleapis.com", e.Host);
        Assert.StartsWith("Gmail API has not been used", e.Message);
        // The console link in the message is offered as the fix (trailing full stop removed).
        Assert.Equal("https://console.developers.google.com/apis/api/gmail.googleapis.com/overview?project=123", e.FixUrl);
    }

    [Fact]
    public async Task ApiErrorFallsBackToCodeThenStatus()
    {
        var code = NetTestData.Api(NetTestData.Microsoft(), new NetFakeHandler("""{"error":{"code":"InvalidAuthenticationToken"}}""", HttpStatusCode.Unauthorized));
        var e1 = await Assert.ThrowsAsync<ApiException>(() => code.GetAsync("https://graph.microsoft.com/v1.0/me"));
        Assert.Equal("InvalidAuthenticationToken", e1.Message);

        var plain = NetTestData.Api(NetTestData.Microsoft(), new NetFakeHandler("<html>Bad gateway</html>", HttpStatusCode.BadGateway));
        var e2 = await Assert.ThrowsAsync<ApiException>(() => plain.GetAsync("https://graph.microsoft.com/v1.0/me"));
        Assert.Equal(502, e2.Status);
        Assert.Equal("HTTP 502", e2.Message);
    }

    [Fact]
    public async Task NonJsonSuccessBodyIsAnEmptyObject()
    {
        var api = NetTestData.Api(NetTestData.Google(), new NetFakeHandler("not json"));
        var json = await api.GetAsync("https://x.example.com/");
        Assert.Empty(json);
    }

    [Fact]
    public void FixUrlTrimsPunctuationAndIgnoresMessagesWithoutALink()
    {
        Assert.Equal("https://console.cloud.google.com/apis/api/chat.googleapis.com/overview?project=1",
            new ApiException(403, "chat.googleapis.com", "Enable it: (https://console.cloud.google.com/apis/api/chat.googleapis.com/overview?project=1),").FixUrl);
        Assert.Null(new ApiException(404, "x", "Not found").FixUrl);
    }

    [Fact]
    public async Task NetworkFailureComesThroughAsAnHttpException()
    {
        var h = new NetFakeHandler(_ => throw new HttpRequestException("No such host is known"));
        var api = NetTestData.Api(NetTestData.Google(), h);
        await Assert.ThrowsAsync<HttpRequestException>(() => api.GetAsync("https://x.example.com/"));
    }

    [Fact]
    public async Task ATokenProviderThatIsNotSignedInStopsTheRequest()
    {
        var oauth = new OAuth(new MemorySecretStore(), () => new TeamConfig { GoogleClientID = "x" }, new HttpClient(new NetFakeHandler("{}")));
        var h = new NetFakeHandler("{}");
        var api = new ApiClient(NetTestData.Google(), oauth, new HttpClient(h));
        var e = await Assert.ThrowsAsync<AuthException>(() => api.GetAsync("https://x.example.com/"));
        Assert.Equal(AuthErrorKind.NotSignedIn, e.Kind);
        Assert.Empty(h.Requests);
    }
}

public class DateParsingTests
{
    [Fact]
    public void ParseDateReadsIsoWithAndWithoutFractionsAndOffsets()
    {
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 9, 15, 23, TimeSpan.Zero), ApiDates.ParseDate("2026-10-21T09:15:23Z"));
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 9, 15, 23, 123, TimeSpan.Zero), ApiDates.ParseDate("2026-10-21T09:15:23.123Z"));
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 8, 0, 0, TimeSpan.Zero), ApiDates.ParseDate("2026-10-21T09:00:00+01:00"));
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero), ApiDates.ParseDate("2026-10-21T00:00:00.000Z"));
    }

    [Fact]
    public void ParseDateRejectsWhatIsNotATimestamp()
    {
        Assert.Null(ApiDates.ParseDate(null));
        Assert.Null(ApiDates.ParseDate(""));
        Assert.Null(ApiDates.ParseDate("2026-10-21"));          // all-day events have a date only
        Assert.Null(ApiDates.ParseDate("yesterday"));
        Assert.Null(ApiDates.ParseDate("2026-10-21T09:15:23"));  // no time zone
    }

    [Fact]
    public void GraphDateWith7DigitFraction() => Assert.NotNull(ApiDates.GraphDate("2026-10-21T09:15:23.1234567Z"));

    [Fact]
    public void GraphDateWithoutZIsUtc() => Assert.NotNull(ApiDates.GraphDate("2026-10-21T09:15:23.0000000"));

    [Fact]
    public void GraphUtcDateIsNotShifted()
    {
        Assert.Equal(ApiDates.ParseDate("2026-10-21T09:15:00Z"), ApiDates.GraphDate("2026-10-21T09:15:00.0000000"));
        Assert.Equal(TimeSpan.Zero, ApiDates.GraphDate("2026-10-21T09:15:00.0000000")!.Value.Offset);
    }

    [Fact]
    public void GraphDateKeepsMillisecondsAndNullStaysNull()
    {
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 9, 15, 23, 456, TimeSpan.Zero), ApiDates.GraphDate("2026-10-21T09:15:23.4567891"));
        Assert.Null(ApiDates.GraphDate(null));
        Assert.Null(ApiDates.GraphDate("not a date"));
    }

    [Fact]
    public void IsoPlainIsUtcWholeSeconds()
    {
        var d = new DateTimeOffset(2026, 11, 14, 11, 0, 0, 900, TimeSpan.FromHours(1));
        Assert.Equal("2026-11-14T10:00:00Z", ApiDates.IsoPlain(d));
    }

    [Fact]
    public void WhenTextTruncatesTowardZero()
    {
        var now = new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);
        Assert.Equal("Starts in 5 min", ApiDates.WhenText(now.AddMinutes(5).AddSeconds(30), now));
        Assert.Equal("Starting now", ApiDates.WhenText(now.AddSeconds(50), now));
        Assert.Equal("Starting now", ApiDates.WhenText(now.AddSeconds(-50), now));
        Assert.Equal("Started 3 min ago", ApiDates.WhenText(now.AddMinutes(-3).AddSeconds(-10), now));
    }
}

public class ApiTextTests
{
    [Fact]
    public void StripHtmlRemovesTagsAndDecodesEntities() =>
        Assert.Equal("Hi <team> & co", ApiText.StripHtml("<div><p>Hi&nbsp;&lt;team&gt; &amp; co</p></div>\n"));

    [Fact]
    public void GmailSnippetEntitiesAreDecoded() =>
        Assert.Equal("It's \"ready\" & <set>", ApiText.DecodeEntities("It&#39;s &quot;ready&quot; &amp; &lt;set&gt;"));

    [Fact]
    public void TrimSpacesKeepsLineBreaks() =>
        Assert.Equal("a\n", ApiText.TrimSpaces(" \t a\n "));

    [Fact]
    public void LabelsSortLikeFinder()
    {
        var names = new List<string> { "Label 10", "label 2", "Label 1", "Alpha", "beta" };
        names.Sort(ApiText.LocalizedStandardCompare);
        Assert.Equal(new[] { "Alpha", "beta", "Label 1", "label 2", "Label 10" }, names);
    }
}

public class FeedbackTests
{
    private static readonly TeamConfig Team = new() { FeedbackFormID = "abcd1234", FeedbackEmail = "reports@example.com" };

    [Fact]
    public void AnEmptyReportCannotBeSentAWrittenOneCan()
    {
        var r = new FeedbackReport();
        Assert.False(r.IsReady);
        r.Message = "The logo didn't update after renaming a client.\nSecond line";
        Assert.True(r.IsReady);
        r.Message = "  hey  ";
        Assert.False(r.IsReady);
    }

    [Fact]
    public void SubjectSaysWhatKindAndStartsWithTheFirstLine()
    {
        var r = new FeedbackReport { Message = "The logo didn't update after renaming a client.\nSecond line" };
        Assert.Equal("Desktop Buddy Bug: The logo didn't update after renaming a client.", FeedbackSender.Subject(r));
    }

    [Fact]
    public void SubjectSkipsLeadingBlankLines()
    {
        var r = new FeedbackReport { Message = "\n\nFirst real line\nSecond" };
        Assert.Equal("Desktop Buddy Bug: First real line", FeedbackSender.Subject(r));
    }

    [Fact]
    public void LongSubjectsAreShortened()
    {
        var r = new FeedbackReport { Kind = FeedbackKind.Idea, Message = new string('a', 100) };
        var s = FeedbackSender.Subject(r);
        Assert.True(s.Length < 90);
        Assert.EndsWith("…", s);
        Assert.StartsWith("Desktop Buddy Idea: ", s);
    }

    [Fact]
    public void ABadReplyAddressIsCaught()
    {
        var r = new FeedbackReport { ReplyTo = "not an email" };
        Assert.False(r.ReplyIsValid);
        r.ReplyTo = "jo@client.example.com";
        Assert.True(r.ReplyIsValid);
        r.ReplyTo = "";
        Assert.True(r.ReplyIsValid);
    }

    [Fact]
    public void ThePayloadHasSubjectKindMessageAndReplyAddress()
    {
        var r = new FeedbackReport { Kind = FeedbackKind.Idea, Message = new string('a', 100), ReplyTo = "jo@client.example.com" };
        var p = FeedbackSender.Payload(r);
        Assert.StartsWith("Desktop Buddy Idea", p["_subject"]);
        Assert.Equal("Idea or fix", p["type"]);
        Assert.Contains("aaaa", p["message"]);
        Assert.Equal("jo@client.example.com", p["email"]);
        Assert.Contains("Reply to: jo@client.example.com", p["message"]);
    }

    [Fact]
    public void NoReplyAddressMeansNoEmailField()
    {
        var p = FeedbackSender.Payload(new FeedbackReport { Message = "Something broke" });
        Assert.False(p.ContainsKey("email"));
        Assert.Equal("Something's wrong", p["type"]);
    }

    [Fact]
    public void TheFormAddressIsBuiltFromTheFormId() =>
        Assert.Equal("https://formspree.io/f/abcd1234", FeedbackSender.Endpoint("abcd1234"));

    [Fact]
    public void AFormIdCannotChangeTheAddress() =>
        Assert.Equal("https://formspree.io/f/a%2F..%2Fb", FeedbackSender.Endpoint("a/../b"));

    [Fact]
    public void AppAndWindowsVersionAreAddedWhenAllowedAndLeftOutWhenNot()
    {
        var r = new FeedbackReport { Message = "Something broke" };
        Assert.Contains("Windows", FeedbackSender.Body(r));
        Assert.Contains("Desktop Buddy", FeedbackSender.Body(r));
        r.IncludeDetails = false;
        Assert.DoesNotContain("Windows", FeedbackSender.Body(r));
        Assert.DoesNotContain("Contract", FeedbackSender.Body(r));     // notifications are never included
    }

    [Fact]
    public void DetailsCountAccounts()
    {
        Assert.EndsWith("1 Google, 0 Microsoft account", FeedbackSender.Details(1, 0));
        Assert.EndsWith("2 Google, 1 Microsoft accounts", FeedbackSender.Details(2, 1));
    }

    [Fact]
    public void ReportingNeedsAFormOrAnEmail()
    {
        Assert.True(FeedbackSender.IsSetUp(Team));
        Assert.True(FeedbackSender.IsSetUp(new TeamConfig { FeedbackEmail = "a@example.com" }));
        Assert.False(FeedbackSender.IsSetUp(new TeamConfig { FeedbackFormID = "" }));
        Assert.Null(FeedbackSender.Key(new TeamConfig { FeedbackEmail = "a@example.com" }));
    }

    [Fact]
    public void TheMailFallbackCarriesTheReport()
    {
        var r = new FeedbackReport { Message = "Something broke", IncludeDetails = false };
        var url = FeedbackSender.MailUrl(r, team: Team)!;
        Assert.StartsWith("mailto:reports@example.com?subject=", url);
        Assert.Contains("Desktop%20Buddy%20Bug", url);
        Assert.Null(FeedbackSender.MailUrl(r, team: new TeamConfig { FeedbackFormID = "x" }));
    }

    [Fact]
    public async Task SendPostsJsonToFormspree()
    {
        var h = new NetFakeHandler("""{"ok":true}""");
        var r = new FeedbackReport { Message = "Something broke", ReplyTo = "jo@client.example.com" };
        await FeedbackSender.SendAsync(r, "Desktop Buddy 2.4.0 · Windows 10.0.1", Team, new HttpClient(h));
        var req = Assert.Single(h.Requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("https://formspree.io/f/abcd1234", req.Uri.AbsoluteUri);
        Assert.Null(req.Authorization);       // nothing identifying is sent
        var body = JsonNode.Parse(req.Body)!;
        Assert.Equal("jo@client.example.com", body["email"]!.GetValue<string>());
        Assert.Contains("Something broke", body["message"]!.GetValue<string>());
        Assert.Contains("Windows 10.0.1", body["message"]!.GetValue<string>());
        Assert.Equal(new[] { "_subject", "email", "message", "type" }, body.AsObject().Select(p => p.Key).Order().ToArray());
    }

    [Fact]
    public async Task SendWithoutAFormIdSaysNotSetUp()
    {
        var e = await Assert.ThrowsAsync<FeedbackException>(() =>
            FeedbackSender.SendAsync(new FeedbackReport { Message = "Something broke" }, team: new TeamConfig { FeedbackEmail = "a@example.com" }, http: new HttpClient(new NetFakeHandler("{}"))));
        Assert.Equal(FeedbackFailureKind.NotSetUp, e.Kind);
        Assert.Equal("Reporting isn't set up in this copy of Desktop Buddy.", e.Message);
    }

    [Fact]
    public async Task ARejectedReportShowsTheServiceMessage()
    {
        var h = new NetFakeHandler("""{"error":"Form not found"}""", HttpStatusCode.UnprocessableEntity);
        var e = await Assert.ThrowsAsync<FeedbackException>(() =>
            FeedbackSender.SendAsync(new FeedbackReport { Message = "Something broke" }, team: Team, http: new HttpClient(h)));
        Assert.Equal(FeedbackFailureKind.Rejected, e.Kind);
        Assert.Equal("The report wasn't accepted: Form not found", e.Message);
    }

    [Fact]
    public async Task OkFalseIsRejectedEvenWithA200()
    {
        var h = new NetFakeHandler("{\"ok\":false}");
        var e = await Assert.ThrowsAsync<FeedbackException>(() =>
            FeedbackSender.SendAsync(new FeedbackReport { Message = "Something broke" }, team: Team, http: new HttpClient(h)));
        Assert.Equal("The report wasn't accepted: unknown reply", e.Message);
    }

    [Fact]
    public async Task NoInternetSaysSo()
    {
        var h = new NetFakeHandler(_ => throw new HttpRequestException("down"));
        var e = await Assert.ThrowsAsync<FeedbackException>(() =>
            FeedbackSender.SendAsync(new FeedbackReport { Message = "Something broke" }, team: Team, http: new HttpClient(h)));
        Assert.Equal(FeedbackFailureKind.Offline, e.Kind);
        Assert.Equal("Couldn't reach the internet. Check your connection and try again.", e.Message);
    }
}
