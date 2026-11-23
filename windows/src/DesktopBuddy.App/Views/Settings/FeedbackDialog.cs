using DesktopBuddy.App.Ui;
using DesktopBuddy.App.Ui.Components;
using DesktopBuddy.Core;
using DesktopBuddy.Presentation.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DesktopBuddy.App.Views.Settings;

/// "Report a bug or an idea": a small form in a dialog (port of FeedbackView in Feedback.swift).
/// Nothing is sent except what the person typed and, if they leave it on, the app and Windows version.
internal static class FeedbackDialog
{
    public static async Task ShowAsync(SettingsContext ctx)
    {
        var state = ctx.State;
        var report = new FeedbackReport { ReplyTo = state.Config.Accounts.FirstOrDefault()?.Email ?? "" };
        var google = state.Config.Accounts.Count(a => a.Provider == Provider.google);
        var details = FeedbackSender.Details(google, state.Config.Accounts.Count - google);
        var sent = false;
        var sending = false;

        var dialog = new ContentDialog
        {
            Title = "Report a bug or idea",
            PrimaryButtonText = "Send report",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
        };
        dialog.Id("FeedbackDialog", "Report a bug or idea");

        var kind = new RadioButtons { MaxColumns = 2 };
        foreach (var k in FeedbackKindInfo.All) kind.Items.Add(k.Title());
        kind.SelectedIndex = 0;
        kind.Id("FeedbackKind", "What kind of report");

        var messageLabel = Sk.Text("What happened?", "DbRowTitleStyle");
        var message = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 140, PlaceholderText = "e.g. The logo didn't update after I renamed a client." };
        message.Id("FeedbackMessage", "Your message");
        var email = new TextBox { Text = report.ReplyTo, PlaceholderText = "name@company.com" };
        email.Id("FeedbackEmail", "Your email");
        var emailProblem = Sk.StatusText(StatusLevel.Critical, "That doesn't look like an email address.");
        emailProblem.Visibility = Visibility.Collapsed;
        var include = new CheckBox { IsChecked = true, Content = "Include app and Windows version" };
        include.Id("FeedbackIncludeDetails", "Include app and Windows version");
        var detailsText = Sk.Caption(details);
        var notSet = new StatusLine(StatusSeverity.Caution, "Reporting isn't set up in this copy of Desktop Buddy yet",
            "Ask your admin for a copy with it built in.") { AutomationKey = "FeedbackNotSet" };
        notSet.Visibility = FeedbackSender.IsSetUp() ? Visibility.Collapsed : Visibility.Visible;
        var failure = Sk.StatusText(StatusLevel.Caution, "", 420);
        failure.Visibility = Visibility.Collapsed;
        var mailInstead = Sk.Btn("Send by email instead", () => { }, "FeedbackMailInstead");
        mailInstead.Visibility = Visibility.Collapsed;
        var privacy = Sk.Caption("No notifications, mail or chats are included.");

        void Update()
        {
            report.Kind = kind.SelectedIndex == 1 ? FeedbackKind.Idea : FeedbackKind.Bug;
            report.Message = message.Text;
            report.ReplyTo = email.Text.Trim();
            report.IncludeDetails = include.IsChecked == true;
            messageLabel.Text = report.Kind == FeedbackKind.Bug ? "What happened?" : "What would make it better?";
            message.PlaceholderText = report.Kind == FeedbackKind.Bug
                ? "e.g. The logo didn't update after I renamed a client."
                : "e.g. Let me snooze a whole account for an hour.";
            emailProblem.Visibility = report.ReplyIsValid ? Visibility.Collapsed : Visibility.Visible;
            detailsText.Visibility = report.IncludeDetails ? Visibility.Visible : Visibility.Collapsed;
            dialog.IsPrimaryButtonEnabled = sent || (report.IsReady && report.ReplyIsValid && !sending && FeedbackSender.IsSetUp());
        }

        kind.SelectionChanged += (_, _) => Update();
        message.TextChanged += (_, _) => Update();
        email.TextChanged += (_, _) => Update();
        include.Checked += (_, _) => Update();
        include.Unchecked += (_, _) => Update();

        var form = new StackPanel { Spacing = 14, Width = 440 };
        form.Children.Add(Sk.Caption("Goes straight to the Desktop Buddy team."));
        form.Children.Add(notSet);
        form.Children.Add(kind);
        var messageBlock = new StackPanel { Spacing = 6 };
        messageBlock.Children.Add(messageLabel);
        messageBlock.Children.Add(message);
        form.Children.Add(messageBlock);
        var emailBlock = new StackPanel { Spacing = 6 };
        emailBlock.Children.Add(Sk.Text("Your email, so we can reply (optional)", "DbRowTitleStyle").With(t => t.TextWrapping = TextWrapping.Wrap));
        emailBlock.Children.Add(email);
        emailBlock.Children.Add(emailProblem);
        form.Children.Add(emailBlock);
        var includeBlock = new StackPanel { Spacing = 0 };
        includeBlock.Children.Add(include);
        detailsText.Margin = new Thickness(28, -4, 0, 0);
        includeBlock.Children.Add(detailsText);
        form.Children.Add(includeBlock);
        form.Children.Add(failure);
        form.Children.Add(mailInstead);
        form.Children.Add(privacy);

        var thanks = new StackPanel { Spacing = 10, Width = 440, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 12), Visibility = Visibility.Collapsed };
        thanks.Children.Add(Sk.Icon(Glyphs.Check, 40, "SkIconPositive"));
        thanks.Children.Add(Sk.Text("Report sent", "DbPanelTitleStyle"));
        thanks.Children.Add(Sk.Caption(""));

        var root = new Grid();
        root.Children.Add(form);
        root.Children.Add(thanks);
        dialog.Content = root;

        mailInstead.Click += (_, _) =>
        {
            if (FeedbackSender.MailUrl(report, details) is { } url) SettingsContext.Open(url);
            dialog.Hide();
        };

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            if (sent) return;                 // "Done" closes the dialog
            args.Cancel = true;
            if (sending) return;
            sending = true;
            dialog.PrimaryButtonText = "Sending…";
            failure.Visibility = Visibility.Collapsed;
            mailInstead.Visibility = Visibility.Collapsed;
            Update();
            try
            {
                if (FeedbackSender.Key() is null && FeedbackSender.MailUrl(report, details) is { } mail)
                {
                    // No form service set up: hand over to the mail app with everything filled in.
                    SettingsContext.Open(mail);
                    dialog.Hide();
                    return;
                }
                await FeedbackSender.SendAsync(report, details);
                sent = true;
                form.Visibility = Visibility.Collapsed;
                thanks.Visibility = Visibility.Visible;
                ((TextBlock)thanks.Children[2]).Text = report.ReplyTo.Length == 0
                    ? "Thanks for helping make Desktop Buddy better."
                    : $"Thanks! We'll reply to {report.ReplyTo} if we need more details.";
                dialog.PrimaryButtonText = "Done";
                dialog.CloseButtonText = "";
                dialog.Title = "";
            }
            catch (FeedbackException e)
            {
                ((TextBlock)failure.Children[1]).Text = e.Message;
                failure.Visibility = Visibility.Visible;
                mailInstead.Visibility = FeedbackSender.MailUrl(report, details) is null ? Visibility.Collapsed : Visibility.Visible;
                dialog.PrimaryButtonText = "Send report";
            }
            finally
            {
                sending = false;
                if (!sent) Update();
                else dialog.IsPrimaryButtonEnabled = true;
            }
        };

        Update();
        await ctx.ShowAsync(dialog);
    }

    /// The form as one element (for snapshots).
    public static FrameworkElement Preview()
    {
        var panel = new StackPanel { Spacing = 14, Width = 440, Margin = new Thickness(32) };
        panel.Children.Add(Sk.Text("Report a bug or idea", "DbPanelTitleStyle"));
        panel.Children.Add(Sk.Caption("Goes straight to the Desktop Buddy team."));
        var kind = new RadioButtons { MaxColumns = 2 };
        foreach (var k in FeedbackKindInfo.All) kind.Items.Add(k.Title());
        kind.SelectedIndex = 0;
        panel.Children.Add(kind);
        panel.Children.Add(Sk.Text("What happened?", "DbRowTitleStyle"));
        panel.Children.Add(new TextBox { AcceptsReturn = true, Height = 140, PlaceholderText = "e.g. The logo didn't update after I renamed a client." });
        panel.Children.Add(Sk.Text("Your email, so we can reply (optional)", "DbRowTitleStyle"));
        panel.Children.Add(new TextBox { PlaceholderText = "name@company.com" });
        panel.Children.Add(new CheckBox { IsChecked = true, Content = "Include app and Windows version" });
        panel.Children.Add(Sk.Caption("No notifications, mail or chats are included."));
        return panel;
    }
}
