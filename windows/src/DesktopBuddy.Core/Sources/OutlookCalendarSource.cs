namespace DesktopBuddy.Core;

// Port of OutlookCalendarSource in MicrosoftSources.swift: Outlook meetings about to start
// (from 10 minutes ago to `leadMinutes` from now), with the Teams join link.
// All-day, cancelled and declined events are skipped.

public static class OutlookCalendarSource
{
    public static async Task<FetchResult> FetchAsync(ApiClient api, int leadMinutes, DateTimeOffset? nowOverride = null, CancellationToken ct = default)
    {
        var acc = api.Account;
        var now = nowOverride ?? DateTimeOffset.UtcNow;
        var json = await api.GetAsync($"{GraphApi.Base}/me/calendarView", new Dictionary<string, string>
        {
            ["startDateTime"] = ApiDates.IsoPlain(now.AddMinutes(-10)),
            ["endDateTime"] = ApiDates.IsoPlain(now.AddMinutes(leadMinutes)),
            ["$select"] = "id,subject,start,isAllDay,isCancelled,organizer,onlineMeeting,webLink,responseStatus",
            ["$orderby"] = "start/dateTime", ["$top"] = "20",
        }, new Dictionary<string, string> { ["Prefer"] = "outlook.timezone=\"UTC\"" }, ct).ConfigureAwait(false);

        var items = new List<BuddyItem>();
        foreach (var ev in json.Objs("value"))
        {
            if (ev.Str("id") is not { } id
                || ev.Bool("isAllDay") == true || ev.Bool("isCancelled") == true
                || ev.Obj("start").Str("dateTime") is not { } startStr
                || ApiDates.GraphDate(startStr) is not { } start || start < now.AddMinutes(-10)) continue;
            if (ev.Obj("responseStatus").Str("response") == "declined") continue;
            var join = ApiJson.Url(ev.Obj("onlineMeeting").Str("joinUrl"));
            var when = ApiDates.WhenText(start, now);
            var organizer = ev.Obj("organizer").Obj("emailAddress").Str("name");
            items.Add(new BuddyItem
            {
                Id = $"meet:{acc.Id}:{id}:{startStr}", Source = ItemSource.meeting, AccountID = acc.Id, AccountLabel = acc.Label,
                Title = ev.Str("subject") ?? "Meeting", From = organizer,
                Detail = when + (join is not null ? " · Teams" : ""), Date = start,
                Url = join ?? ApiJson.Url(ev.Str("webLink")),
            });
        }
        return new FetchResult { Source = ItemSource.meeting, Items = items, LiveIDs = items.Select(i => i.Id).ToHashSet() };
    }
}
