namespace DesktopBuddy.Core;

// Port of ToDoSource in MicrosoftSources.swift: Microsoft To Do tasks due today or overdue,
// one reminder per task per day.

public static class ToDoSource
{
    public static async Task<FetchResult> FetchAsync(ApiClient api, DateTimeOffset? nowOverride = null, CancellationToken ct = default)
    {
        var acc = api.Account;
        var now = nowOverride ?? DateTimeOffset.UtcNow;
        var endOfToday = ApiDates.EndOfToday(now);

        var lists = await api.GetAsync($"{GraphApi.Base}/me/todo/lists", ct: ct).ConfigureAwait(false);
        var items = new List<BuddyItem>();
        foreach (var list in lists.Objs("value"))
        {
            if (list.Str("id") is not { } listID) continue;
            var tasks = await api.GetAsync($"{GraphApi.Base}/me/todo/lists/{listID}/tasks", new Dictionary<string, string>
            {
                ["$filter"] = "status ne 'completed'", ["$top"] = "100",
            }, ct: ct).ConfigureAwait(false);
            foreach (var t in tasks.Objs("value"))
            {
                if (t.Str("id") is not { } id || t.Str("title") is not { Length: > 0 } title
                    || ApiDates.GraphDate(t.Obj("dueDateTime").Str("dateTime")) is not { } due
                    || due >= endOfToday) continue;
                var overdue = ApiDates.StartOfLocalDay(due) < ApiDates.StartOfLocalDay(now);
                items.Add(new BuddyItem
                {
                    Id = $"task:{acc.Id}:{id}:{ApiDates.Day(now)}", Source = ItemSource.task,
                    AccountID = acc.Id, AccountLabel = acc.Label,
                    Title = title, From = list.Str("displayName"),
                    Detail = overdue ? $"Overdue (was due {ApiDates.Day(due)})" : "Due today",
                    Date = now, Url = "https://to-do.office.com/tasks/",
                });
            }
        }
        return new FetchResult { Source = ItemSource.task, Items = items, LiveIDs = items.Select(i => i.Id).ToHashSet() };
    }
}
