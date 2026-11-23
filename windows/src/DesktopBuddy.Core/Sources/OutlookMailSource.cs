namespace DesktopBuddy.Core;

// Port of OutlookMailSource in MicrosoftSources.swift: unread mail in the Inbox from the last 2 days
// through Microsoft Graph v1.0 (or, with "only these categories", unread mail with each category in any
// folder). Up to 25 per query.

public static class GraphApi
{
    public const string Base = "https://graph.microsoft.com/v1.0";
}

public static class OutlookMailSource
{
    public static async Task<FetchResult> FetchAsync(ApiClient api, DateTimeOffset? nowOverride = null, CancellationToken ct = default)
    {
        var acc = api.Account;
        var now = nowOverride ?? DateTimeOffset.UtcNow;
        var messages = new List<System.Text.Json.Nodes.JsonObject>();
        foreach (var (path, query) in ListQueries(acc, now.AddDays(-2)))
            messages.AddRange((await api.GetAsync(path, query, ct: ct).ConfigureAwait(false)).Objs("value"));

        var items = new List<BuddyItem>();
        foreach (var m in messages)
        {
            if (m.Str("id") is not { } id || items.Any(i => i.Id == $"mail:{acc.Id}:{id}")) continue;
            var from = m.Obj("from").Obj("emailAddress");
            var name = ApiJson.NonEmpty(from.Str("name")) ?? from.Str("address");
            items.Add(new BuddyItem
            {
                Id = $"mail:{acc.Id}:{id}", Source = ItemSource.mail, AccountID = acc.Id, AccountLabel = acc.Label,
                Title = ApiJson.NonEmpty(m.Str("subject")) ?? "(no subject)",
                From = name ?? "Unknown sender", Detail = m.Str("bodyPreview"),
                Date = ApiDates.GraphDate(m.Str("receivedDateTime")) ?? now,
                Url = ApiJson.Url(m.Str("webLink")),
                FromAddress = from.Str("address")?.ToLowerInvariant(),
            });
        }
        items = [.. items.OrderByDescending(i => i.Date)];
        return new FetchResult { Source = ItemSource.mail, Items = items, LiveIDs = items.Select(i => i.Id).ToHashSet() };
    }

    /// Unread inbox mail; or, with "only these categories", unread mail with each category in any folder.
    public static List<(string Path, Dictionary<string, string> Query)> ListQueries(AccountConfig acc, DateTimeOffset since)
    {
        var baseFilter = $"receivedDateTime ge {ApiDates.IsoPlain(since)} and isRead eq false";
        const string select = "id,subject,from,bodyPreview,receivedDateTime,webLink";
        if (!acc.LabelsOnly)
        {
            return [($"{GraphApi.Base}/me/mailFolders/inbox/messages", new()
            {
                ["$filter"] = baseFilter, ["$orderby"] = "receivedDateTime desc", ["$select"] = select, ["$top"] = "25",
            })];
        }
        // Graph can't sort a category filter, so results are sorted after merging.
        return acc.OnlyLabels.Select(c =>
        {
            var name = c.Name.Replace("'", "''");
            return ($"{GraphApi.Base}/me/messages", new Dictionary<string, string>
            {
                ["$filter"] = $"{baseFilter} and categories/any(c:c eq '{name}')", ["$select"] = select, ["$top"] = "25",
            });
        }).ToList();
    }

    /// Categories used on your recent mail. (The full category list needs a permission Desktop Buddy doesn't ask for.)
    public static async Task<List<MailLabel>> CategoriesAsync(ApiClient api, CancellationToken ct = default)
    {
        var json = await api.GetAsync($"{GraphApi.Base}/me/messages", new Dictionary<string, string>
        {
            ["$select"] = "categories", ["$top"] = "200", ["$orderby"] = "receivedDateTime desc",
        }, ct: ct).ConfigureAwait(false);
        var names = json.Objs("value").SelectMany(v => v.Strs("categories")).ToHashSet().ToList();
        names.Sort(ApiText.LocalizedStandardCompare);
        return names.Select(n => new MailLabel { Id = n, Name = n }).ToList();
    }
}
