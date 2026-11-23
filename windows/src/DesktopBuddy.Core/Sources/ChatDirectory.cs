using System.Text.Json.Nodes;

namespace DesktopBuddy.Core;

// Port of ChatDirectory in Clients.swift: lists the Google Chat spaces / Teams chats you are in,
// most recently active first, for picking client chats.

public static class ChatDirectory
{
    /// Names for unnamed Google Chat DMs / group chats, cached on disk (space -> name).
    private static string CachePath => Path.Combine(Paths.SupportDir, "chat-names.json");

    /// Lists the Google Chat spaces / Teams chats you're in, most recently active first.
    /// `progress(done, total)` reports name lookups for direct messages. `cachePath` is for tests.
    public static Task<List<ChatRef>> ListAsync(ApiClient api, Action<int, int>? progress = null,
                                                string? cachePath = null, CancellationToken ct = default) =>
        api.Account.Provider == Provider.google
            ? GoogleAsync(api, progress, cachePath ?? CachePath, ct)
            : MicrosoftAsync(api, ct);

    /// Same, with the app-wide token provider.
    public static Task<List<ChatRef>> ListAsync(AccountConfig account, Action<int, int>? progress = null, CancellationToken ct = default) =>
        ListAsync(new ApiClient(account), progress, null, ct);

    private static async Task<List<ChatRef>> GoogleAsync(ApiClient api, Action<int, int>? progress, string cachePath, CancellationToken ct)
    {
        var account = api.Account;
        var raw = new List<JsonObject>();
        string? token = null;
        do
        {
            var q = new Dictionary<string, string> { ["pageSize"] = "200" };
            if (token is not null) q["pageToken"] = token;
            var page = await api.GetAsync("https://chat.googleapis.com/v1/spaces", q, ct: ct).ConfigureAwait(false);
            raw.AddRange(page.Objs("spaces"));
            token = page.Str("nextPageToken");
        } while (token is not null && raw.Count < 1000);

        // Google gives DMs and many group chats no name: name them after the people in them.
        string me;
        try
        {
            var info = await api.GetAsync("https://openidconnect.googleapis.com/v1/userinfo", ct: ct).ConfigureAwait(false);
            me = "users/" + (info.Str("sub") ?? "");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            me = "users/";
        }
        var cache = JsonFile.Load<Dictionary<string, string>>(cachePath) ?? [];
        var unnamed = raw
            .Select(s => s.Str("name"))
            .Where((id, i) => id is not null && string.IsNullOrEmpty(raw[i].Str("displayName")) && !cache.ContainsKey(id))
            .Select(id => id!)
            .ToList();
        var done = 0;
        progress?.Invoke(0, unnamed.Count);
        // Look names up a few at a time (polite to the API, fast enough for a few hundred chats).
        for (var start = 0; start < unnamed.Count; start += 8)
        {
            var batch = unnamed.Skip(start).Take(8).ToList();
            var names = await Task.WhenAll(batch.Select(id => PeopleInAsync(id, me, api, ct))).ConfigureAwait(false);
            for (var i = 0; i < batch.Count; i++)
            {
                if (names[i] is { } name) cache[batch[i]] = name;
                done++;
            }
            progress?.Invoke(done, unnamed.Count);
        }
        JsonFile.Save(cache, cachePath);

        var result = new List<ChatRef>();
        foreach (var s in raw)
        {
            if (s.Str("name") is not { } id) continue;
            var type = s.Str("spaceType") ?? "";
            var kind = type == "DIRECT_MESSAGE" ? ChatKind.direct : (type == "GROUP_CHAT" ? ChatKind.group : ChatKind.space);
            var named = ApiJson.NonEmpty(s.Str("displayName"));
            var name = named ?? (cache.TryGetValue(id, out var cached) ? cached : null)
                ?? (kind == ChatKind.direct ? "Direct message (no recent messages)" : "Unnamed group chat");
            result.Add(new ChatRef
            {
                AccountID = account.Id, ChatID = id, Name = name, Kind = kind,
                LastActive = ApiDates.ParseDate(s.Str("lastActiveTime")),
            });
        }
        return [.. result.OrderByDescending(c => c.LastActive ?? DateTimeOffset.MinValue)];
    }

    /// The other people in a chat, from its recent messages (needs only read access to messages).
    private static async Task<string?> PeopleInAsync(string space, string me, ApiClient api, CancellationToken ct)
    {
        JsonObject page;
        try
        {
            page = await api.GetAsync($"https://chat.googleapis.com/v1/{space}/messages",
                new Dictionary<string, string> { ["pageSize"] = "25", ["orderBy"] = "createTime desc" }, ct: ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;
        }
        var names = new List<string>();
        foreach (var m in page.Objs("messages"))
        {
            var sender = m.Obj("sender");
            if (sender.Str("name") == me || ApiJson.NonEmpty(sender.Str("displayName")) is not { } n || names.Contains(n)) continue;
            names.Add(n);
            if (names.Count == 3) break;
        }
        return names.Count == 0 ? null : string.Join(", ", names);
    }

    private static async Task<List<ChatRef>> MicrosoftAsync(ApiClient api, CancellationToken ct)
    {
        var account = api.Account;
        var me = (await api.GetAsync($"{GraphApi.Base}/me", new Dictionary<string, string> { ["$select"] = "id" }, ct: ct).ConfigureAwait(false)).Str("id");
        var json = await api.GetAsync($"{GraphApi.Base}/me/chats", new Dictionary<string, string>
        {
            ["$expand"] = "members", ["$top"] = "50", ["$orderby"] = "lastMessagePreview/createdDateTime desc",
        }, ct: ct).ConfigureAwait(false);

        var result = new List<ChatRef>();
        foreach (var c in json.Objs("value"))
        {
            if (c.Str("id") is not { } id) continue;
            var others = c.Objs("members")
                .Where(m => m.Str("userId") != me)
                .Select(m => m.Str("displayName"))
                .OfType<string>()
                .ToList();
            var topic = ApiJson.NonEmpty(c.Str("topic"));
            var type = c.Str("chatType") ?? "";
            result.Add(new ChatRef
            {
                AccountID = account.Id, ChatID = id,
                Name = topic ?? (others.Count == 0 ? "Chat" : string.Join(", ", others.Take(3))),
                Kind = type == "oneOnOne" ? ChatKind.direct : ChatKind.group,
                LastActive = ApiDates.GraphDate(c.Str("lastUpdatedDateTime")),
            });
        }
        return result;
    }
}
