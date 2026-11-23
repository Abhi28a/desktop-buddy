namespace DesktopBuddy.Core;

// Port of the TeamsChatSource actor in MicrosoftSources.swift: the last message of each Teams chat that is
// unread, from someone else, from the last 2 days. Caches your own user ID per account. Thread-safe.

public sealed class TeamsChatSource
{
    public static TeamsChatSource Shared { get; } = new();

    private readonly Lock _lock = new();
    private readonly Dictionary<string, string> _me = [];   // account id -> AAD user id

    public async Task<FetchResult> FetchAsync(ApiClient api, DateTimeOffset? nowOverride = null, CancellationToken ct = default)
    {
        var acc = api.Account;
        var now = nowOverride ?? DateTimeOffset.UtcNow;
        var myID = await MyUserIdAsync(api, ct).ConfigureAwait(false);
        var json = await api.GetAsync($"{GraphApi.Base}/me/chats", new Dictionary<string, string>
        {
            ["$expand"] = "lastMessagePreview",
            ["$orderby"] = "lastMessagePreview/createdDateTime desc",
            ["$top"] = "50",
        }, ct: ct).ConfigureAwait(false);

        var cutoff = now.AddDays(-2);
        var items = new List<BuddyItem>();
        foreach (var chat in json.Objs("value"))
        {
            if (chat.Str("id") is not { } chatID
                || chat.Obj("lastMessagePreview") is not { } last
                || last.Str("id") is not { } msgID
                || ApiDates.GraphDate(last.Str("createdDateTime")) is not { } created || created <= cutoff
                || last.Bool("isDeleted") == true) continue;
            var sender = last.Obj("from").Obj("user");
            if (sender.Str("id") == myID) continue;                                  // your own message
            // Teams knows what you've read: skip chats read since the last message.
            var viewpoint = chat.Obj("viewpoint");
            if (ApiDates.GraphDate(viewpoint.Str("lastMessageReadDateTime")) is { } read && read >= created) continue;
            if (viewpoint.Bool("isHidden") == true) continue;

            var body = last.Obj("body").Str("content") ?? "";
            var chatType = chat.Str("chatType") ?? "";
            var topic = ApiJson.NonEmpty(chat.Str("topic"));
            var title = topic ?? (chatType == "oneOnOne" ? "Direct message" : (chatType == "meeting" ? "Meeting chat" : "Group chat"));
            var text = ApiText.StripHtml(body);
            items.Add(new BuddyItem
            {
                Id = $"teams:{acc.Id}:{chatID}:{msgID}", Source = ItemSource.chat, AccountID = acc.Id, AccountLabel = acc.Label,
                Title = title, From = sender.Str("displayName") ?? "Someone",
                Detail = text.Length == 0 ? "(attachment)" : text,
                Date = created, Url = ApiJson.Url(chat.Str("webUrl")),
                ChatRef = chatID,
            });
        }
        // Unread state is reliable in Teams, so chats you've read elsewhere disappear here too.
        return new FetchResult { Source = ItemSource.chat, Items = items, LiveIDs = items.Select(i => i.Id).ToHashSet() };
    }

    private async Task<string> MyUserIdAsync(ApiClient api, CancellationToken ct)
    {
        lock (_lock)
        {
            if (_me.TryGetValue(api.Account.Id, out var cached)) return cached;
        }
        var json = await api.GetAsync($"{GraphApi.Base}/me", new Dictionary<string, string> { ["$select"] = "id" }, ct: ct).ConfigureAwait(false);
        var id = json.Str("id") ?? "";
        lock (_lock) _me[api.Account.Id] = id;
        return id;
    }
}
