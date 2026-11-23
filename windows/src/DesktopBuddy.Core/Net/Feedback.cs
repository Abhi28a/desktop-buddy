using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DesktopBuddy.Core;

// Port of the network part of Feedback.swift (no window: the UI is the app's).
// Reports go to the team's inbox through a form service (Formspree), set up in team.json:
//   "feedbackFormID": "<Formspree form ID>"   the service emails reports to the form's owner
//   "feedbackEmail": "you@example.com"        used if the service can't be reached: the report opens in Mail
// Nothing is sent except what the person typed and, if they leave it on, the app and Windows version.

public enum FeedbackKind { Bug, Idea }

public static class FeedbackKindInfo
{
    public static readonly FeedbackKind[] All = [FeedbackKind.Bug, FeedbackKind.Idea];
    public static string Title(this FeedbackKind k) => k == FeedbackKind.Bug ? "Something's wrong" : "Idea or fix";
    public static string SubjectWord(this FeedbackKind k) => k == FeedbackKind.Bug ? "Bug" : "Idea";
}

public sealed class FeedbackReport
{
    public FeedbackKind Kind { get; set; } = FeedbackKind.Bug;
    public string Message { get; set; } = "";
    public string ReplyTo { get; set; } = "";
    public bool IncludeDetails { get; set; } = true;

    public bool IsReady => new StringInfo(Message.Trim()).LengthInTextElements >= 5;
    public bool ReplyIsValid => ReplyTo.Length == 0 || (ReplyTo.Contains('@') && ReplyTo.Contains('.') && !ReplyTo.Contains(' '));
}

public enum FeedbackFailureKind { NotSetUp, Rejected, Offline }

/// Why a report wasn't delivered. Message is the text to show.
public sealed class FeedbackException(FeedbackFailureKind kind, string message) : Exception(message)
{
    public FeedbackFailureKind Kind { get; } = kind;

    public static FeedbackException NotSetUp() => new(FeedbackFailureKind.NotSetUp, "Reporting isn't set up in this copy of Desktop Buddy.");
    public static FeedbackException Rejected(string m) => new(FeedbackFailureKind.Rejected, $"The report wasn't accepted: {m}");
    public static FeedbackException Offline() => new(FeedbackFailureKind.Offline, "Couldn't reach the internet. Check your connection and try again.");
}

public static class FeedbackSender
{
    // Swift's isNewline: LF, CR, VT, FF, NEL, line separator, paragraph separator.
    private static readonly char[] Newlines = [(char)0x0A, (char)0x0D, (char)0x0B, (char)0x0C, (char)0x85, (char)0x2028, (char)0x2029];

    public static string? Endpoint(string formID)
    {
        var url = "https://formspree.io/f/" + Uri.EscapeDataString(formID);
        return Uri.TryCreate(url, UriKind.Absolute, out _) ? url : null;
    }

    private static string? NonEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    public static string? Key(TeamConfig? team = null) => NonEmpty((team ?? TeamConfig.Current).FeedbackFormID);
    public static string? Email(TeamConfig? team = null) => NonEmpty((team ?? TeamConfig.Current).FeedbackEmail);

    /// Shown in menus only when reports have somewhere to go.
    public static bool IsSetUp(TeamConfig? team = null) => Key(team) is not null || Email(team) is not null;

    public static string Subject(FeedbackReport r)
    {
        var first = r.Message.Split(Newlines, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var info = new StringInfo(first);
        var shortText = info.LengthInTextElements > 60 ? info.SubstringByTextElements(0, 57) + "…" : first;
        return $"Desktop Buddy {r.Kind.SubjectWord()}: {shortText}";
    }

    /// "Desktop Buddy 2.4.0 · Windows 10.0.26200 · 1 Google, 0 Microsoft accounts". The account counts are
    /// left out when not given.
    public static string Details(int? googleAccounts = null, int? microsoftAccounts = null)
    {
        var v = typeof(FeedbackSender).Assembly.GetName().Version;
        var app = v is null ? "?" : $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";
        var os = Environment.OSVersion.Version;
        var s = $"Desktop Buddy {app} · Windows {os.Major}.{os.Minor}.{os.Build}";
        if (googleAccounts is { } g && microsoftAccounts is { } m)
            s += $" · {g} Google, {m} Microsoft account{(g + m == 1 ? "" : "s")}";
        return s;
    }

    /// `details` is the Details(...) text to append when the report allows it.
    public static string Body(FeedbackReport r, string? details = null)
    {
        var s = r.Message.Trim();
        if (r.ReplyTo.Length > 0) s += $"\n\nReply to: {r.ReplyTo}";
        if (r.IncludeDetails) s += $"\n\n— {details ?? Details()}";
        return s;
    }

    public static Dictionary<string, string> Payload(FeedbackReport r, string? details = null)
    {
        var p = new Dictionary<string, string>
        {
            ["_subject"] = Subject(r),
            ["type"] = r.Kind.Title(),
            ["message"] = Body(r, details),
        };
        if (r.ReplyTo.Length > 0) p["email"] = r.ReplyTo;      // Formspree sets it as the reply-to address
        return p;
    }

    /// Sends the report. Throws FeedbackException if it wasn't delivered.
    public static async Task SendAsync(FeedbackReport r, string? details = null, TeamConfig? team = null,
                                       HttpClient? http = null, CancellationToken ct = default)
    {
        var key = Key(team);
        var url = key is null ? null : Endpoint(key);
        if (url is null) throw FeedbackException.NotSetUp();

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(Payload(r, details)), Encoding.UTF8),
        };
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        int status;
        JsonObject json;
        try
        {
            (status, json) = await ApiClient.SendAsync(http ?? ApiClient.Http, req, TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or TimeoutException)
        {
            throw FeedbackException.Offline();
        }
        var ok = status is >= 200 and < 300;
        var accepted = json.Bool("ok") ?? true;
        if (!ok || !accepted)
            throw FeedbackException.Rejected(json.Str("error") ?? "unknown reply");
    }

    /// The same report as a ready-to-send email in the person's own mail app (a mailto: link).
    public static string? MailUrl(FeedbackReport r, string? details = null, TeamConfig? team = null)
    {
        var email = Email(team);
        if (email is null) return null;
        return "mailto:" + Uri.EscapeDataString(email).Replace("%40", "@")
            + "?subject=" + Uri.EscapeDataString(Subject(r)) + "&body=" + Uri.EscapeDataString(Body(r, details));
    }
}
