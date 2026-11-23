using CommunityToolkit.WinUI.Controls;
using DesktopBuddy.App.Ui;
using DesktopBuddy.App.Ui.Components;
using DesktopBuddy.Core;
using DesktopBuddy.Presentation.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DesktopBuddy.App.Views.Settings;

/// Clients: the list, and Add client.
internal sealed class ClientsPage : SettingsPage
{
    public ClientsPage(SettingsContext ctx) : base(ctx) { }

    public override SettingsRoute Route => SettingsRoute.Clients;

    /// "1 sender, 2 keywords" (or "Off", or "No rules yet").
    public static string Summary(ClientConfig c)
    {
        if (!c.Enabled) return "Off";
        var parts = new List<string>();
        if (c.Senders.Count > 0) parts.Add(TimeText.Plural(c.Senders.Count, "sender", "senders"));
        if (c.Keywords.Count > 0) parts.Add(TimeText.Plural(c.Keywords.Count, "keyword", "keywords"));
        if (c.Chats.Count > 0) parts.Add(TimeText.Plural(c.Chats.Count, "chat", "chats"));
        return parts.Count == 0 ? "No rules yet" : string.Join(", ", parts);
    }

    protected override void Build(StackPanel body)
    {
        var top = new Grid { ColumnSpacing = 16, Margin = new Thickness(0, 0, 0, 16) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(Sk.Text("People you hear from but don't have an account for. Their mail and chats show under their own token.", "DbSecondaryStyle")
            .With(t => { t.TextTrimming = TextTrimming.None; t.VerticalAlignment = VerticalAlignment.Center; }));
        var add = Sk.Btn("Add client", () => _ = AddClientFlow.RunAsync(Ctx), "AddClient", primary: true);
        Grid.SetColumn(add, 1);
        top.Children.Add(add);
        body.Children.Add(top);

        if (State.Config.Clients.Count == 0)
        {
            body.Children.Add(Sk.Block(new StackPanel { Spacing = 4 }.With(p =>
            {
                p.Children.Add(Sk.Text("No clients yet", "DbRowTitleStyle"));
                p.Children.Add(Sk.Caption("Add a client to group their mail and chats under one token."));
            })));
            return;
        }

        var list = Sk.Group();
        foreach (var client in State.Config.Clients)
        {
            var c = client;
            var issues = c.Enabled ? State.ClientIssues(c) : [];
            var waiting = State.Items.Count(i => i.ClientID == c.Id);
            var token = Sk.Token(State.OwnerLogo(c.Id), c.Name, c.Tint, 40, waiting);
            var sub = new StackPanel { Spacing = 2 };
            sub.Children.Add(Sk.Caption(Summary(c)));
            if (issues.Count > 0) sub.Children.Add(Sk.StatusText(StatusLevel.Caution, issues.Count == 1 ? "1 thing to fix" : $"{issues.Count} things to fix"));
            var name = c.Name.Length == 0 ? "Client" : c.Name;
            var description = $"{name}, {Summary(c)}";
            if (issues.Count > 0) description += $", {TimeText.Plural(issues.Count, "issue", "issues")}";
            if (waiting > 0) description += $", {waiting} waiting";
            list.Children.Add(Sk.LeadRow(token, name, sub, waiting > 0 ? Sk.Caption($"{waiting} waiting") : null, $"ClientRow_{c.Id}", description)
                .Clickable(() => Ctx.Navigate("client:" + c.Id)));
        }
        body.Children.Add(list);
    }
}

/// One client: header, what needs attention, sender and keyword rules, picked chats, which accounts
/// to look in, appearance (port of ClientDetailView in ClientSettings.swift).
internal sealed class ClientPage : SettingsPage
{
    private readonly string _id;
    private TextBlock? _headerName;

    public ClientPage(SettingsContext ctx, string id) : base(ctx) => _id = id;

    public override SettingsRoute Route => SettingsRoute.ForClient(_id);
    public override bool Exists => State.Client(_id) is not null;

    private void Mutate(Action<ClientConfig> change)
    {
        if (State.Client(_id) is not { } c) return;
        change(c);
        State.UpdateClient(c);
    }

    private int Waiting => State.Items.Count(i => i.ClientID == _id);

    protected override void Build(StackPanel body)
    {
        if (State.Client(_id) is not { } c) return;
        var name = c.Name.Length == 0 ? "this client" : c.Name;

        body.Children.Add(Header(c));

        var issues = State.ClientIssues(c);
        if (issues.Count > 0)
        {
            body.Children.Add(Sk.Heading("Needs attention"));
            var list = new StackPanel { Spacing = 8 };
            foreach (var issue in issues) list.Children.Add(IssueLine(c, issue));
            body.Children.Add(list);
        }

        body.Children.Add(Sk.Heading("Email"));
        body.Children.Add(Sk.Group(
            Sk.Block(new TokenField("Senders", "Type a domain or address, e.g. gatehouse.co.uk",
                "A whole domain (gatehouse.co.uk) or one person (jane@gatehouse.co.uk). Press Return to add.",
                c.Senders, Glyphs.Person, TokenRules.SenderProblem,
                tokens => Mutate(x => x.Senders = tokens.ToList()), Problems, "Senders")),
            Sk.Block(new TokenField("Keywords", "Type a word to look for, e.g. invoice",
                "Matched as a whole word in the subject or preview, so “FEMA” won't match “female”.",
                c.Keywords, Glyphs.Search, TokenRules.KeywordProblem,
                tokens => Mutate(x => x.Keywords = tokens.ToList()), Problems, "Keywords"))));
        body.Children.Add(Sk.Note($"New unread inbox mail that comes from a sender above, or contains a keyword, shows under {name}. Mail you've already read or filed away never interrupts you."));

        ChatsSection(body, c, issues);
        LookInSection(body, c);

        body.Children.Add(Sk.Heading("Appearance"));
        body.Children.Add(Sk.Group(
            Sk.Row("Name", null, Sk.Field(c.Name, text =>
            {
                Quiet(() => Mutate(x => x.Name = text));
                if (_headerName is not null) _headerName.Text = text.Length == 0 ? "Client" : text;
            }, "Client name", "ClientName", "e.g. Gatehouse", width: 280), Glyphs.Edit, "ClientNameRow"),
            Sk.Row("Colour", "Rings the token in this colour.", Sk.TintPicker(c.Tint, t => Mutate(x => x.Tint = t), "Client colour", "ClientTint"), Glyphs.Palette, "ClientTintRow")));

        body.Children.Add(Sk.Heading("Remove"));
        body.Children.Add(Sk.Row("Remove client", "Its mail and chats will show under their accounts again.",
            Sk.Btn("Remove client", () => _ = RemoveAsync(c), "RemoveClient"), Glyphs.Delete, "RemoveClientRow"));
    }

    // MARK: - Header and issues

    private FrameworkElement Header(ClientConfig c)
    {
        var grid = new Grid { ColumnSpacing = 18 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Sk.Token(State.OwnerLogo(c.Id), c.Name, c.Tint, 64, Waiting).With(t => t.VerticalAlignment = VerticalAlignment.Top));

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        _headerName = Sk.Text(c.Name.Length == 0 ? "Client" : c.Name, "DbPanelTitleStyle");
        _headerName.TextTrimming = TextTrimming.CharacterEllipsis;
        _headerName.TextWrapping = TextWrapping.NoWrap;
        _headerName.Id("ClientHeaderName");
        text.Children.Add(_headerName);
        text.Children.Add(Sk.Text(ScopeText(c), "DbSecondaryStyle"));

        var waiting = Waiting;
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        line.Children.Add(Sk.Caption(waiting == 0 ? "Nothing waiting right now" : $"{TimeText.Plural(waiting, "item", "items")} waiting").With(t => t.VerticalAlignment = VerticalAlignment.Center));
        if (waiting > 0)
        {
            line.Children.Add(Sk.Link("Show in Board", () =>
            {
                if (!Ctx.Preview) App.Instance?.OpenBoard(BoardAnchor.Tray, c.Id);
            }, "ClientShowBoard"));
        }
        text.Children.Add(line);

        var buttons = Sk.Buttons(
            Sk.Btn("Change logo", () => _ = ChooseLogoAsync(), "ClientLogoChoose", name: "Change logo"),
            c.LogoPath is not null ? Sk.Btn("Use default", () => Mutate(x => x.LogoPath = null), "ClientLogoDefault") : null);
        buttons.Margin = new Thickness(0, 8, 0, 0);
        buttons.HorizontalAlignment = HorizontalAlignment.Left;
        text.Children.Add(buttons);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var toggle = Sk.Switch(c.Enabled, on => Mutate(x => x.Enabled = on), "Client on", "ClientEnabled");
        ToolTipService.SetToolTip(toggle, c.Enabled ? "Turn off to stop sorting items into this client" : "Turn on");
        var holder = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Top };
        holder.Children.Add(Sk.Caption(c.Enabled ? "On" : "Off").With(t => t.VerticalAlignment = VerticalAlignment.Center));
        holder.Children.Add(toggle);
        Grid.SetColumn(holder, 2);
        grid.Children.Add(holder);
        return new Border { Style = Sk.Sty("DbCardStyle"), Padding = new Thickness(20, 18, 20, 18), Child = grid, Margin = new Thickness(0, 0, 0, 8) };
    }

    private string ScopeText(ClientConfig c)
    {
        var names = c.AccountIDs.Select(id => State.Account(id)?.Label).Where(n => n is not null).Select(n => n!).ToList();
        if (c.AccountIDs.Count == 0 || names.Count == State.Config.Accounts.Count) return "Client, watching all your accounts";
        return $"Client, watching {TimeText.Join(names)}";
    }

    private FrameworkElement IssueLine(ClientConfig c, ClientIssue issue)
    {
        var fixLabel = FixText.Sentence(issue.FixLabel);
        var line = new StatusLine(StatusSeverity.Caution, issue.Text, null,
            issue.Fix is not null && fixLabel.Length > 0 ? fixLabel : null, () => { if (issue.Fix is { } f) State.Apply(f, c.Id); })
        {
            AutomationKey = $"ClientIssue_{issue.Id}",
        };
        return Sk.Block(line, new Thickness(4));
    }

    private async Task ChooseLogoAsync()
    {
        var path = await Ctx.ChooseLogoAsync("client");
        if (path is not null) Mutate(x => x.LogoPath = path);
    }

    // MARK: - Chats

    private static string ChatKindText(ChatRef c) => c.Kind switch
    {
        ChatKind.direct => "Direct message",
        ChatKind.group => "Group chat",
        ChatKind.space => "Space",
        _ => "Chat",
    };

    private void ChatsSection(StackPanel body, ClientConfig c, List<ClientIssue> issues)
    {
        var name = c.Name.Length == 0 ? "this client" : c.Name;
        body.Children.Add(Sk.Heading("Google Chat and Teams"));
        var rows = Sk.Group();
        foreach (var chat in c.Chats)
        {
            var ch = chat;
            var problem = issues.FirstOrDefault(i => i.Chat == ch);
            var account = State.Account(ch.AccountID);
            FrameworkElement lead = account is null
                ? Sk.Icon(AccountPage.ChatGlyph(ch), 20).With(i => i.Margin = new Thickness(4, 0, 4, 0))
                : Sk.AccountToken(State, account, 28);

            UIElement sub = problem is not null
                ? Sk.StatusText(StatusLevel.Caution, "Not being checked")
                : Sk.Caption($"{ChatKindText(ch)} · {account?.Label ?? ""} · being checked");

            var trailing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            if (problem?.Fix is { } fix && problem.FixLabel is { } label)
            {
                var text = FixText.Sentence(label);
                trailing.Children.Add(Sk.Btn(text, () => State.Apply(fix, c.Id), $"ChatFix_{ch.ChatID}", name: $"{text} for {ch.Name}"));
            }
            trailing.Children.Add(Sk.IconBtn(Glyphs.Close, $"Remove {ch.Name}", $"ChatRemove_{ch.ChatID}", () => Mutate(x => x.Chats.RemoveAll(m => m == ch))));
            var row = Sk.LeadRow(lead, ch.Name, sub, trailing, $"ChatRow_{ch.ChatID}", ch.Name);
            if (problem is not null) ToolTipService.SetToolTip(row, problem.Text);
            rows.Children.Add(row);
        }

        var anySignedIn = State.Config.Accounts.Any(a => State.SignedIn.Contains(a.Id));
        var addRow = Sk.Row(c.Chats.Count == 0 ? "No chats picked" : "Add more chats", anySignedIn ? null : "Sign in to an account first.",
            Sk.Btn("Add chats", () => _ = PickChatsAsync(c), "AddChats", enabled: anySignedIn), Glyphs.Chat, "AddChatsRow");
        rows.Children.Add(addRow);
        rows.Children.Add(Sk.Row($"Also match chats named after {name}",
            $"Spaces and group chats whose name includes “{c.Name}”, and direct messages with someone of that name.",
            Sk.Switch(c.MatchChatNames, on => Mutate(x => x.MatchChatNames = on), "Match chat names", "MatchChatNames"), null, "MatchChatNamesRow"));
        body.Children.Add(rows);
        body.Children.Add(Sk.Note("Pick the Google Chat spaces and Teams chats you share with this client. New messages there show under the client's token instead of the account's."));
    }

    private async Task PickChatsAsync(ClientConfig c)
    {
        var picked = await ChatPicker.ShowAsync(Ctx, c.Chats, c.Name);
        if (picked is not null) Mutate(x => x.Chats = picked);
    }

    // MARK: - Look in

    private void LookInSection(StackPanel body, ClientConfig c)
    {
        body.Children.Add(Sk.Heading("Accounts"));
        var selected = c.AccountIDs.Count > 0;
        var combo = Sk.Combo([("All accounts", false), ("Selected accounts", true)], selected,
            some => Mutate(x => x.AccountIDs = some ? State.Config.Accounts.Select(a => a.Id).ToList() : []), "Look in", "LookIn", 200);
        var items = new List<SettingsCard>();
        if (selected)
        {
            foreach (var account in State.Config.Accounts)
            {
                var a = account;
                var on = c.AccountIDs.Contains(a.Id);
                var toggle = Sk.Switch(on, v => Mutate(x =>
                {
                    if (v) { if (!x.AccountIDs.Contains(a.Id)) x.AccountIDs.Add(a.Id); }
                    else x.AccountIDs.RemoveAll(id => id == a.Id);
                }), $"Look in {a.Label}", $"LookIn_{a.Id}");
                // Keep at least one account selected (otherwise it would silently mean "all").
                toggle.IsEnabled = !(c.AccountIDs.Count == 1 && on);
                items.Add(Sk.LeadRow(Sk.AccountToken(State, a, 28), a.Label, Sk.Caption(a.Email), toggle, $"LookInRow_{a.Id}", a.Label));
            }
        }
        body.Children.Add(Sk.Expander("Look in", "Which accounts this client's rules are checked in.", combo, Glyphs.Person, "LookInGroup", expanded: true, [.. items]));
    }

    // MARK: - Remove

    private async Task RemoveAsync(ClientConfig c)
    {
        var name = c.Name.Length == 0 ? "this client" : c.Name;
        var dialog = new ContentDialog
        {
            Title = $"Remove {name}?",
            Content = Sk.Text($"{(c.Name.Length == 0 ? "Its" : c.Name + "'s")} mail and chats will show under their accounts again."),
            PrimaryButtonText = "Remove client",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        dialog.Id("RemoveClientDialog", $"Remove {name}");
        if (await Ctx.ShowAsync(dialog) != ContentDialogResult.Primary) return;
        Ctx.Navigate("clients");
        State.RemoveClient(c.Id);
    }
}

/// "Add client": a small form in a dialog (port of AddClientSheet in ClientSettings.swift).
internal static class AddClientFlow
{
    public static async Task RunAsync(SettingsContext ctx)
    {
        if (ctx.Preview) return;
        var state = ctx.State;
        var draft = new ClientConfig();
        var addNameKeyword = true;

        var preview = new ContentControl();
        void ShowPreview() => preview.Content = Sk.Token(Logos.Resolve(draft.LogoPath, draft.Name), draft.Name.Length == 0 ? "?" : draft.Name, draft.Tint, 48, surface: "DbSurfaceBaseBrush");

        var dialog = new ContentDialog
        {
            Title = "Add client",
            PrimaryButtonText = "Add client",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
        };
        dialog.Id("AddClientDialog", "Add client");
        var warning = Sk.StatusText(StatusLevel.Caution, "");
        warning.Visibility = Visibility.Collapsed;
        var keywordText = Sk.Text("");
        var logoButton = Sk.Btn("Choose…", () => { }, "AddClientLogo", name: "Choose a logo");
        var colors = new ContentControl();

        bool Duplicate()
        {
            var n = draft.Name.Trim();
            return n.Length > 0 && state.Config.Clients.Any(x => string.Equals(x.Name, n, StringComparison.CurrentCultureIgnoreCase));
        }

        void Update()
        {
            var duplicate = Duplicate();
            dialog.IsPrimaryButtonEnabled = draft.Name.Trim().Length > 0 && !duplicate;
            warning.Visibility = duplicate ? Visibility.Visible : Visibility.Collapsed;
            if (warning.Children.Count > 1 && warning.Children[1] is TextBlock t) t.Text = $"You already have a client called {draft.Name.Trim()}.";
            keywordText.Text = $"Also sort emails that mention “{(draft.Name.Trim().Length == 0 ? "the name" : draft.Name.Trim())}”";
            ShowPreview();
        }

        void ShowColors() => colors.Content = Sk.TintPicker(draft.Tint, t => { draft.Tint = t; ShowColors(); Update(); }, "Client colour", "AddClientTint");

        var name = new TextBox { PlaceholderText = "e.g. Gatehouse", Header = "Name", MinWidth = 320 };
        name.Id("AddClientName", "Client name");
        name.TextChanged += (_, _) => { draft.Name = name.Text; Update(); };
        logoButton.Click += async (_, _) =>
        {
            var path = await ctx.ChooseLogoAsync("client");
            if (path is not null) { draft.LogoPath = path; logoButton.Content = "Change…"; Update(); }
        };
        var check = new CheckBox { IsChecked = true, Content = keywordText };
        check.Id("AddClientNameKeyword", "Also sort emails that mention the name");
        check.Checked += (_, _) => addNameKeyword = true;
        check.Unchecked += (_, _) => addNameKeyword = false;

        var content = new StackPanel { Spacing = 16, Width = 400 };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        top.Children.Add(preview);
        var intro = Sk.Caption("For clients who email or chat with you but don't give you an account.");
        intro.MaxWidth = 320;
        intro.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(intro);
        content.Children.Add(top);
        content.Children.Add(name);
        var logoRow = new StackPanel { Spacing = 4 };
        logoRow.Children.Add(Sk.Caption("Logo"));
        logoRow.Children.Add(logoButton);
        content.Children.Add(logoRow);
        var colorRow = new StackPanel { Spacing = 4 };
        colorRow.Children.Add(Sk.Caption("Colour"));
        colorRow.Children.Add(colors);
        content.Children.Add(colorRow);
        content.Children.Add(check);
        content.Children.Add(warning);
        dialog.Content = content;
        ShowColors();
        Update();

        if (await ctx.ShowAsync(dialog) != ContentDialogResult.Primary) return;
        var c = draft with { Name = draft.Name.Trim() };
        if (addNameKeyword) c.Keywords = [c.Name];
        state.AddClient(c);
        ctx.Navigate("client:" + c.Id);
    }
}
