using DesktopBuddy.App.Ui;
using DesktopBuddy.App.Views.Board;

namespace DesktopBuddy.App;

/// The Board: the one triage panel that replaces the notification list and the tray flyout list.
/// The window and content live in Views\Board; this file only hooks them into the app.
public partial class App
{
    partial void ShowBoard(BoardAnchor anchor, string? owner) => BoardHost.Toggle(anchor, owner);

    partial void AddBoardSnapshots(List<SnapshotScreen> screens) => BoardSnapshots.Add(screens);
}
