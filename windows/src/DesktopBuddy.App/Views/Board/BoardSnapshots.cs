using System.Globalization;
using DesktopBuddy.App.Ui;
using DesktopBuddy.Core;
using DesktopBuddy.Presentation.Board;
using Microsoft.UI.Xaml;

namespace DesktopBuddy.App.Views.Board;

/// Board screens for `--snapshot`: the content at 404 x 560 on a plain backdrop, light and dark.
/// Each screen works on its own copy of the sample data (a fresh AppState loaded from a copy of the
/// snapshot folder), so nothing here changes the state other screens use.
internal static class BoardSnapshots
{
    private const double Width = 404, Height = 560;

    public static void Add(List<SnapshotScreen> screens)
    {
        screens.Add(new("board", () => Show(Sample()), Width, Height));
        screens.Add(new("board-filtered", () =>
        {
            var s = Sample();
            return Show(s, owner: s.ActiveOwners.First(o => s.OwnerName(o) == "Gatehouse"));
        }, Width, Height));
        screens.Add(new("board-empty", () => Show(Fork((s, now) =>
        {
            s.ResetForTesting();
            s.SetStatus(s.Config.Accounts[0].Id, ItemSource.mail, new ServiceStatus { LastChecked = now.AddMinutes(-2) });
        })), Width, Height));
        screens.Add(new("board-paused", () => Show(Sample(s =>
        {
            s.SetStatus(IeHub(s).Id, ItemSource.chat, new ServiceStatus { LastChecked = s.Now().AddMinutes(-2) });   // all healthy
            s.PauseFor(TimeSpan.FromHours(1));
        })), Width, Height));
        screens.Add(new("board-problems", () => Show(Sample(s => s.SetExpired(Acme(s).Id, true))), Width, Height));
        screens.Add(new("board-undo", () => Show(Sample(s => s.Done("s2"))), Width, Height));
        screens.Add(new("board-long", () => Show(Long()), Width, Height));
    }

    private static FrameworkElement Show(AppState state, string? owner = null)
    {
        var vm = new BoardViewModel(state, owner: owner) { Culture = new CultureInfo("en-US") };
        return new BoardView(vm);
    }

    // MARK: - Data

    private static AccountConfig Account(AppState s, string label) => s.Config.Accounts.First(a => a.Label == label);
    private static AccountConfig IeHub(AppState s) => Account(s, "IE Hub");
    private static AccountConfig Acme(AppState s) => Account(s, "Acme Ltd");

    /// The sample data with one problem (IE Hub's chat) and a meeting five minutes away, as in the mock-ups.
    private static AppState Sample(Action<AppState>? change = null) => Fork((s, now) =>
    {
        s.SetStatus(IeHub(s).Id, ItemSource.chat, new ServiceStatus { LastChecked = now.AddMinutes(-2), Error = "Google Chat API is not enabled." });
        // The sample meeting started a minute ago; move it five minutes ahead. Everything is put back in one
        // go, so the owners come back in their usual order (clients first), as in the mock-ups.
        var all = s.Items.ToList();
        foreach (var item in all) { s.Dismiss(item.Id); s.Forget(item.Id); }
        s.Offer(all.Select(i => i.Id == "s5" ? i with { Date = now.AddMinutes(5), Detail = "Starts in 5 min" } : i).ToList());
        change?.Invoke(s);
    });

    /// A fresh AppState loaded from a copy of the current (sample) data folder. Paths is put back afterwards.
    private static AppState Fork(Action<AppState, DateTimeOffset> prepare)
    {
        var now = AppState.Shared.Now();
        var original = Paths.SupportDir;
        var copy = Path.Combine(Path.GetTempPath(), "DesktopBuddyBoard-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(copy);
        foreach (var file in Directory.GetFiles(original)) File.Copy(file, Path.Combine(copy, Path.GetFileName(file)), overwrite: true);
        Paths.UseFolder(copy);
        try
        {
            var state = new AppState { Now = () => now, Delay = _ => new TaskCompletionSource().Task };   // Undo stays up
            prepare(state, now);
            return state;
        }
        finally { Paths.UseFolder(original); }
    }

    private static AppState Fork(Action<AppState> prepare) => Fork((s, _) => prepare(s));

    /// Worst case: very long names, emoji, non-Latin text, 40 awkward items and a 4-digit count.
    private static AppState Long() => Fork((s, now) =>
    {
        s.ResetForTesting();

        // A long account name, and a name that has no logo and is not Latin.
        var dyna = s.Account(Account(s, "Dynamatix").Id)!;
        dyna.Label = "Northwind Traders International Holdings (EMEA) Limited";
        s.Update(dyna);
        var acme = s.Account(Acme(s).Id)!;
        acme.Label = "株式会社サンプル・ホールディングス";
        s.Update(acme);

        string Id(ItemSource kind, int n, string account) => $"{kind}:{account}:long{n}";
        var longSender = "Alexandrina-Wilhelmina Featherstonehaugh-Cholmondeley-Marjoribanks";
        var longSubject = "Re: Re: Fwd: Urgent — please review the revised quarterly forecast, the supplier amendments and the attached schedule before the board meeting";
        var samples = new (string? From, string Title, string? Detail)[]
        {
            (longSender, longSubject, "Please find everything attached."),
            ("山田 太郎", "プロジェクトの進捗報告について、至急ご確認をお願いいたします。", null),
            ("محمد بن عبد الله", "مراجعة العقد النهائي قبل نهاية الأسبوع", null),
            ("Zoë Müller 🎉", "Ünïcödé subject with emoji 🚀🔥 and a very long tail that goes on and on and on", null),
            (null, "🔔 " + new string('W', 120), "Detail text that is also quite long and should be trimmed with an ellipsis"),
            ("A", "B", null),
            ("ครอบครัวสุขสันต์", "สวัสดีครับ ขอส่งรายงานประจำเดือนให้พิจารณา", null),
            ("Всеволод Игоревич Достоевский-Петров", "Согласование договора поставки на следующий квартал", null),
        };

        var accounts = new[] { s.Account(dyna.Id)!, s.Account(IeHub(s).Id)!, s.Account(acme.Id)! };
        var items = new List<BuddyItem>();
        for (var n = 0; n < 40; n++)
        {
            var sample = samples[n % samples.Length];
            var account = accounts[n % accounts.Length];
            var source = n % 7 == 3 ? ItemSource.chat : n % 7 == 5 ? ItemSource.task : ItemSource.mail;
            items.Add(new BuddyItem
            {
                Id = Id(source, n, account.Id), Source = source, AccountID = account.Id, AccountLabel = account.Label,
                From = sample.From, Title = sample.Title + (n >= samples.Length ? " #" + n : ""), Detail = sample.Detail,
                Date = now.AddMinutes(-n * 37),
            });
        }
        items.Add(new BuddyItem
        {
            Id = "meet:long", Source = ItemSource.meeting, AccountID = accounts[0].Id, From = longSender,
            Title = "Design review — " + longSubject, Detail = "Starts in 1 min", Date = now.AddSeconds(50),
        });
        // Past the first 40: a thousand more for one account, so the count has four digits.
        var bulk = Enumerable.Range(0, 1200).Select(i => new BuddyItem
        {
            Id = $"mail:{accounts[2].Id}:bulk{i}", Source = ItemSource.mail, AccountID = accounts[2].Id, AccountLabel = accounts[2].Label,
            From = "Bulk sender", Title = "Newsletter " + i, Date = now.AddDays(-5).AddSeconds(-i),
        });
        s.Offer(bulk);
        s.Offer(items);
        s.SetStatus(accounts[0].Id, ItemSource.mail, new ServiceStatus { LastChecked = now.AddMinutes(-1) });
    });

}
