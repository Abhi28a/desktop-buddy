using CommunityToolkit.WinUI.Controls;
using DesktopBuddy.App.Ui;
using DesktopBuddy.App.Ui.Components;
using DesktopBuddy.Core;
using DesktopBuddy.Presentation.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DesktopBuddy.App.Views.Settings;

/// Accounts: the list (token with provider badge, name, email, status in words) and Add account.
internal sealed class AccountsPage : SettingsPage
{
    public AccountsPage(SettingsContext ctx) : base(ctx) { }

    public override SettingsRoute Route => SettingsRoute.Accounts;

    protected override void Build(StackPanel body)
    {
        var accounts = State.Config.Accounts;

        var top = new Grid { ColumnSpacing = 16, Margin = new Thickness(0, 0, 0, 16) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(Sk.Text("The Google Workspace and Microsoft 365 accounts Desktop Buddy watches for you.", "DbSecondaryStyle")
            .With(t => { t.TextTrimming = TextTrimming.None; t.VerticalAlignment = VerticalAlignment.Center; }));
        var add = Sk.Btn("Add account", () => Ctx.Navigate("add-account"), "AddAccount", primary: true);
        Grid.SetColumn(add, 1);
        top.Children.Add(add);
        body.Children.Add(top);

        if (accounts.Count == 0)
        {
            body.Children.Add(Sk.Block(new StackPanel { Spacing = 4 }.With(p =>
            {
                p.Children.Add(Sk.Text("No accounts yet", "DbRowTitleStyle"));
                p.Children.Add(Sk.Caption("Add one to start hearing about mail, chats, meetings and tasks."));
            })));
            return;
        }

        var list = Sk.Group();
        foreach (var a in accounts)
        {
            var account = a;
            var status = AccountStatusText.Describe(State, account);
            var sub = new StackPanel { Spacing = 2 };
            sub.Children.Add(Sk.Caption($"{account.Email} · {account.Provider.Name()}"));
            sub.Children.Add(Sk.StatusText(status.Level, status.Words));
            var card = Sk.LeadRow(Sk.AccountToken(State, account, 40), account.Label.Length == 0 ? "Account" : account.Label, sub, null,
                $"AccountRow_{account.Id}", $"{account.Label}, {account.Email}, {status.Words}");
            list.Children.Add(card.Clickable(() => Ctx.Navigate("account:" + account.Id)));
        }
        body.Children.Add(list);
    }
}

/// One account: header, connection, the services to watch, mail filters, mute rules, appearance,
/// and sign out or remove (port of AccountDetailView in SettingsWindow.swift).
internal sealed class AccountPage : SettingsPage
{
    private readonly string _id;
    private bool _advancedOpen;
    private TextBlock? _headerName;

    public AccountPage(SettingsContext ctx, string id) : base(ctx) => _id = id;

    public override SettingsRoute Route => SettingsRoute.ForAccount(_id);
    public override bool Exists => State.Account(_id) is not null;

    /// Edits a copy of the account and saves it.
    private void Mutate(Action<AccountConfig> change)
    {
        if (State.Account(_id) is not { } a) return;
        change(a);
        State.Update(a);
    }

    protected override void Build(StackPanel body)
    {
        if (State.Account(_id) is not { } a) return;
        body.Children.Add(Header(a));
        body.Children.Add(Connection(a));
        body.Children.Add(Services(a));

        if (a.WatchMail) MailFilters.Build(body, this, a, Mutate, Ctx);
        if (a.WatchMail || a.WatchChat) MuteRules(body, a);

        body.Children.Add(Sk.Heading("Appearance"));
        body.Children.Add(Sk.Group(
            Sk.Row("Name", null, Sk.Field(a.Label, text =>
            {
                Quiet(() => Mutate(x => x.Label = text));
                if (_headerName is not null) _headerName.Text = text.Length == 0 ? "Account" : text;
            }, "Account name", "AccountName", "e.g. Dynamatix", width: 280), Glyphs.Edit, "AccountNameRow"),
            Sk.Row("Colour", "Rings the token in this colour.", Sk.TintPicker(a.Tint, t => Mutate(x => x.Tint = t), "Account colour", "AccountTint"), Glyphs.Palette, "AccountTintRow"),
            LogoRow(a)));

        if (Ctx.ShowsAdmin) body.Children.Add(Advanced(a));

        body.Children.Add(Sk.Heading("Sign out and remove"));
        var signedIn = State.SignedIn.Contains(a.Id);
        body.Children.Add(Sk.Group(
            Sk.Row("Sign out", "Keeps the account here but stops checking it. Sign in again any time.",
                Sk.Btn("Sign out", () => { OAuth.Shared.SignOut(a); State.RefreshSignedIn(); }, "SignOut", enabled: signedIn), Glyphs.SignOut, "SignOutRow"),
            Sk.Row("Remove account", $"Forgets {(a.Label.Length == 0 ? "this account" : a.Label)} on this PC. Nothing changes in {a.Provider.Name()}.",
                Sk.Btn("Remove account", () => _ = RemoveAsync(a), "RemoveAccount"), Glyphs.Delete, "RemoveAccountRow")));
    }

    // MARK: - Header and connection

    private FrameworkElement Header(AccountConfig a)
    {
        var grid = new Grid { ColumnSpacing = 18 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Sk.AccountToken(State, a, 64).With(t => t.VerticalAlignment = VerticalAlignment.Center));

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        _headerName = Sk.Text(a.Label.Length == 0 ? "Account" : a.Label, "DbPanelTitleStyle");
        _headerName.TextTrimming = TextTrimming.CharacterEllipsis;
        _headerName.TextWrapping = TextWrapping.NoWrap;
        _headerName.Id("AccountHeaderName");
        text.Children.Add(_headerName);
        text.Children.Add(Sk.Text(a.Email, "DbSecondaryStyle"));
        text.Children.Add(Sk.Caption(a.Provider.Name()));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var toggle = Sk.Switch(a.Enabled, on => Mutate(x => x.Enabled = on), "Account on", "AccountEnabled");
        ToolTipService.SetToolTip(toggle, a.Enabled ? "Turn off to stop checking this account" : "Turn on to start checking");
        var holder = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        holder.Children.Add(Sk.Caption(a.Enabled ? "On" : "Off").With(t => t.VerticalAlignment = VerticalAlignment.Center));
        holder.Children.Add(toggle);
        Grid.SetColumn(holder, 2);
        grid.Children.Add(holder);
        return new Border { Style = Sk.Sty("DbCardStyle"), Padding = new Thickness(20, 18, 20, 18), Child = grid, Margin = new Thickness(0, 0, 0, 8) };
    }

    private FrameworkElement Connection(AccountConfig a)
    {
        var center = SignInCenter.Shared;
        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 4) };
        if (!Ctx.Preview && center.IsWaiting(a.Id))
        {
            stack.Children.Add(SignInWaiting.Build(a.Id, a.Email, false));
            return stack;
        }

        var info = AccountStatusText.Describe(State, a);
        var checking = State.Checking.Contains(a.Id);
        var wrong = State.WrongAccount.TryGetValue(a.Id, out var w) ? w : null;
        var needsSignIn = State.NeedsSignIn(a.Id);
        string? action = null;
        Action? run = null;
        string key = "Connection";
        if (needsSignIn && a.Enabled)
        {
            action = State.Expired.Contains(a.Id) || wrong is not null ? "Sign in again" : "Sign in";
            run = () => SignIn(a);
            key = "ConnectionSignIn";
        }
        else if (a.Enabled && !checking)
        {
            action = "Check now";
            run = () => _ = Poller.Shared.PollAsync(a);
            key = "ConnectionCheck";
        }
        var detail = checking && info.Level != StatusLevel.Critical ? "Checking now…" : info.Detail;
        var severity = info.Level switch
        {
            StatusLevel.Positive => StatusSeverity.Positive,
            StatusLevel.Caution => StatusSeverity.Caution,
            StatusLevel.Critical => StatusSeverity.Critical,
            _ => StatusSeverity.Neutral,
        };
        var line = new StatusLine(severity, info.Words, detail, action, run) { AutomationKey = key };
        stack.Children.Add(Sk.Block(line, new Thickness(4)));

        if (wrong is not null && a.Enabled)
        {
            stack.Children.Add(Sk.Row($"Use {wrong} instead", "Keeps the account you signed in as and changes this account's address to match.",
                Sk.Btn($"Use {wrong}", () => center.AcceptSignedInAddress(a.Id), "UseSignedInAddress", name: $"Use {wrong} as the address"), Glyphs.Person, "UseSignedInAddressRow"));
        }
        if (!Ctx.Preview && center.Error(a.Id) is { } error)
        {
            var bar = new StatusLine(StatusSeverity.Caution, error, null, "Dismiss", () => center.ClearError(a.Id)) { AutomationKey = "SignInError" };
            stack.Children.Add(Sk.Block(bar, new Thickness(4)));
        }
        return stack;
    }

    private void SignIn(AccountConfig a)
    {
        if (Ctx.Preview) return;
        _ = SignInCenter.Shared.StartAsync(a);     // the app checks the account itself once the sign-in finishes
    }

    // MARK: - Services (one grouped card)

    private FrameworkElement Services(AccountConfig a)
    {
        var items = new List<SettingsCard>();
        var on = 0;
        foreach (var source in AccountStatusText.Services)
        {
            var src = source;
            var watching = AccountStatusText.Watches(a, src);
            if (watching) on++;
            var name = AccountStatusText.ServiceName(a.Provider, src);
            var line = AccountStatusText.Service(State, a, src);

            var description = new StackPanel { Spacing = 6 };
            if (!(watching && line.Error is not null)) description.Children.Add(Sk.StatusText(line.Level, line.Status));
            if (watching && line.Error is { } error)
            {
                var fix = new StatusLine(StatusSeverity.Caution, $"{name} isn't working", error, line.FixUrl is null ? null : "Fix", () => SettingsContext.Open(line.FixUrl!))
                {
                    AutomationKey = $"ServiceError_{src}",
                };
                description.Children.Add(fix);
            }
            var icon = watching ? Sk.SourceIcon(src) : Sk.Icon(src.Glyph(), 16, "SkIconSecondary");
            var row = Sk.RowOf(name, description,
                Sk.Switch(watching, v => Mutate(x => Watch(x, src, v)), name, $"Service_{src}"), null, $"ServiceRow_{src}");
            row.HeaderIcon = icon;
            items.Add(row);
        }
        return Sk.Expander("Notify me about", "Desktop Buddy only reads. It never sends, changes or deletes anything.",
            Sk.Caption($"{on} of {AccountStatusText.Services.Length} on"), null, "Services", expanded: true, [.. items]);
    }

    private static void Watch(AccountConfig a, ItemSource source, bool on)
    {
        switch (source)
        {
            case ItemSource.mail: a.WatchMail = on; break;
            case ItemSource.chat: a.WatchChat = on; break;
            case ItemSource.meeting: a.WatchMeetings = on; break;
            case ItemSource.task: a.WatchTasks = on; break;
        }
    }

    // MARK: - Mute

    private void MuteRules(StackPanel body, AccountConfig a)
    {
        body.Children.Add(Sk.Heading("Mute"));
        var cards = Sk.Group();
        if (a.WatchMail)
        {
            cards.Children.Add(Sk.Block(new TokenField("Senders", "Type a domain or address, e.g. news.example.com",
                "A whole domain (example.com) or one address (noreply@example.com). Press Return to add.",
                a.MutedSenders, Glyphs.Mute, TokenRules.SenderProblem,
                tokens => Mutate(x => x.MutedSenders = tokens.ToList()), Problems, "MuteSenders")));
        }
        if (a.WatchChat) cards.Children.Add(Sk.Block(MutedChats(a)));
        var keywordHint = (a.WatchMail, a.WatchChat) switch
        {
            (true, true) => "Matched as a whole word in a mail's subject, preview or sender, and in a chat's name, message or sender.",
            (true, false) => "Matched as a whole word in the subject, preview or sender's name.",
            _ => "Matched as a whole word in the chat's name, the message or the sender's name.",
        };
        cards.Children.Add(Sk.Block(new TokenField("Keywords", "Type a word, e.g. newsletter", keywordHint,
            a.MutedKeywords, Glyphs.Search, TokenRules.KeywordProblem,
            tokens => Mutate(x => x.MutedKeywords = tokens.ToList()), Problems, "MuteKeywords")));
        body.Children.Add(cards);

        var what = (a.WatchMail, a.WatchChat) switch
        {
            (true, true) => $"Muted mail and chats never pop up, even for a client. They stay unread in {a.Provider.MailName()} and {a.Provider.ChatName()}, so you can still find them there.",
            (true, false) => $"Muted mail never pops up, even for a client. It stays unread in {a.Provider.MailName()}, so you can still find it there.",
            _ => $"Muted chats never pop up, even for a client. They stay unread in {a.Provider.ChatName()}, so you can still find them there.",
        };
        body.Children.Add(Sk.Note(what));
    }

    private FrameworkElement MutedChats(AccountConfig a)
    {
        var panel = new StackPanel { Spacing = 10 };
        var header = new Grid { ColumnSpacing = 10 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(Sk.Icon(Glyphs.Chat, 16).With(i => i.VerticalAlignment = VerticalAlignment.Center));
        var name = Sk.Text("Chats");
        name.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(name, 1);
        header.Children.Add(name);
        var count = Sk.Caption(a.MutedChats.Count == 0 ? "None yet" : $"{a.MutedChats.Count} muted");
        count.VerticalAlignment = VerticalAlignment.Center;
        count.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(count, 2);
        header.Children.Add(count);
        var choose = Sk.Btn("Choose chats", () => _ = ChooseMutedAsync(a), "MuteChatsChoose", enabled: State.SignedIn.Contains(a.Id));
        Grid.SetColumn(choose, 3);
        header.Children.Add(choose);
        panel.Children.Add(header);

        if (a.MutedChats.Count > 0)
        {
            var wrap = new WrapPanel();
            foreach (var chat in a.MutedChats)
            {
                var c = chat;
                wrap.Children.Add(Sk.Chip(c.Name, ChatGlyph(c), () => Mutate(x => x.MutedChats.RemoveAll(m => m == c)), $"Unmute {c.Name}", $"MuteChat_remove_{c.ChatID}"));
            }
            panel.Children.Add(wrap);
        }
        return panel;
    }

    internal static string ChatGlyph(ChatRef c) => c.Kind switch
    {
        ChatKind.direct => Glyphs.Person,
        ChatKind.group => Glyphs.People,
        _ => Glyphs.Chat,
    };

    private async Task ChooseMutedAsync(AccountConfig a)
    {
        var picked = await ChatPicker.ShowAsync(Ctx, a.MutedChats, "", "Mute chats", a.Id);
        if (picked is not null) Mutate(x => x.MutedChats = picked);
    }

    // MARK: - Appearance and advanced

    private FrameworkElement LogoRow(AccountConfig a)
    {
        var token = Sk.AccountToken(State, a, 28);
        return Sk.LeadRow(token, "Logo", Sk.Caption(a.LogoPath is null ? "Shown on this account's notifications" : "Using your own image"),
            Sk.Buttons(
                a.LogoPath is not null ? Sk.Btn("Use default", () => Mutate(x => x.LogoPath = null), "AccountLogoDefault") : null,
                Sk.Btn("Choose…", () => _ = ChooseLogoAsync(a), "AccountLogoChoose", name: "Choose a logo")),
            "AccountLogoRow", "Logo");
    }

    private async Task ChooseLogoAsync(AccountConfig a)
    {
        var path = await Ctx.ChooseLogoAsync("account");
        if (path is not null) Mutate(x => x.LogoPath = path);
    }

    private FrameworkElement Advanced(AccountConfig a)
    {
        var items = new List<SettingsCard>
        {
            Sk.Row("Email", null, Sk.Field(a.Email, v => Quiet(() => Mutate(x => x.Email = v)), "Email", "AdvEmail", width: 320), id: "AdvEmailRow"),
            Sk.Row("Client ID", "Optional. Uses the team default.", Sk.Field(a.ClientID, v => Quiet(() => Mutate(x => x.ClientID = v)), "Client ID", "AdvClientId", "Uses the team default", 320), id: "AdvClientIdRow"),
        };
        if (a.Provider == Provider.google)
        {
            var secret = Sk.Secret("", _ => { }, "Client secret", "AdvClientSecret", "Uses the team default");
            secret.Width = 320;
            secret.MinWidth = 0;
            void Save()
            {
                if (secret.Password.Length == 0) return;
                Secrets.Store.Set(OAuth.SecretKey(a.Id), secret.Password);
                secret.Password = "";
                secret.PlaceholderText = "Saved";
            }
            secret.LostFocus += (_, _) => Save();
            secret.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) Save(); };
            items.Add(Sk.Row("Client secret", "Optional. Uses the team default.", secret, id: "AdvClientSecretRow"));
        }
        else
        {
            items.Add(Sk.Row("Tenant", null, Sk.Field(a.Tenant ?? "", v => Quiet(() => Mutate(x => x.Tenant = v.Length == 0 ? null : v)), "Tenant", "AdvTenant", "organizations", 320), id: "AdvTenantRow"));
        }
        var port = new NumberBox
        {
            Value = a.RedirectPort,
            Minimum = 1024,
            Maximum = 65535,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
            Width = 120,
            Tag = "AdvPort",
        };
        port.Id("AdvPort", "Sign-in port");
        port.ValueChanged += (_, e) =>
        {
            if (!double.IsNaN(e.NewValue)) Quiet(() => Mutate(x => x.RedirectPort = (int)e.NewValue));
        };
        items.Add(Sk.Row("Sign-in port", null, port, id: "AdvPortRow"));

        var uri = OAuth.RedirectUri(a);
        var uriRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var uriText = Sk.Text(uri, "DbBodyStyle");
        uriText.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas");
        uriText.IsTextSelectionEnabled = true;
        uriText.VerticalAlignment = VerticalAlignment.Center;
        uriRow.Children.Add(uriText);
        uriRow.Children.Add(Sk.IconBtn(Glyphs.Copy, "Copy redirect address", "AdvCopyRedirect", () => SettingsContext.CopyToClipboard(uri)));
        items.Add(Sk.Row("Redirect address", null, uriRow, id: "AdvRedirectRow"));

        var expander = Sk.Expander("Advanced", "Sign-in details. Most people never need these.", null, Glyphs.Wrench, "Advanced", _advancedOpen, [.. items]);
        expander.Margin = new Thickness(0, 24, 0, 0);
        expander.Expanded += (_, _) => _advancedOpen = true;
        expander.Collapsed += (_, _) => _advancedOpen = false;
        return expander;
    }

    // MARK: - Remove

    private async Task RemoveAsync(AccountConfig a)
    {
        var name = a.Label.Length == 0 ? "this account" : a.Label;
        var dialog = new ContentDialog
        {
            Title = $"Remove {name}?",
            Content = Sk.Text($"Desktop Buddy will sign out of {name} ({a.Email}) and forget it on this PC. Nothing changes in {a.Provider.Name()}."),
            PrimaryButtonText = "Remove account",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        dialog.Id("RemoveAccountDialog", $"Remove {name}");
        if (await Ctx.ShowAsync(dialog) != ContentDialogResult.Primary) return;
        Ctx.Navigate("accounts");
        State.RemoveAccount(a.Id);
    }
}
