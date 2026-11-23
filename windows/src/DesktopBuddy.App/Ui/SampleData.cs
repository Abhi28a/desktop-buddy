using DesktopBuddy.Core;

namespace DesktopBuddy.App.Ui;

/// Made-up accounts, clients and notifications for `--snapshot` (the same sample data as the
/// Mac screenshots in docs/). Never real people or real addresses.
internal static class SampleData
{
    /// Everyone counts as signed in, and nothing touches the network.
    private sealed class SignedInGateway : IServiceGateway
    {
        public bool IsSignedIn(AccountConfig account) => true;
        public Task<FetchResult> FetchAsync(AccountConfig account, ItemSource source, int meetingLeadMinutes, CancellationToken ct = default) =>
            Task.FromResult(new FetchResult { Source = source });
    }

    /// Fills AppState.Shared (which must be pointed at a throwaway folder first).
    public static AppState Load()
    {
        Gateway.Current = new SignedInGateway();
        var state = AppState.Shared;
        var now = DateTimeOffset.Now;
        state.Now = () => now;

        var dynamatix = new AccountConfig { Label = "Dynamatix", Email = "you@dynamatix.co.uk", ProviderChoice = Provider.google };
        var iehub = new AccountConfig { Label = "IE Hub", Email = "you@iehub.org", ProviderChoice = Provider.google, TintChoice = AccountTint.green };
        var acme = new AccountConfig { Label = "Acme Ltd", Email = "you@acme-ltd.example", ProviderChoice = Provider.microsoft };
        state.AddAccount(dynamatix);
        state.AddAccount(iehub);
        state.AddAccount(acme);

        foreach (var c in state.Config.Clients.ToList())
        {
            var copy = state.Client(c.Id)!;
            copy.Senders = c.Name == "Gatehouse" ? ["gatehouse.example"] : ["riskhawk.example"];
            copy.Keywords = [c.Name, c.Name == "Gatehouse" ? "Mortgage" : "Audit"];
            state.UpdateClient(copy);
        }

        state.SetStatus(dynamatix.Id, ItemSource.mail, new ServiceStatus { LastChecked = now.AddMinutes(-1), Count = 3 });
        state.SetStatus(dynamatix.Id, ItemSource.chat, new ServiceStatus { LastChecked = now.AddMinutes(-1), Count = 1 });
        state.SetStatus(dynamatix.Id, ItemSource.meeting, new ServiceStatus { LastChecked = now.AddMinutes(-1) });
        state.SetStatus(dynamatix.Id, ItemSource.task, new ServiceStatus { LastChecked = now.AddMinutes(-1), Count = 1 });
        state.SetStatus(iehub.Id, ItemSource.chat, new ServiceStatus
        {
            LastChecked = now.AddMinutes(-2),
            Error = "Google Chat API has not been used in project 0000 before or it is disabled.",
        });
        state.SetExpired(acme.Id, true);

        state.Offer(
        [
            new BuddyItem { Id = "s1", Source = ItemSource.mail, AccountID = dynamatix.Id, AccountLabel = "Dynamatix",
                From = "Sample Sender", FromAddress = "sender@gatehouse.example", Title = "Mortgage offer – revised schedule",
                Detail = "Please find the revised schedule attached for review before Friday.", Date = now.AddMinutes(-2) },
            new BuddyItem { Id = "s2", Source = ItemSource.chat, AccountID = dynamatix.Id, AccountLabel = "Dynamatix",
                From = "Sample Colleague", Title = "Gatehouse project", ChatRef = "spaces/sample",
                Detail = "Deployment to UAT is done, can you take a look?", Date = now.AddMinutes(-6) },
            new BuddyItem { Id = "s3", Source = ItemSource.mail, AccountID = dynamatix.Id, AccountLabel = "Dynamatix",
                From = "Sample Sender", FromAddress = "audit@riskhawk.example", Title = "RiskHawk audit plan ready for approval",
                Detail = "The Q4 audit plan has been submitted and is waiting for your approval.", Date = now.AddMinutes(-11) },
            new BuddyItem { Id = "s4", Source = ItemSource.task, AccountID = dynamatix.Id, AccountLabel = "Dynamatix",
                Title = "Send sprint report", Detail = "Google Tasks", Date = now.AddMinutes(-30) },
            new BuddyItem { Id = "s5", Source = ItemSource.meeting, AccountID = iehub.Id, AccountLabel = "IE Hub",
                From = "Sample Calendar", Title = "Design review", Detail = "Starts in 5 min", Date = now.AddMinutes(-1) },
            new BuddyItem { Id = "s6", Source = ItemSource.reminder, Title = "💧 Drink a glass of water",
                Detail = "Stay hydrated!", Date = now.AddMinutes(-3) },
        ]);
        state.ReassignClients();     // the Poller normally matches client rules before offering
        return state;
    }
}
