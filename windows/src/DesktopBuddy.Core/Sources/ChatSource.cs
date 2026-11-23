using System.Text.Json.Nodes;

namespace DesktopBuddy.Core;

// Port of the ChatSource actor in GoogleSources.swift: Google Chat messages from people other than you,
// since the last check (first run: the last 2 hours). Caches display names, your own user ID and
// the time of the last check per account. Thread-safe.

public sealed class ChatSource
{
    public static ChatSource Shared { get; } = new();

    private readonly Lock _lock = new();
    private readonly Dictionary<string, string> _names = [];      // "users/123" -> "Priya Shah"
    private readonly Dictionary<string, string> _me = [];         // account id -> "users/123"
    private readonly Dictionary<string, DateTimeOffset> _since = [];   // account id -> last check

    /// `currentIds` are the chat item IDs the app shows now for this account: Chat has no "unread" flag in
    /// the API, so those stay live until the person clears them.
    public async Task<FetchResult> FetchAsync(ApiClient api, IEnumerable<string>? currentIds = null,
                                              DateTimeOffset? nowOverride = null, CancellationToken ct = default)
    {
        var acc = api.Account;
        var now = nowOverride ?? DateTimeOffset.UtcNow;
        // First run looks back 2 hours so you see what you just missed.
        DateTimeOffset from;
        lock (_lock) from = _since.TryGetValue(acc.Id, out var s) ? s : now.AddHours(-2);
        var myID = await MyUserNameAsync(api, ct).ConfigureAwait(false);

        var spaces = new List<JsonObject>();
        string? token = null;
        do
        {
            var q = new Dictionary<string, string> { ["pageSize"] = "200" };
            if (token is not null) q["pageToken"] = token;
            var page = await api.GetAsync("https://chat.googleapis.com/v1/spaces", q, ct: ct).ConfigureAwait(false);
            spaces.AddRange(page.Objs("spaces"));
            token = page.Str("nextPageToken");
        } while (token is not null);

        var items = new List<BuddyItem>();
        foreach (var space in spaces)
        {
            if (space.Str("name") is not { } spaceName
                || ApiDates.ParseDate(space.Str("lastActiveTime")) is not { } active || active <= from) continue;
            var filter = $"createTime > \"{ApiDates.IsoPlain(from)}\"";
            var page = await api.GetAsync($"https://chat.googleapis.com/v1/{spaceName}/messages",
                new Dictionary<string, string> { ["filter"] = filter, ["pageSize"] = "20", ["orderBy"] = "createTime desc" }, ct: ct).ConfigureAwait(false);
            foreach (var msg in page.Objs("messages"))
            {
                if (msg.Str("name") is not { } name) continue;
                var sender = msg.Obj("sender");
                var senderID = sender.Str("name") ?? "";
                if (senderID == myID) continue;
                var who = await DisplayNameAsync(senderID, sender.Str("displayName"), api, ct).ConfigureAwait(false);
                var spaceTitle = space.Str("spaceType") == "DIRECT_MESSAGE" ? "Direct message" : (space.Str("displayName") ?? "Chat");
                var text = msg.Str("text") ?? msg.Str("formattedText") ?? "(attachment)";
                var spaceID = spaceName.Replace("spaces/", "");
                items.Add(new BuddyItem
                {
                    Id = $"chat:{acc.Id}:{name}", Source = ItemSource.chat, AccountID = acc.Id, AccountLabel = acc.Label,
                    Title = spaceTitle, From = who, Detail = text,
                    Date = ApiDates.ParseDate(msg.Str("createTime")) ?? now,
                    Url = ApiJson.Url($"https://mail.google.com/chat/u/?authuser={acc.Email}#chat/space/{spaceID}"),
                    ChatRef = spaceName,
                });
            }
        }
        lock (_lock) _since[acc.Id] = now;
        // Chat has no "unread" flag in the API, so chat items stay until you clear them.
        var live = new HashSet<string>(currentIds ?? []);
        live.UnionWith(items.Select(i => i.Id));
        return new FetchResult { Source = ItemSource.chat, Items = items, LiveIDs = live };
    }

    private async Task<string> MyUserNameAsync(ApiClient api, CancellationToken ct)
    {
        lock (_lock)
        {
            if (_me.TryGetValue(api.Account.Id, out var cached)) return cached;
        }
        // The OpenID "sub" is the same numeric ID Chat uses in "users/{id}"; no People API needed.
        var info = await api.GetAsync("https://openidconnect.googleapis.com/v1/userinfo", ct: ct).ConfigureAwait(false);
        var id = "users/" + (info.Str("sub") ?? "");
        lock (_lock) _me[api.Account.Id] = id;
        return id;
    }

    private async Task<string> DisplayNameAsync(string userID, string? fallback, ApiClient api, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(fallback)) return fallback;
        lock (_lock)
        {
            if (_names.TryGetValue(userID, out var cached)) return cached;
        }
        var personID = userID.Replace("users/", "people/");
        JsonObject? person = null;
        try
        {
            person = await api.GetAsync($"https://people.googleapis.com/v1/{personID}", new Dictionary<string, string>
            {
                ["personFields"] = "names,emailAddresses",
                ["sources"] = "DIRECTORY_SOURCE_TYPE_DOMAIN_PROFILE",
            }, ct: ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The People API may be off for this project: fall back below.
        }
        var name = person.Objs("names").FirstOrDefault().Str("displayName")
            ?? person.Objs("emailAddresses").FirstOrDefault().Str("value")
            ?? "Someone (enable People API for names)";
        lock (_lock) _names[userID] = name;
        return name;
    }
}
