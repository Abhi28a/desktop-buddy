using DesktopBuddy.App.Ui;
using DesktopBuddy.Core;
using DesktopBuddy.Presentation.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace DesktopBuddy.App.Views.Settings;

/// The "Mail" section of an account page: which mail notifies (port of MailFilterSections in MailFilterSettings.swift).
internal static class MailFilters
{
    public static void Build(StackPanel body, SettingsPage page, AccountConfig a, Action<Action<AccountConfig>> mutate, SettingsContext ctx)
    {
        var word = a.Provider == Provider.google ? "label" : "category";
        var plural = a.Provider == Provider.google ? "labels" : "categories";

        body.Children.Add(Sk.Heading("Mail"));
        var rows = Sk.Group();
        rows.Children.Add(Sk.Row("Which mail notifies you", null,
            Sk.Combo(
                [("All new inbox mail", false), ($"Only mail with chosen {plural}", true)], a.LabelsOnly,
                only => mutate(x => x.OnlyLabelsList = only ? (x.OnlyLabelsList ?? []) : null), "Mail to notify about", "MailScope", 260),
            Glyphs.Mail, id: "MailScopeRow"));

        if (a.LabelsOnly)
        {
            var panel = new StackPanel { Spacing = 10 };
            var header = new Grid { ColumnSpacing = 10 };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = Sk.Icon(Glyphs.Tag, 16);
            icon.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(icon);
            var title = Sk.Text(char.ToUpperInvariant(plural[0]) + plural[1..]);
            title.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(title, 1);
            header.Children.Add(title);
            var add = Sk.Btn($"Add {word}", () => { }, "MailLabelAdd", name: $"Add {word}");
            add.Click += (_, _) => ShowPicker(ctx, page, add, a, word, plural, mutate);
            Grid.SetColumn(add, 2);
            header.Children.Add(add);
            panel.Children.Add(header);

            if (a.OnlyLabels.Count == 0)
            {
                panel.Children.Add(Sk.StatusText(StatusLevel.Caution, $"No {word} chosen yet, so no mail will notify you."));
            }
            else
            {
                var wrap = new WrapPanel();
                foreach (var label in a.OnlyLabels)
                {
                    var l = label;
                    wrap.Children.Add(Sk.Chip(l.Name, Glyphs.Tag, () => mutate(x => x.OnlyLabels.RemoveAll(m => m.Id == l.Id)), $"Remove {l.Name}", $"MailLabel_remove_{l.Id}"));
                }
                panel.Children.Add(wrap);
            }
            rows.Children.Add(Sk.Block(panel));
        }
        body.Children.Add(rows);
        body.Children.Add(Sk.Note(a.LabelsOnly
            ? $"Only unread mail with one of these {plural} notifies you, even if a rule moved it out of your inbox."
            : $"Unread mail that arrives in your inbox notifies you. To hear only about mail your rules {(a.Provider == Provider.google ? "label" : "categorise")}, choose “Only mail with chosen {plural}”."));
    }

    /// A flyout listing the account's Gmail labels or recent Outlook categories, with search.
    private static void ShowPicker(SettingsContext ctx, SettingsPage page, FrameworkElement anchor, AccountConfig account,
                                   string word, string plural, Action<Action<AccountConfig>> mutate)
    {
        if (ctx.Preview) return;
        var all = new List<MailLabel>();
        var loading = true;
        string? error = null;
        var search = new TextBox { PlaceholderText = $"Search {plural}", MinWidth = 260 };
        search.Id("MailLabelSearch", $"Search {plural}");
        var list = new StackPanel();
        var flyout = new Flyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
        var cancel = new CancellationTokenSource();
        var shownNow = new List<MailLabel>();

        void Pick(MailLabel label)
        {
            mutate(x => { if (!x.OnlyLabels.Any(l => l.Id == label.Id)) x.OnlyLabels.Add(label); });
            flyout.Hide();
        }

        void AddTyped()
        {
            var name = search.Text.Trim();
            if (account.Provider != Provider.microsoft || name.Length == 0) return;
            Pick(new MailLabel { Id = name, Name = name });
        }

        void Refresh()
        {
            list.Children.Clear();
            if (loading)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 20, 0, 20) };
                row.Children.Add(new ProgressRing { IsActive = true, Width = 18, Height = 18 });
                row.Children.Add(Sk.Caption($"Loading {plural}…"));
                list.Children.Add(row);
                return;
            }
            if (error is not null)
            {
                list.Children.Add(Sk.StatusText(StatusLevel.Caution, error, 240));
                return;
            }
            var q = search.Text.Trim();
            var taken = account.OnlyLabels.Select(l => l.Id).ToHashSet();
            var shown = shownNow = all.Where(l => !taken.Contains(l.Id) && (q.Length == 0 || l.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase))).ToList();
            if (shown.Count == 0 && q.Length == 0)
            {
                var empty = Sk.Caption(all.Count == 0 ? $"No {plural} found." : $"All your {plural} are already chosen.");
                empty.Margin = new Thickness(0, 20, 0, 20);
                empty.HorizontalAlignment = HorizontalAlignment.Center;
                list.Children.Add(empty);
            }
            foreach (var label in shown)
            {
                var l = label;
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                row.Children.Add(Sk.Icon(Glyphs.Tag, 12));
                row.Children.Add(Sk.Text(l.Name));
                var b = new Button
                {
                    Content = row,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Style = Sk.Sty("DbQuietButtonStyle"),
                    Padding = new Thickness(8, 6, 8, 6),
                };
                b.Id($"MailLabelOption_{l.Id}", l.Name);
                b.Click += (_, _) => Pick(l);
                list.Children.Add(b);
            }
            // Outlook only lists categories seen on recent mail, so allow typing one in.
            if (account.Provider == Provider.microsoft && q.Length > 0 && !all.Any(l => string.Equals(l.Name, q, StringComparison.CurrentCultureIgnoreCase)))
            {
                list.Children.Add(Sk.Btn($"Add “{q}”", AddTyped, "MailLabelAddTyped"));
            }
        }

        search.TextChanged += (_, _) => Refresh();
        search.KeyDown += (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            if (shownNow.Count > 0) Pick(shownNow[0]);
            else AddTyped();
        };

        var content = new StackPanel { Spacing = 8, Width = 280 };
        content.Children.Add(search);
        content.Children.Add(new ScrollViewer { Content = list, MaxHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        if (account.Provider == Provider.microsoft) content.Children.Add(Sk.Caption("Shows categories used on your recent mail. Type a name to add another."));
        flyout.Content = content;
        flyout.Opened += (_, _) => { page.Hold(); search.Focus(FocusState.Programmatic); };
        flyout.Closed += (_, _) => { cancel.Cancel(); page.Release(); };
        Refresh();
        flyout.ShowAt(anchor);

        _ = Task.Run(async () =>
        {
            List<MailLabel>? result = null;
            string? failure = null;
            try
            {
                var api = new ApiClient(account);
                result = account.Provider == Provider.google ? await GmailSource.LabelsAsync(api, cancel.Token) : await OutlookMailSource.CategoriesAsync(api, cancel.Token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e) { failure = $"Couldn't load your {plural}: {e.Message}"; }
            SettingsContext.OnUi(() =>
            {
                if (cancel.IsCancellationRequested) return;
                if (result is not null) all = result;
                error = failure;
                loading = false;
                Refresh();
            });
        });
    }
}
