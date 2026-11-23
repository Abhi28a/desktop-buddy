using System.Net;
using DesktopBuddy.Core;

namespace DesktopBuddy.Tests;

// Tests for the Google and Microsoft sources (Core/Sources): each one's requests, paging limits and how it
// builds items from realistic sample JSON (made-up people at example.com), plus the live gateway's dispatch.

/// Answers GETs by the first matching route; unknown requests get a 404 like a real API would.
internal sealed class Routes
{
    private readonly List<(Func<SeenRequest, bool> Match, Func<SeenRequest, string> Body, HttpStatusCode Status)> _routes = [];
    public NetFakeHandler Handler { get; }

    public Routes() => Handler = new NetFakeHandler(Respond);

    public Routes On(Func<SeenRequest, bool> match, string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        _routes.Add((match, _ => json, status));
        return this;
    }

    public Routes On(Func<SeenRequest, bool> match, Func<SeenRequest, string> json)
    {
        _routes.Add((match, json, HttpStatusCode.OK));
        return this;
    }

    public Routes OnPath(string path, string json, HttpStatusCode status = HttpStatusCode.OK) => On(r => r.Path == path, json, status);

    private (HttpStatusCode, string) Respond(SeenRequest r)
    {
        lock (_routes)
        {
            foreach (var (match, body, status) in _routes)
                if (match(r)) return (status, body(r));
        }
        return (HttpStatusCode.NotFound, """{"error":{"message":"no route for """ + r.Uri.PathAndQuery.Replace("\"", "'") + "\"}}");
    }

    public ApiClient Api(AccountConfig a) => NetTestData.Api(a, Handler);
}

public class GmailSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    // Port of "Only mail with chosen labels" (Gmail half) in SelfTest.swift.
    [Fact]
    public void DefaultQueryIsUnreadInboxMailFromTheLastTwoDays()
    {
        var g = NetTestData.Google();
        var q = GmailSource.ListQueries(g);
        Assert.Equal("in:inbox is:unread newer_than:2d", Assert.Single(q).Query["q"]);
        Assert.Equal("25", q[0].Query["maxResults"]);
        Assert.Null(q[0].Label);
    }

    [Fact]
    public void LabelsChosenButNonePickedMeansNoMail()
    {
        var g = NetTestData.Google();
        g.OnlyLabelsList = [];
        Assert.Empty(GmailSource.ListQueries(g));
    }

    [Fact]
    public void OneQueryPerLabelFiledAnywhereAndNamedForErrors()
    {
        var g = NetTestData.Google();
        g.OnlyLabels = [new MailLabel { Id = "Label_1", Name = "Urgent" }, new MailLabel { Id = "Label_2", Name = "Client/Acme" }];
        var q = GmailSource.ListQueries(g);
        Assert.Equal(["Label_1", "Label_2"], q.Select(x => x.Query["labelIds"]));
        Assert.All(q, x => Assert.DoesNotContain("in:inbox", x.Query["q"]));
        Assert.Equal(["Urgent", "Client/Acme"], q.Select(x => x.Label));
    }

    private const string Message1 = """
        {"id":"m1","threadId":"t-77","internalDate":"1792576800000","snippet":"It&#39;s ready &amp; waiting for &quot;review&quot;",
         "payload":{"headers":[{"name":"Subject","value":"Quarterly plan"},{"name":"from","value":"\"Priya Shah\" <Priya@Example.com>"}]}}
        """;

    private const string Message3 = """{"id":"m3","payload":{"headers":[]}}""";

    private static Routes MailRoutes() => new Routes()
        .OnPath("/gmail/v1/users/me/messages", """{"messages":[{"id":"m1"},{"id":"m2"},{"id":"m3"},{"id":"m1"}],"resultSizeEstimate":4}""")
        .OnPath("/gmail/v1/users/me/messages/m1", Message1)
        .OnPath("/gmail/v1/users/me/messages/m2", """{"id":"m2","payload":{"headers":[{"name":"Subject","value":"Second"}]}}""")
        .OnPath("/gmail/v1/users/me/messages/m3", Message3);

    [Fact]
    public async Task BuildsItemsForNewMailAndKeepsEveryIdLive()
    {
        var routes = MailRoutes();
        var known = new HashSet<string> { "mail:acc1:m2" };     // already on screen (or muted): not fetched again
        var result = await GmailSource.FetchAsync(routes.Api(NetTestData.Google()), known, Now);

        Assert.Equal(ItemSource.mail, result.Source);
        Assert.Equal(new HashSet<string> { "mail:acc1:m1", "mail:acc1:m2", "mail:acc1:m3" }, result.LiveIDs);
        Assert.Equal(["mail:acc1:m1", "mail:acc1:m3"], result.Items.Select(i => i.Id));
        Assert.Equal(0, routes.Handler.Count(r => r.Path.EndsWith("/m2")));

        var m1 = result.Items[0];
        Assert.Equal(ItemSource.mail, m1.Source);
        Assert.Equal("acc1", m1.AccountID);
        Assert.Equal("Work", m1.AccountLabel);
        Assert.Equal("Quarterly plan", m1.Title);
        Assert.Equal("Priya Shah", m1.From);
        Assert.Equal("priya@example.com", m1.FromAddress);
        Assert.Equal("It's ready & waiting for \"review\"", m1.Detail);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1792576800000), m1.Date);
        Assert.Equal("https://mail.google.com/mail/u/?authuser=me@example.com#all/t-77", m1.Url);
        Assert.Equal("acc1", m1.AccountID);
        Assert.Null(m1.ChatRef);
    }

    [Fact]
    public async Task AMessageWithNothingInItStillGetsAnItem()
    {
        var result = await GmailSource.FetchAsync(MailRoutes().Api(NetTestData.Google()), null, Now);
        var m3 = result.Items.Single(i => i.Id.EndsWith(":m3"));
        Assert.Equal("(no subject)", m3.Title);
        Assert.Equal("Unknown sender", m3.From);
        Assert.Null(m3.Detail);
        Assert.Equal(Now, m3.Date);                                // no internalDate: now
        Assert.EndsWith("#all/m3", m3.Url);                        // no thread: the message id
        Assert.Equal("", m3.FromAddress);
    }

    [Fact]
    public async Task ListsWithTheInboxQueryAndFetchesOnlyMetadata()
    {
        var routes = MailRoutes();
        await GmailSource.FetchAsync(routes.Api(NetTestData.Google()), null, Now);
        var list = routes.Handler.Requests.First(r => r.Path == "/gmail/v1/users/me/messages");
        Assert.Equal("in:inbox is:unread newer_than:2d", list.Query["q"]);
        Assert.Equal("25", list.Query["maxResults"]);
        var get = routes.Handler.Requests.First(r => r.Path.EndsWith("/m1"));
        Assert.Contains("format=metadata", get.Uri.Query);
        Assert.Contains("metadataHeaders=From", get.Uri.Query);
        Assert.Contains("metadataHeaders=Subject", get.Uri.Query);
    }

    [Fact]
    public async Task LabelQueriesAreMergedWithoutDuplicates()
    {
        var routes = new Routes()
            .On(r => r.Path == "/gmail/v1/users/me/messages" && r.Query["labelIds"] == "Label_1", """{"messages":[{"id":"a"}]}""")
            .On(r => r.Path == "/gmail/v1/users/me/messages" && r.Query["labelIds"] == "Label_2", """{"messages":[{"id":"a"},{"id":"b"}]}""")
            .On(r => r.Path.StartsWith("/gmail/v1/users/me/messages/"), """{"id":"x","payload":{"headers":[{"name":"Subject","value":"S"}]}}""");
        var g = NetTestData.Google();
        g.OnlyLabels = [new MailLabel { Id = "Label_1", Name = "Urgent" }, new MailLabel { Id = "Label_2", Name = "Client/Acme" }];
        var result = await GmailSource.FetchAsync(routes.Api(g), null, Now);
        Assert.Equal(["mail:acc1:a", "mail:acc1:b"], result.Items.Select(i => i.Id));
        Assert.Equal(1, routes.Handler.Count(r => r.Path.EndsWith("/messages/a")));
        Assert.Equal("is:unread newer_than:2d", routes.Handler.Requests[0].Query["q"]);
    }

    [Fact]
    public async Task AMissingLabelSaysSoInPlainWords()
    {
        var routes = new Routes().OnPath("/gmail/v1/users/me/messages", """{"error":{"code":400,"message":"Invalid label: Label_9"}}""", HttpStatusCode.BadRequest);
        var g = NetTestData.Google();
        g.OnlyLabels = [new MailLabel { Id = "Label_9", Name = "Old project" }];
        var e = await Assert.ThrowsAsync<ApiException>(() => GmailSource.FetchAsync(routes.Api(g), null, Now));
        Assert.Equal(400, e.Status);
        Assert.Equal("The label “Old project” no longer exists in Gmail. Remove it under Mail.", e.Message);
    }

    [Fact]
    public async Task AnInboxErrorKeepsTheProvidersMessage()
    {
        var routes = new Routes().OnPath("/gmail/v1/users/me/messages", """{"error":{"message":"Not found"}}""", HttpStatusCode.NotFound);
        var e = await Assert.ThrowsAsync<ApiException>(() => GmailSource.FetchAsync(routes.Api(NetTestData.Google()), null, Now));
        Assert.Equal("Not found", e.Message);
    }

    [Fact]
    public async Task LabelsAreYourOwnOnesSortedLikeFinder()
    {
        var routes = new Routes().OnPath("/gmail/v1/users/me/labels", """
            {"labels":[{"id":"INBOX","name":"INBOX","type":"system"},{"id":"Label_10","name":"Project 10","type":"user"},
                       {"id":"Label_2","name":"project 2","type":"user"},{"id":"Label_1","name":"Archive","type":"user"},{"id":"x","type":"user"}]}
            """);
        var labels = await GmailSource.LabelsAsync(routes.Api(NetTestData.Google()));
        Assert.Equal(["Archive", "project 2", "Project 10"], labels.Select(l => l.Name));
        Assert.Equal("Label_2", labels[1].Id);
    }

    // Port of "Parsing what the APIs send" (sender address) in SelfTest.swift.
    [Fact]
    public void SenderAddressWithNameIsLowercased() =>
        Assert.Equal("priya@gatehouse.example.com", GmailSource.SenderAddress("Priya Shah <Priya@Gatehouse.example.com>"));

    [Fact]
    public void SenderAddressWithoutNameIsKept() =>
        Assert.Equal("ops@example.com", GmailSource.SenderAddress("ops@example.com"));

    [Fact]
    public void SenderAddressOddShapes()
    {
        Assert.Equal("ops@example.com", GmailSource.SenderAddress("  OPS@example.com\t"));
        Assert.Equal("", GmailSource.SenderAddress(""));
        Assert.Equal("a@example.com", GmailSource.SenderAddress("<A@example.com>"));
        Assert.Equal("b <c", GmailSource.SenderAddress("b <c"));          // no closing bracket: whole text
    }

    [Fact]
    public void CleanSenderShowsTheNameOrTheAddress()
    {
        Assert.Equal("Priya Shah", GmailSource.CleanSender("Priya Shah <priya@example.com>"));
        Assert.Equal("Priya Shah", GmailSource.CleanSender("\"Priya Shah\" <priya@example.com>"));
        Assert.Equal("priya@example.com", GmailSource.CleanSender("<priya@example.com>"));
        Assert.Equal("priya@example.com", GmailSource.CleanSender("priya@example.com"));
    }
}

public class ChatSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    private static string Iso(DateTimeOffset d) => ApiDates.IsoPlain(d);

    private static Routes ChatRoutes() => new Routes()
        .OnPath("/v1/userinfo", """{"sub":"111","email":"me@example.com"}""")
        .On(r => r.Path == "/v1/spaces" && !r.Query.ContainsKey("pageToken"),
            $$"""
            {"spaces":[{"name":"spaces/AAA","spaceType":"SPACE","displayName":"Project Phoenix","lastActiveTime":"{{Iso(Now.AddMinutes(-30))}}"},
                           {"name":"spaces/BBB","spaceType":"SPACE","displayName":"Quiet room","lastActiveTime":"{{Iso(Now.AddHours(-5))}}"}],
               "nextPageToken":"p2"}
            """)
        .On(r => r.Path == "/v1/spaces" && r.Query.GetValueOrDefault("pageToken") == "p2",
            $$"""
            {"spaces":[{"name":"spaces/CCC","spaceType":"DIRECT_MESSAGE","lastActiveTime":"{{Iso(Now.AddMinutes(-5))}}"}]}
            """)
        .OnPath("/v1/spaces/AAA/messages", $$"""
            {"messages":[
              {"name":"spaces/AAA/messages/m1","sender":{"name":"users/222","displayName":"Priya Shah"},"text":"Hello team","createTime":"{{Iso(Now.AddMinutes(-20))}}"},
              {"name":"spaces/AAA/messages/m2","sender":{"name":"users/111","displayName":"Me"},"text":"my own message","createTime":"{{Iso(Now.AddMinutes(-19))}}"},
              {"name":"spaces/AAA/messages/m3","sender":{"name":"users/333"},"formattedText":"<b>hi</b>","createTime":"{{Iso(Now.AddMinutes(-18))}}"}]}
            """)
        .OnPath("/v1/spaces/BBB/messages", "{}")
        .OnPath("/v1/spaces/CCC/messages", $$"""
            {"messages":[{"name":"spaces/CCC/messages/m4","sender":{"name":"users/333"},"createTime":"{{Iso(Now.AddMinutes(-4))}}"}]}
            """)
        .OnPath("/v1/people/333", """{"names":[{"displayName":"Sam Lee"}],"emailAddresses":[{"value":"sam@example.com"}]}""");

    [Fact]
    public async Task BuildsChatItemsFromOthersInActiveSpacesAndPagesThroughSpaces()
    {
        var routes = ChatRoutes();
        var chat = new ChatSource();
        var result = await chat.FetchAsync(routes.Api(NetTestData.Google()), ["chat:acc1:spaces/OLD/messages/z"], Now);

        Assert.Equal(ItemSource.chat, result.Source);
        Assert.Equal(["chat:acc1:spaces/AAA/messages/m1", "chat:acc1:spaces/AAA/messages/m3", "chat:acc1:spaces/CCC/messages/m4"],
            result.Items.Select(i => i.Id));

        var m1 = result.Items[0];
        Assert.Equal("Project Phoenix", m1.Title);
        Assert.Equal("Priya Shah", m1.From);
        Assert.Equal("Hello team", m1.Detail);
        Assert.Equal("spaces/AAA", m1.ChatRef);
        Assert.Equal(Now.AddMinutes(-20), m1.Date);
        Assert.Equal("https://mail.google.com/chat/u/?authuser=me@example.com#chat/space/AAA", m1.Url);
        Assert.Equal(ItemSource.chat, m1.Source);

        Assert.Equal("Sam Lee", result.Items[1].From);                 // no name in the message: People API
        Assert.Equal("<b>hi</b>", result.Items[1].Detail);              // formattedText when there is no text
        Assert.Equal("Direct message", result.Items[2].Title);
        Assert.Equal("(attachment)", result.Items[2].Detail);
        Assert.Equal(1, routes.Handler.Count(r => r.Path == "/v1/people/333"));   // the name is cached

        // Chat has no unread flag: what is on screen stays live, plus the new items.
        Assert.Equal(new HashSet<string>(["chat:acc1:spaces/OLD/messages/z", .. result.Items.Select(i => i.Id)]), result.LiveIDs);
    }

    [Fact]
    public async Task FirstRunLooksBackTwoHoursAndSkipsQuietSpaces()
    {
        var routes = ChatRoutes();
        await new ChatSource().FetchAsync(routes.Api(NetTestData.Google()), null, Now);
        var spacePage = routes.Handler.Requests.First(r => r.Path == "/v1/spaces");
        Assert.Equal("200", spacePage.Query["pageSize"]);
        var messages = routes.Handler.Requests.First(r => r.Path == "/v1/spaces/AAA/messages");
        Assert.Equal($"createTime > \"{Iso(Now.AddHours(-2))}\"", messages.Query["filter"]);
        Assert.Equal("20", messages.Query["pageSize"]);
        Assert.Equal("createTime desc", messages.Query["orderBy"]);
        Assert.Equal(0, routes.Handler.Count(r => r.Path == "/v1/spaces/BBB/messages"));     // last active 5 hours ago
    }

    [Fact]
    public async Task NextCheckStartsWhereTheLastOneEndedAndCachesTheUserId()
    {
        var routes = ChatRoutes();
        var chat = new ChatSource();
        var api = routes.Api(NetTestData.Google());
        await chat.FetchAsync(api, null, Now);
        var second = await chat.FetchAsync(api, ["keep"], Now.AddMinutes(1));

        Assert.Empty(second.Items);                                  // nothing active since the last check
        Assert.Equal(["keep"], second.LiveIDs);
        Assert.Equal(1, routes.Handler.Count(r => r.Path == "/v1/userinfo"));
        Assert.Equal(2, routes.Handler.Count(r => r.Path == "/v1/spaces" && !r.Query.ContainsKey("pageToken")));
        // A new space message after the first check would be asked for from the first check's time.
        var routes2 = ChatRoutes();
        var chat2 = new ChatSource();
        await chat2.FetchAsync(routes2.Api(NetTestData.Google()), null, Now.AddHours(-3).AddMinutes(-30));    // first check long ago
        await chat2.FetchAsync(routes2.Api(NetTestData.Google()), null, Now);
        var asked = routes2.Handler.Requests.Where(r => r.Path == "/v1/spaces/AAA/messages").Select(r => r.Query["filter"]).ToList();
        Assert.Contains($"createTime > \"{Iso(Now.AddHours(-3).AddMinutes(-30))}\"", asked);
    }

    [Fact]
    public async Task TwoAccountsKeepTheirOwnClocks()
    {
        var routes = ChatRoutes();
        var chat = new ChatSource();
        await chat.FetchAsync(routes.Api(NetTestData.Google("a1")), null, Now);
        routes.Handler.Requests.Clear();
        await chat.FetchAsync(routes.Api(NetTestData.Google("a2")), null, Now.AddMinutes(1));       // first run for a2: two hours back
        var filter = routes.Handler.Requests.First(r => r.Path == "/v1/spaces/AAA/messages").Query["filter"];
        Assert.Equal($"createTime > \"{Iso(Now.AddMinutes(1).AddHours(-2))}\"", filter);
    }

    [Fact]
    public async Task APeopleApiFailureFallsBackToThePlaceholderName()
    {
        var routes = ChatRoutes();
        routes = new Routes()
            .OnPath("/v1/userinfo", """{"sub":"111"}""")
            .OnPath("/v1/spaces", $$"""
            {"spaces":[{"name":"spaces/AAA","spaceType":"SPACE","displayName":"Room","lastActiveTime":"{{Iso(Now.AddMinutes(-1))}}"}]}
            """)
            .OnPath("/v1/spaces/AAA/messages", $$"""{"messages":[{"name":"spaces/AAA/messages/m1","sender":{"name":"users/9"},"text":"hi","createTime":"{{Iso(Now)}}"}]}""")
            .OnPath("/v1/people/9", """{"error":{"message":"People API has not been used"}}""", HttpStatusCode.Forbidden);
        var result = await new ChatSource().FetchAsync(routes.Api(NetTestData.Google()), null, Now);
        Assert.Equal("Someone (enable People API for names)", Assert.Single(result.Items).From);
    }

    [Fact]
    public async Task APeopleApiAnswerWithOnlyAnEmailUsesTheEmail()
    {
        var routes = new Routes()
            .OnPath("/v1/userinfo", """{"sub":"111"}""")
            .OnPath("/v1/spaces", $$"""
            {"spaces":[{"name":"spaces/AAA","spaceType":"SPACE","lastActiveTime":"{{Iso(Now.AddMinutes(-1))}}"}]}
            """)
            .OnPath("/v1/spaces/AAA/messages", $$"""{"messages":[{"name":"spaces/AAA/messages/m1","sender":{"name":"users/9"},"text":"hi","createTime":"{{Iso(Now)}}"}]}""")
            .OnPath("/v1/people/9", """{"emailAddresses":[{"value":"sam@example.com"}]}""");
        var result = await new ChatSource().FetchAsync(routes.Api(NetTestData.Google()), null, Now);
        var item = Assert.Single(result.Items);
        Assert.Equal("sam@example.com", item.From);
        Assert.Equal("Chat", item.Title);                          // a space with no name
    }
}

public class CalendarSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    private const string Events = """
        {"items":[
          {"id":"e1","summary":"Design sync","start":{"dateTime":"2026-11-14T10:05:00Z"},"hangoutLink":"https://meet.google.com/abc-defg-hij",
           "htmlLink":"https://calendar.google.com/event?eid=e1","organizer":{"displayName":"Priya Shah","email":"priya@example.com"}},
          {"id":"e2","summary":"Public holiday","start":{"date":"2026-11-14"}},
          {"id":"e3","summary":"Declined one","start":{"dateTime":"2026-11-14T10:02:00Z"},
           "attendees":[{"email":"me@example.com","self":true,"responseStatus":"declined"}]},
          {"id":"e4","summary":"Long gone","start":{"dateTime":"2026-11-14T09:45:00Z"}},
          {"id":"e5","summary":"Stand-up","start":{"dateTime":"2026-11-14T11:57:00+02:00"},
           "htmlLink":"https://calendar.google.com/event?eid=e5","organizer":{"email":"ops@example.com"}},
          {"id":"e6","start":{"dateTime":"2026-11-14T10:00:00Z"},
           "attendees":[{"email":"me@example.com","self":true,"responseStatus":"accepted"},{"email":"x@example.com","responseStatus":"declined"}]}
        ]}
        """;

    [Fact]
    public async Task OnlyMeetingsAboutToStartBecomeItems()
    {
        var routes = new Routes().OnPath("/calendar/v3/calendars/primary/events", Events);
        var result = await CalendarSource.FetchAsync(routes.Api(NetTestData.Google()), 10, Now);

        Assert.Equal(ItemSource.meeting, result.Source);
        Assert.Equal(["e1", "e5", "e6"], result.Items.Select(i => i.Id.Split(':')[2]));
        Assert.Equal(new HashSet<string>(result.Items.Select(i => i.Id)), result.LiveIDs);

        var e1 = result.Items[0];
        Assert.Equal("meet:acc1:e1:2026-11-14T10:05:00Z", e1.Id);
        Assert.Equal("Design sync", e1.Title);
        Assert.Equal("Priya Shah", e1.From);
        Assert.Equal("Starts in 5 min · Google Meet", e1.Detail);
        Assert.Equal(Now.AddMinutes(5), e1.Date);
        Assert.Equal("https://meet.google.com/abc-defg-hij", e1.Url);

        var e5 = result.Items[1];
        Assert.Equal("meet:acc1:e5:2026-11-14T11:57:00+02:00", e5.Id);      // the start text as sent, so a moved meeting is a new item
        Assert.Equal("ops@example.com", e5.From);
        Assert.Equal("Started 3 min ago", e5.Detail);
        Assert.Equal("https://calendar.google.com/event?eid=e5", e5.Url);

        var e6 = result.Items[2];
        Assert.Equal("Meeting", e6.Title);
        Assert.Equal("Starting now", e6.Detail);
        Assert.Null(e6.From);
        Assert.Null(e6.Url);
    }

    [Fact]
    public async Task AsksForTenMinutesAgoUntilTheLeadTime()
    {
        var routes = new Routes().OnPath("/calendar/v3/calendars/primary/events", """{"items":[]}""");
        await CalendarSource.FetchAsync(routes.Api(NetTestData.Google()), 15, Now);
        var q = routes.Handler.Requests[0].Query;
        Assert.Equal("2026-11-14T09:50:00Z", q["timeMin"]);
        Assert.Equal("2026-11-14T10:15:00Z", q["timeMax"]);
        Assert.Equal("true", q["singleEvents"]);
        Assert.Equal("startTime", q["orderBy"]);
        Assert.Equal("20", q["maxResults"]);
    }
}

public class TasksSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TasksDueTodayOrOverdueBecomeOneReminderPerDay()
    {
        string Iso(DateTimeOffset d) => d.ToString("yyyy-MM-dd'T'HH:mm:ss.000'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var routes = new Routes()
            .OnPath("/tasks/v1/users/@me/lists", """{"items":[{"id":"L1","title":"My Tasks"},{"title":"No id"}]}""")
            .OnPath("/tasks/v1/lists/L1/tasks", $$"""
                {"items":[
                  {"id":"t1","title":"File expenses","due":"{{Iso(Now)}}","notes":"Receipts are in the drawer"},
                  {"id":"t2","title":"Renew licence","due":"{{Iso(Now.AddDays(-3))}}"},
                  {"id":"t3","title":"","due":"{{Iso(Now)}}"},
                  {"id":"t4","title":"No due date"},
                  {"title":"No id","due":"{{Iso(Now)}}"}]}
                """);
        var result = await TasksSource.FetchAsync(routes.Api(NetTestData.Google()), Now);

        Assert.Equal(ItemSource.task, result.Source);
        var today = ApiDates.Day(Now);
        Assert.Equal([$"task:acc1:t1:{today}", $"task:acc1:t2:{today}"], result.Items.Select(i => i.Id));
        Assert.Equal(new HashSet<string>(result.Items.Select(i => i.Id)), result.LiveIDs);

        var t1 = result.Items[0];
        Assert.Equal("File expenses", t1.Title);
        Assert.Equal("My Tasks", t1.From);
        Assert.Equal("Due today · Receipts are in the drawer", t1.Detail);
        Assert.Equal(Now, t1.Date);
        Assert.Equal("https://tasks.google.com/", t1.Url);

        var t2 = result.Items[1];
        Assert.Equal($"Overdue (was due {ApiDates.Day(Now.AddDays(-3))})", t2.Detail);

        var tasksReq = routes.Handler.Requests.First(r => r.Path == "/tasks/v1/lists/L1/tasks");
        Assert.Equal("false", tasksReq.Query["showCompleted"]);
        Assert.Equal("100", tasksReq.Query["maxResults"]);
        Assert.Equal("100", routes.Handler.Requests[0].Query["maxResults"]);
        var dueMax = ApiDates.ParseDate(tasksReq.Query["dueMax"])!.Value;
        Assert.True(dueMax > Now && dueMax <= Now.AddDays(1));                 // the end of today, local time
    }
}

public class OutlookMailSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    // Port of "Only mail with chosen labels" (Outlook half) in SelfTest.swift.
    [Fact]
    public void DefaultIsTheInbox()
    {
        var m = NetTestData.Microsoft();
        var q = OutlookMailSource.ListQueries(m, DateTimeOffset.FromUnixTimeSeconds(0));
        Assert.EndsWith("/mailFolders/inbox/messages", Assert.Single(q).Path);
        Assert.Equal("receivedDateTime ge 1970-01-01T00:00:00Z and isRead eq false", q[0].Query["$filter"]);
        Assert.Equal("receivedDateTime desc", q[0].Query["$orderby"]);
        Assert.Equal("25", q[0].Query["$top"]);
        Assert.Equal("id,subject,from,bodyPreview,receivedDateTime,webLink", q[0].Query["$select"]);
    }

    [Fact]
    public void CategoriesSearchEveryFolderAndApostrophesAreEscaped()
    {
        var m = NetTestData.Microsoft();
        m.OnlyLabels = [new MailLabel { Id = "Red", Name = "Red" }, new MailLabel { Id = "O'Brien", Name = "O'Brien" }];
        var o = OutlookMailSource.ListQueries(m, DateTimeOffset.FromUnixTimeSeconds(0));
        Assert.Equal(2, o.Count);
        Assert.All(o, x => Assert.EndsWith("/me/messages", x.Path));
        Assert.EndsWith("categories/any(c:c eq 'Red')", o[0].Query["$filter"]);
        Assert.Contains("eq 'O''Brien'", o[1].Query["$filter"]);
        Assert.False(o[0].Query.ContainsKey("$orderby"));          // Graph can't sort a category filter
    }

    [Fact]
    public void CategoriesChosenButNonePickedMeansNoMail()
    {
        var m = NetTestData.Microsoft();
        m.OnlyLabelsList = [];
        Assert.Empty(OutlookMailSource.ListQueries(m, DateTimeOffset.UnixEpoch));
    }

    private const string Messages = """
        {"value":[
          {"id":"o1","subject":"Invoice 1042","from":{"emailAddress":{"name":"Priya Shah","address":"Priya@Example.com"}},"bodyPreview":"Please find the invoice attached.",
           "receivedDateTime":"2026-11-14T08:00:00Z","webLink":"https://outlook.office.com/mail/id/o1"},
          {"id":"o2","subject":"","from":{"emailAddress":{"name":"","address":"Ops@Example.com"}},"receivedDateTime":"2026-11-14T09:30:00Z"},
          {"id":"o3","from":null,"receivedDateTime":"2026-11-14T09:00:00.1234567"},
          {"id":"o1","subject":"Duplicate"}
        ]}
        """;

    [Fact]
    public async Task BuildsItemsNewestFirstWithNamesAndAddresses()
    {
        var routes = new Routes().OnPath("/v1.0/me/mailFolders/inbox/messages", Messages);
        var result = await OutlookMailSource.FetchAsync(routes.Api(NetTestData.Microsoft()), Now);

        Assert.Equal(ItemSource.mail, result.Source);
        Assert.Equal(["mail:acc2:o2", "mail:acc2:o3", "mail:acc2:o1"], result.Items.Select(i => i.Id));
        Assert.Equal(new HashSet<string>(result.Items.Select(i => i.Id)), result.LiveIDs);

        var o1 = result.Items[2];
        Assert.Equal("Invoice 1042", o1.Title);
        Assert.Equal("Priya Shah", o1.From);
        Assert.Equal("priya@example.com", o1.FromAddress);
        Assert.Equal("Please find the invoice attached.", o1.Detail);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 8, 0, 0, TimeSpan.Zero), o1.Date);
        Assert.Equal("https://outlook.office.com/mail/id/o1", o1.Url);
        Assert.Equal("Contoso", o1.AccountLabel);

        var o2 = result.Items[0];
        Assert.Equal("(no subject)", o2.Title);
        Assert.Equal("Ops@Example.com", o2.From);                    // no display name: the address
        Assert.Equal("ops@example.com", o2.FromAddress);

        var o3 = result.Items[1];
        Assert.Equal("Unknown sender", o3.From);
        Assert.Null(o3.FromAddress);
        Assert.Null(o3.Url);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 9, 0, 0, 123, TimeSpan.Zero), o3.Date);
    }

    [Fact]
    public async Task AsksForUnreadInboxMailFromTheLastTwoDays()
    {
        var routes = new Routes().OnPath("/v1.0/me/mailFolders/inbox/messages", """{"value":[]}""");
        await OutlookMailSource.FetchAsync(routes.Api(NetTestData.Microsoft()), Now);
        var q = routes.Handler.Requests[0].Query;
        Assert.Equal("receivedDateTime ge 2026-11-12T10:00:00Z and isRead eq false", q["$filter"]);
        Assert.Equal("25", q["$top"]);
    }

    [Fact]
    public async Task CategoryQueriesAreMergedAndSorted()
    {
        var routes = new Routes()
            .On(r => r.Path == "/v1.0/me/messages" && r.Query["$filter"].EndsWith("'Red')"),
                """{"value":[{"id":"a","subject":"A","receivedDateTime":"2026-11-14T07:00:00Z"}]}""")
            .On(r => r.Path == "/v1.0/me/messages" && r.Query["$filter"].EndsWith("'Blue')"),
                """{"value":[{"id":"b","subject":"B","receivedDateTime":"2026-11-14T09:00:00Z"},{"id":"a","subject":"A again"}]}""");
        var m = NetTestData.Microsoft();
        m.OnlyLabels = [new MailLabel { Id = "Red", Name = "Red" }, new MailLabel { Id = "Blue", Name = "Blue" }];
        var result = await OutlookMailSource.FetchAsync(routes.Api(m), Now);
        Assert.Equal(["mail:acc2:b", "mail:acc2:a"], result.Items.Select(i => i.Id));
        Assert.Equal("A", result.Items[1].Title);
    }

    [Fact]
    public async Task CategoriesOfRecentMailAreListedOnce()
    {
        var routes = new Routes().OnPath("/v1.0/me/messages", """
            {"value":[{"categories":["Red category","Blue category"]},{"categories":[]},{"categories":["Red category","Project 10","Project 2"]},{}]}
            """);
        var cats = await OutlookMailSource.CategoriesAsync(routes.Api(NetTestData.Microsoft()));
        Assert.Equal(["Blue category", "Project 2", "Project 10", "Red category"], cats.Select(c => c.Name));
        Assert.All(cats, c => Assert.Equal(c.Name, c.Id));
        Assert.Equal("200", routes.Handler.Requests[0].Query["$top"]);
    }
}

public class TeamsChatSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    private static string G(DateTimeOffset d) => d.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", System.Globalization.CultureInfo.InvariantCulture);

    private static string Chats() => $$$$"""
        {"value":[
          {"id":"c1","chatType":"oneOnOne","webUrl":"https://teams.microsoft.com/l/chat/c1","viewpoint":{"lastMessageReadDateTime":"{{{{G(Now.AddHours(-3))}}}}Z"},
           "lastMessagePreview":{"id":"1700","createdDateTime":"{{{{G(Now.AddMinutes(-30))}}}}Z","from":{"user":{"id":"u-other","displayName":"Priya Shah"}},
             "body":{"content":"<p>Hello&nbsp;<b>team</b> &amp; friends</p>"}}},
          {"id":"c2","chatType":"oneOnOne","lastMessagePreview":{"id":"1701","createdDateTime":"{{{{G(Now.AddMinutes(-20))}}}}Z","from":{"user":{"id":"u-me","displayName":"Me"}},"body":{"content":"mine"}}},
          {"id":"c3","chatType":"oneOnOne","viewpoint":{"lastMessageReadDateTime":"{{{{G(Now.AddMinutes(-5))}}}}Z"},
           "lastMessagePreview":{"id":"1702","createdDateTime":"{{{{G(Now.AddMinutes(-10))}}}}Z","from":{"user":{"id":"u-other","displayName":"Priya Shah"}},"body":{"content":"read already"}}},
          {"id":"c4","chatType":"group","viewpoint":{"isHidden":true},
           "lastMessagePreview":{"id":"1703","createdDateTime":"{{{{G(Now.AddMinutes(-10))}}}}Z","from":{"user":{"id":"u-other","displayName":"Priya Shah"}},"body":{"content":"hidden chat"}}},
          {"id":"c5","chatType":"group","lastMessagePreview":{"id":"1704","isDeleted":true,"createdDateTime":"{{{{G(Now.AddMinutes(-10))}}}}Z","from":{"user":{"id":"u-other","displayName":"Priya Shah"}},"body":{"content":""}}},
          {"id":"c6","chatType":"group","lastMessagePreview":{"id":"1705","createdDateTime":"{{{{G(Now.AddDays(-3))}}}}Z","from":{"user":{"id":"u-other","displayName":"Priya Shah"}},"body":{"content":"old news"}}},
          {"id":"c7","chatType":"group","topic":"Launch crew","viewpoint":{"lastMessageReadDateTime":"{{{{G(Now.AddHours(-1))}}}}Z"},
           "lastMessagePreview":{"id":"1706","createdDateTime":"{{{{G(Now.AddMinutes(-15))}}}}Z","from":{"user":{"id":"u-other","displayName":"Sam Lee"}},"body":{"content":""}}},
          {"id":"c8","chatType":"meeting","webUrl":"https://teams.microsoft.com/l/chat/c8","lastMessagePreview":{"id":"1707","createdDateTime":"{{{{G(Now.AddMinutes(-12))}}}}","from":{"user":{"id":"u-3"}},"body":{"content":"Recording is ready"}}},
          {"id":"c9","chatType":"group"},
          {"id":"c10","chatType":"group","lastMessagePreview":{"id":"1708","createdDateTime":"{{{{G(Now.AddMinutes(-12))}}}}Z","from":{"application":{"displayName":"Bot"}},"body":{"content":"Automated post"}}}
        ]}
        """;

    [Fact]
    public async Task OnlyUnreadChatsFromOthersInTheLastTwoDaysBecomeItems()
    {
        var routes = new Routes().OnPath("/v1.0/me", """{"id":"u-me"}""").OnPath("/v1.0/me/chats", Chats());
        var result = await new TeamsChatSource().FetchAsync(routes.Api(NetTestData.Microsoft()), Now);

        Assert.Equal(ItemSource.chat, result.Source);
        Assert.Equal(["teams:acc2:c1:1700", "teams:acc2:c7:1706", "teams:acc2:c8:1707", "teams:acc2:c10:1708"], result.Items.Select(i => i.Id));
        // Teams knows what is read, so liveIDs is exactly the unread items.
        Assert.Equal(new HashSet<string>(result.Items.Select(i => i.Id)), result.LiveIDs);

        var c1 = result.Items[0];
        Assert.Equal("Direct message", c1.Title);
        Assert.Equal("Priya Shah", c1.From);
        Assert.Equal("Hello team & friends", c1.Detail);
        Assert.Equal("https://teams.microsoft.com/l/chat/c1", c1.Url);
        Assert.Equal("c1", c1.ChatRef);
        Assert.Equal(Now.AddMinutes(-30), c1.Date);
        Assert.Equal("Contoso", c1.AccountLabel);

        Assert.Equal("Launch crew", result.Items[1].Title);          // a topic wins
        Assert.Equal("(attachment)", result.Items[1].Detail);         // nothing but markup
        Assert.Equal("Meeting chat", result.Items[2].Title);
        Assert.Equal("Someone", result.Items[2].From);                // no display name
        Assert.Equal("Someone", result.Items[3].From);                // a bot has no user
        Assert.Equal("Group chat", result.Items[3].Title);
    }

    [Fact]
    public async Task AsksForTheLastMessagePreviewOfFiftyChats()
    {
        var routes = new Routes().OnPath("/v1.0/me", """{"id":"u-me"}""").OnPath("/v1.0/me/chats", """{"value":[]}""");
        await new TeamsChatSource().FetchAsync(routes.Api(NetTestData.Microsoft()), Now);
        var q = routes.Handler.Requests.First(r => r.Path == "/v1.0/me/chats").Query;
        Assert.Equal("lastMessagePreview", q["$expand"]);
        Assert.Equal("lastMessagePreview/createdDateTime desc", q["$orderby"]);
        Assert.Equal("50", q["$top"]);
        Assert.Equal("id", routes.Handler.Requests.First(r => r.Path == "/v1.0/me").Query["$select"]);
    }

    [Fact]
    public async Task YourUserIdIsLookedUpOncePerAccount()
    {
        var routes = new Routes().OnPath("/v1.0/me", """{"id":"u-me"}""").OnPath("/v1.0/me/chats", """{"value":[]}""");
        var source = new TeamsChatSource();
        var api = routes.Api(NetTestData.Microsoft());
        await source.FetchAsync(api, Now);
        await source.FetchAsync(api, Now);
        Assert.Equal(1, routes.Handler.Count(r => r.Path == "/v1.0/me"));
        await source.FetchAsync(routes.Api(NetTestData.Microsoft("other")), Now);
        Assert.Equal(2, routes.Handler.Count(r => r.Path == "/v1.0/me"));
    }
}

public class OutlookCalendarSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    private const string Events = """
        {"value":[
          {"id":"a1","subject":"Planning","start":{"dateTime":"2026-11-14T10:05:00.0000000","timeZone":"UTC"},"isAllDay":false,"isCancelled":false,
           "organizer":{"emailAddress":{"name":"Priya Shah","address":"priya@example.com"}},
           "onlineMeeting":{"joinUrl":"https://teams.microsoft.com/l/meetup-join/abc"},"webLink":"https://outlook.office.com/calendar/item/a1","responseStatus":{"response":"accepted"}},
          {"id":"a2","subject":"Offsite","start":{"dateTime":"2026-11-14T00:00:00.0000000"},"isAllDay":true},
          {"id":"a3","subject":"Cancelled sync","start":{"dateTime":"2026-11-14T10:02:00.0000000"},"isCancelled":true},
          {"id":"a4","subject":"Declined sync","start":{"dateTime":"2026-11-14T10:02:00.0000000"},"responseStatus":{"response":"declined"}},
          {"id":"a5","subject":"Long gone","start":{"dateTime":"2026-11-14T09:40:00.0000000"}},
          {"id":"a6","start":{"dateTime":"2026-11-14T09:58:00.0000000"},"webLink":"https://outlook.office.com/calendar/item/a6","organizer":{"emailAddress":{"address":"no-name@example.com"}}}
        ]}
        """;

    [Fact]
    public async Task OnlyMeetingsAboutToStartBecomeItemsWithTheTeamsLink()
    {
        var routes = new Routes().OnPath("/v1.0/me/calendarView", Events);
        var result = await OutlookCalendarSource.FetchAsync(routes.Api(NetTestData.Microsoft()), 10, Now);

        Assert.Equal(ItemSource.meeting, result.Source);
        Assert.Equal(["meet:acc2:a1:2026-11-14T10:05:00.0000000", "meet:acc2:a6:2026-11-14T09:58:00.0000000"], result.Items.Select(i => i.Id));
        Assert.Equal(new HashSet<string>(result.Items.Select(i => i.Id)), result.LiveIDs);

        var a1 = result.Items[0];
        Assert.Equal("Planning", a1.Title);
        Assert.Equal("Priya Shah", a1.From);
        Assert.Equal("Starts in 5 min · Teams", a1.Detail);
        Assert.Equal(Now.AddMinutes(5), a1.Date);
        Assert.Equal("https://teams.microsoft.com/l/meetup-join/abc", a1.Url);

        var a6 = result.Items[1];
        Assert.Equal("Meeting", a6.Title);
        Assert.Null(a6.From);                                            // the organiser has no name
        Assert.Equal("Started 2 min ago", a6.Detail);
        Assert.Equal("https://outlook.office.com/calendar/item/a6", a6.Url);
    }

    [Fact]
    public async Task AsksInUtcForTheRightWindow()
    {
        var routes = new Routes().OnPath("/v1.0/me/calendarView", """{"value":[]}""");
        await OutlookCalendarSource.FetchAsync(routes.Api(NetTestData.Microsoft()), 20, Now);
        var req = routes.Handler.Requests[0];
        Assert.Equal("outlook.timezone=\"UTC\"", req.Headers["Prefer"]);
        Assert.Equal("2026-11-14T09:50:00Z", req.Query["startDateTime"]);
        Assert.Equal("2026-11-14T10:20:00Z", req.Query["endDateTime"]);
        Assert.Equal("start/dateTime", req.Query["$orderby"]);
        Assert.Equal("20", req.Query["$top"]);
        Assert.Equal("id,subject,start,isAllDay,isCancelled,organizer,onlineMeeting,webLink,responseStatus", req.Query["$select"]);
    }
}

public class ToDoSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    private static string G(DateTimeOffset d) => d.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task TasksDueTodayOrOverdueBecomeOneReminderPerDay()
    {
        var routes = new Routes()
            .OnPath("/v1.0/me/todo/lists", """{"value":[{"id":"T1","displayName":"Tasks"},{"displayName":"No id"}]}""")
            .OnPath("/v1.0/me/todo/lists/T1/tasks", $$$"""
                {"value":[
                  {"id":"x1","title":"Send report","dueDateTime":{"dateTime":"{{{G(Now)}}}","timeZone":"UTC"}},
                  {"id":"x2","title":"Pay supplier","dueDateTime":{"dateTime":"{{{G(Now.AddDays(-4))}}}","timeZone":"UTC"}},
                  {"id":"x3","title":"Next week","dueDateTime":{"dateTime":"{{{G(Now.AddDays(4))}}}","timeZone":"UTC"}},
                  {"id":"x4","title":"No due date"},
                  {"id":"x5","title":"","dueDateTime":{"dateTime":"{{{G(Now)}}}","timeZone":"UTC"}}]}
                """);
        var result = await ToDoSource.FetchAsync(routes.Api(NetTestData.Microsoft()), Now);

        Assert.Equal(ItemSource.task, result.Source);
        var today = ApiDates.Day(Now);
        Assert.Equal([$"task:acc2:x1:{today}", $"task:acc2:x2:{today}"], result.Items.Select(i => i.Id));
        Assert.Equal(new HashSet<string>(result.Items.Select(i => i.Id)), result.LiveIDs);

        var x1 = result.Items[0];
        Assert.Equal("Send report", x1.Title);
        Assert.Equal("Tasks", x1.From);
        Assert.Equal("Due today", x1.Detail);
        Assert.Equal(Now, x1.Date);
        Assert.Equal("https://to-do.office.com/tasks/", x1.Url);
        Assert.Equal($"Overdue (was due {ApiDates.Day(Now.AddDays(-4))})", result.Items[1].Detail);

        var q = routes.Handler.Requests.First(r => r.Path.EndsWith("/T1/tasks")).Query;
        Assert.Equal("status ne 'completed'", q["$filter"]);
        Assert.Equal("100", q["$top"]);
    }
}

public class ChatDirectoryTests
{
    private static readonly DateTimeOffset T = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);
    private static string Iso(DateTimeOffset d) => ApiDates.IsoPlain(d);

    private static string TempCache() => Path.Combine(Path.GetTempPath(), "db-chat-" + Guid.NewGuid().ToString("N") + ".json");

    private static Routes GoogleRoutes() => new Routes()
        .OnPath("/v1/userinfo", """{"sub":"111"}""")
        .OnPath("/v1/spaces", $$"""
            {"spaces":[
              {"name":"spaces/S1","spaceType":"SPACE","displayName":"Team Room","lastActiveTime":"{{Iso(T.AddHours(-3))}}"},
              {"name":"spaces/D1","spaceType":"DIRECT_MESSAGE","lastActiveTime":"{{Iso(T.AddMinutes(-5))}}"},
              {"name":"spaces/D2","spaceType":"DIRECT_MESSAGE"},
              {"name":"spaces/G1","spaceType":"GROUP_CHAT","lastActiveTime":"{{Iso(T.AddHours(-1))}}"},
              {"name":"spaces/G2","spaceType":"GROUP_CHAT","lastActiveTime":"{{Iso(T.AddHours(-2))}}"},
              {"spaceType":"SPACE"}]}
            """)
        .OnPath("/v1/spaces/D1/messages", """
            {"messages":[{"sender":{"name":"users/111","displayName":"Me"}},{"sender":{"name":"users/222","displayName":"Priya Shah"}}]}
            """)
        .OnPath("/v1/spaces/G1/messages", """
            {"messages":[{"sender":{"name":"users/222","displayName":"Priya Shah"}},{"sender":{"name":"users/111","displayName":"Me"}},
                         {"sender":{"name":"users/333","displayName":"Sam Lee"}},{"sender":{"name":"users/222","displayName":"Priya Shah"}},
                         {"sender":{"name":"users/444","displayName":"Ana Ruiz"}},{"sender":{"name":"users/555","displayName":"Lee Min"}},{"sender":{"name":"users/666"}}]}
            """)
        .OnPath("/v1/spaces/D2/messages", """{}""")
        .OnPath("/v1/spaces/G2/messages", """{"error":{"message":"denied"}}""", HttpStatusCode.Forbidden);

    [Fact]
    public async Task GoogleSpacesAreNamedAfterThePeopleInThemAndSortedByActivity()
    {
        var routes = GoogleRoutes();
        var cache = TempCache();
        var progress = new List<(int, int)>();
        try
        {
            var list = await ChatDirectory.ListAsync(routes.Api(NetTestData.Google()), (d, t) => progress.Add((d, t)), cache);

            Assert.Equal(["spaces/D1", "spaces/G1", "spaces/G2", "spaces/S1", "spaces/D2"], list.Select(c => c.ChatID));
            Assert.All(list, c => Assert.Equal("acc1", c.AccountID));
            Assert.Equal("Priya Shah", list[0].Name);
            Assert.Equal(ChatKind.direct, list[0].Kind);
            Assert.Equal(T.AddMinutes(-5), list[0].LastActive);
            Assert.Equal("Priya Shah, Sam Lee, Ana Ruiz", list[1].Name);       // not you, no repeats, at most three
            Assert.Equal(ChatKind.group, list[1].Kind);
            Assert.Equal("Unnamed group chat", list[2].Name);                  // the lookup failed
            Assert.Equal("Team Room", list[3].Name);
            Assert.Equal(ChatKind.space, list[3].Kind);
            Assert.Equal("Direct message (no recent messages)", list[4].Name);
            Assert.Null(list[4].LastActive);

            // Four unnamed chats, looked up in one batch of up to eight.
            Assert.Equal([(0, 4), (4, 4)], progress);
            Assert.Equal("25", routes.Handler.Requests.First(r => r.Path == "/v1/spaces/D1/messages").Query["pageSize"]);
            Assert.Equal("200", routes.Handler.Requests.First(r => r.Path == "/v1/spaces").Query["pageSize"]);

            // Names that were found are kept on disk (the ones that weren't are tried again next time).
            var saved = JsonFile.Load<Dictionary<string, string>>(cache)!;
            Assert.Equal("Priya Shah", saved["spaces/D1"]);
            Assert.Equal("Priya Shah, Sam Lee, Ana Ruiz", saved["spaces/G1"]);
            Assert.False(saved.ContainsKey("spaces/G2"));
            Assert.False(saved.ContainsKey("spaces/D2"));

            routes.Handler.Requests.Clear();
            progress.Clear();
            var again = await ChatDirectory.ListAsync(routes.Api(NetTestData.Google()), (d, t) => progress.Add((d, t)), cache);
            Assert.Equal("Priya Shah", again[0].Name);
            Assert.Equal([(0, 2), (2, 2)], progress);                          // D1 and G1 come from the cache
            Assert.Equal(0, routes.Handler.Count(r => r.Path == "/v1/spaces/D1/messages"));
        }
        finally
        {
            File.Delete(cache);
        }
    }

    [Fact]
    public async Task SpacesArePagedUpToAThousand()
    {
        var pages = 0;
        var routes = new Routes()
            .OnPath("/v1/userinfo", """{"sub":"111"}""")
            .On(r => r.Path == "/v1/spaces", r =>
            {
                pages++;
                var spaces = string.Join(",", Enumerable.Range(0, 200).Select(i => $$"""{"name":"spaces/P{{pages}}_{{i}}","spaceType":"SPACE","displayName":"Room {{pages}}-{{i}}"}"""));
                return $$"""{"spaces":[{{spaces}}],"nextPageToken":"more"}""";
            });
        var cache = TempCache();
        try
        {
            var list = await ChatDirectory.ListAsync(routes.Api(NetTestData.Google()), null, cache);
            Assert.Equal(1000, list.Count);
            Assert.Equal(5, pages);
            Assert.Equal("more", routes.Handler.Requests[1].Query["pageToken"]);
        }
        finally
        {
            File.Delete(cache);
        }
    }

    [Fact]
    public async Task MicrosoftChatsAreNamedByTopicOrTheOtherMembers()
    {
        var routes = new Routes()
            .OnPath("/v1.0/me", """{"id":"u-me"}""")
            .OnPath("/v1.0/me/chats", $$"""
                {"value":[
                  {"id":"c1","chatType":"oneOnOne","lastUpdatedDateTime":"{{Iso(T)}}","members":[{"userId":"u-me","displayName":"Me"},{"userId":"u2","displayName":"Priya Shah"}]},
                  {"id":"c2","chatType":"group","topic":"Launch crew","members":[{"userId":"u-me","displayName":"Me"}]},
                  {"id":"c3","chatType":"group","members":[{"userId":"u-me","displayName":"Me"},{"userId":"u2","displayName":"A"},{"userId":"u3","displayName":"B"},{"userId":"u4","displayName":"C"},{"userId":"u5","displayName":"D"}]},
                  {"id":"c4","chatType":"meeting","members":[{"userId":"u-me","displayName":"Me"}]},
                  {"chatType":"group"}]}
                """);
        var list = await ChatDirectory.ListAsync(routes.Api(NetTestData.Microsoft()));
        Assert.Equal(["c1", "c2", "c3", "c4"], list.Select(c => c.ChatID));
        Assert.Equal(["Priya Shah", "Launch crew", "A, B, C", "Chat"], list.Select(c => c.Name));
        Assert.Equal([ChatKind.direct, ChatKind.group, ChatKind.group, ChatKind.group], list.Select(c => c.Kind!.Value));
        Assert.Equal(T, list[0].LastActive);
        Assert.Null(list[1].LastActive);
        Assert.All(list, c => Assert.Equal("acc2", c.AccountID));
        var q = routes.Handler.Requests.First(r => r.Path == "/v1.0/me/chats").Query;
        Assert.Equal("members", q["$expand"]);
        Assert.Equal("50", q["$top"]);
    }
}

public class LiveGatewayTests
{
    private sealed class Rig
    {
        public MemorySecretStore Store { get; } = new();
        public Routes Routes { get; }
        public LiveGateway Gateway { get; }

        public Rig(Func<IReadOnlySet<string>>? knownMail = null, Func<string, IEnumerable<string>>? chatIds = null, Routes? routes = null)
        {
            Routes = routes ?? new Routes();
            Routes.On(r => r.Method == HttpMethod.Post, """{"access_token":"at","expires_in":3600}""");
            Routes.On(r => true, "{}");
            var http = new HttpClient(Routes.Handler);
            var oauth = new OAuth(Store, () => new TeamConfig { GoogleClientID = "g-id", MicrosoftClientID = "m-id" }, http);
            Gateway = new LiveGateway(knownMail, chatIds, oauth, new ChatSource(), new TeamsChatSource(), http);
        }

        public AccountConfig SignedIn(AccountConfig a)
        {
            Store.Set(OAuth.RefreshKey(a.Id), "rt");
            return a;
        }

        public List<string> Hits() => Routes.Handler.Requests.Where(r => r.Method == HttpMethod.Get).Select(r => r.Uri.Host + r.Path).ToList();
    }

    [Fact]
    public void IsSignedInMeansARefreshTokenIsSaved()
    {
        var rig = new Rig();
        var a = NetTestData.Google();
        Assert.False(rig.Gateway.IsSignedIn(a));
        rig.Store.Set("refresh.acc1", "rt");
        Assert.True(rig.Gateway.IsSignedIn(a));
        Assert.False(rig.Gateway.IsSignedIn(NetTestData.Google("other")));
    }

    [Theory]
    [InlineData(ItemSource.mail, "gmail.googleapis.com/gmail/v1/users/me/messages")]
    [InlineData(ItemSource.chat, "chat.googleapis.com/v1/spaces")]
    [InlineData(ItemSource.meeting, "www.googleapis.com/calendar/v3/calendars/primary/events")]
    [InlineData(ItemSource.task, "tasks.googleapis.com/tasks/v1/users/@me/lists")]
    public async Task GoogleAccountsFetchFromGoogle(ItemSource source, string expected)
    {
        var rig = new Rig();
        var a = rig.SignedIn(NetTestData.Google());
        var result = await rig.Gateway.FetchAsync(a, source, 10);
        Assert.Equal(source, result.Source);
        Assert.Contains(expected, rig.Hits());
        Assert.DoesNotContain(rig.Hits(), h => h.StartsWith("graph.microsoft.com"));
    }

    [Theory]
    [InlineData(ItemSource.mail, "graph.microsoft.com/v1.0/me/mailFolders/inbox/messages")]
    [InlineData(ItemSource.chat, "graph.microsoft.com/v1.0/me/chats")]
    [InlineData(ItemSource.meeting, "graph.microsoft.com/v1.0/me/calendarView")]
    [InlineData(ItemSource.task, "graph.microsoft.com/v1.0/me/todo/lists")]
    public async Task MicrosoftAccountsFetchFromGraph(ItemSource source, string expected)
    {
        var rig = new Rig();
        var a = rig.SignedIn(NetTestData.Microsoft());
        var result = await rig.Gateway.FetchAsync(a, source, 10);
        Assert.Equal(source, result.Source);
        Assert.Contains(expected, rig.Hits());
        Assert.All(rig.Hits(), h => Assert.StartsWith("graph.microsoft.com", h));
    }

    [Fact]
    public async Task MeetingsUseTheLeadTimeFromSettings()
    {
        var rig = new Rig();
        await rig.Gateway.FetchAsync(rig.SignedIn(NetTestData.Google()), ItemSource.meeting, 25);
        var q = rig.Routes.Handler.Requests.First(r => r.Path.EndsWith("/events")).Query;
        var min = ApiDates.ParseDate(q["timeMin"])!.Value;
        var max = ApiDates.ParseDate(q["timeMax"])!.Value;
        Assert.Equal(TimeSpan.FromMinutes(35), max - min);                  // 10 minutes back plus the lead time
    }

    [Fact]
    public async Task GmailSkipsMessagesTheAppAlreadyKnowsButKeepsThemLive()
    {
        var routes = new Routes().OnPath("/gmail/v1/users/me/messages", """{"messages":[{"id":"m1"}]}""");
        var rig = new Rig(() => new HashSet<string> { "mail:acc1:m1" }, routes: routes);
        var result = await rig.Gateway.FetchAsync(rig.SignedIn(NetTestData.Google()), ItemSource.mail, 10);
        Assert.Empty(result.Items);
        Assert.Equal(["mail:acc1:m1"], result.LiveIDs);
        Assert.DoesNotContain(rig.Hits(), h => h.EndsWith("/m1"));
    }

    [Fact]
    public async Task GoogleChatItemsOnScreenStayLive()
    {
        string? asked = null;
        var rig = new Rig(chatIds: id => { asked = id; return ["chat:acc1:spaces/A/messages/1"]; });
        var result = await rig.Gateway.FetchAsync(rig.SignedIn(NetTestData.Google()), ItemSource.chat, 10);
        Assert.Equal("acc1", asked);
        Assert.Equal(["chat:acc1:spaces/A/messages/1"], result.LiveIDs);
    }

    [Fact]
    public async Task RemindersAreNotFetchedFromTheNetwork()
    {
        var rig = new Rig();
        await Assert.ThrowsAsync<ArgumentException>(() => rig.Gateway.FetchAsync(rig.SignedIn(NetTestData.Google()), ItemSource.reminder, 10));
        Assert.Empty(rig.Routes.Handler.Requests);
    }

    [Fact]
    public async Task NotSignedInThrowsAnAuthException()
    {
        var rig = new Rig();
        var e = await Assert.ThrowsAsync<AuthException>(() => rig.Gateway.FetchAsync(NetTestData.Google(), ItemSource.mail, 10));
        Assert.Equal(AuthErrorKind.NotSignedIn, e.Kind);
    }

    [Fact]
    public async Task AnExpiredSignInSurfacesAsExpired()
    {
        var routes = new Routes().On(r => r.Method == HttpMethod.Post, """{"error":"invalid_grant"}""");
        var rig = new Rig(routes: routes);
        var e = await Assert.ThrowsAsync<AuthException>(() => rig.Gateway.FetchAsync(rig.SignedIn(NetTestData.Microsoft()), ItemSource.mail, 10));
        Assert.Equal(AuthErrorKind.Expired, e.Kind);
    }

    [Fact]
    public async Task ApiErrorsCarryTheFixLink()
    {
        var routes = new Routes().OnPath("/v1/spaces", """{"error":{"message":"Google Chat API has not been used. Enable it at https://console.developers.google.com/apis/api/chat.googleapis.com/overview?project=1 then retry."}}""", HttpStatusCode.Forbidden);
        var rig = new Rig(routes: routes);
        var e = await Assert.ThrowsAsync<ApiException>(() => rig.Gateway.FetchAsync(rig.SignedIn(NetTestData.Google()), ItemSource.chat, 10));
        Assert.Equal("https://console.developers.google.com/apis/api/chat.googleapis.com/overview?project=1", e.FixUrl);
    }
}
