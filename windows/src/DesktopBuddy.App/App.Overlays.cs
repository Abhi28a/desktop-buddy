using DesktopBuddy.App.Platform;
using DesktopBuddy.App.Ui;
using DesktopBuddy.App.Views.Peek;
using DesktopBuddy.App.Views.Rail;
using DesktopBuddy.Core;
using DesktopBuddy.Presentation.Peek;
using DesktopBuddy.Presentation.Rail;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DesktopBuddy.App;

/// The always-there windows: the Rail (who's waiting) and the Peek (something just arrived), plus sounds.
/// The windows live in Views/Rail and Views/Peek; this file only hooks them into the app and draws their
/// `--snapshot` screens. The Board and the tray menu are elsewhere (App.Board.cs, App.xaml.cs).
public partial class App
{
    private RailController? _rail;
    private PeekController? _peek;
    private DateTimeOffset? _screenshotTimerFor;

    partial void StartOverlays()
    {
        var state = AppState.Shared;
        state.SoundRequested += Sounds.Play;
        state.Changed += EndScreenshotAllowance;
        _rail = new RailController(state);
        _peek = new PeekController(state);
    }

    /// "Allow screenshots for a minute" ends by itself: when the minute is up, switch protection back on
    /// (clearing it raises Changed, which the Rail and the Peek react to).
    private void EndScreenshotAllowance()
    {
        var state = AppState.Shared;
        var now = state.Now();
        if (state.ScreenshotsAllowedUntil is not { } until || until <= now || _screenshotTimerFor == until) return;
        _screenshotTimerFor = until;
        var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.IsRepeating = false;
        timer.Interval = until - now + TimeSpan.FromMilliseconds(150);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (state.ScreenshotsAllowedUntil == until) state.ScreenshotsAllowedUntil = null;
        };
        timer.Start();
    }

    // MARK: - Snapshots

    partial void AddOverlaySnapshots(List<SnapshotScreen> screens)
    {
        var state = AppState.Shared;

        // The Rail's tokens from the sample data. IE Hub has a meeting; its sample start time is "now", so
        // give it a believable ring (6 of its 10 minutes left) like the real thing a few minutes before it starts.
        List<RailTokenViewModel> Tokens()
        {
            using var vm = new RailViewModel(state);
            return vm.Tokens.Select(t => t.HasUrgency ? t with { Urgency = 0.6, MeetingMinutes = 6 } : t).ToList();
        }

        FrameworkElement BuildRail(bool vertical, bool compact, string? hot = null, bool tip = false, List<RailTokenViewModel>? tokens = null,
            double size = 40)
        {
            var content = new RailContent { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var list = tokens ?? Tokens();
            content.Apply(list, vertical, compact, size, animateNew: false);
            if (hot is not null) content.PreviewHot(hot);
            if (!tip) return content;

            // The name card to the left of the hovered token, placed like the real window does.
            var host = new Grid { HorizontalAlignment = HorizontalAlignment.Right };
            host.Children.Add(content);
            var card = new RailTip(list.First(t => t.OwnerId == hot).Tooltip)
            {
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            };
            host.Children.Add(card);
            content.Loaded += (_, _) =>
            {
                content.UpdateLayout();
                var box = content.TokenRects().First(r => r.Owner == hot).Bounds;
                card.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                card.Margin = new Thickness(-card.DesiredSize.Width - 8 + RailContent.Room + 4, box.Y + box.Height / 2 - card.DesiredSize.Height / 2, 0, 0);
            };
            return host;
        }

        string Owner(string name) => state.ActiveOwners.First(o => state.OwnerName(o) == name);

        screens.Add(new("rail-right", () => BuildRail(vertical: true, compact: true), 200, 340, Transparent: true));
        screens.Add(new("rail-bottom", () => BuildRail(vertical: false, compact: true), 380, 140, Transparent: true));
        screens.Add(new("rail-hover", () => BuildRail(vertical: true, compact: true, hot: Owner("RiskHawk"), tip: true), 340, 340, Transparent: true));
        screens.Add(new("rail-names", () => BuildRail(vertical: true, compact: false), 200, 440, Transparent: true));
        screens.Add(new("rail-names-bottom", () => BuildRail(vertical: false, compact: false), 470, 160, Transparent: true));
        screens.Add(new("rail-urgent", () =>
        {
            // Two meetings: one just appeared (a full ring) and one about to start (a sliver).
            var list = Tokens().Select(t => t.HasUrgency ? t with { Urgency = 0.15, MeetingMinutes = 1 } : t).ToList();
            var first = list[0];
            list[0] = first with { Urgency = 0.9, MeetingMinutes = 9 };
            return BuildRail(vertical: true, compact: true, tokens: list);
        }, 200, 340, Transparent: true));
        screens.Add(new("rail-large", () => BuildRail(vertical: true, compact: true, size: RailRules.TokenSize(1.5)), 200, 440, Transparent: true));

        FrameworkElement BuildPeek(PeekCard card)
        {
            var content = new PeekContent { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 24, 0, 0) };
            content.Apply(card);
            return content;
        }

        BuddyItem Sample(string id) => state.Items.First(i => i.Id == id) with { Url = "https://example.com/item" };

        screens.Add(new("peek", () => BuildPeek(PeekCard.From(state, Sample("s1"), more: 5, progress: 0.62)), 480, 250, Transparent: true));
        screens.Add(new("peek-hover", () => BuildPeek(PeekCard.From(state, Sample("s2"), more: 1, progress: 0.4, hovering: true)), 480, 250, Transparent: true));
        screens.Add(new("peek-reminder", () => BuildPeek(PeekCard.From(state, state.Items.First(i => i.Id == "s6"), more: 0, progress: 0.8)), 480, 250, Transparent: true));
        screens.Add(new("peek-long", () =>
        {
            // The worst case: a very long sender and subject, emoji and non-Latin text. A throwaway item; it
            // is not added to the state.
            var item = new BuddyItem
            {
                Id = "snapshot-long", Source = ItemSource.mail, AccountID = state.Config.Accounts[0].Id, AccountLabel = "Dynamatix",
                From = "Maximilian Alexander Featherstonehaugh-Cholmondeley-Wolstenholme the Third (Operations)",
                Title = "Re: Urgent 🎯 quarterly compliance and risk attestation for the Zürich, 東京 and São Paulo offices before Friday",
                Detail = "これは非常に長い詳細テキストの例です 🌸 Это пример очень длинного текста описания, который не должен ломать карточку",
                Date = state.Now().AddMinutes(-3), Url = "https://example.com/mail/1",
            };
            return BuildPeek(PeekCard.From(state, item, more: 128, progress: 0.9));
        }, 480, 250, Transparent: true));
    }
}
