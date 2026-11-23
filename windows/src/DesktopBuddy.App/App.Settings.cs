using DesktopBuddy.App.Ui;
using DesktopBuddy.App.Views.Settings;

namespace DesktopBuddy.App;

/// The Settings window: accounts, sign-in, clients, general, appearance, reminders and team setup.
/// The pages live in Views\Settings.
public partial class App
{
    partial void OpenSettings(string? page) => SettingsWindow.Show(page);

    partial void AddSettingsSnapshots(List<SnapshotScreen> screens) => SettingsSnapshots.Add(screens);
}
