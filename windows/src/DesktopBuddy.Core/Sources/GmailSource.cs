namespace DesktopBuddy.Core;

// Port of GmailSource in GoogleSources.swift: unread inbox mail from the last 2 days (or, with
// "only these labels", unread mail with each label wherever it's filed). Up to 25 per query.

public static class GmailSource
{
    /// `existingIds` are the IDs the app already shows or has muted ("mail:<account>:<id>"): their
    /// message bodies aren't fetched again, but they still count as live.
    public static async Task<FetchResult> FetchAsync(ApiClient api, IReadOnlySet<string>? existingIds = null,
                                                     DateTimeOffset? now = null, CancellationToken ct = default)
    {
        var acc = api.Account;
        var when = now ?? DateTimeOffset.UtcNow;
        var ids = new List<string>();
        foreach (var (label, query) in ListQueries(acc))
        {
            System.Text.Json.Nodes.JsonObject list;
            try
            {
                list = await api.GetAsync("https://gmail.googleapis.com/gmail/v1/users/me/messages", query, ct: ct).ConfigureAwait(false);
            }
            catch (ApiException e) when (label is not null && (e.Status == 400 || e.Status == 404))
            {
                throw new ApiException(e.Status, e.Host, $"The label “{label}” no longer exists in Gmail. Remove it under Mail.");
            }
            foreach (var m in list.Objs("messages"))
                if (m.Str("id") is { } id && !ids.Contains(id)) ids.Add(id);
        }
        var live = ids.Select(id => $"mail:{acc.Id}:{id}").ToHashSet();

        var items = new List<BuddyItem>();
        foreach (var id in ids)
        {
            if (existingIds?.Contains($"mail:{acc.Id}:{id}") == true) continue;
            var msg = await api.GetAsync(
                $"https://gmail.googleapis.com/gmail/v1/users/me/messages/{id}?format=metadata&metadataHeaders=From&metadataHeaders=Subject", ct: ct).ConfigureAwait(false);
            var rawFrom = Header(msg, "From") ?? "";
            var from = rawFrom.Length == 0 ? "Unknown sender" : CleanSender(rawFrom);
            var subject = Header(msg, "Subject") ?? "(no subject)";
            var snippet = msg.Str("snippet") is { } sn ? ApiText.DecodeEntities(sn) : null;
            var ms = msg.Num("internalDate") ?? when.ToUnixTimeMilliseconds();
            // internalDate is a string of milliseconds; Num() reads numeric strings.
            items.Add(new BuddyItem
            {
                Id = $"mail:{acc.Id}:{id}", Source = ItemSource.mail, AccountID = acc.Id, AccountLabel = acc.Label,
                Title = subject, From = from, Detail = snippet,
                Date = DateTimeOffset.FromUnixTimeMilliseconds((long)ms),
                Url = ApiJson.Url($"https://mail.google.com/mail/u/?authuser={acc.Email}#all/{msg.Str("threadId") ?? id}"),
                FromAddress = SenderAddress(rawFrom),
            });
        }
        return new FetchResult { Source = ItemSource.mail, Items = items, LiveIDs = live };
    }

    /// Unread inbox mail; or, with "only these labels", unread mail with each label wherever it's filed.
    /// Each query comes with the label name it's for (null for the inbox), to explain errors.
    public static List<(string? Label, Dictionary<string, string> Query)> ListQueries(AccountConfig acc)
    {
        if (!acc.LabelsOnly)
            return [(null, new() { ["q"] = "in:inbox is:unread newer_than:2d", ["maxResults"] = "25" })];
        return acc.OnlyLabels
            .Select(l => ((string?)l.Name, new Dictionary<string, string> { ["labelIds"] = l.Id, ["q"] = "is:unread newer_than:2d", ["maxResults"] = "25" }))
            .ToList();
    }

    /// Your own labels (not Gmail's built-in ones), A to Z.
    public static async Task<List<MailLabel>> LabelsAsync(ApiClient api, CancellationToken ct = default)
    {
        var json = await api.GetAsync("https://gmail.googleapis.com/gmail/v1/users/me/labels", ct: ct).ConfigureAwait(false);
        var labels = new List<MailLabel>();
        foreach (var l in json.Objs("labels"))
        {
            if (l.Str("type") == "user" && l.Str("id") is { } id && l.Str("name") is { } name)
                labels.Add(new MailLabel { Id = id, Name = name });
        }
        labels.Sort((a, b) => ApiText.LocalizedStandardCompare(a.Name, b.Name));
        return labels;
    }

    /// "Priya <priya@x.com>" -> "priya@x.com"
    public static string SenderAddress(string raw)
    {
        var lt = raw.IndexOf('<');
        var gt = raw.LastIndexOf('>');
        if (lt >= 0 && gt >= 0 && lt < gt)
            return raw[(lt + 1)..gt].ToLowerInvariant();
        return ApiText.TrimSpaces(raw).ToLowerInvariant();
    }

    private static string? Header(System.Text.Json.Nodes.JsonObject msg, string name)
    {
        foreach (var h in msg.Obj("payload").Objs("headers"))
            if (string.Equals(h.Str("name"), name, StringComparison.OrdinalIgnoreCase) && h.Str("value") is { } v)
                return v;
        return null;
    }

    /// "Priya Shah <priya@x.com>" -> "Priya Shah"
    public static string CleanSender(string raw)
    {
        var lt = raw.IndexOf('<');
        if (lt >= 0)
        {
            var name = ApiText.TrimSpaces(raw[..lt]).Trim('"');
            if (name.Length > 0) return name;
            var rest = raw[(lt + 1)..];
            return rest.Length > 0 ? rest[..^1] : rest;
        }
        return raw;
    }
}
