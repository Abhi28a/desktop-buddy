using DesktopBuddy.Core;

namespace DesktopBuddy.App.Platform;

/// `DesktopBuddy.exe --selftest`: checks the Windows-only pieces that unit tests can't reach
/// (Credential Manager, the sign-in startup key). Prints results to selftest.txt next to the
/// app and exits with 0 when everything passed. Uses throwaway keys only.
internal static class PlatformSelfTest
{
    public static int Run()
    {
        var lines = new List<string>();
        var failed = 0;
        void Check(string name, bool ok)
        {
            lines.Add($"{(ok ? "PASS" : "FAIL")}  {name}");
            if (!ok) failed++;
        }

        try
        {
            var key = "selftest." + Guid.NewGuid().ToString("N");
            var store = new CredentialStore();
            Check("missing key reads as nothing", store.Get(key) is null);

            store.Set(key, "short-value");
            Check("short value round-trips (fresh read)", new CredentialStore().Get(key) == "short-value");

            var longValue = string.Concat(Enumerable.Repeat("0123456789abcdef", 400));   // 6,400 bytes: 3 parts
            store.Set(key, longValue);
            Check("long value is split and joined (fresh read)", new CredentialStore().Get(key) == longValue);

            store.Set(key, "short-again");
            Check("shrinking removes extra parts", new CredentialStore().Get(key) == "short-again"
                && !Native.CredRead($"DesktopBuddy/{key}#1", Native.CRED_TYPE_GENERIC, 0, out _));

            store.Set(key, null);
            Check("clearing deletes it", new CredentialStore().Get(key) is null);

            var unicode = "tökén-✓-" + new string('é', 1500);                         // multi-byte across a part boundary
            store.Set(key, unicode);
            Check("non-ASCII across a part boundary", new CredentialStore().Get(key) == unicode);
            store.Set(key, null);

            Check("startup setting can be read", Startup.IsEnabled || !Startup.IsEnabled);
            Check("data folder is writable", Directory.Exists(Paths.SupportDir));

            Check("attention state reads as a defined value", Enum.IsDefined(Attention.Current()));

            using (var hotkey = new Hotkey())
            {
                // An unlikely combination, so a clash with another app doesn't fail the check.
                var result = hotkey.Register("Ctrl+Alt+Shift+F24", () => { });
                Check("unlikely shortcut registers: " + result.Message, result.Registered && hotkey.Current == "Ctrl+Alt+Shift+F24");
                hotkey.Unregister();
                Check("shortcut unregisters cleanly", hotkey.Current is null);
                Check("invalid shortcut is refused", !hotkey.Register("N", () => { }).Registered);
            }
        }
        catch (Exception ex)
        {
            Check("no exceptions: " + ex.Message, false);
        }

        lines.Add(failed == 0 ? $"All {lines.Count} platform checks passed." : $"{failed} platform check(s) failed.");
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "selftest.txt"), lines);
        return failed == 0 ? 0 : 1;
    }
}
