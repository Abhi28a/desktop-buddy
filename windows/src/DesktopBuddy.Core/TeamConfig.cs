using System.Text.Json.Serialization;

namespace DesktopBuddy.Core;

/// Port of Sources/DesktopBuddy/TeamConfig.swift.
/// Shared OAuth clients for the whole team, so people only click "Sign in".
///
/// Looked up in order:
///   1. %LOCALAPPDATA%\DesktopBuddy\team.json   (this PC)
///   2. team.json next to DesktopBuddy.exe        (built into the app)
///
/// On Windows, Google needs a Desktop-type client (loopback redirect). Google documents its
/// secret as not confidential for installed apps; it still isn't committed to the repo.
public sealed record TeamConfig
{
    [JsonPropertyName("googleClientID")] public string? GoogleClientID { get; set; }
    [JsonPropertyName("googleClientSecret")] public string? GoogleClientSecret { get; set; }
    [JsonPropertyName("microsoftClientID")] public string? MicrosoftClientID { get; set; }
    [JsonPropertyName("microsoftTenant")] public string? MicrosoftTenant { get; set; }
    [JsonPropertyName("feedbackFormID")] public string? FeedbackFormID { get; set; }
    [JsonPropertyName("feedbackEmail")] public string? FeedbackEmail { get; set; }

    /// Folder of the running app (where a built-in team.json sits). Tests can override it.
    public static string BundledDir { get; set; } = AppContext.BaseDirectory;

    private static string BundledPath => Path.Combine(BundledDir, "team.json");

    public static TeamConfig Current =>
        JsonFile.Load<TeamConfig>(Paths.TeamJson)
        ?? JsonFile.Load<TeamConfig>(BundledPath)
        ?? new TeamConfig();

    public static string Source
    {
        get
        {
            if (File.Exists(Paths.TeamJson)) return "This PC (%LOCALAPPDATA%\\DesktopBuddy\\team.json)";
            if (File.Exists(BundledPath)) return "Built into the app";
            return "Not set up";
        }
    }

    /// Saves this PC's setup, which takes priority over a built-in one.
    public static void InstallLocal(TeamConfig cfg) => JsonFile.Save(cfg, Paths.TeamJson);

    public bool HasClient(Provider provider) => provider switch
    {
        Provider.google => !string.IsNullOrEmpty(GoogleClientID),
        _ => !string.IsNullOrEmpty(MicrosoftClientID),
    };
}
