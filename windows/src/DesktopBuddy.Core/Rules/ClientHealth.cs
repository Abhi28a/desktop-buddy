namespace DesktopBuddy.Core;

// Port of Sources/DesktopBuddy/ClientHealth.swift.
// Problems that make a client rule silently not work, each with a one-click fix.
// One place decides these so the client page, the chat rows, the account page and
// the sidebar always agree.

public enum ClientFixKind { TurnOnClient, TurnOnAccount, TurnOnService, SignIn, AddToLookIn, RemoveChat, AddRule }

/// The one-click fix for an issue (Swift: ClientIssue.Fix, an enum with associated values).
public sealed record ClientFix(ClientFixKind Kind, string? AccountID = null, ItemSource? Source = null, ChatRef? Chat = null)
{
    public static readonly ClientFix TurnOnClient = new(ClientFixKind.TurnOnClient);
    public static readonly ClientFix AddRule = new(ClientFixKind.AddRule);
    public static ClientFix TurnOnAccount(string accountID) => new(ClientFixKind.TurnOnAccount, accountID);
    public static ClientFix TurnOnService(string accountID, ItemSource source) => new(ClientFixKind.TurnOnService, accountID, source);
    public static ClientFix SignIn(string accountID) => new(ClientFixKind.SignIn, accountID);
    public static ClientFix AddToLookIn(string accountID) => new(ClientFixKind.AddToLookIn, accountID);
    public static ClientFix RemoveChat(ChatRef chat) => new(ClientFixKind.RemoveChat, Chat: chat);
}

public sealed record ClientIssue
{
    public string Id { get; init; } = "";
    public string Text { get; init; } = "";
    public ClientFix? Fix { get; init; }
    public string? FixLabel { get; init; }
    /// Set when the issue is about one picked chat (shown on that row too).
    public ChatRef? Chat { get; init; }
}

/// Why a service on an account isn't being checked right now.
public readonly record struct Blocker(string Text, ClientFix Fix, string FixLabel);

public sealed partial class AppState
{
    /// The accounts a client looks in (all, or the ones picked under "Look in").
    public List<AccountConfig> LookIn(ClientConfig c) =>
        (c.AccountIDs.Count == 0 ? _config.Accounts : _config.Accounts.Where(a => c.AccountIDs.Contains(a.Id)))
            .Select(a => a.Clone()).ToList();

    /// Why a service on an account isn't being checked right now, or null if it is.
    public Blocker? BlockerFor(AccountConfig a, ItemSource source)
    {
        var svc = source == ItemSource.chat ? a.Provider.ChatName() : a.Provider.MailName();
        if (!a.Enabled) return new Blocker($"{a.Label} is turned off", ClientFix.TurnOnAccount(a.Id), $"Turn On {a.Label}");
        var on = source == ItemSource.chat ? a.WatchChat : a.WatchMail;
        if (!on) return new Blocker($"{svc} is turned off for {a.Label}", ClientFix.TurnOnService(a.Id, source), $"Turn On {svc}");
        if (Expired.Contains(a.Id)) return new Blocker($"{a.Label}'s sign-in has expired", ClientFix.SignIn(a.Id), "Sign In Again…");
        if (NeedsSignIn(a.Id)) return new Blocker($"{a.Label} isn't signed in", ClientFix.SignIn(a.Id), "Sign In…");
        return null;
    }

    public List<ClientIssue> ClientIssues(ClientConfig c)
    {
        if (!c.Enabled)
        {
            return [new ClientIssue { Id = "off", Text = $"{c.Name} is turned off, so nothing is sorted under it.", Fix = ClientFix.TurnOnClient, FixLabel = "Turn On" }];
        }
        var output = new List<ClientIssue>();
        var accounts = LookIn(c);

        if (c.Senders.Count == 0 && c.Keywords.Count == 0 && c.Chats.Count == 0 && !c.MatchChatNames)
        {
            output.Add(new ClientIssue { Id = "norules", Text = $"{c.Name} has no rules yet, so it can't catch anything. Add a sender, keyword or chat below." });
        }

        // Email rules need at least one account with Mail being checked.
        if (c.Senders.Count > 0 || c.Keywords.Count > 0)
        {
            var blocked = accounts.Select(a => BlockerFor(a, ItemSource.mail)).Where(b => b is not null).Select(b => b!.Value).ToList();
            if (accounts.Count > 0 && blocked.Count == accounts.Count)
            {
                var b = blocked[0];
                output.Add(new ClientIssue { Id = "mail", Text = $"Email rules aren't being checked: {b.Text}.", Fix = b.Fix, FixLabel = b.FixLabel });
            }
            foreach (var k in c.Keywords.Where(k => AccountNaming.GenericKeywords.Contains(k.ToLowerInvariant()) || k.Length < 3))
            {
                output.Add(new ClientIssue
                {
                    Id = $"kw-{k}",
                    Text = $"“{k}” is a very common word, so lots of unrelated mail will be sorted under {c.Name}. Use something specific to this client.",
                });
            }
        }

        // Each picked chat must be on an account that is in Look in and is checking chat.
        foreach (var chat in c.Chats)
        {
            var a = Account(chat.AccountID);
            if (a is null)
            {
                output.Add(new ClientIssue
                {
                    Id = $"chat-gone-{chat.Id}", Text = $"“{chat.Name}” belongs to an account that was removed.",
                    Fix = ClientFix.RemoveChat(chat), FixLabel = "Remove", Chat = chat,
                });
                continue;
            }
            if (c.AccountIDs.Count > 0 && !c.AccountIDs.Contains(a.Id))
            {
                output.Add(new ClientIssue
                {
                    Id = $"chat-scope-{chat.Id}", Text = $"“{chat.Name}” is in {a.Label}, which isn't in Look in for this client.",
                    Fix = ClientFix.AddToLookIn(a.Id), FixLabel = $"Add {a.Label}", Chat = chat,
                });
            }
            else if (BlockerFor(a, ItemSource.chat) is { } b)
            {
                output.Add(new ClientIssue
                {
                    Id = $"chat-{chat.Id}", Text = $"“{chat.Name}” isn't being checked: {b.Text}.",
                    Fix = b.Fix, FixLabel = b.FixLabel, Chat = chat,
                });
            }
        }

        // Name matching needs chat on at least one account (only worth saying if no chat issue already covers it).
        if (c.MatchChatNames && c.Chats.Count == 0)
        {
            var blocked = accounts.Select(a => BlockerFor(a, ItemSource.chat)).Where(b => b is not null).Select(b => b!.Value).ToList();
            if (accounts.Count > 0 && blocked.Count == accounts.Count)
            {
                var b = blocked[0];
                output.Add(new ClientIssue { Id = "names", Text = $"Chats named after {c.Name} can't be found: {b.Text}.", Fix = b.Fix, FixLabel = b.FixLabel });
            }
        }
        return output;
    }

    /// Clients that stop working if this service is switched off (for the account page).
    public List<ClientConfig> ClientsUsing(AccountConfig a, ItemSource source) =>
        _config.Clients.Where(c =>
        {
            if (!c.Enabled || !(c.AccountIDs.Count == 0 || c.AccountIDs.Contains(a.Id))) return false;
            return source switch
            {
                ItemSource.mail => c.Senders.Count > 0 || c.Keywords.Count > 0,
                ItemSource.chat => c.Chats.Any(x => x.AccountID == a.Id) || c.MatchChatNames,
                _ => false,
            };
        }).Select(c => c.Clone()).ToList();

    /// Carries out an issue's fix. Sign-in and re-checking are the app's job, so they are raised as
    /// SignInRequested and PollRequested.
    public void Apply(ClientFix fix, string clientID)
    {
        switch (fix.Kind)
        {
            case ClientFixKind.TurnOnClient:
                if (Client(clientID) is { } c1) { c1.Enabled = true; UpdateClient(c1); }
                break;
            case ClientFixKind.TurnOnAccount:
                if (Account(fix.AccountID!) is { } a1)
                {
                    a1.Enabled = true;
                    Update(a1);
                    PollRequested?.Invoke(a1);
                }
                break;
            case ClientFixKind.TurnOnService:
                if (Account(fix.AccountID!) is { } a2)
                {
                    if (fix.Source == ItemSource.chat) a2.WatchChat = true; else a2.WatchMail = true;
                    Update(a2);
                    PollRequested?.Invoke(a2);
                }
                break;
            case ClientFixKind.SignIn:
                SignInRequested?.Invoke(fix.AccountID!);
                break;
            case ClientFixKind.AddToLookIn:
                if (Client(clientID) is { } c2 && !c2.AccountIDs.Contains(fix.AccountID!))
                {
                    c2.AccountIDs.Add(fix.AccountID!);
                    UpdateClient(c2);
                }
                break;
            case ClientFixKind.RemoveChat:
                if (Client(clientID) is { } c3) { c3.Chats.RemoveAll(x => x == fix.Chat); UpdateClient(c3); }
                break;
            case ClientFixKind.AddRule:
                break;
        }
    }
}
