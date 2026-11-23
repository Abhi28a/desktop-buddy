namespace DesktopBuddy.Presentation.Board;

public enum BoardProblemKind { SignIn, Service, Several, Paused }

/// How serious the line looks. The view picks the glyph and colour; words always say it too.
public enum BoardSeverity { Neutral, Caution }

/// The one line at the top of the Board when something is wrong (or paused).
/// `SettingsPage` is where "Fix" goes ("account:<id>", or "home" for several problems);
/// null for Paused, whose button is Resume.
public sealed record BoardProblem(BoardProblemKind Kind, BoardSeverity Severity, string Title, string ActionText, string? SettingsPage, int Count = 1);
