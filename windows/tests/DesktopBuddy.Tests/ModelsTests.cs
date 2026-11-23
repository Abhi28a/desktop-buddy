using DesktopBuddy.Core;

namespace DesktopBuddy.Tests;

/// The settings file must load on both platforms, and old files must still load.
public class ModelsTests
{
    [Fact]
    public void MacConfigLoads()
    {
        const string mac = """
        {
          "accounts" : [ { "clientID" : "", "email" : "you@example.com", "enabled" : true, "id" : "A1",
                           "label" : "Example", "providerChoice" : "microsoft", "redirectPort" : 8766,
                           "watchChat" : true, "watchMail" : true, "watchMeetings" : false, "watchTasks" : true,
                           "mutedSendersList" : [ "news.example.com" ] } ],
          "clientsList" : [ { "accountIDs" : [], "chats" : [ { "accountID" : "A1", "chatID" : "19:abc", "name" : "Project", "kind" : "group" } ],
                              "enabled" : true, "id" : "C1", "keywords" : [ "Acme" ], "matchChatNames" : true,
                              "name" : "Acme", "senders" : [ "acme.com" ] } ],
          "dockSpotChoice" : "leftMiddle",
          "meetingLeadMinutes" : 10, "paused" : false, "playSound" : true, "pollSeconds" : 60,
          "reminders" : [ { "emoji" : "💧", "enabled" : true, "endHour" : 18, "everyMinutes" : 30,
                            "id" : "6F9619FF-8B86-D011-B42D-00C04FC964FF", "note" : "", "startHour" : 9,
                            "title" : "Water", "weekdays" : [ 2, 3 ], "lastFired" : "2026-11-14T09:30:00Z" } ]
        }
        """;
        var cfg = JsonFile.Decode<AppConfig>(mac)!;
        var acct = Assert.Single(cfg.Accounts);
        Assert.Equal(Provider.microsoft, acct.Provider);
        Assert.Equal(AccountTint.purple, acct.Tint);           // Microsoft default tint
        Assert.False(acct.WatchMeetings);
        Assert.Equal(["news.example.com"], acct.MutedSenders);
        Assert.False(acct.LabelsOnly);
        var client = Assert.Single(cfg.Clients);
        Assert.Equal(ChatKind.group, client.Chats[0].Kind);
        Assert.Equal(DockSpot.leftBottom, cfg.DockSpot);        // old place is mapped
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 9, 30, 0, TimeSpan.Zero), cfg.Reminders[0].LastFired);
        Assert.True(cfg.HidesFromSharing);                      // missing = on
        Assert.True(cfg.CheckForUpdates);
    }

    [Fact]
    public void RoundTripKeepsKeysAndDates()
    {
        var cfg = new AppConfig();
        cfg.Accounts.Add(new AccountConfig { Label = "Work", Email = "me@work.example" });
        cfg.DockSpot = DockSpot.rightTop;
        cfg.SetSound(AppConfig.NoSound, ItemSource.chat);
        var json = JsonFile.Encode(cfg);
        Assert.Contains("\"dockSpotChoice\": \"rightTop\"", json);
        Assert.DoesNotContain("\"providerChoice\"", json);      // nulls are left out, like Swift
        var back = JsonFile.Decode<AppConfig>(json)!;
        Assert.Equal(DockSpot.rightTop, back.DockSpot);
        Assert.Equal(AppConfig.NoSound, back.Sound(ItemSource.chat));
        Assert.Equal(AppConfig.DefaultSound, back.Sound(ItemSource.mail));

        var item = new BuddyItem { Id = "mail:a:1", Source = ItemSource.mail, Title = "Hi", Date = new DateTimeOffset(2026, 1, 2, 3, 4, 5, 678, TimeSpan.Zero) };
        Assert.Contains("\"date\": \"2026-01-02T03:04:05Z\"", JsonFile.Encode(item));
    }

    [Fact]
    public void ChatRefIdentityIsTheChat()
    {
        var a = new ChatRef { AccountID = "A", ChatID = "spaces/1", Name = "Old" };
        var b = new ChatRef { AccountID = "A", ChatID = "spaces/1", Name = "New name" };
        Assert.Equal(a, b);
        Assert.Single(new HashSet<ChatRef> { a, b });
    }

    [Fact]
    public void OwnerPrefersClientThenAccount()
    {
        Assert.Equal("C", new BuddyItem { ClientID = "C", AccountID = "A" }.Owner);
        Assert.Equal("A", new BuddyItem { AccountID = "A" }.Owner);
        Assert.Equal(BuddyItem.ReminderOwner, new BuddyItem().Owner);
    }

    [Fact]
    public void TeamConfigNeedsOnlyClientIDs()
    {
        Assert.True(new TeamConfig { GoogleClientID = "x.apps.googleusercontent.com" }.HasClient(Provider.google));
        Assert.False(new TeamConfig().HasClient(Provider.microsoft));
    }
}
