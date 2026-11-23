using System.Runtime.InteropServices;
using System.Text;
using DesktopBuddy.Core;

namespace DesktopBuddy.App.Platform;

/// Refresh tokens and client secrets in Windows Credential Manager (the Mac uses the Keychain).
/// One generic credential per key, named "DesktopBuddy/<key>". Credential Manager caps a value
/// at 2,560 bytes and Microsoft refresh tokens can be longer, so long values are split across
/// "DesktopBuddy/<key>", "…#1", "…#2"; the first part's comment holds the number of parts.
/// Values are cached in memory after the first read.
internal sealed class CredentialStore : ISecretStore
{
    private const string Prefix = "DesktopBuddy/";
    private readonly Dictionary<string, string?> _cache = [];
    private readonly Lock _lock = new();

    public string? Get(string key)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var cached)) return cached;
            var value = Read(key);
            _cache[key] = value;
            return value;
        }
    }

    public void Set(string key, string? value)
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(value)) value = null;
            if (_cache.TryGetValue(key, out var cached) && cached == value) return;
            var oldParts = PartCount(key);
            if (value is null)
            {
                for (var i = 0; i < Math.Max(1, oldParts); i++) Native.CredDelete(Target(key, i), Native.CRED_TYPE_GENERIC, 0);
            }
            else
            {
                var bytes = Encoding.UTF8.GetBytes(value);
                var size = Native.CRED_MAX_CREDENTIAL_BLOB_SIZE;
                var parts = Math.Max(1, (bytes.Length + size - 1) / size);
                for (var i = 0; i < parts; i++)
                {
                    var chunk = bytes.AsSpan(i * size, Math.Min(size, bytes.Length - i * size)).ToArray();
                    Write(Target(key, i), chunk, i == 0 ? parts.ToString() : null);
                }
                for (var i = parts; i < oldParts; i++) Native.CredDelete(Target(key, i), Native.CRED_TYPE_GENERIC, 0);
            }
            _cache[key] = value;
        }
    }

    private static string Target(string key, int part) => part == 0 ? Prefix + key : $"{Prefix}{key}#{part}";

    private static int PartCount(string key)
    {
        if (!TryRead(Target(key, 0), out _, out var comment)) return 0;
        return int.TryParse(comment, out var n) && n > 0 ? n : 1;
    }

    private static string? Read(string key)
    {
        if (!TryRead(Target(key, 0), out var first, out var comment)) return null;
        var parts = int.TryParse(comment, out var n) && n > 0 ? n : 1;
        var all = new List<byte>(first);
        for (var i = 1; i < parts; i++)
        {
            if (!TryRead(Target(key, i), out var more, out _)) return null;   // incomplete: treat as missing
            all.AddRange(more);
        }
        return Encoding.UTF8.GetString(all.ToArray());
    }

    private static bool TryRead(string target, out byte[] blob, out string? comment)
    {
        blob = [];
        comment = null;
        if (!Native.CredRead(target, Native.CRED_TYPE_GENERIC, 0, out var ptr)) return false;
        try
        {
            var cred = Marshal.PtrToStructure<Native.CREDENTIAL>(ptr);
            blob = new byte[cred.CredentialBlobSize];
            if (cred.CredentialBlobSize > 0) Marshal.Copy(cred.CredentialBlob, blob, 0, blob.Length);
            comment = cred.Comment;
            return true;
        }
        finally
        {
            Native.CredFree(ptr);
        }
    }

    private static void Write(string target, byte[] blob, string? comment)
    {
        var handle = GCHandle.Alloc(blob, GCHandleType.Pinned);
        try
        {
            var cred = new Native.CREDENTIAL
            {
                Type = Native.CRED_TYPE_GENERIC,
                TargetName = target,
                Comment = comment,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = handle.AddrOfPinnedObject(),
                Persist = Native.CRED_PERSIST_LOCAL_MACHINE,
                UserName = "Desktop Buddy",
            };
            if (!Native.CredWrite(ref cred, 0))
                throw new InvalidOperationException($"Couldn't save to Credential Manager (error {Marshal.GetLastWin32Error()}).");
        }
        finally
        {
            handle.Free();
        }
    }
}
