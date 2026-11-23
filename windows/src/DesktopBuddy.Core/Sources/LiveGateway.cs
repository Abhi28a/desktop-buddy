namespace DesktopBuddy.Core;

// The live IServiceGateway: dispatches by provider and source exactly like the switch in
// Poller.poll (Poller.swift) and answers IsSignedIn from the saved refresh token.
// Install it once at startup:  Gateway.Current = new LiveGateway(knownMailIds, currentChatIds);

public sealed class LiveGateway : IServiceGateway
{
    private readonly OAuth _oauth;
    private readonly HttpClient? _http;
    private readonly ChatSource _chat;
    private readonly TeamsChatSource _teams;

    /// IDs of mail the app already shows or has muted ("mail:<account>:<id>"). Gmail doesn't fetch these
    /// messages again. Called on a background thread: make it thread-safe. Default: nothing is known.
    public Func<IReadOnlySet<string>>? KnownMailIds { get; set; }

    /// IDs of the chat items the app shows now for an account (Google Chat has no "unread" flag, so these stay
    /// live until cleared). Called on a background thread: make it thread-safe. Default: none.
    public Func<string, IEnumerable<string>>? CurrentChatIds { get; set; }

    public LiveGateway(Func<IReadOnlySet<string>>? knownMailIds = null, Func<string, IEnumerable<string>>? currentChatIds = null,
                       OAuth? oauth = null, ChatSource? chat = null, TeamsChatSource? teams = null, HttpClient? http = null)
    {
        KnownMailIds = knownMailIds;
        CurrentChatIds = currentChatIds;
        _oauth = oauth ?? OAuth.Shared;
        _chat = chat ?? ChatSource.Shared;
        _teams = teams ?? TeamsChatSource.Shared;
        _http = http;
    }

    /// A refresh token is saved for this account.
    public bool IsSignedIn(AccountConfig account) => _oauth.IsSignedIn(account);

    public Task<FetchResult> FetchAsync(AccountConfig account, ItemSource source, int meetingLeadMinutes, CancellationToken ct = default)
    {
        var api = new ApiClient(account, _oauth, _http);
        if (account.Provider == Provider.google)
        {
            return source switch
            {
                ItemSource.mail => GmailSource.FetchAsync(api, KnownMailIds?.Invoke(), ct: ct),
                ItemSource.chat => _chat.FetchAsync(api, CurrentChatIds?.Invoke(account.Id), ct: ct),
                ItemSource.meeting => CalendarSource.FetchAsync(api, meetingLeadMinutes, ct: ct),
                ItemSource.task => TasksSource.FetchAsync(api, ct: ct),
                _ => throw new ArgumentException($"{source} is not fetched from the network", nameof(source)),
            };
        }
        return source switch
        {
            ItemSource.mail => OutlookMailSource.FetchAsync(api, ct: ct),
            ItemSource.chat => _teams.FetchAsync(api, ct: ct),
            ItemSource.meeting => OutlookCalendarSource.FetchAsync(api, meetingLeadMinutes, ct: ct),
            ItemSource.task => ToDoSource.FetchAsync(api, ct: ct),
            _ => throw new ArgumentException($"{source} is not fetched from the network", nameof(source)),
        };
    }
}
