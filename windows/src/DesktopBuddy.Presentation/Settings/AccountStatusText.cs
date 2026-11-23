using System.Text.RegularExpressions;
using DesktopBuddy.Core;

namespace DesktopBuddy.Presentation.Settings;

/// How serious a line of status is. The view gives each level its own icon, so it is never colour-only.
public enum StatusLevel { Neutral, Positive, Caution, Critical }

public enum AccountHealth { Off, Connected, ServiceProblems, SignInExpired, WrongAccount, NotSignedIn }

/// An account's state in words ("Connected", "Sign-in expired"), with a level for the icon.
public sealed record AccountStatusInfo(AccountHealth Health, StatusLevel Level, string Words, string? Detail);

/// One service on an account: its words, and its error with where to fix it.
public sealed record ServiceLine(string Status, StatusLevel Level, string? Error, string? FixUrl);

/// The wording for accounts and their services. Pure: it reads AppState and writes strings.
public static class AccountStatusText
{
    /// The name of a service for this account's provider ("Gmail", "Teams chats", "Meetings", "Microsoft To Do").
    public static string ServiceName(Provider provider, ItemSource source) => source switch
    {
        ItemSource.mail => provider.MailName(),
        ItemSource.chat => provider.ChatName(),
        ItemSource.meeting => "Meetings",
        ItemSource.task => provider.TasksName(),
        _ => "Reminders",
    };

    /// The services an account can watch, in the order the account page lists them.
    public static readonly ItemSource[] Services = [ItemSource.mail, ItemSource.chat, ItemSource.meeting, ItemSource.task];

    public static bool Watches(AccountConfig a, ItemSource source) => source switch
    {
        ItemSource.mail => a.WatchMail,
        ItemSource.chat => a.WatchChat,
        ItemSource.meeting => a.WatchMeetings,
        ItemSource.task => a.WatchTasks,
        _ => false,
    };

    /// The account's state in words. Sign-in problems come before service problems.
    public static AccountStatusInfo Describe(AppState state, AccountConfig a, DateTimeOffset? now = null)
    {
        now ??= state.Now();
        if (!a.Enabled) return new(AccountHealth.Off, StatusLevel.Neutral, "Off", "Not being checked.");
        if (state.WrongAccount.TryGetValue(a.Id, out var other))
        {
            return new(AccountHealth.WrongAccount, StatusLevel.Critical, "Signed in as a different account",
                $"You signed in as {other}, but this account is {a.Email}.");
        }
        if (state.Expired.Contains(a.Id)) return new(AccountHealth.SignInExpired, StatusLevel.Critical, "Sign-in expired", $"Sign in again to resume checking {a.Email}.");
        if (!state.SignedIn.Contains(a.Id)) return new(AccountHealth.NotSignedIn, StatusLevel.Critical, "Not signed in", $"Sign in to start checking {a.Email}.");

        var problems = Services.Count(s => Watches(a, s) && state.Status(a.Id, s).Error is not null);
        DateTimeOffset? last = Services.Select(s => state.Status(a.Id, s).LastChecked).Where(t => t is not null).Max();
        var checkedText = last is { } t ? $"Last checked {TimeText.Ago(t, now.Value)}." : "Waiting for the first check.";
        if (problems > 0)
        {
            var words = $"Connected, {TimeText.Plural(problems, "service needs", "services need")} attention";
            return new(AccountHealth.ServiceProblems, StatusLevel.Caution, words, checkedText);
        }
        return new(AccountHealth.Connected, StatusLevel.Positive, "Connected", checkedText);
    }

    /// One service row on the account page.
    public static ServiceLine Service(AppState state, AccountConfig a, ItemSource source, DateTimeOffset? now = null)
    {
        now ??= state.Now();
        if (!Watches(a, source) || !a.Enabled)
        {
            var users = state.ClientsUsing(a, source).Select(c => c.Name).ToList();
            if (users.Count == 0) return new("Off", StatusLevel.Neutral, null, null);
            var what = source == ItemSource.chat ? "chats" : "mail";
            return new($"Off. {TimeText.Join(users)} won't get {what} from {a.Label}.", StatusLevel.Caution, null, null);
        }
        if (state.NeedsSignIn(a.Id)) return new("Paused until you sign in", StatusLevel.Neutral, null, null);
        var s = state.Status(a.Id, source);
        if (s.Error is { } error) return new("Not working", StatusLevel.Caution, ShortError(error), s.FixUrl);
        if (s.LastChecked is { } t)
        {
            var count = s.Count == 0 ? "Nothing new" : $"{s.Count} waiting";
            return new($"{count}, checked {TimeText.Ago(t, now.Value)}", StatusLevel.Positive, null, null);
        }
        return new("Waiting for the first check", StatusLevel.Neutral, null, null);
    }

    /// Provider errors can be long and carry a link. The link goes to the Fix button; this keeps the sentence.
    public static string ShortError(string error)
    {
        var text = Regex.Replace(error, @"\s*https?://\S+", "");
        text = Regex.Replace(text, @"\s+", " ").Trim().TrimEnd(',', ':', ';');
        text = Regex.Replace(text, @"\s*(Enable it by visiting|Visit)\s*$", "", RegexOptions.IgnoreCase).Trim();
        if (text.Length > 150) text = text[..149].TrimEnd() + "…";
        if (text.Length > 0 && !".!?…".Contains(text[^1])) text += ".";
        return text;
    }
}
