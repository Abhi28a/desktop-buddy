using DesktopBuddy.App.Ui;
using DesktopBuddy.App.Ui.Components;
using DesktopBuddy.Core;
using DesktopBuddy.Presentation.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DesktopBuddy.App.Views.Settings;

internal enum AddPhase { Choose, Waiting, Done }

/// The "sign in first" steps for a new account (port of AddAccountSheet in SettingsWindow.swift):
/// pick the browser account, name it from the signed-in address, then let the person rename it.
/// It lives in the window, not the page, so a rebuild never loses its place.
internal sealed class AddAccountFlow
{
    public event Action? Changed;

    public AddPhase Phase { get; private set; } = AddPhase.Choose;
    public Provider? Provider { get; private set; }
    public AccountConfig? Draft { get; private set; }
    public string? Error { get; set; }
    /// Rendering a snapshot: show the waiting panel with a link, without any sign-in running.
    public bool Preview { get; private set; }

    private void Raise() => Changed?.Invoke();

    public void Reset()
    {
        Phase = AddPhase.Choose;
        Provider = null;
        Draft = null;
        Error = null;
        Preview = false;
    }

    public void ResetIfDone()
    {
        if (Phase == AddPhase.Done && !Preview) Reset();
    }

    /// Leaving the page while the browser is open ends that sign-in (closing the sheet, on the Mac).
    public void CancelIfWaiting()
    {
        if (Phase != AddPhase.Waiting || Draft is null) return;
        if (Preview) { Reset(); return; }
        SignInCenter.Shared.Cancel(Draft.Id);
    }

    public void ShowDoneForPreview(AccountConfig account)
    {
        Provider = account.Provider;
        Draft = account;
        Phase = AddPhase.Done;
        Preview = true;
    }

    public void ShowWaitingForPreview(Provider provider)
    {
        Provider = provider;
        Draft = new AccountConfig { Provider = provider };
        Phase = AddPhase.Waiting;
        Preview = true;
    }

    public async Task StartAsync(Provider provider)
    {
        var state = AppState.Shared;
        Error = null;
        Provider = provider;
        // Spread colours so each new account looks different.
        var n = state.Config.Accounts.Count;
        var draft = new AccountConfig
        {
            Provider = provider,
            RedirectPort = 8766,
            Tint = AccountTintInfo.All[(n + (provider == Core.Provider.microsoft ? 1 : 0)) % AccountTintInfo.All.Length],
        };
        Draft = draft;
        Phase = AddPhase.Waiting;
        Raise();

        var center = SignInCenter.Shared;
        if (!await center.StartAsync(draft))
        {
            Error = center.Error(draft.Id);
            center.ClearError(draft.Id);
            Phase = AddPhase.Choose;
            Raise();
            return;
        }

        // Whoever signed in is the account being added.
        string? email = null;
        try { email = (await OAuth.Shared.SignedInEmailsAsync(draft)).FirstOrDefault(); }
        catch (Exception) { email = null; }
        if (email is null)
        {
            OAuth.Shared.Forget(draft);
            Error = $"Signed in, but {provider.Name()} didn't say which account it was. Please try again.";
            Phase = AddPhase.Choose;
            Raise();
            return;
        }
        var existing = state.Config.Accounts.FirstOrDefault(a => a.Provider == provider && string.Equals(a.Email, email, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            OAuth.Shared.Forget(draft);
            Error = $"{email} is already added as “{existing.Label}”. Choose a different account to add another one.";
            Phase = AddPhase.Choose;
            Raise();
            return;
        }

        var account = draft with
        {
            Email = email,
            Label = AccountNaming.UniqueLabel(AccountNaming.SuggestedLabel(email), state.Config.Accounts.Select(a => a.Label).ToHashSet()),
        };
        Draft = account;
        state.AddAccount(account);
        Phase = AddPhase.Done;
        Raise();
        _ = Poller.Shared.PollAsync(account);
    }
}

/// The browser-is-open panel, on the Add account page and on an account page that is signing in again
/// (port of SignInWaitingView in SignInCenter.swift).
internal static class SignInWaiting
{
    public static FrameworkElement Build(string accountId, string email, bool preview)
    {
        var center = SignInCenter.Shared;
        var hasLink = preview || center.Link(accountId) is not null;
        var copied = !preview && center.Copied == accountId;

        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var ring = new ProgressRing { IsActive = true, Width = 28, Height = 28, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
        ring.Id("SignInProgress", "Waiting for your browser");
        grid.Children.Add(ring);

        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(Sk.Text(SignInWaitingText.Title, "DbRowTitleStyle").With(t => t.TextWrapping = TextWrapping.Wrap));
        text.Children.Add(Sk.Caption(SignInWaitingText.Subtitle(email)));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var copy = Sk.Btn(copied ? "Copied" : "Copy sign-in link", () =>
        {
            if (center.CopyLink(accountId) is { } link) SettingsContext.CopyToClipboard(link);
        }, "SignInCopy", enabled: hasLink, name: "Copy sign-in link");
        if (email.Length > 0) ToolTipService.SetToolTip(copy, SignInWaitingText.CopyHelp(email));
        var open = Sk.Btn("Open again", () => center.Reopen(accountId), "SignInReopen", enabled: hasLink);
        var cancel = Sk.QuietBtn("Cancel", () => center.Cancel(accountId), "SignInCancel");
        var buttons = Sk.Buttons(copy, open, cancel);
        Grid.SetColumn(buttons, 1);
        Grid.SetRow(buttons, 1);
        grid.Children.Add(buttons);
        return new Border { Style = Sk.Sty("DbCardStyle"), Padding = new Thickness(20, 18, 20, 18), Child = grid };
    }
}

/// The Add account page: sign in with Google or Microsoft, wait for the browser, then name the account.
/// On a first run it is also Home, with its own welcome heading.
internal sealed class AddAccountPage : SettingsPage
{
    private readonly bool _firstRun;

    public AddAccountPage(SettingsContext ctx, bool firstRun) : base(ctx) => _firstRun = firstRun;

    public override SettingsRoute Route => SettingsRoute.AddAccount;

    /// The first-run welcome has its own heading, so the shell's title is hidden.
    public bool OwnsHeader => _firstRun && Flow.Phase != AddPhase.Done && State.Config.Accounts.Count == 0;

    private AddAccountFlow Flow => Ctx.AddFlow;

    protected override void Build(StackPanel body)
    {
        switch (Flow.Phase)
        {
            case AddPhase.Waiting: BuildWaiting(body); break;
            case AddPhase.Done: BuildDone(body); break;
            default: BuildChoose(body); break;
        }
        Ctx.Shell.RefreshChrome();
    }

    // MARK: - Choose

    private void BuildChoose(StackPanel body)
    {
        var team = Ctx.Team();
        var providers = new[] { Provider.google, Provider.microsoft }.Where(team.HasClient).ToList();

        if (OwnsHeader)
        {
            var hero = new StackPanel { Spacing = 6, Margin = new Thickness(0, 36, 0, 24) };
            hero.Children.Add(new LogoTile(new LogoRef(null, "dynamatix"), "Desktop Buddy", AccountTint.graphite, 56)
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 12),
            });
            hero.Children.Add(Sk.PageTitle("Sign in to get started"));
            hero.Children.Add(Sk.Text("Sign in with the Google Workspace or Microsoft 365 account you work in. Mail, chats, meetings and tasks are all covered by one sign-in.", "DbSecondaryStyle").With(t => t.TextTrimming = TextTrimming.None));
            body.Children.Add(hero);
        }
        else
        {
            body.Children.Add(Sk.Text("Sign in with the Google Workspace or Microsoft 365 account you work in. Mail, chats, meetings and tasks are all covered by one sign-in.", "DbSecondaryStyle").With(t =>
            {
                t.TextTrimming = TextTrimming.None;
                t.Margin = new Thickness(0, 0, 0, 20);
            }));
        }

        if (Flow.Error is { } error)
        {
            var line = new StatusLine(StatusSeverity.Caution, error, null, "Dismiss", () => { Flow.Error = null; QueueRebuild(); }) { AutomationKey = "AddAccountError" };
            body.Children.Add(new Border { Style = Sk.Sty("DbCardStyle"), Padding = new Thickness(4), Child = line, Margin = new Thickness(0, 0, 0, 12) });
        }

        if (providers.Count == 0)
        {
            body.Children.Add(SetupNeeded());
            return;
        }

        var cards = new StackPanel { Spacing = 8 };
        foreach (var p in providers) cards.Children.Add(ProviderCard(p));
        body.Children.Add(cards);
        if (providers.Count == 1)
        {
            var missing = providers[0] == Provider.google ? Provider.microsoft : Provider.google;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
            row.Children.Add(Sk.Caption($"{missing.Name()} sign-in isn't set up in this copy yet.").With(t => t.VerticalAlignment = VerticalAlignment.Center));
            row.Children.Add(Sk.Link("Import setup file", () => _ = ImportAsync(), "ImportSetup"));
            body.Children.Add(row);
        }
        body.Children.Add(Sk.Heading("Desktop Buddy will be able to"));
        body.Children.Add(Permissions(providers));
        body.Children.Add(Sk.Note("Desktop Buddy only reads. It never sends, changes or deletes anything. Sign-ins are kept in Windows Credential Manager on this PC."));
    }

    private FrameworkElement ProviderCard(Provider provider)
    {
        var word = provider == Provider.google ? "Google" : "Microsoft";
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var badge = LogoTile.ProviderBadge(provider, 40);
        badge.HorizontalAlignment = HorizontalAlignment.Left;
        badge.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(badge);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        text.Children.Add(Sk.Text($"Sign in with {word}", "DbRowTitleStyle"));
        text.Children.Add(Sk.Caption(provider.Services()));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var chevron = Sk.Icon(Glyphs.Chevron, 12);
        chevron.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(chevron, 2);
        grid.Children.Add(chevron);

        var id = provider == Provider.google ? "SignInGoogle" : "SignInMicrosoft";
        var button = new Button { Content = grid, Style = Sk.Sty("SkCardButton"), Tag = id };
        button.Id(id, $"Sign in with {word}");
        var p = provider;
        button.Click += (_, _) => { if (!Ctx.Preview) _ = Flow.StartAsync(p); };
        return button;
    }

    private FrameworkElement SetupNeeded()
    {
        var line = new StatusLine(StatusSeverity.Caution, "Sign-in isn't set up in this copy yet",
            "Your admin sets up Google and Microsoft sign-in once for the whole team and shares a setup file. Import it here, or ask them for a copy of Desktop Buddy with sign-in built in.",
            "Import setup file", () => _ = ImportAsync())
        {
            AutomationKey = "SignInNotSetUp",
        };
        return new Border { Style = Sk.Sty("DbCardStyle"), Padding = new Thickness(4), Child = line };
    }

    private static FrameworkElement Permissions(IReadOnlyList<Provider> providers)
    {
        var group = new Grid { ColumnSpacing = 8 };
        foreach (var p in providers)
        {
            group.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lines = new StackPanel { Spacing = 8 };
            if (providers.Count > 1) lines.Children.Add(Sk.Text(p.Name(), "DbRowTitleStyle"));
            foreach (var line in OAuth.PermissionSummary(p))
            {
                var row = new Grid { ColumnSpacing = 10 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var tick = Sk.Icon(Glyphs.Check, 12, "SkIconPositive");
                tick.VerticalAlignment = VerticalAlignment.Top;
                tick.Margin = new Thickness(0, 4, 0, 0);
                row.Children.Add(tick);
                var label = Sk.Text(line);
                Grid.SetColumn(label, 1);
                row.Children.Add(label);
                lines.Children.Add(row);
            }
            var card = new Border { Style = Sk.Sty("DbCardStyle"), Padding = new Thickness(16, 14, 16, 14), Child = lines };
            Grid.SetColumn(card, group.ColumnDefinitions.Count - 1);
            group.Children.Add(card);
        }
        return group;
    }

    private async Task ImportAsync()
    {
        var cfg = await TeamFiles.ImportAsync(Ctx);
        if (cfg is null) return;
        if (!cfg.HasClient(Provider.google) && !cfg.HasClient(Provider.microsoft))
        {
            Flow.Error = "That file doesn't include a Google Workspace or Microsoft 365 sign-in client.";
            QueueRebuild();
            return;
        }
        TeamConfig.InstallLocal(cfg);
        Flow.Error = null;
        Ctx.State.NotifyChanged();
        Ctx.Shell.RefreshAfterAdminChange();
        QueueRebuild();
    }

    // MARK: - Waiting

    private void BuildWaiting(StackPanel body)
    {
        var provider = Flow.Provider ?? Provider.google;
        var word = provider == Provider.google ? "Google" : "Microsoft";
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(0, OwnsHeader ? 36 : 0, 0, 20) };
        head.Children.Add(LogoTile.ProviderBadge(provider, 40));
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Sk.Text($"Sign in with {word}", "DbPanelTitleStyle"));
        text.Children.Add(Sk.Caption(provider.Services()));
        head.Children.Add(text);
        body.Children.Add(head);
        body.Children.Add(SignInWaiting.Build(Flow.Draft?.Id ?? "", "", Flow.Preview));
        body.Children.Add(Sk.Note(SignInWaitingText.WrongProfileHint("")));
    }

    // MARK: - Done

    private void BuildDone(StackPanel body)
    {
        var draft = Flow.Draft!;
        var logo = Logos.Resolve(draft.LogoPath, draft.Label, draft.Email);
        var head = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
        head.Children.Add(Sk.Token(logo, draft.Label, draft.Tint, 64, 0, draft.Provider, surface: "DbSurfaceBaseBrush").With(t =>
        {
            t.HorizontalAlignment = HorizontalAlignment.Left;
            t.Margin = new Thickness(0, 0, 0, 10);
        }));
        head.Children.Add(Sk.Text("You're connected", "DbPanelTitleStyle"));
        head.Children.Add(Sk.Text(draft.Email, "DbSecondaryStyle"));
        body.Children.Add(head);

        var name = Sk.Field(draft.Label, v => draft.Label = v, "Account name", "AccountName", "e.g. Dynamatix", width: 320);
        name.HorizontalAlignment = HorizontalAlignment.Left;
        name.Header = "Name";
        name.Margin = new Thickness(0, 24, 0, 0);
        body.Children.Add(name);
        body.Children.Add(Sk.Note("Mail, chats, meetings and tasks are on. You can turn any of them off in Settings."));

        var done = Sk.Btn("Done", () =>
        {
            var a = draft with { Label = draft.Label.Trim() };
            if (a.Label.Length == 0) a.Label = AccountNaming.SuggestedLabel(a.Email);
            State.Update(a);
            Flow.Reset();
            Ctx.Navigate("account:" + a.Id);
        }, "AddAccountDone", primary: true);
        done.Margin = new Thickness(0, 20, 0, 0);
        body.Children.Add(done);
    }
}
