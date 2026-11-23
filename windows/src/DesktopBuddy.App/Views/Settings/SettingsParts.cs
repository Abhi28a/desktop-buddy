using CommunityToolkit.WinUI.Controls;
using DesktopBuddy.Core;
using DesktopBuddy.Presentation.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DesktopBuddy.App.Views.Settings;

/// Pieces that more than one page shows: Pause and Quiet hours (Home and Notifications).
internal static class Parts
{
    /// "Pause notifications": the switch, and Pause for 1 hour / Pause until tomorrow / Resume inside it.
    public static UIElement Pause(AppState state, SettingsContext ctx, HomeSummary home)
    {
        var paused = home.IsPaused;
        var description = paused ? home.PausedText! : "Everything waits in the Board until you turn this off.";
        var sw = Sk.Switch(paused, on =>
        {
            if (on) { state.Config.Paused = true; state.SaveConfig(); }
            else state.Resume();
        }, "Pause notifications", "PauseSwitch");

        var buttons = Sk.Buttons(
            paused
                ? Sk.Btn("Resume", state.Resume, "PauseResume", primary: true)
                : null,
            Sk.Btn("Pause for 1 hour", () => state.PauseFor(TimeSpan.FromHours(1)), "PauseHour"),
            Sk.Btn("Pause until tomorrow", state.PauseUntilTomorrow, "PauseTomorrow"));
        var quick = Sk.Row("Pause for a while", "Starts again by itself. Tomorrow means 8:00 AM.", buttons, id: "PauseQuick");
        return Sk.Expander("Pause notifications", description, sw, Glyphs.Pause, "Pause", expanded: true, quick);
    }

    /// "Quiet hours": a switch and, while on, the From and Until times.
    public static UIElement QuietHours(AppState state, Action<Action<AppConfig>> set, string idPrefix = "")
    {
        var config = state.Config;
        var on = config.QuietStartHour is not null && config.QuietEndHour is not null;
        var description = HomeSummary.QuietWords(config);
        var sw = Sk.Switch(on, v => set(c =>
        {
            c.QuietStartHour = v ? 22 : null;
            c.QuietEndHour = v ? 8 : null;
        }), "Quiet hours", idPrefix + "QuietSwitch");
        if (!on || config.QuietStartHour is not { } s || config.QuietEndHour is not { } e)
        {
            return Sk.Row("Quiet hours", "Nothing pops up at night. It waits in the Board.", sw, Glyphs.Moon, idPrefix + "QuietHours");
        }

        var hours = Enumerable.Range(0, 24).Select(h => (Sk.HourText(h), h)).ToList();
        var from = Sk.Row("From", null, Sk.Combo(hours, s, v => set(c => c.QuietStartHour = v), "Quiet hours from", idPrefix + "QuietFrom", 140), id: idPrefix + "QuietFromRow");
        var until = Sk.Row("Until", s == e ? "Start and end are the same, so quiet hours are off. Pick different times." : null,
            Sk.Combo(hours, e, v => set(c => c.QuietEndHour = v), "Quiet hours until", idPrefix + "QuietUntil", 140), id: idPrefix + "QuietUntilRow");
        return Sk.Expander("Quiet hours", description, sw, Glyphs.Moon, idPrefix + "QuietHours", expanded: true, from, until);
    }
}
