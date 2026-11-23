using DesktopBuddy.Core;

namespace DesktopBuddy.Tests;

/// The Windows redesign settings: optional in config.json, with defaults, so a config written by
/// the Mac app or an older Windows build still loads. The Mac app ignores the new keys.
public class ConfigFieldsTests
{
    private static readonly string[] NewKeys =
    [
        "boardShortcutChoice", "peekChoice", "peekSecondsChoice",
        "railCompactChoice", "respectWindowsFocusChoice", "windowsNotificationsChoice",
    ];

    [Fact]
    public void OldConfigWithoutTheNewKeysGetsDefaults()
    {
        const string old = """{ "accounts": [], "pollSeconds": 60, "paused": false, "playSound": true }""";
        var cfg = JsonFile.Decode<AppConfig>(old)!;
        Assert.Equal("Ctrl+Alt+N", cfg.BoardShortcut);
        Assert.True(cfg.PeekEnabled);
        Assert.Equal(10, cfg.PeekSeconds);
        Assert.True(cfg.RailCompact);
        Assert.True(cfg.RespectWindowsFocus);
        Assert.False(cfg.WindowsNotifications);
    }

    [Fact]
    public void NewInstanceHasTheSameDefaults()
    {
        var cfg = new AppConfig();
        Assert.Equal(AppConfig.DefaultBoardShortcut, cfg.BoardShortcut);
        Assert.True(cfg.PeekEnabled && cfg.RailCompact && cfg.RespectWindowsFocus);
        Assert.False(cfg.WindowsNotifications);
        Assert.Equal(AppConfig.DefaultPeekSeconds, cfg.PeekSeconds);
    }

    [Fact]
    public void KeysAreLeftOutOfTheFileWhileTheyAreNull()
    {
        var json = JsonFile.Encode(new AppConfig());
        foreach (var key in NewKeys) Assert.DoesNotContain($"\"{key}\"", json);
    }

    [Fact]
    public void ChangedValuesRoundTrip()
    {
        var cfg = new AppConfig
        {
            BoardShortcut = "Ctrl+Shift+F9",
            PeekEnabled = false,
            PeekSeconds = 20,
            RailCompact = false,
            RespectWindowsFocus = false,
            WindowsNotifications = true,
        };
        var json = JsonFile.Encode(cfg);
        // Read the JSON rather than matching text: the encoder escapes the plus sign, which is still valid JSON.
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("Ctrl+Shift+F9", root.GetProperty("boardShortcutChoice").GetString());
        Assert.False(root.GetProperty("peekChoice").GetBoolean());
        Assert.Equal(20, root.GetProperty("peekSecondsChoice").GetInt32());
        Assert.False(root.GetProperty("railCompactChoice").GetBoolean());
        Assert.False(root.GetProperty("respectWindowsFocusChoice").GetBoolean());
        Assert.True(root.GetProperty("windowsNotificationsChoice").GetBoolean());

        var back = JsonFile.Decode<AppConfig>(json)!;
        Assert.Equal("Ctrl+Shift+F9", back.BoardShortcut);
        Assert.False(back.PeekEnabled);
        Assert.Equal(20, back.PeekSeconds);
        Assert.False(back.RailCompact);
        Assert.False(back.RespectWindowsFocus);
        Assert.True(back.WindowsNotifications);
    }

    [Fact]
    public void PeekSecondsStaysBetween4And30()
    {
        var cfg = new AppConfig { PeekSeconds = 1 };
        Assert.Equal(4, cfg.PeekSeconds);
        cfg.PeekSeconds = 99;
        Assert.Equal(30, cfg.PeekSeconds);
        cfg.PeekSeconds = 4;
        Assert.Equal(4, cfg.PeekSeconds);
        cfg.PeekSeconds = 30;
        Assert.Equal(30, cfg.PeekSeconds);

        // A hand-edited file can hold anything; reading clamps it too.
        Assert.Equal(30, JsonFile.Decode<AppConfig>("""{ "peekSecondsChoice": 500 }""")!.PeekSeconds);
        Assert.Equal(4, JsonFile.Decode<AppConfig>("""{ "peekSecondsChoice": -3 }""")!.PeekSeconds);
        Assert.Equal(4, JsonFile.Decode<AppConfig>("""{ "peekSecondsChoice": 0 }""")!.PeekSeconds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankShortcutFallsBackToTheDefault(string blank)
    {
        var cfg = new AppConfig { BoardShortcutChoice = blank };
        Assert.Equal("Ctrl+Alt+N", cfg.BoardShortcut);
    }

    [Fact]
    public void TimedPauseLoadsRoundTripsAndExpires()
    {
        var now = new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);

        var old = JsonFile.Decode<AppConfig>("""{ "accounts": [] }""")!;
        Assert.Null(old.PausedUntilChoice);                                  // missing key loads
        Assert.False(old.IsPausedAt(now));
        Assert.DoesNotContain("pausedUntilChoice", JsonFile.Encode(old));    // left out while null

        var cfg = new AppConfig { PausedUntilChoice = now.AddHours(1) };
        Assert.True(cfg.IsPausedAt(now));
        Assert.True(cfg.IsPausedAt(now.AddMinutes(59)));
        Assert.False(cfg.IsPausedAt(now.AddHours(1)));                       // expired exactly at the time
        Assert.False(cfg.IsPausedAt(now.AddHours(2)));

        var back = JsonFile.Decode<AppConfig>(JsonFile.Encode(cfg))!;
        Assert.Equal(now.AddHours(1), back.PausedUntilChoice);
        Assert.True(back.IsPausedAt(now));
        Assert.False(back.IsPausedAt(now.AddDays(1)));

        var paused = new AppConfig { Paused = true, PausedUntilChoice = now.AddHours(-1) };
        Assert.True(paused.IsPausedAt(now));                                 // Paused wins over an expired time
    }

    [Fact]
    public void UnknownKeysFromANewerFileAreIgnored()
    {
        var cfg = JsonFile.Decode<AppConfig>("""{ "peekChoice": false, "somethingNew": 1 }""")!;
        Assert.False(cfg.PeekEnabled);
    }
}
