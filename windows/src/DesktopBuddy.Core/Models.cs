using System.Text.Json.Serialization;

namespace DesktopBuddy.Core;

// Port of Sources/DesktopBuddy/Models.swift.
// JSON property names match the Mac app's Codable keys exactly, so a config.json
// written on one platform loads on the other.

[JsonConverter(typeof(JsonStringEnumConverter<ItemSource>))]
public enum ItemSource { mail, chat, meeting, task, reminder }

public static class ItemSourceInfo
{
    public static readonly ItemSource[] All = [ItemSource.mail, ItemSource.chat, ItemSource.meeting, ItemSource.task, ItemSource.reminder];

    public static string Label(this ItemSource s) => s switch
    {
        ItemSource.mail => "Mail",
        ItemSource.chat => "Chat",
        ItemSource.meeting => "Meeting",
        ItemSource.task => "Task",
        _ => "Reminder",
    };

    /// Segoe Fluent Icons glyph for each kind (the Mac uses SF Symbols).
    public static string Glyph(this ItemSource s) => s switch
    {
        ItemSource.mail => "",     // Mail
        ItemSource.chat => "",     // Chat bubbles
        ItemSource.meeting => "",  // Video
        ItemSource.task => "",     // Checklist
        _ => "",                   // Ringer
    };

    /// Tint as 0xRRGGBB, matching the Mac's system colours.
    public static uint Tint(this ItemSource s) => s switch
    {
        ItemSource.mail => 0x007AFF,
        ItemSource.chat => 0x34C759,
        ItemSource.meeting => 0x5856D6,
        ItemSource.task => 0xFF9500,
        _ => 0x30B0C7,
    };
}

/// One thing Desktop Buddy is telling you about.
public sealed record BuddyItem
{
    public const string ReminderOwner = "reminders";

    [JsonPropertyName("id")] public string Id { get; set; } = "";                 // stable key, e.g. "mail:<accountID>:<messageId>"
    [JsonPropertyName("source")] public ItemSource Source { get; set; }
    [JsonPropertyName("accountID")] public string? AccountID { get; set; }         // null for local reminders
    [JsonPropertyName("accountLabel")] public string? AccountLabel { get; set; }   // display name at the time it arrived
    [JsonPropertyName("title")] public string Title { get; set; } = "";           // subject / chat name / task / event / reminder
    [JsonPropertyName("from")] public string? From { get; set; }                   // sender for mail & chat
    [JsonPropertyName("detail")] public string? Detail { get; set; }               // snippet / notes / time
    [JsonPropertyName("date")] public DateTimeOffset Date { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }                     // where "Open" goes
    [JsonPropertyName("fromAddress")] public string? FromAddress { get; set; }     // mail sender address, for client rules
    [JsonPropertyName("chatRef")] public string? ChatRef { get; set; }             // Google Chat space name / Teams chat ID, for client rules
    [JsonPropertyName("clientID")] public string? ClientID { get; set; }           // set when a client rule matched

    /// Who shows this item: a matched client first, then the account, else your reminders.
    [JsonIgnore] public string Owner => ClientID ?? AccountID ?? ReminderOwner;
}

// MARK: - Accounts

[JsonConverter(typeof(JsonStringEnumConverter<Provider>))]
public enum Provider { google, microsoft }

public static class ProviderInfo
{
    public static string Name(this Provider p) => p == Provider.google ? "Google Workspace" : "Microsoft 365";
    public static string Services(this Provider p) => p == Provider.google
        ? "Gmail, Google Chat, Calendar and Tasks"
        : "Outlook, Teams chats, Calendar and To Do";
    public static string ChatName(this Provider p) => p == Provider.google ? "Google Chat" : "Teams chats";
    public static string MailName(this Provider p) => p == Provider.google ? "Gmail" : "Outlook mail";
    public static string TasksName(this Provider p) => p == Provider.google ? "Google Tasks" : "Microsoft To Do";
}

/// Accent colours an account or client can use.
[JsonConverter(typeof(JsonStringEnumConverter<AccountTint>))]
public enum AccountTint { blue, purple, pink, red, orange, yellow, green, teal, graphite }

public static class AccountTintInfo
{
    public static readonly AccountTint[] All = Enum.GetValues<AccountTint>();

    /// 0xRRGGBB, same values as the Mac app.
    public static uint Rgb(this AccountTint t) => t switch
    {
        AccountTint.blue => 0x007AFF,
        AccountTint.purple => 0x9452DB,
        AccountTint.pink => 0xF74F9E,
        AccountTint.red => 0xED4545,
        AccountTint.orange => 0xF78721,
        AccountTint.yellow => 0xF5C21A,
        AccountTint.green => 0x33B854,
        AccountTint.teal => 0x1FA8B3,
        _ => 0x8C8C8C,
    };
}

public sealed record AccountConfig
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString().ToUpperInvariant();
    [JsonPropertyName("providerChoice")] public Provider? ProviderChoice { get; set; }    // null = google (accounts made by v1)
    [JsonPropertyName("label")] public string Label { get; set; } = "";                   // e.g. "Dynamatix", "Acme client"
    [JsonPropertyName("email")] public string Email { get; set; } = "";
    [JsonPropertyName("tintChoice")] public AccountTint? TintChoice { get; set; }
    [JsonPropertyName("logoPath")] public string? LogoPath { get; set; }                  // custom logo; otherwise a bundled one matched by name
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("watchMail")] public bool WatchMail { get; set; } = true;
    [JsonPropertyName("watchChat")] public bool WatchChat { get; set; } = true;
    [JsonPropertyName("watchMeetings")] public bool WatchMeetings { get; set; } = true;
    [JsonPropertyName("watchTasks")] public bool WatchTasks { get; set; } = true;

    // Advanced: per-account OAuth client (otherwise the team default is used).
    [JsonPropertyName("clientID")] public string ClientID { get; set; } = "";
    [JsonPropertyName("redirectPort")] public int RedirectPort { get; set; } = 8766;
    [JsonPropertyName("tenant")] public string? Tenant { get; set; }                      // Microsoft: "organizations", "common" or a tenant ID

    // Mail filters (nullable so older settings files still load).
    [JsonPropertyName("mutedSendersList")] public List<string>? MutedSendersList { get; set; }
    [JsonPropertyName("mutedKeywordsList")] public List<string>? MutedKeywordsList { get; set; }
    [JsonPropertyName("onlyLabelsList")] public List<MailLabel>? OnlyLabelsList { get; set; }
    [JsonPropertyName("mutedChatsList")] public List<ChatRef>? MutedChatsList { get; set; }

    [JsonIgnore]
    public List<string> MutedSenders
    {
        get => MutedSendersList ?? [];
        set => MutedSendersList = value.Count == 0 ? null : value;
    }
    [JsonIgnore]
    public List<string> MutedKeywords
    {
        get => MutedKeywordsList ?? [];
        set => MutedKeywordsList = value.Count == 0 ? null : value;
    }
    [JsonIgnore]
    public List<ChatRef> MutedChats
    {
        get => MutedChatsList ?? [];
        set => MutedChatsList = value.Count == 0 ? null : value;
    }
    [JsonIgnore]
    public List<MailLabel> OnlyLabels
    {
        get => OnlyLabelsList ?? [];
        set => OnlyLabelsList = value;
    }
    /// "Only mail with these labels" is chosen (even before any label is picked).
    [JsonIgnore] public bool LabelsOnly => OnlyLabelsList != null;

    [JsonIgnore]
    public Provider Provider
    {
        get => ProviderChoice ?? Provider.google;
        set => ProviderChoice = value;
    }
    [JsonIgnore]
    public AccountTint Tint
    {
        get => TintChoice ?? (Provider == Provider.microsoft ? AccountTint.purple : AccountTint.blue);
        set => TintChoice = value;
    }
}

/// A Gmail label (Id is Google's label ID) or an Outlook category (Id is its name).
public sealed record MailLabel
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
}

// MARK: - Clients

[JsonConverter(typeof(JsonStringEnumConverter<ChatKind>))]
public enum ChatKind { direct, group, space }

/// A chat picked for a client (Google Chat space or Teams chat).
/// Identity is the chat itself (account + chat ID); name and activity can change between lists.
public sealed class ChatRef : IEquatable<ChatRef>
{
    [JsonPropertyName("accountID")] public string AccountID { get; set; } = "";
    [JsonPropertyName("chatID")] public string ChatID { get; set; } = "";     // "spaces/AAA…" or Teams chat ID
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("kind")] public ChatKind? Kind { get; set; }
    [JsonPropertyName("lastActive")] public DateTimeOffset? LastActive { get; set; }

    [JsonIgnore] public string Id => AccountID + "|" + ChatID;

    public bool Equals(ChatRef? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => obj is ChatRef c && Equals(c);
    public override int GetHashCode() => Id.GetHashCode(StringComparison.Ordinal);
    public static bool operator ==(ChatRef? a, ChatRef? b) => a is null ? b is null : a.Equals(b);
    public static bool operator !=(ChatRef? a, ChatRef? b) => !(a == b);
}

/// A client or workspace you want called out separately, even when they don't give you an account:
/// their mails (by sender or keyword) and chats show up under their own logo.
public sealed record ClientConfig
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString().ToUpperInvariant();
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("logoPath")] public string? LogoPath { get; set; }
    [JsonPropertyName("tintChoice")] public AccountTint? TintChoice { get; set; }
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("senders")] public List<string> Senders { get; set; } = [];      // "gatehouse.co.uk" or "jane@client.com"
    [JsonPropertyName("keywords")] public List<string> Keywords { get; set; } = [];    // matched in subject and preview of unread inbox mail
    [JsonPropertyName("chats")] public List<ChatRef> Chats { get; set; } = [];
    [JsonPropertyName("matchChatNames")] public bool MatchChatNames { get; set; } = true;  // also chats whose name contains the client name
    [JsonPropertyName("accountIDs")] public List<string> AccountIDs { get; set; } = [];    // where to look; empty = every account

    [JsonIgnore]
    public AccountTint Tint
    {
        get => TintChoice ?? AccountTint.green;
        set => TintChoice = value;
    }
    [JsonIgnore] public bool HasRules => Senders.Count > 0 || Keywords.Count > 0 || Chats.Count > 0 || MatchChatNames;
}

public sealed record Reminder
{
    [JsonPropertyName("id")] public Guid Id { get; set; } = Guid.NewGuid();
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("emoji")] public string Emoji { get; set; } = "💧";
    [JsonPropertyName("note")] public string Note { get; set; } = "";
    [JsonPropertyName("everyMinutes")] public int EveryMinutes { get; set; } = 30;
    [JsonPropertyName("startHour")] public int StartHour { get; set; } = 9;
    [JsonPropertyName("endHour")] public int EndHour { get; set; } = 18;
    /// Mac Calendar weekday numbers: 1 = Sunday … 7 = Saturday (kept for config compatibility).
    [JsonPropertyName("weekdays")] public HashSet<int> Weekdays { get; set; } = [2, 3, 4, 5, 6];
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("lastFired")] public DateTimeOffset? LastFired { get; set; }

    /// Converts .NET's DayOfWeek (Sunday = 0) to the Mac weekday number (Sunday = 1).
    public static int MacWeekday(DayOfWeek d) => (int)d + 1;
}

// MARK: - Dock placement

/// Where the on-screen dock of logos sits. Text values match the Mac app.
public enum DockSpot { bottomLeft, bottomRight, leftTop, leftBottom, rightTop, rightBottom }

public enum DockEdge { bottom, left, right }

public static class DockSpotInfo
{
    public const DockSpot Standard = DockSpot.bottomRight;   // same as the Mac (Dock.swift)
    public static readonly DockSpot[] All = Enum.GetValues<DockSpot>();

    /// Reads a saved place, mapping places from older versions.
    public static DockSpot Saved(string? raw)
    {
        if (raw is null) return Standard;
        if (Enum.TryParse<DockSpot>(raw, ignoreCase: false, out var s) && Enum.IsDefined(s)) return s;
        return raw switch
        {
            "leftMiddle" => DockSpot.leftBottom,
            "rightMiddle" => DockSpot.rightBottom,
            _ => Standard,
        };
    }

    public static DockEdge Edge(this DockSpot s) => s switch
    {
        DockSpot.bottomLeft or DockSpot.bottomRight => DockEdge.bottom,
        DockSpot.leftTop or DockSpot.leftBottom => DockEdge.left,
        _ => DockEdge.right,
    };

    public static string Title(this DockSpot s) => s switch
    {
        DockSpot.bottomLeft => "Bottom, left corner",
        DockSpot.bottomRight => "Bottom, right corner",
        DockSpot.leftTop => "Left side, top",
        DockSpot.leftBottom => "Left side, bottom",
        DockSpot.rightTop => "Right side, top",
        _ => "Right side, bottom",
    };
}

// MARK: - App settings

public sealed record AppConfig
{
    public const string DefaultSound = "Default";
    public const string NoSound = "None";

    [JsonPropertyName("accounts")] public List<AccountConfig> Accounts { get; set; } = [];
    [JsonPropertyName("reminders")]
    public List<Reminder> Reminders { get; set; } =
    [
        new Reminder { Title = "Drink a glass of water", Emoji = "💧", Note = "Stay hydrated!", EveryMinutes = 30 },
    ];
    [JsonPropertyName("clientsList")] public List<ClientConfig>? ClientsList { get; set; }
    [JsonIgnore]
    public List<ClientConfig> Clients
    {
        get => ClientsList ??= [];
        set => ClientsList = value;
    }
    [JsonPropertyName("reminderLogoPath")] public string? ReminderLogoPath { get; set; }
    [JsonPropertyName("seededClients")] public bool? SeededClients { get; set; }
    [JsonPropertyName("dockScaleChoice")] public double? DockScaleChoice { get; set; }   // on-screen logo size, 0.8 … 1.6 (1 = 30 px)
    [JsonIgnore]
    public double DockScale
    {
        get => DockScaleChoice ?? 1;
        set => DockScaleChoice = value;
    }
    [JsonPropertyName("dockSpotChoice")] public string? DockSpotChoice { get; set; }     // a DockSpot as text, so old places still load
    [JsonIgnore]
    public DockSpot DockSpot
    {
        get => DockSpotInfo.Saved(DockSpotChoice);
        set => DockSpotChoice = value.ToString();
    }
    [JsonPropertyName("pollSeconds")] public int PollSeconds { get; set; } = 60;
    [JsonPropertyName("meetingLeadMinutes")] public int MeetingLeadMinutes { get; set; } = 10;
    [JsonPropertyName("quietStartHour")] public int? QuietStartHour { get; set; }        // e.g. 22
    [JsonPropertyName("quietEndHour")] public int? QuietEndHour { get; set; }            // e.g. 8
    [JsonPropertyName("playSound")] public bool PlaySound { get; set; } = true;
    [JsonPropertyName("soundChoices")] public Dictionary<string, string>? SoundChoices { get; set; }  // ItemSource -> sound name, or "None"

    public string Sound(ItemSource source) =>
        SoundChoices is not null && SoundChoices.TryGetValue(source.ToString(), out var s) ? s : DefaultSound;

    public void SetSound(string name, ItemSource source)
    {
        SoundChoices ??= [];
        SoundChoices[source.ToString()] = name;
    }

    [JsonPropertyName("paused")] public bool Paused { get; set; }
    [JsonPropertyName("shareProtection")] public bool? ShareProtection { get; set; }     // null = on: keep the logos out of screen sharing
    [JsonIgnore]
    public bool HidesFromSharing
    {
        get => ShareProtection ?? true;
        set => ShareProtection = value;
    }
    [JsonPropertyName("tourSeen")] public bool? TourSeen { get; set; }
    [JsonPropertyName("autoUpdateCheck")] public bool? AutoUpdateCheck { get; set; }     // null = on
    [JsonIgnore]
    public bool CheckForUpdates
    {
        get => AutoUpdateCheck ?? true;
        set => AutoUpdateCheck = value;
    }

    // MARK: Windows redesign settings (the Mac app ignores these keys)

    public const string DefaultBoardShortcut = "Ctrl+Alt+N";
    public const int MinPeekSeconds = 4;
    public const int MaxPeekSeconds = 30;
    public const int DefaultPeekSeconds = 10;

    [JsonPropertyName("boardShortcutChoice")] public string? BoardShortcutChoice { get; set; }   // global shortcut that opens the Board, e.g. "Ctrl+Alt+N"
    [JsonIgnore]
    public string BoardShortcut
    {
        get => string.IsNullOrWhiteSpace(BoardShortcutChoice) ? DefaultBoardShortcut : BoardShortcutChoice;
        set => BoardShortcutChoice = value;
    }
    [JsonPropertyName("peekChoice")] public bool? PeekChoice { get; set; }                       // null = on: show the Peek when something arrives
    [JsonIgnore]
    public bool PeekEnabled
    {
        get => PeekChoice ?? true;
        set => PeekChoice = value;
    }
    [JsonPropertyName("peekSecondsChoice")] public int? PeekSecondsChoice { get; set; }          // how long the Peek stays, 4 … 30
    [JsonIgnore]
    public int PeekSeconds
    {
        get => Math.Clamp(PeekSecondsChoice ?? DefaultPeekSeconds, MinPeekSeconds, MaxPeekSeconds);
        set => PeekSecondsChoice = Math.Clamp(value, MinPeekSeconds, MaxPeekSeconds);
    }
    [JsonPropertyName("railCompactChoice")] public bool? RailCompactChoice { get; set; }         // null = on: names only on hover or focus
    [JsonIgnore]
    public bool RailCompact
    {
        get => RailCompactChoice ?? true;
        set => RailCompactChoice = value;
    }
    [JsonPropertyName("respectWindowsFocusChoice")] public bool? RespectWindowsFocusChoice { get; set; }   // null = on: hold the Peek during Do not disturb, presenting, full screen
    [JsonIgnore]
    public bool RespectWindowsFocus
    {
        get => RespectWindowsFocusChoice ?? true;
        set => RespectWindowsFocusChoice = value;
    }
    [JsonPropertyName("pausedUntilChoice")] public DateTimeOffset? PausedUntilChoice { get; set; }   // "Pause for 1 hour" / "until tomorrow": null = no timed pause

    /// Paused for good, or paused until a time that hasn't come yet.
    public bool IsPausedAt(DateTimeOffset now) => Paused || (PausedUntilChoice is { } until && now < until);

    [JsonPropertyName("windowsNotificationsChoice")] public bool? WindowsNotificationsChoice { get; set; }  // null = off: also show in Windows notifications
    [JsonIgnore]
    public bool WindowsNotifications
    {
        get => WindowsNotificationsChoice ?? false;
        set => WindowsNotificationsChoice = value;
    }
}
