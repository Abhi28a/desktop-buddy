using System.Globalization;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace DesktopBuddy.Core;

// Port of the checking half of UpdateChecker in Sources/DesktopBuddy/Updates.swift.
// Asks GitHub Releases whether a newer version exists: one anonymous GET, nothing about the
// person or their accounts is sent. Then downloads the release zip and checks it against its
// .sha256 file. NOT ported: the macOS DMG mounting, code-signature check and swap-and-relaunch;
// the app layer installs the verified file later.

/// A newer release. Download and Checksum are null when the release doesn't carry the Windows files.
public sealed record ReleaseInfo(string Version, string Page, string? Download, string? Checksum)
{
    /// Whether this release can be installed from inside the app (older releases have no checksum).
    public bool CanInstallInPlace => Download is not null && Checksum is not null;
}

/// The result of a check. Exactly one of Available (a newer release), Error (couldn't check) or
/// neither (up to date) is set.
public sealed record UpdateCheckResult(ReleaseInfo? Available, string? Error, DateTimeOffset CheckedAt)
{
    public bool IsUpdateAvailable => Available is not null;
}

public sealed class UpdateException(string message) : Exception(message);

public static class UpdateCheck
{
    public const string Repo = "Abhi28a/desktop-buddy";
    public static readonly Uri LatestApi = new($"https://api.github.com/repos/{Repo}/releases/latest");
    public const string ReleasesPage = $"https://github.com/{Repo}/releases/latest";

    /// The Windows release files.
    public const string AssetName = "Desktop-Buddy-Windows-x64.zip";
    public const string ChecksumAssetName = "Desktop-Buddy-Windows-x64.zip.sha256";

    public const string CheckFailedMessage = "Couldn't check for updates. Check your internet connection and try again.";
    public const string MismatchMessage = "The download didn't match. Try again later.";
    public const string DownloadFailedMessage = "Couldn't download the update. Check your internet connection and try again.";

    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(20);

    // MARK: - Versions

    /// The running app's version (the assembly version from Directory.Build.props, for example "2.4.0").
    public static string CurrentVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? typeof(UpdateCheck).Assembly;
        var v = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "0";
        var plus = v.IndexOf('+');
        return plus >= 0 ? v[..plus] : v;
    }

    /// "v2.1" becomes "2.1"; a tag without the v is kept.
    public static string VersionFromTag(string tag)
    {
        var t = tag.Trim();
        return t.StartsWith('v') || t.StartsWith('V') ? t[1..] : t;
    }

    /// "2.10" is newer than "2.9"; missing parts count as 0, so "2.1" equals "2.1.0".
    public static bool IsNewer(string a, string b)
    {
        var pa = Parts(a);
        var pb = Parts(b);
        for (var i = 0; i < Math.Max(pa.Count, pb.Count); i++)
        {
            var x = i < pa.Count ? pa[i] : 0;
            var y = i < pb.Count ? pb[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }

    private static List<long> Parts(string s) =>
        s.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(p =>
            {
                var digits = new string(p.TakeWhile(char.IsAsciiDigit).ToArray());
                return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
            })
            .ToList();

    // MARK: - Release JSON

    /// Reads GitHub's "latest release" JSON. Returns null for a draft or pre-release (never offered).
    /// Throws if the JSON isn't a release.
    public static ReleaseInfo? ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("tag_name", out var tagEl) || tagEl.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("Not a release.");
        }
        if (Flag(root, "draft") || Flag(root, "prerelease")) return null;

        var version = VersionFromTag(tagEl.GetString()!);
        var page = root.TryGetProperty("html_url", out var pageEl) && pageEl.ValueKind == JsonValueKind.String && IsHttps(pageEl.GetString())
            ? pageEl.GetString()!
            : ReleasesPage;

        string? Asset(string name)
        {
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
            foreach (var a in assets.EnumerateArray())
            {
                if (a.ValueKind != JsonValueKind.Object) continue;
                var n = a.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String ? nameEl.GetString() : null;
                if (!string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) continue;
                var url = a.TryGetProperty("browser_download_url", out var urlEl) && urlEl.ValueKind == JsonValueKind.String ? urlEl.GetString() : null;
                if (IsHttps(url)) return url;   // only ever download over https
            }
            return null;
        }

        return new ReleaseInfo(version, page, Asset(AssetName), Asset(ChecksumAssetName));
    }

    private static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.True;

    private static bool IsHttps(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps;

    // MARK: - Checking

    /// One anonymous GET to GitHub. Never throws for network or format problems: they come back as Error.
    public static async Task<UpdateCheckResult> CheckAsync(HttpClient http, string? currentVersion = null, Uri? feed = null,
                                                           DateTimeOffset? now = null, CancellationToken ct = default)
    {
        var at = now ?? DateTimeOffset.Now;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(CheckTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, feed ?? LatestApi);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("DesktopBuddy", "1"));   // GitHub requires a User-Agent
            using var response = await http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode) return new UpdateCheckResult(null, CheckFailedMessage, at);
            var json = await response.Content.ReadAsStringAsync(timeout.Token);
            var release = ParseRelease(json);
            var available = release is not null && IsNewer(release.Version, currentVersion ?? CurrentVersion()) ? release : null;
            return new UpdateCheckResult(available, null, at);
        }
        catch (Exception e) when (!ct.IsCancellationRequested && e is HttpRequestException or JsonException or InvalidOperationException or OperationCanceledException)
        {
            return new UpdateCheckResult(null, CheckFailedMessage, at);
        }
    }

    // MARK: - Checksum

    /// "abc123...  Desktop-Buddy-Windows-x64.zip" (shasum format) becomes "abc123..." (lower case), or null if it isn't a SHA-256.
    public static string? ExpectedChecksum(string text)
    {
        var first = text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();
        if (first is null || first.Length != 64 || !first.All(char.IsAsciiHexDigit)) return null;
        return first;
    }

    /// SHA-256 of a file as lower-case hex.
    public static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// True when the file's SHA-256 equals the one in the checksum file's text.
    public static bool Verify(string filePath, string checksumText)
    {
        var expected = ExpectedChecksum(checksumText);
        return expected is not null && Sha256Of(filePath) == expected;
    }

    // MARK: - Downloading

    /// Downloads the release zip into workDir, checks it against the release's .sha256 file and returns
    /// the verified file's path. A download that doesn't match is deleted and refused (UpdateException).
    /// Installing it is the app layer's job.
    public static async Task<string> DownloadAndVerifyAsync(HttpClient http, ReleaseInfo release, string workDir,
                                                            IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (release.Download is null || release.Checksum is null)
        {
            throw new UpdateException("This update can't be installed from inside the app. Use Download Manually.");
        }
        Directory.CreateDirectory(workDir);
        var target = Path.Combine(workDir, AssetName);
        try
        {
            using (var response = await http.GetAsync(release.Download, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;
                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var file = File.Create(target);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (total is > 0) progress?.Report((double)done / total.Value);
                }
            }
            var sumText = await http.GetStringAsync(release.Checksum, ct);
            if (!Verify(target, sumText)) throw new UpdateException(MismatchMessage);
            return target;
        }
        catch (UpdateException)
        {
            TryDelete(target);
            throw;
        }
        catch (OperationCanceledException)
        {
            TryDelete(target);
            throw;
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            TryDelete(target);
            throw new UpdateException(DownloadFailedMessage);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { }
    }
}
