using System.Net;
using System.Security.Cryptography;
using System.Text;
using DesktopBuddy.Core;

namespace DesktopBuddy.Tests;

// Port of "Update check" in SelfTest.swift, plus the GitHub release parsing and the download
// check that replace the Mac's DMG handling. No network: HttpClient talks to a fake handler.

public class UpdateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "desktopbuddy-update-" + Guid.NewGuid().ToString("N"));

    public UpdateTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) };

    private static string ReleaseJson(string tag, bool prerelease = false, bool draft = false, bool windowsAssets = true)
    {
        var assets = new List<string>
        {
            """{ "name": "Desktop-Buddy.dmg", "browser_download_url": "https://example.com/mac/Desktop-Buddy.dmg" }""",
            """{ "name": "Desktop-Buddy.dmg.sha256", "browser_download_url": "https://example.com/mac/Desktop-Buddy.dmg.sha256" }""",
        };
        if (windowsAssets)
        {
            assets.Add("""{ "name": "Desktop-Buddy-Windows-x64.zip", "browser_download_url": "https://example.com/win/Desktop-Buddy-Windows-x64.zip" }""");
            assets.Add("""{ "name": "Desktop-Buddy-Windows-x64.zip.sha256", "browser_download_url": "https://example.com/win/Desktop-Buddy-Windows-x64.zip.sha256" }""");
        }
        return "{ \"tag_name\": \"" + tag + "\", \"html_url\": \"https://github.com/Abhi28a/desktop-buddy/releases/tag/" + tag + "\","
            + " \"draft\": " + (draft ? "true" : "false") + ", \"prerelease\": " + (prerelease ? "true" : "false")
            + ", \"assets\": [" + string.Join(",", assets) + "] }";
    }

    // MARK: Versions

    [Fact]
    public void TagsLoseTheLeadingV()
    {
        Assert.Equal("2.1", UpdateCheck.VersionFromTag("v2.1"));                   // tag v2.1 gives 2.1
        Assert.Equal("2.1.3", UpdateCheck.VersionFromTag("2.1.3"));                // tag without v is kept
        Assert.Equal("2.5", UpdateCheck.VersionFromTag("  v2.5 "));
        Assert.Equal("2.5", UpdateCheck.VersionFromTag("V2.5"));
    }

    [Theory]
    [InlineData("2.1", "2.0", true)]            // 2.1 is newer than 2.0
    [InlineData("2.10", "2.9", true)]           // 2.10 is newer than 2.9
    [InlineData("3.0", "2.9.9", true)]          // 3.0 is newer than 2.9.9
    [InlineData("2.1", "2.1.0", false)]         // same version is not newer
    [InlineData("2.0", "2.1", false)]           // older is not newer
    [InlineData("2.4.1", "2.4", true)]
    [InlineData("2.5-beta", "2.4", true)]       // digits are read up to the first non-digit
    [InlineData("junk", "0", false)]
    [InlineData("", "", false)]
    public void IsNewer(string a, string b, bool expected) => Assert.Equal(expected, UpdateCheck.IsNewer(a, b));

    [Fact]
    public void AutomaticChecksAreOnByDefault() => Assert.True(new AppConfig().CheckForUpdates);

    [Fact]
    public void TheCurrentVersionIsAReadableNumber()
    {
        var v = UpdateCheck.CurrentVersion();
        Assert.Matches(@"^\d+(\.\d+)*$", v);
    }

    // MARK: Checksum

    [Fact]
    public void ChecksumFileIsRead()
    {
        var hex = string.Concat(Enumerable.Repeat("ab", 32));
        Assert.Equal(hex, UpdateCheck.ExpectedChecksum($"{hex}  Desktop-Buddy-Windows-x64.zip\n"));   // checksum file is read
        Assert.Equal(hex, UpdateCheck.ExpectedChecksum(hex.ToUpperInvariant()));                       // case-insensitive
        Assert.Equal(hex, UpdateCheck.ExpectedChecksum($"{hex} *Desktop-Buddy-Windows-x64.zip\r\n"));
        Assert.Null(UpdateCheck.ExpectedChecksum("not a checksum"));               // a broken checksum file is refused
        Assert.Null(UpdateCheck.ExpectedChecksum("abc123"));
        Assert.Null(UpdateCheck.ExpectedChecksum(""));
        Assert.Null(UpdateCheck.ExpectedChecksum(new string('g', 64)));
    }

    [Fact]
    public void Sha256OfAFile()
    {
        var f = Path.Combine(_dir, "sum.txt");
        File.WriteAllText(f, "hello");
        Assert.Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", UpdateCheck.Sha256Of(f));
        Assert.True(UpdateCheck.Verify(f, "2CF24DBA5FB0A30E26E83B2AC5B9E29E1B161E5C1FA7425E73043362938B9824  sum.txt"));
        Assert.False(UpdateCheck.Verify(f, new string('0', 64)));
        Assert.False(UpdateCheck.Verify(f, "nonsense"));
    }

    // MARK: Release JSON

    [Fact]
    public void ReleaseJsonIsParsedForTheWindowsAssets()
    {
        var r = UpdateCheck.ParseRelease(ReleaseJson("v2.5"))!;
        Assert.Equal("2.5", r.Version);
        Assert.Equal("https://github.com/Abhi28a/desktop-buddy/releases/tag/v2.5", r.Page);
        Assert.Equal("https://example.com/win/Desktop-Buddy-Windows-x64.zip", r.Download);
        Assert.Equal("https://example.com/win/Desktop-Buddy-Windows-x64.zip.sha256", r.Checksum);   // not the Mac's .dmg files
        Assert.True(r.CanInstallInPlace);
    }

    [Fact]
    public void AReleaseWithoutWindowsFilesCanOnlyBeDownloadedByHand()
    {
        var r = UpdateCheck.ParseRelease(ReleaseJson("v2.5", windowsAssets: false))!;
        Assert.Null(r.Download);
        Assert.Null(r.Checksum);
        Assert.False(r.CanInstallInPlace);
    }

    [Fact]
    public void DraftsAndPreReleasesAreIgnored()
    {
        Assert.Null(UpdateCheck.ParseRelease(ReleaseJson("v9.0", prerelease: true)));
        Assert.Null(UpdateCheck.ParseRelease(ReleaseJson("v9.0", draft: true)));
    }

    [Fact]
    public void ABrokenReleaseIsAnError()
    {
        Assert.ThrowsAny<Exception>(() => UpdateCheck.ParseRelease("{}"));
        Assert.ThrowsAny<Exception>(() => UpdateCheck.ParseRelease("[]"));
        Assert.ThrowsAny<Exception>(() => UpdateCheck.ParseRelease("not json"));
    }

    [Fact]
    public void OnlyHttpsLinksAreTrusted()
    {
        const string json = """
        { "tag_name": "v3.0", "html_url": "http://example.com/page",
          "assets": [ { "name": "Desktop-Buddy-Windows-x64.zip", "browser_download_url": "http://example.com/a.zip" },
                      { "name": "Desktop-Buddy-Windows-x64.zip.sha256", "browser_download_url": "file:///c:/x" } ] }
        """;
        var r = UpdateCheck.ParseRelease(json)!;
        Assert.Equal(UpdateCheck.ReleasesPage, r.Page);
        Assert.Null(r.Download);
        Assert.Null(r.Checksum);
    }

    [Fact]
    public void TheFeedIsTheGitHubLatestReleaseOfTheRepo()
    {
        Assert.Equal("https://api.github.com/repos/Abhi28a/desktop-buddy/releases/latest", UpdateCheck.LatestApi.ToString());
        Assert.Equal("Desktop-Buddy-Windows-x64.zip", UpdateCheck.AssetName);
        Assert.Equal("Desktop-Buddy-Windows-x64.zip.sha256", UpdateCheck.ChecksumAssetName);
    }

    // MARK: Checking

    [Fact]
    public async Task ANewerReleaseIsReportedAsAvailable()
    {
        var handler = new FakeHandler(_ => Ok(ReleaseJson("v2.5")));
        var result = await UpdateCheck.CheckAsync(new HttpClient(handler), "2.4.0");
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("2.5", result.Available!.Version);
        Assert.Null(result.Error);
        var req = Assert.Single(handler.Requests);
        Assert.Equal(UpdateCheck.LatestApi, req.RequestUri);
        Assert.Contains("github", req.Headers.Accept.ToString());
        Assert.NotEmpty(req.Headers.UserAgent);
        Assert.Null(req.Headers.Authorization);                                    // anonymous: nothing about the person is sent
    }

    [Fact]
    public async Task TheSameOrAnOlderReleaseMeansUpToDate()
    {
        var same = await UpdateCheck.CheckAsync(new HttpClient(new FakeHandler(_ => Ok(ReleaseJson("v2.4")))), "2.4.0");
        Assert.False(same.IsUpdateAvailable);
        Assert.Null(same.Error);
        var older = await UpdateCheck.CheckAsync(new HttpClient(new FakeHandler(_ => Ok(ReleaseJson("v2.3")))), "2.4.0");
        Assert.False(older.IsUpdateAvailable);
        Assert.Null(older.Error);
    }

    [Fact]
    public async Task APreReleaseIsNeverOffered()
    {
        var result = await UpdateCheck.CheckAsync(new HttpClient(new FakeHandler(_ => Ok(ReleaseJson("v9.0", prerelease: true)))), "2.4.0");
        Assert.False(result.IsUpdateAvailable);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ProblemsComeBackAsAFriendlyMessageNotAnException()
    {
        var notFound = await UpdateCheck.CheckAsync(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))), "2.4.0");
        Assert.Equal(UpdateCheck.CheckFailedMessage, notFound.Error);
        var garbage = await UpdateCheck.CheckAsync(new HttpClient(new FakeHandler(_ => Ok("<html>nope</html>"))), "2.4.0");
        Assert.Equal(UpdateCheck.CheckFailedMessage, garbage.Error);
        var offline = await UpdateCheck.CheckAsync(new HttpClient(new FakeHandler(_ => throw new HttpRequestException("offline"))), "2.4.0");
        Assert.Equal(UpdateCheck.CheckFailedMessage, offline.Error);
        Assert.Equal("Couldn't check for updates. Check your internet connection and try again.", UpdateCheck.CheckFailedMessage);
        Assert.False(offline.IsUpdateAvailable);
    }

    [Fact]
    public async Task CancellingACheckIsNotAnError()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UpdateCheck.CheckAsync(new HttpClient(new FakeHandler(_ => Ok(ReleaseJson("v2.5")))), "2.4.0", ct: cts.Token));
    }

    // MARK: Downloading

    private static byte[] FakeZip() => Encoding.UTF8.GetBytes("pretend this is a zip file " + new string('z', 5000));

    private static ReleaseInfo Release() => UpdateCheck.ParseRelease(ReleaseJson("v2.5"))!;

    private static HttpResponseMessage Serve(HttpRequestMessage req, byte[] zip, string sumText) =>
        req.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal)
            ? Ok(sumText)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) };

    [Fact]
    public async Task ADownloadThatMatchesItsChecksumIsKept()
    {
        var zip = FakeZip();
        var sum = Convert.ToHexStringLower(SHA256.HashData(zip)) + "  Desktop-Buddy-Windows-x64.zip\n";
        var progress = new List<double>();
        var path = await UpdateCheck.DownloadAndVerifyAsync(new HttpClient(new FakeHandler(r => Serve(r, zip, sum))), Release(),
            Path.Combine(_dir, "work"), new Progress<double>(progress.Add));
        Assert.Equal(Path.Combine(_dir, "work", UpdateCheck.AssetName), path);
        Assert.Equal(zip, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task ADownloadThatDoesNotMatchIsDeletedAndRefused()
    {
        var zip = FakeZip();
        var wrong = new string('0', 64) + "  Desktop-Buddy-Windows-x64.zip\n";
        var ex = await Assert.ThrowsAsync<UpdateException>(() =>
            UpdateCheck.DownloadAndVerifyAsync(new HttpClient(new FakeHandler(r => Serve(r, zip, wrong))), Release(), _dir));
        Assert.Equal("The download didn't match. Try again later.", ex.Message);
        Assert.False(File.Exists(Path.Combine(_dir, UpdateCheck.AssetName)));
    }

    [Fact]
    public async Task ABrokenChecksumFileMeansRefused()
    {
        var ex = await Assert.ThrowsAsync<UpdateException>(() =>
            UpdateCheck.DownloadAndVerifyAsync(new HttpClient(new FakeHandler(r => Serve(r, FakeZip(), "not a checksum"))), Release(), _dir));
        Assert.Equal(UpdateCheck.MismatchMessage, ex.Message);
        Assert.False(File.Exists(Path.Combine(_dir, UpdateCheck.AssetName)));
    }

    [Fact]
    public async Task ANetworkFailureDuringDownloadIsAFriendlyMessage()
    {
        var ex = await Assert.ThrowsAsync<UpdateException>(() =>
            UpdateCheck.DownloadAndVerifyAsync(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError))), Release(), _dir));
        Assert.Equal(UpdateCheck.DownloadFailedMessage, ex.Message);
        Assert.False(File.Exists(Path.Combine(_dir, UpdateCheck.AssetName)));
    }

    [Fact]
    public async Task AReleaseWithoutWindowsFilesCannotBeInstalledInPlace()
    {
        var bare = UpdateCheck.ParseRelease(ReleaseJson("v2.5", windowsAssets: false))!;
        await Assert.ThrowsAsync<UpdateException>(() =>
            UpdateCheck.DownloadAndVerifyAsync(new HttpClient(new FakeHandler(_ => Ok(""))), bare, _dir));
    }
}
