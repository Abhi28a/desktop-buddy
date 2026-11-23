namespace DesktopBuddy.Core;

// Port of CalendarSource in GoogleSources.swift: Google Calendar meetings about to start
// (from 10 minutes ago to `leadMinutes` from now). All-day and declined events are skipped.

public static class CalendarSource
{
    public static async Task<FetchResult> FetchAsync(ApiClient api, int leadMinutes, DateTimeOffset? nowOverride = null, CancellationToken ct = default)
    {
        var acc = api.Account;
        var now = nowOverride ?? DateTimeOffset.UtcNow;
        var json = await api.GetAsync("https://www.googleapis.com/calendar/v3/calendars/primary/events", new Dictionary<string, string>
        {
            ["timeMin"] = ApiDates.IsoPlain(now.AddMinutes(-10)),
            ["timeMax"] = ApiDates.IsoPlain(now.AddMinutes(leadMinutes)),
            ["singleEvents"] = "true", ["orderBy"] = "startTime", ["maxResults"] = "20",
        }, ct: ct).ConfigureAwait(false);

        var items = new List<BuddyItem>();
        foreach (var ev in json.Objs("items"))
        {
            if (ev.Str("id") is not { } id
                || ev.Obj("start").Str("dateTime") is not { } startStr          // skip all-day events
                || ApiDates.ParseDate(startStr) is not { } start) continue;
            if (ev.Objs("attendees").Any(a => a.Bool("self") == true && a.Str("responseStatus") == "declined")) continue;
            // Started more than 10 min ago -> no longer relevant.
            if (start < now.AddMinutes(-10)) continue;

            var meet = ApiJson.Url(ev.Str("hangoutLink"));
            var link = meet ?? ApiJson.Url(ev.Str("htmlLink"));
            var when = ApiDates.WhenText(start, now);
            var organizerObj = ev.Obj("organizer");
            var organizer = organizerObj is null ? null : (organizerObj.Str("displayName") ?? organizerObj.Str("email"));
            items.Add(new BuddyItem
            {
                Id = $"meet:{acc.Id}:{id}:{startStr}", Source = ItemSource.meeting, AccountID = acc.Id, AccountLabel = acc.Label,
                Title = ev.Str("summary") ?? "Meeting", From = organizer,
                Detail = when + (meet is not null ? " · Google Meet" : ""), Date = start, Url = link,
            });
        }
        return new FetchResult { Source = ItemSource.meeting, Items = items, LiveIDs = items.Select(i => i.Id).ToHashSet() };
    }
}
