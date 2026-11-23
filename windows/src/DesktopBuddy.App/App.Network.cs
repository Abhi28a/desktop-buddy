using DesktopBuddy.Core;

namespace DesktopBuddy.App;

/// Connects the network layer (sign-in, Google and Microsoft sources) to AppState.
public partial class App
{
    // The gateway's callbacks run on background threads and AppState is UI-thread only,
    // so they read copies that are refreshed on the UI thread whenever AppState changes.
    private volatile IReadOnlySet<string> _knownMailIds = new HashSet<string>();
    private volatile IReadOnlyDictionary<string, string[]> _chatIdsByAccount = new Dictionary<string, string[]>();

    partial void InstallNetwork()
    {
        var state = AppState.Shared;
        Gateway.Current = new LiveGateway(
            knownMailIds: () => _knownMailIds,
            currentChatIds: accountID => _chatIdsByAccount.TryGetValue(accountID, out var ids) ? ids : []);

        RefreshNetworkCopies();
        state.Changed += RefreshNetworkCopies;
        state.RefreshSignedIn();   // the gateway changed, so re-read who has a saved sign-in

        // A removed account forgets its saved sign-in (refresh token and any client secret).
        state.AccountRemoved += account => OAuth.Shared.Forget(account);

        var signIn = SignInCenter.Shared;
        signIn.SignedIn += id => UI.TryEnqueue(() =>
        {
            state.Batch(() =>
            {
                state.SetExpired(id, false);
                state.WrongAccount.Remove(id);
                state.RefreshSignedIn();
            });
            if (state.Account(id) is { } account) _ = Poller.Shared.PollAsync(account);
        });
        signIn.WrongAccount += (id, address) => UI.TryEnqueue(() =>
        {
            state.Batch(() =>
            {
                state.SetExpired(id, false);
                state.WrongAccount[id] = address;
                state.RefreshSignedIn();
            });
        });
        signIn.AddressAccepted += (id, address) => UI.TryEnqueue(() =>
        {
            if (state.Account(id) is not { } account) return;
            account.Email = address;
            state.Batch(() =>
            {
                state.WrongAccount.Remove(id);
                state.Update(account);
                state.RefreshSignedIn();
            });
            _ = Poller.Shared.PollAsync(account);
        });
    }

    private void RefreshNetworkCopies()
    {
        var state = AppState.Shared;
        var mail = new HashSet<string>(state.MutedMail);
        foreach (var item in state.Items) if (item.Source == ItemSource.mail) mail.Add(item.Id);
        _knownMailIds = mail;
        _chatIdsByAccount = state.Items
            .Where(i => i.Source == ItemSource.chat && i.AccountID is not null)
            .GroupBy(i => i.AccountID!)
            .ToDictionary(g => g.Key, g => g.Select(i => i.Id).ToArray());
    }
}
