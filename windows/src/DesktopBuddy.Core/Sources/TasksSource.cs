namespace DesktopBuddy.Core;

// Port of TasksSource in GoogleSources.swift: Google Tasks due today or overdue, one reminder per task per day.

public static class TasksSource
{
    public static async Task<FetchResult> FetchAsync(ApiClient api, DateTimeOffset? nowOverride = null, CancellationToken ct = default)
    {
        var acc = api.Account;
        var now = nowOverride ?? DateTimeOffset.UtcNow;
        var endOfToday = ApiDates.EndOfToday(now);

        var lists = await api.GetAsync("https://tasks.googleapis.com/tasks/v1/users/@me/lists",
            new Dictionary<string, string> { ["maxResults"] = "100" }, ct: ct).ConfigureAwait(false);
        var items = new List<BuddyItem>();
        foreach (var list in lists.Objs("items"))
        {
            if (list.Str("id") is not { } listID) continue;
            var tasks = await api.GetAsync($"https://tasks.googleapis.com/tasks/v1/lists/{listID}/tasks", new Dictionary<string, string>
            {
                ["showCompleted"] = "false", ["dueMax"] = ApiDates.IsoPlain(endOfToday), ["maxResults"] = "100",
            }, ct: ct).ConfigureAwait(false);
            foreach (var t in tasks.Objs("items"))
            {
                if (t.Str("id") is not { } id || t.Str("title") is not { Length: > 0 } title
                    || ApiDates.ParseDate(t.Str("due")) is not { } due) continue;
                var overdue = ApiDates.StartOfLocalDay(due) < ApiDates.StartOfLocalDay(now);
                var parts = new List<string> { overdue ? $"Overdue (was due {ApiDates.Day(due)})" : "Due today" };
                if (t.Str("notes") is { } notes) parts.Add(notes);
                // One reminder per task per day.
                items.Add(new BuddyItem
                {
                    Id = $"task:{acc.Id}:{id}:{ApiDates.Day(now)}", Source = ItemSource.task, AccountID = acc.Id, AccountLabel = acc.Label,
                    Title = title, From = list.Str("title"), Detail = string.Join(" · ", parts), Date = now,
                    Url = "https://tasks.google.com/",
                });
            }
        }
        return new FetchResult { Source = ItemSource.task, Items = items, LiveIDs = items.Select(i => i.Id).ToHashSet() };
    }
}
