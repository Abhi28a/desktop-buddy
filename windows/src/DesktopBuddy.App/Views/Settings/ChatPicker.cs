using DesktopBuddy.App.Ui;
using DesktopBuddy.Core;
using DesktopBuddy.Presentation.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DesktopBuddy.App.Views.Settings;

/// "Chats for {client}": pick Google Chat spaces and Teams chats from every signed-in account
/// (port of ChatPickerSheet in ClientSettings.swift). Also used to choose chats to mute.
internal static class ChatPicker
{
    /// Returns the picked chats, or null if the person cancelled.
    public static async Task<List<ChatRef>?> ShowAsync(SettingsContext ctx, IEnumerable<ChatRef> selected, string clientName,
                                                       string? title = null, string? accountId = null)
    {
        if (ctx.Preview) return null;
        var state = ctx.State;
        var picked = selected.Select(c => c.Clone()).ToHashSet();
        var all = new List<ChatRef>();
        var errors = new List<string>();
        var loading = true;
        var namesDone = 0;
        var namesTotal = 0;
        var cancel = new CancellationTokenSource();

        var search = new TextBox { PlaceholderText = "Search chats", Margin = new Thickness(0, 0, 0, 8) };
        search.Id("ChatSearch", "Search chats");
        var list = new StackPanel { Spacing = 0 };
        var count = Sk.Caption("");
        var scroll = new ScrollViewer { Content = list, Height = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        void UpdateCount() => count.Text = $"{picked.Count} selected";

        List<(string AccountId, List<ChatRef> Chats)> Groups()
        {
            var q = search.Text.Trim();
            var filtered = all.Where(c => q.Length == 0 || c.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
            var result = new List<(string, List<ChatRef>)>();
            foreach (var account in state.Config.Accounts)
            {
                var chats = filtered.Where(c => c.AccountID == account.Id)
                    .OrderByDescending(c => clientName.Length > 0 && c.Name.Contains(clientName, StringComparison.CurrentCultureIgnoreCase))
                    .ThenByDescending(c => c.LastActive ?? DateTimeOffset.MinValue)
                    .ToList();
                if (chats.Count > 0) result.Add((account.Id, chats));
            }
            return result;
        }

        string Subtitle(ChatRef c)
        {
            var kind = c.Kind == ChatKind.direct ? "Direct message" : c.Kind == ChatKind.group ? "Group chat" : "Space";
            if (c.LastActive is not { } t) return kind;
            var ago = TokenRules.ShortAgo(t, state.Now());
            return $"{kind} · active {(ago == "now" ? "just now" : ago + " ago")}";
        }

        void Refresh()
        {
            list.Children.Clear();
            if (loading)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 16) };
                row.Children.Add(new ProgressRing { IsActive = true, Width = 20, Height = 20 });
                row.Children.Add(Sk.Caption(namesTotal > 0 ? $"Finding names for direct messages… {namesDone} of {namesTotal}" : "Loading your chats…").With(t => t.VerticalAlignment = VerticalAlignment.Center));
                list.Children.Add(row);
            }
            foreach (var e in errors) list.Children.Add(Sk.StatusText(StatusLevel.Caution, e, 460).With(p => p.Margin = new Thickness(0, 4, 0, 4)));
            foreach (var (id, chats) in Groups())
            {
                var account = state.Account(id);
                var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(4, 14, 0, 6) };
                if (account is not null)
                {
                    header.Children.Add(LogoTile.ProviderBadge(account.Provider, 16));
                    header.Children.Add(Sk.Text(account.Label, "DbRowTitleStyle"));
                }
                list.Children.Add(header);
                if (account is not null && state.BlockerFor(account, ItemSource.chat) is { } blocker)
                {
                    var warn = new Grid { ColumnSpacing = 8, Margin = new Thickness(4, 0, 0, 6) };
                    warn.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    warn.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    warn.Children.Add(Sk.StatusText(StatusLevel.Caution, $"{blocker.Text}. Chats you pick here won't be checked until it's on.", 380));
                    if (blocker.Fix.Kind == ClientFixKind.TurnOnService)
                    {
                        var fix = Sk.Btn(FixText.Sentence(blocker.FixLabel), () => { state.Apply(blocker.Fix, ""); Refresh(); }, "ChatPickerFix");
                        Grid.SetColumn(fix, 1);
                        warn.Children.Add(fix);
                    }
                    list.Children.Add(warn);
                }
                foreach (var chat in chats)
                {
                    var c = chat;
                    var row = new Grid { ColumnSpacing = 10 };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var icon = Sk.Icon(AccountPage.ChatGlyph(c), 13);
                    row.Children.Add(icon);
                    var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    var name = Sk.Text(c.Name);
                    name.TextWrapping = TextWrapping.NoWrap;
                    name.TextTrimming = TextTrimming.CharacterEllipsis;
                    text.Children.Add(name);
                    text.Children.Add(Sk.Caption(Subtitle(c)));
                    Grid.SetColumn(text, 1);
                    row.Children.Add(text);
                    if (clientName.Length > 0 && c.Name.Contains(clientName, StringComparison.CurrentCultureIgnoreCase))
                    {
                        var tag = Sk.Pill("Suggested");
                        Grid.SetColumn(tag, 2);
                        row.Children.Add(tag);
                    }
                    var box = new CheckBox { Content = row, IsChecked = picked.Contains(c), MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                    box.Id($"ChatPick_{c.ChatID}", c.Name);
                    box.Checked += (_, _) => { picked.Add(c); UpdateCount(); };
                    box.Unchecked += (_, _) => { picked.Remove(c); UpdateCount(); };
                    list.Children.Add(box);
                }
            }
            if (!loading && all.Count == 0 && errors.Count == 0)
            {
                var empty = Sk.Caption("No chats found.");
                empty.Margin = new Thickness(0, 20, 0, 0);
                empty.HorizontalAlignment = HorizontalAlignment.Center;
                list.Children.Add(empty);
            }
            UpdateCount();
        }

        search.TextChanged += (_, _) => Refresh();

        var content = new StackPanel { Width = 520 };
        content.Children.Add(Sk.Caption("Google Chat spaces and Teams chats you're in"));
        content.Children.Add(new Border { Height = 12 });
        content.Children.Add(search);
        content.Children.Add(scroll);
        content.Children.Add(new Border { Height = 8 });
        content.Children.Add(count);

        var dialog = new ContentDialog
        {
            Title = title ?? $"Chats for {clientName}",
            Content = content,
            PrimaryButtonText = "Done",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.Id("ChatPickerDialog", title ?? $"Chats for {clientName}");
        dialog.Resources["ContentDialogMaxWidth"] = 640.0;
        Refresh();

        // Load while the dialog is open; name lookups for direct messages report progress.
        var eligible = state.Config.Accounts
            .Where(a => a.Enabled && state.SignedIn.Contains(a.Id) && (accountId is null || a.Id == accountId))
            .Select(a => a.Clone()).ToList();
        _ = Task.Run(async () =>
        {
            foreach (var account in eligible)
            {
                try
                {
                    var chats = await ChatDirectory.ListAsync(account, (done, total) => SettingsContext.OnUi(() =>
                    {
                        if (cancel.IsCancellationRequested) return;
                        namesDone = done;
                        namesTotal = total;
                        Refresh();
                    }), cancel.Token).ConfigureAwait(false);
                    SettingsContext.OnUi(() => { if (!cancel.IsCancellationRequested) { all.AddRange(chats); Refresh(); } });
                }
                catch (OperationCanceledException) { return; }
                catch (Exception e)
                {
                    var message = $"{account.Label}: {e.Message}";
                    SettingsContext.OnUi(() => { if (!cancel.IsCancellationRequested) { errors.Add(message); Refresh(); } });
                }
            }
            SettingsContext.OnUi(() => { if (!cancel.IsCancellationRequested) { loading = false; Refresh(); } });
        });

        var result = await ctx.ShowAsync(dialog);
        cancel.Cancel();
        return result == ContentDialogResult.Primary ? picked.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList() : null;
    }
}
