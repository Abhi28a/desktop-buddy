using DesktopBuddy.App.Ui;
using DesktopBuddy.Core;
using DesktopBuddy.Presentation.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Auto = Microsoft.UI.Xaml.Automation.AutomationProperties;

namespace DesktopBuddy.App.Views.Settings;

/// Reminders: your own repeating nudges, which work offline (port of RemindersPane in SettingsWindow.swift).
/// Weekdays are stored as Mac numbers (1 = Sunday ... 7 = Saturday) so a config file loads on both platforms.
internal sealed class RemindersPage : SettingsPage
{
    public RemindersPage(SettingsContext ctx) : base(ctx) { }

    public override SettingsRoute Route => SettingsRoute.Reminders;

    protected override void Build(StackPanel body)
    {
        var top = new Grid { ColumnSpacing = 16, Margin = new Thickness(0, 0, 0, 16) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(Sk.Text("Your own nudges, like a glass of water or a stretch. They run on this PC, work offline and don't need any account.", "DbSecondaryStyle")
            .With(t => { t.TextTrimming = TextTrimming.None; t.VerticalAlignment = VerticalAlignment.Center; }));
        var add = Sk.Btn("Add reminder", () => _ = EditAsync(new Reminder { Title = "", Emoji = "⏰" }, isNew: true), "AddReminder", primary: true);
        Grid.SetColumn(add, 1);
        top.Children.Add(add);
        body.Children.Add(top);

        var reminders = State.Config.Reminders;
        if (reminders.Count == 0)
        {
            body.Children.Add(Sk.Block(new StackPanel { Spacing = 4 }.With(p =>
            {
                p.Children.Add(Sk.Text("No reminders yet", "DbRowTitleStyle"));
                p.Children.Add(Sk.Caption("Add one and it shows up in the Board at the times you choose."));
            })));
            return;
        }
        var rows = Sk.Group();
        foreach (var reminder in reminders.ToList()) rows.Children.Add(ReminderRow(reminder));
        body.Children.Add(rows);
    }

    private FrameworkElement ReminderRow(Reminder r)
    {
        var emoji = new TextBlock { Text = r.Emoji, FontSize = 24, Width = 36, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        emoji.Id($"ReminderEmoji_{r.Id}", "");
        var toggle = Sk.Switch(r.Enabled, on => Update(r.Id, x => x.Enabled = on), $"{r.Title} on", $"ReminderOn_{r.Id}");
        var edit = Sk.Btn("Edit", () => _ = EditAsync(r, isNew: false), $"ReminderEdit_{r.Id}", name: $"Edit {r.Title}");
        var row = Sk.LeadRow(emoji, r.Title, Sk.Caption(ReminderText.Summary(r)), Sk.Buttons(edit, toggle), $"ReminderRow_{r.Id}", r.Title);

        var menu = new MenuFlyout();
        var editItem = new MenuFlyoutItem { Text = "Edit" };
        editItem.Click += (_, _) => _ = EditAsync(r, isNew: false);
        var deleteItem = new MenuFlyoutItem { Text = "Delete" };
        deleteItem.Click += (_, _) => Delete(r.Id);
        menu.Items.Add(editItem);
        menu.Items.Add(deleteItem);
        row.ContextFlyout = menu;
        return row;
    }

    private static string EveryLabel(int m) => m == 60 ? "1 hour" : ReminderText.Every(m).Replace("Every ", "");

    private void Update(Guid id, Action<Reminder> change)
    {
        var i = State.Config.Reminders.FindIndex(x => x.Id == id);
        if (i < 0) return;
        change(State.Config.Reminders[i]);
        State.SaveConfig();
    }

    private void Delete(Guid id)
    {
        State.Config.Reminders.RemoveAll(x => x.Id == id);
        State.SaveConfig();
    }

    private async Task EditAsync(Reminder original, bool isNew)
    {
        var r = original with { Weekdays = [.. original.Weekdays] };
        var dialog = new ContentDialog
        {
            Title = isNew ? "Add reminder" : "Edit reminder",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.Id("ReminderDialog", isNew ? "Add reminder" : "Edit reminder");
        if (!isNew) dialog.SecondaryButtonText = "Delete";

        void Validate() => dialog.IsPrimaryButtonEnabled = r.Title.Trim().Length > 0 && r.Weekdays.Count > 0 && r.EndHour > r.StartHour;

        var emoji = new TextBox { Text = r.Emoji, Width = 64, TextAlignment = TextAlignment.Center, Header = "Emoji", MaxLength = 8 };
        emoji.Id("ReminderEmojiBox", "Emoji");
        emoji.TextChanged += (_, _) => r.Emoji = emoji.Text;
        var title = new TextBox { Text = r.Title, PlaceholderText = "Drink a glass of water", Header = "Title", MinWidth = 280 };
        title.Id("ReminderTitle", "Title");
        title.TextChanged += (_, _) => { r.Title = title.Text; Validate(); };
        var top = new Grid { ColumnSpacing = 12 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.Children.Add(emoji);
        Grid.SetColumn(title, 1);
        top.Children.Add(title);
        var note = new TextBox { Text = r.Note, PlaceholderText = "Optional", Header = "Note" };
        note.Id("ReminderNote", "Note");
        note.TextChanged += (_, _) => r.Note = note.Text;

        var every = new List<(string, int)>();
        foreach (var m in new[] { 15, 20, 30, 45, 60, 90, 120, 180, 240 })
        {
            every.Add((EveryLabel(m), m));
        }
        if (!every.Any(e => e.Item2 == r.EveryMinutes)) every.Add((EveryLabel(r.EveryMinutes), r.EveryMinutes));
        var repeat = Sk.Combo(every, r.EveryMinutes, v => r.EveryMinutes = v, "Repeat every", "ReminderEvery", 170);
        repeat.Header = "Repeat every";
        var from = Sk.Combo(Enumerable.Range(0, 24).Select(h => (Sk.HourText(h), h)).ToList(), r.StartHour, v => { r.StartHour = v; Validate(); }, "From", "ReminderFrom", 130);
        from.Header = "From";
        var until = Sk.Combo(Enumerable.Range(1, 24).Select(h => (Sk.HourText(h % 24), h)).ToList(), r.EndHour, v => { r.EndHour = v; Validate(); }, "Until", "ReminderUntil", 130);
        until.Header = "Until";
        var schedule = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        schedule.Children.Add(repeat);
        schedule.Children.Add(from);
        schedule.Children.Add(until);

        var days = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var (day, shortName, longName) in ReminderText.Week)
        {
            var d = day;
            var button = new ToggleButton { Content = shortName, IsChecked = r.Weekdays.Contains(d), Padding = new Thickness(0), Width = 46, Height = 34, MinWidth = 0 };
            button.Id($"ReminderDay_{d}", longName);
            button.Checked += (_, _) => { r.Weekdays.Add(d); Validate(); };
            button.Unchecked += (_, _) => { r.Weekdays.Remove(d); Validate(); };
            days.Children.Add(button);
        }
        var daysBlock = new StackPanel { Spacing = 6 };
        daysBlock.Children.Add(Sk.Caption("On"));
        daysBlock.Children.Add(days);

        var content = new StackPanel { Spacing = 16, Width = 480 };
        content.Children.Add(top);
        content.Children.Add(note);
        content.Children.Add(schedule);
        content.Children.Add(daysBlock);
        dialog.Content = content;
        Validate();

        var result = await Ctx.ShowAsync(dialog);
        if (result == ContentDialogResult.Secondary) { Delete(r.Id); return; }
        if (result != ContentDialogResult.Primary) return;
        r.Title = r.Title.Trim();
        var i = State.Config.Reminders.FindIndex(x => x.Id == r.Id);
        if (i >= 0) State.Config.Reminders[i] = r; else State.Config.Reminders.Add(r);
        State.SaveConfig();
    }
}
